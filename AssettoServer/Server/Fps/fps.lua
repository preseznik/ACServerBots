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
local hitMarker = 0
local tracers = {}
local rifleSounds = {}
local viewmodelHolder = nil
local viewmodelRoot = nil
local viewmodelKick = 0
local viewmodelBobTime = 0
local viewmodelMove = vec2()
local viewmodelSprint = false
local viewmodelFrameDt = 1 / 60
local localMuzzlePosition = vec3()
local viewmodelPipelineVersion = 'render-camera-v9'
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
local assettoRoot = ac.getFolder(ac.FolderID.Root)
local function clientAssetPath(relativePath)
  if assettoRoot == nil or assettoRoot == '' then return relativePath end
  return assettoRoot .. '/' .. relativePath
end
local rifleAudioRelativePath = 'extension/audio/asrc_fps/rifle.wav'
local rifleAudioPath = clientAssetPath(rifleAudioRelativePath)
local rifleAssetArchivePath = '/fps/assets/asrc-fps-assets-v4.zip'
local rifleViewmodelFileName = 'asrc_assault_rifle_viewmodel.kn5'
local rifleWorldModelFileName = 'asrc_assault_rifle_world.kn5'
local rifleAssetFolder = nil
local rifleAssetsLoading = false
local rifleAssetsFailed = false
local rifleAssetWaitLogged = false
local rifleViewmodelPath = nil
local rifleWorldModelPath = nil
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
local predictedGroundY = nil
local predictedVerticalVelocity = 0
local jumpWasHeld = false
local predictedHorizontalVelocity = vec2()
local predictedAirborne = false
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
local carsRoot = ac.findNodes('carsRoot:yes')
local hiddenCarrierRoots = {}
local createRifleModel
local playRifleSound

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
  stage = ac.StructItem.string(48),
}, function() end)

local snapshotEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsSnapshot'),
  sequence = ac.StructItem.uint32(),
  count = ac.StructItem.byte(),
  actorIDs = ac.StructItem.array(ac.StructItem.byte(), capacity),
  flags = ac.StructItem.array(ac.StructItem.byte(), capacity),
  positions = ac.StructItem.array(ac.StructItem.vec3(), capacity),
  groundYs = ac.StructItem.array(ac.StructItem.float(), capacity),
  yaws = ac.StructItem.array(ac.StructItem.float(), capacity),
  pitches = ac.StructItem.array(ac.StructItem.float(), capacity),
  health = ac.StructItem.array(ac.StructItem.uint16(), capacity),
  kills = ac.StructItem.array(ac.StructItem.uint16(), capacity),
  deaths = ac.StructItem.array(ac.StructItem.uint16(), capacity),
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
        pitch = 0, health = 0, kills = 0, deaths = 0, flags = 0,
      }
      actors[id] = actor
    end
    local previousFlags = actor.flags
    actor.target:set(message.positions[i])
    actor.groundY = message.groundYs[i]
    if actor.render:lengthSquared() < 0.001 then actor.render:set(actor.target) end
    actor.targetYaw = message.yaws[i]
    actor.pitch = message.pitches[i]
    actor.health = message.health[i]
    actor.kills = message.kills[i]
    actor.deaths = message.deaths[i]
    actor.flags = message.flags[i]
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
      local wasDead = bit.band(previousFlags, 2) ~= 0
      local isDead = bit.band(actor.flags, 2) ~= 0
      predictedGroundY = actor.groundY
      if bit.band(actor.flags, 64) ~= 0 then
        actor.render.x = actor.target.x
        actor.render.z = actor.target.z
        predictedHorizontalVelocity = vec2()
      end
      if bit.band(actor.flags, 128) ~= 0 then
        localStance = 2
      elseif bit.band(actor.flags, 32) ~= 0 then
        localStance = 1
      else
        localStance = 0
      end
      if not actor.localInitialized or (wasDead and not isDead) then
        yaw = message.yaws[i]
        pitch = message.pitches[i]
        actor.render:set(actor.target)
        actor.localInitialized = true
        predictedGroundY = actor.target.y
        predictedVerticalVelocity = 0
        predictedHorizontalVelocity = vec2()
        predictedAirborne = false
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
  if message.killerID == localSessionID then hitMarker = 0.2 end
end)

local hitEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsHit'),
  attackerID = ac.StructItem.byte(),
  victimID = ac.StructItem.byte(),
  remainingHealth = ac.StructItem.uint16(),
}, function(sender, message)
  if sender == nil and message.attackerID == localSessionID then hitMarker = 0.16 end
end)

local shotEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsShot'),
  shooterID = ac.StructItem.byte(),
  sequence = ac.StructItem.uint32(),
  origin = ac.StructItem.vec3(),
  direction = ac.StructItem.vec3(),
  distance = ac.StructItem.float(),
}, function(sender, message)
  if sender ~= nil then return end
  local actor = actors[message.shooterID]
  local origin = message.origin:clone()
  if message.shooterID == localSessionID and localMuzzlePosition:lengthSquared() > 0.001 then
    origin:set(localMuzzlePosition)
    viewmodelKick = 1
    pitch = math.min(1.45, pitch + 0.011)
  end
  local distance = math.clamp(message.distance, 0.05, 120)
  tracers[#tracers + 1] = {
    from = origin,
    to = message.origin + message.direction * distance,
    ttl = 0.075,
    flash = 0.045,
    localShot = message.shooterID == localSessionID,
  }
  if actor ~= nil then actor.weaponKick = 1 end
  if playRifleSound ~= nil then
    playRifleSound(message.origin, message.shooterID == localSessionID)
  end
end, nil, true)

local function appendBox(vertices, indices, center, size)
  local h = size / 2
  local x0, x1 = center.x - h.x, center.x + h.x
  local y0, y1 = center.y - h.y, center.y + h.y
  local z0, z1 = center.z - h.z, center.z + h.z
  local function face(a, b, c, d, normal)
    local base = #vertices
    vertices[#vertices + 1] = ac.MeshVertex(a, normal, vec2(0, 0))
    vertices[#vertices + 1] = ac.MeshVertex(b, normal, vec2(1, 0))
    vertices[#vertices + 1] = ac.MeshVertex(c, normal, vec2(1, 1))
    vertices[#vertices + 1] = ac.MeshVertex(d, normal, vec2(0, 1))
    indices[#indices + 1] = base
    indices[#indices + 1] = base + 1
    indices[#indices + 1] = base + 2
    indices[#indices + 1] = base
    indices[#indices + 1] = base + 2
    indices[#indices + 1] = base + 3
  end
  face(vec3(x0, y0, z1), vec3(x1, y0, z1), vec3(x1, y1, z1), vec3(x0, y1, z1), vec3(0, 0, 1))
  face(vec3(x1, y0, z0), vec3(x0, y0, z0), vec3(x0, y1, z0), vec3(x1, y1, z0), vec3(0, 0, -1))
  face(vec3(x1, y0, z1), vec3(x1, y0, z0), vec3(x1, y1, z0), vec3(x1, y1, z1), vec3(1, 0, 0))
  face(vec3(x0, y0, z0), vec3(x0, y0, z1), vec3(x0, y1, z1), vec3(x0, y1, z0), vec3(-1, 0, 0))
  face(vec3(x0, y1, z1), vec3(x1, y1, z1), vec3(x1, y1, z0), vec3(x0, y1, z0), vec3(0, 1, 0))
  face(vec3(x0, y0, z0), vec3(x1, y0, z0), vec3(x1, y0, z1), vec3(x0, y0, z1), vec3(0, -1, 0))
end

local function createBoxGroup(parent, name, boxes, color)
  local vertices, indices = {}, {}
  for _, box in ipairs(boxes) do appendBox(vertices, indices, box[1], box[2]) end
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
  mesh:setShadows(false)
  return mesh
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

local function getRifleAssetArchiveUrl()
  local serverIP = ac.getServerIP()
  local serverHttpPort = ac.getServerPortHTTP()
  if serverIP == nil or serverIP == '' or serverHttpPort == nil or serverHttpPort < 0 then return nil end
  if string.find(serverIP, ':', 1, true) ~= nil and string.sub(serverIP, 1, 1) ~= '[' then
    serverIP = '[' .. serverIP .. ']'
  end
  return string.format('http://%s:%d%s', serverIP, serverHttpPort, rifleAssetArchivePath)
end

local function requestRifleAssets()
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
    clientPackError = nil
    viewmodelRoot = nil
    viewmodelRenderParams = nil
    ac.log('[ASRC FPS] rifle assets cached: folder=' .. folder
      .. '; viewmodel=' .. rifleViewmodelPath .. '; world=' .. rifleWorldModelPath)
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
    cacheKey = 0x41535236,
    textures = {},
    values = {
      gBaseColor = rgbm(0.16, 0.19, 0.22, 1),
    },
    shader = [[
      float4 main(PS_IN pin) {
        float diffuse = 0.32 + 0.68 * saturate(dot(normalize(pin.NormalW),
          normalize(float3(-0.35, 0.8, -0.25))));
        return float4(gBaseColor.rgb * diffuse * gWhiteRefPoint, 1);
      }
    ]],
  }
  markViewmodelStage('direct-render:configured', 'solid lit KN5 render pass')
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
  if actor.root == nil then
    local root = carsRoot:createBoundingSphereNode('ASRC_FPS_' .. actor.id, 1.5)
    local pitcrewPath = clientAssetPath('content/objects3D/pitcrew.kn5')
    local pitcrewAnimationPath = clientAssetPath('content/objects3D/pitcrew_idle_up.ksanim')
    local ok, model = pcall(function()
      return root:loadKN5({filename = pitcrewPath, forceRenderableOn = true})
    end)
    if ok and model ~= nil then
      pcall(function() model:setAnimation(pitcrewAnimationPath, 0, true) end)
      root:setVirtualCarFlag(true)
      actor.root = root
      actor.avatarKind = 'stock-pitcrew'
    else
      createBoxGroup(root, 'ASRC_FPS_MANNEQUIN_BODY_' .. actor.id, {
        {vec3(0, 1.18, 0), vec3(0.48, 0.62, 0.28)},
        {vec3(0, 0.78, 0), vec3(0.40, 0.24, 0.25)},
        {vec3(-0.14, 0.35, 0), vec3(0.18, 0.70, 0.20)},
        {vec3(0.14, 0.35, 0), vec3(0.18, 0.70, 0.20)},
        {vec3(-0.34, 1.13, 0.02), vec3(0.16, 0.68, 0.18)},
        {vec3(0.34, 1.13, 0.02), vec3(0.16, 0.68, 0.18)},
      }, '343A46')
      createBoxGroup(root, 'ASRC_FPS_MANNEQUIN_HEAD_' .. actor.id, {
        {vec3(0, 1.65, 0), vec3(0.28, 0.32, 0.27)},
      }, 'B99B82')
      root:setVirtualCarFlag(true)
      actor.root = root
      actor.avatarKind = 'procedural-mannequin'
      ac.warn('[ASRC FPS] stock pit-crew avatar unavailable for actor=' .. tostring(actor.id)
        .. '; using procedural mannequin; error=' .. tostring(model))
    end
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

local function releaseFpsCamera()
  if camera == nil then return end
  if camera:active() then camera:dispose() end
  camera = nil
  cameraError = nil
  ac.log('[ASRC FPS] FPS camera released to AC menus')
end

local function acquireFpsCamera()
  if camera ~= nil and camera:active() then return true end
  camera, cameraError = ac.grabCamera('AssettoServer FPS deathmatch')
  if camera == nil then return false end
  camera.ownShare = 1
  camera.cameraRestoreThreshold = 0.5
  ac.log('[ASRC FPS] FPS camera acquired with full ownership')
  return true
end

local function applyFpsCamera(actor)
  if actor == nil or camera == nil or not camera:active() then return false end
  local look = vec3(math.sin(yaw) * math.cos(pitch), math.sin(pitch), math.cos(yaw) * math.cos(pitch))
  camera.ownShare = 1
  camera.fov = 72
  if thirdPersonEnabled then
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
    camera.transform.position = actor.render + vec3(0, cameraHeight, 0)
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
  local position = cameraPosition + look * (0.30 - viewmodelKick * 0.04)
    + right * (0.22 + bobX) + up * (-0.20 - bobY - sprintLower + viewmodelKick * 0.012)
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

local function updateLocalThirdPersonAvatar(actor)
  if actor == nil then return false end
  local ok, err = pcall(function()
    ensureAvatar(actor)
    if actor.root == nil or actor.root == false then return end
    local active = bit.band(actor.flags, 1) ~= 0 and bit.band(actor.flags, 2) == 0
    actor.root:setVisible(active and thirdPersonEnabled)
    actor.root:setPosition(actor.render)
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

local function localTrackMovementBlocked(position, movement, crouching)
  local distance = movement:length()
  if distance < 0.0001 then return false end
  local direction = vec3(movement.x / distance, 0, movement.y / distance)
  local side = vec3(-direction.z, 0, direction.x) * 0.3
  local height = crouching and 0.7 or 0.95
  local normal = vec3()
  for _, offset in ipairs({-1, 0, 1}) do
    normal:set(0, 0, 0)
    local origin = position + side * offset + vec3(0, height, 0)
    local hit = physics.raycastTrack(origin, direction, distance + 0.36,
      nil, normal, false, false)
    if hit >= 0 and hit <= distance + 0.36 and math.abs(normal.y) < 0.55 then
      return true
    end
  end
  return false
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
    local diagnosticFlags = 1
      + (rifleAssetFolder ~= nil and 2 or 0)
      + (viewmodelRoot ~= nil and viewmodelRoot ~= false and 4 or 0)
      + (localActor ~= nil and 8 or 0)
      + (camera ~= nil and camera:active() and 16 or 0)
      + (viewmodelDirectDrawCompletions > 0 and 32 or 0)
      + (thirdPersonEnabled and 64 or 0)
      + (localAvatarReady and 128 or 0)
    viewmodelDiagnosticSendOk = clientDiagnosticEvent({
      pipeline = 9,
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
      '[ASRC FPS] viewmodel heartbeat: pipeline=%s assetCached=%s modelLoaded=%s actor=%s cameraActive=%s updates=%d/%d callbacks=frameBegin:%d,draw3D:%d,drawUI:%d directDraw=%d/%d,pending:%d,failures:%d lastStage=%s detail=%s lastPosition=%s',
      viewmodelPipelineVersion, tostring(rifleAssetFolder ~= nil),
      tostring(viewmodelRoot ~= nil and viewmodelRoot ~= false), tostring(localActor ~= nil),
      tostring(camera ~= nil and camera:active()), viewmodelUpdateCompletions,
      viewmodelUpdateAttempts, viewmodelFrameBeginCalls, viewmodelDraw3DCalls,
      viewmodelDrawUICalls, viewmodelDirectDrawCompletions, viewmodelDirectDrawAttempts,
      viewmodelDirectDrawPending, viewmodelDirectDrawFailures, viewmodelLastStage,
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
    local fire = not cursorUnlocked and (ac.getUI().isMouseLeftKeyDown or gamepadFire)
    sprint = ac.isKeyDown(ac.KeyIndex.LeftShift) or ac.isKeyDown(ac.KeyIndex.RightShift)
      or ac.isGamepadButtonPressed(0, ac.GamepadButton.LeftThumb)
    viewmodelMove:set(move)
    viewmodelSprint = sprint
    local jump = ac.isKeyDown(ac.KeyIndex.Space)
    local crouch = ac.isKeyDown(ac.KeyIndex.C)
      or ac.isKeyDown(ac.KeyIndex.LeftControl) or ac.isKeyDown(ac.KeyIndex.RightControl)
      or ac.isKeyDown(ac.KeyIndex.LeftMenu) or ac.isKeyDown(ac.KeyIndex.RightMenu)
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
      + (crouch and 8 or 0)

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
    local geometryBlocked = bit.band(localActor.flags, 64) ~= 0
    if geometryBlocked then desiredVelocity = vec2() end
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
    if localTrackMovementBlocked(localActor.render, predictedStep, localStance ~= 0) then
      predictedHorizontalVelocity = vec2()
    else
      localActor.render:add(vec3(predictedStep.x, 0, predictedStep.y))
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
    local blend = 1 - math.exp(-dt * (actor.id == localSessionID and 6 or 18))
    actor.render:set(math.lerp(actor.render, actor.target, blend))
    actor.yaw = math.lerpAngle(actor.yaw, actor.targetYaw, blend)
    if actor.id ~= localSessionID then
      local sceneOk, sceneError = pcall(function()
        ensureAvatar(actor)
        if actor.root then
          local active = bit.band(actor.flags, 1) ~= 0 and bit.band(actor.flags, 2) == 0
          actor.root:setVisible(active)
          actor.root:setPosition(actor.render)
          actor.root:setOrientation(vec3(math.sin(actor.yaw), 0, math.cos(actor.yaw)), vec3(0, 1, 0))
        end
      end)
      if not sceneOk and not actor.sceneErrorLogged then
        actor.sceneErrorLogged = true
        ac.warn('[ASRC FPS] avatar scene update failed: actor=' .. tostring(actor.id)
          .. '; error=' .. tostring(sceneError))
      end
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
        '[ASRC FPS] render state: actor=%s target=%s render=%s error=%.3f flags=%s cameraActive=%s cameraShare=%s cameraPosition=%s originalCameraPosition=%s grabbed=%s',
        tostring(localActor.id), vec3Text(localActor.target), vec3Text(localActor.render),
        (localActor.target - localActor.render):length(), tostring(localActor.flags),
        tostring(camera ~= nil and camera:active()),
        tostring(camera ~= nil and camera.ownShare or 'nil'),
        camera ~= nil and vec3Text(camera.transform.position) or 'nil',
        camera ~= nil and vec3Text(camera.transformOriginal.position) or 'nil',
        tostring(ac.isCameraGrabbed())))
      ac.log(string.format(
        '[ASRC FPS] viewmodel render state: pipeline=%s loaded=%s updates=%d/%d directDraw=%d/%d,pending:%d,failures:%d lastStage=%s detail=%s lastPosition=%s',
        viewmodelPipelineVersion, tostring(viewmodelRoot ~= nil and viewmodelRoot ~= false),
        viewmodelUpdateCompletions, viewmodelUpdateAttempts, viewmodelDirectDrawCompletions,
        viewmodelDirectDrawAttempts, viewmodelDirectDrawPending, viewmodelDirectDrawFailures,
        viewmodelLastStage, viewmodelLastStageDetail,
        viewmodelLastPosition ~= nil and vec3Text(viewmodelLastPosition) or 'nil'))
    end
  end

  hitMarker = math.max(0, hitMarker - dt)
  for i = #tracers, 1, -1 do
    tracers[i].ttl = tracers[i].ttl - dt
    tracers[i].flash = tracers[i].flash - dt
    if tracers[i].ttl <= 0 and tracers[i].flash <= 0 then table.remove(tracers, i) end
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
    applyFpsCamera(localActor)
    ensureLocalViewmodel()
    updateLocalThirdPersonAvatar(localActor)
  end
end

function script.draw3D()
  viewmodelDraw3DCalls = viewmodelDraw3DCalls + 1
  if not gameplayActive then return end
  drawDirectRifleViewmodel()
  for _, tracer in ipairs(tracers) do
    if tracer.ttl > 0 then
      local alpha = math.min(1, tracer.ttl / 0.04)
      render.debugLine(tracer.from, tracer.to,
        tracer.localShot and rgbm(1, 0.78, 0.25, alpha) or rgbm(1, 0.45, 0.12, alpha))
    end
    if tracer.flash > 0 then
      render.debugSphere(tracer.from, 0.07 + tracer.flash * 0.8,
        rgbm(1, 0.62, 0.16, math.min(1, tracer.flash * 25)))
    end
  end
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
  if hitMarker > 0 and not cursorUnlocked then
    local c = rgbm(1, 0.25, 0.15, math.min(1, hitMarker * 7))
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
  ui.setCursor(vec2(size.x - 260, size.y - 66))
  ui.textAligned('ASSAULT RIFLE  |  INFINITE', 1, vec2(230, 24))
  ui.setCursor(vec2(size.x - 260, size.y - 44))
  ui.textAligned('F6  CAMERA: ' .. (thirdPersonEnabled and 'THIRD PERSON' or 'FIRST PERSON'),
    1, vec2(230, 24))
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
