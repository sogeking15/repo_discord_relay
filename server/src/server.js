require("dotenv").config();
const express = require("express");
const http = require("http");
const { attachRelay } = require("./relay");
const { createDiscordBridge } = require("./discordBridge");

const app = express();
const port = process.env.PORT || 8080;

app.get("/health", (_req, res) => {
  res.json({ status: "ok" });
});

app.post("/shutdown", (req, res) => {
  const ip = req.socket.remoteAddress;
  if (ip !== "127.0.0.1" && ip !== "::1" && ip !== "::ffff:127.0.0.1") {
    return res.status(403).json({ error: "forbidden" });
  }
  res.json({ status: "shutting down" });
  setTimeout(() => process.exit(0), 100);
});

const server = http.createServer(app);
attachRelay(server);

server.listen(port, "0.0.0.0", () => {
  console.log(`relay server listening on :${port}`);
});

if (process.env.DISCORD_BOT_TOKEN) {
  const bridge = createDiscordBridge(process.env.DISCORD_BOT_TOKEN);
  bridge.ready
    .then((user) => console.log(`discord bot logged in as ${user.tag}`))
    .catch((err) => console.error("discord bot failed to log in:", err.message));
} else {
  console.log("DISCORD_BOT_TOKEN not set, skipping Discord bot startup");
}
