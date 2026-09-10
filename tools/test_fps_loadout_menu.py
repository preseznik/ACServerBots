"""Exercise the actual loadout Lua and render its draw calls for layout review.

Requires Pillow and lupa (LuaJIT 2.1). No game code is translated into a web mock.
The preview approximates CSP font/colour rendering; in-game acceptance is separate.
Run: python tools/test_fps_loadout_menu.py --lua-runtime .artifacts/lua-runtime
"""

import argparse
from pathlib import Path
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--lua-runtime", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=ROOT / ".artifacts/loadout-menu")
    args = parser.parse_args()
    sys.path.insert(0, str(args.lua_runtime.resolve()))
    from lupa.luajit21 import LuaRuntime
    from lupa.lua54 import LuaRuntime as LuaSyntaxRuntime

    lua = LuaRuntime(unpack_returned_tuples=True)
    source = (ROOT / "AssettoServer/Server/Fps/fps.lua").read_text(encoding="utf-8")
    # The existing full client exceeds stock LuaJIT's 60-upvalue limit in update(),
    # including before this menu change. Parse the full file with Lua 5.4 and run
    # the changed menu itself with LuaJIT 2.1, the Lua dialect used by CSP.
    LuaSyntaxRuntime().compile(source, name="fps.lua")
    lua.execute('''
        bit = require('bit')
        math.clamp = function(v, a, b) return math.max(a, math.min(b, v)) end
        local mt = {}
        vec2 = function(x, y) return setmetatable({x=x or 0, y=y or x or 0}, mt) end
        mt.__add = function(a,b) return vec2(a.x+b.x,a.y+b.y) end
        mt.__sub = function(a,b) return vec2(a.x-b.x,a.y-b.y) end
        mt.__mul = function(a,b) return vec2(a.x*b,a.y*b) end
        mt.__div = function(a,b) return vec2(a.x/b,a.y/b) end
        rgbm = setmetatable({colors={white={1,1,1,1}}}, {
            __call=function(_,r,g,b,a) return {r,g,b,a} end})
        ui = {Alignment={Start=-1,Center=0}, ImageFit={Fit=2},
              MouseButton={Left=0},MouseCursor={Arrow=0}}
        window = vec2(1664, 944)
        mouse = vec2(-100,-100)
        click = false
        wheel = 0
        sent = 0
        ui.windowSize = function() return window end
        ui.mousePos = function() return mouse end
        ui.mouseClicked = function() return click end
        ui.mouseWheel = function() return wheel end
        ui.captureMouse = function() end
        ui.setMouseCursor = function() end
        hud = {loadout={catalogReceived=true,confirmed=false,
            mainWeapon=1, secondaryWeapon=3, lethal=17,
            allowedMainWeapons=6, allowedSecondaryWeapons=24, allowedLethals=196608,
            operatorModel=0, allowedOperatorModels=3,
            result='CONFIRM A LOADOUT TO JOIN'}}
        hud.loadoutSelectEvent = function(message) sent=sent+1; submitted=message end
        fpsVisual = {requestLoadoutAssets=function() end}
        requestRifleAssets = function() end
    ''')
    lua.execute(source[source.index("local fpsVisual = {"):source.index("local actors = {}")]
                .replace("local fpsVisual =", "fpsVisual =", 1))
    lua.execute('''
        fpsVisual.modern=true
        fpsVisual.requestLoadoutAssets=function() end
        fpsVisual.asset=function(name) return fpsVisual.modernAssetFolder..'/'..name end
    ''')
    lua.globals().fpsVisual.modernAssetFolder = str(ROOT / "AssettoServer.RaceControl.Core/Assets/Fps/Modern")
    lua.globals().io.fileExists = lambda path: Path(path).is_file()
    lua.execute(source[source.index("hud.itemNames = {"):source.index("function hud.aimSensitivity(")])
    lua.execute(source[source.index("function hud.submitLoadout()"):source.index("local function drawMatchStartOverlay(")])
    lua.globals().fpsVisual.loadoutAssetFolder = str(ROOT / "AssettoServer.RaceControl.Core/Assets/Fps")

    commands = []
    def record(kind):
        return lambda *values: commands.append((kind, values))
    for name, kind in [("drawRectFilled", "fill"), ("drawRect", "rect"), ("drawLine", "line"),
                       ("drawImage", "image"), ("dwriteDrawTextClipped", "text")]:
        lua.globals().ui[name] = record(kind)

    def frame(x=-100, y=-100, clicked=False, wheel=0, initial=True, width=1664, height=944):
        g = lua.globals()
        g.window = g.vec2(width, height)
        scale = min((width - 32) / 1600, (height - 32) / 880, 1.5)
        g.mouse = g.vec2((width - 1600 * scale) / 2 + x * scale,
                         (height - 880 * scale) / 2 + y * scale)
        g.click, g.wheel = clicked, wheel
        commands.clear()
        g.hud.drawLoadoutMenu(initial)

    def render(filename):
        g = lua.globals()
        image = Image.new("RGBA", (int(g.window.x), int(g.window.y)), (12, 15, 19, 255))
        draw = ImageDraw.Draw(image)
        for kind, values in commands:
            if kind in ("fill", "rect"):
                p, q, color = values[:3]
                radius = values[3] if len(values) > 3 else 0
                rest = values[4:]
                bounds = (round(p.x), round(p.y), round(q.x), round(q.y))
                if kind == "fill":
                    draw.rounded_rectangle(bounds, radius=round(radius or 0), fill=rgba(color))
                else:
                    draw.rounded_rectangle(bounds, radius=round(radius or 0), outline=rgba(color),
                                           width=max(1, round(rest[-1] if rest and rest[-1] else 1)))
            elif kind == "line":
                p, q, color, width = values
                draw.line((p.x, p.y, q.x, q.y), fill=rgba(color), width=max(1, round(width)))
            elif kind == "image":
                path, p, q, tint, *_ = values
                thumb = Image.open(path).convert("RGBA")
                thumb.thumbnail((round(q.x-p.x), round(q.y-p.y)), Image.Resampling.LANCZOS)
                if tint[1] < 1:
                    thumb.putalpha(thumb.getchannel("A").point(lambda value: int(value * tint[1])))
                image.alpha_composite(thumb, (round((p.x+q.x-thumb.width)/2), round((p.y+q.y-thumb.height)/2)))
            else:
                label, size, p, q, alignment, _, _, color = values
                font = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf" if size >= 20 else
                                          "C:/Windows/Fonts/segoeui.ttf", max(1, round(size)))
                w, h = max(1, round(q.x-p.x)), max(1, round(q.y-p.y))
                layer = Image.new("RGBA", (w, h))
                ink = ImageDraw.Draw(layer)
                box = ink.textbbox((0, 0), label, font=font)
                x = (w - ink.textlength(label, font=font)) / 2 if alignment == 0 else 0
                ink.text((x, (h-(box[3]-box[1]))/2-box[1]), label, font=font, fill=rgba(color))
                image.alpha_composite(layer, (round(p.x), round(p.y)))
        args.output.mkdir(parents=True, exist_ok=True)
        image.convert("RGB").save(args.output / filename)

    frame()
    assert sum(kind == "image" for kind, _ in commands) == 6
    render("loadout-desktop.png")
    frame(width=1280, height=720)
    render("loadout-1280.png")
    frame(width=831, height=619)
    render("loadout-831.png")
    frame(x=1460, y=74, clicked=True)
    assert lua.globals().hud.loadoutTab == "operator"
    frame()  # Capture the settled tab after the click frame.
    assert sum(kind == "image" for kind, _ in commands) == 2
    assert any(kind == "text" and values[0] == "SELECT YOUR OPERATOR" for kind, values in commands)
    render("operators-desktop.png")
    frame(x=1100, y=400, clicked=True)
    assert lua.globals().hud.loadout.operatorModel == 1
    assert lua.globals().sent == 0, "Choosing appearance must not deploy implicitly"
    frame(width=1280, height=720)
    render("operators-1280.png")
    frame(width=831, height=619)
    render("operators-831.png")
    lua.execute("hud.loadout.allowedOperatorModels=1")
    frame(x=1300, y=829, clicked=True)
    assert lua.globals().sent == 0, "Server-locked model must prevent submission"
    frame(x=300, y=400, clicked=True)
    assert lua.globals().hud.loadout.operatorModel == 0
    frame(x=1100, y=400, clicked=True)
    assert lua.globals().hud.loadout.operatorModel == 0, "Locked card must be inert"
    lua.execute("hud.loadout.allowedOperatorModels=3")
    frame(x=1100, y=400, clicked=True)
    frame(x=1300, y=829, clicked=True)
    assert lua.globals().submitted.operatorModel == 1
    lua.execute("sent=0; hud.loadout.operatorModel=0")
    frame(x=1250, y=74, clicked=True)
    assert lua.globals().hud.loadoutTab == "gear"
    frame(x=1130, y=300, clicked=True)
    assert lua.globals().hud.loadout.mainWeapon == 2, "MP5 card must select primary only"
    assert lua.globals().hud.loadout.secondaryWeapon == 3
    frame(x=413, y=170, clicked=True)
    assert lua.globals().hud.loadoutRows.mainWeapon.filter == "rifle"
    assert lua.globals().hud.loadout.mainWeapon == 2, "Filtering must not silently change selection"
    frame(x=679, y=170, clicked=True)
    assert lua.globals().hud.loadoutRows.mainWeapon.filter == "rifle", "Empty LMG filter must be inert"
    lua.execute("hud.setLoadoutFilter('mainWeapon', 'all'); hud.loadout.allowedMainWeapons=2")
    frame(x=1130, y=300, clicked=True)
    frame(x=1300, y=829, clicked=True)
    assert lua.globals().sent == 0, "Invalid/server-locked selection must not submit"
    frame(x=350, y=300, clicked=True)
    frame(x=1300, y=829, clicked=True)
    assert lua.globals().sent == 1 and lua.globals().submitted.mainWeapon == 1
    frame(x=510, y=650, clicked=True)
    assert lua.globals().hud.loadout.secondaryWeapon == 4
    frame(x=985, y=650, clicked=True)
    assert lua.globals().hud.loadout.lethal == 16
    frame(x=100, y=829, clicked=True, initial=False)
    assert lua.globals().hud.pausePage == "main"

    # Inject extra options only in the test runtime; no fictitious items ship.
    lua.execute('''
        hud.loadout.allowedMainWeapons=2147483647
        for i=5,10 do
            hud.itemNames[i]='TEST RIFLE '..i
            table.insert(hud.loadoutItems,{id=i,slot='mainWeapon',category='rifle',
                subtitle='Test option',image='asrc_loadout_assault_rifle.png'})
        end
        hud.setLoadoutFilter('mainWeapon','all')
    ''')
    frame(x=1542, y=454, clicked=True)
    assert lua.globals().hud.loadoutRows.mainWeapon.first == 2
    frame(x=600, y=300, wheel=-1)
    assert lua.globals().hud.loadoutRows.mainWeapon.first == 3
    for _ in range(20):
        frame(x=600, y=300, wheel=-1)
    assert lua.globals().hud.loadoutRows.mainWeapon.first == 7, "Last page must clamp"
    frame(x=1140, y=300, clicked=True)
    assert lua.globals().hud.loadout.mainWeapon == 10, "Last item must remain clickable"
    frame(x=425, y=170, clicked=True)
    assert lua.globals().hud.loadoutRows.mainWeapon.first == 6, "Filter must reveal selected card"
    frame()
    render("loadout-overflow-test.png")
    frame(x=570, y=170, clicked=True)
    assert lua.globals().hud.loadoutRows.mainWeapon.first == 1, "Single-item category resets scroll"
    assert sum(kind == "image" for kind, _ in commands) == 5
    frame(x=175, y=536, clicked=True)
    assert lua.globals().hud.loadoutRows.secondaryWeapon.filter == "pistol"
    lua.execute('''
        hud.itemNames[11]='TEST PISTOL'
        table.insert(hud.loadoutItems,{id=11,slot='secondaryWeapon',category='pistol',
            subtitle='Test option',image='asrc_loadout_colt_1911.png'})
        hud.loadout.allowedSecondaryWeapons=2072
    ''')
    frame(x=770, y=770, clicked=True)
    assert lua.globals().hud.loadoutRows.secondaryWeapon.first == 2
    assert lua.globals().hud.loadoutRows.mainWeapon.first == 1
    frame(x=530, y=650, clicked=True)
    assert lua.globals().hud.loadout.secondaryWeapon == 11
    frame(x=350, y=650, wheel=1)
    assert lua.globals().hud.loadoutRows.secondaryWeapon.first == 1
    lua.execute("hud.loadout.catalogReceived=false")
    previous = lua.globals().sent
    frame(x=1300, y=829, clicked=True)
    assert lua.globals().sent == previous, "Catalog confirmation gates spawning"
    print("PASS: LuaJIT syntax, six images, card selection, disabled filters, server allow-lists, "
          "confirmation, pause return, arrow/wheel scrolling, last-card selection and filter clamping.")
    print(f"Draw-call previews: {args.output.resolve()}")


def rgba(color):
    return tuple(round(max(0, min(1, color[i])) * 255) for i in range(1, 5))


if __name__ == "__main__":
    main()
