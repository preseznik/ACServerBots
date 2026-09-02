"""Render the shipped carbine as transparent HUD artwork.

Run with Blender in background mode. The source model is posed at its authored idle
frame, but arms, bullet and optic glass are excluded so the result is a clean weapon
silhouette rather than a generic icon.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def arguments() -> tuple[Path, Path]:
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) != 2:
        raise SystemExit("usage: blender -b --python render_fps_hud_weapon.py -- SOURCE.fbx OUTPUT.png")
    return Path(values[0]).resolve(), Path(values[1]).resolve()


def look_at(camera: bpy.types.Object, target: Vector) -> None:
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()


def main() -> None:
    source, output = arguments()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(source))
    bpy.context.scene.frame_set(180)
    bpy.context.view_layer.update()

    excluded = {"Cube", "armmesh", "bullet", "lens"}
    weapon = [obj for obj in bpy.context.scene.objects if obj.type == "MESH" and obj.name not in excluded]
    if len(weapon) < 10:
        raise RuntimeError("carbine source does not contain the expected complete weapon")

    for obj in list(bpy.context.scene.objects):
        obj.hide_render = obj not in weapon

    diffuse = bpy.data.images.load(str(source.parent.parent / "textures" / "carbineColor.png"))
    material = bpy.data.materials.new("ASRC_HUD_CARBINE")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    principled = nodes.get("Principled BSDF")
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = diffuse
    links.new(texture.outputs["Color"], principled.inputs["Base Color"])
    principled.inputs["Metallic"].default_value = 0.32
    principled.inputs["Roughness"].default_value = 0.36
    for obj in weapon:
        obj.data.materials.clear()
        obj.data.materials.append(material)

    minimum = Vector((math.inf, math.inf, math.inf))
    maximum = Vector((-math.inf, -math.inf, -math.inf))
    for obj in weapon:
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            minimum.x = min(minimum.x, point.x)
            minimum.y = min(minimum.y, point.y)
            minimum.z = min(minimum.z, point.z)
            maximum.x = max(maximum.x, point.x)
            maximum.y = max(maximum.y, point.y)
            maximum.z = max(maximum.z, point.z)
    center = (minimum + maximum) * 0.5
    extent = maximum - minimum

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.film_transparent = True
    scene.render.resolution_x = 900
    scene.render.resolution_y = 360
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    scene.view_settings.look = "AgX - Medium High Contrast"

    world = bpy.data.worlds.new("ASRC_HUD_WORLD")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.08, 0.1, 0.13, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.55
    scene.world = world

    for name, location, energy, size in (
        ("KEY", center + Vector((-4, -3, 5)), 900, 4.0),
        ("RIM", center + Vector((4, 2, 3)), 650, 3.0),
    ):
        light_data = bpy.data.lights.new(name, "AREA")
        light_data.energy = energy
        light_data.shape = "DISK"
        light_data.size = size
        light = bpy.data.objects.new(name, light_data)
        light.location = location
        scene.collection.objects.link(light)
        look_at(light, center)

    camera_data = bpy.data.cameras.new("ASRC_HUD_CAMERA")
    camera_data.type = "ORTHO"
    camera = bpy.data.objects.new("ASRC_HUD_CAMERA", camera_data)
    scene.collection.objects.link(camera)
    # The source rifle runs chiefly along local Y. A mild three-quarter angle shows
    # the receiver and optic while retaining the recognizable full silhouette.
    camera.location = center + Vector((extent.length * 0.85, -extent.length * 1.8,
                                       extent.length * 0.7))
    look_at(camera, center + Vector((0, 0, extent.z * 0.03)))
    camera_data.ortho_scale = max(extent.z * 2.4, extent.x * 1.5, 0.8)
    scene.camera = camera

    output.parent.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = str(output)
    bpy.ops.render.render(write_still=True)
    print(f"Rendered FPS HUD weapon: {output}")


if __name__ == "__main__":
    main()
