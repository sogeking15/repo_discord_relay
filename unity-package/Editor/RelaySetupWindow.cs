using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DiscordRelayKit.Editor
{
    public class RelaySetupWindow : EditorWindow
    {
        const string NodeProjectPathKey = "DiscordRelayKit.NodeProjectPath";

        string nodeProjectPath = "";
        string botToken = "";
        string port = "8080";
        bool projectFound;

        [MenuItem("Tools/Discord Relay Kit/Setup")]
        public static void Open()
        {
            GetWindow<RelaySetupWindow>("Discord Relay Kit");
        }

        void OnEnable()
        {
            nodeProjectPath = EditorUserSettings.GetConfigValue(NodeProjectPathKey) ?? "";
            RefreshFromDisk();
        }

        void RefreshFromDisk()
        {
            projectFound = File.Exists(Path.Combine(nodeProjectPath, "package.json"));
            if (!projectFound) return;

            var envPath = Path.Combine(nodeProjectPath, ".env");
            if (!File.Exists(envPath)) return;

            var values = ParseEnv(envPath);
            if (values.TryGetValue("DISCORD_BOT_TOKEN", out var token)) botToken = token;
            if (values.TryGetValue("PORT", out var configuredPort)) port = configuredPort;
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Node project", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            var newPath = EditorGUILayout.TextField("Path", nodeProjectPath);
            if (newPath != nodeProjectPath)
            {
                nodeProjectPath = newPath;
                EditorUserSettings.SetConfigValue(NodeProjectPathKey, nodeProjectPath);
                RefreshFromDisk();
            }
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                var picked = EditorUtility.OpenFolderPanel("Select the relay server folder", nodeProjectPath, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    nodeProjectPath = picked;
                    EditorUserSettings.SetConfigValue(NodeProjectPathKey, nodeProjectPath);
                    RefreshFromDisk();
                }
            }
            EditorGUILayout.EndHorizontal();

            var prevColor = GUI.color;
            GUI.color = projectFound ? Color.green : Color.red;
            EditorGUILayout.LabelField(projectFound ? "✓ package.json found" : "✗ not a Node project (no package.json)");
            GUI.color = prevColor;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Discord bot", EditorStyles.boldLabel);
            botToken = EditorGUILayout.PasswordField("Bot token", botToken);
            port = EditorGUILayout.TextField("Port", port);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!projectFound))
            {
                if (GUILayout.Button("Save to server/.env"))
                {
                    SaveToEnv();
                }
            }
        }

        void SaveToEnv()
        {
            var envPath = Path.Combine(nodeProjectPath, ".env");
            var examplePath = Path.Combine(nodeProjectPath, ".env.example");

            List<string> lines;
            if (File.Exists(envPath))
                lines = File.ReadAllLines(envPath).ToList();
            else if (File.Exists(examplePath))
                lines = File.ReadAllLines(examplePath).ToList();
            else
                lines = new List<string>();

            SetKey(lines, "DISCORD_BOT_TOKEN", botToken);
            SetKey(lines, "PORT", port);

            File.WriteAllLines(envPath, lines);
            Debug.Log($"Discord Relay Kit: wrote {envPath}");
        }

        static void SetKey(List<string> lines, string key, string value)
        {
            var prefix = key + "=";
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith(prefix))
                {
                    lines[i] = prefix + value;
                    return;
                }
            }
            lines.Add(prefix + value);
        }

        static Dictionary<string, string> ParseEnv(string path)
        {
            var result = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                var idx = trimmed.IndexOf('=');
                if (idx < 0) continue;
                result[trimmed.Substring(0, idx)] = trimmed.Substring(idx + 1);
            }
            return result;
        }
    }
}
