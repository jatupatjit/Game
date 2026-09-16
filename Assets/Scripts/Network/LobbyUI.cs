using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// LobbyUI - Full-screen lobby menu.
    /// Self-builds at runtime if editor build was not done (_lobbyRoot == null).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public class LobbyUI : MonoBehaviour
    {
        [Header("Canvas & Root")]
        [SerializeField] private Canvas      _canvas;
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
        [SerializeField] private Button     _cancelConnectingButton;

        [Header("Settings")]
        [SerializeField] private float _fadeSpeed            = 3.5f;
        [SerializeField] private float _statusUpdateInterval = 0.5f;

        private enum MenuState { Main, HostRoom, Join, Settings, Connecting }

        private MenuState _currentState = MenuState.Main;
        private float     _statusTimer  = 0f;
        private bool      _fadingOut    = false;

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            SteamLobbyManager.EnsureInstance();

            if (_canvas == null) _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();

            if (_lobbyRoot == null)
                BuildRuntime();
        }

        private void Start()
        {
            if (_hostRoomMenuButton     != null) _hostRoomMenuButton.onClick.AddListener(OnHostRoomMenuClicked);
            if (_settingsMenuButton     != null) _settingsMenuButton.onClick.AddListener(OnSettingsMenuClicked);
            if (_quitGameButton         != null) _quitGameButton.onClick.AddListener(OnQuitGameClicked);
            if (_hostButton             != null) _hostButton.onClick.AddListener(OnHostClicked);
            if (_joinButton             != null) _joinButton.onClick.AddListener(OnJoinMenuClicked);
            if (_backFromHostRoomButton != null) _backFromHostRoomButton.onClick.AddListener(OnBackToMainMenu);
            if (_confirmJoinButton      != null) _confirmJoinButton.onClick.AddListener(OnConfirmJoin);
            if (_pasteCodeButton        != null) _pasteCodeButton.onClick.AddListener(OnPasteCode);
            if (_backFromJoinButton     != null) _backFromJoinButton.onClick.AddListener(OnBackToHostRoom);
            if (_backFromSettingsButton != null) _backFromSettingsButton.onClick.AddListener(OnBackToMainMenu);
            if (_cancelConnectingButton != null) _cancelConnectingButton.onClick.AddListener(OnCancelConnecting);

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

            if (_currentState == MenuState.Connecting && _connectingLabel != null)
            {
                int dots = (int)(Time.unscaledTime * 2.5f) % 4;
                string status = (SteamLobbyManager.Instance != null && !string.IsNullOrEmpty(SteamLobbyManager.Instance.StatusMessage))
                    ? SteamLobbyManager.Instance.StatusMessage : "Connecting";
                _connectingLabel.text = status + new string('.', dots);
            }

            bool escPressed = UnityEngine.InputSystem.Keyboard.current != null &&
                              UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
            if (escPressed)
            {
                if (_currentState == MenuState.Join)
                    OnBackToHostRoom();
                else if (_currentState == MenuState.Connecting)
                    OnCancelConnecting();
                else if (_currentState == MenuState.HostRoom || _currentState == MenuState.Settings)
                    OnBackToMainMenu();
            }
        }

        // ─────────────────────────────────────────── Navigation ───────────────

        private void OnHostRoomMenuClicked() => ShowPanel(MenuState.HostRoom);
        private void OnSettingsMenuClicked() => ShowPanel(MenuState.Settings);
        private void OnJoinMenuClicked()     => ShowPanel(MenuState.Join);
        private void OnBackToMainMenu()      => ShowPanel(MenuState.Main);
        private void OnBackToHostRoom()      => ShowPanel(MenuState.HostRoom);
        private void OnCancelConnecting()
        {
            var mgr = SteamLobbyManager.Instance;
            if (mgr != null)
            {
                mgr.LeaveLobby();
            }
            ShowPanel(MenuState.Main);
        }

        private void OnQuitGameClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ─────────────────────────────────────────── Host / Join ──────────────

        private void OnHostClicked()
        {
            var mgr = SteamLobbyManager.Instance;
            if (mgr == null || !mgr.IsSteamInitialized)
            {
                SetStatus("Steam is not running! Please launch Steam first.", true);
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
                SetStatus("Steam is not running!", true);
                return;
            }
            string code = _roomCodeInput != null ? _roomCodeInput.text.Trim().ToUpperInvariant() : "";
            if (string.IsNullOrEmpty(code)) { SetStatus("Please enter a Room Code!", true); return; }
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

        // ─────────────────────────────────────────── Panel Switching ──────────

        private void ShowPanel(MenuState state)
        {
            _currentState = state;
            SetActive(_mainPanel,       state == MenuState.Main);
            SetActive(_hostRoomPanel,   state == MenuState.HostRoom);
            SetActive(_joinPanel,       state == MenuState.Join);
            SetActive(_settingsPanel,   state == MenuState.Settings);
            SetActive(_connectingPanel, state == MenuState.Connecting);

            if (state == MenuState.Join && _roomCodeInput != null)
            {
                _roomCodeInput.text = "";
                _roomCodeInput.ActivateInputField();
            }
            SetStatus("", false);
        }

        private static void SetActive(GameObject go, bool active) { if (go != null) go.SetActive(active); }

        // ─────────────────────────────────────────── Status Labels ────────────

        private void UpdateStatusLabels()
        {
            var mgr = SteamLobbyManager.Instance;
            bool steamReady = mgr != null && mgr.IsSteamInitialized;

            if (_hostRoomMenuButton != null) _hostRoomMenuButton.interactable = steamReady;
            if (_hostButton         != null) _hostButton.interactable         = steamReady;
            if (_joinButton         != null) _joinButton.interactable         = steamReady;
            if (_confirmJoinButton  != null) _confirmJoinButton.interactable  = steamReady;

            if (_steamStatusLabel != null)
            {
                _steamStatusLabel.gameObject.SetActive(false); // Kept completely hidden as requested
            }

            if (_statusMessageLabel != null)
            {
                if (mgr != null && !string.IsNullOrEmpty(mgr.StatusMessage) && mgr.StatusMessage != "Ready" && !mgr.StatusMessage.StartsWith("Steam Connected"))
                {
                    _statusMessageLabel.text = mgr.StatusMessage;
                    _statusMessageLabel.gameObject.SetActive(true);
                }
                else
                {
                    _statusMessageLabel.text = "";
                    _statusMessageLabel.gameObject.SetActive(false);
                }
            }
        }

        private void SetStatus(string msg, bool isError)
        {
            if (_statusMessageLabel == null) return;
            _statusMessageLabel.text  = msg;
            _statusMessageLabel.color = isError ? new Color(1f, 0.4f, 0.4f) : new Color(0.6f, 0.6f, 0.6f);
        }

        // ─────────────────────────────────────────── Visibility ───────────────

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
                _lobbyRoot.alpha = 1f; _lobbyRoot.interactable = true; _lobbyRoot.blocksRaycasts = true;
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
                _lobbyRoot.alpha = 0f; _lobbyRoot.interactable = false; _lobbyRoot.blocksRaycasts = false;
                gameObject.SetActive(false);
            }
            else { StopAllCoroutines(); StartCoroutine(FadeOutAndHide()); }
        }

        private IEnumerator FadeIn()
        {
            if (_lobbyRoot == null) yield break;
            _lobbyRoot.alpha = 0f; _lobbyRoot.interactable = false; _lobbyRoot.blocksRaycasts = false;
            while (_lobbyRoot.alpha < 0.99f)
            {
                _lobbyRoot.alpha = Mathf.MoveTowards(_lobbyRoot.alpha, 1f, Time.unscaledDeltaTime * _fadeSpeed);
                yield return null;
            }
            _lobbyRoot.alpha = 1f; _lobbyRoot.interactable = true; _lobbyRoot.blocksRaycasts = true;
        }

        private IEnumerator FadeOutAndHide()
        {
            if (_lobbyRoot == null) yield break;
            _lobbyRoot.interactable = false; _lobbyRoot.blocksRaycasts = false;
            while (_lobbyRoot.alpha > 0.01f)
            {
                _lobbyRoot.alpha = Mathf.MoveTowards(_lobbyRoot.alpha, 0f, Time.unscaledDeltaTime * _fadeSpeed);
                yield return null;
            }
            _lobbyRoot.alpha = 0f;
            gameObject.SetActive(false);
        }

        // ─────────────────────────────────── Runtime Self-Builder ─────────────
        // Creates the entire UI hierarchy procedurally at runtime when
        // the serialized references are missing (e.g. scene data was wiped).

        private void BuildRuntime()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            // Canvas / Scaler / Raycaster
            if (_canvas == null) _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight  = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            _lobbyRoot = GetComponent<CanvasGroup>();
            if (_lobbyRoot == null) _lobbyRoot = gameObject.AddComponent<CanvasGroup>();

            // Palette
            Color bgDeep        = ProceduralUIUtility.BgDeep;
            Color panelColor    = ProceduralUIUtility.PanelDark;
            Color accentCyan    = ProceduralUIUtility.AccentCyan;
            Color btnPlay       = ProceduralUIUtility.ButtonPlay;
            Color btnJoin       = ProceduralUIUtility.ButtonJoin;
            Color btnSettings   = ProceduralUIUtility.ButtonSlate;
            Color btnQuit       = ProceduralUIUtility.ButtonQuit;
            Color btnBack       = ProceduralUIUtility.ButtonSlate;
            Color textPrimary   = ProceduralUIUtility.TextPrimary;
            Color textSecondary = ProceduralUIUtility.TextSecondary;

            // Background
            var bg = ProceduralUIUtility.MakeImage(gameObject, "Background", bgDeep);
            ProceduralUIUtility.StretchFull(bg.GetComponent<RectTransform>());

            // Center Card (460 x 520)
            var card   = ProceduralUIUtility.MakePanel(gameObject, "LobbyCard", new Vector2(460, 520), Vector2.zero, panelColor);
            var cardRT = card.GetComponent<RectTransform>();
            cardRT.anchorMin = cardRT.anchorMax = new Vector2(0.5f, 0.5f);
            cardRT.pivot     = new Vector2(0.5f, 0.5f);
            cardRT.anchoredPosition = Vector2.zero;
            var cvl = card.AddComponent<VerticalLayoutGroup>();
            cvl.childAlignment = TextAnchor.UpperCenter;
            cvl.padding        = new RectOffset(36, 36, 32, 32);
            cvl.spacing        = 14;
            cvl.childControlWidth    = true;
            cvl.childControlHeight   = false;
            cvl.childForceExpandWidth = true;

            // Header: Game Title & Subtitle
            LE(ProceduralUIUtility.MakeText(card, "TitleLabel", "DONT DROP IT", 32, FontStyle.Bold, textPrimary, TextAnchor.MiddleCenter).gameObject, 40f);
            LE(ProceduralUIUtility.MakeText(card, "SubTitleLabel", "CO-OP PHYSICS ADVENTURE", 11, FontStyle.Bold, accentCyan, TextAnchor.MiddleCenter).gameObject, 16f);

            _steamStatusLabel   = null;
            _statusMessageLabel = ProceduralUIUtility.MakeText(card, "StatusMessageLabel", "", 12, FontStyle.Italic, ProceduralUIUtility.HexColor("#FCA5A5"), TextAnchor.MiddleCenter);
            LE(_statusMessageLabel.gameObject, 18f);

            // Main panel
            _mainPanel = ProceduralUIUtility.MakeEmpty(card, "MainMenuPanel");
            var mainLE = _mainPanel.AddComponent<LayoutElement>();
            mainLE.preferredHeight = 360;
            mainLE.flexibleHeight = 1f;
            var mvl = _mainPanel.AddComponent<VerticalLayoutGroup>();
            mvl.padding = new RectOffset(0, 0, 0, 0);
            mvl.spacing = 22;
            mvl.childControlWidth = true;
            mvl.childControlHeight = true;
            mvl.childForceExpandWidth = true;
            mvl.childForceExpandHeight = false;
            mvl.childAlignment = TextAnchor.MiddleCenter;

            var (_, pBtn) = ProceduralUIUtility.MakeButton(_mainPanel, "HostRoomMenuButton", "▶   PLAY", btnPlay, 58, 16);
            _hostRoomMenuButton = pBtn;
            var (_, sBtn) = ProceduralUIUtility.MakeButton(_mainPanel, "SettingsMenuButton", "⚙   SETTINGS", btnSettings, 52, 15);
            _settingsMenuButton = sBtn;

            // Controlled breathing spacer before quit button
            var mSpacer = ProceduralUIUtility.MakeEmpty(_mainPanel, "MainSpacer");
            var mSpLE = mSpacer.AddComponent<LayoutElement>();
            mSpLE.preferredHeight = 32;
            mSpLE.flexibleHeight = 0;

            var (_, qBtn) = ProceduralUIUtility.MakeButton(_mainPanel, "QuitGameButton", "✕   QUIT GAME", btnQuit, 48, 14);
            _quitGameButton = qBtn;

            // Host room panel
            _hostRoomPanel = ProceduralUIUtility.MakeEmpty(card, "HostRoomSubPanel");
            var hrLE = _hostRoomPanel.AddComponent<LayoutElement>();
            hrLE.preferredHeight = 360;
            hrLE.flexibleHeight = 1f;
            var hvl = _hostRoomPanel.AddComponent<VerticalLayoutGroup>();
            hvl.padding = new RectOffset(0, 0, 0, 0);
            hvl.spacing = 18;
            hvl.childControlWidth = true;
            hvl.childControlHeight = true;
            hvl.childForceExpandWidth = true;
            hvl.childForceExpandHeight = false;
            hvl.childAlignment = TextAnchor.MiddleCenter;

            var subT1 = ProceduralUIUtility.MakeText(_hostRoomPanel, "SubTitle", "SELECT PLAY MODE", 13, FontStyle.Bold, accentCyan, TextAnchor.MiddleCenter);
            var subT1LE = subT1.gameObject.AddComponent<LayoutElement>();
            subT1LE.preferredHeight = 26;
            subT1LE.flexibleHeight = 0;

            var (_, hBtn) = ProceduralUIUtility.MakeButton(_hostRoomPanel, "HostButton", "👑   HOST ROOM", btnPlay, 58, 16);
            _hostButton = hBtn;
            var (_, jBtn) = ProceduralUIUtility.MakeButton(_hostRoomPanel, "JoinButton", "🔑   JOIN WITH CODE", btnJoin, 54, 15);
            _joinButton = jBtn;

            // Controlled breathing spacer before Back button
            var hrSpacer = ProceduralUIUtility.MakeEmpty(_hostRoomPanel, "HostSpacer");
            var hrSpLE = hrSpacer.AddComponent<LayoutElement>();
            hrSpLE.preferredHeight = 28;
            hrSpLE.flexibleHeight = 0;

            var (_, bBtn) = ProceduralUIUtility.MakeButton(_hostRoomPanel, "BackFromHostRoomButton", "←   BACK", btnBack, 48, 14);
            _backFromHostRoomButton = bBtn;
            _hostRoomPanel.SetActive(false);

            // Join panel
            _joinPanel = ProceduralUIUtility.MakeEmpty(card, "JoinSubPanel");
            var jnLE = _joinPanel.AddComponent<LayoutElement>();
            jnLE.preferredHeight = 360;
            jnLE.flexibleHeight = 1f;
            var jvl = _joinPanel.AddComponent<VerticalLayoutGroup>();
            jvl.padding = new RectOffset(0, 0, 0, 0);
            jvl.spacing = 18;
            jvl.childControlWidth = true;
            jvl.childControlHeight = true;
            jvl.childForceExpandWidth = true;
            jvl.childForceExpandHeight = false;
            jvl.childAlignment = TextAnchor.MiddleCenter;

            var jTitle = ProceduralUIUtility.MakeText(_joinPanel, "JoinTitle", "ENTER 6-DIGIT ROOM CODE", 13, FontStyle.Bold, accentCyan, TextAnchor.MiddleCenter);
            var jTitleLE = jTitle.gameObject.AddComponent<LayoutElement>();
            jTitleLE.preferredHeight = 26;
            jTitleLE.flexibleHeight = 0;

            var inputRow = ProceduralUIUtility.MakeEmpty(_joinPanel, "InputRow");
            var inRowLE = inputRow.AddComponent<LayoutElement>();
            inRowLE.preferredHeight = 54;
            inRowLE.flexibleHeight = 0;
            var inHL = inputRow.AddComponent<HorizontalLayoutGroup>();
            inHL.spacing = 8; inHL.childControlWidth = false; inHL.childControlHeight = true; inHL.childForceExpandWidth = false; inHL.childForceExpandHeight = true;

            var (inGO, cInp) = ProceduralUIUtility.MakeInputField(inputRow, "RoomCodeInput", "ROOM CODE", 54);
            _roomCodeInput = cInp;
            var inRT = inGO.GetComponent<RectTransform>();
            inRT.sizeDelta = new Vector2(270, 54);
            var inLE = inGO.AddComponent<LayoutElement>();
            inLE.preferredWidth = 270; inLE.preferredHeight = 54;

            var (pstGO, pstBtn) = ProceduralUIUtility.MakeButton(inputRow, "PasteCodeButton", "📋 Paste", btnSettings, 54, 13);
            _pasteCodeButton = pstBtn;
            var pstRT = pstGO.GetComponent<RectTransform>();
            pstRT.sizeDelta = new Vector2(110, 54);
            var pstLE = pstGO.GetComponent<LayoutElement>();
            if (pstLE != null) { pstLE.preferredWidth = 110; pstLE.preferredHeight = 54; }

            var (_, cnfBtn) = ProceduralUIUtility.MakeButton(_joinPanel, "ConfirmJoinButton", "✓   JOIN ROOM", btnJoin, 54, 15);
            _confirmJoinButton = cnfBtn;
            var cnfLE = cnfBtn.GetComponent<LayoutElement>();
            if (cnfLE != null) { cnfLE.preferredHeight = 54; cnfLE.flexibleHeight = 0; }

            // Controlled breathing spacer before Back button
            var jnSpacer = ProceduralUIUtility.MakeEmpty(_joinPanel, "JoinSpacer");
            var jnSpLE = jnSpacer.AddComponent<LayoutElement>();
            jnSpLE.preferredHeight = 28;
            jnSpLE.flexibleHeight = 0;

            var (_, bckBtn) = ProceduralUIUtility.MakeButton(_joinPanel, "BackFromJoinButton", "←   BACK", btnBack, 48, 14);
            _backFromJoinButton = bckBtn;
            var bckLE = bckBtn.GetComponent<LayoutElement>();
            if (bckLE != null) { bckLE.preferredHeight = 48; bckLE.flexibleHeight = 0; }
            _joinPanel.SetActive(false);

            // Settings panel
            _settingsPanel = ProceduralUIUtility.MakeEmpty(card, "SettingsSubPanel");
            var stLE = _settingsPanel.AddComponent<LayoutElement>();
            stLE.preferredHeight = 360;
            stLE.flexibleHeight = 1f;
            var svl = _settingsPanel.AddComponent<VerticalLayoutGroup>();
            svl.padding = new RectOffset(0, 0, 0, 0);
            svl.spacing = 18;
            svl.childControlWidth = true;
            svl.childControlHeight = true;
            svl.childForceExpandWidth = true;
            svl.childForceExpandHeight = false;
            svl.childAlignment = TextAnchor.MiddleCenter;

            var stTitle = ProceduralUIUtility.MakeText(_settingsPanel, "SettingsTitle", "SETTINGS", 18, FontStyle.Bold, accentCyan, TextAnchor.MiddleCenter);
            var stTitleLE = stTitle.gameObject.AddComponent<LayoutElement>();
            stTitleLE.preferredHeight = 28;
            stTitleLE.flexibleHeight = 0;

            var blank = ProceduralUIUtility.MakeEmpty(_settingsPanel, "BlankArea");
            var blankLE = blank.AddComponent<LayoutElement>();
            blankLE.preferredHeight = 110;
            blankLE.flexibleHeight = 0;
            ProceduralUIUtility.StretchFull(ProceduralUIUtility.MakeText(blank, "BlankNote", "Game audio and control settings will be configured here.", 13, FontStyle.Italic, textSecondary, TextAnchor.MiddleCenter).GetComponent<RectTransform>());

            // Controlled breathing spacer before Back button
            var stSpacer = ProceduralUIUtility.MakeEmpty(_settingsPanel, "SettingsSpacer");
            var stSpLE = stSpacer.AddComponent<LayoutElement>();
            stSpLE.preferredHeight = 28;
            stSpLE.flexibleHeight = 0;

            var (_, bStgBtn) = ProceduralUIUtility.MakeButton(_settingsPanel, "BackFromSettingsButton", "←   BACK", btnBack, 48, 14);
            _backFromSettingsButton = bStgBtn;
            var bStgLE = bStgBtn.GetComponent<LayoutElement>();
            if (bStgLE != null) { bStgLE.preferredHeight = 48; bStgLE.flexibleHeight = 0; }
            _settingsPanel.SetActive(false);

            // Connecting panel
            _connectingPanel = ProceduralUIUtility.MakeEmpty(card, "ConnectingPanel");
            var connPanelLE = _connectingPanel.AddComponent<LayoutElement>();
            connPanelLE.preferredHeight = 360;
            connPanelLE.flexibleHeight = 1f;

            var connVL = _connectingPanel.AddComponent<VerticalLayoutGroup>();
            connVL.padding = new RectOffset(0, 0, 0, 0);
            connVL.spacing = 16;
            connVL.childControlWidth = true;
            connVL.childControlHeight = true;
            connVL.childForceExpandWidth = true;
            connVL.childForceExpandHeight = false;
            connVL.childAlignment = TextAnchor.MiddleCenter;

            // Centered Spinner container
            var spinnerContainer = ProceduralUIUtility.MakeEmpty(_connectingPanel, "SpinnerContainer");
            var scLE = spinnerContainer.AddComponent<LayoutElement>();
            scLE.preferredHeight = 76;
            scLE.minHeight = 76;
            scLE.flexibleHeight = 0;

            var sp = ProceduralUIUtility.MakeImage(spinnerContainer, "SpinnerRing", accentCyan, ProceduralUIUtility.GetSpinnerRingSprite());
            var spRT = sp.GetComponent<RectTransform>();
            spRT.anchorMin = new Vector2(0.5f, 0.5f);
            spRT.anchorMax = new Vector2(0.5f, 0.5f);
            spRT.pivot = new Vector2(0.5f, 0.5f);
            spRT.sizeDelta = new Vector2(58, 58);
            spRT.anchoredPosition = Vector2.zero;
            sp.gameObject.AddComponent<SpinnerAnimator>();

            // Title
            var titleText = ProceduralUIUtility.MakeText(_connectingPanel, "ConnectingTitle", "ENTERING GAME...", 17, FontStyle.Bold, textPrimary, TextAnchor.MiddleCenter);
            var titleLE = titleText.gameObject.AddComponent<LayoutElement>();
            titleLE.preferredHeight = 28;
            titleLE.flexibleHeight = 0;

            // Dynamic status label
            _connectingLabel = ProceduralUIUtility.MakeText(_connectingPanel, "ConnectingLabel", "Creating room...", 15, FontStyle.Normal, accentCyan, TextAnchor.MiddleCenter);
            var connLblLE = _connectingLabel.gameObject.AddComponent<LayoutElement>();
            connLblLE.preferredHeight = 24;
            connLblLE.flexibleHeight = 0;

            // Subtitle
            var subText = ProceduralUIUtility.MakeText(_connectingPanel, "ConnectingSub", "Initializing Steam relay network and starting host server", 12, FontStyle.Italic, textSecondary, TextAnchor.MiddleCenter);
            var subTextLE = subText.gameObject.AddComponent<LayoutElement>();
            subTextLE.preferredHeight = 22;
            subTextLE.flexibleHeight = 0;

            // Controlled breathing spacer before cancel button
            var bottomSpacer = ProceduralUIUtility.MakeEmpty(_connectingPanel, "BottomSpacer");
            var btmSpacerLE = bottomSpacer.AddComponent<LayoutElement>();
            btmSpacerLE.preferredHeight = 24;
            btmSpacerLE.flexibleHeight = 0;

            // Cancel button
            var (_, cancelBtn) = ProceduralUIUtility.MakeButton(_connectingPanel, "CancelConnectingButton", "←   CANCEL", btnBack, 48, 14);
            _cancelConnectingButton = cancelBtn;
            var cancelLE = cancelBtn.GetComponent<LayoutElement>();
            if (cancelLE != null) { cancelLE.preferredHeight = 48; cancelLE.flexibleHeight = 0; }

            _connectingPanel.SetActive(false);

            Debug.Log("[LobbyUI] Runtime self-build complete.");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out Color c); return c; }

        private static Font BFont()
        {
            Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return f != null ? f : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static Image Img(GameObject p, string n, Color c)
        {
            var go = new GameObject(n); go.transform.SetParent(p.transform, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>(); img.color = c; return img;
        }

        private static GameObject Empty(GameObject p, string n)
        {
            var go = new GameObject(n); go.transform.SetParent(p.transform, false);
            go.AddComponent<RectTransform>(); return go;
        }

        private static GameObject Panel(GameObject p, string n, Vector2 sz, Color c)
        {
            var go = new GameObject(n); go.transform.SetParent(p.transform, false);
            go.AddComponent<RectTransform>().sizeDelta = sz;
            go.AddComponent<Image>().color = c; return go;
        }

        private static Text Txt(GameObject p, string n, string t, int sz, FontStyle fs, Color c, TextAnchor al)
        {
            var go = new GameObject(n); go.transform.SetParent(p.transform, false);
            go.AddComponent<RectTransform>().sizeDelta = new Vector2(400, 30);
            var tx = go.AddComponent<Text>();
            tx.font = BFont(); tx.text = t; tx.fontSize = sz; tx.fontStyle = fs;
            tx.color = c; tx.alignment = al;
            tx.horizontalOverflow = HorizontalWrapMode.Wrap;
            tx.verticalOverflow   = VerticalWrapMode.Overflow;
            return tx;
        }

        private static Button Btn(GameObject p, string n, string lbl, Color bg, float h)
        {
            var go = new GameObject(n); go.transform.SetParent(p.transform, false);
            go.AddComponent<RectTransform>().sizeDelta = new Vector2(0, h);
            go.AddComponent<LayoutElement>().preferredHeight = h;
            var img = go.AddComponent<Image>(); img.color = bg;
            var btn = go.AddComponent<Button>();
            var col = btn.colors;
            col.normalColor = bg; col.highlightedColor = bg * 1.3f;
            col.pressedColor = bg * 0.75f; col.selectedColor = bg; col.fadeDuration = 0.08f;
            btn.colors = col; btn.targetGraphic = img;
            Stretch(Txt(go,"Label",lbl,15,FontStyle.Bold,Color.white,TextAnchor.MiddleCenter).GetComponent<RectTransform>());
            return btn;
        }

        private static InputField InputF(GameObject p, string n, string ph, float h)
        {
            var go = new GameObject(n); go.transform.SetParent(p.transform, false);
            go.AddComponent<RectTransform>().sizeDelta = new Vector2(0, h);
            go.AddComponent<LayoutElement>().preferredHeight = h;
            var bgImg = go.AddComponent<Image>(); bgImg.color = H("#1F2937");
            var inf = go.AddComponent<InputField>(); inf.caretWidth = 2; inf.characterLimit = 8;

            var tgo = Txt(go,"Text","",18,FontStyle.Bold,Color.white,TextAnchor.MiddleCenter);
            var trt = tgo.GetComponent<RectTransform>();
            Stretch(trt); trt.offsetMin = new Vector2(10,0); trt.offsetMax = new Vector2(-10,0);

            var pgo = Txt(go,"Placeholder",ph,14,FontStyle.Italic,H("#9CA3AF"),TextAnchor.MiddleCenter);
            var prt = pgo.GetComponent<RectTransform>();
            Stretch(prt); prt.offsetMin = new Vector2(10,0); prt.offsetMax = new Vector2(-10,0);

            inf.textComponent = tgo; inf.placeholder = pgo; inf.targetGraphic = bgImg;
            return inf;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void VL(GameObject go, float spacing)
        {
            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.spacing = spacing; vl.childControlWidth = true; vl.childControlHeight = false;
            vl.childForceExpandWidth = true; vl.childAlignment = TextAnchor.UpperCenter;
        }

        private static void LE(GameObject go, float height)
        {
            go.AddComponent<LayoutElement>().preferredHeight = height;
        }

#if UNITY_EDITOR
        [ContextMenu("Build Lobby UI (Editor Only)")]
        private void BuildLobbyUIInEditor() => LobbyUIBuilder.Build(this);
#endif
    }
}
