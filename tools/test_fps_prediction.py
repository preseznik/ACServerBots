"""Exercise grounded stair prediction without CSP or desktop control."""
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
    source = (repo / 'AssettoServer/Server/Fps/fps.lua').read_text(encoding='utf-8')
    SyntaxRuntime().compile(source)
    lua = LuaRuntime()
    lua.execute("bit=require('bit'); fpsVisual={}; predictedAirborne=false")
    lua.execute(source[source.index('function fpsVisual.correctGroundedPredictionHeight('):
                       source.index('local function localTrackProbeMovement(')])
    lua.execute('''
      -- Replay the measured 63 cm client height lag during a grounded Nuketown climb.
      local actor={flags=21, render={y=0.829}, target={y=1.457}}
      fpsVisual.correctGroundedPredictionHeight(actor)
      assert(actor.render.y==1.457)
      -- Walking/sprinting at multiple render and snapshot rates must not accumulate lag.
      for _,fps in ipairs({30,60,144}) do
        for _,speed in ipairs({6,9}) do
          for _,snapshotHz in ipairs({10,20}) do
            actor.render.y=0; actor.target.y=0
            for frame=1,fps do
              local time=frame/fps
              local snapshotTime=math.floor(time*snapshotHz)/snapshotHz
              actor.target.y=math.floor(snapshotTime*speed/0.3048)*0.2032
              fpsVisual.correctGroundedPredictionHeight(actor)
              assert(actor.render.y>=actor.target.y)
              actor.render.y=actor.render.y+(actor.target.y-actor.render.y)*(1-math.exp(-6/fps))
            end
          end
        end
      end
      -- Descending stays smooth; the clamp does not teleport down to a lower tread.
      actor.render.y=2; actor.target.y=1.8
      fpsVisual.correctGroundedPredictionHeight(actor); assert(actor.render.y==2)
      -- Neither airborne snapshots nor a jump awaiting acknowledgement is clamped.
      actor.flags=5; actor.render.y=1; actor.target.y=2
      fpsVisual.correctGroundedPredictionHeight(actor); assert(actor.render.y==1)
      actor.flags=21; predictedAirborne=true
      fpsVisual.correctGroundedPredictionHeight(actor); assert(actor.render.y==1)
    ''')
    update = source[source.index('function script.update(dt)'):]
    start = update.index('fpsVisual.correctGroundedPredictionHeight(localActor)')
    assert start < update.index('local predictedStep = predictedHorizontalVelocity * dt')
    print('PASS: grounded stair height, render/snapshot rates, descent and jump prediction; Lua syntax')


if __name__ == '__main__':
    main()
