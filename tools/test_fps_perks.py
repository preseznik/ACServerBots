"""Exercise real perk input/network/outline Lua and audit all FPS packet schemas."""
import argparse
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--lua-runtime', type=Path, required=True)
    options = parser.parse_args()
    sys.path.insert(0, str(options.lua_runtime.resolve()))
    from lupa.luajit21 import LuaRuntime
    from lupa.lua54 import LuaRuntime as SyntaxRuntime
    source = (ROOT / 'AssettoServer/Server/Fps/fps.lua').read_text(encoding='utf-8')
    packets = (ROOT / 'AssettoServer/Network/ClientMessages/FpsPackets.cs').read_text(encoding='utf-8')
    SyntaxRuntime().compile(source)
    transport = LuaRuntime(unpack_returned_tuples=True)
    transport.execute('''
      ac={StructItem=setmetatable({}, {__index=function() return function() return 0 end end})}
      ac.OnlineEvent=function(layout,callback,namespace,udp) registeredUdp=udp==true end
    ''')
    for name,value in re.findall(r'^local (\w+) = (\d+)\s*$', source, re.M):
        transport.globals()[name] = int(value)
    checked = 0
    for match in re.finditer(r'\[OnlineEvent\(Key = "([^"]+)"[^\]]*\)\]\s*public sealed class \w+[^\{]*\{(.*?)\n\}', packets, re.S):
        key, body = match.groups()
        start = source.index("ac.StructItem.key('" + key + "')")
        registration_start = source.rfind('ac.OnlineEvent({', 0, start)
        registration_end = re.search(r'(?:\}, function\(\) end|\nend)[^\n]*?\)', source[start:])
        assert registration_end, key
        transport.execute(source[registration_start:start+registration_end.end()])
        assert transport.globals().registeredUdp == ('Udp = true' in match[0].split('public sealed')[0]), \
            f'{key}: C#/Lua TCP/UDP registration mismatch'
        lua_body = source[start:source.index('}, function', start)]
        expected = {}
        for field in re.finditer(r'\[OnlineEventField\(Name = "([^"]+)"(?:, Size = (\w+))?\)\]\s*public (\w+)(\[\])? \w+', body):
            name, size, kind, array = field.groups()
            if kind.startswith('Fps'): kind = re.search(r'enum ' + kind + r' : (\w+)', packets)[1]
            kind = {'uint': 'uint32', 'ushort': 'uint16', 'Vector3': 'vec3', 'Vector2': 'vec2'}.get(kind, kind)
            if kind == 'string': kind = f'string({size})'
            if array:
                count = int(size) if size.isdigit() else int(re.search(r'const int ' + size + r' = (\d+)', body)[1])
                kind = f'array({kind},{count})'
            expected[name] = kind
        actual = {}
        for name, count in re.findall(r'(\w+)\s*=\s*ac.StructItem.string\((\d+)\)', lua_body):
            actual[name] = f'string({count})'
        for field in re.finditer(r'(\w+)\s*=\s*ac.StructItem\.(\w+)\((?:ac.StructItem\.(\w+)\(\),\s*(\w+))?\)', lua_body):
            name, kind, subtype, count = field.groups()
            if count and not count.isdigit(): count = re.search(r'local ' + count + r' = (\d+)', source)[1]
            actual[name] = f'array({subtype},{count})' if subtype else kind
        assert expected == actual, (key, expected, actual)
        checked += 1
    assert checked >= 20
    lua = LuaRuntime(unpack_returned_tuples=True)
    lua.execute('''
      bit=require('bit'); fpsVisual={grenades={}}; effectClock=10; gameplayActive=true
      localSessionID=0; matchState=1; teamMatch=false
      isTeamMatch=function() return teamMatch end
      math.clamp=function(v,a,b) return math.max(a,math.min(b,v)) end
      local mt={}
      vec3=function(x,y,z) return setmetatable({x=x or 0,y=y or 0,z=z or 0},mt) end
      mt.__add=function(a,b) return vec3(a.x+b.x,a.y+b.y,a.z+b.z) end
      mt.__sub=function(a,b) return vec3(a.x-b.x,a.y-b.y,a.z-b.z) end
      mt.__index={clone=function(a) return vec3(a.x,a.y,a.z) end}
      actors={[0]={id=0,flags=1,health=100,team=1,spawnCount=2},
        [1]={id=1,flags=1,health=100,team=2},[2]={id=2,flags=1,health=100,team=1}}
      hud={radarReveal={},awardPopups={},awardPopupCapacity=4}
      fpsAudio={play=function() end}; groups={}
      rgbm=function(...) return {...} end
      function ref()
        local r={count=0}
        r.append=function(self,other) self.count=self.count+other.count; return self end
        r.clear=function(self) self.count=0; return self end
        r.size=function(self) return self.count end
        r.setOutline=function(self,color) self.color=color; return self end
        r.findMeshes=function(self,filter)
          assert(filter=='?', 'CSP wildcard must select named meshes')
          return {count=self.skinned and 0 or 1}
        end
        r.findSkinnedMeshes=function(self,filter)
          assert(filter=='?', 'CSP wildcard must select named skinned meshes')
          return {count=self.skinned and 2 or 0}
        end
        r.setVisible=function(self,visible) self.visible=visible end
        r.setPosition=function(self,p) self.position=p end
        r.dispose=function(self) self.disposed=true end
        return r
      end
      actors[1].root=ref(); actors[2].root=ref()
      actors[1].modernModel=ref(); actors[1].modernModel.skinned=true
      ac={OnlineEvent=function(_,cb) return cb end,
        emptySceneReference=function() local r=ref(); groups[#groups+1]=r; return r end,
        StructItem=setmetatable({}, {__index=function() return function() end end}),
        getSim=function() return {originShift=vec3()} end,log=function() end}
    ''')
    lua.execute(source[source.index('fpsVisual.abilities = {'):source.index('hud.readyEvent =')])
    lua.execute('''
      local input=fpsVisual.fieldInput
      for _,leftFirst in ipairs({true,false}) do
        fpsVisual.abilities.input={}
        local f,g=input(.016,not leftFirst,leftFirst,false,false,true,false)
        assert(not f and not g)
        f,g=input(.016,true,true,false,false,true,false); assert(f and not g)
        for i=1,20 do f,g=input(.016,true,true,false,false,true,false); assert(f and not g) end
        f,g=input(.016,false,false,false,false,true,false); assert(not f and not g)
      end
      fpsVisual.abilities.input={}
      local f,g=input(.016,true,true,false,false,true,false); assert(f and not g)
      f,g=input(.016,false,false,false,false,true,false); assert(not f and not g, 'Chord release cannot tap-throw')
      fpsVisual.abilities.input={}
      f,g=input(.016,true,false,false,false,true,false); assert(not g)
      f,g=input(.016,false,false,false,false,true,false); assert(g, 'Quick RB tap still throws')
      for i=1,8 do f,g=input(.016,false,false,false,false,true,false) end
      assert(not g)
      fpsVisual.abilities.input={}
      for i=1,10 do f,g=input(.016,true,false,false,false,true,false) end
      assert(g and not f)
      f,g=input(.016,true,true,false,false,true,true); assert(g and not f, 'Cannot cancel a cooked grenade with the chord')
      f,g=input(.016,false,false,true,false,true,false); assert(f)
      f,g=input(.016,true,true,true,true,false,false); assert(not f and not g, 'Menus block every ability')

      local function packet(sequence,silence,vision)
        return {sequence=sequence,count=1,actorIDs={[0]=0},cooldowns={[0]=90},silence={[0]=silence},vision={[0]=vision}}
      end
      hud.abilitiesEvent({},packet(1,15,2)); assert(fpsVisual.abilities.actors[0]==nil, 'Ignore client-originated ability state')
      hud.abilitiesEvent(nil,packet(2,15,2)); fpsVisual.updateAbilities(.01)
      assert(actors[0].deadSilenceRemaining==15 and actors[0].visionRemaining==2)
      hud.abilitiesEvent(nil,packet(1,0,0)); fpsVisual.updateAbilities(.01)
      assert(actors[0].deadSilenceRemaining==15, 'Ignore out-of-order state')
      effectClock=11; fpsVisual.updateAbilities(.01)
      assert(actors[0].deadSilenceRemaining==0, 'Stale state cannot leave silence active')
      effectClock=12; hud.abilitiesEvent(nil,packet(3,0,2)); fpsVisual.updateAbilities(.01)
      actors[0].perkMask=bit.bor(bit.lshift(1,3),bit.lshift(1,4))
      teamMatch=true
      local gadget=ref()
      fpsVisual.abilities.devices[7]={id=7,type=3,ownerID=1,position=vec3(),root=gadget,expiresAt=40}
      fpsVisual.updateFieldVisuals()
      assert(fpsVisual.abilities.outlines.vision==nil, 'Vision no longer depends on mesh outlines')
      assert(fpsVisual.abilities.outlines.engineer.count==1)
      local oldEngineer=fpsVisual.abilities.outlines.engineer
      local groupCount=#groups
      fpsVisual.updateFieldVisuals()
      assert(fpsVisual.abilities.outlines.engineer==oldEngineer and #groups==groupCount,
        'Retain owning outline references across frames')
      actors[1].modernModel=nil
      fpsVisual.updateFieldVisuals()
      actors[0].health=0; fpsVisual.updateFieldVisuals()
      assert(oldEngineer.color==nil and next(fpsVisual.abilities.outlines)==nil, 'Death clears outlines')
      actors[0].health=100; gameplayActive=false; fpsVisual.updateFieldVisuals()
      assert(gadget.visible==false, 'Menus hide gadgets')
      gameplayActive=true; actors[0].visionRemaining=0; fpsVisual.updateFieldVisuals()
      assert(fpsVisual.abilities.outlines.vision==nil, 'Vision expires independently of Engineer')
      fpsVisual.resetAbilities()
      assert(gadget.disposed and next(fpsVisual.abilities.devices)==nil)
      local grenade=ref(); fpsVisual.grenades[17]={root=grenade}
      hud.fieldDeviceEvent(nil,{deviceID=7,ownerID=1,type=3,state=3,position=vec3(),
        health=100,uses=2,remaining=30,actorID=1,grenadeID=17,target=vec3()})
      assert(grenade.disposed and fpsVisual.grenades[17]==nil)
      assert(fpsVisual.abilities.intercepted[17]>effectClock)
      assert(#fpsVisual.abilities.effects==1)
      fpsVisual.updateAbilities(.3); assert(#fpsVisual.abilities.effects==0)
      hud.fieldDeviceEvent(nil,{deviceID=7,state=2})
      assert(fpsVisual.abilities.devices[7]==nil)
    ''')
    lua.execute(source[source.index('function hud.drawVision('):source.index('function hud.drawAimNameplate(')])
    lua.execute('''
      vec2=function(x,y) return {x=x,y=y} end
      ac.getCameraPosition=function() return vec3(0,1.62,0) end
      ac.getCameraForward=function() return vec3(0,0,1) end
      render={ProjectFace={Center=0},projectPoint=function(p)
        return vec2(.5+p.x/p.z,.5-(p.y-1.62)/p.z)
      end}
      rects={}; ui={drawRect=function(p,q,c) rects[#rects+1]={p=p,q=q,c=c} end}
      actors[0].health=100; actors[0].visionRemaining=2; actors[0].perkMask=bit.lshift(1,3)
      actors[1].render=vec3(0,0,10); actors[1].root=nil; actors[1].modernModel=nil
      actors[2].render=vec3(3,0,10)
      actors[3]={id=3,flags=1,health=100,team=2,render=vec3(0,0,-10)}
      actors[4]={id=4,flags=3,health=0,team=2,render=vec3(1,0,10)}
      local function frame()
        rects={}; hud.drawVision(vec2(1920,1080),1); return #rects
      end
      assert(frame()==2, 'One enemy rectangle with black backing, no mesh dependency')
      local height=rects[1].q.y-rects[1].p.y
      actors[1].flags=33; assert(frame()==2)
      assert(rects[1].q.y-rects[1].p.y<height, 'Bounds follow crouch height')
      actors[1].flags=1; teamMatch=false; assert(frame()==4, 'FFA highlights all opponents')
      teamMatch=true; actors[1].render.x=1000; assert(frame()==0, 'Offscreen enemies are not clamped into fake boxes')
      actors[1].render.x=0
      cursorUnlocked=true; assert(frame()==0); cursorUnlocked=false
      scoreboardHeld=true; assert(frame()==0); scoreboardHeld=false
      gameplayActive=false; assert(frame()==0); gameplayActive=true
      actors[0].health=0; assert(frame()==0); actors[0].health=100
      actors[0].visionRemaining=0; assert(frame()==0); actors[0].visionRemaining=2
      actors[0].perkMask=0; assert(frame()==0); actors[0].perkMask=bit.lshift(1,3)
      render.projectPoint=function() return vec2(0/0,0/0) end; assert(frame()==0)
      render.projectPoint=function() error('projection unavailable') end; assert(frame()==0)

      -- Exercise the real timer receiver with authoritative packets over a full charge.
      fpsVisual.resetAbilities()
      for tick=0,2400 do
        effectClock=100+tick*.05
        hud.abilitiesEvent(nil,{sequence=tick+1,count=1,actorIDs={[0]=0},
          cooldowns={[0]=math.max(0,120-tick*.05)},silence={[0]=0},vision={[0]=math.max(0,2-tick*.05)}})
        fpsVisual.updateAbilities(.05)
        assert(math.abs(actors[0].fieldUpgradeCooldown-math.max(0,120-tick*.05))<.001)
        if tick==20 then assert(actors[0].visionRemaining>.9) end
      end
      assert(actors[0].fieldUpgradeCooldown==0 and actors[0].visionRemaining==0)
    ''')
    lua.execute(source[source.index('hud.loadoutStateEvent ='):source.index('fpsVisual.abilities = {')])
    lua.execute('''
      hud.loadout={mainWeapon=1,lethal=17,secondaryWeapon=3,fieldUpgrade=2,
        perk1=2,perk2=3,perk3=7,queued=true}
      hud.loadoutStorage={mainWeapon=1,lethal=17,secondaryWeapon=3,fieldUpgrade=2,
        perk1=2,perk2=3,perk3=7}
      local state={actorID=0,mainWeapon=1,lethal=17,secondaryWeapon=3,activeSlot=1,
        lethalsRemaining=1,fieldUpgrade=1,perk1=0,perk2=0,perk3=0}
      hud.loadoutStateEvent(nil,state)
      assert(hud.loadout.queued and hud.loadout.fieldUpgrade==2 and hud.loadout.perk3==7,
        'Switching weapons must not overwrite next-spawn choices')
      assert(actors[0].fieldUpgrade==1 and actors[0].perkMask==0,
        'Pending perks must not activate early')
      state.fieldUpgrade=2; state.perk1=2; state.perk2=3; state.perk3=7
      hud.loadoutStateEvent(nil,state)
      assert(not hud.loadout.queued and fpsVisual.hasPerk(actors[0],7))
      hud.loadout.dirty=true; hud.loadout.fieldUpgrade=3
      hud.loadoutStateEvent(nil,state)
      assert(hud.loadout.fieldUpgrade==3, 'Do not overwrite unconfirmed menu edits')
    ''')
    from PIL import Image
    assets = ROOT / 'AssettoServer.RaceControl.Core/Assets/Fps'
    icons = [*assets.glob('asrc_perk_*.png'), *assets.glob('asrc_upgrade_*.png')]
    assert len(icons) == 10
    for path in icons:
        icon = Image.open(path)
        assert icon.mode == 'RGBA' and icon.getextrema()[3] == (0, 255), path
    from validate_fps_modern_assets import inspect_kn5
    for name, height in [('munitions_box', .539), ('trophy_system', .6)]:
        model = inspect_kn5(assets / f'asrc_{name}.kn5')
        assert 0 < model.triangles <= 6000 and model.rigid_meshes == 1
        assert model.shaders == ('ksPerPixel',) and model.material_blend_modes == (0,)
        assert all(w == h == 1024 for _,w,h in model.texture_dimensions)
        _,_,minimum,maximum = model.mesh_bounds[0]
        assert abs(minimum[1]) < .001 and abs(maximum[1]-height) < .001, 'Grounded metre-scale pivot'
    assert "* (fpsVisual.hasPerk(localActor, 1) and 1.15 or 1)" in source
    assert 'if fpsVisual.hasPerk(actor, 2) then volume = volume * 0.15 end' in source
    assert 'if message.suppressed ~= 1 then' in source
    print(f'PASS: {checked} C#/Lua packet schemas AND TCP/UDP registrations; controller chord/tap/cook guards; '
          '120-second charge replication, Vision rectangles and gameplay/projection gates, Engineer cleanup, '
          'Trophy removal/tombstones, queued selections, ten transparent icons, two KN5 props.')


if __name__ == '__main__':
    main()
