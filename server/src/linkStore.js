const linksByPlayer = new Map(); // playerId -> { discordUserId, username }
const linksByDiscordUser = new Map(); // discordUserId -> playerId

function link(playerId, discordUserId, username) {
  linksByPlayer.set(playerId, { discordUserId, username });
  linksByDiscordUser.set(discordUserId, playerId);
}

function getDiscordUserId(playerId) {
  return linksByPlayer.get(playerId)?.discordUserId;
}

function getPlayerId(discordUserId) {
  return linksByDiscordUser.get(discordUserId);
}

function listLinkedPlayers() {
  return Array.from(linksByPlayer.entries()).map(([playerId, info]) => ({
    playerId,
    discordUserId: info.discordUserId,
    username: info.username,
  }));
}

module.exports = { link, getDiscordUserId, getPlayerId, listLinkedPlayers };
