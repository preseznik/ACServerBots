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
  startCountdownSeconds = 0,
  restartCountdownSeconds = 0,
  restartCountdownUpdatedAt = 0,
  thirdPersonDistance = 3.2,
  thirdPersonDistanceTarget = 3.2,
  thirdPersonDistanceMin = 1.25,
  thirdPersonDistanceMax = 7.0,
  thirdPersonZoomStep = 0.4,
  modernAssetRevision = 12,
  operatorAnimations = {},
  standingClipOrder = { 'aim_idle', 'aim_up', 'aim_down', 'walk_forward',
    'jog_forward', 'jog_forward_right', 'strafe_right', 'jog_backward_right',
    'walk_backward', 'jog_backward_left', 'strafe_left', 'jog_forward_left', 'sprint' },
  joggingDirections = { 'jog_forward', 'jog_forward_right', 'strafe_right',
    'jog_backward_right', 'walk_backward', 'jog_backward_left', 'strafe_left', 'jog_forward_left' },
  actorModels = {},
  actorSkins = {},
  operatorModels = {
    [0] = { id = 0, name = 'OFFICER', file = 'asrc_modern_operator_carbine.kn5',
      portrait = 'asrc_operator_officer.png', materialPrefix = 'ASRC_OFFICER_',
      skins = {
        [0] = { name = 'STANDARD ISSUE', portrait = 'asrc_operator_officer.png' },
        [1] = { name = 'BLUE-GREY', portrait = 'asrc_operator_officer_bluegrey.png',
          uniform = 'asrc_modern_team2_uniform.png', gear = 'asrc_modern_team2_gear.png' },
      },
      stanceOffsets = { [1] = -0.50, [2] = -0.50 } },
    [1] = { id = 1, name = 'GHOST', file = 'asrc_modern_ghost_carbine.kn5',
      portrait = 'asrc_operator_ghost.png', materialPrefix = 'ASRC_GHOST_',
      skins = {
        [0] = { name = 'NIGHTWAR', portrait = 'asrc_operator_ghost.png' },
        [1] = { name = 'BLUE-GREY', portrait = 'asrc_operator_ghost_bluegrey.png',
          uniform = 'asrc_modern_ghost_team2_uniform.png', gear = 'asrc_modern_ghost_team2_gear.png' },
        [2] = { name = 'DESERT TAN', portrait = 'asrc_operator_ghost_desert.png',
          uniform = 'asrc_modern_ghost_desert_uniform.png', gear = 'asrc_modern_ghost_desert_gear.png' },
      },
      stanceOffsets = { [1] = -0.50, [2] = -0.50 } },
  },
  crouchSuppressedUntilRelease = false,
  crouchToggleReleaseStands = false,
  carrierControlsOverride = nil,
  carrierControlsOverrideErrorLogged = false,
  viewmodelFireUntil = 0,
  viewmodelEquipUntil = 0,
  viewmodelPistolPoseSeedPending = false,
  activeGrenadeType = nil,
  grenadePrimeStarted = 0,
  grenadeReleasedAt = nil,
  grenadePrimeDuration = 0.32,
  grenadeHoldPhase = 0.30,
  grenadeReleasePhase = 0.39,
  grenadeReleasePoint = 0.28,
  grenadeReleaseDuration = 0.8,
  grenadeInputHeld = false,
  explosionLights = {},
  explosionLightUnavailable = false,
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
    archivePath = '/fps/assets/asrc-fps-assets-v22.zip',
    fileName = 'asrc_carbine_hud.png',
    imagePath = nil,
    loading = false,
    failed = false,
  },
  loadoutAssetArchivePath = '/fps/assets/asrc-fps-assets-v22.zip',
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
  fragGrenadeViewmodelFileName = 'asrc_frag_grenade_viewmodel.kn5',
  fragGrenadeWorldModelFileName = 'asrc_frag_grenade_world.kn5',
  fragGrenadeClips = { throw = 'asrc_frag_grenade_throw.ksanim' },
  stickyGrenadeViewmodelFileName = 'asrc_sticky_grenade_viewmodel.kn5',
  stickyGrenadeWorldModelFileName = 'asrc_sticky_grenade_world.kn5',
  stickyGrenadeClips = { throw = 'asrc_sticky_grenade_throw.ksanim' },
  loadedViewmodelAsset = nil,
  pickups = {},
  operatorClips = {
    aim_idle = 'asrc_modern_operator_aim_idle.ksanim',
    aim_up = 'asrc_modern_operator_aim_up.ksanim',
    aim_down = 'asrc_modern_operator_aim_down.ksanim',
    walk_forward = 'asrc_modern_operator_walk_forward.ksanim',
    jog_forward = 'asrc_modern_operator_jog_forward.ksanim',
    jog_forward_left = 'asrc_modern_operator_jog_forward_left.ksanim',
    jog_forward_right = 'asrc_modern_operator_jog_forward_right.ksanim',
    jog_backward_left = 'asrc_modern_operator_jog_backward_left.ksanim',
    jog_backward_right = 'asrc_modern_operator_jog_backward_right.ksanim',
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
local matchType = 0
local winnerTeam = 0
local team1Kills = 0
local team2Kills = 0
local teams = {}
local matchTypeLabels = {
  [0] = 'FFA', [1] = 'TDM', [2] = 'HARDCORE FFA', [3] = 'HARDCORE TDM',
}
local function isTeamMatch()
  return matchType == 1 or matchType == 3
end
local function matchLabel()
  return matchTypeLabels[matchType] or 'FFA'
end
function fpsVisual.matchModeTitle()
  if matchType == 1 then return 'TEAM DEATH MATCH' end
  if matchType == 2 then return 'HARDCORE FREE FOR ALL' end
  if matchType == 3 then return 'HARDCORE TEAM DEATH MATCH' end
  return 'FREE FOR ALL'
end
local function matchTargetText()
  if isTeamMatch() then
    return string.format('T1 %d  -  %d T2   TARGET %d', team1Kills, team2Kills, killLimit)
  end
  return string.format('TARGET %d', killLimit)
end
local outOfBoundsRemaining = 0
local killFeed = {}
local effectClock = 0
local hitMarkerUntil = 0
local tracers = {}
local impacts = {}
local clearActorImpacts = nil
local sparks = {}
local maxTracers = 16
local maxImpactMarks = 96
local fpsAudio = {
  directory = 'extension/audio/asrc_fps/',
  active = {},
  maxActive = 64,
  variants = {},
  fallbackLogged = {},
  playbackFailureLogged = {},
  playbackReadyLogged = false,
  relayReadyLogged = false,
  relayUnavailableLogged = false,
  catalogUnavailableLogged = false,
  fire = {
    [1] = {'fire_assault_rifle_01.wav', 'fire_assault_rifle_02.wav',
      'fire_assault_rifle_03.wav'},
    [2] = {'fire_compact_smg_01.wav', 'fire_compact_smg_02.wav',
      'fire_compact_smg_03.wav'},
    [3] = {'fire_desert_eagle_01.wav', 'fire_desert_eagle_02.wav',
      'fire_desert_eagle_03.wav'},
    [4] = {'fire_colt_1911_01.wav', 'fire_colt_1911_02.wav',
      'fire_colt_1911_03.wav'},
  },
  reload = {
    [1] = 'reload_assault_rifle.wav', [2] = 'reload_compact_smg.wav',
    [3] = 'reload_desert_eagle.wav', [4] = 'reload_colt_1911.wav',
  },
  footsteps = {'footstep_boot_01.wav', 'footstep_boot_02.wav',
    'footstep_boot_03.wav', 'footstep_boot_04.wav', 'footstep_boot_05.wav',
    'footstep_boot_06.wav'},
  crawl = {'crawl_gear_01.wav', 'crawl_gear_02.wav'},
  jump = {'jump_grunt_01.wav', 'jump_grunt_02.wav'},
  landLight = {'land_light_01.wav', 'land_light_02.wav'},
  landHeavy = {'land_heavy_01.wav', 'land_heavy_02.wav'},
  traversal = {'traversal_grunt_01.wav', 'traversal_grunt_02.wav'},
  hurt = {'hurt_grunt_01.wav', 'hurt_grunt_02.wav', 'hurt_grunt_03.wav'},
  death = {'death_cry_01.wav', 'death_cry_02.wav', 'death_cry_03.wav',
    'death_cry_04.wav'},
  impactHard = {'impact_hard_01.wav', 'impact_hard_02.wav', 'impact_hard_03.wav'},
  impactBody = {'impact_body_01.wav', 'impact_body_02.wav'},
  fragExplosion = {'grenade_frag_explosion_01.wav', 'grenade_frag_explosion_02.wav'},
  stickyExplosion = {'grenade_sticky_explosion_01.wav',
    'grenade_sticky_explosion_02.wav'},
}
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
local rifleAssetArchivePath = '/fps/assets/asrc-fps-assets-v22.zip'
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
  rifleAssetArchivePath = '/fps/assets/asrc-fps-modern-v12.zip'
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

function fpsAudio.next(files, key)
  local index = (fpsAudio.variants[key] or 0) + 1
  if index > #files then index = 1 end
  fpsAudio.variants[key] = index
  return files[index]
end

function fpsAudio.dispose(index)
  local sound = fpsAudio.active[index]
  if sound == nil then return end
  pcall(function() sound.event:dispose() end)
  table.remove(fpsAudio.active, index)
end

function fpsAudio.logPlaybackFailure(fileName, stage, detail)
  local key = tostring(fileName) .. ':' .. tostring(stage)
  if fpsAudio.playbackFailureLogged[key] then return end
  fpsAudio.playbackFailureLogged[key] = true
  ac.warn('[ASRC FPS] audio playback failed: file=' .. tostring(fileName)
    .. ' stage=' .. tostring(stage) .. ' detail=' .. tostring(detail))
end

function fpsAudio.startEvent(event, fileName, position, localSound, volume, ttl)
  while #fpsAudio.active >= fpsAudio.maxActive do fpsAudio.dispose(1) end
  fpsAudio.active[#fpsAudio.active + 1] = {
    event = event,
    ttl = ttl,
    fileName = fileName,
    validityChecked = false,
  }
  local index = #fpsAudio.active
  local ok, err = pcall(function()
    event.volume = volume
    -- The FPS camera can still be classified as an AC interior camera. File-backed
    -- cues should not inherit the default 0.25 interior multiplier.
    event.cameraInteriorMultiplier = 1
    event.cameraExteriorMultiplier = 1
    event.cameraTrackMultiplier = 1
    if not localSound and position ~= nil then event:setPosition(position) end
    event:start()
  end)
  if not ok then
    fpsAudio.dispose(index)
    fpsAudio.logPlaybackFailure(fileName, 'start', err)
    return false
  end
  return true
end

function fpsAudio.play(fileName, position, localSound, volume, maxDistance, ttl, fallback)
  local relayOk, listeners = pcall(function()
    return ac.broadcastSharedEvent('asrc.fps.audio.v1', {
      version = 1,
      fileName = fileName,
      localSound = localSound,
      volume = volume,
      maxDistance = maxDistance,
      ttl = ttl,
      hasPosition = position ~= nil,
      position = position or vec3(),
    })
  end)
  if relayOk and (tonumber(listeners) or 0) > 0 then
    if not fpsAudio.relayReadyLogged then
      fpsAudio.relayReadyLogged = true
      ac.log('[ASRC FPS] local audio relay connected')
    end
    return
  end
  if not fpsAudio.relayUnavailableLogged then
    fpsAudio.relayUnavailableLogged = true
    ac.warn('[ASRC FPS] local audio relay unavailable: '
      .. tostring(relayOk and listeners or listeners))
  end
  if fallback ~= nil then
    local fallbackOk, fallbackEvent = pcall(function()
      return ac.AudioEvent(fallback, false, false)
    end)
    if fallbackOk and fallbackEvent ~= nil then
      fpsAudio.startEvent(fallbackEvent, fallback, position, localSound, volume, ttl)
    else
      fpsAudio.logPlaybackFailure(fallback, 'fallback-create', fallbackEvent)
    end
    if not fpsAudio.fallbackLogged[fallback] then
      fpsAudio.fallbackLogged[fallback] = true
      ac.warn('[ASRC FPS] local audio relay unavailable; using one-shot fallback: '
        .. tostring(fileName))
    end
  elseif not fpsAudio.catalogUnavailableLogged then
    fpsAudio.catalogUnavailableLogged = true
    ac.warn('[ASRC FPS] local audio relay unavailable; optional cues are muted')
  end
end

function fpsAudio.actorPosition(actor)
  if actor == nil then return nil end
  return actor.render:lengthSquared() > 0.001 and actor.render or actor.target
end

function fpsAudio.activeWeapon(actor)
  if actor == nil then return 1 end
  return actor.activeSlot == 1 and (actor.secondaryWeapon or 4)
    or (actor.mainWeapon or 1)
end

function fpsAudio.playWeaponFire(weaponType, position, localShot)
  local variants = fpsAudio.fire[weaponType] or fpsAudio.fire[1]
  local maxDistance = weaponType == 2 and 70 or weaponType == 4 and 60
    or weaponType == 3 and 100 or 90
  fpsAudio.play(fpsAudio.next(variants, 'fire-' .. tostring(weaponType)), position,
    localShot, localShot and 0.85 or 0.72, maxDistance, 0.9,
    'cars/:own/backfire_ext')
end

function fpsAudio.playImpact(impactType, position)
  local variants = impactType == 2 and fpsAudio.impactBody or fpsAudio.impactHard
  fpsAudio.play(fpsAudio.next(variants, 'impact-' .. tostring(impactType)), position,
    false, 0.50, 25, 0.7)
end

function fpsAudio.playGrenadeExplosion(grenadeType, position)
  local variants = grenadeType == 17 and fpsAudio.stickyExplosion or fpsAudio.fragExplosion
  fpsAudio.play(fpsAudio.next(variants, 'explosion-' .. tostring(grenadeType)), position,
    false, 1.0, 120, 1.4, 'event:/collisions/car/metal')
end

function fpsAudio.playHurt(actor)
  if actor == nil or effectClock - (actor.audioHurtAt or -10) < 0.3 then return end
  actor.audioHurtAt = effectClock
  fpsAudio.play(fpsAudio.next(fpsAudio.hurt, 'hurt'), fpsAudio.actorPosition(actor),
    actor.id == localSessionID, 0.60, 24, 1.0)
end

function fpsAudio.resetActor(actor, newLife)
  if actor == nil then return end
  actor.audioInitialized = false
  actor.audioLastPosition = nil
  actor.audioStepClock = 0
  actor.audioAirborneStarted = nil
  actor.audioAirbornePeak = nil
  actor.audioHurtAt = nil
  if newLife then actor.audioDeathSpawnCount = nil end
end

function fpsAudio.playDeath(actor)
  if actor == nil then return end
  local spawnCount = actor.spawnCount or -1
  if actor.audioDeathSpawnCount == spawnCount then return end
  actor.audioDeathSpawnCount = spawnCount
  actor.audioStepClock = 0
  actor.audioAirborneStarted = nil
  actor.audioAirbornePeak = nil
  fpsAudio.play(fpsAudio.next(fpsAudio.death, 'death'), fpsAudio.actorPosition(actor),
    actor.id == localSessionID, 0.60, 24, 1.5)
end

function fpsAudio.playEquip(actor)
  local weapon = fpsAudio.activeWeapon(actor)
  fpsAudio.play(weapon <= 2 and 'equip_long_gun.wav' or 'equip_pistol.wav',
    fpsAudio.actorPosition(actor), actor.id == localSessionID, 0.55, 18, 0.7)
end

function fpsAudio.playReload(actor)
  local weapon = fpsAudio.activeWeapon(actor)
  fpsAudio.play(fpsAudio.reload[weapon] or fpsAudio.reload[1],
    fpsAudio.actorPosition(actor), actor.id == localSessionID, 0.55, 18, 1.2)
end

function fpsAudio.snapshotTransition(actor, previousFlags, previousSpawnCount,
    previousReload, previousActionState, previousY)
  local spawnChanged = previousSpawnCount ~= nil and previousSpawnCount ~= actor.spawnCount
  local active = bit.band(actor.flags, 1) ~= 0
  local dead = bit.band(actor.flags, 2) ~= 0
  if not actor.audioInitialized or spawnChanged then
    fpsAudio.resetActor(actor, true)
    actor.audioInitialized = true
    actor.audioLastPosition = actor.target:clone()
    return
  end
  if not active or dead then
    actor.audioStepClock = 0
    actor.audioAirborneStarted = nil
    actor.audioAirbornePeak = nil
    actor.audioHurtAt = nil
    actor.audioLastPosition = actor.target:clone()
    return
  end
  local wasGrounded = bit.band(previousFlags, 16) ~= 0
  local grounded = bit.band(actor.flags, 16) ~= 0
  if wasGrounded and not grounded then
    actor.audioAirborneStarted = effectClock
    actor.audioAirbornePeak = actor.target.y
    if bit.band(actor.actionState or 0, 1) == 0 and actor.target.y >= previousY then
      fpsAudio.play(fpsAudio.next(fpsAudio.jump, 'jump'), fpsAudio.actorPosition(actor),
        actor.id == localSessionID, 0.60, 20, 0.8)
    end
  elseif not grounded then
    actor.audioAirbornePeak = math.max(actor.audioAirbornePeak or actor.target.y, actor.target.y)
  elseif not wasGrounded then
    local airborneSeconds = effectClock - (actor.audioAirborneStarted or effectClock)
    local fallDistance = (actor.audioAirbornePeak or actor.target.y) - actor.target.y
    local variants = fallDistance > 1.5 or airborneSeconds > 0.65
      and fpsAudio.landHeavy or fpsAudio.landLight
    fpsAudio.play(fpsAudio.next(variants, variants == fpsAudio.landHeavy and 'land-heavy'
      or 'land-light'), fpsAudio.actorPosition(actor), actor.id == localSessionID,
      0.60, 18, 0.9)
    actor.audioAirborneStarted = nil
    actor.audioAirbornePeak = nil
  end
  if bit.band(previousActionState or 0, 1) == 0
      and bit.band(actor.actionState or 0, 1) ~= 0 then
    fpsAudio.play(fpsAudio.next(fpsAudio.traversal, 'traversal'),
      fpsAudio.actorPosition(actor), actor.id == localSessionID, 0.60, 20, 0.9)
  end
  if (previousReload or 0) <= 0 and (actor.reloadRemaining or 0) > 0 then
    fpsAudio.playReload(actor)
  end
end

function fpsAudio.update(dt)
  for index = #fpsAudio.active, 1, -1 do
    local sound = fpsAudio.active[index]
    if not sound.validityChecked then
      sound.validityChecked = true
      local ok, valid = pcall(function() return sound.event:isValid() end)
      if not ok or not valid then
        fpsAudio.logPlaybackFailure(sound.fileName, 'post-start-validity',
          ok and 'invalid event' or valid)
      elseif not fpsAudio.playbackReadyLogged then
        fpsAudio.playbackReadyLogged = true
        ac.log('[ASRC FPS] online fallback audio valid: ' .. tostring(sound.fileName))
      end
    end
    sound.ttl = sound.ttl - dt
    if sound.ttl <= 0 then fpsAudio.dispose(index) end
  end
  if not gameplayActive then return end
  for _, actor in pairs(actors) do
    local position = fpsAudio.actorPosition(actor)
    if position ~= nil then
      if actor.audioLastPosition == nil then actor.audioLastPosition = position:clone() end
      local delta = position - actor.audioLastPosition
      local speed = math.sqrt(delta.x * delta.x + delta.z * delta.z) / math.max(dt, 0.001)
      local moving = bit.band(actor.flags, 1) ~= 0 and bit.band(actor.flags, 2) == 0
        and bit.band(actor.flags, 16) ~= 0 and speed > 0.35
      local prone = bit.band(actor.flags, 128) ~= 0
      if moving and not prone then
        local crouching = bit.band(actor.flags, 32) ~= 0
        local sprinting = not crouching
          and ((actor.id == localSessionID and viewmodelSprint) or speed > 7.2)
        local interval = crouching and 0.48 or sprinting and 0.28 or 0.40
        actor.audioStepClock = (actor.audioStepClock or 0) + dt
        if actor.audioStepClock >= interval then
          actor.audioStepClock = actor.audioStepClock - interval
          local baseVolume = actor.id == localSessionID and 0.16 or 0.45
          local volume = crouching and baseVolume * 0.5
            or sprinting and baseVolume * 1.2 or baseVolume
          fpsAudio.play(fpsAudio.next(fpsAudio.footsteps, 'footstep'), position,
            actor.id == localSessionID, volume, 14, 0.75)
        end
      else
        actor.audioStepClock = 0
      end
      actor.audioLastPosition:set(position)
    end
  end
end

function fpsAudio.resetAll()
  for index = #fpsAudio.active, 1, -1 do fpsAudio.dispose(index) end
  for _, actor in pairs(actors) do fpsAudio.resetActor(actor, true) end
  fpsAudio.variants = {}
end

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
local impactSparks = nil
local impactSmoke = nil
local hud = {
  protocol = 14,
  capacity = 32,
  grenadeCapacity = 8,
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
  nativePreDriveMenu = false,
  preDrivePage = 'briefing',
  preDriveLeaveArmed = false,
  preDriveDeployPending = false,
  preDriveReturnAfterLoadout = false,
  preDriveStatus = '',
  preDriveGamepadAWasDown = false,
  preDriveGamepadBWasDown = false,
  preDriveGamepadXWasDown = false,
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
  pickupPromptText = '',
  pickupProgress = 0,
  loadout = {
    catalogReceived = false,
    confirmed = false,
    dirty = false,
    result = 'WAITING FOR SERVER CATALOG',
    allowedMainWeapons = 0,
    allowedLethals = 0,
    allowedSecondaryWeapons = 0,
    mainWeapon = 1,
    lethal = 16,
    secondaryWeapon = 4,
    operatorModel = 0,
    operatorSkin = 0,
    allowedOfficerSkins = 1,
    allowedGhostSkins = 1,
    allowedOperatorModels = 1,
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
  interact = ac.KeyIndex.F,
  melee = ac.KeyIndex.V,
}
hud.bindings = ac.storage({
  fire = hud.bindingDefaults.fire,
  sprint = hud.bindingDefaults.sprint,
  crouch = hud.bindingDefaults.crouch,
  reload = hud.bindingDefaults.reload,
  jump = hud.bindingDefaults.jump,
  grenade = hud.bindingDefaults.grenade,
  interact = hud.bindingDefaults.interact,
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
  operatorModel = 0,
  operatorSkin = 0,
}, 'asrc.fps.loadout.')
hud.itemNames = {
  [0] = 'OUT OF BOUNDS',
  [1] = 'ASSAULT RIFLE', [2] = 'MP5 SMG',
  [3] = 'DESERT EAGLE', [4] = 'COLT 1911',
  [16] = 'FRAG GRENADE', [17] = 'STICKY GRENADE',
}

-- Category and image metadata live with the presentation catalog. Adding another
-- item to a slot automatically makes its row scrollable and enables its category.
hud.loadoutItems = {
  { id = 1, slot = 'mainWeapon', category = 'rifle', subtitle = 'Assault Rifle', image = 'asrc_loadout_assault_rifle.png' },
  { id = 2, slot = 'mainWeapon', category = 'smg', subtitle = 'SMG', image = 'asrc_loadout_compact_smg.png' },
  { id = 3, slot = 'secondaryWeapon', category = 'pistol', subtitle = 'Pistol', image = 'asrc_loadout_desert_eagle.png' },
  { id = 4, slot = 'secondaryWeapon', category = 'pistol', subtitle = 'Pistol', image = 'asrc_loadout_colt_1911.png' },
  { id = 16, slot = 'lethal', category = 'grenade', subtitle = 'M67', image = 'asrc_loadout_frag_grenade.png' },
  { id = 17, slot = 'lethal', category = 'grenade', subtitle = 'Semtex', image = 'asrc_loadout_sticky_grenade.png' },
}
hud.loadoutRows = {
  mainWeapon = { filter = 'all', first = 1, mask = 'allowedMainWeapons', categories = {
    { 'all', 'All', 62 }, { 'rifle', 'Assault Rifles', 154 }, { 'smg', 'SMGs', 94 },
    { 'lmg', 'LMG', 92 }, { 'sniper', 'Sniper', 108 }, { 'shotgun', 'Shotgun', 116 },
  } },
  secondaryWeapon = { filter = 'pistol', first = 1, mask = 'allowedSecondaryWeapons', categories = {
    { 'pistol', 'Pistol', 92 }, { 'launcher', 'Launcher', 140 }, { 'special', 'Special', 116 },
  } },
  lethal = { filter = 'all', first = 1, mask = 'allowedLethals', categories = {} },
}

function hud.loadoutRowItems(field, filter)
  local items = {}
  for _, item in ipairs(hud.loadoutItems) do
    if item.slot == field and (filter == 'all' or filter == item.category) then
      items[#items + 1] = item
    end
  end
  return items
end

function hud.setLoadoutFilter(field, filter)
  local items = hud.loadoutRowItems(field, filter)
  if #items == 0 then return end
  local row = hud.loadoutRows[field]
  row.filter = filter
  row.first = 1
  for index, item in ipairs(items) do
    if item.id == hud.loadout[field] then row.first = math.max(1, index - 1); break end
  end
end

function hud.scrollLoadoutRow(field, direction)
  local row = hud.loadoutRows[field]
  local count = #hud.loadoutRowItems(field, row.filter)
  row.first = math.clamp(row.first + direction, 1, math.max(1, count - 1))
end

function hud.itemAllowed(mask, itemID)
  return bit.band(mask, bit.lshift(1, itemID)) ~= 0
end

function hud.skinAllowed(model, skin)
  local descriptor = fpsVisual.operatorModels[model]
  local mask = model == 1 and hud.loadout.allowedGhostSkins or hud.loadout.allowedOfficerSkins
  return descriptor ~= nil and descriptor.skins[skin] ~= nil and hud.itemAllowed(mask, skin)
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
  fpsVisual.explosionSparks = ac.Particles.Sparks({
    color = rgbm(1, 0.52, 0.12, 1), life = 0.85, size = 0.055,
    directionSpread = 1.0, positionSpread = 0.12,
  })
  fpsVisual.explosionSmoke = ac.Particles.Smoke({
    color = rgbm(0.12, 0.10, 0.08, 0.92), colorConsistency = 0.55,
    thickness = 1.15, life = 2.4, size = 0.42, spreadK = 1.25,
    growK = 1.4, targetYVelocity = 1.7,
  })
  fpsVisual.explosionFlame = ac.Particles.Flame({
    color = rgbm(1, 0.34, 0.04, 1), size = 0.36,
    temperatureMultiplier = 1.25, flameIntensity = 1.35,
  })
end)

function hud.connect()
  local ok, result = pcall(function()
    return ac.connect({
      ac.StructItem.key('asrc.fps.hud.v14'),
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
      startCountdownSeconds = ac.StructItem.float(),
      restartCountdownSeconds = ac.StructItem.float(),
      killLimit = ac.StructItem.uint16(),
      winnerID = ac.StructItem.byte(),
      matchType = ac.StructItem.byte(),
      winnerTeam = ac.StructItem.byte(),
      team1Kills = ac.StructItem.uint16(),
      team2Kills = ac.StructItem.uint16(),
      outOfBoundsRemaining = ac.StructItem.float(),
      scoreboardHeld = ac.StructItem.byte(),
      cursorUnlocked = ac.StructItem.byte(),
      persistentCursor = ac.StructItem.byte(),
      appPersistentCursor = ac.StructItem.byte(),
      hitMarkerRemaining = ac.StructItem.float(),
      adsActive = ac.StructItem.byte(),
      aimTargetID = ac.StructItem.byte(),
      aimTargetPosition = ac.StructItem.vec3(),
      aimTargetUpdatedAt = ac.StructItem.float(),
      linkState = ac.StructItem.byte(),
      clientError = ac.StructItem.string(128),
      pickupPrompt = ac.StructItem.string(72),
      pickupProgress = ac.StructItem.float(),
      actorCount = ac.StructItem.byte(),
      actorIDs = ac.StructItem.array(ac.StructItem.byte(), hud.capacity),
      actorFlags = ac.StructItem.array(ac.StructItem.byte(), hud.capacity),
      actorTeams = ac.StructItem.array(ac.StructItem.byte(), hud.capacity),
      radarFlags = ac.StructItem.array(ac.StructItem.byte(), hud.capacity),
      actorPositions = ac.StructItem.array(ac.StructItem.vec3(), hud.capacity),
      actorYaws = ac.StructItem.array(ac.StructItem.float(), hud.capacity),
      actorHealth = ac.StructItem.array(ac.StructItem.uint16(), hud.capacity),
      actorKills = ac.StructItem.array(ac.StructItem.uint16(), hud.capacity),
      actorDeaths = ac.StructItem.array(ac.StructItem.uint16(), hud.capacity),
      actorScores = ac.StructItem.array(ac.StructItem.uint32(), hud.capacity),
      actorNames = ac.StructItem.array(ac.StructItem.string(32), hud.capacity),
      grenadeThreatCount = ac.StructItem.byte(),
      grenadeThreatPositions = ac.StructItem.array(ac.StructItem.vec3(), hud.grenadeCapacity),
      grenadeThreatVelocities = ac.StructItem.array(ac.StructItem.vec3(), hud.grenadeCapacity),
      grenadeThreatRemaining = ac.StructItem.array(ac.StructItem.float(), hud.grenadeCapacity),
      grenadeThreatUpdateTime = ac.StructItem.float(),
      killFeedCount = ac.StructItem.byte(),
      killFeed = ac.StructItem.array(ac.StructItem.string(72), hud.killFeedCapacity),
      awardPopupCount = ac.StructItem.byte(),
      awardPopupTexts = ac.StructItem.array(ac.StructItem.string(64), hud.awardPopupCapacity),
      awardPopupAlphas = ac.StructItem.array(ac.StructItem.float(), hud.awardPopupCapacity),
    }, false, ac.SharedNamespace.Shared)
  end)
  if ok then
    hud.bridge = result
    ac.log('[ASRC FPS] HUD bridge ready: asrc.fps.hud.v14')
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
        and bit.band(actor.flags, 2) == 0 then
      local teammate = isTeamMatch() and localActor.team ~= 0 and actor.team == localActor.team
      if teammate then
        -- Friendlies are persistent navigation information and clamp to the radar rim
        -- when they are beyond its 40 m scale. Enemies retain the existing reveal rules.
        hud.radarVisible[id] = 3
      elseif bit.band(actor.flags, 8) == 0
          and (actor.target - localActor.target):lengthSquared() <= 40 * 40 then
        local shotReveal = (hud.radarReveal[id] or 0) > effectClock
        if shotReveal or hud.hasRadarLineOfSight(localActor, actor) then
          hud.radarVisible[id] = shotReveal and 2 or 1
        end
      end
    end
  end
end

-- Match the server's vertical actor capsule, using the interpolated visual position
-- so the reticle selects the player the client actually sees. This is HUD-only.
function hud.aimCapsuleDistance(origin, direction, position, height)
  local radius = 0.42
  local inset = math.min(radius, height * 0.5)
  local bottom, top = position.y + inset, position.y + height - inset
  local best = math.huge
  local ox, oz = origin.x - position.x, origin.z - position.z
  local a = direction.x * direction.x + direction.z * direction.z
  if a > 1e-8 then
    local b = 2 * (ox * direction.x + oz * direction.z)
    local c = ox * ox + oz * oz - radius * radius
    local d = b * b - 4 * a * c
    if d >= 0 then
      local root = math.sqrt(d)
      for sign = -1, 1, 2 do
        local distance = (-b + sign * root) / (2 * a)
        local y = origin.y + direction.y * distance
        if distance >= 0 and y >= bottom and y <= top then best = math.min(best, distance) end
      end
    end
  end
  for cap = 0, 1 do
    local oy = origin.y - (cap == 0 and bottom or top)
    local b = ox * direction.x + oy * direction.y + oz * direction.z
    local c = ox * ox + oy * oy + oz * oz - radius * radius
    local d = b * b - c
    if c <= 0 then best = 0
    elseif d >= 0 then
      local distance = -b - math.sqrt(d)
      if distance >= 0 then best = math.min(best, distance) end
    end
  end
  return best
end

function hud.updateAimTarget()
  hud.aimTarget = nil
  local own = actors[localSessionID]
  if not gameplayActive or matchState ~= 1 or not hud.loadout.confirmed
      or cursorUnlocked or scoreboardHeld or thirdPersonEnabled
      or fpsVisual.activeGrenadeType ~= nil or viewmodelSprint
      or own == nil or own.health <= 0 or own.reloadRemaining > 0
      or bit.band(own.flags, 3) ~= 1 or camera == nil or not camera:active() then return end
  local origin, direction = camera.transform.position, camera.transform.look
  local nearest, nearestHeight, nearestDistance = nil, 0, 120
  for id, actor in pairs(actors) do
    if id ~= localSessionID and actor.health > 0 and bit.band(actor.flags, 3) == 1 then
      local stance = fpsVisual.actorStance(actor)
      local height = stance == 2 and 0.65 or stance == 1 and 1.15 or 1.8
      local distance = hud.aimCapsuleDistance(origin, direction, actor.render, height)
      if distance < nearestDistance or (distance == nearestDistance
          and nearest ~= nil and id < nearest.id) then
        nearest, nearestHeight, nearestDistance = actor, height, distance
      end
    end
  end
  if nearest == nil then return end
  -- Only the closest actor can own the reticle. Never disclose a name through cover.
  local ok, obstruction = pcall(physics.raycastTrack, origin, direction,
    nearestDistance, nil, nil, false, false)
  if not ok or obstruction == nil or (obstruction >= 0 and obstruction < nearestDistance) then return end
  hud.aimTarget = nearest
  hud.aimTargetPosition = nearest.render + vec3(0, nearestHeight + 0.2, 0)
end

function hud.publishAimTarget(now)
  hud.bridge.aimTargetID = hud.aimTarget ~= nil and hud.aimTarget.id or 255
  if hud.aimTarget ~= nil then hud.bridge.aimTargetPosition = hud.aimTargetPosition end
  hud.bridge.aimTargetUpdatedAt = now
end

function hud.publish(dt)
  if hud.bridge == nil then return end
  local now = ui.time()
  hud.bridge.protocol = hud.protocol
  hud.bridge.onlineHeartbeat = now
  hud.bridge.gameplayActive = gameplayActive and 1 or 0
  hud.publishAimTarget(now)
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
  hud.bridge.startCountdownSeconds = fpsVisual.startCountdownSeconds
  hud.bridge.restartCountdownSeconds = math.max(0, fpsVisual.restartCountdownSeconds
    - (ui.time() - fpsVisual.restartCountdownUpdatedAt))
  hud.bridge.killLimit = killLimit
  hud.bridge.winnerID = winnerID
  hud.bridge.matchType = matchType
  hud.bridge.winnerTeam = winnerTeam
  hud.bridge.team1Kills = team1Kills
  hud.bridge.team2Kills = team2Kills
  hud.bridge.outOfBoundsRemaining = outOfBoundsRemaining
  hud.bridge.scoreboardHeld = scoreboardHeld and 1 or 0
  hud.bridge.cursorUnlocked = cursorUnlocked and 1 or 0
  hud.bridge.persistentCursor = persistentCursor and 1 or 0
  hud.bridge.hitMarkerRemaining = math.max(0, hitMarkerUntil - effectClock)
  hud.bridge.adsActive = fpsVisual.ads > 0.05 and 1 or 0
  hud.bridge.linkState = localActor == nil and 0 or inputSendOk and 1 or 2
  hud.bridge.clientError = clientPackError or ''
  hud.bridge.pickupPrompt = string.sub(hud.pickupPromptText or '', 1, 72)
  hud.bridge.pickupProgress = math.clamp(hud.pickupProgress or 0, 0, 1)

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
    hud.bridge.actorTeams[index] = actor.team or 0
    hud.bridge.radarFlags[index] = hud.radarVisible[actor.id] or 0
    hud.bridge.actorPositions[index] = actor.target
    hud.bridge.actorYaws[index] = actor.targetYaw
    hud.bridge.actorHealth[index] = actor.health
    hud.bridge.actorKills[index] = actor.kills
    hud.bridge.actorDeaths[index] = actor.deaths
    hud.bridge.actorScores[index] = actor.score
    hud.bridge.actorNames[index] = string.sub(names[actor.id] or ('Operative ' .. actor.id), 1, 32)
  end

  local grenadeThreatCount = 0
  if localActor ~= nil and bit.band(localActor.flags, 1) ~= 0
      and bit.band(localActor.flags, 2) == 0 then
    for _, grenade in pairs(fpsVisual.grenades) do
      if grenadeThreatCount >= hud.grenadeCapacity then break end
      local age = math.clamp(effectClock - (grenade.seenAt or effectClock), 0, 0.1)
      local position = grenade.position + grenade.velocity * age
      if fpsVisual.isHostileGrenade(grenade, localActor)
          and grenade.remaining - age > 0
          and (position - localActor.target):lengthSquared() <= 18 * 18 then
        hud.bridge.grenadeThreatPositions[grenadeThreatCount] = position
        hud.bridge.grenadeThreatVelocities[grenadeThreatCount] = grenade.velocity
        hud.bridge.grenadeThreatRemaining[grenadeThreatCount] = grenade.remaining - age
        grenadeThreatCount = grenadeThreatCount + 1
      end
    end
  end
  hud.bridge.grenadeThreatCount = grenadeThreatCount
  hud.bridge.grenadeThreatUpdateTime = now

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
ac.log(string.format('[ASRC FPS] client asset paths: root=%s remoteArchive=%s audioCatalog=%s',
  tostring(assettoRoot), rifleAssetArchivePath, clientAssetPath(fpsAudio.directory)))
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
  operatorModel = ac.StructItem.byte(),
  operatorSkin = ac.StructItem.byte(),
}, function() end)

hud.loadoutCatalogEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsLoadoutCatalog'),
  allowedMainWeapons = ac.StructItem.uint32(),
  allowedLethals = ac.StructItem.uint32(),
  allowedSecondaryWeapons = ac.StructItem.uint32(),
  defaultMainWeapon = ac.StructItem.byte(),
  defaultLethal = ac.StructItem.byte(),
  defaultSecondaryWeapon = ac.StructItem.byte(),
  allowedOperatorModels = ac.StructItem.uint32(),
  defaultOperatorModel = ac.StructItem.byte(),
  allowedOfficerSkins = ac.StructItem.uint32(),
  allowedGhostSkins = ac.StructItem.uint32(),
  defaultOperatorSkin = ac.StructItem.byte(),
}, function(sender, message)
  if sender ~= nil then return end
  local selection = hud.loadout
  selection.catalogReceived = true
  selection.allowedMainWeapons = message.allowedMainWeapons
  selection.allowedLethals = message.allowedLethals
  selection.allowedSecondaryWeapons = message.allowedSecondaryWeapons
  selection.allowedOperatorModels = message.allowedOperatorModels
  selection.allowedOfficerSkins = message.allowedOfficerSkins
  selection.allowedGhostSkins = message.allowedGhostSkins
  selection.operatorModel = fpsVisual.operatorModels[hud.loadoutStorage.operatorModel] ~= nil
      and hud.itemAllowed(message.allowedOperatorModels, hud.loadoutStorage.operatorModel)
      and hud.loadoutStorage.operatorModel or message.defaultOperatorModel
  selection.operatorSkin = hud.skinAllowed(selection.operatorModel, hud.loadoutStorage.operatorSkin)
    and hud.loadoutStorage.operatorSkin or message.defaultOperatorSkin
  selection.mainWeapon = hud.itemAllowed(message.allowedMainWeapons,
      hud.loadoutStorage.mainWeapon) and hud.loadoutStorage.mainWeapon
    or message.defaultMainWeapon
  selection.lethal = hud.itemAllowed(message.allowedLethals,
      hud.loadoutStorage.lethal) and hud.loadoutStorage.lethal
    or message.defaultLethal
  selection.secondaryWeapon = hud.itemAllowed(message.allowedSecondaryWeapons,
      hud.loadoutStorage.secondaryWeapon) and hud.loadoutStorage.secondaryWeapon
    or message.defaultSecondaryWeapon
  selection.dirty = false
  selection.result = 'CONFIRM A LOADOUT TO JOIN'
end)

hud.loadoutResultEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsLoadoutResult'),
  result = ac.StructItem.byte(),
  mainWeapon = ac.StructItem.byte(),
  lethal = ac.StructItem.byte(),
  secondaryWeapon = ac.StructItem.byte(),
  operatorModel = ac.StructItem.byte(),
  operatorSkin = ac.StructItem.byte(),
}, function(sender, message)
  if sender ~= nil then return end
  if message.result == 1 then
    hud.loadout.confirmed = true
    hud.loadout.dirty = false
    hud.loadout.result = 'LOADOUT APPLIED'
    hud.loadoutStorage.mainWeapon = message.mainWeapon
    hud.loadoutStorage.lethal = message.lethal
    hud.loadoutStorage.secondaryWeapon = message.secondaryWeapon
    hud.loadoutStorage.operatorModel = message.operatorModel
    hud.loadoutStorage.operatorSkin = message.operatorSkin
    if hud.preDriveDeployPending then
      hud.preDriveDeployPending = false
      hud.preDriveReturnAfterLoadout = false
      hud.tryStartFromDeployment()
    elseif hud.preDriveReturnAfterLoadout then
      hud.preDriveReturnAfterLoadout = false
      hud.preDrivePage = 'briefing'
      hud.preDriveStatus = 'LOADOUT READY'
    end
  elseif message.result == 2 then
    hud.loadout.dirty = false
    hud.loadout.result = 'QUEUED FOR NEXT RESPAWN'
    hud.loadoutStorage.mainWeapon = message.mainWeapon
    hud.loadoutStorage.lethal = message.lethal
    hud.loadoutStorage.secondaryWeapon = message.secondaryWeapon
    hud.loadoutStorage.operatorModel = message.operatorModel
    hud.loadoutStorage.operatorSkin = message.operatorSkin
    if hud.preDriveDeployPending then
      hud.preDriveDeployPending = false
      hud.preDriveReturnAfterLoadout = false
      hud.tryStartFromDeployment()
    elseif hud.preDriveReturnAfterLoadout then
      hud.preDriveReturnAfterLoadout = false
      hud.preDrivePage = 'briefing'
      hud.preDriveStatus = 'LOADOUT QUEUED FOR NEXT SPAWN'
    end
  else
    hud.preDriveDeployPending = false
    hud.preDriveReturnAfterLoadout = false
    hud.preDriveStatus = 'LOADOUT WAS REJECTED BY THE SERVER'
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
      team = teams[message.actorID] or 0,
    }
    actors[message.actorID] = actor
  end
  local previousActiveSlot = actor.activeSlot
  actor.mainWeapon = message.mainWeapon
  actor.lethal = message.lethal
  actor.secondaryWeapon = message.secondaryWeapon
  actor.activeSlot = message.activeSlot
  actor.lethalsRemaining = message.lethalsRemaining
  if previousActiveSlot ~= nil and previousActiveSlot ~= actor.activeSlot
      and actor.audioInitialized then
    fpsAudio.playEquip(actor)
  end
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
        spawnCount = nil, team = teams[id] or 0,
      }
      actors[id] = actor
    end
    local previousFlags = actor.flags
    local previousSpawnCount = actor.spawnCount
    local previousReload = actor.reloadRemaining
    local previousActionState = actor.actionState
    local previousY = actor.target.y
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
    fpsAudio.snapshotTransition(actor, previousFlags, previousSpawnCount,
      previousReload, previousActionState, previousY)
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
      actor.animationLocomotion = nil
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
          actor.animationLastPosition = nil
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
        actor.animationLastPosition = nil
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

hud.boundaryEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsBoundary'),
  outside = ac.StructItem.byte(),
  remainingSeconds = ac.StructItem.float(),
}, function(sender, message)
  if sender ~= nil then return end
  outOfBoundsRemaining = message.outside ~= 0 and math.max(0, message.remainingSeconds) or 0
end)

hud.rosterEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsRoster'),
  actorID = ac.StructItem.byte(),
  role = ac.StructItem.byte(),
  team = ac.StructItem.byte(),
  name = ac.StructItem.string(32),
  operatorModel = ac.StructItem.byte(),
  operatorSkin = ac.StructItem.byte(),
}, function(sender, message)
  if sender ~= nil then return end
  local previousName = names[message.actorID]
  names[message.actorID] = message.name
  teams[message.actorID] = message.team
  local model = fpsVisual.operatorModels[message.operatorModel] ~= nil and message.operatorModel or 0
  fpsVisual.actorModels[message.actorID] = model
  local skin = fpsVisual.operatorModels[model].skins[message.operatorSkin] ~= nil and message.operatorSkin or 0
  fpsVisual.actorSkins[message.actorID] = skin
  hud.radarReveal[message.actorID] = nil
  hud.radarVisible[message.actorID] = nil
  local actor = actors[message.actorID]
  if actor ~= nil then
    if previousName ~= nil and (previousName ~= message.name or actor.role ~= message.role) then
      fpsAudio.resetActor(actor, true)
    end
    actor.role = message.role
    if actor.team ~= message.team or actor.operatorModel ~= model or actor.operatorSkin ~= skin then
      fpsVisual.resetOperatorAvatar(actor)
      actor.operatorFallback = false
    end
    actor.operatorModel = model
    actor.operatorSkin = skin
    actor.team = message.team
  end
end)

hud.matchEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsMatch'),
  state = ac.StructItem.byte(),
  remainingSeconds = ac.StructItem.float(),
  startCountdownSeconds = ac.StructItem.float(),
  restartCountdownSeconds = ac.StructItem.float(),
  killLimit = ac.StructItem.uint16(),
  maximumHealth = ac.StructItem.uint16(),
  winnerID = ac.StructItem.byte(),
  matchType = ac.StructItem.byte(),
  winnerTeam = ac.StructItem.byte(),
  team1Kills = ac.StructItem.uint16(),
  team2Kills = ac.StructItem.uint16(),
  weatherType = ac.StructItem.byte(),
  timeOfDaySeconds = ac.StructItem.uint32(),
}, function(sender, message)
  if sender ~= nil then return end
  if matchState == 2 and message.state ~= 2 then
    killFeed = {}
    hud.awardPopups = {}
    hitMarkerUntil = 0
    outOfBoundsRemaining = 0
    hud.loadout.activeSlot = 0
    for _, actor in pairs(actors) do
      actor.score, actor.kills, actor.deaths = 0, 0, 0
    end
    fpsVisual.activeGrenadeType = nil
    fpsVisual.grenadeInputHeld = false
    for id in pairs(fpsVisual.grenades) do fpsVisual.removeGrenade(id) end
    for id, pickup in pairs(fpsVisual.pickups) do
      if pickup.root ~= nil then pcall(function() pickup.root:dispose() end) end
      fpsVisual.pickups[id] = nil
    end
  end
  matchState = message.state
  fpsVisual.restartCountdownSeconds = message.restartCountdownSeconds
  fpsVisual.restartCountdownUpdatedAt = ui.time()
  remainingSeconds = message.remainingSeconds
  fpsVisual.startCountdownSeconds = message.startCountdownSeconds
  killLimit = message.killLimit
  hud.maximumHealth = math.max(1, message.maximumHealth)
  winnerID = message.winnerID
  matchType = message.matchType
  winnerTeam = message.winnerTeam
  team1Kills = message.team1Kills
  team2Kills = message.team2Kills
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
  local killerName = message.killerID == 255 and (message.itemID == 0 and 'ARENA' or 'SELF')
    or (names[message.killerID] or ('Player ' .. message.killerID))
  killFeed[#killFeed + 1] = {
    text = killerName .. '  [' .. (hud.itemNames[message.itemID] or 'DAMAGE') .. ']  '
      .. (names[message.victimID] or ('Player ' .. message.victimID)),
    ttl = 4,
  }
  if message.killerID == localSessionID then hitMarkerUntil = effectClock + 0.22 end
  clearActorImpacts(message.victimID)
  fpsAudio.playDeath(actors[message.victimID])
end)

hud.hitEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsHit'),
  attackerID = ac.StructItem.byte(),
  victimID = ac.StructItem.byte(),
  remainingHealth = ac.StructItem.uint16(),
  itemID = ac.StructItem.byte(),
}, function(sender, message)
  if sender ~= nil then return end
  fpsAudio.playHurt(actors[message.victimID])
  if message.attackerID == localSessionID then hitMarkerUntil = effectClock + 0.16 end
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
      actionState = 0, spawnCount = nil, team = teams[message.actorID] or 0,
    }
    actors[message.actorID] = actor
  end
  actor.score = message.totalScore
  if message.actorID ~= localSessionID or message.points == 0 then return end
  local labels = { string.format('+%d', message.points) }
  if bit.band(message.flags, 1) ~= 0 then
    labels[#labels + 1] = 'KILL'
    fpsAudio.play('kill_confirm.wav', nil, true, 0.35, 1, 0.6)
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
  droppedByActorID = ac.StructItem.byte(),
  result = ac.StructItem.byte(),
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
      droppedByActorID = message.droppedByActorID,
      position = message.position:clone(),
      bornAt = effectClock,
      root = nil,
      model = nil,
    }
  elseif message.collectorID == localSessionID then
    fpsAudio.play('pickup_magazine.wav', nil, true, 0.35, 1, 0.6)
    hud.awardPopups[#hud.awardPopups + 1] = {
      text = message.result == 2 and ('EQUIPPED ' .. (hud.itemNames[message.weaponType]
        or 'WEAPON')) or '+1 MAGAZINE', age = 0, ttl = 2.2,
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

function fpsVisual.illuminateExplosion(grenadeID, position, now)
  if fpsVisual.explosionLightUnavailable then return end
  local ok, lightOrError = pcall(function()
    local light = ac.LightSource(ac.LightType.Regular)
    light.position:set(position + vec3(0, 0.35, 0))
    light.color = rgb(8.5, 2.1, 0.32)
    light.range = 13
    light.spot = 0
    light.diffuseConcentration = 0.35
    light.specularMultiplier = 0.7
    light.rangeGradientOffset = 0
    light.fadeAt = 120
    light.fadeSmooth = 8
    light.volumetricLight = true
    light.skipLightMap = true
    light.affectsCars = true
    light.showInReflections = true
    light.shadows = false
    return light
  end)
  if not ok or lightOrError == nil then
    fpsVisual.explosionLightUnavailable = true
    ac.warn('[ASRC FPS] dynamic explosion lighting unavailable: ' .. tostring(lightOrError))
    return
  end
  fpsVisual.explosionLights[grenadeID] = {
    light = lightOrError, bornAt = now, expiresAt = now + 0.42,
  }
end

function fpsVisual.updateExplosionLights(now)
  for grenadeID, state in pairs(fpsVisual.explosionLights) do
    local life = math.clamp((state.expiresAt - now) / 0.42, 0, 1)
    state.light.range = 13 * life * life
    state.light.color = rgb(8.5 * life, 2.1 * life, 0.32 * life)
    if now >= state.expiresAt then
      pcall(function() state.light:dispose() end)
      fpsVisual.explosionLights[grenadeID] = nil
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
    fpsAudio.playImpact(message.impact, point)
  end
  if actor ~= nil then actor.weaponKick = 1 end
  fpsAudio.playWeaponFire(message.weaponType, message.origin,
    message.shooterID == localSessionID)
end, nil, true)

fpsVisual.grenades = {}
function fpsVisual.isHostileGrenade(grenade, localActor)
  if grenade.ownerID == localSessionID then return false end
  if isTeamMatch() and localActor ~= nil and localActor.team ~= 0 then
    local owner = actors[grenade.ownerID]
    if owner ~= nil and owner.team == localActor.team then return false end
  end
  return true
end

function fpsVisual.predictedGrenadePosition(grenade)
  local age = math.clamp(effectClock - (grenade.seenAt or effectClock), 0, 0.1)
  return grenade.position + grenade.velocity * age
end

function fpsVisual.removeGrenade(id)
  local grenade = fpsVisual.grenades[id]
  if grenade ~= nil and grenade.root ~= nil and grenade.root ~= false then
    pcall(function() grenade.root:dispose() end)
  end
  fpsVisual.grenades[id] = nil
end

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
    local firstObservation = fpsVisual.grenades[id] == nil
    local grenade = fpsVisual.grenades[id] or { id = id }
    grenade.ownerID = message.ownerIDs[i]
    grenade.type = message.types[i]
    grenade.flags = message.flags[i]
    grenade.position = message.positions[i]:clone()
    grenade.velocity = message.velocities[i]:clone()
    grenade.remaining = message.remaining[i]
    grenade.seenAt = effectClock
    fpsVisual.grenades[id] = grenade
    if firstObservation and grenade.ownerID ~= localSessionID then
      fpsAudio.play('grenade_throw.wav', grenade.position, false, 0.55, 20, 0.7)
    end
  end
  for id, grenade in pairs(fpsVisual.grenades) do
    if grenade.seenAt ~= effectClock and effectClock - (grenade.seenAt or 0) > 0.15 then
      fpsVisual.removeGrenade(id)
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
  fpsVisual.removeGrenade(message.grenadeID)
  if message.ownerID == localSessionID and fpsVisual.activeGrenadeType ~= nil then
    fpsVisual.activeGrenadeType = nil
    fpsVisual.grenadeReleasedAt = nil
  end
  local origin = message.position:clone()
  if fpsVisual.explosionSparks ~= nil then
    fpsVisual.explosionSparks:emit(origin + vec3(0, 0.12, 0), vec3(0, 4.5, 0), 42)
  end
  if fpsVisual.explosionSmoke ~= nil then
    fpsVisual.explosionSmoke:emit(origin + vec3(0, 0.18, 0), vec3(0, 1.5, 0), 18)
  end
  if fpsVisual.explosionFlame ~= nil then
    fpsVisual.explosionFlame:emit(origin + vec3(0, 0.16, 0), vec3(0, 1.8, 0), 12)
  end
  for i = 1, 36 do
    local angle = i / 36 * math.pi * 2
    local speed = 3.8 + (i % 7) * 0.72
    sparks[#sparks + 1] = {
      position = origin + vec3(0, 0.12, 0),
      velocity = vec3(math.cos(angle) * speed, 1.8 + (i % 6) * 0.8,
        math.sin(angle) * speed),
      ttl = 0.55 + (i % 5) * 0.09,
    }
  end
  fpsVisual.illuminateExplosion(message.grenadeID, origin, ui.time())
  fpsAudio.playGrenadeExplosion(message.type, origin)
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

function fpsVisual.resetOperatorAvatar(actor)
  if actor.root ~= nil and actor.root ~= false then pcall(function() actor.root:dispose() end) end
  actor.root, actor.modernModel, actor.weaponRoot, actor.weaponMesh = nil, nil, nil, nil
  actor.weaponAsset, actor.loadedOperatorModel, actor.skinApplied = nil, nil, nil
  actor.animationClip, actor.animationPreviousClip, actor.animationPhase = nil, nil, 0
  actor.animationLastPosition, actor.animationLocomotion = nil, nil
  actor.nativeScenePrepared, actor.nativeSceneVisible = false, false
end

function fpsVisual.operatorForActor(actor)
  local id = actor.operatorFallback and 0 or fpsVisual.actorModels[actor.id] or 0
  return fpsVisual.operatorModels[id] or fpsVisual.operatorModels[0]
end

function fpsVisual.operatorFailed(actor, reason)
  if fpsVisual.operatorForActor(actor).id == 1 then
    fpsVisual.resetOperatorAvatar(actor)
    actor.operatorFallback = true
    ac.warn('[ASRC FPS] Ghost unavailable for actor ' .. tostring(actor.id)
      .. '; using Officer: ' .. tostring(reason))
  else
    fpsVisual.fallback(reason)
  end
end

function fpsVisual.skinForActor(actor)
  local descriptor = fpsVisual.operatorForActor(actor)
  local id = fpsVisual.actorSkins[actor.id] or 0
  if descriptor.skins[id] == nil then id = 0 end
  return descriptor.skins[id], id
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
  rifleAssetArchivePath = '/fps/assets/asrc-fps-assets-v22.zip'
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

function fpsVisual.desiredViewmodelAsset(actor)
  return fpsVisual.activeGrenadeType or fpsVisual.weaponAssetKey(actor)
end

function fpsVisual.isPistolAsset(assetKey)
  return assetKey == 3 or assetKey == 4
end

function fpsVisual.isGrenadeAsset(assetKey)
  return assetKey == 16 or assetKey == 17
end

function fpsVisual.isLoadoutAsset(assetKey)
  return assetKey == 2 or fpsVisual.isPistolAsset(assetKey)
    or fpsVisual.isGrenadeAsset(assetKey)
end

function fpsVisual.loadoutClips(assetKey)
  if fpsVisual.isGrenadeAsset(assetKey) then
    return assetKey == 17 and fpsVisual.stickyGrenadeClips or fpsVisual.fragGrenadeClips
  end
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
  if fpsVisual.isGrenadeAsset(assetKey) then
    return assetKey == 17 and fpsVisual.stickyGrenadeViewmodelFileName
      or fpsVisual.fragGrenadeViewmodelFileName
  end
  return assetKey == 2 and fpsVisual.compactSmgViewmodelFileName
    or fpsVisual.pistolViewmodelFileName(assetKey)
end

function fpsVisual.loadoutWorldModelFileName(assetKey)
  if fpsVisual.isGrenadeAsset(assetKey) then
    return assetKey == 17 and fpsVisual.stickyGrenadeWorldModelFileName
      or fpsVisual.fragGrenadeWorldModelFileName
  end
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
    if fpsVisual.modern then
      local catalogOk, catalogError = pcall(fpsVisual.loadOperatorAnimations, folder)
      if not catalogOk then
        fpsVisual.fallback('operator animation catalog: ' .. tostring(catalogError))
        return
      end
    end
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

function fpsVisual.applyOperatorSkin(actor)
  local skin, skinID = fpsVisual.skinForActor(actor)
  if not fpsVisual.modern or actor.modernModel == nil
      or actor.skinApplied == skinID then return true end
  if skinID == 0 then
    -- A skin change rebuilds the instance, restoring its embedded original maps.
    actor.skinApplied = 0
    return true
  end
  local ok, err = pcall(function()
    local descriptor = fpsVisual.operatorForActor(actor)
    local uniformPath, gearPath = fpsVisual.asset(skin.uniform), fpsVisual.asset(skin.gear)
    if uniformPath == nil or gearPath == nil
        or not io.fileExists(uniformPath) or not io.fileExists(gearPath) then
      error('Operator skin textures are missing')
    end
    -- loadKN5() pools material resources. Select the two descendants by their
    -- authored material names, then fork those materials for this actor before
    -- replacing textures so one player's skin cannot recolor another instance.
    local uniform = actor.modernModel:findAny('material:' .. descriptor.materialPrefix .. 'UNIFORM')
    local gear = actor.modernModel:findAny('material:' .. descriptor.materialPrefix .. 'GEAR')
    if uniform == nil or uniform:size() ~= 1 or gear == nil or gear:size() ~= 1 then
      error(string.format('operator uniform or gear material was not found (uniform=%d, gear=%d)',
        uniform ~= nil and uniform:size() or -1, gear ~= nil and gear:size() or -1))
    end
    uniform:ensureUniqueMaterials()
    gear:ensureUniqueMaterials()
    uniform:setMaterialTexture('txDiffuse', uniformPath)
    gear:setMaterialTexture('txDiffuse', gearPath)
  end)
  if not ok then
    fpsVisual.operatorFailed(actor, 'operator skin actor ' .. tostring(actor.id) .. ': '
      .. tostring(err))
    return false
  end
  actor.skinApplied = skinID
  return true
end

local function ensureLocalViewmodel()
  local actor = actors[localSessionID]
  local assetKey = fpsVisual.desiredViewmodelAsset(actor)
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
    if fpsVisual.isGrenadeAsset(assetKey) then
      model:setAnimation(fpsVisual.loadoutAssetFolder .. '/'
        .. fpsVisual.loadoutClips(assetKey).throw, 0, true)
      fpsVisual.viewmodelPistolPoseSeedPending = false
      fpsVisual.viewmodelEquipUntil = 0
    elseif fpsVisual.isPistolAsset(assetKey) then
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

local function ensureAvatar(actor)
  if rifleAssetFolder == nil then
    requestRifleAssets()
    return
  end
  local descriptor = fpsVisual.operatorForActor(actor)
  if fpsVisual.modern and actor.loadedOperatorModel ~= nil
      and actor.loadedOperatorModel ~= descriptor.id then
    fpsVisual.resetOperatorAvatar(actor)
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
        local child = root:loadKN5({filename = fpsVisual.asset(descriptor.file), forceRenderableOn = true})
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
        fpsVisual.operatorFailed(actor, 'operator load actor ' .. tostring(actor.id) .. ': ' .. tostring(model))
        return
      end
      actor.modernModel = model
      actor.loadedOperatorModel = descriptor.id
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
      -- Weapon meshes point along local Z with local Y as their thickness axis.
      -- Settle local Y onto world-up; the previous 82 degree final pitch stood
      -- the rifle on its stock and buried its lower half in the floor.
      local angle = math.rad(68) * (1 - fall)
      local forward = baseForward * math.cos(angle) + vec3(0, 1, 0) * math.sin(angle)
      local up = vec3(0, 1, 0) * math.cos(angle) - baseForward * math.sin(angle)
      local groundOffset = assetKey == 2 and 0.13
        or assetKey == 3 and 0.065
        or assetKey == 4 and 0.06
        or (fpsVisual.modern and 0.17 or 0.275)
      local height = 0.65 * (1 - fall) * (1 - fall) + groundOffset
      pickup.root:setPosition(pickup.position + vec3(0, height, 0) + ac.getSim().originShift)
      pickup.root:setOrientation(forward, up)
      pickup.root:setVisible(gameplayActive, false)
    end
  end
end

function fpsVisual.updatePickupInteraction(dt, held)
  hud.pickupPromptText = ''
  hud.pickupProgress = 0
  local actor = actors[localSessionID]
  if cursorUnlocked or actor == nil or bit.band(actor.flags, 2) ~= 0 then
    fpsVisual.pickupInteractionTarget = nil
    fpsVisual.pickupInteractionProgress = 0
    return
  end

  local nearest, nearestDistance = nil, math.huge
  local activeWeapon = fpsVisual.activeWeapon(actor)
  for _, pickup in pairs(fpsVisual.pickups) do
    local slotWeapon = pickup.weaponType <= 2 and (actor.mainWeapon or hud.loadout.mainWeapon)
      or (actor.secondaryWeapon or hud.loadout.secondaryWeapon)
    if pickup.droppedByActorID ~= localSessionID
        and effectClock - pickup.bornAt >= 0.4
        and pickup.weaponType ~= activeWeapon
        and pickup.weaponType ~= slotWeapon then
      local distance = (actor.target - pickup.position):lengthSquared()
      if distance <= 1.35 * 1.35
          and (distance < nearestDistance
            or (distance == nearestDistance and (nearest == nil or pickup.id < nearest.id))) then
        nearest, nearestDistance = pickup, distance
      end
    end
  end
  if nearest == nil then
    fpsVisual.pickupInteractionTarget = nil
    fpsVisual.pickupInteractionProgress = 0
    return
  end

  if fpsVisual.pickupInteractionTarget ~= nearest.id then
    fpsVisual.pickupInteractionTarget = nearest.id
    fpsVisual.pickupInteractionProgress = 0
  end
  if held then
    fpsVisual.pickupInteractionProgress = math.min(1,
      (fpsVisual.pickupInteractionProgress or 0) + dt / 0.45)
  else
    fpsVisual.pickupInteractionProgress = 0
  end

  local current = nearest.weaponType <= 2 and (actor.mainWeapon or hud.loadout.mainWeapon)
    or (actor.secondaryWeapon or hud.loadout.secondaryWeapon)
  hud.pickupPromptText = string.format('HOLD %s / X TO SWAP %s FOR %s',
    hud.bindingName(hud.bindings.interact), hud.itemNames[current] or 'WEAPON',
    hud.itemNames[nearest.weaponType] or 'WEAPON')
  hud.pickupProgress = fpsVisual.pickupInteractionProgress or 0
end

function fpsVisual.updateGrenadeModels()
  if next(fpsVisual.grenades) == nil then return end
  if fpsVisual.loadoutAssetFolder == nil then
    fpsVisual.requestLoadoutAssets()
    return
  end
  for _, grenade in pairs(fpsVisual.grenades) do
    if grenade.root == nil then
      local loaded, rootOrError = pcall(function()
        local root = carsRoot:createBoundingSphereNode('ASRC_FPS_GRENADE_' .. grenade.id, 0.45)
        if root == nil then error('grenade holder could not be created') end
        local path = fpsVisual.worldModelPath(grenade.type)
        local model = root:loadKN5({filename = path, forceRenderableOn = true})
        if model == nil then
          root:dispose()
          error('grenade KN5 could not be loaded: ' .. tostring(path))
        end
        model:setShadows(true)
        model:setCullMode(render.CullMode.Back)
        model:setDepthMode(render.DepthMode.Normal)
        model:setMotionStencil(1)
        root:setVirtualCarFlag(true)
        root:setMotionStencil(1)
        grenade.model = model
        return root
      end)
      if loaded then
        grenade.root = rootOrError
        grenade.root:clearMotion()
      else
        grenade.root = false
        ac.warn('[ASRC FPS] grenade world model failed: ' .. tostring(rootOrError))
      end
    end
    if grenade.root ~= nil and grenade.root ~= false then
      local moving = grenade.velocity ~= nil and grenade.velocity:lengthSquared() > 0.01
      local forward = moving and grenade.velocity:clone():normalize() or vec3(0, 0, 1)
      local up = math.abs(forward.y) > 0.92 and vec3(1, 0, 0) or vec3(0, 1, 0)
      grenade.root:setPosition(grenade.position + ac.getSim().originShift)
      grenade.root:setOrientation(forward, up)
      grenade.root:setVisible(gameplayActive, false)
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
  local adsAllowed = not thirdPersonEnabled and actor.reloadRemaining <= 0
    and not viewmodelSprint and fpsVisual.activeGrenadeType == nil
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

function fpsVisual.loadOperatorAnimations(folder)
  local manifest = JSON.parse(io.load(folder .. '/asrc-modern-assets.json'))
  local catalog = manifest.operatorAnimations
  if type(catalog) ~= 'table' then error('Missing animation catalog') end
  for name, file in pairs(fpsVisual.operatorClips) do
    local entry = catalog[name]
    if type(entry) ~= 'table' or entry.file ~= file or not io.fileExists(folder .. '/' .. file)
        or type(entry.durationSeconds) ~= 'number' or entry.durationSeconds <= 0 then
      error('Invalid animation: ' .. name)
    end
    local partial = name == 'fire' or name == 'reload'
    if entry.trackCoverage ~= (partial and 'upperBody' or 'fullBody') then
      error('Invalid animation track coverage: ' .. name)
    end
  end
  for _, name in ipairs(fpsVisual.standingClipOrder) do
    if name:sub(1, 4) ~= 'aim_' then
      local entry = catalog[name]
      if type(entry.strideMeters) ~= 'number' or entry.strideMeters <= 0.5
          or entry.strideMeters >= 8 or entry.loop ~= true then
        error('Invalid locomotion stride: ' .. name)
      end
    end
  end
  fpsVisual.operatorAnimations = catalog
end

function fpsVisual.sampleAnimationMotion(actor, dt)
  local previous = actor.animationLastPosition
  actor.animationLastPosition = actor.render:clone()
  local dx = previous ~= nil and actor.render.x - previous.x or 0
  local dz = previous ~= nil and actor.render.z - previous.z or 0
  local distance = math.sqrt(dx * dx + dz * dz)
  if previous == nil or dt <= 0 or dt > 0.25 or distance > math.max(1.5, dt * 15) then
    actor.animationVelocityX, actor.animationVelocityZ = 0, 0
    actor.animationLocomotion = nil
    return 0, 0
  end
  local rawSpeed = distance / dt
  local mix = 1 - math.exp(-dt * (rawSpeed < 0.08 and 40 or 18))
  actor.animationVelocityX = math.lerp(actor.animationVelocityX or 0, dx / dt, mix)
  actor.animationVelocityZ = math.lerp(actor.animationVelocityZ or 0, dz / dt, mix)
  local speed = math.sqrt(actor.animationVelocityX ^ 2 + actor.animationVelocityZ ^ 2)
  return speed, distance
end

function fpsVisual.updateStandingLocomotion(actor, speed, distance, dt)
  local state = actor.animationLocomotion
  if state == nil then
    state = { weights = {}, targets = {}, phase = 0, moving = false, sprinting = false }
    actor.animationLocomotion = state
  end
  for _, name in ipairs(fpsVisual.standingClipOrder) do state.targets[name] = 0 end
  state.moving = speed > (state.moving and 0.18 or 0.30)
  state.sprinting = speed > (state.sprinting and 6.7 or 7.2)
  if state.moving then
    local forwardX, forwardZ = math.sin(actor.yaw), math.cos(actor.yaw)
    local forward = actor.animationVelocityX * forwardX + actor.animationVelocityZ * forwardZ
    local right = actor.animationVelocityX * forwardZ - actor.animationVelocityZ * forwardX
    local direction = (math.atan2(right, forward) / (math.pi / 4)) % 8
    local sector = math.floor(direction)
    local fraction = direction - sector
    local first = fpsVisual.joggingDirections[sector + 1]
    local second = fpsVisual.joggingDirections[(sector + 1) % 8 + 1]
    state.targets[first], state.targets[second] = 1 - fraction, fraction
    local forwardWeight = state.targets.jog_forward
    local walk = math.clamp((3.6 - speed) / 1.2, 0, 1) * forwardWeight
    local sprint = state.sprinting and math.clamp((speed - 6.7) / 1.6, 0, 1) * forwardWeight or 0
    state.targets.walk_forward = walk
    state.targets.sprint = sprint
    state.targets.jog_forward = forwardWeight - walk - sprint
  else
    state.targets[actor.pitch > 0.32 and 'aim_up' or actor.pitch < -0.32 and 'aim_down' or 'aim_idle'] = 1
  end
  local mix = 1 - math.exp(-dt / 0.08)
  local dominant, maximum = 'aim_idle', -1
  local stride, movementWeight = 0, 0
  for _, name in ipairs(fpsVisual.standingClipOrder) do
    local initial = name == 'aim_idle' and 1 or 0
    local weight = math.lerp(state.weights[name] or initial, state.targets[name], mix)
    if weight < 0.001 then weight = 0 end
    state.weights[name] = weight
    if weight > maximum then dominant, maximum = name, weight end
    local entry = fpsVisual.operatorAnimations[name]
    if entry.strideMeters ~= nil then
      stride = stride + entry.strideMeters * weight
      movementWeight = movementWeight + weight
    end
  end
  if state.moving and movementWeight > 0.001 then
    state.phase = (state.phase + distance / (stride / movementWeight)) % 1
  end
  return dominant, state.phase
end

function fpsVisual.blendStandingLocomotion(actor, dominant, position)
  local weights = actor.animationLocomotion.weights
  local accumulated = weights[dominant]
  for _, name in ipairs(fpsVisual.standingClipOrder) do
    local weight = weights[name]
    if name ~= dominant and weight > 0 then
      accumulated = accumulated + weight
      actor.modernModel:blendAnimation(fpsVisual.asset(fpsVisual.operatorClips[name]),
        position, weight / accumulated, false)
    end
  end
end

function fpsVisual.updateActorAnimation(actor, dt)
  if not fpsVisual.modern or actor.modernModel == nil then return true end
  local speed, distance = fpsVisual.sampleAnimationMotion(actor, dt)
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
  local standing = false
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
    else
      standing = true
      clip, position = fpsVisual.updateStandingLocomotion(actor, speed, distance, dt)
    end
    if looping then
      actor.animationPhase = ((actor.animationPhase or 0)
        + dt * math.max(0.75, speed * 0.35)) % 1
      position = actor.animationPhase
    elseif not standing then
      actor.animationPhase = position
    end
  end

  if actor.animationClip ~= clip and not (standing and actor.animationWasStanding) then
    actor.animationPreviousClip = actor.animationClip
    actor.animationPreviousPosition = actor.animationPosition or 0
    actor.animationBlend = 0
    actor.animationClip = clip
  end
  actor.animationClip = clip
  actor.animationWasStanding = standing
  actor.animationPosition = position
  actor.animationBlend = math.min(1, (actor.animationBlend or 0) + dt / 0.12)
  local ok, err = pcall(function()
    local stanceGroundOffset = fpsVisual.operatorForActor(actor).stanceOffsets[stance] or 0
    actor.modernModel:setPosition(vec3(0, stanceGroundOffset, 0))
    actor.modernModel:setAnimation(fpsVisual.asset(fpsVisual.operatorClips[clip]),
      position, true)
    if standing then fpsVisual.blendStandingLocomotion(actor, clip, position) end
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
    fpsVisual.operatorFailed(actor, 'operator animation actor ' .. tostring(actor.id) .. ': ' .. tostring(err))
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
  if fpsVisual.isGrenadeAsset(fpsVisual.loadedViewmodelAsset) then
    local position
    if fpsVisual.grenadeReleasedAt == nil then
      position = fpsVisual.grenadeHoldPhase * math.clamp(
        (effectClock - fpsVisual.grenadePrimeStarted) / fpsVisual.grenadePrimeDuration, 0, 1)
    else
      local elapsed = math.max(0, effectClock - fpsVisual.grenadeReleasedAt)
      if elapsed <= fpsVisual.grenadeReleasePoint then
        position = math.lerp(fpsVisual.grenadeHoldPhase, fpsVisual.grenadeReleasePhase,
          elapsed / fpsVisual.grenadeReleasePoint)
      else
        position = math.lerp(fpsVisual.grenadeReleasePhase, 1, math.clamp(
          (elapsed - fpsVisual.grenadeReleasePoint)
            / (fpsVisual.grenadeReleaseDuration - fpsVisual.grenadeReleasePoint), 0, 1))
      end
    end
    local ok, err = pcall(function()
      viewmodelRoot:setAnimation(fpsVisual.loadoutAssetFolder .. '/'
        .. fpsVisual.loadoutClips(fpsVisual.loadedViewmodelAsset).throw, position, true)
    end)
    if not ok then
      clientPackError = 'FPS GRENADE ANIMATION ERROR - CHECK LIVE LOG'
      ac.warn('[ASRC FPS] grenade throw animation failed: ' .. tostring(err))
      return false
    end
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
  local grenadeViewmodel = fpsVisual.isGrenadeAsset(fpsVisual.loadedViewmodelAsset)
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
  local hipForward = grenadeViewmodel and 0.37
    or (pistolViewmodel and 0.39 or (modernViewmodel and 0.32 or 0.30))
  local hipRight = grenadeViewmodel and -0.11
    or (pistolViewmodel and -0.15 or (modernViewmodel and -0.18 or 0.22))
  local hipUp = grenadeViewmodel and -0.25
    or (pistolViewmodel and -0.24 or (modernViewmodel and -0.32 or -0.20))
  -- The Modern KN5 faces back toward its root, so its apparent screen-right
  -- direction is opposite the holder translation. These calibrated offsets put
  -- the optic axis on the camera look vector and bring the rear sight close
  -- enough to read as true ADS instead of a zoomed hip-fire pose.
  local adsForward = grenadeViewmodel and hipForward
    or (pistolViewmodel and 0.34 or (modernViewmodel and 0.12 or 0.38))
  -- The pistol KN5 faces back toward its holder, so decreasing this camera-right
  -- translation moves the rendered Desert Eagle toward screen-right.
  local pistolAdsRight = fpsVisual.loadedViewmodelAsset == 3 and 0.025 or 0.035
  local adsRight = grenadeViewmodel and hipRight or (pistolViewmodel and pistolAdsRight
    or (modernViewmodel and 0.0003 or 0.00)
  )
  local adsUp = grenadeViewmodel and hipUp
    or (pistolViewmodel and -0.12 or (modernViewmodel and -0.2218 or -0.10))
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
          actor.animationLastPosition = nil
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
        if not fpsVisual.applyOperatorSkin(actor) then return false end
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
  if #tracers == 0 and #impacts == 0 and #sparks == 0 then return end
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
    if not fpsVisual.applyOperatorSkin(actor) then return end
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
  if fpsVisual.activeGrenadeType ~= nil
      and fpsVisual.grenadeReleasedAt ~= nil
      and effectClock - fpsVisual.grenadeReleasedAt >= fpsVisual.grenadeReleaseDuration then
    fpsVisual.activeGrenadeType = nil
    fpsVisual.grenadeReleasedAt = nil
  end
  if not gameplayActive then
    fpsVisual.activeGrenadeType = nil
    fpsVisual.grenadeReleasedAt = nil
    fpsVisual.grenadeInputHeld = false
  end
  if gameplayActive then hud.requestWeaponImage() end
  fpsVisual.updatePickups()
  fpsVisual.updateGrenadeModels()
  -- The companion HUD can win exclusive UI ownership before this script sees
  -- a gameplay-mode callback. Reset pause ownership on the simulation state
  -- transition instead, which is observed by script.update() in either case.
  if gameplayActive and not previousGameplayActive then
    hud.nativePauseMenu = false
    hud.nativePreDriveMenu = false
    hud.preDrivePage = 'briefing'
    hud.preDriveLeaveArmed = false
    hud.preDriveDeployPending = false
    hud.preDriveReturnAfterLoadout = false
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
    if previousGameplayActive == true and not gameplayActive then fpsAudio.resetAll() end
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
    cursorUnlocked = matchState == 2 or scoreboardHeld or persistentCursor or not hud.loadout.confirmed
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
    local matchInputLocked = matchState ~= 1
    fpsVisual.adsInput = not matchInputLocked and not cursorUnlocked and not thirdPersonEnabled
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
    if matchInputLocked then move:set(0, 0) end
    local gamepadFire = ac.getGamepadAxisValue(0, ac.GamepadAxis.RightTrigger) > 0.35
    -- Raw VK input remains available while mouse-delta capture owns the pointer. CSP UI
    -- state alone reports false in that state on some builds, which previously meant the
    -- server received movement but never a Fire bit.
    local rawMouseFire = ac.isKeyDown(ac.KeyIndex.LeftButton)
    local uiMouseFire = ac.getUI().isMouseLeftKeyDown or ui.mouseDown(ui.MouseButton.Left)
    local boundFire = hud.bindingDown('fire', rawMouseFire or uiMouseFire)
    local fire = not matchInputLocked and not cursorUnlocked and (boundFire or gamepadFire)
    if fire and not fireCaptureLogged then
      fireCaptureLogged = true
      ac.log(string.format(
        '[ASRC FPS] fire input captured: bound=%s rawMouse=%s uiMouse=%s gamepad=%s',
        tostring(boundFire), tostring(rawMouseFire), tostring(uiMouseFire),
        tostring(gamepadFire)))
    end
    sprint = not matchInputLocked and (hud.bindingDown('sprint', ac.isKeyDown(ac.KeyIndex.LeftShift))
      or ac.isGamepadButtonPressed(0, ac.GamepadButton.LeftThumb)
    )
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
    local jump = not matchInputLocked
      and hud.bindingDown('jump', ac.isKeyDown(ac.KeyIndex.Space))
    local crouch = not matchInputLocked
      and hud.bindingDown('crouch', ac.isKeyDown(ac.KeyIndex.C))
    local crouchToggleMode = hud.controlSettings.crouchToggle == true
    local reload = not matchInputLocked
      and hud.bindingDown('reload', ac.isKeyDown(ac.KeyIndex.R))
    local gamepadWeaponSwitch = ac.isGamepadButtonPressed(0, ac.GamepadButton.Y)
    local gamepadGrenade = ac.isGamepadButtonPressed(0, ac.GamepadButton.RightShoulder)
    local gamepadInteract = ac.isGamepadButtonPressed(0, ac.GamepadButton.X)
    local interact = not matchInputLocked and not cursorUnlocked and (hud.bindingDown('interact',
      ac.isKeyDown(ac.KeyIndex.F)) or gamepadInteract)
    fpsVisual.updatePickupInteraction(dt, interact)
    local grenade = not matchInputLocked and (hud.bindingDown('grenade',
      ac.isKeyDown(ac.KeyIndex.G)) or gamepadGrenade)
    if grenade and not fpsVisual.grenadeInputHeld and not cursorUnlocked
        and localActor ~= nil and (localActor.lethalsRemaining or 0) > 0
        and fpsVisual.activeGrenadeType == nil then
      fpsVisual.activeGrenadeType = localActor.lethal or hud.loadout.lethal
      fpsVisual.grenadePrimeStarted = effectClock
      fpsVisual.grenadeReleasedAt = nil
      fpsVisual.adsInput = 0
      fpsAudio.play('grenade_prime.wav', nil, true, 0.55, 1, 0.6)
      ac.log('[ASRC FPS] local grenade cooking started: type='
        .. tostring(fpsVisual.activeGrenadeType))
    end
    if not grenade and fpsVisual.grenadeInputHeld
        and fpsVisual.activeGrenadeType ~= nil
        and fpsVisual.grenadeReleasedAt == nil then
      fpsVisual.grenadeReleasedAt = effectClock
      fpsAudio.play('grenade_throw.wav', nil, true, 0.55, 1, 0.7)
      ac.log('[ASRC FPS] local cooked grenade released: type='
        .. tostring(fpsVisual.activeGrenadeType))
    end
    fpsVisual.grenadeInputHeld = grenade
    if not matchInputLocked and not cursorUnlocked and ac.isKeyPressed(ac.KeyIndex.D1) then
      hud.loadout.activeSlot = 0
    elseif not matchInputLocked and not cursorUnlocked and ac.isKeyPressed(ac.KeyIndex.D2) then
      hud.loadout.activeSlot = 1
    elseif not matchInputLocked and not cursorUnlocked
        and gamepadWeaponSwitch and not weaponSwitchWasHeld then
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
      + (interact and 256 or 0)

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
  fpsVisual.updateExplosionLights(visualNow)
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
  fpsAudio.update(dt)
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
  hud.updateAimTarget()
  hud.publish(dt)
end

function hud.drawAimNameplate(size, scale, position, name, health, maximumHealth, friendly)
  local offset = position - ac.getCameraPosition()
  local forward = ac.getCameraForward()
  if offset.x * forward.x + offset.y * forward.y + offset.z * forward.z <= 0 then return end
  local ok, point = pcall(render.projectPoint, position, render.ProjectFace.Center)
  if not ok or point == nil or point.x ~= point.x or point.y ~= point.y
      or point.x < 0 or point.x > 1 or point.y < 0 or point.y > 1 then return end
  local anchor = vec2(point.x * size.x, point.y * size.y)
  local color = friendly and rgbm(0.18, 0.58, 1, 1) or rgbm(1, 0.2, 0.16, 1)
  local textMin, textMax = anchor + vec2(-112, -39) * scale, anchor + vec2(112, -13) * scale
  ui.dwriteDrawTextClipped(name, 19 * scale, textMin + vec2(1, 1) * scale,
    textMax + vec2(1, 1) * scale, ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0, 0, 0, 0.95))
  ui.dwriteDrawTextClipped(name, 19 * scale, textMin, textMax,
    ui.Alignment.Center, ui.Alignment.Center, false, color)
  local barMin, barMax = anchor + vec2(-62, -10) * scale, anchor + vec2(62, -3) * scale
  ui.drawRectFilled(barMin - vec2(2, 2) * scale, barMax + vec2(2, 2) * scale,
    rgbm(0.015, 0.02, 0.03, 0.95), 3 * scale)
  ui.drawRectFilled(barMin, barMax, rgbm(0.12, 0.14, 0.17, 0.95), 2 * scale)
  local ratio = math.clamp(health / math.max(1, maximumHealth), 0, 1)
  ui.drawRectFilled(barMin, vec2(barMin.x + (barMax.x - barMin.x) * ratio, barMax.y), color, 2 * scale)
end

function hud.drawAimTarget(size, scale)
  local target, own = hud.aimTarget, actors[localSessionID]
  if target == nil or own == nil or cursorUnlocked or scoreboardHeld or target.health <= 0 then return end
  hud.drawAimNameplate(size, scale, hud.aimTargetPosition,
    names[target.id] or ('Operative ' .. target.id), target.health, hud.maximumHealth,
    isTeamMatch() and own.team ~= 0 and own.team == target.team)
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

function hud.drawPickupPrompt(size, scale)
  if cursorUnlocked or scoreboardHeld or hud.pickupPromptText == '' then return end
  local width = math.min(720 * scale, size.x * 0.82)
  local center = size * 0.5
  local textMin = vec2(center.x - width * 0.5, center.y + 188 * scale)
  local textMax = textMin + vec2(width, 34 * scale)
  ui.dwriteDrawTextClipped(hud.pickupPromptText, 23 * scale,
    textMin + vec2(1.5, 1.5) * scale, textMax + vec2(1.5, 1.5) * scale,
    ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0, 0, 0, 0.92))
  ui.dwriteDrawTextClipped(hud.pickupPromptText, 23 * scale, textMin, textMax,
    ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0.92, 0.97, 1, 1))
  local barMin = vec2(center.x - 145 * scale, center.y + 228 * scale)
  local barMax = vec2(center.x + 145 * scale, center.y + 233 * scale)
  ui.drawRectFilled(barMin, barMax, rgbm(0.09, 0.13, 0.17, 1), 3 * scale)
  ui.drawRectFilled(barMin, vec2(math.lerp(barMin.x, barMax.x,
    math.clamp(hud.pickupProgress, 0, 1)), barMax.y),
    rgbm(0.2, 0.72, 0.96, 1), 3 * scale)
end

function hud.drawGrenadeMarker(position, remaining, size, scale)
  local worldPosition = position + vec3(0, 0.32, 0)
  local ok, projected = pcall(render.projectPoint, worldPosition, render.ProjectFace.Center)
  if not ok or projected == nil or projected.x ~= projected.x or projected.y ~= projected.y then
    return
  end

  local center = size * 0.5
  local direction = projected - vec2(0.5, 0.5)
  local cameraOk, cameraForward = pcall(ac.getCameraForward)
  local toGrenade = worldPosition - ac.getCameraPosition()
  local inFront = not cameraOk or toGrenade.x * cameraForward.x
    + toGrenade.y * cameraForward.y + toGrenade.z * cameraForward.z > 0
  if not inFront then direction:scale(-1) end
  local onScreen = inFront and projected.x >= 0.035 and projected.x <= 0.965
    and projected.y >= 0.06 and projected.y <= 0.94
  local screenPosition
  if onScreen then
    screenPosition = vec2(projected.x * size.x, projected.y * size.y)
  else
    if direction:lengthSquared() < 0.0001 then direction = vec2(0, 1) end
    direction:normalize()
    local half = vec2(size.x * 0.5 - 42 * scale, size.y * 0.5 - 62 * scale)
    local xScale = math.abs(direction.x) > 0.001 and half.x / math.abs(direction.x) or 1e6
    local yScale = math.abs(direction.y) > 0.001 and half.y / math.abs(direction.y) or 1e6
    screenPosition = center + direction * math.min(xScale, yScale)
  end

  local urgent = remaining <= 0.7
  local warning = remaining <= 1.35
  local pulse = 1 + (0.5 + 0.5 * math.sin(ui.time() * 15)) * (urgent and 0.22 or 0.08)
  local color = urgent and rgbm(1, 0.18, 0.08, 1)
    or (warning and rgbm(1, 0.62, 0.12, 1) or rgbm(0.96, 0.98, 1, 1))
  local shadow = rgbm(0, 0, 0, 0.78)
  local radius = 6.5 * scale * pulse
  ui.drawCircleFilled(screenPosition + vec2(1.5, 1.5) * scale, radius + 2 * scale,
    shadow, 20)
  ui.drawCircleFilled(screenPosition, radius, color, 20)
  ui.drawRectFilled(screenPosition + vec2(-2.2, -10.5) * scale,
    screenPosition + vec2(2.2, -5) * scale, color, 1.5 * scale)
  ui.drawLine(screenPosition + vec2(1.5, -10) * scale,
    screenPosition + vec2(6.5, -8) * scale, color, math.max(1, 1.4 * scale))
  if onScreen then
    ui.drawTriangleFilled(screenPosition + vec2(0, -14) * scale,
      screenPosition + vec2(-5.5, -23) * scale,
      screenPosition + vec2(5.5, -23) * scale, color)
  else
    local perpendicular = vec2(-direction.y, direction.x)
    local tip = screenPosition + direction * 15 * scale
    local base = screenPosition - direction * 9 * scale
    ui.drawTriangleFilled(tip, base + perpendicular * 7 * scale,
      base - perpendicular * 7 * scale, color)
  end
end

function hud.drawGrenadeIndicators(size, scale)
  if cursorUnlocked or scoreboardHeld then return end
  local localActor = actors[localSessionID]
  if localActor == nil or bit.band(localActor.flags, 1) == 0
      or bit.band(localActor.flags, 2) ~= 0 then return end
  for _, grenade in pairs(fpsVisual.grenades) do
    local age = math.clamp(effectClock - (grenade.seenAt or effectClock), 0, 0.1)
    local remaining = grenade.remaining - age
    local position = fpsVisual.predictedGrenadePosition(grenade)
    if remaining > 0 and fpsVisual.isHostileGrenade(grenade, localActor)
        and (position - localActor.target):lengthSquared() <= 18 * 18 then
      hud.drawGrenadeMarker(position, remaining, size, scale)
    end
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
        local friendly = (hud.radarVisible[id] or 0) == 3
        ui.drawCircleFilled(center + point, 4.5 * scale,
          friendly and rgbm(0.18, 0.58, 1, 1) or rgbm(1, 0.22, 0.15, 0.95), 16)
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
  if isTeamMatch() then
    local width = 410 * scale
    local team1, team2 = {}, {}
    for place = 1, #ranking do
      local actor = ranking[place]
      if actor.team == 1 then team1[#team1 + 1] = actor
      elseif actor.team == 2 then team2[#team2 + 1] = actor end
    end
    local rows = math.min(4, math.max(#team1, #team2))
    local panelMin, panelMax = vec2(margin, top),
      vec2(margin + width, top + (78 + rows * 22) * scale)
    ui.drawRectFilled(panelMin, panelMax, rgbm(0.025, 0.035, 0.05, 0.9), 8 * scale)
    ui.drawRect(panelMin, panelMax, rgbm(0.38, 0.62, 0.78, 0.58), 8 * scale,
      nil, math.max(1, 1.2 * scale))
    ui.drawRectFilled(panelMin, vec2(margin + width * 0.5, top + 5 * scale),
      rgbm(0.12, 0.52, 1, 1))
    ui.drawRectFilled(vec2(margin + width * 0.5, top),
      vec2(margin + width, top + 5 * scale), rgbm(0.94, 0.2, 0.14, 1))
    ui.setCursor(vec2(margin + 12 * scale, top + 12 * scale))
    ui.textColored('TEAM 1', rgbm(0.46, 0.76, 1, 1))
    ui.setCursor(vec2(margin + 118 * scale, top + 8 * scale))
    ui.pushFont(ui.Font.Title); ui.text(tostring(team1Kills)); ui.popFont()
    ui.setCursor(vec2(margin + 217 * scale, top + 12 * scale))
    ui.textColored('TEAM 2', rgbm(1, 0.48, 0.4, 1))
    ui.setCursor(vec2(margin + 325 * scale, top + 8 * scale))
    ui.pushFont(ui.Font.Title); ui.text(tostring(team2Kills)); ui.popFont()
    for row = 1, rows do
      for team = 1, 2 do
        local actor = (team == 1 and team1 or team2)[row]
        if actor ~= nil then
          local x = margin + (team == 2 and 217 or 12) * scale
          local y = top + (49 + row * 22) * scale
          if actor.id == localSessionID then
            ui.drawRectFilled(vec2(x - 5 * scale, y - 2 * scale),
              vec2(x + 184 * scale, y + 19 * scale), rgbm(0.16, 0.45, 0.68, 0.42), 2 * scale)
          end
          ui.setCursor(vec2(x, y))
          ui.text(string.format('%-12s %2d/%2d',
            string.sub(names[actor.id] or ('Player ' .. actor.id), 1, 12),
            actor.kills, actor.deaths))
        end
      end
    end
  else
    local width = 340 * scale
    local rows = math.min(8, #ranking)
    local panelMin, panelMax = vec2(margin, top),
      vec2(margin + width, top + (55 + rows * 24) * scale)
    ui.drawRectFilled(panelMin, panelMax, rgbm(0.025, 0.035, 0.05, 0.88), 8 * scale)
    ui.drawRect(panelMin, panelMax, rgbm(0.38, 0.62, 0.78, 0.58), 8 * scale,
      nil, math.max(1, 1.2 * scale))
    ui.drawRectFilled(panelMin, vec2(margin + 5 * scale, panelMax.y),
      rgbm(0.93, 0.67, 0.15, 1), 2 * scale)
    ui.setCursor(vec2(margin + 15 * scale, top + 9 * scale))
    ui.textColored('FREE FOR ALL', rgbm(1, 0.83, 0.42, 1))
    ui.setCursor(vec2(margin + 15 * scale, top + 31 * scale))
    ui.textColored('POS  OPERATOR           SCORE   K/D', rgbm(0.55, 0.64, 0.72, 1))
    for place = 1, rows do
      local actor = ranking[place]
      local y = top + (36 + place * 24) * scale
      if actor.id == localSessionID then
        ui.drawRectFilled(vec2(margin + 8 * scale, y - 2 * scale),
          vec2(margin + width - 8 * scale, y + 20 * scale), rgbm(0.16, 0.45, 0.68, 0.42), 2 * scale)
      end
      ui.setCursor(vec2(margin + 15 * scale, y))
      ui.text(string.format('%2d   %-17s %5d  %2d/%2d', place,
        string.sub(names[actor.id] or ('Player ' .. actor.id), 1, 17),
        actor.score, actor.kills, actor.deaths))
    end
  end
end

function hud.drawFallbackScoreboard(size, scale, ranking)
  local center = size * 0.5
  ui.drawRectFilled(vec2(), size, rgbm(0.005, 0.008, 0.012, 0.52))
  local width = math.min((isTeamMatch() and 1180 or 900) * scale, size.x * 0.94)
  local height = math.min(640 * scale, size.y * 0.92)
  local p1, p2 = center - vec2(width, height) * 0.5, center + vec2(width, height) * 0.5
  ui.drawRectFilled(p1, p2, rgbm(0.025, 0.03, 0.04, 0.97), 8 * scale)
  ui.drawRect(p1, p2, rgbm(0.42, 0.53, 0.64, 0.72), 8 * scale, nil,
    math.max(1, 1.5 * scale))
  ui.drawRectFilled(p1, vec2(p2.x, p1.y + 6 * scale),
    isTeamMatch() and rgbm(0.12, 0.52, 1, 1) or rgbm(0.93, 0.67, 0.15, 1))
  ui.setCursor(p1 + vec2(28, 20) * scale)
  ui.textColored(isTeamMatch() and 'TEAM DEATHMATCH' or 'FREE FOR ALL',
    rgbm(0.72, 0.8, 0.86, 1))
  ui.setCursor(p1 + vec2(28, 43) * scale)
  ui.pushFont(ui.Font.Huge); ui.text('SCOREBOARD'); ui.popFont()
  ui.setCursor(vec2(p2.x - 270 * scale, p1.y + 31 * scale))
  ui.textAligned(string.format('%02d:%02d  •  TARGET %d',
    math.floor(math.max(0, remainingSeconds) / 60),
    math.floor(math.max(0, remainingSeconds) % 60), killLimit), 1, vec2(240, 24) * scale)

  if isTeamMatch() then
    local team1, team2 = {}, {}
    for place = 1, #ranking do
      local actor = ranking[place]
      if actor.team == 1 then team1[#team1 + 1] = actor
      elseif actor.team == 2 then team2[#team2 + 1] = actor end
    end
    local gap = 18 * scale
    local columnWidth = (width - 74 * scale - gap) * 0.5
    local columnTop = p1.y + 94 * scale
    local rowTop, rowHeight = columnTop + 86 * scale, 24 * scale
    local function drawTeamColumn(team, members, score, x, color)
      ui.drawRectFilled(vec2(x, columnTop), vec2(x + columnWidth, columnTop + 70 * scale),
        rgbm(color.r * 0.18, color.g * 0.18, color.b * 0.18, 0.96), 3 * scale)
      ui.drawRectFilled(vec2(x, columnTop), vec2(x + 6 * scale, columnTop + 70 * scale), color)
      ui.setCursor(vec2(x + 18 * scale, columnTop + 14 * scale))
      ui.textColored('TEAM ' .. team, color)
      ui.setCursor(vec2(x + columnWidth - 105 * scale, columnTop + 7 * scale))
      ui.pushFont(ui.Font.Huge)
      ui.textAligned(tostring(score), 1, vec2(86, 48) * scale)
      ui.popFont()
      ui.setCursor(vec2(x + 10 * scale, rowTop - 20 * scale))
      ui.textColored('OPERATOR', rgbm(0.54, 0.62, 0.7, 1))
      ui.setCursor(vec2(x + columnWidth - 210 * scale, rowTop - 20 * scale))
      ui.textColored('SCORE     K     D    HP', rgbm(0.54, 0.62, 0.7, 1))
      for row = 1, math.min(16, #members) do
        local actor = members[row]
        local y = rowTop + (row - 1) * rowHeight
        ui.drawRectFilled(vec2(x, y), vec2(x + columnWidth, y + rowHeight - 2 * scale),
          actor.id == localSessionID and rgbm(0.12, 0.4, 0.64, 0.72)
            or rgbm(0.06, 0.075, 0.095, row % 2 == 0 and 0.86 or 0.62), 2 * scale)
        ui.drawRectFilled(vec2(x, y), vec2(x + 3 * scale, y + rowHeight - 2 * scale), color)
        ui.setCursor(vec2(x + 10 * scale, y + 3 * scale))
        ui.text(string.format('%2d  %-18s', row,
          string.sub(names[actor.id] or ('Player ' .. actor.id), 1, 18)))
        ui.setCursor(vec2(x + columnWidth - 210 * scale, y + 3 * scale))
        ui.text(string.format('%5d   %3d   %3d   %3d', actor.score, actor.kills,
          actor.deaths, actor.health))
      end
    end
    drawTeamColumn(1, team1, team1Kills, p1.x + 28 * scale, rgbm(0.18, 0.58, 1, 1))
    drawTeamColumn(2, team2, team2Kills, p1.x + 28 * scale + columnWidth + gap,
      rgbm(1, 0.24, 0.18, 1))
  else
    local listX, listWidth = p1.x + 28 * scale, width - 56 * scale
    local rowTop, rowHeight = p1.y + 126 * scale, 27 * scale
    ui.drawRectFilled(vec2(listX, p1.y + 94 * scale),
      vec2(listX + listWidth, p1.y + 121 * scale), rgbm(0.08, 0.1, 0.13, 0.96), 2 * scale)
    ui.setCursor(vec2(listX + 14 * scale, p1.y + 99 * scale))
    ui.textColored('POS   OPERATOR', rgbm(0.62, 0.69, 0.75, 1))
    ui.setCursor(vec2(listX + listWidth - 330 * scale, p1.y + 99 * scale))
    ui.textColored('SCORE        KILLS     DEATHS      HP', rgbm(0.62, 0.69, 0.75, 1))
    for place = 1, math.min(16, #ranking) do
      local actor = ranking[place]
      local y = rowTop + (place - 1) * rowHeight
      ui.drawRectFilled(vec2(listX, y), vec2(listX + listWidth, y + rowHeight - 2 * scale),
        actor.id == localSessionID and rgbm(0.12, 0.4, 0.64, 0.72)
          or rgbm(0.06, 0.075, 0.095, place % 2 == 0 and 0.86 or 0.62), 2 * scale)
      ui.drawRectFilled(vec2(listX, y), vec2(listX + (place == 1 and 6 or 3) * scale,
        y + rowHeight - 2 * scale), place == 1 and rgbm(0.93, 0.67, 0.15, 1)
          or rgbm(0.3, 0.36, 0.42, 0.8))
      ui.setCursor(vec2(listX + 14 * scale, y + 4 * scale))
      ui.text(string.format('%2d    %-24s', place,
        string.sub(names[actor.id] or ('Player ' .. actor.id), 1, 24)))
      ui.setCursor(vec2(listX + listWidth - 330 * scale, y + 4 * scale))
      ui.text(string.format('%6d        %3d        %3d      %3d', actor.score,
        actor.kills, actor.deaths, actor.health))
    end
  end
  ui.transparentWindow('asrc-fps-scoreboard-controls',
    vec2(p1.x + 20 * scale, p2.y - 50 * scale), vec2(width - 40 * scale, 42 * scale),
    true, true, function()
      ui.setCursor(vec2(8, 8) * scale)
      if ui.checkbox('Keep mouse cursor visible after releasing TAB', persistentCursor) then
        persistentCursor = not persistentCursor
      end
      ui.sameLine(12 * scale)
      ui.textColored('Release TAB to close scoreboard', rgbm(0.75, 0.78, 0.84, 1))
    end)
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
  if not hud.loadout.catalogReceived then return false end
  for field, row in pairs(hud.loadoutRows) do
    if not hud.itemAllowed(hud.loadout[row.mask], hud.loadout[field]) then return false end
  end
  if fpsVisual.operatorModels[hud.loadout.operatorModel] == nil
      or not hud.itemAllowed(hud.loadout.allowedOperatorModels, hud.loadout.operatorModel)
      or not hud.skinAllowed(hud.loadout.operatorModel, hud.loadout.operatorSkin) then return false end
  hud.loadout.result = 'SENDING TO SERVER...'
  hud.loadoutSelectEvent({
    mainWeapon = hud.loadout.mainWeapon,
    lethal = hud.loadout.lethal,
    secondaryWeapon = hud.loadout.secondaryWeapon,
    operatorModel = hud.loadout.operatorModel,
    operatorSkin = hud.loadout.operatorSkin,
  })
  return true
end

function hud.drawLoadoutMenu(initialSelection, fromPreDrive)
  local size = ui.windowSize()
  local scale = math.min((size.x - 32) / 1600, (size.y - 32) / 880, 1.5)
  local panelSize = vec2(1600, 880) * scale
  local panelMin = (size - panelSize) * 0.5
  local mouse = (ui.mousePos() - panelMin) / scale
  local clicked = ui.mouseClicked(ui.MouseButton.Left)
  local accent = rgbm(0.05, 0.78, 0.96, 1)
  local white = rgbm(0.93, 0.96, 0.98, 1)
  local muted = rgbm(0.58, 0.72, 0.80, 1)
  local disabled = rgbm(0.30, 0.38, 0.43, 1)
  local border = rgbm(0.18, 0.27, 0.32, 1)
  fpsVisual.requestLoadoutAssets()
  ui.captureMouse(true)
  ui.setMouseCursor(ui.MouseCursor.Arrow)
  ui.drawRectFilled(vec2(), size, rgbm(0.006, 0.01, 0.016, 0.88))
  ui.drawRectFilled(panelMin, panelMin + panelSize, rgbm(0.022, 0.032, 0.038, 0.99), 12 * scale)
  ui.drawRect(panelMin, panelMin + panelSize, rgbm(0.20, 0.56, 0.70, 0.9), 12 * scale,
    nil, math.max(1, scale))

  local function point(x, y) return panelMin + vec2(x, y) * scale end
  local function hovered(x, y, w, h)
    return mouse.x >= x and mouse.x < x + w and mouse.y >= y and mouse.y < y + h
  end
  local function text(label, x, y, w, h, fontSize, color, centered)
    ui.dwriteDrawTextClipped(label, fontSize * scale, point(x, y), point(x + w, y + h),
      centered and ui.Alignment.Center or ui.Alignment.Start, ui.Alignment.Center, false, color)
  end
  local function rect(x, y, w, h, fill, stroke)
    ui.drawRectFilled(point(x, y), point(x + w, y + h), fill, 7 * scale)
    if stroke then ui.drawRect(point(x, y), point(x + w, y + h), stroke,
      7 * scale, nil, math.max(1, 1.5 * scale)) end
  end
  local function line(x1, y1, x2, y2, color, width)
    ui.drawLine(point(x1, y1), point(x2, y2), color, math.max(1, (width or 1) * scale))
  end
  local function check(x, y)
    line(x, y + 6, x + 5, y + 11, accent, 2)
    line(x + 5, y + 11, x + 15, y, accent, 2)
  end
  local function lock(x, y)
    ui.drawRect(point(x, y + 7), point(x + 9, y + 15), disabled, 1 * scale)
    ui.drawRect(point(x + 2, y + 1), point(x + 7, y + 10), disabled, 3 * scale)
  end
  local function button(label, x, y, w, h, enabled, primary)
    local hot = enabled and hovered(x, y, w, h)
    rect(x, y, w, h, hot and rgbm(0.08, 0.40, 0.52, 1)
      or primary and enabled and rgbm(0.03, 0.37, 0.52, 1) or rgbm(0.045, 0.075, 0.09, 1),
      enabled and (primary and accent or border) or border)
    text(label, x + 8, y, w - 16, h, 18, enabled and white or disabled, true)
    return hot and clicked
  end
  local function filters(field, x, y)
    local row = hud.loadoutRows[field]
    for _, category in ipairs(row.categories) do
      local enabled = #hud.loadoutRowItems(field, category[1]) > 0
      local selected = category[1] == row.filter
      local hot = enabled and hovered(x, y, category[3], 32)
      text(category[2], x + 10, y, category[3] - (enabled and 20 or 32), 30, 17,
        enabled and (selected and white or hot and accent or muted) or disabled)
      if not enabled then lock(x + category[3] - 22, y + 7) end
      if selected then line(x + 6, y + 32, x + category[3] - 6, y + 32, accent, 2) end
      if hot and clicked then hud.setLoadoutFilter(field, category[1]) end
      x = x + category[3]
    end
  end
  local function cards(field, x, y, width, height)
    local row = hud.loadoutRows[field]
    local items = hud.loadoutRowItems(field, row.filter)
    row.first = math.clamp(row.first, 1, math.max(1, #items - 1))
    if #items > 2 then
      if hovered(x, y, width, height) then
        local wheel = ui.mouseWheel()
        if wheel ~= 0 then hud.scrollLoadoutRow(field, wheel > 0 and -1 or 1) end
      end
      text(string.format('%d-%d / %d', row.first, math.min(row.first + 1, #items), #items),
        x + width - 196, y + height + 4, 112, 28, 14, muted, true)
      if button('<', x + width - 76, y + height + 3, 34, 29, row.first > 1, false) then
        hud.scrollLoadoutRow(field, -1)
      end
      if button('>', x + width - 34, y + height + 3, 34, 29, row.first < #items - 1, false) then
        hud.scrollLoadoutRow(field, 1)
      end
    end
    local cardWidth = (width - 16) / 2
    for index = row.first, math.min(row.first + 1, #items) do
      local item = items[index]
      local cx = x + (index - row.first) * (cardWidth + 16)
      local allowed = hud.loadout.catalogReceived and hud.itemAllowed(hud.loadout[row.mask], item.id)
      local hot = allowed and hovered(cx, y, cardWidth, height)
      if hot and clicked then
        hud.loadout[field] = item.id
        hud.loadout.dirty = true
        hud.loadout.result = fromPreDrive and 'SAVE THIS LOADOUT BEFORE DEPLOYING'
          or initialSelection and 'CONFIRM A LOADOUT TO JOIN'
          or 'CHANGES APPLY ON NEXT RESPAWN'
      end
      local selected = hud.loadout[field] == item.id and allowed
      rect(cx, y, cardWidth, height, selected and rgbm(0.035, 0.085, 0.11, 1)
        or hot and rgbm(0.06, 0.10, 0.12, 1) or rgbm(0.033, 0.047, 0.057, 1),
        selected and accent or hot and muted or border)
      local folder = fpsVisual.loadoutAssetFolder
      if folder ~= nil then
        local imageBottom = field == 'mainWeapon' and 8 or 36
        ui.drawImage(folder .. '/' .. item.image, point(cx + 18, y + 8),
          point(cx + cardWidth - 18, y + height - imageBottom),
          allowed and rgbm.colors.white or rgbm(0.35, 0.35, 0.35, 0.7), nil, nil, ui.ImageFit.Fit)
      else
        text(fpsVisual.loadoutAssetsFailed and 'IMAGE UNAVAILABLE' or 'LOADING IMAGE...',
          cx + 16, y + 52, cardWidth - 32, height - 114, 15, disabled, true)
      end
      if selected then
        check(cx + cardWidth - 123, y + 15)
        text('SELECTED', cx + cardWidth - 98, y + 9, 86, 25, 14, accent)
      elseif not allowed then
        text(hud.loadout.catalogReceived and 'SERVER LOCKED' or 'WAITING FOR SERVER',
          cx + 12, y + 8, cardWidth - 24, 25, 13, disabled)
      end
      text(hud.itemNames[item.id] or 'UNKNOWN', cx + 20, y + height - 58,
        cardWidth - 40, 31, field == 'mainWeapon' and 25 or 21, allowed and white or disabled)
      text(item.subtitle, cx + 20, y + height - 28, cardWidth - 40, 24, 16,
        allowed and muted or disabled)
    end
    if #items == 0 then text('NO ITEMS IN THIS CATEGORY', x, y, width, height, 19, muted, true) end
  end

  text('L O A D O U T', 42, 24, 500, 24, 15, muted)
  text(fpsVisual.modern and hud.loadoutTab == 'skin' and 'SELECT YOUR SKIN'
    or fpsVisual.modern and hud.loadoutTab == 'operator' and 'SELECT YOUR OPERATOR'
    or fromPreDrive and 'PREPARE TO DEPLOY'
    or initialSelection and 'SELECT YOUR GEAR' or 'CHANGE YOUR GEAR', 42, 49, 980, 52, 46, white)
  if fpsVisual.modern then
    requestRifleAssets()
    if button('GEAR', 1040, 53, 134, 42, true, hud.loadoutTab == nil or hud.loadoutTab == 'gear') then hud.loadoutTab = 'gear' end
    if button('OPERATOR', 1190, 53, 188, 42, true, hud.loadoutTab == 'operator') then hud.loadoutTab = 'operator' end
    if button('SKIN', 1394, 53, 164, 42, true, hud.loadoutTab == 'skin') then hud.loadoutTab = 'skin' end
  end
  text(fpsVisual.modern and hud.loadoutTab == 'skin' and 'Choose colors for your operator. Changes apply on your next spawn.'
    or fpsVisual.modern and hud.loadoutTab == 'operator' and (hud.loadout.allowedOperatorModels ~= 3
      and 'Your team assigns your operator. Choose its colors in the Skin tab.'
      or 'Choose your operator, then customize its colors in the Skin tab.')
    or fromPreDrive and 'Choose one item per slot, then save and return to the match briefing.'
    or initialSelection and 'Choose one item per slot. Confirm to spawn.'
    or 'Choose one item per slot. Changes apply on your next respawn.', 42, 105, 1300, 26, 18, muted)
  line(42, 145, 1558, 145, border)
  if fpsVisual.modern and hud.loadoutTab == 'skin' then
    local model = fpsVisual.operatorModels[hud.loadout.operatorModel] or fpsVisual.operatorModels[0]
    local count = hud.loadout.operatorModel == 1 and 3 or 2
    local width = (1516 - (count - 1) * 24) / count
    for id = 0, count - 1 do
      local skin = model.skins[id]
      local x = 42 + id * (width + 24)
      local allowed = hud.loadout.catalogReceived and hud.skinAllowed(hud.loadout.operatorModel, id)
      local selected = hud.loadout.operatorSkin == id
      local hot = allowed and hovered(x, 166, width, 610)
      if hot and clicked then
        hud.loadout.operatorSkin = id
        hud.loadout.dirty = true
        hud.loadout.result = fromPreDrive and 'SAVE YOUR SKIN BEFORE DEPLOYING'
          or initialSelection and 'CONFIRM YOUR SKIN TO SPAWN' or 'SKIN CHANGES ON NEXT RESPAWN'
      end
      rect(x, 166, width, 610, selected and rgbm(0.035, 0.085, 0.11, 1)
        or rgbm(0.033, 0.047, 0.057, 1), selected and accent or hot and muted or border)
      local portrait = fpsVisual.asset(skin.portrait)
      if portrait ~= nil and io.fileExists(portrait) then
        ui.drawImage(portrait, point(x + 24, 184), point(x + width - 24, 674),
          allowed and rgbm.colors.white or rgbm(0.35, 0.35, 0.35, 0.7), nil, nil, ui.ImageFit.Fit)
      end
      text(skin.name, x + 24, 690, width - 48, 40, 29, allowed and white or disabled)
      text(selected and 'SELECTED' or allowed and 'SELECT SKIN' or 'UNAVAILABLE',
        x + 24, 736, width - 48, 24, 17, selected and accent or muted)
    end
  elseif fpsVisual.modern and hud.loadoutTab == 'operator' then
    for id = 0, 1 do
      local descriptor = fpsVisual.operatorModels[id]
      local x = 42 + id * 774
      local allowed = hud.loadout.catalogReceived and hud.itemAllowed(hud.loadout.allowedOperatorModels, id)
      local selected = hud.loadout.operatorModel == id
      local hot = allowed and hovered(x, 166, 742, 610)
      if hot and clicked then
        hud.loadout.operatorModel = id
        if not hud.skinAllowed(id, hud.loadout.operatorSkin) then hud.loadout.operatorSkin = 0 end
        hud.loadout.dirty = true
        hud.loadout.result = fromPreDrive and 'SAVE YOUR OPERATOR BEFORE DEPLOYING'
          or initialSelection and 'CONFIRM TO SPAWN' or 'OPERATOR CHANGES ON NEXT RESPAWN'
      end
      rect(x, 166, 742, 610, selected and rgbm(0.035, 0.085, 0.11, 1)
        or rgbm(0.033, 0.047, 0.057, 1), selected and accent or hot and muted or border)
      local portrait = fpsVisual.asset(descriptor.portrait)
      if portrait ~= nil and io.fileExists(portrait) then
        ui.drawImage(portrait, point(x + 100, 184), point(x + 642, 682),
          allowed and rgbm.colors.white or rgbm(0.35, 0.35, 0.35, 0.7), nil, nil, ui.ImageFit.Fit)
      else
        text('LOADING OPERATOR...', x + 30, 360, 682, 60, 20, muted, true)
      end
      text(descriptor.name, x + 24, 688, 480, 42, 30, allowed and white or disabled)
      text(hud.loadout.allowedOperatorModels ~= 3 and (id == 1 and 'TEAM 2' or 'TEAM 1')
        or id == 1 and 'NIGHTWAR' or 'STANDARD ISSUE', x + 24, 733, 500, 26, 16, muted)
      text(not allowed and 'OTHER TEAM' or selected and 'SELECTED' or 'SELECT OPERATOR',
        x + 504, 700, 212, 42, 17, selected and accent or muted, true)
    end
  else
  text('01', 42, 158, 45, 30, 25, accent)
  text('MAIN WEAPON', 96, 158, 210, 30, 23, white)
  filters('mainWeapon', 320, 157)
  text('Locked categories: coming soon', 1215, 158, 343, 30, 14, muted)
  cards('mainWeapon', 42, 202, 1516, 234)
  line(42, 474, 1558, 474, border)
  text('02', 42, 487, 45, 30, 25, accent)
  text('SECONDARY WEAPON', 96, 487, 650, 30, 23, white)
  text('03', 816, 487, 45, 30, 25, accent)
  text('LETHAL EQUIPMENT', 870, 487, 670, 30, 23, white)
  filters('secondaryWeapon', 42, 523)
  text('One grenade per life', 816, 523, 700, 30, 17, muted)
  line(800, 492, 800, 784, border)
  cards('secondaryWeapon', 42, 568, 742, 184)
  cards('lethal', 816, 568, 742, 184)
  end
  line(42, 795, 1558, 795, border)

  local selectedCount = 0
  for field, row in pairs(hud.loadoutRows) do
    if hud.loadout.catalogReceived and hud.itemAllowed(hud.loadout[row.mask], hud.loadout[field]) then
      selectedCount = selectedCount + 1
    end
  end
  local summaryX = (initialSelection and not fromPreDrive) and 42 or 290
  local operatorValid = fpsVisual.operatorModels[hud.loadout.operatorModel] ~= nil
    and hud.itemAllowed(hud.loadout.allowedOperatorModels, hud.loadout.operatorModel)
    and hud.skinAllowed(hud.loadout.operatorModel, hud.loadout.operatorSkin)
  if selectedCount == 3 then check(summaryX, 814) end
  text(string.format('%d / 3 SLOTS SELECTED%s', selectedCount, fpsVisual.modern and ('  /  '
    .. ((fpsVisual.operatorModels[hud.loadout.operatorModel] or {}).name or 'SELECT OPERATOR')) or ''),
    summaryX + 28, 805, 680, 28, 17, white)
  text(hud.loadout.result, 972, 106, 586, 26, 15,
    selectedCount == 3 and muted or rgbm(1, 0.62, 0.25, 1))
  text(string.format('%s  /  %s  /  %s', hud.itemNames[hud.loadout.mainWeapon] or '?',
    hud.itemNames[hud.loadout.secondaryWeapon] or '?', hud.itemNames[hud.loadout.lethal] or '?'),
    summaryX, 834, 810, 24, 14, muted)
  if button(fromPreDrive and 'SAVE LOADOUT  >'
      or initialSelection and 'CONFIRM & SPAWN  >' or 'QUEUE FOR RESPAWN  >',
      1126, 809, 432, 53, selectedCount == 3 and operatorValid, true) then
    if fromPreDrive then
      hud.preDriveReturnAfterLoadout = true
      hud.preDriveDeployPending = false
      hud.preDriveStatus = 'CONFIRMING LOADOUT...'
    end
    hud.submitLoadout()
  end
  if (not initialSelection or fromPreDrive) and button(fromPreDrive and '<  MATCH BRIEFING'
      or '<  MATCH MENU', 42, 809, 220, 53, true, false) then
    if fromPreDrive then
      hud.preDriveReturnAfterLoadout = false
      hud.preDrivePage = 'briefing'
    else
      hud.pausePage = 'main'
    end
  end
end

function hud.tryStartFromDeployment()
  hud.preDriveStatus = 'DEPLOYING...'
  local ok, started = pcall(ac.tryToStart, true)
  if ok and started then
    ac.log('[ASRC FPS] deployment menu action: deploy')
    return true
  end
  hud.preDriveStatus = 'DEPLOY FAILED — OPEN THE NATIVE AC MENU AND PRESS DRIVE'
  ac.warn('[ASRC FPS] deployment menu could not start session: ' .. tostring(started))
  return false
end

function hud.requestDeployment()
  if hud.preDriveDeployPending then return end
  if not hud.loadout.catalogReceived then
    hud.preDriveStatus = 'WAITING FOR THE SERVER LOADOUT CATALOG...'
    return
  end
  if hud.loadout.confirmed and not hud.loadout.dirty then
    hud.tryStartFromDeployment()
    return
  end
  hud.preDriveDeployPending = true
  hud.preDriveReturnAfterLoadout = false
  hud.preDriveStatus = 'CONFIRMING LOADOUT...'
  if not hud.submitLoadout() then
    hud.preDriveDeployPending = false
    hud.preDriveStatus = 'SELECT ONE AVAILABLE ITEM IN EVERY LOADOUT SLOT'
  end
end

function hud.drawPreDriveMenu()
  local size = ui.windowSize()
  local scale = math.clamp(math.min((size.x - 32) / 1200, (size.y - 32) / 720), 0.70, 1.55)
  local panelSize = vec2(1200, 720) * scale
  local panelMin = (size - panelSize) * 0.5
  local panelMax = panelMin + panelSize
  local mouse = ui.mousePos()
  local clicked = ui.mouseClicked(ui.MouseButton.Left)
  local accent = rgbm(0.10, 0.72, 0.96, 1)
  local white = rgbm(0.94, 0.97, 1, 1)
  local muted = rgbm(0.55, 0.67, 0.76, 1)
  local border = rgbm(0.22, 0.38, 0.49, 0.78)
  local teamBlue = rgbm(0.18, 0.58, 1, 1)
  local teamRed = rgbm(0.95, 0.20, 0.17, 1)
  local aDown = ac.isGamepadButtonPressed(0, ac.GamepadButton.A)
  local bDown = ac.isGamepadButtonPressed(0, ac.GamepadButton.B)
  local xDown = ac.isGamepadButtonPressed(0, ac.GamepadButton.X)
  local aPressed = aDown and not hud.preDriveGamepadAWasDown
  local bPressed = bDown and not hud.preDriveGamepadBWasDown
  local xPressed = xDown and not hud.preDriveGamepadXWasDown
  hud.preDriveGamepadAWasDown = aDown
  hud.preDriveGamepadBWasDown = bDown
  hud.preDriveGamepadXWasDown = xDown

  ui.captureMouse(true)
  ui.setMouseCursor(ui.MouseCursor.Arrow)

  if hud.preDrivePage == 'loadout' then
    if bPressed or ac.isKeyPressed(ac.KeyIndex.Escape) then
      hud.preDriveReturnAfterLoadout = false
      hud.preDrivePage = 'briefing'
    else
      hud.drawLoadoutMenu(true, true)
    end
    return
  end

  ui.drawRectFilled(vec2(), size, rgbm(0.004, 0.008, 0.014, 0.72))
  ui.drawRectFilled(panelMin, panelMax, rgbm(0.018, 0.027, 0.038, 0.97), 10 * scale)
  ui.drawRect(panelMin, panelMax, border, 10 * scale, nil, math.max(1, 1.5 * scale))
  ui.drawRectFilled(panelMin, vec2(panelMax.x, panelMin.y + 5 * scale), accent, 10 * scale)

  local function point(x, y) return panelMin + vec2(x, y) * scale end
  local function hovered(x, y, w, h)
    local p1, p2 = point(x, y), point(x + w, y + h)
    return mouse.x >= p1.x and mouse.x <= p2.x and mouse.y >= p1.y and mouse.y <= p2.y
  end
  local function text(label, x, y, w, h, fontSize, color, alignment)
    ui.dwriteDrawTextClipped(label, fontSize * scale, point(x, y), point(x + w, y + h),
      alignment or ui.Alignment.Start, ui.Alignment.Center, false, color or white)
  end
  local function line(x1, y1, x2, y2, color, width)
    ui.drawLine(point(x1, y1), point(x2, y2), color,
      math.max(1, (width or 1) * scale))
  end
  local function button(label, x, y, w, h, enabled, primary, danger)
    local hot = enabled and hovered(x, y, w, h)
    local fill = danger and rgbm(0.42, 0.08, 0.07, 0.96)
      or primary and rgbm(0.03, 0.34, 0.49, 0.98) or rgbm(0.05, 0.10, 0.14, 0.96)
    if hot then
      fill = danger and rgbm(0.72, 0.16, 0.11, 1) or rgbm(0.09, 0.42, 0.56, 1)
    elseif not enabled then
      fill = rgbm(0.035, 0.055, 0.065, 0.92)
    end
    ui.drawRectFilled(point(x, y), point(x + w, y + h), fill, 5 * scale)
    ui.drawRect(point(x, y), point(x + w, y + h), enabled and (primary and accent or border)
      or rgbm(0.16, 0.21, 0.24, 0.75), 5 * scale, nil, math.max(1, 1.2 * scale))
    text(label, x + 8, y, w - 16, h, primary and 20 or 16,
      enabled and white or rgbm(0.30, 0.36, 0.40, 1), ui.Alignment.Center)
    return hot and clicked
  end

  local function controlsButton(label, position, dimensions, danger)
    local p2 = position + dimensions
    local hot = mouse.x >= position.x and mouse.x <= p2.x
      and mouse.y >= position.y and mouse.y <= p2.y
    local fill = danger and rgbm(0.42, 0.08, 0.07, 0.96) or rgbm(0.05, 0.10, 0.14, 0.96)
    if hot then fill = danger and rgbm(0.72, 0.16, 0.11, 1) or rgbm(0.09, 0.42, 0.56, 1) end
    ui.drawRectFilled(position, p2, fill, 5 * scale)
    ui.drawRect(position, p2, hot and accent or border, 5 * scale, nil,
      math.max(1, 1.2 * scale))
    ui.setCursor(position + vec2(14, 11) * scale)
    ui.text(label)
    return hot and clicked
  end

  if hud.preDrivePage == 'controls' then
    hud.drawControlsMenu(panelMin, panelSize, scale, controlsButton, 'deployment')
    if bPressed or ac.isKeyPressed(ac.KeyIndex.Escape) then
      hud.bindingCapture = nil
      hud.preDrivePage = 'briefing'
    end
    return
  end

  if ac.isKeyPressed(ac.KeyIndex.Return) or aPressed then hud.requestDeployment() end
  if ac.isKeyPressed(ac.KeyIndex.L) or xPressed then
    hud.preDrivePage = 'loadout'
    hud.preDriveLeaveArmed = false
    hud.loadout.result = hud.loadout.confirmed and 'CURRENT LOADOUT' or 'CONFIRM A LOADOUT TO JOIN'
  end
  if bPressed then hud.nativePreDriveMenu = true end

  local ok, trackName = pcall(ac.getTrackName)
  if not ok or trackName == nil or trackName == '' then trackName = ac.getTrackID() end
  local own = actors[localSessionID]
  local ownTeam = own and own.team or teams[localSessionID] or 0
  local status = matchState == 0 and (fpsVisual.startCountdownSeconds > 0
      and string.format('STARTING IN %d', math.max(1, math.ceil(fpsVisual.startCountdownSeconds)))
      or 'WAITING FOR PLAYERS') or 'MATCH IN PROGRESS'

  text('A S R C   F P S', 38, 26, 500, 22, 14, muted)
  text('DEPLOYMENT', 38, 47, 520, 52, 42, white)
  text(fpsVisual.matchModeTitle(), 38, 101, 520, 28, 18, accent)
  text(tostring(trackName), 660, 42, 500, 32, 18, muted, ui.Alignment.End)
  text(status, 660, 75, 500, 26, 15, white, ui.Alignment.End)
  line(38, 139, 1162, 139, border)
  line(444, 162, 444, 610, border)

  text('MATCH BRIEFING', 38, 160, 360, 34, 24, white)
  text('MODE', 38, 211, 120, 22, 13, muted)
  text(fpsVisual.matchModeTitle(), 170, 207, 230, 28, 17, white)
  text('OBJECTIVE', 38, 250, 120, 22, 13, muted)
  text(matchTargetText(), 170, 246, 230, 28, 17, white)
  text('TIME', 38, 289, 120, 22, 13, muted)
  text(string.format('%02d:%02d', math.floor(remainingSeconds / 60),
    math.floor(remainingSeconds % 60)), 170, 285, 230, 28, 17, white)
  text('ASSIGNMENT', 38, 328, 120, 22, 13, muted)
  text(isTeamMatch() and (ownTeam == 2 and 'TEAM 2' or ownTeam == 1 and 'TEAM 1'
    or 'AUTO-BALANCE') or 'FREE AGENT', 170, 324, 230, 28, 17,
    ownTeam == 2 and teamRed or ownTeam == 1 and teamBlue or white)

  line(38, 371, 412, 371, border)
  text('ACTIVE LOADOUT', 38, 389, 360, 26, 17, muted)
  text(hud.itemNames[hud.loadout.mainWeapon] or 'WAITING FOR CATALOG', 38, 421, 360, 30, 22, white)
  text((hud.itemNames[hud.loadout.secondaryWeapon] or 'SECONDARY') .. '  /  '
    .. (hud.itemNames[hud.loadout.lethal] or 'LETHAL'), 38, 454, 360, 25, 15, muted)
  if button('CUSTOMIZE LOADOUT   [L / X]', 38, 495, 374, 48,
      hud.loadout.catalogReceived, false, false) then
    hud.preDrivePage = 'loadout'
    hud.preDriveLeaveArmed = false
    hud.loadout.result = hud.loadout.confirmed and 'CURRENT LOADOUT' or 'CONFIRM A LOADOUT TO JOIN'
  end
  text(hud.preDriveStatus ~= '' and hud.preDriveStatus or hud.loadout.result,
    38, 555, 374, 40, 13, hud.preDriveDeployPending and accent or muted)

  local roster = {}
  for id, name in pairs(names) do
    roster[#roster + 1] = { id = id, name = name, actor = actors[id], team = teams[id] or 0 }
  end
  table.sort(roster, function(a, b)
    if a.team ~= b.team then return a.team < b.team end
    local aScore = a.actor and a.actor.score or 0
    local bScore = b.actor and b.actor.score or 0
    if aScore ~= bScore then return aScore > bScore end
    return a.id < b.id
  end)
  text('ROSTER', 478, 160, 684, 34, 24, white)
  text(string.format('%d CONNECTED', #roster), 918, 164, 244, 26, 14, muted, ui.Alignment.End)

  local function rosterHeader(label, score, x, width, color)
    ui.drawRectFilled(point(x, 205), point(x + width, 249), rgbm(color.r, color.g, color.b, 0.14), 4 * scale)
    ui.drawRectFilled(point(x, 205), point(x + 4, 249), color, 2 * scale)
    text(label, x + 16, 205, width - 100, 44, 18, white)
    text(tostring(score), x + width - 82, 205, 66, 44, 23, color, ui.Alignment.End)
  end
  local function rosterRows(team, x, y, width, maximum)
    local row = 0
    for index = 1, #roster do
      local entry = roster[index]
      if team == nil or entry.team == team then
        if row >= maximum then break end
        local actor = entry.actor
        local color = entry.id == localSessionID and accent or white
        if row % 2 == 0 then
          ui.drawRectFilled(point(x, y + row * 35), point(x + width, y + row * 35 + 33),
            rgbm(0.05, 0.075, 0.095, 0.72), 2 * scale)
        end
        text(entry.id == localSessionID and '>  ' .. entry.name or entry.name,
          x + 12, y + row * 35, width - 150, 33, 15, color)
        text(string.format('%d / %d', actor and actor.kills or 0, actor and actor.deaths or 0),
          x + width - 132, y + row * 35, 120, 33, 14, muted, ui.Alignment.End)
        row = row + 1
      end
    end
    if row == 0 then text('WAITING FOR OPERATIVES', x + 12, y, width - 24, 38, 14, muted) end
  end
  if isTeamMatch() then
    local columnWidth = 326
    rosterHeader('TEAM 1', team1Kills, 478, columnWidth, teamBlue)
    rosterHeader('TEAM 2', team2Kills, 836, columnWidth, teamRed)
    rosterRows(1, 478, 260, columnWidth, 9)
    rosterRows(2, 836, 260, columnWidth, 9)
  else
    rosterHeader('FREE FOR ALL', #roster, 478, 684, accent)
    rosterRows(nil, 478, 260, 684, 9)
  end

  line(38, 623, 1162, 623, border)
  if button(hud.preDriveDeployPending and 'CONFIRMING...' or 'DEPLOY   [ENTER / A]',
      750, 642, 412, 52, hud.loadout.catalogReceived and not hud.preDriveDeployPending,
      true, false) then hud.requestDeployment() end
  if button('FPS CONTROLS', 38, 642, 180, 52, true, false, false) then
    hud.preDrivePage = 'controls'
    hud.preDriveLeaveArmed = false
  end
  if button('NATIVE AC MENU   [B]', 228, 642, 220, 52, true, false, false) then
    ac.log('[ASRC FPS] deployment menu action: native AC menu')
    hud.nativePreDriveMenu = true
    hud.preDriveLeaveArmed = false
  end
  if not hud.preDriveLeaveArmed then
    if button('LEAVE SERVER', 458, 642, 170, 52, true, false, true) then
      hud.preDriveLeaveArmed = true
    end
  else
    if button('CONFIRM LEAVE', 458, 642, 170, 52, true, false, true) then
      ac.log('[ASRC FPS] deployment menu action: leave server confirmed')
      ac.shutdownAssettoCorsa()
    end
    if button('CANCEL', 638, 642, 102, 52, true, false, false) then
      hud.preDriveLeaveArmed = false
    end
  end
  text('F2 restores this FPS deployment screen from the native AC menu.',
    478, 603, 684, 18, 12, muted, ui.Alignment.End)
end

local function drawMatchStartOverlay(size, scale)
  if matchState ~= 0 then return end
  local center = size * 0.5
  ui.drawRectFilled(vec2(), size, rgbm(0.005, 0.008, 0.012, 0.28))
  local panelMin = center + vec2(-330, -170) * scale
  local panelMax = center + vec2(330, 170) * scale
  ui.drawRectFilled(panelMin, panelMax, rgbm(0.025, 0.035, 0.05, 0.88), 5 * scale)
  ui.drawRectFilled(panelMin, vec2(panelMax.x, panelMin.y + 5 * scale),
    rgbm(0.18, 0.62, 0.96, 1), 5 * scale)
  ui.dwriteDrawTextClipped('GAME MODE', 22 * scale,
    center + vec2(-330, -125) * scale, center + vec2(330, -95) * scale,
    ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0.45, 0.72, 0.95, 1))
  ui.dwriteDrawTextClipped(fpsVisual.matchModeTitle(), 44 * scale,
    center + vec2(-330, -88) * scale, center + vec2(330, -28) * scale,
    ui.Alignment.Center, ui.Alignment.Center, false, rgbm.colors.white)
  if fpsVisual.startCountdownSeconds > 0 then
    ui.dwriteDrawTextClipped('MATCH STARTS IN', 18 * scale,
      center + vec2(-330, 0) * scale, center + vec2(330, 28) * scale,
      ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0.7, 0.76, 0.82, 1))
    ui.dwriteDrawTextClipped(tostring(math.max(1,
      math.ceil(fpsVisual.startCountdownSeconds))),
      82 * scale, center + vec2(-330, 30) * scale, center + vec2(330, 140) * scale,
      ui.Alignment.Center, ui.Alignment.Center, false, rgbm(1, 0.72, 0.18, 1))
  else
    ui.dwriteDrawTextClipped('WAITING FOR FIRST HUMAN PLAYER', 23 * scale,
      center + vec2(-330, 12) * scale, center + vec2(330, 52) * scale,
      ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0.78, 0.82, 0.86, 1))
  end
end

function hud.drawMatchResults(size, rows, modeTitle, winnerText, countdown, localID, teamScores)
  local scale = math.min((size.x - 48) / 1400, (size.y - 48) / 820, 1.5)
  local p = (size - vec2(1400, 820) * scale) * 0.5
  local gold = rgbm(1, 0.74, 0.25, 1)
  local white = rgbm(0.93, 0.96, 1, 1)
  local muted = rgbm(0.58, 0.66, 0.75, 1)
  local function text(value, x, y, width, height, fontSize, color, align)
    ui.dwriteDrawTextClipped(tostring(value), fontSize * scale,
      p + vec2(x, y) * scale, p + vec2(x + width, y + height) * scale,
      align or ui.Alignment.Start, ui.Alignment.Center, false, color)
  end
  local function rect(x, y, width, height, color)
    ui.drawRectFilled(p + vec2(x, y) * scale,
      p + vec2(x + width, y + height) * scale, color)
  end
  ui.drawRectFilled(vec2(), size, rgbm(0.005, 0.008, 0.012, 0.82))
  rect(0, 0, 1400, 820, rgbm(0.025, 0.035, 0.05, 0.98))
  rect(0, 0, 1400, 5, gold)
  text('MATCH COMPLETE', 32, 24, 870, 62, 46, white)
  text(modeTitle .. '  /  FINAL STANDINGS', 34, 91, 860, 28, 18, muted)
  text(winnerText, 32, 130, 860, 48, 30, gold)
  text('NEXT MATCH IN', 940, 27, 425, 32, 18, muted, ui.Alignment.End)
  text(tostring(math.max(0, math.ceil(countdown))) .. 's', 940, 62, 425, 62,
    48, gold, ui.Alignment.End)
  text(teamScores or 'SAME ARENA  /  TEAMS AND LOADOUTS RETAINED',
    880, 139, 485, 34, 17, white, ui.Alignment.End)
  rect(32, 190, 1336, 1, rgbm(0.2, 0.27, 0.34, 1))

  local columns = #rows > 16 and 2 or 1
  local columnWidth = columns == 2 and 650 or 1336
  local fields = {
    {label = '#', x = 0.012, width = 0.05},
    {label = 'OPERATOR', x = 0.075, width = 0.405},
    {label = 'SCORE', x = 0.49, width = 0.13},
    {label = 'KILLS', x = 0.635, width = 0.10},
    {label = 'DEATHS', x = 0.747, width = 0.105},
    {label = 'K/D', x = 0.868, width = 0.12},
  }
  for column = 0, columns - 1 do
    local x = 32 + column * 686
    for _, field in ipairs(fields) do
      text(field.label, x + field.x * columnWidth, 201, field.width * columnWidth,
        28, 15, muted, field.x >= 0.49 and ui.Alignment.End or ui.Alignment.Start)
    end
    for row = 1, 16 do
      local place = column * 16 + row
      local actor = rows[place]
      if actor ~= nil then
        local y = 238 + (row - 1) * 31
        local own = actor.id == localID
        rect(x, y, columnWidth, 29, own and rgbm(0.12, 0.32, 0.48, 1)
          or rgbm(0.065, 0.085, 0.11, row % 2 == 0 and 0.9 or 0.55))
        local stripe = actor.team == 1 and rgbm(0.2, 0.58, 1, 1)
          or actor.team == 2 and rgbm(1, 0.28, 0.22, 1) or place == 1 and gold or muted
        rect(x, y, 3, 29, stripe)
        local values = {place, actor.name, actor.score, actor.kills, actor.deaths,
          string.format('%.2f', actor.kills / math.max(1, actor.deaths))}
        for index, field in ipairs(fields) do
          text(values[index], x + field.x * columnWidth, y, field.width * columnWidth,
            29, index == 2 and 20 or 18, own and white or rgbm(0.82, 0.88, 0.94, 1),
            field.x >= 0.49 and ui.Alignment.End or ui.Alignment.Start)
        end
      end
    end
  end
  text(#rows .. ' OPERATORS', 32, 759, 400, 32, 16, muted)
  text('NEXT ROUND STARTS AUTOMATICALLY', 790, 759, 576, 32, 16, muted, ui.Alignment.End)
  rect(32, 801, 1336, 3, rgbm(0.1, 0.14, 0.18, 1))
  rect(32, 801, 1336 * math.clamp(countdown / 20, 0, 1), 3, gold)
end

function hud.drawCompletion(size)
  local rows = {}
  for _, actor in pairs(actors) do
    if bit.band(actor.flags, 1) ~= 0 then
      rows[#rows + 1] = {id = actor.id, name = names[actor.id] or ('Player ' .. actor.id),
        score = actor.score, kills = actor.kills, deaths = actor.deaths, team = actor.team}
    end
  end
  table.sort(rows, function(a, b)
    if a.kills ~= b.kills then return a.kills > b.kills end
    if a.deaths ~= b.deaths then return a.deaths < b.deaths end
    return a.id < b.id
  end)
  hud.drawMatchResults(size, rows, fpsVisual.matchModeTitle(),
    isTeamMatch() and (winnerTeam == 0 and 'DRAW' or ('WINNER: TEAM ' .. winnerTeam))
      or ('WINNER: ' .. (names[winnerID] or 'No winner')),
    math.max(0, fpsVisual.restartCountdownSeconds - (ui.time() - fpsVisual.restartCountdownUpdatedAt)),
    localSessionID, isTeamMatch() and string.format('TEAM 1   %d  :  %d   TEAM 2', team1Kills, team2Kills) or nil)
end

function script.drawUI()
  if hud.exclusiveSubscription ~= nil and not hud.drawingFallback then return end
  viewmodelDrawUICalls = viewmodelDrawUICalls + 1
  if not gameplayActive then return end
  if matchState == 2 then
    hud.drawCompletion(ui.windowSize())
    return
  end
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
  hud.drawAimTarget(size, hudScale)
  hud.drawGrenadeIndicators(size, hudScale)
  hud.drawPickupPrompt(size, hudScale)
  drawMatchStartOverlay(size, hudScale)
  if outOfBoundsRemaining > 0 then
    local titleMin = vec2(0, center.y - 205 * hudScale)
    local titleMax = vec2(size.x, center.y - 125 * hudScale)
    local numberMin = vec2(0, center.y - 120 * hudScale)
    local numberMax = vec2(size.x, center.y + 145 * hudScale)
    local countdown = tostring(math.max(1, math.ceil(outOfBoundsRemaining)))
    ui.drawRectFilled(vec2(), size, rgbm(0.12, 0, 0, 0.34))
    ui.dwriteDrawTextClipped('RETURN TO PLAYABLE AREA', 46 * hudScale,
      titleMin + vec2(2, 2) * hudScale, titleMax + vec2(2, 2) * hudScale,
      ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0, 0, 0, 0.9))
    ui.dwriteDrawTextClipped('RETURN TO PLAYABLE AREA', 46 * hudScale,
      titleMin, titleMax, ui.Alignment.Center, ui.Alignment.Center, false,
      rgbm(1, 0.86, 0.82, 1))
    ui.dwriteDrawTextClipped(countdown, 190 * hudScale,
      numberMin + vec2(4, 4) * hudScale, numberMax + vec2(4, 4) * hudScale,
      ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0, 0, 0, 0.92))
    ui.dwriteDrawTextClipped(countdown, 190 * hudScale, numberMin, numberMax,
      ui.Alignment.Center, ui.Alignment.Center, false, rgbm(1, 0.24, 0.14, 1))
  end

  local actor = actors[localSessionID]
  if clientPackError ~= nil then
    ui.setCursor(vec2(hudMargin, size.y - hudMargin - 174 * hudScale))
    ui.pushStyleColor(ui.StyleColor.Text, rgbm(1, 0.18, 0.12, 1))
    ui.text(clientPackError)
    ui.popStyleColor()
  end
  hud.drawFallbackStatusWidgets(size, hudScale, hudMargin, actor)
  ui.setCursor(vec2(center.x - 140, 20))
  ui.textAligned(string.format('%02d:%02d   %s', math.floor(remainingSeconds / 60),
    math.floor(remainingSeconds % 60), matchTargetText()), 0.5, vec2(280, 24))

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
    hud.drawFallbackScoreboard(size, hudScale, ranking)
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

end

function hud.drawControlsMenu(panelMin, panelSize, scale, pauseButton, returnPage)
  local controls = {
    { label = 'FIRE', action = 'fire' },
    { label = 'SPRINT', action = 'sprint' },
    { label = 'CROUCH / PRONE', action = 'crouch', crouchMode = true },
    { label = 'RELOAD', action = 'reload' },
    { label = 'JUMP', action = 'jump' },
    { label = 'GRENADE', action = 'grenade' },
    { label = 'INTERACT / PICK UP', action = 'interact' },
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
  ui.textColored('XBOX:  Y  SWITCH WEAPON    •    RB  GRENADE    •    X  PICK UP',
    rgbm(0.72, 0.8, 0.86, 1))

  for index = 1, #controls do
    local item = controls[index]
    local rowY = 82 + (index - 1) * 42
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
  if pauseButton(returnPage == 'deployment' and 'BACK TO DEPLOYMENT' or 'BACK TO MATCH MENU',
      left + vec2(0, 530) * scale,
      vec2(260, 42) * scale, false) then
    hud.bindingCapture = nil
    if returnPage == 'deployment' then
      hud.preDrivePage = 'briefing'
    else
      hud.pausePage = 'main'
    end
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
  ui.textColored(matchLabel() .. '  •  LIVE SERVER', rgbm(0.46, 0.78, 0.95, 1))
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
  ui.text(string.format('%02d:%02d remaining  •  %s',
    math.floor(remainingSeconds / 60), math.floor(remainingSeconds % 60),
    string.lower(matchTargetText())))
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
  if mode ~= 'pause' and mode ~= 'menu' then
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
  if mode == 'menu' and not previewCamera.everEnteredGameplay then
    if hud.nativePreDriveMenu then
      if ac.isKeyPressed(ac.KeyIndex.F2) then
        hud.nativePreDriveMenu = false
        hud.preDriveStatus = 'FPS DEPLOYMENT RESTORED'
        ac.log('[ASRC FPS] native pre-Drive menu released: FPS deployment restored')
      else
        return false
      end
    end
    hud.drawPreDriveMenu()
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

hud.readySent = hud.readyEvent({ protocol = 6 })
ac.log(string.format('[ASRC FPS] ready sent: protocol=6 result=%s', tostring(hud.readySent)))
