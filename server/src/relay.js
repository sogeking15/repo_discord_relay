const { WebSocketServer } = require("ws");
const { randomUUID } = require("crypto");
const linkStore = require("./linkStore");

const connections = new Map(); // playerId -> ws
const queues = new Map(); // playerId -> pending message[]

let discordBridge = null;
function setDiscordBridge(bridge) {
  discordBridge = bridge;
}

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

// link.complete is protocol bookkeeping (the OAuth callback already sends its
// own welcome DM) - notifying about it here too would be a redundant, useless
// DM ("New message from system, open the game to see it").
async function notifyViaDiscordIfOffline(toPlayerId, msg) {
  if (!discordBridge || msg.kind === "link.complete") return;

  const discordUserId = linkStore.getDiscordUserId(toPlayerId);
  if (!discordUserId) return;

  const text = (msg.payload && msg.payload.text) || `New message from ${msg.fromPlayerId} - open the game to see it.`;

  try {
    await discordBridge.sendDM(discordUserId, text);
    console.log(`[relay] notified ${toPlayerId} via DM (offline)`);
  } catch (err) {
    console.warn(`[relay] could not DM ${toPlayerId} (${discordUserId}):`, err.message);
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
  } else {
    notifyViaDiscordIfOffline(toPlayerId, msg);
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

module.exports = { attachRelay, deliverToPlayer, setDiscordBridge };
