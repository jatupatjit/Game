using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// LobbyUI - Full-screen lobby menu with:
    /// - Main Menu: Host Room, Setting, Quit
    /// - Host Room Menu: Host, Join, Back
    /// - Join Menu: Code Input, Enter Room, Paste, Back
    /// - Setting Menu: Blank screen, Back
    /// Auto-hides when a network session becomes active.
    ///
    /// Setup: Right-click the LobbyUI component → "Build Lobby UI (Editor Only)"
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyUI : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private CanvasGroup _lobbyRoot;

        [Header("Main Menu Panel (Host Room / Setting / Quit)")]
        [SerializeField] private GameObject _mainPanel;
        [SerializeField] private Button     _hostRoomMenuButton;
        [SerializeField] private Button     _settingsMenuButton;
        [SerializeField] private Button     _quitGameButton;
        [SerializeField] private Text       _steamStatusLabel;
        [SerializeField] private Text       _statusMessageLabel;

        [Header("Host Room Panel (Host / Join / Back)")]
        [SerializeField] private GameObject _hostRoomPanel;
        [SerializeField] private Button     _hostButton;
        [SerializeField] private Button     _joinButton;
        [SerializeField] private Button     _backFromHostRoomButton;

        [Header("Join Code Panel (Input / Enter / Paste / Back)")]
        [SerializeField] private GameObject _joinPanel;
        [SerializeField] private InputField _roomCodeInput;
        [SerializeField] private Button     _confirmJoinButton;
        [SerializeField] private Button     _pasteCodeButton;
        [SerializeField] private Button     _backFromJoinButton;

        [Header("Settings Panel (Blank / Back)")]
        [SerializeField] private GameObject _settingsPanel;
        [SerializeField] private Button     _backFromSettingsButton;

        [Header("Connecting Panel")]
        [SerializeField] private GameObject _connectingPanel;
        [SerializeField] private Text       _connectingLabel;

        [Header("Settings")]
        [SerializeField] private float _fadeSpeed            = 3.5f;
        [SerializeField] private float _statusUpdateInterval = 0.5f;

        private enum MenuState
        {
            Main,
            HostRoom,
            Join,
            Settings,
            Connecting
        }

        private MenuState _currentState    = MenuState.Main;
        private float     _statusTimer     = 0f;
        private bool      _fadingOut       = false;

        // ───────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            SteamLobbyManager.EnsureInstance();
        }

        private void Start()
        {
            // Main Panel buttons
            if (_hostRoomMenuButton    != null) _hostRoomMenuButton.onClick.AddListener(OnHostRoomMenuClicked);
            if (_settingsMenuButton    != null) _settingsMenuButton.onClick.AddListener(OnSettingsMenuClicked);
            if (_quitGameButton        != null) _quitGameButton.onClick.AddListener(OnQuitGameClicked);

            // Host Room Sub-Panel buttons
            if (_hostButton            != null) _hostButton.onClick.AddListener(OnHostClicked);
            if (_joinButton            != null) _joinButton.onClick.AddListener(OnJoinMenuClicked);
            if (_backFromHostRoomButton!= null) _backFromHostRoomButton.onClick.AddListener(OnBackToMainMenu);

            // Join Sub-Panel buttons
            if (_confirmJoinButton     != null) _confirmJoinButton.onClick.AddListener(OnConfirmJoin);
            if (_pasteCodeButton       != null) _pasteCodeButton.onClick.AddListener(OnPasteCode);
            if (_backFromJoinButton    != null) _backFromJoinButton.onClick.AddListener(OnBackToHostRoom);

            // Settings Sub-Panel buttons
            if (_backFromSettingsButton!= null) _backFromSettingsButton.onClick.AddListener(OnBackToMainMenu);

            // Room code input setup
            if (_roomCodeInput != null)
            {
                _roomCodeInput.onValueChanged.AddListener(OnCodeInputChanged);
                _roomCodeInput.characterLimit = 8;
            }

            ShowPanel(MenuState.Main);
            ShowLobby(instant: true);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        private void Update()
        {
            // Auto-hide when session becomes active
            if (!_fadingOut && IsSessionActive())
            {
                _fadingOut = true;
                StartCoroutine(FadeOutAndHide());
                return;
            }

            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f)
            {
                _statusTimer = _statusUpdateInterval;
                UpdateStatusLabels();
            }

            // Animate connecting dots
            if (_currentState == MenuState.Connecting && _connectingLabel != null)
            {
                int dots = (int)(Time.unscaledTime * 2.5f) % 4;
                string status = (SteamLobbyManager.Instance != null && !string.IsNullOrEmpty(SteamLobbyManager.Instance.StatusMessage))
                    ? SteamLobbyManager.Instance.StatusMessage : "Connecting";
                _connectingLabel.text = status + new string('.', dots);
            }

            // ESC key back navigation
            bool escPressed = false;
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                escPressed = UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
            }

            if (escPressed)
            {
                if (_currentState == MenuState.Join)
                {
                    OnBackToHostRoom();
                }
                else if (_currentState == MenuState.HostRoom || _currentState == MenuState.Settings)
                {
                    OnBackToMainMenu();
                }
            }
        }

        // ──────────────────────────────────────────── Navigation Handlers ──────

        private void OnHostRoomMenuClicked() => ShowPanel(MenuState.HostRoom);
        private void OnSettingsMenuClicked() => ShowPanel(MenuState.Settings);
        private void OnJoinMenuClicked()     => ShowPanel(MenuState.Join);
        private void OnBackToMainMenu()      => ShowPanel(MenuState.Main);
        private void OnBackToHostRoom()      => ShowPanel(MenuState.HostRoom);

        private void OnQuitGameClicked()
        {
            Debug.Log("[LobbyUI] Quit game requested.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ──────────────────────────────────────────── Steam Host / Join ────────

        private void OnHostClicked()
        {
            var mgr = SteamLobbyManager.Instance;
            if (mgr == null || !mgr.IsSteamInitialized)
            {
                SetStatus("Steam is not running! Please launch Steam first.", isError: true);
                return;
            }
            ShowPanel(MenuState.Connecting);
            if (_connectingLabel != null) _connectingLabel.text = "Creating room...";
            mgr.HostSteamLobby(4);
        }

        private void OnConfirmJoin()
        {
            var mgr = SteamLobbyManager.Instance;
            if (mgr == null || !mgr.IsSteamInitialized)
            {
                SetStatus("Steam is not running!", isError: true);
                return;
            }

            string code = _roomCodeInput != null
                ? _roomCodeInput.text.Trim().ToUpperInvariant() : "";

            if (string.IsNullOrEmpty(code))
            {
                SetStatus("Please enter a Room Code!", isError: true);
                return;
            }

            ShowPanel(MenuState.Connecting);
            if (_connectingLabel != null) _connectingLabel.text = "Joining Room: " + code;
            mgr.JoinLobbyByCode(code);
        }

        private void OnPasteCode()
        {
            if (_roomCodeInput == null) return;
            string clipboard = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(clipboard))
            {
                string cleaned = clipboard.Trim().ToUpperInvariant();
                if (cleaned.Length > 8) cleaned = cleaned.Substring(0, 8);
                _roomCodeInput.text = cleaned;
            }
        }

        private void OnCodeInputChanged(string value)
        {
            if (_roomCodeInput == null) return;
            string upper = value.ToUpperInvariant();
            if (upper != value) _roomCodeInput.text = upper;
        }

        // ──────────────────────────────────────────── Panel Switching ──────────

        private void ShowPanel(MenuState state)
        {
            _currentState = state;

            SetActive(_mainPanel,        state == MenuState.Main);
            SetActive(_hostRoomPanel,    state == MenuState.HostRoom);
            SetActive(_joinPanel,        state == MenuState.Join);
            SetActive(_settingsPanel,    state == MenuState.Settings);
            SetActive(_connectingPanel,  state == MenuState.Connecting);

            if (state == MenuState.Join && _roomCodeInput != null)
            {
                _roomCodeInput.text = "";
                _roomCodeInput.ActivateInputField();
            }

            SetStatus("", false);
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null) go.SetActive(active);
        }

        // ──────────────────────────────────────────── Status Labels ────────────

        private void UpdateStatusLabels()
        {
            var mgr = SteamLobbyManager.Instance;
            if (_steamStatusLabel == null) return;

            bool steamReady = mgr != null && mgr.IsSteamInitialized;

            if (steamReady)
            {
                _steamStatusLabel.text  = "● " + mgr.SteamPlayerName;
                _steamStatusLabel.color = new Color(0f, 0.85f, 1f);
                if (_hostRoomMenuButton != null) _hostRoomMenuButton.interactable = true;
                if (_hostButton         != null) _hostButton.interactable         = true;
                if (_joinButton         != null) _joinButton.interactable         = true;
                if (_confirmJoinButton  != null) _confirmJoinButton.interactable  = true;
            }
            else
            {
                int dots = (int)(Time.unscaledTime * 2.5f) % 4;
                _steamStatusLabel.text  = "● Waiting for Steam" + new string('.', dots);
                _steamStatusLabel.color = new Color(1f, 0.72f, 0f);
                if (_hostRoomMenuButton != null) _hostRoomMenuButton.interactable = false;
                if (_hostButton         != null) _hostButton.interactable         = false;
                if (_joinButton         != null) _joinButton.interactable         = false;
                if (_confirmJoinButton  != null) _confirmJoinButton.interactable  = false;
            }

            if (_statusMessageLabel != null && mgr != null &&
                !string.IsNullOrEmpty(mgr.StatusMessage) && mgr.StatusMessage != "Ready")
            {
                _statusMessageLabel.text = mgr.StatusMessage;
            }
        }

        private void SetStatus(string msg, bool isError)
        {
            if (_statusMessageLabel == null) return;
            _statusMessageLabel.text  = msg;
            _statusMessageLabel.color = isError
                ? new Color(1f, 0.4f, 0.4f)
                : new Color(0.6f, 0.6f, 0.6f);
        }

        // ──────────────────────────────────────────── Visibility ───────────────

        private bool IsSessionActive()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && (nm.IsClient || nm.IsServer || nm.IsHost);
        }

        public void ShowLobby(bool instant = false)
        {
            if (_lobbyRoot == null) return;
            _fadingOut = false;
            gameObject.SetActive(true);

            if (instant)
            {
                _lobbyRoot.alpha          = 1f;
                _lobbyRoot.interactable   = true;
                _lobbyRoot.blocksRaycasts = true;
            }
            else
            {
                StopAllCoroutines();
                StartCoroutine(FadeIn());
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        public void HideLobby(bool instant = false)
        {
            if (_lobbyRoot == null) return;
            if (instant)
            {
                _lobbyRoot.alpha          = 0f;
                _lobbyRoot.interactable   = false;
                _lobbyRoot.blocksRaycasts = false;
                gameObject.SetActive(false);
            }
            else
            {
                StopAllCoroutines();
                StartCoroutine(FadeOutAndHide());
            }
        }

        private IEnumerator FadeIn()
        {
            if (_lobbyRoot == null) yield break;
            _lobbyRoot.alpha          = 0f;
            _lobbyRoot.interactable   = false;
            _lobbyRoot.blocksRaycasts = false;

            while (_lobbyRoot.alpha < 0.99f)
            {
                _lobbyRoot.alpha = Mathf.MoveTowards(
                    _lobbyRoot.alpha, 1f, Time.unscaledDeltaTime * _fadeSpeed);
                yield return null;
            }
            _lobbyRoot.alpha          = 1f;
            _lobbyRoot.interactable   = true;
            _lobbyRoot.blocksRaycasts = true;
        }

        private IEnumerator FadeOutAndHide()
        {
            if (_lobbyRoot == null) yield break;
            _lobbyRoot.interactable   = false;
            _lobbyRoot.blocksRaycasts = false;

            while (_lobbyRoot.alpha > 0.01f)
            {
                _lobbyRoot.alpha = Mathf.MoveTowards(
                    _lobbyRoot.alpha, 0f, Time.unscaledDeltaTime * _fadeSpeed);
                yield return null;
            }
            _lobbyRoot.alpha = 0f;
            gameObject.SetActive(false);
        }

#if UNITY_EDITOR
        [ContextMenu("Build Lobby UI (Editor Only)")]
        private void BuildLobbyUIInEditor() => LobbyUIBuilder.Build(this);
#endif
    }
}
