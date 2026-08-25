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
local inputSendOk = true
local gameplayActive = false
local previousGameplayActive = nil
local firstSnapshotLogged = false
local localActorSnapshotLogged = false
local inputDiagnosticAccumulator = 0
local renderDiagnosticAccumulator = 0
local cameraDiagnosticAccumulator = 1
local inputWasActive = false
local camera, cameraError = ac.grabCamera('AssettoServer FPS deathmatch')
if camera ~= nil then camera.ownShare = 0 end
local carsRoot = ac.findNodes('carsRoot:yes')
local hiddenCarrierRoots = {}

local function vec3Text(value)
  return string.format('(%.3f, %.3f, %.3f)', value.x, value.y, value.z)
end

ac.log(string.format('[ASRC FPS] script loaded: session=%s carIndex=%s cameraActive=%s cameraError=%s',
  tostring(localSessionID), tostring(car.index),
  tostring(camera ~= nil and camera:active()), tostring(cameraError)))

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

local snapshotEvent = ac.OnlineEvent({
  ac.StructItem.key('ASRC_FpsSnapshot'),
  sequence = ac.StructItem.uint32(),
  count = ac.StructItem.byte(),
  actorIDs = ac.StructItem.array(ac.StructItem.byte(), capacity),
  flags = ac.StructItem.array(ac.StructItem.byte(), capacity),
  positions = ac.StructItem.array(ac.StructItem.vec3(), capacity),
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
        ac.log(string.format(
          '[ASRC FPS] local actor snapshot acquired: actor=%s position=%s yaw=%.3f flags=%s health=%s',
          tostring(id), vec3Text(actor.target), actor.targetYaw, tostring(actor.flags),
          tostring(actor.health)))
      end
      local wasDead = bit.band(previousFlags, 2) ~= 0
      local isDead = bit.band(actor.flags, 2) ~= 0
      if not actor.localInitialized or (wasDead and not isDead) then
        yaw = message.yaws[i]
        pitch = message.pitches[i]
        actor.render:set(actor.target)
        actor.localInitialized = true
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

local function ensureAvatar(actor)
  if actor.id == localSessionID or actor.root ~= nil then return end
  local root = carsRoot:createBoundingSphereNode('ASRC_FPS_' .. actor.id, 1.5)
  local ok, model = pcall(function() return root:loadKN5('content/objects3D/pitcrew.kn5') end)
  if ok and model ~= nil then
    pcall(function() model:setAnimation('content/objects3D/pitcrew_idle_up.ksanim', 0, true) end)
    root:setVirtualCarFlag(true)
    actor.root = root
  else
    root:dispose()
    actor.root = false
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
  -- Keep the holder reserved for the FPS mode, but fully yield its output to
  -- AC while menus are open. Re-grabbing only after Drive is unreliable: AC
  -- or another script can already own it, leaving the server actor moving
  -- while the player keeps seeing the stationary carrier camera.
  if camera ~= nil and camera:active() then camera.ownShare = 0 end
end

function script.update(dt)
  hideCarrierCars()

  local localActor = actors[localSessionID]
  local move = vec2()
  local sprint = false
  gameplayActive = fpsGameplayIsActive()
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
    -- Do not lock user controls here: some CSP input backends return a neutral
    -- state while that lock is refreshed. Gentle Stop keeps the hidden carrier
    -- still without suppressing the FPS input sources read below.
    physics.setGentleStop(car.index, true)
    cameraDiagnosticAccumulator = cameraDiagnosticAccumulator + dt
    if camera == nil or not camera:active() then
      camera, cameraError = ac.grabCamera('AssettoServer FPS deathmatch')
      if camera ~= nil and camera:active() then
        ac.log('[ASRC FPS] FPS camera acquired during gameplay')
        cameraDiagnosticAccumulator = 0
      elseif cameraDiagnosticAccumulator >= 1 then
        ac.log(string.format('[ASRC FPS] FPS camera unavailable: error=%s', tostring(cameraError)))
        cameraDiagnosticAccumulator = 0
      end
    end

    -- Main/pits/results UI was excluded above. Once gameplay is active, FPS
    -- owns the pointer even if a third-party app incorrectly asks for it.
    local mouse = ac.accessMouseDelta(true, true, true)
    local rightX = clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.RightThumbX))
    local rightY = clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.RightThumbY))
    yaw = yaw + mouse.x * 0.0022 + rightX * dt * 2.8
    pitch = math.clamp(pitch - mouse.y * 0.0022 + rightY * dt * 2.2, -1.45, 1.45)

    -- Read both explicit FPS controls and AC's mapped driving controls. The
    -- latter remains available while game-rule locks suppress the carrier car,
    -- and covers GameInput devices which are not exposed as raw XInput pad 0.
    local mapped = physics.getCarInputControls()
    local keyboardX = inputAxis(ac.KeyIndex.A, ac.KeyIndex.D, ac.KeyIndex.Left, ac.KeyIndex.Right)
    local keyboardY = inputAxis(ac.KeyIndex.S, ac.KeyIndex.W, ac.KeyIndex.Down, ac.KeyIndex.Up)
    local rawX = clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.LeftThumbX))
    local rawY = -clampStick(ac.getGamepadAxisValue(0, ac.GamepadAxis.LeftThumbY))
    move = vec2(
      selectInput(keyboardX, rawX, clampStick(mapped.steer)),
      selectInput(keyboardY, rawY, clampStick(mapped.gas - mapped.brake)))
    if move:lengthSquared() > 1 then move:normalize() end
    local gamepadFire = ac.getGamepadAxisValue(0, ac.GamepadAxis.RightTrigger) > 0.35
    local fire = ac.getUI().isMouseLeftKeyDown or gamepadFire
    sprint = ac.isKeyDown(ac.KeyIndex.LeftShift) or ac.isKeyDown(ac.KeyIndex.RightShift)
      or ac.isGamepadButtonPressed(0, ac.GamepadButton.LeftThumb)
    local buttons = (fire and 1 or 0) + (sprint and 2 or 0)

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
    releaseFpsCamera()
    sendAccumulator = 0
    inputDiagnosticAccumulator = 0
    inputWasActive = false
  end

  if gameplayActive and localActor ~= nil and bit.band(localActor.flags, 1) ~= 0
      and bit.band(localActor.flags, 2) == 0 then
    local forward = vec2(math.sin(yaw), math.cos(yaw))
    local right = vec2(forward.y, -forward.x)
    local predicted = forward * move.y + right * move.x
    localActor.render:add(vec3(predicted.x, 0, predicted.y) * (sprint and 9 or 6) * dt)
  end

  for _, actor in pairs(actors) do
    local blend = 1 - math.exp(-dt * (actor.id == localSessionID and 6 or 18))
    actor.render:set(math.lerp(actor.render, actor.target, blend))
    actor.yaw = math.lerpAngle(actor.yaw, actor.targetYaw, blend)
    ensureAvatar(actor)
    if actor.root then
      local active = bit.band(actor.flags, 1) ~= 0 and bit.band(actor.flags, 2) == 0
      actor.root:setVisible(active)
      actor.root:setPosition(actor.render)
      actor.root:setOrientation(vec3(math.sin(actor.yaw), 0, math.cos(actor.yaw)), vec3(0, 1, 0))
    end
  end

  if gameplayActive and camera ~= nil and localActor ~= nil then
    local look = vec3(math.sin(yaw) * math.cos(pitch), math.sin(pitch), math.cos(yaw) * math.cos(pitch))
    camera.ownShare = 1
    camera.fov = 72
    camera.transform.position = localActor.render + vec3(0, 1.65, 0)
    camera.transform.look = look
    camera.transform.up = vec3(0, 1, 0)
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
        '[ASRC FPS] render state: actor=%s target=%s render=%s error=%.3f flags=%s cameraActive=%s cameraShare=%s',
        tostring(localActor.id), vec3Text(localActor.target), vec3Text(localActor.render),
        (localActor.target - localActor.render):length(), tostring(localActor.flags),
        tostring(camera ~= nil and camera:active()),
        tostring(camera ~= nil and camera.ownShare or 'nil')))
    end
  end

  hitMarker = math.max(0, hitMarker - dt)
  for i = #killFeed, 1, -1 do
    killFeed[i].ttl = killFeed[i].ttl - dt
    if killFeed[i].ttl <= 0 then table.remove(killFeed, i) end
  end
end

function script.drawUI()
  if not gameplayActive then return end
  local size = ui.windowSize()
  local center = size / 2
  ui.drawLine(center - vec2(9, 0), center - vec2(3, 0), rgbm.colors.white, 2)
  ui.drawLine(center + vec2(3, 0), center + vec2(9, 0), rgbm.colors.white, 2)
  ui.drawLine(center - vec2(0, 9), center - vec2(0, 3), rgbm.colors.white, 2)
  ui.drawLine(center + vec2(0, 3), center + vec2(0, 9), rgbm.colors.white, 2)
  if hitMarker > 0 then
    local c = rgbm(1, 0.25, 0.15, math.min(1, hitMarker * 7))
    ui.drawLine(center - vec2(8, 8), center - vec2(3, 3), c, 3)
    ui.drawLine(center + vec2(8, 8), center + vec2(3, 3), c, 3)
    ui.drawLine(center + vec2(8, -8), center + vec2(3, -3), c, 3)
    ui.drawLine(center + vec2(-8, 8), center + vec2(-3, 3), c, 3)
  end

  local actor = actors[localSessionID]
  ui.setCursor(vec2(28, size.y - 94))
  ui.pushFont(ui.Font.Title)
  ui.text(string.format('HEALTH  %d', actor and actor.health or 0))
  ui.text(string.format('K %d   D %d', actor and actor.kills or 0, actor and actor.deaths or 0))
  ui.popFont()
  ui.textColored(actor == nil and 'LINK: WAITING FOR PLAYER STATE'
      or (inputSendOk and 'LINK: ACTIVE' or 'LINK: INPUT SEND BLOCKED'),
    actor ~= nil and inputSendOk and rgbm(0.35, 1, 0.45, 1) or rgbm(1, 0.55, 0.2, 1))
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
  ui.setCursor(vec2(28, 28))
  ui.text('DEATHMATCH')
  for i = 1, math.min(8, #ranking) do
    local rankedActor = ranking[i]
    ui.text(string.format('%2d  %-18s  %2d / %2d', i,
      names[rankedActor.id] or ('Player ' .. rankedActor.id), rankedActor.kills, rankedActor.deaths))
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
