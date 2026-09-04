require("dotenv").config();
const express = require("express");
const http = require("http");
const { attachRelay, deliverToPlayer } = require("./relay");
const { createDiscordBridge } = require("./discordBridge");
const discordOAuth = require("./discordOAuth");

const app = express();
const port = process.env.PORT || 8080;

let bridge = null;

app.use(express.json());

app.get("/health", (_req, res) => {
  res.json({ status: "ok" });
});

app.get("/link/start", (req, res) => {
  const { playerId } = req.query;
  if (!playerId) return res.status(400).json({ error: "playerId required" });
  const authUrl = discordOAuth.generateOAuthUrl(playerId);
  res.json({ authUrl });
});

app.get("/link/callback", async (req, res) => {
  const { code, state } = req.query;
  if (!code || !state) return res.status(400).send("Missing code or state parameter.");

  try {
    const { playerId, discordUserId, username } = await discordOAuth.handleOAuthCallback(code, state);

    deliverToPlayer(playerId, "system", "link.complete", { discordUserId });

    if (bridge) {
      try {
        await bridge.sendDM(discordUserId, "You're linked! You'll receive messages here from now on.");
      } catch (err) {
        console.warn(`[link] could not send welcome DM to ${username}:`, err.message);
      }
    }

    res.send("<h1>Linked!</h1><p>You can close this tab and return to the game.</p>");
  } catch (err) {
    console.error("[link] OAuth callback error:", err.message);
    res.status(400).send(`<h1>Link failed</h1><p>${err.message}</p>`);
  }
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
  bridge = createDiscordBridge(process.env.DISCORD_BOT_TOKEN);
  bridge.ready
    .then((user) => console.log(`discord bot logged in as ${user.tag}`))
    .catch((err) => console.error("discord bot failed to log in:", err.message));
} else {
  console.log("DISCORD_BOT_TOKEN not set, skipping Discord bot startup");
}
