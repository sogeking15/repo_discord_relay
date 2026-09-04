const { WebSocketServer } = require("ws");
const { randomUUID } = require("crypto");

const connections = new Map(); // playerId -> ws
const queues = new Map(); // playerId -> pending message[]

function send(ws, data) {
  ws.send(JSON.stringify(data));
}

function flushQueue(playerId, ws) {
  const pending = queues.get(playerId);
  if (!pending) return;
  for (const msg of pending) {
    send(ws, { type: "message.deliver", ...msg });
  }
}

function deliverToPlayer(toPlayerId, fromPlayerId, kind, payload) {
  const msg = {
    messageId: randomUUID(),
    fromPlayerId,
    kind,
    payload,
    ts: Date.now(),
  };

  if (!queues.has(toPlayerId)) queues.set(toPlayerId, []);
  queues.get(toPlayerId).push(msg);

  const target = connections.get(toPlayerId);
  if (target && target.readyState === target.OPEN) {
    send(target, { type: "message.deliver", ...msg });
  }
  return msg;
}

function attachRelay(httpServer) {
  const wss = new WebSocketServer({ server: httpServer, path: "/relay" });

  wss.on("connection", (ws) => {
    ws.on("message", (raw) => {
      let data;
      try {
        data = JSON.parse(raw);
      } catch {
        return send(ws, { type: "error", message: "invalid json" });
      }

      switch (data.type) {
        case "identify": {
          if (!data.playerId) return send(ws, { type: "error", message: "playerId required" });
          ws.playerId = data.playerId;
          connections.set(data.playerId, ws);
          send(ws, { type: "identified", playerId: data.playerId });
          flushQueue(data.playerId, ws);
          break;
        }

        case "message.send": {
          if (!ws.playerId) return send(ws, { type: "error", message: "identify first" });
          if (!data.toPlayerId) return send(ws, { type: "error", message: "toPlayerId required" });
          deliverToPlayer(data.toPlayerId, ws.playerId, data.kind, data.payload);
          break;
        }

        case "message.ack": {
          if (!ws.playerId) return;
          const pending = queues.get(ws.playerId);
          if (!pending) return;
          queues.set(
            ws.playerId,
            pending.filter((m) => m.messageId !== data.messageId)
          );
          break;
        }

        default:
          send(ws, { type: "error", message: `unknown type: ${data.type}` });
      }
    });

    ws.on("close", () => {
      if (ws.playerId && connections.get(ws.playerId) === ws) {
        connections.delete(ws.playerId);
      }
    });
  });

  return wss;
}

module.exports = { attachRelay, deliverToPlayer };
