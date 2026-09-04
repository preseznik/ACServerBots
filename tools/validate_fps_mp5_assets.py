"""Validate generated Compact SMG/MP5 KN5 and KSANIM assets."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from validate_fps_modern_assets import inspect_kn5, inspect_ksanim


CLIPS = ("idle", "fire", "reload", "reload_empty", "equip", "sprint")
EXPECTED_FRAMES = {
    "idle": 26,
    "fire": 7,
    "reload": 59,
    "reload_empty": 65,
    "equip": 17,
    "sprint": 26,
}


def validate_viewmodel(path: Path) -> set[str]:
    summary = inspect_kn5(path)
    if summary.triangles != 27_948:
        raise ValueError(f"Unexpected MP5 viewmodel triangles: {summary.triangles}")
    if summary.rigid_meshes != 0 or summary.skinned_meshes != 3:
        raise ValueError(
            "MP5 viewmodel must contain arms, body and magazine skinned meshes: "
            f"rigid={summary.rigid_meshes}, skinned={summary.skinned_meshes}")
    if summary.materials != 3 or set(summary.shaders) != {"ksSkinnedMesh"}:
        raise ValueError(
            f"Unexpected MP5 viewmodel materials: {summary.material_names}, "
            f"shaders={summary.shaders}")
    if summary.bones != 51:
        raise ValueError(f"MP5 viewmodel rig changed: bones={summary.bones}")
    required_nodes = {
        "ASRC_COMPACT_SMG_ARMS__MESH",
        "ASRC_VIEWMODEL_WEAPON_BONE",
        "ASRC_VIEWMODEL_MAGAZINE_BONE",
        "ASRC_COMPACT_SMG_VIEWMODEL_BODY",
        "ASRC_COMPACT_SMG_VIEWMODEL_MAGAZINE",
    }
    missing = required_nodes - set(summary.node_names)
    if missing:
        raise ValueError(f"MP5 viewmodel nodes missing: {sorted(missing)}")
    if len(summary.texture_dimensions) != 9:
        raise ValueError(f"Unexpected MP5 viewmodel textures: {summary.texture_dimensions}")
    if any(width > 2048 or height > 2048
           for _, width, height in summary.texture_dimensions):
        raise ValueError("MP5 viewmodel texture budget exceeded")
    print(
        f"Validated {path.name}: triangles={summary.triangles}, "
        f"skinnedMeshes={summary.skinned_meshes}, bones={summary.bones}, "
        f"materials={summary.materials}, textures={len(summary.texture_dimensions)}")
    return set(summary.node_names)


def validate_world(path: Path) -> None:
    summary = inspect_kn5(path)
    if summary.triangles != 14_248:
        raise ValueError(f"Unexpected MP5 world triangles: {summary.triangles}")
    if summary.rigid_meshes != 2 or summary.skinned_meshes != 0:
        raise ValueError(
            f"Unexpected MP5 world layout: rigid={summary.rigid_meshes}, "
            f"skinned={summary.skinned_meshes}")
    if summary.materials != 2 or set(summary.shaders) != {"ksPerPixel"}:
        raise ValueError(
            f"Unexpected MP5 world materials: {summary.material_names}, "
            f"shaders={summary.shaders}")
    if len(summary.texture_dimensions) != 6:
        raise ValueError(f"Unexpected MP5 world textures: {summary.texture_dimensions}")
    if any(width > 512 or height > 512
           for _, width, height in summary.texture_dimensions):
        raise ValueError("MP5 world texture budget exceeded")
    print(
        f"Validated {path.name}: triangles={summary.triangles}, "
        f"rigidMeshes={summary.rigid_meshes}, materials={summary.materials}, "
        f"textures={len(summary.texture_dimensions)}")


def validate_animations(asset_dir: Path, node_names: set[str]) -> None:
    expected_tracks = None
    animations = {}
    for clip in CLIPS:
        path = asset_dir / f"asrc_compact_smg_{clip}.ksanim"
        tracks = inspect_ksanim(path)
        animations[clip] = tracks
        names = tuple(tracks)
        if expected_tracks is None:
            expected_tracks = names
        elif names != expected_tracks:
            raise ValueError(f"MP5 animation track mismatch: {path.name}")
        missing = set(names) - node_names
        if missing:
            raise ValueError(f"MP5 animation targets missing KN5 nodes: {sorted(missing)}")
        for bone_name in ("ASRC_VIEWMODEL_WEAPON_BONE",
                          "ASRC_VIEWMODEL_MAGAZINE_BONE"):
            if bone_name not in tracks:
                raise ValueError(f"MP5 animation lacks {bone_name}: {path.name}")
        frame_count = len(next(iter(tracks.values())))
        if frame_count != EXPECTED_FRAMES[clip]:
            raise ValueError(f"Unexpected MP5 {clip} frame count: {frame_count}")

    for clip in ("reload", "reload_empty"):
        magazine_frames = animations[clip]["ASRC_VIEWMODEL_MAGAZINE_BONE"]
        magazine_span = max(
            max(frame[axis] for frame in magazine_frames)
            - min(frame[axis] for frame in magazine_frames)
            for axis in range(4, 7))
        if magazine_span < 15.0:
            raise ValueError(f"MP5 {clip} does not extract magazine: span={magazine_span}")
    right_arm_frames = animations["reload"]["R_arm"]
    right_arm_span = max(
        max(frame[axis] for frame in right_arm_frames)
        - min(frame[axis] for frame in right_arm_frames)
        for axis in range(7))
    if right_arm_span < 0.02:
        raise ValueError("MP5 reload does not retain the carbine arm animation")
    print("Validated six MP5 animation clips with detachable magazine motion")


def main() -> None:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--asset-dir", required=True)
    parser.add_argument("--success-marker", required=True)
    options = parser.parse_args(args)
    asset_dir = Path(options.asset_dir).resolve()
    node_names = validate_viewmodel(asset_dir / "asrc_compact_smg_viewmodel.kn5")
    validate_world(asset_dir / "asrc_compact_smg_world.kn5")
    validate_animations(asset_dir, node_names)
    Path(options.success_marker).write_text("ok\n", encoding="utf-8")


if __name__ == "__main__":
    main()
