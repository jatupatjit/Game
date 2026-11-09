#if UNITY_EDITOR
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Editor-only 2-player testing with Multiplayer Play Mode, without Steam.
    /// Turn on with Tools > Multiplayer > Player 2 Auto-Join (Localhost). When Play starts, the main editor
    /// hosts on 127.0.0.1:7777 and the Player 2 window joins it as a client, so both windows are playable.
    /// The setting lives in EditorPrefs, which the main editor and the Player 2 clone share.
    /// </summary>
    public sealed class MppmLocalJoin : MonoBehaviour
    {
        private const string PrefKey = "DDI.MppmLocalJoin";
        private const string MenuPath = "Tools/Multiplayer/Player 2 Auto-Join (Localhost)";
        private const ushort Port = 7777;

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem(MenuPath, false, 30)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            if (Enabled) SetPlayerTwoActive(true);
            Debug.Log("[MppmLocalJoin] " + (Enabled
                ? "ON: press Play; this window hosts and the Player 2 window joins on 127.0.0.1."
                : "OFF: Play starts normally (title screen, Steam)."));
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return !EditorApplication.isPlaying;
        }

        /// <summary>Opens or closes the MPPM Player 2 window (MPPM has no public API for this).</summary>
        private static void SetPlayerTwoActive(bool active)
        {
            try
            {
                var type = System.Type.GetType("Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode, UnityEditor.MultiplayerModule") ??
                    FindType("Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode");
                var player = type?.GetProperty("PlayerTwo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null);
                player?.GetType().GetMethod(active ? "Activate" : "Deactivate", System.Type.EmptyTypes)?.Invoke(player, null);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MppmLocalJoin] Could not open Player 2 automatically; open it from Window > Multiplayer > Multiplayer Play Mode. " + e.Message);
            }
        }

        private static System.Type FindType(string fullName)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Enabled || FindAnyObjectByType<MppmLocalJoin>() != null) return;
            var go = new GameObject("MppmLocalJoin") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            go.AddComponent<MppmLocalJoin>();
        }

        private IEnumerator Start()
        {
            bool host = Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor;
            string who = host ? "Host" : "Player 2";
            // Give the host a head start so the client's first try usually connects.
            yield return new WaitForSecondsRealtime(host ? 0.5f : 3f);
            NetworkManager manager = NetworkManager.Singleton;
            float waited = 0f;
            while (manager == null && waited < 20f) { yield return null; waited += Time.unscaledDeltaTime; manager = NetworkManager.Singleton; }
            if (manager == null) { Debug.LogWarning("[MppmLocalJoin] No NetworkManager in this scene."); yield break; }
            if (manager.IsListening) yield break; // already hosting/joined some other way

            SteamLobbyManager.EnsureInstance().SetTransportType(SteamLobbyManager.TransportType.UnityTransport);
            var transport = manager.GetComponent<UnityTransport>();
            if (transport == null) { Debug.LogWarning("[MppmLocalJoin] NetworkManager has no UnityTransport."); yield break; }
            transport.SetConnectionData("127.0.0.1", Port);
            manager.NetworkConfig.NetworkTransport = transport;

            if (host)
            {
                Debug.Log("[MppmLocalJoin] Host start=" + manager.StartHost());
                yield break;
            }
            // Player 2: retry until the host is up (the main editor may still be loading).
            for (int attempt = 1; attempt <= 15; attempt++)
            {
                if (!manager.IsListening) Debug.Log($"[MppmLocalJoin] {who} join try {attempt}: " + manager.StartClient());
                float t = 0f;
                while (t < 4f && !manager.IsConnectedClient) { yield return null; t += Time.unscaledDeltaTime; }
                if (manager.IsConnectedClient) { Debug.Log("[MppmLocalJoin] Player 2 joined."); yield break; }
                if (manager.IsListening) { manager.Shutdown(); yield return new WaitForSecondsRealtime(0.5f); }
            }
            Debug.LogWarning("[MppmLocalJoin] Player 2 could not join. Is the main editor in Play?");
        }
    }
}
#endif
