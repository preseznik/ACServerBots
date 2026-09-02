"""Validate generated Desert Eagle KN5 models without Blender scene state."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from validate_fps_modern_assets import inspect_kn5


def validate(path: Path, expected_meshes: int, expected_materials: int,
             expected_textures: int) -> None:
    summary = inspect_kn5(path)
    if not 13_279 <= summary.triangles <= 14_000:
        raise ValueError(f"Unexpected Desert Eagle triangle count in {path.name}: {summary.triangles}")
    if summary.rigid_meshes != expected_meshes or summary.skinned_meshes != 0:
        raise ValueError(
            f"Unexpected Desert Eagle mesh layout in {path.name}: "
            f"rigid={summary.rigid_meshes}, skinned={summary.skinned_meshes}")
    if summary.materials != expected_materials:
        raise ValueError(
            f"Unexpected Desert Eagle material count in {path.name}: {summary.materials}")
    if set(summary.shaders) != {"ksPerPixel"}:
        raise ValueError(f"Unexpected Desert Eagle shader in {path.name}: {summary.shaders}")
    if len(summary.texture_dimensions) != expected_textures:
        raise ValueError(
            f"Unexpected Desert Eagle texture count in {path.name}: "
            f"{len(summary.texture_dimensions)}")
    if any(width > 1024 or height > 1024
           for _, width, height in summary.texture_dimensions):
        raise ValueError(f"Desert Eagle texture budget exceeded in {path.name}")
    print(
        f"Validated {path.name}: triangles={summary.triangles}, "
        f"meshes={summary.rigid_meshes}, materials={summary.materials}, "
        f"textures={len(summary.texture_dimensions)}")


def main() -> None:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--asset-dir", required=True)
    parser.add_argument("--success-marker", required=True)
    options = parser.parse_args(args)
    asset_dir = Path(options.asset_dir).resolve()
    validate(asset_dir / "asrc_desert_eagle_viewmodel.kn5", 6, 6, 10)
    validate(asset_dir / "asrc_desert_eagle_world.kn5", 4, 4, 8)
    Path(options.success_marker).write_text("ok\n", encoding="utf-8")


if __name__ == "__main__":
    main()
