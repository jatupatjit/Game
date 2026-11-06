using System.Reflection;
using CoopGame.Network;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Editor-only: pressing Play on a level scene (Level01, Level02, Tutorial, ...) starts the
    /// game the same way the Lobby portal does. Play mode boots the Lobby scene (which owns the
    /// NetworkManager), starts a local host on 127.0.0.1:7788, waits for the local player, then
    /// loads the scene you had open through NGO scene management. Lobby itself is untouched.
    /// Works for any scene without its own NetworkManager; scenes outside Build Settings are
    /// added for the Play session only and removed again when Play mode ends.
    /// Toggle: Tools/Expedition/Play From Any Scene.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayFromAnyScene
    {
        private const string LobbyPath = "Assets/Scenes/Lobby.unity";
        private const string MenuPath = "Tools/Expedition/Play From Any Scene";
        private const string EnabledPref = "CoopGame.PlayFromAnyScene.Enabled";
        private const string TargetKey = "CoopGame.PlayFromAnyScene.Target";
        public const ushort Port = 7788;
        private const float TimeoutSeconds = 30f;

        private enum Step { Idle, WaitManager, WaitPlayer, Done }
        private static Step _step;
        private static float _deadline;
        private static string _target;

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPref, true);
            set => EditorPrefs.SetBool(EnabledPref, value);
        }

        static PlayFromAnyScene()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            // Recover if the Editor reloaded mid-session before Play mode ended cleanly.
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) RestoreBuildList();
            };
        }

        [MenuItem(MenuPath, priority = 0)]
        private static void Toggle() => Enabled = !Enabled;

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode: PrepareBoot(); break;
                case PlayModeStateChange.EnteredPlayMode: BeginBoot(); break;
                case PlayModeStateChange.ExitingPlayMode: StopDriver(); break;
                case PlayModeStateChange.EnteredEditMode:
                    EditorSceneManager.playModeStartScene = null;
                    SessionState.EraseString(TargetKey);
                    RestoreBuildList();
                    break;
            }
        }

        // NGO can only load scenes that have a build index. Scenes outside Build Settings
        // (test rigs, previews) are appended for this Play session only and removed afterwards.
        private const string AddedSceneKey = "CoopGame.PlayFromAnyScene.AddedToBuild";

        private static void EnsureInBuildList(string path)
        {
            RestoreBuildList();
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path != path) continue;
                if (scenes[i].enabled) return;
                // Present but disabled: enable it temporarily.
                scenes[i] = new EditorBuildSettingsScene(scenes[i].guid, true);
                EditorBuildSettings.scenes = scenes;
                SessionState.SetString(AddedSceneKey, "enable|" + path);
                return;
            }

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes)
            {
                new EditorBuildSettingsScene(path, true)
            };
            EditorBuildSettings.scenes = list.ToArray();
            SessionState.SetString(AddedSceneKey, "add|" + path);
        }

        private static void RestoreBuildList()
        {
            string record = SessionState.GetString(AddedSceneKey, string.Empty);
            if (string.IsNullOrEmpty(record)) return;
            SessionState.EraseString(AddedSceneKey);

            int split = record.IndexOf('|');
            string mode = record.Substring(0, split), path = record.Substring(split + 1);
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].path != path) continue;
                if (mode == "add") list.RemoveAt(i);
                else list[i] = new EditorBuildSettingsScene(list[i].guid, false);
                break;
            }
            EditorBuildSettings.scenes = list.ToArray();
        }

        private static void PrepareBoot()
        {
            EditorSceneManager.playModeStartScene = null;
            SessionState.EraseString(TargetKey);
            if (!Enabled) return;

            Scene active = SceneManager.GetActiveScene();
            if (!IsBootableLevel(active)) return;

            // Play mode loads the level from disk through NGO, so unsaved edits would be lost.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorApplication.isPlaying = false;
                return;
            }

            var lobby = AssetDatabase.LoadAssetAtPath<SceneAsset>(LobbyPath);
            if (lobby == null)
            {
                Debug.LogWarning($"[PlayFromAnyScene] {LobbyPath} not found; playing '{active.name}' as-is.");
                return;
            }

            EnsureInBuildList(active.path);
            EditorSceneManager.playModeStartScene = lobby;
            SessionState.SetString(TargetKey, active.path);
            Debug.Log($"[PlayFromAnyScene] Booting through Lobby -> local host -> '{active.name}'. Toggle in {MenuPath}.");
        }

        private static bool IsBootableLevel(Scene scene)
        {
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path) || scene.path == LobbyPath) return false;

            // A scene that brings its own NetworkManager (test rigs) keeps its own startup.
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<NetworkManager>(true) != null) return false;
            return true;
        }

        private static void BeginBoot()
        {
            _target = SessionState.GetString(TargetKey, string.Empty);
            if (string.IsNullOrEmpty(_target)) return;
            _step = Step.WaitManager;
            _deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            EditorApplication.update -= Drive;
            EditorApplication.update += Drive;
        }

        private static void StopDriver()
        {
            EditorApplication.update -= Drive;
            _step = Step.Idle;
        }

        private static void Fail(string message)
        {
            Debug.LogError("[PlayFromAnyScene] " + message);
            StopDriver();
        }

        private static void Drive()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            if (Time.realtimeSinceStartup > _deadline) { Fail($"Timed out at step {_step}."); return; }

            NetworkManager manager = NetworkManager.Singleton;
            switch (_step)
            {
                case Step.WaitManager:
                    // Let Lobby Awake/Start (SteamLobbyManager, UI) finish first, like the dev probe.
                    if (manager == null || Time.frameCount < 3 || manager.ShutdownInProgress) return;
                    if (manager.IsListening) { _step = Step.WaitPlayer; return; }
                    if (!StartHost(manager)) { Fail("Local host failed to start (is port " + Port + " in use?)."); return; }
                    _step = Step.WaitPlayer;
                    return;

                case Step.WaitPlayer:
                    if (manager == null || !manager.IsListening) { Fail("NetworkManager stopped before the level loaded."); return; }
                    if (manager.LocalClient?.PlayerObject == null || manager.SceneManager == null) return;
                    SceneEventProgressStatus status = manager.SceneManager.LoadScene(_target, LoadSceneMode.Single);
                    if (status != SceneEventProgressStatus.Started) { Fail($"NGO could not load '{_target}': {status}."); return; }
                    Debug.Log($"[PlayFromAnyScene] Host ready, loading '{_target}' (clients join 127.0.0.1:{Port}).");
                    _step = Step.Done;
                    StopDriver();
                    return;
            }
        }

        private static bool StartHost(NetworkManager manager)
        {
            SteamLobbyManager lobby = SteamLobbyManager.EnsureInstance();
            lobby.SetTransportType(SteamLobbyManager.TransportType.UnityTransport);
            var transport = manager.GetComponent<UnityTransport>();
            if (transport == null) return false;

            transport.SetConnectionData("127.0.0.1", Port);
            manager.NetworkConfig.NetworkTransport = transport;
            // Same flag the offline HOST ROOM path sets: no Steam retry / invites during dev play.
            typeof(SteamLobbyManager).GetProperty("IsLocalSession", BindingFlags.Instance | BindingFlags.Public)
                ?.GetSetMethod(true)?.Invoke(lobby, new object[] { true });
            return manager.StartHost();
        }
    }
}
