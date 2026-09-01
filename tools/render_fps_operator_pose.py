"""Render deterministic front, side and rear previews of the Modern operator pose."""

from pathlib import Path
import math
import sys
import tempfile

import bpy
import numpy as np
from mathutils import Vector


args = sys.argv[sys.argv.index("--") + 1:]
repo_root = Path(args[0]).resolve()
preview_dir = Path(args[1]).resolve()
officer_zip = Path(args[2]).resolve()
carbine_fbx = Path(args[3]).resolve()
pose_name = args[4] if len(args) > 4 else "aim_idle"
pose_phase = float(args[5]) if len(args) > 5 else 0.0
preview_dir.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(repo_root / "tools"))
sys.path.insert(0, str(repo_root / "tools" / "vendor"))

import blender_assetto_corsa_tools as ac_tools
from blender_assetto_corsa_tools import exporter
from blender_assetto_corsa_tools.exporter.ksanim_writer import KSAnimWriter
from build_fps_modern_assets import OPERATOR_CLIPS, build_officer, make_operator_pose_callback

if pose_name not in OPERATOR_CLIPS or not 0 <= pose_phase <= 1:
    raise ValueError(f"Invalid operator preview pose/phase: {pose_name} {pose_phase}")


def look_at(camera, target: Vector) -> None:
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()


ac_tools.register()
with tempfile.TemporaryDirectory(prefix="asrc-operator-preview-") as temporary:
    work = Path(temporary)
    generated = work / "generated"
    generated.mkdir()
    build_officer(officer_zip, carbine_fbx, generated, work,
                  exporter, KSAnimWriter)
    armature = bpy.data.objects["ASRC_MODERN_OPERATOR_RIG"]
    weapon = bpy.data.objects["ASRC_CARBINE_WORLD"]
    local_min = Vector(tuple(min(vertex.co[index] for vertex in weapon.data.vertices)
                             for index in range(3)))
    local_max = Vector(tuple(max(vertex.co[index] for vertex in weapon.data.vertices)
                             for index in range(3)))
    print("ASRC_PREVIEW_WEAPON_LOCAL_BOUNDS", tuple(round(value, 5) for value in local_min),
          tuple(round(value, 5) for value in local_max))
    print("ASRC_PREVIEW_WEAPON_MATRIX", [tuple(round(value, 5) for value in row)
                                         for row in weapon.matrix_world])
    # Rendering reevaluates imported source actions at the current frame. Clear
    # them so the proof image shows the generated KSANIM callback pose instead.
    armature.animation_data_clear()
    frame_count = OPERATOR_CLIPS[pose_name][0]
    frame = round(pose_phase * (frame_count - 1))
    make_operator_pose_callback(armature, pose_name, frame_count)(frame)
    if pose_name == "death":
        fall = pose_phase * pose_phase * (3.0 - 2.0 * pose_phase)
        armature.rotation_mode = "XYZ"
        armature.rotation_euler.x = math.radians(84) * fall
        weapon.hide_render = True
    bpy.context.view_layer.update()
    print("ASRC_PREVIEW_POSE_POSITION", armature.data.pose_position)
    for name in ("mixamorig:Hips_01", "mixamorig:Spine2_04",
                 "mixamorig:LeftArm_011", "mixamorig:LeftForeArm_012",
                 "mixamorig:LeftHand_013", "mixamorig:RightArm_035",
                 "mixamorig:RightForeArm_036", "mixamorig:RightHand_037"):
        item = armature.pose.bones[name]
        print("ASRC_PREVIEW_BONE", name, tuple(round(value, 4) for value in item.head))
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH":
            print("ASRC_PREVIEW_MESH", obj.name, "parent", obj.parent.name if obj.parent else None,
                  "modifiers", [(modifier.type,
                                 modifier.object.name if modifier.type == "ARMATURE"
                                 and modifier.object else None)
                                for modifier in obj.modifiers])
            evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
            evaluated_mesh = evaluated.to_mesh()
            delta = max((evaluated_mesh.vertices[index].co - vertex.co).length
                        for index, vertex in enumerate(obj.data.vertices))
            print("ASRC_PREVIEW_DEFORMATION", obj.name, round(delta, 6))
            if obj.name == "ASRC_CARBINE_WORLD":
                points = np.array([tuple(evaluated.matrix_world @ vertex.co)
                                   for vertex in evaluated_mesh.vertices])
                covariance = np.cov(points, rowvar=False)
                values, vectors = np.linalg.eigh(covariance)
                principal = vectors[:, np.argmax(values)]
                print("ASRC_PREVIEW_WEAPON_PRINCIPAL",
                      tuple(round(float(value), 5) for value in principal))
            evaluated.to_mesh_clear()

    camera_data = bpy.data.cameras.new("ASRC_OPERATOR_PREVIEW_CAMERA")
    camera = bpy.data.objects.new("ASRC_OPERATOR_PREVIEW_CAMERA", camera_data)
    bpy.context.scene.collection.objects.link(camera)
    bpy.context.scene.camera = camera
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 2.05
    preview_min = Vector((math.inf, math.inf, math.inf))
    preview_max = Vector((-math.inf, -math.inf, -math.inf))
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH" or obj.hide_render:
            continue
        evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        evaluated_mesh = evaluated.to_mesh()
        for vertex in evaluated_mesh.vertices:
            point = evaluated.matrix_world @ vertex.co
            for axis in range(3):
                preview_min[axis] = min(preview_min[axis], point[axis])
                preview_max[axis] = max(preview_max[axis], point[axis])
        evaluated.to_mesh_clear()
    target = (preview_min + preview_max) * 0.5
    full_scale = max(preview_max - preview_min) * 1.18
    print("ASRC_PREVIEW_BOUNDS", tuple(round(value, 5) for value in preview_min),
          tuple(round(value, 5) for value in preview_max))

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "WORLD"
    scene.render.resolution_x = 720
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False

    # The imported officer faces local -Y. Keep all three views so rifle axis
    # and both hands can be checked without guessing from a foreshortened shot.
    views = (
        ("front", target + Vector((0, -4.2, 0)), target, full_scale),
        ("side", target + Vector((4.2, 0, 0)), target, full_scale),
        ("rear-three-quarter", target + Vector((2.8, 3.4, 0.3)), target, full_scale),
        ("top", target + Vector((0, 0, 4.2)), target, full_scale),
    ) if pose_name == "death" else (
        ("front", target + Vector((0, -4.2, 0)), target, full_scale),
        ("side", target + Vector((4.2, 0, 0)), target, full_scale),
        ("rear-three-quarter", target + Vector((2.8, 3.4, 0.3)), target, full_scale),
        ("grip-front", Vector((0, -3.0, 1.38)), Vector((0, -0.2, 1.38)), 0.95),
        ("grip-side", Vector((3.0, -0.2, 1.38)), Vector((0, -0.2, 1.38)), 0.95),
    )
    for name, location, view_target, ortho_scale in views:
        camera.location = location
        camera.data.ortho_scale = ortho_scale
        look_at(camera, view_target)
        suffix = f"{pose_name}-{round(pose_phase * 100):03d}-{name}"
        scene.render.filepath = str(preview_dir / f"operator-{suffix}.png")
        bpy.ops.render.render(write_still=True)
