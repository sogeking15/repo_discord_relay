using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace DiscordRelayKit.Tests
{
    // Starts the real Node relay server as a child process and drives the real
    // DiscordRelay runtime API against it, with a raw-WebSocket TestPeer standing
    // in for "the other player" (DiscordRelay is a single-connection static API,
    // matching one Unity process = one local player, so two peers in one process
    // isn't otherwise possible - and a raw peer is closer to reality anyway, since
    // the other side is never actually another Unity instance).
    public class DiscordRelayInteropTests
    {
        const int Port = 8199;
        static readonly string BaseUrl = $"localhost:{Port}";

        Process serverProcess;
        Action<RelayMessage> messageHandler;

        [OneTimeSetUp]
        public void StartServer()
        {
            var serverPath = FindServerFolder();
            serverProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = "src/server.js",
                    WorkingDirectory = serverPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            serverProcess.StartInfo.EnvironmentVariables["PORT"] = Port.ToString();
            serverProcess.OutputDataReceived += (_, e) => { if (e.Data != null) Debug.Log($"[relay] {e.Data}"); };
            serverProcess.ErrorDataReceived += (_, e) => { if (e.Data != null) Debug.LogWarning($"[relay] {e.Data}"); };
            serverProcess.Start();
            serverProcess.BeginOutputReadLine();
            serverProcess.BeginErrorReadLine();
        }

        [OneTimeTearDown]
        public void StopServer()
        {
            if (serverProcess == null || serverProcess.HasExited) return;
            try { serverProcess.Kill(); } catch { /* already gone */ }
            serverProcess.Dispose();
        }

        [TearDown]
        public void CleanUpAfterEachTest()
        {
            if (messageHandler != null)
            {
                DiscordRelay.OnMessageReceived -= messageHandler;
                messageHandler = null;
            }
            DiscordRelay.Disconnect();
        }

        [UnityTest]
        public IEnumerator UnityClientReceivesMessageFromExternalPeer()
        {
            yield return WaitForServerHealthy();

            var peer = new TestPeer();
            _ = peer.ConnectAsync($"ws://{BaseUrl}/relay", "peer-a");
            yield return WaitUntilOrFail(() => peer.Ready, 5f, "external peer never connected");

            var received = false;
            RelayMessage inbox = null;
            messageHandler = m => { received = true; inbox = m; };
            DiscordRelay.OnMessageReceived += messageHandler;

            DiscordRelay.Connect(BaseUrl, "unity-player");
            yield return WaitUntilOrFail(() => DiscordRelay.IsConnected, 5f, "Unity client never connected");

            _ = peer.SendMessage("unity-player", "greeting", new JObject { ["text"] = "hello from peer" });
            yield return WaitUntilOrFail(() => received, 5f, "Unity client never received the message");

            Assert.AreEqual("peer-a", inbox.FromPlayerId);
            Assert.AreEqual("greeting", inbox.Kind);
            StringAssert.Contains("hello from peer", inbox.Payload);

            peer.Close();
        }

        [UnityTest]
        public IEnumerator UnityClientSendsMessageToExternalPeer()
        {
            yield return WaitForServerHealthy();

            var peer = new TestPeer();
            _ = peer.ConnectAsync($"ws://{BaseUrl}/relay", "peer-b");
            yield return WaitUntilOrFail(() => peer.Ready, 5f, "external peer never connected");

            DiscordRelay.Connect(BaseUrl, "unity-sender");
            yield return WaitUntilOrFail(() => DiscordRelay.IsConnected, 5f, "Unity client never connected");

            DiscordRelay.Send("peer-b", "ping", "{\"n\":42}");
            yield return WaitUntilOrFail(() => peer.LastPayload != null, 5f, "external peer never received the message");

            Assert.AreEqual("ping", peer.LastKind);
            StringAssert.Contains("42", peer.LastPayload);

            peer.Close();
        }

        [UnityTest]
        public IEnumerator MessageSentWhileOfflineIsDeliveredOnReconnect()
        {
            yield return WaitForServerHealthy();

            var peer = new TestPeer();
            _ = peer.ConnectAsync($"ws://{BaseUrl}/relay", "peer-c");
            yield return WaitUntilOrFail(() => peer.Ready, 5f, "external peer never connected");

            _ = peer.SendMessage("offline-player", "note", new JObject { ["text"] = "queued while offline" });
            yield return new WaitForSeconds(0.5f);

            var received = false;
            RelayMessage inbox = null;
            messageHandler = m => { received = true; inbox = m; };
            DiscordRelay.OnMessageReceived += messageHandler;

            DiscordRelay.Connect(BaseUrl, "offline-player");
            yield return WaitUntilOrFail(() => received, 5f, "queued message was not delivered on connect");

            Assert.AreEqual("queued while offline", (string)JObject.Parse(inbox.Payload)["text"]);

            peer.Close();
        }

        IEnumerator WaitForServerHealthy()
        {
            using var client = new HttpClient();
            var deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline)
            {
                Task<HttpResponseMessage> task = null;
                try { task = client.GetAsync($"http://{BaseUrl}/health"); }
                catch { /* server not listening yet */ }

                if (task != null)
                {
                    while (!task.IsCompleted) yield return null;
                    if (!task.IsFaulted && task.Result.IsSuccessStatusCode) yield break;
                }
                yield return null;
            }
            Assert.Fail("relay server never became healthy");
        }

        static IEnumerator WaitUntilOrFail(Func<bool> condition, float timeoutSeconds, string failureMessage)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail(failureMessage);
                yield return null;
            }
        }

        static string FindServerFolder([CallerFilePath] string thisFile = "")
        {
            var dir = Path.GetDirectoryName(thisFile);
            return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "server"));
        }

        // Raw WebSocket peer standing in for "the other player" - deliberately not
        // using the package's own RelaySocket, so the test exercises the wire
        // protocol independently rather than testing RelaySocket against itself.
        class TestPeer
        {
            readonly ClientWebSocket ws = new ClientWebSocket();

            public volatile bool Ready;
            public volatile string LastKind;
            public volatile string LastPayload;

            public async Task ConnectAsync(string url, string playerId)
            {
                await ws.ConnectAsync(new Uri(url), CancellationToken.None);
                await SendRaw(new JObject { ["type"] = "identify", ["playerId"] = playerId });
                _ = ReceiveLoop();
            }

            async Task ReceiveLoop()
            {
                var buffer = new byte[8192];
                while (ws.State == WebSocketState.Open)
                {
                    string text;
                    using (var stream = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                            if (result.MessageType == WebSocketMessageType.Close) return;
                            stream.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);
                        text = Encoding.UTF8.GetString(stream.ToArray());
                    }

                    JObject data;
                    try { data = JObject.Parse(text); }
                    catch { continue; }

                    switch ((string)data["type"])
                    {
                        case "identified":
                            Ready = true;
                            break;
                        case "message.deliver":
                            LastKind = (string)data["kind"];
                            LastPayload = data["payload"]?.ToString(Newtonsoft.Json.Formatting.None);
                            _ = SendRaw(new JObject { ["type"] = "message.ack", ["messageId"] = data["messageId"] });
                            break;
                    }
                }
            }

            public Task SendMessage(string toPlayerId, string kind, JToken payload) => SendRaw(new JObject
            {
                ["type"] = "message.send",
                ["toPlayerId"] = toPlayerId,
                ["kind"] = kind,
                ["payload"] = payload,
            });

            async Task SendRaw(JObject obj)
            {
                var bytes = Encoding.UTF8.GetBytes(obj.ToString(Newtonsoft.Json.Formatting.None));
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }

            public void Close() { try { ws.Abort(); } catch { /* already closed */ } }
        }
    }
}
