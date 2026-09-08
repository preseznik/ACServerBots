"""Run both actual CSP results renderers with LuaJIT and preview their draw calls.

Requires Pillow and lupa. Font rendering approximates CSP; live gameplay is separate.
Run: python tools/test_fps_match_results.py --lua-runtime .artifacts/lua-runtime
"""
import argparse
from pathlib import Path
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--lua-runtime', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=ROOT / '.artifacts/fps-results')
    args = parser.parse_args()
    sys.path.insert(0, str(args.lua_runtime.resolve()))
    from lupa.luajit21 import LuaRuntime
    from lupa.lua54 import LuaRuntime as SyntaxRuntime

    client = (ROOT / 'AssettoServer/Server/Fps/fps.lua').read_text(encoding='utf-8')
    app = (ROOT / 'AssettoServer.RaceControl.Core/Assets/Fps/Hud/asrc_fps_hud.lua').read_text(encoding='utf-8')
    renderers = [client[client.index('function hud.drawMatchResults('):client.index('function hud.drawCompletion(')],
                 app[app.index('local function drawMatchResults('):app.index('local function drawCompletion(')]]
    assert renderers[0].strip() == renderers[1].replace('local function drawMatchResults(', 'function hud.drawMatchResults(').strip()
    for source in [client, app]:
        SyntaxRuntime().compile(source)

    # Exercise the actual network callback: results persist, the next round clears
    # presentation state, and player-originated match packets are ignored.
    events = LuaRuntime(unpack_returned_tuples=True)
    events.execute('''
        ac = {StructItem=setmetatable({}, {__index=function() return function() return 0 end end})}
        ac.OnlineEvent = function(schema, callback) return callback end
        ui = {time=function() return 100 end}
        disposed = 0
        matchState = 2
        actors = {[0]={score=300,kills=3,deaths=1}}
        killFeed = {'old kill'}
        hitMarkerUntil = 20
        outOfBoundsRemaining = 3
        hud = {loadout={activeSlot=1,confirmed=true},awardPopups={'old award'}}
        fpsVisual = {grenades={[1]={}},pickups={[1]={root={dispose=function() disposed=disposed+1 end}}}}
        fpsVisual.removeGrenade = function(id) fpsVisual.grenades[id]=nil end
        message = {state=2,restartCountdownSeconds=10,startCountdownSeconds=0,
                   remainingSeconds=200,maximumHealth=100,killLimit=20,winnerID=0,
                   matchType=0,winnerTeam=0,team1Kills=0,team2Kills=0,weatherType=0,timeOfDaySeconds=0}
    ''')
    events.execute(client[client.index('hud.matchEvent ='):client.index('clearActorImpacts = function')])
    events.execute('''
        hud.matchEvent(nil,message)
        assert(actors[0].score==300 and #killFeed==1 and matchState==2)
        message.state=1
        hud.matchEvent({},message)
        assert(matchState==2 and actors[0].score==300)
        hud.matchEvent(nil,message)
        assert(matchState==1 and actors[0].score==0 and actors[0].kills==0 and actors[0].deaths==0)
        assert(#killFeed==0 and #hud.awardPopups==0 and hitMarkerUntil==0 and outOfBoundsRemaining==0)
        assert(hud.loadout.confirmed and hud.loadout.activeSlot==0)
        assert(next(fpsVisual.grenades)==nil and next(fpsVisual.pickups)==nil and disposed==1)
        actors[0].score=100
        hud.matchEvent(nil,message)
        assert(actors[0].score==100, 'Repeated running updates must not erase new-round scores')
    ''')

    for renderer_index, renderer in enumerate(renderers):
        lua = LuaRuntime(unpack_returned_tuples=True)
        lua.execute('''
            math.clamp = function(v,a,b) return math.max(a,math.min(b,v)) end
            local mt = {}
            vec2 = function(x,y) return setmetatable({x=x or 0,y=y or x or 0},mt) end
            mt.__add = function(a,b) return vec2(a.x+b.x,a.y+b.y) end
            mt.__sub = function(a,b) return vec2(a.x-b.x,a.y-b.y) end
            mt.__mul = function(a,b) return vec2(a.x*b,a.y*b) end
            rgbm = function(r,g,b,a) return {r,g,b,a} end
            ui = {Alignment={Start=-1,Center=0,End=1}}
            hud = {}
        ''')
        draw = lua.execute(renderer + ('\nreturn hud.drawMatchResults' if renderer_index == 0 else '\nreturn drawMatchResults'))
        calls = []
        lua.globals().ui.drawRectFilled = lambda *v: calls.append(('rect', v))
        lua.globals().ui.dwriteDrawTextClipped = lambda *v: calls.append(('text', v))
        for width, height, count, team, seconds in [(1920, 1080, 32, False, 20),
                                                  (1280, 720, 32, True, 9),
                                                  (831, 619, 16, False, 1),
                                                  (1920, 1080, 2, True, 0)]:
            rows = lua.table_from([lua.table_from(dict(id=i, name=f'Operator {i + 1:02}',
                score=(count-i)*125, kills=count-i, deaths=i, team=1+i % 2 if team else 0))
                for i in range(count)])
            calls.clear()
            draw(lua.globals().vec2(width, height), rows, 'TEAM DEATHMATCH' if team else 'FREE FOR ALL',
                 'WINNER: TEAM 1' if team else 'WINNER: Operator 01', seconds, 3,
                 'TEAM 1   75  :  62   TEAM 2' if team else None)
            labels = [v[0] for kind, v in calls if kind == 'text']
            assert f'{seconds}s' in labels and 'MATCH COMPLETE' in labels
            assert sum(label.startswith('Operator ') for label in labels) == count, 'Every participant must be visible'
            assert '32.00' in labels if count == 32 else True, 'Zero deaths must not divide by zero'
            for kind, v in calls:
                p, q = (v[0], v[1]) if kind == 'rect' else (v[2], v[3])
                assert 0 <= p.x <= q.x <= width and 0 <= p.y <= q.y <= height, (kind, p, q)
            if renderer_index == 0:
                render(calls, width, height, args.output / f'results-{width}-{count}-{"tdm" if team else "ffa"}.png')
    print('Both LuaJIT results renderers passed: all 32 rows, FFA/TDM, countdown, zero deaths, and four viewport/roster combinations.')


def render(calls, width, height, path):
    canvas = Image.new('RGBA', (width, height), (16, 23, 31, 255))
    def color(value):
        return tuple(round(max(0, min(1, value[i])) * 255) for i in range(1, 5))
    for kind, values in calls:
        if kind == 'rect':
            p, q, tint = values
            ImageDraw.Draw(canvas).rectangle((round(p.x), round(p.y), round(q.x), round(q.y)), fill=color(tint))
        else:
            label, size, p, q, alignment, _, _, tint = values
            font = ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf' if size >= 25 else
                                      'C:/Windows/Fonts/segoeui.ttf', max(1, round(size)))
            w, h = max(1, round(q.x-p.x)), max(1, round(q.y-p.y))
            layer = Image.new('RGBA', (w, h))
            ink = ImageDraw.Draw(layer)
            box = ink.textbbox((0, 0), label, font=font)
            extent = ink.textlength(label, font=font)
            x = (w-extent)/2 if alignment == 0 else w-extent if alignment == 1 else 0
            ink.text((x, (h-box[3]+box[1])/2-box[1]), label, font=font, fill=color(tint))
            canvas.alpha_composite(layer, (round(p.x), round(p.y)))
    path.parent.mkdir(parents=True, exist_ok=True)
    canvas.convert('RGB').save(path)


if __name__ == '__main__':
    main()
