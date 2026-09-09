using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// ConnectionHUD provides a streamlined Room Code matchmaking UI for Steam:
    /// - Host creates a Steam Lobby with an auto-generated 5-6 character Room Code (e.g. "K7M2X9").
    /// - Clients join instantly simply by entering the 5-6 character Room Code.
    /// - Includes One-Click Copy Room Code and Steam Friends Overlay Invites.
    /// - LAN / Direct IP mode has been removed as requested.
    /// </summary>
    [DisallowMultipleComponent]
    public class ConnectionHUD : MonoBehaviour
    {
        [Header("UI Settings")]
        [Tooltip("Position offset from top-left screen corner")]
        [SerializeField] private Vector2 _guiOffset = new Vector2(15f, 15f);

        [Tooltip("Show or hide the connection HUD overlay")]
        [SerializeField] private bool _showHUD = true;

        private string _inputRoomCode = "";

        private void Awake()
        {
            // Ensure SteamLobbyManager exists in scene
            if (FindFirstObjectByType<SteamLobbyManager>() == null)
            {
                gameObject.AddComponent<SteamLobbyManager>();
            }
        }

        private void Update()
        {
            // Press Tab or F1 to toggle HUD visibility
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                if (UnityEngine.InputSystem.Keyboard.current.tabKey.wasPressedThisFrame ||
                    UnityEngine.InputSystem.Keyboard.current.f1Key.wasPressedThisFrame)
                {
                    _showHUD = !_showHUD;
                }
            }
        }

        private void OnGUI()
        {
            if (!_showHUD) return;

            NetworkManager networkManager = NetworkManager.Singleton;
            SteamLobbyManager steamManager = SteamLobbyManager.Instance;

            if (networkManager == null)
            {
                GUILayout.BeginArea(new Rect(_guiOffset.x, _guiOffset.y, 320f, 70f), GUI.skin.box);
                GUILayout.Label("NetworkManager not found in scene!");
                GUILayout.EndArea();
                return;
            }

            // Clean, compact container for Steam Room Code networking
            GUILayout.BeginArea(new Rect(_guiOffset.x, _guiOffset.y, 330f, 240f), GUI.skin.box);
            GUILayout.Label("<b>Co-op Multiplayer (Steam)</b>");

            // State 1: Offline -> Host Room or Join by Room Code
            if (!networkManager.IsClient && !networkManager.IsServer)
            {
                DrawOfflineLobbyUI(networkManager, steamManager);
            }
            // State 2: Session Active -> Show Room Code, Connected Players, and Disconnect
            else
            {
                DrawActiveSessionUI(networkManager, steamManager);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("<size=9><color=#888888>• [Esc] Toggle Cursor  |  [Tab/F1] Hide HUD</color></size>");
            GUILayout.EndArea();
        }

        private void DrawOfflineLobbyUI(NetworkManager nm, SteamLobbyManager steamManager)
        {
            if (steamManager != null && steamManager.IsSteamInitialized)
            {
                GUILayout.Label($"<color=#70d6ff>Steam:</color> <b>{steamManager.SteamPlayerName}</b>");
            }
            else
            {
                GUILayout.Label("<color=#ff6b6b>Steam not running or offline!</color>");
                GUILayout.Label("<size=10>Open Steam client on PC to play Co-op.</size>");
            }

            GUILayout.Space(6);

            GUI.enabled = steamManager != null && steamManager.IsSteamInitialized;

            // 1. Host Room Button (Generates 5-6 char Room Code)
            if (GUILayout.Button("<b>Host Room (Create Code)</b>", GUILayout.Height(32)))
            {
                steamManager?.HostSteamLobby(4);
            }

            GUILayout.Space(6);

            // 2. Join by 5-6 Char Room Code
            GUILayout.Label("Join Room by Code:");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Code:", GUILayout.Width(42));
            
            // Automatically format input to uppercase and clean whitespace
            string typed = GUILayout.TextField(_inputRoomCode, 8, GUILayout.Height(26));
            if (typed != _inputRoomCode)
            {
                _inputRoomCode = typed.ToUpperInvariant().Trim();
            }

            if (GUILayout.Button("Join Room", GUILayout.Width(90), GUILayout.Height(26)))
            {
                if (!string.IsNullOrEmpty(_inputRoomCode))
                {
                    steamManager?.JoinLobbyByCode(_inputRoomCode);
                }
            }
            GUILayout.EndHorizontal();

            GUI.enabled = true;

            GUILayout.Space(6);
            if (steamManager != null && !string.IsNullOrEmpty(steamManager.StatusMessage))
            {
                GUILayout.Label($"<size=10><i>Status: {steamManager.StatusMessage}</i></size>");
            }
        }

        private void DrawActiveSessionUI(NetworkManager nm, SteamLobbyManager steamManager)
        {
            string mode = nm.IsHost ? "Host" : (nm.IsServer ? "Server" : "Client");
            string roomCode = (steamManager != null && !string.IsNullOrEmpty(steamManager.CurrentRoomCode))
                ? steamManager.CurrentRoomCode
                : "------";

            GUILayout.Label($"<b>Session Mode:</b> {mode}");
            GUILayout.Space(2);

            // Display Room Code prominently
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Room Code:</b>", GUILayout.Width(85));
            GUILayout.Label($"<size=14><b><color=#70d6ff>{roomCode}</color></b></size>");
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            if (steamManager != null && steamManager.CurrentLobby.HasValue)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Copy Code", GUILayout.Height(26)))
                {
                    steamManager.CopyRoomCodeToClipboard();
                }
                if (GUILayout.Button("Invite Friends", GUILayout.Height(26)))
                {
                    steamManager.OpenSteamInviteOverlay();
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(2);
            if (nm.IsServer || nm.IsHost)
            {
                GUILayout.Label($"<b>Connected Players:</b> {nm.ConnectedClientsIds.Count}");
            }

            if (steamManager != null && !string.IsNullOrEmpty(steamManager.StatusMessage))
            {
                GUILayout.Label($"<size=10><i>Status: {steamManager.StatusMessage}</i></size>");
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Leave Room & Disconnect", GUILayout.Height(28)))
            {
                steamManager?.LeaveLobby();
                nm.Shutdown();
            }
        }
    }
}

