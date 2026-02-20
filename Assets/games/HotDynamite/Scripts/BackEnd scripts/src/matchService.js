const { createMatchState, toSnapshot, tick, replaceHumanWithBot } = require("./hotDynamiteEngine");

class MatchService {
  constructor({ roomService, sessionStore, broadcastRoom, sendToPlayer }) {
    this.roomService = roomService;
    this.sessionStore = sessionStore;
    this.broadcastRoom = broadcastRoom;
    this.sendToPlayer = sendToPlayer;
    this.matches = new Map();
    this.tickRateMs = 50;
  }

  startMatch(roomId) {
    const room = this.roomService.getRoom(roomId);
    if (!room) {
      return { error: "ROOM_NOT_FOUND" };
    }

    if (this.matches.has(roomId)) {
      return { error: "MATCH_ALREADY_RUNNING" };
    }

    const now = Date.now();
    const state = createMatchState(room, now);
    const context = {
      roomId,
      state,
      lastTickMs: now,
      interval: null
    };

    context.interval = setInterval(() => {
      const t = Date.now();
      const dt = Math.max(0.001, (t - context.lastTickMs) / 1000);
      context.lastTickMs = t;

      tick(state, dt, t, (playerId) => {
        const session = this.sessionStore.getByPlayerId(playerId);
        if (!session) {
          return { x: 0, y: 0 };
        }

        return {
          x: Number(session.input.moveX || 0),
          y: Number(session.input.moveY || 0)
        };
      });

      this.broadcastRoom(roomId, "state_snapshot", toSnapshot(state, t));

      if (state.phase === "ended") {
        this.broadcastRoom(roomId, "match_ended", {
          roomId,
          winnerId: state.winnerId,
          endedServerTimeMs: state.endedServerTimeMs || Date.now()
        });
        this.stopMatch(roomId);
      }
    }, this.tickRateMs);

    this.matches.set(roomId, context);
    this.broadcastRoom(roomId, "match_started", {
      roomId,
      startServerTimeMs: now
    });

    return { ok: true };
  }

  stopMatch(roomId) {
    const context = this.matches.get(roomId);
    if (!context) {
      return;
    }

    clearInterval(context.interval);
    this.matches.delete(roomId);
  }

  onPlayerDisconnected(session) {
    if (!session || !session.roomId) {
      return;
    }

    const context = this.matches.get(session.roomId);
    if (!context) {
      return;
    }

    replaceHumanWithBot(context.state, session.playerId);
  }

  stopAll() {
    for (const roomId of this.matches.keys()) {
      this.stopMatch(roomId);
    }
  }
}

module.exports = { MatchService };
