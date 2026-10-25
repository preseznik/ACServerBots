"""Check the exported Dust II bytes, independently of Blender's source scene."""
import argparse
import hashlib
import json
import math
from pathlib import Path

import numpy as np

from validate_fps_modern_assets import Reader, _png_dimensions


def inspect(path):
    raw = path.read_bytes(); reader = Reader(raw, path.name)
    if reader.read(6) != b"sc6969" or reader.uint() != 5:
        raise ValueError("Expected a version 5 KN5")
    textures = {}
    for _ in range(reader.uint()):
        if reader.uint() != 1:
            raise ValueError("Inactive embedded texture")
        name = reader.string(); data = reader.read(reader.uint())
        width, height = _png_dimensions(data, name)
        if name in textures or name.startswith("__") or max(width, height) > 4096:
            raise ValueError(f"Invalid or duplicate texture: {name}")
        textures[name] = {"dimensions": [width, height], "sha256": hashlib.sha256(data).hexdigest()}
    materials = []
    for _ in range(reader.uint()):
        mat = dict(name=reader.string(), shader=reader.string(), blend=reader.byte(),
                   alphaTested=reader.boolean(), depth=reader.uint(), textures={})
        for _ in range(reader.uint()):
            name = reader.string(); values = reader.floats(10)
            if not all(math.isfinite(v) for v in values):
                raise ValueError(f"Non-finite material {name}")
        for _ in range(reader.uint()):
            name = reader.string(); slot = reader.uint(); image = reader.string()
            if name in mat["textures"] or image not in textures:
                raise ValueError(f"Missing or duplicate texture binding {mat['name']}/{name}/{image}")
            mat["textures"][name] = {"slot": slot, "image": image}
        materials.append(mat)
    nodes, meshes = set(), []
    def node():
        kind = reader.uint(); name = reader.string(); children = reader.uint(); active = reader.boolean()
        if name in nodes:
            raise ValueError(f"Duplicate node {name}")
        nodes.add(name)
        if kind == 1:
            transform = reader.floats(16)
            if not all(math.isfinite(v) for v in transform):
                raise ValueError(f"Invalid transform {name}")
            for _ in range(children): node()
            return
        if kind != 2 or children:
            raise ValueError(f"Expected a rigid leaf mesh: {name}")
        shadow, visible, transparent = reader.boolean(), reader.boolean(), reader.boolean()
        count = reader.uint()
        if not 3 <= count <= 65536:
            raise ValueError(f"Invalid vertex count: {name}/{count}")
        v = np.frombuffer(reader.read(count * 44), dtype="<f4").reshape(-1, 11)
        if not np.isfinite(v).all():
            raise ValueError(f"Non-finite vertices {name}")
        n, t = v[:, 3:6], v[:, 8:11]
        frame_error = max(float(np.max(abs(np.linalg.norm(n, axis=1) - 1))),
                          float(np.max(abs(np.linalg.norm(t, axis=1) - 1))),
                          float(np.max(abs(np.sum(n * t, axis=1)))))
        index_count = reader.uint()
        indices = np.frombuffer(reader.read(index_count * 2), dtype="<u2")
        if index_count % 3 or max(indices) >= count:
            raise ValueError(f"Invalid indices {name}")
        material = reader.uint(); reader.uint(); lod = reader.floats(2); bounds = reader.floats(4)
        renderable = reader.boolean()
        if material >= len(materials) or not all(math.isfinite(v) for v in lod + bounds):
            raise ValueError(f"Invalid mesh metadata {name}")
        triangles = v[indices.reshape(-1, 3)]
        surface_area = np.linalg.norm(np.cross(triangles[:, 1, :3] - triangles[:, 0, :3],
                                               triangles[:, 2, :3] - triangles[:, 0, :3]), axis=1) / 2
        uv_b = triangles[:, 1, 6:8] - triangles[:, 0, 6:8]
        uv_c = triangles[:, 2, 6:8] - triangles[:, 0, 6:8]
        uv_area = abs(uv_b[:, 0] * uv_c[:, 1] - uv_b[:, 1] * uv_c[:, 0]) / 2
        diffuse = materials[material]["textures"].get("txDiffuse")
        dimensions = textures[diffuse["image"]]["dimensions"] if diffuse else [1, 1]
        density = np.sqrt(uv_area * dimensions[0] * dimensions[1] / np.maximum(surface_area, 1e-12))
        meshes.append(dict(name=name, vertices=count, triangles=index_count // 3, material=materials[material]["name"],
            active=active, visible=visible, renderable=renderable, shadow=shadow,
            uvArea=float(uv_area.sum()), surfaceArea=float(surface_area.sum()),
            uvMinimum=v[:, 6:8].min(axis=0).tolist(), uvMaximum=v[:, 6:8].max(axis=0).tolist(),
            texelsPerMeterP10=float(np.percentile(density, 10)), texelsPerMeterMedian=float(np.median(density)),
            frameError=frame_error, minimum=v[:, :3].min(axis=0).tolist(), maximum=v[:, :3].max(axis=0).tolist()))
    node(); reader.finish()
    return dict(file=path.name, bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest(),
                materials=materials, textures=textures, meshes=meshes, nodes=len(nodes))


def main():
    p = argparse.ArgumentParser(); p.add_argument("track", type=Path); p.add_argument("output", type=Path)
    args = p.parse_args()
    visual = inspect(args.track / "css_dust2.kn5"); collision = inspect(args.track / "css_dust2_collision.kn5")
    conversion = json.loads((args.track / "conversion-report.json").read_text())
    errors = []
    def check(ok, message):
        if not ok: errors.append(message)
    check(sum(m["triangles"] for m in visual["meshes"]) == conversion["visual_triangles"], "Visual triangle count differs from source conversion")
    check(sum(m["triangles"] for m in collision["meshes"]) == conversion["road_triangles"] + conversion["wall_triangles"] + 2, "Collision triangle count differs")
    check(not collision["textures"], "Collision KN5 contains visual textures")
    for mat in visual["materials"]:
        check(mat["shader"] in {"ksPerPixel", "ksPerPixelNM", "ksPerPixelAlpha"}, "Unexpected shader " + mat["name"])
        check("txDiffuse" in mat["textures"], "Untextured material " + mat["name"])
        if mat["shader"] == "ksPerPixelNM": check("txNormal" in mat["textures"], "Missing normal map " + mat["name"])
    for mesh in visual["meshes"]:
        check(mesh["active"] and mesh["visible"] and mesh["renderable"], "Hidden visual " + mesh["name"])
        check(mesh["frameError"] < .002, "Invalid tangent frame " + mesh["name"])
        check(mesh["name"].startswith("VIS_"), "Visual could enter automatic collision " + mesh["name"])
    for material in conversion["materials"]:
        if not material["blended_colour_baked"]:
            continue
        baked = [mesh for mesh in visual["meshes"] if mesh["material"] == material["material"]]
        check(bool(baked) and .35 <= sum(mesh["uvArea"] for mesh in baked) <= 1.001,
              "Blended texture atlas coverage is invalid: " + material["material"])
        check(bool(baked) and all(mesh["uvMinimum"][0] >= -1e-5 and mesh["uvMaximum"][0] <= 1.00001
                                 and mesh["uvMinimum"][1] >= -1.00001 and mesh["uvMaximum"][1] <= 1e-5 for mesh in baked),
              "Blended texture UVs leave the packed atlas: " + material["material"])
        check(bool(baked) and min(mesh["texelsPerMeterP10"] for mesh in baked) >= 30,
              "Blended texture has insufficient ground-level detail: " + material["material"])
    for mesh in collision["meshes"]:
        check(not mesh["renderable"] and not mesh["shadow"], "Visible collision " + mesh["name"])
        check(mesh["name"].startswith(("1ROAD_", "1WALL_")), "Unclassified collision " + mesh["name"])
    report = dict(passed=not errors, runtimeValidated=False, errors=errors, visual=visual, collision=collision)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(dict(passed=not errors, errors=errors, materials=len(visual["materials"]),
                         textures=len(visual["textures"]), visualTriangles=sum(m["triangles"] for m in visual["meshes"]))))
    raise SystemExit(bool(errors))


if __name__ == "__main__": main()
