"""Build DanaeH's CC BY M1911 for the AssettoServer FPS loadout.

The source FBX and textures remain outside the repository. This generator
normalizes the pistol around its grip, inserts the separately authored
magazine, and reuses the proven Desert Eagle pistol rig and animations.
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
import build_fps_desert_eagle_assets as pistol


ASSET_SLUG = "colt_1911"
SOURCE_FILE_NAME = "m1911+mag+bullets_final_low.fbx"
PISTOL_TEXTURE_PREFIX = "m1911_V5_low_1001"
MAGAZINE_TEXTURE_PREFIX = "mag_v5_low_1001"
GRIP_ANCHOR = Vector((0.07113, 0.00062, -0.01552))
MAGAZINE_INSERT_OFFSET = Vector((-0.10149, 0.00062, 0.00251))
SOURCE_TO_RUNTIME = Matrix.Rotation(math.radians(90), 4, "Z")
MAGAZINE_OBJECT = "mag_low.006"
OMITTED_ACCESSORIES = {"shell_low", "bullet_low"}
SLIDE_OBJECTS = {
    "top_low", "cover_low", "foresight_low", "farsight_low",
    "detail1_low", "detail2_low", "front1_low", "front2_low",
}
EXPECTED_SOURCE_OBJECTS = {
    "barrel_low", "front2_low", "trigger_low", "hammerstop_low",
    "back_low", "attach_low", "magrelease_low", "detail2_low",
    "detail1_low", "cover_low", "foresight_low", "farsight_low",
    "hammer_low", "front1_low", "safety_low", "top_low", "main_low",
    "shell_low", "bullet_low", "screws_low", "grip_low", MAGAZINE_OBJECT,
}


def weapon_materials(texture_dir: Path, work_dir: Path, skinned: bool) -> dict:
    shader_name = "ksSkinnedMesh" if skinned else "ksPerPixel"
    pistol_size = 1024 if skinned else 512
    return {
        "PISTOL": pistol.create_material("ASRC_COLT_1911_PISTOL", shader_name, {
            "txDiffuse": (
                texture_dir / f"{PISTOL_TEXTURE_PREFIX}_BaseColor.png",
                pistol_size, False),
            "txNormal": (
                texture_dir / f"{PISTOL_TEXTURE_PREFIX}_Normal.png",
                min(pistol_size, 512), True),
        }, work_dir),
        "MAGAZINE": pistol.create_material("ASRC_COLT_1911_MAGAZINE", shader_name, {
            "txDiffuse": (
                texture_dir / f"{MAGAZINE_TEXTURE_PREFIX}_BaseColor.png",
                512, False),
            "txNormal": (
                texture_dir / f"{MAGAZINE_TEXTURE_PREFIX}_Normal.png",
                256, True),
        }, work_dir),
    }


def import_weapon(fbx_path: Path, texture_dir: Path, work_dir: Path,
                  skinned: bool, grip_target: Vector | None = None) -> list:
    existing = set(bpy.context.scene.objects)
    bpy.ops.import_scene.fbx(filepath=str(fbx_path))
    imported = [obj for obj in bpy.context.scene.objects if obj not in existing]
    source_meshes = {
        obj.name: obj for obj in imported
        if obj.type == "MESH" and obj.name != "Cube"
    }
    if set(source_meshes) != EXPECTED_SOURCE_OBJECTS:
        missing = sorted(EXPECTED_SOURCE_OBJECTS - set(source_meshes))
        unexpected = sorted(set(source_meshes) - EXPECTED_SOURCE_OBJECTS)
        raise RuntimeError(
            f"M1911 source contract changed: missing={missing}, unexpected={unexpected}")

    materials = weapon_materials(texture_dir, work_dir, skinned)
    grouped: dict[str, list] = {"BODY": [], "SLIDE": [], "MAGAZINE": []}
    target = Matrix.Translation(grip_target or Vector()) @ SOURCE_TO_RUNTIME
    for name, obj in source_meshes.items():
        if name in OMITTED_ACCESSORIES:
            continue
        world = obj.matrix_world.copy()
        obj.parent = None
        source_offset = MAGAZINE_INSERT_OFFSET if name == MAGAZINE_OBJECT else Vector()
        obj.matrix_world = (target @ Matrix.Translation(-GRIP_ANCHOR)
                            @ Matrix.Translation(source_offset) @ world)
        material_key = "MAGAZINE" if name == MAGAZINE_OBJECT else "PISTOL"
        pistol.assign_single_material(obj, materials[material_key])
        group_key = ("MAGAZINE" if name == MAGAZINE_OBJECT
                     else "SLIDE" if name in SLIDE_OBJECTS else "BODY")
        grouped[group_key].append(obj)

    retained = {obj for objects in grouped.values() for obj in objects}
    for obj in imported:
        if obj not in retained and obj.name in bpy.data.objects:
            pistol.remove_object(obj)

    prefix = "ASRC_COLT_1911_VIEWMODEL" if skinned else "ASRC_COLT_1911_WORLD"
    result = []
    for group_key in ("BODY", "SLIDE", "MAGAZINE"):
        name = f"{prefix}_{group_key}"
        if len(grouped[group_key]) == 1:
            joined = grouped[group_key][0]
            joined.name = name
            joined.data.name = name
        else:
            joined = pistol.join_objects(grouped[group_key], name)
        pistol.assign_single_material(
            joined, materials["MAGAZINE" if group_key == "MAGAZINE" else "PISTOL"])
        result.append(joined)
    pistol.purge_unused_data()
    return result


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
    fbx_path = source_dir / "source" / SOURCE_FILE_NAME
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
    output_dir = Path(options.output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    pistol.ASSET_SLUG = ASSET_SLUG
    pistol.FIRING_ARM_NODE_NAME = "ASRC_COLT_1911_FIRING_ARM"
    pistol.SUPPORT_ARM_NODE_NAME = "ASRC_COLT_1911_SUPPORT_ARM"
    pistol.import_weapon = import_weapon
    with tempfile.TemporaryDirectory(prefix="asrc-colt-1911-") as temporary:
        work_dir = Path(temporary)
        pistol.build_viewmodel(
            carbine_fbx, fbx_path, texture_dir, output_dir, work_dir,
            exporter, KSAnimWriter,
            Path(options.preview_path).resolve() if options.preview_path else None)
        pistol.build_world(fbx_path, texture_dir, output_dir, work_dir, exporter)
    Path(options.success_marker).write_text("ok\n", encoding="utf-8")


if __name__ == "__main__":
    main()
