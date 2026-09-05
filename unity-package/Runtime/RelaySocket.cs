using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DiscordRelayKit
{
    internal class RelaySocket
    {
        readonly string url;
        readonly string playerId;
        readonly RelayRunner runner;
        ClientWebSocket ws;
        CancellationTokenSource cts;
        volatile bool identified;

        public event Action<RelayMessage> OnMessage;
        public event Action OnOpen;
        public event Action<string> OnClose;

        // Gated on the identify handshake completing, not just the raw socket -
        // ws.State flips to Open as soon as the WS handshake finishes, which is
        // before "identify" has even been sent, let alone acknowledged. A caller
        // sending on IsOpen alone can race ahead of identify and get rejected.
        public bool IsOpen => identified && ws != null && ws.State == WebSocketState.Open;

        public RelaySocket(string url, string playerId, RelayRunner runner)
        {
            this.url = url;
            this.playerId = playerId;
            this.runner = runner;
        }

        public void Connect()
        {
            cts = new CancellationTokenSource();
            _ = RunAsync(cts.Token);
        }

        async Task RunAsync(CancellationToken token)
        {
            ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(new Uri(url), token);
                await SendRaw(new JObject { ["type"] = "identify", ["playerId"] = playerId });

                var buffer = new byte[8192];
                while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    using var stream = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        stream.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;

                    HandleIncoming(Encoding.UTF8.GetString(stream.ToArray()));
                }
            }
            catch (Exception e)
            {
                runner.MainThreadActions.Enqueue(() => OnClose?.Invoke(e.Message));
                return;
            }

            runner.MainThreadActions.Enqueue(() => OnClose?.Invoke("closed"));
        }

        void HandleIncoming(string text)
        {
            JObject data;
            try { data = JObject.Parse(text); }
            catch { return; }

            switch ((string)data["type"])
            {
                case "identified":
                    identified = true;
                    runner.MainThreadActions.Enqueue(() => OnOpen?.Invoke());
                    break;

                case "message.deliver":
                    var msg = new RelayMessage
                    {
                        MessageId = (string)data["messageId"],
                        FromPlayerId = (string)data["fromPlayerId"],
                        Kind = (string)data["kind"],
                        Payload = data["payload"]?.ToString(Newtonsoft.Json.Formatting.None),
                        Ts = (long?)data["ts"] ?? 0,
                    };
                    _ = SendRaw(new JObject { ["type"] = "message.ack", ["messageId"] = msg.MessageId });
                    runner.MainThreadActions.Enqueue(() => OnMessage?.Invoke(msg));
                    break;

                case "error":
                    var errorText = (string)data["message"];
                    runner.MainThreadActions.Enqueue(() => Debug.LogWarning($"DiscordRelayKit: relay server error - {errorText}"));
                    break;
            }
        }

        public void SendMessage(string toPlayerId, string kind, string payloadJson)
        {
            JToken payload = string.IsNullOrEmpty(payloadJson) ? null : JToken.Parse(payloadJson);
            _ = SendRaw(new JObject
            {
                ["type"] = "message.send",
                ["toPlayerId"] = toPlayerId,
                ["kind"] = kind,
                ["payload"] = payload,
            });
        }

        string HttpBase()
        {
            var httpUrl = url.Replace("ws://", "http://");
            return httpUrl.Substring(0, httpUrl.Length - "/relay".Length);
        }

        public async void RequestLink(Action<string> onAuthUrlReceived, Action<string> onError)
        {
            try
            {
                using var client = new HttpClient();
                var response = await client.GetAsync($"{HttpBase()}/link/start?playerId={Uri.EscapeDataString(playerId)}");
                var text = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(text);

                if (!response.IsSuccessStatusCode)
                {
                    var errMsg = (string)json["error"] ?? $"link/start failed ({(int)response.StatusCode})";
                    runner.MainThreadActions.Enqueue(() => onError?.Invoke(errMsg));
                    return;
                }

                var authUrl = (string)json["authUrl"];
                runner.MainThreadActions.Enqueue(() => onAuthUrlReceived?.Invoke(authUrl));
            }
            catch (Exception e)
            {
                runner.MainThreadActions.Enqueue(() => onError?.Invoke(e.Message));
            }
        }

        public async void GetLinkedPlayers(Action<LinkedPlayer[]> onResult, Action<string> onError)
        {
            try
            {
                using var client = new HttpClient();
                var response = await client.GetAsync($"{HttpBase()}/players/linked");
                var text = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(text);

                if (!response.IsSuccessStatusCode)
                {
                    var errMsg = (string)json["error"] ?? $"players/linked failed ({(int)response.StatusCode})";
                    runner.MainThreadActions.Enqueue(() => onError?.Invoke(errMsg));
                    return;
                }

                var players = new System.Collections.Generic.List<LinkedPlayer>();
                foreach (var p in (JArray)json["players"])
                {
                    players.Add(new LinkedPlayer
                    {
                        PlayerId = (string)p["playerId"],
                        DiscordUserId = (string)p["discordUserId"],
                        Username = (string)p["username"],
                    });
                }

                runner.MainThreadActions.Enqueue(() => onResult?.Invoke(players.ToArray()));
            }
            catch (Exception e)
            {
                runner.MainThreadActions.Enqueue(() => onError?.Invoke(e.Message));
            }
        }

        async Task SendRaw(JObject obj)
        {
            if (ws == null || ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(obj.ToString(Newtonsoft.Json.Formatting.None));
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
        }

        public void Close()
        {
            cts?.Cancel();
            try { ws?.Abort(); } catch { /* already closed */ }
        }
    }
}
