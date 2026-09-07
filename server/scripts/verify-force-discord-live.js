const WebSocket = require("ws");

const ws = new WebSocket("ws://localhost:8080/relay");

ws.on("open", () => {
  ws.send(JSON.stringify({ type: "identify", playerId: "player-1" }));
});

ws.on("message", (raw) => {
  const data = JSON.parse(raw);
  console.log("<-", data);

  if (data.type === "identified") {
    console.log("player-1 is now online. Sending to self with forceDiscord: true...");
    ws.send(
      JSON.stringify({
        type: "message.send",
        toPlayerId: "player-1",
        kind: "chat",
        payload: { text: "forced DM while online test" },
        forceDiscord: true,
      })
    );
  }

  if (data.type === "message.deliver") {
    ws.send(JSON.stringify({ type: "message.ack", messageId: data.messageId }));
  }
});

setTimeout(() => {
  ws.close();
  process.exit(0);
}, 3000);
