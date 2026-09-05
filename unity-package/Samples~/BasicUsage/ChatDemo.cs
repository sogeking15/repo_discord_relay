using UnityEngine;

namespace DiscordRelayKit.Samples
{
    // Drop this on any GameObject to see the whole API in one place. Right-click
    // the component in Play Mode to trigger linking or send a test message.
    public class ChatDemo : MonoBehaviour
    {
        [SerializeField] string relayBaseUrl = "localhost:8080";
        [SerializeField] string myPlayerId = "player-1";
        [SerializeField] string sendToPlayerId = "player-2";

        void OnEnable()
        {
            DiscordRelay.Connected += HandleConnected;
            DiscordRelay.Disconnected += HandleDisconnected;
            DiscordRelay.OnMessageReceived += HandleMessageReceived;
            DiscordRelay.OnLinked += HandleLinked;
        }

        void OnDisable()
        {
            DiscordRelay.Connected -= HandleConnected;
            DiscordRelay.Disconnected -= HandleDisconnected;
            DiscordRelay.OnMessageReceived -= HandleMessageReceived;
            DiscordRelay.OnLinked -= HandleLinked;
        }

        void Start()
        {
            DiscordRelay.Connect(relayBaseUrl, myPlayerId);
        }

        void HandleConnected() => Debug.Log("[ChatDemo] connected to relay");

        void HandleDisconnected(string reason) => Debug.Log($"[ChatDemo] disconnected: {reason}");

        void HandleMessageReceived(RelayMessage msg) =>
            Debug.Log($"[ChatDemo] received '{msg.Kind}' from {msg.FromPlayerId}: {msg.Payload}");

        void HandleLinked(string discordUserId) => Debug.Log($"[ChatDemo] Discord account linked: {discordUserId}");

        [ContextMenu("Request Discord Link")]
        void RequestDiscordLink()
        {
            DiscordRelay.RequestLink(
                authUrl =>
                {
                    Debug.Log($"[ChatDemo] opening {authUrl}");
                    Application.OpenURL(authUrl);
                },
                error => Debug.LogError($"[ChatDemo] link request failed: {error}")
            );
        }

        [ContextMenu("Send Test Message")]
        void SendTestMessage()
        {
            DiscordRelay.Send(sendToPlayerId, "chat", "{\"text\":\"Hello from Unity!\"}");
        }
    }
}
