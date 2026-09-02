using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace DiscordRelayKit
{
    internal class RelayRunner : MonoBehaviour
    {
        internal readonly ConcurrentQueue<Action> MainThreadActions = new ConcurrentQueue<Action>();

        internal static RelayRunner Create()
        {
            var go = new GameObject("DiscordRelayKit.Runner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<RelayRunner>();
        }

        void Update()
        {
            while (MainThreadActions.TryDequeue(out var action))
                action();
        }
    }
}
