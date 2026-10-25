"""Exercise real FPS bindings/menu Lua and render controls draw calls without desktop control."""
import argparse
from pathlib import Path
import sys
from PIL import Image, ImageDraw, ImageFont


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--lua-runtime', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path('.artifacts/controller-bindings'))
    args = parser.parse_args()
    sys.path.insert(0, str(args.lua_runtime.resolve()))
    from lupa.luajit21 import LuaRuntime
    from lupa.lua54 import LuaRuntime as SyntaxRuntime
    repo = Path(__file__).resolve().parents[1]
    source = (repo / 'AssettoServer/Server/Fps/fps.lua').read_text(encoding='utf-8')
    SyntaxRuntime().compile(source)
    lua = LuaRuntime(unpack_returned_tuples=True)
    lua.execute('''
      bit=require('bit'); math.clamp=function(v,a,b) return math.max(a,math.min(b,v)) end
      math.lerp=function(a,b,t) return a+(b-a)*t end
      local v={}; vec2=function(x,y) return setmetatable({x=x or 0,y=y or x or 0},v) end
      v.__add=function(a,b) return vec2(a.x+b.x,a.y+b.y) end
      v.__sub=function(a,b) return vec2(a.x-b.x,a.y-b.y) end
      v.__mul=function(a,b) return vec2(a.x*b,a.y*b) end
      v.__index={lengthSquared=function(a) return a.x*a.x+a.y*a.y end}
      rgbm=function(r,g,b,a) return {r,g,b,a} end
      keys={}; pressed={}; pad={}; axes={}; storage={}; now=0; pauseCalls=0
      sim={isPaused=false,isLive=true,isInMainMenu=false,isReplayActive=false}
      previewCamera={everEnteredGameplay=true}
      fpsVisual={controllerSprint={latched=false},abilities={input={}}}
      hud={bindingCaptureDevice='keyboard',bindingCapturePrevious={},gamepadMenuWasDown=false,
        pausePage='controls',pauseInputLogged=true,nativePauseMenu=false}
      ac={KeyIndex={LeftButton=1,RightButton=2,MiddleButton=4,XButton1=5,XButton2=6,Space=32,
          LeftShift=160,RightShift=161,LeftControl=162,RightControl=163,LeftMenu=164,RightMenu=165,
          Tab=9,Return=13,Back=8,Left=37,Right=39,Up=38,Down=40,D0=48,D9=57,A=65,Z=90,
          F1=112,F12=123,Escape=27,C=67,R=82,G=71,F=70,V=86,Q=81,X=88},
        GamepadButton={DPadUp=1,DPadDown=2,DPadLeft=4,DPadRight=8,Start=16,Back=32,
          LeftThumb=64,RightThumb=128,LeftShoulder=256,RightShoulder=512,A=4096,B=8192,X=16384,Y=32768},
        GamepadAxis={LeftTrigger=0,RightTrigger=1},log=function() end,
        isKeyDown=function(k) return keys[k]==true end,isKeyPressed=function(k) return pressed[k]==true end,
        isGamepadButtonPressed=function(_,b) return pad[b]==true end,
        getGamepadAxisValue=function(_,a) return axes[a] or 0 end,getSim=function() return sim end,
        tryToPause=function(p) pauseCalls=pauseCalls+1; sim.isPaused=p; return true end}
      ac.storage=function(defaults,prefix)
        local saved=storage[prefix] or {}; storage[prefix]=saved
        for k,v in pairs(defaults) do if saved[k]==nil then saved[k]=v end end
        return saved
      end
      window=vec2(1920,1080); mouse=vec2(-100,-100); clicked=false; cursor=vec2(); fontSize=16
      ui={Font={Huge=32,Title=24},Alignment={Start=-1,Center=0},MouseButton={Left=0},MouseCursor={Arrow=0},
        time=function() return now end,windowSize=function() return window end,
        mousePos=function() return mouse end,mouseClicked=function() return clicked end,
        mouseDown=function() return false end,captureMouse=function() end,setMouseCursor=function() end,
        setCursor=function(p) cursor=p end,pushFont=function(f) fontSize=f end,popFont=function() fontSize=16 end}
    ''')
    init = source[source.index('hud.bindingDefaults ='):source.index('hud.loadoutStorage =')]
    lua.execute(init)
    lua.execute(source[source.index('function hud.aimSensitivity('):source.index('pcall(function()\n  impactSparks')])
    lua.execute(source[source.index('function fpsVisual.fieldInput('):source.index('hud.abilitiesEvent =')])
    lua.execute('''
      assert(hud.gamepadBindingName('jump')=='A')
      axes[0]=0.62; assert(hud.gamepadBindingValue('aim')==0.62)
      axes[1]=0.8; assert(hud.gamepadBindingDown('fire'))
      hud.gamepadBindings.fire='B'; assert(not hud.gamepadBindingDown('fire'))
      pad[8192]=true; assert(hud.gamepadBindingDown('fire')); pad={}; axes={}
      keys[32]=true; assert(hud.bindingDown('jump',false)); assert(not hud.gamepadBindingDown('jump'))
      -- Capturing Xbox B must not bind a keyboard key or treat B as menu Back.
      hud.beginBindingCapture('jump','xbox'); now=0.3; pad[8192]=true; hud.updateBindingCapture()
      assert(hud.gamepadBindings.jump=='B' and hud.bindings.jump==32 and hud.bindingCaptureConsumed)
      pad={}; hud.beginBindingCapture('reload','keyboard'); now=0.6; pressed[70]=true
      hud.updateBindingCapture(); assert(hud.bindings.reload==70 and hud.gamepadBindings.reload=='X'); pressed={}
      -- Held buttons at capture start are ignored until released and pressed again.
      pad[4096]=true; hud.beginBindingCapture('sprint','xbox'); now=0.9; hud.updateBindingCapture()
      assert(hud.bindingCapture=='sprint'); pad={}; hud.updateBindingCapture()
      pad[4096]=true; hud.updateBindingCapture(); assert(hud.gamepadBindings.sprint=='A')
      pad={}; hud.beginBindingCapture('fire','xbox'); now=1.2; axes[0]=0.7; hud.updateBindingCapture()
      assert(hud.gamepadBindings.fire=='LT'); axes={}
      -- Escape and Start cancel without being stored as gameplay bindings.
      hud.beginBindingCapture('fire','xbox'); pressed[27]=true; hud.updateBindingCapture()
      assert(hud.bindingCapture==nil and hud.gamepadBindings.fire=='LT'); pressed={}
      hud.beginBindingCapture('fire','keyboard'); pad[16]=true; hud.updateGamepadMenu()
      assert(hud.bindingCapture==nil and pauseCalls==0); pad={}; hud.updateGamepadMenu()
      -- Polling from update and the pause renderer cannot double-toggle a held Start.
      hud.nativePauseMenu=true; pad[16]=true; hud.updateGamepadMenu(); hud.updateGamepadMenu()
      assert(sim.isPaused and not hud.nativePauseMenu and hud.pausePage=='main' and pauseCalls==1)
      pad={}; hud.updateGamepadMenu(); pad[16]=true; hud.updateGamepadMenu(); hud.updateGamepadMenu()
      assert(not sim.isPaused and pauseCalls==2)
      pad={}; hud.updateGamepadMenu(); previewCamera.everEnteredGameplay=false
      pad[16]=true; hud.updateGamepadMenu(); assert(pauseCalls==2)
      pad={}; hud.updateGamepadMenu(); previewCamera.everEnteredGameplay=true
    ''')
    lua.execute(init)  # Same persistent stores are reopened as they are after reconnect.
    lua.execute("assert(hud.gamepadBindings.sprint=='A' and hud.gamepadBindings.fire=='LT' and hud.bindings.reload==70)")
    # Execute the actual mapped gameplay reads, including the default shoulder chord path.
    input_start = source.index('    local jump = not matchInputLocked')
    input_end = source.index('    fpsVisual.updatePickupInteraction(dt, interact)', input_start)
    lua.execute('function inputs(dt)\n' + source[input_start:input_end] + '''
      return {jump=jump,crouch=crouch,reload=reload,switchWeapon=gamepadWeaponSwitch,
        fieldUpgrade=fieldUpgrade,grenade=grenade,interact=interact}
      end
      function abilities(dt) local i=inputs(dt); return i.fieldUpgrade,i.grenade end
    ''')
    lua.execute('''
      keys={}; pad={}; matchInputLocked=false; cursorUnlocked=false
      pad[512]=true; pad[256]=true
      local field,grenade=abilities(.05); assert(field and not grenade)
      pad={}; abilities(.2); hud.gamepadBindings.fieldUpgrade='Up'; hud.gamepadBindings.grenade='LT'
      pad[512]=true; field,grenade=abilities(.2); assert(not field and not grenade) -- Old RB is unbound.
      pad={}; axes[0]=1; field,grenade=abilities(.05); assert(grenade and not field)
      axes={}; pad[1]=true; field,grenade=abilities(.05); assert(field and not grenade)
      for action,key in pairs(hud.bindingDefaults) do hud.bindings[action]=key end
      for action,id in pairs(hud.gamepadDefaults) do hud.gamepadBindings[action]=id end
      pad={}; axes={}; keys={}; hud.pausePage='controls'; hud.bindingCapture=nil
      for _,action in ipairs({'jump','crouch','reload','switchWeapon','interact'}) do
        local original=hud.gamepadBindings[action]
        hud.gamepadBindings[action]='Down'; pad[2]=true
        assert(inputs(.05)[action],action..' remapped controller input')
        pad={}; assert(not inputs(.05)[action],action..' release')
        keys[hud.bindings[action]]=true
        assert(inputs(.05)[action],action..' keyboard input remains independent')
        keys={}; hud.gamepadBindings[action]=original
      end
      pad[4096]=true; pad[8192]=true; pad[16384]=true; matchInputLocked=true
      local i=inputs(.05); assert(not i.jump and not i.crouch and not i.reload and not i.interact)
      matchInputLocked=false; pad={}
    ''')
    lua.execute(source[source.index('function hud.drawControlsMenu('):source.index('function hud.drawEnvironmentMenu(')])
    lua.execute(source[source.index('function hud.drawEnvironmentMenu('):source.index('function hud.exclusiveCallback(')])
    lua.execute(source[source.index('function hud.drawPreDriveMenu('):source.index('function hud.drawMatchResults(')])
    commands=[]
    for name,kind in [('drawRectFilled','fill'),('drawRect','rect'),('drawLine','line'),('dwriteDrawTextClipped','text')]:
        lua.globals().ui[name] = lambda *v,k=kind: commands.append((k,v))
    lua.execute('''
      ui.text=function(label) ui.dwriteDrawTextClipped(label,fontSize,cursor,cursor+vec2(400,32),-1,0,false,rgbm(1,1,1,1)) end
      ui.textColored=function(label,color) ui.dwriteDrawTextClipped(label,fontSize,cursor,cursor+vec2(400,26),-1,0,false,color) end
    ''')
    args.output.mkdir(parents=True,exist_ok=True)
    def rgba(c): return tuple(round(max(0,min(1,c[i]))*255) for i in range(1,5))
    def frame(width,height,x=-100,y=-100,click=False):
        g=lua.globals(); g.window=g.vec2(width,height); g.mouse=g.vec2(x,y); g.clicked=click
        commands.clear(); g.hud.drawPauseMenu()
    for width,height in [(1920,1080),(1280,720),(831,619)]:
        frame(width,height)
        labels=[v[0] for k,v in commands if k=='text']
        assert 'KEYBOARD / MOUSE' in labels and 'XBOX CONTROLLER' in labels
        assert 'ESC (FIXED)' in labels and 'START / OPTIONS (FIXED)' in labels
        image=Image.new('RGBA',(width,height),(9,14,20,255)); draw=ImageDraw.Draw(image)
        for kind,v in commands:
            if kind in ('fill','rect','line'):
                a,b,c=v[:3]; box=(round(a.x),round(a.y),round(b.x),round(b.y))
                assert 0<=a.x<=width and 0<=b.x<=width and 0<=a.y<=height and 0<=b.y<=height
                if kind=='fill': draw.rectangle(box,fill=rgba(c))
                elif kind=='rect': draw.rectangle(box,outline=rgba(c),width=1)
                else: draw.line(box,fill=rgba(c),width=1)
            else:
                label,size,a,b,_,_,_,color=v
                font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',max(1,round(size)))
                assert draw.textlength(label,font=font)<=b.x-a.x+1, f'Text overflow: {label}'
                box=draw.textbbox((0,0),label,font=font)
                draw.text((a.x,a.y+(b.y-a.y-(box[3]-box[1]))/2-box[1]),label,font=font,fill=rgba(color))
        image.convert('RGB').save(args.output/f'controls-{width}.png')
    # Click the real Xbox Jump cell, capture B, then reset both columns through the real UI.
    frame(1920,1080, (1920-900)/2+42+560, (1080-720)/2+24+89+5*30+10, True)
    assert lua.globals().hud.bindingCapture=='jump' and lua.globals().hud.bindingCaptureDevice=='xbox'
    lua.execute('now=2; pad[8192]=true'); frame(1920,1080)
    assert lua.globals().hud.gamepadBindings.jump=='B'
    frame(1920,1080,(1920-900)/2+42+320,(1080-720)/2+24+630,True)
    assert lua.globals().hud.gamepadBindings.jump=='A' and lua.globals().hud.bindings.reload==82
    # Run the parent deployment page too: captured B must not become navigation.
    lua.execute('''
      clicked=false; hud.preDrivePage='controls'; pad={}
      hud.beginBindingCapture('jump','xbox'); now=2.4; pad[8192]=true
      hud.drawPreDriveMenu(); assert(hud.gamepadBindings.jump=='B' and hud.preDrivePage=='controls')
      hud.drawPreDriveMenu(); assert(hud.preDrivePage=='controls')
      pad={}; hud.drawPreDriveMenu(); pad[8192]=true; hud.drawPreDriveMenu()
      assert(hud.preDrivePage=='briefing')
    ''')
    lua.execute('''
      actors={}; names={}; teams={}; remainingSeconds=600; localSessionID=0; team1Kills=0; team2Kills=0
      matchLabel=function() return 'TEAM DEATHMATCH' end
      matchTargetText=function() return '75 KILLS' end
      isTeamMatch=function() return true end
      fpsVisual.matchModeTitle=matchLabel
      ui.textWrapped=function() end
      hud.loadout={catalogReceived=true,confirmed=true,mainWeapon=1,secondaryWeapon=3,lethal=16,result='READY'}
      hud.itemNames={[1]='RIFLE',[3]='PISTOL',[16]='FRAG'}; hud.preDriveStatus=''
      hud.environmentWeather=15; hud.environmentTimeSeconds=46800
      environmentSent=0; hud.environmentRequestEvent=function() environmentSent=environmentSent+1 end
      deployed=0; hud.requestDeployment=function() deployed=deployed+1 end
      shutdowns=0; ac.shutdownAssettoCorsa=function() shutdowns=shutdowns+1 end
      ac.getTrackName=function() return 'NUKETOWN' end
      function drawMenu() now=now+.016; hud.drawPauseMenu() end
      function pressMenu(button,draw)
        draw=draw or drawMenu; pad={}; draw(); pad[button]=true; draw()
      end
      function focusedItem()
        for _,item in ipairs(hud.menuNavigation.items) do
          if item.id==hud.menuNavigation.focus then return item end
        end
      end
      hud.menuNavigation=nil; hud.pausePage='main'; pad={}; mouse=vec2(-100,-100); drawMenu()
      local first=focusedItem(); pressMenu(2); assert(focusedItem().y>first.y)
      pressMenu(4096); assert(hud.pausePage=='controls')
      drawMenu(); assert(hud.bindingCapture==nil) -- Held A cannot activate the child page.
      first=focusedItem(); pressMenu(8); assert(focusedItem().x>first.x)
      pressMenu(4096); assert(hud.bindingCapture=='fire' and hud.bindingCaptureDevice=='xbox')
      pad={}; drawMenu(); now=now+.3; pad[8192]=true; drawMenu()
      assert(hud.gamepadBindings.fire=='B' and hud.pausePage=='controls')
      drawMenu(); assert(hud.pausePage=='controls') -- Held captured B cannot navigate Back.
      pressMenu(8192); assert(hud.pausePage=='main')
      drawMenu(); assert(hud.pausePage=='main') -- Held B cannot also close the parent.
      pressMenu(2); pressMenu(4096); drawMenu() -- Controls again.
      for i=1,12 do pressMenu(2) end
      assert(hud.menuNavigation.focus=='slider:hipSensitivity')
      local sensitivity=hud.aimSettings.hipSensitivity
      pressMenu(8); assert(hud.aimSettings.hipSensitivity>sensitivity)
      assert(hud.menuNavigation.focus=='slider:hipSensitivity')
      pressMenu(4); assert(math.abs(hud.aimSettings.hipSensitivity-sensitivity)<.001)
      pressMenu(8192); drawMenu()
      -- Holding D-pad repeats after a delay, but holding A/B never repeats.
      pad={}; drawMenu(); pad[2]=true; drawMenu(); first=focusedItem()
      drawMenu(); assert(focusedItem().id==first.id)
      now=now+.4; drawMenu(); assert(focusedItem().y>first.y)
      pressMenu(2); pressMenu(4096); assert(hud.pausePage=='pure'); drawMenu()
      local hour=hud.environmentDraftTimeSeconds
      pressMenu(8); pressMenu(4096); assert(hud.environmentDraftTimeSeconds~=hour)
      pressMenu(2); pressMenu(4096); assert(hud.environmentDraftWeather~=nil and environmentSent==0)
      pressMenu(8192); assert(hud.pausePage=='main' and not hud.environmentDraftReady); drawMenu()
      -- B dismisses a leave confirmation; it never confirms shutdown.
      hud.leaveServerArmed=true; pressMenu(8192); assert(not hud.leaveServerArmed and shutdowns==0)
      sim.isPaused=true; pressMenu(8192); assert(not sim.isPaused)
      assert(not hud.gamepadBindingDown('crouch')) -- Menu Back cannot become a crouch.
      pad={}; assert(not hud.gamepadBindingDown('crouch'))
      pad[8192]=true; assert(hud.gamepadBindingDown('crouch'))
      -- Mouse still operates after controller navigation.
      pad={}; mouse=vec2(600,490); clicked=true; drawMenu(); clicked=false
      assert(hud.pausePage=='controls' and not hud.menuNavigation.active)
      -- Deployment's focused A no longer always deploys; navigate to Controls first.
      hud.menuNavigation=nil; hud.preDrivePage='briefing'; pad={}; mouse=vec2(-100,-100)
      local function deployment() now=now+.016; hud.drawPreDriveMenu() end
      deployment(); pressMenu(2,deployment); pressMenu(4096,deployment)
      assert(hud.preDrivePage=='controls' and deployed==0); deployment()
      pressMenu(8192,deployment); assert(hud.preDrivePage=='briefing'); deployment()
      pressMenu(2,deployment); pressMenu(8,deployment); pressMenu(8,deployment); pressMenu(8,deployment)
      pressMenu(4096,deployment); assert(deployed==1 and shutdowns==0)
    ''')
    print('PASS: separate bindings, capture/reset, remapped abilities, Start, D-pad/A/B navigation, sliders, '
          'held-input and gameplay guards, deployment, weather, mouse coexistence; three controls layouts')


if __name__=='__main__':
    main()
