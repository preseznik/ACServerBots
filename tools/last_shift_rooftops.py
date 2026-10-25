"""Continuous dispatch landing guards and visible utility-roof equipment.

Original metric geometry, in Assetto Corsa X-east/Y-up/Z-north coordinates.
The utility roof retains its existing movement exclusion. The short open landing
rail uses a movement-only strip plus physical rails, so its gaps pass shots.
"""
from __future__ import annotations

import math

import bpy


def _solid(d, label, center, size, mat="galvanized"):
    visual = d.box(label, center, size, mat)
    d.proxy(label, center, size)
    return visual


def _fan(d, x, y, z, radius=.33):
    """Five raised blades and a wire guard over the housing's dark east face."""
    vertices, faces = [], []
    for index in range(5):
        angle = index*2*math.pi/5
        start = len(vertices)
        for distance, offset in ((.06,-.30),(.27,-.02),(.29,.33),(.08,.37)):
            a = angle+offset
            vertices.append(d.a.xyz((x,y+distance*math.sin(a),z+distance*math.cos(a))))
        faces.append((start,start+3,start+2,start+1))
    mesh = bpy.data.meshes.new("roof condenser impeller")
    mesh.from_pydata(vertices,[],faces)
    mesh.update()
    obj = bpy.data.objects.new("roof condenser impeller",mesh)
    bpy.context.collection.objects.link(obj)
    d.register(obj,"roof condenser impeller","galvanized")
    d.cylinder("roof condenser fan motor",(x+.016,y,z),.068,.043,
               "steel",axis="x",vertices=12)
    # Sparse open geometry casts the grille shadow without a solid grille plate.
    ring_x = x+.055
    for i in range(16):
        a, b = i*math.pi/8,(i+1)*math.pi/8
        d.beam("roof condenser fan guard rim",
               (ring_x,y+radius*math.sin(a),z+radius*math.cos(a)),
               (ring_x,y+radius*math.sin(b),z+radius*math.cos(b)),
               .010,"dark",6)
    for offset in (-.22,-.11,0.,.11,.22):
        half = math.sqrt(radius*radius-offset*offset)
        d.beam("roof condenser fan guard wire",(ring_x,y-half,z+offset),
               (ring_x,y+half,z+offset),.008,"dark",6)
    d.beam("roof condenser fan guard brace",(ring_x+.004,y,z-radius),
           (ring_x+.004,y,z+radius),.009,"dark",6)


def _landing(d):
    # The slab is x=4.4..8, z=25..28 at Y=3. Existing west parapet is
    # centred at x=4.5. This north return overlaps both it and the dispatch wall.
    _solid(d,"dispatch landing north parapet",(6.25,3.525,27.92),
           (3.5,1.05,.30),"concrete")
    _solid(d,"dispatch landing north coping",(6.185,4.085,27.92),
           (3.75,.07,.38),"galvanized")
    _solid(d,"dispatch landing west coping",(4.50,4.085,26.365),
           (.38,.07,2.73),"galvanized")
    # Close only the 0.9 m exposed strip beside the top of the stairs. Its left
    # post stays outside x=7.1 and the doorway route at z=26 stays unobstructed.
    for x in (7.18,7.96):
        _solid(d,"dispatch landing return guard post",(x,3.55,25.10),
               (.07,1.10,.07))
        d.box("landing return post footplate",(x,3.025,25.10),
              (.14,.05,.14),"steel")
    for y in (3.55,4.07):
        d.beam("dispatch landing return rail",(7.14,y,25.10),(8.,y,25.10),
               .035,"galvanized",10)
        d.proxy("dispatch landing return rail",(7.57,y,25.10),(.86,.07,.07))
    d.beam("stair handrail landing connector",(7.,4.02,25.),
           (7.18,4.07,25.10),.035,"galvanized",10)
    d.box("landing rail wall attachment",(7.86,4.07,25.10),
          (.055,.15,.15),"steel")
    d.a.movement_box((7.57,3.55,25.10),(.86,1.10,.10))
    d.features.append({"kind":"landing_guards",
        "label":"Continuous north parapet and short south landing return rail",
        "position":[6.25,3.,26.50],
        "northParapet":{"center":[6.25,3.525,27.92],"size":[3.5,1.05,.30]},
        "returnMovementStrip":{"center":[7.57,3.55,25.10],"size":[.86,1.10,.10]},
        "stairMouth":[4.6,7.1,25.],"doorRouteZ":26.})

    d.roof_probes["walkRoutes"].extend([
        {"name":"guarded landing stair arrival","points":[[5.85,3.,25.],[5.85,3.,26.]],"bidirectional":True},
        {"name":"guarded landing west door crossing","points":[[5.85,3.,26.],[9.5,3.,26.]],"bidirectional":True},
        {"name":"guarded landing north circulation","points":[[5.2,3.,27.1],[7.25,3.,27.1],[7.25,3.,26.]],"bidirectional":True},
    ])
    d.roof_probes["standingPoints"].extend([
        {"name":"guarded landing central standing","position":[6.25,3.,26.4]},
        {"name":"guarded landing beside return rail","position":[7.45,3.,25.85]},
    ])
    d.roof_probes["shotProbes"].extend([
        {"name":"landing north parapet blocks low shot","from":[6.25,3.65,27.2],"to":[6.25,3.65,28.5],"blocked":True},
        {"name":"landing north view remains open above parapet","from":[6.25,4.5,27.2],"to":[6.25,4.5,28.5],"blocked":False},
        {"name":"landing return open rail gap passes shots","from":[7.57,3.80,24.7],"to":[7.57,3.80,25.5],"blocked":False},
        {"name":"landing return top rail blocks matching shot","from":[7.57,4.07,24.7],"to":[7.57,4.07,25.5],"blocked":True},
    ])
    d.roof_probes["blockedMoves"].extend([
        {"name":"landing north edge guarded","from":[6.2,3.,27.25],"to":[6.2,3.,28.4],"height":1.8},
        {"name":"landing south return gap guarded","from":[7.57,3.,25.8],"to":[7.57,3.,24.8],"height":1.8},
    ])


def _utility_roof(d):
    # Existing shed is x=-8..2, z=21.5..28.5, with its roof at Y=3.6.
    d.box("utility roof weather membrane",(-3.,3.615,25.),(9.65,.03,6.65),"dark")
    for x in (-6.8,-5.5,-4.2,-2.9,-1.6,-.3,1.):
        d.box("utility roof membrane lap",(x,3.641,25.),(.030,.022,6.5),"steel")
    for x in (-7.88,1.88):
        _solid(d,"utility roof side parapet",(x,3.79,25.),(.24,.38,6.52),"concrete")
        _solid(d,"utility roof side coping",(x,4.005,25.),(.34,.05,6.42))
    for z in (21.62,28.38):
        _solid(d,"utility roof end parapet",(-3.,3.79,z),(10.,.38,.24),"concrete")
        _solid(d,"utility roof end coping",(-3.,4.005,z),(10.10,.05,.34))

    # Twin axial fans face the landing. The housing rests on isolated rails;
    # supply duct penetrates the roof, and an external isolator explains service.
    for x in (-1.75,-.65):
        for z in (24.12,26.08):
            d.box("roof condenser rubber mount",(x,3.675,z),(.28,.12,.25),"dark")
        d.box("roof condenser support rail",(x,3.76,25.1),(.12,.08,2.38),"steel")
    housing = _solid(d,"utility rooftop condenser housing",(-1.2,4.21,25.1),
                     (1.45,.88,2.50),"galvanized")
    # Colour actual fan/coil faces instead of stacking near-coplanar dark panels.
    housing.data.materials.append(d.a.MATS["dark"])
    for polygon in housing.data.polygons:
        if polygon.normal.x > .9 or abs(polygon.normal.y) > .9:
            polygon.material_index = len(housing.data.materials)-1
    d.box("condenser top cap",(-1.2,4.67,25.1),(1.53,.04,2.58),"galvanized")
    for z in (24.48,25.72):
        _fan(d,-.442,4.22,z)
    for z in (23.833,26.367):
        for y in (3.98,4.09,4.20,4.31,4.42):
            d.box("condenser coil louvre",(-1.2,y,z),(1.10,.024,.040),"galvanized")
    _solid(d,"utility roof supply duct",(-3.02,4.00,24.48),(2.20,.42,.48))
    _solid(d,"utility roof duct penetration",(-4.01,3.80,24.48),(.48,.40,.48))
    d.box("utility roof duct flashing",(-4.01,3.646,24.48),(.76,.032,.76),"steel")
    for x in (-3.80,-2.90,-2.02):
        d.box("roof duct joint band",(x,4.00,24.48),(.035,.445,.505),"dark")
    d.box("condenser local isolator",(-.42,3.98,26.06),(.13,.23,.18),"paint")
    d.box("condenser isolator switch",(-.345,3.98,26.06),(.032,.08,.06),"safety_red")
    d.beam("condenser power conduit",(-.39,3.88,26.06),(.56,3.70,26.06),.022,"dark",8)
    d.beam("condenser roof conduit",(.56,3.70,26.06),(.56,3.70,22.6),.022,"dark",8)

    # Access hatch and pads provide an intelligible maintenance route without
    # making this excluded rooftop playable or introducing a climbable ladder.
    d.box("utility roof hatch curb",(.78,3.70,22.68),(.92,.18,1.18),"steel")
    d.box("utility roof hatch lid",(.78,3.808,22.68),(1.02,.055,1.28),"galvanized")
    for z in (22.28,23.08):
        d.beam("roof hatch hinge",(.28,3.82,z),(.28,3.82,z+.10),.022,"dark",8)
    d.beam("roof hatch pull",(1.15,3.87,22.53),(1.15,3.87,22.81),.024,"steel",8)
    for z in (23.63,24.35,25.07,25.79,26.51):
        d.box("utility roof service pad",(.90,3.6525,z),(.76,.045,.64),"concrete")

    d.box("utility exhaust roof flashing",(-5.50,3.645,26.55),(.65,.03,.65),"galvanized")
    d.cylinder("utility exhaust stack",(-5.50,4.435,26.55),.15,1.65,"galvanized",vertices=16)
    d.proxy("utility exhaust stack",(-5.50,4.435,26.55),(.30,1.65,.30))
    for z in (26.40,26.70):
        d.beam("exhaust cap stand-off",(-5.50,5.25,z),(-5.50,5.39,z),.016,"steel",8)
    d.cylinder("utility exhaust rain cap",(-5.50,5.415,26.55),.28,.06,"steel",vertices=16)
    # A short strapped mast with a Yagi aerial and supported cable run.
    d.box("utility aerial plinth",(-6.8,3.69,27.),(.56,.18,.56),"concrete")
    d.cylinder("utility aerial mast",(-6.8,5.14,27.),.028,2.85,"galvanized",vertices=10)
    for y in (3.90,4.24):
        d.box("aerial mast clamp",(-6.8,y,27.),(.12,.055,.12),"steel")
        d.beam("aerial mounting brace",(-6.8,y,27.),(-7.66,3.95,27.),.024,"galvanized",8)
    d.beam("aerial boom",(-6.8,6.40,26.4),(-6.8,6.40,27.6),.020,"galvanized",8)
    for z,width in ((26.42,1.12),(26.80,.90),(27.15,.75),(27.55,.58)):
        d.beam("aerial cross element",(-6.8-width/2,6.40,z),(-6.8+width/2,6.40,z),.012,"galvanized",6)
    d.beam("aerial feeder",(-6.755,3.72,27.),(-6.755,6.38,27.),.012,"dark",6)
    d.beam("aerial roof cable",(-6.755,3.67,27.),(-6.755,3.67,22.2),.012,"dark",6)
    for z in (23.,24.5,26.):
        d.box("aerial cable saddle",(-6.755,3.65,z),(.11,.040,.045),"galvanized")
    d.features.append({"kind":"roof_equipment",
        "label":"Utility roof parapets, twin-fan plant, ductwork, access pads, exhaust and strapped aerial",
        "position":[-3.,3.6,25.],"roofBounds":[-8.,21.5,2.,28.5],
        "condenserCenter":[-1.2,4.21,25.1],"serviceRoofPlayable":False})


def _roofline_vents(d):
    # Restrained silhouettes on otherwise blank visible rooflines. Workshop
    # already has two roof ventilators, so do not duplicate those installations.
    for x in (12.,21.):
        d.box("dispatch roof vent flashing",(x,6.551,25.4),(.64,.05,.64),"steel")
        d.cylinder("dispatch roof vent",(x,6.86,25.4),.16,.60,"galvanized",vertices=12)
        d.cylinder("dispatch vent rain cap",(x,7.185,25.4),.27,.07,"steel",vertices=12)
        d.proxy("dispatch roof vent",(x,6.86,25.4),(.32,.60,.32))
    for x in (-34.,-17.):
        d.box("background roof vent curb",(x,10.14,44.),(.90,.28,.90),"steel")
        d.cylinder("background roof vent",(x,10.67,44.),.25,.85,"galvanized",vertices=12)
        d.cylinder("background vent cap",(x,11.12,44.),.43,.10,"steel",vertices=12)
    d.features.append({"kind":"roof_equipment","label":"Dispatch and background ventilation silhouettes",
                       "position":[12.,6.5,25.4]})


def build(d, layout):
    d.roof_probes = {"walkRoutes":[],"standingPoints":[],"shotProbes":[],"blockedMoves":[]}
    _landing(d)
    _utility_roof(d)
    _roofline_vents(d)
