"""Validate generated Desert Eagle KN5 and KSANIM assets without scene state."""

from __future__ import annotations

import argparse
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from validate_fps_modern_assets import inspect_kn5, inspect_ksanim


CLIPS = ("idle", "fire", "equip", "sprint", "reload")
EXPECTED_FRAMES = {
    "idle": 30,
    "fire": 8,
    "equip": 20,
    "sprint": 24,
    "reload": 55,
}


def validate_viewmodel(path: Path) -> None:
    summary = inspect_kn5(path)
    if summary.triangles != 26_979:
        raise ValueError(
            f"Unexpected Desert Eagle viewmodel triangle count: {summary.triangles}")
    if summary.rigid_meshes != 0 or summary.skinned_meshes != 6:
        raise ValueError(
            "Desert Eagle viewmodel must contain two arm meshes and four "
            f"skinned weapon meshes: rigid={summary.rigid_meshes}, "
            f"skinned={summary.skinned_meshes}")
    if summary.materials != 5 or set(summary.shaders) != {"ksSkinnedMesh"}:
        raise ValueError(
            f"Unexpected Desert Eagle viewmodel materials: {summary.material_names}, "
            f"shaders={summary.shaders}")
    if summary.bones != 51:
        raise ValueError(f"Desert Eagle viewmodel rig changed: bones={summary.bones}")
    required_nodes = {
        "ASRC_DESERT_EAGLE_FIRING_ARM__MESH",
        "ASRC_DESERT_EAGLE_SUPPORT_ARM__MESH",
        "ASRC_VIEWMODEL_WEAPON_BONE",
        "ASRC_VIEWMODEL_MAGAZINE_BONE",
        "ASRC_DESERT_EAGLE_VIEWMODEL_MAIN_BODY",
        "ASRC_DESERT_EAGLE_VIEWMODEL_SLIDE",
        "ASRC_DESERT_EAGLE_VIEWMODEL_MAGAZINE",
        "ASRC_DESERT_EAGLE_VIEWMODEL_BULLET",
    }
    missing = required_nodes - set(summary.node_names)
    if missing:
        raise ValueError(f"Desert Eagle viewmodel nodes are missing: {sorted(missing)}")
    if len(summary.texture_dimensions) != 11:
        raise ValueError(
            f"Unexpected Desert Eagle viewmodel textures: {summary.texture_dimensions}")
    if any(width > 2048 or height > 2048
           for _, width, height in summary.texture_dimensions):
        raise ValueError("Desert Eagle viewmodel texture budget exceeded")
    print(
        f"Validated {path.name}: triangles={summary.triangles}, "
        f"skinnedMeshes={summary.skinned_meshes}, bones={summary.bones}, "
        f"materials={summary.materials}, textures={len(summary.texture_dimensions)}")


def validate_world(path: Path) -> None:
    summary = inspect_kn5(path)
    if summary.triangles != 13_279:
        raise ValueError(f"Unexpected Desert Eagle world triangles: {summary.triangles}")
    if summary.rigid_meshes != 4 or summary.skinned_meshes != 0:
        raise ValueError(
            f"Unexpected Desert Eagle world layout: rigid={summary.rigid_meshes}, "
            f"skinned={summary.skinned_meshes}")
    if summary.materials != 4 or set(summary.shaders) != {"ksPerPixel"}:
        raise ValueError(
            f"Unexpected Desert Eagle world materials/shaders: "
            f"{summary.material_names}, {summary.shaders}")
    if len(summary.texture_dimensions) != 8:
        raise ValueError(
            f"Unexpected Desert Eagle world textures: {summary.texture_dimensions}")
    if any(width > 512 or height > 512
           for _, width, height in summary.texture_dimensions):
        raise ValueError("Desert Eagle world texture budget exceeded")
    print(
        f"Validated {path.name}: triangles={summary.triangles}, "
        f"rigidMeshes={summary.rigid_meshes}, materials={summary.materials}, "
        f"textures={len(summary.texture_dimensions)}")


def validate_animations(asset_dir: Path, node_names: set[str]) -> None:
    expected_tracks = None
    modern_dir = asset_dir / "Modern"
    animations = {}
    for clip in CLIPS:
        path = asset_dir / f"asrc_desert_eagle_{clip}.ksanim"
        tracks = inspect_ksanim(path)
        animations[clip] = tracks
        names = tuple(tracks)
        if expected_tracks is None:
            expected_tracks = names
        elif names != expected_tracks:
            raise ValueError(f"Desert Eagle animation track mismatch: {path.name}")
        missing = set(names) - node_names
        if missing:
            raise ValueError(
                f"Desert Eagle animation targets missing KN5 nodes: {sorted(missing)}")
        for bone_name in ("ASRC_VIEWMODEL_WEAPON_BONE",
                          "ASRC_VIEWMODEL_MAGAZINE_BONE"):
            if bone_name not in tracks:
                raise ValueError(
                    f"Desert Eagle animation lacks {bone_name}: {path.name}")
        frame_count = len(next(iter(tracks.values())))
        if frame_count != EXPECTED_FRAMES[clip]:
            raise ValueError(
                f"Unexpected Desert Eagle {clip} frame count: {frame_count}")
        carbine = modern_dir / f"asrc_modern_carbine_{clip}.ksanim"
        if carbine.is_file() and path.read_bytes() == carbine.read_bytes():
            raise ValueError(
                f"Desert Eagle {clip} still copies the carbine animation byte-for-byte")

    carbine_idle = inspect_ksanim(modern_dir / "asrc_modern_carbine_idle.ksanim")
    right_arm = animations["idle"]["R_arm"][0]
    carbine_right_arm = carbine_idle["R_arm"][0]
    alignment_delta = math.sqrt(sum(
        (right_arm[index] - carbine_right_arm[index]) ** 2
        for index in range(4, 7)
    ))
    if alignment_delta < 1.0:
        raise ValueError("Desert Eagle firing arm was not aligned to the pistol grip")
    for finger_name in ("R_point1", "R_middle1", "R_ring1", "R_pink1"):
        rotation_magnitude = math.sqrt(sum(
            value * value for value in animations["idle"][finger_name][0][:3]))
        if rotation_magnitude < 0.08:
            raise ValueError(
                f"Desert Eagle right-hand firing grip is missing: {finger_name}")

    reload_tracks = animations["reload"]
    magazine_frames = reload_tracks["ASRC_VIEWMODEL_MAGAZINE_BONE"]
    magazine_span = max(
        max(frame[axis] for frame in magazine_frames)
        - min(frame[axis] for frame in magazine_frames)
        for axis in range(4, 7)
    )
    if magazine_span < 10.0:
        raise ValueError(
            f"Desert Eagle reload does not extract the magazine: span={magazine_span}")
    support_idle = animations["idle"]["L_arm"]
    support_reload = animations["reload"]["L_arm"]
    support_idle_span = max(
        max(frame[axis] for frame in support_idle)
        - min(frame[axis] for frame in support_idle)
        for axis in range(4, 7)
    )
    support_reload_span = max(
        max(frame[axis] for frame in support_reload)
        - min(frame[axis] for frame in support_reload)
        for axis in range(4, 7)
    )
    if support_idle_span > 0.01:
        raise ValueError(
            f"Desert Eagle support arm moves outside reload: span={support_idle_span}")
    hidden_pose = support_idle[0][4:7]
    for clip in ("fire", "equip", "sprint"):
        non_reload_delta = max(
            abs(frame[axis] - hidden_pose[axis - 4])
            for frame in animations[clip]["L_arm"]
            for axis in range(4, 7)
        )
        if non_reload_delta > 0.01:
            raise ValueError(
                f"Desert Eagle support arm enters during {clip}: "
                f"delta={non_reload_delta}")
    carbine_support = carbine_idle["L_arm"][0]
    hidden_delta = math.sqrt(sum(
        (hidden_pose[index - 4] - carbine_support[index]) ** 2
        for index in range(4, 7)
    ))
    if hidden_delta < 100.0:
        raise ValueError(
            f"Desert Eagle support arm is not hidden off-screen: delta={hidden_delta}")
    if support_reload_span < 80.0:
        raise ValueError(
            f"Desert Eagle support arm does not enter for reload: span={support_reload_span}")
    if any(abs(support_reload[0][axis] - support_reload[-1][axis]) > 0.01
           for axis in range(4, 7)):
        raise ValueError("Desert Eagle support arm does not finish off-screen")
    print(
        "Validated two-hand Desert Eagle animations: "
        f"{', '.join(CLIPS)}; armAlignment={alignment_delta:.2f}; "
        f"magazineSpan={magazine_span:.2f}; supportArmSpan={support_reload_span:.2f}; "
        f"hiddenDelta={hidden_delta:.2f}")


def main() -> None:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--asset-dir", required=True)
    parser.add_argument("--success-marker", required=True)
    options = parser.parse_args(args)
    asset_dir = Path(options.asset_dir).resolve()
    viewmodel_path = asset_dir / "asrc_desert_eagle_viewmodel.kn5"
    validate_viewmodel(viewmodel_path)
    validate_world(asset_dir / "asrc_desert_eagle_world.kn5")
    validate_animations(asset_dir, set(inspect_kn5(viewmodel_path).node_names))
    Path(options.success_marker).write_text("ok\n", encoding="utf-8")


if __name__ == "__main__":
    main()
