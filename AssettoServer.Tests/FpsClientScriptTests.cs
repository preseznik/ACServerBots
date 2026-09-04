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
        int topLevelLocalCount = script.Split('\n')
            .Count(line => line.StartsWith("local ", StringComparison.Ordinal));
        int snapshotStart = script.IndexOf("hud.snapshotEvent", StringComparison.Ordinal);
        int rosterStart = script.IndexOf("hud.rosterEvent", StringComparison.Ordinal);
        string snapshotDefinition = script[snapshotStart..rosterStart];
        int shotStart = script.IndexOf("hud.shotEvent", StringComparison.Ordinal);
        int meshStart = script.IndexOf("local function appendBox", StringComparison.Ordinal);
        string shotDefinition = script[shotStart..meshStart];
        int updateStart = script.IndexOf("function script.update(dt)", StringComparison.Ordinal);
        int frameBeginStart = script.IndexOf("function script.frameBegin", StringComparison.Ordinal);
        int draw3DStart = script.IndexOf("function script.draw3D", StringComparison.Ordinal);
        int nativeSceneStart = script.IndexOf("local function updateNativeRifleViewmodel",
            StringComparison.Ordinal);
        int remoteSceneStart = script.IndexOf("local function updateRemoteActors",
            StringComparison.Ordinal);
        int localCollisionStart = script.IndexOf("local function localTrackProbeMovement",
            StringComparison.Ordinal);
        string updateDefinition = script[updateStart..frameBeginStart];
        int nativeSceneServiceStart = updateDefinition.IndexOf("ensureLocalViewmodel()",
            StringComparison.Ordinal);
        int inputCaptureStart = updateDefinition.IndexOf("scoreboardHeld = ac.isKeyDown",
            StringComparison.Ordinal);
        int predictionStart = updateDefinition.IndexOf("local forward = vec2",
            StringComparison.Ordinal);
        string frameBeginDefinition = script[frameBeginStart..draw3DStart];
        int drawUiStart = script.IndexOf("function script.drawUI", StringComparison.Ordinal);
        string draw3DDefinition = script[draw3DStart..drawUiStart];
        string nativeSceneDefinition = script[nativeSceneStart..localCollisionStart];
        string nativeRifleDefinition = script[nativeSceneStart..remoteSceneStart];

        Assert.Multiple(() =>
        {
            Assert.That(topLevelLocalCount, Is.LessThanOrEqualTo(190),
                "CSP Lua rejects a main chunk with more than 200 locals; keep headroom for future additions.");
            Assert.That(script, Does.Not.Contain("ac.setCarActive"));
            Assert.That(script, Does.Contain("physics.setCarNoInput(true)"));
            Assert.That(script, Does.Contain("physics.setCarNoInput(false)"));
            Assert.That(script, Does.Contain("ac.overrideCarControls()"));
            Assert.That(script, Does.Contain("setCarrierInputSuppressed(true)"));
            Assert.That(script, Does.Contain("setCarrierInputSuppressed(false)"));
            Assert.That(script, Does.Contain("carrierControlsOverride.combineAxis = not suppressed"));
            Assert.That(script, Does.Contain("carrierControlsOverride.gas = 0"));
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
            Assert.That(script, Does.Contain("selectedSlot = hud.loadout.activeSlot }, false, 255)"));
            Assert.That(script, Does.Contain("not state.isInMainMenu"));
            Assert.That(script, Does.Contain("releaseFpsCamera()"));
            Assert.That(script, Does.Contain("camera, cameraError = ac.grabCamera"));
            Assert.That(script, Does.Contain("camera.ownShare = 1"));
            Assert.That(script, Does.Contain("local previewCamera = {"));
            Assert.That(script, Does.Contain("state.isInMainMenu or not state.isSessionStarted"));
            Assert.That(script, Does.Contain("previewCamera.everEnteredGameplay then return false"));
            Assert.That(script, Does.Contain("local focus = actor.target + vec3(0, 1.25, 0)"));
            Assert.That(script, Does.Contain("physics.raycastTrack(focus, direction, distance"));
            Assert.That(script, Does.Contain("pre-Drive arena camera locked:"));
            Assert.That(updateDefinition, Does.Contain("previewCamera.apply(localActor)"));
            Assert.That(frameBeginDefinition, Does.Contain("previewCamera.isEligible(localActor)"));
            Assert.That(frameBeginDefinition, Does.Contain("previewCamera.apply(localActor)"));
            Assert.That(script, Does.Contain("fpsNearClip = 0.0001"));
            Assert.That(script, Does.Contain("ac.overrideCameraClipPlanes(fpsNearClip, nil)"));
            Assert.That(script, Does.Contain("params.clipNear = fpsNearClip"));
            Assert.That(script, Does.Contain("restoreFpsClipPlane()"));
            Assert.That(script, Does.Contain("camera near-clip request:"));
            Assert.That(script, Does.Contain("nearClip=%.3f clipMethod=%s"));
            Assert.That(script, Does.Contain("camera:dispose()"));
            Assert.That(script, Does.Contain("function script.frameBegin"));
            Assert.That(updateDefinition, Does.Not.Contain("applyFpsCamera(localActor)"));
            Assert.That(frameBeginDefinition,
                Does.Contain("applyFpsCamera(localActor, viewmodelFrameDt)"));
            Assert.That(script, Does.Contain("local function probeFirstPersonCameraClearance"));
            Assert.That(script, Does.Contain("local function resolveFirstPersonCameraPosition"));
            Assert.That(script, Does.Contain("firstPersonCameraRadius = 0.24"));
            Assert.That(script, Does.Contain("for _ = 1, 4 do"));
            Assert.That(script, Does.Contain("camera.transform.position = resolveFirstPersonCameraPosition"));
            Assert.That(script, Does.Contain("first-person camera clearance engaged:"));
            Assert.That(script, Does.Contain("cameraCorrections=%d"));
            Assert.That(updateDefinition, Does.Not.Contain("updateRifleViewmodel(dt"));
            Assert.That(nativeSceneDefinition, Does.Contain(
                "updateRifleViewmodel(dt, actor, viewmodelMove, viewmodelSprint)"));
            Assert.That(script, Does.Not.Contain("render.on('main.root.opaque', function()"));
            Assert.That(script, Does.Contain("root:setMotionStencil(1)"));
            Assert.That(script, Does.Contain("model:setMotionStencil(1)"));
            Assert.That(draw3DDefinition, Does.Not.Contain("updateNativeRifleViewmodel"));
            Assert.That(script, Does.Contain("ac.KeyIndex.F6"));
            Assert.That(script, Does.Contain("third-person over-shoulder"));
            Assert.That(script, Does.Contain("thirdPersonDistanceTarget = 3.2"));
            Assert.That(script, Does.Contain("thirdPersonDistanceMin = 1.25"));
            Assert.That(script, Does.Contain("thirdPersonDistanceMax = 7.0"));
            Assert.That(updateDefinition, Does.Contain("local wheel = ui.mouseWheel()"));
            Assert.That(updateDefinition, Does.Contain("thirdPersonEnabled and not cursorUnlocked"));
            Assert.That(updateDefinition, Does.Contain("ac.KeyIndex.LeftShift"));
            Assert.That(updateDefinition, Does.Contain("ac.KeyIndex.RightShift"));
            Assert.That(updateDefinition, Does.Contain(
                "thirdPersonDistanceTarget - wheel * fpsVisual.thirdPersonZoomStep"));
            Assert.That(script, Does.Contain(
                "focus - forward * fpsVisual.thirdPersonDistance"));
            Assert.That(script, Does.Contain("SHIFT + WHEEL %.1f m"));
            Assert.That(script, Does.Contain("physics.raycastTrack(focus, direction, distance"));
            Assert.That(script, Does.Contain("actor.root:setVisible(active and thirdPersonEnabled)"));
            Assert.That(script, Does.Contain(
                "viewmodelHolder:setPosition(viewmodelRenderPosition + ac.getSim().originShift)"));
            Assert.That(script, Does.Contain(
                "viewmodelHolder:setOrientation(viewmodelRenderLook, viewmodelRenderUp)"));
            Assert.That(script, Does.Contain("local cameraPosition = camera.transform.position:clone()"));
            Assert.That(script, Does.Contain("local look = camera.transform.look:clone()"));
            Assert.That(script, Does.Not.Contain("local up = camera.transform.up:clone()"));
            Assert.That(script, Does.Contain("local viewUp = vec3(-math.sin(yaw) * math.sin(pitch)"));
            Assert.That(script, Does.Contain("actor.root:setOrientation(vec3(math.sin(yaw)"));
            Assert.That(script, Does.Contain("groundYs = ac.StructItem.array"));
            Assert.That(script, Does.Contain("collisionDirections = ac.StructItem.array(ac.StructItem.byte()"));
            Assert.That(script, Does.Contain("collisionDirection / 254 * math.pi * 2"));
            Assert.That(updateDefinition, Does.Contain(
                "updateLocalThirdPersonAvatar(localActor, true)"));
            Assert.That(script, Does.Contain("local third-person avatar ready:"));
            Assert.That(script, Does.Contain("procedural-skinned-operator"));
            Assert.That(script, Does.Contain("local function updateRemoteActors(dt)"));
            Assert.That(updateDefinition, Does.Contain("updateRemoteActors(dt)"));
            Assert.That(nativeSceneDefinition, Does.Contain(
                "if active then ensureAvatar(actor) end"));
            Assert.That(nativeSceneServiceStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(nativeSceneServiceStart, Is.LessThan(inputCaptureStart),
                "CSP can interrupt the tail of online-script updates; native scene servicing must run early.");
            Assert.That(nativeSceneServiceStart, Is.LessThan(predictionStart),
                "Native scene servicing must precede the expensive prediction/collision pass.");
            Assert.That(frameBeginDefinition, Does.Not.Contain("updateRemoteActors"));
            Assert.That(draw3DDefinition, Does.Not.Contain("updateRemoteActors"));
            Assert.That(script, Does.Contain("remoteRender.actorSnapshotCount = visibleActors"));
            Assert.That(script, Does.Contain(
                "actor.root:setPosition(scenePosition + ac.getSim().originShift)"));
            Assert.That(script, Does.Contain("actor.root:setVisible(true, false)"));
            Assert.That(script, Does.Contain("if resetMotion then actor.root:clearMotion() end"));
            Assert.That(script, Does.Contain("corpseLifetime = 3.75"));
            Assert.That(script, Does.Contain("corpseFallSeconds = 0.72"));
            Assert.That(script, Does.Contain("fall * math.rad(84)"));
            Assert.That(script, Does.Contain("fpsVisual.setActorWeaponVisible(actor, not dead)"));
            Assert.That(snapshotDefinition, Does.Contain("fpsVisual.beginActorCorpse(actor)"));
            Assert.That(script, Does.Contain("anchor.y + velocity.y * age - 4.905 * age * age"));
            Assert.That(script, Does.Contain(
                "return effectClock - actor.corpseStarted < fpsVisual.corpseLifetime"));
            Assert.That(script, Does.Contain("actor.root:setOrientation(sceneLook, sceneUp)"));
            Assert.That(script, Does.Not.Contain("remoteAvatarRenderParams"));
            Assert.That(script, Does.Contain("native remote actor scene ready:"));
            Assert.That(script, Does.Not.Contain("content/objects3D/pitcrew.kn5"));
            Assert.That(script, Does.Contain("'quickPitsMenu'"));
            Assert.That(script, Does.Contain("ac.disableQuickMenuPitstop(true)"));
            Assert.That(script, Does.Contain("ac.accessMouseDelta(true, true, true)"));
            Assert.That(script, Does.Contain("yaw = yaw - mouse.x"));
            Assert.That(script, Does.Contain("local keyboardX = -inputAxis"));
            Assert.That(script, Does.Contain(
                "local rightX = -clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.RightThumbX))"));
            Assert.That(script, Does.Contain(
                "local rawX = -clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.LeftThumbX))"));
            Assert.That(script, Does.Contain(
                "local rawY = clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.LeftThumbY))"));
            Assert.That(script, Does.Contain(
                "selectInput(keyboardX, rawX, -clampStick(mapped.steer))"));
            Assert.That(script, Does.Contain("selectInput(keyboardY, rawY, 0)"));
            Assert.That(script, Does.Not.Contain("clampStick(mapped.gas - mapped.brake)"));
            Assert.That(script, Does.Contain("ac.GamepadAxis.LeftTrigger"));
            Assert.That(script, Does.Contain("ac.GamepadButton.Y"));
            Assert.That(script, Does.Contain("ac.GamepadButton.RightShoulder"));
            Assert.That(script, Does.Contain(
                "gamepadWeaponSwitch and not weaponSwitchWasHeld"));
            Assert.That(script, Does.Contain(
                "hud.loadout.activeSlot = hud.loadout.activeSlot == 0 and 1 or 0"));
            Assert.That(script, Does.Contain(
                "XBOX:  Y  SWITCH WEAPON    •    RB  GRENADE"));
            Assert.That(script, Does.Contain("ac.KeyIndex.RightButton"));
            Assert.That(script, Does.Contain("ui.mouseDown(ui.MouseButton.Right)"));
            Assert.That(script, Does.Contain("math.lerp(72, 56, fpsVisual.ads)"));
            Assert.That(script, Does.Contain("math.lerp(hipRight, adsRight, fpsVisual.ads)"));
            Assert.That(script, Does.Contain(
                "local adsForward = grenadeViewmodel and hipForward"));
            Assert.That(script, Does.Contain(
                "local pistolAdsRight = fpsVisual.loadedViewmodelAsset == 3 and 0.025 or 0.035"));
            Assert.That(script, Does.Contain(
                "local adsRight = grenadeViewmodel and hipRight or (pistolViewmodel and pistolAdsRight"));
            Assert.That(script, Does.Contain(
                "local adsUp = grenadeViewmodel and hipUp"));
            Assert.That(script, Does.Contain("local visualKickScale = math.lerp(1, 0.35, fpsVisual.ads)"));
            Assert.That(script, Does.Contain(
                "local downwardLook = math.clamp((-pitch - math.rad(35)) / math.rad(45), 0, 1)"));
            Assert.That(script, Does.Contain(
                "math.lerp(0.38, 0.14, fpsVisual.ads) * downwardCurve"));
            Assert.That(script, Does.Contain("- downwardPull - viewmodelKick"));
            Assert.That(script, Does.Contain("local cameraRecoilScale = math.lerp(1, 0.45, fpsVisual.ads)"));
            Assert.That(script, Does.Contain("function fpsVisual.stanceRecoilMultiplier(stance)"));
            Assert.That(script, Does.Contain("stance == 2 and 0.55 or stance == 1 and 0.7 or 1.08"));
            Assert.That(script, Does.Contain(
                "pitch + 0.011 * cameraRecoilScale * stanceRecoilScale"));
            Assert.That(script, Does.Contain("ac.LightSource(ac.LightType.Regular)"));
            Assert.That(script, Does.Contain("muzzleLightLifetime = 0.055"));
            Assert.That(script, Does.Contain("muzzleLightLocalRange = 5.5"));
            Assert.That(script, Does.Contain("muzzleLightRemoteRange = 2.25"));
            Assert.That(script, Does.Contain("muzzleLightRemoteFadeAt = 500"));
            Assert.That(script, Does.Contain(
                "state.light.range = localFirstPerson and fpsVisual.muzzleLightLocalRange"));
            Assert.That(script, Does.Contain(
                "state.light.fadeAt = localFirstPerson and 4.5 or fpsVisual.muzzleLightRemoteFadeAt"));
            Assert.That(script, Does.Contain("light.shadows = false"));
            Assert.That(script, Does.Contain("fpsVisual.updateMuzzleLights(visualNow)"));
            Assert.That(script, Does.Contain("state.light:dispose()"));
            Assert.That(script, Does.Contain("and not thirdPersonEnabled"));
            Assert.That(script, Does.Contain(
                "local renderedBase = actor.render:lengthSquared() > 0.001 and actor.render or actor.target"));
            Assert.That(script, Does.Contain("local renderedYaw = actor.yaw or actor.targetYaw"));
            Assert.That(script, Does.Contain("(fpsVisual.adsInput > 0.5 and 32 or 0)"));
            Assert.That(script, Does.Contain("if fpsVisual.adsInput > 0.05 then sprint = false end"));
            Assert.That(script, Does.Contain("ac.isKeyDown(ac.KeyIndex.Space)"));
            Assert.That(script, Does.Contain("(jump and 4 or 0)"));
            Assert.That(script, Does.Contain("local avatarPosition = actor.render:clone()"));
            Assert.That(script, Does.Contain("local standingProbeHeights = {0.52, 0.9, 1.48}"));
            Assert.That(script, Does.Contain("predictedVerticalVelocity = 7.25"));
            Assert.That(script, Does.Contain("ac.KeyIndex.LeftControl"));
            Assert.That(script, Does.Contain("ac.KeyIndex.LeftMenu"));
            Assert.That(script, Does.Contain("ac.KeyIndex.C"));
            Assert.That(script, Does.Contain("(crouch and 8 or 0)"));
            Assert.That(script, Does.Contain("ac.KeyIndex.LeftButton"));
            Assert.That(script, Does.Contain("ui.mouseDown(ui.MouseButton.Left)"));
            Assert.That(script, Does.Contain("[ASRC FPS] fire input captured:"));
            Assert.That(script, Does.Contain("ac.KeyIndex.R"));
            Assert.That(script, Does.Contain("(reload and 16 or 0)"));
            Assert.That(script, Does.Contain("scoreboardHeld = ac.isKeyDown(ac.KeyIndex.Tab)"));
            Assert.That(script, Does.Contain("cursorUnlocked = scoreboardHeld or persistentCursor"));
            Assert.That(script, Does.Contain("if not cursorUnlocked then"));
            Assert.That(script, Does.Contain("ui.captureMouse(true)"));
            Assert.That(script, Does.Contain("ui.transparentWindow('asrc-fps-scoreboard-controls'"));
            Assert.That(script, Does.Contain("ui.checkbox('Keep mouse cursor visible after releasing TAB'"));
            Assert.That(script, Does.Contain("DEATHMATCH SCOREBOARD"));
            Assert.That(script, Does.Contain("bit.band(localActor.flags, 16)"));
            Assert.That(script, Does.Contain("bit.band(actor.flags, 64)"));
            Assert.That(script, Does.Contain("predictionCollisionConstrained = true"));
            Assert.That(script, Does.Contain("predictionClearSnapshots >= 3"));
            Assert.That(script, Does.Contain("[ASRC FPS] prediction collision constraint:"));
            Assert.That(script, Does.Contain("[ASRC FPS] prediction hard correction:"));
            Assert.That(updateDefinition, Does.Not.Contain("predictionCollisionLocked"));
            Assert.That(script, Does.Contain("bit.band(actor.flags, 128)"));
            Assert.That(script, Does.Contain("function fpsVisual.actorStance(actor)"));
            Assert.That(script, Does.Contain("if actor.id == localSessionID then return localStance end"));
            Assert.That(script, Does.Contain("bit.band(actionState, 2) ~= 0"));
            Assert.That(script, Does.Contain("modernAssetRevision = 8"));
            Assert.That(script, Does.Contain("fpsVisual.crouchSuppressedUntilRelease = true"));
            Assert.That(script, Does.Contain(
                "operatorStanceGroundOffsets = { [1] = -0.50, [2] = -0.50 }"));
            Assert.That(script, Does.Contain(
                "actor.modernModel:setPosition(vec3(0, stanceGroundOffset, 0))"));
            Assert.That(script, Does.Not.Contain("actorVisualGroundOffset"));
            Assert.That(script, Does.Contain("if not dead then scenePosition = avatarPosition end"));
            Assert.That(script, Does.Not.Contain("actorScenePosition"));
            Assert.That(script, Does.Contain(
                "local aimingMovementScale = fpsVisual.adsInput > 0.5 and 0.4 or 1"));
            Assert.That(script, Does.Contain("cameraHeight = math.lerp"));
            Assert.That(script, Does.Contain("physics.raycastTrack"));
            Assert.That(script, Does.Contain("localTrackProbeMovement"));
            Assert.That(script, Does.Contain("localTrackResolveMovement"));
            Assert.That(script, Does.Contain("collisionProbeOffsets = {-1, -0.5, 0, 0.5, 1}"));
            Assert.That(script, Does.Contain("if amount > 0 then"));
            Assert.That(script, Does.Contain("viewmodelWallRetraction"));
            Assert.That(script, Does.Contain("[ASRC FPS] first snapshot"));
            Assert.That(script, Does.Contain("[ASRC FPS] local actor snapshot acquired"));
            Assert.That(script, Does.Contain("[ASRC FPS] snapshot heartbeat"));
            Assert.That(script, Does.Contain("[ASRC FPS] input sample"));
            Assert.That(script, Does.Contain("[ASRC FPS] render state"));
            Assert.That(script, Does.Contain("[ASRC FPS] remote render state"));
            Assert.That(script, Does.Contain("[ASRC FPS] viewmodel render state"));
            Assert.That(script, Does.Contain("[ASRC FPS] viewmodel pipeline:"));
            Assert.That(script, Does.Contain("[ASRC FPS] viewmodel heartbeat:"));
            Assert.That(script, Does.Contain("[ASRC FPS] viewmodel stage:"));
            Assert.That(script, Does.Contain("callbacks=frameBegin:%d,draw3D:%d,drawUI:%d directDraw=%d/%d"));
            Assert.That(script, Does.Contain("ac.StructItem.key('ASRC_FpsClientDiagnostic')"));
            Assert.That(script, Does.Contain("remoteActorID = ac.StructItem.byte()"));
            Assert.That(script, Does.Contain("remoteTarget = ac.StructItem.vec3()"));
            Assert.That(script, Does.Contain("remoteRender = ac.StructItem.vec3()"));
            Assert.That(script, Does.Contain(
                "diagnosticRemoteActor.root:getPosition() - ac.getSim().originShift"));
            Assert.That(nativeSceneDefinition, Does.Contain(
                "actor.render:set(math.lerp(actor.render, actor.target, poseBlend))"));
            Assert.That(nativeSceneDefinition, Does.Contain("math.exp(-dt * 40)"));
            Assert.That(script, Does.Contain("local function lerpAngle(current, target, mix)"));
            Assert.That(script, Does.Contain(
                "local delta = (target - current + math.pi) % (math.pi * 2) - math.pi"));
            Assert.That(script, Does.Not.Contain("math.lerpAngle"));
            Assert.That(script, Does.Contain("pipeline = 21"));
            Assert.That(script, Does.Contain("native-scene-v21-angle-lerp-fix"));
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
            Assert.That(script, Does.Contain("/fps/assets/asrc-fps-assets-v21.zip"));
            Assert.That(script, Does.Contain("asrc_compact_smg_viewmodel.kn5"));
            Assert.That(script, Does.Contain("asrc_compact_smg_world.kn5"));
            Assert.That(script, Does.Contain("asrc_compact_smg_reload_empty.ksanim"));
            Assert.That(script, Does.Contain("asrc_desert_eagle_viewmodel.kn5"));
            Assert.That(script, Does.Contain("asrc_desert_eagle_world.kn5"));
            Assert.That(script, Does.Contain("asrc_desert_eagle_idle.ksanim"));
            Assert.That(script, Does.Contain("asrc_desert_eagle_fire.ksanim"));
            Assert.That(script, Does.Contain("asrc_desert_eagle_equip.ksanim"));
            Assert.That(script, Does.Contain("asrc_desert_eagle_sprint.ksanim"));
            Assert.That(script, Does.Contain("asrc_desert_eagle_reload.ksanim"));
            Assert.That(script, Does.Contain("asrc_colt_1911_viewmodel.kn5"));
            Assert.That(script, Does.Contain("asrc_colt_1911_world.kn5"));
            Assert.That(script, Does.Contain("asrc_colt_1911_idle.ksanim"));
            Assert.That(script, Does.Contain("asrc_colt_1911_fire.ksanim"));
            Assert.That(script, Does.Contain("asrc_colt_1911_equip.ksanim"));
            Assert.That(script, Does.Contain("asrc_colt_1911_sprint.ksanim"));
            Assert.That(script, Does.Contain("asrc_colt_1911_reload.ksanim"));
            Assert.That(script, Does.Contain("asrc_frag_grenade_viewmodel.kn5"));
            Assert.That(script, Does.Contain("asrc_frag_grenade_world.kn5"));
            Assert.That(script, Does.Contain("asrc_frag_grenade_throw.ksanim"));
            Assert.That(script, Does.Contain("asrc_sticky_grenade_viewmodel.kn5"));
            Assert.That(script, Does.Contain("asrc_sticky_grenade_world.kn5"));
            Assert.That(script, Does.Contain("asrc_sticky_grenade_throw.ksanim"));
            Assert.That(script, Does.Contain("fpsVisual.updateGrenadeModels()"));
            Assert.That(script, Does.Contain("fpsVisual.activeGrenadeType = localActor.lethal"));
            Assert.That(script, Does.Contain("fpsVisual.grenadeReleasedAt = effectClock"));
            Assert.That(script, Does.Contain("fpsVisual.grenadeHoldPhase"));
            Assert.That(script, Does.Contain("fpsVisual.grenadeReleasePoint"));
            Assert.That(script, Does.Contain("local cooked grenade released"));
            Assert.That(script, Does.Contain("fpsVisual.explosionSmoke:emit"));
            Assert.That(script, Does.Contain("fpsVisual.illuminateExplosion"));
            Assert.That(script, Does.Contain("viewmodelPistolPoseSeedPending = false"));
            Assert.That(script, Does.Contain(
                "fpsVisual.pistolClips(assetKey).reload, 0, true"));
            Assert.That(script, Does.Contain(
                "and fpsVisual.viewmodelPistolPoseSeedPending then"));
            Assert.That(script, Does.Contain(
                "if actor.reloadRemaining > 0 then"));
            Assert.That(script, Does.Contain(
                "clip = fpsVisual.isPistolAsset(fpsVisual.loadedViewmodelAsset)"));
            Assert.That(script, Does.Contain(
                "fpsVisual.loadoutClips(fpsVisual.loadedViewmodelAsset)[clip]"));
            Assert.That(script, Does.Not.Contain("viewmodelSupportArm"));
            Assert.That(script, Does.Contain(
                "or fpsVisual.isLoadoutAsset(fpsVisual.loadedViewmodelAsset)"));
            Assert.That(script, Does.Contain(
                "weapon.archivePath == fpsVisual.loadoutAssetArchivePath"));
            Assert.That(script, Does.Contain("fpsVisual.requestLoadoutAssets()"));
            Assert.That(script, Does.Contain("fpsVisual.weaponAssetKey(actor)"));
            Assert.That(script, Does.Contain(
                "actor.weaponMesh:setVisible(visible and not fpsVisual.isLoadoutAsset(actor.weaponAsset)"));
            Assert.That(script, Does.Contain("fileName = 'asrc_carbine_hud.png'"));
            Assert.That(script, Does.Contain("ui.drawImage(fpsVisual.hudWeapon.imagePath"));
            Assert.That(script, Does.Contain("asrc_rifle_diffuse.png"));
            Assert.That(script, Does.Contain("asrc_operator_skin.png"));
            Assert.That(script, Does.Contain("__ASRC_FPS_THEME__"));
            Assert.That(script, Does.Contain("/fps/assets/asrc-fps-modern-v8.zip"));
            Assert.That(script, Does.Contain("asrc_modern_operator_carbine.kn5"));
            Assert.That(script, Does.Contain("asrc_modern_carbine_viewmodel.kn5"));
            Assert.That(script, Does.Contain("asrc_modern_carbine_pickup.kn5"));
            Assert.That(script, Does.Contain("ac.StructItem.key('ASRC_FpsPickup')"));
            Assert.That(script, Does.Contain("text = '+1 MAGAZINE'"));
            Assert.That(script, Does.Contain("function fpsVisual.updatePickups()"));
            Assert.That(script, Does.Contain("modernViewmodel and 0.32"));
            Assert.That(script, Does.Contain("modernViewmodel and -0.18"));
            Assert.That(script, Does.Contain("pistolViewmodel and 0.39"));
            Assert.That(script, Does.Contain("pistolViewmodel and -0.15"));
            Assert.That(script, Does.Contain("pistolViewmodel and -0.24"));
            Assert.That(script, Does.Contain("pistolViewmodel and -0.12"));
            Assert.That(script, Does.Contain(
                "local pistolReloadPhase = pistolViewmodel and actor.reloadRemaining > 0"));
            Assert.That(script, Does.Contain("local pistolReloadAngle = math.rad(22)"));
            Assert.That(script, Does.Contain(
                "viewmodelRenderLook = look * reloadCos - viewUp * reloadSin"));
            Assert.That(script, Does.Contain(
                "viewmodelRenderUp = viewUp * reloadCos + look * reloadSin"));
            Assert.That(script, Does.Contain("modernViewmodel and -0.32"));
            Assert.That(script, Does.Contain("modernViewmodel and 0.67"));
            Assert.That(script, Does.Contain("asrc_modern_operator_vault.ksanim"));
            Assert.That(script, Does.Contain("asrc_modern_operator_crouch_idle.ksanim"));
            Assert.That(script, Does.Contain("asrc_modern_operator_crouch_move.ksanim"));
            Assert.That(script, Does.Contain("asrc_modern_operator_prone_idle.ksanim"));
            Assert.That(script, Does.Contain("asrc_modern_operator_prone_crawl.ksanim"));
            Assert.That(script, Does.Contain("model:setAnimation"));
            Assert.That(script, Does.Contain("actor.modernModel:setAnimation"));
            Assert.That(script, Does.Contain("actor.modernModel:blendAnimation"));
            Assert.That(script, Does.Contain("actionStates = ac.StructItem.uint32()"));
            Assert.That(script, Does.Contain("bit.lshift(1, capacity + i)"));
            Assert.That(script, Does.Contain("MODERN THEME FAILED - BLOCKS FALLBACK"));
            Assert.That(script, Does.Contain("crucial = rifleViewmodelFileName"));
            Assert.That(script, Does.Contain("requesting rifle assets:"));
            Assert.That(script, Does.Contain("rifle assets cached:"));
            Assert.That(script, Does.Contain("forceRenderableOn = true"));
            Assert.That(script, Does.Contain("cached weapon viewmodel loaded:"));
            Assert.That(script, Does.Contain(
                "createBoundingSphereNode('ASRC_FPS_VIEWMODEL_HOLDER', 2)"));
            Assert.That(script, Does.Contain("model:setDepthMode(render.DepthMode.Normal)"));
            Assert.That(script, Does.Contain("viewmodelRoot:getLocalAABB()"));
            Assert.That(script, Does.Not.Contain("viewmodelRoot:getChildrenCount()"));
            Assert.That(script, Does.Contain("viewmodelHolder:setPosition(viewmodelRenderPosition"));
            Assert.That(script, Does.Contain("viewmodelHolder:setOrientation(viewmodelRenderLook"));
            Assert.That(script, Does.Not.Contain("viewmodelRoot:setPosition(position)"));
            Assert.That(script, Does.Contain("viewmodelRenderPosition = position:clone()"));
            Assert.That(script, Does.Contain("viewmodelRenderLook = look:clone()"));
            Assert.That(script, Does.Contain("viewmodelRenderUp = viewUp:clone()"));
            Assert.That(script, Does.Not.Contain("render.setTransform(viewmodelRenderTransform, true)"));
            Assert.That(script, Does.Not.Contain("viewmodelRenderParams"));
            Assert.That(nativeSceneDefinition, Does.Contain("viewmodelHolder:clearMotion()"));
            Assert.That(nativeSceneDefinition, Does.Contain("native-scene:deferred"));
            Assert.That(frameBeginDefinition, Does.Not.Contain("ensureLocalViewmodel()"));
            Assert.That(nativeRifleDefinition, Does.Not.Contain("ensureLocalViewmodel()"));
            Assert.That(updateDefinition, Does.Contain(
                "updateLocalThirdPersonAvatar(localActor, true)"));
            Assert.That(frameBeginDefinition, Does.Contain(
                "updateLocalThirdPersonAvatar(localActor, false)"));
            Assert.That(frameBeginDefinition,
                Does.Contain("updateNativeRifleViewmodel(viewmodelFrameDt)"));
            Assert.That(script, Does.Contain("native assault-rifle viewmodel scene ready"));
            Assert.That(script, Does.Contain("cached weapon viewmodel failed:"));
            Assert.That(script, Does.Contain("FPS RIFLE ASSET DOWNLOAD FAILED - CHECK SERVER HTTP PORT"));
            Assert.That(script, Does.Not.Contain("clientAssetPath(rifleViewmodelRelativePath)"));
            Assert.That(script, Does.Contain("drawFallbackRifle(size)"));
            Assert.That(script, Does.Contain("function script.draw3D()"));
            Assert.That(script, Does.Contain("direct shot-effect templates ready"));
            Assert.That(script, Does.Contain("drawDirectShotEffects()"));
            Assert.That(shotDefinition, Does.Contain("from = muzzleOrigin:clone()"));
            Assert.That(shotDefinition, Does.Contain("flashFrom = muzzleOrigin:clone()"));
            Assert.That(shotDefinition, Does.Contain("to = targetPoint"));
            Assert.That(shotDefinition, Does.Contain("travelTime = math.clamp"));
            Assert.That(shotDefinition, Does.Contain("while #tracers >= maxTracers"));
            Assert.That(shotDefinition, Does.Contain("expiresAt = now + travelTime"));
            Assert.That(shotDefinition, Does.Contain("targetID = ac.StructItem.byte()"));
            Assert.That(shotDefinition, Does.Contain("targetSpawnCount"));
            Assert.That(shotDefinition, Does.Contain("first shot event received:"));
            Assert.That(shotDefinition, Does.Contain("while #impacts >= maxImpactMarks"));
            Assert.That(script, Does.Contain("maxImpactMarks = 96"));
            Assert.That(script, Does.Contain("(now - tracer.bornAt) / tracer.travelTime"));
            Assert.That(script, Does.Contain("if tracers[i].expiresAt <= visualNow"));
            Assert.That(script, Does.Contain("clearActorImpacts(message.victimID)"));
            Assert.That(script, Does.Contain("target.spawnCount ~= impact.targetSpawnCount"));
            Assert.That(script, Does.Contain("render.setTransform(tracer.flashFrom"));
            Assert.That(script, Does.Contain("render.mesh(tracerRenderParams)"));
            Assert.That(script, Does.Contain("render.mesh(impactRenderParams)"));
            Assert.That(script, Does.Contain("render.mesh(sparkRenderParams)"));
            Assert.That(script, Does.Contain("local function muzzleFlashRenderParams(tracer)"));
            Assert.That(script, Does.Contain("if distance >= 60 then return muzzleFlashFarRenderParams end"));
            Assert.That(script, Does.Contain("if distance >= 20 then return muzzleFlashMidRenderParams end"));
            Assert.That(script, Does.Contain("render.mesh(muzzleFlashRenderParams(tracer))"));
            Assert.That(script, Does.Not.Contain("render.debugLine(tracer.from, tracer.to"));
            Assert.That(script, Does.Not.Contain("render.debugPlane(impact.position"));
            Assert.That(script, Does.Contain("ac.Particles.Sparks"));
            Assert.That(script, Does.Contain("ac.Particles.Smoke"));
            Assert.That(snapshotDefinition, Does.Contain("ammo = ac.StructItem.array"));
            Assert.That(snapshotDefinition, Does.Contain("reserveMagazines = ac.StructItem.array"));
            Assert.That(snapshotDefinition, Does.Contain("vitals = ac.StructItem.array"));
            Assert.That(snapshotDefinition, Does.Contain(
                "actor.stamina = bit.rshift(message.vitals[i], 8)"));
            Assert.That(snapshotDefinition, Does.Contain("reloadRemaining = ac.StructItem.array"));
            Assert.That(snapshotDefinition, Does.Contain("spawnCounts = ac.StructItem.array"));
            Assert.That(snapshotDefinition, Does.Contain("remote actor respawn reconciled:"));
            Assert.That(shotDefinition, Does.Contain("impact = ac.StructItem.byte()"));
            Assert.That(script, Does.Contain("model:setMaterialTexture('txDiffuse', rifleDiffusePath)"));
            Assert.That(script, Does.Contain("mesh:setMaterialTexture('txDiffuse', texturePath)"));
            Assert.That(script, Does.Contain("local operatorUV ="));
            Assert.That(script, Does.Contain("operatorUV.torso"));
            Assert.That(script, Does.Contain("operatorUV.pants"));
            Assert.That(script, Does.Contain("operatorUV.boot"));
            Assert.That(script, Does.Contain("procedural-skinned-operator"));
            Assert.That(script, Does.Contain("RELOADING  %.1fs"));
            Assert.That(script, Does.Contain("%d RESERVE MAGS"));
            Assert.That(script, Does.Not.Contain("ASSAULT RIFLE  |  INFINITE"));
            Assert.That(script, Does.Contain("extension/audio/asrc_fps/rifle.wav"));
            Assert.That(script, Does.Contain("extension/audio/asrc_fps/explosion.wav"));
            Assert.That(script, Does.Contain("ac.StructItem.key('asrc.fps.hud.v5')"));
            Assert.That(script, Does.Contain("localStamina = ac.StructItem.byte()"));
            Assert.That(script, Does.Contain(
                "fpsVisual.stamina.value - fpsVisual.stamina.drainPerSecond * dt"));
            Assert.That(script, Does.Contain("STAMINA  %d%%"));
            Assert.That(script, Does.Contain("adsActive = ac.StructItem.byte()"));
            Assert.That(script, Does.Contain(
                "hud.bridge.adsActive = fpsVisual.ads > 0.05 and 1 or 0"));
            Assert.That(script, Does.Contain("if fpsVisual.ads <= 0.05 then"));
            Assert.That(script, Does.Contain("ac.StructItem.key('ASRC_FpsAward')"));
            Assert.That(script, Does.Contain("awardPopupTexts"));
            Assert.That(script, Does.Contain("HEADSHOT"));
            Assert.That(script, Does.Contain("ONE SHOT"));
            Assert.That(script, Does.Contain("capacity = 32"));
            Assert.That(script, Does.Contain("appHeartbeat = ac.StructItem.float()"));
            Assert.That(script, Does.Contain("function hud.appOwnsHud()"));
            Assert.That(script, Does.Contain("age >= -0.1 and age <= 0.5"));
            Assert.That(script, Does.Contain("function hud.hasRadarLineOfSight"));
            Assert.That(script, Does.Contain("distance > 40"));
            Assert.That(script, Does.Contain("hud.radarReveal[message.shooterID] = effectClock + 2"));
            Assert.That(script, Does.Contain("bit.band(actor.flags, 8) == 0"));
            Assert.That(script, Does.Contain("hud.radarReveal[id] = nil"));
            Assert.That(script, Does.Contain("function hud.drawFallbackRadar"));
            Assert.That(script, Does.Contain("COMBAT RADAR  40 m"));
            Assert.That(script, Does.Contain("local right = -(offset.x * rightX + offset.z * rightZ)"));
            Assert.That(script, Does.Contain("function hud.exclusiveCallback(mode)"));
            Assert.That(script, Does.Contain("ui.onExclusiveHUD(hud.exclusiveCallback, true)"));
            Assert.That(script, Does.Contain("hud.drawingFallback = true"));
            Assert.That(script, Does.Contain("if mode == 'pause' and previewCamera.everEnteredGameplay"));
            Assert.That(script, Does.Contain("function hud.drawPauseMenu()"));
            Assert.That(script, Does.Contain("local mouse = ui.mousePos()"));
            Assert.That(script, Does.Contain("local function pauseButton("));
            Assert.That(script, Does.Contain("ui.mouseClicked(ui.MouseButton.Left)"));
            Assert.That(script, Does.Not.Contain("asrc-fps-pause-controls"));
            Assert.That(script, Does.Not.Contain("pauseControlsActive"));
            Assert.That(script, Does.Contain("hud.bindings = ac.storage({"));
            Assert.That(script, Does.Contain("'asrc.fps.bindings.'"));
            Assert.That(script, Does.Contain("hud.bindingDown('fire'"));
            Assert.That(script, Does.Contain("hud.bindingDown('sprint'"));
            Assert.That(script, Does.Contain("hud.bindingDown('crouch'"));
            Assert.That(script, Does.Contain("hud.bindingDown('reload'"));
            Assert.That(script, Does.Contain("hud.bindingDown('jump'"));
            Assert.That(script, Does.Contain("hud.bindingCapture"));
            Assert.That(script, Does.Contain("ac.isKeyPressed(candidate.key)"));
            Assert.That(script, Does.Contain("RESET DEFAULTS"));
            Assert.That(script, Does.Not.Contain("ac.ControlButton('asrc.fps/"));
            Assert.That(script, Does.Not.Contain("ui.beginToolWindow('asrc-fps-controls-bindings'"));
            Assert.That(script, Does.Contain("FPS CONTROLS"));
            Assert.That(script, Does.Contain("HIP-FIRE AIM SENSITIVITY"));
            Assert.That(script, Does.Contain("ADS AIM SENSITIVITY"));
            Assert.That(script, Does.Contain("crouchToggle = false"));
            Assert.That(script, Does.Contain("hud.controlSettings.crouchToggle"));
            Assert.That(script, Does.Contain("(crouchToggleMode and 64 or 0)"));
            Assert.That(script, Does.Contain("modeLabel = hud.controlSettings.crouchToggle and 'TOGGLE' or 'HOLD'"));
            Assert.That(script, Does.Contain("hipSensitivity = 1.0"));
            Assert.That(script, Does.Contain("adsSensitivity = 0.8"));
            Assert.That(script, Does.Contain("hud.aimSensitivity(fpsVisual.adsInput)"));
            Assert.That(script, Does.Contain("mouse.x * 0.0022 * aimSensitivity"));
            Assert.That(script, Does.Contain("rightX * dt * 2.8 * aimSensitivity"));
            Assert.That(script, Does.Contain("TIME & WEATHER"));
            Assert.That(script, Does.Contain("ac.StructItem.key('ASRC_FpsEnvironmentRequest')"));
            Assert.That(script, Does.Contain("Authoritative WeatherFX controls"));
            Assert.That(script, Does.Contain("environmentRequestEvent({"));
            Assert.That(script, Does.Not.Contain("ac.setAppWindowVisible('PurePlanner'"));
            Assert.That(script, Does.Not.Contain("if hud.pausePage == 'pure' then return 'apps' end"));
            Assert.That(script, Does.Contain("if not ok then return fallback end\n  return down"));
            Assert.That(script, Does.Not.Contain(
                "hud.bindingDown('sprint', ac.isKeyDown(ac.KeyIndex.LeftShift))\n      or ac.isKeyDown(ac.KeyIndex.RightShift)"));
            Assert.That(script, Does.Contain("if gameplayActive and not previousGameplayActive then"));
            Assert.That(script, Does.Contain("hud.pausePage = 'main'"));
            Assert.That(script, Does.Contain("[ASRC FPS] pause menu action: return to match"));
            Assert.That(script, Does.Contain("MATCH MENU"));
            Assert.That(script, Does.Contain("DEATHMATCH  •  LIVE SERVER"));
            Assert.That(script, Does.Contain("RETURN TO MATCH"));
            Assert.That(script, Does.Contain("ac.tryToPause(false)"));
            Assert.That(script, Does.Contain("ASSETTO CORSA OPTIONS"));
            Assert.That(script, Does.Contain("if hud.nativePauseMenu then return false end"));
            Assert.That(script, Does.Contain(
                "if hud.nativePauseMenu then return false end\n    hud.drawPauseMenu()"));
            Assert.That(script, Does.Contain("CONFIRM LEAVE"));
            Assert.That(script, Does.Contain("ac.shutdownAssettoCorsa()"));
            Assert.That(script, Does.Contain("hud.publish(dt)"));
        });
    }

    [Test]
    public void ConfigureClientScript_InjectsOneValidatedThemeMarker()
    {
        string source = $"local theme = '{FpsWorld.VisualThemeMarker}'";

        Assert.Multiple(() =>
        {
            Assert.That(FpsWorld.ConfigureClientScript(source,
                AssettoServer.Server.Configuration.Extra.FpsVisualTheme.Blocks),
                Is.EqualTo("local theme = 'Blocks'"));
            Assert.That(FpsWorld.ConfigureClientScript(source,
                AssettoServer.Server.Configuration.Extra.FpsVisualTheme.Modern),
                Is.EqualTo("local theme = 'Modern'"));
            Assert.Throws<InvalidDataException>(() => FpsWorld.ConfigureClientScript(
                "local theme = 'Blocks'",
                AssettoServer.Server.Configuration.Extra.FpsVisualTheme.Blocks));
            Assert.Throws<InvalidDataException>(() => FpsWorld.ConfigureClientScript(
                source + source,
                AssettoServer.Server.Configuration.Extra.FpsVisualTheme.Modern));
        });
    }
}
