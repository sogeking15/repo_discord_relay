# Discord Relay Kit

Async Discord messaging for Unity games: send a message to a player and get
notified when they reply — even if their game is closed. A small Node relay
server bridges your game's WebSocket connections to Discord DMs, and a Unity
package gives you a small C# API (`Connect`, `Send`, `OnMessageReceived`,
`RequestLink`, `OnLinked`, `GetLinkedPlayers`) to use it.

This repo has two parts:
- `server/` — the relay server (Node.js)
- `unity-package/` — the Unity package (UPM), with two samples under
  `Samples~/`

## Requirements

- Unity 6000.0 or newer
- [Node.js](https://nodejs.org/) 18+
- A Discord account (to create the bot application)

## 1. Install the Unity package

In your Unity project: **Window → Package Manager → + → Add package from git URL**

```
https://github.com/sogeking15/repo_discord_relay.git?path=/unity-package
```

## 2. Create your Discord bot

1. Go to [discord.com/developers/applications](https://discord.com/developers/applications) → **New Application** → give it any name.
2. Click the **Bot** tab (left sidebar) → **Reset Token** → copy it somewhere temporary. You'll paste this into Unity in step 6, not here.
3. Leave all three **Privileged Gateway Intents** off — this kit doesn't need them.

## 3. Create a test Discord server

An Application (what you just made) and a Discord *server* are different things — creating the bot doesn't create a server for you.

In Discord itself (not the Developer Portal): click the **+** at the bottom of your server list → **Create My Own** → name it anything (e.g. "My Game Test").

## 4. Invite the bot to that server

Still in the Developer Portal, on your application:

1. **OAuth2** tab → **URL Generator**
2. Scopes: check `bot` and `applications.commands`
3. Bot Permissions: check `Send Messages`, `Embed Links`, and `Create Instant Invite` (the last one is required for the auto-link flow in step 5 — without it, players can be OAuth-authorized but never actually added to the server)
4. Copy the generated URL at the bottom, open it, and pick the server you made in step 3

## 5. Set up OAuth2 (for linking player accounts)

This is what lets a player click one link, authorize, and be automatically linked and added to your server — no invite link, no manual join.

1. Developer Portal → your application → **OAuth2** tab
2. Copy the **Client ID**
3. Click **Reset Secret** and copy the **Client Secret**
4. Under **Redirects**, click **Add Redirect**, enter exactly:
   ```
   http://localhost:8080/link/callback
   ```
   (change `8080` if you're using a different port) → **Save Changes**

You'll also need your test server's **Guild ID**: in Discord, enable **Developer Mode** (User Settings → Advanced), then right-click your server's icon → **Copy Server ID**.

## 6. Configure the relay server from Unity

In your Unity project: **Tools → Discord Relay Kit → Setup**

| Field | Value |
|---|---|
| Node project path | Full path to this repo's `server/` folder (e.g. `.../repo_discord_relay/server`) |
| Bot token | From step 2 |
| Port | `8080` (default is fine) |
| Client ID | From step 5 |
| Client secret | From step 5 |
| Redirect URI | Pre-filled to match the port; must match step 5 exactly |
| Guild ID | From step 5 |

Click **Save to server/.env**. Everything here is written straight to a local
`.env` file next to the server — nothing is sent anywhere else, and `.env` is
already git-ignored.

## 7. Start the server

Click **Start** in the same window. The status indicator should turn green
("Running") within a second or two. The **LAN IP** field (with a **Copy**
button) is there for testing from a second device on the same Wi-Fi — point
that device's Base URL at it instead of `localhost`.

## 8. Try it: the Chat UI sample

**Window → Package Manager → In Project → Discord Relay Kit → Samples →
Chat UI → Import**

Open the imported `ChatUIDemo.unity` scene and press Play. You'll see:

- **Link Discord Account** — click it, authorize in the browser tab that
  opens, and you'll land on a plain "Linked!" page
- **Refresh** — click it (or it happens automatically after linking) to
  populate the **Linked players** list
- Select yourself from that list, type a message, click **Send**

That message will show up in your Discord DMs from the bot. (The demo
deliberately always sends via Discord — see "How delivery works" below for
why that's a demo-specific choice, not the default.)

## 9. Use it in your own game

```csharp
using DiscordRelayKit;

void Start()
{
    DiscordRelay.Connect("localhost:8080", myPlayerId);
    DiscordRelay.OnMessageReceived += msg =>
    {
        // msg.Kind is whatever string you chose when sending ("chat", "cardPlayed", ...)
        // msg.Payload is a JSON string - deserialize it however your game wants
    };
}

void RequestDiscordLink()
{
    DiscordRelay.RequestLink(
        authUrl => Application.OpenURL(authUrl),
        error => Debug.LogError(error)
    );
    // DiscordRelay.OnLinked fires with the player's Discord user ID once they finish
}

void SendSomething()
{
    DiscordRelay.Send(otherPlayerId, "chat", "{\"text\":\"hi\"}");
}
```

See `Samples~/BasicUsage/ChatDemo.cs` for a minimal end-to-end example with
`[ContextMenu]` actions you can trigger in the Inspector during Play Mode.

## How delivery works

- **Recipient online** (connected via WebSocket right now) → delivered live, in-app
- **Recipient offline, but linked to Discord** → delivered as a Discord DM
- **`DiscordRelay.Send(..., forceDiscord: true)`** → always sent via Discord DM, regardless of online status, *in addition to* normal delivery

The default (no `forceDiscord`) avoids double-notifying someone who's
actively in your game. The Chat UI sample uses `forceDiscord: true` because,
in a single-window demo, you're always "online" from the server's point of
view — without it, sending a message to yourself would just look like it did
nothing.

## Troubleshooting

**"Invalid OAuth2 redirect_uri" when clicking a link URL**
The Redirect URI registered in the Developer Portal (step 5) doesn't exactly
match what the server is sending — check for a trailing slash or a port
mismatch.

**The bot-invite page shows no servers in the dropdown**
You haven't created an actual Discord server yet (see step 3) — the
Application from step 2 isn't one.

**"EADDRINUSE" / "address already in use" when clicking Start**
Something is already listening on that port — most often a previous server
instance you forgot was running. Click **Stop** first, or check for a
lingering `node` process.

**Sent a message but no DM arrived, even though the player is linked**
If the recipient is currently connected (online), the message delivers
in-app instead of via DM by default — that's correct behavior, not a bug.
Use `forceDiscord: true` if you want it to always go to Discord regardless.

**Changed server code but the behavior didn't change**
Node doesn't hot-reload — restart the server (Stop, then Start) after any
change to files under `server/`.

**Everything I linked is gone after restarting the server**
The link table (and message queues) are in-memory only — restarting the
server clears them. See "Known limitations" below.

## Known limitations

- **No persistence.** Linked players and queued messages live in memory and
  are lost on server restart. Fine for local development, not yet suitable
  for a production deployment.
- **One Discord bot per deployment.** Each game/server instance needs its
  own bot application and OAuth credentials — see steps 2–5.
- **No inbound routing from Discord.** A reply typed directly in the Discord
  DM does not get sent back into the game — only outbound (game → Discord)
  delivery is implemented.
- **Local-only by default.** Everything above assumes `localhost`. Running
  the server somewhere other than your own machine works, but needs its own
  publicly reachable Redirect URI registered in step 5.
