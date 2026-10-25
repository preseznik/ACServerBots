"""Package only a checked Dust II candidate; leave existing FPS packs untouched."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import zipfile
import zlib

import numpy as np


def sha(path):
    with path.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def main():
    p = argparse.ArgumentParser(); p.add_argument("build", type=Path); args = p.parse_args()
    root = args.build.resolve(); track = root / "content/tracks/css_dust2"; validation = root / "validation"
    version = json.loads((track / "conversion-report.json").read_text())["version"]
    gate = json.loads((validation / "validation.json").read_text())
    materials = json.loads((validation / "materials.json").read_text())
    if not gate["passed"] or not materials["passed"]:
        raise ValueError("Dust II offline and material checks must pass before packaging")
    for model in gate["models"]:
        if sha(track / model["file"]) != model["sha256"]:
            raise ValueError("Map changed after route validation")
    for model in (materials["visual"], materials["collision"]):
        if sha(track / model["file"]) != model["sha256"]:
            raise ValueError("Map changed after material validation")
    prepared = track / "race-control"; prepared.mkdir(exist_ok=True)
    for source, name in (("fps_arena.json", "arena.json"), ("fps_geometry.bin", "geometry.bin"),
                         ("fps_navigation.bin", "navigation.bin")):
        shutil.copy2(validation / source, prepared / name)
    arena = json.loads((prepared / "arena.json").read_text())
    write_json(prepared / "prepared.json", {"PreparationVersion": 5, "BoundsPaddingMeters": arena["BoundsPaddingMeters"],
        "CollisionIncludeMeshes": [], "CollisionExcludeMeshes": [],
        "Inputs": {name: sha(track / name) for name in ("models.ini", "css_dust2.kn5", "css_dust2_collision.kn5")},
        "GeometrySha256": sha(prepared / "geometry.bin"), "NavigationSha256": sha(prepared / "navigation.bin")})
    # AC's map transform uses world X/Z; rasterize the measured navigation only.
    navigation = json.loads((validation / "navigation-points.json").read_text())
    pixels = np.zeros((1024, 1024, 4), dtype=np.uint8)
    for node in navigation["nodes"]:
        if node["component"] != navigation["primary"]:
            continue
        x, _, z = node["p"]; px = round((x + 85) / .15); py = round((z + 103) / .15)
        if 2 <= px < 1022 and 2 <= py < 1022:
            pixels[py-1:py+2, px-1:px+2] = (235, 225, 202, 255)
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    raw = b"".join(b"\0" + row.tobytes() for row in pixels)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">2I5B", 1024, 1024, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")
    (track / "map.png").write_bytes(png); (track / "ui/outline.png").write_bytes(png)
    (track / "data/map.ini").write_text("[PARAMETERS]\nWIDTH=1024\nHEIGHT=1024\nMARGIN=0\nSCALE_FACTOR=0.15\nX_OFFSET=85\nZ_OFFSET=103\nDRAWING_SIZE=10\n")
    (track / "README.txt").write_text(
        f"DUST II (COUNTER-STRIKE: SOURCE) - LOCAL FPS PROTOTYPE {version}\n\n"
        "Source: the owner's installed Counter-Strike: Source cstrike/maps/de_dust2.bsp\n"
        "Map and game assets: Valve. Conversion inputs and hashes: conversion-report.json.\n"
        "Prepared following the Nuketown visual/collision/start-marker contract.\n\n"
        "16 distributed FPS starts; 16 remote car carriers; independent collision;\n"
        "movement-only Source clips; stair collision adapters; 0.3 m navigation.\n"
        "Use the included prepared FPS pack with Race Control preparation version 5.\n"
        "A generic re-prepare uses a coarser 0.6 m grid; rebuild with Dust2Gate to retain\n"
        "this map's finer stair navigation. Copy css_dust2 to AC/content/tracks for the client.\n\n"
        "Offline validation is recorded beside the build. AC/CSP rendering, performance,\n"
        "multiplayer presentation and human movement/shot alignment still need playtesting.\n"
        "Props are static; Source baked lighting, particles and soundscapes are omitted.\n"
        "This is a local prototype; no redistribution rights are asserted.\n", encoding="utf-8")
    pack = root / "package"; destination = pack / "content/tracks/css_dust2"
    shutil.copytree(track, destination, dirs_exist_ok=True)
    write_json(pack / "asrc-fps-maps.json", {"schemaVersion": 1, "packVersion": 1, "preparationVersion": 5,
        "maps": [{"trackId": "css_dust2", "name": "Dust II (CS:S FPS)", "version": version,
                  "spawnPoints": 16, "preparationRefreshed": True, "runtimeValidated": False}]})
    manifest = [{"path": str(file.relative_to(pack)).replace("\\", "/"), "bytes": file.stat().st_size, "sha256": sha(file)}
                for file in sorted(pack.rglob("*")) if file.is_file()]
    write_json(root / "package-files.json", manifest)
    archive = root / f"css_dust2_{version}-local-prototype.zip"
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=5) as zip_:
        for entry in manifest:
            zip_.write(pack / entry["path"], entry["path"])
    (root / "SHA256SUMS.txt").write_text(sha(archive) + "  " + archive.name + "\n")
    print(json.dumps({"archive": str(archive), "files": len(manifest), "sha256": sha(archive)}))


if __name__ == "__main__": main()
