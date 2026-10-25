"""Author Last Shift's original textured FPS depot and export an AC track.

AC coordinates throughout: X east, Y up, Z north, metres. Blender is only the
authoring/export host. No imported map geometry, paid generation or live install.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import sys
from types import SimpleNamespace

import bpy
import numpy as np
from mathutils import Vector

VISUALS = []
COLLIDERS = []
MARKERS = []
ROOFS = []
VOLUMES = []
SEMANTICS = []
MATS = {}
TEXTURE_ASSETS = {}
TEXTURE_ROOT = Path(__file__).resolve().parents[1]/"maps"/"last_shift"/"textures"
TEXTURE_CACHE = None
DETAILS = None
SIGN_REQUESTS = []
PHOTO_MATERIALS = {
    "asphalt": ("worn_asphalt", .08, 12),
    "patch": ("worn_asphalt", .06, 10),
    "concrete": ("concrete_floor_worn_02", .08, 12),
    "brick": ("brick_wall_02", .06, 10),
    "roof": ("corrugated_iron_03", .3, 28),
    "fleet": ("green_metal_rust", .25, 32),
    "steel": ("rusty_metal_02", .22, 24),
}


def xyz(p):
    return p[0], -p[2], p[1]


def reset():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat)


def material(name, color, roughness=0.8):
    mat = bpy.data.materials.new("LS_" + name)
    mat.use_nodes = True
    mat.diffuse_color = (*color, 1)
    mat.roughness = roughness
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Roughness"].default_value = roughness
    if name in PHOTO_MATERIALS:
        asset_id, specular, exponent = PHOTO_MATERIALS[name]
        asset = TEXTURE_ASSETS[asset_id]
        mat["tile_metres"] = asset["tileSizeMeters"]
        mat["ac_shader"] = "ksPerPixelNM"
        mat["ac_specular"] = specular
        mat["ac_specular_exp"] = exponent

        def image_node(channel, slot, blender_only=False):
            source = asset["files"][channel]
            path = TEXTURE_ROOT/source["path"]
            if hashlib.sha256(path.read_bytes()).hexdigest() != source["sha256"]:
                raise ValueError(f"Source texture changed: {path}")
            prefix = "__" if blender_only else ""
            image_name = f"{prefix}LS_{asset_id}_{channel}.png"
            img = bpy.data.images.get(image_name)
            if img is None:
                img = bpy.data.images.load(str(path),check_existing=False)
                if not blender_only and path.suffix.lower() != ".png":
                    # Packing a loaded JPEG retains its original bytes even if
                    # renamed .png. Encode and reload the actual export format.
                    cached = TEXTURE_CACHE/image_name
                    _ = img.pixels[0]  # Decode before changing the lazy source path.
                    img.file_format = "PNG"
                    img.filepath_raw = str(cached)
                    img.save()
                    bpy.data.images.remove(img)
                    img = bpy.data.images.load(str(cached),check_existing=False)
                img.name = image_name
                img.colorspace_settings.name = "sRGB" if channel == "diffuse" else "Non-Color"
                img.pack()
            node = mat.node_tree.nodes.new("ShaderNodeTexImage")
            node.name = prefix+mat.name+"_"+channel
            node.image = img
            if slot:
                node.assettoCorsa.shaderInputName = slot
            return node

        diffuse = image_node("diffuse","txDiffuse")
        mat.node_tree.links.new(diffuse.outputs["Color"],shader.inputs["Base Color"])
        # Installed AC shader uses B=T cross N; its tangent normal is DirectX.
        # Blender uses the original OpenGL variant. No source pixels are edited.
        image_node("normalDX","txNormal")
        normal_gl = image_node("normalGL",None,blender_only=True)
        normal = mat.node_tree.nodes.new("ShaderNodeNormalMap")
        mat.node_tree.links.new(normal_gl.outputs["Color"],normal.inputs["Color"])
        mat.node_tree.links.new(normal.outputs["Normal"],shader.inputs["Normal"])
        rough = image_node("roughness",None,blender_only=True)
        mat.node_tree.links.new(rough.outputs["Color"],shader.inputs["Roughness"])
        mat.node_tree.nodes.active = diffuse
    else:
        # Uniform finishes are intentional for sign lettering, painted markings,
        # opaque glass and rubber. Building/road/metal surfaces use photographs.
        mat["tile_metres"] = (1.0,1.0)
        mat["ac_shader"] = "ksPerPixel"
        mat["ac_specular"] = .55 if name == "glass" else .12
        mat["ac_specular_exp"] = 90 if name == "glass" else 22
        img = bpy.data.images.new(mat.name+".png",width=2,height=2,alpha=True)
        img.pixels = list((*color,1))*4
        img.file_format = "PNG"
        img.pack()
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.name = mat.name+"_diffuse"
        tex.image = img
        tex.assettoCorsa.shaderInputName = "txDiffuse"
        mat.node_tree.nodes.active = tex
        mat.node_tree.links.new(tex.outputs["Color"],shader.inputs["Base Color"])
    MATS[name] = mat
    return mat


def uv_world(obj):
    uv = obj.data.uv_layers.active or obj.data.uv_layers.new()
    tile = obj.data.materials[0].get("tile_metres",(2.0,2.0))
    phase = obj.rotation_euler.to_matrix().inverted() @ obj.location
    for poly in obj.data.polygons:
        axis = max(range(3), key=lambda i: abs(poly.normal[i]))
        axes = [i for i in range(3) if i != axis]
        # KN5 has no tangent handedness field. Keep every planar chart positive.
        u_sign = (1 if poly.normal[axis]>=0 else -1)*(1 if axis in (0,2) else -1)
        for idx in poly.loop_indices:
            vertex = obj.data.vertices[obj.data.loops[idx].vertex_index].co+phase
            uv.data[idx].uv = (u_sign*vertex[axes[0]]/tile[0],vertex[axes[1]]/tile[1])


def cube_mesh(name, center, size, mat, yaw=0):
    bpy.ops.mesh.primitive_cube_add(location=xyz(center))
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = (size[0], size[2], size[1])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_euler.z = math.radians(yaw)
    obj.data.materials.append(mat)
    uv_world(obj)
    return obj


def box(label, center, size, mat="concrete", solid=True, yaw=0, roof=False):
    obj = cube_mesh(f"LS_VIS_{len(VISUALS):04d}", center, size, MATS[mat], yaw)
    VISUALS.append(obj)
    SEMANTICS.append({"node": obj.name, "label": label, "center": center,
                      "size": size, "yaw": yaw, "solid": solid})
    if roof:
        ROOFS.append(obj)
    if solid:
        proxy(label, center, size, yaw)
    return obj


def proxy(label, center, size, yaw=0):
    obj = cube_mesh(f"1WALL_LS_{len(COLLIDERS):04d}", center, size, MATS["collision"], yaw)
    obj.hide_render = True
    obj.hide_set(True)
    obj["purpose"] = label
    COLLIDERS.append(obj)
    return obj


def marker(name, position, yaw=0):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.location = xyz(position)
    obj.rotation_euler.z = math.radians(yaw)
    MARKERS.append(obj)
    return obj


def movement_box(center, size, yaw=0):
    index = len(VOLUMES)
    theta = math.radians(yaw)
    dx = (math.cos(theta) * size[0] / 2, 0, -math.sin(theta) * size[0] / 2)
    dz = (math.sin(theta) * size[2] / 2, 0, math.cos(theta) * size[2] / 2)
    marker(f"FPS_MOVEBOX_{index}_CENTER", center)
    for suffix, delta in (("X", dx), ("Y", (0, size[1] / 2, 0)), ("Z", dz)):
        marker(f"FPS_MOVEBOX_{index}_{suffix}", tuple(a + b for a, b in zip(center, delta)))
    VOLUMES.append({"center": center, "size": size, "yaw": yaw})


def roof(label, x0, x1, z0, z1, y):
    box(label, ((x0+x1)/2, y, (z0+z1)/2), (x1-x0, .25, z1-z0), "roof", roof=True)
    movement_box(((x0+x1)/2, (y+25)/2, (z0+z1)/2), (x1-x0, 25-y, z1-z0))


def wall(label, axis, constant, start, end, height, openings=(), base=0, mat="concrete"):
    """Wall segments with real holes, including lintels above doors/windows."""
    cursor = start
    for a, b, sill, top in sorted(openings):
        if a > cursor:
            wall_piece(label, axis, constant, cursor, a, base, height, mat)
        if sill > base:
            wall_piece(label, axis, constant, a, b, base, sill-base, mat)
        if top < base+height:
            wall_piece(label, axis, constant, a, b, top, base+height-top, mat)
        cursor = b
    if end > cursor:
        wall_piece(label, axis, constant, cursor, end, base, height, mat)


def wall_piece(label, axis, constant, start, end, base, height, mat):
    center = ((start+end)/2, base+height/2, constant) if axis == "x" else (constant, base+height/2, (start+end)/2)
    size = (end-start, height, .3) if axis == "x" else (.3, height, end-start)
    box(label, center, size, mat)


def sign(text, location, size=0.65, yaw=0):
    # Bake all lettering into the shared sign atlas once the base scene exists.
    SIGN_REQUESTS.append((text,location,size,yaw))


def stairs(label, x0, x1, z0, z1, height):
    count = 16
    for i in range(count):
        a = z0 + (z1-z0)*i/count
        b = z0 + (z1-z0)*(i+1)/count
        h = height*(i+1)/count
        box(label+" tread", ((x0+x1)/2, h/2, (a+b)/2), (x1-x0, h, b-a), "concrete", solid=False)
    # A continuous ramp is the collision surface, avoiding step-edge snagging.
    verts = [(x0,0,z0),(x1,0,z0),(x0,height,z1),(x1,height,z1),(x0,0,z1),(x1,0,z1)]
    faces = [(0,1,3,2),(2,3,5,4),(0,2,4),(1,5,3),(0,4,5,1)]
    mesh = bpy.data.meshes.new(label+"_ramp")
    mesh.from_pydata([xyz(p) for p in verts], [], [tuple(reversed(f)) for f in faces])
    mesh.update()
    obj = bpy.data.objects.new(f"1ROAD_LS_{len(COLLIDERS):04d}", mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(MATS["collision"])
    uv_world(obj)
    obj.hide_render = True
    obj.hide_set(True)
    COLLIDERS.append(obj)


def build(layout):
    for name, rgb in {
        "asphalt": (.16,.17,.18), "concrete": (.47,.45,.40),
        "brick": (.37,.24,.19), "fleet": (.12,.29,.27),
        "roof": (.24,.26,.27), "steel": (.19,.22,.23),
        "paint": (.76,.74,.64), "yellow": (.65,.46,.13),
        "dark": (.055,.065,.069), "glass": (.075,.13,.15),
        "patch": (.12,.13,.135),
        "collision": (.3,.3,.3),
        "galvanized": (.35,.38,.37), "sign_blue": (.025,.09,.15),
        "safety_red": (.52,.035,.022), "lamp_lens": (.92,.79,.49),
    }.items():
        material(name, rgb)
    ground = box("depot asphalt", (0,-.2,0), (104,.4,84), "asphalt", solid=False)
    ground.name = "LS_GROUND_ASPHALT"
    SEMANTICS[-1]["node"] = ground.name
    # A full hidden ground proxy keeps navigation independent of visual inlays.
    proxy("depot ground support",(0,-.2,0),(104,.4,84)).name = "1ROAD_LS_DEPOT"
    # Bounds are slightly inside physical enclosure, so outside scenery is never navigable.
    for i,p in enumerate(((-44.6,0,-34.6),(44.6,0,-34.6),(44.6,0,34.6),(-44.6,0,34.6))):
        marker(f"FPS_BOUNDARY_{i}",p)
    for x in (-45,45):
        box("perimeter wall", (x,1.5,0), (.4,3,70), "concrete")
    box("north perimeter wall",(0,1.5,35),(90,3,.4),"concrete")
    # Closed vehicle gate keeps the arena boundary sealed while explaining access.
    proxy("south perimeter and closed depot gate",(0,1.5,-35),(90,3,.4))
    box("south perimeter west",(-16.5,1.5,-35),(57,3,.4),"concrete",solid=False)
    box("south perimeter east",(35,1.5,-35),(20,3,.4),"concrete",solid=False)
    for x in range(-42,44,7):
        if not 12 < x < 25:
            box("perimeter pilaster", (x,1.6,-34.8), (.55,3.2,.6))
        box("perimeter pilaster", (x,1.6,34.8), (.55,3.2,.6))
    # West workshop. Two large east entrances plus north and south exits.
    wall("workshop west", "z", -31, -16, 18, 6)
    wall("workshop east", "z", -9, -16, 18, 6, [(-12,-6,0,4.5),(8,14,0,4.5)])
    wall("workshop south", "x", -16, -31, -9, 6, [(-24,-19,0,4)])
    wall("workshop north", "x", 18, -31, -9, 6, [(-21,-17,0,3)])
    wall("workshop partition", "x", 1, -31, -9, 3, [(-26,-22,0,3),(-15,-11,0,3)], mat="brick")
    roof("workshop roof", -31.5,-8.5,-16.5,18.5,6.2)
    for z in (-13,-5,6,16):
        box("workshop column", (-29,2.7,z), (.6,5.4,.6), "steel")
    box("workbench south", (-28,.5,-8), (2,1,5), "steel")
    box("equipment island north", (-20,1.05,11), (3,2.1,3), "fleet")
    box("parts cage", (-28,1.2,9), (3,2.4,6), "steel")
    box("repair lift", (-18,.09,-7), (2.4,.18,5.5), "yellow", solid=False)
    sign("MAINTENANCE", (-20.5,4.7,-16.17), .85)
    sign("01", (-8.8,4.95,-9), .85, 90)
    sign("02", (-8.8,4.95,11), .85, 90)
    # Dispatch: upper room at north, open stairwell at south. Two stair approaches.
    wall("dispatch west", "z", 8, 13,28,6.2, [(14,16.5,0,2.6),(24,27,3,5.5)], mat="brick")
    wall("dispatch east", "z", 24,13,28,6.2, mat="brick")
    wall("dispatch north", "x", 28,8,24,6.2, mat="brick")
    wall("dispatch south", "x", 13,8,24,6.2, [(11,14,0,2.6),(15,21,3.9,5.4)], mat="brick")
    box("dispatch upper slab", (16,2.9,25), (16,.2,6))
    stairs("internal dispatch stairs",20,23,14,22,3)
    stairs("external dispatch stairs",4.6,7.1,17,25,3)
    box("external landing", (6.2,2.9,26.5), (3.6,.2,3))
    wall("upper landing parapet", "x",22,8.3,19.9,1.05,base=3)
    wall("external landing parapet", "z",4.5,25,28,1.05,base=3)
    box("dispatch desk", (11,.45,25.5), (3,.9,1.4), "fleet")
    box("upper dispatch desk", (16,3.45,26), (3,.9,1.2), "fleet")
    roof("dispatch roof",7.7,24.3,12.7,28.3,6.4)
    sign("DISPATCH", (16,2.95,12.82), .6)
    # East covered wash lane with a west-side crossover.
    wall("wash east screen", "z",40,-7,7,4.5,mat="fleet")
    wall("wash west screen", "z",30,-7,7,4.5,[(1,5,0,3)],mat="fleet")
    roof("wash canopy",29.7,40.3,-7.3,7.3,4.65)
    for x in (30,40):
        for z in (-7,7):
            box("wash upright",(x,2.2,z),(.4,4.4,.4),"steel")
    box("wash equipment",(38.8,1,-1),(1.5,2,3),"steel")
    # The hanging WASH board and functional cover structures are authored below.
    box("north utility shed",(-3,1.8,25),(10,3.6,7),"steel")
    movement_box((-3,14.3,25),(10,21.4,7))
    # Surface markings and maintenance patches remain non-solid.
    for x in (-4,8,25):
        for z in range(-28,14,5):
            box("yard dashed line",(x,.0025,z),(.13,.004,2.4),"yellow",solid=False)
    for x,z,w,d in ((-3,-21,5,3),(12,-10,4,2),(-35,-8,3,7),(28,11,5,4)):
        box("asphalt repair",(x,.002,z),(w,.003,d),"patch",solid=False)
    for z in (-12,12):
        box("wash drain",(35,.011,z),(8,.02,.18),"dark",solid=False)
    # Unreachable context buildings beyond the authored boundary.
    for x,z,w,d,h in ((-24,46,34,12,10),(22,49,25,18,14),(-60,3,19,40,11),(61,-17,18,26,8)):
        box("background massing",(x,h/2,z),(w,h,d),"brick",solid=False)
    carrier = proxy("remote carrier pad", (219,-.25,0), (48,.5,24))
    carrier.name = "1ROAD_LS_CARRIER"
    for index,(x,y,z,yaw) in enumerate(layout["spawns"]):
        marker(f"AC_START_{index}",(x,y,z),yaw)
        pit = (201+(index%6)*6,.15,-6+(index//6)*12)
        marker(f"AC_PIT_{index}",pit)
        marker(f"AC_CREW_{index}",(pit[0]+1.8,pit[1],pit[2]))
    marker("AC_HOTLAP_START_0",(219,.15,0))
    for index,z in enumerate((-9,9)):
        marker(f"AC_TIME_{index}_L",(198,.1,z))
        marker(f"AC_TIME_{index}_R",(240,.1,z))


def export(path, keep, exporter, collision=False):
    bpy.context.view_layer.update()
    keep_ids = {o.as_pointer() for o in keep}
    renamed = []
    renamed_materials = []
    renamed_textures = []
    for obj in bpy.data.objects:
        if obj.as_pointer() not in keep_ids and not obj.name.startswith("__"):
            renamed.append((obj,obj.name))
            obj.name = "__"+obj.name
    for mat in bpy.data.materials:
        excluded = (mat == MATS["collision"]) != collision
        if excluded:
            renamed_materials.append((mat,mat.name))
            mat.name = "__"+mat.name
            for node in mat.node_tree.nodes:
                if node.type == "TEX_IMAGE" and not node.name.startswith("__"):
                    renamed_textures.append((node,node.name))
                    node.name = "__"+node.name
    settings = {
        "nodes": {"*": {"lodIn":0,"lodOut":1500,"visible":True,
                           "renderable":not collision,"castShadows":not collision}},
        "materials": {mat.name:{"shaderName":mat["ac_shader"],"properties":{
            "ksAmbient":{"valueA":.35},"ksDiffuse":{"valueA":.65},
            "ksSpecular":{"valueA":mat["ac_specular"]},
            "ksSpecularEXP":{"valueA":mat["ac_specular_exp"]},
            "nmObjectSpace":{"valueA":0}}}
            for mat in MATS.values() if not mat.name.startswith("__")},
    }
    try:
        warnings = []
        with path.open("wb") as output:
            exporter.KN5FileWriter(output,bpy.context,settings,warnings,
                                  root_node_name="LAST_SHIFT",forward_axis="-Y",
                                  transform_tangent_space=not collision).write()
        # FPS markers are valid custom metadata; the general AC exporter warns
        # about unknown logical nodes. Keep unexpected diagnostics visible.
        unexpected = [w for w in warnings if "'FPS_BOUNDARY_" not in w and "'FPS_MOVEBOX_" not in w]
        for w in unexpected:
            print("EXPORT WARNING:",w)
        print(f"Exported {path.name}: {path.stat().st_size} bytes; {len(unexpected)} unexpected warnings")
    finally:
        for obj,name in renamed:
            obj.name = name
        for mat,name in renamed_materials:
            mat.name = name
        for node,name in renamed_textures:
            node.name = name


def write_support(track, layout):
    (track/"data").mkdir(exist_ok=True)
    (track/"ui").mkdir(exist_ok=True)
    (track/"extension").mkdir(exist_ok=True)
    (track/"models.ini").write_text("[MODEL_0]\nFILE=last_shift.kn5\nPOSITION=0,0,0\nROTATION=0,0,0\n\n[MODEL_1]\nFILE=last_shift_collision.kn5\nPOSITION=0,0,0\nROTATION=0,0,0\n",encoding="utf-8")
    (track/"data"/"surfaces.ini").write_text("[SURFACE_0]\nKEY=ROAD\nFRICTION=1\nDAMPING=0\nWAV=\nWAV_PITCH=1\nFF_EFFECT=NULL\nDIRT_ADDITIVE=0\nIS_VALID_TRACK=1\nBLACK_FLAG_TIME=0\nSIN_HEIGHT=0\nSIN_LENGTH=0\nIS_PITLANE=0\nVIBRATION_GAIN=0\nVIBRATION_LENGTH=0\n\n[SURFACE_1]\nKEY=WALL\nFRICTION=0.8\nDAMPING=0\nWAV=\nWAV_PITCH=1\nFF_EFFECT=NULL\nDIRT_ADDITIVE=0\nIS_VALID_TRACK=0\nBLACK_FLAG_TIME=0\nSIN_HEIGHT=0\nSIN_LENGTH=0\nIS_PITLANE=0\nVIBRATION_GAIN=0\nVIBRATION_LENGTH=0\n",encoding="utf-8")
    (track/"data"/"lighting.ini").write_text("[LIGHTING]\nSUN_PITCH_ANGLE=55\nSUN_HEADING_ANGLE=-35\n",encoding="utf-8")
    (track/"data"/"crew.ini").write_text("[HEADER]\nSIDE=1\n",encoding="utf-8")
    (track/"data"/"map.ini").write_text("[PARAMETERS]\nWIDTH=1200\nHEIGHT=1200\nMARGIN=0\nSCALE_FACTOR=0.09\nX_OFFSET=54\nZ_OFFSET=54\nDRAWING_SIZE=10\n",encoding="utf-8")
    config = f"[ABOUT]\nAUTHOR=Last Shift project\nVERSION={layout['version']}\nDESCRIPTION=Detailed municipal bus depot\n\n[INCLUDE]\nINCLUDE=common/conditions.ini\n\n[GRASS_FX]\nENABLED=0\n"
    for i,light in enumerate(DETAILS.lights):
        config += (f"\n[LIGHT_{i}]\nDESCRIPTION={light['label']}\n"
                   f"POSITION={','.join(str(v) for v in light['position'])}\nDIRECTION=0,-1,0\n"
                   f"COLOR=1,0.82,0.59,{18 if light['nightOnly'] else 7}\n"
                   f"RANGE={light['range']}\nSPOT=150\nSPOT_SHARPNESS=0.35\n"
                   "RANGE_GRADIENT_OFFSET=0.25\nFADE_AT=120\nFADE_SMOOTH=25\nSHADOWS=0\n")
        if light['nightOnly']:
            config += "CONDITION=NIGHT_SMOOTH\n"
    config += "\n[MATERIAL_ADJUSTMENT_0]\nMATERIALS=LS_lamp_lens\nKEY_0=ksEmissive\nVALUE_0=8,6,3\nCONDITION=NIGHT_SMOOTH\n"
    (track/"extension"/"ext_config.ini").write_text(config,encoding="utf-8")
    ui = {"name":"Last Shift","description":layout["description"],
          "tags":["FPS","original","industrial","depot"],"geotags":["46.0","14.5"],
          "country":"Fictional","city":"Fictional Central European depot", "length":"90 x 70 m",
          "width":"varies","pitboxes":str(len(layout["spawns"])),"run":"FPS arena",
          "author":"Last Shift project","version":layout["version"]}
    (track/"ui"/"ui_track.json").write_text(json.dumps(ui,indent=2)+"\n",encoding="utf-8")
    (track/"README.txt").write_text(
        f"LAST SHIFT {layout['version']}\n\n"
        "Original municipal bus depot for Race Control FPS mode.\n"
        "Install this last_shift folder under Assetto Corsa/content/tracks/.\n"
        "Select Last Shift in Race Control, prepare it as an FPS arena, then start FPS mode.\n"
        "Requires the current FPS server with preparation version 5 / movement-volume support.\n"
        "12 carrier slots. Designed for 8-12 participants.\n\n"
        "The reworked depot includes enterable buses with open windows, maintenance bays,\n"
        "dispatch rooms and stairs, a wash lane, service buildings and perimeter street furniture.\n"
        "The buildings and equipment follow their depot functions, with routes through the site.\n"
        "Photographic diffuse and normal maps cover asphalt, concrete, brick, roofs, fleet paint\n"
        "and equipment. Bus wheels use original generated imagery. Depot lettering uses exact\n"
        "bitmap text in one shared 2048-pixel atlas, with one flat face per sign.\n\n"
        "Sign boards have no duplicate front face. Floor finishes and markings use\n"
        "adjacent inlaid surfaces. The stair landing is guarded and the utility roof\n"
        "has parapets, a condenser, ductwork, an exhaust and an aerial.\n\n"
        "No racing AI spline is supplied; this map is for FPS mode.\n"
        "Geometry is original project-authored content. Photographic materials are CC0 from\n"
        "Poly Haven. Generated imagery and sign-atlas provenance are recorded separately in\n"
        "texture-sources.json and TEXTURE_CREDITS.txt. Human visual and gameplay testing is separate\n"
        "from the build's automated geometry and package checks.\n",encoding="utf-8")
    sources = json.loads((TEXTURE_ROOT/"manifest.json").read_text(encoding="utf-8"))
    wheel = json.loads((TEXTURE_ROOT/"generated"/"wheel-sidewall.provenance.json").read_text(encoding="utf-8"))
    wheel.pop("generationOutput",None)
    wheel.update(id="bus_wheel_sidewall",embeddedTexture="LS_wheel_sidewall.png")
    atlas = json.loads((TEXTURE_CACHE/"LS_SIGN_ATLAS.json").read_text(encoding="utf-8"))
    atlas.pop("fontPath",None)
    atlas.update(id="depot_sign_atlas",embeddedTexture="LS_SIGN_ATLAS.png",
                 fontDistribution="Rasterized glyphs only; no font file is included")
    sources["licenseScope"] = "The provider and CC0 metadata apply to assets; generatedAssets records separate original content."
    sources["generatedAssets"] = [wheel,atlas]
    (track/"texture-sources.json").write_text(json.dumps(sources,indent=2)+"\n",encoding="utf-8")
    credits = ("LAST SHIFT - PACKAGED TEXTURE CREDITS\n\n"
               "The Poly Haven section applies to the six downloaded materials in texture-sources.json\n"
               "under assets. Original generated imagery and raster signage are listed separately below.\n\n"
               +(TEXTURE_ROOT/"SOURCES.txt").read_text(encoding="utf-8")
               +"\nORIGINAL WHEEL IMAGERY\n"
               +f"{wheel['asset']} - {wheel['tool']}, generated {wheel['generatedDate']}.\n"
               +f"Dimensions: {wheel['dimensions'][0]} x {wheel['dimensions'][1]}; SHA-256: {wheel['sha256']}\n"
               +"Original generated pixels are unchanged. The full generation prompt is in\n"
               +"texture-sources.json under generatedAssets; this image is not a Poly Haven asset.\n\n"
               +"ORIGINAL DEPOT SIGN ATLAS\n"
               +f"{atlas['atlas']} - exact project-authored labels rasterized with {atlas['rasterizer']}.\n"
               +f"Dimensions: {atlas['dimensions'][0]} x {atlas['dimensions'][1]}; SHA-256: {atlas['sha256']}\n"
               +f"Font: {atlas['font']}. Rasterized glyphs only; no font file is distributed.\n"
               +"Per-sign text, atlas rectangles and source geometry are recorded in texture-sources.json.\n")
    (track/"TEXTURE_CREDITS.txt").write_text(credits,encoding="utf-8")


def write_minimap(track, layout):
    # Explicit shared raster transform: pixel=(world+54)/.09, Z down the image.
    count = 1200
    axis = (np.arange(count)+.5)*.09-54
    xx, zz = np.meshgrid(axis,axis)
    pixels = np.zeros((count,count,4),dtype=np.float32)
    inside = (np.abs(xx)<44.6)&(np.abs(zz)<34.6)
    pixels[inside] = (.72,.72,.72,1)
    def rectangle(x,z,w,d,yaw):
        theta = math.radians(yaw)
        dx,dz = xx-x,zz-z
        lx = dx*math.cos(theta)-dz*math.sin(theta)
        lz = dx*math.sin(theta)+dz*math.cos(theta)
        return (np.abs(lx)<w/2)&(np.abs(lz)<d/2)&inside
    for item in SEMANTICS:
        x,y,z = item["center"]
        w,h,d = item["size"]
        if item["solid"] and y-h/2<1.8 and y+h/2>.5:
            pixels[rectangle(x,z,w,d,item["yaw"])] = (.17,.17,.17,1)
    for item in layout["buses"]:
        pixels[rectangle(item["x"],item["z"],2.7,11.6,item["yaw"])] = (.17,.17,.17,1)
    img = bpy.data.images.new("__last_shift_minimap",width=count,height=count,alpha=True)
    img.pixels.foreach_set(np.flipud(pixels).copy().ravel())
    img.file_format = "PNG"
    img.filepath_raw = str(track/"map.png")
    img.save()
    (track/"ui"/"outline.png").write_bytes((track/"map.png").read_bytes())
    bpy.data.images.remove(img)


def probes(layout):
    result = {"trackId":layout["trackId"],"expectedSpawnCount":len(layout["spawns"]),
        "walkRoutes":[
            {"name":"workshop south door","points":[[-21.5,0,-19],[-21.5,0,-13]],"bidirectional":True},
            {"name":"workshop bay 01","points":[[-5,0,-9],[-12,0,-9]],"bidirectional":True},
            {"name":"workshop bay 02","points":[[-5,0,11],[-12,0,11]],"bidirectional":True},
            {"name":"workshop internal crossing","points":[[-24,0,-3],[-24,0,5]],"bidirectional":True},
            {"name":"dispatch front door","points":[[12.5,0,10],[12.5,0,17]],"bidirectional":True},
            {"name":"dispatch west door","points":[[5,0,15.25],[11,0,15.25]],"bidirectional":True},
            {"name":"internal dispatch stairs","points":[[21.5,0,13.7],[21.5,0,14],[21.5,3,22],[21.5,3,25]],"bidirectional":True},
            {"name":"external dispatch stairs","points":[[5.85,0,16],[5.85,0,17],[5.85,3,25],[5.85,3,26],[9.5,3,26]],"bidirectional":True},
            {"name":"wash lane","points":[[34,0,-12],[34,0,12]],"bidirectional":True},
            {"name":"wash crossover","points":[[27,0,3],[34,0,3]],"bidirectional":True},
            {"name":"west flank","points":[[-40,0,-19],[-40,0,17]],"bidirectional":True}
        ],
        "shotProbes":[
            {"name":"workshop wall blocks bullets","from":[-34,1.5,-3],"to":[-28,1.5,-3],"blocked":True},
            {"name":"bay opening remains clear","from":[-12,1.5,-9],"to":[-6,1.5,-9],"blocked":False},
            {"name":"wash crossover remains clear","from":[27,1.5,3],"to":[34,1.5,3],"blocked":False},
            {"name":"south spawn protection","from":[-34,1.5,-24],"to":[-34,1.5,-15],"blocked":True}
        ],
        "standingPoints":[
            {"name":"upper dispatch landing","position":[21.5,3,25]},
            {"name":"external landing","position":[5.85,3,26]},
            {"name":"workshop south bay","position":[-21,0,-6]},
            {"name":"wash lane","position":[34,0,0]}
        ]}
    extra = Path(__file__).resolve().parents[1]/"maps"/"last_shift"/"detail-probes.json"
    if extra.exists():
        result["walkRoutes"].extend(json.loads(extra.read_text(encoding="utf-8"))["walkRoutes"])
    for key,values in DETAILS.bus_probes.items():
        result[key].extend(values)
    for key,values in DETAILS.site_probes.items():
        result[key].extend(values)
    for key,values in DETAILS.roof_probes.items():
        result.setdefault(key,[]).extend(values)
    return result


def render(root, track):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 1440
    scene.render.resolution_y = 1000
    scene.render.resolution_percentage = 100
    scene.world.color = (.45,.45,.45)
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (.65,.72,.8,1)
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = .65
    bpy.ops.object.light_add(type="SUN",location=(0,0,40))
    sun = bpy.context.object
    sun.name = "__preview_sun"
    sun.rotation_euler = (math.radians(25),math.radians(-20),math.radians(25))
    sun.data.energy = 2
    sun.data.angle = math.radians(18)
    bpy.ops.object.camera_add()
    cam = bpy.context.object
    cam.name = "__preview_camera"
    scene.camera = cam
    scene.view_settings.view_transform = "AgX"
    def capture(name,position,target,ortho=None):
        cam.location = xyz(position)
        direction = Vector(xyz(target))-cam.location
        cam.rotation_euler = direction.to_track_quat("-Z","Y").to_euler()
        cam.data.type = "ORTHO" if ortho else "PERSP"
        cam.data.ortho_scale = ortho or 100
        cam.data.lens = 25
        scene.render.filepath = str(root/name)
        bpy.ops.render.render(write_still=True)
    capture("overview.png",(89,98,-101),(0,0,0),128)
    capture("yard.png",(-3,1.72,-27),(8,2,8))
    capture("workshop.png",(-17,1.72,-12),(-21,1.7,10))
    capture("dispatch.png",(2,1.72,6),(17,2.3,20))
    capture("staff-stop.png",(34,1.72,10),(24,1.5,15))
    capture("workbench.png",(-24,1.72,-12),(-28,1.25,-8))
    capture("bus-entry.png",(6.4,1.72,-1),(1.4,1.5,-5.5))
    capture("bus-interior.png",(1,2.05,-13.7),(1,1.65,-4.1))
    capture("bus-wheel.png",(4,1.10,-13.9),(2.46,.52,-12.7))
    capture("wash-sign.png",(35,1.72,-13),(35,3.5,-7.18))
    capture("parts-annex.png",(-27,1.72,-25),(-30,1.5,-17))
    capture("bay-signs.png",(6,1.72,1),(-9,3.4,1))
    capture("bay-signs-distant.png",(26,1.72,1),(-9,3.4,1))
    capture("landing-rooftop.png",(7,4.72,26.6),(-3,4.6,25))
    capture("landing-threshold.png",(6.8,4.1,26),(8.1,3,25.4))
    # A separate dusk render demonstrates fixture placement, not in-game lighting.
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = .07
    sun.data.energy = .04
    capture("yard-dusk.png",(-3,1.72,-27),(8,2,8))
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = .65
    sun.data.energy = 2
    for obj in ROOFS:
        obj.hide_render = True
    scene.render.resolution_x = scene.render.resolution_y = 1200
    capture("layout.png",(0,100,0),(0,0,0),108)
    for obj in ROOFS:
        obj.hide_render = False
    # The Content Manager preview shows the authored geometry, not concept art.
    (track/"ui"/"preview.png").write_bytes((root/"overview.png").read_bytes())


def main():
    global TEXTURE_CACHE, DETAILS
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-root",required=True)
    parser.add_argument("--layout",required=True)
    parser.add_argument("--exporter-root",required=True)
    parser.add_argument("--skip-renders",action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    root = Path(args.output_root).resolve()
    root.mkdir(parents=True,exist_ok=True)
    TEXTURE_CACHE = root/"texture-cache"
    TEXTURE_CACHE.mkdir(parents=True,exist_ok=True)
    track = root/"content"/"tracks"/"last_shift"
    track.mkdir(parents=True,exist_ok=True)
    sys.path.insert(0,str(Path(args.exporter_root).resolve().parent))
    import blender_assetto_corsa_tools as ac_tools
    from blender_assetto_corsa_tools import exporter
    ac_tools.register()
    layout = json.loads(Path(args.layout).read_text(encoding="utf-8"))
    if layout["trackId"] != "last_shift":
        raise ValueError("This authoring script builds only last_shift")
    source_manifest = json.loads((TEXTURE_ROOT/"manifest.json").read_text(encoding="utf-8"))
    TEXTURE_ASSETS.update({item["id"]:item for item in source_manifest["assets"]})
    reset()
    build(layout)
    sys.path.insert(0,str(Path(__file__).resolve().parent))
    from last_shift_detail_api import DetailAPI, architectural_details
    from last_shift_decals import DecalAtlas
    import last_shift_buses
    import last_shift_site_layout
    import last_shift_street_props
    import last_shift_workshop_props
    import last_shift_rooftops
    import last_shift_surfaces
    DETAILS = DetailAPI(SimpleNamespace(VISUALS=VISUALS, COLLIDERS=COLLIDERS, ROOFS=ROOFS, MATS=MATS, SEMANTICS=SEMANTICS,
                         box=box, proxy=proxy, xyz=xyz, uv_world=uv_world, movement_box=movement_box))
    DETAILS.decals = DecalAtlas(DETAILS.a,TEXTURE_CACHE)
    for text,location,size,yaw in SIGN_REQUESTS:
        center = (location[0],location[1]+size*.3,location[2])
        DETAILS.panel("building sign "+text,text,center,len(text)*size*.7,size*1.15,
                      {0:"south",90:"east",180:"north",270:"west"}[yaw])
    architectural_details(DETAILS,layout)
    last_shift_site_layout.build(DETAILS,layout)
    last_shift_buses.build(DETAILS,layout)
    last_shift_street_props.build(DETAILS)
    last_shift_workshop_props.build(DETAILS)
    last_shift_rooftops.build(DETAILS,layout)
    last_shift_surfaces.build(DETAILS)
    DETAILS.decals.finalize()
    DETAILS.batch()
    export(track/"last_shift.kn5",VISUALS+MARKERS,exporter)
    export(track/"last_shift_collision.kn5",COLLIDERS,exporter,collision=True)
    write_support(track,layout)
    write_minimap(track,layout)
    manifest = {**layout,"coordinates":"Assetto Corsa X east, Y up, Z north; metres",
                "visualMeshes":len(VISUALS),"collisionMeshes":len(COLLIDERS),
                "movementVolumes":VOLUMES,"geometry":SEMANTICS,"probes":probes(layout),
                "detailFeatures":DETAILS.features,"detailCollision":DETAILS.collision,
                "lights":DETAILS.lights,"signDecals":DETAILS.decals.records,
                "surfaceRepairs":DETAILS.surface_repairs,
                "wheelGeometry":DETAILS.wheel_geometry}
    (root/"last-shift-layout.json").write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")
    bpy.ops.wm.save_as_mainfile(filepath=str(root/"last_shift.blend"))
    if not args.skip_renders:
        render(root,track)
    files = {str(p.relative_to(track)).replace("\\", "/"): hashlib.sha256(p.read_bytes()).hexdigest()
             for p in sorted(track.rglob("*")) if p.is_file() and p.name != "conversion-report.json"}
    report = {"trackId":"last_shift","version":layout["version"],
              "stage":"surface-roof-fixes", "origin":"Original depot, enterable buses and props; CC0 surfaces, generated wheel art and raster sign atlas",
              "sourceLayoutSha256":hashlib.sha256(Path(args.layout).read_bytes()).hexdigest(),
              "authoringScriptSha256":hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
              "detailSourceSha256":{p.name:hashlib.sha256(p.read_bytes()).hexdigest()
                   for p in Path(__file__).parent.glob("last_shift_*props.py")} | {
                   name:hashlib.sha256((Path(__file__).parent/name).read_bytes()).hexdigest()
                   for name in ("last_shift_detail_api.py","last_shift_buses.py","last_shift_decals.py","last_shift_site_layout.py","last_shift_surfaces.py","last_shift_rooftops.py")},
              "detailFeatureCount":len(DETAILS.features),"lightCount":len(DETAILS.lights),
              "textureManifestSha256":hashlib.sha256((TEXTURE_ROOT/"manifest.json").read_bytes()).hexdigest(),
              "texturedMaterials":{key:value[0] for key,value in PHOTO_MATERIALS.items()},
              "normalConvention":{"AssettoCorsa":"DirectX; bitangent=T cross N","Blender":"OpenGL"},
              "blenderVersion":bpy.app.version_string,"visualMeshes":len(VISUALS),
              "collisionMeshes":len(COLLIDERS),"spawnCount":len(layout["spawns"]),
              "requiredFpsPreparationVersion":5,"files":files,
              "acceptance":"See separate offline and server-smoke receipts. Human AC/CSP playtest pending."}
    (track/"conversion-report.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
    print(json.dumps({"track":str(track),"visualMeshes":len(VISUALS),"collisionMeshes":len(COLLIDERS),"spawns":len(layout["spawns"])}))


if __name__ == "__main__":
    main()
