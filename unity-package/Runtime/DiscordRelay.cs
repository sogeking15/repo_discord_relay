using System;

namespace DiscordRelayKit
{
    public static class DiscordRelay
    {
        public static event Action Connected;
        public static event Action<string> Disconnected;
        public static event Action<RelayMessage> OnMessageReceived;

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
            socket.OnMessage += msg => OnMessageReceived?.Invoke(msg);
            socket.Connect();
        }

        public static void Send(string toPlayerId, string kind, string payloadJson = null)
        {
            if (socket == null)
                throw new InvalidOperationException("DiscordRelay.Connect() must be called before Send().");
            socket.SendMessage(toPlayerId, kind, payloadJson);
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
