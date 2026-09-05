const { randomUUID } = require("crypto");
const linkStore = require("./linkStore");

const STATE_TTL_MS = 10 * 60 * 1000;
const pendingStates = new Map(); // state -> { playerId, expiresAt }

function cleanupExpiredStates() {
  const now = Date.now();
  for (const [state, data] of pendingStates) {
    if (now > data.expiresAt) pendingStates.delete(state);
  }
}

function generateOAuthUrl(playerId) {
  const state = randomUUID();
  pendingStates.set(state, { playerId, expiresAt: Date.now() + STATE_TTL_MS });
  cleanupExpiredStates();

  const params = new URLSearchParams({
    client_id: process.env.DISCORD_CLIENT_ID,
    redirect_uri: process.env.DISCORD_REDIRECT_URI,
    response_type: "code",
    scope: "identify guilds.join",
    state,
  });

  return `https://discord.com/api/oauth2/authorize?${params.toString()}`;
}

// Adds the linked user to our guild using their guilds.join-scoped access
// token, so the bot always shares a guild with them before it ever tries to
// DM - Discord blocks DMs otherwise (error 50007). No-op (204) if already a
// member.
async function joinGuild(accessToken, discordId) {
  const guildId = process.env.DISCORD_GUILD_ID;
  if (!guildId) {
    console.warn("[discordOAuth] DISCORD_GUILD_ID not set - skipping auto-join to server");
    return;
  }

  const response = await fetch(`https://discord.com/api/v10/guilds/${guildId}/members/${discordId}`, {
    method: "PUT",
    headers: {
      Authorization: `Bot ${process.env.DISCORD_BOT_TOKEN}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ access_token: accessToken }),
  });

  if (!response.ok) {
    throw new Error(`failed to add user to guild: ${response.status} ${await response.text()}`);
  }
}

async function handleOAuthCallback(code, state) {
  const pending = pendingStates.get(state);
  if (!pending) throw new Error("invalid state parameter");
  pendingStates.delete(state);
  if (Date.now() > pending.expiresAt) throw new Error("state expired");

  const tokenResponse = await fetch("https://discord.com/api/v10/oauth2/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      client_id: process.env.DISCORD_CLIENT_ID,
      client_secret: process.env.DISCORD_CLIENT_SECRET,
      code,
      grant_type: "authorization_code",
      redirect_uri: process.env.DISCORD_REDIRECT_URI,
    }),
  });
  if (!tokenResponse.ok) throw new Error(`token exchange failed: ${tokenResponse.statusText}`);
  const tokenData = await tokenResponse.json();

  const userResponse = await fetch("https://discord.com/api/v10/users/@me", {
    headers: { Authorization: `Bearer ${tokenData.access_token}` },
  });
  if (!userResponse.ok) throw new Error(`failed to fetch user info: ${userResponse.statusText}`);
  const userData = await userResponse.json();

  try {
    await joinGuild(tokenData.access_token, userData.id);
  } catch (err) {
    console.warn(`[discordOAuth] could not add ${userData.username} to guild:`, err.message);
  }

  linkStore.link(pending.playerId, userData.id, userData.username);

  return { playerId: pending.playerId, discordUserId: userData.id, username: userData.username };
}

module.exports = { generateOAuthUrl, handleOAuthCallback };
