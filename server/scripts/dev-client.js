const WebSocket = require("ws");

const [, , playerId, toPlayerId, message] = process.argv;

if (!playerId) {
  console.error("usage: node scripts/dev-client.js <playerId> [toPlayerId] [message]");
  process.exit(1);
}

const url = process.env.RELAY_URL || "ws://localhost:8080/relay";
const ws = new WebSocket(url);

ws.on("open", () => {
  ws.send(JSON.stringify({ type: "identify", playerId }));
});

ws.on("message", (raw) => {
  const data = JSON.parse(raw);
  console.log("<-", data);

  if (data.type === "identified" && toPlayerId && message) {
    ws.send(JSON.stringify({ type: "message.send", toPlayerId, kind: "text", payload: { text: message } }));
  }

  if (data.type === "message.deliver") {
    ws.send(JSON.stringify({ type: "message.ack", messageId: data.messageId }));
  }
});

ws.on("close", () => console.log("connection closed"));
