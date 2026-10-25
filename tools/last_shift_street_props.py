"""Original street furniture for Last Shift; all coordinates are AC metres.

The central circulation routes and authored spawns stay clear. Visual fittings
share one simple collision proxy per substantial object instead of many snags.
"""


def _record(d, kind, label, position, **details):
    d.features.append(dict(kind=kind, label=label, position=list(position), **details))


def _post(d, label, x, z, height=2.6, radius=.045):
    d.cylinder(label + " galvanized post", (x, height / 2, z), radius, height,
               "galvanized", vertices=10)
    d.box(label + " base plate", (x, .035, z), (.22, .07, .22), "galvanized")
    d.proxy(label + " post collision", (x, height / 2, z), (.18, height, .18))


def _wayfinding(d, label, x, z, lines):
    for offset in (-.66, .66):
        _post(d, label, x + offset, z, 2.8)
    d.panel(label, lines, (x, 2.28, z - .025), 1.65, 1.0,
            mat="sign_blue", text_mat="paint", text_size=.12)
    d.proxy(label + " sign collision", (x, 2.28, z), (1.65, 1.0, .08))
    _record(d, "wayfinding", label, (x, 0, z), width=1.65, text=lines)


def _road_sign(d, label, x, z, speed=False):
    _post(d, label, x, z, 2.55)
    d.cylinder(label + " red disc", (x, 2.3, z - .055), .3, .045,
               "safety_red", axis="z", vertices=32)
    if speed:
        d.cylinder(label + " white inset", (x, 2.3, z - .082), .235, .012,
                   "paint", axis="z", vertices=32)
        d.panel(label + " speed", "10", (x, 2.3, z - .094), .31, .3,
                mat="paint", text_mat="dark", text_size=.21)
        d.panel(label + " caption", "DEPOT LIMIT", (x, 1.76, z - .065), .68, .22,
                mat="paint", text_mat="dark", text_size=.065)
    else:
        d.box(label + " no entry bar", (x, 2.3, z - .083), (.43, .105, .016), "paint")
        d.panel(label + " caption", "STAFF ONLY", (x, 1.76, z - .065), .66, .22,
                mat="paint", text_mat="dark", text_size=.07)
    d.proxy(label + " disc collision", (x, 2.3, z - .055), (.6, .6, .07))
    _record(d, "traffic_sign", label, (x, 0, z), text="10 / DEPOT LIMIT" if speed else "NO ENTRY / STAFF ONLY")


def _bus_stop(d):
    x, z = 27.5, 12.7
    _post(d, "dispatch bus stop", x, z, 3.05)
    d.panel("dispatch bus stop header", "04\nDEPOT", (x, 2.65, z - .07), .62, .65,
            mat="sign_blue", text_mat="paint", text_size=.16)
    d.box("dispatch timetable frame", (x, 1.72, z - .07), (.45, .72, .055), "galvanized")
    d.panel("dispatch timetable", "STAFF SHUTTLE\nMON - FRI\n05:40  06:10\n13:40  14:10\n21:40  22:10",
            (x, 1.72, z - .12), .4, .65, mat="paint", text_mat="dark", text_size=.043)
    d.proxy("dispatch bus stop boards collision", (x, 2.22, z - .06), (.62, 1.55, .12))
    # A bolted municipal steel bench, facing the open yard to the south.
    bx, bz = 26.4, 15.25
    for dx in (-.74, .74):
        for dz in (-.19, .19):
            d.box("bench steel leg", (bx + dx, .225, bz + dz), (.045, .45, .045), "galvanized")
            d.box("bench foot plate", (bx + dx, .025, bz + dz), (.13, .05, .11), "steel")
        d.beam("bench back support", (bx + dx, .12, bz + .19),
               (bx + dx, .94, bz + .24), .027, "galvanized")
    for offset in (-.18, -.06, .06, .18):
        d.box("bench seat slat", (bx, .47, bz + offset), (1.9, .055, .09), "fleet")
    for height in (.68, .84):
        d.box("bench back slat", (bx, height, bz + .24), (1.9, .115, .045), "fleet")
    d.proxy("dispatch bench collision", (bx, .48, bz + .02), (1.9, .96, .56))
    _record(d, "bus_stop", "dispatch staff shuttle stop", (x, 0, z),
            route="04", benchPosition=[bx, 0, bz], timetable="Original fictional staff service")


def _bollard(d, x, z):
    d.cylinder("corner bollard", (x, .5, z), .08, 1.0, "yellow", vertices=12)
    d.cylinder("bollard black lower stripe", (x, .62, z), .082, .12, "dark", vertices=12)
    d.cylinder("bollard black upper stripe", (x, .88, z), .082, .12, "dark", vertices=12)
    d.box("bollard mounting flange", (x, .035, z), (.24, .07, .24), "steel")
    d.proxy("bollard collision", (x, .5, z), (.2, 1.0, .2))
    _record(d, "bollard", "building corner protection", (x, 0, z))


def _bin(d, label, x, z, material):
    d.box(label + " body", (x, .51, z), (.56, .83, .61), material)
    d.box(label + " lid", (x, .955, z), (.62, .065, .66), "dark")
    d.box(label + " handle", (x, .9, z + .37), (.38, .045, .05), "dark")
    for dx in (-.24, .24):
        d.cylinder(label + " wheel", (x + dx, .12, z + .22), .105, .07,
                   "dark", axis="x", vertices=12)
    d.panel(label + " label", "DEPOT\nWASTE", (x, .61, z - .315), .3, .26,
            mat="paint", text_mat="dark", text_size=.055)
    d.proxy(label + " collision", (x, .5, z), (.62, 1.0, .7))


def _cabinet(d, label, x, z):
    d.box(label + " concrete plinth", (x, .09, z), (1.05, .18, .78), "concrete")
    d.box(label + " enclosure", (x, .92, z), (.91, 1.5, .63), "galvanized")
    d.box(label + " door", (x, .96, z - .328), (.82, 1.34, .032), "fleet")
    d.box(label + " rain hood", (x, 1.7, z), (1.0, .075, .72), "galvanized")
    d.box(label + " latch", (x + .31, .91, z - .36), (.045, .14, .025), "dark")
    for height in (1.17, 1.23, 1.29, 1.35):
        d.box(label + " vent", (x, height, z - .349), (.5, .023, .018), "dark")
    d.panel(label + " warning", "ELECTRICAL\nKEEP CLEAR", (x, .7, z - .354), .44, .22,
            mat="yellow", text_mat="dark", text_size=.049)
    d.proxy(label + " collision", (x, .89, z), (1.05, 1.78, .78))
    _record(d, "service_cabinet", label, (x, 0, z), dimensions=[1.05, 1.78, .78])


def build(d):
    """Add grounded perimeter furnishings without moving gameplay geometry."""
    for index, (x, z, face) in enumerate((
        (-42, -15, "east"), (-42, 15, "east"),
        (-18, -32, "north"), (8, -32, "north"),
        (42, -18, "west"), (42, 12, "west"),
        (1, 32, "south"), (27, 32, "south"),
    ), 1):
        label = "depot streetlamp %02d" % index
        d.lamp(label, x, z, height=6, face=face)
        d.features[-1].update(height=6, face=face)
    _bus_stop(d)
    _wayfinding(d, "south yard directions", -6.5, -18.5,
                "< MAINTENANCE\nDISPATCH >\nWASH >")
    _wayfinding(d, "east yard directions", 27, 9.2,
                "< DISPATCH\nSTAFF STOP >\nWASH - SOUTH")
    d.panel("utility building identification", "DEPOT SERVICES\nAUTHORISED STAFF", (-3, 2.3, 21.43),
            2.1, .55, mat="sign_blue", text_mat="paint", text_size=.12)
    _record(d, "wayfinding", "utility building identification", (-3, 2.3, 21.43), wallMounted=True)
    _road_sign(d, "yard speed restriction", -6.7, -29.2, speed=True)
    _road_sign(d, "east service access", 42.2, -10, speed=False)
    for x, z in ((-8.1, -14.8), (-8.1, 16.6), (-30, -17.0), (-29.5, 19),
                 (25.0, 13.0), (25.0, 27.5), (29.0, -8.2), (41.0, -8.2),
                 (29.0, 8.2), (41.0, 8.2)):
        _bollard(d, x, z)
    for label, x, z in (("west service waste", -42.6, -3), ("east service waste", 42.6, 1.2)):
        _bin(d, label + " general", x, z, "fleet")
        _bin(d, label + " recycling", x, z + 1.1, "sign_blue")
        _record(d, "waste_collection", label, (x, 0, z), bins=2)
    _cabinet(d, "north electrical feeder", -13.5, 33.2)
    _cabinet(d, "east lighting controls", 42.3, -26.0)
