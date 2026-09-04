const { Client, GatewayIntentBits } = require("discord.js");

function createDiscordBridge(token) {
  const client = new Client({ intents: [GatewayIntentBits.Guilds] });

  const ready = new Promise((resolve, reject) => {
    client.once("clientReady", () => resolve(client.user));
    client.once("error", reject);
  });

  client.once("clientReady", () => {
    console.log(`bot is a member of ${client.guilds.cache.size} guild(s)`);
  });

  client.login(token);

  async function sendDM(discordUserId, message) {
    const user = await client.users.fetch(discordUserId);
    const channel = await user.createDM();
    await channel.send(message);
  }

  return { client, ready, sendDM };
}

module.exports = { createDiscordBridge };
