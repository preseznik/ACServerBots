"""Depot cover belongs to real facilities; low pavements connect their doors."""
import math


def build(d, layout):
    d.site_probes = {"walkRoutes": [], "standingPoints": [], "shotProbes": []}

    def solid(label, center, size, mat="concrete", yaw=0):
        d.box(label, center, size, mat, yaw)
        d.proxy(label, center, size, yaw)

    def canopy(label, center, size, yaw=0):
        d.a.box(label, center, size, "roof", solid=True, roof=True, yaw=yaw)
        bottom = center[1]+size[1]/2+.08
        d.a.movement_box((center[0], (bottom+25)/2, center[2]),
                         (size[0], 25-bottom, size[2]), yaw)

    def route(name, points):
        d.site_probes["walkRoutes"].append({"name": name, "points": points, "bidirectional": True})

    # A shallow lean-to is physically attached to the workshop's south wall.
    solid("parts receiving annex south wall", (-31,1.4,-19), (15,2.8,.4), "brick")
    solid("parts receiving annex west return", (-38.3,1.4,-17.5), (.4,2.8,3), "brick")
    canopy("parts receiving lean-to roof", (-31,2.99,-17.5), (15.4,.18,3.4))
    d.panel("parts receiving identification", "PARTS RECEIVING", (-29,2.13,-19.23), 4.3,.58)
    d.box("parts loading apron", (-26,.02,-17.5), (4.7,.04,2.4), "concrete")
    for x in (-36,-33):
        solid("parts storage stillage", (x,.55,-17.3), (1.8,1.1,1.3), "steel")
        for yy in (.08,.96):
            d.box("stillage pallet rail", (x,yy,-18), (1.9,.08,.08), "galvanized")
    d.features.append({"kind":"service_annex", "label":"Attached parts receiving lean-to", "position":[-31,0,-17.5]})

    # North cover is a bank of wash-water treatment and electrical equipment.
    solid("wash plant enclosure", (34,1.4,20.75), (12,2.8,1.5), "concrete")
    canopy("wash plant weather hood", (34,2.95,20.75), (12.35,.16,1.8))
    for x in (29.1,31.5,33.9,36.3,38.7):
        d.box("wash plant cabinet door", (x,1.40,21.53), (2.10,2.4,.055), "fleet")
        d.box("wash plant door handle", (x+.72,1.2,21.58), (.055,.25,.055), "galvanized")
        for yy in (.42,.50,.58,.66):
            d.box("wash plant ventilation louvre", (x,yy,21.57), (1.45,.035,.025), "dark")
    d.panel("wash plant identification", "WASH WATER RECOVERY  /  ELECTRICAL", (34,2.32,21.60), 6,.38, "north")
    d.panel("wash plant warning", "AUTHORISED STAFF ONLY", (29.2,1.55,21.60), 1.7,.38, "north", "yellow", "dark")
    d.features.append({"kind":"service_plant", "label":"Wash water recovery plant", "position":[34,0,20.75]})

    # The old angled wall becomes the back of a staff rest shelter.
    yaw = 12
    angle = math.radians(yaw)
    def p(dx,y,dz):
        return (-10+dx*math.cos(angle)+dz*math.sin(angle),y,
                -26-dx*math.sin(angle)+dz*math.cos(angle))
    solid("staff shelter brick back", p(0,1.2,0), (9,2.4,.5), "brick", yaw)
    solid("staff shelter paved plinth", p(0,.02,1.15), (9.5,.04,2.9), "concrete", yaw)
    canopy("staff shelter roof", p(0,2.72,1.10), (9.6,.18,3.0), yaw)
    for dx in (-4.4,4.4):
        solid("staff shelter canopy post", p(dx,1.34,2.3), (.10,2.68,.10), "galvanized", yaw)
    for dx in (-2.5,2.5):
        solid("staff shelter bench", p(dx,.49,.67), (2.6,.9,.55), "dark", yaw)
        d.box("staff shelter bench seat", p(dx,.53,.67), (2.75,.085,.64), "galvanized", yaw)
        d.box("staff shelter bench back", p(dx,.85,.41), (2.75,.50,.07), "galvanized", yaw)
    d.features.append({"kind":"staff_shelter", "label":"Covered staff rest area", "position":list(p(0,0,1.1))})
    route("staff shelter centre access", [list(p(0,0,4)),list(p(0,.04,1.2))])

    # Low kerbs and deliberate gaps provide readable pedestrian routes.
    for label,x,z,w,depth in (
            ("south workshop pavement",-16,-17.25,14.7,2.4),
            ("east workshop pavement",-7.65,1,2.1,34.8),
            ("dispatch front pavement",16.6,12.0,18.4,1.8),
            ("staff stop pavement",26.75,14.1,4.9,6.2),
            ("west workshop service apron",-34,4,4.5,5)):
        solid(label,(x,.02,z),(w,.04,depth))
    for z,depth in ((-14.2,3.5),(-.75,10),(16.25,3.9)):
        solid("workshop pavement kerb",(-6.61,.05,z),(.16,.10,depth))
    for x,width in ((9.2,3.1),(19.8,10.8)):
        solid("dispatch pavement kerb",(x,.05,11.08),(width,.10,.16))
    for x,width in ((-22.4,1.8),(-14.3,8.6)):
        solid("south workshop pavement kerb",(x,.05,-18.47),(width,.10,.16))
    for z in (-13,-3,7,17):
        d.box("pavement expansion joint",(-7.65,.0408,z),(2.1,.001,.016),"dark")
    d.features.append({"kind":"sidewalks", "label":"Workshop, dispatch and staff pedestrian pavements", "position":[-7.65,0,1]})
    route("east workshop pavement", [[-7.65,.04,-14],[-7.65,.04,16]])
    route("dispatch paved front approach", [[12.5,0,9.8],[12.5,.04,12],[12.5,0,15]])

    # Refuelling equipment keeps the original short cover footprint useful.
    solid("refuelling island kerb",(22,.06,-8),(1.2,.12,4.5))
    for z in (-9.35,-6.65):
        solid("diesel dispenser",(22,.89,z),(.68,1.54,.68),"fleet")
        d.panel("diesel dispenser display","DIESEL\nLITRES  000.0",(22,1.24,z+.36),.49,.36,"north","dark","paint")
        for side in (-1,1):
            d.beam("dispenser hose",(22+side*.40,1.40,z),(22+side*.46,.35,z),.033,"dark",8)
    d.cylinder("fuel sign post",(22,1.95,-10.0),.035,3.9,"galvanized")
    d.panel("fuel safety sign","DIESEL\nNO SMOKING",(22,3.45,-10.06),1.25,.65)
    d.features.append({"kind":"refuelling_island", "label":"Yard diesel refuelling point", "position":[22,0,-8]})

    # Utility cabinets sit on an apron beside the workshop they serve.
    solid("compressed air service cabinet",(-35,1.05,4),(1.10,2.1,3.9),"fleet")
    for z in (2.9,5.1):
        d.box("service cabinet door",(-34.43,1.1,z),(.045,1.8,1.55),"galvanized")
        d.box("service cabinet handle",(-34.38,1.2,z+.45),(.06,.25,.045),"dark")
    d.panel("air service identification","COMPRESSED AIR\nWORKSHOP SUPPLY",(-35.59,1.45,4),2.35,.55,"west")
    for z in (3.3,4.7):
        d.beam("workshop air supply",(-34.4,2.1,z),(-31.1,2.1,z),.045,"galvanized")
    d.features.append({"kind":"workshop_services", "label":"Attached workshop compressor bank", "position":[-35,0,4]})

    # Raised planting beds bound pedestrian refuges; soil and coping explain mass.
    for x,z,yaw in ((-4,5,0),(31,-22,25)):
        solid("pedestrian refuge planting trough",(x,.45,z),(1.1,.9,4),"concrete",yaw)
        d.box("planter soil",(x,.90,z),(.86,.035,3.75),"dark",yaw)
        for dx in (-.48,.48):
            a=math.radians(yaw)
            d.box("planter coping",(x+dx*math.cos(a),.94,z-dx*math.sin(a)),(.17,.13,4.1),"concrete",yaw)
        d.features.append({"kind":"pedestrian_refuge", "label":"Raised depot planting bed", "position":[x,0,z]})

    # Suspended sign: board and rods meet the canopy, above player head height.
    d.panel("suspended wash sign","WASH",(35,3.85,-7.18),3.5,.85)
    d.proxy("suspended wash signboard",(35,3.85,-7.18),(3.5,.85,.055))
    for x in (33.7,36.3):
        d.beam("wash sign suspension",(x,4.56,-7.18),(x,4.25,-7.18),.022,"galvanized")
    d.features.append({"kind":"mounted_sign", "label":"WASH board with two canopy suspension rods", "position":[35,3.85,-7.18]})
