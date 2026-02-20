const { id } = require("./id");
const { MIN_PLAYERS } = require("./roomService");

const ARENA_MIN = { x: -8, y: -4.5 };
const ARENA_MAX = { x: 8, y: 4.5 };
const MOVE_SPEED = 5.0;
const BOMB_HOLDER_SPEED_MULTIPLIER = 1.1;
const BOMB_TIMER_SECONDS = 10.0;
const PASS_COOLDOWN_SECONDS = 0.5;
const PASS_DISTANCE = 1.15;

function normalize2(x, y) {
  const mag = Math.sqrt(x * x + y * y);
  if (mag <= 0.00001) {
    return { x: 0, y: 0 };
  }
  return { x: x / mag, y: y / mag };
}

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function makeSpawnPoints(count) {
  const centerX = 0;
  const centerY = 0;
  const radius = 3.2;
  const points = [];
  for (let i = 0; i < count; i += 1) {
    const t = (Math.PI * 2 * i) / count;
    points.push({ x: centerX + Math.cos(t) * radius, y: centerY + Math.sin(t) * radius });
  }
  return points;
}

function createMatchState(room, nowMs) {
  const humans = [...room.players.keys()].map((playerId, index) => ({
    id: playerId,
    isBot: false,
    appearance: index,
    x: 0,
    y: 0,
    vx: 0,
    vy: 0,
    alive: true,
    hasBomb: false,
    input: { x: 0, y: 0 }
  }));

  const botsToAdd = room.withBots ? Math.max(0, MIN_PLAYERS - humans.length) : 0;
  const bots = [];
  for (let i = 0; i < botsToAdd; i += 1) {
    bots.push({
      id: id("bot"),
      isBot: true,
      appearance: humans.length + i,
      x: 0,
      y: 0,
      vx: 0,
      vy: 0,
      alive: true,
      hasBomb: false,
      input: { x: 0, y: 0 }
    });
  }

  const players = [...humans, ...bots];
  const points = makeSpawnPoints(Math.max(players.length, MIN_PLAYERS));
  players.forEach((p, i) => {
    p.x = points[i].x;
    p.y = points[i].y;
  });

  const alive = players.filter((p) => p.alive);
  const randomHolder = alive[Math.floor(Math.random() * alive.length)] || null;
  if (randomHolder) {
    randomHolder.hasBomb = true;
  }

  return {
    roomId: room.roomId,
    tick: 0,
    phase: "running",
    winnerId: "",
    startedServerTimeMs: nowMs,
    endedServerTimeMs: 0,
    players,
    bombHolderId: randomHolder ? randomHolder.id : "",
    bombTimeLeft: BOMB_TIMER_SECONDS,
    nextPassAllowedAtMs: nowMs + PASS_COOLDOWN_SECONDS * 1000
  };
}

function toSnapshot(state, nowMs) {
  return {
    roomId: state.roomId,
    tick: state.tick,
    serverTimeMs: nowMs,
    phase: state.phase,
    winnerId: state.winnerId,
    bomb: {
      holderId: state.bombHolderId,
      timeLeft: state.bombTimeLeft
    },
    players: state.players.map((p) => ({
      id: p.id,
      x: p.x,
      y: p.y,
      vx: p.vx,
      vy: p.vy,
      alive: p.alive,
      hasBomb: p.hasBomb,
      isBot: p.isBot,
      appearance: p.appearance
    }))
  };
}

function findAlivePlayers(state) {
  return state.players.filter((p) => p.alive);
}

function getHolder(state) {
  return state.players.find((p) => p.id === state.bombHolderId) || null;
}

function getNearestAliveTarget(state, fromPlayer) {
  let best = null;
  let bestDist = Number.MAX_VALUE;
  for (const p of state.players) {
    if (!p.alive || p.id === fromPlayer.id) {
      continue;
    }
    const dx = p.x - fromPlayer.x;
    const dy = p.y - fromPlayer.y;
    const d2 = dx * dx + dy * dy;
    if (d2 < bestDist) {
      bestDist = d2;
      best = p;
    }
  }
  return { target: best, distance: Math.sqrt(bestDist) };
}

function botInputFor(player, state) {
  const holder = getHolder(state);
  if (!holder || !holder.alive) {
    return { x: 0, y: 0 };
  }

  if (player.hasBomb) {
    const { target } = getNearestAliveTarget(state, player);
    if (!target) {
      return { x: 0, y: 0 };
    }
    return normalize2(target.x - player.x, target.y - player.y);
  }

  return normalize2(player.x - holder.x, player.y - holder.y);
}

function transferBomb(state, fromPlayer, toPlayer, nowMs) {
  fromPlayer.hasBomb = false;
  toPlayer.hasBomb = true;
  state.bombHolderId = toPlayer.id;
  state.nextPassAllowedAtMs = nowMs + PASS_COOLDOWN_SECONDS * 1000;
}

function assignBombToRandomAlive(state, nowMs) {
  const alive = findAlivePlayers(state);
  if (alive.length === 0) {
    state.bombHolderId = "";
    return;
  }

  const holder = alive[Math.floor(Math.random() * alive.length)];
  for (const p of state.players) {
    p.hasBomb = p.id === holder.id;
  }
  state.bombHolderId = holder.id;
  state.bombTimeLeft = BOMB_TIMER_SECONDS;
  state.nextPassAllowedAtMs = nowMs + PASS_COOLDOWN_SECONDS * 1000;
}

function tick(state, dtSeconds, nowMs, getHumanInput) {
  if (state.phase !== "running") {
    return;
  }

  const holder = getHolder(state);
  for (const p of state.players) {
    if (!p.alive) {
      p.vx = 0;
      p.vy = 0;
      continue;
    }

    let inVec = p.isBot ? botInputFor(p, state) : getHumanInput(p.id);
    inVec = normalize2(inVec.x || 0, inVec.y || 0);
    const speed = MOVE_SPEED * (p.hasBomb ? BOMB_HOLDER_SPEED_MULTIPLIER : 1);
    p.vx = inVec.x * speed;
    p.vy = inVec.y * speed;
    p.x = clamp(p.x + p.vx * dtSeconds, ARENA_MIN.x, ARENA_MAX.x);
    p.y = clamp(p.y + p.vy * dtSeconds, ARENA_MIN.y, ARENA_MAX.y);
  }

  if (holder && holder.alive && nowMs >= state.nextPassAllowedAtMs) {
    const nearest = getNearestAliveTarget(state, holder);
    if (nearest.target && nearest.distance <= PASS_DISTANCE) {
      transferBomb(state, holder, nearest.target, nowMs);
    }
  }

  state.bombTimeLeft = Math.max(0, state.bombTimeLeft - dtSeconds);
  if (state.bombTimeLeft <= 0) {
    const explodingHolder = getHolder(state);
    if (explodingHolder && explodingHolder.alive) {
      explodingHolder.alive = false;
      explodingHolder.hasBomb = false;
    }

    const alive = findAlivePlayers(state);
    if (alive.length <= 1) {
      state.phase = "ended";
      state.endedServerTimeMs = nowMs;
      state.winnerId = alive.length === 1 ? alive[0].id : "";
      state.bombHolderId = "";
      state.bombTimeLeft = 0;
    } else {
      assignBombToRandomAlive(state, nowMs);
    }
  }

  state.tick += 1;
}

function replaceHumanWithBot(state, humanPlayerId) {
  const player = state.players.find((p) => p.id === humanPlayerId);
  if (!player) {
    return;
  }

  player.id = id("bot");
  player.isBot = true;
}

module.exports = {
  createMatchState,
  toSnapshot,
  tick,
  replaceHumanWithBot
};
