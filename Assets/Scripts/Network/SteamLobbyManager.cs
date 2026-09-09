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
        public const string RoomCodeKey = "RoomCode";
        private const string HostAddressKey = "HostAddress";

        private static readonly char[] CodeCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

        /// <summary>
        /// Generates a clean, readable 5-6 character alphanumeric Room Code (excluding ambiguous 0/O, 1/I).
        /// </summary>
        public static string GenerateRoomCode(int length = 6)
        {
            var random = new System.Random();
            char[] result = new char[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = CodeCharacters[random.Next(CodeCharacters.Length)];
            }
            return new string(result);
        }

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
        public string CurrentRoomCode { get; private set; } = "";
        public string StatusMessage { get; private set; } = "Ready";

        // Cached Transports
        private FacepunchTransport _facepunchTransport;
        private UnityTransport _unityTransport;

        public static SteamLobbyManager EnsureInstance()
        {
            if (Instance != null) return Instance;

            var existing = FindFirstObjectByType<SteamLobbyManager>();
            if (existing != null)
            {
                Instance = existing;
                return Instance;
            }

            GameObject go = new GameObject("[SteamLobbyManager]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SteamLobbyManager>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Destroy only this duplicate component or object, never destroy the NetworkManager
                if (transform.parent == null && gameObject.name == "[SteamLobbyManager]")
                {
                    Destroy(gameObject);
                }
                else
                {
                    Destroy(this);
                }
                return;
            }

            Instance = this;
            if (transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }

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
            if (Instance == this)
            {
                Instance = null;
            }
            // CRITICAL: NEVER call SteamClient.Shutdown() here!
            // SteamClient should stay alive across scene loads and lobby resets.
        }

        private void OnApplicationQuit()
        {
            UnregisterSteamCallbacks();
            if (IsSteamInitialized)
            {
                Debug.Log("[SteamLobbyManager] Application quitting, shutting down SteamClient.");
                SteamClient.Shutdown();
            }
        }

        /// <summary>
        /// Attempts to initialize Steamworks or refresh connection status.
        /// Safe to call multiple times without crashing or disconnecting.
        /// </summary>
        public void InitializeSteam(bool force = false)
        {
            if (SteamClient.IsValid && !force)
            {
                StatusMessage = $"Steam Connected: {SteamClient.Name} ({SteamClient.SteamId})";
                return;
            }

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

        /// <summary>
        /// Public method for UI to refresh Steam connection if Steam was opened after the game.
        /// </summary>
        public void RefreshSteam()
        {
            InitializeSteam(force: true);
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

            // Generate 6-character room code and register in Steam Lobby metadata
            CurrentRoomCode = GenerateRoomCode(6);
            lobby.SetData(RoomCodeKey, CurrentRoomCode);
            lobby.SetData(HostAddressKey, SteamClient.SteamId.ToString());
            lobby.SetData("name", $"{SteamClient.Name}'s Lobby [{CurrentRoomCode}]");

            StatusMessage = $"Room Created! Code: {CurrentRoomCode}";
            Debug.Log($"[SteamLobbyManager] Room created successfully. RoomCode: {CurrentRoomCode} (LobbyId: {lobby.Id})");
        }

        private void OnLobbyEntered(Lobby lobby)
        {
            CurrentLobby = lobby;
            string code = lobby.GetData(RoomCodeKey);
            if (!string.IsNullOrEmpty(code))
            {
                CurrentRoomCode = code;
            }
            StatusMessage = $"Entered Room: {CurrentRoomCode} ({lobby.MemberCount} players)";
            Debug.Log($"[SteamLobbyManager] Entered lobby {lobby.Id} with RoomCode: {CurrentRoomCode}. Member count: {lobby.MemberCount}");

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
        /// Copies current short Room Code (or Steam Lobby ID fallback) to clipboard.
        /// </summary>
        public void CopyRoomCodeToClipboard()
        {
            if (!string.IsNullOrEmpty(CurrentRoomCode))
            {
                GUIUtility.systemCopyBuffer = CurrentRoomCode;
                StatusMessage = $"Room Code '{CurrentRoomCode}' copied to clipboard!";
                Debug.Log($"[SteamLobbyManager] Copied Room Code '{CurrentRoomCode}' to clipboard.");
            }
            else if (CurrentLobby.HasValue)
            {
                GUIUtility.systemCopyBuffer = CurrentLobby.Value.Id.ToString();
                StatusMessage = $"Lobby ID copied to clipboard: {CurrentLobby.Value.Id}";
            }
        }

        /// <summary>
        /// Legacy method for backward compatibility.
        /// </summary>
        public void CopyLobbyIdToClipboard() => CopyRoomCodeToClipboard();

        /// <summary>
        /// Joins a Steam lobby by its 5-6 character Room Code.
        /// Queries active Steam lobbies worldwide matching the RoomCode metadata.
        /// </summary>
        public async void JoinLobbyByCode(string roomCode)
        {
            if (string.IsNullOrWhiteSpace(roomCode))
            {
                StatusMessage = "Please enter a valid Room Code!";
                return;
            }

            string cleanCode = roomCode.Trim().ToUpperInvariant();

            if (!IsSteamInitialized)
            {
                StatusMessage = "Steam is not running or offline!";
                return;
            }

            StatusMessage = $"Searching for Room: {cleanCode}...";
            Debug.Log($"[SteamLobbyManager] Searching Steam for RoomCode: '{cleanCode}'");

            try
            {
                Lobby[] lobbies = await SteamMatchmaking.LobbyList
                    .FilterDistanceWorldwide()
                    .WithKeyValue(RoomCodeKey, cleanCode)
                    .WithSlotsAvailable(1)
                    .WithMaxResults(25)
                    .RequestAsync();

                if (lobbies != null && lobbies.Length > 0)
                {
                    Lobby targetLobby = lobbies[0];
                    StatusMessage = $"Connecting to Room {cleanCode}...";
                    Debug.Log($"[SteamLobbyManager] Found matching Lobby ID: {targetLobby.Id} for RoomCode: {cleanCode}");

                    RoomEnter result = await targetLobby.Join();
                    if (result != RoomEnter.Success)
                    {
                        StatusMessage = $"Failed to join room: {result}";
                        Debug.LogError($"[SteamLobbyManager] Failed to join lobby {targetLobby.Id}: {result}");
                    }
                    return;
                }

                // Fallback: If player typed raw 64-bit Steam Lobby ID, support it directly
                if (ulong.TryParse(cleanCode, out ulong rawLobbyId))
                {
                    JoinLobbyById(rawLobbyId);
                    return;
                }

                StatusMessage = $"Room '{cleanCode}' not found! Check code and try again.";
                Debug.LogWarning($"[SteamLobbyManager] No active lobby found matching RoomCode: {cleanCode}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Search error: {ex.Message}";
                Debug.LogError($"[SteamLobbyManager] Exception during JoinLobbyByCode: {ex}");
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
        /// Regenerates a fresh 5-6 char Room Code for the active lobby (Host only).
        /// </summary>
        public void RegenerateRoomCode()
        {
            if (!CurrentLobby.HasValue) return;

            CurrentRoomCode = GenerateRoomCode(6);
            CurrentLobby.Value.SetData(RoomCodeKey, CurrentRoomCode);
            CurrentLobby.Value.SetData("name", $"{SteamClient.Name}'s Lobby [{CurrentRoomCode}]");
            StatusMessage = $"New Room Code: {CurrentRoomCode}";
            Debug.Log($"[SteamLobbyManager] Regenerated Room Code: {CurrentRoomCode}");
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
            CurrentRoomCode = "";
            StatusMessage = "Ready";
        }

        /// <summary>
        /// Completely and cleanly disconnects from current Netcode session and Steam lobby,
        /// restores cursor and scene camera, and reloads the active scene fresh to prevent any
        /// frozen screen, missing objects, or stale network state.
        /// </summary>
        public void DisconnectAndReturnToLobby()
        {
            Debug.Log("[SteamLobbyManager] Disconnecting and returning to clean Lobby state...");

            // 1. Restore Cursor immediately
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // 2. Re-enable scene cameras so there is never a blank/hung screen
            Camera[] allCams = Resources.FindObjectsOfTypeAll<Camera>();
            foreach (Camera cam in allCams)
            {
                if (cam != null && cam.gameObject.name.Contains("Main Camera"))
                {
                    cam.gameObject.SetActive(true);
                }
            }

            // 3. Leave Steam lobby
            LeaveLobby();

            // 4. Shut down Netcode session
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer))
            {
                nm.Shutdown();
            }

            // 5. Reload scene to reset all physical GameObjects, colliders, and NGO state cleanly
            string activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!string.IsNullOrEmpty(activeSceneName))
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(activeSceneName);
            }
        }

        #endregion
    }
}
