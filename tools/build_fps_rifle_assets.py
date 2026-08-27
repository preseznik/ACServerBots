"""Build project-owned FPS rifle blockout KN5 models for Assetto Corsa.

Run through tools/Build-FpsClientAssets.ps1. The repository-pinned Blender
exporter writes KN5 directly, so this build is deterministic and headless.
"""

from __future__ import annotations

import argparse
import math
from pathlib import Path
import sys

import bpy


def ac_to_blender(position: tuple[float, float, float]) -> tuple[float, float, float]:
    """Map AC X/Y/Z coordinates to Blender X/Y/Z before FBX axis conversion."""

    x, y, z = position
    return x, -z, y


def ac_size_to_blender(size: tuple[float, float, float]) -> tuple[float, float, float]:
    x, y, z = size
    return x, z, y


def material(name: str, color: tuple[float, float, float, float], metallic: float = 0.0):
    value = bpy.data.materials.new(name)
    value.diffuse_color = color
    value.metallic = metallic
    value.roughness = 0.42
    value.use_nodes = True
    image = bpy.data.images.new(f"{name}.png", width=2, height=2, alpha=True)
    image.file_format = "PNG"
    image.pixels = list(color) * 4
    image.pack()
    texture = value.node_tree.nodes.new("ShaderNodeTexImage")
    texture.name = f"{name}_DIFFUSE"
    texture.image = image
    texture.assettoCorsa.shaderInputName = "txDiffuse"
    shader = value.node_tree.nodes.get("Principled BSDF")
    if shader:
        value.node_tree.links.new(texture.outputs["Color"], shader.inputs["Base Color"])
    return value


def box(name: str, position: tuple[float, float, float], size: tuple[float, float, float], mat,
        bevel: float = 0.012):
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
    obj.data.materials.append(mat)
    return obj


def cylinder(name: str, position: tuple[float, float, float], radius: float, length: float, mat):
    # Blender cylinder axis is Z. Rotate it onto Blender Y, which maps to AC Z.
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=radius, depth=length,
                                       location=ac_to_blender(position),
                                       rotation=(math.pi / 2, 0, 0))
    obj = bpy.context.object
    obj.name = name
    obj.data.materials.append(mat)
    return obj


def reset_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for item in list(bpy.data.materials):
        bpy.data.materials.remove(item)


def build_rifle(include_arms: bool):
    body = material("ASRC_RIFLE_BODY", (0.055, 0.075, 0.09, 1), 0.65)
    detail = material("ASRC_RIFLE_DETAIL", (0.18, 0.22, 0.25, 1), 0.8)
    diagnostic = material("ASRC_RIFLE_DIAGNOSTIC", (1.0, 0.035, 0.48, 1), 0.15)
    sleeve = material("ASRC_RIFLE_SLEEVE", (0.09, 0.12, 0.15, 1))
    glove = material("ASRC_RIFLE_GLOVE", (0.025, 0.03, 0.035, 1))

    # Recognizable compact assault-rifle silhouette, authored in AC coordinates
    # with the barrel pointing along local +Z.
    box("RIFLE_RECEIVER", (0, 0.00, 0.30), (0.15, 0.16, 0.38), body)
    box("RIFLE_HANDGUARD", (0, 0.015, 0.60), (0.13, 0.13, 0.28), body)
    cylinder("RIFLE_BARREL", (0, 0.025, 0.89), 0.025, 0.34, detail)
    cylinder("RIFLE_MUZZLE", (0, 0.025, 1.075), 0.04, 0.08, diagnostic)
    box("RIFLE_STOCK", (0, -0.005, 0.02), (0.18, 0.18, 0.24), body)
    box("RIFLE_TOP_RAIL", (0, 0.105, 0.48), (0.13, 0.025, 0.48), detail, 0.004)
    box("RIFLE_OPTIC", (0, 0.155, 0.44), (0.09, 0.08, 0.13), diagnostic)
    box("RIFLE_PISTOL_GRIP", (0, -0.13, 0.31), (0.09, 0.22, 0.12), body)
    box("RIFLE_MAGAZINE", (0, -0.14, 0.48), (0.10, 0.25, 0.12), diagnostic)

    if include_arms:
        box("VIEWMODEL_RIGHT_SLEEVE", (0.22, -0.12, 0.16), (0.15, 0.16, 0.54), sleeve, 0.025)
        box("VIEWMODEL_LEFT_SLEEVE", (-0.18, -0.08, 0.55), (0.15, 0.15, 0.46), sleeve, 0.025)
        box("VIEWMODEL_RIGHT_GLOVE", (0.10, -0.06, 0.35), (0.14, 0.11, 0.16), glove, 0.025)
        box("VIEWMODEL_LEFT_GLOVE", (-0.08, -0.025, 0.73), (0.14, 0.11, 0.16), glove, 0.025)


def export_kn5(path: Path, exporter) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    warnings: list[str] = []
    # Blender AC Tools defaults LOD Out to zero. CSP can load such a KN5 and
    # return a valid scene reference while culling every mesh immediately.
    # Viewmodels move with the camera and world weapons can be seen across an
    # arena, so give every generated mesh an explicit useful render range.
    settings = {
        "nodes": {
            "*": {
                "lodIn": 0.0,
                "lodOut": 10_000.0,
                "visible": True,
                "renderable": True,
                "castShadows": False,
            }
        }
    }
    with path.open("wb") as output:
        writer = exporter.KN5FileWriter(
            output,
            bpy.context,
            settings,
            warnings,
            root_node_name=path.stem.upper(),
            even_split=False,
            forward_axis="-Y",
        )
        writer.write()
    for warning in warnings:
        print(f"KN5 export warning: {warning}")
    print(f"Built {path} ({path.stat().st_size} bytes)")


def main() -> None:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--exporter-root", required=True)
    options = parser.parse_args(args)
    output = Path(options.output_dir).resolve()
    exporter_root = Path(options.exporter_root).resolve()
    sys.path.insert(0, str(exporter_root.parent))
    import blender_assetto_corsa_tools as ac_tools
    from blender_assetto_corsa_tools import exporter

    ac_tools.register()

    reset_scene()
    build_rifle(include_arms=True)
    export_kn5(output / "asrc_assault_rifle_viewmodel.kn5", exporter)

    reset_scene()
    build_rifle(include_arms=False)
    export_kn5(output / "asrc_assault_rifle_world.kn5", exporter)


if __name__ == "__main__":
    main()
