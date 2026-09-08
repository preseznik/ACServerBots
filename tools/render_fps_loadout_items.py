"""Render transparent loadout cards from the same source meshes as the FPS weapons.

Run with Blender --background --python this_script -- --source-root WEAPONS
--mp5-source EXTRACTED_MP5 --output-dir Assets/Fps. Reuses the weapon builders'
material assignments, inserted magazines and omitted loose accessories.
"""

from __future__ import annotations

import argparse
from pathlib import Path
import sys
import tempfile

import bpy
from mathutils import Matrix, Vector

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
sys.path.insert(0, str(TOOLS / "vendor"))
import blender_assetto_corsa_tools as ac_tools
import build_fps_desert_eagle_assets as eagle
import build_fps_colt_1911_assets as colt
import build_fps_mp5_assets as mp5
import build_fps_grenade_assets as grenade


def import_rifle(source_root: Path) -> list:
    root = source_root / "fps-animated-carbine"
    bpy.ops.import_scene.fbx(filepath=str(root / "source/arms@carbine.fbx"))
    bpy.context.scene.frame_set(180)
    bpy.context.view_layer.update()
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"
              and obj.name not in {"Cube", "armmesh", "bullet", "lens"}]
    material = bpy.data.materials.new("LOADOUT_CARBINE")
    material.use_nodes = True
    texture = material.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(str(root / "textures/carbineColor.png"))
    material.node_tree.links.new(texture.outputs["Color"],
                                material.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    for obj in meshes:
        eagle.assign_single_material(obj, material)
    return meshes


def prepare_meshes(meshes: list) -> list:
    """Bake the pose before removing rigs, then normalize studio lighting scale."""
    bpy.context.view_layer.update()
    graph = bpy.context.evaluated_depsgraph_get()
    baked = []
    for obj in meshes:
        evaluated = obj.evaluated_get(graph)
        data = bpy.data.meshes.new_from_object(evaluated, depsgraph=graph)
        data.transform(obj.matrix_world)
        copy = bpy.data.objects.new(obj.name + "_CARD", data)
        baked.append(copy)
    for obj in list(bpy.context.scene.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    points = [v.co.copy() for obj in baked for v in obj.data.vertices]
    lower = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    upper = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    center = (lower + upper) / 2
    scale = 1 / max(upper - lower)
    print(f"Source card bounds: {tuple(upper - lower)}")
    for obj in baked:
        obj.data.transform(Matrix.Scale(scale, 4) @ Matrix.Translation(-center))
        bpy.context.scene.collection.objects.link(obj)
        for material in obj.data.materials:
            shader = material.node_tree.nodes.get("Principled BSDF")
            shader.inputs["Metallic"].default_value = 0.28
            shader.inputs["Roughness"].default_value = 0.42
            for node in list(material.node_tree.nodes):
                if node.type != "TEX_IMAGE":
                    continue
                slot = node.assettoCorsa.shaderInputName
                if slot == "txDiffuse":
                    material.node_tree.links.new(node.outputs["Color"], shader.inputs["Base Color"])
                elif slot == "txNormal" and not shader.inputs["Normal"].is_linked:
                    normal = material.node_tree.nodes.new("ShaderNodeNormalMap")
                    material.node_tree.links.new(node.outputs["Color"], normal.inputs["Color"])
                    material.node_tree.links.new(normal.outputs["Normal"], shader.inputs["Normal"])
    return baked


def look_at(obj, point=Vector()):
    obj.rotation_euler = (point - obj.location).to_track_quat("-Z", "Y").to_euler()


def render(slug: str, meshes: list, output: Path):
    meshes = prepare_meshes(meshes)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.film_transparent = True
    scene.render.resolution_x = 900 if slug in ("assault_rifle", "compact_smg") else 560
    scene.render.resolution_y = 360
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.look = "AgX - Medium High Contrast"
    world = bpy.data.worlds.new("Loadout studio")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.2, 0.23, 0.28, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.6
    scene.world = world
    for location, energy, size in [((-2, -2, 3), 450, 3), ((2, 1, 2), 550, 2), ((-2, 2, 0), 120, 2)]:
        data = bpy.data.lights.new("Studio", "AREA")
        data.energy, data.size = energy, size
        light = bpy.data.objects.new("Studio", data)
        scene.collection.objects.link(light)
        light.location = location
        look_at(light)
    data = bpy.data.cameras.new("Card")
    data.type = "ORTHO"
    camera = bpy.data.objects.new("Card", data)
    scene.collection.objects.link(camera)
    camera.location = (3, 0.35, 0.6)
    if "grenade" in slug:
        camera.location = (-2.5, -3, 1.2)
    look_at(camera)
    scene.camera = camera
    bpy.context.view_layer.update()
    view = camera.matrix_world.inverted()
    points = [view @ (obj.matrix_world @ v.co) for obj in meshes for v in obj.data.vertices]
    width = max(p.x for p in points) - min(p.x for p in points)
    height = max(p.y for p in points) - min(p.y for p in points)
    aspect = scene.render.resolution_x / scene.render.resolution_y
    data.ortho_scale = max(width, height * aspect) * 1.14
    # Center the projected silhouette, which differs slightly from its world AABB.
    offset = Vector(((min(p.x for p in points) + max(p.x for p in points)) / 2,
                     (min(p.y for p in points) + max(p.y for p in points)) / 2, 0))
    camera.location += camera.rotation_euler.to_matrix() @ offset
    scene.render.filepath = str(output / f"asrc_loadout_{slug}.png")
    bpy.ops.render.render(write_still=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--mp5-source", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--only", default="")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    args.source_root = args.source_root.resolve()
    args.mp5_source = args.mp5_source.resolve()
    args.output_dir = args.output_dir.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    ac_tools.register()
    with tempfile.TemporaryDirectory(prefix="asrc-loadout-art-") as temporary:
        work = Path(temporary)
        for slug in ("assault_rifle", "compact_smg", "desert_eagle", "colt_1911", "frag_grenade", "sticky_grenade"):
            if args.only and args.only != slug:
                continue
            bpy.ops.wm.read_factory_settings(use_empty=True)
            if slug == "assault_rifle":
                meshes = import_rifle(args.source_root)
            elif slug == "compact_smg":
                meshes = mp5.import_weapon(args.mp5_source / "MP5.fbx", args.mp5_source, work, False)
            elif slug == "desert_eagle":
                source = args.source_root / "desert-eagle"
                meshes = eagle.import_weapon(source / "source/Deagle_full.fbx", source / "textures", work, False)
                meshes = [obj for obj in meshes if not obj.name.endswith("_BULLET")]
            elif slug == "colt_1911":
                source = args.source_root / "m1911-pistol-with-magazine-and-bullet"
                meshes = colt.import_weapon(source / "source" / colt.SOURCE_FILE_NAME, source / "textures", work, False)
            else:
                source = args.source_root / ("m67-grenade" if slug == "frag_grenade" else "semtex-sticky-grenade")
                meshes = grenade.import_grenade(slug, source, work, False)
            render(slug, meshes, args.output_dir)
    print("Loadout artwork rendered successfully")


if __name__ == "__main__":
    main()
