"""Acquire the bounded Last Shift material set from the official Poly Haven API.

Powered by Poly Haven: https://polyhaven.com
The downloaded assets are CC0. Source bytes are retained without image edits.
This is an optional acquisition tool; normal map builds work entirely offline.
"""

import hashlib
import json
from pathlib import Path
import urllib.request


ROOT = Path(__file__).resolve().parents[1] / "maps" / "last_shift" / "textures"
USER_AGENT = "LastShiftTextureAcquisition/1.0"
ASSETS = {
    "worn_asphalt": "Depot yard and service lanes",
    "concrete_floor_worn_02": "Worn concrete floors, plinths and barriers",
    "brick_wall_02": "Weathered red brick workshop and dispatch walls",
    "corrugated_iron_03": "Galvanized corrugated roofs and utility cladding",
    "green_metal_rust": "Worn painted fleet panels, doors and equipment",
    "rusty_metal_02": "Weathered steel equipment and trims",
}
MAPS = {
    "diffuse": ("Diffuse", "2k", "jpg", "sRGB"),
    "normalGL": ("nor_gl", "1k", "png", "Non-Color"),
    "normalDX": ("nor_dx", "1k", "png", "Non-Color"),
    "roughness": ("Rough", "1k", "jpg", "Non-Color"),
}


def read_url(url):
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def main():
    ROOT.mkdir(parents=True, exist_ok=True)
    manifest = {
        "schemaVersion": 1,
        "provider": "Poly Haven",
        "license": "CC0-1.0",
        "licenseUrl": "https://polyhaven.com/license",
        "licenseDeedUrl": "https://creativecommons.org/publicdomain/zero/1.0/",
        "apiTermsUrl": "https://github.com/Poly-Haven/Public-API/blob/master/ToS.md",
        "credit": "Powered by Poly Haven",
        "sourceBytesUnmodified": True,
        "assets": [],
    }
    total_size = 0
    for asset_id, usage in ASSETS.items():
        info_url = f"https://api.polyhaven.com/info/{asset_id}"
        files_url = f"https://api.polyhaven.com/files/{asset_id}"
        info = json.loads(read_url(info_url))
        source_files = json.loads(read_url(files_url))
        asset = {
            "id": asset_id,
            "name": info["name"],
            "sourceUrl": f"https://polyhaven.com/a/{asset_id}",
            "infoUrl": info_url,
            "filesUrl": files_url,
            "license": "CC0-1.0",
            "licenseUrl": "https://polyhaven.com/license",
            "authors": info["authors"],
            "tileSizeMeters": [round(value / 1000.0, 5) for value in info["dimensions"][:2]],
            "usage": usage,
            "files": {},
        }
        for role, (source_role, resolution, extension, color_space) in MAPS.items():
            source = source_files[source_role][resolution][extension]
            name = source["url"].rsplit("/", 1)[-1]
            target = ROOT / asset_id / name
            target.parent.mkdir(exist_ok=True)
            if target.exists():
                data = target.read_bytes()
                if hashlib.md5(data).hexdigest() != source["md5"]:
                    raise RuntimeError(f"Existing file does not match provider hash: {target}")
            else:
                print(f"Downloading {asset_id}: {role}", flush=True)
                data = read_url(source["url"])
                if len(data) != source["size"] or hashlib.md5(data).hexdigest() != source["md5"]:
                    raise RuntimeError(f"Provider size/hash mismatch: {source['url']}")
                target.write_bytes(data)
            total_size += len(data)
            asset["files"][role] = {
                "path": target.relative_to(ROOT).as_posix(),
                "url": source["url"],
                "sizeBytes": len(data),
                "md5": source["md5"],
                "sha256": hashlib.sha256(data).hexdigest(),
                "resolution": resolution,
                "colorSpace": color_space,
            }
        manifest["assets"].append(asset)
    manifest["totalSizeBytes"] = total_size
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Verified {len(ASSETS)} material sets, {total_size:,} source bytes.", flush=True)


if __name__ == "__main__":
    main()
