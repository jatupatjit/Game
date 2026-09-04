using System;
using System.Threading.Tasks;
using Netcode.Transports.Facepunch;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// SteamLobbyManager manages Steamworks initialization, Steam Lobby creation,
    /// matchmaking callbacks, overlay invites, and dual transport switching between
    /// Steam (FacepunchTransport) and Direct IP (UnityTransport).
    /// </summary>
    [DisallowMultipleComponent]
    public class SteamLobbyManager : MonoBehaviour
    {
        public static SteamLobbyManager Instance { get; private set; }

        public const uint AppId = 480; // Spacewar - Valve Public Free Test AppID
        private const string HostAddressKey = "HostAddress";

        [Header("Transport Settings")]
        [Tooltip("Active transport type for current network session")]
        [SerializeField] private TransportType _currentTransportType = TransportType.Steam;

        public enum TransportType
        {
            Steam,
            UnityTransport
        }

        public TransportType CurrentTransportType => _currentTransportType;
        public bool IsSteamInitialized => SteamClient.IsValid;
        public string SteamPlayerName => IsSteamInitialized ? SteamClient.Name : "Offline / No Steam";
        public ulong SteamPlayerId => IsSteamInitialized ? SteamClient.SteamId.Value : 0;
        public Lobby? CurrentLobby { get; private set; }
        public string StatusMessage { get; private set; } = "Ready";

        // Cached Transports
        private FacepunchTransport _facepunchTransport;
        private UnityTransport _unityTransport;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeSteam();
            EnsureTransports();
        }

        private void Start()
        {
            RegisterSteamCallbacks();
        }

        private void Update()
        {
            // Pump Steamworks callbacks every frame if initialized
            if (IsSteamInitialized)
            {
                SteamClient.RunCallbacks();
            }
        }

        private void OnDestroy()
        {
            UnregisterSteamCallbacks();
            if (IsSteamInitialized)
            {
                SteamClient.Shutdown();
            }
        }

        private void InitializeSteam()
        {
            if (IsSteamInitialized) return;

            try
            {
                // Set asyncCallbacks to false so Steam callbacks run synchronously on Unity's main thread
                SteamClient.Init(AppId, false);
                if (SteamClient.IsValid)
                {
                    StatusMessage = $"Steam Connected: {SteamClient.Name} ({SteamClient.SteamId})";
                    Debug.Log($"[SteamLobbyManager] Steamworks initialized successfully! Logged in as: {SteamClient.Name}");
                }
                else
                {
                    StatusMessage = "Steam Client is not running.";
                    Debug.LogWarning("[SteamLobbyManager] SteamClient is not valid. Make sure Steam is running on your machine.");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Steam init error: {ex.Message}";
                Debug.LogWarning($"[SteamLobbyManager] Failed to initialize Steamworks: {ex.Message}");
            }
        }

        private void EnsureTransports()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null)
            {
                nm = FindFirstObjectByType<NetworkManager>();
            }

            if (nm == null)
            {
                Debug.LogWarning("[SteamLobbyManager] NetworkManager not found in scene yet.");
                return;
            }

            // Find or Add FacepunchTransport
            _facepunchTransport = nm.GetComponent<FacepunchTransport>();
            if (_facepunchTransport == null)
            {
                _facepunchTransport = nm.gameObject.AddComponent<FacepunchTransport>();
            }

            // Find or Add UnityTransport
            _unityTransport = nm.GetComponent<UnityTransport>();
            if (_unityTransport == null)
            {
                _unityTransport = nm.gameObject.AddComponent<UnityTransport>();
            }
        }

        public void SetTransportType(TransportType transportType)
        {
            _currentTransportType = transportType;
            EnsureTransports();

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null) return;

            if (_currentTransportType == TransportType.Steam)
            {
                if (_facepunchTransport != null)
                {
                    nm.NetworkConfig.NetworkTransport = _facepunchTransport;
                    Debug.Log("[SteamLobbyManager] Active transport set to: FacepunchTransport (Steam Relay)");
                }
            }
            else
            {
                if (_unityTransport != null)
                {
                    nm.NetworkConfig.NetworkTransport = _unityTransport;
                    Debug.Log("[SteamLobbyManager] Active transport set to: UnityTransport (Direct IP/LAN)");
                }
            }
        }

        #region Steam Matchmaking & Invite Callbacks

        private void RegisterSteamCallbacks()
        {
            SteamMatchmaking.OnLobbyCreated += OnLobbyCreated;
            SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
        }

        private void UnregisterSteamCallbacks()
        {
            SteamMatchmaking.OnLobbyCreated -= OnLobbyCreated;
            SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
            SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested;
        }

        private void OnLobbyCreated(Result result, Lobby lobby)
        {
            if (result != Result.OK)
            {
                StatusMessage = $"Failed to create Steam Lobby: {result}";
                Debug.LogError($"[SteamLobbyManager] Lobby creation failed: {result}");
                return;
            }

            CurrentLobby = lobby;
            lobby.SetPublic();
            lobby.SetJoinable(true);
            lobby.SetData(HostAddressKey, SteamClient.SteamId.ToString());
            lobby.SetData("name", $"{SteamClient.Name}'s Co-op Physics Lobby");

            StatusMessage = $"Lobby Created! ID: {lobby.Id}";
            Debug.Log($"[SteamLobbyManager] Lobby created successfully. LobbyId: {lobby.Id}");
        }

        private void OnLobbyEntered(Lobby lobby)
        {
            CurrentLobby = lobby;
            StatusMessage = $"Entered Lobby: {lobby.Id} ({lobby.MemberCount} players)";
            Debug.Log($"[SteamLobbyManager] Entered lobby {lobby.Id}. Member count: {lobby.MemberCount}");

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null) return;

            // If we are the Host, we already started Host upon creating lobby
            if (nm.IsHost || nm.IsServer) return;

            // If we are a joining client:
            string hostSteamIdStr = lobby.GetData(HostAddressKey);
            if (ulong.TryParse(hostSteamIdStr, out ulong hostSteamId))
            {
                Debug.Log($"[SteamLobbyManager] Connecting to Host SteamID: {hostSteamId}");
                SetTransportType(TransportType.Steam);
                if (_facepunchTransport != null)
                {
                    _facepunchTransport.targetSteamId = hostSteamId;
                }
                nm.StartClient();
            }
            else
            {
                StatusMessage = "Error: Lobby HostAddress is invalid!";
                Debug.LogError("[SteamLobbyManager] Failed to read HostAddress from Steam Lobby data.");
            }
        }

        private async void OnGameLobbyJoinRequested(Lobby lobby, SteamId friendSteamId)
        {
            Debug.Log($"[SteamLobbyManager] Received join request from friend: {friendSteamId} to Lobby: {lobby.Id}");
            StatusMessage = $"Joining Lobby from invite: {lobby.Id}...";

            // If already in a network session, shut it down first
            if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            {
                NetworkManager.Singleton.Shutdown();
                await Task.Delay(200);
            }

            RoomEnter result = await lobby.Join();
            if (result != RoomEnter.Success)
            {
                StatusMessage = $"Failed to join lobby: {result}";
                Debug.LogError($"[SteamLobbyManager] Failed to join lobby from invite: {result}");
            }
        }

        #endregion

        #region Public Actions

        /// <summary>
        /// Creates a Steam Lobby and starts hosting Netcode session.
        /// </summary>
        public async void HostSteamLobby(int maxMembers = 4)
        {
            if (!IsSteamInitialized)
            {
                StatusMessage = "Cannot host Steam lobby: Steam is not running!";
                Debug.LogError("[SteamLobbyManager] Steam is not initialized.");
                return;
            }

            SetTransportType(TransportType.Steam);

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null) return;

            StatusMessage = "Creating Steam Lobby...";
            Lobby? lobby = await SteamMatchmaking.CreateLobbyAsync(maxMembers);
            if (lobby.HasValue)
            {
                nm.StartHost();
            }
            else
            {
                StatusMessage = "Failed to create Steam Lobby async.";
            }
        }

        /// <summary>
        /// Opens Steam overlay invite dialog so host or player can invite friends directly.
        /// </summary>
        public void OpenSteamInviteOverlay()
        {
            if (!IsSteamInitialized)
            {
                Debug.LogWarning("[SteamLobbyManager] Steam is not running.");
                return;
            }

            if (CurrentLobby.HasValue)
            {
                SteamFriends.OpenGameInviteOverlay(CurrentLobby.Value.Id);
            }
            else
            {
                // If not in a lobby, open regular friends overlay
                SteamFriends.OpenOverlay("friends");
            }
        }

        /// <summary>
        /// Copies current Steam Lobby ID to system clipboard for easy sharing.
        /// </summary>
        public void CopyLobbyIdToClipboard()
        {
            if (CurrentLobby.HasValue)
            {
                GUIUtility.systemCopyBuffer = CurrentLobby.Value.Id.ToString();
                StatusMessage = $"Lobby ID copied to clipboard: {CurrentLobby.Value.Id}";
                Debug.Log($"[SteamLobbyManager] Copied Lobby ID {CurrentLobby.Value.Id} to clipboard.");
            }
        }

        /// <summary>
        /// Joins a Steam lobby directly by its 64-bit ID.
        /// </summary>
        public async void JoinLobbyById(ulong lobbyId)
        {
            if (!IsSteamInitialized)
            {
                StatusMessage = "Steam is not running!";
                return;
            }

            StatusMessage = $"Connecting to Lobby: {lobbyId}...";
            Lobby lobby = new Lobby(lobbyId);
            RoomEnter result = await lobby.Join();
            if (result != RoomEnter.Success)
            {
                StatusMessage = $"Failed to join lobby: {result}";
                Debug.LogError($"[SteamLobbyManager] Failed to join lobby {lobbyId}: {result}");
            }
        }

        /// <summary>
        /// Leaves current Steam lobby and resets state.
        /// </summary>
        public void LeaveLobby()
        {
            if (CurrentLobby.HasValue)
            {
                CurrentLobby.Value.Leave();
                CurrentLobby = null;
            }
            StatusMessage = "Left lobby";
        }

        #endregion
    }
}
