const { Client, GatewayIntentBits } = require("discord.js");

function createDiscordBridge(token) {
  const client = new Client({ intents: [GatewayIntentBits.Guilds] });

  const ready = new Promise((resolve, reject) => {
    client.once("clientReady", () => resolve(client.user));
    client.once("error", reject);
  });

  client.login(token);

  return { client, ready };
}

module.exports = { createDiscordBridge };
