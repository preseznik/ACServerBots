"""Execute the HUD camera-clip lifecycle with CSP app APIs mocked at the boundary."""
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

    repo = Path(__file__).resolve().parents[1]
    source = (repo / 'AssettoServer.RaceControl.Core/Assets/Fps/Hud/asrc_fps_hud.lua').read_text(encoding='utf-8')
    SyntaxRuntime().compile(source)
    lua = LuaRuntime()
    lua.execute('''
      now=10; calls={}; warnings={}; logs={}; releases={}; script={}
      bridgeProtocol=16
      bridge={protocol=0, gameplayActive=0, onlineHeartbeat=10, persistentCursor=0}
      sim={isSessionStarted=true, isLive=true, cameraClipNear=0.1}
      ui={time=function() return now end}
      ac={getSim=function() return sim end,
          log=function(message) logs[#logs+1]=message end,
          warn=function(message) warnings[#warnings+1]=message end,
          onRelease=function(callback) releases[#releases+1]=callback end,
          overrideCameraClipPlanes=function(near, far)
            assert(far==nil, 'Do not change the far plane')
            if failOverride then error('Unavailable override') end
            calls[#calls+1]=near or 'released'
            sim.cameraClipNear=near or 0.1
          end}
      audioPlayer={update=function() end, reset=function() end}
      function tick(dt, fresh)
        now=now+dt
        if fresh then bridge.onlineHeartbeat=now end
        script.update(dt)
      end
      function expectReleased()
        assert(calls[#calls]=='released')
        assert(sim.cameraClipNear==0.1)
      end
    ''')
    # Keep bridgeIsLive and the controller in the same lexical scope as script.update.
    controller = source[source.index('local function bridgeIsLive()'):
                        source.index('local function actorName(')]
    update = source[source.index('function script.update(dt)'):
                    source.index('function appOverlay(')]
    lua.execute(controller + '\n' + update)
    lua.execute('''
      -- Ordinary racing never acquires the global override.
      for i=1,10 do tick(0.1, true) end
      assert(#calls==0)
      bridge.protocol=16; bridge.gameplayActive=1
      tick(0.6, false); assert(#calls==0) -- stale FPS session
      tick(0.01, true); assert(#calls==1 and calls[1]>0 and calls[1]<0.01)
      local near=calls[1]
      for i=1,30 do tick(0.02, true) end
      assert(#calls==1, 'An active override must not be resubmitted every frame')
      assert(string.find(logs[#logs], 'observed: 0.0050 m', 1, true))
      assert(#warnings==0)

      -- Independent game-state gates release even before the bridge catches up.
      for _,field in ipairs({'isPaused','isInMainMenu','isReplayActive','isLookingAtSessionResults'}) do
        sim[field]=true; tick(0.01, true); expectReleased()
        sim[field]=false; tick(0.01, true); assert(calls[#calls]==near)
      end
      for _,field in ipairs({'isSessionStarted','isLive'}) do
        sim[field]=false; tick(0.01, true); expectReleased()
        sim[field]=true; tick(0.01, true); assert(calls[#calls]==near)
      end
      tick(0.6, false); expectReleased() -- lost online script / disconnect
      tick(0.01, true); assert(calls[#calls]==near)
      bridge.protocol=13; tick(0.01, true); expectReleased()
      bridge.protocol=16; tick(0.01, true); assert(calls[#calls]==near)
      bridge.gameplayActive=0; tick(0.01, true); expectReleased()
      bridge.gameplayActive=1; tick(0.01, true); assert(calls[#calls]==near)
      bridge.onlineHeartbeat=now+2; tick(0.01, false); expectReleased()
      tick(0.01, true); assert(calls[#calls]==near)
      assert(#releases==1); releases[1](); expectReleased() -- app unload
      local count=#calls; releases[1](); assert(#calls==count)

      -- Failure does not claim ownership; a later retry can recover.
      warnings={}; failOverride=true
      tick(0.01, true); tick(0.01, true)
      assert(#calls==count and #warnings==1)
      failOverride=false; tick(0.01, true); assert(calls[#calls]==near)
      -- Report observed state, not merely that the request succeeded.
      sim.cameraClipNear=0.1; tick(0.3, true)
      assert(string.find(warnings[#warnings], 'did not take effect', 1, true))
      bridge.protocol=0; bridge.gameplayActive=0; tick(0.01, true); expectReleased()
      count=#calls
      for i=1,30 do tick(0.1, true) end
      assert(#calls==count, 'No override should survive return to racing')
    ''')
    print('PASS: HUD update acquires clip override; observed-state verification; menu/pause/replay/')
    print('results/stale bridge/protocol/disconnect/unload cleanup; error recovery; racing unaffected')


if __name__ == '__main__':
    main()
