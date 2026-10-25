"""Original enterable depot buses: open glazing, low floors and textured wheels.

All dimensions are Assetto Corsa metres. The body is a set of solid panels with
real door/window apertures; only the roof is excluded from player movement.
"""
from __future__ import annotations

import math
from pathlib import Path

import bpy


FLOOR = .40
DOORS = (4.72, -.20)
DOOR_WIDTH = 1.52
WHEEL_RADIUS = .52
WHEEL_DEPTH = .24
WHEEL_SEGMENTS = 24
AXLES = (-3.70, 3.15)
# Crop the radial chart to the generated tyre circumference. The image remains
# unchanged; its unused black square border must not surround the round mesh.
WHEEL_UV_CENTER = (.489, .514)
WHEEL_UV_RADIUS = .458


def build_mesh(d, label, vertices, faces, mat, name=None, uvs=None):
    mesh = bpy.data.meshes.new(label)
    mesh.from_pydata([d.a.xyz(p) for p in vertices], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(label, mesh)
    bpy.context.collection.objects.link(obj)
    d.register(obj, label, mat)
    if name:
        obj.name = name
    if uvs:
        layer = obj.data.uv_layers.active
        for polygon in mesh.polygons:
            for loop in polygon.loop_indices:
                layer.data[loop].uv = uvs[mesh.loops[loop].vertex_index]
    return obj


def wheel_material(d):
    path = Path(__file__).resolve().parents[1]/"maps"/"last_shift"/"textures"/"generated"/"wheel-sidewall.png"
    if not path.is_file():
        raise FileNotFoundError(f"Missing authored wheel sidewall texture: {path}")
    mat = bpy.data.materials.new("LS_wheel_sidewall")
    mat.use_nodes = True
    mat.diffuse_color = (.09, .09, .09, 1)
    mat["tile_metres"] = (1., 1.)
    mat["ac_shader"] = "ksPerPixel"
    mat["ac_specular"] = .12
    mat["ac_specular_exp"] = 18.
    image = bpy.data.images.load(str(path), check_existing=False)
    image.name = "LS_wheel_sidewall.png"
    image.colorspace_settings.name = "sRGB"
    image.pack()
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.name = "LS_wheel_sidewall_diffuse"
    tex.image = image
    tex.assettoCorsa.shaderInputName = "txDiffuse"
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Roughness"].default_value = .77
    shader.inputs["Alpha"].default_value = 1
    mat.node_tree.links.new(tex.outputs["Color"], shader.inputs["Base Color"])
    mat.node_tree.nodes.active = tex
    d.a.MATS["wheel_sidewall"] = mat


def wheel(d, p, bus_name, side, axle_z, yaw):
    """Axle is local X; flat caps use one radial sidewall/rim photograph each."""
    center = (side*1.34, WHEEL_RADIUS, axle_z)
    vertices = []
    for x in (-WHEEL_DEPTH/2, WHEEL_DEPTH/2):
        for i in range(WHEEL_SEGMENTS):
            angle = 2*math.pi*i/WHEEL_SEGMENTS
            vertices.append(p(center[0]+x, center[1]+WHEEL_RADIUS*math.sin(angle),
                              center[2]+WHEEL_RADIUS*math.cos(angle)))
    faces = []
    for i in range(WHEEL_SEGMENTS):
        j = (i+1) % WHEEL_SEGMENTS
        faces.append((i+WHEEL_SEGMENTS, j+WHEEL_SEGMENTS, j, i))
    suffix = f"{bus_name}_{'R' if side > 0 else 'L'}_{'F' if axle_z > 0 else 'R'}"
    obj = build_mesh(d, "bus tyre tread", vertices, faces, "dark", "LS_WHEEL_"+suffix)
    axis = [math.cos(math.radians(yaw)), 0., -math.sin(math.radians(yaw))]
    d.wheel_geometry.append({"node": obj.name, "bus": bus_name, "role": "tread",
                            "center": list(p(*center)), "axis": axis,
                            "radius": WHEEL_RADIUS, "depth": WHEEL_DEPTH})
    for cap_side in (-1, 1):
        # A flat disk, not modelled hub hardware. The source image is opaque.
        cap_x = center[0]+cap_side*(WHEEL_DEPTH/2+.001)
        cap_vertices = [p(cap_x, center[1], center[2])]
        uvs = [WHEEL_UV_CENTER]
        for i in range(WHEEL_SEGMENTS):
            angle = 2*math.pi*i/WHEEL_SEGMENTS
            sy, sz = math.sin(angle), math.cos(angle)
            cap_vertices.append(p(cap_x, center[1]+WHEEL_RADIUS*sy,
                                  center[2]+WHEEL_RADIUS*sz))
            uvs.append((WHEEL_UV_CENTER[0]+WHEEL_UV_RADIUS*sz,
                        WHEEL_UV_CENTER[1]+WHEEL_UV_RADIUS*sy))
        cap_faces = []
        for i in range(WHEEL_SEGMENTS):
            face = (0, i+1, (i+1) % WHEEL_SEGMENTS+1)
            cap_faces.append(face if cap_side < 0 else tuple(reversed(face)))
        cap = build_mesh(d, "flat textured bus wheel sidewall", cap_vertices,
                         cap_faces, "wheel_sidewall",
                         "LS_WHEEL_CAP_"+suffix+f"_{cap_side:+d}", uvs)
        d.wheel_geometry.append({"node": cap.name, "bus": bus_name, "role": "sidewall",
                                "center": list(p(cap_x, center[1], center[2])),
                                "axis": axis, "radius": WHEEL_RADIUS, "depth": 0.})


def ramp(d, p, bus_name, z, yaw):
    # A 15 degree ramp keeps capsule footprint support within navigation tolerance.
    # The rendered surface and physical triangles use the same coordinates.
    x0, x1 = 1.20, 2.65
    z0, z1 = z-DOOR_WIDTH/2, z+DOOR_WIDTH/2
    local = [(x0,FLOOR,z0),(x1,0,z0),(x1,0,z1),(x0,FLOOR,z1),
             (x0,0,z0),(x0,0,z1)]
    faces = [(0,3,2,1),(0,1,4),(3,5,2),(0,4,5,3),(4,1,2,5)]
    vertices = [p(*q) for q in local]
    build_mesh(d, "bus boarding ramp", vertices, faces, "galvanized")
    mesh = bpy.data.meshes.new("bus boarding ramp collision")
    mesh.from_pydata([d.a.xyz(q) for q in vertices], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(f"1ROAD_LS_{len(d.a.COLLIDERS):04d}", mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(d.a.MATS["collision"])
    d.a.uv_world(obj)
    obj.hide_render = True
    obj.hide_set(True)
    d.a.COLLIDERS.append(obj)
    record = {"node": obj.name, "label": bus_name+" open door boarding ramp",
              "center": p((x0+x1)/2,FLOOR/2,z),
              "size": (x1-x0,FLOOR,DOOR_WIDTH), "yaw": yaw, "solid": True}
    d.a.SEMANTICS.append(record)
    d.collision.append(record)


def add_probes(d, p, bus_name):
    def route(label, points):
        d.bus_probes["walkRoutes"].append({"name": bus_name+" "+label,
            "points": [p(*q) for q in points], "bidirectional": True})

    for index, z in enumerate(DOORS):
        for offset in (-.20, 0., .20):
            route(f"door {index+1} shoulder {offset:+.2f}",
                  [(2.8,0,z+offset),(2.25,0,z+offset),(1.20,FLOOR,z+offset),
                   (0,FLOOR,z+offset)])
    route("enter aisle and exit", [(2.8,0,DOORS[0]),(2.25,0,DOORS[0]),
          (1.20,FLOOR,DOORS[0]),(0,FLOOR,DOORS[0]),(0,FLOOR,DOORS[1]),
          (1.20,FLOOR,DOORS[1]),(2.25,0,DOORS[1]),(2.8,0,DOORS[1])])
    for x in (-.20, 0., .20):
        route(f"full aisle seat clearance {x:+.2f}", [(x,FLOOR,-5.),(x,FLOOR,4.9)])
    for z in (-4.7,-2.3,DOORS[1],2.3,DOORS[0]):
        d.bus_probes["standingPoints"].append({"name": bus_name+f" interior {z:.2f}",
                                               "position": p(0,FLOOR,z)})
    for side in (-1,1):
        for y, blocked, label in ((2.10,False,"open window"),(.95,True,"solid lower panel")):
            points = (p(0,y,-2.3),p(side*2.,y,-2.3))
            for reverse in (False,True):
                d.bus_probes["shotProbes"].append({"name": bus_name+f" {label} side {side} {'in' if reverse else 'out'}",
                    "from": points[reverse], "to": points[not reverse], "blocked": blocked})
    d.bus_probes["shotProbes"].append({"name":bus_name+" solid roof",
        "from":p(0,2.3,-2.3),"to":p(0,4.,-2.3),"blocked":True})


def build_bus(d, item, number):
    x, z, yaw = item["x"], item["z"], item["yaw"]
    angle = math.radians(yaw)
    def p(dx, y, dz):
        return (x+dx*math.cos(angle)+dz*math.sin(angle), y,
                z-dx*math.sin(angle)+dz*math.cos(angle))
    def box(label, center, size, mat="fleet", solid=True):
        obj = d.box("bus "+label, p(*center), size, mat, yaw=yaw)
        if solid:
            d.proxy(item["name"]+" "+label, p(*center), size, yaw)
        return obj

    box("low floor chassis",(0,.25,0),(2.56,.30,11.5),"dark")
    box("interior floor finish",(0,.403,0),(2.38,.006,11.2),"dark",False)
    roof = box("roof",(0,3.04,0),(2.68,.16,11.6),"paint")
    roof["bus_roof"] = True
    if hasattr(d.a,"ROOFS"):
        d.a.ROOFS.append(roof)
    # Roof starts at 2.96; interior actor head is 2.20. Volume bottom is 3.20.
    d.a.movement_box(p(0,14.1,0),(2.8,21.8,11.7),yaw)
    for side in (-1,1):
        openings = [(c-DOOR_WIDTH/2,c+DOOR_WIDTH/2) for c in DOORS] if side>0 else []
        cursor = -5.7
        for start,end in sorted(openings)+[(5.7,5.7)]:
            if start>cursor:
                span, middle = start-cursor,(cursor+start)/2
                box("lower side panel",(side*1.27,.84,middle),(.14,.92,span))
                box("lower rubber rub strip",(side*1.35,.66,middle),(.035,.09,span),"dark",False)
                box("window sill trim",(side*1.35,1.28,middle),(.03,.055,span),"galvanized",False)
            cursor=end
        box("upper window rail",(side*1.29,2.88,0),(.12,.16,11.5),"fleet")
        # Posts match visible window frames. No opaque or invisible glazing.
        for zz in (-5.63,-4.55,-3.05,-1.5,.7,2.25,3.81,5.62):
            if any(a-.04<zz<b+.04 for a,b in openings):
                continue
            box("window frame post",(side*1.29,2.05,zz),(.10,1.5,.07),"dark")
        for door in DOORS if side>0 else ():
            for edge in (-1,1):
                box("open door jamb",(side*1.3,1.63,door+edge*(DOOR_WIDTH/2+.055)),(.14,2.46,.10),"dark")
            box("door header",(1.3,2.85,door),(.17,.18,DOOR_WIDTH+.20),"dark")
            ramp(d,p,item["name"],door,yaw)
            # Leaves folded flat along the outside body beyond the clear jamb.
            box("folded door leaf",(1.40,1.62,door-DOOR_WIDTH/2-.20),(.08,2.35,.18),"fleet",False)
        for axle in AXLES:
            box("interior wheel arch",(side*1.09,.78,axle),(.35,.76,1.24),"dark")
            wheel(d,p,item["name"],side,axle,yaw)
    for end in (-1,1):
        box("end lower panel",(0,.85,end*5.71),(2.60,.90,.14))
        box("windscreen header",(0,2.87,end*5.71),(2.60,.18,.14),"fleet")
        for xx in (-1.24,1.24):
            box("windscreen pillar",(xx,2.06,end*5.71),(.12,1.54,.16),"dark")
        box("bumper",(0,.59,end*5.82),(2.63,.19,.16),"galvanized",False)
        for xx in (-.99,.99):
            box("headlight" if end>0 else "rear lamp",(xx,1.05,end*5.80),
                (.27,.23,.045),"lamp_lens" if end>0 else "safety_red",False)
    box("front lower grille",(0,.91,5.795),(1.38,.34,.025),"dark",False)
    for yy in (.79,.89,.99):
        box("front grille slat",(0,yy,5.82),(1.30,.022,.02),"galvanized",False)
    # Bitmap quads share the depot sign atlas and follow every bus's actual yaw.
    # Keep all lettering out of geometry and all backing panels non-colliding.
    d.decals.panel(item["name"]+" destination display","04  DEPOT",p(0,2.87,5.82),
                   2.10,.22,"north","dark","yellow",yaw=yaw)
    d.decals.panel(item["name"]+" front registration",f"SV {number}-04",p(0,.61,5.925),
                   .52,.13,"north","paint","dark",yaw=yaw)
    d.decals.panel(item["name"]+" rear registration",f"SV {number}-04",p(0,.61,-5.925),
                   .52,.13,"south","paint","dark",yaw=yaw)
    for side,face in ((-1,"west"),(1,"east")):
        d.decals.panel(item["name"]+f" fleet number side {side}",str(number),
                       p(side*1.36,1.13,2.0),.62,.23,face,"fleet","paint",yaw=yaw)
        d.decals.panel(item["name"]+f" transit operator side {side}","SEVER TRANSIT",
                       p(side*1.36,1.08,-2.28),1.15,.18,face,"fleet","paint",yaw=yaw)
    box("roof ventilation unit",(0,3.23,-2.4),(1.60,.22,1.90),"galvanized",False)
    # Narrow seats leave a 1.64 m central aisle; single collision envelope each.
    for side in (-1,1):
        for zz in (-4.65,-3.65,-2.65,-1.65,1.35,2.35,3.35):
            if side>0 and any(abs(zz-door)<1.25 for door in DOORS):
                continue
            # Cushions over a wheel housing clear its 1.16 m top. The front axle
            # stays behind the front aperture, including a standing actor's
            # shoulder width through the rear half of that doorway.
            seat_base = 1.22 if any(abs(zz-axle)<.93 for axle in AXLES) else .82
            box("seat cushion",(side*1.,seat_base,zz),(.35,.12,.60),"sign_blue",False)
            box("seat back",(side*1.,seat_base+.28,zz-.27),(.35,.50,.07),"sign_blue",False)
            box("seat pedestal",(side*1.,(FLOOR+seat_base)/2,zz),(.16,seat_base-FLOOR,.32),"galvanized",False)
            d.proxy(item["name"]+" seat",p(side*1.,(FLOOR+seat_base+.53)/2,zz),
                    (.35,seat_base+.53-FLOOR,.62),yaw)
    # Driver's equipment stays to the left of the front entry and main aisle.
    box("driver console",(-.93,1.03,5.15),(.40,.38,.55),"dark")
    box("driver seat",(-.88,.96,4.32),(.40,.78,.66),"sign_blue")
    for zz in (-4.8,-1.15,1.05,4.0):
        d.beam("bus grab pole",p(-1.13,FLOOR,zz),p(-1.13,2.73,zz),.023,"yellow",12)
    for xx in (-.96,.96):
        d.beam("bus ceiling grab rail",p(xx,2.70,-5.1),p(xx,2.70,5.0),.022,"yellow",12)
        d.beam("bus mirror stalk",p(xx*1.27,2.53,5.29),p(xx*1.58,2.61,5.45),.022,"galvanized")
        box("mirror",(xx*1.60,2.50,5.43),(.13,.33,.08),"dark",False)
    for zz in (-3.8,0.,3.8):
        box("interior ceiling light",(0,2.94,zz),(.22,.025,.86),"lamp_lens",False)
    d.features.append({"kind":"bus_detail","label":f"Enterable fleet {number}",
        "bus":item["name"],"position":[x,0,z],"floorHeight":FLOOR,
        "doorWidth":DOOR_WIDTH,"doorCenters":[p(1.30,FLOOR,c) for c in DOORS],
        "windowSill":1.30,"windowTop":2.80,"roofVolumeBottom":3.20})
    add_probes(d,p,item["name"])


def build(d, layout):
    d.bus_probes = {"walkRoutes":[],"standingPoints":[],"shotProbes":[]}
    d.wheel_geometry = []
    wheel_material(d)
    for number,item in enumerate(layout["buses"],217):
        build_bus(d,item,number)
