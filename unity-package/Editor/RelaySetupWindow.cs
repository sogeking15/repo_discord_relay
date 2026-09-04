using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DiscordRelayKit.Editor
{
    public class RelaySetupWindow : EditorWindow
    {
        const string NodeProjectPathKey = "DiscordRelayKit.NodeProjectPath";
        const double HealthPollIntervalSeconds = 2.0;

        string nodeProjectPath = "";
        string botToken = "";
        string port = "8080";
        string clientId = "";
        string clientSecret = "";
        string redirectUri = "";
        string guildId = "";
        bool projectFound;

        bool serverRunning;
        bool healthCheckInFlight;
        double lastHealthCheck;
        string lanIp = "";

        [MenuItem("Tools/Discord Relay Kit/Setup")]
        public static void Open()
        {
            GetWindow<RelaySetupWindow>("Discord Relay Kit");
        }

        void OnEnable()
        {
            nodeProjectPath = EditorUserSettings.GetConfigValue(NodeProjectPathKey) ?? "";
            RefreshFromDisk();
            lanIp = LocalNetwork.GetLanIPAddress();
            healthCheckInFlight = true;
            _ = CheckHealthAsync();
            EditorApplication.update += PollHealth;
        }

        void OnDisable()
        {
            EditorApplication.update -= PollHealth;
        }

        void PollHealth()
        {
            if (healthCheckInFlight) return;
            if (EditorApplication.timeSinceStartup - lastHealthCheck < HealthPollIntervalSeconds) return;
            lastHealthCheck = EditorApplication.timeSinceStartup;
            healthCheckInFlight = true;
            _ = CheckHealthAsync();
        }

        async Task CheckHealthAsync()
        {
            var running = false;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
                var response = await client.GetAsync($"http://localhost:{port}/health");
                running = response.IsSuccessStatusCode;
            }
            catch
            {
                running = false;
            }
            serverRunning = running;
            healthCheckInFlight = false;
            Repaint();
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
            if (values.TryGetValue("DISCORD_CLIENT_ID", out var id)) clientId = id;
            if (values.TryGetValue("DISCORD_CLIENT_SECRET", out var secret)) clientSecret = secret;
            if (values.TryGetValue("DISCORD_GUILD_ID", out var guild)) guildId = guild;
            redirectUri = values.TryGetValue("DISCORD_REDIRECT_URI", out var uri) && !string.IsNullOrEmpty(uri)
                ? uri
                : $"http://localhost:{port}/link/callback";
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
            EditorGUILayout.LabelField("Discord OAuth2 (account linking)", EditorStyles.boldLabel);
            clientId = EditorGUILayout.TextField("Client ID", clientId);
            clientSecret = EditorGUILayout.PasswordField("Client secret", clientSecret);
            redirectUri = EditorGUILayout.TextField("Redirect URI", redirectUri);
            EditorGUILayout.HelpBox("Register this exact Redirect URI in the portal's OAuth2 tab.", MessageType.None);
            guildId = EditorGUILayout.TextField("Guild ID", guildId);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!projectFound))
            {
                if (GUILayout.Button("Save to server/.env"))
                {
                    SaveToEnv();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Local server", EditorStyles.boldLabel);

            var statusColor = GUI.color;
            GUI.color = serverRunning ? Color.green : Color.red;
            EditorGUILayout.LabelField(serverRunning ? "● Running" : "● Stopped");
            GUI.color = statusColor;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("LAN IP", lanIp);
            if (GUILayout.Button("Copy", GUILayout.Width(60)))
            {
                EditorGUIUtility.systemCopyBuffer = lanIp;
            }
            if (GUILayout.Button("Refresh", GUILayout.Width(60)))
            {
                lanIp = LocalNetwork.GetLanIPAddress();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!projectFound || serverRunning))
            {
                if (GUILayout.Button("Start")) StartServer();
            }
            using (new EditorGUI.DisabledScope(!serverRunning))
            {
                if (GUILayout.Button("Stop")) StopServer();
            }
            EditorGUILayout.EndHorizontal();
        }

        void StartServer()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = "src/server.js",
                WorkingDirectory = nodeProjectPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.EnvironmentVariables["PORT"] = port;

            var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) Debug.Log($"[relay] {e.Data}"); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) Debug.LogWarning($"[relay] {e.Data}"); };

            try
            {
                process.Start();
            }
            catch (Exception e)
            {
                Debug.LogError($"Discord Relay Kit: failed to start the server - {e.Message}. Is node.js on PATH, and is port {port} already in use by something else?");
                return;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Deliberately not held onto beyond this call - the process is meant
            // to outlive the editor (and survive domain reloads), so Stop works
            // via /shutdown instead of a held Process handle. Log streaming above
            // only lasts until the next recompile, when this subscription is lost.
        }

        void StopServer()
        {
            _ = StopServerAsync();
        }

        async Task StopServerAsync()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                await client.PostAsync($"http://localhost:{port}/shutdown", null);
            }
            catch
            {
                // server may already be down
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
            SetKey(lines, "DISCORD_CLIENT_ID", clientId);
            SetKey(lines, "DISCORD_CLIENT_SECRET", clientSecret);
            SetKey(lines, "DISCORD_REDIRECT_URI", redirectUri);
            SetKey(lines, "DISCORD_GUILD_ID", guildId);

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
