"""Exercise real client animation selection with real exported clip metadata."""
import argparse
import json
from pathlib import Path
import sys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--lua-runtime', type=Path, required=True)
    args = parser.parse_args()
    sys.path.insert(0, str(args.lua_runtime.resolve()))
    from lupa.luajit21 import LuaRuntime
    from lupa.lua54 import LuaRuntime as Lua54
    from validate_fps_modern_assets import inspect_ksanim
    repo = Path(__file__).resolve().parent.parent
    source = (repo / 'AssettoServer/Server/Fps/fps.lua').read_text(encoding='utf-8')
    Lua54().compile(source)
    assets = repo / 'AssettoServer.RaceControl.Core/Assets/Fps/Modern'
    manifest = json.loads((assets / 'asrc-modern-assets.json').read_text())
    lua = LuaRuntime(unpack_returned_tuples=True)
    lua.execute(source[source.index('local fpsVisual = {'):source.index('local actors = {}')]
                .replace('local fpsVisual =', 'fpsVisual =', 1))
    lua.globals().catalog = lua.table_from(manifest['operatorAnimations'], recursive=True)
    lua.execute('''
      bit=require('bit'); effectClock=0; localSessionID=99
      math.clamp=function(v,lo,hi) return math.max(lo,math.min(hi,v)) end
      math.lerp=function(a,b,t) return a+(b-a)*t end
      local vector={}
      vector.__index=vector
      function vector:clone() return vec3(self.x,self.y,self.z) end
      function vec3(x,y,z) return setmetatable({x=x or 0,y=y or 0,z=z or 0},vector) end
      ac={log=function() end}
      fpsVisual.modern=true
      fpsVisual.asset=function(name) return name end
      fpsVisual.actorStance=function(actor) return actor.stance or 0 end
      fpsVisual.operatorForActor=function() return {stanceOffsets={[1]=-0.5,[2]=-0.5}} end
      fpsVisual.operatorFailed=function(_,err) error(err) end
      io.load=function() return 'manifest' end
      io.fileExists=function() return true end
      JSON={parse=function() return {operatorAnimations=catalog} end}
      function newActor()
        local actor={id=0,render=vec3(),yaw=0,pitch=0,flags=16,actionState=0,reloadRemaining=0}
        actor.modernModel={setPosition=function() end,
          setAnimation=function(_,path,phase) actor.calls={{path,phase,1}} end,
          blendAnimation=function(_,path,phase,weight) table.insert(actor.calls,{path,phase,weight}) end}
        return actor
      end
      function tick(actor,vx,vz,dt,vy)
        actor.render.x=actor.render.x+vx*dt; actor.render.z=actor.render.z+vz*dt
        actor.render.y=actor.render.y+(vy or 0)*dt; effectClock=effectClock+dt
        assert(fpsVisual.updateActorAnimation(actor,dt))
      end
      function travel(actor,vx,vz,seconds,fps)
        for i=1,math.floor(seconds*fps+0.5) do tick(actor,vx,vz,1/fps) end
      end
      function dominant(actor) return actor.animationClip end
    ''')
    lua.execute(source[source.index('function fpsVisual.loadOperatorAnimations('):
                       source.index('function fpsVisual.updateViewmodelAnimation(')])
    lua.execute('''
      fpsVisual.loadOperatorAnimations('/assets')
      local stride=catalog.jog_forward.strideMeters
      catalog.jog_forward.strideMeters=0
      assert(not pcall(fpsVisual.loadOperatorAnimations,'/assets'))
      catalog.jog_forward.strideMeters=stride
      local path=catalog.jog_forward.file
      catalog.jog_forward.file='../untrusted.ksanim'
      assert(not pcall(fpsVisual.loadOperatorAnimations,'/assets'))
      catalog.jog_forward.file=path
      local mask=catalog.fire.trackCoverage; catalog.fire.trackCoverage='fullBody'
      assert(not pcall(fpsVisual.loadOperatorAnimations,'/assets'))
      catalog.fire.trackCoverage=mask
      fpsVisual.loadOperatorAnimations('/assets')
      -- Normal authoritative movement must never use the sprint cycle.
      for _,fps in ipairs({30,60,144}) do
        local actor=newActor(); travel(actor,0,6,2,fps)
        assert(dominant(actor)=='jog_forward',dominant(actor))
        assert(actor.animationLocomotion.weights.sprint==0)
        travel(actor,0,9,1,fps); assert(dominant(actor)=='sprint')
        travel(actor,0,6,1,fps); assert(dominant(actor)=='jog_forward')
        travel(actor,0,2.4,1,fps); assert(dominant(actor)=='walk_forward')
        travel(actor,0,0,1,fps); assert(dominant(actor)=='aim_idle')
      end
      -- All eight motion directions, including yaw-relative strafing.
      for i,name in ipairs(fpsVisual.joggingDirections) do
        local a=newActor(); local angle=(i-1)*math.pi/4
        travel(a,math.sin(angle)*6,math.cos(angle)*6,2,60)
        assert(dominant(a)==name, name..' -> '..dominant(a))
      end
      local turned=newActor(); turned.yaw=math.pi/2
      travel(turned,0,-6,2,60); assert(dominant(turned)=='strafe_right')
      -- Vertical movement and small alternating network jitter cannot start running.
      local vertical=newActor()
      for i=1,120 do tick(vertical,0,0,1/60,9) end
      assert(dominant(vertical)=='aim_idle')
      local jitter=newActor()
      for i=1,240 do tick(jitter,i%2==0 and 0.12 or -0.12,0,1/60) end
      assert(dominant(jitter)=='aim_idle')
      -- Distance, not FPS, advances the warmed-up cycle.
      local phases={}
      for _,fps in ipairs({30,60,144}) do
        local a=newActor(); travel(a,0,6,2,fps)
        a.animationLocomotion.phase=0; travel(a,0,6,1,fps)
        local expected=(6/catalog.jog_forward.strideMeters)%1
        assert(math.abs(a.animationLocomotion.phase-expected)<0.0001)
      end
      -- Direction and gait blends keep one common foot phase.
      local a=newActor(); travel(a,0,6,2,60)
      local phase=a.animationLocomotion.phase
      tick(a,6,0,1/60)
      local advance=(a.animationLocomotion.phase-phase)%1
      assert(advance>0 and advance<0.1)
      local other=false
      for _,call in ipairs(a.calls) do
        assert(math.abs(call[2]-a.animationLocomotion.phase)<0.0001)
        if call[1]:find('strafe_right') or call[1]:find('jog_forward_right') then other=true end
      end
      assert(other)
      -- Action overlays do not replace the locomotion base.
      a.reloadRemaining=0.9; travel(a,0,6,1,60)
      assert(dominant(a)=='jog_forward')
      assert(a.calls[#a.calls][1]:find('_reload.ksanim'))
      a.reloadRemaining=0; a.animationFireUntil=effectClock+0.12; tick(a,0,6,1/60)
      assert(dominant(a)=='jog_forward' and a.calls[#a.calls][1]:find('_fire.ksanim'))
      -- Crouch/prone, airborne and death retain their dedicated clips.
      a.stance=1; travel(a,0,3.4,1,60); assert(dominant(a)=='crouch_move')
      a.stance=2; travel(a,0,1.8,1,60); assert(dominant(a)=='prone_crawl')
      a.stance=0; a.flags=0; tick(a,0,6,1/60); assert(dominant(a)=='jump_start')
      travel(a,0,6,0.5,60); assert(dominant(a)=='airborne')
      a.flags=16; tick(a,0,6,1/60); assert(dominant(a)=='land')
      a.flags=18; tick(a,0,0,1/60); assert(dominant(a)=='death')
      -- Respawn/model reset, teleport and long frame gaps clear stale movement.
      a=newActor(); travel(a,0,9,2,60); a.animationLastPosition=nil
      tick(a,0,0,1/60); assert(dominant(a)=='aim_idle' and a.animationLocomotion.phase==0)
      travel(a,0,9,2,60); a.render.x=a.render.x+10; tick(a,0,0,1/60)
      assert(dominant(a)=='aim_idle' and a.animationLocomotion.phase==0)
      travel(a,0,9,2,60); tick(a,0,9,0.5)
      assert(dominant(a)=='aim_idle')
    ''')
    # Confirm the actual shipped overlays cannot alter any locomotion leg/root track.
    legs = {n for n in inspect_ksanim(assets / 'asrc_modern_operator_jog_forward.ksanim')
            if any(word in n for word in ('Hips', 'Leg', 'Foot', 'Toe', '_rootJoint'))}
    for name in ('fire', 'reload'):
        tracks = inspect_ksanim(assets / f'asrc_modern_operator_{name}.ksanim')
        assert not legs.intersection(tracks), name
        assert len(tracks) == 56
    for name, entry in manifest['operatorAnimations'].items():
        if 'directionDegrees' in entry:
            directions = {'jog_forward': 0, 'walk_forward': 0, 'sprint': 0,
                'jog_forward_right': 45, 'strafe_right': 90, 'jog_backward_right': 135,
                'walk_backward': 180, 'jog_backward_left': -135, 'strafe_left': -90,
                'jog_forward_left': -45}
            assert abs((entry['directionDegrees'] - directions[name] + 180) % 360 - 180) < 0.1
    print('PASS: catalog, 8 directions, 30/60/144 FPS, gaits, phase continuity, jitter, '
          'vertical motion, overlays, stances, traversal transitions and history resets')


if __name__ == '__main__':
    main()
