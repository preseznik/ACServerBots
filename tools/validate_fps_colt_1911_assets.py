"""Validate generated Colt 1911 KN5 and KSANIM assets."""

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
    if summary.triangles != 25_003:
        raise ValueError(f"Unexpected Colt 1911 viewmodel triangles: {summary.triangles}")
    if summary.rigid_meshes != 0 or summary.skinned_meshes != 5:
        raise ValueError(
            "Colt 1911 viewmodel must contain two arms and three weapon meshes: "
            f"rigid={summary.rigid_meshes}, skinned={summary.skinned_meshes}")
    if summary.materials != 3 or set(summary.shaders) != {"ksSkinnedMesh"}:
        raise ValueError(
            f"Unexpected Colt 1911 materials: {summary.material_names}, "
            f"shaders={summary.shaders}")
    if summary.bones != 51:
        raise ValueError(f"Colt 1911 viewmodel rig changed: bones={summary.bones}")
    required_nodes = {
        "ASRC_COLT_1911_FIRING_ARM__MESH",
        "ASRC_COLT_1911_SUPPORT_ARM__MESH",
        "ASRC_VIEWMODEL_WEAPON_BONE",
        "ASRC_VIEWMODEL_MAGAZINE_BONE",
        "ASRC_COLT_1911_VIEWMODEL_BODY",
        "ASRC_COLT_1911_VIEWMODEL_SLIDE",
        "ASRC_COLT_1911_VIEWMODEL_MAGAZINE",
    }
    missing = required_nodes - set(summary.node_names)
    if missing:
        raise ValueError(f"Colt 1911 viewmodel nodes missing: {sorted(missing)}")
    if len(summary.texture_dimensions) != 7:
        raise ValueError(
            f"Unexpected Colt 1911 viewmodel textures: {summary.texture_dimensions}")
    if any(width > 2048 or height > 2048
           for _, width, height in summary.texture_dimensions):
        raise ValueError("Colt 1911 viewmodel texture budget exceeded")
    print(
        f"Validated {path.name}: triangles={summary.triangles}, "
        f"skinnedMeshes={summary.skinned_meshes}, bones={summary.bones}, "
        f"materials={summary.materials}, textures={len(summary.texture_dimensions)}")


def validate_world(path: Path) -> None:
    summary = inspect_kn5(path)
    if summary.triangles != 11_303:
        raise ValueError(f"Unexpected Colt 1911 world triangles: {summary.triangles}")
    if summary.rigid_meshes != 3 or summary.skinned_meshes != 0:
        raise ValueError(
            f"Unexpected Colt 1911 world layout: rigid={summary.rigid_meshes}, "
            f"skinned={summary.skinned_meshes}")
    if summary.materials != 2 or set(summary.shaders) != {"ksPerPixel"}:
        raise ValueError(
            f"Unexpected Colt 1911 world materials: {summary.material_names}, "
            f"shaders={summary.shaders}")
    if len(summary.texture_dimensions) != 4:
        raise ValueError(f"Unexpected Colt 1911 world textures: {summary.texture_dimensions}")
    if any(width > 512 or height > 512
           for _, width, height in summary.texture_dimensions):
        raise ValueError("Colt 1911 world texture budget exceeded")
    print(
        f"Validated {path.name}: triangles={summary.triangles}, "
        f"rigidMeshes={summary.rigid_meshes}, materials={summary.materials}, "
        f"textures={len(summary.texture_dimensions)}")


def validate_animations(asset_dir: Path, node_names: set[str]) -> None:
    expected_tracks = None
    animations = {}
    for clip in CLIPS:
        path = asset_dir / f"asrc_colt_1911_{clip}.ksanim"
        tracks = inspect_ksanim(path)
        animations[clip] = tracks
        names = tuple(tracks)
        if expected_tracks is None:
            expected_tracks = names
        elif names != expected_tracks:
            raise ValueError(f"Colt 1911 animation track mismatch: {path.name}")
        missing = set(names) - node_names
        if missing:
            raise ValueError(
                f"Colt 1911 animation targets missing KN5 nodes: {sorted(missing)}")
        for bone_name in ("ASRC_VIEWMODEL_WEAPON_BONE",
                          "ASRC_VIEWMODEL_MAGAZINE_BONE"):
            if bone_name not in tracks:
                raise ValueError(f"Colt 1911 animation lacks {bone_name}: {path.name}")
        frame_count = len(next(iter(tracks.values())))
        if frame_count != EXPECTED_FRAMES[clip]:
            raise ValueError(
                f"Unexpected Colt 1911 {clip} frame count: {frame_count}")

    deagle_idle = inspect_ksanim(asset_dir / "asrc_desert_eagle_idle.ksanim")
    right_arm = animations["idle"]["R_arm"][0]
    deagle_right_arm = deagle_idle["R_arm"][0]
    pose_delta = math.sqrt(sum(
        (right_arm[index] - deagle_right_arm[index]) ** 2
        for index in range(4, 7)
    ))
    if pose_delta > 0.001:
        raise ValueError(f"Colt 1911 diverged from proven pistol hand pose: {pose_delta}")
    for finger_name in ("R_point1", "R_middle1", "R_ring1", "R_pink1"):
        rotation_magnitude = math.sqrt(sum(
            value * value for value in animations["idle"][finger_name][0][:3]))
        if rotation_magnitude < 0.08:
            raise ValueError(f"Colt 1911 right-hand grip missing: {finger_name}")

    magazine_frames = animations["reload"]["ASRC_VIEWMODEL_MAGAZINE_BONE"]
    magazine_span = max(
        max(frame[axis] for frame in magazine_frames)
        - min(frame[axis] for frame in magazine_frames)
        for axis in range(4, 7)
    )
    if magazine_span < 10.0:
        raise ValueError(
            f"Colt 1911 reload does not extract magazine: span={magazine_span}")
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
            f"Colt 1911 support arm moves outside reload: span={support_idle_span}")
    hidden_pose = support_idle[0][4:7]
    for clip in ("fire", "equip", "sprint"):
        non_reload_delta = max(
            abs(frame[axis] - hidden_pose[axis - 4])
            for frame in animations[clip]["L_arm"]
            for axis in range(4, 7)
        )
        if non_reload_delta > 0.01:
            raise ValueError(
                f"Colt 1911 support arm enters during {clip}: delta={non_reload_delta}")
    deagle_hidden_pose = deagle_idle["L_arm"][0][4:7]
    hidden_pose_delta = math.sqrt(sum(
        (hidden_pose[index] - deagle_hidden_pose[index]) ** 2
        for index in range(3)
    ))
    if hidden_pose_delta > 0.01:
        raise ValueError(
            "Colt 1911 support-arm hidden pose diverged from Desert Eagle: "
            f"delta={hidden_pose_delta}")
    if support_reload_span < 80.0:
        raise ValueError(
            f"Colt 1911 support arm does not enter for reload: span={support_reload_span}")
    if any(abs(support_reload[0][axis] - support_reload[-1][axis]) > 0.01
           for axis in range(4, 7)):
        raise ValueError("Colt 1911 support arm does not finish off-screen")
    print(
        "Validated two-hand Colt 1911 animations: "
        f"{', '.join(CLIPS)}; magazineSpan={magazine_span:.2f}; "
        f"supportArmSpan={support_reload_span:.2f}; hiddenPoseDelta={hidden_pose_delta:.2f}")


def main() -> None:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--asset-dir", required=True)
    parser.add_argument("--success-marker", required=True)
    options = parser.parse_args(args)
    asset_dir = Path(options.asset_dir).resolve()
    viewmodel_path = asset_dir / "asrc_colt_1911_viewmodel.kn5"
    validate_viewmodel(viewmodel_path)
    validate_world(asset_dir / "asrc_colt_1911_world.kn5")
    validate_animations(asset_dir, set(inspect_kn5(viewmodel_path).node_names))
    Path(options.success_marker).write_text("ok\n", encoding="utf-8")


if __name__ == "__main__":
    main()
