using System.Reflection;
using AssettoServer.Server.Fps;

namespace AssettoServer.Tests;

public sealed class FpsClientScriptTests
{
    [Test]
    public void CarrierCars_PreserveNativeParticipantStateAndDisableNativeLeaderboard()
    {
        Assembly assembly = typeof(FpsWorld).Assembly;
        using Stream stream = assembly.GetManifestResourceStream("AssettoServer.Server.Fps.fps.lua")
                              ?? throw new AssertionException("Embedded FPS client script was not found");
        using var reader = new StreamReader(stream);
        string script = reader.ReadToEnd();
        int snapshotStart = script.IndexOf("local snapshotEvent", StringComparison.Ordinal);
        int rosterStart = script.IndexOf("local rosterEvent", StringComparison.Ordinal);
        string snapshotDefinition = script[snapshotStart..rosterStart];
        int shotStart = script.IndexOf("local shotEvent", StringComparison.Ordinal);
        int meshStart = script.IndexOf("local function appendBox", StringComparison.Ordinal);
        string shotDefinition = script[shotStart..meshStart];
        int updateStart = script.IndexOf("function script.update(dt)", StringComparison.Ordinal);
        int frameBeginStart = script.IndexOf("function script.frameBegin", StringComparison.Ordinal);
        int draw3DStart = script.IndexOf("function script.draw3D", StringComparison.Ordinal);
        int directDrawStart = script.IndexOf("local function drawDirectRifleViewmodel",
            StringComparison.Ordinal);
        int localCollisionStart = script.IndexOf("local function localTrackMovementBlocked",
            StringComparison.Ordinal);
        string updateDefinition = script[updateStart..frameBeginStart];
        string frameBeginDefinition = script[frameBeginStart..draw3DStart];
        int drawUiStart = script.IndexOf("function script.drawUI", StringComparison.Ordinal);
        string draw3DDefinition = script[draw3DStart..drawUiStart];
        string directDrawDefinition = script[directDrawStart..localCollisionStart];

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Not.Contain("ac.setCarActive"));
            Assert.That(script, Does.Contain("physics.setCarNoInput(true)"));
            Assert.That(script, Does.Contain("physics.setCarNoInput(false)"));
            Assert.That(script, Does.Contain("ac.findNodes('carRoot:' .. i)"));
            Assert.That(script, Does.Contain("root:setVisible(false)"));
            Assert.That(script, Does.Contain("ac.disableExtraHUDElements"));
            Assert.That(script, Does.Contain("'leaderboard'"));
            Assert.That(script, Does.Not.Contain("physics.lockUserControlsFor"));
            Assert.That(script, Does.Not.Contain("physics.lockUserGearboxFor"));
            Assert.That(script, Does.Contain("physics.setGentleStop(car.index, true)"));
            Assert.That(script, Does.Contain("physics.getCarInputControls()"));
            Assert.That(script, Does.Contain("ac.isKeyDown(positiveKey)"));
            Assert.That(script, Does.Contain("ac.KeyIndex.Up"));
            Assert.That(script, Does.Not.Contain("ui.keyboardButtonDown"));
            Assert.That(script, Does.Contain("}, function() end, nil, true)"));
            Assert.That(snapshotDefinition, Does.Contain("end, nil, true)"));
            Assert.That(shotDefinition, Does.Contain("end, nil, true"));
            Assert.That(script, Does.Contain("buttons = buttons }, false, 255)"));
            Assert.That(script, Does.Contain("not state.isInMainMenu"));
            Assert.That(script, Does.Contain("releaseFpsCamera()"));
            Assert.That(script, Does.Contain("camera, cameraError = ac.grabCamera"));
            Assert.That(script, Does.Contain("camera.ownShare = 1"));
            Assert.That(script, Does.Contain("camera:dispose()"));
            Assert.That(script, Does.Contain("function script.frameBegin"));
            Assert.That(updateDefinition, Does.Not.Contain("applyFpsCamera(localActor)"));
            Assert.That(frameBeginDefinition, Does.Contain("applyFpsCamera(localActor)"));
            Assert.That(updateDefinition, Does.Not.Contain("updateRifleViewmodel(dt"));
            Assert.That(directDrawDefinition, Does.Contain(
                "updateRifleViewmodel(viewmodelFrameDt, actor, viewmodelMove, viewmodelSprint)"));
            Assert.That(draw3DDefinition, Does.Contain("drawDirectRifleViewmodel()"));
            Assert.That(script, Does.Contain("ac.KeyIndex.F6"));
            Assert.That(script, Does.Contain("third-person over-shoulder"));
            Assert.That(script, Does.Contain("physics.raycastTrack(focus, direction, distance"));
            Assert.That(script, Does.Contain("actor.root:setVisible(active and thirdPersonEnabled)"));
            Assert.That(script, Does.Contain(
                "render.setTransform(viewmodelRenderPosition, viewmodelRenderLook, viewmodelRenderUp, true)"));
            Assert.That(script, Does.Contain("ac.getCameraPosition():clone()"));
            Assert.That(script, Does.Contain("ac.getCameraForward():clone()"));
            Assert.That(script, Does.Contain("ac.getCameraUp():clone()"));
            Assert.That(script, Does.Contain("actor.root:setOrientation(vec3(math.sin(yaw)"));
            Assert.That(script, Does.Contain("groundYs = ac.StructItem.array"));
            Assert.That(script, Does.Contain("updateLocalThirdPersonAvatar(localActor)"));
            Assert.That(script, Does.Contain("local third-person avatar ready:"));
            Assert.That(script, Does.Contain("procedural-mannequin"));
            Assert.That(script, Does.Contain("clientAssetPath('content/objects3D/pitcrew.kn5')"));
            Assert.That(script, Does.Contain("'quickPitsMenu'"));
            Assert.That(script, Does.Contain("ac.disableQuickMenuPitstop(true)"));
            Assert.That(script, Does.Contain("ac.accessMouseDelta(true, true, true)"));
            Assert.That(script, Does.Contain("yaw = yaw - mouse.x"));
            Assert.That(script, Does.Contain("local keyboardX = -inputAxis"));
            Assert.That(script, Does.Contain("ac.isKeyDown(ac.KeyIndex.Space)"));
            Assert.That(script, Does.Contain("(jump and 4 or 0)"));
            Assert.That(script, Does.Contain("predictedVerticalVelocity = 7.25"));
            Assert.That(script, Does.Contain("ac.KeyIndex.LeftControl"));
            Assert.That(script, Does.Contain("ac.KeyIndex.LeftMenu"));
            Assert.That(script, Does.Contain("ac.KeyIndex.C"));
            Assert.That(script, Does.Contain("(crouch and 8 or 0)"));
            Assert.That(script, Does.Contain("scoreboardHeld = ac.isKeyDown(ac.KeyIndex.Tab)"));
            Assert.That(script, Does.Contain("cursorUnlocked = scoreboardHeld or persistentCursor"));
            Assert.That(script, Does.Contain("if not cursorUnlocked then"));
            Assert.That(script, Does.Contain("ui.captureMouse(true)"));
            Assert.That(script, Does.Contain("ui.transparentWindow('asrc-fps-scoreboard-controls'"));
            Assert.That(script, Does.Contain("ui.checkbox('Keep mouse cursor visible after releasing TAB'"));
            Assert.That(script, Does.Contain("DEATHMATCH SCOREBOARD"));
            Assert.That(script, Does.Contain("bit.band(localActor.flags, 16)"));
            Assert.That(script, Does.Contain("bit.band(actor.flags, 64)"));
            Assert.That(script, Does.Contain("bit.band(actor.flags, 128)"));
            Assert.That(script, Does.Contain("cameraHeight = math.lerp"));
            Assert.That(script, Does.Contain("physics.raycastTrack"));
            Assert.That(script, Does.Contain("localTrackMovementBlocked"));
            Assert.That(script, Does.Contain("[ASRC FPS] first snapshot"));
            Assert.That(script, Does.Contain("[ASRC FPS] local actor snapshot acquired"));
            Assert.That(script, Does.Contain("[ASRC FPS] snapshot heartbeat"));
            Assert.That(script, Does.Contain("[ASRC FPS] input sample"));
            Assert.That(script, Does.Contain("[ASRC FPS] render state"));
            Assert.That(script, Does.Contain("[ASRC FPS] viewmodel render state"));
            Assert.That(script, Does.Contain("[ASRC FPS] viewmodel pipeline:"));
            Assert.That(script, Does.Contain("[ASRC FPS] viewmodel heartbeat:"));
            Assert.That(script, Does.Contain("[ASRC FPS] viewmodel stage:"));
            Assert.That(script, Does.Contain("callbacks=frameBegin:%d,draw3D:%d,drawUI:%d directDraw=%d/%d"));
            Assert.That(script, Does.Contain("ac.StructItem.key('ASRC_FpsClientDiagnostic')"));
            Assert.That(script, Does.Contain("viewmodel diagnostic sent to server:"));
            Assert.That(script, Does.Contain("[ASRC FPS] ready sent"));
            Assert.That(script, Does.Contain("ac.StructItem.key('ASRC_FpsShot')"));
            Assert.That(script, Does.Contain("createRifleModel = function"));
            Assert.That(script, Does.Contain("asrc_assault_rifle_viewmodel.kn5"));
            Assert.That(script, Does.Contain("asrc_assault_rifle_world.kn5"));
            Assert.That(script, Does.Contain("ac.getFolder(ac.FolderID.Root)"));
            Assert.That(script, Does.Contain("web.loadRemoteAssets"));
            Assert.That(script, Does.Contain("ac.getServerIP()"));
            Assert.That(script, Does.Contain("ac.getServerPortHTTP()"));
            Assert.That(script, Does.Contain("/fps/assets/asrc-fps-assets-v4.zip"));
            Assert.That(script, Does.Contain("crucial = rifleViewmodelFileName"));
            Assert.That(script, Does.Contain("requesting rifle assets:"));
            Assert.That(script, Does.Contain("rifle assets cached:"));
            Assert.That(script, Does.Contain("forceRenderableOn = true"));
            Assert.That(script, Does.Contain("cached assault-rifle viewmodel loaded:"));
            Assert.That(script, Does.Contain("createNode('ASRC_FPS_VIEWMODEL_HOLDER', false)"));
            Assert.That(script, Does.Contain("model:setDepthMode(render.DepthMode.Off)"));
            Assert.That(script, Does.Contain("viewmodelRoot:getLocalAABB()"));
            Assert.That(script, Does.Not.Contain("viewmodelRoot:getChildrenCount()"));
            Assert.That(script, Does.Not.Contain("viewmodelHolder:setPosition(position)"));
            Assert.That(script, Does.Not.Contain("viewmodelHolder:setOrientation(look, up)"));
            Assert.That(script, Does.Not.Contain("viewmodelRoot:setPosition(position)"));
            Assert.That(script, Does.Contain("viewmodelRenderPosition = position:clone()"));
            Assert.That(script, Does.Contain("viewmodelRenderLook = look:clone()"));
            Assert.That(script, Does.Contain("viewmodelRenderUp = up:clone()"));
            Assert.That(script, Does.Not.Contain("render.setTransform(viewmodelRenderTransform, true)"));
            Assert.That(script, Does.Contain("return render.mesh(viewmodelRenderParams)"));
            Assert.That(script, Does.Contain("render.setDepthMode(render.DepthMode.Off)"));
            Assert.That(script, Does.Contain("direct assault-rifle viewmodel draw completed"));
            Assert.That(script, Does.Contain("cached rifle viewmodel failed:"));
            Assert.That(script, Does.Contain("FPS RIFLE ASSET DOWNLOAD FAILED - CHECK SERVER HTTP PORT"));
            Assert.That(script, Does.Not.Contain("clientAssetPath(rifleViewmodelRelativePath)"));
            Assert.That(script, Does.Contain("drawFallbackRifle(size)"));
            Assert.That(script, Does.Contain("function script.draw3D()"));
            Assert.That(script, Does.Contain("render.debugLine(tracer.from, tracer.to"));
            Assert.That(script, Does.Contain("extension/audio/asrc_fps/rifle.wav"));
        });
    }
}
