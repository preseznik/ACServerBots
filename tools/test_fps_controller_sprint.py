"""Run the actual controller sprint input path under LuaJIT, without a game session."""
import argparse
from pathlib import Path
import sys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--lua-runtime', type=Path, required=True)
    args = parser.parse_args()
    sys.path.insert(0, str(args.lua_runtime.resolve()))
    from lupa.luajit21 import LuaRuntime
    from lupa.lua54 import LuaRuntime as SyntaxRuntime

    source = (Path(__file__).resolve().parents[1] / 'AssettoServer/Server/Fps/fps.lua').read_text(encoding='utf-8')
    SyntaxRuntime().compile(source)
    lua = LuaRuntime()
    lua.execute('''
      bit=require('bit')
      fpsVisual={controllerSprint={latched=false,wasHeld=false}}
      ac={GamepadButton={LeftThumb=1},KeyIndex={LeftShift=1},
          isGamepadButtonPressed=function() return pad end,isKeyDown=function() return keyboard end}
      hud={bindingDown=function(_,fallback) return fallback end,gamepadBindingDown=function() return pad end}
      move={lengthSquared=function() return moving and 1 or 0 end}
      function reset()
        pad=false; keyboard=false; moving=true; matchInputLocked=false; cursorUnlocked=false
        localActor={flags=21}; localStance=0; predictedAirborne=false; fpsVisual.adsInput=0
        fpsVisual.controllerSprint={latched=false,wasHeld=false}
        fpsVisual.stamina={value=100,maximum=100,exhausted=false,drainPerSecond=20,
          recoveryDelay=0,recoveryDelaySeconds=1,recoveryPerSecond=25,exhaustionRelease=25}
      end
    ''')
    lua.execute(source[source.index('function fpsVisual.updateControllerSprint('):
                       source.index('function script.update(dt)')])
    start = source.index('    local controllerSprint = fpsVisual.updateControllerSprint(')
    end = source.index('    viewmodelMove:set(move)', start)
    lua.execute('function sample(dt) local sprint=false\n' + source[start:end] + '\nreturn sprint end')
    lua.execute('''
      reset(); assert(not sample(0))
      pad=true; assert(sample(0))
      for i=1,100 do assert(sample(0)) end -- Holding cannot repeatedly flip the toggle.
      pad=false; assert(sample(0))         -- Button release keeps sprint engaged.
      pad=true; assert(not sample(0))     -- Second press stops sprint.
      pad=false; assert(not sample(0))
      keyboard=true; assert(sample(0)); keyboard=false; assert(not sample(0))
      for _,case in ipairs({'stop','aim','crouch','prone','exhausted','empty','menu','match','dead','inactive','missing'}) do
        reset(); pad=true; assert(sample(0)); pad=false; assert(sample(0))
        if case=='stop' then moving=false
        elseif case=='aim' then fpsVisual.adsInput=1
        elseif case=='crouch' then localStance=1
        elseif case=='prone' then localStance=2
        elseif case=='exhausted' then fpsVisual.stamina.exhausted=true
        elseif case=='empty' then fpsVisual.stamina.value=0
        elseif case=='menu' then cursorUnlocked=true
        elseif case=='match' then matchInputLocked=true
        elseif case=='dead' then localActor.flags=23
        elseif case=='inactive' then localActor.flags=0
        elseif case=='missing' then localActor=nil end
        pad=true; assert(not sample(0),case)
        moving=true; fpsVisual.adsInput=0; localStance=0; fpsVisual.stamina.exhausted=false
        fpsVisual.stamina.value=100; cursorUnlocked=false; matchInputLocked=false; localActor={flags=21}
        assert(not sample(0),case..' held button reactivated')
        pad=false; assert(not sample(0)); pad=true; assert(sample(0))
      end
      reset(); pad=true; assert(sample(0)); pad=false; predictedAirborne=true
      assert(not sample(0)); predictedAirborne=false; assert(sample(0)) -- Resume after a jump.
      fpsVisual.stamina.value=0.1; assert(not sample(0.05))
      assert(not fpsVisual.controllerSprint.latched)
      reset(); pad=true; assert(sample(0))
      fpsVisual.updateControllerSprint(true,false) -- Pause while L3 is held.
      assert(not sample(0)); pad=false; sample(0); pad=true; assert(sample(0))
      fpsVisual.controllerSprint.latched=false -- Spawn resets the latch, preserving debounce.
      assert(not sample(0)); pad=false; sample(0); pad=true; assert(sample(0))
    ''')
    spawn = source[source.index('if not actor.localInitialized or spawnChanged'):source.index('hud.boundaryEvent =')]
    assert 'fpsVisual.controllerSprint.latched = false' in spawn
    assert "fpsVisual.updateControllerSprint(hud.gamepadBindingDown('sprint'), false)" in source
    print('PASS: L3 toggle/debounce, keyboard hold, cancellation, exhaustion, jump, pause and respawn; Lua syntax')


if __name__ == '__main__':
    main()
