local license = [[
Copyright (C) 2026 Niewiarowski, compujuckel

This program is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or any later version.
]]

local capacity = 16
local fpsVisual = {
  requested = '__ASRC_FPS_THEME__',
  modern = false,
  active = 'Blocks',
  error = nil,
  adsInput = 0,
  ads = 0,
  thirdPersonDistance = 3.2,
  thirdPersonDistanceTarget = 3.2,
  thirdPersonDistanceMin = 1.25,
  thirdPersonDistanceMax = 7.0,
  thirdPersonZoomStep = 0.4,
  modernAssetRevision = 8,
  -- KSANIM conversion maps the officer's local vertical root translation to Z.
  -- CSP preview520 then raises the crouch/prone hips track by 50 cm relative to
  -- standing. Ground the complete animated child in scene space, whose Y axis is
  -- unambiguous, so feet, hips, torso and head all move together.
  operatorStanceGroundOffsets = { [1] = -0.50, [2] = -0.50 },
  crouchSuppressedUntilRelease = false,
  crouchToggleReleaseStands = false,
  carrierControlsOverride = nil,
  carrierControlsOverrideErrorLogged = false,
  viewmodelFireUntil = 0,
  viewmodelEquipUntil = 0,
  viewmodelPistolPoseSeedPending = false,
  muzzleLights = {},
  muzzleLightUnavailable = false,
  muzzleLightLifetime = 0.055,
  muzzleLightLocalRange = 5.5,
  muzzleLightRemoteRange = 2.25,
  muzzleLightRemoteFadeAt = 500,
  muzzleLightReuseSeconds = 1,
  corpseLifetime = 3.75,
  corpseFallSeconds = 0.72,
  stamina = {
    value = 100,
    exhausted = false,
    recoveryDelay = 0,
    maximum = 100,
    drainPerSecond = 20,
    recoveryPerSecond = 18,
    recoveryDelaySeconds = 0.9,
    exhaustionRelease = 25,
  },
  hudWeapon = {
    archivePath = '/fps/assets/asrc-fps-assets-v20.zip',
    fileName = 'asrc_carbine_hud.png',
    imagePath = nil,
    loading = false,
    failed = false,
  },
  loadoutAssetArchivePath = '/fps/assets/asrc-fps-assets-v20.zip',
  loadoutAssetFolder = nil,
  loadoutAssetsLoading = false,
  loadoutAssetsFailed = false,
  compactSmgViewmodelFileName = 'asrc_compact_smg_viewmodel.kn5',
  compactSmgWorldModelFileName = 'asrc_compact_smg_world.kn5',
  compactSmgClips = {
    idle = 'asrc_compact_smg_idle.ksanim',
    fire = 'asrc_compact_smg_fire.ksanim',
    reload = 'asrc_compact_smg_reload.ksanim',
    reload_empty = 'asrc_compact_smg_reload_empty.ksanim',
    equip = 'asrc_compact_smg_equip.ksanim',
    sprint = 'asrc_compact_smg_sprint.ksanim',
  },
  desertEagleViewmodelFileName = 'asrc_desert_eagle_viewmodel.kn5',
  desertEagleWorldModelFileName = 'asrc_desert_eagle_world.kn5',
  desertEagleClips = {
    idle = 'asrc_desert_eagle_idle.ksanim',
    fire = 'asrc_desert_eagle_fire.ksanim',
    equip = 'asrc_desert_eagle_equip.ksanim',
    sprint = 'asrc_desert_eagle_sprint.ksanim',
    reload = 'asrc_desert_eagle_reload.ksanim',
  },
  colt1911ViewmodelFileName = 'asrc_colt_1911_viewmodel.kn5',
  colt1911WorldModelFileName = 'asrc_colt_1911_world.kn5',
  colt1911Clips = {
    idle = 'asrc_colt_1911_idle.ksanim',
    fire = 'asrc_colt_1911_fire.ksanim',
    equip = 'asrc_colt_1911_equip.ksanim',
    sprint = 'asrc_colt_1911_sprint.ksanim',
    reload = 'asrc_colt_1911_reload.ksanim',
  },
  loadedViewmodelAsset = nil,
  pickups = {},
  operatorClips = {
    aim_idle = 'asrc_modern_operator_aim_idle.ksanim',
    aim_up = 'asrc_modern_operator_aim_up.ksanim',
    aim_down = 'asrc_modern_operator_aim_down.ksanim',
    walk_forward = 'asrc_modern_operator_walk_forward.ksanim',
    walk_backward = 'asrc_modern_operator_walk_backward.ksanim',
    strafe_left = 'asrc_modern_operator_strafe_left.ksanim',
    strafe_right = 'asrc_modern_operator_strafe_right.ksanim',
    sprint = 'asrc_modern_operator_sprint.ksanim',
    crouch_idle = 'asrc_modern_operator_crouch_idle.ksanim',
    crouch_move = 'asrc_modern_operator_crouch_move.ksanim',
    prone_idle = 'asrc_modern_operator_prone_idle.ksanim',
    prone_crawl = 'asrc_modern_operator_prone_crawl.ksanim',
    jump_start = 'asrc_modern_operator_jump_start.ksanim',
    airborne = 'asrc_modern_operator_airborne.ksanim',
    land = 'asrc_modern_operator_land.ksanim',
    mantle = 'asrc_modern_operator_mantle.ksanim',
    vault = 'asrc_modern_operator_vault.ksanim',
    fire = 'asrc_modern_operator_fire.ksanim',
    reload = 'asrc_modern_operator_reload.ksanim',
    death = 'asrc_modern_operator_death.ksanim',
  },
  viewmodelClips = {
    idle = 'asrc_modern_carbine_idle.ksanim',
    fire = 'asrc_modern_carbine_fire.ksanim',
    reload = 'asrc_modern_carbine_reload.ksanim',
    reload_empty = 'asrc_modern_carbine_reload_empty.ksanim',
    equip = 'asrc_modern_carbine_equip.ksanim',
    sprint = 'asrc_modern_carbine_sprint.ksanim',
  },
}
local actors = {}
local names = {}
local localSessionID = car.sessionID
local sequence = 0
local sendAccumulator = 0
local yaw = 0
local pitch = 0
local remainingSeconds = 0
local killLimit = 20
local matchState = 0
local winnerID = 255
local killFeed = {}
local effectClock = 0
local hitMarkerUntil = 0
local tracers = {}
local impacts = {}
local clearActorImpacts = nil
local sparks = {}
local maxTracers = 16
local maxImpactMarks = 96
local rifleSounds = {}
local viewmodelHolder = nil
local viewmodelRoot = nil
local viewmodelKick = 0
local viewmodelBobTime = 0
local viewmodelWallRetraction = 0
local viewmodelMove = vec2()
local viewmodelSprint = false
local viewmodelFrameDt = 1 / 60
local localMuzzlePosition = vec3()
local viewmodelPipelineVersion = 'native-scene-v21-angle-lerp-fix'
local viewmodelLastStage = 'not-started'
local viewmodelLastStageDetail = ''
local viewmodelStagesSeen = {}
local viewmodelUpdateAttempts = 0
local viewmodelUpdateCompletions = 0
local viewmodelFrameBeginCalls = 0
local viewmodelDraw3DCalls = 0
local viewmodelDrawUICalls = 0
local viewmodelDiagnosticAccumulator = 0.5
local viewmodelLastPosition = nil
local viewmodelRenderPosition = nil
local viewmodelRenderLook = nil
local viewmodelRenderUp = nil
local viewmodelDirectDrawAttempts = 0
local viewmodelDirectDrawCompletions = 0
local viewmodelDirectDrawPending = 0
local viewmodelDirectDrawFailures = 0
local viewmodelDirectRenderFailureLogged = false
local viewmodelServerDiagnosticAccumulator = 5
local viewmodelLastSentStage = nil
local viewmodelDiagnosticSendOk = true
local rifleAudioFallbackLogged = false
local clientPackError = nil
local remoteRifleFallbackLogged = false
local remoteRender = {
  actorSnapshotCount = 0,
  actorsDrawn = 0,
  drawAttempts = 0,
  drawCompletions = 0,
  drawPending = 0,
  drawFailures = 0,
  failureLogged = false,
  readyLogged = false,
}
local shotEffectTemplateHolder = nil
local tracerRenderParams = nil
local impactRenderParams = nil
local sparkRenderParams = nil
local muzzleFlashNearRenderParams = nil
local muzzleFlashMidRenderParams = nil
local muzzleFlashFarRenderParams = nil
local shotRender = {
  eventsReceived = 0,
  effectsRendered = 0,
  failureLogged = false,
  readyLogged = false,
}
local assettoRoot = ac.getFolder(ac.FolderID.Root)
local function clientAssetPath(relativePath)
  if assettoRoot == nil or assettoRoot == '' then return relativePath end
  return assettoRoot .. '/' .. relativePath
end
local rifleAudioRelativePath = 'extension/audio/asrc_fps/rifle.wav'
local rifleAudioPath = clientAssetPath(rifleAudioRelativePath)
local rifleAssetArchivePath = '/fps/assets/asrc-fps-assets-v20.zip'
local rifleViewmodelFileName = 'asrc_assault_rifle_viewmodel.kn5'
local rifleWorldModelFileName = 'asrc_assault_rifle_world.kn5'
local rifleDiffuseFileName = 'asrc_rifle_diffuse.png'
local operatorSkinFileName = 'asrc_operator_skin.png'
local rifleAssetFolder = nil
local rifleAssetsLoading = false
local rifleAssetsFailed = false
local rifleAssetWaitLogged = false
local rifleViewmodelPath = nil
local rifleWorldModelPath = nil
local rifleDiffusePath = nil
local operatorSkinPath = nil
fpsVisual.pickupFileName = rifleWorldModelFileName
fpsVisual.pickupPath = nil
if fpsVisual.requested == 'Modern' then
  fpsVisual.modern = true
  fpsVisual.active = 'Modern'
  -- CSP caches remote asset archives by URL. Every regenerated KN5/KSANIM payload
  -- must advance this revision or clients can keep rendering the previous poses.
  rifleAssetArchivePath = '/fps/assets/asrc-fps-modern-v8.zip'
  rifleViewmodelFileName = 'asrc_modern_carbine_viewmodel.kn5'
  rifleWorldModelFileName = 'asrc_modern_operator_carbine.kn5'
  fpsVisual.pickupFileName = 'asrc_modern_carbine_pickup.kn5'
  rifleDiffuseFileName = nil
  operatorSkinFileName = nil
elseif fpsVisual.requested ~= 'Blocks' then
  fpsVisual.error = 'INVALID FPS THEME - USING BLOCKS'
end
local inputSendOk = true
local gameplayActive = false
local previousGameplayActive = nil
local firstSnapshotLogged = false
local localActorSnapshotLogged = false
local lastSnapshotDiagnosticSequence = nil
local lastSnapshotDiagnosticPosition = nil
local inputDiagnosticAccumulator = 0
local renderDiagnosticAccumulator = 0.5
local cameraRetryAccumulator = 1
local inputWasActive = false
local fireCaptureLogged = false
local predictedGroundY = nil
local predictedVerticalVelocity = 0
local jumpWasHeld = false
local predictedHorizontalVelocity = vec2()
local predictedAirborne = false
local predictionCollisionConstrained = false
local predictionClearSnapshots = 0
local localStance = 0 -- 0 standing, 1 crouching, 2 prone

function fpsVisual.stanceRecoilMultiplier(stance)
  return stance == 2 and 0.55 or stance == 1 and 0.7 or 1.08
end

local crouchWasHeld = false
local crouchHeldSeconds = 0
local crouchLatched = false
local cameraHeight = 1.65
local thirdPersonEnabled = false
local thirdPersonToggleWasHeld = false
local weaponSwitchWasHeld = false
local localAvatarReady = false
local localAvatarKind = 'none'
local localAvatarErrorLogged = false
local scoreboardHeld = false
local persistentCursor = false
local cursorUnlocked = false
local camera = nil
local cameraError = nil
local previewCamera = {
  everEnteredGameplay = false,
  locked = false,
  actorID = nil,
  spawnCount = nil,
  position = vec3(),
  look = vec3(0, -0.2, 1),
}
-- A perspective camera cannot use a literal zero near plane. Keep it effectively
-- on the camera surface so close viewmodel geometry exits behind the view instead
-- of exposing a visible receiver/stock cross-section.
local fpsNearClip = 0.0001
local fpsClipPlaneApplied = false
local fpsClipPlaneMethod = 'not-applied'
local fpsOriginalCarCameraClipNear = {}
local firstPersonCameraRadius = 0.24
local firstPersonCameraSkin = 0.025
local firstPersonCameraOffset = vec3()
local firstPersonCameraConstrained = false
local firstPersonCameraCorrections = 0
local firstPersonCameraProbeDirections = {
  vec3(1, 0, 0), vec3(-1, 0, 0), vec3(0, 0, 1), vec3(0, 0, -1),
  vec3(0.70710678, 0, 0.70710678), vec3(-0.70710678, 0, 0.70710678),
  vec3(0.70710678, 0, -0.70710678), vec3(-0.70710678, 0, -0.70710678),
  vec3(0, 1, 0), vec3(0, -1, 0),
  vec3(0.70710678, 0.70710678, 0), vec3(-0.70710678, 0.70710678, 0),
  vec3(0, 0.70710678, 0.70710678), vec3(0, 0.70710678, -0.70710678),
}
local carsRoot = ac.findNodes('carsRoot:yes')
local hiddenCarrierRoots = {}
local createRifleModel
local requestRifleAssets
local playRifleSound
local impactSparks = nil
local impactSmoke = nil
local hud = {
  protocol = 5,
  capacity = 32,
  killFeedCapacity = 6,
  awardPopupCapacity = 4,
  awardPopups = {},
  bridge = nil,
  bridgeError = nil,
  bridgeMismatchLogged = false,
  onlineSequence = 0,
  publishAccumulator = 0,
  radarAccumulator = 0,
  radarReveal = {},
  radarVisible = {},
  actorScratch = {},
  drawingFallback = false,
  exclusiveSubscription = nil,
  nativePauseMenu = false,
  leaveServerArmed = false,
  pauseInputLogged = false,
  pausePage = 'main',
  controlsErrorLogged = false,
  controlsContentLogged = false,
  bindingCapture = nil,
  bindingCaptureAfter = 0,
  environmentWeather = 15,
  environmentTimeSeconds = 13 * 60 * 60,
  environmentDraftWeather = 15,
  environmentDraftTimeSeconds = 13 * 60 * 60,
  environmentDraftReady = false,
  maximumHealth = 100,
  loadout = {
    catalogReceived = false,
    confirmed = false,
    result = 'WAITING FOR SERVER CATALOG',
    allowedMainWeapons = 0,
    allowedLethals = 0,
    allowedSecondaryWeapons = 0,
    mainWeapon = 1,
    lethal = 16,
    secondaryWeapon = 4,
    activeSlot = 0,
    lethalsRemaining = 0,
  },
}

hud.bindingDefaults = {
  fire = ac.KeyIndex.LeftButton,
  sprint = ac.KeyIndex.LeftShift,
  crouch = ac.KeyIndex.C,
  reload = ac.KeyIndex.R,
  jump = ac.KeyIndex.Space,
  grenade = ac.KeyIndex.G,
  melee = ac.KeyIndex.V,
}
hud.bindings = ac.storage({
  fire = hud.bindingDefaults.fire,
  sprint = hud.bindingDefaults.sprint,
  crouch = hud.bindingDefaults.crouch,
  reload = hud.bindingDefaults.reload,
  jump = hud.bindingDefaults.jump,
  grenade = hud.bindingDefaults.grenade,
  melee = hud.bindingDefaults.melee,
}, 'asrc.fps.bindings.')
hud.aimSettings = ac.storage({
  hipSensitivity = 1.0,
  adsSensitivity = 0.8,
}, 'asrc.fps.aim.')
hud.controlSettings = ac.storage({
  crouchToggle = false,
}, 'asrc.fps.controls.')
hud.loadoutStorage = ac.storage({
  mainWeapon = 1,
  lethal = 16,
  secondaryWeapon = 4,
}, 'asrc.fps.loadout.')
hud.itemNames = {
  [1] = 'ASSAULT RIFLE', [2] = 'MP5 SMG',
  [3] = 'DESERT EAGLE', [4] = 'COLT 1911',
  [16] = 'FRAG GRENADE', [17] = 'STICKY GRENADE',
}

function hud.itemAllowed(mask, itemID)
  return bit.band(mask, bit.lshift(1, itemID)) ~= 0
end

function hud.aimSensitivity(ads)
  local hip = math.clamp(tonumber(hud.aimSettings.hipSensitivity) or 1.0, 0.2, 3.0)
  local aimed = math.clamp(tonumber(hud.aimSettings.adsSensitivity) or 0.8, 0.2, 3.0)
  return math.lerp(hip, aimed, math.clamp(ads or 0, 0, 1))
end

hud.bindingCandidates = {
  { key = ac.KeyIndex.LeftButton, name = 'MOUSE 1' },
  { key = ac.KeyIndex.RightButton, name = 'MOUSE 2' },
  { key = ac.KeyIndex.MiddleButton, name = 'MOUSE 3' },
  { key = ac.KeyIndex.XButton1, name = 'MOUSE 4' },
  { key = ac.KeyIndex.XButton2, name = 'MOUSE 5' },
  { key = ac.KeyIndex.Space, name = 'SPACE' },
  { key = ac.KeyIndex.LeftShift, name = 'LEFT SHIFT' },
  { key = ac.KeyIndex.RightShift, name = 'RIGHT SHIFT' },
  { key = ac.KeyIndex.LeftControl, name = 'LEFT CTRL' },
  { key = ac.KeyIndex.RightControl, name = 'RIGHT CTRL' },
  { key = ac.KeyIndex.LeftMenu, name = 'LEFT ALT' },
  { key = ac.KeyIndex.RightMenu, name = 'RIGHT ALT' },
  { key = ac.KeyIndex.Tab, name = 'TAB' },
  { key = ac.KeyIndex.Return, name = 'ENTER' },
  { key = ac.KeyIndex.Back, name = 'BACKSPACE' },
  { key = ac.KeyIndex.Left, name = 'LEFT ARROW' },
  { key = ac.KeyIndex.Right, name = 'RIGHT ARROW' },
  { key = ac.KeyIndex.Up, name = 'UP ARROW' },
  { key = ac.KeyIndex.Down, name = 'DOWN ARROW' },
}
for code = ac.KeyIndex.D0, ac.KeyIndex.D9 do
  hud.bindingCandidates[#hud.bindingCandidates + 1] = { key = code, name = tostring(code - ac.KeyIndex.D0) }
end
for code = ac.KeyIndex.A, ac.KeyIndex.Z do
  hud.bindingCandidates[#hud.bindingCandidates + 1] = { key = code, name = string.char(code) }
end
for code = ac.KeyIndex.F1, ac.KeyIndex.F12 do
  hud.bindingCandidates[#hud.bindingCandidates + 1] = {
    key = code, name = 'F' .. tostring(code - ac.KeyIndex.F1 + 1),
  }
end

function hud.bindingName(key)
  for i = 1, #hud.bindingCandidates do
    if hud.bindingCandidates[i].key == key then return hud.bindingCandidates[i].name end
  end
  return 'KEY ' .. tostring(key)
end

function hud.bindingDown(action, fallback)
  local key = hud.bindings[action]
  if key == nil then return fallback end
  local ok, down = pcall(ac.isKeyDown, key)
  if not ok then return fallback end
  return down
end

pcall(function()
  impactSparks = ac.Particles.Sparks({
    color = rgbm(1, 0.72, 0.28, 1), life = 0.35, size = 0.025,
    directionSpread = 0.85, positionSpread = 0.025,
  })
  impactSmoke = ac.Particles.Smoke({
    color = rgbm(0.18, 0.18, 0.18, 0.38), colorConsistency = 0.8,
    thickness = 0.65, life = 0.55, size = 0.055, spreadK = 0.7,
    growK = 0.65, targetYVelocity = 0.12,
  })
end)

function hud.connect()
  local ok, result = pcall(function()
    return ac.connect({
      ac.StructItem.key('asrc.fps.hud.v5'),
      protocol = ac.StructItem.uint16(),
      onlineSequence = ac.StructItem.uint32(),
      onlineHeartbeat = ac.StructItem.float(),
      appProtocol = ac.StructItem.uint16(),
      appHeartbeat = ac.StructItem.float(),
      gameplayActive = ac.StructItem.byte(),
      localActorID = ac.StructItem.byte(),
      localHealth = ac.StructItem.uint16(),
      localMaximumHealth = ac.StructItem.uint16(),
      localStamina = ac.StructItem.byte(),
      localAmmo = ac.StructItem.byte(),
      localReserveMagazines = ac.StructItem.byte(),
      localReloadRemaining = ac.StructItem.float(),
      localMainWeapon = ac.StructItem.byte(),
      localLethal = ac.StructItem.byte(),
      localSecondaryWeapon = ac.StructItem.byte(),
      localActiveSlot = ac.StructItem.byte(),
      localLethalsRemaining = ac.StructItem.byte(),
      localKills = ac.StructItem.uint16(),
      localDeaths = ac.StructItem.uint16(),
      localScore = ac.StructItem.uint32(),
      viewYaw = ac.StructItem.float(),
      matchState = ac.StructItem.byte(),
      remainingSeconds = ac.StructItem.float(),
      killLimit = ac.StructItem.uint16(),
      winnerID = ac.StructItem.byte(),
      scoreboardHeld = ac.StructItem.byte(),
      cursorUnlocked = ac.StructItem.byte(),
      persistentCursor = ac.StructItem.byte(),
      appPersistentCursor = ac.StructItem.byte(),
      hitMarkerRemaining = ac.StructItem.float(),
      adsActive = ac.StructItem.byte(),
      linkState = ac.StructItem.byte(),
      clientError = ac.StructItem.string(128),
      actorCount = ac.StructItem.byte(),
      actorIDs = ac.StructItem.array(ac.StructItem.byte(), hud.capacity),
      actorFlags = ac.StructItem.array(ac.StructItem.byte(), hud.capacity),
      radarFlags = ac.StructItem.array(ac.StructItem.byte(), hud.capacity),
      actorPositions = ac.StructItem.array(ac.StructItem.vec3(), hud.capacity),
      actorYaws = ac.StructItem.array(ac.StructItem.float(), hud.capacity),
      actorHealth = ac.StructItem.array(ac.StructItem.uint16(), hud.capacity),
      actorKills = ac.StructItem.array(ac.StructItem.uint16(), hud.capacity),
      actorDeaths = ac.StructItem.array(ac.StructItem.uint16(), hud.capacity),
      actorScores = ac.StructItem.array(ac.StructItem.uint32(), hud.capacity),
      actorNames = ac.StructItem.array(ac.StructItem.string(32), hud.capacity),
      killFeedCount = ac.StructItem.byte(),
      killFeed = ac.StructItem.array(ac.StructItem.string(72), hud.killFeedCapacity),
      awardPopupCount = ac.StructItem.byte(),
      awardPopupTexts = ac.StructItem.array(ac.StructItem.string(64), hud.awardPopupCapacity),
      awardPopupAlphas = ac.StructItem.array(ac.StructItem.float(), hud.awardPopupCapacity),
    }, false, ac.SharedNamespace.Shared)
  end)
  if ok then
    hud.bridge = result
    ac.log('[ASRC FPS] HUD bridge ready: asrc.fps.hud.v5')
  else
    hud.bridgeError = tostring(result)
    ac.warn('[ASRC FPS] HUD bridge unavailable; online fallback remains active: '
      .. hud.bridgeError)
  end
end

function hud.appOwnsHud()
  if hud.bridge == nil or hud.bridge.appProtocol ~= hud.protocol then
    if hud.bridge ~= nil and hud.bridge.appProtocol ~= 0
        and not hud.bridgeMismatchLogged then
      hud.bridgeMismatchLogged = true
      ac.warn(string.format('[ASRC FPS] HUD app bridge mismatch: online=%d app=%d',
        hud.protocol, hud.bridge.appProtocol))
    end
    return false
  end
  local age = ui.time() - hud.bridge.appHeartbeat
  return age >= -0.1 and age <= 0.5
end

function hud.hasRadarLineOfSight(localActor, targetActor)
  local origin = localActor.target + vec3(0, 1.45, 0)
  local target = targetActor.target + vec3(0, 1.1, 0)
  local offset = target - origin
  local distance = offset:length()
  if distance < 0.01 or distance > 40 then return false end
  local direction = offset / distance
  local hitPoint, hitNormal = vec3(), vec3()
  local hit = physics.raycastTrack(origin, direction, distance, hitPoint, hitNormal, false, false)
  return hit < 0 or hit >= distance - 0.2
end

function hud.updateRadar(localActor)
  table.clear(hud.radarVisible)
  if localActor == nil then return end
  for id, actor in pairs(actors) do
    if id ~= localSessionID and bit.band(actor.flags, 1) ~= 0
        and bit.band(actor.flags, 2) == 0 and bit.band(actor.flags, 8) == 0 then
      local inRange = (actor.target - localActor.target):lengthSquared() <= 40 * 40
      if inRange then
        local shotReveal = (hud.radarReveal[id] or 0) > effectClock
        if shotReveal or hud.hasRadarLineOfSight(localActor, actor) then
          hud.radarVisible[id] = shotReveal and 2 or 1
        end
      end
    end
  end
end

function hud.publish(dt)
  if hud.bridge == nil then return end
  local now = ui.time()
  hud.bridge.protocol = hud.protocol
  hud.bridge.onlineHeartbeat = now
  hud.bridge.gameplayActive = gameplayActive and 1 or 0
  hud.publishAccumulator = hud.publishAccumulator + dt
  hud.radarAccumulator = hud.radarAccumulator + dt
  if hud.publishAccumulator < 0.05 then return end
  hud.publishAccumulator = hud.publishAccumulator - 0.05
  local localActor = actors[localSessionID]
  if hud.radarAccumulator >= 0.1 then
    hud.radarAccumulator = hud.radarAccumulator % 0.1
    hud.updateRadar(localActor)
  end

  hud.onlineSequence = hud.onlineSequence + 1
  hud.bridge.onlineSequence = hud.onlineSequence
  hud.bridge.localActorID = localSessionID
  hud.bridge.localHealth = localActor ~= nil and localActor.health or 0
  hud.bridge.localMaximumHealth = hud.maximumHealth
  hud.bridge.localStamina = math.clamp(math.floor(fpsVisual.stamina.value + 0.5), 0, 100)
  hud.bridge.localAmmo = localActor ~= nil and localActor.ammo or 0
  hud.bridge.localReserveMagazines = localActor ~= nil and localActor.reserveMagazines or 0
  hud.bridge.localReloadRemaining = localActor ~= nil and localActor.reloadRemaining or 0
  hud.bridge.localMainWeapon = localActor ~= nil and (localActor.mainWeapon or 1)
    or hud.loadout.mainWeapon
  hud.bridge.localLethal = localActor ~= nil and (localActor.lethal or 16)
    or hud.loadout.lethal
  hud.bridge.localSecondaryWeapon = localActor ~= nil and (localActor.secondaryWeapon or 4)
    or hud.loadout.secondaryWeapon
  hud.bridge.localActiveSlot = localActor ~= nil and (localActor.activeSlot or 0)
    or hud.loadout.activeSlot
  hud.bridge.localLethalsRemaining = localActor ~= nil
    and (localActor.lethalsRemaining or 0) or 0
  hud.bridge.localKills = localActor ~= nil and localActor.kills or 0
  hud.bridge.localDeaths = localActor ~= nil and localActor.deaths or 0
  hud.bridge.localScore = localActor ~= nil and localActor.score or 0
  hud.bridge.viewYaw = yaw
  hud.bridge.matchState = matchState
  hud.bridge.remainingSeconds = remainingSeconds
  hud.bridge.killLimit = killLimit
  hud.bridge.winnerID = winnerID
  hud.bridge.scoreboardHeld = scoreboardHeld and 1 or 0
  hud.bridge.cursorUnlocked = cursorUnlocked and 1 or 0
  hud.bridge.persistentCursor = persistentCursor and 1 or 0
  hud.bridge.hitMarkerRemaining = math.max(0, hitMarkerUntil - effectClock)
  hud.bridge.adsActive = fpsVisual.ads > 0.05 and 1 or 0
  hud.bridge.linkState = localActor == nil and 0 or inputSendOk and 1 or 2
  hud.bridge.clientError = clientPackError or ''

  table.clear(hud.actorScratch)
  for _, actor in pairs(actors) do
    if bit.band(actor.flags, 1) ~= 0 then hud.actorScratch[#hud.actorScratch + 1] = actor end
  end
  table.sort(hud.actorScratch, function(left, right) return left.id < right.id end)
  local actorCount = math.min(hud.capacity, #hud.actorScratch)
  hud.bridge.actorCount = actorCount
  for index = 0, actorCount - 1 do
    local actor = hud.actorScratch[index + 1]
    hud.bridge.actorIDs[index] = actor.id
    hud.bridge.actorFlags[index] = actor.flags
    hud.bridge.radarFlags[index] = hud.radarVisible[actor.id] or 0
    hud.bridge.actorPositions[index] = actor.target
    hud.bridge.actorYaws[index] = actor.targetYaw
    hud.bridge.actorHealth[index] = actor.health
    hud.bridge.actorKills[index] = actor.kills
    hud.bridge.actorDeaths[index] = actor.deaths
    hud.bridge.actorScores[index] = actor.score
    hud.bridge.actorNames[index] = string.sub(names[actor.id] or ('Operative ' .. actor.id), 1, 32)
  end

  local feedStart = math.max(1, #killFeed - hud.killFeedCapacity + 1)
  local feedCount = math.min(hud.killFeedCapacity, #killFeed)
  hud.bridge.killFeedCount = feedCount
  for index = 0, feedCount - 1 do
    hud.bridge.killFeed[index] = string.sub(killFeed[feedStart + index].text, 1, 72)
  end
  local popupCount = math.min(hud.awardPopupCapacity, #hud.awardPopups)
  hud.bridge.awardPopupCount = popupCount
  for index = 0, popupCount - 1 do
    local popup = hud.awardPopups[index + 1]
    hud.bridge.awardPopupTexts[index] = string.sub(popup.text, 1, 64)
    hud.bridge.awardPopupAlphas[index] = math.min(1, popup.age / 0.15, popup.ttl / 0.4)
  end
end

hud.connect()

local function vec3Text(value)
  return string.format('(%.3f, %.3f, %.3f)', value.x, value.y, value.z)
end

local function lerpAngle(current, target, mix)
  local delta = (target - current + math.pi) % (math.pi * 2) - math.pi
  return current + delta * mix
end

function fpsVisual.smoothstep01(value)
  value = math.clamp(value, 0, 1)
  return value * value * (3 - 2 * value)
end

function fpsVisual.clearActorCorpse(actor)
  actor.corpseStarted = nil
  actor.corpseAnchor = nil
  actor.corpseVelocity = nil
  actor.corpseGroundY = nil
  actor.corpseYaw = nil
  actor.corpseFallSign = nil
end

function fpsVisual.setActorWeaponVisible(actor, visible)
  if actor.weaponRoot ~= nil and actor.weaponRoot ~= false then
    actor.weaponRoot:setVisible(visible, false)
  end
  if actor.weaponMesh ~= nil and actor.weaponMesh ~= false then
    actor.weaponMesh:setVisible(visible and not fpsVisual.isLoadoutAsset(actor.weaponAsset), false)
  end
end

function fpsVisual.beginActorCorpse(actor)
  if actor.corpseStarted ~= nil then return end
  local velocity = vec3()
  if actor.animationLastPosition ~= nil then
    velocity:set((actor.render - actor.animationLastPosition)
      / math.max(viewmodelFrameDt, 1 / 120))
    local horizontalSpeed = math.sqrt(velocity.x * velocity.x + velocity.z * velocity.z)
    if horizontalSpeed > 4.5 then
      local scale = 4.5 / horizontalSpeed
      velocity.x = velocity.x * scale
      velocity.z = velocity.z * scale
    end
    velocity.y = math.clamp(velocity.y, -6, 3)
  end
  actor.corpseStarted = effectClock
  actor.corpseAnchor = actor.render:clone()
  actor.corpseVelocity = velocity
  actor.corpseGroundY = actor.groundY ~= nil
    and math.min(actor.groundY, actor.corpseAnchor.y) or actor.corpseAnchor.y
  actor.corpseYaw = actor.yaw
  actor.corpseFallSign = (actor.id + (actor.deaths or 0)) % 2 == 0 and 1 or -1
  actor.animationDeathStarted = effectClock
end

function fpsVisual.actorSceneActive(actor)
  if bit.band(actor.flags, 1) == 0 then return false end
  if bit.band(actor.flags, 2) == 0 then return true end
  if actor.corpseStarted == nil then return true end
  return effectClock - actor.corpseStarted < fpsVisual.corpseLifetime
end

function fpsVisual.actorStance(actor)
  -- Local third person should react in the same frame as input instead of waiting for
  -- the next 20 Hz snapshot. Remote actors use both representations of prone: the
  -- original compact flag and the compact redundant action-state bit.
  if actor.id == localSessionID then return localStance end
  local actionState = actor.actionState or 0
  if bit.band(actor.flags, 128) ~= 0
      or (bit.band(actionState, 1) == 0 and bit.band(actionState, 2) ~= 0) then
    return 2
  end
  if bit.band(actor.flags, 32) ~= 0 then return 1 end
  return 0
end

function fpsVisual.actorScenePose(actor)
  local forward = vec3(math.sin(actor.yaw), 0, math.cos(actor.yaw))
  local up = vec3(0, 1, 0)
  if bit.band(actor.flags, 2) == 0 then
    if actor.corpseStarted ~= nil then fpsVisual.clearActorCorpse(actor) end
    return actor.render, forward, up, true
  end
  fpsVisual.beginActorCorpse(actor)
  local age = math.max(0, effectClock - actor.corpseStarted)
  if age >= fpsVisual.corpseLifetime then return actor.render, forward, up, false end

  -- CSP does not expose a safe per-bone ragdoll for dynamically loaded skinned KN5s.
  -- Preserve the authored limb collapse and pivot the complete operator at its feet.
  -- The previous 18-degree tip was barely visible; this reaches a full prone pose,
  -- retains death momentum and adds a small deterministic roll before settling.
  local anchor = actor.corpseAnchor or actor.render
  local velocity = actor.corpseVelocity or vec3()
  local drag = 2.4
  local travel = (1 - math.exp(-age * drag)) / drag
  local position = anchor:clone()
  position.x = position.x + velocity.x * travel
  position.z = position.z + velocity.z * travel
  position.y = math.max(actor.corpseGroundY or anchor.y,
    anchor.y + velocity.y * age - 4.905 * age * age)

  local fall = fpsVisual.smoothstep01(age / fpsVisual.corpseFallSeconds)
  local settleAge = math.max(0, age - fpsVisual.corpseFallSeconds)
  local settle = math.sin(settleAge * 15) * math.exp(-settleAge * 8) * math.rad(3)
  local angle = (fall * math.rad(84) + settle) * (actor.corpseFallSign or 1)
  local roll = fpsVisual.smoothstep01(math.clamp((age - 0.16) / 0.62, 0, 1))
    * math.rad(((actor.id * 17 + (actor.deaths or 0) * 11) % 2 == 0) and 11 or -11)
  local baseForward = vec3(math.sin(actor.corpseYaw or actor.yaw), 0,
    math.cos(actor.corpseYaw or actor.yaw))
  local cosine, sine = math.cos(angle), math.sin(angle)
  local baseRight = vec3(baseForward.z, 0, -baseForward.x)
  forward:set(baseForward * cosine + vec3(0, 1, 0) * sine)
  local fallenUp = vec3(0, 1, 0) * cosine - baseForward * sine
  up:set(fallenUp * math.cos(roll) + baseRight * math.sin(roll))
  return position, forward, up, true
end

local function markViewmodelStage(stage, detail)
  viewmodelLastStage = stage
  viewmodelLastStageDetail = detail == nil and '' or tostring(detail)
  if viewmodelStagesSeen[stage] then return end
  viewmodelStagesSeen[stage] = true
  ac.log('[ASRC FPS] viewmodel stage: ' .. stage
    .. (viewmodelLastStageDetail ~= '' and ('; ' .. viewmodelLastStageDetail) or ''))
end

local function runViewmodelStage(stage, action)
  markViewmodelStage(stage .. ':begin')
  local ok, result = pcall(action)
  if not ok then
    markViewmodelStage(stage .. ':failed', result)
    ac.warn('[ASRC FPS] viewmodel stage failed: ' .. stage .. '; error=' .. tostring(result))
    return false
  end
  markViewmodelStage(stage .. ':complete')
  return true
end

ac.log(string.format('[ASRC FPS] script loaded: session=%s carIndex=%s cameraActive=%s cameraError=%s',
  tostring(localSessionID), tostring(car.index),
  'false', 'awaiting arena preview or Drive'))
ac.log(string.format('[ASRC FPS] client asset paths: root=%s remoteArchive=%s audio=%s',
  tostring(assettoRoot), rifleAssetArchivePath, rifleAudioPath))
ac.log('[ASRC FPS] viewmodel pipeline: ' .. viewmodelPipelineVersion)
ac.log('[ASRC FPS] visual theme requested=' .. fpsVisual.requested
  .. '; active=' .. fpsVisual.active)

-- FPS has its own match clock, scoreboard and damage display. In particular,
-- the stock leaderboard assumes the local AC car is driving a normal timed
-- session and crashes when the carrier is used only as a network identity.
ac.disableExtraHUDElements({
  'sessionTime', 'fuel', 'proximity', 'leaderboard', 'startingLights',
  'wrongWay', 'damage', 'quickPitsMenu',
}, true)
ac.disableQuickMenuPitstop(true)
physics.setGentleStop(car.index, true)

-- setCarNoInput() remains the primary carrier lock. The explicit controls
-- override also consumes trigger/pedal input on CSP builds which continue to
-- evaluate the underlying AC controls while the FPS actor owns input.
do
  local ok, result = pcall(function() return ac.overrideCarControls() end)
  if ok then
    fpsVisual.carrierControlsOverride = result
  else
    ac.warn('[ASRC FPS] carrier controls override unavailable; using game-rule lock: '
      .. tostring(result))
  end
end

local function setCarrierInputSuppressed(suppressed)
  if fpsVisual.carrierControlsOverride == nil then return end
  local ok, err = pcall(function()
    fpsVisual.carrierControlsOverride.combineAxis = not suppressed
    fpsVisual.carrierControlsOverride.steer = suppressed and 0 or math.huge
    fpsVisual.carrierControlsOverride.gas = 0
    fpsVisual.carrierControlsOverride.brake = 0
    fpsVisual.carrierControlsOverride.handbrake = suppressed and 1 or 0
    fpsVisual.carrierControlsOverride.clutch = 1
  end)
  if not ok then
    fpsVisual.carrierControlsOverride = nil
    if not fpsVisual.carrierControlsOverrideErrorLogged then
      fpsVisual.carrierControlsOverrideErrorLogged = true
      ac.warn('[ASRC FPS] carrier controls override failed; using game-rule lock: '
        .. tostring(err))
    end
  end
end

local function hideCarrierCars()
  for i = 0, sim.carsCount - 1 do
    if hiddenCarrierRoots[i] == nil then
      local root = ac.findNodes('carRoot:' .. i)
      if #root > 0 then
        -- Keep the AC car active: CSP and the stock leaderboard require the
        -- local participant to remain in the active roster. Only hide its
        -- scene graph while the FPS avatar and camera replace its rendering.
        root:setVisible(false)
        hiddenCarrierRoots[i] = root
      end
    end
  end
end

hud.inputEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsInput'),
  sequence = ac.StructItem.uint32(),
  move = ac.StructItem.vec2(),
  yaw = ac.StructItem.float(),
  pitch = ac.StructItem.float(),
  buttons = ac.StructItem.uint16(),
  selectedSlot = ac.StructItem.byte(),
}, function() end, nil, true)

hud.loadoutSelectEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsLoadoutSelect'),
  mainWeapon = ac.StructItem.byte(),
  lethal = ac.StructItem.byte(),
  secondaryWeapon = ac.StructItem.byte(),
}, function() end)

hud.loadoutCatalogEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsLoadoutCatalog'),
  allowedMainWeapons = ac.StructItem.uint32(),
  allowedLethals = ac.StructItem.uint32(),
  allowedSecondaryWeapons = ac.StructItem.uint32(),
  defaultMainWeapon = ac.StructItem.byte(),
  defaultLethal = ac.StructItem.byte(),
  defaultSecondaryWeapon = ac.StructItem.byte(),
}, function(sender, message)
  if sender ~= nil then return end
  local selection = hud.loadout
  selection.catalogReceived = true
  selection.allowedMainWeapons = message.allowedMainWeapons
  selection.allowedLethals = message.allowedLethals
  selection.allowedSecondaryWeapons = message.allowedSecondaryWeapons
  selection.mainWeapon = hud.itemAllowed(message.allowedMainWeapons,
      hud.loadoutStorage.mainWeapon) and hud.loadoutStorage.mainWeapon
    or message.defaultMainWeapon
  selection.lethal = hud.itemAllowed(message.allowedLethals,
      hud.loadoutStorage.lethal) and hud.loadoutStorage.lethal
    or message.defaultLethal
  selection.secondaryWeapon = hud.itemAllowed(message.allowedSecondaryWeapons,
      hud.loadoutStorage.secondaryWeapon) and hud.loadoutStorage.secondaryWeapon
    or message.defaultSecondaryWeapon
  selection.result = 'CONFIRM A LOADOUT TO JOIN'
end)

hud.loadoutResultEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsLoadoutResult'),
  result = ac.StructItem.byte(),
  mainWeapon = ac.StructItem.byte(),
  lethal = ac.StructItem.byte(),
  secondaryWeapon = ac.StructItem.byte(),
}, function(sender, message)
  if sender ~= nil then return end
  if message.result == 1 then
    hud.loadout.confirmed = true
    hud.loadout.result = 'LOADOUT APPLIED'
    hud.loadoutStorage.mainWeapon = message.mainWeapon
    hud.loadoutStorage.lethal = message.lethal
    hud.loadoutStorage.secondaryWeapon = message.secondaryWeapon
  elseif message.result == 2 then
    hud.loadout.result = 'QUEUED FOR NEXT RESPAWN'
    hud.loadoutStorage.mainWeapon = message.mainWeapon
    hud.loadoutStorage.lethal = message.lethal
    hud.loadoutStorage.secondaryWeapon = message.secondaryWeapon
  else
    hud.loadout.result = message.result == 3 and 'SELECTION REJECTED BY SERVER'
      or 'LOADOUT UNAVAILABLE'
  end
end)

hud.loadoutStateEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsLoadoutState'),
  actorID = ac.StructItem.byte(),
  mainWeapon = ac.StructItem.byte(),
  lethal = ac.StructItem.byte(),
  secondaryWeapon = ac.StructItem.byte(),
  activeSlot = ac.StructItem.byte(),
  lethalsRemaining = ac.StructItem.byte(),
}, function(sender, message)
  if sender ~= nil then return end
  local actor = actors[message.actorID]
  if actor == nil then
    actor = {
      id = message.actorID, target = vec3(), render = vec3(), yaw = 0, targetYaw = 0,
      collisionNormal = vec2(), pitch = 0, health = 0, stamina = 100, kills = 0,
      deaths = 0, score = 0, flags = 0, actionState = 0, ammo = 0,
      reserveMagazines = 0, reloadRemaining = 0, spawnCount = nil,
    }
    actors[message.actorID] = actor
  end
  actor.mainWeapon = message.mainWeapon
  actor.lethal = message.lethal
  actor.secondaryWeapon = message.secondaryWeapon
  actor.activeSlot = message.activeSlot
  actor.lethalsRemaining = message.lethalsRemaining
  if message.actorID == localSessionID then
    hud.loadout.mainWeapon = message.mainWeapon
    hud.loadout.lethal = message.lethal
    hud.loadout.secondaryWeapon = message.secondaryWeapon
    hud.loadout.activeSlot = message.activeSlot
    hud.loadout.lethalsRemaining = message.lethalsRemaining
  end
end)

hud.readyEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsReady'),
  protocol = ac.StructItem.uint16(),
}, function() end)

hud.environmentRequestEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsEnvironmentRequest'),
  weatherType = ac.StructItem.byte(),
  timeOfDaySeconds = ac.StructItem.uint32(),
}, function() end)

hud.clientDiagnosticEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsClientDiagnostic'),
  pipeline = ac.StructItem.byte(),
  flags = ac.StructItem.uint16(),
  attempts = ac.StructItem.uint32(),
  completions = ac.StructItem.uint32(),
  frameBeginCalls = ac.StructItem.uint32(),
  draw3DCalls = ac.StructItem.uint32(),
  drawUICalls = ac.StructItem.uint32(),
  directDrawAttempts = ac.StructItem.uint32(),
  directDrawCompletions = ac.StructItem.uint32(),
  directDrawPending = ac.StructItem.uint32(),
  directDrawFailures = ac.StructItem.uint32(),
  position = ac.StructItem.vec3(),
  remoteActorID = ac.StructItem.byte(),
  remoteTarget = ac.StructItem.vec3(),
  remoteRender = ac.StructItem.vec3(),
  remoteTargetYaw = ac.StructItem.float(),
  remoteRenderYaw = ac.StructItem.float(),
  stage = ac.StructItem.string(48),
}, function() end)

hud.snapshotEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsSnapshot'),
  sequence = ac.StructItem.uint32(),
  count = ac.StructItem.byte(),
  actorIDs = ac.StructItem.array(ac.StructItem.byte(), capacity),
  flags = ac.StructItem.array(ac.StructItem.byte(), capacity),
  actionStates = ac.StructItem.uint32(),
  spawnCounts = ac.StructItem.array(ac.StructItem.uint32(), capacity),
  positions = ac.StructItem.array(ac.StructItem.vec3(), capacity),
  groundYs = ac.StructItem.array(ac.StructItem.float(), capacity),
  collisionDirections = ac.StructItem.array(ac.StructItem.byte(), capacity),
  yaws = ac.StructItem.array(ac.StructItem.float(), capacity),
  pitches = ac.StructItem.array(ac.StructItem.float(), capacity),
  vitals = ac.StructItem.array(ac.StructItem.uint16(), capacity),
  kills = ac.StructItem.array(ac.StructItem.uint16(), capacity),
  deaths = ac.StructItem.array(ac.StructItem.uint16(), capacity),
  ammo = ac.StructItem.array(ac.StructItem.byte(), capacity),
  reserveMagazines = ac.StructItem.array(ac.StructItem.byte(), capacity),
  reloadRemaining = ac.StructItem.array(ac.StructItem.float(), capacity),
}, function(sender, message)
  if sender ~= nil then return end
  if not firstSnapshotLogged then
    firstSnapshotLogged = true
    ac.log(string.format('[ASRC FPS] first snapshot: sequence=%s count=%s localSession=%s',
      tostring(message.sequence), tostring(message.count), tostring(localSessionID)))
  end
  for i = 0, message.count - 1 do
    local id = message.actorIDs[i]
    local actor = actors[id]
    if actor == nil then
      actor = {
        id = id, target = vec3(), render = vec3(), yaw = 0, targetYaw = 0,
        collisionNormal = vec2(),
        pitch = 0, health = 0, stamina = 100, kills = 0, deaths = 0, score = 0, flags = 0,
        actionState = 0, ammo = 0, reserveMagazines = 0, reloadRemaining = 0,
        spawnCount = nil,
      }
      actors[id] = actor
    end
    local previousFlags = actor.flags
    local previousSpawnCount = actor.spawnCount
    actor.target:set(message.positions[i])
    actor.groundY = message.groundYs[i]
    local collisionDirection = message.collisionDirections[i]
    if collisionDirection == 255 then
      actor.collisionNormal:set(0, 0)
    else
      local collisionAngle = collisionDirection / 254 * math.pi * 2 - math.pi
      actor.collisionNormal:set(math.cos(collisionAngle), math.sin(collisionAngle))
    end
    if actor.render:lengthSquared() < 0.001 then actor.render:set(actor.target) end
    actor.targetYaw = message.yaws[i]
    actor.pitch = message.pitches[i]
    actor.health = bit.band(message.vitals[i], 255)
    actor.stamina = bit.rshift(message.vitals[i], 8)
    actor.kills = message.kills[i]
    actor.deaths = message.deaths[i]
    actor.ammo = message.ammo[i]
    actor.reserveMagazines = message.reserveMagazines[i]
    actor.reloadRemaining = message.reloadRemaining[i]
    actor.flags = message.flags[i]
    actor.actionState = (bit.band(message.actionStates, bit.lshift(1, i)) ~= 0 and 1 or 0)
      + (bit.band(message.actionStates, bit.lshift(1, capacity + i)) ~= 0 and 2 or 0)
    actor.spawnCount = message.spawnCounts[i]
    local spawnChanged = previousSpawnCount ~= nil and previousSpawnCount ~= actor.spawnCount
    local wasDead = bit.band(previousFlags, 2) ~= 0
    local isDead = bit.band(actor.flags, 2) ~= 0
    if clearActorImpacts ~= nil and (spawnChanged or (not wasDead and isDead)) then
      clearActorImpacts(id)
      hud.radarReveal[id] = nil
      hud.radarVisible[id] = nil
    end
    if spawnChanged then
      fpsVisual.clearActorCorpse(actor)
      actor.render:set(actor.target)
      actor.yaw = actor.targetYaw
      actor.weaponKick = 0
      actor.animationClip = nil
      actor.animationPreviousClip = nil
      actor.animationLastPosition = nil
      actor.animationDeathStarted = nil
      actor.animationPhase = 0
      actor.animationWasGrounded = nil
      actor.animationJumpStarted = nil
      actor.animationLanded = nil
      actor.animationActionState = nil
      actor.animationTraversalStarted = nil
      hitMarkerUntil = effectClock
      ac.log(string.format(
        '[ASRC FPS] remote actor respawn reconciled: actor=%s spawn=%s position=%s',
        tostring(id), tostring(actor.spawnCount), vec3Text(actor.target)))
    end
    if not wasDead and isDead then
      fpsVisual.beginActorCorpse(actor)
    elseif wasDead and not isDead then
      fpsVisual.clearActorCorpse(actor)
    end
    if id == localSessionID then
      if not localActorSnapshotLogged then
        localActorSnapshotLogged = true
        lastSnapshotDiagnosticSequence = message.sequence
        lastSnapshotDiagnosticPosition = actor.target:clone()
        ac.log(string.format(
          '[ASRC FPS] local actor snapshot acquired: actor=%s position=%s yaw=%.3f flags=%s health=%s',
          tostring(id), vec3Text(actor.target), actor.targetYaw, tostring(actor.flags),
          tostring(actor.health)))
      else
        local sequenceDelta = message.sequence - lastSnapshotDiagnosticSequence
        if sequenceDelta < 0 then sequenceDelta = sequenceDelta + 4294967296 end
        if sequenceDelta >= 20 then
          local positionDelta = actor.target - lastSnapshotDiagnosticPosition
          ac.log(string.format(
            '[ASRC FPS] snapshot heartbeat: sequence=%s actor=%s target=%s delta=%s distance=%.3f flags=%s',
            tostring(message.sequence), tostring(id), vec3Text(actor.target),
            vec3Text(positionDelta), positionDelta:length(), tostring(actor.flags)))
          lastSnapshotDiagnosticSequence = message.sequence
          lastSnapshotDiagnosticPosition:set(actor.target)
        end
      end
      predictedGroundY = actor.groundY
      fpsVisual.stamina.value = actor.stamina
      if fpsVisual.stamina.value <= 0 then
        fpsVisual.stamina.exhausted = true
      elseif fpsVisual.stamina.value >= fpsVisual.stamina.exhaustionRelease then
        fpsVisual.stamina.exhausted = false
      end
      local geometryBlocked = bit.band(actor.flags, 64) ~= 0
      if geometryBlocked then
        if not predictionCollisionConstrained then
          ac.log(string.format(
            '[ASRC FPS] prediction collision constraint: actor=%s render=%s target=%s normal=(%.3f, %.3f) error=%.3f',
            tostring(id), vec3Text(actor.render), vec3Text(actor.target),
            actor.collisionNormal.x, actor.collisionNormal.y,
            (actor.target - actor.render):length()))
        end
        predictionCollisionConstrained = true
        predictionClearSnapshots = 0
        if (actor.target - actor.render):length() > 1.2 then
          actor.render:set(actor.target)
          predictedHorizontalVelocity = vec2()
          predictedVerticalVelocity = 0
          predictedAirborne = not (bit.band(actor.flags, 16) ~= 0)
          ac.log(string.format('[ASRC FPS] prediction hard correction: actor=%s target=%s',
            tostring(id), vec3Text(actor.target)))
        end
      elseif predictionCollisionConstrained then
        predictionClearSnapshots = predictionClearSnapshots + 1
        if predictionClearSnapshots >= 3 then
          predictionCollisionConstrained = false
          predictionClearSnapshots = 0
          ac.log(string.format('[ASRC FPS] prediction collision constraint cleared: actor=%s',
            tostring(id)))
        end
      end
      if bit.band(actor.flags, 128) ~= 0 then
        localStance = 2
      elseif bit.band(actor.flags, 32) ~= 0 then
        localStance = 1
      else
        localStance = 0
      end
      if not actor.localInitialized or spawnChanged or (wasDead and not isDead) then
        yaw = message.yaws[i]
        pitch = message.pitches[i]
        actor.render:set(actor.target)
        actor.localInitialized = true
        predictedGroundY = actor.target.y
        predictedVerticalVelocity = 0
        predictedHorizontalVelocity = vec2()
        predictedAirborne = false
        fpsVisual.stamina.value = actor.stamina
        fpsVisual.stamina.exhausted = false
        fpsVisual.stamina.recoveryDelay = 0
        predictionCollisionConstrained = geometryBlocked
        predictionClearSnapshots = 0
        jumpWasHeld = false
        crouchWasHeld = false
        weaponSwitchWasHeld = false
        crouchHeldSeconds = 0
        crouchLatched = false
        fpsVisual.crouchToggleReleaseStands = false
        fpsVisual.crouchSuppressedUntilRelease = false
        cameraHeight = 1.65
      end
    end
  end
end, nil, true)

hud.rosterEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsRoster'),
  actorID = ac.StructItem.byte(),
  role = ac.StructItem.byte(),
  name = ac.StructItem.string(32),
}, function(sender, message)
  if sender ~= nil then return end
  names[message.actorID] = message.name
  hud.radarReveal[message.actorID] = nil
  hud.radarVisible[message.actorID] = nil
  local actor = actors[message.actorID]
  if actor ~= nil then actor.role = message.role end
end)

hud.matchEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsMatch'),
  state = ac.StructItem.byte(),
  remainingSeconds = ac.StructItem.float(),
  killLimit = ac.StructItem.uint16(),
  maximumHealth = ac.StructItem.uint16(),
  winnerID = ac.StructItem.byte(),
  weatherType = ac.StructItem.byte(),
  timeOfDaySeconds = ac.StructItem.uint32(),
}, function(sender, message)
  if sender ~= nil then return end
  matchState = message.state
  remainingSeconds = message.remainingSeconds
  killLimit = message.killLimit
  hud.maximumHealth = math.max(1, message.maximumHealth)
  winnerID = message.winnerID
  hud.environmentWeather = message.weatherType
  hud.environmentTimeSeconds = message.timeOfDaySeconds
end)

clearActorImpacts = function(actorID)
  for index = #impacts, 1, -1 do
    if impacts[index].targetID == actorID then table.remove(impacts, index) end
  end
end

hud.killEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsKill'),
  killerID = ac.StructItem.byte(),
  victimID = ac.StructItem.byte(),
  killerKills = ac.StructItem.uint16(),
  victimDeaths = ac.StructItem.uint16(),
  itemID = ac.StructItem.byte(),
}, function(sender, message)
  if sender ~= nil then return end
  local killerName = message.killerID == 255 and 'SELF'
    or (names[message.killerID] or ('Player ' .. message.killerID))
  killFeed[#killFeed + 1] = {
    text = killerName .. '  [' .. (hud.itemNames[message.itemID] or 'DAMAGE') .. ']  '
      .. (names[message.victimID] or ('Player ' .. message.victimID)),
    ttl = 4,
  }
  if message.killerID == localSessionID then hitMarkerUntil = effectClock + 0.22 end
  clearActorImpacts(message.victimID)
end)

hud.hitEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsHit'),
  attackerID = ac.StructItem.byte(),
  victimID = ac.StructItem.byte(),
  remainingHealth = ac.StructItem.uint16(),
  itemID = ac.StructItem.byte(),
}, function(sender, message)
  if sender == nil and message.attackerID == localSessionID then
    hitMarkerUntil = effectClock + 0.16
  end
end)

hud.awardEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsAward'),
  actorID = ac.StructItem.byte(),
  victimID = ac.StructItem.byte(),
  points = ac.StructItem.uint16(),
  totalScore = ac.StructItem.uint32(),
  flags = ac.StructItem.byte(),
}, function(sender, message)
  if sender ~= nil then return end
  local actor = actors[message.actorID]
  if actor == nil then
    actor = {
      id = message.actorID, target = vec3(), render = vec3(), yaw = 0, targetYaw = 0,
      collisionNormal = vec2(), pitch = 0, health = 0, kills = 0, deaths = 0,
      score = 0, flags = 0, ammo = 0, reserveMagazines = 0, reloadRemaining = 0,
      actionState = 0, spawnCount = nil,
    }
    actors[message.actorID] = actor
  end
  actor.score = message.totalScore
  if message.actorID ~= localSessionID or message.points == 0 then return end
  local labels = { string.format('+%d', message.points) }
  if bit.band(message.flags, 1) ~= 0 then
    labels[#labels + 1] = 'KILL'
  elseif bit.band(message.flags, 2) ~= 0 then
    labels[#labels + 1] = 'ASSIST'
  end
  if bit.band(message.flags, 4) ~= 0 then labels[#labels + 1] = 'HEADSHOT' end
  if bit.band(message.flags, 8) ~= 0 then labels[#labels + 1] = 'ONE SHOT' end
  hud.awardPopups[#hud.awardPopups + 1] = {
    text = table.concat(labels, '  '), age = 0, ttl = 2.6,
  }
  while #hud.awardPopups > hud.awardPopupCapacity do table.remove(hud.awardPopups, 1) end
end)

fpsVisual.pickupEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsPickup'),
  pickupID = ac.StructItem.uint32(),
  state = ac.StructItem.byte(),
  weaponType = ac.StructItem.byte(),
  collectorID = ac.StructItem.byte(),
  position = ac.StructItem.vec3(),
}, function(sender, message)
  if sender ~= nil then return end
  local existing = fpsVisual.pickups[message.pickupID]
  if existing ~= nil and existing.root ~= nil then
    pcall(function() existing.root:dispose() end)
  end
  fpsVisual.pickups[message.pickupID] = nil
  if message.state == 1 then
    fpsVisual.pickups[message.pickupID] = {
      id = message.pickupID,
      weaponType = message.weaponType,
      position = message.position:clone(),
      bornAt = effectClock,
      root = nil,
      model = nil,
    }
  elseif message.collectorID == localSessionID then
    hud.awardPopups[#hud.awardPopups + 1] = {
      text = '+1 MAGAZINE', age = 0, ttl = 2.2,
    }
    while #hud.awardPopups > hud.awardPopupCapacity do table.remove(hud.awardPopups, 1) end
  end
end)

function fpsVisual.illuminateMuzzle(shooterID, position, now, localFirstPerson)
  if fpsVisual.muzzleLightUnavailable then return end
  local state = fpsVisual.muzzleLights[shooterID]
  if state == nil then
    local ok, lightOrError = pcall(function()
      local light = ac.LightSource(ac.LightType.Regular)
      light.range = 0
      light.spot = 0
      light.diffuseConcentration = 0.65
      light.specularMultiplier = 0.35
      light.rangeGradientOffset = 0
      light.fadeAt = 4.5
      light.fadeSmooth = 2
      light.volumetricLight = false
      light.skipLightMap = true
      light.affectsCars = false
      light.showInReflections = false
      light.shadows = false
      return light
    end)
    if not ok or lightOrError == nil then
      fpsVisual.muzzleLightUnavailable = true
      ac.log('[ASRC FPS] dynamic muzzle lighting unavailable: ' .. tostring(lightOrError))
      return
    end
    state = { light = lightOrError, expiresAt = 0, disposeAt = 0 }
    fpsVisual.muzzleLights[shooterID] = state
  end
  local ok, err = pcall(function()
    state.light.position:set(position)
    state.light.color = rgb(5.4, 2.35, 0.65)
    state.light.range = localFirstPerson and fpsVisual.muzzleLightLocalRange
      or fpsVisual.muzzleLightRemoteRange
    state.light.fadeAt = localFirstPerson and 4.5 or fpsVisual.muzzleLightRemoteFadeAt
    state.light.fadeSmooth = localFirstPerson and 2 or 25
    state.expiresAt = now + fpsVisual.muzzleLightLifetime
    state.disposeAt = now + fpsVisual.muzzleLightReuseSeconds
  end)
  if not ok then
    pcall(function() state.light:dispose() end)
    fpsVisual.muzzleLights[shooterID] = nil
    fpsVisual.muzzleLightUnavailable = true
    ac.log('[ASRC FPS] dynamic muzzle lighting failed: ' .. tostring(err))
  end
end

function fpsVisual.updateMuzzleLights(now)
  for shooterID, state in pairs(fpsVisual.muzzleLights) do
    if state.expiresAt > 0 and state.expiresAt <= now then
      state.light.range = 0
      state.expiresAt = 0
    end
    if state.disposeAt > 0 and state.disposeAt <= now then
      pcall(function() state.light:dispose() end)
      fpsVisual.muzzleLights[shooterID] = nil
    end
  end
end

hud.shotEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsShot'),
  shooterID = ac.StructItem.byte(),
  sequence = ac.StructItem.uint32(),
  origin = ac.StructItem.vec3(),
  direction = ac.StructItem.vec3(),
  distance = ac.StructItem.float(),
  impact = ac.StructItem.byte(),
  targetID = ac.StructItem.byte(),
  weaponType = ac.StructItem.byte(),
}, function(sender, message)
  if sender ~= nil then return end
  shotRender.eventsReceived = shotRender.eventsReceived + 1
  if message.shooterID ~= localSessionID then
    hud.radarReveal[message.shooterID] = effectClock + 2
  end
  local actor = actors[message.shooterID]
  local muzzleOrigin = message.origin:clone()
  if message.shooterID == localSessionID and not thirdPersonEnabled
      and localMuzzlePosition:lengthSquared() > 0.001 then
    muzzleOrigin:set(localMuzzlePosition)
    local stanceRecoilScale = fpsVisual.stanceRecoilMultiplier(localStance)
    viewmodelKick = stanceRecoilScale
    fpsVisual.viewmodelFireUntil = effectClock + 0.12
    local cameraRecoilScale = math.lerp(1, 0.45, fpsVisual.ads)
    pitch = math.min(1.45, pitch + 0.011 * cameraRecoilScale * stanceRecoilScale)
  elseif actor ~= nil then
    actor.animationFireUntil = effectClock + 0.12
    -- Anchor third-person flashes to the pose actually rendered on this client. Using the
    -- latest authoritative target made a remote light appear ahead of an interpolated model.
    local renderedBase = actor.render:lengthSquared() > 0.001 and actor.render or actor.target
    local renderedYaw = actor.yaw or actor.targetYaw
    local cosPitch = math.cos(actor.pitch or 0)
    local forward = vec3(math.sin(renderedYaw) * cosPitch, math.sin(actor.pitch or 0),
      math.cos(renderedYaw) * cosPitch)
    local right = vec3(math.cos(renderedYaw), 0, -math.sin(renderedYaw))
    local stance = bit.band(actor.actionState or 0, 2) ~= 0 and 2
      or bit.band(actor.actionState or 0, 1) ~= 0 and 1 or 0
    local muzzleHeight = stance == 2 and 0.48 or stance == 1 and 0.86 or 1.14
    muzzleOrigin:set(renderedBase + vec3(0, muzzleHeight, 0)
      + forward * 0.72 + right * 0.20)
  end
  local distance = math.clamp(message.distance, 0.05, 120)
  local targetPoint = message.origin + message.direction * distance
  local tracerDistance = (targetPoint - muzzleOrigin):length()
  local travelTime = math.clamp(tracerDistance / 260, 0.035, 0.08)
  local now = ui.time()
  local localFirstPersonShot = message.shooterID == localSessionID and not thirdPersonEnabled
  fpsVisual.illuminateMuzzle(message.shooterID, muzzleOrigin, now, localFirstPersonShot)
  while #tracers >= maxTracers do table.remove(tracers, 1) end
  tracers[#tracers + 1] = {
    -- Damage remains authoritative along the camera/crosshair ray. The tracer is
    -- cosmetic and converges from the rendered muzzle to that authoritative endpoint.
    from = muzzleOrigin:clone(),
    flashFrom = muzzleOrigin:clone(),
    to = targetPoint,
    bornAt = now,
    travelTime = travelTime,
    expiresAt = now + travelTime + 0.02,
    flashUntil = now + 0.045,
    localShot = message.shooterID == localSessionID,
  }
  if shotRender.eventsReceived == 1 then
    ac.log(string.format(
      '[ASRC FPS] first shot event received: shooter=%s sequence=%s impact=%s distance=%.2f muzzle=%s target=%s',
      tostring(message.shooterID), tostring(message.sequence), tostring(message.impact), distance,
      vec3Text(muzzleOrigin), vec3Text(targetPoint)))
  end
  if message.impact ~= 0 then
    local point = targetPoint:clone()
    local normal = -message.direction
    if message.impact == 1 then
      local trackPoint = vec3()
      local trackNormal = vec3()
      local trackHit = physics.raycastTrack(message.origin, message.direction,
        distance + 0.12, trackPoint, trackNormal, false, false)
      if trackHit >= 0 then
        point:set(trackPoint)
        normal:set(trackNormal)
      end
    end
    if normal:lengthSquared() < 0.01 then normal:set(-message.direction) else normal:normalize() end
    while #impacts >= maxImpactMarks do table.remove(impacts, 1) end
    local targetActor = message.impact == 2 and actors[message.targetID] or nil
    impacts[#impacts + 1] = {
      position = point + normal * 0.008,
      normal = normal:clone(),
      expiresAt = now + (message.impact == 1 and 30 or 0.28),
      world = message.impact == 1,
      targetID = targetActor ~= nil and message.targetID or nil,
      targetSpawnCount = targetActor ~= nil and targetActor.spawnCount or nil,
      targetOffset = targetActor ~= nil and (point - targetActor.target) or nil,
    }
    if message.impact == 1 then
      if impactSparks ~= nil then impactSparks:emit(point + normal * 0.02, normal * 2.4, 7) end
      if impactSmoke ~= nil then impactSmoke:emit(point + normal * 0.025, normal * 0.22, 2) end
      local tangent = vec3(normal.z, 0, -normal.x)
      if tangent:lengthSquared() < 0.01 then tangent:set(1, 0, 0) else tangent:normalize() end
      for index = 1, 7 do
        local spread = (index - 4) * 0.22
        sparks[#sparks + 1] = {
          position = point + normal * 0.025,
          velocity = normal * (1.1 + index * 0.12) + tangent * spread
            + vec3(0, 0.35 + (index % 3) * 0.18, 0),
          ttl = 0.24 + (index % 3) * 0.05,
        }
      end
    end
  end
  if actor ~= nil then actor.weaponKick = 1 end
  if playRifleSound ~= nil then
    playRifleSound(message.origin, message.shooterID == localSessionID)
  end
end, nil, true)

fpsVisual.grenades = {}
fpsVisual.grenadeSnapshotEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsGrenadeSnapshot'),
  sequence = ac.StructItem.uint32(),
  count = ac.StructItem.byte(),
  grenadeIDs = ac.StructItem.array(ac.StructItem.uint32(), 8),
  ownerIDs = ac.StructItem.array(ac.StructItem.byte(), 8),
  types = ac.StructItem.array(ac.StructItem.byte(), 8),
  flags = ac.StructItem.array(ac.StructItem.byte(), 8),
  positions = ac.StructItem.array(ac.StructItem.vec3(), 8),
  velocities = ac.StructItem.array(ac.StructItem.vec3(), 8),
  remaining = ac.StructItem.array(ac.StructItem.float(), 8),
}, function(sender, message)
  if sender ~= nil then return end
  for i = 0, message.count - 1 do
    local id = message.grenadeIDs[i]
    local grenade = fpsVisual.grenades[id] or { id = id }
    grenade.ownerID = message.ownerIDs[i]
    grenade.type = message.types[i]
    grenade.flags = message.flags[i]
    grenade.position = message.positions[i]:clone()
    grenade.velocity = message.velocities[i]:clone()
    grenade.remaining = message.remaining[i]
    grenade.seenAt = effectClock
    fpsVisual.grenades[id] = grenade
  end
  for id, grenade in pairs(fpsVisual.grenades) do
    if grenade.seenAt ~= effectClock and effectClock - (grenade.seenAt or 0) > 0.15 then
      fpsVisual.grenades[id] = nil
    end
  end
end, nil, true)

fpsVisual.grenadeExplodedEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsGrenadeExploded'),
  grenadeID = ac.StructItem.uint32(),
  ownerID = ac.StructItem.byte(),
  type = ac.StructItem.byte(),
  position = ac.StructItem.vec3(),
}, function(sender, message)
  if sender ~= nil then return end
  fpsVisual.grenades[message.grenadeID] = nil
  for i = 1, 18 do
    local angle = i / 18 * math.pi * 2
    sparks[#sparks + 1] = {
      position = message.position:clone(),
      velocity = vec3(math.cos(angle) * 4, 1.2 + (i % 4), math.sin(angle) * 4),
      ttl = 0.45,
    }
  end
  if playRifleSound ~= nil then playRifleSound(message.position, false) end
end)

local function uvRegion(column, row, x0, y0, x1, y1)
  local cell = 0.25
  local inset = 0.002
  local baseU, baseV = (column - 1) * cell, (row - 1) * cell
  return {
    baseU + x0 * cell + inset,
    baseV + y0 * cell + inset,
    baseU + x1 * cell - inset,
    baseV + y1 * cell - inset,
  }
end

local function uvCell(column, row)
  return uvRegion(column, row, 0, 0, 1, 1)
end

local function boxUV(front, back, side, top, bottom)
  return {
    front = front,
    back = back,
    left = side,
    right = side,
    top = top,
    bottom = bottom or side,
  }
end

local operatorUV = {
  head = {
    front = uvCell(1, 1),
    back = uvCell(2, 1),
    left = uvRegion(3, 1, 0, 0, 0.5, 1),
    right = uvRegion(3, 1, 0.5, 0, 1, 1),
    top = uvCell(4, 1),
    bottom = uvCell(3, 4),
  },
  torso = boxUV(uvCell(1, 2), uvCell(2, 2), uvCell(3, 2),
    uvCell(4, 4), uvCell(4, 4)),
  sleeve = boxUV(uvCell(3, 2), uvCell(3, 2), uvCell(3, 2),
    uvCell(4, 4), uvCell(4, 4)),
  pants = boxUV(uvCell(1, 3), uvCell(2, 3), uvCell(3, 3),
    uvCell(3, 3), uvCell(4, 3)),
  boot = boxUV(uvCell(4, 3), uvCell(4, 3), uvCell(4, 3),
    uvCell(4, 3), uvCell(4, 3)),
}

local function appendBox(vertices, indices, center, size, uvSet)
  local h = size / 2
  local x0, x1 = center.x - h.x, center.x + h.x
  local y0, y1 = center.y - h.y, center.y + h.y
  local z0, z1 = center.z - h.z, center.z + h.z
  local function face(a, b, c, d, normal, faceName)
    local uv = uvSet ~= nil and (uvSet[faceName] or uvSet.default) or nil
    local u0, v0, u1, v1 = 0, 0, 1, 1
    if uv ~= nil then u0, v0, u1, v1 = uv[1], uv[2], uv[3], uv[4] end
    local base = #vertices
    vertices[#vertices + 1] = ac.MeshVertex(a, normal, vec2(u0, v1))
    vertices[#vertices + 1] = ac.MeshVertex(b, normal, vec2(u1, v1))
    vertices[#vertices + 1] = ac.MeshVertex(c, normal, vec2(u1, v0))
    vertices[#vertices + 1] = ac.MeshVertex(d, normal, vec2(u0, v0))
    indices[#indices + 1] = base
    indices[#indices + 1] = base + 1
    indices[#indices + 1] = base + 2
    indices[#indices + 1] = base
    indices[#indices + 1] = base + 2
    indices[#indices + 1] = base + 3
  end
  face(vec3(x0, y0, z1), vec3(x1, y0, z1), vec3(x1, y1, z1), vec3(x0, y1, z1), vec3(0, 0, 1), 'front')
  face(vec3(x1, y0, z0), vec3(x0, y0, z0), vec3(x0, y1, z0), vec3(x1, y1, z0), vec3(0, 0, -1), 'back')
  face(vec3(x1, y0, z1), vec3(x1, y0, z0), vec3(x1, y1, z0), vec3(x1, y1, z1), vec3(1, 0, 0), 'right')
  face(vec3(x0, y0, z0), vec3(x0, y0, z1), vec3(x0, y1, z1), vec3(x0, y1, z0), vec3(-1, 0, 0), 'left')
  face(vec3(x0, y1, z1), vec3(x1, y1, z1), vec3(x1, y1, z0), vec3(x0, y1, z0), vec3(0, 1, 0), 'top')
  face(vec3(x0, y0, z0), vec3(x1, y0, z0), vec3(x1, y0, z1), vec3(x0, y0, z1), vec3(0, -1, 0), 'bottom')
end

local function createBoxGroup(parent, name, boxes, color, texturePath)
  local vertices, indices = {}, {}
  for _, box in ipairs(boxes) do appendBox(vertices, indices, box[1], box[2], box[3]) end
  local mesh = parent:createMesh(name, name .. '_MAT', ac.VertexBuffer(vertices),
    ac.IndicesBuffer(indices), false, false)
  if mesh == nil then return nil end
  mesh:applyShaderReplacements(string.format([[
    SHADER = ksPerPixel
    CAST_SHADOWS = 0
    CULL_MODE = NONE
    RESOURCE_0 = txDiffuse, 'color::#%s'
    PROP_0 = ksDiffuse, 0.72
    PROP_1 = ksAmbient, 0.38
    PROP_2 = ksSpecular, 0.22
    PROP_3 = ksSpecularEXP, 35
  ]], color))
  if texturePath ~= nil then
    pcall(function() mesh:setMaterialTexture('txDiffuse', texturePath) end)
  end
  mesh:setShadows(false)
  return mesh
end

local function createOperatorBody(parent, prefix)
  return createBoxGroup(parent, prefix .. '_SKINNED_BODY', {
    {vec3(0, 1.18, 0), vec3(0.48, 0.62, 0.28), operatorUV.torso},
    {vec3(0, 0.78, 0), vec3(0.40, 0.24, 0.25), operatorUV.pants},
    {vec3(-0.14, 0.43, 0), vec3(0.18, 0.54, 0.20), operatorUV.pants},
    {vec3(0.14, 0.43, 0), vec3(0.18, 0.54, 0.20), operatorUV.pants},
    {vec3(-0.14, 0.11, 0), vec3(0.19, 0.22, 0.22), operatorUV.boot},
    {vec3(0.14, 0.11, 0), vec3(0.19, 0.22, 0.22), operatorUV.boot},
    {vec3(-0.34, 1.13, 0.02), vec3(0.16, 0.68, 0.18), operatorUV.sleeve},
    {vec3(0.34, 1.13, 0.02), vec3(0.16, 0.68, 0.18), operatorUV.sleeve},
    {vec3(0, 1.65, 0), vec3(0.28, 0.32, 0.27), operatorUV.head},
  }, 'FFFFFF', operatorSkinPath)
end

local function directEffectRenderParams(mesh, cacheKey, color)
  return {
    mesh = mesh,
    async = true,
    cacheKey = cacheKey,
    textures = {},
    values = { gBaseColor = color },
    shader = [[
      float4 main(PS_IN pin) {
        return pin.ApplyFog(float4(gBaseColor.rgb * gWhiteRefPoint, 1));
      }
    ]],
  }
end

local function ensureShotEffectTemplates()
  if tracerRenderParams ~= nil then return tracerRenderParams ~= false end
  local ok, result = pcall(function()
    shotEffectTemplateHolder = carsRoot:createNode('ASRC_FPS_SHOT_EFFECT_HOLDER', false)
    if shotEffectTemplateHolder == nil then error('shot-effect holder could not be created') end

    local tracerRoot = shotEffectTemplateHolder:createNode('ASRC_FPS_TRACER', false)
    local impactRoot = shotEffectTemplateHolder:createNode('ASRC_FPS_IMPACT', false)
    local sparkRoot = shotEffectTemplateHolder:createNode('ASRC_FPS_SPARK', false)
    local muzzleNearRoot = shotEffectTemplateHolder:createNode('ASRC_FPS_MUZZLE_NEAR', false)
    local muzzleMidRoot = shotEffectTemplateHolder:createNode('ASRC_FPS_MUZZLE_MID', false)
    local muzzleFarRoot = shotEffectTemplateHolder:createNode('ASRC_FPS_MUZZLE_FAR', false)
    if tracerRoot == nil or impactRoot == nil or sparkRoot == nil
        or muzzleNearRoot == nil or muzzleMidRoot == nil or muzzleFarRoot == nil then
      error('one or more shot-effect roots could not be created')
    end
    createBoxGroup(tracerRoot, 'ASRC_FPS_TRACER_MESH', {
      {vec3(0, 0, 0.07), vec3(0.012, 0.012, 0.14)},
    }, 'FFD35A')
    createBoxGroup(impactRoot, 'ASRC_FPS_IMPACT_MESH', {
      {vec3(0, 0, 0.003), vec3(0.12, 0.12, 0.006)},
    }, '17130F')
    createBoxGroup(sparkRoot, 'ASRC_FPS_SPARK_MESH', {
      {vec3(0, 0, 0.045), vec3(0.018, 0.018, 0.09)},
    }, 'FF9B2F')
    -- Remote muzzle flares use distance LODs so the cosmetic flash retains a few pixels
    -- of angular size across the full arena. Illumination radius remains independently small.
    createBoxGroup(muzzleNearRoot, 'ASRC_FPS_MUZZLE_NEAR_MESH', {
      {vec3(0, 0, 0.055), vec3(0.06, 0.06, 0.11)},
    }, 'FFD24A')
    createBoxGroup(muzzleMidRoot, 'ASRC_FPS_MUZZLE_MID_MESH', {
      {vec3(0, 0, 0.08), vec3(0.18, 0.18, 0.16)},
    }, 'FFD24A')
    createBoxGroup(muzzleFarRoot, 'ASRC_FPS_MUZZLE_FAR_MESH', {
      {vec3(0, 0, 0.12), vec3(0.45, 0.45, 0.24)},
    }, 'FFD24A')
    tracerRoot:setShadows(false)
    impactRoot:setShadows(false)
    sparkRoot:setShadows(false)
    muzzleNearRoot:setShadows(false)
    muzzleMidRoot:setShadows(false)
    muzzleFarRoot:setShadows(false)
    shotEffectTemplateHolder:setVisible(false)
    return {
      directEffectRenderParams(tracerRoot, 0x41535251, rgbm(1, 0.76, 0.18, 1)),
      directEffectRenderParams(impactRoot, 0x41535252, rgbm(0.045, 0.032, 0.022, 1)),
      directEffectRenderParams(sparkRoot, 0x41535253, rgbm(1, 0.42, 0.08, 1)),
      directEffectRenderParams(muzzleNearRoot, 0x41535254, rgbm(1, 0.64, 0.12, 1)),
      directEffectRenderParams(muzzleMidRoot, 0x41535255, rgbm(1, 0.64, 0.12, 1)),
      directEffectRenderParams(muzzleFarRoot, 0x41535256, rgbm(1, 0.64, 0.12, 1)),
    }
  end)
  if not ok then
    tracerRenderParams = false
    impactRenderParams = false
    sparkRenderParams = false
    muzzleFlashNearRenderParams = false
    muzzleFlashMidRenderParams = false
    muzzleFlashFarRenderParams = false
    ac.warn('[ASRC FPS] direct shot-effect template failed: ' .. tostring(result))
    return false
  end
  tracerRenderParams = result[1]
  impactRenderParams = result[2]
  sparkRenderParams = result[3]
  muzzleFlashNearRenderParams = result[4]
  muzzleFlashMidRenderParams = result[5]
  muzzleFlashFarRenderParams = result[6]
  ac.log('[ASRC FPS] direct shot-effect templates ready')
  return true
end

local function muzzleFlashRenderParams(tracer)
  if tracer.localShot or camera == nil then return sparkRenderParams end
  local distance = (tracer.flashFrom - camera.transform.position):length()
  if distance >= 60 then return muzzleFlashFarRenderParams end
  if distance >= 20 then return muzzleFlashMidRenderParams end
  return muzzleFlashNearRenderParams
end

createRifleModel = function(parent, prefix, includeArms)
  local root = parent:createNode(prefix .. '_ROOT', false)
  if root == nil then return nil end
  createBoxGroup(root, prefix .. '_RIFLE', {
    {vec3(0, 0.00, 0.09), vec3(0.15, 0.16, 0.24)},
    {vec3(0, 0.00, 0.34), vec3(0.13, 0.15, 0.34)},
    {vec3(0, 0.015, 0.60), vec3(0.115, 0.12, 0.30)},
    {vec3(0, 0.02, 0.83), vec3(0.045, 0.045, 0.24)},
    {vec3(0, 0.02, 0.975), vec3(0.07, 0.07, 0.07)},
    {vec3(0, 0.105, 0.42), vec3(0.045, 0.05, 0.13)},
    {vec3(0, -0.13, 0.37), vec3(0.09, 0.20, 0.12)},
  }, '151B21')
  createBoxGroup(root, prefix .. '_DETAILS', {
    {vec3(0, 0.075, 0.60), vec3(0.13, 0.025, 0.22)},
    {vec3(0, -0.055, 0.17), vec3(0.17, 0.045, 0.08)},
  }, '39434A')
  if includeArms then
    createBoxGroup(root, prefix .. '_SLEEVES', {
      {vec3(0.18, -0.11, 0.19), vec3(0.13, 0.14, 0.48)},
      {vec3(-0.15, -0.08, 0.52), vec3(0.13, 0.13, 0.38)},
    }, '303941')
    createBoxGroup(root, prefix .. '_GLOVES', {
      {vec3(0.10, -0.055, 0.38), vec3(0.13, 0.10, 0.14)},
      {vec3(-0.08, -0.025, 0.66), vec3(0.13, 0.10, 0.14)},
    }, '171A1D')
  end
  root:setShadows(false)
  return root
end

function fpsVisual.asset(fileName)
  return rifleAssetFolder ~= nil and fileName ~= nil
    and (rifleAssetFolder .. '/' .. fileName) or nil
end

function fpsVisual.fallback(reason)
  if not fpsVisual.modern then return end
  fpsVisual.modern = false
  fpsVisual.active = 'Blocks'
  fpsVisual.error = 'MODERN THEME FAILED - BLOCKS FALLBACK: ' .. tostring(reason)
  clientPackError = fpsVisual.error
  markViewmodelStage('modern-fallback', reason)
  if viewmodelHolder ~= nil then pcall(function() viewmodelHolder:dispose() end) end
  viewmodelHolder = nil
  viewmodelRoot = nil
  rifleAssetFolder = nil
  rifleAssetsLoading = false
  rifleAssetsFailed = false
  rifleAssetWaitLogged = false
  rifleAssetArchivePath = '/fps/assets/asrc-fps-assets-v20.zip'
  rifleViewmodelFileName = 'asrc_assault_rifle_viewmodel.kn5'
  rifleWorldModelFileName = 'asrc_assault_rifle_world.kn5'
  fpsVisual.pickupFileName = rifleWorldModelFileName
  rifleDiffuseFileName = 'asrc_rifle_diffuse.png'
  operatorSkinFileName = 'asrc_operator_skin.png'
  rifleViewmodelPath = nil
  rifleWorldModelPath = nil
  fpsVisual.loadedViewmodelAsset = nil
  fpsVisual.viewmodelPistolPoseSeedPending = false
  rifleDiffusePath = nil
  operatorSkinPath = nil
  fpsVisual.pickupPath = nil
  for _, actor in pairs(actors) do
    if actor.root ~= nil and actor.root ~= false then
      pcall(function() actor.root:dispose() end)
    end
    actor.root = nil
    actor.weaponRoot = nil
    actor.modernModel = nil
    actor.weaponMesh = nil
    actor.nativeScenePrepared = false
    actor.nativeSceneVisible = false
  end
  for _, pickup in pairs(fpsVisual.pickups) do
    if pickup.root ~= nil and pickup.root ~= false then
      pcall(function() pickup.root:dispose() end)
    end
    pickup.root = nil
    pickup.model = nil
  end
  localAvatarReady = false
  ac.warn('[ASRC FPS] ' .. fpsVisual.error)
end

local function getAssetArchiveUrl(archivePath)
  local serverIP = ac.getServerIP()
  local serverHttpPort = ac.getServerPortHTTP()
  if serverIP == nil or serverIP == '' or serverHttpPort == nil or serverHttpPort < 0 then return nil end
  if string.find(serverIP, ':', 1, true) ~= nil and string.sub(serverIP, 1, 1) ~= '[' then
    serverIP = '[' .. serverIP .. ']'
  end
  return string.format('http://%s:%d%s', serverIP, serverHttpPort, archivePath)
end

function fpsVisual.activeWeapon(actor)
  if actor == nil then
    return hud.loadout.activeSlot == 1 and hud.loadout.secondaryWeapon
      or hud.loadout.mainWeapon
  end
  return actor.activeSlot == 1 and (actor.secondaryWeapon or 4)
    or (actor.mainWeapon or 1)
end

function fpsVisual.weaponAssetKey(actor)
  local weapon = fpsVisual.activeWeapon(actor)
  return fpsVisual.isLoadoutAsset(weapon) and weapon or 1
end

function fpsVisual.isPistolAsset(assetKey)
  return assetKey == 3 or assetKey == 4
end

function fpsVisual.isLoadoutAsset(assetKey)
  return assetKey == 2 or fpsVisual.isPistolAsset(assetKey)
end

function fpsVisual.loadoutClips(assetKey)
  return assetKey == 2 and fpsVisual.compactSmgClips
    or fpsVisual.pistolClips(assetKey)
end

function fpsVisual.pistolClips(assetKey)
  return assetKey == 4 and fpsVisual.colt1911Clips or fpsVisual.desertEagleClips
end

function fpsVisual.pistolViewmodelFileName(assetKey)
  return assetKey == 4 and fpsVisual.colt1911ViewmodelFileName
    or fpsVisual.desertEagleViewmodelFileName
end

function fpsVisual.pistolWorldModelFileName(assetKey)
  return assetKey == 4 and fpsVisual.colt1911WorldModelFileName
    or fpsVisual.desertEagleWorldModelFileName
end

function fpsVisual.loadoutViewmodelFileName(assetKey)
  return assetKey == 2 and fpsVisual.compactSmgViewmodelFileName
    or fpsVisual.pistolViewmodelFileName(assetKey)
end

function fpsVisual.loadoutWorldModelFileName(assetKey)
  return assetKey == 2 and fpsVisual.compactSmgWorldModelFileName
    or fpsVisual.pistolWorldModelFileName(assetKey)
end

function fpsVisual.viewmodelPath(assetKey)
  if fpsVisual.isLoadoutAsset(assetKey) and fpsVisual.loadoutAssetFolder ~= nil then
    return fpsVisual.loadoutAssetFolder .. '/' .. fpsVisual.loadoutViewmodelFileName(assetKey)
  end
  return rifleViewmodelPath
end

function fpsVisual.worldModelPath(assetKey)
  if fpsVisual.isLoadoutAsset(assetKey) and fpsVisual.loadoutAssetFolder ~= nil then
    return fpsVisual.loadoutAssetFolder .. '/' .. fpsVisual.loadoutWorldModelFileName(assetKey)
  end
  return rifleWorldModelPath
end

function fpsVisual.actorWeaponPosition(actor)
  local kick = actor.weaponKick or 0
  if fpsVisual.isPistolAsset(actor.weaponAsset) then
    return vec3(0.22, 1.13, 0.39 - kick * 0.04)
  end
  if actor.weaponAsset == 2 then
    -- The rigid MP5, like both pistols, is exported with its grip at the root.
    -- Place that root at the animated hand rather than using the procedural
    -- rifle's receiver-relative attachment offset.
    return vec3(0.22, 1.13, 0.39 - kick * 0.07)
  end
  return vec3(0.22, 1.13, 0.08 - kick * 0.07)
end

function fpsVisual.requestLoadoutAssets()
  if fpsVisual.loadoutAssetFolder ~= nil or fpsVisual.loadoutAssetsLoading
      or fpsVisual.loadoutAssetsFailed then return end
  if not fpsVisual.modern then
    if rifleAssetFolder ~= nil then
      fpsVisual.loadoutAssetFolder = rifleAssetFolder
    else
      requestRifleAssets()
    end
    return
  end
  local archiveUrl = getAssetArchiveUrl(fpsVisual.loadoutAssetArchivePath)
  if archiveUrl == nil then return end
  fpsVisual.loadoutAssetsLoading = true
  web.loadRemoteAssets({
    url = archiveUrl,
    headers = {},
    crucial = fpsVisual.compactSmgViewmodelFileName,
  }, function(err, folder)
    fpsVisual.loadoutAssetsLoading = false
    if (err ~= nil and err ~= '') or folder == nil or folder == '' then
      fpsVisual.loadoutAssetsFailed = true
      clientPackError = 'FPS LOADOUT ASSET DOWNLOAD FAILED - CHECK SERVER HTTP PORT'
      ac.warn('[ASRC FPS] loadout asset download failed: ' .. tostring(err))
      return
    end
    fpsVisual.loadoutAssetFolder = folder
    local weapon = fpsVisual.hudWeapon
    if weapon.archivePath == fpsVisual.loadoutAssetArchivePath then
      weapon.imagePath = folder .. '/' .. weapon.fileName
      weapon.loading = false
      weapon.failed = false
    end
    ac.log('[ASRC FPS] loadout weapon assets cached: ' .. folder)
  end)
end

function hud.requestWeaponImage()
  local weapon = fpsVisual.hudWeapon
  if weapon.imagePath ~= nil or weapon.loading or weapon.failed then return end
  -- CSP extracts each remote-assets URL into one shared cache folder. Starting a
  -- second request for the same URL races its temporary-file finalization, so the
  -- HUD image must consume the loadout archive request instead of downloading it.
  if weapon.archivePath == fpsVisual.loadoutAssetArchivePath then
    if fpsVisual.loadoutAssetFolder ~= nil then
      weapon.imagePath = fpsVisual.loadoutAssetFolder .. '/' .. weapon.fileName
    elseif fpsVisual.loadoutAssetsFailed then
      weapon.failed = true
    else
      fpsVisual.requestLoadoutAssets()
    end
    return
  end
  local archiveUrl = getAssetArchiveUrl(weapon.archivePath)
  if archiveUrl == nil then return end
  weapon.loading = true
  web.loadRemoteAssets({
    url = archiveUrl,
    headers = {},
    crucial = weapon.fileName,
  }, function(err, folder)
    weapon.loading = false
    if (err ~= nil and err ~= '') or folder == nil or folder == '' then
      weapon.failed = true
      ac.warn('[ASRC FPS] HUD weapon image download failed: ' .. tostring(err))
      return
    end
    weapon.imagePath = folder .. '/' .. weapon.fileName
    ac.log('[ASRC FPS] HUD weapon image cached: ' .. weapon.imagePath)
  end)
end

requestRifleAssets = function()
  if rifleAssetFolder ~= nil or rifleAssetsLoading or rifleAssetsFailed then return end
  local archiveUrl = getAssetArchiveUrl(rifleAssetArchivePath)
  if archiveUrl == nil then
    if not rifleAssetWaitLogged then
      rifleAssetWaitLogged = true
      ac.log('[ASRC FPS] waiting for server HTTP endpoint before requesting rifle assets')
    end
    return
  end

  rifleAssetsLoading = true
  ac.log('[ASRC FPS] requesting rifle assets: ' .. archiveUrl)
  web.loadRemoteAssets({
    url = archiveUrl,
    headers = {},
    crucial = rifleViewmodelFileName,
  }, function(err, folder)
    rifleAssetsLoading = false
    if (err ~= nil and err ~= '') or folder == nil or folder == '' then
      if fpsVisual.modern then
        fpsVisual.fallback('asset download: ' .. tostring(err))
        return
      end
      rifleAssetsFailed = true
      clientPackError = 'FPS RIFLE ASSET DOWNLOAD FAILED - CHECK SERVER HTTP PORT'
      ac.warn('[ASRC FPS] remote rifle asset download failed: error=' .. tostring(err)
        .. '; folder=' .. tostring(folder) .. '; url=' .. archiveUrl)
      return
    end

    rifleAssetFolder = folder
    rifleViewmodelPath = folder .. '/' .. rifleViewmodelFileName
    rifleWorldModelPath = folder .. '/' .. rifleWorldModelFileName
    rifleDiffusePath = rifleDiffuseFileName ~= nil and (folder .. '/' .. rifleDiffuseFileName) or nil
    operatorSkinPath = operatorSkinFileName ~= nil and (folder .. '/' .. operatorSkinFileName) or nil
    fpsVisual.pickupPath = folder .. '/' .. fpsVisual.pickupFileName
    clientPackError = fpsVisual.error
    viewmodelRoot = nil
    if fpsVisual.modern then
      fpsVisual.requestLoadoutAssets()
    else
      fpsVisual.loadoutAssetFolder = folder
    end
    ac.log('[ASRC FPS] rifle assets cached: folder=' .. folder
      .. '; viewmodel=' .. rifleViewmodelPath .. '; world=' .. rifleWorldModelPath
      .. '; rifleTexture=' .. tostring(rifleDiffusePath)
      .. '; operatorSkin=' .. tostring(operatorSkinPath)
      .. '; theme=' .. fpsVisual.active)
  end)
end

local function ensureLocalViewmodel()
  local actor = actors[localSessionID]
  local assetKey = fpsVisual.weaponAssetKey(actor)
  if viewmodelRoot ~= nil and fpsVisual.loadedViewmodelAsset ~= assetKey then
    if viewmodelHolder ~= nil then pcall(function() viewmodelHolder:dispose() end) end
    viewmodelHolder = nil
    viewmodelRoot = nil
    viewmodelRenderPosition = nil
    viewmodelRenderLook = nil
    viewmodelRenderUp = nil
    viewmodelDirectDrawCompletions = 0
    viewmodelStagesSeen['native-scene:deferred'] = nil
    viewmodelStagesSeen['native-scene:ready'] = nil
    fpsVisual.viewmodelPistolPoseSeedPending = false
  end
  if viewmodelRoot ~= nil then return viewmodelRoot ~= false end
  if rifleAssetFolder == nil then
    markViewmodelStage('asset-wait', 'remote archive is not cached yet')
    requestRifleAssets()
    return false
  end
  if fpsVisual.isLoadoutAsset(assetKey) and fpsVisual.loadoutAssetFolder == nil then
    markViewmodelStage('loadout-asset-wait', fpsVisual.loadoutViewmodelFileName(assetKey))
    fpsVisual.requestLoadoutAssets()
    return false
  end
  local viewmodelPath = fpsVisual.viewmodelPath(assetKey)
  markViewmodelStage('load-requested', viewmodelPath)
  local ok, result = pcall(function()
    -- carsRoot expects dynamic children to be bounding-sphere nodes. Attaching an
    -- ordinary node here can crash CSP when the native renderer first traverses it.
    markViewmodelStage('holder-create:begin')
    viewmodelHolder = carsRoot:createBoundingSphereNode('ASRC_FPS_VIEWMODEL_HOLDER', 2)
    if viewmodelHolder == nil then error('viewmodel holder could not be created') end
    markViewmodelStage('holder-create:complete')
    markViewmodelStage('kn5-load:begin', viewmodelPath)
    local model = viewmodelHolder:loadKN5({
      filename = viewmodelPath,
      forceRenderableOn = true,
    })
    if model == nil then error('loadKN5 returned no model for ' .. viewmodelPath) end
    markViewmodelStage('kn5-load:complete')
    markViewmodelStage('model-configure:begin')
    model:setShadows(false)
    model:setVisible(true, false)
    model:setCullMode(render.CullMode.None)
    model:setDepthMode(render.DepthMode.Normal)
    model:setMotionStencil(1)
    if not fpsVisual.isLoadoutAsset(assetKey) and rifleDiffusePath ~= nil then
      pcall(function() model:setMaterialTexture('txDiffuse', rifleDiffusePath) end)
    end
    if fpsVisual.isPistolAsset(assetKey) then
      -- A newly loaded KN5 starts in its authored two-arm rest pose. CSP skips
      -- constant bone channels in the non-reload clips, but evaluates the
      -- support arm in reload because that channel moves. Seed reload frame 0
      -- for one hidden scene update before equip so every fresh pistol instance
      -- begins with the support shoulder below the view frustum.
      model:setAnimation(fpsVisual.loadoutAssetFolder .. '/'
        .. fpsVisual.pistolClips(assetKey).reload, 0, true)
      fpsVisual.viewmodelPistolPoseSeedPending = true
      fpsVisual.viewmodelEquipUntil = effectClock + 0.55
    elseif fpsVisual.isLoadoutAsset(assetKey) then
      fpsVisual.viewmodelPistolPoseSeedPending = false
      model:setAnimation(fpsVisual.loadoutAssetFolder .. '/'
        .. fpsVisual.loadoutClips(assetKey).equip, 0, true)
      fpsVisual.viewmodelEquipUntil = effectClock + 0.55
    elseif fpsVisual.modern then
      fpsVisual.viewmodelPistolPoseSeedPending = false
      model:setAnimation(fpsVisual.asset(fpsVisual.viewmodelClips.equip), 0, true)
      fpsVisual.viewmodelEquipUntil = effectClock + 0.55
    end
    markViewmodelStage('model-configure:complete')
    return model
  end)
  if not ok then
    if viewmodelHolder ~= nil then viewmodelHolder:dispose() end
    viewmodelHolder = nil
    fpsVisual.loadedViewmodelAsset = assetKey
    if fpsVisual.modern and not fpsVisual.isLoadoutAsset(assetKey) then
      viewmodelRoot = nil
      fpsVisual.fallback('viewmodel load: ' .. tostring(result))
      return false
    end
    viewmodelRoot = false
    clientPackError = fpsVisual.error
      or (fpsVisual.isLoadoutAsset(assetKey) and 'FPS LOADOUT VIEWMODEL ERROR - CHECK LIVE LOG'
        or 'FPS RIFLE MODEL ERROR - CACHED VIEWMODEL COULD NOT BE LOADED')
    ac.warn('[ASRC FPS] cached weapon viewmodel failed: ' .. tostring(result)
      .. '; cached path ' .. tostring(viewmodelPath) .. '; using 2D fallback')
    return false
  end
  viewmodelRoot = result
  fpsVisual.loadedViewmodelAsset = assetKey
  if not runViewmodelStage('holder-initial-hide', function()
    viewmodelHolder:setVisible(false)
  end) then
    if fpsVisual.modern then
      fpsVisual.fallback('viewmodel holder initialization')
      return false
    end
    clientPackError = 'FPS RIFLE MODEL ERROR - VIEWMODEL HOLDER COULD NOT BE HIDDEN'
    return false
  end
  markViewmodelStage('bounds-read:begin')
  local boundsOk, boundsMin, boundsMax, meshCount = pcall(function()
    return viewmodelRoot:getLocalAABB()
  end)
  markViewmodelStage(boundsOk and 'bounds-read:complete' or 'bounds-read:failed',
    boundsOk and ('meshes=' .. tostring(meshCount)) or boundsMin)
  ac.log('[ASRC FPS] cached weapon viewmodel loaded: ' .. tostring(viewmodelPath)
    .. '; bounds=' .. (boundsOk and (vec3Text(boundsMin) .. '..' .. vec3Text(boundsMax)
      .. '; meshes=' .. tostring(meshCount)) or ('unavailable: ' .. tostring(boundsMin))))
  markViewmodelStage('native-scene:configured', 'dynamic KN5 scene node')
  return true
end

local function drawFallbackRifle(size)
  if viewmodelDirectDrawCompletions > 0 or cursorUnlocked then return end
  local actor = actors[localSessionID]
  if actor == nil or bit.band(actor.flags, 1) == 0 or bit.band(actor.flags, 2) ~= 0 then return end
  local scale = math.max(0.75, math.min(1.2, size.y / 1080))
  local kick = viewmodelKick * 42 * scale
  local bob = math.sin(viewmodelBobTime) * 5 * scale
  local origin = vec2(size.x - 485 * scale + bob, size.y - 180 * scale + kick)
  local dark = rgbm(0.055, 0.07, 0.085, 1)
  local metal = rgbm(0.13, 0.16, 0.19, 1)
  local edge = rgbm(0.28, 0.32, 0.36, 1)
  local glove = rgbm(0.045, 0.05, 0.055, 1)
  ui.drawRectFilled(origin, origin + vec2(260, 62) * scale, metal, 6 * scale)
  ui.drawRect(origin, origin + vec2(260, 62) * scale, edge, 6 * scale, nil, 2 * scale)
  ui.drawRectFilled(origin + vec2(215, 16) * scale,
    origin + vec2(410, 36) * scale, dark, 4 * scale)
  ui.drawRectFilled(origin + vec2(405, 20) * scale,
    origin + vec2(455, 31) * scale, metal, 3 * scale)
  ui.drawRectFilled(origin + vec2(44, -25) * scale,
    origin + vec2(162, 0) * scale, dark, 4 * scale)
  ui.drawTriangleFilled(origin + vec2(110, 62) * scale,
    origin + vec2(178, 62) * scale, origin + vec2(158, 145) * scale, dark)
  ui.drawTriangleFilled(origin + vec2(0, 14) * scale,
    origin + vec2(-105, 70) * scale, origin + vec2(20, 61) * scale, dark)
  ui.drawRectFilled(origin + vec2(58, 52) * scale,
    origin + vec2(105, 98) * scale, glove, 12 * scale)
  ui.drawRectFilled(origin + vec2(245, 35) * scale,
    origin + vec2(292, 82) * scale, glove, 12 * scale)
end

playRifleSound = function(position, localShot)
  local ok, event = pcall(function()
    return ac.AudioEvent.fromFile({
      filename = rifleAudioPath,
      use3D = not localShot,
      useOcclusion = not localShot,
      loop = false,
      minDistance = 1,
      maxDistance = 180,
    }, true)
  end)
  if not ok or event == nil or not event:isValid() then
    if event ~= nil then event:dispose() end
    event = ac.AudioEvent('cars/:own/backfire_ext', false, false)
    if not rifleAudioFallbackLogged then
      rifleAudioFallbackLogged = true
      ac.log('[ASRC FPS] custom rifle audio unavailable; using carrier backfire fallback')
    end
  end
  if event == nil or not event:isValid() then return end
  event.volume = localShot and 0.9 or 0.72
  if not localShot then event:setPosition(position) end
  event:start()
  rifleSounds[#rifleSounds + 1] = {event = event, ttl = 0.6}
end

local function ensureAvatar(actor)
  if rifleAssetFolder == nil then
    requestRifleAssets()
    return
  end
  if actor.root == nil then
    local root = carsRoot:createBoundingSphereNode('ASRC_FPS_' .. actor.id, 1.5)
    if root == nil then
      actor.root = false
      if fpsVisual.modern then fpsVisual.fallback('operator root actor ' .. tostring(actor.id)) end
      return
    end
    if fpsVisual.modern then
      local loaded, model = pcall(function()
        local child = root:loadKN5({filename = rifleWorldModelPath, forceRenderableOn = true})
        if child == nil then error('loadKN5 returned no operator model') end
        child:setShadows(true)
        child:setVisible(true, false)
        child:setCullMode(render.CullMode.Back)
        child:setDepthMode(render.DepthMode.Normal)
        child:setMotionStencil(1)
        child:setAnimation(fpsVisual.asset(fpsVisual.operatorClips.aim_idle), 0, true)
        return child
      end)
      if not loaded or model == nil then
        root:dispose()
        actor.root = nil
        fpsVisual.fallback('operator load actor ' .. tostring(actor.id) .. ': ' .. tostring(model))
        return
      end
      actor.modernModel = model
      local weaponMeshOk, weaponMesh = pcall(function()
        return model:findSkinnedMeshes('ASRC_CARBINE_WORLD')
      end)
      actor.weaponMesh = weaponMeshOk and weaponMesh or nil
      actor.weaponRoot = false
      actor.avatarKind = 'modern-animated-operator'
    else
      createOperatorBody(root, 'ASRC_FPS_OPERATOR_' .. actor.id)
      actor.avatarKind = 'procedural-skinned-operator'
    end
    root:setVirtualCarFlag(true)
    root:setMotionStencil(1)
    root:setVisible(false, false)
    actor.root = root
    actor.weaponAsset = nil
    actor.nativeScenePrepared = false
  end
  if actor.root == false then return end
  local assetKey = fpsVisual.weaponAssetKey(actor)
  if actor.weaponAsset ~= assetKey then
    if actor.weaponRoot ~= nil and actor.weaponRoot ~= false then
      pcall(function() actor.weaponRoot:dispose() end)
    end
    actor.weaponRoot = nil
    actor.weaponAsset = assetKey
  end
  if fpsVisual.isLoadoutAsset(assetKey) and fpsVisual.loadoutAssetFolder == nil then
    fpsVisual.requestLoadoutAssets()
    return
  end
  if fpsVisual.modern and not fpsVisual.isLoadoutAsset(assetKey) then
    actor.weaponRoot = false
    return
  end
  if actor.weaponRoot ~= nil then return end
  local worldModelPath = fpsVisual.worldModelPath(assetKey)
  if worldModelPath == nil then return end

  local weaponOk, weapon = pcall(function()
    return actor.root:loadKN5({filename = worldModelPath, forceRenderableOn = true})
  end)
  actor.weaponRoot = weaponOk and weapon or nil
  if actor.weaponRoot == nil then
    if fpsVisual.isLoadoutAsset(assetKey) then
      actor.weaponRoot = false
      clientPackError = 'FPS LOADOUT WORLD MODEL ERROR - CHECK LIVE LOG'
      ac.warn('[ASRC FPS] cached loadout world model unavailable at '
        .. tostring(worldModelPath))
      return
    end
    actor.weaponRoot = createRifleModel(actor.root, 'ASRC_FPS_REMOTE_RIFLE_' .. actor.id, false)
    if not remoteRifleFallbackLogged then
      remoteRifleFallbackLogged = true
      ac.warn('[ASRC FPS] cached world rifle unavailable at ' .. tostring(rifleWorldModelPath)
        .. '; remote actors use procedural fallback')
    end
  end
  if actor.weaponRoot ~= nil then
    if not fpsVisual.isLoadoutAsset(assetKey) and rifleDiffusePath ~= nil then
      pcall(function() actor.weaponRoot:setMaterialTexture('txDiffuse', rifleDiffusePath) end)
    end
    actor.weaponRoot:setPosition(fpsVisual.actorWeaponPosition(actor))
  end
end

function fpsVisual.updatePickups()
  if rifleAssetFolder == nil then return end
  for _, pickup in pairs(fpsVisual.pickups) do
    local assetKey = fpsVisual.isLoadoutAsset(pickup.weaponType) and pickup.weaponType or 1
    if fpsVisual.isLoadoutAsset(assetKey) and fpsVisual.loadoutAssetFolder == nil then
      fpsVisual.requestLoadoutAssets()
    elseif pickup.root == nil then
      local loaded, rootOrError = pcall(function()
        local root = carsRoot:createBoundingSphereNode('ASRC_FPS_PICKUP_' .. pickup.id, 1.2)
        if root == nil then error('pickup holder could not be created') end
        local modelPath = fpsVisual.isLoadoutAsset(assetKey) and fpsVisual.worldModelPath(assetKey)
          or fpsVisual.pickupPath
        local model = root:loadKN5({filename = modelPath, forceRenderableOn = true})
        if model == nil then
          root:dispose()
          error('pickup KN5 could not be loaded')
        end
        model:setShadows(true)
        model:setCullMode(render.CullMode.Back)
        model:setDepthMode(render.DepthMode.Normal)
        model:setMotionStencil(1)
        root:setVirtualCarFlag(true)
        root:setMotionStencil(1)
        pickup.model = model
        return root
      end)
      if loaded then
        pickup.root = rootOrError
        pickup.root:clearMotion()
      else
        pickup.root = false
        ac.warn('[ASRC FPS] dropped-weapon pickup model failed: ' .. tostring(rootOrError))
      end
    end
    if pickup.root ~= nil and pickup.root ~= false then
      local age = math.max(0, effectClock - pickup.bornAt)
      local fall = fpsVisual.smoothstep01(math.clamp(age / 0.55, 0, 1))
      local yawAngle = (pickup.id * 2.399963) % (math.pi * 2)
      local baseForward = vec3(math.sin(yawAngle), 0, math.cos(yawAngle))
      local angle = math.rad(82) * fall
      local forward = baseForward * math.cos(angle) + vec3(0, 1, 0) * math.sin(angle)
      local up = vec3(0, 1, 0) * math.cos(angle) - baseForward * math.sin(angle)
      local height = 0.72 * (1 - fall) * (1 - fall) + 0.08
      pickup.root:setPosition(pickup.position + vec3(0, height, 0) + ac.getSim().originShift)
      pickup.root:setOrientation(forward, up)
      pickup.root:setVisible(gameplayActive, false)
    end
  end
end

local function inputAxis(negativeKey, positiveKey, alternateNegativeKey, alternatePositiveKey)
  local positive = ac.isKeyDown(positiveKey)
    or (alternatePositiveKey ~= nil and ac.isKeyDown(alternatePositiveKey))
  local negative = ac.isKeyDown(negativeKey)
    or (alternateNegativeKey ~= nil and ac.isKeyDown(alternateNegativeKey))
  return (positive and 1 or 0) - (negative and 1 or 0)
end

local function clampStick(value)
  return math.abs(value) < 0.12 and 0 or value
end

local function selectInput(primary, secondary, fallback)
  if math.abs(primary) > 0.001 then return primary end
  if math.abs(secondary) > 0.001 then return secondary end
  return fallback
end

local function fpsGameplayIsActive()
  local state = ac.getSim()
  return state.isSessionStarted and state.isLive and not state.isPaused
    and not state.isInMainMenu and not state.isLookingAtSessionResults
    and not state.isReplayActive
end

local function applyFpsClipPlane()
  if fpsClipPlaneApplied then return end
  local methods = {}
  local overrideAvailable = type(ac.overrideCameraClipPlanes) == 'function'
  if overrideAvailable then
    local ok, err = pcall(function() ac.overrideCameraClipPlanes(fpsNearClip, nil) end)
    if ok then
      table.insert(methods, 'global-override')
    else
      ac.warn('[ASRC FPS] global camera clip override failed: ' .. tostring(err))
    end
  end
  for cameraIndex = 0, car.carCamerasCount - 1 do
    local params = ac.accessCarCamera(cameraIndex)
    if params ~= nil then
      fpsOriginalCarCameraClipNear[cameraIndex] = params.clipNear == nil
        and false or params.clipNear
      params.clipNear = fpsNearClip
      table.insert(methods, 'car-' .. tostring(cameraIndex))
    end
  end
  fpsClipPlaneApplied = true
  fpsClipPlaneMethod = #methods > 0 and table.concat(methods, ',') or 'unavailable'
  ac.log(string.format('[ASRC FPS] camera near-clip request: requested=%.4f observed=%.4f method=%s',
    fpsNearClip, ac.getSim().cameraClipNear, fpsClipPlaneMethod))
end

local function restoreFpsClipPlane()
  if not fpsClipPlaneApplied then return end
  if type(ac.overrideCameraClipPlanes) == 'function' then
    pcall(function() ac.overrideCameraClipPlanes(nil, nil) end)
  end
  for cameraIndex, original in pairs(fpsOriginalCarCameraClipNear) do
    local params = ac.accessCarCamera(cameraIndex)
    if params ~= nil then params.clipNear = original == false and nil or original end
  end
  fpsOriginalCarCameraClipNear = {}
  fpsClipPlaneApplied = false
  fpsClipPlaneMethod = 'released'
end

local function releaseFpsCamera()
  if camera ~= nil and camera:active() then camera:dispose() end
  camera = nil
  cameraError = nil
  restoreFpsClipPlane()
  firstPersonCameraOffset:set(0, 0, 0)
  firstPersonCameraConstrained = false
  ac.log('[ASRC FPS] FPS camera released to AC menus')
end

local function acquireFpsCamera()
  if camera ~= nil and camera:active() then
    applyFpsClipPlane()
    return true
  end
  camera, cameraError = ac.grabCamera('AssettoServer FPS deathmatch')
  if camera == nil then return false end
  camera.ownShare = 1
  camera.cameraRestoreThreshold = 0.5
  applyFpsClipPlane()
  ac.log('[ASRC FPS] FPS camera acquired with full ownership')
  return true
end

function previewCamera.isEligible(actor)
  if actor == nil or previewCamera.everEnteredGameplay then return false end
  local state = ac.getSim()
  local awaitingDrive = state.isInMainMenu or not state.isSessionStarted
  return awaitingDrive and not state.isReplayActive and not state.isLookingAtSessionResults
    and bit.band(actor.flags, 1) ~= 0 and bit.band(actor.flags, 2) == 0
end

function previewCamera.lockToActor(actor)
  local forward = vec3(math.sin(actor.targetYaw), 0, math.cos(actor.targetYaw))
  local right = vec3(forward.z, 0, -forward.x)
  local focus = actor.target + vec3(0, 1.25, 0)
  local desired = focus - forward * 5.5 + right * 1.8 + vec3(0, 3.2, 0)
  local offset = desired - focus
  local distance = offset:length()
  if distance > 0.001 then
    local direction = offset / distance
    local normal = vec3()
    local hit = physics.raycastTrack(focus, direction, distance, nil, normal, false, false)
    if hit >= 0 and hit < distance then
      desired = focus + direction * math.max(0.75, hit - 0.25)
    end
  end
  local look = focus - desired
  if look:lengthSquared() <= 0.001 then return false end
  look:normalize()
  previewCamera.position:set(desired)
  previewCamera.look:set(look)
  previewCamera.actorID = actor.id
  previewCamera.spawnCount = actor.spawnCount
  previewCamera.locked = true
  ac.log(string.format(
    '[ASRC FPS] pre-Drive arena camera locked: actor=%s spawn=%s position=%s focus=%s',
    tostring(actor.id), tostring(actor.spawnCount), vec3Text(desired), vec3Text(focus)))
  return true
end

function previewCamera.apply(actor)
  if camera == nil or not camera:active() then return false end
  if not previewCamera.locked or previewCamera.actorID ~= actor.id
      or previewCamera.spawnCount ~= actor.spawnCount then
    if not previewCamera.lockToActor(actor) then return false end
  end
  camera.ownShare = 1
  camera.fov = 68
  camera.transform.position = previewCamera.position
  camera.transform.look = previewCamera.look
  camera.transform.up = vec3(0, 1, 0)
  return true
end

local function probeFirstPersonCameraClearance(position)
  local resolved = position:clone()
  local constrained = false
  -- Resolve a small camera sphere rather than only its origin. This protects the near
  -- plane from vertical walls, corners, sloped overhangs, and low ceilings at eye level.
  for _ = 1, 4 do
    local closestDistance = firstPersonCameraRadius
    local closestDirection = nil
    for _, direction in ipairs(firstPersonCameraProbeDirections) do
      local normal = vec3()
      local hit = physics.raycastTrack(resolved, direction, firstPersonCameraRadius,
        nil, normal, false, false)
      if hit >= 0 and hit < closestDistance then
        closestDistance = hit
        closestDirection = direction
      end
    end
    if closestDirection == nil then break end
    resolved = resolved - closestDirection
      * (firstPersonCameraRadius - closestDistance + firstPersonCameraSkin)
    constrained = true
  end
  return resolved, constrained
end

local function resolveFirstPersonCameraPosition(actor, dt)
  local desired = actor.render + vec3(0, cameraHeight, 0)
  local corrected = desired:clone()
  local constrained = false

  -- Sweep from a point safely inside the torso to the desired eye. This catches an
  -- overhang while standing up or walking below sloped scenery before the eye crosses it.
  local anchorHeight = math.max(0.22, cameraHeight - 0.72)
  local anchor = actor.render + vec3(0, anchorHeight, 0)
  local eyeDelta = corrected - anchor
  local eyeDistance = eyeDelta:length()
  if eyeDistance > 0.001 then
    local eyeDirection = eyeDelta / eyeDistance
    local normal = vec3()
    local hit = physics.raycastTrack(anchor, eyeDirection, eyeDistance,
      nil, normal, false, false)
    if hit >= 0 and hit < eyeDistance then
      corrected = anchor + eyeDirection * math.max(0, hit - firstPersonCameraSkin)
      constrained = true
    end
  end

  local clearancePosition, clearanceConstrained = probeFirstPersonCameraClearance(corrected)
  corrected = clearancePosition
  constrained = constrained or clearanceConstrained
  local targetOffset = corrected - desired
  if constrained then
    -- Entering a surface is corrected immediately. Only release is smoothed, so the
    -- camera remains clip-free without buzzing against individual wall triangles.
    firstPersonCameraOffset:set(targetOffset)
    firstPersonCameraCorrections = firstPersonCameraCorrections + 1
  else
    firstPersonCameraOffset:set(math.lerp(firstPersonCameraOffset, vec3(),
      1 - math.exp(-dt * 10)))
  end

  local resolved = desired + firstPersonCameraOffset
  -- A decaying offset can approach different nearby geometry in a tight corner. Validate
  -- the final position once more before handing it to CSP.
  local finalPosition, finalConstrained = probeFirstPersonCameraClearance(resolved)
  if finalConstrained then
    resolved = finalPosition
    firstPersonCameraOffset:set(resolved - desired)
    constrained = true
    firstPersonCameraCorrections = firstPersonCameraCorrections + 1
  end
  if constrained and not firstPersonCameraConstrained then
    ac.log(string.format('[ASRC FPS] first-person camera clearance engaged: desired=%s resolved=%s offset=%s',
      vec3Text(desired), vec3Text(resolved), vec3Text(firstPersonCameraOffset)))
  end
  firstPersonCameraConstrained = constrained
  return resolved
end

local function applyFpsCamera(actor, dt)
  if actor == nil or camera == nil or not camera:active() then return false end
  local look = vec3(math.sin(yaw) * math.cos(pitch), math.sin(pitch), math.cos(yaw) * math.cos(pitch))
  local adsAllowed = not thirdPersonEnabled and actor.reloadRemaining <= 0 and not viewmodelSprint
  local adsTarget = adsAllowed and fpsVisual.adsInput or 0
  local adsSpeed = adsTarget > fpsVisual.ads and 15 or 11
  fpsVisual.ads = math.lerp(fpsVisual.ads, adsTarget, 1 - math.exp(-dt * adsSpeed))
  camera.ownShare = 1
  camera.fov = thirdPersonEnabled and 72 or math.lerp(72, 56, fpsVisual.ads)
  if thirdPersonEnabled then
    firstPersonCameraOffset:set(0, 0, 0)
    firstPersonCameraConstrained = false
    fpsVisual.thirdPersonDistance = math.lerp(fpsVisual.thirdPersonDistance,
      fpsVisual.thirdPersonDistanceTarget, 1 - math.exp(-dt * 12))
    local forward = vec3(math.sin(yaw), 0, math.cos(yaw))
    local right = vec3(forward.z, 0, -forward.x)
    local focus = actor.render + vec3(0, math.max(1.05, cameraHeight - 0.25), 0)
    local desired = focus - forward * fpsVisual.thirdPersonDistance
      + right * 0.72 + vec3(0, 0.55, 0)
    local cameraOffset = desired - focus
    local distance = cameraOffset:length()
    if distance > 0.001 then
      local direction = cameraOffset / distance
      local normal = vec3()
      local hit = physics.raycastTrack(focus, direction, distance, nil, normal, false, false)
      if hit >= 0 and hit < distance then
        desired = focus + direction * math.max(0.35, hit - 0.15)
      end
    end
    local aimTarget = actor.render + vec3(0, cameraHeight, 0) + look * 30
    local cameraLook = aimTarget - desired
    if cameraLook:lengthSquared() > 0.001 then cameraLook:normalize() else cameraLook:set(look) end
      camera.transform.position = desired
    camera.transform.look = cameraLook
  else
    camera.transform.position = resolveFirstPersonCameraPosition(actor, dt)
    camera.transform.look = look
  end
  camera.transform.up = vec3(0, 1, 0)
  return true
end

function fpsVisual.updateActorAnimation(actor, dt)
  if not fpsVisual.modern or actor.modernModel == nil then return true end
  local nowPosition = actor.render:clone()
  local displacement = actor.animationLastPosition ~= nil
    and (nowPosition - actor.animationLastPosition) or vec3()
  actor.animationLastPosition = nowPosition
  local speed = displacement:length() / math.max(dt, 0.001)
  local grounded = bit.band(actor.flags, 16) ~= 0
  local dead = bit.band(actor.flags, 2) ~= 0
  local actionState = actor.actionState or 0
  local stance = fpsVisual.actorStance(actor)
  if actor.animationLoggedStance ~= stance then
    actor.animationLoggedStance = stance
    if actor.id == localSessionID then
      ac.log(string.format(
        '[ASRC FPS] local operator stance changed: stance=%d assetRevision=%d',
        stance, fpsVisual.modernAssetRevision))
    end
  end
  if actor.animationWasGrounded == nil then
    actor.animationWasGrounded = grounded
  elseif actor.animationWasGrounded and not grounded and bit.band(actionState, 1) == 0 then
    actor.animationJumpStarted = effectClock
  elseif not actor.animationWasGrounded and grounded then
    actor.animationLanded = effectClock
  end
  actor.animationWasGrounded = grounded
  if actor.animationActionState ~= actionState then
    if bit.band(actionState, 1) ~= 0 then actor.animationTraversalStarted = effectClock end
    actor.animationActionState = actionState
  end
  local clip = 'aim_idle'
  local position = 0
  local looping = false
  if dead then
    if actor.animationDeathStarted == nil then actor.animationDeathStarted = effectClock end
    clip = 'death'
    position = math.clamp((effectClock - actor.animationDeathStarted) / 1.45, 0, 1)
  else
    actor.animationDeathStarted = nil
    if bit.band(actionState, 1) ~= 0 then
      clip = bit.band(actionState, 2) ~= 0 and 'vault' or 'mantle'
      position = math.clamp((effectClock - (actor.animationTraversalStarted or effectClock))
        / 0.45, 0, 1)
    elseif actor.animationLanded ~= nil and effectClock - actor.animationLanded < 0.3 then
      clip = 'land'
      position = math.clamp((effectClock - actor.animationLanded) / 0.3, 0, 1)
    elseif not grounded then
      local jumpAge = effectClock - (actor.animationJumpStarted or -10)
      if jumpAge < 0.22 then
        clip = 'jump_start'
        position = math.clamp(jumpAge / 0.22, 0, 1)
      else
        clip = 'airborne'
        position = 0.5
      end
    elseif stance == 2 then
      clip = speed > 0.35 and 'prone_crawl' or 'prone_idle'
      looping = speed > 0.35
    elseif stance == 1 then
      clip = speed > 0.35 and 'crouch_move' or 'crouch_idle'
      looping = speed > 0.35
    elseif speed > 4.8 then
      clip = 'sprint'
      looping = true
    elseif speed > 0.35 then
      local forwardX, forwardZ = math.sin(actor.yaw), math.cos(actor.yaw)
      local forwardAmount = displacement.x * forwardX + displacement.z * forwardZ
      local rightAmount = displacement.x * forwardZ - displacement.z * forwardX
      if math.abs(rightAmount) > math.abs(forwardAmount) * 1.15 then
        clip = rightAmount < 0 and 'strafe_left' or 'strafe_right'
      else
        clip = forwardAmount < 0 and 'walk_backward' or 'walk_forward'
      end
      looping = true
    elseif actor.pitch > 0.32 then
      clip = 'aim_up'
    elseif actor.pitch < -0.32 then
      clip = 'aim_down'
    end
    if looping then
      actor.animationPhase = ((actor.animationPhase or 0)
        + dt * math.max(0.75, speed * 0.35)) % 1
      position = actor.animationPhase
    else
      actor.animationPhase = position
    end
  end

  if actor.animationClip ~= clip then
    actor.animationPreviousClip = actor.animationClip
    actor.animationPreviousPosition = actor.animationPosition or 0
    actor.animationBlend = 0
    actor.animationClip = clip
  end
  actor.animationPosition = position
  actor.animationBlend = math.min(1, (actor.animationBlend or 0) + dt / 0.12)
  local ok, err = pcall(function()
    local stanceGroundOffset = fpsVisual.operatorStanceGroundOffsets[stance] or 0
    actor.modernModel:setPosition(vec3(0, stanceGroundOffset, 0))
    actor.modernModel:setAnimation(fpsVisual.asset(fpsVisual.operatorClips[clip]),
      position, true)
    if actor.animationPreviousClip ~= nil and actor.animationBlend < 1 then
      actor.modernModel:blendAnimation(
        fpsVisual.asset(fpsVisual.operatorClips[actor.animationPreviousClip]),
        actor.animationPreviousPosition, 1 - actor.animationBlend, false)
    end
    if actor.reloadRemaining > 0 and not dead then
      actor.modernModel:blendAnimation(fpsVisual.asset(fpsVisual.operatorClips.reload),
        math.clamp(1 - actor.reloadRemaining / 1.8, 0, 1), 0.82, false)
    elseif (actor.animationFireUntil or 0) > effectClock and not dead then
      local firePosition = math.clamp(1 - (actor.animationFireUntil - effectClock) / 0.12, 0, 1)
      actor.modernModel:blendAnimation(fpsVisual.asset(fpsVisual.operatorClips.fire),
        firePosition, 0.9, false)
    end
  end)
  if not ok then
    fpsVisual.fallback('operator animation actor ' .. tostring(actor.id) .. ': ' .. tostring(err))
    return false
  end
  return true
end

function fpsVisual.updateViewmodelAnimation(actor, dt, moving, sprint)
  if (not fpsVisual.modern and not fpsVisual.isLoadoutAsset(fpsVisual.loadedViewmodelAsset))
      or viewmodelRoot == nil or viewmodelRoot == false then return true end
  if fpsVisual.isPistolAsset(fpsVisual.loadedViewmodelAsset)
      and fpsVisual.viewmodelPistolPoseSeedPending then
    fpsVisual.viewmodelPistolPoseSeedPending = false
    return true
  end
  fpsVisual.viewmodelPhase = ((fpsVisual.viewmodelPhase or 0) + dt * 1.15) % 1
  local clip = 'idle'
  local position = fpsVisual.viewmodelPhase
  if actor.reloadRemaining > 0 then
    clip = fpsVisual.isPistolAsset(fpsVisual.loadedViewmodelAsset)
      and 'reload'
      or (actor.ammo == 0 and 'reload_empty' or 'reload')
    position = math.clamp(1 - actor.reloadRemaining / 1.8, 0, 1)
  elseif fpsVisual.viewmodelFireUntil > effectClock then
    clip = 'fire'
    position = math.clamp(1 - (fpsVisual.viewmodelFireUntil - effectClock) / 0.12, 0, 1)
  elseif fpsVisual.viewmodelEquipUntil > effectClock then
    clip = 'equip'
    position = math.clamp(1 - (fpsVisual.viewmodelEquipUntil - effectClock) / 0.55, 0, 1)
  elseif sprint and moving then
    clip = 'sprint'
  end
  local ok, err = pcall(function()
    local animationPath = fpsVisual.isLoadoutAsset(fpsVisual.loadedViewmodelAsset)
      and (fpsVisual.loadoutAssetFolder .. '/'
        .. fpsVisual.loadoutClips(fpsVisual.loadedViewmodelAsset)[clip])
      or fpsVisual.asset(fpsVisual.viewmodelClips[clip])
    viewmodelRoot:setAnimation(animationPath, position, true)
  end)
  if not ok then
    if fpsVisual.modern then
      fpsVisual.fallback('viewmodel animation: ' .. tostring(err))
    else
      clientPackError = 'FPS PISTOL ANIMATION ERROR - CHECK LIVE LOG'
      ac.warn('[ASRC FPS] pistol viewmodel animation failed: ' .. tostring(err))
    end
    return false
  end
  return true
end

local function updateRifleViewmodel(dt, actor, move, sprint)
  viewmodelUpdateAttempts = viewmodelUpdateAttempts + 1
  if viewmodelRoot == nil or viewmodelRoot == false then return end
  local visible = gameplayActive and actor ~= nil and bit.band(actor.flags, 1) ~= 0
    and bit.band(actor.flags, 2) == 0 and not cursorUnlocked and not thirdPersonEnabled
  if not visible or camera == nil or not camera:active() then return end
  viewmodelKick = viewmodelKick * math.exp(-dt * 17)
  local moving = move:lengthSquared() > 0.01
  local pistolViewmodel = fpsVisual.isPistolAsset(fpsVisual.loadedViewmodelAsset)
  local modernViewmodel = fpsVisual.modern
    or fpsVisual.isLoadoutAsset(fpsVisual.loadedViewmodelAsset)
  if not fpsVisual.updateViewmodelAnimation(actor, dt, moving, sprint) then return end
  if moving then viewmodelBobTime = viewmodelBobTime + dt * (sprint and 12 or 8) end
  -- Camera and weapon scene transforms are submitted together in frameBegin. Keeping
  -- both on the requested grabbed-camera pose lets CSP calculate matching motion vectors.
  local cameraPosition = camera.transform.position:clone()
  local look = camera.transform.look:clone()
  if look:lengthSquared() < 0.001 then look:set(0, 0, 1) else look:normalize() end
  local right = vec3(look.z, 0, -look.x)
  if right:lengthSquared() < 0.001 then right:set(1, 0, 0) else right:normalize() end
  -- Build a true camera-relative orthonormal frame. World-up combined with a pitched
  -- look vector shears the KN5 basis and is why steep ADS previously folded the sight.
  local viewUp = vec3(-math.sin(yaw) * math.sin(pitch), math.cos(pitch),
    -math.cos(yaw) * math.sin(pitch))
  local adsMotionScale = 1 - fpsVisual.ads
  local bobX = moving and math.sin(viewmodelBobTime) * 0.004 * adsMotionScale or 0
  local bobY = moving and math.abs(math.cos(viewmodelBobTime)) * 0.003 * adsMotionScale or 0
  local sprintLower = sprint and moving and 0.04 * adsMotionScale or 0
  local wallNormal = vec3()
  local wallHit = physics.raycastTrack(cameraPosition, look, 0.9,
    nil, wallNormal, false, false)
  local wallRetractionTarget = wallHit >= 0 and wallHit < 0.9
    and math.clamp((0.9 - wallHit) / 0.75, 0, 1) or 0
  viewmodelWallRetraction = math.lerp(viewmodelWallRetraction, wallRetractionTarget,
    1 - math.exp(-dt * 18))
  local hipForward = pistolViewmodel and 0.39 or (modernViewmodel and 0.32 or 0.30)
  local hipRight = pistolViewmodel and -0.15 or (modernViewmodel and -0.18 or 0.22)
  local hipUp = pistolViewmodel and -0.24 or (modernViewmodel and -0.32 or -0.20)
  -- The Modern KN5 faces back toward its root, so its apparent screen-right
  -- direction is opposite the holder translation. These calibrated offsets put
  -- the optic axis on the camera look vector and bring the rear sight close
  -- enough to read as true ADS instead of a zoomed hip-fire pose.
  local adsForward = pistolViewmodel and 0.34 or (modernViewmodel and 0.12 or 0.38)
  -- The pistol KN5 faces back toward its holder, so decreasing this camera-right
  -- translation moves the rendered Desert Eagle toward screen-right.
  local pistolAdsRight = fpsVisual.loadedViewmodelAsset == 3 and 0.025 or 0.035
  local adsRight = pistolViewmodel and pistolAdsRight
    or (modernViewmodel and 0.0003 or 0.00)
  local adsUp = pistolViewmodel and -0.12 or (modernViewmodel and -0.2218 or -0.10)
  local pistolReloadPhase = pistolViewmodel and actor.reloadRemaining > 0
    and math.clamp(1 - actor.reloadRemaining / 1.8, 0, 1) or 0
  local pistolReloadLower = fpsVisual.smoothstep01(
    math.clamp(pistolReloadPhase / 0.22, 0, 1))
  local pistolReloadRaise = fpsVisual.smoothstep01(
    math.clamp((pistolReloadPhase - 0.72) / 0.28, 0, 1))
  local pistolReloadWeight = pistolReloadLower * (1 - pistolReloadRaise)
  local visualKickScale = math.lerp(1, 0.35, fpsVisual.ads)
  -- At steep downward pitch the source arms extend beyond their authored first-person
  -- framing. Pull the Modern rig toward the camera on a smooth cubic curve so close
  -- geometry exits behind the near plane instead of exposing sleeve and stock ends.
  local downwardLook = math.clamp((-pitch - math.rad(35)) / math.rad(45), 0, 1)
  local downwardCurve = fpsVisual.smoothstep01(downwardLook)
  local downwardPull = modernViewmodel
    and math.lerp(0.38, 0.14, fpsVisual.ads) * downwardCurve or 0
  local position = cameraPosition
    + look * (math.lerp(hipForward, adsForward, fpsVisual.ads)
      - downwardPull - viewmodelKick * 0.04 * visualKickScale
      - viewmodelWallRetraction * 0.25)
    + right * (math.lerp(hipRight, adsRight, fpsVisual.ads) + bobX)
    + viewUp * (math.lerp(hipUp, adsUp, fpsVisual.ads)
      - bobY - sprintLower + viewmodelKick * 0.012 * visualKickScale
      - viewmodelWallRetraction * 0.12)
    + right * (0.035 * pistolReloadWeight)
    - viewUp * (0.035 * pistolReloadWeight)
  viewmodelLastPosition = position:clone()
  viewmodelRenderPosition = position:clone()
  local pistolReloadAngle = math.rad(22) * pistolReloadWeight
  if pistolReloadAngle > 0 then
    local reloadCos = math.cos(pistolReloadAngle)
    local reloadSin = math.sin(pistolReloadAngle)
    viewmodelRenderLook = look * reloadCos - viewUp * reloadSin
    viewmodelRenderUp = viewUp * reloadCos + look * reloadSin
  else
    viewmodelRenderLook = look:clone()
    viewmodelRenderUp = viewUp:clone()
  end
  localMuzzlePosition:set(position
    + look * (pistolViewmodel and 0.58
      or (modernViewmodel and 0.67 or 0.99))
    + viewUp * (pistolViewmodel and 0.10
      or (modernViewmodel and 0.08 or 0.02)))
  viewmodelUpdateCompletions = viewmodelUpdateCompletions + 1
  if not viewmodelStagesSeen['native-transform:ready'] then
    markViewmodelStage('native-transform:ready', vec3Text(position))
  end
end

local function updateNativeRifleViewmodel(dt)
  local actor = actors[localSessionID]
  local visible = actor ~= nil and bit.band(actor.flags, 1) ~= 0 and bit.band(actor.flags, 2) == 0
    and not cursorUnlocked and not thirdPersonEnabled and camera ~= nil and camera:active()
  if not visible then
    if viewmodelHolder ~= nil then viewmodelHolder:setVisible(false, false) end
    return
  end

  updateRifleViewmodel(dt, actor, viewmodelMove, viewmodelSprint)
  if viewmodelHolder == nil or viewmodelRoot == nil or viewmodelRoot == false
      or viewmodelRenderPosition == nil
      or viewmodelRenderLook == nil or viewmodelRenderUp == nil then return end

  viewmodelDirectDrawAttempts = viewmodelDirectDrawAttempts + 1
  local ok, result = pcall(function()
    -- SceneReference positions use graphics space. Server snapshots and grabbed-camera
    -- transforms use world space, so apply CSP's current floating-origin offset.
    viewmodelHolder:setPosition(viewmodelRenderPosition + ac.getSim().originShift)
    viewmodelHolder:setOrientation(viewmodelRenderLook, viewmodelRenderUp)
    if not viewmodelStagesSeen['native-scene:deferred'] then
      viewmodelHolder:setVisible(false, false)
      viewmodelHolder:clearMotion()
      viewmodelDirectDrawPending = viewmodelDirectDrawPending + 1
      markViewmodelStage('native-scene:deferred', 'waiting one update before first visibility')
      return false
    end
    viewmodelHolder:setVisible(true, false)
    if viewmodelDirectDrawCompletions == 0 then viewmodelHolder:clearMotion() end
  end)

  if not ok then
    viewmodelDirectDrawFailures = viewmodelDirectDrawFailures + 1
    markViewmodelStage('native-scene:failed', result)
    if fpsVisual.modern then
      fpsVisual.fallback('viewmodel scene update: ' .. tostring(result))
    else
      clientPackError = 'FPS RIFLE SCENE UPDATE FAILED - CHECK LIVE LOG'
    end
    if not viewmodelDirectRenderFailureLogged then
      viewmodelDirectRenderFailureLogged = true
      ac.warn('[ASRC FPS] native rifle scene update failed: ' .. tostring(result))
    end
  elseif result ~= false then
    viewmodelDirectDrawCompletions = viewmodelDirectDrawCompletions + 1
    clientPackError = fpsVisual.error
    if not viewmodelStagesSeen['native-scene:ready'] then
      markViewmodelStage('native-scene:ready', 'motion-tracked scene node visible')
      ac.log('[ASRC FPS] native assault-rifle viewmodel scene ready')
    end
  end
end

local function updateRemoteActors(dt)
  local visibleActors = 0
  remoteRender.actorsDrawn = 0
  for _, actor in pairs(actors) do
    if actor.id ~= localSessionID and fpsVisual.actorSceneActive(actor) then
      visibleActors = visibleActors + 1
    end
  end
  remoteRender.actorSnapshotCount = visibleActors
  for _, actor in pairs(actors) do
    if actor.id ~= localSessionID then
      local active = fpsVisual.actorSceneActive(actor)
      remoteRender.drawAttempts = remoteRender.drawAttempts + 1
      local ok, result = pcall(function()
        -- CSP can stop an online-script update once its time budget is exhausted.
        -- Creation and movement must stay in the same per-actor pass: a separate
        -- preparation pass can complete while every transform update is skipped.
        if active then ensureAvatar(actor) end
        if actor.root == nil or actor.root == false then return active and false or nil end
        if not active then
          actor.root:setVisible(false, false)
          actor.nativeSceneVisible = false
          return nil
        end
        local resetMotion = not actor.nativeSceneVisible
          or actor.nativeSceneSpawnCount ~= actor.spawnCount
        local renderError = actor.target - actor.render
        if resetMotion or renderError:lengthSquared() > 2.25 then
          actor.render:set(actor.target)
          actor.yaw = actor.targetYaw
        else
          local poseBlend = 1 - math.exp(-dt * 40)
          actor.render:set(math.lerp(actor.render, actor.target, poseBlend))
          actor.yaw = lerpAngle(actor.yaw, actor.targetYaw, poseBlend)
        end
        local scenePosition, sceneLook, sceneUp, sceneVisible = fpsVisual.actorScenePose(actor)
        if not sceneVisible then
          actor.root:setVisible(false, false)
          actor.nativeSceneVisible = false
          return nil
        end
        actor.root:setPosition(scenePosition + ac.getSim().originShift)
        actor.root:setOrientation(sceneLook, sceneUp)
        local dead = bit.band(actor.flags, 2) ~= 0
        fpsVisual.setActorWeaponVisible(actor, not dead)
        if not fpsVisual.updateActorAnimation(actor, dt) then return false end
        actor.weaponKick = (actor.weaponKick or 0) * math.exp(-dt * 15)
        if actor.weaponRoot ~= nil and actor.weaponRoot ~= false then
          actor.weaponRoot:setPosition(fpsVisual.actorWeaponPosition(actor))
          actor.weaponRoot:setOrientation(vec3(0, math.sin(actor.pitch), math.cos(actor.pitch)),
            vec3(0, 1, 0))
        end
        if actor.nativeScenePrepared == false then
          actor.root:setVisible(false, false)
          actor.root:clearMotion()
          actor.nativeScenePrepared = true
          return false
        end
        actor.root:setVisible(true, false)
        if resetMotion then actor.root:clearMotion() end
        actor.nativeSceneVisible = true
        actor.nativeSceneSpawnCount = actor.spawnCount
        return true
      end)
      if not ok then
        remoteRender.drawFailures = remoteRender.drawFailures + 1
        if fpsVisual.modern then
          fpsVisual.fallback('operator scene actor ' .. tostring(actor.id) .. ': ' .. tostring(result))
          return
        end
        if not remoteRender.failureLogged then
          remoteRender.failureLogged = true
          ac.warn('[ASRC FPS] native remote avatar scene update failed: ' .. tostring(result))
        end
      elseif result == false then
        remoteRender.drawPending = remoteRender.drawPending + 1
      elseif result then
        remoteRender.drawCompletions = remoteRender.drawCompletions + 1
        remoteRender.actorsDrawn = remoteRender.actorsDrawn + 1
      end
    end
  end

  if remoteRender.actorsDrawn > 0 and not remoteRender.readyLogged then
    remoteRender.readyLogged = true
    ac.log(string.format('[ASRC FPS] native remote actor scene ready: drawn=%d visible=%d',
      remoteRender.actorsDrawn, visibleActors))
  end
end

local function effectUp(direction)
  return math.abs(direction.y) > 0.92 and vec3(1, 0, 0) or vec3(0, 1, 0)
end

local function drawDirectShotEffects()
  if #tracers == 0 and #impacts == 0 and #sparks == 0
      and next(fpsVisual.grenades) == nil then return end
  if not ensureShotEffectTemplates() then return end

  local rendered = 0
  local now = ui.time()
  local ok, err = pcall(function()
    render.setBlendMode(render.BlendMode.OpaqueForced)
    render.setCullMode(render.CullMode.None)
    render.setDepthMode(render.DepthMode.Normal)

    for _, tracer in ipairs(tracers) do
      if tracer.expiresAt > now then
        local direction = tracer.to - tracer.from
        if direction:lengthSquared() > 0.001 then
          direction:normalize()
          local progress = math.clamp((now - tracer.bornAt) / tracer.travelTime, 0, 1)
          local position = math.lerp(tracer.from, tracer.to, progress)
          render.setTransform(position, direction, effectUp(direction), true)
          if render.mesh(tracerRenderParams) ~= false then rendered = rendered + 1 end
          if tracer.flashUntil > now then
            render.setTransform(tracer.flashFrom, direction, effectUp(direction), true)
            if render.mesh(muzzleFlashRenderParams(tracer)) ~= false then rendered = rendered + 1 end
          end
        end
      end
    end

    for _, impact in ipairs(impacts) do
      if impact.expiresAt > now then
        render.setTransform(impact.position, impact.normal, effectUp(impact.normal), true)
        if render.mesh(impactRenderParams) ~= false then rendered = rendered + 1 end
      end
    end

    for _, spark in ipairs(sparks) do
      if spark.ttl > 0 and spark.velocity:lengthSquared() > 0.001 then
        local direction = spark.velocity:clone():normalize()
        render.setTransform(spark.position, direction, effectUp(direction), true)
        if render.mesh(sparkRenderParams) ~= false then rendered = rendered + 1 end
      end
    end

    for _, grenade in pairs(fpsVisual.grenades) do
      local direction = grenade.velocity ~= nil and grenade.velocity:lengthSquared() > 0.001
        and grenade.velocity:clone():normalize() or vec3(0, 0, 1)
      render.setTransform(grenade.position, direction, vec3(0, 1, 0), true)
      if render.mesh(sparkRenderParams) ~= false then rendered = rendered + 1 end
    end
  end)
  render.setTransform(vec3(), vec3(0, 0, 1), vec3(0, 1, 0))
  render.setCullMode(render.CullMode.Back)
  render.setBlendMode(render.BlendMode.Opaque)
  if not ok then
    if not shotRender.failureLogged then
      shotRender.failureLogged = true
      ac.warn('[ASRC FPS] direct shot-effect draw failed: ' .. tostring(err))
    end
    return
  end
  shotRender.effectsRendered = shotRender.effectsRendered + rendered
  if rendered > 0 and not shotRender.readyLogged then
    shotRender.readyLogged = true
    ac.log(string.format('[ASRC FPS] direct shot-effect rendering ready: meshes=%d shots=%d',
      rendered, shotRender.eventsReceived))
  end
end

local function updateLocalThirdPersonAvatar(actor, prepareOnly)
  if actor == nil then return false end
  local ok, err = pcall(function()
    if prepareOnly then
      ensureAvatar(actor)
      return
    end
    if actor.root == nil or actor.root == false then return end
    local active = fpsVisual.actorSceneActive(actor)
    actor.root:setVisible(active and thirdPersonEnabled)
    if not active then return end
    local avatarPosition = actor.render:clone()
    -- Snapshot interpolation is useful for the camera, but on an upward stair step it
    -- briefly leaves the mannequin below the authoritative support plane. Keep its feet
    -- on that plane while grounded so the model cannot appear embedded in a tread.
    if bit.band(actor.flags, 16) ~= 0 and actor.target.y > avatarPosition.y then
      avatarPosition.y = actor.target.y
    end
    local dead = bit.band(actor.flags, 2) ~= 0
    local scenePosition, sceneLook, sceneUp, sceneVisible = fpsVisual.actorScenePose(actor)
    if not sceneVisible then
      actor.root:setVisible(false)
      return
    end
    if not dead then scenePosition = avatarPosition end
    actor.root:setPosition(scenePosition + ac.getSim().originShift)
    -- Local mouse yaw is immediate; replicated yaw is intentionally delayed by snapshots.
    -- A corpse instead retains the yaw captured at death while its visual root settles.
    if dead then
      actor.root:setOrientation(sceneLook, sceneUp)
    else
      actor.root:setOrientation(vec3(math.sin(yaw), 0, math.cos(yaw)), vec3(0, 1, 0))
    end
    fpsVisual.setActorWeaponVisible(actor, not dead)
    if not fpsVisual.updateActorAnimation(actor, viewmodelFrameDt) then return end
    if actor.weaponRoot ~= nil and actor.weaponRoot ~= false then
      actor.weaponRoot:setPosition(fpsVisual.actorWeaponPosition(actor))
      actor.weaponRoot:setOrientation(vec3(0, math.sin(actor.pitch), math.cos(actor.pitch)),
        vec3(0, 1, 0))
    end
  end)
  if not ok then
    if fpsVisual.modern then fpsVisual.fallback('local operator scene: ' .. tostring(err)) end
    if not localAvatarErrorLogged then
      localAvatarErrorLogged = true
      ac.warn('[ASRC FPS] local third-person avatar update failed: ' .. tostring(err))
    end
    localAvatarReady = false
    return false
  end
  local ready = actor.root ~= nil and actor.root ~= false
  if ready and not localAvatarReady then
    localAvatarKind = actor.avatarKind or 'unknown'
    ac.log('[ASRC FPS] local third-person avatar ready: kind=' .. localAvatarKind)
  end
  localAvatarReady = ready
  return ready
end

local clientCollisionRadius = 0.40
local collisionProbeOffsets = {-1, -0.5, 0, 0.5, 1}
-- Keep the lowest client prediction ray just above the authoritative 48 cm step
-- allowance. Lower rays classify ordinary stair risers as walls and make prediction
-- fight the server while the capsule steps up.
local standingProbeHeights = {0.52, 0.9, 1.48}
local crouchingProbeHeights = {0.52, 0.76, 0.98}
local proneProbeHeights = {0.52, 0.55}

local function localTrackProbeMovement(position, movement, stance)
  local distance = movement:length()
  if distance < 0.0001 then return false, vec2() end
  local direction = vec3(movement.x / distance, 0, movement.y / distance)
  local side = vec3(-direction.z, 0, direction.x) * (clientCollisionRadius * 0.88)
  local heights = stance == 2 and proneProbeHeights
    or stance == 1 and crouchingProbeHeights or standingProbeHeights
  local normal = vec3()
  local blockingNormal = vec2()
  local closestHit = math.huge
  for _, height in ipairs(heights) do
    for _, offset in ipairs(collisionProbeOffsets) do
      normal:set(0, 0, 0)
      local origin = position + side * offset + vec3(0, height, 0)
      local hit = physics.raycastTrack(origin, direction, distance + clientCollisionRadius,
        nil, normal, false, false)
      if hit >= 0 and hit <= distance + clientCollisionRadius
          and math.abs(normal.y) < 0.55 and hit < closestHit then
        closestHit = hit
        blockingNormal:set(normal.x, normal.z)
      end
    end
  end
  return closestHit < math.huge, blockingNormal
end

local function projectPlanarMovement(movement, normal)
  local normalLength = normal:length()
  if normalLength < 0.0001 then return vec2() end
  local nx = normal.x / normalLength
  local nz = normal.y / normalLength
  local amount = movement.x * nx + movement.y * nz
  return vec2(movement.x - nx * amount, movement.y - nz * amount)
end

local function localTrackResolveMovement(position, movement, stance, blockedDirection)
  local resolved = vec2(movement.x, movement.y)
  local constrained = false

  -- Supplemental arena collision can be server-only. Its blocked direction prevents
  -- prediction from entering those walls while still allowing tangent or escape motion.
  if blockedDirection ~= nil and blockedDirection:lengthSquared() > 0.0001 then
    local normalLength = blockedDirection:length()
    local nx = blockedDirection.x / normalLength
    local nz = blockedDirection.y / normalLength
    local amount = resolved.x * nx + resolved.y * nz
    if amount > 0 then
      resolved:set(resolved.x - nx * amount, resolved.y - nz * amount)
      constrained = true
    end
  end

  local blocked, localNormal = localTrackProbeMovement(position, resolved, stance)
  if not blocked then return resolved, constrained end
  constrained = true
  resolved = projectPlanarMovement(resolved, localNormal)
  if resolved:lengthSquared() < 0.00000001 then return vec2(), true end

  -- A tangent ray can immediately touch the same rough wall. Only add another
  -- constraint when its normal actually opposes the remaining movement.
  local blockedAgain, secondNormal = localTrackProbeMovement(position, resolved, stance)
  if blockedAgain and secondNormal:lengthSquared() > 0.0001 then
    local opposition = math.abs(resolved.x * secondNormal.x + resolved.y * secondNormal.y)
    if opposition > resolved:length() * secondNormal:length() * 0.08 then
      resolved = projectPlanarMovement(resolved, secondNormal)
    end
  end
  return resolved, true
end

function script.update(dt)
  hideCarrierCars()

  local localActor = actors[localSessionID]
  local move = vec2()
  local sprint = false
  local jumpStarted = false
  gameplayActive = fpsGameplayIsActive()
  if gameplayActive then hud.requestWeaponImage() end
  fpsVisual.updatePickups()
  -- The companion HUD can win exclusive UI ownership before this script sees
  -- a gameplay-mode callback. Reset pause ownership on the simulation state
  -- transition instead, which is observed by script.update() in either case.
  if gameplayActive and not previousGameplayActive then
    hud.nativePauseMenu = false
    hud.leaveServerArmed = false
    hud.pauseInputLogged = false
    hud.pausePage = 'main'
    hud.controlsContentLogged = false
  end
  if gameplayActive then previewCamera.everEnteredGameplay = true end
  if gameplayActive and hud.appOwnsHud() and hud.bridge ~= nil then
    persistentCursor = hud.bridge.appPersistentCursor ~= 0
  end
  viewmodelServerDiagnosticAccumulator = viewmodelServerDiagnosticAccumulator + dt
  if gameplayActive and (viewmodelLastSentStage ~= viewmodelLastStage
      or viewmodelServerDiagnosticAccumulator >= 5) then
    local diagnosticRemoteActor = nil
    for _, candidate in pairs(actors) do
      if candidate.id ~= localSessionID and bit.band(candidate.flags, 1) ~= 0
          and bit.band(candidate.flags, 2) == 0
          and (diagnosticRemoteActor == nil or candidate.id < diagnosticRemoteActor.id) then
        diagnosticRemoteActor = candidate
      end
    end
    local diagnosticFlags = 1
      + (rifleAssetFolder ~= nil and 2 or 0)
      + (viewmodelRoot ~= nil and viewmodelRoot ~= false and 4 or 0)
      + (localActor ~= nil and 8 or 0)
      + (camera ~= nil and camera:active() and 16 or 0)
      + (viewmodelDirectDrawCompletions > 0 and 32 or 0)
      + (thirdPersonEnabled and 64 or 0)
      + (localAvatarReady and 128 or 0)
      + (remoteRender.actorSnapshotCount > 0 and 256 or 0)
      + (remoteRender.actorsDrawn > 0 and 512 or 0)
      + (shotRender.eventsReceived > 0 and 1024 or 0)
      + (shotRender.effectsRendered > 0 and 2048 or 0)
    local diagnosticRemoteRender = diagnosticRemoteActor ~= nil
      and diagnosticRemoteActor.render or vec3()
    if diagnosticRemoteActor ~= nil and diagnosticRemoteActor.root ~= nil
        and diagnosticRemoteActor.root ~= false then
      local sceneOk, scenePosition = pcall(function()
        return diagnosticRemoteActor.root:getPosition() - ac.getSim().originShift
      end)
      if sceneOk then diagnosticRemoteRender = scenePosition end
    end
    viewmodelDiagnosticSendOk = hud.clientDiagnosticEvent({
      pipeline = 21,
      flags = diagnosticFlags,
      attempts = viewmodelUpdateAttempts,
      completions = viewmodelUpdateCompletions,
      frameBeginCalls = viewmodelFrameBeginCalls,
      draw3DCalls = viewmodelDraw3DCalls,
      drawUICalls = viewmodelDrawUICalls,
      directDrawAttempts = viewmodelDirectDrawAttempts,
      directDrawCompletions = viewmodelDirectDrawCompletions,
      directDrawPending = viewmodelDirectDrawPending,
      directDrawFailures = viewmodelDirectDrawFailures,
      position = viewmodelLastPosition or vec3(),
      remoteActorID = diagnosticRemoteActor ~= nil and diagnosticRemoteActor.id or 255,
      remoteTarget = diagnosticRemoteActor ~= nil and diagnosticRemoteActor.target or vec3(),
      remoteRender = diagnosticRemoteRender,
      remoteTargetYaw = diagnosticRemoteActor ~= nil and diagnosticRemoteActor.targetYaw or 0,
      remoteRenderYaw = diagnosticRemoteActor ~= nil and diagnosticRemoteActor.yaw or 0,
      stage = viewmodelLastStage,
    })
    ac.log('[ASRC FPS] viewmodel diagnostic sent to server: stage=' .. viewmodelLastStage
      .. '; result=' .. tostring(viewmodelDiagnosticSendOk))
    viewmodelLastSentStage = viewmodelLastStage
    viewmodelServerDiagnosticAccumulator = 0
  end
  viewmodelDiagnosticAccumulator = viewmodelDiagnosticAccumulator + dt
  if gameplayActive and viewmodelDiagnosticAccumulator >= 1 then
    viewmodelDiagnosticAccumulator = viewmodelDiagnosticAccumulator - 1
    ac.log(string.format(
      '[ASRC FPS] viewmodel heartbeat: pipeline=%s assetCached=%s modelLoaded=%s actor=%s cameraActive=%s nearClip=%.3f clipMethod=%s updates=%d/%d callbacks=frameBegin:%d,draw3D:%d,drawUI:%d directDraw=%d/%d,pending:%d,failures:%d remote=%d/%d,attempts:%d,pending:%d,failures:%d lastStage=%s detail=%s lastPosition=%s',
      viewmodelPipelineVersion, tostring(rifleAssetFolder ~= nil),
      tostring(viewmodelRoot ~= nil and viewmodelRoot ~= false), tostring(localActor ~= nil),
      tostring(camera ~= nil and camera:active()), ac.getSim().cameraClipNear,
      fpsClipPlaneMethod, viewmodelUpdateCompletions,
      viewmodelUpdateAttempts, viewmodelFrameBeginCalls, viewmodelDraw3DCalls,
      viewmodelDrawUICalls, viewmodelDirectDrawCompletions, viewmodelDirectDrawAttempts,
      viewmodelDirectDrawPending, viewmodelDirectDrawFailures, remoteRender.actorsDrawn,
      remoteRender.actorSnapshotCount, remoteRender.drawAttempts, remoteRender.drawPending,
      remoteRender.drawFailures, viewmodelLastStage,
      viewmodelLastStageDetail,
      viewmodelLastPosition ~= nil and vec3Text(viewmodelLastPosition) or 'nil'))
  end
  if previousGameplayActive ~= gameplayActive then
    local state = ac.getSim()
    ac.log(string.format(
      '[ASRC FPS] gameplay state changed: active=%s sessionStarted=%s live=%s paused=%s mainMenu=%s results=%s replay=%s localActor=%s cameraActive=%s',
      tostring(gameplayActive), tostring(state.isSessionStarted), tostring(state.isLive),
      tostring(state.isPaused), tostring(state.isInMainMenu),
      tostring(state.isLookingAtSessionResults), tostring(state.isReplayActive),
      tostring(localActor ~= nil), tostring(camera ~= nil and camera:active())))
    previousGameplayActive = gameplayActive
  end
  if gameplayActive then
    -- CSP documents that getCarInputControls() keeps reporting the physical
    -- controls while the carrier input is disabled. This prevents AC steering,
    -- throttle and camera bindings from competing with the FPS actor.
    physics.setCarNoInput(true)
    physics.setGentleStop(car.index, true)
    setCarrierInputSuppressed(true)
    cameraRetryAccumulator = cameraRetryAccumulator + dt
    if not acquireFpsCamera() then
      if cameraRetryAccumulator >= 1 then
        ac.log(string.format('[ASRC FPS] FPS camera unavailable: error=%s', tostring(cameraError)))
        cameraRetryAccumulator = 0
      end
    else
      cameraRetryAccumulator = 0
    end

    -- Some tracks can exhaust CSP's online-script update budget before reaching the
    -- tail of this callback. Service persistent scene nodes immediately after camera
    -- acquisition so models and remote poses cannot be skipped while input/collision
    -- work continues. CSP commits world-node transforms from script.update(), while the
    -- camera-relative rifle remains in frameBegin() to stay synchronized with the view.
    if localActor ~= nil and camera ~= nil and camera:active() then
      ensureLocalViewmodel()
      updateLocalThirdPersonAvatar(localActor, true)
    end
    updateRemoteActors(dt)

    -- Main/pits/results UI was excluded above. Once gameplay is active, FPS
    -- owns the pointer even if a third-party app incorrectly asks for it.
    scoreboardHeld = ac.isKeyDown(ac.KeyIndex.Tab)
    cursorUnlocked = scoreboardHeld or persistentCursor or not hud.loadout.confirmed
    local thirdPersonToggle = ac.isKeyDown(ac.KeyIndex.F6)
    if thirdPersonToggle and not thirdPersonToggleWasHeld then
      thirdPersonEnabled = not thirdPersonEnabled
      ac.log('[ASRC FPS] camera mode changed: '
        .. (thirdPersonEnabled and 'third-person over-shoulder' or 'first-person'))
    end
    thirdPersonToggleWasHeld = thirdPersonToggle
    local thirdPersonZoomShift = ac.isKeyDown(ac.KeyIndex.LeftShift)
      or ac.isKeyDown(ac.KeyIndex.RightShift)
    if thirdPersonEnabled and not cursorUnlocked and thirdPersonZoomShift then
      local wheel = ui.mouseWheel()
      if math.abs(wheel) > 0.001 then
        fpsVisual.thirdPersonDistanceTarget = math.clamp(
          fpsVisual.thirdPersonDistanceTarget - wheel * fpsVisual.thirdPersonZoomStep,
          fpsVisual.thirdPersonDistanceMin, fpsVisual.thirdPersonDistanceMax)
      end
    end
    local mouse = vec2()
    if not cursorUnlocked then
      mouse = ac.accessMouseDelta(true, true, true)
      ac.hideMouseCursor(true)
    end
    local rawMouseAds = ac.isKeyDown(ac.KeyIndex.RightButton)
    local uiMouseAds = ac.getUI().isMouseRightKeyDown or ui.mouseDown(ui.MouseButton.Right)
    local gamepadAds = math.clamp(
      ac.getGamepadAxisValue(0, ac.GamepadAxis.LeftTrigger), 0, 1)
    fpsVisual.adsInput = not cursorUnlocked and not thirdPersonEnabled
      and math.max((rawMouseAds or uiMouseAds) and 1 or 0, gamepadAds) or 0
    local aimSensitivity = hud.aimSensitivity(fpsVisual.adsInput)
    local rightX = -clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.RightThumbX))
    local rightY = clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.RightThumbY))
    yaw = yaw - mouse.x * 0.0022 * aimSensitivity
      + rightX * dt * 2.8 * aimSensitivity
    pitch = math.clamp(pitch - mouse.y * 0.0022 * aimSensitivity
      + rightY * dt * 2.2 * aimSensitivity, -1.45, 1.45)

    -- FPS axes deliberately do not reuse throttle/brake: the right trigger is
    -- Fire here, and must never become forward movement or carrier acceleration.
    -- Mapped steering remains a fallback for devices not exposed as raw pad 0.
    local mapped = physics.getCarInputControls()
    local keyboardX = -inputAxis(ac.KeyIndex.A, ac.KeyIndex.D, ac.KeyIndex.Left, ac.KeyIndex.Right)
    local keyboardY = inputAxis(ac.KeyIndex.S, ac.KeyIndex.W, ac.KeyIndex.Down, ac.KeyIndex.Up)
    local rawX = -clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.LeftThumbX))
    local rawY = clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.LeftThumbY))
    move = vec2(
      selectInput(keyboardX, rawX, -clampStick(mapped.steer)),
      selectInput(keyboardY, rawY, 0))
    if move:lengthSquared() > 1 then move:normalize() end
    local gamepadFire = ac.getGamepadAxisValue(0, ac.GamepadAxis.RightTrigger) > 0.35
    -- Raw VK input remains available while mouse-delta capture owns the pointer. CSP UI
    -- state alone reports false in that state on some builds, which previously meant the
    -- server received movement but never a Fire bit.
    local rawMouseFire = ac.isKeyDown(ac.KeyIndex.LeftButton)
    local uiMouseFire = ac.getUI().isMouseLeftKeyDown or ui.mouseDown(ui.MouseButton.Left)
    local boundFire = hud.bindingDown('fire', rawMouseFire or uiMouseFire)
    local fire = not cursorUnlocked and (boundFire or gamepadFire)
    if fire and not fireCaptureLogged then
      fireCaptureLogged = true
      ac.log(string.format(
        '[ASRC FPS] fire input captured: bound=%s rawMouse=%s uiMouse=%s gamepad=%s',
        tostring(boundFire), tostring(rawMouseFire), tostring(uiMouseFire),
        tostring(gamepadFire)))
    end
    sprint = hud.bindingDown('sprint', ac.isKeyDown(ac.KeyIndex.LeftShift))
      or ac.isGamepadButtonPressed(0, ac.GamepadButton.LeftThumb)
    if fpsVisual.adsInput > 0.05 then sprint = false end
    local sprintRequested = sprint and move:lengthSquared() > 0.0001
      and localStance == 0 and not predictedAirborne
    sprint = sprintRequested and not fpsVisual.stamina.exhausted
      and fpsVisual.stamina.value > 0
    if sprint then
      fpsVisual.stamina.value = math.max(0,
        fpsVisual.stamina.value - fpsVisual.stamina.drainPerSecond * dt)
      fpsVisual.stamina.recoveryDelay = fpsVisual.stamina.recoveryDelaySeconds
      if fpsVisual.stamina.value <= 0 then
        fpsVisual.stamina.value = 0
        fpsVisual.stamina.exhausted = true
        sprint = false
      end
    else
      fpsVisual.stamina.recoveryDelay = math.max(0,
        fpsVisual.stamina.recoveryDelay - dt)
      if fpsVisual.stamina.recoveryDelay <= 0 then
        fpsVisual.stamina.value = math.min(fpsVisual.stamina.maximum,
          fpsVisual.stamina.value + fpsVisual.stamina.recoveryPerSecond * dt)
        if fpsVisual.stamina.exhausted
            and fpsVisual.stamina.value >= fpsVisual.stamina.exhaustionRelease then
          fpsVisual.stamina.exhausted = false
        end
      end
    end
    viewmodelMove:set(move)
    viewmodelSprint = sprint
    local jump = hud.bindingDown('jump', ac.isKeyDown(ac.KeyIndex.Space))
    local crouch = hud.bindingDown('crouch', ac.isKeyDown(ac.KeyIndex.C))
    local crouchToggleMode = hud.controlSettings.crouchToggle == true
    local reload = hud.bindingDown('reload', ac.isKeyDown(ac.KeyIndex.R))
    local gamepadWeaponSwitch = ac.isGamepadButtonPressed(0, ac.GamepadButton.Y)
    local gamepadGrenade = ac.isGamepadButtonPressed(0, ac.GamepadButton.RightShoulder)
    local grenade = hud.bindingDown('grenade', ac.isKeyDown(ac.KeyIndex.G))
      or gamepadGrenade
    if not cursorUnlocked and ac.isKeyPressed(ac.KeyIndex.D1) then
      hud.loadout.activeSlot = 0
    elseif not cursorUnlocked and ac.isKeyPressed(ac.KeyIndex.D2) then
      hud.loadout.activeSlot = 1
    elseif not cursorUnlocked and gamepadWeaponSwitch and not weaponSwitchWasHeld then
      hud.loadout.activeSlot = hud.loadout.activeSlot == 0 and 1 or 0
      ac.log('[ASRC FPS] Xbox Y switched weapon slot to '
        .. tostring(hud.loadout.activeSlot + 1))
    end
    weaponSwitchWasHeld = gamepadWeaponSwitch
    jumpStarted = jump and not jumpWasHeld
    local crouchPressed = crouch and not crouchWasHeld
    local jumpConsumed = false
    if not crouch then fpsVisual.crouchSuppressedUntilRelease = false end
    if localStance == 2 then
      if crouchPressed or jumpStarted then
        localStance = 1
        crouchLatched = true
        crouchHeldSeconds = 0
        fpsVisual.crouchToggleReleaseStands = false
        jumpConsumed = jumpStarted
      end
    elseif localStance == 1 and jumpStarted then
      localStance = 0
      crouchLatched = false
      crouchHeldSeconds = 0
      fpsVisual.crouchToggleReleaseStands = false
      fpsVisual.crouchSuppressedUntilRelease = true
    elseif crouchToggleMode then
      if localStance == 0 then
        if crouchPressed and not fpsVisual.crouchSuppressedUntilRelease then
          localStance = 1
          crouchLatched = true
          crouchHeldSeconds = dt
          fpsVisual.crouchToggleReleaseStands = false
        end
      elseif crouchPressed then
        -- A short second press releases crouch on button-up. Holding that same
        -- press continues into prone without requiring a separate binding.
        crouchHeldSeconds = dt
        fpsVisual.crouchToggleReleaseStands = true
      elseif crouch then
        crouchHeldSeconds = crouchHeldSeconds + dt
        if crouchHeldSeconds >= 0.65 then
          localStance = 2
          crouchLatched = false
          crouchHeldSeconds = 0
          fpsVisual.crouchToggleReleaseStands = false
        end
      elseif crouchWasHeld and fpsVisual.crouchToggleReleaseStands then
        localStance = 0
        crouchLatched = false
        crouchHeldSeconds = 0
        fpsVisual.crouchToggleReleaseStands = false
      elseif crouchWasHeld then
        crouchHeldSeconds = 0
      end
    elseif localStance == 0 then
      if crouch and not fpsVisual.crouchSuppressedUntilRelease then
        localStance = 1
        crouchHeldSeconds = dt
        crouchLatched = false
      end
    elseif crouchLatched then
      if crouchPressed then
        crouchLatched = false
        crouchHeldSeconds = dt
        fpsVisual.crouchToggleReleaseStands = false
      end
    elseif crouch then
      crouchHeldSeconds = crouchHeldSeconds + dt
      if crouchHeldSeconds >= 0.65 then
        localStance = 2
        crouchHeldSeconds = 0
      end
    else
      localStance = 0
      crouchHeldSeconds = 0
      fpsVisual.crouchToggleReleaseStands = false
    end
    crouchWasHeld = crouch
    if jumpConsumed then jumpStarted = false end
    jumpWasHeld = jump
    local buttons = (fire and 1 or 0) + (sprint and 2 or 0) + (jump and 4 or 0)
      + (crouch and 8 or 0) + (reload and 16 or 0)
      + (fpsVisual.adsInput > 0.5 and 32 or 0)
      + (crouchToggleMode and 64 or 0)
      + (grenade and not cursorUnlocked and 128 or 0)

    sendAccumulator = sendAccumulator + dt
    if sendAccumulator >= 0.05 then
      sendAccumulator = sendAccumulator - 0.05
      sequence = sequence + 1
      inputSendOk = hud.inputEvent({ sequence = sequence, move = move, yaw = yaw,
        pitch = pitch, buttons = buttons, selectedSlot = hud.loadout.activeSlot }, false, 255)
    end

    inputDiagnosticAccumulator = inputDiagnosticAccumulator + dt
    local inputIsActive = move:lengthSquared() > 0.0001 or buttons ~= 0
      or mouse:lengthSquared() > 0.0001 or math.abs(rightX) > 0.001 or math.abs(rightY) > 0.001
    if inputIsActive and not inputWasActive then
      ac.log(string.format(
        '[ASRC FPS] input became active: sequence=%s move=(%.3f, %.3f) mouse=(%.3f, %.3f) buttons=%s sendOk=%s',
        tostring(sequence), move.x, move.y, mouse.x, mouse.y, tostring(buttons),
        tostring(inputSendOk)))
    end
    inputWasActive = inputIsActive
    if inputDiagnosticAccumulator >= 1 then
      inputDiagnosticAccumulator = inputDiagnosticAccumulator - 1
      ac.log(string.format(
        '[ASRC FPS] input sample: sequence=%s keyboard=(%.1f, %.1f) rawPad=(%.3f, %.3f) mapped=(%.3f, %.3f, %.3f) move=(%.3f, %.3f) mouse=(%.3f, %.3f) yaw=%.3f pitch=%.3f buttons=%s sendOk=%s',
        tostring(sequence), keyboardX, keyboardY, rawX, rawY, mapped.steer, mapped.gas,
        mapped.brake, move.x, move.y, mouse.x, mouse.y, yaw, pitch, tostring(buttons),
        tostring(inputSendOk)))
    end
  else
    -- Stopping accessMouseDelta() releases and restores the cursor shortly;
    -- camera ownership is retained only for the initial arena preview. It does not
    -- capture input or suppress AC's pre-Drive menu.
    physics.setCarNoInput(false)
    setCarrierInputSuppressed(false)
    if previewCamera.isEligible(localActor) then
      if acquireFpsCamera() then previewCamera.apply(localActor) end
    else
      releaseFpsCamera()
    end
    if viewmodelHolder ~= nil then viewmodelHolder:setVisible(false, false) end
    for _, actor in pairs(actors) do
      if actor.root ~= nil and actor.root ~= false then actor.root:setVisible(false, false) end
      actor.nativeSceneVisible = false
    end
    sendAccumulator = 0
    inputDiagnosticAccumulator = 0
    inputWasActive = false
    viewmodelMove:set(0, 0)
    viewmodelSprint = false
    fpsVisual.adsInput = 0
    fpsVisual.ads = 0
    thirdPersonToggleWasHeld = false
    weaponSwitchWasHeld = false
    jumpWasHeld = false
    predictedHorizontalVelocity = vec2()
    predictedAirborne = false
    predictionCollisionConstrained = false
    predictionClearSnapshots = 0
    localStance = 0
    crouchWasHeld = false
    crouchHeldSeconds = 0
    crouchLatched = false
    fpsVisual.crouchToggleReleaseStands = false
    fpsVisual.crouchSuppressedUntilRelease = false
    cameraHeight = 1.65
    scoreboardHeld = false
    cursorUnlocked = false
  end

  if gameplayActive and localActor ~= nil and bit.band(localActor.flags, 1) ~= 0
      and bit.band(localActor.flags, 2) == 0 then
    local forward = vec2(math.sin(yaw), math.cos(yaw))
    local right = vec2(forward.y, -forward.x)
    local predicted = forward * move.y + right * move.x
    local aimingMovementScale = fpsVisual.adsInput > 0.5 and 0.4 or 1
    local desiredVelocity = predicted * (localStance == 2 and 1.8
      or localStance == 1 and 3.4 or sprint and 9 or 6) * aimingMovementScale
    if predictedGroundY == nil then predictedGroundY = localActor.target.y end
    local grounded = bit.band(localActor.flags, 16) ~= 0
    if jumpStarted and (grounded or localActor.render.y <= predictedGroundY + 0.05) then
      predictedHorizontalVelocity:set(desiredVelocity)
      predictedVerticalVelocity = 7.25
      predictedAirborne = true
    elseif not predictedAirborne and grounded then
      predictedHorizontalVelocity:set(desiredVelocity)
    else
      predictedHorizontalVelocity:set(math.lerp(predictedHorizontalVelocity,
        desiredVelocity, math.min(1, dt * 1.5)))
    end
    local predictedStep = predictedHorizontalVelocity * dt
    local resolvedStep, locallyConstrained = localTrackResolveMovement(localActor.render,
      predictedStep, localStance, localActor.collisionNormal)
    localActor.render:add(vec3(resolvedStep.x, 0, resolvedStep.y))
    if locallyConstrained and dt > 0.0001 then
      predictedHorizontalVelocity:set(resolvedStep.x / dt, resolvedStep.y / dt)
    end
    if localActor.render.y > predictedGroundY or predictedVerticalVelocity > 0 then
      predictedVerticalVelocity = predictedVerticalVelocity - 15 * dt
      localActor.render.y = localActor.render.y + predictedVerticalVelocity * dt
      if localActor.render.y <= predictedGroundY then
        localActor.render.y = predictedGroundY
        predictedVerticalVelocity = 0
        predictedAirborne = false
      end
    elseif grounded then
      predictedAirborne = false
    end
  end

  local targetCameraHeight = localStance == 2 and 0.42 or localStance == 1 and 1.05 or 1.65
  cameraHeight = math.lerp(cameraHeight, targetCameraHeight, 1 - math.exp(-dt * 9))

  for _, actor in pairs(actors) do
    local localActorRender = actor.id == localSessionID
    local blendRate = localActorRender and (predictionCollisionConstrained and 14 or 6) or 18
    local blend = 1 - math.exp(-dt * blendRate)
    if localActorRender then
      local correction = actor.target - actor.render
      local planarCorrection = vec2(correction.x * blend, correction.z * blend)
      local resolvedCorrection = localTrackResolveMovement(actor.render, planarCorrection,
        localStance, actor.collisionNormal)
      actor.render:add(vec3(resolvedCorrection.x, 0, resolvedCorrection.y))
      actor.render.y = math.lerp(actor.render.y, actor.target.y, blend)
      actor.yaw = lerpAngle(actor.yaw, actor.targetYaw, blend)
    end
  end

  renderDiagnosticAccumulator = renderDiagnosticAccumulator + dt
  if gameplayActive and renderDiagnosticAccumulator >= 1 then
    renderDiagnosticAccumulator = renderDiagnosticAccumulator - 1
    if localActor == nil then
      ac.log(string.format('[ASRC FPS] render state: local actor %s is missing; cameraActive=%s',
        tostring(localSessionID),
        tostring(camera ~= nil and camera:active())))
    else
      ac.log(string.format(
        '[ASRC FPS] render state: actor=%s target=%s render=%s error=%.3f flags=%s cameraActive=%s cameraShare=%s cameraPosition=%s originalCameraPosition=%s cameraClearance=%s cameraOffset=%s cameraCorrections=%d grabbed=%s',
        tostring(localActor.id), vec3Text(localActor.target), vec3Text(localActor.render),
        (localActor.target - localActor.render):length(), tostring(localActor.flags),
        tostring(camera ~= nil and camera:active()),
        tostring(camera ~= nil and camera.ownShare or 'nil'),
        camera ~= nil and vec3Text(camera.transform.position) or 'nil',
        camera ~= nil and vec3Text(camera.transformOriginal.position) or 'nil',
        tostring(firstPersonCameraConstrained), vec3Text(firstPersonCameraOffset),
        firstPersonCameraCorrections,
        tostring(ac.isCameraGrabbed())))
      ac.log(string.format(
        '[ASRC FPS] viewmodel render state: pipeline=%s loaded=%s updates=%d/%d directDraw=%d/%d,pending:%d,failures:%d lastStage=%s detail=%s lastPosition=%s',
        viewmodelPipelineVersion, tostring(viewmodelRoot ~= nil and viewmodelRoot ~= false),
        viewmodelUpdateCompletions, viewmodelUpdateAttempts, viewmodelDirectDrawCompletions,
        viewmodelDirectDrawAttempts, viewmodelDirectDrawPending, viewmodelDirectDrawFailures,
        viewmodelLastStage, viewmodelLastStageDetail,
        viewmodelLastPosition ~= nil and vec3Text(viewmodelLastPosition) or 'nil'))
      local diagnosticRemoteActor = nil
      for _, candidate in pairs(actors) do
        if candidate.id ~= localSessionID and bit.band(candidate.flags, 1) ~= 0
            and bit.band(candidate.flags, 2) == 0
            and (diagnosticRemoteActor == nil or candidate.id < diagnosticRemoteActor.id) then
          diagnosticRemoteActor = candidate
        end
      end
      if diagnosticRemoteActor ~= nil then
        ac.log(string.format(
          '[ASRC FPS] remote render state: actor=%s target=%s render=%s error=%.3f targetYaw=%.3f renderYaw=%.3f yawError=%.3f',
          tostring(diagnosticRemoteActor.id), vec3Text(diagnosticRemoteActor.target),
          vec3Text(diagnosticRemoteActor.render),
          (diagnosticRemoteActor.target - diagnosticRemoteActor.render):length(),
          diagnosticRemoteActor.targetYaw, diagnosticRemoteActor.yaw,
          math.abs(math.atan2(math.sin(diagnosticRemoteActor.targetYaw
            - diagnosticRemoteActor.yaw), math.cos(diagnosticRemoteActor.targetYaw
            - diagnosticRemoteActor.yaw)))))
      end
    end
  end

  effectClock = effectClock + dt
  local visualNow = ui.time()
  fpsVisual.updateMuzzleLights(visualNow)
  for i = #tracers, 1, -1 do
    if tracers[i].expiresAt <= visualNow then table.remove(tracers, i) end
  end
  for i = #impacts, 1, -1 do
    local impact = impacts[i]
    local remove = impact.expiresAt <= visualNow
    if not remove and impact.targetID ~= nil then
      local target = actors[impact.targetID]
      remove = target == nil or bit.band(target.flags, 2) ~= 0
        or (impact.targetSpawnCount ~= nil and target.spawnCount ~= impact.targetSpawnCount)
      if not remove then impact.position:set(target.target + impact.targetOffset) end
    end
    if remove then table.remove(impacts, i) end
  end
  for i = #sparks, 1, -1 do
    local spark = sparks[i]
    spark.ttl = spark.ttl - dt
    spark.position:add(spark.velocity * dt)
    spark.velocity.y = spark.velocity.y - 9.81 * dt
    if spark.ttl <= 0 then table.remove(sparks, i) end
  end
  for i = #rifleSounds, 1, -1 do
    local sound = rifleSounds[i]
    sound.ttl = sound.ttl - dt
    if sound.ttl <= 0 or not sound.event:isValid() then
      sound.event:dispose()
      table.remove(rifleSounds, i)
    end
  end
  for i = #killFeed, 1, -1 do
    killFeed[i].ttl = killFeed[i].ttl - dt
    if killFeed[i].ttl <= 0 then table.remove(killFeed, i) end
  end
  for i = #hud.awardPopups, 1, -1 do
    local popup = hud.awardPopups[i]
    popup.age = popup.age + dt
    popup.ttl = popup.ttl - dt
    if popup.ttl <= 0 then table.remove(hud.awardPopups, i) end
  end
  hud.publish(dt)
end

function hud.drawAwardPopups(center)
  if cursorUnlocked then return end
  for i = 1, #hud.awardPopups do
    local popup = hud.awardPopups[i]
    local alpha = math.min(1, popup.age / 0.15, popup.ttl / 0.4)
    ui.setCursor(center + vec2(34, -86 + (i - 1) * 25))
    ui.pushFont(ui.Font.Title)
    ui.textColored(popup.text, rgbm(1, 0.78, 0.22, alpha))
    ui.popFont()
  end
end

function script.frameBegin(dt, gameDT)
  viewmodelFrameBeginCalls = viewmodelFrameBeginCalls + 1
  viewmodelFrameDt = math.max(0.001, math.min(dt, 0.05))
  local localActor = actors[localSessionID]
  if fpsGameplayIsActive() and localActor ~= nil and acquireFpsCamera() then
    applyFpsCamera(localActor, viewmodelFrameDt)
    localActor.weaponKick = (localActor.weaponKick or 0) * math.exp(-viewmodelFrameDt * 15)
    updateLocalThirdPersonAvatar(localActor, false)
    updateNativeRifleViewmodel(viewmodelFrameDt)
  elseif previewCamera.isEligible(localActor) and acquireFpsCamera() then
    previewCamera.apply(localActor)
  end
end

function script.draw3D()
  viewmodelDraw3DCalls = viewmodelDraw3DCalls + 1
  if not gameplayActive then return end
  drawDirectShotEffects()
end

function hud.drawFallbackRadar(size, scale, margin)
  local diameter = 190 * scale
  local radius = diameter * 0.5
  local center = vec2(margin + radius, margin + radius)
  local panelMin = center - vec2(radius, radius)
  local panelMax = center + vec2(radius, radius)
  ui.drawRectFilled(panelMin, panelMax, rgbm(0.025, 0.035, 0.05, 0.88), 9 * scale)
  ui.drawRect(panelMin, panelMax, rgbm(0.38, 0.62, 0.78, 0.68), 9 * scale,
    nil, math.max(1, 1.4 * scale))
  ui.drawCircle(center, radius - 8 * scale, rgbm(0.5, 0.7, 0.82, 0.5), 48,
    math.max(1, scale))
  ui.drawCircle(center, (radius - 8 * scale) * 0.5, rgbm(0.35, 0.5, 0.62, 0.34),
    36, math.max(1, scale))
  ui.drawLine(center - vec2(radius - 8 * scale, 0),
    center + vec2(radius - 8 * scale, 0), rgbm(0.3, 0.45, 0.56, 0.25),
    math.max(1, scale))
  ui.drawLine(center - vec2(0, radius - 8 * scale),
    center + vec2(0, radius - 8 * scale), rgbm(0.3, 0.45, 0.56, 0.25),
    math.max(1, scale))

  local own = actors[localSessionID]
  if own ~= nil then
    local lookX, lookZ = math.sin(yaw), math.cos(yaw)
    local rightX, rightZ = lookZ, -lookX
    local usableRadius = radius - 16 * scale
    for id, actor in pairs(actors) do
      if id ~= localSessionID and (hud.radarVisible[id] or 0) ~= 0 then
        local offset = actor.target - own.target
        -- Match the companion app's player-up basis. FPS yaw increases toward
        -- screen-left, so presentation-right is the negated world right dot product.
        local right = -(offset.x * rightX + offset.z * rightZ)
        local forward = offset.x * lookX + offset.z * lookZ
        local point = vec2(right, -forward) / 40 * usableRadius
        local length = point:length()
        if length > usableRadius then point:scale(usableRadius / length) end
        ui.drawCircleFilled(center + point, 4.5 * scale,
          rgbm(1, 0.22, 0.15, 0.95), 16)
      end
    end
  end
  ui.drawTriangleFilled(center - vec2(0, 8 * scale),
    center + vec2(-5 * scale, 6 * scale), center + vec2(5 * scale, 6 * scale),
    rgbm(0.35, 0.9, 1, 1))
  ui.setCursor(vec2(margin + 10 * scale, margin + diameter - 24 * scale))
  ui.textColored('COMBAT RADAR  40 m', rgbm(0.65, 0.8, 0.9, 0.9))
  return diameter
end

function hud.drawFallbackRanking(ranking, scale, margin, radarDiameter)
  local top = margin + radarDiameter + 12 * scale
  local width = 310 * scale
  local rows = math.min(8, #ranking)
  local panelMin = vec2(margin, top)
  local panelMax = vec2(margin + width, top + (34 + rows * 23) * scale)
  ui.drawRectFilled(panelMin, panelMax, rgbm(0.025, 0.035, 0.05, 0.84), 8 * scale)
  ui.drawRect(panelMin, panelMax, rgbm(0.38, 0.62, 0.78, 0.58), 8 * scale,
    nil, math.max(1, 1.2 * scale))
  ui.setCursor(vec2(margin + 12 * scale, top + 8 * scale))
  ui.text('DEATHMATCH')
  for place = 1, rows do
    local actor = ranking[place]
    ui.setCursor(vec2(margin + 12 * scale, top + (10 + place * 23) * scale))
    ui.text(string.format('%2d  %-16s  %4d  %2d/%2d', place,
      names[actor.id] or ('Player ' .. actor.id), actor.score, actor.kills, actor.deaths))
  end
end

function hud.drawFallbackStatusWidgets(size, scale, margin, actor)
  local activeWeapon = actor ~= nil and (actor.activeSlot == 1
      and actor.secondaryWeapon or actor.mainWeapon) or hud.loadout.mainWeapon
  local bottom = size.y - margin
  local height = 148 * scale
  local leftWidth = 330 * scale
  local rightWidth = 390 * scale
  local leftMin = vec2(margin, bottom - height)
  local leftMax = vec2(margin + leftWidth, bottom)
  ui.drawRectFilled(leftMin, leftMax, rgbm(0.025, 0.035, 0.05, 0.9), 7 * scale)
  ui.drawRect(leftMin, leftMax, rgbm(0.45, 0.62, 0.78, 0.52), 7 * scale,
    nil, math.max(1, scale))
  ui.drawRectFilled(leftMin, vec2(leftMin.x + 4 * scale, leftMax.y),
    rgbm(0.22, 0.82, 0.98, 0.95), 2 * scale)
  ui.setCursor(leftMin + vec2(16, 10) * scale)
  ui.textColored('OPERATOR STATUS', rgbm(0.55, 0.78, 0.9, 0.9))
  local health = actor ~= nil and actor.health or 0
  ui.setCursor(leftMin + vec2(16, 31) * scale)
  ui.pushFont(ui.Font.Title)
  ui.textColored(string.format('HEALTH   %d', health),
    health <= 25 and rgbm(1, 0.2, 0.16, 1) or rgbm.colors.white)
  ui.popFont()
  local healthRatio = math.clamp(health / math.max(1, hud.maximumHealth), 0, 1)
  local healthBarMin = leftMin + vec2(16, 61) * scale
  local healthBarMax = leftMin + vec2(314, 72) * scale
  ui.drawRectFilled(healthBarMin, healthBarMax, rgbm(0.08, 0.12, 0.16, 0.94), 3 * scale)
  ui.drawRectFilled(healthBarMin, vec2(healthBarMin.x
      + (healthBarMax.x - healthBarMin.x) * healthRatio, healthBarMax.y),
    healthRatio <= 0.25 and rgbm(1, 0.2, 0.16, 1) or rgbm(0.25, 0.88, 0.72, 1),
    3 * scale)
  local stamina = math.clamp(math.floor(fpsVisual.stamina.value + 0.5), 0, 100)
  ui.setCursor(leftMin + vec2(16, 79) * scale)
  ui.textColored(string.format('STAMINA  %d%%', stamina),
    stamina <= 20 and rgbm(1, 0.58, 0.16, 1) or rgbm(0.75, 0.88, 0.95, 1))
  local staminaBarMin = leftMin + vec2(16, 101) * scale
  local staminaBarMax = leftMin + vec2(314, 111) * scale
  ui.drawRectFilled(staminaBarMin, staminaBarMax, rgbm(0.08, 0.12, 0.16, 0.94), 3 * scale)
  ui.drawRectFilled(staminaBarMin, vec2(staminaBarMin.x
      + (staminaBarMax.x - staminaBarMin.x) * stamina / 100, staminaBarMax.y),
    stamina <= 20 and rgbm(1, 0.58, 0.16, 1) or rgbm(0.25, 0.72, 1, 1), 3 * scale)
  ui.setCursor(leftMin + vec2(16, 119) * scale)
  ui.text(string.format('K %d   D %d   SCORE %d', actor and actor.kills or 0,
    actor and actor.deaths or 0, actor and actor.score or 0))
  ui.setCursor(leftMin + vec2(190, 119) * scale)
  ui.textColored(actor == nil and 'LINK: WAITING' or inputSendOk and 'LINK: ACTIVE'
      or 'LINK: BLOCKED', actor ~= nil and inputSendOk and rgbm(0.35, 1, 0.45, 1)
      or rgbm(1, 0.55, 0.2, 1))

  local right = size.x - margin
  local rightMin = vec2(right - rightWidth, bottom - height)
  local rightMax = vec2(right, bottom)
  ui.drawRectFilled(rightMin, rightMax, rgbm(0.025, 0.035, 0.05, 0.9), 7 * scale)
  ui.drawRect(rightMin, rightMax, rgbm(0.45, 0.62, 0.78, 0.52), 7 * scale,
    nil, math.max(1, scale))
  ui.drawRectFilled(vec2(rightMax.x - 4 * scale, rightMin.y), rightMax,
    rgbm(0.22, 0.82, 0.98, 0.95), 2 * scale)
  if fpsVisual.hudWeapon.imagePath ~= nil then
    ui.drawImage(fpsVisual.hudWeapon.imagePath, rightMin + vec2(8, 26) * scale,
      rightMin + vec2(252, 124) * scale, rgbm(1, 1, 1, 0.98))
  else
    ui.setCursor(rightMin + vec2(42, 72) * scale)
    ui.textColored('LOADING CARBINE...', rgbm(0.55, 0.68, 0.76, 0.8))
  end
  ui.setCursor(rightMin + vec2(16, 10) * scale)
  ui.text(actor ~= nil and actor.reloadRemaining > 0
    and string.format('RELOADING  %.1fs', actor.reloadRemaining)
    or (hud.itemNames[activeWeapon] or 'FIREARM'))
  ui.setCursor(rightMin + vec2(258, 32) * scale)
  ui.pushFont(ui.Font.Title)
  ui.text(string.format('%02d', actor and actor.ammo or 0))
  ui.popFont()
  ui.setCursor(rightMin + vec2(258, 64) * scale)
  ui.text(string.format('%d RESERVE MAGS', actor and actor.reserveMagazines or 0))
  ui.setCursor(rightMin + vec2(258, 89) * scale)
  ui.text('R  RELOAD')
  ui.setCursor(rightMin + vec2(258, 108) * scale)
  ui.text(string.format('G  %s  x%d', hud.itemNames[actor and actor.lethal or hud.loadout.lethal]
    or 'LETHAL', actor and actor.lethalsRemaining or 0))
  ui.setCursor(rightMin + vec2(16, 126) * scale)
  ui.text(thirdPersonEnabled and string.format('F6  3P  SHIFT + WHEEL %.1f m',
    fpsVisual.thirdPersonDistanceTarget) or 'F6  FIRST PERSON')
  if actor ~= nil and actor.reloadRemaining > 0 then
    local reloadMin = rightMin + vec2(258, 133) * scale
    local reloadMax = rightMin + vec2(374, 141) * scale
    ui.drawRectFilled(reloadMin, reloadMax, rgbm(0.08, 0.12, 0.16, 0.94), 3 * scale)
    ui.drawRectFilled(reloadMin, vec2(reloadMin.x
        + (reloadMax.x - reloadMin.x) * math.clamp(1 - actor.reloadRemaining / 1.8, 0, 1),
      reloadMax.y), rgbm(1, 0.7, 0.2, 1), 3 * scale)
  end
end

function hud.cycleLoadoutItem(mask, current, items, direction)
  local start = 1
  for index = 1, #items do
    if items[index] == current then start = index; break end
  end
  for offset = 1, #items do
    local index = ((start - 1 + direction * offset) % #items) + 1
    if hud.itemAllowed(mask, items[index]) then return items[index] end
  end
  return current
end

function hud.submitLoadout()
  if not hud.loadout.catalogReceived then return end
  hud.loadout.result = 'SENDING TO SERVER...'
  hud.loadoutSelectEvent({
    mainWeapon = hud.loadout.mainWeapon,
    lethal = hud.loadout.lethal,
    secondaryWeapon = hud.loadout.secondaryWeapon,
  })
end

function hud.drawLoadoutMenu(initialSelection)
  local size = ui.windowSize()
  local scale = math.clamp(math.min(size.x / 1920, size.y / 1080), 0.75, 1.5)
  local panelSize = vec2(760, 560) * scale
  local panelMin = (size - panelSize) * 0.5
  local mouse = ui.mousePos()
  ui.captureMouse(true)
  ui.setMouseCursor(ui.MouseCursor.Arrow)
  ui.drawRectFilled(vec2(), size, rgbm(0.006, 0.01, 0.016, 0.82))
  ui.drawRectFilled(panelMin, panelMin + panelSize, rgbm(0.025, 0.035, 0.05, 0.98),
    10 * scale)
  ui.drawRect(panelMin, panelMin + panelSize, rgbm(0.42, 0.68, 0.88, 0.85),
    10 * scale, nil, math.max(1, 1.5 * scale))

  local function button(label, position, dimensions, accent)
    local maximum = position + dimensions
    local hovered = mouse.x >= position.x and mouse.x <= maximum.x
      and mouse.y >= position.y and mouse.y <= maximum.y
    local base = accent and rgbm(0.12, 0.42, 0.58, 1) or rgbm(0.10, 0.18, 0.25, 1)
    ui.drawRectFilled(position, maximum, hovered and rgbm(0.2, 0.5, 0.68, 1) or base,
      5 * scale)
    ui.drawRect(position, maximum, rgbm(0.42, 0.66, 0.82, 0.9), 5 * scale,
      nil, math.max(1, scale))
    ui.setCursor(position + vec2(14, 12) * scale)
    ui.text(label)
    return hovered and ui.mouseClicked(ui.MouseButton.Left)
  end

  local left = panelMin + vec2(42, 32) * scale
  ui.setCursor(left)
  ui.pushFont(ui.Font.Huge)
  ui.text(initialSelection and 'SELECT LOADOUT' or 'CHANGE LOADOUT')
  ui.popFont()
  ui.setCursor(left + vec2(0, 52) * scale)
  ui.textColored(initialSelection and 'Confirmation is required before spawning.'
      or 'Changes apply on your next respawn.', rgbm(0.55, 0.78, 0.94, 1))

  local rows = {
    { label = 'MAIN WEAPON', field = 'mainWeapon', mask = 'allowedMainWeapons',
      items = { 1, 2 } },
    { label = 'LETHAL EQUIPMENT', field = 'lethal', mask = 'allowedLethals',
      items = { 16, 17 } },
    { label = 'SECONDARY WEAPON', field = 'secondaryWeapon',
      mask = 'allowedSecondaryWeapons', items = { 3, 4 } },
  }
  for index = 1, #rows do
    local row = rows[index]
    local y = 105 + (index - 1) * 100
    ui.setCursor(left + vec2(0, y) * scale)
    ui.textColored(row.label, rgbm(0.62, 0.72, 0.8, 1))
    if button('<', left + vec2(0, y + 28) * scale, vec2(52, 44) * scale, false) then
      hud.loadout[row.field] = hud.cycleLoadoutItem(hud.loadout[row.mask],
        hud.loadout[row.field], row.items, -1)
    end
    ui.setCursor(left + vec2(90, y + 40) * scale)
    ui.pushFont(ui.Font.Title)
    ui.text(hud.itemNames[hud.loadout[row.field]] or 'UNKNOWN')
    ui.popFont()
    if button('>', left + vec2(560, y + 28) * scale, vec2(52, 44) * scale, false) then
      hud.loadout[row.field] = hud.cycleLoadoutItem(hud.loadout[row.mask],
        hud.loadout[row.field], row.items, 1)
    end
  end

  ui.setCursor(left + vec2(0, 417) * scale)
  ui.textColored(hud.loadout.result, hud.loadout.catalogReceived
    and rgbm(0.65, 0.88, 1, 1) or rgbm(1, 0.6, 0.2, 1))
  if button(initialSelection and 'CONFIRM & SPAWN' or 'QUEUE FOR RESPAWN',
      left + vec2(360, 410) * scale, vec2(252, 46) * scale, true) then
    hud.submitLoadout()
  end
  if not initialSelection and button('BACK TO MATCH MENU', left + vec2(0, 472) * scale,
      vec2(245, 42) * scale, false) then
    hud.pausePage = 'main'
  end
end

function script.drawUI()
  if hud.exclusiveSubscription ~= nil and not hud.drawingFallback then return end
  viewmodelDrawUICalls = viewmodelDrawUICalls + 1
  if not gameplayActive then return end
  if not hud.loadout.confirmed then
    hud.drawLoadoutMenu(true)
    return
  end
  local size = ui.windowSize()
  local center = size / 2
  local hudScale = math.clamp(math.min(size.x / 1920, size.y / 1080), 0.75, 1.65)
  local hudMargin = 28 * hudScale
  if cursorUnlocked then
    -- AC's own TAB leaderboard also asks for mouse ownership. Capture it here so the
    -- FPS scoreboard controls receive the click instead of rendering as inert HUD.
    ui.captureMouse(true)
    ui.setMouseCursor(ui.MouseCursor.Arrow)
  end
  if not cursorUnlocked then
    drawFallbackRifle(size)
    if fpsVisual.ads <= 0.05 then
      ui.drawLine(center - vec2(9, 0), center - vec2(3, 0), rgbm.colors.white, 2)
      ui.drawLine(center + vec2(3, 0), center + vec2(9, 0), rgbm.colors.white, 2)
      ui.drawLine(center - vec2(0, 9), center - vec2(0, 3), rgbm.colors.white, 2)
      ui.drawLine(center + vec2(0, 3), center + vec2(0, 9), rgbm.colors.white, 2)
    end
  end
  if hitMarkerUntil > effectClock and not cursorUnlocked then
    local c = rgbm(1, 0.25, 0.15,
      math.min(1, (hitMarkerUntil - effectClock) * 7))
    ui.drawLine(center - vec2(8, 8), center - vec2(3, 3), c, 3)
    ui.drawLine(center + vec2(8, 8), center + vec2(3, 3), c, 3)
    ui.drawLine(center + vec2(8, -8), center + vec2(3, -3), c, 3)
    ui.drawLine(center + vec2(-8, 8), center + vec2(-3, 3), c, 3)
  end
  hud.drawAwardPopups(center)

  local actor = actors[localSessionID]
  if clientPackError ~= nil then
    ui.setCursor(vec2(hudMargin, size.y - hudMargin - 174 * hudScale))
    ui.pushStyleColor(ui.StyleColor.Text, rgbm(1, 0.18, 0.12, 1))
    ui.text(clientPackError)
    ui.popStyleColor()
  end
  hud.drawFallbackStatusWidgets(size, hudScale, hudMargin, actor)
  ui.setCursor(vec2(center.x - 80, 20))
  ui.textAligned(string.format('%02d:%02d   TARGET %d', math.floor(remainingSeconds / 60),
    math.floor(remainingSeconds % 60), killLimit), 0.5, vec2(160, 24))

  for i, item in ipairs(killFeed) do
    ui.setCursor(vec2(size.x - 390, 28 + (i - 1) * 24))
    ui.text(item.text)
  end
  local ranking = {}
  for _, rankedActor in pairs(actors) do
    if bit.band(rankedActor.flags, 1) ~= 0 then ranking[#ranking + 1] = rankedActor end
  end
  table.sort(ranking, function(a, b)
    if a.kills ~= b.kills then return a.kills > b.kills end
    if a.deaths ~= b.deaths then return a.deaths < b.deaths end
    return a.id < b.id
  end)
  if scoreboardHeld then
    local panelMin = center - vec2(390, 280)
    local panelMax = center + vec2(390, 280)
    ui.drawRectFilled(panelMin, panelMax, rgbm(0.025, 0.03, 0.04, 0.92), 8)
    ui.drawRect(panelMin, panelMax, rgbm(0.75, 0.78, 0.84, 0.7), 8, nil, 2)
    ui.setCursor(panelMin + vec2(28, 22))
    ui.pushFont(ui.Font.Title)
    ui.text('DEATHMATCH SCOREBOARD')
    ui.popFont()
    ui.setCursor(panelMin + vec2(28, 66))
    ui.text('POS   PLAYER                    SCORE   KILLS   DEATHS   HEALTH')
    for i = 1, math.min(16, #ranking) do
      local rankedActor = ranking[i]
      ui.setCursor(panelMin + vec2(28, 70 + i * 27))
      ui.text(string.format('%2d    %-24s   %5d    %3d      %3d      %3d', i,
        names[rankedActor.id] or ('Player ' .. rankedActor.id), rankedActor.score,
        rankedActor.kills, rankedActor.deaths, rankedActor.health))
    end
    ui.transparentWindow('asrc-fps-scoreboard-controls', panelMin + vec2(20, 505),
      vec2(740, 48), true, true, function()
        ui.setCursor(vec2(8, 8))
        if ui.checkbox('Keep mouse cursor visible after releasing TAB', persistentCursor) then
          persistentCursor = not persistentCursor
        end
        ui.sameLine(12)
        ui.textColored('Release TAB to close scoreboard', rgbm(0.75, 0.78, 0.84, 1))
      end)
  else
    local radarDiameter = hud.drawFallbackRadar(size, hudScale, hudMargin)
    hud.drawFallbackRanking(ranking, hudScale, hudMargin, radarDiameter)
  end
  if persistentCursor and not scoreboardHeld then
    ui.setCursor(vec2(center.x - 145, size.y - 42))
    ui.textColored('MOUSE CURSOR UNLOCKED  •  Hold TAB to disable',
      rgbm(0.9, 0.75, 0.3, 1))
  end
  if camera == nil then
    ui.setCursor(vec2(center.x - 300, center.y - 20))
    ui.textColored('FPS camera compatibility gate failed: ' .. tostring(cameraError), rgbm.colors.red)
  end
  if matchState == 2 then
    ui.setCursor(vec2(center.x - 220, center.y - 120))
    ui.pushFont(ui.Font.Huge)
    ui.text('MATCH COMPLETE')
    ui.popFont()
    ui.text('Winner: ' .. (names[winnerID] or 'No winner'))
    for i = 1, #ranking do
      local rankedActor = ranking[i]
      ui.text(string.format('%2d. %-22s  %3d kills  %3d deaths', i,
        names[rankedActor.id] or ('Player ' .. rankedActor.id), rankedActor.kills, rankedActor.deaths))
    end
  end
end

function hud.drawControlsMenu(panelMin, panelSize, scale, pauseButton)
  local controls = {
    { label = 'FIRE', action = 'fire' },
    { label = 'SPRINT', action = 'sprint' },
    { label = 'CROUCH / PRONE', action = 'crouch', crouchMode = true },
    { label = 'RELOAD', action = 'reload' },
    { label = 'JUMP', action = 'jump' },
    { label = 'GRENADE', action = 'grenade' },
    { label = 'MELEE', action = 'melee', reserved = true },
  }
  local left = panelMin + vec2(42, 34) * scale

  if hud.bindingCapture ~= nil and ui.time() >= hud.bindingCaptureAfter then
    if ac.isKeyPressed(ac.KeyIndex.Escape) then
      hud.bindingCapture = nil
    else
      for i = 1, #hud.bindingCandidates do
        local candidate = hud.bindingCandidates[i]
        if ac.isKeyPressed(candidate.key) then
          hud.bindings[hud.bindingCapture] = candidate.key
          ac.log(string.format('[ASRC FPS] binding changed: action=%s key=%s',
            hud.bindingCapture, candidate.name))
          hud.bindingCapture = nil
          break
        end
      end
    end
  end

  ui.setCursor(left)
  ui.pushFont(ui.Font.Huge)
  ui.text('FPS CONTROLS')
  ui.popFont()
  ui.setCursor(left + vec2(0, 49) * scale)
  ui.textColored('Select an action, then press a keyboard or mouse button.',
    rgbm(0.55, 0.76, 0.9, 1))
  ui.setCursor(left + vec2(0, 68) * scale)
  ui.textColored('XBOX:  Y  SWITCH WEAPON    •    RB  GRENADE',
    rgbm(0.72, 0.8, 0.86, 1))

  for index = 1, #controls do
    local item = controls[index]
    local rowY = 86 + (index - 1) * 47
    ui.setCursor(left + vec2(0, rowY + 12) * scale)
    ui.text(item.label)
    if item.reserved then
      ui.setCursor(left + vec2(175, rowY + 12) * scale)
      ui.textColored('FUTURE ACTION', rgbm(1, 0.62, 0.25, 1))
    end
    local value = hud.bindingCapture == item.action and 'PRESS A KEY…'
      or hud.bindingName(hud.bindings[item.action])
    local bindingWidth = item.crouchMode and 180 or 300
    if pauseButton(value, left + vec2(430, rowY) * scale,
        vec2(bindingWidth, 40) * scale, false) then
      hud.bindingCapture = item.action
      hud.bindingCaptureAfter = ui.time() + 0.25
    end
    if item.crouchMode then
      local modeLabel = hud.controlSettings.crouchToggle and 'TOGGLE' or 'HOLD'
      if pauseButton(modeLabel, left + vec2(618, rowY) * scale,
          vec2(112, 40) * scale, false) then
        hud.controlSettings.crouchToggle = not hud.controlSettings.crouchToggle
        fpsVisual.crouchToggleReleaseStands = false
        ac.log('[ASRC FPS] crouch input mode changed: '
          .. (hud.controlSettings.crouchToggle and 'toggle' or 'hold'))
      end
    end
  end

  local mouse = ui.mousePos()
  local function sensitivitySlider(label, field, rowY)
    local minimum, maximum = 0.2, 3.0
    local value = math.clamp(tonumber(hud.aimSettings[field]) or
      (field == 'adsSensitivity' and 0.8 or 1.0), minimum, maximum)
    ui.setCursor(left + vec2(0, rowY + 5) * scale)
    ui.text(label)
    ui.setCursor(left + vec2(344, rowY + 5) * scale)
    ui.text(string.format('%d%%', math.floor(value * 100 + 0.5)))
    local trackMin = left + vec2(430, rowY + 13) * scale
    local trackMax = trackMin + vec2(300, 8) * scale
    local hovered = mouse.x >= trackMin.x - 8 * scale
      and mouse.x <= trackMax.x + 8 * scale
      and mouse.y >= trackMin.y - 12 * scale
      and mouse.y <= trackMax.y + 12 * scale
    if hovered and ui.mouseDown(ui.MouseButton.Left) then
      local unit = math.clamp((mouse.x - trackMin.x) / (trackMax.x - trackMin.x), 0, 1)
      value = math.floor((math.lerp(minimum, maximum, unit) * 20) + 0.5) / 20
      hud.aimSettings[field] = value
    end
    local unit = (value - minimum) / (maximum - minimum)
    local knob = vec2(math.lerp(trackMin.x, trackMax.x, unit),
      (trackMin.y + trackMax.y) * 0.5)
    ui.drawRectFilled(trackMin, trackMax, rgbm(0.11, 0.18, 0.24, 1), 4 * scale)
    ui.drawRectFilled(trackMin, vec2(knob.x, trackMax.y),
      rgbm(0.22, 0.58, 0.82, 1), 4 * scale)
    ui.drawRectFilled(knob - vec2(6, 10) * scale, knob + vec2(6, 10) * scale,
      hovered and rgbm(0.65, 0.88, 1, 1) or rgbm(0.42, 0.72, 0.92, 1), 3 * scale)
  end

  sensitivitySlider('HIP-FIRE AIM SENSITIVITY', 'hipSensitivity', 425)
  sensitivitySlider('ADS AIM SENSITIVITY', 'adsSensitivity', 471)

  if pauseButton('RESET DEFAULTS', left + vec2(300, 530) * scale,
      vec2(190, 42) * scale, false) then
    for action, key in pairs(hud.bindingDefaults) do hud.bindings[action] = key end
    hud.aimSettings.hipSensitivity = 1.0
    hud.aimSettings.adsSensitivity = 0.8
    hud.controlSettings.crouchToggle = false
    fpsVisual.crouchToggleReleaseStands = false
    hud.bindingCapture = nil
    ac.log('[ASRC FPS] FPS controls reset to defaults')
  end
  if pauseButton('BACK TO MATCH MENU', left + vec2(0, 530) * scale,
      vec2(260, 42) * scale, false) then
    hud.bindingCapture = nil
    hud.pausePage = 'main'
  end
end

function hud.drawEnvironmentMenu(panelMin, panelSize, scale, pauseButton)
  local weather = {
    { type = 15, label = 'CLEAR' }, { type = 16, label = 'FEW CLOUDS' },
    { type = 17, label = 'SCATTERED' }, { type = 18, label = 'BROKEN CLOUDS' },
    { type = 19, label = 'OVERCAST' }, { type = 20, label = 'FOG' },
    { type = 21, label = 'MIST' }, { type = 3, label = 'LIGHT DRIZZLE' },
    { type = 6, label = 'LIGHT RAIN' }, { type = 7, label = 'RAIN' },
    { type = 8, label = 'HEAVY RAIN' }, { type = 31, label = 'WINDY' },
  }
  local left = panelMin + vec2(42, 34) * scale
  ui.setCursor(left)
  ui.pushFont(ui.Font.Huge)
  ui.text('TIME & WEATHER')
  ui.popFont()
  ui.setCursor(left + vec2(0, 49) * scale)
  ui.textColored('Authoritative WeatherFX controls — synchronized to every client.',
    rgbm(0.55, 0.76, 0.9, 1))

  ui.setCursor(left + vec2(0, 91) * scale)
  ui.text('TIME OF DAY')
  local hour = math.floor(hud.environmentDraftTimeSeconds / 3600) % 24
  if pauseButton('- 1 HOUR', left + vec2(0, 119) * scale,
      vec2(170, 40) * scale, false) then
    hud.environmentDraftTimeSeconds = ((hour + 23) % 24) * 3600
  end
  ui.setCursor(left + vec2(205, 128) * scale)
  ui.pushFont(ui.Font.Title)
  ui.text(string.format('%02d:00', hour))
  ui.popFont()
  if pauseButton('+ 1 HOUR', left + vec2(315, 119) * scale,
      vec2(170, 40) * scale, false) then
    hud.environmentDraftTimeSeconds = ((hour + 1) % 24) * 3600
  end

  ui.setCursor(left + vec2(0, 185) * scale)
  ui.text('WEATHER TYPE')
  for index = 1, #weather do
    local item = weather[index]
    local column = (index - 1) % 3
    local row = math.floor((index - 1) / 3)
    local label = hud.environmentDraftWeather == item.type and ('> ' .. item.label) or item.label
    if pauseButton(label, left + vec2(column * 250, 215 + row * 52) * scale,
        vec2(230, 40) * scale, false) then
      hud.environmentDraftWeather = item.type
    end
  end

  if pauseButton('APPLY TO MATCH', left + vec2(500, 500) * scale,
      vec2(230, 42) * scale, false) then
    hud.environmentRequestEvent({
      weatherType = hud.environmentDraftWeather,
      timeOfDaySeconds = hud.environmentDraftTimeSeconds,
    })
    ac.log(string.format('[ASRC FPS] environment request sent: weather=%s time=%s',
      tostring(hud.environmentDraftWeather), tostring(hud.environmentDraftTimeSeconds)))
  end
  if pauseButton('BACK TO MATCH MENU', left + vec2(0, 500) * scale,
      vec2(260, 42) * scale, false) then
    hud.pausePage = 'main'
    hud.environmentDraftReady = false
  end
end

function hud.drawPauseMenu()
  local size = ui.windowSize()
  local scale = math.clamp(math.min(size.x / 1920, size.y / 1080), 0.75, 1.5)
  local panelSize = vec2(900, 620) * scale
  local panelMin = (size - panelSize) * 0.5
  local panelMax = panelMin + panelSize
  local left = panelMin + vec2(42, 38) * scale
  local dividerX = panelMin.x + 350 * scale
  ui.captureMouse(true)
  ui.setMouseCursor(ui.MouseCursor.Arrow)
  ui.drawRectFilled(vec2(), size, rgbm(0.008, 0.012, 0.018, 0.7))
  ui.drawRectFilled(panelMin, panelMax, rgbm(0.025, 0.035, 0.05, 0.97), 10 * scale)
  ui.drawRect(panelMin, panelMax, rgbm(0.42, 0.62, 0.78, 0.7), 10 * scale, nil,
    math.max(1, 1.5 * scale))
  ui.drawLine(vec2(dividerX, panelMin.y + 28 * scale),
    vec2(dividerX, panelMax.y - 28 * scale), rgbm(0.35, 0.5, 0.62, 0.38),
    math.max(1, scale))

  local mouse = ui.mousePos()
  if not hud.pauseInputLogged then
    ac.log(string.format('[ASRC FPS] pause menu active: size=%.0fx%.0f mouse=(%.0f,%.0f)',
      size.x, size.y, mouse.x, mouse.y))
    hud.pauseInputLogged = true
  end
  local function pauseButton(label, position, dimensions, danger)
    local p2 = position + dimensions
    local hovered = mouse.x >= position.x and mouse.x <= p2.x
      and mouse.y >= position.y and mouse.y <= p2.y
    local held = hovered and ui.mouseDown(ui.MouseButton.Left)
    local base = danger and rgbm(0.42, 0.10, 0.08, 0.96) or rgbm(0.10, 0.18, 0.25, 0.96)
    local hot = danger and rgbm(0.72, 0.18, 0.12, 1) or rgbm(0.18, 0.38, 0.52, 1)
    ui.drawRectFilled(position, p2, hovered and hot or base, 5 * scale)
    ui.drawRect(position, p2, hovered and rgbm(0.58, 0.84, 1, 1)
      or rgbm(0.34, 0.52, 0.66, 0.75), 5 * scale, nil, math.max(1, 1.2 * scale))
    if held then
      ui.drawRectFilled(position, p2, rgbm(0, 0, 0, 0.18), 5 * scale)
    end
    ui.setCursor(position + vec2(16, 13) * scale)
    ui.text(label)
    return hovered and ui.mouseClicked(ui.MouseButton.Left)
  end

  if hud.pausePage == 'controls' then
    hud.drawControlsMenu(panelMin, panelSize, scale, pauseButton)
    return
  end
  if hud.pausePage == 'loadout' then
    hud.drawLoadoutMenu(false)
    return
  end
  if hud.pausePage == 'pure' then
    if not hud.environmentDraftReady then
      hud.environmentDraftWeather = hud.environmentWeather
      hud.environmentDraftTimeSeconds = hud.environmentTimeSeconds
      hud.environmentDraftReady = true
    end
    hud.drawEnvironmentMenu(panelMin, panelSize, scale, pauseButton)
    return
  end

  ui.setCursor(left)
  ui.pushFont(ui.Font.Huge)
  ui.text('MATCH MENU')
  ui.popFont()
  ui.setCursor(left + vec2(0, 58) * scale)
  ui.textColored('DEATHMATCH  •  LIVE SERVER', rgbm(0.46, 0.78, 0.95, 1))
  ui.setCursor(left + vec2(0, 92) * scale)
  ui.textWrapped('The match continues on the server while this menu is open.')

  local buttonSize = vec2(260, 46) * scale
  if pauseButton('RETURN TO MATCH', left + vec2(0, 150) * scale, buttonSize, false) then
    ac.log('[ASRC FPS] pause menu action: return to match')
    ac.tryToPause(false)
  end
  if pauseButton('FPS CONTROLS', left + vec2(0, 205) * scale, buttonSize, false) then
    ac.log('[ASRC FPS] pause menu action: controls')
    hud.pausePage = 'controls'
    hud.leaveServerArmed = false
    hud.controlsContentLogged = false
  end
  if pauseButton('LOADOUT', left + vec2(0, 260) * scale, buttonSize, false) then
    ac.log('[ASRC FPS] pause menu action: loadout')
    hud.loadout.result = 'CHANGES APPLY ON NEXT RESPAWN'
    hud.pausePage = 'loadout'
    hud.leaveServerArmed = false
  end
  if pauseButton('TIME & WEATHER', left + vec2(0, 315) * scale,
      buttonSize, false) then
    ac.log('[ASRC FPS] pause menu action: authoritative time and weather')
    hud.environmentDraftReady = false
    hud.pausePage = 'pure'
    hud.leaveServerArmed = false
  end
  if pauseButton('ASSETTO CORSA OPTIONS', left + vec2(0, 370) * scale,
      buttonSize, false) then
    ac.log('[ASRC FPS] pause menu action: native options')
    hud.nativePauseMenu = true
    hud.leaveServerArmed = false
  end
  if not hud.leaveServerArmed then
    if pauseButton('LEAVE SERVER', left + vec2(0, 425) * scale, buttonSize, true) then
      ac.log('[ASRC FPS] pause menu action: arm leave confirmation')
      hud.leaveServerArmed = true
    end
  else
    ui.setCursor(left + vec2(0, 425) * scale)
    ui.textColored('Leave the current server?', rgbm(1, 0.52, 0.35, 1))
    if pauseButton('CONFIRM LEAVE', left + vec2(0, 457) * scale,
        vec2(162, 42) * scale, true) then
      ac.log('[ASRC FPS] pause menu action: leave server confirmed')
      ac.shutdownAssettoCorsa()
    end
    if pauseButton('CANCEL', left + vec2(170, 457) * scale,
        vec2(90, 42) * scale, false) then
      ac.log('[ASRC FPS] pause menu action: leave cancelled')
      hud.leaveServerArmed = false
    end
  end
  ui.setCursor(left + vec2(0, 518) * scale)
  ui.textColored('Match environment changes are synchronized by the server.',
    rgbm(0.62, 0.7, 0.78, 1))

  local ranking = {}
  for _, actor in pairs(actors) do
    if bit.band(actor.flags, 1) ~= 0 then ranking[#ranking + 1] = actor end
  end
  table.sort(ranking, function(a, b)
    if a.kills ~= b.kills then return a.kills > b.kills end
    if a.deaths ~= b.deaths then return a.deaths < b.deaths end
    return a.id < b.id
  end)
  local right = vec2(dividerX + 34 * scale, panelMin.y + 42 * scale)
  ui.setCursor(right)
  ui.pushFont(ui.Font.Title)
  ui.text('CURRENT MATCH')
  ui.popFont()
  ui.setCursor(right + vec2(0, 40) * scale)
  ui.text(string.format('%02d:%02d remaining  •  target %d kills',
    math.floor(remainingSeconds / 60), math.floor(remainingSeconds % 60), killLimit))
  ui.setCursor(right + vec2(0, 78) * scale)
  ui.textColored('POS   OPERATIVE                 K     D    HP', rgbm(0.55, 0.7, 0.8, 1))
  for place = 1, math.min(10, #ranking) do
    local actor = ranking[place]
    ui.setCursor(right + vec2(0, 80 + place * 31) * scale)
    local marker = actor.id == localSessionID and '>' or ' '
    ui.text(string.format('%s%2d   %-22s %3d   %3d   %3d', marker, place,
      names[actor.id] or ('Operative ' .. actor.id), actor.kills, actor.deaths, actor.health))
  end
end

function hud.exclusiveCallback(mode)
  if mode ~= 'pause' then
    hud.nativePauseMenu = false
    hud.leaveServerArmed = false
    hud.pauseInputLogged = false
    hud.pausePage = 'main'
    hud.bindingCapture = nil
    hud.environmentDraftReady = false
  end
  if mode == 'pause' and previewCamera.everEnteredGameplay then
    if hud.nativePauseMenu then return false end
    hud.drawPauseMenu()
    return true
  end
  if mode ~= 'game' or not gameplayActive then return false end
  if hud.appOwnsHud() and hud.loadout.confirmed then return false end
  hud.drawingFallback = true
  script.drawUI()
  hud.drawingFallback = false
  return true
end

hud.exclusiveOk, hud.exclusiveSubscription = pcall(function()
  return ui.onExclusiveHUD(hud.exclusiveCallback, true)
end)
if not hud.exclusiveOk then
  ac.warn('[ASRC FPS] exclusive HUD unavailable; using script.drawUI fallback: '
    .. tostring(hud.exclusiveSubscription))
  hud.exclusiveSubscription = nil
else
  ac.log('[ASRC FPS] exclusive online HUD fallback registered')
end

hud.readySent = hud.readyEvent({ protocol = 2 })
ac.log(string.format('[ASRC FPS] ready sent: protocol=2 result=%s', tostring(hud.readySent)))
