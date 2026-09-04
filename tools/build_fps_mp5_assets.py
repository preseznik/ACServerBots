"""Build the CC BY MP5 FPS models for Assetto Corsa/CSP.

The downloaded source remains outside the repository. The first-person model
reuses the proven carbine arms, skeleton and animation ranges while replacing
the carbine geometry with the MP5 and a separately animated magazine.
"""

from __future__ import annotations

import argparse
import math
from pathlib import Path
import sys
import tempfile

import bpy
from mathutils import Matrix, Vector


ASSET_SLUG = "compact_smg"
VIEWMODEL_CLIPS = {
    "idle": (180, 205),
    "fire": (1, 7),
    "reload": (11, 69),
    "reload_empty": (71, 135),
    "equip": (207, 223),
    "sprint": (180, 205),
}
BODY_SOURCE_NAMES = {
    "Bolt_low",
    "Trigger_low",
    "CH_low",
    "Receiver_low",
    "Barrel_low",
    "Stock_low",
    "FS_low",
}
MAGAZINE_SOURCE_NAME = "Mag_low"
WEAPON_BONE_NAME = "ASRC_VIEWMODEL_WEAPON_BONE"
MAGAZINE_BONE_NAME = "ASRC_VIEWMODEL_MAGAZINE_BONE"
ARMS_NODE_NAME = "ASRC_COMPACT_SMG_ARMS"

# The FBX is authored in metres with its trigger at the origin. This anchor is
# the centre of the trigger guard and makes rigid world copies attach at the
# hand rather than at the receiver centre.
GRIP_ANCHOR = Vector((0.0, -0.00635, -0.00739))
# The accepted carbine arm animation grips its trigger at this point. Aligning
# the two trigger anchors also puts the MP5 handguard under the support hand and
# keeps both iron sights close to the carbine optic axis.
VIEWMODEL_GRIP_TARGET = Vector((0.0, -0.0640, 0.0825))


def reset_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials,
                       bpy.data.images, bpy.data.actions, bpy.data.cameras,
                       bpy.data.lights):
        for item in list(collection):
            collection.remove(item)


def remove_object(obj) -> None:
    bpy.data.objects.remove(obj, do_unlink=True)


def purge_unused_data() -> None:
    for collection in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials,
                       bpy.data.images, bpy.data.actions):
        for item in list(collection):
            if item.users == 0:
                collection.remove(item)


def triangle_count(obj) -> int:
    return sum(max(0, len(polygon.vertices) - 2) for polygon in obj.data.polygons)


def load_image(path: Path, name: str, work_dir: Path, maximum_size: int,
               non_color: bool):
    if not path.is_file():
        raise FileNotFoundError(path)
    image = bpy.data.images.load(str(path), check_existing=False)
    image.name = name
    if non_color:
        image.colorspace_settings.name = "Non-Color"
    width, height = image.size
    scale = min(1.0, maximum_size / max(width, height))
    if scale < 1.0:
        image.scale(max(1, round(width * scale)), max(1, round(height * scale)))
    staged = work_dir / f"{name}.png"
    image.filepath_raw = str(staged)
    image.file_format = "PNG"
    image.save()
    image.pack()
    return image


def create_material(name: str, shader_name: str,
                    textures: dict[str, tuple[Path, bool]], work_dir: Path,
                    maximum_size: int):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    material.assettoCorsa.shaderName = shader_name
    material.assettoCorsa.alphaBlendMode = "0"
    material.assettoCorsa.alphaTested = False
    material.assettoCorsa.depthMode = "0"
    for slot, (path, non_color) in textures.items():
        image = load_image(path, f"{name}_{slot}", work_dir, maximum_size,
                           non_color)
        node = material.node_tree.nodes.new("ShaderNodeTexImage")
        node.name = f"{name}_{slot}"
        node.image = image
        node.assettoCorsa.shaderInputName = slot
        if hasattr(node, "show_texture"):
            node.show_texture = slot == "txDiffuse"
    return material


def create_weapon_materials(source_dir: Path, work_dir: Path,
                            skinned: bool) -> tuple[object, object]:
    shader = "ksSkinnedMesh" if skinned else "ksPerPixel"
    maximum_size = 2048 if skinned else 512
    body = create_material(f"ASRC_MP5_{'VIEWMODEL' if skinned else 'WORLD'}_BODY",
                           shader, {
        "txDiffuse": (source_dir / "MP5_BaseColor.png", False),
        "txNormal": (source_dir / "MP5_Normal.png", True),
        "txMaps": (source_dir / "MP5_Roughness.png", True),
    }, work_dir, maximum_size)
    magazine = create_material(
        f"ASRC_MP5_{'VIEWMODEL' if skinned else 'WORLD'}_MAGAZINE", shader, {
            "txDiffuse": (source_dir / "Low_Maglow_BaseColor.png", False),
            "txNormal": (source_dir / "Low_Maglow_Normal.png", True),
            "txMaps": (source_dir / "Low_Maglow_Roughness.png", True),
        }, work_dir, maximum_size)
    return body, magazine


def assign_single_material(obj, material) -> None:
    obj.data.materials.clear()
    obj.data.materials.append(material)


def join_objects(objects: list, name: str):
    if not objects:
        raise RuntimeError(f"MP5 source has no meshes for {name}")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = name
    result.data.name = name
    return result


def import_weapon(fbx_path: Path, source_dir: Path, work_dir: Path,
                  skinned: bool, grip_target: Vector | None = None) -> list:
    existing = set(bpy.context.scene.objects)
    bpy.ops.import_scene.fbx(filepath=str(fbx_path))
    imported = [obj for obj in bpy.context.scene.objects if obj not in existing]
    source_meshes = [obj for obj in imported if obj.type == "MESH"]
    names = {obj.name for obj in source_meshes}
    expected = BODY_SOURCE_NAMES | {MAGAZINE_SOURCE_NAME}
    if names != expected:
        raise RuntimeError(
            "MP5 source contract changed: "
            f"missing={sorted(expected - names)}, unexpected={sorted(names - expected)}")
    if sum(triangle_count(obj) for obj in source_meshes) != 14_248:
        raise RuntimeError("MP5 source triangle contract changed")

    body_material, magazine_material = create_weapon_materials(
        source_dir, work_dir, skinned)
    offset = -GRIP_ANCHOR + (grip_target or Vector())
    body_parts = []
    magazine = None
    for obj in source_meshes:
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = Matrix.Translation(offset) @ world
        obj.animation_data_clear()
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        if obj.name == MAGAZINE_SOURCE_NAME:
            assign_single_material(obj, magazine_material)
            magazine = obj
        else:
            assign_single_material(obj, body_material)
            body_parts.append(obj)

    for obj in imported:
        if obj.type != "MESH" and obj.name in bpy.data.objects:
            remove_object(obj)
    prefix = "ASRC_COMPACT_SMG_VIEWMODEL" if skinned else "ASRC_COMPACT_SMG_WORLD"
    body = join_objects(body_parts, f"{prefix}_BODY")
    assign_single_material(body, body_material)
    if magazine is None:
        raise RuntimeError("MP5 magazine was not imported")
    magazine.name = f"{prefix}_MAGAZINE"
    magazine.data.name = magazine.name
    return [body, magazine]


def add_weapon_bones(armature) -> None:
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    weapon_bone = armature.data.edit_bones.new(WEAPON_BONE_NAME)
    weapon_bone.head = Vector((0, 0, 0))
    weapon_bone.tail = Vector((0, 0.1, 0))
    weapon_bone.use_deform = True
    magazine_bone = armature.data.edit_bones.new(MAGAZINE_BONE_NAME)
    magazine_bone.head = Vector((0, 0, 0))
    magazine_bone.tail = Vector((0, 0.1, 0))
    magazine_bone.parent = weapon_bone
    magazine_bone.use_deform = True
    bpy.ops.object.mode_set(mode="OBJECT")


def skin_weapon(armature, weapon_objects: list) -> None:
    for obj in weapon_objects:
        for modifier in list(obj.modifiers):
            obj.modifiers.remove(modifier)
        for group in list(obj.vertex_groups):
            obj.vertex_groups.remove(group)
        modifier = obj.modifiers.new("ASRC_VIEWMODEL_RIG", "ARMATURE")
        modifier.object = armature
        bone_name = (MAGAZINE_BONE_NAME
                     if obj.name.endswith("_MAGAZINE") else WEAPON_BONE_NAME)
        group = obj.vertex_groups.new(name=bone_name)
        group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")


def smoothstep(value: float) -> float:
    value = max(0.0, min(1.0, value))
    return value * value * (3.0 - 2.0 * value)


def ranged_phase(phase: float, start: float, end: float) -> float:
    if end <= start:
        return 1.0 if phase >= end else 0.0
    return smoothstep((phase - start) / (end - start))


def make_magazine_callback(armature, clip_name: str, start: int, end: int):
    weapon = armature.pose.bones[WEAPON_BONE_NAME]
    magazine = armature.pose.bones[MAGAZINE_BONE_NAME]

    def callback(frame: int) -> None:
        weapon.matrix_basis = Matrix.Identity(4)
        magazine.matrix_basis = Matrix.Identity(4)
        if clip_name not in {"reload", "reload_empty"}:
            return
        phase = (frame - start) / max(1, end - start)
        release = ranged_phase(phase, 0.20, 0.40)
        insert = ranged_phase(phase, 0.66, 0.88)
        travel = release * (1.0 - insert)
        # Armature Y maps to world down in the imported carbine rig. Add a
        # modest sideways cant so the curved MP5 magazine remains readable.
        magazine.location.y -= 18.0 * travel
        magazine.location.x -= 2.5 * travel

    return callback


def export_kn5(path: Path, exporter, cast_shadows: bool) -> None:
    warnings: list[str] = []
    materials = {
        material.name: {
            "shaderName": material.assettoCorsa.shaderName,
            "alphaBlendMode": "Opaque",
            "depthMode": "DepthNormal",
            "properties": {
                "ksAmbient": {"valueA": 0.35},
                "ksDiffuse": {"valueA": 0.65},
                "ksSpecular": {"valueA": 0.42},
                "ksSpecularEXP": {"valueA": 35.0},
            },
        }
        for material in bpy.data.materials if material.users > 0
    }
    settings = {
        "nodes": {"*": {
            "lodIn": 0.0,
            "lodOut": 10_000.0,
            "visible": True,
            "renderable": True,
            "castShadows": cast_shadows,
        }},
        "materials": materials,
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as output:
        writer = exporter.KN5FileWriter(
            output, bpy.context, settings, warnings,
            root_node_name=path.stem.upper(), even_split=False, forward_axis="-Y",
            separate_mesh_node_names=True)
        writer.write()
    if path.stat().st_size < 100_000 or path.read_bytes()[:6] != b"sc6969":
        raise RuntimeError(f"Invalid MP5 KN5 output: {path}")
    for warning in warnings:
        print(f"KN5 export warning: {warning}")
    print(f"Built {path} ({path.stat().st_size} bytes)")


def render_previews(path: Path, armature, callbacks: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    armature.data.pose_position = "POSE"
    bpy.ops.object.camera_add(location=Vector((0, 0, 0)))
    camera = bpy.context.object
    target = Vector((0, -1, 0))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.clip_start = 0.03
    camera.data.sensor_fit = "VERTICAL"
    bpy.context.scene.camera = camera
    for location, energy, size in (
            (Vector((0.8, -0.2, 1.0)), 80, 1.8),
            (Vector((-0.9, 0.1, 0.5)), 45, 1.5)):
        bpy.ops.object.light_add(type="AREA", location=location)
        light = bpy.context.object
        light.data.energy = energy
        light.data.shape = "DISK"
        light.data.size = size
        light.rotation_euler = (target - light.location).to_track_quat("-Z", "Y").to_euler()
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.world.color = (0.025, 0.035, 0.05)
    render_roots = [obj for obj in scene.objects
                    if obj.type in {"ARMATURE", "MESH"} and obj.parent is None]
    base_matrices = {obj: obj.matrix_world.copy() for obj in render_roots}
    previews = (
        (path, "idle", 186, Vector((-0.18, -0.32, -0.32)), 72),
        (path.with_name(f"{path.stem}-ads{path.suffix}"),
         "idle", 186, Vector((0.0003, -0.12, -0.2218)), 56),
        (path.with_name(f"{path.stem}-reload{path.suffix}"),
         "reload", 39, Vector((-0.18, -0.32, -0.32)), 72),
    )
    for output_path, clip_name, frame, holder_offset, fov in previews:
        for obj, matrix in base_matrices.items():
            obj.matrix_world = Matrix.Translation(holder_offset) @ matrix
        scene.frame_set(frame)
        callbacks[clip_name](frame)
        bpy.context.view_layer.update()
        camera.data.angle = math.radians(fov)
        scene.render.filepath = str(output_path)
        bpy.ops.render.render(write_still=True)
        print(f"Rendered {output_path}")


def build_viewmodel(carbine_fbx: Path, fbx_path: Path, source_dir: Path,
                    output_dir: Path, work_dir: Path, exporter, ksanim_writer,
                    preview_path: Path | None) -> None:
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=str(carbine_fbx))
    for obj in list(bpy.context.scene.objects):
        if obj.type in {"LIGHT", "CAMERA"} or obj.name == "Cube":
            remove_object(obj)
    armature = bpy.data.objects.get("Null")
    arms = bpy.data.objects.get("armmesh")
    if armature is None or arms is None or arms.type != "MESH":
        raise RuntimeError("Carbine source lacks the expected Null rig and armmesh")
    armature.name = "ASRC_MODERN_VIEWMODEL_RIG"
    armature.data.name = "ASRC_MODERN_VIEWMODEL_RIG"
    arms.name = ARMS_NODE_NAME
    arms.data.name = ARMS_NODE_NAME

    carbine_textures = carbine_fbx.parent.parent / "textures"
    arms_material = create_material("ASRC_CARBINE_ARMS", "ksSkinnedMesh", {
        "txDiffuse": (carbine_textures / "armColor.png", False),
        "txNormal": (carbine_textures / "armNormal.png", True),
        "txMaps": (carbine_textures / "armsmoothness.png", True),
    }, work_dir, 2048)
    assign_single_material(arms, arms_material)
    bpy.context.scene.frame_set(180)
    bpy.context.view_layer.update()
    for obj in list(bpy.context.scene.objects):
        if obj not in {armature, arms}:
            remove_object(obj)
    purge_unused_data()

    weapon_objects = import_weapon(
        fbx_path, source_dir, work_dir, skinned=True,
        grip_target=VIEWMODEL_GRIP_TARGET)
    add_weapon_bones(armature)
    skin_weapon(armature, weapon_objects)
    retained = {armature, arms, *weapon_objects}
    for obj in list(bpy.context.scene.objects):
        if obj not in retained:
            remove_object(obj)
    purge_unused_data()

    armature.data.pose_position = "REST"
    export_kn5(output_dir / f"asrc_{ASSET_SLUG}_viewmodel.kn5", exporter,
               cast_shadows=False)
    armature.data.pose_position = "POSE"
    callbacks = {
        clip: make_magazine_callback(armature, clip, start, end)
        for clip, (start, end) in VIEWMODEL_CLIPS.items()
    }
    for clip, (start, end) in VIEWMODEL_CLIPS.items():
        path = output_dir / f"asrc_{ASSET_SLUG}_{clip}.ksanim"
        with path.open("wb") as output:
            ksanim_writer(output, bpy.context, [armature], start, end,
                          callbacks[clip]).write()
        if path.stat().st_size < 32 or int.from_bytes(path.read_bytes()[:4], "little") != 2:
            raise RuntimeError(f"Invalid MP5 KSANIM output: {path}")
        print(f"Built {path} ({path.stat().st_size} bytes)")
    if preview_path is not None:
        render_previews(preview_path, armature, callbacks)


def build_world(fbx_path: Path, source_dir: Path, output_dir: Path,
                work_dir: Path, exporter) -> None:
    reset_scene()
    import_weapon(fbx_path, source_dir, work_dir, skinned=False)
    export_kn5(output_dir / f"asrc_{ASSET_SLUG}_world.kn5", exporter,
               cast_shadows=True)


def main() -> None:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", required=True)
    parser.add_argument("--carbine-fbx", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--exporter-root", required=True)
    parser.add_argument("--success-marker", required=True)
    parser.add_argument("--preview-path")
    options = parser.parse_args(args)

    source_dir = Path(options.source_dir).resolve()
    fbx_path = source_dir / "MP5.fbx"
    carbine_fbx = Path(options.carbine_fbx).resolve()
    for source in (fbx_path, carbine_fbx):
        if not source.is_file():
            raise FileNotFoundError(source)
    exporter_root = Path(options.exporter_root).resolve()
    sys.path.insert(0, str(exporter_root.parent))
    import blender_assetto_corsa_tools as ac_tools
    from blender_assetto_corsa_tools import exporter
    from blender_assetto_corsa_tools.exporter.ksanim_writer import KSAnimWriter

    ac_tools.register()
    output_dir = Path(options.output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="asrc-mp5-") as temporary:
        work_dir = Path(temporary)
        build_viewmodel(carbine_fbx, fbx_path, source_dir, output_dir, work_dir,
                        exporter, KSAnimWriter,
                        Path(options.preview_path).resolve()
                        if options.preview_path else None)
        build_world(fbx_path, source_dir, output_dir, work_dir, exporter)
    Path(options.success_marker).write_text("ok\n", encoding="utf-8")


if __name__ == "__main__":
    main()
