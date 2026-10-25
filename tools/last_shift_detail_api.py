"""Metric, original depot fittings shared by Last Shift's detail modules."""
import math
from collections import defaultdict

import bpy
from mathutils import Vector


class DetailAPI:
    def __init__(self, author):
        self.a = author
        self.features = []
        self.lights = []
        self.collision = []
        self.decals = None
        self.first_visual = len(author.VISUALS)

    def box(self, label, center, size, mat="steel", yaw=0, solid=False):
        return self.a.box(label, center, size, mat, solid=solid, yaw=yaw)

    def proxy(self, label, center, size, yaw=0):
        obj = self.a.proxy(label, center, size, yaw)
        record = {"node": obj.name,"label": label,"center": center,
                  "size": size,"yaw": yaw,"solid": True}
        self.a.SEMANTICS.append(record)
        self.collision.append(record)
        return obj

    def register(self, obj, label, mat):
        obj.name = f"LS_VIS_{len(self.a.VISUALS):04d}"
        obj["detail_label"] = label
        obj.data.materials.append(self.a.MATS[mat])
        self.a.uv_world(obj)
        self.a.VISUALS.append(obj)
        return obj

    def cylinder(self, label, center, radius, depth, mat="steel", axis="y", vertices=12):
        bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius,
                                            depth=depth, location=self.a.xyz(center))
        obj = bpy.context.object
        direction = Vector(self.a.xyz({"x": (1,0,0), "y": (0,1,0), "z": (0,0,1)}[axis]))
        obj.rotation_euler = direction.to_track_quat("Z", "Y").to_euler()
        return self.register(obj, label, mat)

    def beam(self, label, start, end, radius=.035, mat="steel", vertices=8):
        p, q = Vector(self.a.xyz(start)), Vector(self.a.xyz(end))
        bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius,
                                            depth=(q-p).length, location=(p+q)/2)
        obj = bpy.context.object
        obj.rotation_euler = (q-p).to_track_quat("Z", "Y").to_euler()
        return self.register(obj, label, mat)

    def panel(self, label, text, center, width, height, face="south", mat="sign_blue",
              text_mat="paint", text_size=None):
        return self.decals.panel(label,text,center,width,height,face,mat,text_mat,text_size)

    def light(self, label, position, power=450, radius=1.0, night_only=False):
        self.lights.append({"label":label,"position":list(position),"nightOnly":night_only,
                            "range":18 if night_only else 9,"power":power})
        bpy.ops.object.light_add(type="AREA",location=self.a.xyz(position))
        obj = bpy.context.object
        obj.name = "__fixture_"+str(len(self.lights))
        obj.data.energy = power
        obj.data.color = (1,.82,.59)
        obj.data.shape = "DISK"
        obj.data.size = radius
        obj["night_only"] = night_only
        # Light points down the Blender -Z axis by default.

    def lamp(self, label, x, z, height=6, face="south"):
        dx,dz = {"south":(0,-1),"north":(0,1),"east":(1,0),"west":(-1,0)}[face]
        self.cylinder(label+" concrete footing",(x,.12,z),.24,.24,"concrete")
        self.cylinder(label+" tapered base",(x,.7,z),.11,1.4,"steel")
        self.cylinder(label+" galvanized mast",(x,height/2,z),.07,height,"galvanized")
        self.beam(label+" arm",(x,height-.15,z),(x+dx*1.3,height,z+dz*1.3),.045,"galvanized")
        p = (x+dx*1.3,height,z+dz*1.3)
        self.box(label+" luminaire",p,(.38,.17,.75) if dx==0 else (.75,.17,.38),"steel")
        self.box(label+" lens",(p[0],height-.092,p[2]),(.29,.018,.59) if dx==0 else (.59,.018,.29),"lamp_lens")
        self.proxy(label+" mast",(x,height/2,z),(.18,height,.18))
        self.light(label,(p[0],height-.15,p[2]),650,1,night_only=True)
        self.features.append({"kind":"streetlamp","label":label,"position":[x,0,z]})

    def batch(self):
        """Join detail by material and 12 m cell, keeping culling and KN5 limits."""
        new = self.a.VISUALS[self.first_visual:]
        groups = defaultdict(list)
        preserved = []
        for obj in new:
            if obj.name.startswith(("LS_WHEEL_", "LS_DECAL_", "LS_SURFACE_")) or obj in self.a.ROOFS:
                preserved.append(obj)
                continue
            p = obj.location
            groups[(obj.data.materials[0].name,math.floor(p.x/12),math.floor(-p.y/12))].append(obj)
        self.a.VISUALS[self.first_visual:] = preserved
        remapped = {}
        for objects in groups.values():
            names = [obj.name for obj in objects]
            bpy.ops.object.select_all(action="DESELECT")
            for obj in objects:
                obj.select_set(True)
            bpy.context.view_layer.objects.active = objects[0]
            if len(objects) > 1:
                bpy.ops.object.join()
            joined = bpy.context.object
            joined.name = f"LS_DETAIL_{len(self.a.VISUALS):04d}"
            remapped.update({name:joined.name for name in names})
            # Joined mesh keeps its authored UV charts; do not remap text/props.
            self.a.VISUALS.append(joined)
        for record in self.a.SEMANTICS:
            record["node"] = remapped.get(record["node"],record["node"])


def architectural_details(d, layout):
    # Concrete working surfaces and a road outside the closed depot entrance.
    for label,x,z,w,depth in (("workshop floor",-20,1,21.65,33.65),
                              ("dispatch ground floor",16,20.5,15.65,14.65),
                              ("wash concrete pad",35,0,9.65,16)):
        d.box(label,(x,.003,z),(w,.004,depth),"concrete")
    for z in (-14,-5,4,13):
        d.box("concrete slab joint",(-20,.0055,z),(21.6,.001,.018),"dark")
    for x in (-27,-20,-13):
        d.box("concrete slab joint",(x,.0055,1),(.018,.001,33.6),"dark")
    d.box("external access road",(0,-.06,-44),(106,.10,12),"asphalt")
    d.box("external footpath",(0,.02,-37.3),(106,.04,3.5),"concrete")
    for x in range(-48,50,8):
        d.box("external road marking",(x,-.008,-44),(4,.003,.12),"paint")
    for x,w in ((-17,58),(37,24)):
        d.box("external pavement kerb",(x,.09,-39.1),(w,.18,.2),"concrete")
    for x in (12,25):
        d.box("gate pier",(x,1.65,-35),(.5,3.3,.7),"concrete")
    for x in (15.3,21.7):
        d.box("closed depot gate leaf",(x,1.42,-34.99),(6.2,2.76,.12),"fleet")
        for yy in (.14,1.45,2.82):
            d.box("gate horizontal rail",(x,yy,-34.86),(6.2,.09,.085),"galvanized")
        for dx in (-2.7,-1.8,-.9,0,.9,1.8,2.7):
            d.box("gate reinforcement",(x+dx,1.42,-34.9),(.055,2.76,.09),"steel")
        for dx in (-2.65,2.65):
            d.cylinder("gate roller",(x+dx,.13,-34.75),.12,.14,"dark",axis="z")
    d.box("gate ground track",(18.5,.018,-34.76),(13.5,.035,.1),"galvanized")
    d.panel("vehicle gate sign","DEPOT 04  /  VEHICLE ACCESS\nKEEP CLEAR",(18.5,2.07,-34.79),4.4,.65,"north")
    d.box("gate intercom",(25.3,1.35,-34.61),(.24,.37,.1),"galvanized")
    d.panel("gate intercom label","CALL",(25.3,1.38,-34.54),.2,.13,"north",text_size=.055)
    d.features.append({"kind":"entrance","label":"Closed service gate and access road","position":[18.5,0,-35]})
    # Utility-shed door seams and vent grille read as a service building.
    d.box("utility service door",(-3,1.35,21.46),(1.35,2.65,.035),"fleet")
    d.box("utility service door latch",(-3.47,1.32,21.41),(.035,.21,.035),"galvanized")
    d.box("utility ventilation grille",(.1,1.7,21.46),(1.2,.7,.04),"dark")
    for y in (1.45,1.58,1.71,1.84,1.97):
        d.box("utility grille louvre",(.1,y,21.40),(1.18,.04,.08),"galvanized")
    # Rainwater systems, roof fascias and plinths follow the existing footprints.
    for name,x0,x1,z0,z1,h in (("workshop",-31,-9,-16,18,6.2),
                               ("dispatch",8,24,13,28,6.4)):
        for z in (z0-.22,z1+.22):
            d.box(name+" roof fascia",((x0+x1)/2,h-.16,z),(x1-x0+.8,.3,.14),"galvanized")
            d.beam(name+" gutter",(x0,h-.36,z),(x1,h-.36,z),.085,"steel")
        for x,z in ((x0+.4,z0-.23),(x1-.4,z1+.23)):
            d.beam(name+" downpipe",(x,.25,z),(x,h-.35,z),.065,"galvanized")
            for y in (.55,2.3,4.7):
                d.box(name+" pipe bracket",(x,y,z),(.19,.06,.16),"dark")
        d.features.append({"kind":"architecture","label":name+" rainwater system","position":[x0,0,z0]})
    # Roller shutter tracks, rolled doors, warning jambs and inspection windows.
    for bay,zc in ((1,-9),(2,11)):
        for z in (zc-3.06,zc+3.06):
            d.box("bay shutter guide",(-8.79,2.25,z),(.17,4.5,.12),"galvanized")
            for y in (.4,.95,1.5):
                d.box("bay hazard jamb",(-8.68,y,z),(.035,.25,.14),"yellow")
        d.box("rolled shutter housing",(-8.68,4.42,zc),(.6,.38,6.4),"galvanized")
        for z in (zc-2.4,zc-1.2,zc,zc+1.2,zc+2.4):
            d.box("shutter ventilation fins",(-8.35,4.42,z),(.025,.24,.8),"dark")
        d.panel("bay operating notice","KEEP CLEAR\nBAY 0"+str(bay),(-8.74,2.7,zc+4.2),1.8,.7,"east")
    # Window surrounds and narrow mullions preserve the upper opening sightline.
    for x in (15,21):
        d.box("dispatch window jamb",(x,4.65,12.79),(.09,1.58,.12),"galvanized")
    for y in (3.87,5.43):
        d.box("dispatch window frame",(18,y,12.79),(6.16,.09,.12),"galvanized")
    for x in (17,19):
        d.box("dispatch window mullion",(x,4.65,12.79),(.055,1.5,.08),"galvanized")
    for x in (10.96,14.04):
        d.box("dispatch entrance reveal",(x,1.3,12.79),(.12,2.6,.17),"galvanized")
    d.panel("depot identity","SEVER MUNICIPAL TRANSIT  /  DEPOT 04",(16,5.72,12.76),13,.36,"south")
    d.panel("staff entrance","STAFF ONLY\nREPORT TO DISPATCH",(9.6,1.85,12.76),1.6,.75,"south")
    # Stair rail top and midrails, with open ends at each landing.
    for x in (4.7,7.0):
        for i in range(5):
            z=17+i*2
            y=i*.75
            d.beam("stair handrail upright",(x,y,z),(x,y+1.02,z),.029,"galvanized")
        for offset in (.55,1.02):
            d.beam("stair handrail",(x,offset,17),(x,3+offset,25),.035,"galvanized")
    d.features.append({"kind":"architecture","label":"dispatch frames and stair rails","position":[8,0,13]})
    # Industrial roof vents and wall grilles are off the movement surface.
    for z in (-7,10):
        d.cylinder("workshop roof ventilator",(-25,6.7,z),.35,.8,"galvanized")
        d.cylinder("ventilator rain cap",(-25,7.13,z),.52,.12,"steel")
    for x in (-28,-13):
        d.box("workshop high vent",(x,4.4,-16.19),(1.45,.9,.09),"dark")
        for y in (4.08,4.25,4.42,4.59,4.76):
            d.box("vent horizontal louvre",(x,y,-16.27),(1.42,.055,.1),"galvanized")
    # Lit workshop strips, also exported as CSP light positions.
    for x,z in ((-25,-9),(-15,-9),(-25,10),(-15,10)):
        d.box("fluorescent fixture",(x,5.72,z),(.28,.15,2.2),"galvanized")
        d.box("fluorescent diffuser",(x,5.63,z),(.21,.025,2),"lamp_lens")
        d.light("maintenance task light",(x,5.5,z),480,2)
    for x in (12,21):
        d.box("dispatch bulkhead",(x,2.84,12.64),(.48,.16,.22),"steel")
        d.box("dispatch bulkhead lens",(x,2.75,12.64),(.38,.02,.15),"lamp_lens")
        d.light("dispatch entrance light",(x,2.67,12.6),65,.35)
    # Background reads as a working industrial street instead of featureless cubes.
    for x,z,w,h in ((-24,39.97,34,10),(22,39.97,25,14),(61,-30.02,18,8)):
        d.box("context cornice",(x,h-.3,z),(w,.35,.2),"concrete")
        for xx in range(int(x-w/2+2),int(x+w/2-1),4):
            for yy in range(4,int(h-1),3):
                d.box("context window",(xx,yy,z-.07),(1.4,1.65,.06),"glass")
                d.box("context window sill",(xx,yy-.9,z-.12),(1.65,.12,.3),"concrete")


