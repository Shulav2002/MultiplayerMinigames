const { id } = require("./id");

class SessionStore {
  constructor() {
    this.bySocket = new Map();
    this.byPlayerId = new Map();
  }

  create(socket) {
    const playerId = id("p");
    const session = {
      playerId,
      socket,
      roomId: null,
      ready: false,
      input: { seq: 0, moveX: 0, moveY: 0, clientTimeMs: 0 }
    };

    this.bySocket.set(socket, session);
    this.byPlayerId.set(playerId, session);
    return session;
  }

  getBySocket(socket) {
    return this.bySocket.get(socket) || null;
  }

  getByPlayerId(playerId) {
    return this.byPlayerId.get(playerId) || null;
  }

  updateInput(playerId, payload) {
    const session = this.byPlayerId.get(playerId);
    if (!session || !payload) {
      return;
    }

    const seq = Number(payload.seq || 0);
    if (seq < session.input.seq) {
      return;
    }

    session.input = {
      seq,
      moveX: Number(payload.moveX || 0),
      moveY: Number(payload.moveY || 0),
      clientTimeMs: Number(payload.clientTimeMs || 0)
    };
  }

  removeBySocket(socket) {
    const session = this.bySocket.get(socket);
    if (!session) {
      return null;
    }

    this.bySocket.delete(socket);
    this.byPlayerId.delete(session.playerId);
    return session;
  }
}

module.exports = { SessionStore };
