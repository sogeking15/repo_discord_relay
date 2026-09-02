using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace DiscordRelayKit
{
    internal class RelaySocket
    {
        readonly string url;
        readonly string playerId;
        readonly RelayRunner runner;
        ClientWebSocket ws;
        CancellationTokenSource cts;

        public event Action<RelayMessage> OnMessage;
        public event Action OnOpen;
        public event Action<string> OnClose;

        public bool IsOpen => ws != null && ws.State == WebSocketState.Open;

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
