"""Inspect the shipped KN5 payloads, including material slots and collision flags.

This is an asset check; it does not establish AC/CSP visual acceptance.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path

from validate_fps_modern_assets import Reader, _png_dimensions


MAJOR_SURFACES = {"LS_asphalt", "LS_concrete", "LS_brick", "LS_fleet",
                  "LS_roof", "LS_steel", "LS_patch"}
UNIFORM_FINISHES = {"LS_paint", "LS_yellow", "LS_glass", "LS_dark", "LS_galvanized",
                    "LS_sign_blue", "LS_safety_red", "LS_lamp_lens"}


def read_kn5(path: Path) -> dict:
    data = path.read_bytes()
    reader = Reader(data, path.name)
    if reader.read(6) != b"sc6969" or reader.uint() != 5:
        raise ValueError(f"Unsupported KN5 header: {path.name}")
    textures = {}
    texture_count = reader.uint()
    if texture_count > 128:
        raise ValueError(f"Unexpected texture count: {texture_count}")
    for _ in range(texture_count):
        if reader.integer() != 1:
            raise ValueError("Inactive embedded texture")
        name = reader.string()
        blob = reader.read(reader.uint())
        width, height = _png_dimensions(blob, name)
        if name in textures:
            raise ValueError(f"Duplicate embedded texture: {name}")
        textures[name] = {"width": width, "height": height, "bytes": len(blob),
                          "sha256": hashlib.sha256(blob).hexdigest()}
    materials = []
    material_count = reader.uint()
    if not 0 < material_count <= 64:
        raise ValueError(f"Unexpected material count: {material_count}")
    for _ in range(material_count):
        material = {"name": reader.string(), "shader": reader.string(),
                    "blend": reader.byte(), "alphaTested": reader.boolean(),
                    "depth": reader.integer(), "properties": {}, "textures": {}}
        for _ in range(reader.uint()):
            name, values = reader.string(), reader.floats(10)
            if not all(math.isfinite(v) for v in values):
                raise ValueError(f"Nonfinite shader property: {name}")
            material["properties"][name] = values[0]
        for _ in range(reader.uint()):
            slot, index, name = reader.string(), reader.uint(), reader.string()
            if slot in material["textures"]:
                raise ValueError(f"Duplicate material slot: {slot}")
            material["textures"][slot] = {"index": index, "name": name}
        materials.append(material)
    meshes, node_names = [], set()

    def read_node():
        kind, name, children = reader.uint(), reader.string(), reader.uint()
        active = reader.boolean()
        if name in node_names:
            raise ValueError(f"Duplicate KN5 node: {name}")
        node_names.add(name)
        if kind == 1:
            if not all(math.isfinite(v) for v in reader.floats(16)):
                raise ValueError(f"Nonfinite node transform: {name}")
            for _ in range(children):
                read_node()
            return
        if kind != 2 or children:
            raise ValueError(f"Expected rigid leaf mesh: {name}")
        mesh = {"name": name, "active": active, "castShadows": reader.boolean(),
                "visible": reader.boolean(), "transparent": reader.boolean()}
        count = reader.uint()
        if not 3 <= count <= 65536:
            raise ValueError(f"Invalid vertex count: {name}")
        mesh["vertices"] = count
        maximum_frame_error = 0.0
        for _ in range(count):
            vertex = reader.floats(11)
            if not all(math.isfinite(v) for v in vertex):
                raise ValueError(f"Nonfinite vertex: {name}")
            normal, tangent = vertex[3:6], vertex[8:11]
            normal_length = math.sqrt(sum(v*v for v in normal))
            tangent_length = math.sqrt(sum(v*v for v in tangent))
            dot = sum(a*b for a, b in zip(normal, tangent))
            maximum_frame_error = max(maximum_frame_error, abs(normal_length-1),
                                      abs(tangent_length-1), abs(dot))
        mesh["maximumFrameError"] = maximum_frame_error
        index_count = reader.uint()
        if index_count % 3:
            raise ValueError(f"Invalid triangle indices: {name}")
        indices = reader.unpack(f"{index_count}H")
        if indices and max(indices) >= count:
            raise ValueError(f"Vertex index out of range: {name}")
        mesh["triangles"] = index_count // 3
        material_id = reader.uint()
        if material_id >= len(materials):
            raise ValueError(f"Material out of range: {name}")
        mesh["material"] = materials[material_id]["name"]
        mesh["shader"] = materials[material_id]["shader"]
        reader.uint()  # Layer.
        lod_in, lod_out = reader.floats(2)
        if not all(math.isfinite(v) for v in (lod_in, lod_out)) or lod_out < lod_in:
            raise ValueError(f"Invalid LOD range: {name}")
        if not all(math.isfinite(v) for v in reader.floats(4)):
            raise ValueError(f"Nonfinite bounds: {name}")
        mesh["renderable"] = reader.boolean()
        meshes.append(mesh)

    read_node()
    reader.finish()
    return {"file": path.name, "sha256": hashlib.sha256(data).hexdigest(),
            "bytes": len(data), "textures": textures, "materials": materials,
            "meshes": meshes}


def validate(track: Path) -> dict:
    visual = read_kn5(track / "last_shift.kn5")
    collision = read_kn5(track / "last_shift_collision.kn5")
    errors = []
    used = {mesh["material"] for mesh in visual["meshes"]}
    if not visual["meshes"] or not collision["meshes"]:
        errors.append("Visual and collision KN5s must both contain meshes")
    for name in MAJOR_SURFACES - used:
        errors.append(f"Missing expected major surface: {name}")
    for name, texture in visual["textures"].items():
        users = {material["name"] for material in visual["materials"]
                 if any(binding["name"] == name for binding in material["textures"].values())}
        uniform = bool(users) and users <= UNIFORM_FINISHES
        if name.startswith("__") or (min(texture["width"], texture["height"]) <= 128 and not uniform):
            errors.append(f"Blender-only or placeholder texture embedded: {name}")
        if max(texture["width"], texture["height"]) > 2048:
            errors.append(f"Texture exceeds the 2K map budget: {name}")
    for material in visual["materials"]:
        if material["name"] not in used:
            continue
        name, slots = material["name"], material["textures"]
        if material["shader"] not in {"ksPerPixel", "ksPerPixelNM"}:
            errors.append(f"Unexpected shader on {name}: {material['shader']}")
        required = {"txDiffuse"}
        if name in MAJOR_SURFACES or material["shader"] == "ksPerPixelNM":
            required.add("txNormal")
            if material["shader"] != "ksPerPixelNM":
                errors.append(f"Major surface lacks normal-map shader: {name}")
        if set(slots) != required:
            errors.append(f"Unexpected or missing texture slots on {name}: {sorted(slots)}")
        for slot, binding in slots.items():
            texture = visual["textures"].get(binding["name"])
            if texture is None:
                errors.append(f"Missing embedded {slot} texture on {name}")
            elif name in MAJOR_SURFACES and min(texture["width"], texture["height"]) < 1024:
                errors.append(f"Major surface texture below 1K: {name}/{slot}")
            if binding["index"] != {"txDiffuse": 0, "txNormal": 1}.get(slot):
                errors.append(f"Incorrect shader texture index: {name}/{slot}")
        properties = material["properties"]
        if properties.get("ksAmbient", 0) <= 0 or properties.get("ksDiffuse", 0) <= 0:
            errors.append(f"Missing positive lighting properties: {name}")
        if properties.get("nmObjectSpace", 0) != 0:
            errors.append(f"Expected tangent-space normal map: {name}")
    for mesh in visual["meshes"]:
        if not mesh["active"] or not mesh["visible"] or not mesh["renderable"]:
            errors.append(f"Visual mesh is hidden: {mesh['name']}")
        if mesh["shader"] == "ksPerPixelNM" and mesh["maximumFrameError"] > .002:
            errors.append(f"Invalid normal/tangent frame: {mesh['name']}")
    for mesh in collision["meshes"]:
        if mesh["renderable"] or mesh["castShadows"]:
            errors.append(f"Collision proxy can render or cast shadows: {mesh['name']}")
    return {"passed": not errors, "clientRuntimeTested": False, "errors": errors,
            "visual": visual, "collision": collision}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--track", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        report = validate(args.track)
    except (ValueError, OSError) as error:
        report = {"passed": False, "clientRuntimeTested": False, "errors": [str(error)]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"passed": report["passed"], "errors": report["errors"],
                      "output": str(args.output)}))
    raise SystemExit(0 if report["passed"] else 1)


if __name__ == "__main__":
    main()
