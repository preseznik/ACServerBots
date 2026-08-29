local license = [[
Copyright (C) 2026 Niewiarowski, compujuckel

This program is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or any later version.
]]

local capacity = 16
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
local viewmodelPipelineVersion = 'render-camera-v12-authoritative-remote-pose'
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
local viewmodelRenderParams = nil
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
local remoteAvatarTemplateHolder = nil
local remoteAvatarTemplateRoot = nil
local remoteAvatarRenderParams = nil
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
local rifleAssetArchivePath = '/fps/assets/asrc-fps-assets-v6.zip'
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
local crouchWasHeld = false
local crouchHeldSeconds = 0
local crouchLatched = false
local cameraHeight = 1.65
local thirdPersonEnabled = false
local thirdPersonToggleWasHeld = false
local localAvatarReady = false
local localAvatarKind = 'none'
local localAvatarErrorLogged = false
local scoreboardHeld = false
local persistentCursor = false
local cursorUnlocked = false
local camera = nil
local cameraError = nil
local fpsNearClip = 0.03
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

local function vec3Text(value)
  return string.format('(%.3f, %.3f, %.3f)', value.x, value.y, value.z)
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
  'false', 'not acquired until Drive'))
ac.log(string.format('[ASRC FPS] client asset paths: root=%s remoteArchive=%s audio=%s',
  tostring(assettoRoot), rifleAssetArchivePath, rifleAudioPath))
ac.log('[ASRC FPS] viewmodel pipeline: ' .. viewmodelPipelineVersion)

-- FPS has its own match clock, scoreboard and damage display. In particular,
-- the stock leaderboard assumes the local AC car is driving a normal timed
-- session and crashes when the carrier is used only as a network identity.
ac.disableExtraHUDElements({
  'sessionTime', 'fuel', 'proximity', 'leaderboard', 'startingLights',
  'wrongWay', 'damage', 'quickPitsMenu',
}, true)
ac.disableQuickMenuPitstop(true)
physics.setGentleStop(car.index, true)

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

local inputEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsInput'),
  sequence = ac.StructItem.uint32(),
  move = ac.StructItem.vec2(),
  yaw = ac.StructItem.float(),
  pitch = ac.StructItem.float(),
  buttons = ac.StructItem.byte(),
}, function() end, nil, true)

local readyEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsReady'),
  protocol = ac.StructItem.uint16(),
}, function() end)

local clientDiagnosticEvent = ac.OnlineEvent({
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

local snapshotEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsSnapshot'),
  sequence = ac.StructItem.uint32(),
  count = ac.StructItem.byte(),
  actorIDs = ac.StructItem.array(ac.StructItem.byte(), capacity),
  flags = ac.StructItem.array(ac.StructItem.byte(), capacity),
  spawnCounts = ac.StructItem.array(ac.StructItem.uint32(), capacity),
  positions = ac.StructItem.array(ac.StructItem.vec3(), capacity),
  groundYs = ac.StructItem.array(ac.StructItem.float(), capacity),
  collisionDirections = ac.StructItem.array(ac.StructItem.byte(), capacity),
  yaws = ac.StructItem.array(ac.StructItem.float(), capacity),
  pitches = ac.StructItem.array(ac.StructItem.float(), capacity),
  health = ac.StructItem.array(ac.StructItem.uint16(), capacity),
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
        pitch = 0, health = 0, kills = 0, deaths = 0, flags = 0,
        ammo = 0, reserveMagazines = 0, reloadRemaining = 0, spawnCount = nil,
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
    -- Remote actors have no client-side prediction. Keep their visual pose on the
    -- exact authoritative snapshot so a rendered mannequin can never drift away
    -- from the server capsule used by hitscan validation.
    if id ~= localSessionID then
      actor.render:set(actor.target)
      actor.yaw = actor.targetYaw
    end
    actor.pitch = message.pitches[i]
    actor.health = message.health[i]
    actor.kills = message.kills[i]
    actor.deaths = message.deaths[i]
    actor.ammo = message.ammo[i]
    actor.reserveMagazines = message.reserveMagazines[i]
    actor.reloadRemaining = message.reloadRemaining[i]
    actor.flags = message.flags[i]
    actor.spawnCount = message.spawnCounts[i]
    local spawnChanged = previousSpawnCount ~= nil and previousSpawnCount ~= actor.spawnCount
    local wasDead = bit.band(previousFlags, 2) ~= 0
    local isDead = bit.band(actor.flags, 2) ~= 0
    if clearActorImpacts ~= nil and (spawnChanged or (not wasDead and isDead)) then
      clearActorImpacts(id)
    end
    if spawnChanged then
      actor.render:set(actor.target)
      actor.yaw = actor.targetYaw
      actor.weaponKick = 0
      hitMarkerUntil = effectClock
      ac.log(string.format(
        '[ASRC FPS] remote actor respawn reconciled: actor=%s spawn=%s position=%s',
        tostring(id), tostring(actor.spawnCount), vec3Text(actor.target)))
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
        predictionCollisionConstrained = geometryBlocked
        predictionClearSnapshots = 0
        jumpWasHeld = false
        crouchWasHeld = false
        crouchHeldSeconds = 0
        crouchLatched = false
        cameraHeight = 1.65
      end
    end
  end
end, nil, true)

local rosterEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsRoster'),
  actorID = ac.StructItem.byte(),
  role = ac.StructItem.byte(),
  name = ac.StructItem.string(32),
}, function(sender, message)
  if sender ~= nil then return end
  names[message.actorID] = message.name
  local actor = actors[message.actorID]
  if actor ~= nil then actor.role = message.role end
end)

local matchEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsMatch'),
  state = ac.StructItem.byte(),
  remainingSeconds = ac.StructItem.float(),
  killLimit = ac.StructItem.uint16(),
  winnerID = ac.StructItem.byte(),
}, function(sender, message)
  if sender ~= nil then return end
  matchState = message.state
  remainingSeconds = message.remainingSeconds
  killLimit = message.killLimit
  winnerID = message.winnerID
end)

clearActorImpacts = function(actorID)
  for index = #impacts, 1, -1 do
    if impacts[index].targetID == actorID then table.remove(impacts, index) end
  end
end

local killEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsKill'),
  killerID = ac.StructItem.byte(),
  victimID = ac.StructItem.byte(),
  killerKills = ac.StructItem.uint16(),
  victimDeaths = ac.StructItem.uint16(),
}, function(sender, message)
  if sender ~= nil then return end
  killFeed[#killFeed + 1] = {
    text = (names[message.killerID] or ('Player ' .. message.killerID)) .. '  >  '
      .. (names[message.victimID] or ('Player ' .. message.victimID)),
    ttl = 4,
  }
  if message.killerID == localSessionID then hitMarkerUntil = effectClock + 0.22 end
  clearActorImpacts(message.victimID)
end)

local hitEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsHit'),
  attackerID = ac.StructItem.byte(),
  victimID = ac.StructItem.byte(),
  remainingHealth = ac.StructItem.uint16(),
}, function(sender, message)
  if sender == nil and message.attackerID == localSessionID then
    hitMarkerUntil = effectClock + 0.16
  end
end)

local shotEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsShot'),
  shooterID = ac.StructItem.byte(),
  sequence = ac.StructItem.uint32(),
  origin = ac.StructItem.vec3(),
  direction = ac.StructItem.vec3(),
  distance = ac.StructItem.float(),
  impact = ac.StructItem.byte(),
  targetID = ac.StructItem.byte(),
}, function(sender, message)
  if sender ~= nil then return end
  shotRender.eventsReceived = shotRender.eventsReceived + 1
  local actor = actors[message.shooterID]
  local muzzleOrigin = message.origin:clone()
  if message.shooterID == localSessionID and localMuzzlePosition:lengthSquared() > 0.001 then
    muzzleOrigin:set(localMuzzlePosition)
    viewmodelKick = 1
    pitch = math.min(1.45, pitch + 0.011)
  elseif actor ~= nil then
    local forward = vec3(math.sin(actor.targetYaw), 0, math.cos(actor.targetYaw))
    local right = vec3(forward.z, 0, -forward.x)
    muzzleOrigin:set(actor.target + vec3(0, 1.14, 0) + forward * 0.72 + right * 0.20)
  end
  local distance = math.clamp(message.distance, 0.05, 120)
  local targetPoint = message.origin + message.direction * distance
  local tracerDistance = (targetPoint - muzzleOrigin):length()
  local travelTime = math.clamp(tracerDistance / 260, 0.035, 0.08)
  local now = ui.time()
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
    if tracerRoot == nil or impactRoot == nil or sparkRoot == nil then
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
    tracerRoot:setShadows(false)
    impactRoot:setShadows(false)
    sparkRoot:setShadows(false)
    shotEffectTemplateHolder:setVisible(false)
    return {
      directEffectRenderParams(tracerRoot, 0x41535251, rgbm(1, 0.76, 0.18, 1)),
      directEffectRenderParams(impactRoot, 0x41535252, rgbm(0.045, 0.032, 0.022, 1)),
      directEffectRenderParams(sparkRoot, 0x41535253, rgbm(1, 0.42, 0.08, 1)),
    }
  end)
  if not ok then
    tracerRenderParams = false
    impactRenderParams = false
    sparkRenderParams = false
    ac.warn('[ASRC FPS] direct shot-effect template failed: ' .. tostring(result))
    return false
  end
  tracerRenderParams = result[1]
  impactRenderParams = result[2]
  sparkRenderParams = result[3]
  ac.log('[ASRC FPS] direct shot-effect templates ready')
  return true
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

local function ensureRemoteAvatarTemplate()
  if remoteAvatarRenderParams ~= nil then return remoteAvatarRenderParams ~= false end
  if rifleAssetFolder == nil then
    requestRifleAssets()
    return false
  end
  local ok, result = pcall(function()
    remoteAvatarTemplateHolder = carsRoot:createNode('ASRC_FPS_REMOTE_AVATAR_HOLDER', false)
    if remoteAvatarTemplateHolder == nil then error('remote avatar holder could not be created') end
    remoteAvatarTemplateRoot = remoteAvatarTemplateHolder:createNode('ASRC_FPS_REMOTE_AVATAR', false)
    if remoteAvatarTemplateRoot == nil then error('remote avatar root could not be created') end
    createOperatorBody(remoteAvatarTemplateRoot, 'ASRC_FPS_REMOTE')
    createRifleModel(remoteAvatarTemplateRoot, 'ASRC_FPS_REMOTE_WEAPON', false)
    remoteAvatarTemplateRoot:setShadows(false)
    remoteAvatarTemplateHolder:setVisible(false)
    return {
      mesh = remoteAvatarTemplateRoot,
      async = true,
      cacheKey = 0x41535243,
      textures = { txDiffuse = operatorSkinPath },
      values = {
        gBaseColor = rgbm(1, 1, 1, 1),
      },
      shader = [[
        float4 main(PS_IN pin) {
          float3 albedo = txDiffuse.SampleLevel(samLinear, pin.Tex, 0).rgb;
          float diffuse = 0.34 + 0.66 * saturate(dot(normalize(pin.NormalW),
            normalize(float3(-0.35, 0.8, -0.25))));
          return pin.ApplyFog(float4(albedo * gBaseColor.rgb * diffuse * gWhiteRefPoint, 1));
        }
      ]],
    }
  end)
  if not ok then
    remoteAvatarRenderParams = false
    ac.warn('[ASRC FPS] direct remote avatar template failed: ' .. tostring(result))
    return false
  end
  remoteAvatarRenderParams = result
  ac.log('[ASRC FPS] direct remote avatar template ready')
  return true
end

local function getRifleAssetArchiveUrl()
  local serverIP = ac.getServerIP()
  local serverHttpPort = ac.getServerPortHTTP()
  if serverIP == nil or serverIP == '' or serverHttpPort == nil or serverHttpPort < 0 then return nil end
  if string.find(serverIP, ':', 1, true) ~= nil and string.sub(serverIP, 1, 1) ~= '[' then
    serverIP = '[' .. serverIP .. ']'
  end
  return string.format('http://%s:%d%s', serverIP, serverHttpPort, rifleAssetArchivePath)
end

requestRifleAssets = function()
  if rifleAssetFolder ~= nil or rifleAssetsLoading or rifleAssetsFailed then return end
  local archiveUrl = getRifleAssetArchiveUrl()
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
      rifleAssetsFailed = true
      clientPackError = 'FPS RIFLE ASSET DOWNLOAD FAILED - CHECK SERVER HTTP PORT'
      ac.warn('[ASRC FPS] remote rifle asset download failed: error=' .. tostring(err)
        .. '; folder=' .. tostring(folder) .. '; url=' .. archiveUrl)
      return
    end

    rifleAssetFolder = folder
    rifleViewmodelPath = folder .. '/' .. rifleViewmodelFileName
    rifleWorldModelPath = folder .. '/' .. rifleWorldModelFileName
    rifleDiffusePath = folder .. '/' .. rifleDiffuseFileName
    operatorSkinPath = folder .. '/' .. operatorSkinFileName
    clientPackError = nil
    viewmodelRoot = nil
    viewmodelRenderParams = nil
    ac.log('[ASRC FPS] rifle assets cached: folder=' .. folder
      .. '; viewmodel=' .. rifleViewmodelPath .. '; world=' .. rifleWorldModelPath
      .. '; rifleTexture=' .. rifleDiffusePath .. '; operatorSkin=' .. operatorSkinPath)
  end)
end

local function ensureLocalViewmodel()
  if viewmodelRoot ~= nil then return viewmodelRoot ~= false end
  if rifleAssetFolder == nil then
    markViewmodelStage('asset-wait', 'remote archive is not cached yet')
    requestRifleAssets()
    return false
  end
  markViewmodelStage('load-requested', rifleViewmodelPath)
  local ok, result = pcall(function()
    -- A first-person weapon is always close to the active camera. An ordinary
    -- node avoids the world-space frustum assumptions of car bounding nodes.
    markViewmodelStage('holder-create:begin')
    viewmodelHolder = carsRoot:createNode('ASRC_FPS_VIEWMODEL_HOLDER', false)
    if viewmodelHolder == nil then error('viewmodel holder could not be created') end
    markViewmodelStage('holder-create:complete')
    markViewmodelStage('kn5-load:begin', rifleViewmodelPath)
    local model = viewmodelHolder:loadKN5({
      filename = rifleViewmodelPath,
      forceRenderableOn = true,
    })
    if model == nil then error('loadKN5 returned no model for ' .. rifleViewmodelPath) end
    markViewmodelStage('kn5-load:complete')
    markViewmodelStage('model-configure:begin')
    model:setShadows(false)
    model:setVisible(true, false)
    model:setCullMode(render.CullMode.None)
    model:setDepthMode(render.DepthMode.Off)
    markViewmodelStage('model-configure:complete')
    return model
  end)
  if not ok then
    if viewmodelHolder ~= nil then viewmodelHolder:dispose() end
    viewmodelHolder = nil
    viewmodelRoot = false
    clientPackError = 'FPS RIFLE MODEL ERROR - CACHED VIEWMODEL COULD NOT BE LOADED'
    ac.warn('[ASRC FPS] cached rifle viewmodel failed: ' .. tostring(result)
      .. '; cached path ' .. rifleViewmodelPath .. '; using 2D fallback')
    return false
  end
  viewmodelRoot = result
  if not runViewmodelStage('holder-initial-hide', function()
    viewmodelHolder:setVisible(false)
  end) then
    clientPackError = 'FPS RIFLE MODEL ERROR - VIEWMODEL HOLDER COULD NOT BE HIDDEN'
    return false
  end
  markViewmodelStage('bounds-read:begin')
  local boundsOk, boundsMin, boundsMax, meshCount = pcall(function()
    return viewmodelRoot:getLocalAABB()
  end)
  markViewmodelStage(boundsOk and 'bounds-read:complete' or 'bounds-read:failed',
    boundsOk and ('meshes=' .. tostring(meshCount)) or boundsMin)
  ac.log('[ASRC FPS] cached assault-rifle viewmodel loaded: ' .. rifleViewmodelPath
    .. '; bounds=' .. (boundsOk and (vec3Text(boundsMin) .. '..' .. vec3Text(boundsMax)
      .. '; meshes=' .. tostring(meshCount)) or ('unavailable: ' .. tostring(boundsMin))))
  viewmodelRenderParams = {
    mesh = viewmodelRoot,
    async = true,
    cacheKey = 0x41535238,
    textures = { txDiffuse = rifleDiffusePath },
    values = {
      gBaseColor = rgbm(1, 1, 1, 1),
    },
    shader = [[
      float4 main(PS_IN pin) {
        float3 albedo = txDiffuse.SampleLevel(samLinear, pin.Tex, 0).rgb;
        float diffuse = 0.32 + 0.68 * saturate(dot(normalize(pin.NormalW),
          normalize(float3(-0.35, 0.8, -0.25))));
        return float4(albedo * gBaseColor.rgb * diffuse * gWhiteRefPoint, 1);
      }
    ]],
  }
  markViewmodelStage('direct-render:configured', 'textured lit KN5 render pass')
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
    createOperatorBody(root, 'ASRC_FPS_OPERATOR_' .. actor.id)
    root:setVirtualCarFlag(true)
    actor.root = root
    actor.avatarKind = 'procedural-skinned-operator'
  end
  if actor.root == false or actor.weaponRoot ~= nil then return end
  if rifleWorldModelPath == nil then
    requestRifleAssets()
    return
  end

  local weaponOk, weapon = pcall(function()
    return actor.root:loadKN5({filename = rifleWorldModelPath, forceRenderableOn = true})
  end)
  actor.weaponRoot = weaponOk and weapon or nil
  if actor.weaponRoot == nil then
    actor.weaponRoot = createRifleModel(actor.root, 'ASRC_FPS_REMOTE_RIFLE_' .. actor.id, false)
    if not remoteRifleFallbackLogged then
      remoteRifleFallbackLogged = true
      ac.warn('[ASRC FPS] cached world rifle unavailable at ' .. tostring(rifleWorldModelPath)
        .. '; remote actors use procedural fallback')
    end
  end
  if actor.weaponRoot ~= nil then
    pcall(function() actor.weaponRoot:setMaterialTexture('txDiffuse', rifleDiffusePath) end)
    actor.weaponRoot:setPosition(vec3(0.22, 1.13, 0.08))
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
  ac.log(string.format('[ASRC FPS] camera near-clip request: requested=%.3f observed=%.3f method=%s',
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
  camera.ownShare = 1
  camera.fov = 72
  if thirdPersonEnabled then
    firstPersonCameraOffset:set(0, 0, 0)
    firstPersonCameraConstrained = false
    local forward = vec3(math.sin(yaw), 0, math.cos(yaw))
    local right = vec3(forward.z, 0, -forward.x)
    local focus = actor.render + vec3(0, math.max(1.05, cameraHeight - 0.25), 0)
    local desired = focus - forward * 3.2 + right * 0.72 + vec3(0, 0.55, 0)
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

local function updateRifleViewmodel(dt, actor, move, sprint)
  viewmodelUpdateAttempts = viewmodelUpdateAttempts + 1
  if not ensureLocalViewmodel() then return end
  local visible = gameplayActive and actor ~= nil and bit.band(actor.flags, 1) ~= 0
    and bit.band(actor.flags, 2) == 0 and not cursorUnlocked and not thirdPersonEnabled
  if not visible or camera == nil or not camera:active() then return end
  viewmodelKick = viewmodelKick * math.exp(-dt * 17)
  local moving = move:lengthSquared() > 0.01
  if moving then viewmodelBobTime = viewmodelBobTime + dt * (sprint and 12 or 8) end
  -- The grabbed camera transform is a request which CSP applies later in the frame. Draw
  -- callbacks must anchor viewmodels to the renderer's actual camera pose instead.
  local cameraPosition = ac.getCameraPosition():clone()
  local look = ac.getCameraForward():clone()
  local up = ac.getCameraUp():clone()
  if look:lengthSquared() < 0.001 then look:set(camera.transform.look) else look:normalize() end
  if up:lengthSquared() < 0.001 then up:set(0, 1, 0) else up:normalize() end
  local right = vec3(look.z, 0, -look.x)
  if right:lengthSquared() < 0.001 then right:set(1, 0, 0) else right:normalize() end
  local bobX = moving and math.sin(viewmodelBobTime) * 0.004 or 0
  local bobY = moving and math.abs(math.cos(viewmodelBobTime)) * 0.003 or 0
  local sprintLower = sprint and moving and 0.04 or 0
  local wallNormal = vec3()
  local wallHit = physics.raycastTrack(cameraPosition, look, 0.9,
    nil, wallNormal, false, false)
  local wallRetractionTarget = wallHit >= 0 and wallHit < 0.9
    and math.clamp((0.9 - wallHit) / 0.75, 0, 1) or 0
  viewmodelWallRetraction = math.lerp(viewmodelWallRetraction, wallRetractionTarget,
    1 - math.exp(-dt * 18))
  local position = cameraPosition
    + look * (0.30 - viewmodelKick * 0.04 - viewmodelWallRetraction * 0.25)
    + right * (0.22 + bobX)
    + up * (-0.20 - bobY - sprintLower + viewmodelKick * 0.012
      - viewmodelWallRetraction * 0.12)
  viewmodelLastPosition = position:clone()
  viewmodelRenderPosition = position:clone()
  viewmodelRenderLook = look:clone()
  viewmodelRenderUp = up:clone()
  localMuzzlePosition:set(position + look * 0.99 + up * 0.02)
  viewmodelUpdateCompletions = viewmodelUpdateCompletions + 1
  if not viewmodelStagesSeen['direct-transform:ready'] then
    markViewmodelStage('direct-transform:ready', vec3Text(position))
  end
end

local function drawDirectRifleViewmodel()
  local actor = actors[localSessionID]
  if actor == nil or bit.band(actor.flags, 1) == 0 or bit.band(actor.flags, 2) ~= 0
      or cursorUnlocked or thirdPersonEnabled or camera == nil or not camera:active() then return end

  -- Scene operations later in script.update can be interrupted by CSP for some tracks.
  -- Prepare the first-person pose here so rendering never depends on reaching that tail.
  updateRifleViewmodel(viewmodelFrameDt, actor, viewmodelMove, viewmodelSprint)
  if viewmodelRenderParams == nil or viewmodelRenderPosition == nil
      or viewmodelRenderLook == nil or viewmodelRenderUp == nil then return end

  viewmodelDirectDrawAttempts = viewmodelDirectDrawAttempts + 1
  -- Camera coordinates are world-space while CSP renders in a shifting graphics origin.
  -- Apply that origin explicitly so the viewmodel remains rigidly attached to the view.
  render.setTransform(viewmodelRenderPosition, viewmodelRenderLook, viewmodelRenderUp, true)
  render.setBlendMode(render.BlendMode.OpaqueForced)
  render.setCullMode(render.CullMode.None)
  render.setDepthMode(render.DepthMode.Off)
  local ok, result = pcall(function()
    return render.mesh(viewmodelRenderParams)
  end)
  render.setDepthMode(render.DepthMode.Normal)
  render.setCullMode(render.CullMode.Back)
  render.setBlendMode(render.BlendMode.Opaque)
  render.setTransform(vec3(), vec3(0, 0, 1), vec3(0, 1, 0))

  if not ok then
    viewmodelDirectDrawFailures = viewmodelDirectDrawFailures + 1
    markViewmodelStage('direct-render:failed', result)
    clientPackError = 'FPS RIFLE DIRECT RENDER FAILED - CHECK LIVE LOG'
    if not viewmodelDirectRenderFailureLogged then
      viewmodelDirectRenderFailureLogged = true
      ac.warn('[ASRC FPS] direct rifle render failed: ' .. tostring(result))
    end
  elseif result == false then
    viewmodelDirectDrawPending = viewmodelDirectDrawPending + 1
    if not viewmodelStagesSeen['direct-render:shader-pending'] then
      markViewmodelStage('direct-render:shader-pending')
    end
  else
    viewmodelDirectDrawCompletions = viewmodelDirectDrawCompletions + 1
    clientPackError = nil
    if not viewmodelStagesSeen['direct-render:ready'] then
      markViewmodelStage('direct-render:ready', 'first mesh draw completed')
      ac.log('[ASRC FPS] direct assault-rifle viewmodel draw completed')
    end
  end
end

local function drawRemoteActors()
  local visibleActors = 0
  local directActors = 0
  remoteRender.actorsDrawn = 0
  for _, actor in pairs(actors) do
    if actor.id ~= localSessionID and bit.band(actor.flags, 1) ~= 0
        and bit.band(actor.flags, 2) == 0 then
      visibleActors = visibleActors + 1
      if actor.remoteSceneReady then
        remoteRender.actorsDrawn = remoteRender.actorsDrawn + 1
      else
        directActors = directActors + 1
      end
    end
  end
  remoteRender.actorSnapshotCount = visibleActors
  if directActors == 0 then
    if remoteRender.actorsDrawn > 0 and not remoteRender.readyLogged then
      remoteRender.readyLogged = true
      ac.log(string.format(
        '[ASRC FPS] persistent remote actor scene rendering ready: drawn=%d visible=%d',
        remoteRender.actorsDrawn, visibleActors))
    end
    return
  end
  if not ensureRemoteAvatarTemplate() then return end

  render.setBlendMode(render.BlendMode.OpaqueForced)
  render.setCullMode(render.CullMode.None)
  render.setDepthMode(render.DepthMode.Normal)
  for _, actor in pairs(actors) do
    if actor.id ~= localSessionID and bit.band(actor.flags, 1) ~= 0
        and bit.band(actor.flags, 2) == 0 and not actor.remoteSceneReady then
      remoteRender.drawAttempts = remoteRender.drawAttempts + 1
      render.setTransform(actor.target,
        vec3(math.sin(actor.targetYaw), 0, math.cos(actor.targetYaw)), vec3(0, 1, 0), true)
      local ok, result = pcall(function()
        return render.mesh(remoteAvatarRenderParams)
      end)
      if not ok then
        remoteRender.drawFailures = remoteRender.drawFailures + 1
        if not remoteRender.failureLogged then
          remoteRender.failureLogged = true
          ac.warn('[ASRC FPS] direct remote avatar draw failed: ' .. tostring(result))
        end
      elseif result == false then
        remoteRender.drawPending = remoteRender.drawPending + 1
      else
        remoteRender.drawCompletions = remoteRender.drawCompletions + 1
        remoteRender.actorsDrawn = remoteRender.actorsDrawn + 1
      end
    end
  end
  render.setTransform(vec3(), vec3(0, 0, 1), vec3(0, 1, 0))
  render.setCullMode(render.CullMode.Back)
  render.setBlendMode(render.BlendMode.Opaque)

  if remoteRender.actorsDrawn > 0 and not remoteRender.readyLogged then
    remoteRender.readyLogged = true
    ac.log(string.format('[ASRC FPS] direct remote actor rendering ready: drawn=%d visible=%d',
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
            if render.mesh(sparkRenderParams) ~= false then rendered = rendered + 1 end
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

local function updateLocalThirdPersonAvatar(actor)
  if actor == nil then return false end
  local ok, err = pcall(function()
    ensureAvatar(actor)
    if actor.root == nil or actor.root == false then return end
    local active = bit.band(actor.flags, 1) ~= 0 and bit.band(actor.flags, 2) == 0
    actor.root:setVisible(active and thirdPersonEnabled)
    local avatarPosition = actor.render:clone()
    -- Snapshot interpolation is useful for the camera, but on an upward stair step it
    -- briefly leaves the mannequin below the authoritative support plane. Keep its feet
    -- on that plane while grounded so the model cannot appear embedded in a tread.
    if bit.band(actor.flags, 16) ~= 0 and actor.target.y > avatarPosition.y then
      avatarPosition.y = actor.target.y
    end
    actor.root:setPosition(avatarPosition)
    -- Local mouse yaw is immediate; replicated yaw is intentionally delayed by snapshots.
    -- Using it here made the shoulder camera orbit a body still facing its old direction.
    actor.root:setOrientation(vec3(math.sin(yaw), 0, math.cos(yaw)), vec3(0, 1, 0))
    if actor.weaponRoot ~= nil and actor.weaponRoot ~= false then
      actor.weaponRoot:setPosition(vec3(0.22, 1.13, 0.08 - (actor.weaponKick or 0) * 0.07))
      actor.weaponRoot:setOrientation(vec3(0, math.sin(actor.pitch), math.cos(actor.pitch)),
        vec3(0, 1, 0))
    end
  end)
  if not ok then
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

local function updateRemoteAvatar(actor)
  if actor == nil or actor.id == localSessionID then return false end
  local ok, err = pcall(function()
    ensureAvatar(actor)
    if actor.root == nil or actor.root == false then return end
    local active = gameplayActive and bit.band(actor.flags, 1) ~= 0
      and bit.band(actor.flags, 2) == 0
    actor.root:setVisible(active)
    if not active then return end
    actor.root:setPosition(actor.target)
    actor.root:setOrientation(vec3(math.sin(actor.targetYaw), 0, math.cos(actor.targetYaw)),
      vec3(0, 1, 0))
    if actor.weaponRoot ~= nil and actor.weaponRoot ~= false then
      actor.weaponRoot:setPosition(vec3(0.22, 1.13, 0.08 - (actor.weaponKick or 0) * 0.07))
      actor.weaponRoot:setOrientation(vec3(0, math.sin(actor.pitch), math.cos(actor.pitch)),
        vec3(0, 1, 0))
    end
  end)
  if not ok then
    actor.remoteSceneReady = false
    if not actor.remoteSceneErrorLogged then
      actor.remoteSceneErrorLogged = true
      ac.warn('[ASRC FPS] remote actor scene update failed: actor=' .. tostring(actor.id)
        .. '; error=' .. tostring(err))
    end
    return false
  end
  local ready = actor.root ~= nil and actor.root ~= false
  if ready and not actor.remoteSceneReady then
    ac.log('[ASRC FPS] persistent remote actor ready: actor=' .. tostring(actor.id)
      .. '; position=' .. vec3Text(actor.target))
  end
  actor.remoteSceneReady = ready
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
    viewmodelDiagnosticSendOk = clientDiagnosticEvent({
      pipeline = 12,
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
      remoteRender = diagnosticRemoteActor ~= nil and diagnosticRemoteActor.render or vec3(),
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
    cameraRetryAccumulator = cameraRetryAccumulator + dt
    if not acquireFpsCamera() then
      if cameraRetryAccumulator >= 1 then
        ac.log(string.format('[ASRC FPS] FPS camera unavailable: error=%s', tostring(cameraError)))
        cameraRetryAccumulator = 0
      end
    else
      cameraRetryAccumulator = 0
    end

    -- Main/pits/results UI was excluded above. Once gameplay is active, FPS
    -- owns the pointer even if a third-party app incorrectly asks for it.
    scoreboardHeld = ac.isKeyDown(ac.KeyIndex.Tab)
    cursorUnlocked = scoreboardHeld or persistentCursor
    local thirdPersonToggle = ac.isKeyDown(ac.KeyIndex.F6)
    if thirdPersonToggle and not thirdPersonToggleWasHeld then
      thirdPersonEnabled = not thirdPersonEnabled
      ac.log('[ASRC FPS] camera mode changed: '
        .. (thirdPersonEnabled and 'third-person over-shoulder' or 'first-person'))
    end
    thirdPersonToggleWasHeld = thirdPersonToggle
    local mouse = vec2()
    if not cursorUnlocked then
      mouse = ac.accessMouseDelta(true, true, true)
      ac.hideMouseCursor(true)
    end
    local rightX = clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.RightThumbX))
    local rightY = clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.RightThumbY))
    yaw = yaw - mouse.x * 0.0022 + rightX * dt * 2.8
    pitch = math.clamp(pitch - mouse.y * 0.0022 + rightY * dt * 2.2, -1.45, 1.45)

    -- Read both explicit FPS controls and AC's mapped driving controls. The
    -- latter remains available while game-rule locks suppress the carrier car,
    -- and covers GameInput devices which are not exposed as raw XInput pad 0.
    local mapped = physics.getCarInputControls()
    local keyboardX = -inputAxis(ac.KeyIndex.A, ac.KeyIndex.D, ac.KeyIndex.Left, ac.KeyIndex.Right)
    local keyboardY = inputAxis(ac.KeyIndex.S, ac.KeyIndex.W, ac.KeyIndex.Down, ac.KeyIndex.Up)
    local rawX = clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.LeftThumbX))
    local rawY = -clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.LeftThumbY))
    move = vec2(
      selectInput(keyboardX, rawX, clampStick(mapped.steer)),
      selectInput(keyboardY, rawY, clampStick(mapped.gas - mapped.brake)))
    if move:lengthSquared() > 1 then move:normalize() end
    local gamepadFire = ac.getGamepadAxisValue(0, ac.GamepadAxis.RightTrigger) > 0.35
    -- Raw VK input remains available while mouse-delta capture owns the pointer. CSP UI
    -- state alone reports false in that state on some builds, which previously meant the
    -- server received movement but never a Fire bit.
    local rawMouseFire = ac.isKeyDown(ac.KeyIndex.LeftButton)
    local uiMouseFire = ac.getUI().isMouseLeftKeyDown or ui.mouseDown(ui.MouseButton.Left)
    local fire = not cursorUnlocked and (rawMouseFire or uiMouseFire or gamepadFire)
    if fire and not fireCaptureLogged then
      fireCaptureLogged = true
      ac.log(string.format('[ASRC FPS] fire input captured: rawMouse=%s uiMouse=%s gamepad=%s',
        tostring(rawMouseFire), tostring(uiMouseFire), tostring(gamepadFire)))
    end
    sprint = ac.isKeyDown(ac.KeyIndex.LeftShift) or ac.isKeyDown(ac.KeyIndex.RightShift)
      or ac.isGamepadButtonPressed(0, ac.GamepadButton.LeftThumb)
    viewmodelMove:set(move)
    viewmodelSprint = sprint
    local jump = ac.isKeyDown(ac.KeyIndex.Space)
    local crouch = ac.isKeyDown(ac.KeyIndex.C)
      or ac.isKeyDown(ac.KeyIndex.LeftControl) or ac.isKeyDown(ac.KeyIndex.RightControl)
      or ac.isKeyDown(ac.KeyIndex.LeftMenu) or ac.isKeyDown(ac.KeyIndex.RightMenu)
    local reload = ac.isKeyDown(ac.KeyIndex.R)
    jumpStarted = jump and not jumpWasHeld
    local crouchPressed = crouch and not crouchWasHeld
    local jumpConsumed = false
    if localStance == 2 then
      if crouchPressed or jumpStarted then
        localStance = 1
        crouchLatched = true
        crouchHeldSeconds = 0
        jumpConsumed = jumpStarted
      end
    elseif localStance == 0 then
      if crouch then
        localStance = 1
        crouchHeldSeconds = dt
        crouchLatched = false
      end
    elseif crouchLatched then
      if crouchPressed then
        crouchLatched = false
        crouchHeldSeconds = dt
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
    end
    crouchWasHeld = crouch
    if jumpConsumed then jumpStarted = false end
    jumpWasHeld = jump
    local buttons = (fire and 1 or 0) + (sprint and 2 or 0) + (jump and 4 or 0)
      + (crouch and 8 or 0) + (reload and 16 or 0)

    sendAccumulator = sendAccumulator + dt
    if sendAccumulator >= 0.05 then
      sendAccumulator = sendAccumulator - 0.05
      sequence = sequence + 1
      inputSendOk = inputEvent({ sequence = sequence, move = move, yaw = yaw,
        pitch = pitch, buttons = buttons }, false, 255)
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
    -- releasing the camera also returns controller/menu ownership to AC.
    physics.setCarNoInput(false)
    releaseFpsCamera()
    sendAccumulator = 0
    inputDiagnosticAccumulator = 0
    inputWasActive = false
    viewmodelMove:set(0, 0)
    viewmodelSprint = false
    thirdPersonToggleWasHeld = false
    jumpWasHeld = false
    predictedHorizontalVelocity = vec2()
    predictedAirborne = false
    predictionCollisionConstrained = false
    predictionClearSnapshots = 0
    localStance = 0
    crouchWasHeld = false
    crouchHeldSeconds = 0
    crouchLatched = false
    cameraHeight = 1.65
    scoreboardHeld = false
    cursorUnlocked = false
  end

  if gameplayActive and localActor ~= nil and bit.band(localActor.flags, 1) ~= 0
      and bit.band(localActor.flags, 2) == 0 then
    local forward = vec2(math.sin(yaw), math.cos(yaw))
    local right = vec2(forward.y, -forward.x)
    local predicted = forward * move.y + right * move.x
    local desiredVelocity = predicted * (localStance == 2 and 1.8
      or localStance == 1 and 3.4 or sprint and 9 or 6)
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
      actor.yaw = math.lerpAngle(actor.yaw, actor.targetYaw, blend)
    else
      -- The server capsule is authoritative for both visibility and hits. A remote
      -- pose must therefore be copied, not independently predicted/interpolated.
      actor.render:set(actor.target)
      actor.yaw = actor.targetYaw
    end
    if actor.id ~= localSessionID then updateRemoteAvatar(actor) end
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
  for _, actor in pairs(actors) do
    actor.weaponKick = (actor.weaponKick or 0) * math.exp(-dt * 15)
    if actor.id ~= localSessionID and actor.weaponRoot ~= nil and actor.weaponRoot ~= false then
      local weaponOk, weaponError = pcall(function()
        actor.weaponRoot:setPosition(vec3(0.22, 1.13, 0.08 - actor.weaponKick * 0.07))
        actor.weaponRoot:setOrientation(vec3(0, math.sin(actor.pitch), math.cos(actor.pitch)),
          vec3(0, 1, 0))
      end)
      if not weaponOk and not actor.weaponSceneErrorLogged then
        actor.weaponSceneErrorLogged = true
        ac.warn('[ASRC FPS] avatar weapon scene update failed: actor=' .. tostring(actor.id)
          .. '; error=' .. tostring(weaponError))
      end
    end
  end
  for i = #killFeed, 1, -1 do
    killFeed[i].ttl = killFeed[i].ttl - dt
    if killFeed[i].ttl <= 0 then table.remove(killFeed, i) end
  end
end

function script.frameBegin(dt, gameDT)
  viewmodelFrameBeginCalls = viewmodelFrameBeginCalls + 1
  viewmodelFrameDt = math.max(0.001, math.min(dt, 0.05))
  if not fpsGameplayIsActive() then return end
  local localActor = actors[localSessionID]
  if localActor ~= nil and acquireFpsCamera() then
    applyFpsCamera(localActor, viewmodelFrameDt)
    ensureLocalViewmodel()
    updateLocalThirdPersonAvatar(localActor)
  end
end

function script.draw3D()
  viewmodelDraw3DCalls = viewmodelDraw3DCalls + 1
  if not gameplayActive then return end
  drawRemoteActors()
  drawDirectRifleViewmodel()
  drawDirectShotEffects()
end

function script.drawUI()
  viewmodelDrawUICalls = viewmodelDrawUICalls + 1
  if not gameplayActive then return end
  local size = ui.windowSize()
  local center = size / 2
  if cursorUnlocked then
    -- AC's own TAB leaderboard also asks for mouse ownership. Capture it here so the
    -- FPS scoreboard controls receive the click instead of rendering as inert HUD.
    ui.captureMouse(true)
    ui.setMouseCursor(ui.MouseCursor.Arrow)
  end
  if not cursorUnlocked then
    drawFallbackRifle(size)
    ui.drawLine(center - vec2(9, 0), center - vec2(3, 0), rgbm.colors.white, 2)
    ui.drawLine(center + vec2(3, 0), center + vec2(9, 0), rgbm.colors.white, 2)
    ui.drawLine(center - vec2(0, 9), center - vec2(0, 3), rgbm.colors.white, 2)
    ui.drawLine(center + vec2(0, 3), center + vec2(0, 9), rgbm.colors.white, 2)
  end
  if hitMarkerUntil > effectClock and not cursorUnlocked then
    local c = rgbm(1, 0.25, 0.15,
      math.min(1, (hitMarkerUntil - effectClock) * 7))
    ui.drawLine(center - vec2(8, 8), center - vec2(3, 3), c, 3)
    ui.drawLine(center + vec2(8, 8), center + vec2(3, 3), c, 3)
    ui.drawLine(center + vec2(8, -8), center + vec2(3, -3), c, 3)
    ui.drawLine(center + vec2(-8, 8), center + vec2(-3, 3), c, 3)
  end

  local actor = actors[localSessionID]
  if clientPackError ~= nil then
    ui.setCursor(vec2(28, size.y - 126))
    ui.pushStyleColor(ui.StyleColor.Text, rgbm(1, 0.18, 0.12, 1))
    ui.text(clientPackError)
    ui.popStyleColor()
  end
  ui.setCursor(vec2(28, size.y - 94))
  ui.pushFont(ui.Font.Title)
  ui.text(string.format('HEALTH  %d', actor and actor.health or 0))
  ui.text(string.format('K %d   D %d', actor and actor.kills or 0, actor and actor.deaths or 0))
  ui.popFont()
  ui.textColored(actor == nil and 'LINK: WAITING FOR PLAYER STATE'
      or (inputSendOk and 'LINK: ACTIVE' or 'LINK: INPUT SEND BLOCKED'),
    actor ~= nil and inputSendOk and rgbm(0.35, 1, 0.45, 1) or rgbm(1, 0.55, 0.2, 1))
  ui.setCursor(vec2(size.x - 300, size.y - 94))
  if actor ~= nil and actor.reloadRemaining > 0 then
    ui.textAligned(string.format('RELOADING  %.1fs', actor.reloadRemaining), 1, vec2(270, 24))
  else
    ui.textAligned('ASSAULT RIFLE', 1, vec2(270, 24))
  end
  ui.setCursor(vec2(size.x - 300, size.y - 70))
  ui.pushFont(ui.Font.Title)
  ui.textAligned(string.format('%02d  |  %d MAGS', actor and actor.ammo or 0,
    actor and actor.reserveMagazines or 0), 1, vec2(270, 28))
  ui.popFont()
  ui.setCursor(vec2(size.x - 300, size.y - 44))
  ui.textAligned('R  RELOAD', 1, vec2(270, 24))
  ui.setCursor(vec2(size.x - 300, size.y - 20))
  ui.textAligned('F6  CAMERA: ' .. (thirdPersonEnabled and 'THIRD PERSON' or 'FIRST PERSON'),
    1, vec2(270, 24))
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
    ui.text('POS   PLAYER                         KILLS   DEATHS   HEALTH')
    for i = 1, math.min(16, #ranking) do
      local rankedActor = ranking[i]
      ui.setCursor(panelMin + vec2(28, 70 + i * 27))
      ui.text(string.format('%2d    %-28s   %3d      %3d      %3d', i,
        names[rankedActor.id] or ('Player ' .. rankedActor.id), rankedActor.kills,
        rankedActor.deaths, rankedActor.health))
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
    ui.setCursor(vec2(28, 28))
    ui.text('DEATHMATCH')
    for i = 1, math.min(8, #ranking) do
      local rankedActor = ranking[i]
      ui.text(string.format('%2d  %-18s  %2d / %2d', i,
        names[rankedActor.id] or ('Player ' .. rankedActor.id), rankedActor.kills, rankedActor.deaths))
    end
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

local readySent = readyEvent({ protocol = 1 })
ac.log(string.format('[ASRC FPS] ready sent: protocol=1 result=%s', tostring(readySent)))
