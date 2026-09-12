"""Check both ammo HUD paths and render their real draw calls (not a CSP capture)."""
import argparse
from pathlib import Path
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--lua-runtime', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=ROOT / '.artifacts/ammo-hud')
    args = parser.parse_args()
    sys.path.insert(0, str(args.lua_runtime.resolve()))
    from lupa.luajit21 import LuaRuntime
    from lupa.lua54 import LuaRuntime as SyntaxRuntime

    online = (ROOT / 'AssettoServer/Server/Fps/fps.lua').read_text(encoding='utf-8')
    app = (ROOT / 'AssettoServer.RaceControl.Core/Assets/Fps/Hud/asrc_fps_hud.lua').read_text(encoding='utf-8')
    for source in (online, app):
        SyntaxRuntime().compile(source)
    renderer = online[online.index('function hud.drawAmmoPanel('):online.index('function hud.drawFallbackStatusWidgets(')]
    app_renderer = app[app.index('local function drawAmmoPanel('):app.index('local function drawStatusWidgets(')]
    assert renderer.strip() == app_renderer.replace('local function drawAmmoPanel(', 'function hud.drawAmmoPanel(').strip()

    lua = LuaRuntime(unpack_returned_tuples=True)
    lua.execute('''
      math.clamp=function(v,a,b) return math.max(a,math.min(b,v)) end
      local mt={}
      vec2=function(x,y) return setmetatable({x=x or 0,y=y or 0},mt) end
      mt.__add=function(a,b) return vec2(a.x+b.x,a.y+b.y) end
      mt.__sub=function(a,b) return vec2(a.x-b.x,a.y-b.y) end
      mt.__mul=function(a,b) return vec2(a.x*b,a.y*b) end
      rgbm=setmetatable({colors={white={1,1,1,1}}},{__call=function(_,r,g,b,a) return {r,g,b,a} end})
      ui={Alignment={Start=-1,Center=0},ImageFit={Fit=2},Font={Title=0}}
      for _,name in ipairs({'setCursor','text','textColored','pushFont','popFont'}) do ui[name]=function() end end
      hud={loadout={mainWeapon=1,lethal=17},maximumHealth=100}
      fpsVisual={stamina={value=100},hudWeapon={imagePath='asrc_carbine_hud.png'},
        loadoutAssetFolder='assets',weaponStats={
          [1]={capacity=40,reloadSeconds=1.8},[2]={capacity=30,reloadSeconds=1.55},
          [3]={capacity=7,reloadSeconds=1.65},[4]={capacity=8,reloadSeconds=1.35}}}
      inputSendOk=true; thirdPersonEnabled=false
      assettoRoot='assets'; weaponImagePath='asrc_carbine_hud.png'
      panel=function() end
    ''')
    lua.execute(online[online.index('hud.itemNames = {'):online.index('hud.loadoutRows = {')])
    lua.execute(app[app.index('local weaponImages = {'):app.index('local matchTypeLabels = {')]
                .replace('local weaponImages =', 'weaponImages =', 1).replace('local itemNames =', 'itemNames =', 1))
    lua.execute(renderer)
    g = lua.globals()
    records, fonts = [], []
    for name, kind in [('drawRectFilled', 'fill'), ('drawRect', 'rect'), ('drawLine', 'line'), ('drawImage', 'image')]:
        g.ui[name] = lambda *values, kind=kind: records.append((kind, values))
    g.ui.pushDWriteFont = lambda name: fonts.append(name)
    g.ui.popDWriteFont = lambda: fonts.pop()
    g.ui.dwriteDrawTextClipped = lambda *values: records.append(('text', (*values, fonts[-1] if fonts else 'Segoe UI')))

    # Exercise the actual status-to-panel adapters, including switching primary/secondary.
    draw_panel = g.hud.drawAmmoPanel
    g.captureAmmo = lambda *values: setattr(g, 'captured', values[-1])
    g.hud.drawAmmoPanel = g.captureAmmo
    lua.execute(online[online.index('function hud.drawFallbackStatusWidgets('):online.index('function hud.cycleLoadoutItem(')])
    lua.execute(app[app.index('local function drawStatusWidgets('):app.index('local function drawMatchAndFeed(')]
                .replace('local function drawStatusWidgets(', 'function appStatus(', 1)
                .replace('drawAmmoPanel(size,', 'captureAmmo(size,'))
    for weapon, capacity, duration in [(1, 40, 1.8), (2, 30, 1.55), (3, 7, 1.65), (4, 8, 1.35)]:
        actor = lua.table_from(dict(mainWeapon=weapon if weapon < 3 else 1, secondaryWeapon=weapon if weapon >= 3 else 3,
                                    activeSlot=int(weapon >= 3), ammo=capacity-2, reserveMagazines=3,
                                    health=100, reloadRemaining=duration/2, lethal=17, lethalsRemaining=0))
        g.bridge = lua.table_from(dict(localMainWeapon=actor.mainWeapon, localSecondaryWeapon=actor.secondaryWeapon,
                                      localActiveSlot=actor.activeSlot, localAmmo=actor.ammo, localReserveMagazines=3,
                                      localMagazineCapacity=capacity, localReloadDuration=duration,
                                      localReloadRemaining=duration/2, localLethal=17, localLethalsRemaining=0,
                                      localHealth=100, localMaximumHealth=100, localStamina=100,
                                      localKills=0, localDeaths=0, localScore=0, linkState=1))
        g.hud.drawFallbackStatusWidgets(g.vec2(1920, 1080), 1, 20, actor)
        fallback = dict(g.captured.items())
        g.appStatus(g.vec2(1920, 1080), 1, 20)
        companion = dict(g.captured.items())
        for field in ['weaponName','ammo','reserveMagazines','magazineCapacity','reloadRemaining',
                      'reloadDuration','lethalName','lethalsRemaining']:
            assert companion[field] == fallback[field], (weapon, field)
        assert Path(companion['imagePath']).name == Path(fallback['imagePath']).name
    g.hud.drawAmmoPanel = draw_panel

    def frame(ammo=32, mags=3, capacity=40, weapon=1, reload=0, scale=1, width=1920, height=1080):
        records.clear()
        state = lua.table_from(dict(weaponName=g.hud.itemNames[weapon], imagePath=g.weaponImages[weapon],
                                   ammo=ammo, reserveMagazines=mags, magazineCapacity=capacity,
                                   reloadRemaining=reload, reloadDuration=g.fpsVisual.weaponStats[weapon].reloadSeconds,
                                   lethalName='STICKY GRENADE', lethalsRemaining=0))
        draw_panel(g.vec2(width, height), scale, 20*scale, state)
        assert not fonts, 'Font stack leaked'
        # The alpha cutout is entirely outside the smaller 360 x 168 backing panel.
        panel_min, panel_max = next(v[:2] for kind, v in records if kind == 'fill')
        image_min, image_max = next(v[1:3] for kind, v in records if kind == 'image')
        assert round(panel_max.x-panel_min.x) == round(360*scale)
        assert round(panel_max.y-panel_min.y) == round(168*scale)
        assert abs(panel_min.x-image_max.x-12*scale) < 0.01
        assert abs(image_max.x-image_min.x-148*scale) < 0.01
        assert all(v[0].x >= panel_min.x for kind, v in records if kind in ('fill', 'rect'))
        labels = [v[0] for kind, v in records if kind == 'text']
        assert f'{ammo:02d}' in labels
        assert (f'{mags*capacity:02d}' if capacity else '--') in labels
        assert f'{mags} RESERVE MAGS' in labels
        assert (f'{ammo+mags*capacity} ROUNDS TOTAL' if capacity else '-- ROUNDS TOTAL') in labels
        return labels

    def color(c):
        return tuple(max(0, min(255, round(c[i]*255))) for i in range(1,5))

    def render(filename, scale):
        # Include the separate cutout and empty 12 px gutter; no invented backing behind it.
        panel_min = next(v[0] for k,v in records if k=='fill')
        base = g.vec2(panel_min.x-160*scale, panel_min.y)
        canvas = Image.new('RGBA', (round(520*scale), round(168*scale)), (0,0,0,0))
        ink = ImageDraw.Draw(canvas)
        for kind, v in records:
            if kind in ('fill','rect','line'):
                p,q,c = v[:3]
                box = (round(p.x-base.x),round(p.y-base.y),round(q.x-base.x),round(q.y-base.y))
                assert min(box)>=-1 and box[2]<=canvas.width+1 and box[3]<=canvas.height+1
                if kind=='line': ink.line(box, fill=color(c), width=max(1,round(v[3])))
                else:
                    radius=round(v[3] or 0) if len(v)>3 else 0
                    ink.rounded_rectangle(box, radius=radius, **({'fill':color(c)} if kind=='fill' else
                        {'outline':color(c),'width':max(1,round(v[-1] or 1))}))
            elif kind=='image':
                path,p,q=v[:3]
                asset = ROOT / 'AssettoServer.RaceControl.Core/Assets/Fps' / Path(path).name
                if not asset.exists(): asset=asset.parent/'Hud'/asset.name
                thumb=Image.open(asset).convert('RGBA')
                if len(v) > 5 and v[4] is not None:
                    uv1,uv2=v[4:6]
                    thumb=thumb.crop((round(uv1.x*thumb.width),round(uv1.y*thumb.height),
                                      round(uv2.x*thumb.width),round(uv2.y*thumb.height)))
                    thumb=thumb.resize((round(q.x-p.x),round(q.y-p.y)),Image.Resampling.LANCZOS)
                else:
                    thumb.thumbnail((round(q.x-p.x),round(q.y-p.y)),Image.Resampling.LANCZOS)
                canvas.alpha_composite(thumb,(round((p.x+q.x-thumb.width)/2-base.x),round((p.y+q.y-thumb.height)/2-base.y)))
            else:
                label,size,p,q,_,_,_,c,font_name=v
                font=ImageFont.truetype('C:/Windows/Fonts/bahnschrift.ttf' if 'Bahnschrift' in font_name else
                                       'C:/Windows/Fonts/segoeui.ttf',max(1,round(size)))
                length=ink.textlength(label,font=font)
                assert length<=q.x-p.x+2, ('Text clips horizontally',label,length,q.x-p.x)
                bounds=ink.textbbox((0,0),label,font=font)
                ink.text((p.x-base.x,(p.y+q.y-(bounds[3]-bounds[1]))/2-base.y-bounds[1]),label,font=font,fill=color(c))
        args.output.mkdir(parents=True,exist_ok=True)
        assert canvas.getpixel((0, 0))[3] == 0, 'Weapon area acquired a backing panel'
        canvas.save(args.output/filename)

    # Current screenshot, real capacities, partial magazine, low ammo, empty, reload and pickup.
    frame(); render('rifle-1080.png',1)
    frame(scale=2,width=3840,height=2160); render('rifle-detail.png',2)
    frame(scale=.75,width=1280,height=720); render('rifle-720.png',.75)
    for weapon,capacity,mags in [(1,40,4),(2,30,5),(3,7,4),(4,8,5)]:
        for ammo,reserves,reload in [(capacity,mags,0),(2,mags,0),(0,0,0),(0,1,.6),(capacity,1,0)]:
            frame(ammo,reserves,capacity,weapon,reload)
            render(f'weapon-{weapon}-ammo-{ammo}-mags-{reserves}.png',1)
    frame(capacity=0)  # No inferred reserves while waiting for authoritative stats.
    print('PASS: matching HUD layouts/adapters; all four weapons, reserve and total arithmetic;')
    print('reload, empty, low ammo, switching, font/text fit, compact panel and external alpha cutout.')
    print(f'Draw-call previews (not in-game): {args.output}')


if __name__ == '__main__':
    main()
