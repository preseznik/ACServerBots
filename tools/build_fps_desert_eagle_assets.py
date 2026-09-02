"""Build the CC BY Desert Eagle FPS models for Assetto Corsa/CSP.

The downloaded FBX and textures stay outside the repository. This script performs
the reproducible scale/orientation, texture-budget and KN5 conversion steps only.
Run it through tools/Build-FpsDesertEagleAssets.ps1.
"""

from __future__ import annotations

import argparse
import math
from pathlib import Path
import sys
import tempfile

import bpy
from mathutils import Vector


SOURCE_MATERIALS = {
    "MainBody": ("MAIN_BODY", "T_Deagle_MainBody", 1024),
    "Slide": ("SLIDE", "T_Deagle_Slide", 1024),
    "Magazine": ("MAGAZINE", "T_Deagle_Magazine", 512),
    "Bullet": ("BULLET", "T_Deagle_Bullet", 512),
}
GRIP_ANCHOR = Vector((0.0, 0.10845, 0.16544))
VIEWMODEL_OFFSET = Vector((0.0, -0.34, 0.02))


def reset_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in (bpy.data.meshes, bpy.data.materials, bpy.data.images,
                       bpy.data.cameras, bpy.data.lights):
        for item in list(collection):
            collection.remove(item)


def load_image(path: Path, name: str, work_dir: Path, maximum_size: int,
               non_color: bool = False):
    if not path.is_file():
        raise FileNotFoundError(f"Desert Eagle texture was not found: {path}")
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


def textured_material(name: str, texture_prefix: str, texture_dir: Path,
                      work_dir: Path, maximum_size: int):
    material = bpy.data.materials.new(f"ASRC_DEAGLE_{name}")
    material.use_nodes = True
    material.assettoCorsa.shaderName = "ksPerPixel"
    material.assettoCorsa.alphaBlendMode = "0"
    material.assettoCorsa.alphaTested = False
    material.assettoCorsa.depthMode = "0"
    nodes = material.node_tree.nodes

    diffuse = load_image(texture_dir / f"{texture_prefix}_BaseColor.png",
                         f"ASRC_DEAGLE_{name}_DIFFUSE", work_dir, maximum_size)
    diffuse_node = nodes.new("ShaderNodeTexImage")
    diffuse_node.name = f"ASRC_DEAGLE_{name}_DIFFUSE"
    diffuse_node.image = diffuse
    diffuse_node.assettoCorsa.shaderInputName = "txDiffuse"

    normal_size = min(maximum_size, 512 if maximum_size >= 1024 else 256)
    normal = load_image(texture_dir / f"{texture_prefix}_Normal.png",
                        f"ASRC_DEAGLE_{name}_NORMAL", work_dir, normal_size, True)
    normal_node = nodes.new("ShaderNodeTexImage")
    normal_node.name = f"ASRC_DEAGLE_{name}_NORMAL"
    normal_node.image = normal
    normal_node.assettoCorsa.shaderInputName = "txNormal"
    if hasattr(diffuse_node, "show_texture"):
        diffuse_node.show_texture = True
    nodes.active = diffuse_node

    shader = nodes.get("Principled BSDF")
    if shader is not None:
        material.node_tree.links.new(diffuse_node.outputs["Color"], shader.inputs["Base Color"])
        normal_map = nodes.new("ShaderNodeNormalMap")
        material.node_tree.links.new(normal_node.outputs["Color"], normal_map.inputs["Color"])
        material.node_tree.links.new(normal_map.outputs["Normal"], shader.inputs["Normal"])
        shader.inputs["Metallic"].default_value = 0.72
        shader.inputs["Roughness"].default_value = 0.31
    return material


def solid_material(name: str, color: tuple[float, float, float, float], work_dir: Path):
    material = bpy.data.materials.new(name)
    material.diffuse_color = color
    material.use_nodes = True
    material.assettoCorsa.shaderName = "ksPerPixel"
    material.assettoCorsa.alphaBlendMode = "0"
    material.assettoCorsa.alphaTested = False
    material.assettoCorsa.depthMode = "0"
    image = bpy.data.images.new(f"{name}_DIFFUSE", width=2, height=2, alpha=True)
    image.pixels = list(color) * 4
    image.filepath_raw = str(work_dir / f"{name}_DIFFUSE.png")
    image.file_format = "PNG"
    image.save()
    image.pack()
    node = material.node_tree.nodes.new("ShaderNodeTexImage")
    node.name = f"{name}_DIFFUSE"
    node.image = image
    node.assettoCorsa.shaderInputName = "txDiffuse"
    if hasattr(node, "show_texture"):
        node.show_texture = True
    material.node_tree.nodes.active = node
    return material


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


def ac_to_blender(position: tuple[float, float, float]) -> tuple[float, float, float]:
    x, y, z = position
    return x, -z, y


def ac_size_to_blender(size: tuple[float, float, float]) -> tuple[float, float, float]:
    x, y, z = size
    return x, z, y


def box(name: str, position: tuple[float, float, float],
        size: tuple[float, float, float], material, bevel: float = 0.012):
    bpy.ops.mesh.primitive_cube_add(location=ac_to_blender(position))
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = ac_size_to_blender(size)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0:
        modifier = obj.modifiers.new("EDGE_SOFTENING", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.data.materials.append(material)
    return obj


def import_weapon(fbx_path: Path, texture_dir: Path, work_dir: Path,
                  viewmodel: bool) -> list:
    bpy.ops.import_scene.fbx(filepath=str(fbx_path))
    source_meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(source_meshes) != 28:
        raise RuntimeError(
            f"Desert Eagle source contract changed: expected 28 meshes, got {len(source_meshes)}")

    materials = {
        source: textured_material(label, prefix, texture_dir, work_dir,
                                  maximum_size if viewmodel else min(maximum_size, 512))
        for source, (label, prefix, maximum_size) in SOURCE_MATERIALS.items()
    }
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
        if viewmodel:
            obj.matrix_world.translation += VIEWMODEL_OFFSET
        obj.data.materials.clear()
        obj.data.materials.append(materials[source_name])
        grouped[source_name].append(obj)

    for obj in list(bpy.context.scene.objects):
        if obj.type != "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)
    for material in list(bpy.data.materials):
        if material.users == 0:
            bpy.data.materials.remove(material)

    weapon_objects = []
    for source_name, objects in grouped.items():
        label = SOURCE_MATERIALS[source_name][0]
        weapon_objects.append(join_objects(objects, f"ASRC_DESERT_EAGLE_{label}"))
    return weapon_objects


def add_viewmodel_arms(work_dir: Path) -> list:
    sleeve = solid_material("ASRC_DEAGLE_SLEEVE", (0.09, 0.12, 0.15, 1), work_dir)
    glove = solid_material("ASRC_DEAGLE_GLOVE", (0.025, 0.03, 0.035, 1), work_dir)
    sleeve_parts = [
        box("ASRC_DEAGLE_RIGHT_SLEEVE_PART", (0.20, -0.14, 0.12),
            (0.15, 0.17, 0.50), sleeve, 0.025),
        box("ASRC_DEAGLE_LEFT_SLEEVE_PART", (-0.17, -0.11, 0.20),
            (0.15, 0.16, 0.45), sleeve, 0.025),
    ]
    glove_parts = [
        box("ASRC_DEAGLE_RIGHT_GLOVE_PART", (0.045, -0.035, 0.31),
            (0.13, 0.11, 0.16), glove, 0.022),
        box("ASRC_DEAGLE_LEFT_GLOVE_PART", (-0.045, -0.025, 0.36),
            (0.13, 0.11, 0.16), glove, 0.022),
    ]
    return [
        join_objects(sleeve_parts, "ASRC_DESERT_EAGLE_VIEWMODEL_SLEEVES"),
        join_objects(glove_parts, "ASRC_DESERT_EAGLE_VIEWMODEL_GLOVES"),
    ]


def export_kn5(path: Path, exporter, cast_shadows: bool) -> None:
    warnings: list[str] = []
    materials = {
        material.name: {
            "shaderName": "ksPerPixel",
            "alphaBlendMode": "Opaque",
            "depthMode": "DepthNormal",
            "properties": {
                "ksAmbient": {"valueA": 0.35},
                "ksDiffuse": {"valueA": 0.65},
                "ksSpecular": {"valueA": 0.55},
                "ksSpecularEXP": {"valueA": 45.0},
            },
        }
        for material in bpy.data.materials
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


def build_model(fbx_path: Path, texture_dir: Path, output_path: Path,
                work_dir: Path, exporter, viewmodel: bool) -> None:
    reset_scene()
    import_weapon(fbx_path, texture_dir, work_dir, viewmodel)
    if viewmodel:
        add_viewmodel_arms(work_dir)
    export_kn5(output_path, exporter, cast_shadows=not viewmodel)


def main() -> None:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--exporter-root", required=True)
    parser.add_argument("--success-marker", required=True)
    options = parser.parse_args(args)

    source_dir = Path(options.source_dir).resolve()
    output_dir = Path(options.output_dir).resolve()
    fbx_path = source_dir / "source" / "Deagle_full.fbx"
    texture_dir = source_dir / "textures"
    if not fbx_path.is_file():
        raise FileNotFoundError(f"Desert Eagle FBX was not found: {fbx_path}")
    exporter_root = Path(options.exporter_root).resolve()
    sys.path.insert(0, str(exporter_root.parent))
    import blender_assetto_corsa_tools as ac_tools
    from blender_assetto_corsa_tools import exporter

    ac_tools.register()
    output_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="asrc-deagle-") as temporary:
        work_dir = Path(temporary)
        build_model(fbx_path, texture_dir,
                    output_dir / "asrc_desert_eagle_viewmodel.kn5",
                    work_dir, exporter, viewmodel=True)
        build_model(fbx_path, texture_dir,
                    output_dir / "asrc_desert_eagle_world.kn5",
                    work_dir, exporter, viewmodel=False)
    Path(options.success_marker).write_text("ok\n", encoding="utf-8")


if __name__ == "__main__":
    main()
