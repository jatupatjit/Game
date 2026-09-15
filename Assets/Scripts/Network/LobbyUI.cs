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
            if (_hostRoomMenuButton    != null) _hostRoomMenuButton.onClick.AddListener(OnHostRoomMenuClicked);
            if (_settingsMenuButton    != null) _settingsMenuButton.onClick.AddListener(OnSettingsMenuClicked);
            if (_quitGameButton        != null) _quitGameButton.onClick.AddListener(OnQuitGameClicked);
            if (_hostButton            != null) _hostButton.onClick.AddListener(OnHostClicked);
            if (_joinButton            != null) _joinButton.onClick.AddListener(OnJoinMenuClicked);
            if (_backFromHostRoomButton != null) _backFromHostRoomButton.onClick.AddListener(OnBackToMainMenu);
            if (_confirmJoinButton     != null) _confirmJoinButton.onClick.AddListener(OnConfirmJoin);
            if (_pasteCodeButton       != null) _pasteCodeButton.onClick.AddListener(OnPasteCode);
            if (_backFromJoinButton    != null) _backFromJoinButton.onClick.AddListener(OnBackToHostRoom);
            if (_backFromSettingsButton != null) _backFromSettingsButton.onClick.AddListener(OnBackToMainMenu);

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
            Color bgDeep        = H("#0A0E1A");
            Color panelColor    = H("#111827EE");
            Color accentCyan    = H("#00D9FF");
            Color btnHost       = H("#1D4ED8");
            Color btnJoin       = H("#059669");
            Color btnSettings   = H("#4B5563");
            Color btnQuit       = H("#991B1B");
            Color btnBack       = H("#374151");
            Color btnCopy       = H("#1F2937");
            Color textPrimary   = H("#F9FAFB");
            Color textSecondary = H("#9CA3AF");

            // Background
            var bg = Img(gameObject, "Background", bgDeep);
            Stretch(bg.GetComponent<RectTransform>());

            // Center Card
            var card   = Panel(gameObject, "LobbyCard", new Vector2(500, 580), panelColor);
            var cardRT = card.GetComponent<RectTransform>();
            cardRT.anchorMin = cardRT.anchorMax = new Vector2(0.5f, 0.5f);
            cardRT.pivot     = new Vector2(0.5f, 0.5f);
            cardRT.anchoredPosition = Vector2.zero;
            var cvl = card.AddComponent<VerticalLayoutGroup>();
            cvl.childAlignment = TextAnchor.UpperCenter;
            cvl.padding        = new RectOffset(40, 40, 36, 36);
            cvl.spacing        = 14;
            cvl.childControlWidth    = true;
            cvl.childControlHeight   = false;
            cvl.childForceExpandWidth = true;

            // Header
            LE(Txt(card,"TitleLabel","DONT DROP IT",34,FontStyle.Bold,textPrimary,TextAnchor.MiddleCenter).gameObject, 46f);
            LE(Img(card, "AccentLine", accentCyan).gameObject, 2f);

            _steamStatusLabel   = Txt(card,"SteamStatusLabel","● Checking Steam...",13,FontStyle.Normal,textSecondary,TextAnchor.MiddleCenter);
            LE(_steamStatusLabel.gameObject, 24f);

            _statusMessageLabel = Txt(card,"StatusMessageLabel","",12,FontStyle.Italic,textSecondary,TextAnchor.MiddleCenter);
            LE(_statusMessageLabel.gameObject, 22f);

            // Main panel
            _mainPanel = Empty(card, "MainMenuPanel");
            VL(_mainPanel, 14);
            _hostRoomMenuButton = Btn(_mainPanel,"HostRoomMenuButton","HOST ROOM",btnHost,   50);
            _settingsMenuButton = Btn(_mainPanel,"SettingsMenuButton", "SETTINGS", btnSettings,50);
            _quitGameButton     = Btn(_mainPanel,"QuitGameButton",     "QUIT GAME",btnQuit,   50);

            // Host room panel
            _hostRoomPanel = Empty(card, "HostRoomSubPanel");
            VL(_hostRoomPanel, 14);
            LE(Txt(_hostRoomPanel,"SubTitle","CREATE OR JOIN",14,FontStyle.Bold,accentCyan,TextAnchor.MiddleCenter).gameObject, 26f);
            _hostButton            = Btn(_hostRoomPanel,"HostButton","HOST",btnHost,50);
            _joinButton            = Btn(_hostRoomPanel,"JoinButton","JOIN",btnJoin,50);
            _backFromHostRoomButton= Btn(_hostRoomPanel,"BackFromHostRoomButton","BACK",btnBack,46);
            _hostRoomPanel.SetActive(false);

            // Join panel
            _joinPanel = Empty(card, "JoinSubPanel");
            VL(_joinPanel, 12);
            LE(Txt(_joinPanel,"JoinTitle","ENTER ROOM CODE",14,FontStyle.Bold,accentCyan,TextAnchor.MiddleCenter).gameObject,24f);
            _roomCodeInput = InputF(_joinPanel,"RoomCodeInput","ROOM CODE",48);
            var row = Empty(_joinPanel,"JoinBtnRow");
            LE(row, 44f);
            var rhl = row.AddComponent<HorizontalLayoutGroup>();
            rhl.spacing = 10; rhl.childControlWidth = true; rhl.childControlHeight = true; rhl.childForceExpandWidth = true;
            _pasteCodeButton    = Btn(row,"PasteCodeButton",   "Paste",btnCopy,44);
            _confirmJoinButton  = Btn(row,"ConfirmJoinButton", "Enter",btnJoin,44);
            _backFromJoinButton = Btn(row,"BackFromJoinButton","Back", btnBack,44);
            _joinPanel.SetActive(false);

            // Settings panel
            _settingsPanel = Empty(card, "SettingsSubPanel");
            VL(_settingsPanel, 18);
            LE(Txt(_settingsPanel,"SettingsTitle","SETTINGS",18,FontStyle.Bold,accentCyan,TextAnchor.MiddleCenter).gameObject,32f);
            var blank = Empty(_settingsPanel,"BlankArea");
            LE(blank, 120f);
            Stretch(Txt(blank,"BlankNote","(Settings will be here)",13,FontStyle.Italic,textSecondary,TextAnchor.MiddleCenter).GetComponent<RectTransform>());
            _backFromSettingsButton = Btn(_settingsPanel,"BackFromSettingsButton","BACK",btnBack,46);
            _settingsPanel.SetActive(false);

            // Connecting panel
            _connectingPanel = Empty(card,"ConnectingPanel");
            VL(_connectingPanel, 16);
            var sp = Img(_connectingPanel,"SpinnerRing",accentCyan);
            sp.GetComponent<RectTransform>().sizeDelta = new Vector2(40,40);
            var sple = sp.gameObject.AddComponent<LayoutElement>(); sple.preferredWidth = 40; sple.preferredHeight = 40;
            sp.gameObject.AddComponent<SpinnerAnimator>();
            _connectingLabel = Txt(_connectingPanel,"ConnectingLabel","Connecting...",16,FontStyle.Bold,accentCyan,TextAnchor.MiddleCenter);
            LE(_connectingLabel.gameObject, 30f);
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
