"""Build the CC BY M67 and Semtex-style FPS grenade assets for CSP.

The source FBX files remain outside the repository. Each grenade gets a rigid
world model and a skinned first-person model which reuses the existing carbine
arms. A shared 0.8 second overhand throw is baked separately for each KN5 so
the clip remains self-contained in the client asset archive.
"""

from __future__ import annotations

import argparse
import math
from pathlib import Path
import sys
import tempfile

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_fps_desert_eagle_assets as common


THROW_FRAMES = 48
WEAPON_BONE_NAME = "ASRC_GRENADE_BONE"
FIRING_ARM_NODE_NAME = "ASRC_GRENADE_FIRING_ARM"
SUPPORT_ARM_NODE_NAME = "ASRC_GRENADE_SUPPORT_ARM"
SUPPORT_HIDDEN_OFFSET = Vector((0.0, -125.0, 0.0))


SPECS = {
    "frag_grenade": {
        "source": Path("source/granada_sketchfab.fbx"),
        "scale": 0.055,
        "materials": {
            "base_low": "base",
            "segurar_low": "segurar",
            "pipe_low": "pipe",
            "gatilho_low": "gatilho",
            "bomba_low": "bomba",
        },
    },
    "sticky_grenade": {
        "source": Path("source/Grenade_export.fbx"),
        "scale": 1.0,
        "materials": {"grenade": ""},
    },
}


def bounds_center(objects: list) -> Vector:
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    lower = Vector((min(point.x for point in points), min(point.y for point in points),
                    min(point.z for point in points)))
    upper = Vector((max(point.x for point in points), max(point.y for point in points),
                    max(point.z for point in points)))
    return (lower + upper) * 0.5


def create_grenade_material(asset_slug: str, texture_dir: Path, texture_prefix: str,
                            work_dir: Path, skinned: bool):
    shader = "ksSkinnedMesh" if skinned else "ksPerPixel"
    maximum = 1024 if skinned else 512
    if texture_prefix:
        diffuse = texture_dir / f"{texture_prefix}_albedo.png"
        normal = texture_dir / f"{texture_prefix}_normal.png"
    else:
        diffuse = texture_dir / "BaseColor.png"
        normal = texture_dir / "Normal.png"
    textures = {"txDiffuse": (diffuse, maximum, False)}
    if normal.is_file():
        textures["txNormal"] = (normal, maximum, True)
    label = texture_prefix.upper() if texture_prefix else "BODY"
    return common.create_material(
        f"ASRC_{asset_slug.upper()}_{label}", shader, textures, work_dir)


def import_grenade(asset_slug: str, source_dir: Path, work_dir: Path,
                   skinned: bool, grip_target: Vector | None = None) -> list:
    spec = SPECS[asset_slug]
    fbx_path = source_dir / spec["source"]
    existing = set(bpy.context.scene.objects)
    bpy.ops.import_scene.fbx(filepath=str(fbx_path))
    imported = [obj for obj in bpy.context.scene.objects if obj not in existing]
    source_meshes = [obj for obj in imported if obj.type == "MESH"]
    expected = set(spec["materials"])
    actual = {obj.name for obj in source_meshes}
    if actual != expected:
        raise RuntimeError(
            f"{asset_slug} source contract changed: expected {sorted(expected)}, "
            f"got {sorted(actual)}")

    center = bounds_center(source_meshes)
    materials = {
        prefix: create_grenade_material(asset_slug, source_dir / "textures", prefix,
                                         work_dir, skinned)
        for prefix in set(spec["materials"].values())
    }
    grouped: dict[str, list] = {prefix: [] for prefix in materials}
    for obj in source_meshes:
        world = obj.matrix_world.copy()
        world.translation -= center
        world = Matrix.Scale(spec["scale"], 4) @ world
        if grip_target is not None:
            # The grenade is gripped around its lower half, unlike a pistol whose
            # grip anchor sits directly on the wrist. Raise its centre so the
            # body clears the curled firing-hand fingers in the actual camera.
            world.translation += grip_target + Vector((0.015, -0.010, 0.090))
        obj.parent = None
        obj.matrix_world = world
        prefix = spec["materials"][obj.name]
        common.assign_single_material(obj, materials[prefix])
        grouped[prefix].append(obj)

    for obj in imported:
        if obj.type != "MESH" and obj.name in bpy.data.objects:
            common.remove_object(obj)

    model_kind = "VIEWMODEL" if skinned else "WORLD"
    output = []
    for prefix, objects in grouped.items():
        label = prefix.upper() if prefix else "BODY"
        output.append(common.join_objects(
            objects, f"ASRC_{asset_slug.upper()}_{model_kind}_{label}"))
    return output


def rename_arms(arms):
    firing_arm, support_arm = common.split_viewmodel_arms(arms)
    firing_arm.name = FIRING_ARM_NODE_NAME
    firing_arm.data.name = FIRING_ARM_NODE_NAME
    support_arm.name = SUPPORT_ARM_NODE_NAME
    support_arm.data.name = SUPPORT_ARM_NODE_NAME
    return firing_arm, support_arm


def add_grenade_bone(armature) -> None:
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bone = armature.data.edit_bones.new(WEAPON_BONE_NAME)
    bone.head = Vector((0, 0, 0))
    bone.tail = Vector((0, 0.1, 0))
    bone.use_deform = True
    bpy.ops.object.mode_set(mode="OBJECT")


def skin_to_grenade_bone(armature, objects: list) -> None:
    for obj in objects:
        for modifier in list(obj.modifiers):
            obj.modifiers.remove(modifier)
        for group in list(obj.vertex_groups):
            obj.vertex_groups.remove(group)
        modifier = obj.modifiers.new("ASRC_GRENADE_RIG", "ARMATURE")
        modifier.object = armature
        group = obj.vertex_groups.new(name=WEAPON_BONE_NAME)
        group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")


def smoothstep(value: float) -> float:
    value = max(0.0, min(1.0, value))
    return value * value * (3.0 - 2.0 * value)


def phase_between(phase: float, start: float, end: float) -> float:
    return smoothstep((phase - start) / max(0.0001, end - start))


def make_throw_callback(armature, base_basis: dict[str, Matrix],
                        base_right_wrist: Vector):
    firing_arm = armature.pose.bones["R_arm"]
    firing_wrist = armature.pose.bones["R_wrist"]
    support_arm = armature.pose.bones["L_arm"]
    support_wrist = armature.pose.bones["L_wrist"]
    grenade = armature.pose.bones[WEAPON_BONE_NAME]

    def callback(frame: int) -> None:
        phase = frame / max(1, THROW_FRAMES - 1)
        for pose_bone in armature.pose.bones:
            pose_bone.matrix_basis = base_basis[pose_bone.name].copy()

        # Bring the throwing arm into a compact chest-high ready pose, draw it
        # back, then drive it across the camera in an overhand release.
        draw = phase_between(phase, 0.0, 0.16)
        windup = phase_between(phase, 0.16, 0.34)
        release = phase_between(phase, 0.34, 0.56)
        follow = phase_between(phase, 0.56, 0.82)
        offset = Vector((-2.0, -1.0, -1.5))
        offset += Vector((-2.0, 1.5, 3.5)) * draw
        offset += Vector((5.5, -7.0, 6.0)) * windup
        offset += Vector((-10.0, 15.0, -8.0)) * release
        offset += Vector((2.0, 3.0, -4.0)) * follow
        firing_matrix = firing_arm.matrix.copy()
        firing_matrix.translation += offset
        firing_arm.matrix = firing_matrix
        bpy.context.view_layer.update()

        # The support hand starts at the safety assembly, pulls away with the
        # pin and exits below the view before the throwing arm accelerates.
        support_exit = phase_between(phase, 0.20, 0.34)
        support_target = (firing_wrist.matrix.translation
                          + Vector((3.5, 4.0, 6.5))
                          - support_wrist.matrix.translation)
        support_target += Vector((-5.0, 4.0, 0.0)) * phase_between(phase, 0.08, 0.22)
        support_offset = support_target.lerp(SUPPORT_HIDDEN_OFFSET, support_exit)
        support_matrix = support_arm.matrix.copy()
        support_matrix.translation += support_offset
        support_arm.matrix = support_matrix

        bpy.context.view_layer.update()
        grenade_matrix = grenade.matrix.copy()
        grenade_matrix.translation += firing_wrist.matrix.translation - base_right_wrist
        # The projectile becomes authoritative at 0.28 s. Move the held copy
        # behind the near plane just after that release point.
        if phase >= 0.39:
            grenade_matrix.translation += Vector((0.0, -125.0, 0.0))
        grenade.matrix = grenade_matrix

    return callback


def export_kn5(path: Path, exporter, cast_shadows: bool) -> None:
    common.export_kn5(path, exporter, cast_shadows)


def render_preview(path: Path, armature, callback) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if armature.animation_data is not None:
        armature.animation_data.action = None
    bpy.ops.object.camera_add(location=Vector((0, 0, 0)))
    camera = bpy.context.object
    camera.rotation_euler = Vector((0, -1, 0)).to_track_quat("-Z", "Y").to_euler()
    camera.data.clip_start = 0.03
    camera.data.angle = math.radians(72)
    bpy.context.scene.camera = camera
    bpy.ops.object.light_add(type="AREA", location=Vector((0.8, -0.2, 1.0)))
    bpy.context.object.data.energy = 90
    bpy.context.object.data.size = 2
    bpy.context.object.rotation_euler = Vector((-0.1, -1, -0.1)).to_track_quat("-Z", "Y").to_euler()
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.world.color = (0.025, 0.035, 0.05)
    roots = [obj for obj in scene.objects
             if obj.type in {"ARMATURE", "MESH"} and obj.parent is None]
    bases = {obj: obj.matrix_world.copy() for obj in roots}
    for suffix, frame in (("ready", 4), ("pin", 11), ("windup", 16), ("release", 19)):
        for obj, matrix in bases.items():
            obj.matrix_world = Matrix.Translation(Vector((-0.11, -0.37, -0.25))) @ matrix
        scene.frame_set(frame)
        callback(frame)
        bpy.context.view_layer.update()
        output = path.with_name(f"{path.stem}-{suffix}{path.suffix}")
        scene.render.filepath = str(output)
        bpy.ops.render.render(write_still=True)
        print(f"Rendered {output}")


def build_viewmodel(asset_slug: str, source_dir: Path, carbine_fbx: Path,
                    output_dir: Path, work_dir: Path, exporter, ksanim_writer,
                    preview_path: Path | None) -> None:
    common.reset_scene()
    bpy.ops.import_scene.fbx(filepath=str(carbine_fbx))
    for obj in list(bpy.context.scene.objects):
        if obj.type in {"LIGHT", "CAMERA"} or obj.name == "Cube":
            common.remove_object(obj)
    armature = bpy.data.objects.get("Null")
    arms = bpy.data.objects.get("armmesh")
    if armature is None or arms is None or arms.type != "MESH":
        raise RuntimeError("Carbine source does not contain the expected Null rig and armmesh")
    armature.name = "ASRC_GRENADE_VIEWMODEL_RIG"
    armature.data.name = "ASRC_GRENADE_VIEWMODEL_RIG"
    carbine_textures = carbine_fbx.parent.parent / "textures"
    arms_material = common.create_material("ASRC_GRENADE_ARMS", "ksSkinnedMesh", {
        "txDiffuse": (carbine_textures / "armColor.png", 2048, False),
        "txNormal": (carbine_textures / "armNormal.png", 2048, True),
        "txMaps": (carbine_textures / "armsmoothness.png", 2048, True),
    }, work_dir)
    common.assign_single_material(arms, arms_material)
    firing_arm, support_arm = rename_arms(arms)
    bpy.context.scene.frame_set(180)
    bpy.context.view_layer.update()
    right_wrist = armature.pose.bones["R_wrist"]
    grip_target = armature.matrix_world @ right_wrist.matrix.translation
    for obj in list(bpy.context.scene.objects):
        if obj not in {armature, firing_arm, support_arm}:
            common.remove_object(obj)
    common.purge_unused_data()
    grenade_objects = import_grenade(
        asset_slug, source_dir, work_dir, skinned=True, grip_target=grip_target)
    add_grenade_bone(armature)
    skin_to_grenade_bone(armature, grenade_objects)
    retained = {armature, firing_arm, support_arm, *grenade_objects}
    for obj in list(bpy.context.scene.objects):
        if obj not in retained:
            common.remove_object(obj)
    common.purge_unused_data()
    armature.data.pose_position = "REST"
    export_kn5(output_dir / f"asrc_{asset_slug}_viewmodel.kn5", exporter, False)

    armature.data.pose_position = "POSE"
    bpy.context.scene.frame_set(180)
    bpy.context.view_layer.update()
    base_basis = {bone.name: bone.matrix_basis.copy() for bone in armature.pose.bones}
    base_right_wrist = armature.pose.bones["R_wrist"].matrix.translation.copy()
    callback = make_throw_callback(armature, base_basis, base_right_wrist)
    animation_path = output_dir / f"asrc_{asset_slug}_throw.ksanim"
    with animation_path.open("wb") as output:
        ksanim_writer(output, bpy.context, [armature], 0, THROW_FRAMES - 1,
                      callback).write()
    if animation_path.stat().st_size < 32 \
            or int.from_bytes(animation_path.read_bytes()[:4], "little") != 2:
        raise RuntimeError(f"Invalid grenade KSANIM output: {animation_path}")
    print(f"Built {animation_path} ({animation_path.stat().st_size} bytes)")
    if preview_path is not None:
        render_preview(preview_path, armature, callback)


def build_world(asset_slug: str, source_dir: Path, output_dir: Path,
                work_dir: Path, exporter) -> None:
    common.reset_scene()
    import_grenade(asset_slug, source_dir, work_dir, skinned=False)
    export_kn5(output_dir / f"asrc_{asset_slug}_world.kn5", exporter, True)


def main() -> None:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--frag-source-dir", required=True)
    parser.add_argument("--sticky-source-dir", required=True)
    parser.add_argument("--carbine-fbx", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--exporter-root", required=True)
    parser.add_argument("--success-marker", required=True)
    parser.add_argument("--preview-dir")
    options = parser.parse_args(args)
    source_dirs = {
        "frag_grenade": Path(options.frag_source_dir).resolve(),
        "sticky_grenade": Path(options.sticky_source_dir).resolve(),
    }
    carbine_fbx = Path(options.carbine_fbx).resolve()
    output_dir = Path(options.output_dir).resolve()
    exporter_root = Path(options.exporter_root).resolve()
    sys.path.insert(0, str(exporter_root.parent))
    import blender_assetto_corsa_tools as ac_tools
    from blender_assetto_corsa_tools import exporter
    from blender_assetto_corsa_tools.exporter.ksanim_writer import KSAnimWriter

    for asset_slug, source_dir in source_dirs.items():
        required = source_dir / SPECS[asset_slug]["source"]
        if not required.is_file():
            raise FileNotFoundError(required)
    if not carbine_fbx.is_file():
        raise FileNotFoundError(carbine_fbx)
    ac_tools.register()
    output_dir.mkdir(parents=True, exist_ok=True)
    preview_dir = Path(options.preview_dir).resolve() if options.preview_dir else None
    with tempfile.TemporaryDirectory(prefix="asrc-grenade-") as temporary:
        work_dir = Path(temporary)
        for asset_slug, source_dir in source_dirs.items():
            preview = (preview_dir / f"asrc-{asset_slug.replace('_', '-')}.png"
                       if preview_dir is not None else None)
            build_viewmodel(asset_slug, source_dir, carbine_fbx, output_dir,
                            work_dir, exporter, KSAnimWriter, preview)
            build_world(asset_slug, source_dir, output_dir, work_dir, exporter)
    Path(options.success_marker).write_text("ok\n", encoding="utf-8")


if __name__ == "__main__":
    main()
