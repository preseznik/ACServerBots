"""Check exported Last Shift detail inventory and conservative asset budgets.

This verifies the shipped nodes, not only the authoring manifest. Collision
shape/movement acceptance belongs to LastShiftGate and the in-game playtest.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
from pathlib import Path

from validate_last_shift_materials import read_kn5
from validate_fps_modern_assets import Reader, inspect_kn5
from last_shift_surface_audit import rendered_triangles, check_horizontal_interfaces, check_sign_faces


SOURCE_MODULES = ("last_shift_detail_api.py", "last_shift_street_props.py",
                  "last_shift_workshop_props.py", "last_shift_buses.py",
                  "last_shift_decals.py", "last_shift_site_layout.py",
                  "last_shift_surfaces.py", "last_shift_rooftops.py")
# Generous regression ceilings for this compact depot; these are not FPS claims.
BUDGETS = {
    "visualTriangles": 100_000,
    "visualMeshes": 1_600,
    "collisionTriangles": 12_000,
    "collisionMeshes": 500,
    "visualMaterials": 32,
    "visualBytes": 128 * 1024 * 1024,
}


def collision_vertex_bounds(path: Path) -> dict:
    """The map exporter bakes proxy vertices under one identity root."""
    summary = inspect_kn5(path)
    bounds = {name: (minimum, maximum) for name, _, minimum, maximum in summary.mesh_bounds}
    if set(summary.node_names) != set(bounds) | {"LAST_SHIFT"}:
        raise ValueError("Collision proxies must be direct children of LAST_SHIFT")
    reader = Reader(path.read_bytes(), path.name)
    reader.read(10)  # Header and version already validated by inspect_kn5.
    for _ in range(reader.uint()):
        reader.uint()
        reader.string()
        reader.read(reader.uint())
    for _ in range(reader.uint()):
        reader.string()
        reader.string()
        reader.read(6)  # Blend, alpha-test, depth mode.
        for _ in range(reader.uint()):
            reader.string()
            reader.read(40)
        for _ in range(reader.uint()):
            reader.string()
            reader.uint()
            reader.string()
    if reader.uint() != 1 or reader.string() != "LAST_SHIFT":
        raise ValueError("Expected LAST_SHIFT collision root")
    reader.uint()
    reader.boolean()
    matrix = reader.floats(16)
    identity = [1 if i // 4 == i % 4 else 0 for i in range(16)]
    if any(abs(a-b) > 1e-6 for a, b in zip(matrix, identity)):
        raise ValueError("Proxy bounds check requires an identity collision root")
    return bounds


def selected_mesh_geometry(path: Path, wanted: set[str] | None) -> dict:
    """Read selected exported vertices, applying KN5 parent transforms."""
    reader = Reader(path.read_bytes(), path.name)
    if reader.read(6) != b"sc6969" or reader.uint() != 5:
        raise ValueError("Expected KN5 version 5")
    for _ in range(reader.uint()):
        reader.uint()
        reader.string()
        reader.read(reader.uint())
    materials = []
    for _ in range(reader.uint()):
        materials.append(reader.string())
        reader.string()
        reader.read(6)
        for _ in range(reader.uint()):
            reader.string()
            reader.read(40)
        for _ in range(reader.uint()):
            reader.string()
            reader.uint()
            reader.string()
    identity = [float(i // 4 == i % 4) for i in range(16)]
    result = {}

    def visit(parent):
        kind, name, children = reader.uint(), reader.string(), reader.uint()
        active = reader.boolean()
        if kind == 1:
            local = reader.floats(16)
            world = [sum(local[row*4+k]*parent[k*4+col] for k in range(4))
                     for row in range(4) for col in range(4)]
            for _ in range(children):
                visit(world)
            return
        if kind != 2 or children:
            raise ValueError(f"Expected a rigid leaf mesh: {name}")
        cast_shadows, visible, transparent = reader.read(3)
        count = reader.uint()
        points, uvs = [], []
        selected = wanted is None or name in wanted
        if selected:
            for _ in range(count):
                vertex = reader.floats(11)
                points.append([sum(vertex[k]*parent[k*4+axis] for k in range(3))
                               + parent[12+axis] for axis in range(3)])
                uvs.append(vertex[6:8])
        else:
            reader.read(count*44)
        indices = reader.uint()
        index_bytes = reader.read(indices*2)
        material = materials[reader.uint()]
        reader.read(28)  # Layer, two LOD distances and bounding sphere.
        renderable = reader.boolean()
        if selected:
            result[name] = {"points": points, "uvs": uvs, "vertices": count,
                            "triangles": indices//3, "material": material,
                            "indices": struct.unpack("<" + "H" * indices, index_bytes),
                            "active": active, "visible": bool(visible), "renderable": renderable}

    visit(identity)
    reader.finish()
    return result


def check_signs_and_wheels(layout: dict, visual: dict, track: Path, errors: list) -> dict:
    decals, wheels = layout.get("signDecals", []), layout.get("wheelGeometry", [])
    if not decals:
        errors.append("No atlas sign decals recorded")
    if not wheels:
        errors.append("No bus wheel geometry recorded")
    meshes = selected_mesh_geometry(track / "last_shift.kn5", None)
    materials = {material["name"]: material for material in visual["materials"]}
    buses = {bus["name"]: bus for bus in layout["buses"]}
    sign_results, wheel_results = [], []
    for decal in decals:
        name = decal["node"]
        mesh = meshes.get(name)
        if mesh is None:
            errors.append(f"Atlas sign node was not exported independently: {name}")
            continue
        # KN5 may split a corner across triangles for slightly different tangents.
        corners = {tuple(point) + tuple(uv) for point, uv in zip(mesh["points"], mesh["uvs"])}
        if (not name.startswith("LS_DECAL_") or mesh["vertices"] not in (4, 6)
                or len(corners) != 4 or mesh["triangles"] != 2):
            errors.append(f"Sign is not a four-corner/two-triangle decal: {name}")
        material = materials[mesh["material"]]
        texture_name = material["textures"].get("txDiffuse", {}).get("name")
        texture = visual["textures"].get(texture_name, {})
        if texture_name != decal["atlas"] or mesh["material"] != decal["material"]:
            errors.append(f"Sign uses a different atlas/material than authored: {name}")
        if min(texture.get("width", 0), texture.get("height", 0)) < 1024:
            errors.append(f"Sign atlas below 1K or missing from KN5: {name}")
        uv_width = max(uv[0] for uv in mesh["uvs"]) - min(uv[0] for uv in mesh["uvs"])
        uv_height = max(uv[1] for uv in mesh["uvs"]) - min(uv[1] for uv in mesh["uvs"])
        if uv_width <= 0 or uv_height <= 0:
            errors.append(f"Sign has collapsed atlas UVs: {name}")
        actual_uv_bounds = [min(uv[0] for uv in mesh["uvs"]), min(uv[1] for uv in mesh["uvs"]),
                            max(uv[0] for uv in mesh["uvs"]), max(uv[1] for uv in mesh["uvs"])]
        # The pinned exporter writes Blender (u, v) as KN5 (u, -v).
        bounds = decal.get("uvBounds", [])
        expected_uv_bounds = [bounds[0], -bounds[3], bounds[2], -bounds[1]] if len(bounds) == 4 else []
        if not expected_uv_bounds or any(abs(a-b) > 1e-5 for a, b in zip(actual_uv_bounds, expected_uv_bounds)):
            errors.append(f"Sign atlas UV rectangle differs from its authored tile: {name}")
        sign_results.append({"node": name, "material": mesh["material"], "atlas": texture_name,
                             "vertices": mesh["vertices"], "uniqueCorners": len(corners),
                             "triangles": mesh["triangles"]})
    for wheel in wheels:
        name = wheel["node"]
        mesh = meshes.get(name)
        if mesh is None:
            errors.append(f"Wheel node was not exported independently: {name}")
            continue
        axis = wheel["axis"]
        length = math.sqrt(sum(v*v for v in axis))
        if len(axis) != 3 or abs(length-1) > .001 or abs(axis[1]) > .001:
            errors.append(f"Wheel axle is not a horizontal unit vector: {name}")
            continue
        bus = buses.get(wheel.get("bus"))
        if bus is None:
            errors.append(f"Wheel refers to a missing bus: {name}")
            continue
        yaw = math.radians(bus["yaw"])
        expected_axis = (math.cos(yaw), 0, -math.sin(yaw))
        if abs(sum(a*b for a, b in zip(axis, expected_axis))) < .999:
            errors.append(f"Wheel axle is not transverse to its bus: {name}")
        center, radius, depth = wheel["center"], wheel["radius"], wheel["depth"]
        # For a horizontal axle, these form an orthonormal wheel frame.
        frame = (axis, (0, 1, 0), (-axis[2], 0, axis[0]))
        intervals = []
        for direction in frame:
            projected = [sum((p[i]-center[i])*direction[i] for i in range(3)) for p in mesh["points"]]
            intervals.append((min(projected), max(projected)))
        spans = [high-low for low, high in intervals]
        if not name.startswith("LS_WHEEL_"):
            errors.append(f"Wheel lacks preserved wheel node prefix: {name}")
        if abs(spans[0]-depth) > max(.005, depth*.08):
            errors.append(f"Wheel axle depth/orientation mismatch: {name}: {spans[0]:.4f}m expected {depth:.4f}m")
        if any(abs(span-2*radius) > .025 for span in spans[1:]):
            errors.append(f"Wheel radial dimensions/orientation mismatch: {name}: {spans[1:]}")
        if any(abs(low+high)/2 > .01 for low, high in intervals):
            errors.append(f"Wheel is displaced from its authored center: {name}")
        if wheel.get("role") == "sidewall":
            material = materials[mesh["material"]]
            texture_name = material["textures"].get("txDiffuse", {}).get("name")
            texture = visual["textures"].get(texture_name, {})
            if texture_name != "LS_wheel_sidewall.png" or min(texture.get("width", 0), texture.get("height", 0)) < 1024:
                errors.append(f"Flat wheel sidewall lacks the authored 1K+ texture: {name}")
        wheel_results.append({"node": name, "axleDepth": spans[0], "radialSpans": spans[1:]})
    for name in buses:
        if sum(wheel.get("bus") == name and wheel.get("role") == "tread" for wheel in wheels) != 4:
            errors.append(f"Bus does not expose four independently checked wheels: {name}")
    triangles = list(rendered_triangles(meshes))
    surfaces = check_horizontal_interfaces(triangles, errors)
    surfaces["signMounts"] = check_sign_faces(triangles, meshes, decals, errors)
    return {"signs": sign_results, "wheels": wheel_results, "surfaceAudit": surfaces}


def check_bus_probe_coverage(layout: dict, errors: list) -> int:
    reference_file = Path(__file__).resolve().parents[1]/"maps"/"last_shift"/"bus-clearance-probes.json"
    reference = json.loads(reference_file.read_text(encoding="utf-8"))["walkRoutes"]
    def signature(points):
        return tuple(tuple(round(value, 4) for value in point) for point in points)
    authored = {signature(route["points"]) for route in layout["probes"]["walkRoutes"] if route.get("bidirectional", True)}
    for route in reference:
        if signature(route["points"]) not in authored:
            errors.append("Missing independent bus doorway coverage: " + route["name"])
    return len(reference)


def validate(track: Path, layout_file: Path) -> dict:
    layout_bytes = layout_file.read_bytes()
    layout = json.loads(layout_bytes)
    visual = read_kn5(track / "last_shift.kn5")
    collision = read_kn5(track / "last_shift_collision.kn5")
    collision_nodes = {mesh["name"]: mesh for mesh in collision["meshes"]}
    bounds = collision_vertex_bounds(track / "last_shift_collision.kn5")
    errors = []
    features = layout.get("detailFeatures", [])
    if not isinstance(features, list) or not features:
        raise ValueError("Expected a nonempty detailFeatures list in the generated layout")
    kinds = {feature.get("kind") for feature in features}
    if "streetlamp" not in kinds:
        errors.append("No streetlamps recorded")
    if not kinds & {"wayfinding", "traffic_sign"}:
        errors.append("No depot signage recorded")
    if not any(feature.get("benchPosition") for feature in features if feature.get("kind") == "bus_stop"):
        errors.append("No staff-stop bench recorded")
    for kind in ("service_cabinet", "workshop_workstations", "wash_workstation", "dispatch_workstations",
                 "landing_guards", "roof_equipment"):
        if kind not in kinds:
            errors.append(f"Missing service detail category: {kind}")
    if not any(mesh["name"].startswith("LS_DETAIL_") for mesh in visual["meshes"]):
        errors.append("No batched detail geometry was exported")

    source_hashes = json.loads((track / "conversion-report.json").read_text(encoding="utf-8"))
    source_hashes = source_hashes.get("detailSourceSha256", {})
    for name in SOURCE_MODULES:
        actual = hashlib.sha256(Path(__file__).with_name(name).read_bytes()).hexdigest()
        if source_hashes.get(name) != actual:
            errors.append(f"Missing or stale detail source hash: {name}")

    proxies = layout.get("detailCollision", [])
    if not isinstance(proxies, list) or not proxies:
        raise ValueError("Expected a nonempty detailCollision list")
    checked_proxies = []
    names = set()
    for proxy in proxies:
        name, label = proxy["node"], proxy["label"]
        if name in names:
            errors.append(f"Duplicate detail collision node: {name}")
        names.add(name)
        mesh = collision_nodes.get(name)
        if mesh is None:
            errors.append(f"{label}: collision proxy was not exported: {name}")
            continue
        if not mesh["active"] or mesh["renderable"] or mesh["castShadows"]:
            errors.append(f"{label}: collision proxy has unsafe visibility/activity flags: {name}")
        if not name.startswith(("1WALL_", "1ROAD_")):
            errors.append(f"{label}: collision proxy lacks physical mesh naming: {name}")
        center, size = proxy["center"], proxy["size"]
        if len(center) != 3 or len(size) != 3 or not all(math.isfinite(x) for x in center + size) or min(size) <= 0:
            raise ValueError(f"Invalid collision dimensions: {label}")
        angle = math.radians(proxy.get("yaw", 0))
        c, s = abs(math.cos(angle)), abs(math.sin(angle))
        extent = ((c*size[0]+s*size[2])/2, size[1]/2, (s*size[0]+c*size[2])/2)
        expected_min = [center[i]-extent[i] for i in range(3)]
        expected_max = [center[i]+extent[i] for i in range(3)]
        actual_min, actual_max = bounds[name]
        maximum_error = max(abs(a-b) for a, b in zip(expected_min+expected_max, actual_min+actual_max))
        if maximum_error > .01:
            errors.append(f"{label}: exported proxy bounds differ from authored dimensions by {maximum_error:.4f}m")
        checked_proxies.append({"node": name, "label": label, "maximumBoundsErrorMetres": maximum_error})

    counts = {
        "visualTriangles": sum(mesh["triangles"] for mesh in visual["meshes"]),
        "visualMeshes": len(visual["meshes"]),
        "collisionTriangles": sum(mesh["triangles"] for mesh in collision["meshes"]),
        "collisionMeshes": len(collision["meshes"]),
        "visualMaterials": len(visual["materials"]),
        "visualBytes": visual["bytes"],
    }
    for name, limit in BUDGETS.items():
        if counts[name] > limit:
            errors.append(f"Detail budget exceeded: {name}={counts[name]}, limit={limit}")
    visual_checks = check_signs_and_wheels(layout, visual, track, errors)
    bus_probe_count = check_bus_probe_coverage(layout, errors)
    blocked_moves = layout["probes"].get("blockedMoves", [])
    if len(blocked_moves) < 2:
        errors.append("Missing physical landing-guard movement probes")
    return {
        "passed": not errors,
        "scope": "Detail inventory, source hashes, exported proxy bounds/flags and asset budgets. "
                 "Movement clearance needs LastShiftGate; visual agreement and runtime performance need in-game acceptance.",
        "errors": errors,
        "layoutSha256": hashlib.sha256(layout_bytes).hexdigest(),
        "models": [{"file": model["file"], "sha256": model["sha256"]}
                   for model in (visual, collision)],
        "counts": counts, "budgets": BUDGETS, "featureKinds": sorted(kinds),
        "featureCount": len(features), "detailSourceSha256": source_hashes,
        "collisionChecks": checked_proxies,
        "visualChecks": visual_checks,
        "independentBusDoorwayRoutes": bus_probe_count,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build-root", type=Path)
    parser.add_argument("--track", type=Path)
    parser.add_argument("--layout", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    if args.build_root:
        args.track = args.track or args.build_root / "content" / "tracks" / "last_shift"
        args.layout = args.layout or args.build_root / "last-shift-layout.json"
    if args.track is None or args.layout is None:
        parser.error("Supply --build-root or both --track and --layout")
    try:
        report = validate(args.track, args.layout)
    except (ValueError, OSError, TypeError, KeyError) as error:
        report = {"passed": False, "errors": [str(error)]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"passed": report["passed"], "errors": report["errors"],
                      "output": str(args.output)}))
    raise SystemExit(0 if report["passed"] else 1)


if __name__ == "__main__":
    main()
