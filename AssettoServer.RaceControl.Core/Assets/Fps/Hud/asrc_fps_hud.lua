local license = [[
Copyright (C) 2026 Niewiarowski, compujuckel

This program is free software: you can redistribute it and/or modify it under the terms of the
GNU Affero General Public License as published by the Free Software Foundation, version 3.
]]

local bridgeProtocol = 5
local actorCapacity = 32
local killFeedCapacity = 6
local awardPopupCapacity = 4
local bridge = ac.connect({
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
  actorIDs = ac.StructItem.array(ac.StructItem.byte(), actorCapacity),
  actorFlags = ac.StructItem.array(ac.StructItem.byte(), actorCapacity),
  radarFlags = ac.StructItem.array(ac.StructItem.byte(), actorCapacity),
  actorPositions = ac.StructItem.array(ac.StructItem.vec3(), actorCapacity),
  actorYaws = ac.StructItem.array(ac.StructItem.float(), actorCapacity),
  actorHealth = ac.StructItem.array(ac.StructItem.uint16(), actorCapacity),
  actorKills = ac.StructItem.array(ac.StructItem.uint16(), actorCapacity),
  actorDeaths = ac.StructItem.array(ac.StructItem.uint16(), actorCapacity),
  actorScores = ac.StructItem.array(ac.StructItem.uint32(), actorCapacity),
  actorNames = ac.StructItem.array(ac.StructItem.string(32), actorCapacity),
  killFeedCount = ac.StructItem.byte(),
  killFeed = ac.StructItem.array(ac.StructItem.string(72), killFeedCapacity),
  awardPopupCount = ac.StructItem.byte(),
  awardPopupTexts = ac.StructItem.array(ac.StructItem.string(64), awardPopupCapacity),
  awardPopupAlphas = ac.StructItem.array(ac.StructItem.float(), awardPopupCapacity),
}, false, ac.SharedNamespace.Shared)

local ranking = {}
local appCursorInitialized = false
local mismatchLogged = false
local assettoRoot = ac.getFolder(ac.FolderID.Root)
local weaponImagePath = (assettoRoot ~= nil and assettoRoot ~= '')
  and (assettoRoot .. '/apps/lua/asrc_fps_hud/asrc_carbine_hud.png')
  or 'asrc_carbine_hud.png'
local itemNames = {
  [1] = 'ASSAULT RIFLE', [2] = 'MP5 SMG',
  [3] = 'DESERT EAGLE', [4] = 'COLT 1911',
  [16] = 'FRAG GRENADE', [17] = 'STICKY GRENADE',
}

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
        ui.drawCircleFilled(center + point, 4.5 * scale, rgbm(1, 0.22, 0.15, 0.95), 16)
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
  local width = 310 * scale
  local rows = math.min(8, #ranking)
  panel(vec2(margin, top), vec2(margin + width, top + (34 + rows * 23) * scale), scale, 0.72)
  ui.setCursor(vec2(margin + 12 * scale, top + 8 * scale))
  ui.text('DEATHMATCH')
  for place = 1, rows do
    local index = ranking[place]
    ui.setCursor(vec2(margin + 12 * scale, top + (10 + place * 23) * scale))
    ui.text(string.format('%2d  %-16s  %4d  %2d/%2d', place, actorName(index),
      bridge.actorScores[index], bridge.actorKills[index], bridge.actorDeaths[index]))
  end
end

local function drawStatusWidgets(size, scale, margin)
  local bottom = size.y - margin
  local height = 148 * scale
  local leftWidth = 330 * scale
  local rightWidth = 390 * scale
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

  local right = size.x - margin
  local rightMin = vec2(right - rightWidth, bottom - height)
  local rightMax = vec2(right, bottom)
  panel(rightMin, rightMax, scale, 0.86)
  ui.drawRectFilled(vec2(rightMax.x - 4 * scale, rightMin.y), rightMax,
    rgbm(0.22, 0.82, 0.98, 0.95), 2 * scale)
  ui.drawImage(weaponImagePath, rightMin + vec2(8, 26) * scale,
    rightMin + vec2(252, 124) * scale, rgbm(1, 1, 1, 0.98))
  ui.setCursor(rightMin + vec2(16, 10) * scale)
  if bridge.localReloadRemaining > 0 then
    ui.text(string.format('RELOADING  %.1fs', bridge.localReloadRemaining))
  else
    local activeWeapon = bridge.localActiveSlot == 1
      and bridge.localSecondaryWeapon or bridge.localMainWeapon
    ui.text(itemNames[activeWeapon] or 'FIREARM')
  end
  ui.setCursor(rightMin + vec2(258, 32) * scale)
  ui.pushFont(ui.Font.Title)
  ui.text(string.format('%02d', bridge.localAmmo))
  ui.popFont()
  ui.setCursor(rightMin + vec2(258, 64) * scale)
  ui.text(string.format('%d RESERVE MAGS', bridge.localReserveMagazines))
  ui.setCursor(rightMin + vec2(258, 89) * scale)
  ui.text('R  RELOAD')
  ui.setCursor(rightMin + vec2(258, 108) * scale)
  ui.text(string.format('G  %s  x%d', itemNames[bridge.localLethal] or 'LETHAL',
    bridge.localLethalsRemaining))
  if bridge.localReloadRemaining > 0 then
    local reloadMin = rightMin + vec2(258, 115) * scale
    local reloadMax = rightMin + vec2(374, 125) * scale
    ui.drawRectFilled(reloadMin, reloadMax, rgbm(0.08, 0.12, 0.16, 0.94), 3 * scale)
    ui.drawRectFilled(reloadMin, vec2(reloadMin.x
      + (reloadMax.x - reloadMin.x) * math.clamp(1 - bridge.localReloadRemaining / 1.8, 0, 1),
      reloadMax.y),
      rgbm(1, 0.7, 0.2, 1), 3 * scale)
  end
end

local function drawMatchAndFeed(size, scale, margin)
  local centerX = size.x * 0.5
  local clockWidth = 230 * scale
  panel(vec2(centerX - clockWidth * 0.5, margin), vec2(centerX + clockWidth * 0.5,
    margin + 42 * scale), scale, 0.76)
  ui.setCursor(vec2(centerX - clockWidth * 0.5, margin + 10 * scale))
  ui.textAligned(string.format('%02d:%02d   TARGET %d',
    math.floor(math.max(0, bridge.remainingSeconds) / 60),
    math.floor(math.max(0, bridge.remainingSeconds) % 60), bridge.killLimit), 0.5,
    vec2(clockWidth, 24 * scale))

  local feedWidth = 410 * scale
  for index = 0, math.min(killFeedCapacity, bridge.killFeedCount) - 1 do
    ui.setCursor(vec2(size.x - margin - feedWidth, margin + index * 24 * scale))
    ui.textAligned(bridgeString(bridge.killFeed[index]), 1, vec2(feedWidth, 22 * scale))
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

local function drawAwards(size, scale)
  if bridge.cursorUnlocked ~= 0 then return end
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
  local half = vec2(math.min(390 * scale, size.x * 0.44), math.min(280 * scale, size.y * 0.44))
  local p1, p2 = center - half, center + half
  panel(p1, p2, scale, 0.94)
  ui.setCursor(p1 + vec2(28, 22) * scale)
  ui.pushFont(ui.Font.Title)
  ui.text('DEATHMATCH SCOREBOARD')
  ui.popFont()
  ui.setCursor(p1 + vec2(28, 66) * scale)
  ui.text('POS   PLAYER                    SCORE   KILLS   DEATHS   HEALTH')
  for place = 1, math.min(16, #ranking) do
    local index = ranking[place]
    ui.setCursor(p1 + vec2(28, 70 + place * 27) * scale)
    ui.text(string.format('%2d    %-24s   %5d    %3d      %3d      %3d', place,
      actorName(index), bridge.actorScores[index], bridge.actorKills[index],
      bridge.actorDeaths[index], bridge.actorHealth[index]))
  end
  ui.transparentWindow('asrc-fps-hud-scoreboard-controls', p1 + vec2(20, 505) * scale,
    vec2(740, 48) * scale, true, true, function()
      ui.setCursor(vec2(8, 8) * scale)
      local enabled = bridge.appPersistentCursor ~= 0
      if ui.checkbox('Keep mouse cursor visible after releasing TAB', enabled) then
        bridge.appPersistentCursor = enabled and 0 or 1
      end
      ui.sameLine(12 * scale)
      ui.textColored('Release TAB to close scoreboard', rgbm(0.75, 0.78, 0.84, 1))
    end)
end

local function drawCompletion(size, scale)
  if bridge.matchState ~= 2 then return end
  local center = size * 0.5
  local p1, p2 = center - vec2(260, 150) * scale, center + vec2(260, 150) * scale
  panel(p1, p2, scale, 0.94)
  ui.setCursor(p1 + vec2(28, 24) * scale)
  ui.pushFont(ui.Font.Huge)
  ui.text('MATCH COMPLETE')
  ui.popFont()
  local winner = 'No winner'
  for index = 0, math.min(actorCapacity, bridge.actorCount) - 1 do
    if bridge.actorIDs[index] == bridge.winnerID then winner = actorName(index) end
  end
  ui.text('Winner: ' .. winner)
  for place = 1, math.min(8, #ranking) do
    local index = ranking[place]
    ui.text(string.format('%2d. %-20s  %5d pts  %3d K  %3d D', place, actorName(index),
      bridge.actorScores[index], bridge.actorKills[index], bridge.actorDeaths[index]))
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
  drawRadar(size, scale, margin)
  drawCompactRanking(scale, margin)
  drawStatusWidgets(size, scale, margin)
  drawMatchAndFeed(size, scale, margin)
  drawAim(size, scale)
  drawAwards(size, scale)
  drawScoreboard(size, scale)
  drawCompletion(size, scale)
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
