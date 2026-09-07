const http = require("http");
const WebSocket = require("ws");
const { attachRelay, deliverToPlayer, setDiscordBridge } = require("../src/relay");
const linkStore = require("../src/linkStore");

const calls = [];
setDiscordBridge({
  sendDM: async (discordUserId, text) => {
    calls.push({ discordUserId, text });
  },
});

linkStore.link("target-player", "fake-discord-id", "fakeuser");

const server = http.createServer();
attachRelay(server);

server.listen(0, async () => {
  const port = server.address().port;
  const ws = new WebSocket(`ws://localhost:${port}/relay`);

  ws.on("open", () => {
    ws.send(JSON.stringify({ type: "identify", playerId: "target-player" }));
  });

  ws.on("message", async (raw) => {
    const data = JSON.parse(raw);
    if (data.type !== "identified") return;

    // target-player is now genuinely online. Without forceDiscord, sending
    // to them should NOT call the bridge.
    deliverToPlayer("target-player", "sender", "chat", { text: "no force" }, { forceDiscord: false });
    await sleep(200);
    assert(calls.length === 0, `expected no DM without forceDiscord, got ${calls.length}`);

    // With forceDiscord, it should call the bridge even though they're online.
    deliverToPlayer("target-player", "sender", "chat", { text: "forced" }, { forceDiscord: true });
    await sleep(200);
    assert(calls.length === 1, `expected 1 DM with forceDiscord, got ${calls.length}`);
    assert(calls[0].discordUserId === "fake-discord-id", "wrong discordUserId");
    assert(calls[0].text === "forced", "wrong text");

    console.log("PASS: forceDiscord notifies even when online, normal send does not");
    ws.close();
    server.close();
    process.exit(0);
  });
});

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function assert(cond, msg) {
  if (!cond) {
    console.error("FAIL:", msg);
    process.exit(1);
  }
}
