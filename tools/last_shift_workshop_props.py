"""Original depot workstations; AC coordinates in metres, with Y up.

Large additions have simple collision; small fixtures stay against walls or on
existing furniture. The authored door, bay, stair and wash routes remain clear.
"""
from math import cos, pi, sin


def _extinguisher(d, x, y, z):
    d.box("extinguisher wall bracket", (x-.08, y, z), (.16, .58, .23), "dark")
    d.cylinder("extinguisher red bottle", (x, y, z), .095, .43, "safety_red")
    d.cylinder("extinguisher neck", (x, y+.25, z), .035, .08, "galvanized")
    d.beam("extinguisher handle", (x-.04, y+.29, z), (x+.10, y+.29, z), .022, "dark")
    d.beam("extinguisher short hose", (x+.08, y+.23, z), (x+.12, y-.13, z), .018, "dark")
    d.panel("extinguisher marker", "FIRE", (x-.055, y+.73, z), .33, .38,
            face="east", mat="safety_red", text_size=.09)


def _workshop(d):
    # Existing bench spans x -29..-27, z -10.5..-5.5, with its top at Y=1.
    d.box("worn bench worktop", (-28, 1.025, -8), (2, .05, 5), "galvanized")
    d.box("bench tool backboard", (-28.91, 1.67, -8), (.055, 1.25, 4.7), "fleet")
    for z in (-9.95, -9.0, -8.05, -7.10, -6.15):
        for y in (.25, .52, .79):
            d.box("bench drawer front", (-26.982, y, z), (.025, .215, .86), "fleet")
            d.beam("bench drawer pull", (-26.94, y+.025, z-.18),
                   (-26.94, y+.025, z+.18), .019, "galvanized")
    for i in range(8):
        z = -9.75 + i*.48
        length = .22 + (i % 3)*.045
        d.beam("hanging workshop spanner", (-28.86, 1.70, z),
               (-28.86, 1.70+length, z), .018, "galvanized")
        d.box("spanner open jaw", (-28.845, 1.73+length, z), (.025, .05, .074), "galvanized")
    d.box("bench vice base", (-27.35, 1.065, -6.10), (.38, .075, .32), "steel")
    for x in (-27.48, -27.25):
        d.box("bench vice jaw", (x, 1.18, -6.10), (.10, .18, .30), "galvanized")
    d.beam("bench vice screw", (-27.57, 1.13, -6.10), (-26.98, 1.13, -6.10), .035, "steel")
    d.beam("bench vice handle", (-26.98, 1.02, -6.10), (-26.98, 1.30, -6.10), .017, "galvanized")
    d.box("bench parts tray", (-27.7, 1.045, -8.8), (.70, .04, .46), "dark")
    for x, z in ((-27.87, -8.90), (-27.62, -8.79), (-27.77, -8.65)):
        d.cylinder("loose tray fitting", (x, 1.10, z), .055, .08, "galvanized", vertices=8)
    d.panel("bench station number", "SERVICE 01", (-26.94, .77, -8.0), .62, .14,
            face="east", mat="sign_blue", text_size=.065)

    # Compressor and rolling service cart occupy the wall-side service pocket.
    d.proxy("workshop compressor", (-30.05, .63, -12.7), (.86, 1.26, 1.40))
    d.cylinder("compressor pressure tank", (-30.05, .49, -12.7), .32, 1.12,
               "fleet", axis="z", vertices=16)
    for z in (-13.12, -12.28):
        d.box("compressor tank foot", (-30.05, .10, z), (.50, .20, .13), "steel")
    d.box("compressor motor", (-30.05, .93, -12.83), (.40, .27, .48), "steel")
    d.box("compressor belt guard", (-30.04, .98, -12.43), (.48, .42, .08), "dark")
    d.cylinder("compressor pressure gauge", (-29.835, .91, -13.03), .08, .04,
               "paint", axis="x")
    d.beam("compressor copper supply", (-30.05, .78, -13.12),
           (-30.05, 1.10, -13.12), .025, "galvanized")
    d.proxy("workshop service cart", (-27.45, .49, -12.50), (.92, .98, .72))
    for y in (.22, .61, .88):
        d.box("service cart shelf", (-27.45, y, -12.50), (.86, .055, .64), "safety_red")
    for x in (-27.84, -27.06):
        for z in (-12.77, -12.23):
            d.beam("service cart upright", (x, .12, z), (x, .91, z), .022, "steel")
            d.cylinder("service cart castor", (x, .09, z), .085, .065, "dark", axis="x")
    d.beam("service cart push handle", (-27.85, .96, -12.82),
           (-27.05, .96, -12.82), .026, "galvanized")
    d.box("cart open tool case", (-27.45, .94, -12.5), (.48, .065, .35), "dark")

    # Shallow shelf fronts detail the existing solid parts block, not hollow it.
    d.proxy("parts shelf frontage", (-26.36, 1.20, 9), (.28, 2.40, 6))
    d.box("parts rack dark backing", (-26.481, 1.20, 9), (.025, 2.30, 5.86), "dark")
    for y in (.12, .80, 1.49, 2.17):
        d.box("parts rack shelf edge", (-26.34, y, 9), (.28, .055, 5.90), "galvanized")
    for z in (6.07, 8.02, 9.98, 11.93):
        d.box("parts rack vertical", (-26.29, 1.20, z), (.085, 2.4, .065), "steel")
    for row, y in enumerate((.3775, 1.0575, 1.7475)):
        for i in range(6):
            z = 6.50 + i*.98
            mat = "galvanized" if (i+row) % 3 else "fleet"
            d.box("parts storage bin", (-26.34, y, z), (.27, .46, .78), mat)
            d.box("parts bin handle", (-26.19, y+.03, z), (.025, .055, .21), "dark")
            d.box("parts bin paper label", (-26.186, y-.10, z), (.027, .065, .16), "paint")
    d.panel("parts rack inventory header", "FILTERS / BELTS / FITTINGS", (-26.24, 2.33, 9),
            2.7, .15, face="east", mat="sign_blue", text_size=.075)
    d.proxy("stored service tyres", (-30, .35, 13.4), (.90, .70, .90))
    for y in (.12, .35, .58):
        for i in range(12):
            a, b = 2*pi*i/12, 2*pi*(i+1)/12
            d.beam("stored tyre rubber ring", (-30+.32*cos(a), y, 13.4+.32*sin(a)),
                   (-30+.32*cos(b), y, 13.4+.32*sin(b)), .11, "dark", vertices=8)
    _extinguisher(d, -30.70, 1.15, -3)
    d.features.append({"kind": "workshop_workstations", "label": "Repair bench, compressor, cart and parts storage",
                       "position": [-28, 0, 0]})


def _wash(d):
    # Equipment is already solid; fittings and hoses hug the east screen.
    d.box("wash machine control fascia", (38.025, 1.46, -1), (.045, .57, .80), "sign_blue")
    d.box("wash machine readout", (37.994, 1.58, -1.08), (.025, .17, .40), "dark")
    for z, mat in ((-1.20, "safety_red"), (-.95, "fleet"), (-.70, "yellow")):
        d.cylinder("wash machine control button", (37.976, 1.30, z), .045, .045, mat, axis="x")
    d.panel("wash programme plate", "RINSE / WASH", (37.978, 1.83, -1), .65, .14,
            face="west", mat="paint", text_mat="dark", text_size=.07)
    for z, mat in ((-1.6, "paint"), (-.7, "yellow")):
        d.box("wash detergent bottle", (38.7, 2.15, z), (.22, .30, .28), mat)
        d.cylinder("detergent bottle cap", (38.7, 2.315, z), .045, .03, "dark")
    d.box("hose reel mounting bracket", (39.78, 1.46, 4), (.14, .45, .44), "steel")
    d.cylinder("wash hose drum", (39.60, 1.46, 4), .26, .22, "dark", axis="x", vertices=16)
    for x in (39.46, 39.73):
        d.cylinder("hose reel flange", (x, 1.46, 4), .33, .035, "galvanized", axis="x", vertices=16)
    d.cylinder("hose reel axle", (39.62, 1.46, 4), .045, .38, "steel", axis="x")
    hose = ((39.44, 1.46, 4), (39.20, .65, 3.7), (38.8, .04, 3.45),
            (38.2, .04, 3.05), (37.75, .04, 3.6), (38.15, .04, 4.25),
            (38.9, .04, 4.1), (39.28, .07, 3.4), (39.65, .88, 2.85))
    for start, end in zip(hose, hose[1:]):
        d.beam("wash hose", start, end, .026, "dark")
    d.beam("wash spray lance", (39.65, .90, 2.85), (39.65, 1.85, 2.85), .021, "galvanized")
    d.box("wash lance grip", (39.65, 1.05, 2.85), (.065, .23, .075), "dark")
    for y in (1.10, 1.65):
        d.box("wash lance wall hook", (39.77, y, 2.85), (.23, .035, .07), "steel")
    d.features.append({"kind": "wash_workstation", "label": "Wash controls, detergent and wall hose reel",
                       "position": [38.8, 0, 1.5]})


def _desk(d, x, floor, z):
    top = floor+.9
    d.box("dispatch desktop edge", (x, top+.02, z), (3, .04, 1.15), "paint")
    d.box("dispatch telephone body", (x+.66, top+.10, z-.02), (.38, .14, .25), "dark")
    d.beam("dispatch telephone handset", (x+.49, top+.20, z+.035),
           (x+.84, top+.20, z+.035), .034, "dark")
    for i in range(3):
        for j in range(3):
            d.box("telephone keypad", (x+.59+i*.055, top+.175, z-.065-j*.04),
                  (.027, .014, .024), "paint")
    for i, mat in enumerate(("sign_blue", "fleet", "safety_red", "sign_blue")):
        d.box("dispatch record binder", (x-1.0+i*.095, top+.195, z+.20), (.075, .35, .28), mat)
        d.box("binder spine label", (x-1.0+i*.095, top+.22, z+.052), (.052, .09, .018), "paint")
    d.box("dispatch paper stack", (x+.05, top+.055, z-.23), (.40, .055, .29), "paint")
    d.box("dispatch terminal base", (x-.26, top+.055, z+.20), (.32, .065, .26), "dark")
    d.box("dispatch terminal support", (x-.26, top+.18, z+.24), (.065, .22, .065), "steel")
    d.box("dispatch terminal casing", (x-.26, top+.39, z+.20), (.48, .35, .18), "dark")
    d.panel("dispatch terminal screen", "DEPOT\nSERVICE LOG", (x-.26, top+.40, z+.10), .42, .28,
            mat="sign_blue", text_size=.055)
    chair_z = z-1.13
    d.proxy("dispatch chair", (x, floor+.55, chair_z), (.66, 1.10, .69))
    d.box("dispatch chair seat", (x, floor+.48, chair_z), (.54, .11, .53), "dark")
    d.box("dispatch chair back", (x, floor+.82, chair_z-.27), (.55, .52, .07), "fleet")
    for dx in (-.22, .22):
        for dz in (-.21, .21):
            d.beam("dispatch chair leg", (x+dx, floor+.025, chair_z+dz),
                   (x+dx, floor+.44, chair_z+dz), .022, "steel")
        d.beam("dispatch chair back support", (x+dx, floor+.45, chair_z-.26),
               (x+dx, floor+.88, chair_z-.26), .018, "steel")


def build(d):
    _workshop(d)
    _wash(d)
    _desk(d, 11, 0, 25.5)
    _desk(d, 16, 3, 26)
    d.features.append({"kind": "dispatch_workstations", "label": "Dispatch desks with phones, binders and chairs",
                       "position": [14, 0, 26]})
