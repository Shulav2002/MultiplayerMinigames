const WebSocket = require("ws");
const { SessionStore } = require("./sessionStore");
const { RoomService } = require("./roomService");
const { MatchService } = require("./matchService");
const { createMessageRouter } = require("./messageRouter");
const { send } = require("./wire");

const PORT = Number(process.env.PORT || 8080);

const sessionStore = new SessionStore();
const roomService = new RoomService();

function broadcastRoom(roomId, type, payload) {
  const room = roomService.getRoom(roomId);
  if (!room) {
    return;
  }

  for (const playerId of room.players.keys()) {
    const session = sessionStore.getByPlayerId(playerId);
    if (!session) {
      continue;
    }
    send(session.socket, type, payload);
  }
}

function sendToPlayer(playerId, type, payload) {
  const session = sessionStore.getByPlayerId(playerId);
  if (!session) {
    return;
  }
  send(session.socket, type, payload);
}

const matchService = new MatchService({
  roomService,
  sessionStore,
  broadcastRoom,
  sendToPlayer
});

const router = createMessageRouter({
  sessionStore,
  roomService,
  matchService
});

const wss = new WebSocket.Server({ port: PORT });

wss.on("connection", (socket) => {
  const session = sessionStore.create(socket);
  console.log(`[ws] connected: ${session.playerId}`);

  socket.on("message", (raw) => {
    const current = sessionStore.getBySocket(socket);
    if (!current) {
      return;
    }
    router.route(current, raw);
  });

  socket.on("close", () => {
    const removed = sessionStore.removeBySocket(socket);
    if (!removed) {
      return;
    }

    console.log(`[ws] disconnected: ${removed.playerId}`);
    matchService.onPlayerDisconnected(removed);
    const leaveResult = roomService.leaveRoom(removed);
    if (leaveResult.room) {
      router.broadcastRoomState(leaveResult.room);
    }
  });
});

process.on("SIGINT", () => {
  console.log("Stopping server...");
  matchService.stopAll();
  wss.close(() => process.exit(0));
});

console.log(`HotDynamite backend listening on ws://localhost:${PORT}`);
