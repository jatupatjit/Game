using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame.Network
{
    /// <summary>
    /// ConnectionHUD provides the complete in-game & lobby matchmaking UI:
    /// 1. Lobby Menu (Offline):
    ///    - Main: [Host Room] [Setting] [Quit]
    ///    - Host Room Sub-Menu: [Host] [Join] [Back]
    ///    - Join Sub-Menu: [Code Input] [Enter Room] [Paste] [Back]
    ///    - Setting Sub-Menu: Blank screen [Back]
    ///    - Quit: Quits game
    /// 2. In-Game Session (Online):
    ///    - Top-left (above left corner) displays Room Code & connected info
    ///    - Pressing [ESC] pops up Pause Menu with [Continue], [Setting], [Quit]
    /// </summary>
    [DisallowMultipleComponent]
    public class ConnectionHUD : MonoBehaviour
    {
        [Header("UI Settings")]
        [Tooltip("Position offset from top-left screen corner")]
        [SerializeField] private Vector2 _guiOffset = new Vector2(15f, 15f);

        [Tooltip("Show or hide the connection HUD overlay")]
        [SerializeField] private bool _showHUD = true;

        private enum LobbyMenuState
        {
            Main,
            HostRoom,
            Join,
            Settings
        }

        private LobbyMenuState _lobbyState = LobbyMenuState.Main;
        private string _inputRoomCode = "";

        // In-game Pause menu state
        private bool _isPauseMenuOpen = false;
        private bool _pauseInSettings = false;

        private void Awake()
        {
            SteamLobbyManager.EnsureInstance();
        }

        private void Update()
        {
            // Read ESC from New Input System
            bool escPressed = false;
            if (Keyboard.current != null)
            {
                escPressed = Keyboard.current.escapeKey.wasPressedThisFrame;
            }

            if (escPressed)
            {
                NetworkManager nm = NetworkManager.Singleton;
                bool sessionActive = nm != null && (nm.IsClient || nm.IsServer || nm.IsHost);

                if (sessionActive)
                {
                    // In-game pause toggle
                    if (_pauseInSettings)
                    {
                        _pauseInSettings = false;
                    }
                    else if (_isPauseMenuOpen)
                    {
                        _isPauseMenuOpen = false;
                        Cursor.lockState = CursorLockMode.Locked;
                        Cursor.visible = false;
                    }
                    else
                    {
                        _isPauseMenuOpen = true;
                        Cursor.lockState = CursorLockMode.None;
                        Cursor.visible = true;
                    }
                }
                else
                {
                    // Lobby back navigation
                    if (_lobbyState == LobbyMenuState.Join)
                    {
                        _lobbyState = LobbyMenuState.HostRoom;
                    }
                    else if (_lobbyState == LobbyMenuState.HostRoom || _lobbyState == LobbyMenuState.Settings)
                    {
                        _lobbyState = LobbyMenuState.Main;
                    }
                }
            }

            // Tab or F1 to toggle HUD visibility
            if (Keyboard.current != null)
            {
                if (Keyboard.current.tabKey.wasPressedThisFrame ||
                    Keyboard.current.f1Key.wasPressedThisFrame)
                {
                    _showHUD = !_showHUD;
                }
            }
        }

        private void OnGUI()
        {
            if (!_showHUD) return;

            NetworkManager networkManager = NetworkManager.Singleton;
            SteamLobbyManager steamManager = SteamLobbyManager.EnsureInstance();

            if (networkManager == null)
            {
                GUILayout.BeginArea(new Rect(_guiOffset.x, _guiOffset.y, 320f, 70f), GUI.skin.box);
                GUILayout.Label("NetworkManager not found in scene!");
                GUILayout.EndArea();
                return;
            }

            bool sessionActive = networkManager.IsClient || networkManager.IsServer || networkManager.IsHost;

            if (!sessionActive)
            {
                // Draw Lobby Menu with 3 buttons: Host Room - Setting - Quit
                DrawLobbyMenu(steamManager);
            }
            else
            {
                // In-Game: Above left corner shows Room Code
                DrawAboveLeftRoomCodeHUD(networkManager, steamManager);

                // In-Game: ESC Pause Menu
                if (_isPauseMenuOpen)
                {
                    DrawInGamePauseMenu(networkManager, steamManager);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 1. LOBBY MENU (Host Room, Setting, Quit)
        // ─────────────────────────────────────────────────────────────────────

        private void DrawLobbyMenu(SteamLobbyManager steamManager)
        {
            // Centered panel for peak aesthetic
            float panelWidth = 360f;
            float panelHeight = 340f;
            float posX = (Screen.width - panelWidth) * 0.5f;
            float posY = (Screen.height - panelHeight) * 0.5f;

            GUILayout.BeginArea(new Rect(posX, posY, panelWidth, panelHeight), GUI.skin.box);

            // Title
            GUILayout.Space(8);
            GUILayout.Label("<size=18><b><color=#00d9ff>DONT DROP IT</color></b></size>", GetCenteredLabelStyle());
            GUILayout.Space(2);

            // Steam status
            bool steamReady = steamManager != null && steamManager.IsSteamInitialized;
            if (steamReady)
            {
                GUILayout.Label($"<color=#70d6ff>● {steamManager.SteamPlayerName}</color>", GetCenteredLabelStyle());
            }
            else
            {
                int dotCount = ((int)(Time.unscaledTime * 2.5f) % 4);
                string dots = new string('.', dotCount);
                GUILayout.Label($"<color=#ffb703>● Waiting for Steam{dots}</color>", GetCenteredLabelStyle());
            }

            GUILayout.Space(12);

            GUI.enabled = steamReady;

            switch (_lobbyState)
            {
                case LobbyMenuState.Main:
                    DrawMainMenu();
                    break;

                case LobbyMenuState.HostRoom:
                    DrawHostRoomMenu(steamManager);
                    break;

                case LobbyMenuState.Join:
                    DrawJoinMenu(steamManager);
                    break;

                case LobbyMenuState.Settings:
                    DrawSettingsMenu(isPause: false);
                    break;
            }

            GUI.enabled = true;

            // Status message
            if (steamManager != null && !string.IsNullOrEmpty(steamManager.StatusMessage) && steamManager.StatusMessage != "Ready")
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label($"<size=10><i>Status: {steamManager.StatusMessage}</i></size>", GetCenteredLabelStyle());
            }

            GUILayout.EndArea();
        }

        private void DrawMainMenu()
        {
            GUILayout.Label("<size=13><b>MAIN MENU</b></size>", GetCenteredLabelStyle());
            GUILayout.Space(10);

            // 1. Host Room
            if (GUILayout.Button("<b>🎮  Host Room</b>", GUILayout.Height(44)))
            {
                _lobbyState = LobbyMenuState.HostRoom;
            }

            GUILayout.Space(8);

            // 2. Setting
            if (GUILayout.Button("<b>⚙  Setting</b>", GUILayout.Height(44)))
            {
                _lobbyState = LobbyMenuState.Settings;
            }

            GUILayout.Space(8);

            // 3. Quit
            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("<b>✕  Quit Game</b>", GUILayout.Height(44)))
            {
                QuitGame();
            }
            GUI.backgroundColor = Color.white;
        }

        private void DrawHostRoomMenu(SteamLobbyManager steamManager)
        {
            GUILayout.Label("<size=13><b>HOST ROOM MENU</b></size>", GetCenteredLabelStyle());
            GUILayout.Space(10);

            // Button 1: Host (Enters game lobby with room code above left corner)
            GUI.backgroundColor = new Color(0.2f, 0.6f, 1.0f);
            if (GUILayout.Button("<b>▶  Host</b>", GUILayout.Height(44)))
            {
                steamManager?.HostSteamLobby(4);
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(8);

            // Button 2: Join (Opens input for code game)
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.5f);
            if (GUILayout.Button("<b>🔑  Join</b>", GUILayout.Height(44)))
            {
                _lobbyState = LobbyMenuState.Join;
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(8);

            // Back button
            if (GUILayout.Button("<b>← Back</b>", GUILayout.Height(36)))
            {
                _lobbyState = LobbyMenuState.Main;
            }
        }

        private void DrawJoinMenu(SteamLobbyManager steamManager)
        {
            GUILayout.Label("<size=13><b>ENTER ROOM CODE</b></size>", GetCenteredLabelStyle());
            GUILayout.Space(8);

            GUILayout.Label("Code Game:", GetCenteredLabelStyle());
            GUILayout.Space(2);

            string typed = GUILayout.TextField(_inputRoomCode, 8, GUILayout.Height(32));
            if (typed != _inputRoomCode)
            {
                _inputRoomCode = typed.ToUpperInvariant().Trim();
            }

            GUILayout.Space(8);

            GUILayout.BeginHorizontal();

            // Enter Room button
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.5f);
            if (GUILayout.Button("<b>Enter Room</b>", GUILayout.Height(36)))
            {
                if (!string.IsNullOrEmpty(_inputRoomCode))
                {
                    steamManager?.JoinLobbyByCode(_inputRoomCode);
                }
            }
            GUI.backgroundColor = Color.white;

            // Paste button
            if (GUILayout.Button("Paste Code", GUILayout.Width(90), GUILayout.Height(36)))
            {
                string clipboard = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(clipboard))
                {
                    _inputRoomCode = clipboard.Trim().ToUpperInvariant();
                    if (_inputRoomCode.Length > 8) _inputRoomCode = _inputRoomCode.Substring(0, 8);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Back button
            if (GUILayout.Button("<b>← Back</b>", GUILayout.Height(34)))
            {
                _lobbyState = LobbyMenuState.HostRoom;
            }
        }

        private void DrawSettingsMenu(bool isPause)
        {
            GUILayout.Label("<size=14><b>SETTINGS</b></size>", GetCenteredLabelStyle());
            GUILayout.Space(14);

            // Blank screen as requested by user ("Setting is basic so.. we dont have much thing there yet just press and enter to blank screen first")
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Space(24);
            GUILayout.Label("<color=#888888><i>(Settings screen - blank for now)</i></color>", GetCenteredLabelStyle());
            GUILayout.Space(24);
            GUILayout.EndVertical();

            GUILayout.Space(14);

            if (GUILayout.Button("<b>← Back</b>", GUILayout.Height(38)))
            {
                if (isPause)
                {
                    _pauseInSettings = false;
                }
                else
                {
                    _lobbyState = LobbyMenuState.Main;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 2. IN-GAME ROOM CODE (Above Left Corner)
        // ─────────────────────────────────────────────────────────────────────

        private void DrawAboveLeftRoomCodeHUD(NetworkManager nm, SteamLobbyManager steamManager)
        {
            string mode = nm.IsHost ? "HOST" : (nm.IsServer ? "SERVER" : "CLIENT");
            string roomCode = (steamManager != null && !string.IsNullOrEmpty(steamManager.CurrentRoomCode))
                ? steamManager.CurrentRoomCode
                : "------";

            float hudWidth = 240f;
            float hudHeight = 110f;
            GUILayout.BeginArea(new Rect(_guiOffset.x, _guiOffset.y, hudWidth, hudHeight), GUI.skin.box);

            GUILayout.Label($"<color=#70d6ff><b>MODE: {mode}</b></color>");
            GUILayout.Label($"<b>ROOM CODE:</b> <size=18><b><color=#00d9ff>{roomCode}</color></b></size>");

            if (nm.IsHost || nm.IsServer)
            {
                GUILayout.Label($"Players: {nm.ConnectedClientsIds.Count}");
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("📋 Copy", GUILayout.Height(22)))
            {
                steamManager?.CopyRoomCodeToClipboard();
            }
            if (GUILayout.Button("Invite", GUILayout.Height(22)))
            {
                steamManager?.OpenSteamInviteOverlay();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        // ─────────────────────────────────────────────────────────────────────
        // 3. IN-GAME ESC PAUSE MENU (Continue, Setting, Quit)
        // ─────────────────────────────────────────────────────────────────────

        private void DrawInGamePauseMenu(NetworkManager nm, SteamLobbyManager steamManager)
        {
            float pauseWidth = 320f;
            float pauseHeight = 280f;
            float posX = (Screen.width - pauseWidth) * 0.5f;
            float posY = (Screen.height - pauseHeight) * 0.5f;

            // Semi-transparent overlay feel using modal box
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");

            GUILayout.BeginArea(new Rect(posX, posY, pauseWidth, pauseHeight), GUI.skin.box);

            if (_pauseInSettings)
            {
                DrawSettingsMenu(isPause: true);
            }
            else
            {
                GUILayout.Space(10);
                GUILayout.Label("<size=18><b><color=#00d9ff>PAUSED</color></b></size>", GetCenteredLabelStyle());
                GUILayout.Space(16);

                // 1. Continue
                GUI.backgroundColor = new Color(0.2f, 0.8f, 0.5f);
                if (GUILayout.Button("<b>▶  Continue</b>", GUILayout.Height(44)))
                {
                    _isPauseMenuOpen = false;
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                GUI.backgroundColor = Color.white;

                GUILayout.Space(8);

                // 2. Setting
                if (GUILayout.Button("<b>⚙  Setting</b>", GUILayout.Height(44)))
                {
                    _pauseInSettings = true;
                }

                GUILayout.Space(8);

                // 3. Quit
                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("<b>✕  Quit</b>", GUILayout.Height(44)))
                {
                    _isPauseMenuOpen = false;
                    steamManager?.DisconnectAndReturnToLobby();
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.EndArea();
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private GUIStyle GetCenteredLabelStyle()
        {
            var style = new GUIStyle(GUI.skin.label);
            style.alignment = TextAnchor.MiddleCenter;
            return style;
        }
    }
}
