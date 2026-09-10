"""Run the real CSP Lua appearance callbacks against a small scene/event harness."""
import argparse
from pathlib import Path
import sys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--lua-runtime", type=Path, required=True)
    args = parser.parse_args()
    sys.path.insert(0, str(args.lua_runtime.resolve()))
    from lupa.luajit21 import LuaRuntime
    lua = LuaRuntime(unpack_returned_tuples=True)
    source = (Path(__file__).resolve().parent.parent
              / "AssettoServer/Server/Fps/fps.lua").read_text(encoding="utf-8")
    lua.execute(source[source.index("local fpsVisual = {"):source.index("local actors = {}")]
                .replace("local fpsVisual =", "fpsVisual =", 1))
    lua.execute('''
        bit=require('bit')
        actors={}; names={}; teams={}; warnings={}; callbacks={}
        hud={radarReveal={},radarVisible={},loadout={},loadoutStorage={operatorModel=1}}
        ac={StructItem={key=function(v) return v end, byte=function() end,
            uint32=function() end, string=function() end}}
        ac.OnlineEvent=function(def, fn) callbacks[def[1]]=fn; return fn end
        ac.warn=function(message) table.insert(warnings,message) end
        fpsAudio={resetActor=function() end}
        hud.itemAllowed=function(mask,id) return bit.band(mask,bit.lshift(1,id))~=0 end
        fpsVisual.asset=function(name) return '/assets/'..name end
        fpsVisual.fallback=function(reason) globalFallback=reason end
        io.fileExists=function(path) return not missingTextures end
        function fakeActor(id,team)
            local actor={id=id,team=team,role=0,operatorModel=0,disposed=0}
            actor.root={dispose=function() actor.disposed=actor.disposed+1 end}
            return actor
        end
    ''')
    lua.execute(source[source.index("function fpsVisual.resetOperatorAvatar("):
                       source.index("function fpsVisual.fallback(")])
    lua.execute(source[source.index("hud.rosterEvent = ac.OnlineEvent({"):
                       source.index("hud.matchEvent = ac.OnlineEvent({")])
    lua.execute(source[source.index("function fpsVisual.applyOperatorTeamSkin("):
                       source.index("local function ensureLocalViewmodel(")])
    lua.execute(source[source.index("hud.loadoutCatalogEvent = ac.OnlineEvent({"):
                       source.index("hud.loadoutResultEvent = ac.OnlineEvent({")])
    # The result callback is a complete top-level event before loadout-state handling.
    start = source.index("hud.loadoutResultEvent = ac.OnlineEvent({")
    end = source.index("hud.loadoutStateEvent = ac.OnlineEvent({", start)
    lua.execute(source[start:end])
    lua.execute('''
        fpsVisual.modern=true
        local roster=callbacks.ASRC_FpsRoster
        -- Roster can arrive before the unreliable positional snapshot creates an actor.
        roster(nil,{actorID=7,role=1,team=2,name='Remote',operatorModel=1})
        actors[7]=fakeActor(7,2)
        assert(fpsVisual.operatorForActor(actors[7]).id==1)
        roster(nil,{actorID=7,role=1,team=2,name='Remote',operatorModel=1})
        assert(actors[7].operatorModel==1 and actors[7].root==nil)
        actors[0]=fakeActor(0,1)
        fpsVisual.actorModels[0]=0
        assert(fpsVisual.operatorForActor(actors[0]).id==0)
        -- Client-originated or invalid identities cannot select asset paths.
        roster(12,{actorID=0,role=1,team=2,name='Spoof',operatorModel=1})
        assert(fpsVisual.operatorForActor(actors[0]).id==0)
        roster(nil,{actorID=9,role=1,team=1,name='Unknown',operatorModel=250})
        assert(fpsVisual.actorModels[9]==0)
        -- Fail only this Ghost instance; a repeated roster must not cause a retry loop.
        fpsVisual.operatorFailed(actors[7],'missing KN5')
        assert(fpsVisual.operatorForActor(actors[7]).id==0)
        assert(fpsVisual.modern and globalFallback==nil)
        roster(nil,{actorID=7,role=1,team=2,name='Remote',operatorModel=1})
        assert(actors[7].operatorFallback)
        roster(nil,{actorID=7,role=1,team=2,name='Remote',operatorModel=0})
        assert(not actors[7].operatorFallback)
        -- Team 2 forks each material before editing it; Team 1 remains untouched.
        local edits={}
        local material=function(name)
            local unique=false
            return {size=function() return 1 end,
                ensureUniqueMaterials=function() unique=true end,
                setMaterialTexture=function(_,slot,path)
                    assert(unique); table.insert(edits,{name,path})
                end}
        end
        actors[7].modernModel={findAny=function(_,name) return material(name) end}
        actors[7].team=2; actors[7].teamSkinApplied=nil
        fpsVisual.actorModels[7]=1
        assert(fpsVisual.applyOperatorTeamSkin(actors[7]))
        assert(#edits==2 and edits[1][1]=='material:ASRC_GHOST_UNIFORM')
        assert(edits[1][2]=='/assets/asrc_modern_ghost_team2_uniform.png')
        actors[0].modernModel={findAny=function() error('Team 1 must not be recolored') end}
        assert(fpsVisual.applyOperatorTeamSkin(actors[0]))
        -- Model-specific texture failure also stays within the actor.
        actors[7].teamSkinApplied=nil; missingTextures=true
        assert(not fpsVisual.applyOperatorTeamSkin(actors[7]))
        assert(actors[7].operatorFallback and globalFallback==nil)
        -- Persist acknowledged selection, then restore only server-allowed choices.
        hud.loadoutStorage.mainWeapon=1; hud.loadoutStorage.lethal=16
        hud.loadoutStorage.secondaryWeapon=4
        local result=callbacks.ASRC_FpsLoadoutResult
        result(nil,{result=2,mainWeapon=1,lethal=16,secondaryWeapon=4,operatorModel=1})
        assert(hud.loadoutStorage.operatorModel==1)
        local catalog=callbacks.ASRC_FpsLoadoutCatalog
        local message={allowedMainWeapons=6,allowedLethals=196608,allowedSecondaryWeapons=24,
            defaultMainWeapon=1,defaultLethal=16,defaultSecondaryWeapon=4,
            allowedOperatorModels=3,defaultOperatorModel=0}
        catalog(nil,message)
        assert(hud.loadout.operatorModel==1)
        message.allowedOperatorModels=1
        catalog(nil,message)
        assert(hud.loadout.operatorModel==0)
        hud.loadoutStorage.operatorModel=255
        catalog(nil,message)
        assert(hud.loadout.operatorModel==0)
    ''')
    print("PASS: roster-before-snapshot, different players, spoof rejection, unknown IDs, "
          "per-actor fallback, pooled-material isolation, acknowledged persistence and catalog fallback.")


if __name__ == "__main__":
    main()
