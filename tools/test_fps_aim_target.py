"""Exercise hip/ADS target selection and both HUD render paths with LuaJIT.

Draw-call previews approximate CSP fonts; they do not replace in-game acceptance.
"""
import argparse
from pathlib import Path
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--lua-runtime', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=ROOT / '.artifacts/fps-aim-target')
    args = parser.parse_args()
    sys.path.insert(0, str(args.lua_runtime.resolve()))
    from lupa.luajit21 import LuaRuntime
    from lupa.lua54 import LuaRuntime as SyntaxRuntime

    client = (ROOT / 'AssettoServer/Server/Fps/fps.lua').read_text(encoding='utf-8')
    app = (ROOT / 'AssettoServer.RaceControl.Core/Assets/Fps/Hud/asrc_fps_hud.lua').read_text(encoding='utf-8')
    for source in (client, app):
        SyntaxRuntime().compile(source)
    online_schema = client[client.index("ac.StructItem.key('asrc.fps.hud.v14')"):
                           client.index('}, false, ac.SharedNamespace.Shared)')]
    app_schema = app[app.index("ac.StructItem.key('asrc.fps.hud.v14')"):
                     app.index('}, false, ac.SharedNamespace.Shared)')]
    for old, new in [('hud.capacity', 'actorCapacity'), ('hud.grenadeCapacity', 'grenadeCapacity'),
                     ('hud.killFeedCapacity', 'killFeedCapacity'), ('hud.awardPopupCapacity', 'awardPopupCapacity')]:
        online_schema = online_schema.replace(old, new)
    assert ''.join(online_schema.split()) == ''.join(app_schema.split()), 'Shared bridge layout drifted'
    renderer = client[client.index('function hud.drawAimNameplate('):client.index('function hud.drawAimTarget(')]
    app_renderer = app[app.index('local function drawAimNameplate('):app.index('local function drawAimTarget(')]
    assert renderer.strip() == app_renderer.replace('local function drawAimNameplate(',
                                                    'function hud.drawAimNameplate(').strip()

    lua = LuaRuntime(unpack_returned_tuples=True)
    lua.execute('''
        bit=require('bit')
        math.clamp=function(v,a,b) return math.max(a,math.min(b,v)) end
        local v3={}; local v2={}
        vec3=function(x,y,z) return setmetatable({x=x or 0,y=y or 0,z=z or 0},v3) end
        vec2=function(x,y) return setmetatable({x=x or 0,y=y or 0},v2) end
        v3.__add=function(a,b) return vec3(a.x+b.x,a.y+b.y,a.z+b.z) end
        v3.__sub=function(a,b) return vec3(a.x-b.x,a.y-b.y,a.z-b.z) end
        v3.__index={lengthSquared=function(a) return a.x*a.x+a.y*a.y+a.z*a.z end}
        v2.__add=function(a,b) return vec2(a.x+b.x,a.y+b.y) end
        v2.__sub=function(a,b) return vec2(a.x-b.x,a.y-b.y) end
        v2.__mul=function(a,b) return vec2(a.x*b,a.y*b) end
        rgbm=function(r,g,b,a) return {r,g,b,a} end
        ui={Alignment={Center=0},time=function() return 20 end}
        camera={active=function() return true end,
            transform={position=vec3(0,1.65,0),look=vec3(0,0,1)}}
        ac={getCameraPosition=function() return camera.transform.position end,
            getCameraForward=function() return camera.transform.look end}
        projected=vec2(0.5,0.45)
        render={ProjectFace={Center=0},projectPoint=function() return projected end}
        obstruction=-1; raycasts=0
        physics={raycastTrack=function() raycasts=raycasts+1; return obstruction end}
        function actor(id,z,team,flags)
            return {id=id,render=vec3(0,0,z),target=vec3(100,0,z),team=team,
                flags=flags or 1,health=75,reloadRemaining=0}
        end
        actors={[0]=actor(0,0,1),[1]=actor(1,10,1),[2]=actor(2,20,2)}
        names={[1]='Friendly Ghost',[2]='Enemy Officer'}
        localSessionID=0; localStance=0; gameplayActive=true; matchState=1
        cursorUnlocked=false; scoreboardHeld=false; thirdPersonEnabled=false; viewmodelSprint=false
        fpsVisual={ads=1,adsInput=1}; hud={loadout={confirmed=true},bridge={},maximumHealth=100}
        teamMatch=true; isTeamMatch=function() return teamMatch end
    ''')
    lua.execute(client[client.index('function fpsVisual.actorStance('):client.index('function fpsVisual.actorScenePose(')])
    lua.execute(client[client.index('function hud.aimCapsuleDistance('):client.index('function hud.publish(dt)')])
    lua.execute(renderer)
    lua.execute(client[client.index('function hud.drawAimTarget('):client.index('function hud.drawAwardPopups(')])
    lua.execute('''
        hud.updateAimTarget(); assert(hud.aimTarget.id==1 and raycasts==1)
        assert(math.abs(hud.aimTargetPosition.y-2)<1e-6)
        hud.publishAimTarget(20); assert(hud.bridge.aimTargetID==1 and hud.bridge.aimTargetUpdatedAt==20)
        assert(hud.bridge.aimTargetPosition==hud.aimTargetPosition)
        obstruction=5; hud.updateAimTarget(); assert(hud.aimTarget==nil)
        hud.publishAimTarget(21); assert(hud.bridge.aimTargetID==255)
        obstruction=-1; actors[1].flags=3; hud.updateAimTarget(); assert(hud.aimTarget.id==2)
        actors[1].flags=0; hud.updateAimTarget(); assert(hud.aimTarget.id==2)
        actors[1].flags=1; actors[1].health=0; hud.updateAimTarget(); assert(hud.aimTarget.id==2)
        actors[1].health=75; actors[1].render.x=2; hud.updateAimTarget(); assert(hud.aimTarget.id==2)
        actors[1].render.x=0
        camera.transform.look=vec3(1,0,0); hud.updateAimTarget(); assert(hud.aimTarget==nil)
        camera.transform.look=vec3(0,0,1)
        actors[1].render.z=130; actors[2].render.z=140; hud.updateAimTarget(); assert(hud.aimTarget==nil)
        actors[1].render.z=10; actors[2].render.z=20
        actors[1].flags=33; camera.transform.position.y=1.05
        hud.updateAimTarget(); assert(hud.aimTarget.id==1 and math.abs(hud.aimTargetPosition.y-1.35)<1e-6)
        actors[1].flags=129; camera.transform.position.y=0.42
        hud.updateAimTarget(); assert(hud.aimTarget.id==1 and math.abs(hud.aimTargetPosition.y-0.85)<1e-6)
        actors[1].flags=1; camera.transform.position.y=1.65
        fpsVisual.adsInput=0; fpsVisual.ads=0
        hud.updateAimTarget(); assert(hud.aimTarget.id==1, 'Hip aim must identify targets')
        local gates={
          {'cursorUnlocked=true','cursorUnlocked=false'}, {'scoreboardHeld=true','scoreboardHeld=false'},
          {'thirdPersonEnabled=true','thirdPersonEnabled=false'}, {'viewmodelSprint=true','viewmodelSprint=false'},
          {'matchState=2','matchState=1'}, {'gameplayActive=false','gameplayActive=true'},
          {'actors[0].health=0','actors[0].health=75'}, {'actors[0].flags=3','actors[0].flags=1'},
          {'actors[0].reloadRemaining=1','actors[0].reloadRemaining=0'},
          {'fpsVisual.activeGrenadeType=16','fpsVisual.activeGrenadeType=nil'},
          {'hud.loadout.confirmed=false','hud.loadout.confirmed=true'}}
        for _, gate in ipairs(gates) do
          hud.updateAimTarget(); assert(hud.aimTarget~=nil)
          loadstring(gate[1])(); hud.updateAimTarget(); assert(hud.aimTarget==nil,gate[1])
          loadstring(gate[2])()
        end
        local raycast=physics.raycastTrack
        physics.raycastTrack=function() error('unavailable') end
        hud.updateAimTarget(); assert(hud.aimTarget==nil)
        physics.raycastTrack=raycast; hud.updateAimTarget()
    ''')

    calls = []
    lua.globals().ui.drawRectFilled = lambda *values: calls.append(('rect', values))
    lua.globals().ui.dwriteDrawTextClipped = lambda *values: calls.append(('text', values))
    lua.execute('''
        bridge={aimTargetID=1,aimTargetPosition=vec3(0,2,10),aimTargetUpdatedAt=20,
            adsActive=0,cursorUnlocked=0,scoreboardHeld=0,matchState=1,localHealth=75,
            localMaximumHealth=100,localActorID=0,actorCount=2,
            actorIDs={[0]=0,[1]=1},actorHealth={[0]=75,[1]=75},
            actorFlags={[0]=1,[1]=1},actorTeams={[0]=1,[1]=1}}
        actorCapacity=32; localActorIndex=function() return 0 end
        actorName=function(index) return names[bridge.actorIDs[index]] end
    ''')
    app_draw = lua.execute(app_renderer + app[app.index('local function drawAimTarget('):app.index('local function drawAim(size')]
                           + '\nreturn drawAimTarget')
    fallback_draw = lua.globals().hud.drawAimTarget
    args.output.mkdir(parents=True, exist_ok=True)
    for draw_index, draw in enumerate((fallback_draw, app_draw)):
        for friendly in (True, False):
            lua.globals().teamMatch = friendly
            calls.clear()
            draw(lua.globals().vec2(1280, 720), 1)
            assert len(calls) == 5
            color = calls[-1][1][2]
            assert (color[3] > color[1]) == friendly, 'Friendlies must be blue, enemies red'
            bar_min, bar_max = calls[-1][1][:2]
            assert abs(bar_max.x - bar_min.x - 124 * .75) < 1e-5
            image = Image.new('RGB', (1280, 720), (34, 39, 45))
            ink = ImageDraw.Draw(image)
            for kind, values in calls:
                if kind == 'rect':
                    p, q, color, radius = values
                    ink.rounded_rectangle((p.x, p.y, q.x, q.y), radius=radius,
                                          fill=tuple(round(color[i] * 255) for i in range(1, 4)))
                else:
                    label, size, p, q, _, _, _, color = values
                    font = ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf', round(size))
                    ink.text(((p.x+q.x)/2, (p.y+q.y)/2), label, anchor='mm', font=font,
                             fill=tuple(round(color[i] * 255) for i in range(1, 4)))
            image.save(args.output / f'{"fallback" if draw_index == 0 else "app"}-{"friendly" if friendly else "enemy"}.png')
        calls.clear()
        lua.execute('projected=vec2(1.2,0.5)')
        draw(lua.globals().vec2(1280,720),1); assert not calls
        lua.execute('projected=vec2(0/0,0.5)')
        draw(lua.globals().vec2(1280,720),1); assert not calls
        lua.execute('projected=vec2(0.5,0.45)')
    for expression, undo in [('bridge.aimTargetUpdatedAt=19', 'bridge.aimTargetUpdatedAt=20'),
                              ('bridge.aimTargetID=255', 'bridge.aimTargetID=1'),
                              ('bridge.actorHealth[1]=0', 'bridge.actorHealth[1]=75'),
                              ('bridge.scoreboardHeld=1', 'bridge.scoreboardHeld=0')]:
        calls.clear(); lua.execute(expression)
        app_draw(lua.globals().vec2(1280,720),1); assert not calls
        lua.execute(undo)
    print('PASS: hip/ADS aiming, gameplay gates, nearest live actor, stance capsules, range, cover, failed visibility checks, '
          'per-frame bridge clearing, mirrored layout, friendly/enemy colors, health ratio, stale target and projection guards.')
    print(f'Draw-call previews: {args.output.resolve()}')


if __name__ == '__main__':
    main()
