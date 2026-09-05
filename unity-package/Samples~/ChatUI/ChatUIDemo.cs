using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DiscordRelayKit.Samples
{
    // Attach to any empty GameObject in any scene - builds its own Canvas/UI at
    // runtime, so there's no separate scene setup required. Shows the full loop:
    // a picker for linked players, a text field, Send, and a log of what comes back.
    public class ChatUIDemo : MonoBehaviour
    {
        [SerializeField] string relayBaseUrl = "localhost:8080";
        [SerializeField] string myPlayerId = "player-1";

        Text logText;
        InputField messageInput;
        Text selectedLabel;
        Transform playerListContent;

        string selectedPlayerId;
        readonly List<GameObject> playerRows = new List<GameObject>();

        void Awake()
        {
            BuildUI();
        }

        void OnEnable()
        {
            DiscordRelay.Connected += HandleConnected;
            DiscordRelay.OnMessageReceived += HandleMessageReceived;
        }

        void OnDisable()
        {
            DiscordRelay.Connected -= HandleConnected;
            DiscordRelay.OnMessageReceived -= HandleMessageReceived;
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

        public void RefreshPlayerList()
        {
            DiscordRelay.GetLinkedPlayers(
                players =>
                {
                    // The HTTP call behind this isn't tied to this component's
                    // lifetime, so it can still resolve after this object (and
                    // its UI) is gone - e.g. the player navigated away while it
                    // was in flight.
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

        GameObject CreatePlayerRow(LinkedPlayer player)
        {
            var go = new GameObject(player.PlayerId, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(playerListContent, false);
            go.GetComponent<LayoutElement>().minHeight = 30;
            go.GetComponent<Image>().color = new Color(0, 0, 0, 0.15f);

            var label = CreateText(go.transform, $"{player.PlayerId} ({player.Username})", TextAnchor.MiddleLeft);
            var labelRt = label.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(8, 0);
            labelRt.offsetMax = Vector2.zero;

            var playerId = player.PlayerId;
            go.GetComponent<Button>().onClick.AddListener(() => SelectPlayer(playerId));
            return go;
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            if (FindObjectOfType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }

            var listPanel = CreatePanel(canvasGo.transform, new Vector2(0, 0), new Vector2(0.3f, 1));

            CreateText(listPanel.transform, "Linked players", TextAnchor.MiddleLeft).rectTransform.SetAnchors(0, 0.92f, 1, 1);

            var scrollGo = new GameObject("PlayerScroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(Mask));
            scrollGo.transform.SetParent(listPanel.transform, false);
            ((RectTransform)scrollGo.transform).SetAnchors(0, 0.08f, 1, 0.9f);

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(scrollGo.transform, false);
            var contentRt = (RectTransform)content.transform;
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            content.GetComponent<VerticalLayoutGroup>().childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRect = scrollGo.GetComponent<ScrollRect>();
            scrollRect.content = contentRt;
            scrollRect.horizontal = false;
            playerListContent = content.transform;

            var refreshGo = CreateButton(listPanel.transform, "Refresh", RefreshPlayerList);
            ((RectTransform)refreshGo.transform).SetAnchors(0, 0, 1, 0.07f);

            var rightPanel = CreatePanel(canvasGo.transform, new Vector2(0.3f, 0), new Vector2(1, 1));

            var logScrollGo = new GameObject("LogScroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(Mask));
            logScrollGo.transform.SetParent(rightPanel.transform, false);
            ((RectTransform)logScrollGo.transform).SetAnchors(0, 0.2f, 1, 1);

            var logContentGo = new GameObject("LogContent", typeof(RectTransform));
            logContentGo.transform.SetParent(logScrollGo.transform, false);
            var logContentRt = (RectTransform)logContentGo.transform;
            logContentRt.anchorMin = Vector2.zero;
            logContentRt.anchorMax = Vector2.one;
            logContentRt.offsetMin = logContentRt.offsetMax = Vector2.zero;

            logText = CreateText(logContentGo.transform, "(messages will appear here)", TextAnchor.LowerLeft);
            logText.rectTransform.SetAnchors(0, 0, 1, 1);
            logScrollGo.GetComponent<ScrollRect>().content = logContentRt;
            logScrollGo.GetComponent<ScrollRect>().horizontal = false;

            selectedLabel = CreateText(rightPanel.transform, "Sending to: (none selected)", TextAnchor.MiddleLeft);
            selectedLabel.rectTransform.SetAnchors(0, 0.13f, 1, 0.2f);

            var inputGo = new GameObject("MessageInput", typeof(RectTransform), typeof(Image), typeof(InputField));
            inputGo.transform.SetParent(rightPanel.transform, false);
            ((RectTransform)inputGo.transform).SetAnchors(0, 0.06f, 1, 0.13f);

            var inputTextGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            inputTextGo.transform.SetParent(inputGo.transform, false);
            var inputText = inputTextGo.GetComponent<Text>();
            inputText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            inputText.color = Color.black;
            ((RectTransform)inputTextGo.transform).SetAnchors(0, 0, 1, 1, new Vector2(8, 0), Vector2.zero);

            messageInput = inputGo.GetComponent<InputField>();
            messageInput.textComponent = inputText;

            var sendGo = CreateButton(rightPanel.transform, "Send", OnSendClicked);
            ((RectTransform)sendGo.transform).SetAnchors(0, 0, 1, 0.05f);
        }

        static GameObject CreatePanel(Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).SetAnchors(anchorMin.x, anchorMin.y, anchorMax.x, anchorMax.y);
            go.GetComponent<Image>().color = new Color(0, 0, 0, 0.05f);
            return go;
        }

        static Text CreateText(Transform parent, string text, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.color = Color.black;
            t.alignment = anchor;
            return t;
        }

        static GameObject CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Button>().onClick.AddListener(onClick);
            var text = CreateText(go.transform, label, TextAnchor.MiddleCenter);
            text.rectTransform.SetAnchors(0, 0, 1, 1);
            return go;
        }
    }

    static class RectTransformExtensions
    {
        public static void SetAnchors(this RectTransform rt, float xMin, float yMin, float xMax, float yMax)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        public static void SetAnchors(this RectTransform rt, float xMin, float yMin, float xMax, float yMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }
    }
}
