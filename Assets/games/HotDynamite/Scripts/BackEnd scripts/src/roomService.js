const { id } = require("./id");

const MIN_PLAYERS = 4;

class RoomService {
  constructor() {
    this.rooms = new Map();
  }

  createRoom(hostSession, payload = {}) {
    const roomId = id("room");
    const room = {
      roomId,
      roomName: String(payload.roomName || "HotDynamite Room"),
      hostPlayerId: hostSession.playerId,
      maxPlayers: Math.max(MIN_PLAYERS, Number(payload.maxPlayers || MIN_PLAYERS)),
      withBots: payload.withBots !== false,
      players: new Map()
    };

    room.players.set(hostSession.playerId, { playerId: hostSession.playerId, ready: true });
    hostSession.roomId = roomId;
    hostSession.ready = true;

    this.rooms.set(roomId, room);
    return room;
  }

  joinRoom(session, roomId) {
    const room = this.rooms.get(roomId);
    if (!room) {
      return { error: "ROOM_NOT_FOUND" };
    }

    if (room.players.has(session.playerId)) {
      session.roomId = roomId;
      return { room };
    }

    if (room.players.size >= room.maxPlayers) {
      return { error: "ROOM_FULL" };
    }

    room.players.set(session.playerId, { playerId: session.playerId, ready: false });
    session.roomId = roomId;
    session.ready = false;
    return { room };
  }

  leaveRoom(session) {
    if (!session || !session.roomId) {
      return { room: null, deleted: false };
    }

    const room = this.rooms.get(session.roomId);
    session.roomId = null;
    session.ready = false;

    if (!room) {
      return { room: null, deleted: false };
    }

    room.players.delete(session.playerId);
    if (room.players.size === 0) {
      this.rooms.delete(room.roomId);
      return { room: null, deleted: true };
    }

    if (room.hostPlayerId === session.playerId) {
      room.hostPlayerId = [...room.players.keys()][0];
    }

    return { room, deleted: false };
  }

  setReady(session, ready) {
    if (!session || !session.roomId) {
      return { error: "NOT_IN_ROOM" };
    }

    const room = this.rooms.get(session.roomId);
    if (!room) {
      return { error: "ROOM_NOT_FOUND" };
    }

    const entry = room.players.get(session.playerId);
    if (!entry) {
      return { error: "PLAYER_NOT_IN_ROOM" };
    }

    entry.ready = !!ready;
    session.ready = !!ready;
    return { room };
  }

  getRoom(roomId) {
    return this.rooms.get(roomId) || null;
  }

  buildRoomState(room, youId) {
    const humanCount = room.players.size;
    const botCount = room.withBots ? Math.max(0, MIN_PLAYERS - humanCount) : 0;
    return {
      roomId: room.roomId,
      youId,
      humanCount,
      botCount,
      minPlayers: MIN_PLAYERS,
      players: [...room.players.keys()]
    };
  }
}

module.exports = { RoomService, MIN_PLAYERS };
