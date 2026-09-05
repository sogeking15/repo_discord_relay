using System;
using Newtonsoft.Json.Linq;

namespace DiscordRelayKit
{
    public static class DiscordRelay
    {
        public static event Action Connected;
        public static event Action<string> Disconnected;
        public static event Action<RelayMessage> OnMessageReceived;
        public static event Action<string> OnLinked;

        static RelaySocket socket;
        static RelayRunner runner;

        public static bool IsConnected => socket != null && socket.IsOpen;

        public static void Connect(string baseUrl, string playerId)
        {
            if (runner == null) runner = RelayRunner.Create();

            socket?.Close();
            socket = new RelaySocket(NormalizeUrl(baseUrl), playerId, runner);
            socket.OnOpen += () => Connected?.Invoke();
            socket.OnClose += reason => Disconnected?.Invoke(reason);
            socket.OnMessage += HandleMessage;
            socket.Connect();
        }

        public static void Send(string toPlayerId, string kind, string payloadJson = null)
        {
            if (socket == null)
                throw new InvalidOperationException("DiscordRelay.Connect() must be called before Send().");
            socket.SendMessage(toPlayerId, kind, payloadJson);
        }

        // Returns a Discord OAuth2 URL for the caller to open (e.g. Application.OpenURL) -
        // the kit doesn't decide how that's presented. OnLinked fires once the player
        // completes it in their browser; no code to type, no server to join manually.
        public static void RequestLink(Action<string> onAuthUrlReceived, Action<string> onError = null)
        {
            if (socket == null)
                throw new InvalidOperationException("DiscordRelay.Connect() must be called before RequestLink().");
            socket.RequestLink(onAuthUrlReceived, onError);
        }

        public static void GetLinkedPlayers(Action<LinkedPlayer[]> onResult, Action<string> onError = null)
        {
            if (socket == null)
                throw new InvalidOperationException("DiscordRelay.Connect() must be called before GetLinkedPlayers().");
            socket.GetLinkedPlayers(onResult, onError);
        }

        static void HandleMessage(RelayMessage msg)
        {
            if (msg.Kind == "link.complete")
            {
                OnLinked?.Invoke(ExtractDiscordUserId(msg.Payload));
            }
            else
            {
                OnMessageReceived?.Invoke(msg);
            }
        }

        static string ExtractDiscordUserId(string payloadJson)
        {
            try
            {
                return (string)JObject.Parse(payloadJson)["discordUserId"];
            }
            catch
            {
                return null;
            }
        }

        public static void Disconnect()
        {
            socket?.Close();
            socket = null;
        }

        static string NormalizeUrl(string baseUrl)
        {
            var cleaned = baseUrl
                .Replace("wss://", "")
                .Replace("ws://", "")
                .Replace("https://", "")
                .Replace("http://", "")
                .TrimEnd('/');
            return $"ws://{cleaned}/relay";
        }
    }
}
