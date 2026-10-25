"""Replace stacked floor coatings with adjacent faces sharing one surface.

Rendering only: the original walkable ground and slab proxies remain intact.
Convex clipping also handles the angled shelter plinth without a bounding-box gap.
"""
import math

import bmesh
import bpy
from mathutils import Matrix


PRIORITIES = {
    "depot asphalt": 0, "external access road": 1, "asphalt repair": 2,
    "yard dashed line": 3, "external road marking": 3,
    "workshop floor": 4, "dispatch ground floor": 4, "wash concrete pad": 4,
    "concrete slab joint": 5,
    "external footpath": 6, "parts loading apron": 6, "staff shelter paved plinth": 6,
    "south workshop pavement": 6, "east workshop pavement": 6,
    "dispatch front pavement": 6, "staff stop pavement": 6,
    "west workshop service apron": 6, "pavement expansion joint": 7,
}


def area(poly):
    return abs(sum(p[0]*q[1]-q[0]*p[1] for p,q in zip(poly,poly[1:]+poly[:1])))/2


def clip(poly, start, end, inside):
    def distance(p):
        return (end[0]-start[0])*(p[1]-start[1])-(end[1]-start[1])*(p[0]-start[0])
    result = []
    for p,q in zip(poly,poly[1:]+poly[:1]):
        a,b = distance(p),distance(q)
        keep_a,keep_b = (a >= 0,b >= 0) if inside else (a <= 0,b <= 0)
        if keep_a:
            result.append(p)
        if keep_a != keep_b:
            t = a/(a-b)
            result.append((p[0]+t*(q[0]-p[0]),p[1]+t*(q[1]-p[1])))
    clean = []
    for point in result:
        if not clean or math.dist(point,clean[-1]) > 1e-8:
            clean.append(point)
    if len(clean) > 1 and math.dist(clean[0],clean[-1]) < 1e-8:
        clean.pop()
    changed = True
    while changed and len(clean) >= 3:
        changed = False
        for index,p in enumerate(clean):
            previous,following = clean[index-1],clean[(index+1)%len(clean)]
            cross = (p[0]-previous[0])*(following[1]-p[1])-(p[1]-previous[1])*(following[0]-p[0])
            if abs(cross) < 1e-9:
                clean.pop(index)
                changed = True
                break
    return clean


def subtract(poly, cutter):
    remaining, result = poly, []
    for p,q in zip(cutter,cutter[1:]+cutter[:1]):
        outside = clip(remaining,p,q,False)
        if len(outside) >= 3 and area(outside) > 1e-8:
            result.append(outside)
        remaining = clip(remaining,p,q,True)
        if len(remaining) < 3 or area(remaining) < 1e-8:
            break
    return result


def rectangle(record):
    x,_,z = record["center"]
    w,_,depth = record["size"]
    angle = math.radians(record["yaw"])
    c,s = math.cos(angle),math.sin(angle)
    return [(x+dx*c+dz*s,z-dx*s+dz*c)
            for dx,dz in ((-w/2,-depth/2),(w/2,-depth/2),(w/2,depth/2),(-w/2,depth/2))]


def build(d):
    a = d.a
    bpy.context.view_layer.update()
    by_name = {obj.name:obj for obj in a.VISUALS}
    selected = [(r,by_name[r["node"]]) for r in a.SEMANTICS
                if r["node"] in by_name and r["label"] in PRIORITIES]
    selected.sort(key=lambda pair: PRIORITIES[pair[0]["label"]])
    layers = []
    for record,obj in selected:
        label = record["label"]
        height = record["center"][1]+record["size"][1]/2
        if label in ("asphalt repair","yard dashed line","workshop floor",
                     "dispatch ground floor","wash concrete pad","concrete slab joint"):
            height = 0.
        elif label == "external road marking":
            height = -.01
        elif label == "pavement expansion joint":
            height = .04
        layers.append({"record":record,"object":obj,"polygon":rectangle(record),"height":height})

    d.surface_repairs = []
    removed_names = set()
    for index,layer in enumerate(layers):
        record,obj = layer["record"],layer["object"]
        pieces = [layer["polygon"]]
        for upper in layers[index+1:]:
            pieces = [piece for polygon in pieces for piece in subtract(polygon,upper["polygon"])]
        # Broad slabs retain their visible vertical edges. Millimetre-thick paint
        # and pad boxes become top faces only, eliminating their extra layers.
        keep_sides = record["size"][1] >= .02
        verts,faces = [],[]
        if keep_sides:
            for face in obj.data.polygons:
                if face.normal.z > .5:
                    continue
                start = len(verts)
                verts.extend(tuple(obj.matrix_world @ obj.data.vertices[v].co) for v in face.vertices)
                faces.append(tuple(range(start,len(verts))))
        for polygon in pieces:
            start = len(verts)
            verts.extend(a.xyz((x,layer["height"],z)) for x,z in reversed(polygon))
            faces.append(tuple(range(start,len(verts))))
        if not faces:
            removed_names.add(obj.name)
            position = a.VISUALS.index(obj)
            if position < d.first_visual:
                d.first_visual -= 1
            a.VISUALS.remove(obj)
            bpy.data.objects.remove(obj,do_unlink=True)
            continue
        material = obj.data.materials[0]
        mesh = bpy.data.meshes.new(record["label"]+" single surface")
        mesh.from_pydata(verts,[],faces)
        mesh.update()
        obj.data = mesh
        obj.matrix_world = Matrix.Identity(4)
        obj.data.materials.append(material)
        a.uv_world(obj)
        old_name = obj.name
        obj.name = f"LS_SURFACE_{index:03d}"
        for semantic in a.SEMANTICS:
            if semantic["node"] == old_name:
                semantic["node"] = obj.name
        d.surface_repairs.append({"node":obj.name,"label":record["label"],
                                  "height":layer["height"],"visiblePatches":len(pieces)})

    # The west upper-door brick sill is buried beneath the two adjoining slabs.
    # Its top used to render exactly on the concrete at Y=3, producing the stripe.
    removed_caps = 0
    removed_roofs = 0
    for record in a.SEMANTICS:
        is_sill = (record["label"] == "dispatch west"
                   and abs(record["center"][1]+record["size"][1]/2-3) < 1e-6)
        is_old_roof = record["label"] == "north utility shed"
        if record["node"] not in by_name or not (is_sill or is_old_roof):
            continue
        obj = by_name[record["node"]]
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        caps = [face for face in bm.faces if face.normal.z > .99]
        removed_caps += len(caps) if is_sill else 0
        removed_roofs += len(caps) if is_old_roof else 0
        bmesh.ops.delete(bm,geom=caps,context="FACES_ONLY")
        bm.to_mesh(obj.data)
        bm.free()
        a.uv_world(obj)
    if removed_caps != 1:
        raise ValueError(f"Expected one buried upper-door sill cap, found {removed_caps}")
    if removed_roofs != 1:
        raise ValueError(f"Expected one replaced utility roof face, found {removed_roofs}")

    # Bus floor finish and chassis use the same material; the 6mm duplicate is
    # redundant. Retain the chassis as the single visible walking surface.
    for record in a.SEMANTICS:
        if record["label"] == "bus interior floor finish" and record["node"] in by_name:
            obj = by_name[record["node"]]
            removed_names.add(obj.name)
            a.VISUALS.remove(obj)
            bpy.data.objects.remove(obj,do_unlink=True)
    a.SEMANTICS[:] = [record for record in a.SEMANTICS if record["node"] not in removed_names]
    d.features.append({"kind":"surface_inlays","label":"Non-overlapping floors, markings and joints",
                       "position":[0,0,0],"buriedSillCapsRemoved":removed_caps})
