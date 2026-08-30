"use strict";

const ui = {
  connection: document.querySelector(".connection"),
  serverState: document.getElementById("server-state"),
  webAddress: document.getElementById("web-address"),
  updatedAt: document.getElementById("updated-at"),
  liveTitle: document.getElementById("live-title"),
  metricSession: document.getElementById("metric-session"),
  metricTrack: document.getElementById("metric-track"),
  metricTimeLabel: document.getElementById("metric-time-label"),
  metricTime: document.getElementById("metric-time"),
  metricTargetLabel: document.getElementById("metric-target-label"),
  metricTarget: document.getElementById("metric-target"),
  metricActive: document.getElementById("metric-active"),
  mapTitle: document.getElementById("map-title"),
  mapCaption: document.getElementById("map-caption"),
  mapEmpty: document.getElementById("map-empty"),
  canvas: document.getElementById("live-map"),
  rosterCaption: document.getElementById("roster-caption"),
  rosterBody: document.getElementById("roster-body"),
  launcherStatus: document.getElementById("launcher-status"),
  sessionControlTitle: document.getElementById("session-control-title"),
  environmentPanel: document.getElementById("environment-panel"),
  environmentWeather: document.getElementById("environment-weather"),
  environmentTime: document.getElementById("environment-time"),
  applyEnvironment: document.getElementById("apply-environment"),
  selectedPlayerPanel: document.getElementById("selected-player-panel"),
  selectedPlayer: document.getElementById("selected-player"),
  selectedPlayerCaption: document.getElementById("selected-player-caption"),
  selectedPlayerType: document.getElementById("selected-player-type"),
  selectedPlayerHealth: document.getElementById("selected-player-health"),
  selectedPlayerScore: document.getElementById("selected-player-score"),
  toast: document.getElementById("toast"),
};

let status = null;
let track = null;
let controlToken = "";
let actionPending = false;
let toastTimer = 0;
let selectedPlayerId = null;
let environmentDirty = false;
let pendingEnvironment = null;

document.querySelectorAll(".nav-item").forEach(button => {
  button.addEventListener("click", () => {
    document.querySelectorAll(".nav-item").forEach(item => item.classList.toggle("active", item === button));
    document.querySelectorAll(".view").forEach(view => view.classList.toggle("active", view.id === `view-${button.dataset.view}`));
    requestAnimationFrame(drawMap);
  });
});

document.querySelectorAll(".action").forEach(button => {
  if (button.dataset.action) button.addEventListener("click", () => executeAction(button.dataset.action));
});
ui.applyEnvironment.addEventListener("click", executeEnvironment);
ui.environmentWeather.addEventListener("change", () => { environmentDirty = true; });
ui.environmentTime.addEventListener("change", () => { environmentDirty = true; });
ui.selectedPlayer.addEventListener("change", () => {
  selectedPlayerId = Number(ui.selectedPlayer.value);
  renderSelectedPlayer(status?.live?.cars || []);
});
document.querySelectorAll(".collapsible").forEach(panel => panel.addEventListener("toggle", () => requestAnimationFrame(drawMap)));

new ResizeObserver(drawMap).observe(ui.canvas.parentElement);
window.addEventListener("resize", drawMap);

async function fetchJson(path) {
  const response = await fetch(path, { cache: "no-store" });
  if (response.status === 204) return null;
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return response.json();
}

async function pollStatus() {
  try {
    status = await fetchJson("/api/v1/status");
    controlToken = status.controlToken || "";
    renderStatus();
  } catch (error) {
    renderDisconnected(error);
  }
}

async function pollTrack() {
  try {
    const next = await fetchJson("/api/v1/track");
    if (next) track = next;
    drawMap();
  } catch {
    // State polling owns the visible connection warning.
  }
}

async function executeAction(action) {
  if (!controlToken || actionPending) return;
  actionPending = true;
  renderButtons();
  try {
    const response = await fetch(`/api/v1/actions/${action}`, {
      method: "POST",
      headers: { "X-ASRC-Control": controlToken },
    });
    const payload = await response.json().catch(() => ({ message: response.statusText }));
    if (!response.ok) throw new Error(payload.message || "Action was rejected.");
    showToast(payload.message || "Action accepted.");
    await pollStatus();
  } catch (error) {
    showToast(error.message || String(error), true);
  } finally {
    actionPending = false;
    renderButtons();
  }
}

async function executeEnvironment() {
  if (!controlToken || actionPending) return;
  actionPending = true;
  renderButtons();
  const request = {
    weatherType: Number(ui.environmentWeather.value),
    timeOfDaySeconds: Number(ui.environmentTime.value),
  };
  try {
    const response = await fetch("/api/v1/environment", {
      method: "POST",
      headers: { "X-ASRC-Control": controlToken, "Content-Type": "application/json" },
      body: JSON.stringify(request),
    });
    const payload = await response.json().catch(() => ({ message: response.statusText }));
    if (!response.ok) throw new Error(payload.message || "Environment update was rejected.");
    pendingEnvironment = request;
    showToast(payload.message || "Environment update requested.");
    await pollStatus();
  } catch (error) {
    showToast(error.message || String(error), true);
  } finally {
    actionPending = false;
    renderButtons();
  }
}

function renderStatus() {
  const launcher = status.launcher;
  const live = status.live;
  const online = Boolean(launcher.canStopServer || live?.serverRunning);
  ui.connection.classList.toggle("online", online);
  ui.serverState.textContent = launcher.serverState || (online ? "ONLINE" : "OFFLINE");
  ui.webAddress.textContent = new URL(status.webAddress).host;
  ui.updatedAt.textContent = `Updated ${new Date(status.generatedAt).toLocaleTimeString()}`;
  ui.launcherStatus.textContent = launcher.status || "Ready";

  const isFps = Boolean(live?.isFps || launcher.mode === "FPS");
  const sessionLabel = isFps ? "MATCH" : "RACE";
  ui.liveTitle.textContent = `LIVE ${sessionLabel}`;
  ui.sessionControlTitle.textContent = `${sessionLabel} CONTROL`;
  document.querySelector('[data-action="start-session"]').textContent = `START ${sessionLabel}`;
  document.querySelector('[data-action="stop-session"]').textContent = `STOP ${sessionLabel}`;
  document.querySelector('[data-action="restart-session"]').textContent = `RESTART ${sessionLabel}`;

  ui.metricSession.textContent = live?.session?.type || live?.session?.name || launcher.sessionLabel || "—";
  ui.metricTrack.textContent = displayTrack(launcher.track, launcher.layout);
  ui.metricActive.textContent = String((live?.cars || []).filter(car => car.isActive).length);

  if (isFps) {
    ui.metricTimeLabel.textContent = "TIME LEFT";
    ui.metricTime.textContent = formatDuration(live?.session?.timeLeftMilliseconds);
    ui.metricTargetLabel.textContent = "TARGET";
    ui.metricTarget.textContent = live?.session?.killLimit ? `${live.session.killLimit} KILLS` : "—";
    ui.mapTitle.textContent = "ARENA MAP";
    ui.environmentPanel.hidden = false;
    ui.selectedPlayerPanel.hidden = false;
    const environment = live?.environment;
    if (environment) {
      if (pendingEnvironment
          && environment.weatherType === pendingEnvironment.weatherType
          && Math.floor(environment.timeOfDaySeconds / 3600)
             === Math.floor(pendingEnvironment.timeOfDaySeconds / 3600)) {
        pendingEnvironment = null;
        environmentDirty = false;
      }
      if (!environmentDirty && !pendingEnvironment) {
        ui.environmentWeather.value = String(environment.weatherType ?? 15);
        const hourSeconds = Math.floor((environment.timeOfDaySeconds || 0) / 3600) * 3600;
        if ([...ui.environmentTime.options].some(option => Number(option.value) === hourSeconds)) {
          ui.environmentTime.value = String(hourSeconds);
        }
      }
    }
  } else {
    ui.metricTimeLabel.textContent = live?.session?.phase === "countdown" ? "COUNTDOWN" : "SERVER TIME";
    ui.metricTime.textContent = formatDuration(live?.session?.phase === "countdown" ? live.session.countdownMilliseconds : live?.simulatedMilliseconds, true);
    ui.metricTargetLabel.textContent = "LAPS";
    ui.metricTarget.textContent = live?.session?.laps || "—";
    ui.mapTitle.textContent = "TRACK MAP";
    ui.environmentPanel.hidden = true;
    ui.selectedPlayerPanel.hidden = true;
  }

  renderRoster(live?.cars || [], isFps);
  renderSelectedPlayer(live?.cars || []);
  renderButtons();
  renderSecondaryViews(launcher);
  drawMap();
}

function renderButtons() {
  const launcher = status?.launcher;
  const capabilities = {
    "launch-server": launcher?.canLaunchServer,
    "stop-server": launcher?.canStopServer,
    "restart-server": launcher?.canRestartServer,
    "start-session": launcher?.canStartSession,
    "stop-session": launcher?.canStopSession,
    "restart-session": launcher?.canRestartSession,
  };
  document.querySelectorAll(".action").forEach(button => {
    if (button.dataset.action) button.disabled = actionPending || !capabilities[button.dataset.action];
  });
  ui.applyEnvironment.disabled = actionPending
    || !(status?.live?.isFps && status?.live?.serverRunning);
}

function renderSelectedPlayer(cars) {
  const active = cars.filter(car => car.isActive);
  if (!active.some(car => car.sessionId === selectedPlayerId)) {
    selectedPlayerId = active[0]?.sessionId ?? null;
  }
  const previous = String(selectedPlayerId ?? "");
  ui.selectedPlayer.replaceChildren();
  active.forEach(car => {
    const option = document.createElement("option");
    option.value = String(car.sessionId);
    option.textContent = car.name || `Slot ${car.sessionId + 1}`;
    ui.selectedPlayer.append(option);
  });
  ui.selectedPlayer.value = previous;
  const selected = active.find(car => car.sessionId === selectedPlayerId);
  ui.selectedPlayerCaption.textContent = selected?.name || "No active player";
  ui.selectedPlayerType.textContent = selected ? (selected.isBot ? "BOT" : "HUMAN") : "—";
  ui.selectedPlayerHealth.textContent = selected ? `${Math.max(0, selected.health || 0)}%` : "—";
  ui.selectedPlayerScore.textContent = selected ? `${selected.kills || 0} / ${selected.deaths || 0}` : "—";
}

function renderRoster(cars, isFps) {
  const active = cars.filter(car => car.isActive);
  active.sort((a, b) => isFps
    ? (b.kills - a.kills) || (a.deaths - b.deaths) || (a.sessionId - b.sessionId)
    : ((a.racePosition ?? 999) - (b.racePosition ?? 999)) || (a.sessionId - b.sessionId));
  ui.rosterCaption.textContent = `${active.length} active`;
  ui.rosterBody.replaceChildren();
  if (!active.length) {
    const row = ui.rosterBody.insertRow();
    const cell = row.insertCell();
    cell.colSpan = 7;
    cell.className = "table-empty";
    cell.textContent = "No live participants.";
    return;
  }

  active.forEach((car, index) => {
    const row = ui.rosterBody.insertRow();
    addCell(row, String(index + 1));
    const name = addCell(row, car.name || `Slot ${car.sessionId + 1}`);
    if (!car.isBot) name.className = "player-human";
    addCell(row, car.isBot ? "BOT" : car.isConnected ? "HUMAN" : "PLAYER");
    if (isFps) addHealthCell(row, car.health);
    else addCell(row, `${Math.round(car.speedKmh || 0)} km/h`);
    addCell(row, isFps ? String(car.kills || 0) : String(car.lap || 0));
    addCell(row, isFps ? String(car.deaths || 0) : String(car.racePosition ?? "—"));
    const state = isFps ? (car.health > 0 ? "ALIVE" : "RESPAWNING") : car.isDnf ? "DNF" : car.hasFinished ? "FINISHED" : "ACTIVE";
    const stateCell = addCell(row, state);
    stateCell.className = state === "ALIVE" || state === "ACTIVE" ? "state-alive" : state === "RESPAWNING" || state === "DNF" ? "state-dead" : "";
  });
}

function addCell(row, text) {
  const cell = row.insertCell();
  cell.textContent = text;
  return cell;
}

function addHealthCell(row, healthValue) {
  const health = Math.max(0, Math.min(100, Number(healthValue) || 0));
  const cell = row.insertCell();
  const wrap = document.createElement("div");
  wrap.className = "health";
  const label = document.createElement("span");
  label.textContent = `${health}%`;
  const bar = document.createElement("span");
  bar.className = "health-track";
  const fill = document.createElement("span");
  fill.className = "health-fill";
  fill.style.width = `${health}%`;
  bar.append(fill);
  wrap.append(label, bar);
  cell.append(wrap);
}

function renderSecondaryViews(launcher) {
  setText("overview-event", launcher.eventName);
  setText("overview-mode", launcher.mode);
  setText("overview-server", launcher.serverName);
  setText("overview-track", displayTrack(launcher.track, launcher.layout));
  setText("preset-name", launcher.eventName);
  setText("preset-mode", launcher.mode);
  setText("preset-track", displayTrack(launcher.track, launcher.layout));
  setText("settings-address", status.webAddress);
}

function setText(id, value) {
  document.getElementById(id).textContent = value || "—";
}

function renderDisconnected(error) {
  ui.connection.classList.remove("online");
  ui.serverState.textContent = "WEB GUI OFFLINE";
  ui.updatedAt.textContent = `Connection error: ${error.message || error}`;
  ui.launcherStatus.textContent = "The browser cannot reach Race Control.";
  status = null;
  renderButtons();
}

function drawMap() {
  const canvas = ui.canvas;
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  if (!width || !height) return;
  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  canvas.width = Math.round(width * dpr);
  canvas.height = Math.round(height * dpr);
  const ctx = canvas.getContext("2d");
  ctx.scale(dpr, dpr);
  ctx.clearRect(0, 0, width, height);

  const cars = status?.live?.cars?.filter(car => car.isActive) || [];
  if (!track || (!track.hasFpsArena && (!track.points || track.points.length < 2))) {
    ui.mapEmpty.classList.remove("hidden");
    ui.mapCaption.textContent = "No track data";
    return;
  }
  ui.mapEmpty.classList.add("hidden");
  ui.mapCaption.textContent = `${track.track || "Track"}${track.layout ? ` / ${track.layout}` : ""}`;

  const bounds = getBounds(track, cars);
  const padding = 30;
  const scale = Math.min((width - padding * 2) / Math.max(1, bounds.maxX - bounds.minX), (height - padding * 2) / Math.max(1, bounds.maxZ - bounds.minZ));
  const offsetX = (width - (bounds.maxX - bounds.minX) * scale) / 2;
  const offsetY = (height - (bounds.maxZ - bounds.minZ) * scale) / 2;
  const project = (x, z) => [offsetX + (x - bounds.minX) * scale, offsetY + (bounds.maxZ - z) * scale];

  if (track.hasFpsArena) drawArena(ctx, track, project, scale);
  else drawTrack(ctx, track.points, project, scale);
  cars.forEach(car => drawActor(ctx, car, project));
}

function getBounds(map, cars) {
  if (map.hasFpsArena) return { minX: map.minimumX, maxX: map.maximumX, minZ: map.minimumZ, maxZ: map.maximumZ };
  const points = [...(map.points || []), ...cars];
  return points.reduce((b, point) => ({ minX: Math.min(b.minX, point.x), maxX: Math.max(b.maxX, point.x), minZ: Math.min(b.minZ, point.z), maxZ: Math.max(b.maxZ, point.z) }), { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity });
}

function drawArena(ctx, map, project, scale) {
  const size = Math.max(1, map.arenaCellSize * scale);
  ctx.fillStyle = "#202830";
  for (const cell of map.arenaCells || []) {
    const [x, y] = project(cell.x, cell.z);
    ctx.fillRect(x - size / 2, y - size / 2, size + .5, size + .5);
  }
  ctx.strokeStyle = "#46515c";
  ctx.lineWidth = 1;
  ctx.strokeRect(...project(map.minimumX, map.maximumZ), (map.maximumX - map.minimumX) * scale, (map.maximumZ - map.minimumZ) * scale);
}

function drawTrack(ctx, points, project, scale) {
  const averageWidth = points.reduce((sum, p) => sum + (p.leftWidth || 4) + (p.rightWidth || 4), 0) / Math.max(1, points.length);
  ctx.lineJoin = "round";
  ctx.lineCap = "round";
  ctx.beginPath();
  points.forEach((point, index) => {
    const [x, y] = project(point.x, point.z);
    if (index === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
  });
  ctx.closePath();
  ctx.strokeStyle = "#45505a";
  ctx.lineWidth = Math.max(5, averageWidth * scale);
  ctx.stroke();
  ctx.strokeStyle = "#252c33";
  ctx.lineWidth = Math.max(3, averageWidth * scale - 3);
  ctx.stroke();
}

function drawActor(ctx, car, project) {
  const [x, y] = project(car.x, car.z);
  const heading = Number(car.headingRadians) || 0;
  ctx.save();
  ctx.translate(x, y);
  ctx.rotate(heading);
  ctx.beginPath();
  ctx.moveTo(0, -9);
  ctx.lineTo(6, 7);
  ctx.lineTo(-6, 7);
  ctx.closePath();
  ctx.fillStyle = car.isBot ? "#ef4348" : "#48d8e6";
  ctx.fill();
  ctx.strokeStyle = "#f4f6f8";
  ctx.lineWidth = 1;
  ctx.stroke();
  ctx.restore();
  ctx.font = "11px Segoe UI";
  ctx.textAlign = "center";
  ctx.fillStyle = car.isBot ? "#e3e7eb" : "#69e6f1";
  ctx.fillText(car.name || `Slot ${car.sessionId + 1}`, x, y + 21);
}

function displayTrack(trackName, layout) {
  if (!trackName) return "—";
  return layout ? `${trackName} / ${layout}` : trackName;
}

function formatDuration(milliseconds, includeHours = false) {
  if (!Number.isFinite(milliseconds)) return "—";
  const total = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;
  return includeHours || hours > 0
    ? `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`
    : `${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
}

function showToast(message, error = false) {
  window.clearTimeout(toastTimer);
  ui.toast.textContent = message;
  ui.toast.classList.toggle("error", error);
  ui.toast.classList.add("show");
  toastTimer = window.setTimeout(() => ui.toast.classList.remove("show"), 3500);
}

pollStatus();
pollTrack();
window.setInterval(pollStatus, 750);
window.setInterval(pollTrack, 5000);
