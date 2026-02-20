const { send, sendError } = require("./wire");

function parseMessage(raw) {
  try {
    const msg = JSON.parse(raw.toString());
    if (!msg || typeof msg.type !== "string") {
      return null;
    }
    return msg;
  } catch {
    return null;
  }
}

function createMessageRouter({ sessionStore, roomService, matchService }) {
  function broadcastRoomState(room) {
    if (!room) {
      return;
    }

    for (const playerId of room.players.keys()) {
      const targetSession = sessionStore.getByPlayerId(playerId);
      if (!targetSession) {
        continue;
      }
      send(
        targetSession.socket,
        "room_state",
        roomService.buildRoomState(room, targetSession.playerId)
      );
    }
  }

  function leaveIfInRoom(session) {
    const result = roomService.leaveRoom(session);
    if (result.room) {
      broadcastRoomState(result.room);
    }
  }

  function route(session, rawData) {
    const msg = parseMessage(rawData);
    if (!msg) {
      sendError(session.socket, "BAD_JSON", "Malformed JSON message.");
      return;
    }

    const payload = msg.payload || {};
    switch (msg.type) {
      case "create_room": {
        leaveIfInRoom(session);
        const room = roomService.createRoom(session, payload);
        broadcastRoomState(room);
        break;
      }
      case "join_room": {
        leaveIfInRoom(session);
        const result = roomService.joinRoom(session, String(payload.roomId || ""));
        if (result.error) {
          sendError(session.socket, result.error, "Could not join room.");
          return;
        }
        broadcastRoomState(result.room);
        break;
      }
      case "ready": {
        const result = roomService.setReady(session, !!payload.ready);
        if (result.error) {
          sendError(session.socket, result.error, "Could not set ready.");
          return;
        }
        broadcastRoomState(result.room);
        break;
      }
      case "start_match": {
        const roomId = String(payload.roomId || session.roomId || "");
        const room = roomService.getRoom(roomId);
        if (!room) {
          sendError(session.socket, "ROOM_NOT_FOUND", "Room not found.");
          return;
        }
        if (room.hostPlayerId !== session.playerId) {
          sendError(session.socket, "NOT_HOST", "Only host can start match.");
          return;
        }
        const result = matchService.startMatch(roomId);
        if (result.error) {
          sendError(session.socket, result.error, "Could not start match.");
        }
        break;
      }
      case "input": {
        if (!session.roomId) {
          return;
        }
        sessionStore.updateInput(session.playerId, payload);
        break;
      }
      case "leave_room": {
        leaveIfInRoom(session);
        break;
      }
      default:
        sendError(session.socket, "UNKNOWN_TYPE", `Unsupported message type: ${msg.type}`);
        break;
    }
  }

  return { route, broadcastRoomState, leaveIfInRoom };
}

module.exports = { createMessageRouter };
