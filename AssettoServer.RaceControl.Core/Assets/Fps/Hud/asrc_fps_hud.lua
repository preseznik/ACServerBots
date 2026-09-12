local license = [[
Copyright (C) 2026 Niewiarowski, compujuckel

This program is free software: you can redistribute it and/or modify it under the terms of the
GNU Affero General Public License as published by the Free Software Foundation, version 3.
]]

local bridgeProtocol = 16
local actorCapacity = 32
local grenadeCapacity = 8
local killFeedCapacity = 6
local awardPopupCapacity = 4
local bridge = ac.connect({
  ac.StructItem.key('asrc.fps.hud.v16'),
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
  localMagazineCapacity = ac.StructItem.byte(),
  localReloadDuration = ac.StructItem.float(),
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
  actorIDs = ac.StructItem.array(ac.StructItem.byte(), actorCapacity),
  actorFlags = ac.StructItem.array(ac.StructItem.byte(), actorCapacity),
  actorTeams = ac.StructItem.array(ac.StructItem.byte(), actorCapacity),
  radarFlags = ac.StructItem.array(ac.StructItem.byte(), actorCapacity),
  actorPositions = ac.StructItem.array(ac.StructItem.vec3(), actorCapacity),
  actorYaws = ac.StructItem.array(ac.StructItem.float(), actorCapacity),
  actorHealth = ac.StructItem.array(ac.StructItem.uint16(), actorCapacity),
  actorKills = ac.StructItem.array(ac.StructItem.uint16(), actorCapacity),
  actorDeaths = ac.StructItem.array(ac.StructItem.uint16(), actorCapacity),
  actorScores = ac.StructItem.array(ac.StructItem.uint32(), actorCapacity),
  actorNames = ac.StructItem.array(ac.StructItem.string(32), actorCapacity),
  grenadeThreatCount = ac.StructItem.byte(),
  grenadeThreatPositions = ac.StructItem.array(ac.StructItem.vec3(), grenadeCapacity),
  grenadeThreatVelocities = ac.StructItem.array(ac.StructItem.vec3(), grenadeCapacity),
  grenadeThreatRemaining = ac.StructItem.array(ac.StructItem.float(), grenadeCapacity),
  grenadeThreatUpdateTime = ac.StructItem.float(),
  killFeedCount = ac.StructItem.byte(),
  killFeed = ac.StructItem.array(ac.StructItem.string(72), killFeedCapacity),
  awardPopupCount = ac.StructItem.byte(),
  awardPopupTexts = ac.StructItem.array(ac.StructItem.string(64), awardPopupCapacity),
  awardPopupAlphas = ac.StructItem.array(ac.StructItem.float(), awardPopupCapacity),
  lethalMedalItem = ac.StructItem.byte(),
  lethalMedalCount = ac.StructItem.byte(),
  lethalMedalAge = ac.StructItem.float(),
}, false, ac.SharedNamespace.Shared)

local ranking = {}
local appCursorInitialized = false
local mismatchLogged = false
local assettoRoot = ac.getFolder(ac.FolderID.Root)
local weaponImagePath = (assettoRoot ~= nil and assettoRoot ~= '')
  and (assettoRoot .. '/apps/lua/asrc_fps_hud/asrc_carbine_hud.png')
  or 'asrc_carbine_hud.png'
local weaponImages = {
  [1] = weaponImagePath,
  [2] = assettoRoot .. '/content/objects3D/asrc_fps/asrc_loadout_compact_smg.png',
  [3] = assettoRoot .. '/content/objects3D/asrc_fps/asrc_loadout_desert_eagle.png',
  [4] = assettoRoot .. '/content/objects3D/asrc_fps/asrc_loadout_colt_1911.png',
  [16] = assettoRoot .. '/content/objects3D/asrc_fps/asrc_loadout_frag_grenade.png',
  [17] = assettoRoot .. '/content/objects3D/asrc_fps/asrc_loadout_sticky_grenade.png',
}
local itemNames = {
  [1] = 'ASSAULT RIFLE', [2] = 'MP5 SMG',
  [3] = 'DESERT EAGLE', [4] = 'COLT 1911',
  [16] = 'FRAG GRENADE', [17] = 'STICKY GRENADE',
}
local matchTypeLabels = {
  [0] = 'FFA', [1] = 'TDM', [2] = 'HARDCORE FFA', [3] = 'HARDCORE TDM',
}
local function isTeamMatch()
  return bridge.matchType == 1 or bridge.matchType == 3
end
local function matchLabel()
  return matchTypeLabels[bridge.matchType] or 'FFA'
end
local function matchModeTitle()
  if bridge.matchType == 1 then return 'TEAM DEATH MATCH' end
  if bridge.matchType == 2 then return 'HARDCORE FREE FOR ALL' end
  if bridge.matchType == 3 then return 'HARDCORE TEAM DEATH MATCH' end
  return 'FREE FOR ALL'
end
local function matchTargetText()
  if isTeamMatch() then
    return string.format('T1 %d  -  %d T2   TARGET %d', bridge.team1Kills,
      bridge.team2Kills, bridge.killLimit)
  end
  return string.format('TARGET %d', bridge.killLimit)
end
local audioPlayer = {
  directory = assettoRoot .. '/extension/audio/asrc_fps/',
  active = {},
  maxActive = 64,
  failureLogged = {},
  readyLogged = false,
  wasLive = false,
}

function audioPlayer.dispose(index)
  local sound = audioPlayer.active[index]
  if sound == nil then return end
  pcall(function() sound.event:dispose() end)
  table.remove(audioPlayer.active, index)
end

function audioPlayer.reset()
  for index = #audioPlayer.active, 1, -1 do audioPlayer.dispose(index) end
end

function audioPlayer.logFailure(fileName, stage, detail)
  local key = tostring(fileName) .. ':' .. tostring(stage)
  if audioPlayer.failureLogged[key] then return end
  audioPlayer.failureLogged[key] = true
  ac.warn('[ASRC FPS HUD] audio playback failed: file=' .. tostring(fileName)
    .. ' stage=' .. tostring(stage) .. ' detail=' .. tostring(detail))
end

function audioPlayer.distanceGain(position, maxDistance)
  local distance = (position - ac.getCameraPosition()):length()
  if distance >= maxDistance then return 0 end
  local fullVolumeDistance = math.min(2, maxDistance * 0.25)
  if distance <= fullVolumeDistance then return 1 end
  local remaining = 1 - (distance - fullVolumeDistance)
    / (maxDistance - fullVolumeDistance)
  return math.clamp(remaining, 0, 1) ^ 2
end

function audioPlayer.play(data)
  if type(data) ~= 'table' or tonumber(data.version) ~= 1 then return end
  local fileName = tostring(data.fileName or '')
  if not fileName:match('^[a-z0-9_]+%.wav$') then
    audioPlayer.logFailure(fileName, 'payload', 'unsafe filename')
    return
  end
  local filePath = audioPlayer.directory .. fileName
  local existsOk, exists = pcall(function() return io.fileExists(filePath) end)
  if not existsOk or not exists then
    audioPlayer.logFailure(fileName, 'file', existsOk and 'missing' or exists)
    return
  end
  local localSound = data.localSound == true or data.localSound == 1
  local volume = math.clamp(tonumber(data.volume) or 0, 0, 2)
  local maxDistance = math.clamp(tonumber(data.maxDistance) or 1, 1, 500)
  local ttl = math.clamp(tonumber(data.ttl) or 0.5, 0.1, 3)
  local position = nil
  if not localSound then
    if not (data.hasPosition == true or data.hasPosition == 1) or data.position == nil then
      audioPlayer.logFailure(fileName, 'payload', 'remote cue has no position')
      return
    end
    position = data.position
    volume = volume * audioPlayer.distanceGain(position, maxDistance)
    if volume <= 0.001 then return end
  end
  local minDistance = math.min(2, maxDistance * 0.25)
  local ok, event = pcall(function()
    return ac.AudioEvent.fromFile({
      filename = filePath,
      use3D = not localSound,
      useOcclusion = not localSound,
      loop = false,
      minDistance = minDistance,
      maxDistance = maxDistance,
    }, not localSound)
  end)
  if not ok or event == nil then
    audioPlayer.logFailure(fileName, 'create', ok and 'nil event' or event)
    return
  end
  while #audioPlayer.active >= audioPlayer.maxActive do audioPlayer.dispose(1) end
  audioPlayer.active[#audioPlayer.active + 1] = {
    event = event,
    fileName = fileName,
    ttl = ttl,
    validityChecked = false,
  }
  local index = #audioPlayer.active
  local startOk, startError = pcall(function()
    event.volume = volume
    event.cameraInteriorMultiplier = 1
    event.cameraExteriorMultiplier = 1
    event.cameraTrackMultiplier = 1
    if position ~= nil then event:setPosition(position) end
    event:start()
  end)
  if not startOk then
    audioPlayer.dispose(index)
    audioPlayer.logFailure(fileName, 'start', startError)
  end
end

function audioPlayer.update(dt)
  for index = #audioPlayer.active, 1, -1 do
    local sound = audioPlayer.active[index]
    if not sound.validityChecked then
      sound.validityChecked = true
      local ok, valid = pcall(function() return sound.event:isValid() end)
      if not ok or not valid then
        audioPlayer.logFailure(sound.fileName, 'post-start-validity',
          ok and 'invalid event' or valid)
      elseif not audioPlayer.readyLogged then
        audioPlayer.readyLogged = true
        ac.log('[ASRC FPS HUD] file-backed audio playback valid: '
          .. audioPlayer.directory .. sound.fileName)
      end
    end
    sound.ttl = sound.ttl - dt
    if sound.ttl <= 0 then audioPlayer.dispose(index) end
  end
end

local audioRelaySubscription = ac.onSharedEvent('asrc.fps.audio.v1', function(data)
  audioPlayer.play(data)
end)

ac.onRelease(function()
  audioPlayer.reset()
end)

local function bridgeString(value)
  if type(value) == 'string' then return value end
  local ok, decoded = pcall(ffi.string, value)
  return ok and decoded or ''
end

local function bridgeIsLive()
  local age = ui.time() - bridge.onlineHeartbeat
  return bridge.protocol == bridgeProtocol and bridge.gameplayActive ~= 0
    and age >= -0.1 and age <= 0.5
end

-- The global override is available to Lua apps, not online scripts. Car-camera
-- clipNear does not affect ac.grabCamera(), which switches to the free camera.
local fpsCameraClip = { near = 0.005, applied = false, failureLogged = false }

function fpsCameraClip.setActive(active)
  if active == fpsCameraClip.applied then return end
  local ok, err = pcall(function()
    ac.overrideCameraClipPlanes(active and fpsCameraClip.near or nil, nil)
  end)
  if not ok then
    if not fpsCameraClip.failureLogged then
      fpsCameraClip.failureLogged = true
      ac.warn('[ASRC FPS HUD] camera clip override failed: ' .. tostring(err))
    end
    return
  end
  fpsCameraClip.applied = active
  fpsCameraClip.verifyAfter = active and ui.time() + 0.2 or nil
  ac.log(active and '[ASRC FPS HUD] FPS camera near clip requested: 0.005 m'
    or '[ASRC FPS HUD] FPS camera clip override released')
end

function fpsCameraClip.update()
  local state = ac.getSim()
  fpsCameraClip.setActive(bridgeIsLive() and state.isSessionStarted and state.isLive
    and not state.isPaused and not state.isInMainMenu
    and not state.isLookingAtSessionResults and not state.isReplayActive)
  if fpsCameraClip.verifyAfter ~= nil and ui.time() >= fpsCameraClip.verifyAfter then
    fpsCameraClip.verifyAfter = nil
    local observed = state.cameraClipNear
    ac.log(string.format('[ASRC FPS HUD] FPS camera near clip observed: %.4f m', observed))
    if observed > fpsCameraClip.near + 0.001 then
      ac.warn('[ASRC FPS HUD] FPS camera near clip did not take effect; check other camera apps')
    end
  end
end

ac.onRelease(function() fpsCameraClip.setActive(false) end)

local function actorName(index)
  local value = bridgeString(bridge.actorNames[index])
  if value == '' then return 'Operative ' .. tostring(bridge.actorIDs[index]) end
  return value
end

local function panel(p1, p2, scale, alpha)
  ui.drawRectFilled(p1, p2, rgbm(0.025, 0.035, 0.05, alpha or 0.82), 7 * scale)
  ui.drawRect(p1, p2, rgbm(0.45, 0.62, 0.78, 0.42), 7 * scale, nil, math.max(1, scale))
end

local function buildRanking()
  table.clear(ranking)
  for index = 0, math.min(actorCapacity, bridge.actorCount) - 1 do
    if bit.band(bridge.actorFlags[index], 1) ~= 0 then
      ranking[#ranking + 1] = index
    end
  end
  table.sort(ranking, function(left, right)
    if bridge.actorKills[left] ~= bridge.actorKills[right] then
      return bridge.actorKills[left] > bridge.actorKills[right]
    end
    if bridge.actorDeaths[left] ~= bridge.actorDeaths[right] then
      return bridge.actorDeaths[left] < bridge.actorDeaths[right]
    end
    return bridge.actorIDs[left] < bridge.actorIDs[right]
  end)
end

local function localActorIndex()
  for index = 0, math.min(actorCapacity, bridge.actorCount) - 1 do
    if bridge.actorIDs[index] == bridge.localActorID then return index end
  end
  return nil
end

local function drawRadar(size, scale, margin)
  local diameter = 190 * scale
  local radius = diameter * 0.5
  local center = vec2(margin + radius, margin + radius)
  panel(center - vec2(radius, radius), center + vec2(radius, radius), scale, 0.76)
  ui.drawCircle(center, radius - 8 * scale, rgbm(0.5, 0.7, 0.82, 0.5), 48, math.max(1, scale))
  ui.drawCircle(center, (radius - 8 * scale) * 0.5, rgbm(0.35, 0.5, 0.62, 0.34), 36,
    math.max(1, scale))
  ui.drawLine(center - vec2(radius - 8 * scale, 0), center + vec2(radius - 8 * scale, 0),
    rgbm(0.3, 0.45, 0.56, 0.25), math.max(1, scale))
  ui.drawLine(center - vec2(0, radius - 8 * scale), center + vec2(0, radius - 8 * scale),
    rgbm(0.3, 0.45, 0.56, 0.25), math.max(1, scale))

  local ownIndex = localActorIndex()
  if ownIndex ~= nil then
    local own = bridge.actorPositions[ownIndex]
    local lookX, lookZ = math.sin(bridge.viewYaw), math.cos(bridge.viewYaw)
    local rightX, rightZ = lookZ, -lookX
    local usableRadius = radius - 16 * scale
    for index = 0, math.min(actorCapacity, bridge.actorCount) - 1 do
      if index ~= ownIndex and bridge.radarFlags[index] ~= 0 then
        local offset = bridge.actorPositions[index] - own
        -- CSP's FPS yaw increases toward screen-left, so the conventional world-space
        -- right basis must be negated for a player-up presentation.
        local right = -(offset.x * rightX + offset.z * rightZ)
        local forward = offset.x * lookX + offset.z * lookZ
        local point = vec2(right, -forward) / 40 * usableRadius
        local length = point:length()
        if length > usableRadius then point:scale(usableRadius / length) end
        local friendly = bridge.radarFlags[index] == 3
        ui.drawCircleFilled(center + point, 4.5 * scale,
          friendly and rgbm(0.18, 0.58, 1, 1) or rgbm(1, 0.22, 0.15, 0.95), 16)
      end
    end
  end
  ui.drawTriangleFilled(center - vec2(0, 8 * scale), center + vec2(-5 * scale, 6 * scale),
    center + vec2(5 * scale, 6 * scale), rgbm(0.35, 0.9, 1, 1))
  ui.setCursor(vec2(margin + 10 * scale, margin + diameter - 24 * scale))
  ui.textColored('COMBAT RADAR  40 m', rgbm(0.65, 0.8, 0.9, 0.9))
end

local function drawCompactRanking(scale, margin)
  local top = margin + 202 * scale
  if isTeamMatch() then
    local width = 410 * scale
    local team1, team2 = {}, {}
    for place = 1, #ranking do
      local index = ranking[place]
      if bridge.actorTeams[index] == 1 then team1[#team1 + 1] = index
      elseif bridge.actorTeams[index] == 2 then team2[#team2 + 1] = index end
    end
    local rows = math.min(4, math.max(#team1, #team2))
    local bottom = top + (78 + rows * 22) * scale
    panel(vec2(margin, top), vec2(margin + width, bottom), scale, 0.78)
    ui.drawRectFilled(vec2(margin, top), vec2(margin + width * 0.5, top + 5 * scale),
      rgbm(0.12, 0.52, 1, 1))
    ui.drawRectFilled(vec2(margin + width * 0.5, top),
      vec2(margin + width, top + 5 * scale), rgbm(0.94, 0.2, 0.14, 1))
    ui.setCursor(vec2(margin + 12 * scale, top + 12 * scale))
    ui.textColored('TEAM 1', rgbm(0.46, 0.76, 1, 1))
    ui.setCursor(vec2(margin + 118 * scale, top + 8 * scale))
    ui.pushFont(ui.Font.Title)
    ui.text(tostring(bridge.team1Kills))
    ui.popFont()
    ui.setCursor(vec2(margin + 217 * scale, top + 12 * scale))
    ui.textColored('TEAM 2', rgbm(1, 0.48, 0.4, 1))
    ui.setCursor(vec2(margin + 325 * scale, top + 8 * scale))
    ui.pushFont(ui.Font.Title)
    ui.text(tostring(bridge.team2Kills))
    ui.popFont()
    ui.setCursor(vec2(margin + 12 * scale, top + 45 * scale))
    ui.textColored('PLAYER          K/D', rgbm(0.55, 0.64, 0.72, 1))
    ui.setCursor(vec2(margin + 217 * scale, top + 45 * scale))
    ui.textColored('PLAYER          K/D', rgbm(0.55, 0.64, 0.72, 1))
    for row = 1, rows do
      for team = 1, 2 do
        local index = (team == 1 and team1 or team2)[row]
        if index ~= nil then
          local x = margin + (team == 2 and 217 or 12) * scale
          local y = top + (49 + row * 22) * scale
          if bridge.actorIDs[index] == bridge.localActorID then
            ui.drawRectFilled(vec2(x - 5 * scale, y - 2 * scale),
              vec2(x + 184 * scale, y + 19 * scale), rgbm(0.16, 0.45, 0.68, 0.42), 2 * scale)
          end
          ui.setCursor(vec2(x, y))
          ui.text(string.format('%-12s %2d/%2d', string.sub(actorName(index), 1, 12),
            bridge.actorKills[index], bridge.actorDeaths[index]))
        end
      end
    end
  else
    local width = 340 * scale
    local rows = math.min(8, #ranking)
    panel(vec2(margin, top), vec2(margin + width, top + (55 + rows * 24) * scale), scale, 0.76)
    ui.drawRectFilled(vec2(margin, top), vec2(margin + 5 * scale,
      top + (55 + rows * 24) * scale), rgbm(0.93, 0.67, 0.15, 1), 2 * scale)
    ui.setCursor(vec2(margin + 15 * scale, top + 9 * scale))
    ui.textColored('FREE FOR ALL', rgbm(1, 0.83, 0.42, 1))
    ui.setCursor(vec2(margin + 15 * scale, top + 31 * scale))
    ui.textColored('POS  OPERATOR           SCORE   K/D', rgbm(0.55, 0.64, 0.72, 1))
    for place = 1, rows do
      local index = ranking[place]
      local y = top + (36 + place * 24) * scale
      if bridge.actorIDs[index] == bridge.localActorID then
        ui.drawRectFilled(vec2(margin + 8 * scale, y - 2 * scale),
          vec2(margin + width - 8 * scale, y + 20 * scale), rgbm(0.16, 0.45, 0.68, 0.42), 2 * scale)
      end
      ui.setCursor(vec2(margin + 15 * scale, y))
      ui.text(string.format('%2d   %-17s %5d  %2d/%2d', place,
        string.sub(actorName(index), 1, 17), bridge.actorScores[index],
        bridge.actorKills[index], bridge.actorDeaths[index]))
    end
  end
end

local function drawAmmoPanel(size, scale, margin, state)
  local p = vec2(size.x - margin - 360 * scale, size.y - margin - 168 * scale)
  local white, cyan = rgbm(0.96, 0.98, 1, 1), rgbm(0.10, 0.86, 0.94, 1)
  local muted, border = rgbm(0.55, 0.72, 0.8, 1), rgbm(0.27, 0.49, 0.57, 1)
  local ammo = math.max(0, math.floor(state.ammo or 0))
  local mags = math.max(0, math.floor(state.reserveMagazines or 0))
  local capacity = state.magazineCapacity or 0
  local reserve = capacity > 0 and mags * capacity or nil
  local reloading = (state.reloadRemaining or 0) > 0
  local ammoColor = ammo == 0 and rgbm(1, 0.30, 0.25, 1)
    or (capacity > 0 and ammo <= capacity * 0.25) and rgbm(1, 0.72, 0.25, 1) or white
  local function point(x, y) return p + vec2(x, y) * scale end
  local function text(value, x, y, w, h, fontSize, color)
    ui.dwriteDrawTextClipped(tostring(value), fontSize * scale, point(x, y),
      point(x + w, y + h), ui.Alignment.Start, ui.Alignment.Center, false, color)
  end
  -- Separate alpha cutout: no backing, border or panel underneath the weapon.
  if state.imagePath ~= nil then
    if string.find(state.imagePath, 'asrc_carbine_hud.png', 1, true) ~= nil then
      -- The legacy rifle thumbnail has large transparent side gutters. Sample just
      -- its silhouette so the visible rifle, not the empty texture, matches the mockup.
      ui.drawImage(state.imagePath, point(-160, 30), point(-12, 130),
        white, vec2(190 / 900, 0), vec2(716 / 900, 1))
    else
      ui.drawImage(state.imagePath, point(-160, 30), point(-12, 130),
        white, nil, nil, ui.ImageFit.Fit)
    end
  end
  ui.drawRectFilled(p, point(360, 168), rgbm(0.035, 0.053, 0.061, 0.96), 6 * scale)
  ui.drawRect(p, point(360, 168), border, 6 * scale, nil, math.max(1, scale))
  ui.drawRectFilled(point(357, 5), point(360, 163), cyan, 2 * scale)
  ui.pushDWriteFont('Bahnschrift:@System;Weight=Bold')
  text(state.weaponName or 'FIREARM', 14, 7, 330, 21, 16, white)
  ui.popDWriteFont()
  ui.pushDWriteFont('Segoe UI')
  text('IN MAG', 18, 32, 108, 16, 10, muted)
  text('RESERVE', 159, 32, 180, 16, 10, muted)
  ui.pushDWriteFont('Bahnschrift:@System;Weight=Bold')
  text(string.format('%02d', ammo), 14, 47, 108, 65, 62, ammoColor)
  text('/', 118, 52, 30, 57, 50, border)
  text(reserve ~= nil and string.format('%02d', reserve) or '--',
    159, 54, 185, 58, 54, cyan)
  ui.popDWriteFont()
  -- The numerical count remains authoritative; up to five icons fit the compact rail.
  for index = 1, math.min(mags, 5) do
    local x = 15 + (index - 1) * 10
    ui.drawRectFilled(point(x, 120), point(x + 6, 132), cyan, scale)
    ui.drawRect(point(x, 120), point(x + 6, 132), border, scale, nil, scale)
    ui.drawRectFilled(point(x - 1, 132), point(x + 7, 134), cyan)
  end
  text(string.format('%d RESERVE MAGS', mags), 72, 114, 126, 24, 10, white)
  text(reserve ~= nil and string.format('%d ROUNDS TOTAL', ammo + reserve) or '-- ROUNDS TOTAL',
    203, 114, 141, 24, 10, muted)
  ui.drawLine(point(14, 142), point(344, 142), border, scale)
  if reloading then
    local duration = state.reloadDuration or 0
    local progress = duration > 0 and math.clamp(1 - state.reloadRemaining / duration, 0, 1) or 0
    ui.drawLine(point(14, 142), point(14 + 330 * progress, 142),
      rgbm(1, 0.72, 0.25, 1), 2 * scale)
  end
  local function key(label, x, color)
    ui.drawRect(point(x, 147), point(x + 16, 163), border, 3 * scale, nil, scale)
    text(label, x + 4, 146, 12, 18, 11, color)
  end
  key('R', 14, white)
  text(reloading and string.format('RELOADING  %.1fs', state.reloadRemaining) or 'RELOAD',
    39, 145, 137, 20, 10, reloading and rgbm(1, 0.72, 0.25, 1) or white)
  local lethalColor = (state.lethalsRemaining or 0) > 0 and white or muted
  key('G', 184, lethalColor)
  text(string.format('%s  x%d', state.lethalName or 'LETHAL', state.lethalsRemaining or 0),
    207, 145, 137, 20, 10, lethalColor)
  ui.popDWriteFont()
end

local function drawStatusWidgets(size, scale, margin)
  local bottom = size.y - margin
  local height = 148 * scale
  local leftWidth = 330 * scale
  local leftMin = vec2(margin, bottom - height)
  local leftMax = vec2(margin + leftWidth, bottom)
  panel(leftMin, leftMax, scale, 0.86)
  ui.drawRectFilled(leftMin, vec2(leftMin.x + 4 * scale, leftMax.y),
    rgbm(0.22, 0.82, 0.98, 0.95), 2 * scale)
  ui.setCursor(leftMin + vec2(16, 10) * scale)
  ui.textColored('OPERATOR STATUS', rgbm(0.55, 0.78, 0.9, 0.9))
  ui.setCursor(leftMin + vec2(16, 31) * scale)
  local healthColor = bridge.localHealth <= 25 and rgbm(1, 0.2, 0.16, 1) or rgbm.colors.white
  ui.pushFont(ui.Font.Title)
  ui.textColored(string.format('HEALTH   %d', bridge.localHealth), healthColor)
  ui.popFont()
  local healthRatio = math.clamp(bridge.localHealth / math.max(1, bridge.localMaximumHealth), 0, 1)
  local healthBarMin = leftMin + vec2(16, 61) * scale
  local healthBarMax = leftMin + vec2(314, 72) * scale
  ui.drawRectFilled(healthBarMin, healthBarMax, rgbm(0.08, 0.12, 0.16, 0.94), 3 * scale)
  ui.drawRectFilled(healthBarMin, vec2(healthBarMin.x
    + (healthBarMax.x - healthBarMin.x) * healthRatio,
    healthBarMax.y), healthRatio <= 0.25 and rgbm(1, 0.2, 0.16, 1)
      or rgbm(0.25, 0.88, 0.72, 1), 3 * scale)

  ui.setCursor(leftMin + vec2(16, 79) * scale)
  ui.textColored(string.format('STAMINA  %d%%', bridge.localStamina),
    bridge.localStamina <= 20 and rgbm(1, 0.58, 0.16, 1) or rgbm(0.75, 0.88, 0.95, 1))
  local staminaBarMin = leftMin + vec2(16, 101) * scale
  local staminaBarMax = leftMin + vec2(314, 111) * scale
  ui.drawRectFilled(staminaBarMin, staminaBarMax, rgbm(0.08, 0.12, 0.16, 0.94), 3 * scale)
  ui.drawRectFilled(staminaBarMin, vec2(staminaBarMin.x
    + (staminaBarMax.x - staminaBarMin.x) * math.clamp(bridge.localStamina / 100, 0, 1),
    staminaBarMax.y),
    bridge.localStamina <= 20 and rgbm(1, 0.58, 0.16, 1)
      or rgbm(0.25, 0.72, 1, 1), 3 * scale)
  ui.setCursor(leftMin + vec2(16, 119) * scale)
  ui.text(string.format('K %d   D %d   SCORE %d', bridge.localKills, bridge.localDeaths,
    bridge.localScore))
  local linkText = bridge.linkState == 1 and 'LINK: ACTIVE'
    or bridge.linkState == 2 and 'LINK: INPUT SEND BLOCKED' or 'LINK: WAITING FOR PLAYER STATE'
  ui.setCursor(leftMin + vec2(190, 119) * scale)
  ui.textColored(linkText, bridge.linkState == 1 and rgbm(0.35, 1, 0.45, 1)
    or rgbm(1, 0.55, 0.2, 1))

  local activeWeapon = bridge.localActiveSlot == 1
    and bridge.localSecondaryWeapon or bridge.localMainWeapon
  drawAmmoPanel(size, scale, margin, {
    weaponName = itemNames[activeWeapon], imagePath = weaponImages[activeWeapon],
    ammo = bridge.localAmmo, reserveMagazines = bridge.localReserveMagazines,
    magazineCapacity = bridge.localMagazineCapacity, reloadDuration = bridge.localReloadDuration,
    reloadRemaining = bridge.localReloadRemaining, lethalName = itemNames[bridge.localLethal],
    lethalsRemaining = bridge.localLethalsRemaining,
  })
end

local function drawMatchAndFeed(size, scale, margin)
  local centerX = size.x * 0.5
  local clockWidth = 350 * scale
  panel(vec2(centerX - clockWidth * 0.5, margin), vec2(centerX + clockWidth * 0.5,
    margin + 42 * scale), scale, 0.76)
  ui.setCursor(vec2(centerX - clockWidth * 0.5, margin + 10 * scale))
  ui.textAligned(string.format('%02d:%02d   %s',
    math.floor(math.max(0, bridge.remainingSeconds) / 60),
    math.floor(math.max(0, bridge.remainingSeconds) % 60), matchTargetText()), 0.5,
    vec2(clockWidth, 24 * scale))

  local feedWidth = 410 * scale
  for index = 0, math.min(killFeedCapacity, bridge.killFeedCount) - 1 do
    ui.setCursor(vec2(size.x - margin - feedWidth, margin + index * 24 * scale))
    ui.textAligned(bridgeString(bridge.killFeed[index]), 1, vec2(feedWidth, 22 * scale))
  end
end

local function drawAimNameplate(size, scale, position, name, health, maximumHealth, friendly)
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

local function drawAimTarget(size, scale)
  local age = ui.time() - bridge.aimTargetUpdatedAt
  if bridge.aimTargetID == 255 or age < 0 or age > 0.15
      or bridge.cursorUnlocked ~= 0 or bridge.scoreboardHeld ~= 0
      or bridge.matchState ~= 1 or bridge.localHealth <= 0 then return end
  local ownIndex = localActorIndex()
  if ownIndex == nil then return end
  for index = 0, math.min(actorCapacity, bridge.actorCount) - 1 do
    if bridge.actorIDs[index] == bridge.aimTargetID and bridge.actorHealth[index] > 0
        and bit.band(bridge.actorFlags[index], 3) == 1 then
      drawAimNameplate(size, scale, bridge.aimTargetPosition, actorName(index),
        bridge.actorHealth[index], bridge.localMaximumHealth,
        isTeamMatch() and bridge.actorTeams[ownIndex] ~= 0
          and bridge.actorTeams[ownIndex] == bridge.actorTeams[index])
      return
    end
  end
end

local function drawAim(size, scale)
  if bridge.cursorUnlocked ~= 0 then return end
  local center = size * 0.5
  local gap, extent = 3 * scale, 9 * scale
  if bridge.adsActive == 0 then
    ui.drawLine(center - vec2(extent, 0), center - vec2(gap, 0), rgbm.colors.white, 2 * scale)
    ui.drawLine(center + vec2(gap, 0), center + vec2(extent, 0), rgbm.colors.white, 2 * scale)
    ui.drawLine(center - vec2(0, extent), center - vec2(0, gap), rgbm.colors.white, 2 * scale)
    ui.drawLine(center + vec2(0, gap), center + vec2(0, extent), rgbm.colors.white, 2 * scale)
  end
  if bridge.hitMarkerRemaining > 0 then
    local color = rgbm(1, 0.25, 0.15, math.min(1, bridge.hitMarkerRemaining * 7))
    ui.drawLine(center - vec2(8, 8) * scale, center - vec2(3, 3) * scale, color, 3 * scale)
    ui.drawLine(center + vec2(8, 8) * scale, center + vec2(3, 3) * scale, color, 3 * scale)
    ui.drawLine(center + vec2(8, -8) * scale, center + vec2(3, -3) * scale, color, 3 * scale)
    ui.drawLine(center + vec2(-8, 8) * scale, center + vec2(-3, 3) * scale, color, 3 * scale)
  end
end

local function drawLethalMedal(size, scale, itemID, count, age, imagePath)
  if count <= 0 or age < 0 or age >= 3 or (itemID ~= 16 and itemID ~= 17) then return end
  local alpha = math.clamp(math.min(age / 0.16, (3 - age) / 0.5), 0, 1)
  if alpha <= 0 then return end
  local p = vec2(size.x * 0.5, (100 - 10 * (1 - math.min(1, age / 0.18))) * scale)
  local gold = rgbm(1, 0.77, 0.31, alpha)
  local dimGold = rgbm(0.54, 0.39, 0.17, alpha)
  local function point(x, y) return p + vec2(x, y) * scale end
  -- Original medal treatment: brass medallion, short wings, cyan team-HUD accent.
  ui.drawCircleFilled(p, 36 * scale, rgbm(0.025, 0.038, 0.045, 0.94 * alpha), 48)
  ui.drawCircle(p, 36 * scale, gold, 48, 2 * scale)
  ui.drawCircle(p, 31 * scale, dimGold, 48, scale)
  for side = -1, 1, 2 do
    for index = 0, 2 do
      ui.drawLine(point(side * 42, -12 + index * 12),
        point(side * (76 - index * 9), -19 + index * 12), gold, 3 * scale)
    end
  end
  if imagePath ~= nil then
    -- Both grenade thumbnails share this alpha-safe crop (560 x 360 source).
    ui.drawImage(imagePath, point(-16, -21), point(16, 21),
      rgbm(1, 1, 1, alpha), vec2(155 / 560, 16 / 360), vec2(405 / 560, 344 / 360))
  else
    -- Remain legible while the thumbnail assets are still downloading.
    ui.drawRectFilled(point(-9, -12), point(9, 15), gold, 7 * scale)
    ui.drawRectFilled(point(-5, -19), point(5, -11), gold, scale)
    ui.drawLine(point(4, -18), point(15, 4), gold, 2 * scale)
  end
  ui.drawRectFilled(point(-132, 44), point(132, 96),
    rgbm(0.025, 0.038, 0.045, 0.88 * alpha), 4 * scale)
  ui.drawLine(point(-45, 43), point(45, 43), rgbm(0.1, 0.86, 0.94, alpha), 2 * scale)
  ui.pushDWriteFont('Bahnschrift:@System;Weight=Bold')
  ui.dwriteDrawTextClipped('GRENADE KILL', 22 * scale, point(-125, 47), point(125, 74),
    ui.Alignment.Center, ui.Alignment.Center, false, gold)
  ui.popDWriteFont()
  ui.pushDWriteFont('Segoe UI')
  local subtitle = count > 1 and string.format('%d ELIMINATIONS', count)
    or itemID == 16 and 'FRAG GRENADE' or 'STICKY GRENADE'
  ui.dwriteDrawTextClipped(subtitle, 11 * scale, point(-125, 74), point(125, 92),
    ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0.9, 0.95, 0.98, alpha))
  ui.popDWriteFont()
end

local function drawAwards(size, scale)
  if bridge.cursorUnlocked ~= 0 then return end
  if bridge.scoreboardHeld == 0 then
    drawLethalMedal(size, scale, bridge.lethalMedalItem, bridge.lethalMedalCount,
      bridge.lethalMedalAge, weaponImages[bridge.lethalMedalItem])
  end
  local center = size * 0.5
  for index = 0, math.min(awardPopupCapacity, bridge.awardPopupCount) - 1 do
    local alpha = math.clamp(bridge.awardPopupAlphas[index], 0, 1)
    ui.setCursor(center + vec2(34, -86 + index * 25) * scale)
    ui.pushFont(ui.Font.Title)
    ui.textColored(bridgeString(bridge.awardPopupTexts[index]), rgbm(1, 0.78, 0.22, alpha))
    ui.popFont()
  end
end

local function drawScoreboard(size, scale)
  if bridge.scoreboardHeld == 0 then return end
  local center = size * 0.5
  ui.drawRectFilled(vec2(), size, rgbm(0.005, 0.008, 0.012, 0.52))
  local width = math.min((isTeamMatch() and 1180 or 900) * scale, size.x * 0.94)
  local height = math.min(640 * scale, size.y * 0.92)
  local p1 = center - vec2(width, height) * 0.5
  local p2 = center + vec2(width, height) * 0.5
  panel(p1, p2, scale, 0.97)
  ui.drawRectFilled(p1, vec2(p2.x, p1.y + 6 * scale),
    isTeamMatch() and rgbm(0.12, 0.52, 1, 1) or rgbm(0.93, 0.67, 0.15, 1))
  ui.setCursor(p1 + vec2(28, 20) * scale)
  ui.textColored(isTeamMatch() and 'TEAM DEATHMATCH' or 'FREE FOR ALL',
    rgbm(0.72, 0.8, 0.86, 1))
  ui.setCursor(p1 + vec2(28, 43) * scale)
  ui.pushFont(ui.Font.Huge)
  ui.text('SCOREBOARD')
  ui.popFont()
  ui.setCursor(vec2(p2.x - 270 * scale, p1.y + 31 * scale))
  ui.textAligned(string.format('%02d:%02d  •  TARGET %d',
    math.floor(math.max(0, bridge.remainingSeconds) / 60),
    math.floor(math.max(0, bridge.remainingSeconds) % 60), bridge.killLimit), 1,
    vec2(240, 24) * scale)

  if isTeamMatch() then
    local team1, team2 = {}, {}
    for place = 1, #ranking do
      local index = ranking[place]
      if bridge.actorTeams[index] == 1 then team1[#team1 + 1] = index
      elseif bridge.actorTeams[index] == 2 then team2[#team2 + 1] = index end
    end
    local gap = 18 * scale
    local columnWidth = (width - 74 * scale - gap) * 0.5
    local columnTop = p1.y + 94 * scale
    local rowTop = columnTop + 86 * scale
    local rowHeight = 24 * scale
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
        local index = members[row]
        local y = rowTop + (row - 1) * rowHeight
        local own = bridge.actorIDs[index] == bridge.localActorID
        ui.drawRectFilled(vec2(x, y), vec2(x + columnWidth, y + rowHeight - 2 * scale),
          own and rgbm(0.12, 0.4, 0.64, 0.72)
            or rgbm(0.06, 0.075, 0.095, row % 2 == 0 and 0.86 or 0.62), 2 * scale)
        ui.drawRectFilled(vec2(x, y), vec2(x + 3 * scale, y + rowHeight - 2 * scale), color)
        ui.setCursor(vec2(x + 10 * scale, y + 3 * scale))
        ui.text(string.format('%2d  %-18s', row, string.sub(actorName(index), 1, 18)))
        ui.setCursor(vec2(x + columnWidth - 210 * scale, y + 3 * scale))
        ui.text(string.format('%5d   %3d   %3d   %3d', bridge.actorScores[index],
          bridge.actorKills[index], bridge.actorDeaths[index], bridge.actorHealth[index]))
      end
    end
    drawTeamColumn(1, team1, bridge.team1Kills, p1.x + 28 * scale,
      rgbm(0.18, 0.58, 1, 1))
    drawTeamColumn(2, team2, bridge.team2Kills,
      p1.x + 28 * scale + columnWidth + gap, rgbm(1, 0.24, 0.18, 1))
  else
    local listX = p1.x + 28 * scale
    local listWidth = width - 56 * scale
    local rowTop = p1.y + 126 * scale
    local rowHeight = 27 * scale
    ui.drawRectFilled(vec2(listX, p1.y + 94 * scale),
      vec2(listX + listWidth, p1.y + 121 * scale), rgbm(0.08, 0.1, 0.13, 0.96), 2 * scale)
    ui.setCursor(vec2(listX + 14 * scale, p1.y + 99 * scale))
    ui.textColored('POS   OPERATOR', rgbm(0.62, 0.69, 0.75, 1))
    ui.setCursor(vec2(listX + listWidth - 330 * scale, p1.y + 99 * scale))
    ui.textColored('SCORE        KILLS     DEATHS      HP', rgbm(0.62, 0.69, 0.75, 1))
    for place = 1, math.min(16, #ranking) do
      local index = ranking[place]
      local y = rowTop + (place - 1) * rowHeight
      local own = bridge.actorIDs[index] == bridge.localActorID
      local leader = place == 1
      ui.drawRectFilled(vec2(listX, y), vec2(listX + listWidth, y + rowHeight - 2 * scale),
        own and rgbm(0.12, 0.4, 0.64, 0.72)
          or rgbm(0.06, 0.075, 0.095, place % 2 == 0 and 0.86 or 0.62), 2 * scale)
      ui.drawRectFilled(vec2(listX, y), vec2(listX + (leader and 6 or 3) * scale,
        y + rowHeight - 2 * scale), leader and rgbm(0.93, 0.67, 0.15, 1)
          or rgbm(0.3, 0.36, 0.42, 0.8))
      ui.setCursor(vec2(listX + 14 * scale, y + 4 * scale))
      ui.text(string.format('%2d    %-24s', place, string.sub(actorName(index), 1, 24)))
      ui.setCursor(vec2(listX + listWidth - 330 * scale, y + 4 * scale))
      ui.text(string.format('%6d        %3d        %3d      %3d', bridge.actorScores[index],
        bridge.actorKills[index], bridge.actorDeaths[index], bridge.actorHealth[index]))
    end
  end
  ui.transparentWindow('asrc-fps-hud-scoreboard-controls',
    vec2(p1.x + 20 * scale, p2.y - 50 * scale), vec2(width - 40 * scale, 42 * scale),
    true, true, function()
      ui.setCursor(vec2(8, 8) * scale)
      local enabled = bridge.appPersistentCursor ~= 0
      if ui.checkbox('Keep mouse cursor visible after releasing TAB', enabled) then
        bridge.appPersistentCursor = enabled and 0 or 1
      end
      ui.sameLine(12 * scale)
      ui.textColored('Release TAB to close scoreboard', rgbm(0.75, 0.78, 0.84, 1))
    end)
end

local function drawMatchResults(size, rows, modeTitle, winnerText, countdown, localID, teamScores)
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

local function drawCompletion(size, scale)
  local rows, winner = {}, 'No winner'
  for _, index in ipairs(ranking) do
    rows[#rows + 1] = {id = bridge.actorIDs[index], name = actorName(index),
      score = bridge.actorScores[index], kills = bridge.actorKills[index],
      deaths = bridge.actorDeaths[index], team = bridge.actorTeams[index]}
    if bridge.actorIDs[index] == bridge.winnerID then winner = actorName(index) end
  end
  drawMatchResults(size, rows, matchModeTitle(),
    isTeamMatch() and (bridge.winnerTeam == 0 and 'DRAW' or ('WINNER: TEAM ' .. bridge.winnerTeam))
      or ('WINNER: ' .. winner), bridge.restartCountdownSeconds, bridge.localActorID,
    isTeamMatch() and string.format('TEAM 1   %d  :  %d   TEAM 2', bridge.team1Kills, bridge.team2Kills) or nil)
end

local function drawPickupPrompt(size, scale)
  if bridge.cursorUnlocked ~= 0 or bridge.scoreboardHeld ~= 0 then return end
  local text = bridgeString(bridge.pickupPrompt)
  if text == '' then return end
  local width = math.min(720 * scale, size.x * 0.82)
  local center = size * 0.5
  local textMin = vec2(center.x - width * 0.5, center.y + 188 * scale)
  local textMax = textMin + vec2(width, 34 * scale)
  ui.dwriteDrawTextClipped(text, 23 * scale,
    textMin + vec2(1.5, 1.5) * scale, textMax + vec2(1.5, 1.5) * scale,
    ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0, 0, 0, 0.92))
  ui.dwriteDrawTextClipped(text, 23 * scale, textMin, textMax,
    ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0.92, 0.97, 1, 1))
  local barMin = vec2(center.x - 145 * scale, center.y + 228 * scale)
  local barMax = vec2(center.x + 145 * scale, center.y + 233 * scale)
  ui.drawRectFilled(barMin, barMax, rgbm(0.09, 0.13, 0.17, 1), 3 * scale)
  ui.drawRectFilled(barMin, vec2(math.lerp(barMin.x, barMax.x,
    math.clamp(bridge.pickupProgress, 0, 1)), barMax.y),
    rgbm(0.2, 0.72, 0.96, 1), 3 * scale)
end

local function drawGrenadeMarker(position, remaining, size, scale)
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
  local radius = 6.5 * scale * pulse
  ui.drawCircleFilled(screenPosition + vec2(1.5, 1.5) * scale, radius + 2 * scale,
    rgbm(0, 0, 0, 0.78), 20)
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

local function drawGrenadeIndicators(size, scale)
  if bridge.cursorUnlocked ~= 0 or bridge.scoreboardHeld ~= 0 then return end
  local age = math.clamp(ui.time() - bridge.grenadeThreatUpdateTime, 0, 0.1)
  for index = 0, math.min(grenadeCapacity, bridge.grenadeThreatCount) - 1 do
    local remaining = bridge.grenadeThreatRemaining[index] - age
    if remaining > 0 then
      local position = bridge.grenadeThreatPositions[index]
        + bridge.grenadeThreatVelocities[index] * age
      drawGrenadeMarker(position, remaining, size, scale)
    end
  end
end

local function drawBoundaryWarning(size, scale)
  if bridge.outOfBoundsRemaining <= 0 then return end
  local center = size * 0.5
  local titleMin = vec2(0, center.y - 205 * scale)
  local titleMax = vec2(size.x, center.y - 125 * scale)
  local numberMin = vec2(0, center.y - 120 * scale)
  local numberMax = vec2(size.x, center.y + 145 * scale)
  local countdown = tostring(math.max(1, math.ceil(bridge.outOfBoundsRemaining)))
  ui.drawRectFilled(vec2(), size, rgbm(0.12, 0, 0, 0.34))
  ui.dwriteDrawTextClipped('RETURN TO PLAYABLE AREA', 46 * scale,
    titleMin + vec2(2, 2) * scale, titleMax + vec2(2, 2) * scale,
    ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0, 0, 0, 0.9))
  ui.dwriteDrawTextClipped('RETURN TO PLAYABLE AREA', 46 * scale, titleMin, titleMax,
    ui.Alignment.Center, ui.Alignment.Center, false, rgbm(1, 0.86, 0.82, 1))
  ui.dwriteDrawTextClipped(countdown, 190 * scale,
    numberMin + vec2(4, 4) * scale, numberMax + vec2(4, 4) * scale,
    ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0, 0, 0, 0.92))
  ui.dwriteDrawTextClipped(countdown, 190 * scale, numberMin, numberMax,
    ui.Alignment.Center, ui.Alignment.Center, false, rgbm(1, 0.24, 0.14, 1))
end

local function drawMatchStart(size, scale)
  if bridge.matchState ~= 0 then return end
  local center = size * 0.5
  ui.drawRectFilled(vec2(), size, rgbm(0.005, 0.008, 0.012, 0.28))
  local p1 = center + vec2(-330, -170) * scale
  local p2 = center + vec2(330, 170) * scale
  panel(p1, p2, scale, 0.94)
  ui.drawRectFilled(p1, vec2(p2.x, p1.y + 5 * scale),
    rgbm(0.18, 0.62, 0.96, 1), 5 * scale)
  ui.dwriteDrawTextClipped('GAME MODE', 22 * scale,
    center + vec2(-330, -125) * scale, center + vec2(330, -95) * scale,
    ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0.45, 0.72, 0.95, 1))
  ui.dwriteDrawTextClipped(matchModeTitle(), 44 * scale,
    center + vec2(-330, -88) * scale, center + vec2(330, -28) * scale,
    ui.Alignment.Center, ui.Alignment.Center, false, rgbm.colors.white)
  if bridge.startCountdownSeconds > 0 then
    ui.dwriteDrawTextClipped('MATCH STARTS IN', 18 * scale,
      center + vec2(-330, 0) * scale, center + vec2(330, 28) * scale,
      ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0.7, 0.76, 0.82, 1))
    ui.dwriteDrawTextClipped(tostring(math.max(1,
      math.ceil(bridge.startCountdownSeconds))), 82 * scale,
      center + vec2(-330, 30) * scale, center + vec2(330, 140) * scale,
      ui.Alignment.Center, ui.Alignment.Center, false, rgbm(1, 0.72, 0.18, 1))
  else
    ui.dwriteDrawTextClipped('WAITING FOR FIRST HUMAN PLAYER', 23 * scale,
      center + vec2(-330, 12) * scale, center + vec2(330, 52) * scale,
      ui.Alignment.Center, ui.Alignment.Center, false, rgbm(0.78, 0.82, 0.86, 1))
  end
end

local function drawHud()
  local size = ui.windowSize()
  local scale = math.clamp(math.min(size.x / 1920, size.y / 1080), 0.75, 1.65)
  local margin = 28 * scale
  if bridge.cursorUnlocked ~= 0 then
    ui.captureMouse(true)
    ui.setMouseCursor(ui.MouseCursor.Arrow)
  end
  buildRanking()
  if bridge.matchState == 2 then
    drawCompletion(size, scale)
    return
  end
  drawRadar(size, scale, margin)
  drawCompactRanking(scale, margin)
  drawStatusWidgets(size, scale, margin)
  drawMatchAndFeed(size, scale, margin)
  drawAim(size, scale)
  drawAimTarget(size, scale)
  drawAwards(size, scale)
  drawGrenadeIndicators(size, scale)
  drawPickupPrompt(size, scale)
  drawBoundaryWarning(size, scale)
  drawMatchStart(size, scale)
  drawScoreboard(size, scale)
  local clientError = bridgeString(bridge.clientError)
  if clientError ~= '' then
    ui.setCursor(vec2(margin, size.y - margin - 126 * scale))
    ui.textColored(clientError, rgbm(1, 0.18, 0.12, 1))
  end
end

local function exclusiveHud(mode)
  if mode ~= 'game' or not bridgeIsLive() then return false end
  drawHud()
  return true
end

local exclusiveSubscription = ui.onExclusiveHUD(exclusiveHud, true)

function script.update(dt)
  fpsCameraClip.update()
  local audioLive = bridgeIsLive()
  audioPlayer.update(dt)
  if audioPlayer.wasLive and not audioLive then audioPlayer.reset() end
  audioPlayer.wasLive = audioLive
  bridge.appProtocol = bridgeProtocol
  bridge.appHeartbeat = ui.time()
  if bridge.protocol ~= 0 and bridge.protocol ~= bridgeProtocol and not mismatchLogged then
    mismatchLogged = true
    ac.warn(string.format('[ASRC FPS HUD] bridge mismatch: app=%d online=%d',
      bridgeProtocol, bridge.protocol))
  end
  if bridgeIsLive() and not appCursorInitialized then
    bridge.appPersistentCursor = bridge.persistentCursor
    appCursorInitialized = true
    ac.log('[ASRC FPS HUD] authoritative bridge connected')
  elseif not bridgeIsLive() then
    appCursorInitialized = false
  end
end

function appOverlay(dt)
  -- Keeps the app eagerly loaded. Drawing and exclusivity are owned by
  -- ui.onExclusiveHUD() so regular AC UI remains available outside FPS gameplay.
end
