const linksByPlayer = new Map(); // playerId -> discordUserId
const linksByDiscordUser = new Map(); // discordUserId -> playerId

function link(playerId, discordUserId) {
  linksByPlayer.set(playerId, discordUserId);
  linksByDiscordUser.set(discordUserId, playerId);
}

function getDiscordUserId(playerId) {
  return linksByPlayer.get(playerId);
}

function getPlayerId(discordUserId) {
  return linksByDiscordUser.get(discordUserId);
}

module.exports = { link, getDiscordUserId, getPlayerId };
