function send(socket, type, payload) {
  if (!socket || socket.readyState !== socket.OPEN) {
    return;
  }

  socket.send(JSON.stringify({ type, payload }));
}

function sendError(socket, code, message) {
  send(socket, "error", { code, message });
}

module.exports = { send, sendError };
