"""Validate generated grenade KN5 and KSANIM assets."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from validate_fps_modern_assets import inspect_kn5, inspect_ksanim


ASSETS = {
    "frag_grenade": {"world_meshes": 5, "viewmodel_meshes": 7, "materials": 6},
    "sticky_grenade": {"world_meshes": 1, "viewmodel_meshes": 3, "materials": 2},
}


def validate(asset_dir: Path, slug: str, expected: dict) -> None:
    viewmodel = inspect_kn5(asset_dir / f"asrc_{slug}_viewmodel.kn5")
    world = inspect_kn5(asset_dir / f"asrc_{slug}_world.kn5")
    if viewmodel.rigid_meshes != 0 or viewmodel.skinned_meshes != expected["viewmodel_meshes"]:
        raise ValueError(f"Unexpected {slug} viewmodel mesh layout: {viewmodel}")
    if world.rigid_meshes != expected["world_meshes"] or world.skinned_meshes != 0:
        raise ValueError(f"Unexpected {slug} world mesh layout: {world}")
    if viewmodel.materials != expected["materials"] or world.materials != expected["materials"] - 1:
        raise ValueError(
            f"Unexpected {slug} materials: viewmodel={viewmodel.materials}, world={world.materials}")
    if set(viewmodel.shaders) != {"ksSkinnedMesh"} or set(world.shaders) != {"ksPerPixel"}:
        raise ValueError(f"Unexpected {slug} shaders")
    if viewmodel.triangles > 35_000 or world.triangles > 12_000:
        raise ValueError(
            f"{slug} triangle budget exceeded: viewmodel={viewmodel.triangles}, world={world.triangles}")
    if any(width > 2048 or height > 2048 for _, width, height in viewmodel.texture_dimensions):
        raise ValueError(f"{slug} viewmodel texture budget exceeded")
    if any(width > 512 or height > 512 for _, width, height in world.texture_dimensions):
        raise ValueError(f"{slug} world texture budget exceeded")
    tracks = inspect_ksanim(asset_dir / f"asrc_{slug}_throw.ksanim")
    if len(next(iter(tracks.values()))) != 48:
        raise ValueError(f"Unexpected {slug} throw frame count")
    required = {"R_arm", "L_arm", "R_wrist", "ASRC_GRENADE_BONE"}
    if not required.issubset(tracks):
        raise ValueError(f"{slug} throw tracks are missing: {sorted(required - set(tracks))}")
    if not set(tracks).issubset(viewmodel.node_names):
        raise ValueError(f"{slug} animation targets nodes absent from its KN5")
    grenade_positions = [frame[4:7] for frame in tracks["ASRC_GRENADE_BONE"]]
    max_span = max(max(position[axis] for position in grenade_positions)
                   - min(position[axis] for position in grenade_positions)
                   for axis in range(3))
    if max_span < 100:
        raise ValueError(f"{slug} held model is not hidden after release: span={max_span}")
    print(
        f"Validated {slug}: viewmodelTriangles={viewmodel.triangles}, "
        f"worldTriangles={world.triangles}, frames=48, releaseSpan={max_span:.2f}")


def main() -> None:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--asset-dir", required=True)
    parser.add_argument("--success-marker", required=True)
    options = parser.parse_args(args)
    asset_dir = Path(options.asset_dir).resolve()
    for slug, expected in ASSETS.items():
        validate(asset_dir, slug, expected)
    Path(options.success_marker).write_text("ok\n", encoding="utf-8")


if __name__ == "__main__":
    main()
