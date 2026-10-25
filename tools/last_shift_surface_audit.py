"""Focused checks of rendered surfaces at Last Shift's reported problem areas."""
from __future__ import annotations

import math


def sub(a, b):
    return tuple(x-y for x, y in zip(a, b))


def dot(a, b):
    return sum(x*y for x, y in zip(a, b))


def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def rendered_triangles(meshes):
    for name, mesh in meshes.items():
        if not (mesh["active"] and mesh["visible"] and mesh["renderable"]):
            continue
        for index in range(0, len(mesh["indices"]), 3):
            points = [mesh["points"][i] for i in mesh["indices"][index:index+3]]
            normal = cross(sub(points[1], points[0]), sub(points[2], points[0]))
            length = math.sqrt(dot(normal, normal))
            if length > 1e-8:
                yield {"node": name, "triangle": index//3, "material": mesh["material"],
                       "points": points, "normal": tuple(v/length for v in normal)}


def _side(a, b, p):
    return (b[0]-a[0])*(p[1]-a[1])-(b[1]-a[1])*(p[0]-a[0])


def _clip(polygon, boundary):
    """Intersect convex polygons; touching edges contribute zero area."""
    if sum(a[0]*b[1]-b[0]*a[1] for a, b in zip(boundary, boundary[1:]+boundary[:1])) < 0:
        boundary = list(reversed(boundary))
    for a, b in zip(boundary, boundary[1:]+boundary[:1]):
        if not polygon:
            break
        output = []
        previous = polygon[-1]
        previous_side = _side(a, b, previous)
        for point in polygon:
            side = _side(a, b, point)
            if (side >= 0) != (previous_side >= 0):
                ratio = previous_side/(previous_side-side)
                output.append(tuple(x+ratio*(y-x) for x, y in zip(previous, point)))
            if side >= 0:
                output.append(point)
            previous, previous_side = point, side
        polygon = output
    return polygon


def _area(polygon):
    return abs(sum(a[0]*b[1]-b[0]*a[1] for a, b in zip(polygon, polygon[1:]+polygon[:1])))/2


def check_horizontal_interfaces(triangles, errors):
    # These cover the user's disputed sill/landing and the three concrete pads.
    # Compare actual faces, including pairs merged into the same render batch.
    zones = {
        "external landing and upper doorway": (4.3, 9.0, 23.8, 28.1, 2.98, 3.02),
        "workshop floor": (-31, -9, -16, 18, -.01, .12),
        "dispatch floor": (8, 24, 13, 28, -.01, .12),
        "wash pad": (30, 40, -8, 8, -.01, .12),
    }
    structural = {"LS_asphalt", "LS_concrete", "LS_brick", "LS_dark"}
    findings, counts, coverage = [], {}, {}
    for zone, (x0, x1, z0, z1, y0, y1) in zones.items():
        boundary = [(x0,z0), (x1,z0), (x1,z1), (x0,z1)]
        candidates = []
        for tri in triangles:
            p = tri["points"]
            if (tri["material"] not in structural or tri["normal"][1] < .99999
                    or not y0 <= p[0][1] <= y1):
                continue
            polygon = _clip([(v[0],v[2]) for v in p], boundary)
            if _area(polygon) > .0001:
                candidates.append((tri, polygon))
        counts[zone] = len(candidates)
        for index, (first, polygon) in enumerate(candidates):
            for second, other in candidates[index+1:]:
                separation = abs(first["points"][0][1]-second["points"][0][1])
                if separation >= .015:
                    continue
                overlap = _area(_clip(polygon, other))
                if overlap <= .01:
                    continue
                findings.append({"zone": zone, "first": first["node"], "firstTriangle": first["triangle"],
                                 "second": second["node"], "secondTriangle": second["triangle"],
                                 "separationMetres": separation, "overlapSquareMetres": overlap})
        if y1 < 1:
            ground = []
            for tri in triangles:
                p = tri["points"]
                if tri["normal"][1] > .99999 and y0 <= p[0][1] <= y1:
                    polygon = [(v[0],v[2]) for v in p]
                    ground.append((polygon, min(v[0] for v in polygon), max(v[0] for v in polygon),
                                   min(v[1] for v in polygon), max(v[1] for v in polygon)))
            missing, samples = [], 0
            for ix in range(math.ceil((x1-x0)/1.2)):
                for iz in range(math.ceil((z1-z0)/1.2)):
                    point = (x0+.37+ix*1.2, z0+.43+iz*1.2)
                    if point[0] >= x1 or point[1] >= z1:
                        continue
                    samples += 1
                    supported = False
                    for polygon, left, right, bottom, top in ground:
                        if not (left <= point[0] <= right and bottom <= point[1] <= top):
                            continue
                        sides = [_side(a, b, point) for a, b in zip(polygon, polygon[1:]+polygon[:1])]
                        if min(sides) >= -1e-7 or max(sides) <= 1e-7:
                            supported = True
                            break
                    if not supported:
                        missing.append(point)
            coverage[zone] = {"samples": samples, "missingRenderedFloor": missing}
            if missing:
                errors.append(f"Rendered floor coverage missing at {len(missing)} samples in {zone}")
    if findings:
        errors.append(f"Rendered horizontal surfaces overlap within 15mm at {len(findings)} face pairs; see surfaceAudit")
    return {"candidateTriangles": counts, "nearCoplanarOverlaps": findings, "floorCoverage": coverage}


def check_sign_faces(triangles, meshes, decals, errors):
    by_node = {}
    for tri in triangles:
        by_node.setdefault(tri["node"], []).append(tri)
    results = []
    for sign in decals:
        name, backing = sign["node"], sign.get("backingNode")
        normal = sign["normal"]
        origin = sign.get("frontPosition")
        if origin is None or backing not in meshes or name not in meshes:
            errors.append(f"Sign lacks an independently exported open-front backing: {name}")
            continue
        right = (normal[2], 0, -normal[0])
        width, height = sign["width"], sign["height"]
        expected = [tuple(origin[i]+u*width*.5*right[i]+(v*height*.5 if i == 1 else 0)
                          for i in range(3)) for u, v in ((-1,-1),(1,-1),(1,1),(-1,1))]
        corners = {tuple(p) for p in meshes[name]["points"]}
        corner_error = max(max(min(math.dist(p, q) for q in expected) for p in corners),
                           max(min(math.dist(p, q) for q in corners) for p in expected))
        if len(corners) != 4 or corner_error > .0001:
            errors.append(f"Sign exported corners differ from the authored front plane: {name}")
        if any(dot(tri["normal"], normal) < .999 for tri in by_node.get(name, [])):
            errors.append(f"Sign exported face points away from its outward normal: {name}")
        back_faces = by_node.get(backing, [])
        if len(back_faces) != 10 or any(dot(tri["normal"], normal) > .0001 for tri in back_faces):
            errors.append(f"Sign backing retains a competing front face or is incomplete: {name}")

        # Sample across the face and close to its edges. A nearly parallel host
        # within 15mm behind can flicker; one in front can hide the sign outright.
        samples = [tuple(origin[i]+(-.98+.245*u)*width*.5*right[i]
                         + ((-.98+.49*v)*height*.5 if i == 1 else 0) for i in range(3))
                   for u in range(9) for v in range(5)]
        conflicts = []
        for tri in triangles:
            if tri["node"] in (name, backing):
                continue
            denominator = dot(normal, tri["normal"])
            if abs(denominator) < .95:
                continue
            depths = [dot(sub(p, origin), normal) for p in tri["points"]]
            if min(depths) >= .15 or max(depths) <= -.015:
                continue
            for sample_index, sample in enumerate(samples):
                distance = dot(sub(tri["points"][0], sample), tri["normal"])/denominator
                if not -.015 < distance < .15:
                    continue
                point = tuple(sample[i]+distance*normal[i] for i in range(3))
                p = tri["points"]
                if all(dot(cross(sub(b, a), sub(point, a)), tri["normal"]) >= -1e-7
                       for a, b in zip(p, p[1:]+p[:1])):
                    conflicts.append({"node": tri["node"], "triangle": tri["triangle"],
                                      "sample": sample_index, "signedDistanceMetres": distance})
                    break
        if conflicts:
            errors.append(f"Sign face has competing or obscuring host geometry: {name}")
        results.append({"node": name, "backing": backing, "maximumCornerErrorMetres": corner_error,
                        "backingTriangles": len(back_faces), "hostSamples": len(samples),
                        "hostConflicts": conflicts})
    return results
