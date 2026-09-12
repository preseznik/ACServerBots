"""Exercise confirmed-kill medals and render real HUD draw calls, not a CSP capture."""
import argparse
from pathlib import Path
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--lua-runtime', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=ROOT / '.artifacts/lethal-medal')
    args = parser.parse_args()
    sys.path.insert(0, str(args.lua_runtime.resolve()))
    from lupa.luajit21 import LuaRuntime
    from lupa.lua54 import LuaRuntime as SyntaxRuntime

    online = (ROOT / 'AssettoServer/Server/Fps/fps.lua').read_text(encoding='utf-8')
    app = (ROOT / 'AssettoServer.RaceControl.Core/Assets/Fps/Hud/asrc_fps_hud.lua').read_text(encoding='utf-8')
    for source in (online, app):
        SyntaxRuntime().compile(source)
    renderer = online[online.index('function hud.drawLethalMedal('):online.index('function hud.drawAwardPopups(')]
    companion = app[app.index('local function drawLethalMedal('):app.index('local function drawAwards(')]
    assert renderer.strip() == companion.replace('local function drawLethalMedal(', 'function hud.drawLethalMedal(').strip()

    lua = LuaRuntime(unpack_returned_tuples=True)
    lua.execute("""
      localSessionID=5; killFeed={}; names={}; actors={}; effectClock=0
      hud={lethalMedalDeaths={},bridge={},itemNames={}}
      clearActorImpacts=function() end
      fpsAudio={playDeath=function() end}
      ac={OnlineEvent=function(_,callback) return callback end,
        StructItem=setmetatable({}, {__index=function() return function() end end})}
      math.clamp=function(v,a,b) return math.max(a,math.min(b,v)) end
      local mt={}
      vec2=function(x,y) return setmetatable({x=x or 0,y=y or 0},mt) end
      mt.__add=function(a,b) return vec2(a.x+b.x,a.y+b.y) end
      mt.__mul=function(a,b) return vec2(a.x*b,a.y*b) end
      rgbm=function(r,g,b,a) return {r,g,b,a} end
      ui={Alignment={Center=0},ImageFit={Fit=2},Font={Title=0}}
      for _,name in ipairs({'setCursor','textColored','pushFont','popFont'}) do ui[name]=function() end end
    """)
    lua.execute(online[online.index('function hud.onLethalKill('):online.index('hud.hitEvent =')])
    lua.execute(renderer)
    g = lua.globals()

    def kill(item=16, killer=5, victim=8, deaths=1, sender=None):
        g.hud.killEvent(sender, lua.table_from(dict(itemID=item, killerID=killer, victimID=victim,
                                                  victimDeaths=deaths, killerKills=1)))

    # Only the server's confirmed lethal kills for this player can award the medal.
    for item in (0, 1, 2, 3, 4):
        kill(item=item)
    kill(killer=7)
    kill(killer=255)
    kill(victim=5)
    kill(victim=255)
    kill(sender=lua.table())
    assert g.hud.lethalMedal is None

    for item in (16, 17):
        g.hud.lethalMedalDeaths = lua.table()
        kill(item=item)
        assert g.hud.lethalMedal.count == 1 and g.hud.lethalMedal.itemID == item
        kill(item=item)  # Duplicate confirmed packet is not a second elimination.
        assert g.hud.lethalMedal.count == 1
        g.hud.updateLethalMedal(0.1)
        kill(item=item, victim=9)
        assert g.hud.lethalMedal.count == 2
        g.hud.updateLethalMedal(0.5)
        kill(item=item, victim=10)
        assert g.hud.lethalMedal.count == 1  # Separate later kill replaces the banner.
        g.hud.updateLethalMedal(0.6)
        kill(item=item, deaths=2)  # Same victim's next life can award again.
        assert g.hud.lethalMedal.count == 1
        g.hud.updateLethalMedal(0.2)
        g.hud.publishLethalMedal()
        assert g.hud.bridge.lethalMedalItem == item
        assert g.hud.bridge.lethalMedalCount == 1
        assert abs(g.hud.bridge.lethalMedalAge-0.2) < 0.0001
        g.hud.updateLethalMedal(2.81)
        assert g.hud.lethalMedal is None
        g.hud.publishLethalMedal()
        assert g.hud.bridge.lethalMedalCount == g.hud.bridge.lethalMedalItem == g.hud.bridge.lethalMedalAge == 0

    # Execute the reset assignments from the actual new-round branch.
    match = online[online.index('hud.matchEvent ='):online.index('function hud.onLethalKill(')]
    reset = match[match.index('    killFeed = {}'):match.index('    hitMarkerUntil = 0')]
    kill(deaths=3)
    lua.execute(reset)
    assert g.hud.lethalMedal is None and list(g.hud.lethalMedalDeaths.items()) == []
    kill(deaths=1)
    assert g.hud.lethalMedal.count == 1
    assert 'hud.updateLethalMedal(dt)\n  hud.publish(dt)' in online
    assert 'hud.publishLethalMedal()\n  local popupCount' in online
    assert 'hud.onLethalKill' not in online[online.index('hud.awardEvent ='):online.index('fpsVisual.pickupEvent =')]

    # Both real render adapters receive the same item/count/age and hide under menus/scoreboard.
    captured = []
    g.drawLethalMedal = lambda *values: captured.append(values)
    g.weaponImages = lua.table_from({16: 'asrc_loadout_frag_grenade.png', 17: 'asrc_loadout_sticky_grenade.png'})
    g.awardPopupCapacity = 4
    g.bridge = lua.table_from(dict(cursorUnlocked=0, scoreboardHeld=0, awardPopupCount=0,
                                  lethalMedalItem=17, lethalMedalCount=2, lethalMedalAge=0.2))
    lua.execute(app[app.index('local function drawAwards('):app.index('local function drawScoreboard(')]
                .replace('local function drawAwards(', 'function appAwards(', 1))
    g.appAwards(g.vec2(1920, 1080), 1)
    assert captured[-1][2:5] == (17, 2, 0.2)
    for field in ('cursorUnlocked', 'scoreboardHeld'):
        captured.clear()
        g.bridge[field] = 1
        g.appAwards(g.vec2(1920, 1080), 1)
        assert not captured
        g.bridge[field] = 0
    fallback_start = online.index('  if not cursorUnlocked and not scoreboardHeld and hud.lethalMedal ~= nil then')
    fallback = online[fallback_start:online.index('  hud.drawAwardPopups(center)', fallback_start)]
    draw_medal = g.hud.drawLethalMedal
    g.hud.drawLethalMedal = g.drawLethalMedal
    g.fpsVisual = lua.table_from(dict(loadoutAssetFolder='assets'))
    g.size, g.hudScale = g.vec2(1920, 1080), 1
    g.hud.lethalMedal = lua.table_from(dict(itemID=17, count=2, age=0.2))
    lua.execute(fallback)
    assert captured[-1][2:5] == (17, 2, 0.2)
    assert Path(captured[-1][-1]).name == 'asrc_loadout_sticky_grenade.png'
    for field in ('cursorUnlocked', 'scoreboardHeld'):
        captured.clear()
        g[field] = True
        lua.execute(fallback)
        assert not captured
        g[field] = False
    g.hud.drawLethalMedal = draw_medal

    records, fonts = [], []
    for name in ('drawRectFilled', 'drawCircleFilled', 'drawCircle', 'drawLine', 'drawImage'):
        g.ui[name] = lambda *values, kind=name: records.append((kind, values))
    g.ui.pushDWriteFont = lambda name: fonts.append(name)
    g.ui.popDWriteFont = lambda: fonts.pop()
    g.ui.dwriteDrawTextClipped = lambda *values: records.append(('text', (*values, fonts[-1])))

    def frame(item=16, count=1, age=0.5, scale=1, image=True):
        records.clear()
        draw_medal(g.vec2(1920, 1080), scale, item, count, age, g.weaponImages[item] if image else None)
        assert not fonts
        labels = [v[0] for k,v in records if k == 'text']
        if 0 < age < 3 and count > 0 and item in (16, 17):
            assert 'GRENADE KILL' in labels
            assert (f'{count} ELIMINATIONS' if count > 1 else
                    'FRAG GRENADE' if item == 16 else 'STICKY GRENADE') in labels
        else:
            assert not records

    def color(c):
        return tuple(max(0,min(255,round(c[i]*255))) for i in range(1,5))

    def render(filename, scale):
        canvas = Image.new('RGBA', (round(300*scale), round(158*scale)), (0,0,0,0))
        ink = ImageDraw.Draw(canvas)
        origin = (960-150*scale, 52*scale)
        def point(p):
            return round(p.x-origin[0]), round(p.y-origin[1])
        for kind, v in records:
            if kind in ('drawCircle', 'drawCircleFilled'):
                p,r,c = v[:3]
                x,y = point(p)
                box=(x-r,y-r,x+r,y+r)
                ink.ellipse(box, **({'fill':color(c)} if kind.endswith('Filled') else
                            {'outline':color(c),'width':max(1,round(v[-1]))}))
            elif kind == 'drawLine':
                ink.line((*point(v[0]),*point(v[1])),fill=color(v[2]),width=max(1,round(v[3])))
            elif kind == 'drawRectFilled':
                ink.rounded_rectangle((*point(v[0]),*point(v[1])),radius=round(v[3]),fill=color(v[2]))
            elif kind == 'drawImage':
                asset=ROOT/'AssettoServer.RaceControl.Core/Assets/Fps'/Path(v[0]).name
                thumb=Image.open(asset).convert('RGBA')
                p,q=v[1:3]
                uv1,uv2=v[4:6]
                thumb=thumb.crop((round(uv1.x*thumb.width),round(uv1.y*thumb.height),
                                  round(uv2.x*thumb.width),round(uv2.y*thumb.height)))
                thumb=thumb.resize((round(q.x-p.x),round(q.y-p.y)),Image.Resampling.LANCZOS)
                canvas.alpha_composite(thumb,(round((p.x+q.x-thumb.width)/2-origin[0]),
                                              round((p.y+q.y-thumb.height)/2-origin[1])))
            else:
                label,size,p,q,_,_,_,c,font_name=v
                font=ImageFont.truetype('C:/Windows/Fonts/bahnschrift.ttf' if 'Bahnschrift' in font_name
                                       else 'C:/Windows/Fonts/segoeui.ttf',max(1,round(size)))
                width=ink.textlength(label,font=font)
                assert width <= q.x-p.x+2, ('Text clips',label)
                bounds=ink.textbbox((0,0),label,font=font)
                ink.text(((p.x+q.x-width)/2-origin[0],(p.y+q.y-(bounds[3]-bounds[1]))/2-origin[1]-bounds[1]),
                         label,font=font,fill=color(c))
        args.output.mkdir(parents=True,exist_ok=True)
        canvas.save(args.output/filename)

    for scale in (0.75, 1, 1.65):
        frame(scale=scale)
        render(f'frag-{scale}.png',scale)
    frame(item=17,count=3,scale=2); render('sticky-multikill-detail.png',2)
    frame(image=False); render('missing-thumbnail.png',1)
    frame(age=0.08)
    assert abs(next(v[2][4] for k,v in records if k == 'drawCircle') - 0.5) < 0.001
    frame(age=2.75)
    assert abs(next(v[2][4] for k,v in records if k == 'drawCircle') - 0.5) < 0.001
    for args_ in (dict(age=0), dict(age=3), dict(count=0), dict(item=1)):
        frame(**args_)
    print('PASS: server-only lethal kills; no assists/self/remote/gun medals; duplicate suppression;')
    print('burst kills, new victim life, expiry/reset, bridge and render parity, menu hiding, fades and text fit.')
    print(f'Draw-call previews (not in-game): {args.output}')


if __name__ == '__main__':
    main()
