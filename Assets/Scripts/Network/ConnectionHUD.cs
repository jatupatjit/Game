using Netcode.Transports.Facepunch;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// ConnectionHUD provides dual-mode networking UI:
    /// 1. Steam Lobby Mode (Steamworks AppID 480, P2P Relay, Friend Invites)
    /// 2. Direct IP Mode (UnityTransport, Localhost/LAN, Dedicated Server)
    /// </summary>
    [DisallowMultipleComponent]
    public class ConnectionHUD : MonoBehaviour
    {
        [Header("UI Settings")]
        [Tooltip("Position offset from top-left screen corner")]
        [SerializeField] private Vector2 _guiOffset = new Vector2(15f, 15f);

        [Tooltip("Show or hide the connection HUD overlay")]
        [SerializeField] private bool _showHUD = true;

        [Header("Direct IP Settings")]
        [SerializeField] private string _ipAddress = "127.0.0.1";
        [SerializeField] private ushort _port = 7777;

        private string _inputLobbyId = "";
        private int _selectedTab = 0; // 0 = Steam Lobby, 1 = Direct IP (LAN)
        private readonly string[] _tabNames = { "Steam Lobby (AppID 480)", "Direct IP (LAN)" };

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

            // Wider container for dual mode UI
            GUILayout.BeginArea(new Rect(_guiOffset.x, _guiOffset.y, 350f, 290f), GUI.skin.box);
            GUILayout.Label("<b>Co-op Physics Game - Network</b>");

            // State 1: Offline -> Choose mode & start session
            if (!networkManager.IsClient && !networkManager.IsServer)
            {
                _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames, GUILayout.Height(25));
                GUILayout.Space(6);

                if (_selectedTab == 0)
                {
                    DrawSteamLobbyUI(networkManager, steamManager);
                }
                else
                {
                    DrawDirectIpUI(networkManager, steamManager);
                }
            }
            // State 2: Session Active -> Show details and disconnect
            else
            {
                DrawActiveSessionUI(networkManager, steamManager);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("<size=9><color=#888888>• [Esc] Toggle Cursor  |  [Tab/F1] Hide HUD</color></size>");
            GUILayout.EndArea();
        }

        private void DrawSteamLobbyUI(NetworkManager nm, SteamLobbyManager steamManager)
        {
            if (steamManager != null && steamManager.IsSteamInitialized)
            {
                GUILayout.Label($"<color=#70d6ff>Steam:</color> <b>{steamManager.SteamPlayerName}</b>  <size=10>({steamManager.SteamPlayerId})</size>");
            }
            else
            {
                GUILayout.Label("<color=#ff6b6b>Steam not running or offline!</color>");
                GUILayout.Label("<size=10>Open Steam client on PC to use Steam Lobby.</size>");
            }

            GUILayout.Space(4);

            GUI.enabled = steamManager != null && steamManager.IsSteamInitialized;
            if (GUILayout.Button("Host Steam Lobby & Invite Friends", GUILayout.Height(32)))
            {
                steamManager?.HostSteamLobby(4);
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            _inputLobbyId = GUILayout.TextField(_inputLobbyId, GUILayout.Height(24));
            if (GUILayout.Button("Join Lobby ID", GUILayout.Width(100), GUILayout.Height(24)))
            {
                if (ulong.TryParse(_inputLobbyId.Trim(), out ulong targetLobbyId))
                {
                    steamManager?.JoinLobbyById(targetLobbyId);
                }
            }
            GUILayout.EndHorizontal();

            GUI.enabled = true;

            GUILayout.Space(4);
            if (steamManager != null && !string.IsNullOrEmpty(steamManager.StatusMessage))
            {
                GUILayout.Label($"<size=10><i>Status: {steamManager.StatusMessage}</i></size>");
            }
        }

        private void DrawDirectIpUI(NetworkManager nm, SteamLobbyManager steamManager)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("IP:", GUILayout.Width(35));
            _ipAddress = GUILayout.TextField(_ipAddress);
            GUILayout.Label("Port:", GUILayout.Width(35));
            string portStr = GUILayout.TextField(_port.ToString(), GUILayout.Width(50));
            if (ushort.TryParse(portStr, out ushort parsedPort)) _port = parsedPort;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Configure UnityTransport
            void SetupUnityTransport()
            {
                steamManager?.SetTransportType(SteamLobbyManager.TransportType.UnityTransport);
                UnityTransport utp = nm.GetComponent<UnityTransport>();
                if (utp != null)
                {
                    utp.ConnectionData.Address = _ipAddress;
                    utp.ConnectionData.Port = _port;
                    utp.ConnectionData.ServerListenAddress = "0.0.0.0";
                }
            }

            if (GUILayout.Button("Start Host (LAN / Direct IP)", GUILayout.Height(28)))
            {
                SetupUnityTransport();
                nm.StartHost();
            }

            if (GUILayout.Button("Start Client (Connect to IP)", GUILayout.Height(28)))
            {
                SetupUnityTransport();
                nm.StartClient();
            }

            if (GUILayout.Button("Start Dedicated Server", GUILayout.Height(22)))
            {
                SetupUnityTransport();
                nm.StartServer();
            }
        }

        private void DrawActiveSessionUI(NetworkManager nm, SteamLobbyManager steamManager)
        {
            string mode = nm.IsHost ? "Host" : (nm.IsServer ? "Dedicated Server" : "Client");
            string transportName = nm.NetworkConfig.NetworkTransport != null
                ? nm.NetworkConfig.NetworkTransport.GetType().Name
                : "None";

            GUILayout.Label($"<b>Session Mode:</b> {mode} | <b>Transport:</b> {transportName}");

            if (nm.IsHost || nm.IsClient)
            {
                GUILayout.Label($"<b>Local Client ID:</b> {nm.LocalClientId}");
            }
            if (nm.IsServer || nm.IsHost)
            {
                GUILayout.Label($"<b>Connected Players:</b> {nm.ConnectedClientsIds.Count}");
            }

            // If Steam lobby is active, provide Lobby ID & Invite buttons
            if (steamManager != null && steamManager.IsSteamInitialized && steamManager.CurrentLobby.HasValue)
            {
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Copy Lobby ID", GUILayout.Height(26)))
                {
                    steamManager.CopyLobbyIdToClipboard();
                }
                if (GUILayout.Button("Invite Friends (Overlay)", GUILayout.Height(26)))
                {
                    steamManager.OpenSteamInviteOverlay();
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            if (GUILayout.Button("Disconnect & Stop Session", GUILayout.Height(28)))
            {
                steamManager?.LeaveLobby();
                nm.Shutdown();
            }
        }
    }
}

