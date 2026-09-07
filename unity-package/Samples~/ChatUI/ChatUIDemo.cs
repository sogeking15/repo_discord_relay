using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DiscordRelayKit.Samples
{
    // Logic only - the UI itself lives in ChatUIDemo.unity, wired to these
    // fields in the Inspector. See that scene for the actual layout.
    public class ChatUIDemo : MonoBehaviour
    {
        [SerializeField] string relayBaseUrl = "localhost:8080";
        [SerializeField] string myPlayerId = "player-1";

        [Header("Wired in the scene")]
        [SerializeField] RectTransform playerListContent;
        [SerializeField] GameObject playerRowPrefab;
        [SerializeField] InputField messageInput;
        [SerializeField] Text selectedLabel;
        [SerializeField] Text logText;
        [SerializeField] Button sendButton;
        [SerializeField] Button refreshButton;
        [SerializeField] Button linkButton;

        string selectedPlayerId;
        readonly List<GameObject> playerRows = new List<GameObject>();

        void Awake()
        {
            sendButton.onClick.AddListener(OnSendClicked);
            refreshButton.onClick.AddListener(RefreshPlayerList);
            linkButton.onClick.AddListener(LinkDiscordAccount);
        }

        void OnEnable()
        {
            DiscordRelay.Connected += HandleConnected;
            DiscordRelay.OnMessageReceived += HandleMessageReceived;
            DiscordRelay.OnLinked += HandleLinked;
        }

        void OnDisable()
        {
            DiscordRelay.Connected -= HandleConnected;
            DiscordRelay.OnMessageReceived -= HandleMessageReceived;
            DiscordRelay.OnLinked -= HandleLinked;
        }

        void Start()
        {
            DiscordRelay.Connect(relayBaseUrl, myPlayerId);
        }

        void HandleConnected()
        {
            AppendLog("system", "connected to relay");
            RefreshPlayerList();
        }

        void HandleMessageReceived(RelayMessage msg)
        {
            AppendLog(msg.FromPlayerId, ExtractText(msg.Payload));
        }

        void HandleLinked(string discordUserId)
        {
            AppendLog("system", $"linked - Discord user {discordUserId}");
            RefreshPlayerList();
        }

        public void LinkDiscordAccount()
        {
            AppendLog("system", "requesting a Discord link...");
            DiscordRelay.RequestLink(
                authUrl =>
                {
                    if (this == null) return;
                    AppendLog("system", "opening Discord in your browser - authorize there to finish linking");
                    Application.OpenURL(authUrl);
                },
                error =>
                {
                    if (this == null) return;
                    AppendLog("system", $"link request failed: {error}");
                }
            );
        }

        public void RefreshPlayerList()
        {
            DiscordRelay.GetLinkedPlayers(
                players =>
                {
                    // The HTTP call behind this isn't tied to this component's
                    // lifetime, so it can still resolve after this object is
                    // gone - e.g. the player navigated away while it was in flight.
                    if (this == null) return;

                    foreach (var row in playerRows) Destroy(row);
                    playerRows.Clear();

                    foreach (var player in players)
                    {
                        playerRows.Add(CreatePlayerRow(player));
                    }
                },
                error =>
                {
                    if (this == null) return;
                    AppendLog("system", $"failed to load linked players: {error}");
                }
            );
        }

        public void SelectPlayer(string playerId)
        {
            selectedPlayerId = playerId;
            selectedLabel.text = $"Sending to: {playerId}";
        }

        public void OnSendClicked()
        {
            if (string.IsNullOrEmpty(selectedPlayerId))
            {
                AppendLog("system", "select a player first");
                return;
            }

            var text = messageInput.text;
            if (string.IsNullOrEmpty(text)) return;

            DiscordRelay.Send(selectedPlayerId, "chat", JsonForText(text));
            AppendLog("me", text);
            messageInput.text = "";
        }

        GameObject CreatePlayerRow(LinkedPlayer player)
        {
            var row = Instantiate(playerRowPrefab, playerListContent);
            row.GetComponentInChildren<Text>().text = $"{player.PlayerId} ({player.Username})";
            var playerId = player.PlayerId;
            row.GetComponent<Button>().onClick.AddListener(() => SelectPlayer(playerId));
            row.SetActive(true);
            return row;
        }

        static string JsonForText(string text) => new JObject { ["text"] = text }.ToString(Newtonsoft.Json.Formatting.None);

        static string ExtractText(string payloadJson)
        {
            try { return (string)JObject.Parse(payloadJson)["text"] ?? payloadJson; }
            catch { return payloadJson; }
        }

        void AppendLog(string fromId, string text)
        {
            logText.text += $"\n[{fromId}] {text}";
        }
    }
}
