"""Build the animated Modern FPS theme for Assetto Corsa/CSP.

The source assets are intentionally outside the repository. This script emits only
project-owned derivative KN5, KSANIM and manifest files into the requested output
directory. Run it through Build-FpsModernAssets.ps1 with Blender 5.1.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import shutil
import sys
import tempfile
import zipfile

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector


OFFICER_KEEP = {
    "default_PackedMaterial0mat_0",
    "Eyelashes_PackedMaterial0mat_0",
    "Body_PackedMaterial0mat_0",
    "Tops_PackedMaterial1mat_0",
    "Shoes_PackedMaterial2mat_0",
    "Hats_PackedMaterial1mat_0",
    "Bottoms_PackedMaterial2mat_0",
    "Gloves_PackedMaterial2mat_0",
}

OPERATOR_CLIPS = {
    "aim_idle": (31, "aim_idle"),
    "aim_up": (16, "aim_up"),
    "aim_down": (16, "aim_down"),
    "walk_forward": (31, "walk_forward"),
    "walk_backward": (31, "walk_backward"),
    "strafe_left": (31, "strafe_left"),
    "strafe_right": (31, "strafe_right"),
    "sprint": (25, "sprint"),
    "crouch_idle": (31, "crouch_idle"),
    "crouch_move": (31, "crouch_move"),
    "prone_idle": (31, "prone_idle"),
    "prone_crawl": (31, "prone_crawl"),
    "jump_start": (13, "jump_start"),
    "airborne": (21, "airborne"),
    "land": (13, "land"),
    "mantle": (19, "mantle"),
    "vault": (19, "vault"),
    "fire": (7, "fire"),
    "reload": (55, "reload"),
    "death": (46, "death"),
}

VIEWMODEL_CLIPS = {
    "idle": (180, 205),
    "fire": (1, 7),
    "reload": (11, 69),
    "reload_empty": (71, 135),
    "equip": (207, 223),
    "sprint": (180, 205),
}

RED_DOT_TEXTURE_SIZE = 512
RED_DOT_CORE_RADIUS = 7
RED_DOT_GLOW_RADIUS = 18


def reset_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in (bpy.data.materials, bpy.data.images, bpy.data.actions):
        for item in list(collection):
            collection.remove(item)


def remove_object(obj) -> None:
    bpy.data.objects.remove(obj, do_unlink=True)


def triangle_count(obj) -> int:
    return sum(max(0, len(polygon.vertices) - 2) for polygon in obj.data.polygons)


def purge_unused_data() -> None:
    for collection in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials,
                       bpy.data.images, bpy.data.actions):
        for item in list(collection):
            if item.users == 0:
                collection.remove(item)


def load_image(path: Path, image_name: str, work_dir: Path, non_color: bool = False):
    image = bpy.data.images.load(str(path), check_existing=False)
    image.name = image_name
    if non_color:
        image.colorspace_settings.name = "Non-Color"
    width, height = image.size
    scale = min(1.0, 2048.0 / max(width, height))
    if scale < 1.0:
        image.scale(max(1, round(width * scale)), max(1, round(height * scale)))
    staged = work_dir / image_name
    image.filepath_raw = str(staged)
    image.file_format = "PNG"
    image.save()
    image.pack()
    return image


def create_material(name: str, shader_name: str, textures: dict[str, tuple[Path, bool]],
                    work_dir: Path):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    material.assettoCorsa.shaderName = shader_name
    material.assettoCorsa.alphaBlendMode = "0"
    material.assettoCorsa.alphaTested = False
    material.assettoCorsa.depthMode = "0"
    nodes = material.node_tree.nodes
    for slot, (path, non_color) in textures.items():
        image = load_image(path, f"{name}_{slot}.png", work_dir, non_color)
        node = nodes.new("ShaderNodeTexImage")
        node.name = f"{name}_{slot}"
        node.image = image
        node.assettoCorsa.shaderInputName = slot
        if hasattr(node, "show_texture"):
            node.show_texture = slot == "txDiffuse"
    return material


def create_red_dot_reticle(path: Path) -> None:
    """Create a small optical dot instead of stretching the source ring over the lens."""
    size = RED_DOT_TEXTURE_SIZE
    coordinates = np.arange(size, dtype=np.float32) - (size - 1) * 0.5
    x, y = np.meshgrid(coordinates, coordinates)
    distance = np.sqrt(x * x + y * y)
    pixels = np.zeros((size, size, 4), dtype=np.float32)
    pixels[:, :, 0] = 1.0
    pixels[:, :, 1] = 0.055
    pixels[:, :, 2] = 0.01
    glow = np.clip((RED_DOT_GLOW_RADIUS - distance)
                   / (RED_DOT_GLOW_RADIUS - RED_DOT_CORE_RADIUS), 0.0, 1.0)
    pixels[:, :, 3] = glow * 0.42
    pixels[distance <= RED_DOT_CORE_RADIUS, 3] = 1.0

    image = bpy.data.images.new("ASRC_RED_DOT_RETICLE", width=size, height=size, alpha=True)
    image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw = str(path)
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


def assign_single_material(obj, material) -> None:
    obj.data.materials.clear()
    obj.data.materials.append(material)


def join_objects(objects: list, name: str):
    if not objects:
        raise RuntimeError(f"No objects selected for {name}")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = name
    return result


def export_kn5(path: Path, exporter, material_shaders: dict[str, str],
               transparent_materials: set[str] | None = None,
               transparent_nodes: set[str] | None = None) -> list[str]:
    warnings: list[str] = []
    transparent_materials = transparent_materials or set()
    transparent_nodes = transparent_nodes or set()
    settings = {
        "nodes": {
            "*": {
                "lodIn": 0.0,
                "lodOut": 10_000.0,
                "visible": True,
                "renderable": True,
                "castShadows": True,
            },
            **{
                name: {
                    "transparent": True,
                    "castShadows": False,
                }
                for name in transparent_nodes
            },
        },
        "materials": {
            name: {
                "shaderName": shader,
                "alphaBlendMode": ("AlphaBlend" if name in transparent_materials
                                   else "Opaque"),
                "depthMode": ("DepthNoWrite" if name in transparent_materials
                              else "DepthNormal"),
                "properties": {
                    "ksAmbient": {"valueA": 0.35},
                    "ksDiffuse": {"valueA": 0.65},
                    "ksSpecular": {"valueA": 0.25},
                    "ksSpecularEXP": {"valueA": 20.0},
                },
            }
            for name, shader in material_shaders.items()
        },
    }
    with path.open("wb") as output:
        writer = exporter.KN5FileWriter(
            output, bpy.context, settings, warnings,
            root_node_name=path.stem.upper(), even_split=False, forward_axis="-Y",
            separate_mesh_node_names=True)
        writer.write()
    if path.stat().st_size < 1024 or path.read_bytes()[:6] != b"sc6969":
        raise RuntimeError(f"Invalid KN5 output: {path}")
    for warning in warnings:
        print(f"KN5 export warning: {warning}")
    return warnings


def export_ksanim(path: Path, writer_type, objects: list, frame_start: int,
                  frame_end: int, callback=None) -> None:
    with path.open("wb") as output:
        writer_type(output, bpy.context, objects, frame_start, frame_end,
                    frame_callback=callback).write()
    if path.stat().st_size < 32 or int.from_bytes(path.read_bytes()[:4], "little") != 2:
        raise RuntimeError(f"Invalid KSANIM output: {path}")


def reparent_armature_to_root(armature) -> None:
    world = armature.matrix_world.copy()
    armature.parent = None
    armature.matrix_world = world
    for obj in list(bpy.context.scene.objects):
        if obj is armature or obj.type == "MESH":
            continue
        if obj.name.startswith("Node_") or obj.name.startswith("RootNode") \
                or obj.type in {"EMPTY", "LIGHT", "CAMERA"}:
            remove_object(obj)


def build_officer(officer_zip: Path, carbine_fbx: Path, output: Path,
                  work_dir: Path, exporter, ksanim_writer) -> dict:
    reset_scene()
    officer_dir = work_dir / "officer"
    officer_dir.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(officer_zip) as archive:
        archive.extractall(officer_dir)
    bpy.ops.import_scene.gltf(filepath=str(officer_dir / "scene.gltf"))
    armature = bpy.data.objects.get("Armature")
    if armature is None:
        raise RuntimeError("Officer source did not contain the expected Armature")
    reparent_armature_to_root(armature)
    armature.name = "ASRC_MODERN_OPERATOR_RIG"
    armature.data.name = "ASRC_MODERN_OPERATOR_RIG"
    # The source avatar is authored at roughly 2.9 m. Keep this scale on the
    # armature root so every clip and skinned child resolves to a 1.82 m actor.
    armature.scale = tuple(component * 0.63 for component in armature.scale)
    bpy.context.view_layer.update()

    for obj in list(bpy.context.scene.objects):
        if obj.type == "MESH" and obj.name not in OFFICER_KEEP:
            remove_object(obj)
    meshes = [obj for obj in armature.children if obj.type == "MESH"]
    if len(meshes) != len(OFFICER_KEEP):
        raise RuntimeError(f"Officer appearance selection produced {len(meshes)} meshes")

    texture_dir = officer_dir / "textures"
    officer_materials = {
        "skin": create_material("ASRC_OFFICER_SKIN", "ksSkinnedMesh", {
            "txDiffuse": (texture_dir / "PackedMaterial0mat_diffuse.png", False),
            "txNormal": (texture_dir / "PackedMaterial0mat_normal.png", True),
            "txMaps": (texture_dir / "PackedMaterial0mat_specularGlossiness.png", True),
        }, work_dir),
        "uniform": create_material("ASRC_OFFICER_UNIFORM", "ksSkinnedMesh", {
            "txDiffuse": (texture_dir / "PackedMaterial1mat_diffuse.png", False),
            "txNormal": (texture_dir / "PackedMaterial1mat_normal.png", True),
            "txMaps": (texture_dir / "PackedMaterial1mat_specularGlossiness.png", True),
        }, work_dir),
        "gear": create_material("ASRC_OFFICER_GEAR", "ksSkinnedMesh", {
            "txDiffuse": (texture_dir / "PackedMaterial2mat_diffuse.png", False),
            "txNormal": (texture_dir / "PackedMaterial2mat_normal.png", True),
            "txMaps": (texture_dir / "PackedMaterial2mat_specularGlossiness.png", True),
        }, work_dir),
    }
    material_groups = {"skin": [], "uniform": [], "gear": []}
    for obj in meshes:
        if "PackedMaterial0" in obj.name:
            group = "skin"
        elif "PackedMaterial1" in obj.name:
            group = "uniform"
        else:
            group = "gear"
        assign_single_material(obj, officer_materials[group])
        material_groups[group].append(obj)
    officer_meshes = []
    for group, objects in material_groups.items():
        joined = join_objects(objects, f"ASRC_OFFICER_{group.upper()}")
        assign_single_material(joined, officer_materials[group])
        officer_meshes.append(joined)

    # Import the source carbine again, freeze it in its idle pose, reduce it for
    # replicated world actors and rigid-weight it to the officer's right hand.
    existing = set(bpy.context.scene.objects)
    bpy.ops.import_scene.fbx(filepath=str(carbine_fbx))
    imported = [obj for obj in bpy.context.scene.objects if obj not in existing]
    bpy.context.scene.frame_set(180)
    bpy.context.view_layer.update()
    weapon_parts = [obj for obj in imported if obj.type == "MESH"
                    and obj.name not in {"Cube", "armmesh", "bullet"}]
    for obj in weapon_parts:
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world
        obj.animation_data_clear()
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        obj.select_set(False)
    world_material = create_material("ASRC_CARBINE_WORLD", "ksSkinnedMesh", {
        "txDiffuse": (carbine_fbx.parent.parent / "textures" / "carbineColor.png", False),
        "txNormal": (carbine_fbx.parent.parent / "textures" / "carbineNormal.png", True),
        "txMaps": (carbine_fbx.parent.parent / "textures" / "carbinespecular.png", True),
    }, work_dir)
    for obj in weapon_parts:
        assign_single_material(obj, world_material)
    weapon = join_objects(weapon_parts, "ASRC_CARBINE_WORLD")
    assign_single_material(weapon, world_material)
    weapon_triangles = triangle_count(weapon)
    if weapon_triangles > 6_000:
        modifier = weapon.modifiers.new("ASRC_WORLD_LOD", "DECIMATE")
        modifier.ratio = 6_000 / weapon_triangles
        bpy.context.view_layer.objects.active = weapon
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    retained = {armature, weapon, *officer_meshes}
    for obj in list(bpy.context.scene.objects):
        if obj not in retained:
            remove_object(obj)
    purge_unused_data()

    hand = armature.data.bones.get("mixamorig:RightHand_037")
    if hand is None:
        raise RuntimeError("Officer right-hand bone was not found")
    hand_world = armature.matrix_world @ hand.matrix_local
    hand_rotation = hand_world.to_quaternion().to_matrix().to_4x4()
    weapon_world = Matrix.Translation(hand_world.translation) @ hand_rotation \
        @ Matrix.Scale(0.82, 4)
    modifier = weapon.modifiers.new("ASRC_OPERATOR_RIG", "ARMATURE")
    modifier.object = armature
    group = weapon.vertex_groups.new(name=hand.name)
    group.add(range(len(weapon.data.vertices)), 1.0, "REPLACE")
    weapon.parent = armature
    weapon.matrix_world = weapon_world

    # The carbine source uses local -Y from pistol grip towards the muzzle.
    # Solve its bind matrix from the evaluated skinned mesh so the common
    # rifle-ready pose points the barrel along actor-forward (-Y). This avoids
    # relying on the Mixamo hand bone's non-intuitive local axes.
    rifle_pose = rifle_ready_pose_basis(armature)
    for name, matrix_basis in rifle_pose.items():
        armature.pose.bones[name].matrix_basis = matrix_basis.copy()
    rotate(armature.pose.bones[hand.name], "Z", math.radians(82))
    bpy.context.view_layer.update()
    evaluated = weapon.evaluated_get(bpy.context.evaluated_depsgraph_get())
    evaluated_mesh = evaluated.to_mesh()
    local_points = np.array([(*vertex.co, 1.0) for vertex in weapon.data.vertices])
    world_points = np.array([tuple(evaluated.matrix_world @ vertex.co)
                             for vertex in evaluated_mesh.vertices])
    coefficients, _, _, _ = np.linalg.lstsq(local_points, world_points, rcond=None)
    evaluated_mesh_to_world = Matrix((
        (*coefficients[:, 0],),
        (*coefficients[:, 1],),
        (*coefficients[:, 2],),
        (0.0, 0.0, 0.0, 1.0),
    ))
    evaluated.to_mesh_clear()
    posed_hand = armature.pose.bones[hand.name]
    desired_mesh_to_world = Matrix.Translation(armature.matrix_world @ posed_hand.head) \
        @ Matrix.Scale(0.82, 4)
    weapon.matrix_world = weapon.matrix_world @ evaluated_mesh_to_world.inverted() \
        @ desired_mesh_to_world
    for pose_bone in armature.pose.bones:
        pose_bone.matrix_basis.identity()
    bpy.context.view_layer.update()

    armature.data.pose_position = "REST"
    actor_path = output / "asrc_modern_operator_carbine.kn5"
    export_kn5(actor_path, exporter, {
        material.name: "ksSkinnedMesh" for material in [*officer_materials.values(), world_material]
    })
    armature.data.pose_position = "POSE"

    for clip_name, (frame_count, pose_name) in OPERATOR_CLIPS.items():
        callback = make_operator_pose_callback(armature, pose_name, frame_count)
        export_ksanim(output / f"asrc_modern_operator_{clip_name}.ksanim",
                      ksanim_writer, [armature], 0, frame_count - 1, callback)

    triangles = sum(triangle_count(obj) for obj in officer_meshes) + triangle_count(weapon)
    return {
        "file": actor_path.name,
        "triangles": triangles,
        "materials": 4,
        "bones": len([bone for bone in armature.data.bones if bone.use_deform]),
        "clips": list(OPERATOR_CLIPS),
    }


def bone(armature, suffix: str):
    return next((item for item in armature.pose.bones if item.name.endswith(suffix)), None)


def rotate(target, axis: str, angle: float) -> None:
    if target is None:
        return
    axes = {"X": Vector((1, 0, 0)), "Y": Vector((0, 1, 0)), "Z": Vector((0, 0, 1))}
    target.rotation_mode = "QUATERNION"
    target.rotation_quaternion = target.rotation_quaternion @ Quaternion(axes[axis], angle)


def rifle_ready_pose_basis(armature, right_pole_degrees: float = -180,
                           left_pole_degrees: float = 0,
                           pose_setup=None,
                           right_target=Vector((-7, -25, 139)),
                           left_target=Vector((7, -45, 145)),
                           right_pole=Vector((-38, -10, 128)),
                           left_pole=Vector((38, -20, 128))) -> dict[str, Matrix]:
    """Bake a deterministic two-hand rifle pose for the selected officer rig."""
    left_arm = bone(armature, "LeftArm_011")
    right_arm = bone(armature, "RightArm_035")
    left_forearm = bone(armature, "LeftForeArm_012")
    right_forearm = bone(armature, "RightForeArm_036")
    required = (left_arm, right_arm, left_forearm, right_forearm)
    if any(item is None for item in required):
        raise RuntimeError("Officer arm chain required for the rifle-ready pose is incomplete")

    for pose_bone in armature.pose.bones:
        pose_bone.matrix_basis.identity()
        pose_bone.rotation_mode = "QUATERNION"
    if pose_setup is not None:
        pose_setup()
    bpy.context.view_layer.update()

    targets = []
    constraints = []

    def solve(label: str, forearm, target_position: Vector,
              pole_position: Vector, pole_angle: float) -> None:
        target = bpy.data.objects.new(f"ASRC_{label}_HAND_TARGET", None)
        pole = bpy.data.objects.new(f"ASRC_{label}_ELBOW_POLE", None)
        bpy.context.scene.collection.objects.link(target)
        bpy.context.scene.collection.objects.link(pole)
        target.matrix_world.translation = armature.matrix_world @ target_position
        pole.matrix_world.translation = armature.matrix_world @ pole_position
        constraint = forearm.constraints.new("IK")
        constraint.name = f"ASRC_{label}_RIFLE_GRIP"
        constraint.target = target
        constraint.pole_target = pole
        constraint.pole_angle = pole_angle
        constraint.chain_count = 2
        constraint.use_tail = True
        targets.extend((target, pole))
        constraints.append((forearm, constraint))

    # Source skeleton coordinates are centimetres. The right hand closes around
    # the trigger/pistol grip while the left reaches forward to the handguard.
    # Separate elbow poles keep both upper arms lowered and visibly bent.
    solve("RIGHT", right_forearm, right_target, right_pole,
          math.radians(right_pole_degrees))
    solve("LEFT", left_forearm, left_target, left_pole,
          math.radians(left_pole_degrees))
    bpy.context.view_layer.update()

    solved_matrices = {
        item.name: item.matrix.copy()
        for item in (left_arm, right_arm, left_forearm, right_forearm)
    }
    for owner, constraint in constraints:
        owner.constraints.remove(constraint)
    for target in targets:
        bpy.data.objects.remove(target, do_unlink=True)
    for pose_bone in armature.pose.bones:
        pose_bone.matrix_basis.identity()
    if pose_setup is not None:
        pose_setup()
    bpy.context.view_layer.update()
    for item in (left_arm, right_arm, left_forearm, right_forearm):
        item.matrix = solved_matrices[item.name]
        bpy.context.view_layer.update()
    result = {
        item.name: item.matrix_basis.copy()
        for item in (left_arm, right_arm, left_forearm, right_forearm)
    }
    for pose_bone in armature.pose.bones:
        pose_bone.matrix_basis.identity()
    bpy.context.view_layer.update()
    return result


def make_operator_pose_callback(armature, pose_name: str, frame_count: int):
    hips = bone(armature, "Hips_01")
    spine = bone(armature, "Spine2_04")
    left_arm = bone(armature, "LeftArm_011")
    right_arm = bone(armature, "RightArm_035")
    left_forearm = bone(armature, "LeftForeArm_012")
    right_forearm = bone(armature, "RightForeArm_036")
    left_hand = bone(armature, "LeftHand_013")
    right_hand = bone(armature, "RightHand_037")
    left_leg = bone(armature, "LeftUpLeg_062")
    right_leg = bone(armature, "RightUpLeg_057")
    left_knee = bone(armature, "LeftLeg_063")
    right_knee = bone(armature, "RightLeg_058")
    left_foot = bone(armature, "LeftFoot_064")
    right_foot = bone(armature, "RightFoot_059")

    def crouch_torso() -> None:
        hips.location.y = -50
        rotate(hips, "X", math.radians(24))
        rotate(spine, "X", math.radians(10))

    def prone_torso() -> None:
        hips.location.y = -47.5
        rotate(hips, "X", math.radians(82))
        rotate(spine, "X", math.radians(-8))

    stance_pose = pose_name in {"crouch_idle", "crouch_move", "prone_idle", "prone_crawl"}
    aim_hand_rotation = None
    if stance_pose:
        aim_pose = rifle_ready_pose_basis(armature)
        for name, matrix_basis in aim_pose.items():
            armature.pose.bones[name].matrix_basis = matrix_basis.copy()
        rotate(spine, "X", math.radians(4))
        rotate(right_hand, "Z", math.radians(82))
        bpy.context.view_layer.update()
        aim_hand_rotation = right_hand.matrix.to_quaternion()
        for pose_bone in armature.pose.bones:
            pose_bone.matrix_basis.identity()
        bpy.context.view_layer.update()

    if pose_name in {"crouch_idle", "crouch_move"}:
        rifle_pose = rifle_ready_pose_basis(
            armature, pose_setup=crouch_torso,
            right_target=Vector((-7, -42, 80)),
            left_target=Vector((7, -61, 85)),
            right_pole=Vector((-38, -28, 72)),
            left_pole=Vector((38, -34, 72)))
    elif pose_name in {"prone_idle", "prone_crawl"}:
        rifle_pose = rifle_ready_pose_basis(
            armature, pose_setup=prone_torso,
            right_target=Vector((-7, -63, 56)),
            left_target=Vector((7, -78, 56)),
            right_pole=Vector((-38, -48, 45)),
            left_pole=Vector((38, -65, 48)))
    else:
        rifle_pose = rifle_ready_pose_basis(armature)

    def curl_fingers(side: str, direction: float) -> None:
        for digit in ("Index", "Middle", "Ring", "Pinky"):
            for segment, degrees in ((1, 48), (2, 62), (3, 48)):
                prefix = f"mixamorig:{side}Hand{digit}{segment}_"
                finger = next((item for item in armature.pose.bones
                               if item.name.startswith(prefix)), None)
                rotate(finger, "X", math.radians(degrees) * direction)
        for segment, degrees in ((1, 24), (2, 38), (3, 26)):
            prefix = f"mixamorig:{side}HandThumb{segment}_"
            thumb = next((item for item in armature.pose.bones
                          if item.name.startswith(prefix)), None)
            rotate(thumb, "X", math.radians(degrees) * direction)

    def callback(frame: int) -> None:
        for pose_bone in armature.pose.bones:
            pose_bone.matrix_basis.identity()
            pose_bone.rotation_mode = "QUATERNION"
        for name, matrix_basis in rifle_pose.items():
            armature.pose.bones[name].matrix_basis = matrix_basis.copy()
        phase = frame / max(1, frame_count - 1)
        cycle = math.sin(phase * math.tau)
        cycle_q = math.sin(phase * math.tau + math.pi / 2)

        # Common two-handed rifle-ready pose is baked above from explicit hand
        # and elbow targets. Action clips add small offsets without returning to
        # the source T-pose.
        rotate(spine, "X", math.radians(4))
        rotate(left_hand, "X", math.radians(55))
        rotate(right_hand, "Z", math.radians(82))
        if pose_name != "death":
            curl_fingers("Left", 1)
            curl_fingers("Right", -1)

        if pose_name == "aim_up":
            rotate(spine, "X", math.radians(-24))
        elif pose_name == "aim_down":
            rotate(spine, "X", math.radians(24))
        elif pose_name in {"walk_forward", "walk_backward", "strafe_left", "strafe_right"}:
            direction = -1 if pose_name == "walk_backward" else 1
            rotate(left_leg, "X", cycle * 0.45 * direction)
            rotate(right_leg, "X", -cycle * 0.45 * direction)
            rotate(left_knee, "X", max(0.0, -cycle) * 0.35)
            rotate(right_knee, "X", max(0.0, cycle) * 0.35)
            rotate(hips, "Z", cycle_q * 0.05)
            if pose_name.startswith("strafe"):
                side = -1 if pose_name.endswith("left") else 1
                rotate(hips, "Y", side * 0.12)
        elif pose_name == "sprint":
            rotate(spine, "X", math.radians(15))
            rotate(left_leg, "X", cycle * 0.75)
            rotate(right_leg, "X", -cycle * 0.75)
            rotate(left_knee, "X", max(0.0, -cycle) * 0.65)
            rotate(right_knee, "X", max(0.0, cycle) * 0.65)
        elif pose_name in {"crouch_idle", "crouch_move"}:
            # Source skeleton translations are centimetres. Ground the feet and
            # compress the silhouette into a deliberate tactical crouch instead
            # of the old 0.32 cm offset which left the whole actor floating.
            moving = pose_name.endswith("move")
            # The source rig is Z-forward/Y-up before KN5 conversion, so local
            # Y (not local Z) is the authored vertical translation.
            crouch_torso()
            rotate(left_leg, "X", math.radians(95) + (cycle * 0.16 if moving else 0))
            rotate(right_leg, "X", math.radians(95) - (cycle * 0.16 if moving else 0))
            rotate(left_knee, "X", math.radians(-142) - (cycle * 0.12 if moving else 0))
            rotate(right_knee, "X", math.radians(-142) + (cycle * 0.12 if moving else 0))
            rotate(left_foot, "X", math.radians(47))
            rotate(right_foot, "X", math.radians(47))
        elif pose_name in {"prone_idle", "prone_crawl"}:
            # Rotate the full body into a grounded prone silhouette, then
            # counter-rotate the firing hand so the rigidly skinned carbine
            # remains parallel to the floor instead of pointing into it.
            prone_torso()
            rotate(left_hand, "X", math.radians(-70))
            rotate(left_knee, "X", math.radians(18))
            rotate(right_knee, "X", math.radians(28))
            rotate(left_foot, "X", math.radians(-18))
            rotate(right_foot, "X", math.radians(-28))
            if pose_name.endswith("crawl"):
                rotate(left_leg, "Y", cycle * 0.22)
                rotate(right_leg, "Y", -cycle * 0.22)
                rotate(left_arm, "Y", -cycle * 0.12)
                rotate(right_arm, "Y", cycle * 0.12)
        elif pose_name == "jump_start":
            bend = math.sin(phase * math.pi) * 0.55
            hips.location.z = -bend * 0.25
            rotate(left_knee, "X", bend)
            rotate(right_knee, "X", bend)
        elif pose_name == "airborne":
            rotate(left_leg, "X", math.radians(-18))
            rotate(right_leg, "X", math.radians(24))
            rotate(left_knee, "X", math.radians(32))
        elif pose_name == "land":
            bend = math.sin(phase * math.pi) * 0.7
            hips.location.z = -bend * 0.3
            rotate(left_knee, "X", bend)
            rotate(right_knee, "X", bend)
        elif pose_name in {"mantle", "vault"}:
            lift = math.sin(phase * math.pi)
            vault = pose_name == "vault"
            hips.location.z = lift * (0.48 if vault else 0.28)
            rotate(spine, "X", math.radians(-28 if vault else -18) + lift * 0.25)
            rotate(left_leg, "X", -lift * (0.95 if vault else 0.6))
            rotate(right_leg, "X", lift * (0.65 if vault else 0.35))
        elif pose_name == "fire":
            recoil = math.sin(phase * math.pi) * 0.16
            rotate(spine, "X", -recoil)
            rotate(right_arm, "X", recoil)
        elif pose_name == "reload":
            reach = math.sin(phase * math.pi)
            rotate(left_arm, "X", reach * 0.9)
            rotate(left_forearm, "Y", -reach * 0.8)
        elif pose_name == "death":
            # Runtime pivots the complete scene root to the floor. This authored clip
            # supplies the part a rigid-body tip cannot: knees buckle, the torso twists,
            # both hands release the rifle and the limbs finish in an asymmetric heap.
            fall = phase * phase * (3.0 - 2.0 * phase)
            impact = math.sin(min(1.0, phase / 0.72) * math.pi)
            hips.location.z = -0.28 * fall
            rotate(hips, "Z", math.radians(11) * fall)
            rotate(hips, "X", math.radians(8) * impact)
            rotate(spine, "X", math.radians(24) * fall)
            rotate(spine, "Y", math.radians(19) * fall)
            rotate(left_leg, "X", math.radians(-38) * fall)
            rotate(right_leg, "X", math.radians(-22) * fall)
            rotate(left_leg, "Y", math.radians(18) * fall)
            rotate(right_leg, "Y", math.radians(-12) * fall)
            rotate(left_knee, "X", math.radians(82) * fall)
            rotate(right_knee, "X", math.radians(58) * fall)
            rotate(left_arm, "Z", math.radians(74) * fall)
            rotate(left_arm, "X", math.radians(-48) * fall)
            rotate(left_forearm, "X", math.radians(62) * fall)
            rotate(left_forearm, "Y", math.radians(22) * fall)
            rotate(right_arm, "Z", math.radians(-61) * fall)
            rotate(right_arm, "X", math.radians(39) * fall)
            rotate(right_forearm, "X", math.radians(52) * fall)
            rotate(right_forearm, "Y", math.radians(-17) * fall)
            rotate(left_hand, "Z", math.radians(28) * fall)
            rotate(right_hand, "X", math.radians(-31) * fall)

        if aim_hand_rotation is not None:
            # IK supplies a stance-correct hand position. Preserve the proven
            # standing grip orientation so the rigidly skinned rifle remains
            # level instead of inheriting crouch/prone torso rotation.
            bpy.context.view_layer.update()
            hand_position = right_hand.matrix.translation.copy()
            right_hand.matrix = (Matrix.Translation(hand_position)
                                 @ aim_hand_rotation.to_matrix().to_4x4())

    return callback


def build_viewmodel(carbine_fbx: Path, output: Path, work_dir: Path,
                    exporter, ksanim_writer) -> dict:
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=str(carbine_fbx))
    for obj in list(bpy.context.scene.objects):
        if obj.type in {"LIGHT", "CAMERA"} or obj.name == "Cube":
            remove_object(obj)
    armature = bpy.data.objects.get("Null")
    if armature is None:
        raise RuntimeError("Carbine source did not contain the expected Null armature")
    armature.name = "ASRC_MODERN_VIEWMODEL_RIG"
    armature.data.name = "ASRC_MODERN_VIEWMODEL_RIG"

    textures = carbine_fbx.parent.parent / "textures"
    arms_material = create_material("ASRC_CARBINE_ARMS", "ksSkinnedMesh", {
        "txDiffuse": (textures / "armColor.png", False),
        "txNormal": (textures / "armNormal.png", True),
        "txMaps": (textures / "armsmoothness.png", True),
    }, work_dir)
    carbine_material = create_material("ASRC_CARBINE_VIEWMODEL", "ksSkinnedMesh", {
        "txDiffuse": (textures / "carbineColor.png", False),
        "txNormal": (textures / "carbineNormal.png", True),
        "txMaps": (textures / "carbinespecular.png", True),
    }, work_dir)
    red_dot_path = work_dir / "asrc_red_dot_reticle.png"
    create_red_dot_reticle(red_dot_path)
    optic_material = create_material("ASRC_CARBINE_OPTIC", "ksSkinnedMesh", {
        "txDiffuse": (red_dot_path, False),
    }, work_dir)

    bpy.context.scene.frame_set(180)
    bpy.context.view_layer.update()
    arms = bpy.data.objects.get("armmesh")
    if arms is None or arms.type != "MESH":
        raise RuntimeError("Carbine source did not contain the expected armmesh")
    assign_single_material(arms, arms_material)
    lens = bpy.data.objects.get("lens")
    if lens is None or lens.type != "MESH":
        raise RuntimeError("Carbine source did not contain the expected optic lens")

    # CSP preview520 reliably renders dynamically loaded SkinnedMesh nodes, but it
    # drops the rigid rifle subtree when that subtree shares an animated KN5 with
    # the arms. Bake all rifle parts at the source idle frame and bind the result
    # to a dedicated constant deform bone. The rifle then uses the exact same
    # rendering path as the visible arms without inheriting an animated hand
    # transform a second time.
    weapon_parts = [obj for obj in bpy.context.scene.objects
                    if obj.type == "MESH" and obj not in {arms, lens}
                    and obj.name != "Cube"]
    if len(weapon_parts) < 10:
        raise RuntimeError("Carbine source does not contain the complete rifle geometry")
    for obj in [*weapon_parts, lens]:
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world
        obj.animation_data_clear()
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        assign_single_material(obj, optic_material if obj == lens else carbine_material)
    weapon = join_objects(weapon_parts, "ASRC_CARBINE_VIEWMODEL_WEAPON")
    assign_single_material(weapon, carbine_material)
    lens.name = "ASRC_CARBINE_VIEWMODEL_OPTIC"

    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    weapon_bone_name = "ASRC_VIEWMODEL_WEAPON_BONE"
    weapon_bone = armature.data.edit_bones.new(weapon_bone_name)
    weapon_bone.head = Vector((0, 0, 0))
    weapon_bone.tail = Vector((0, 0.1, 0))
    weapon_bone.use_deform = True
    bpy.ops.object.mode_set(mode="OBJECT")
    for rigid_mesh in (weapon, lens):
        for group in list(rigid_mesh.vertex_groups):
            rigid_mesh.vertex_groups.remove(group)
        modifier = rigid_mesh.modifiers.new("ASRC_VIEWMODEL_RIG", "ARMATURE")
        modifier.object = armature
        group = rigid_mesh.vertex_groups.new(name=weapon_bone_name)
        group.add(range(len(rigid_mesh.data.vertices)), 1.0, "REPLACE")

    retained = {armature, arms, weapon, lens}
    for obj in list(bpy.context.scene.objects):
        if obj not in retained:
            remove_object(obj)
    purge_unused_data()
    meshes = [arms, weapon, lens]

    armature.data.pose_position = "REST"
    viewmodel_path = output / "asrc_modern_carbine_viewmodel.kn5"
    export_kn5(viewmodel_path, exporter, {
        arms_material.name: "ksSkinnedMesh",
        carbine_material.name: "ksSkinnedMesh",
        optic_material.name: "ksSkinnedMesh",
    }, transparent_materials={optic_material.name},
       transparent_nodes={lens.name})
    armature.data.pose_position = "POSE"

    for clip_name, (start, end) in VIEWMODEL_CLIPS.items():
        export_ksanim(output / f"asrc_modern_carbine_{clip_name}.ksanim",
                      ksanim_writer, [armature], start, end)

    triangles = sum(triangle_count(obj) for obj in meshes)
    return {
        "file": viewmodel_path.name,
        "triangles": triangles,
        "materials": 3,
        "bones": len([bone for bone in armature.data.bones if bone.use_deform]),
        "clips": list(VIEWMODEL_CLIPS),
        "redDotCoreDiameterPixels": RED_DOT_CORE_RADIUS * 2,
        "redDotTextureSizePixels": RED_DOT_TEXTURE_SIZE,
    }


def build_pickup_weapon(carbine_fbx: Path, output: Path, work_dir: Path,
                        exporter) -> dict:
    """Export a centered rigid carbine for the authoritative dropped pickup."""
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=str(carbine_fbx))
    bpy.context.scene.frame_set(180)
    bpy.context.view_layer.update()
    weapon_parts = [obj for obj in bpy.context.scene.objects
                    if obj.type == "MESH"
                    and obj.name not in {"Cube", "armmesh", "bullet", "lens"}]
    if len(weapon_parts) < 10:
        raise RuntimeError("Carbine pickup source does not contain complete rifle geometry")

    textures = carbine_fbx.parent.parent / "textures"
    material = create_material("ASRC_CARBINE_PICKUP", "ksPerPixelMultiMap", {
        "txDiffuse": (textures / "carbineColor.png", False),
        "txNormal": (textures / "carbineNormal.png", True),
        "txMaps": (textures / "carbinespecular.png", True),
    }, work_dir)
    for obj in weapon_parts:
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world
        obj.animation_data_clear()
        obj.modifiers.clear()
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        assign_single_material(obj, material)
    weapon = join_objects(weapon_parts, "ASRC_CARBINE_PICKUP")
    assign_single_material(weapon, material)
    triangles = triangle_count(weapon)
    if triangles > 6_000:
        modifier = weapon.modifiers.new("ASRC_PICKUP_LOD", "DECIMATE")
        modifier.ratio = 6_000 / triangles
        bpy.context.view_layer.objects.active = weapon
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    bpy.ops.object.select_all(action="DESELECT")
    weapon.select_set(True)
    bpy.context.view_layer.objects.active = weapon
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    minimum = Vector((math.inf, math.inf, math.inf))
    maximum = Vector((-math.inf, -math.inf, -math.inf))
    for vertex in weapon.data.vertices:
        minimum.x = min(minimum.x, vertex.co.x)
        minimum.y = min(minimum.y, vertex.co.y)
        minimum.z = min(minimum.z, vertex.co.z)
        maximum.x = max(maximum.x, vertex.co.x)
        maximum.y = max(maximum.y, vertex.co.y)
        maximum.z = max(maximum.z, vertex.co.z)
    center = (minimum + maximum) * 0.5
    weapon.data.transform(Matrix.Translation(-center))
    weapon.location = Vector((0, 0, 0))

    for obj in list(bpy.context.scene.objects):
        if obj is not weapon:
            remove_object(obj)
    purge_unused_data()
    pickup_path = output / "asrc_modern_carbine_pickup.kn5"
    export_kn5(pickup_path, exporter, {material.name: "ksPerPixelMultiMap"})
    return {
        "file": pickup_path.name,
        "triangles": triangle_count(weapon),
        "materials": 1,
    }


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--exporter-root", required=True)
    parser.add_argument("--officer-zip", required=True)
    parser.add_argument("--carbine-fbx", required=True)
    options = parser.parse_args(args)

    output = Path(options.output_dir).resolve()
    output.mkdir(parents=True, exist_ok=True)
    exporter_root = Path(options.exporter_root).resolve()
    officer_zip = Path(options.officer_zip).resolve()
    carbine_fbx = Path(options.carbine_fbx).resolve()
    for source in (officer_zip, carbine_fbx):
        if not source.is_file():
            raise FileNotFoundError(source)
    for stale in output.iterdir():
        if stale.suffix.lower() in {".kn5", ".ksanim", ".json"}:
            stale.unlink()

    sys.path.insert(0, str(Path(__file__).resolve().parent))
    sys.path.insert(0, str(exporter_root.parent))
    import blender_assetto_corsa_tools as ac_tools
    from blender_assetto_corsa_tools import exporter
    from blender_assetto_corsa_tools.exporter.ksanim_writer import KSAnimWriter

    ac_tools.register()
    work_dir = Path(tempfile.mkdtemp(prefix="asrc-modern-assets-"))
    try:
        actor = build_officer(officer_zip, carbine_fbx, output, work_dir,
                              exporter, KSAnimWriter)
        viewmodel = build_viewmodel(carbine_fbx, output, work_dir,
                                    exporter, KSAnimWriter)
        pickup = build_pickup_weapon(carbine_fbx, output, work_dir, exporter)
        if actor["triangles"] > 40_000 or actor["materials"] > 4:
            raise RuntimeError(f"Modern actor exceeds budget: {actor}")
        if viewmodel["triangles"] > 30_000 or viewmodel["materials"] > 3:
            raise RuntimeError(f"Modern viewmodel exceeds budget: {viewmodel}")
        if pickup["triangles"] > 6_000 or pickup["materials"] > 1:
            raise RuntimeError(f"Modern pickup exceeds budget: {pickup}")
        files = sorted(path for path in output.iterdir()
                       if path.suffix.lower() in {".kn5", ".ksanim"})
        manifest = {
            "schemaVersion": 1,
            "theme": "Modern",
            "redistributionRightsConfirmedByUser": True,
            "sources": {
                "operator": "FPS/Characters/army-officer",
                "viewmodelAndWorldWeapon": "FPS/Weapons/fps-animated-carbine",
                "m4a1Used": False,
            },
            "exporter": {
                "kn5": "repository-pinned blender_assetto_corsa_tools with ASRC SkinnedMesh extension",
                "ksanimSourceRevision": "920bac087de1caad32ae63725dc6fa302c9b9c18",
                "license": "GPL-3.0-or-later",
            },
            "operator": actor,
            "viewmodel": viewmodel,
            "pickup": pickup,
            "files": {path.name: sha256(path) for path in files},
        }
        manifest_path = output / "asrc-modern-assets.json"
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        from validate_fps_modern_assets import validate_modern_asset_set
        validated = validate_modern_asset_set(output)
        manifest["validation"] = {
            "status": "passed",
            "kn5": "bone matrices, indices, normalized weights, shaders, textures and budgets",
            "ksanim": "track compatibility, rifle-ready grip, finite frames and planar root lock",
            "operatorSkinnedMeshes": validated["operator"].skinned_meshes,
            "viewmodelSkinnedMeshes": validated["viewmodel"].skinned_meshes,
            "viewmodelWeaponSkinnedMeshes": validated["viewmodelWeaponSkinnedMeshes"],
            "viewmodelOpticSkinnedMeshes": validated["viewmodelOpticSkinnedMeshes"],
            "pickupRigidMeshes": validated["pickup"].rigid_meshes,
            "stancePosesValidated": True,
            "deathCollapseValidated": True,
            "uniqueNodeNames": True,
        }
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(manifest, indent=2))
    finally:
        shutil.rmtree(work_dir, ignore_errors=True)


if __name__ == "__main__":
    main()
