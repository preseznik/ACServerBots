"""Build the CC BY Desert Eagle FPS models for Assetto Corsa/CSP.

The downloaded Desert Eagle and carbine viewmodel sources stay outside the
repository. The first-person model reuses the carbine's skinned arms and rig,
then bakes a pistol grip plus a reload-only support-hand pose. Run this script
through tools/Build-FpsDesertEagleAssets.ps1.
"""

from __future__ import annotations

import argparse
import math
from pathlib import Path
import sys
import tempfile

import bpy
from mathutils import Matrix, Vector


SOURCE_MATERIALS = {
    "MainBody": ("MAIN_BODY", "T_Deagle_MainBody", 1024),
    "Slide": ("SLIDE", "T_Deagle_Slide", 1024),
    "Magazine": ("MAGAZINE", "T_Deagle_Magazine", 512),
    "Bullet": ("BULLET", "T_Deagle_Bullet", 512),
}
ASSET_SLUG = "desert_eagle"
VIEWMODEL_CLIPS = {
    "idle": 30,
    "fire": 8,
    "equip": 20,
    "sprint": 24,
    "reload": 55,
}
GRIP_ANCHOR = Vector((0.0, 0.10845, 0.16544))
WEAPON_BONE_NAME = "ASRC_VIEWMODEL_WEAPON_BONE"
MAGAZINE_BONE_NAME = "ASRC_VIEWMODEL_MAGAZINE_BONE"
FIRING_ARM_NODE_NAME = "ASRC_DESERT_EAGLE_FIRING_ARM"
SUPPORT_ARM_NODE_NAME = "ASRC_DESERT_EAGLE_SUPPORT_ARM"
# Armature-space centimetres. The source right wrist is already the weapon
# anchor, so keep its authored firing-hand curl and move the complete arm chain
# slightly back while moving the pistol forward. This closes the remaining
# near-camera gap without substituting the left/support arm.
FIRING_ARM_GRIP_FINE_TUNE = Vector((-1.5, -0.75, -2.5))
WEAPON_GRIP_FINE_TUNE = Vector((0.0, 0.0, 1.5))
# Moving the support shoulder in armature space moves its complete authored
# chain. Keep it well below the view frustum outside reload, then align its
# wrist just below and to the left of the firing wrist while it follows the
# extracted magazine. This avoids CSP's unreliable per-skinned-mesh visibility.
SUPPORT_ARM_HIDDEN_OFFSET = Vector((0.0, -125.0, 0.0))
SUPPORT_HAND_MAGAZINE_OFFSET = Vector((4.0, 7.0, 17.0))


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


def load_image(path: Path, name: str, work_dir: Path, maximum_size: int,
               non_color: bool = False):
    if not path.is_file():
        raise FileNotFoundError(f"FPS texture was not found: {path}")
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
                    textures: dict[str, tuple[Path, int, bool]], work_dir: Path):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    material.assettoCorsa.shaderName = shader_name
    material.assettoCorsa.alphaBlendMode = "0"
    material.assettoCorsa.alphaTested = False
    material.assettoCorsa.depthMode = "0"
    nodes = material.node_tree.nodes
    shader = nodes.get("Principled BSDF")
    for slot, (path, maximum_size, non_color) in textures.items():
        image = load_image(path, f"{name}_{slot}", work_dir, maximum_size, non_color)
        node = nodes.new("ShaderNodeTexImage")
        node.name = f"{name}_{slot}"
        node.image = image
        node.assettoCorsa.shaderInputName = slot
        if hasattr(node, "show_texture"):
            node.show_texture = slot == "txDiffuse"
        if shader is not None and slot == "txDiffuse":
            material.node_tree.links.new(node.outputs["Color"], shader.inputs["Base Color"])
        elif shader is not None and slot == "txNormal":
            normal_map = nodes.new("ShaderNodeNormalMap")
            material.node_tree.links.new(node.outputs["Color"], normal_map.inputs["Color"])
            material.node_tree.links.new(normal_map.outputs["Normal"], shader.inputs["Normal"])
    if shader is not None:
        shader.inputs["Metallic"].default_value = 0.35
        shader.inputs["Roughness"].default_value = 0.4
    return material


def weapon_materials(texture_dir: Path, work_dir: Path, skinned: bool) -> dict:
    shader_name = "ksSkinnedMesh" if skinned else "ksPerPixel"
    result = {}
    for source, (label, prefix, maximum_size) in SOURCE_MATERIALS.items():
        diffuse_size = maximum_size if skinned else min(maximum_size, 512)
        normal_size = min(diffuse_size, 512 if diffuse_size >= 1024 else 256)
        result[source] = create_material(f"ASRC_DEAGLE_{label}", shader_name, {
            "txDiffuse": (texture_dir / f"{prefix}_BaseColor.png", diffuse_size, False),
            "txNormal": (texture_dir / f"{prefix}_Normal.png", normal_size, True),
        }, work_dir)
    return result


def assign_single_material(obj, material) -> None:
    obj.data.materials.clear()
    obj.data.materials.append(material)


def split_viewmodel_arms(arms):
    """Expose each disconnected arm as its own runtime-controllable mesh."""
    bpy.ops.object.select_all(action="DESELECT")
    arms.select_set(True)
    bpy.context.view_layer.objects.active = arms
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE")
    bpy.ops.object.mode_set(mode="OBJECT")
    parts = [obj for obj in bpy.context.selected_objects if obj.type == "MESH"]
    if len(parts) != 2:
        raise RuntimeError(
            "Carbine arm mesh contract changed: expected two disconnected arms, "
            f"got {len(parts)}")

    classified = {}
    for part in parts:
        weights = {"L": 0.0, "R": 0.0}
        for vertex in part.data.vertices:
            for link in vertex.groups:
                group_name = part.vertex_groups[link.group].name
                if group_name.startswith("L_"):
                    weights["L"] += link.weight
                elif group_name.startswith("R_"):
                    weights["R"] += link.weight
        side = max(weights, key=weights.get)
        other_side = "R" if side == "L" else "L"
        if weights[side] <= weights[other_side] * 10:
            raise RuntimeError(
                f"Could not classify separated arm {part.name}: {weights}")
        if side in classified:
            raise RuntimeError(f"Multiple separated meshes classified as {side} arm")
        classified[side] = part

    if set(classified) != {"L", "R"}:
        raise RuntimeError(f"Separated arm classification failed: {sorted(classified)}")
    firing_arm = classified["R"]
    support_arm = classified["L"]
    firing_arm.name = FIRING_ARM_NODE_NAME
    firing_arm.data.name = FIRING_ARM_NODE_NAME
    support_arm.name = SUPPORT_ARM_NODE_NAME
    support_arm.data.name = SUPPORT_ARM_NODE_NAME
    print(
        "Split viewmodel arms: "
        f"firingVertices={len(firing_arm.data.vertices)}, "
        f"supportVertices={len(support_arm.data.vertices)}")
    return firing_arm, support_arm


def join_objects(objects: list, name: str):
    if not objects:
        raise RuntimeError(f"Desert Eagle source has no meshes for {name}")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = name
    result.data.name = name
    return result


def import_weapon(fbx_path: Path, texture_dir: Path, work_dir: Path,
                  skinned: bool, grip_target: Vector | None = None) -> list:
    existing = set(bpy.context.scene.objects)
    bpy.ops.import_scene.fbx(filepath=str(fbx_path))
    imported = [obj for obj in bpy.context.scene.objects if obj not in existing]
    source_meshes = [obj for obj in imported if obj.type == "MESH"]
    if len(source_meshes) != 28:
        raise RuntimeError(
            f"Desert Eagle source contract changed: expected 28 meshes, got {len(source_meshes)}")

    materials = weapon_materials(texture_dir, work_dir, skinned)
    grouped: dict[str, list] = {source: [] for source in SOURCE_MATERIALS}
    for obj in source_meshes:
        source_names = {slot.name for slot in obj.data.materials}
        if len(source_names) != 1:
            raise RuntimeError(f"Unexpected Desert Eagle material slots on {obj.name}: {source_names}")
        source_name = next(iter(source_names))
        if source_name not in grouped:
            raise RuntimeError(f"Unexpected Desert Eagle material on {obj.name}: {source_name}")
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world
        obj.matrix_world.translation -= GRIP_ANCHOR
        if grip_target is not None:
            obj.matrix_world.translation += grip_target
        assign_single_material(obj, materials[source_name])
        grouped[source_name].append(obj)

    for obj in imported:
        if obj.type != "MESH" and obj.name in bpy.data.objects:
            remove_object(obj)

    prefix = "ASRC_DESERT_EAGLE_VIEWMODEL" if skinned else "ASRC_DESERT_EAGLE_WORLD"
    weapon_objects = []
    for source_name, objects in grouped.items():
        label = SOURCE_MATERIALS[source_name][0]
        weapon_objects.append(join_objects(objects, f"{prefix}_{label}"))
    return weapon_objects


def find_right_hand(armature):
    candidates = [bone for bone in armature.pose.bones if bone.name == "R_wrist"]
    if len(candidates) != 1:
        raise RuntimeError(
            "Carbine viewmodel right-hand contract changed: "
            f"candidates={[bone.name for bone in candidates]}, "
            f"bones={[bone.name for bone in armature.pose.bones]}")
    return candidates[0]


def add_weapon_bones(armature) -> None:
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    if armature.data.edit_bones.get(WEAPON_BONE_NAME) is not None:
        raise RuntimeError(f"Carbine rig already contains {WEAPON_BONE_NAME}")
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


def skin_weapon_to_constant_bone(armature, weapon_objects: list) -> None:
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


def make_pistol_pose_callback(armature, clip_name: str, frame_count: int,
                              base_basis: dict[str, Matrix]):
    right_arm = armature.pose.bones["R_arm"]
    right_wrist = armature.pose.bones["R_wrist"]
    support_arm = armature.pose.bones["L_arm"]
    support_wrist = armature.pose.bones["L_wrist"]
    weapon = armature.pose.bones[WEAPON_BONE_NAME]
    magazine = armature.pose.bones[MAGAZINE_BONE_NAME]

    def callback(frame: int) -> None:
        phase = frame / max(1, frame_count - 1)
        for pose_bone in armature.pose.bones:
            pose_bone.matrix_basis = base_basis[pose_bone.name].copy()

        presentation_offset = Vector((0.0, 0.0, 0.0))
        magazine_drop = 0.0
        if clip_name == "idle":
            breathing = math.sin(phase * math.tau)
            presentation_offset.y += 0.18 * breathing
        elif clip_name == "fire":
            recoil = math.sin(phase * math.pi)
            presentation_offset.z -= 1.2 * recoil
        elif clip_name == "equip":
            reveal = ranged_phase(phase, 0.0, 0.82)
            presentation_offset.x += 5.0 * (1.0 - reveal)
            presentation_offset.y -= 7.0 * (1.0 - reveal)
        elif clip_name == "sprint":
            bob = math.sin(phase * math.tau)
            presentation_offset.x += 0.8 * bob
            presentation_offset.y += 0.7 * abs(bob)
        elif clip_name == "reload":
            release = ranged_phase(phase, 0.30, 0.46)
            insert = ranged_phase(phase, 0.56, 0.72)
            magazine_drop = 14.0 * release * (1.0 - insert)

        bpy.context.view_layer.update()
        support_offset = SUPPORT_ARM_HIDDEN_OFFSET.copy()
        if clip_name == "reload":
            support_enter = ranged_phase(phase, 0.12, 0.30)
            support_exit = ranged_phase(phase, 0.76, 0.94)
            support_weight = support_enter * (1.0 - support_exit)
            support_target = (right_wrist.matrix.translation
                              + FIRING_ARM_GRIP_FINE_TUNE
                              + SUPPORT_HAND_MAGAZINE_OFFSET
                              - support_wrist.matrix.translation)
            # Follow most of the magazine travel. Leaving a small amount of
            # separation keeps the gloved fingers readable instead of clipping
            # completely through the magazine.
            support_target.y -= magazine_drop * 0.88
            support_target.x -= math.sin(phase * math.pi) * 1.2
            support_offset = SUPPORT_ARM_HIDDEN_OFFSET.lerp(
                support_target, support_weight)
        support_matrix = support_arm.matrix.copy()
        support_matrix.translation += support_offset
        support_arm.matrix = support_matrix

        arm_matrix = right_arm.matrix.copy()
        arm_matrix.translation += FIRING_ARM_GRIP_FINE_TUNE + presentation_offset
        right_arm.matrix = arm_matrix
        weapon_matrix = weapon.matrix.copy()
        weapon_matrix.translation += WEAPON_GRIP_FINE_TUNE + presentation_offset
        weapon.matrix = weapon_matrix
        magazine.location.y -= magazine_drop

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
                "ksSpecular": {"valueA": 0.55},
                "ksSpecularEXP": {"valueA": 45.0},
            },
        }
        for material in bpy.data.materials if material.users > 0
    }
    settings = {
        "nodes": {
            "*": {
                "lodIn": 0.0,
                "lodOut": 10_000.0,
                "visible": True,
                "renderable": True,
                "castShadows": cast_shadows,
            }
        },
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
        raise RuntimeError(f"Invalid Desert Eagle KN5 output: {path}")
    for warning in warnings:
        print(f"KN5 export warning: {warning}")
    print(f"Built {path} ({path.stat().st_size} bytes)")


def render_previews(path: Path, armature, grip_target: Vector,
                    callbacks: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    armature.data.pose_position = "POSE"
    # Rendering evaluates the FBX action again. The callbacks already restore
    # the captured frame-180 basis, so detach the source action for honest
    # previews of the generated KSANIM poses.
    if armature.animation_data is not None:
        armature.animation_data.action = None

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
    scene.render.film_transparent = False
    scene.world.color = (0.025, 0.035, 0.05)
    render_roots = [
        obj for obj in scene.objects
        if obj.type in {"ARMATURE", "MESH"} and obj.parent is None
    ]
    base_matrices = {obj: obj.matrix_world.copy() for obj in render_roots}
    previews = (
        # Match fps.lua's actual camera-relative holder offsets and FOV. This
        # deliberately exposes near-camera depth separation that a distant
        # presentation camera hides.
        (path, "idle", VIEWMODEL_CLIPS["idle"] // 4,
         Vector((-0.15, -0.39, -0.24)), 72),
        (path.with_name(f"{path.stem}-ads{path.suffix}"),
         "idle", VIEWMODEL_CLIPS["idle"] // 4,
         Vector((0.035, -0.34, -0.12)), 56),
        (path.with_name(f"{path.stem}-reload-mid{path.suffix}"),
         "reload", round((VIEWMODEL_CLIPS["reload"] - 1) * 0.48),
         Vector((-0.15, -0.39, -0.24)), 72),
        (path.with_name(f"{path.stem}-reload-insert{path.suffix}"),
         "reload", round((VIEWMODEL_CLIPS["reload"] - 1) * 0.66),
         Vector((-0.15, -0.39, -0.24)), 72),
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

    for obj, matrix in base_matrices.items():
        obj.matrix_world = matrix


def build_viewmodel(carbine_fbx: Path, fbx_path: Path, texture_dir: Path,
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
        raise RuntimeError("Carbine source does not contain the expected Null rig and armmesh")
    armature.name = "ASRC_MODERN_VIEWMODEL_RIG"
    armature.data.name = "ASRC_MODERN_VIEWMODEL_RIG"

    carbine_textures = carbine_fbx.parent.parent / "textures"
    arms_material = create_material("ASRC_CARBINE_ARMS", "ksSkinnedMesh", {
        "txDiffuse": (carbine_textures / "armColor.png", 2048, False),
        "txNormal": (carbine_textures / "armNormal.png", 2048, True),
        "txMaps": (carbine_textures / "armsmoothness.png", 2048, True),
    }, work_dir)
    assign_single_material(arms, arms_material)
    firing_arm, support_arm = split_viewmodel_arms(arms)
    bpy.context.scene.frame_set(180)
    bpy.context.view_layer.update()
    right_hand = find_right_hand(armature)
    grip_target = armature.matrix_world @ right_hand.matrix.translation
    for obj in list(bpy.context.scene.objects):
        if obj not in {armature, firing_arm, support_arm}:
            remove_object(obj)
    purge_unused_data()
    weapon_objects = import_weapon(
        fbx_path, texture_dir, work_dir, skinned=True, grip_target=grip_target)
    add_weapon_bones(armature)
    skin_weapon_to_constant_bone(armature, weapon_objects)

    retained = {armature, firing_arm, support_arm, *weapon_objects}
    for obj in list(bpy.context.scene.objects):
        if obj not in retained:
            remove_object(obj)
    purge_unused_data()
    armature.data.pose_position = "REST"
    export_kn5(output_dir / f"asrc_{ASSET_SLUG}_viewmodel.kn5", exporter,
               cast_shadows=False)
    armature.data.pose_position = "POSE"
    bpy.context.scene.frame_set(180)
    bpy.context.view_layer.update()
    base_basis = {
        pose_bone.name: pose_bone.matrix_basis.copy()
        for pose_bone in armature.pose.bones
    }
    callbacks = {
        clip_name: make_pistol_pose_callback(
            armature, clip_name, frame_count, base_basis)
        for clip_name, frame_count in VIEWMODEL_CLIPS.items()
    }
    for clip_name, frame_count in VIEWMODEL_CLIPS.items():
        path = output_dir / f"asrc_{ASSET_SLUG}_{clip_name}.ksanim"
        with path.open("wb") as output:
            ksanim_writer(output, bpy.context, [armature], 0, frame_count - 1,
                          callbacks[clip_name]).write()
        if path.stat().st_size < 32 or int.from_bytes(path.read_bytes()[:4], "little") != 2:
            raise RuntimeError(f"Invalid Desert Eagle KSANIM output: {path}")
        print(f"Built {path} ({path.stat().st_size} bytes)")
    if preview_path is not None:
        render_previews(preview_path, armature, grip_target, callbacks)


def build_world(fbx_path: Path, texture_dir: Path, output_dir: Path,
                work_dir: Path, exporter) -> None:
    reset_scene()
    import_weapon(fbx_path, texture_dir, work_dir, skinned=False)
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
    output_dir = Path(options.output_dir).resolve()
    fbx_path = source_dir / "source" / "Deagle_full.fbx"
    texture_dir = source_dir / "textures"
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
    output_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="asrc-deagle-") as temporary:
        work_dir = Path(temporary)
        build_viewmodel(carbine_fbx, fbx_path, texture_dir, output_dir, work_dir,
                        exporter, KSAnimWriter,
                        Path(options.preview_path).resolve() if options.preview_path else None)
        build_world(fbx_path, texture_dir, output_dir, work_dir, exporter)
    Path(options.success_marker).write_text("ok\n", encoding="utf-8")


if __name__ == "__main__":
    main()
