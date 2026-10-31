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
        private bool      _waitingForHost;

        public bool IsMenuVisible => isActiveAndEnabled &&
            (_lobbyRoot == null || _lobbyRoot.alpha > 0.01f);

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            // Only allow LobbyUI in Lobby (lobby/menu scene)
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!string.IsNullOrEmpty(sceneName) && sceneName != "Lobby")
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }

            SteamLobbyManager.EnsureInstance();

            if (_canvas == null) _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();

            if (_lobbyRoot == null || _mainPanel == null)
                BuildRuntime();
            ApplyTheme();
        }

        /// <summary>
        /// Restyles the scene-built (or runtime-built) lobby with <see cref="UITheme"/>. Visual only:
        /// layout, references and behaviour are untouched, so Lobby.unity needs no re-save.
        /// </summary>
        internal void ApplyTheme()
        {
            LocalizeThai();

            Transform background = transform.Find("Background");
            if (background != null && background.TryGetComponent(out Image backdrop))
            {
                UITheme.StyleBackdrop(backdrop); // blurred level art from the kit
            }
            Transform card = transform.Find("LobbyCard");
            if (card != null) UITheme.StylePanel(card.GetComponent<Image>());

            UITheme.StyleButton(_hostRoomMenuButton, UIButtonStyle.Primary);
            UITheme.StyleButton(_hostButton, UIButtonStyle.Primary);
            UITheme.StyleButton(_confirmJoinButton, UIButtonStyle.Primary);
            foreach (Button button in new[] { _settingsMenuButton, _quitGameButton, _joinButton, _backFromHostRoomButton,
                         _pasteCodeButton, _backFromJoinButton, _backFromSettingsButton, _cancelConnectingButton })
                UITheme.StyleButton(button, UIButtonStyle.Secondary);
            UITheme.StyleInputField(_roomCodeInput);

            foreach (SpinnerAnimator spinner in GetComponentsInChildren<SpinnerAnimator>(true))
                if (spinner.TryGetComponent(out Image ring)) ring.color = UITheme.Primary;

            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                text.verticalOverflow = VerticalWrapMode.Overflow;
                if (text.GetComponentInParent<Button>(true) != null)
                {
                    text.fontSize = Mathf.Max(text.fontSize, MinButtonFontSize);
                    continue;
                }
                if (text.GetComponentInParent<InputField>(true) != null) continue;
                string n = text.name;
                UITextRole role = text == _statusMessageLabel ? UITextRole.Danger :
                    n == "TitleLabel" || n == "ConnectingTitle" ? UITextRole.Primary :
                    n.Contains("Title") || n == "ConnectingLabel" ? UITextRole.Accent : UITextRole.Secondary;
                UITheme.StyleText(text, role);
                int size = n == "SubTitleLabel" ? SubtitleFontSize : Mathf.Max(text.fontSize, MinLabelFontSize);
                if (size != text.fontSize)
                {
                    text.fontSize = size;
                    if (text.TryGetComponent(out LayoutElement element) && element.preferredHeight > 0f)
                        element.preferredHeight = Mathf.Max(element.preferredHeight, size + 8f);
                }
            }
            if (card != null) ApplyCuteLook((RectTransform)card);
            if (card != null) CompactLayout((RectTransform)card);
        }

        private const int SubtitleFontSize = 16, MinLabelFontSize = 15, MinButtonFontSize = 16;

        // ── Cute kit (Canva pages 65-67): beige card, wooden plank title per screen, glossy buttons ──
        private Text _plankTitle;
        private const float CuteCardWidth = 620f, CuteButtonHeight = 86f, CuteSmallButtonHeight = 76f;

        private void ApplyCuteLook(RectTransform card)
        {
            UITheme.StyleCuteCard(card.GetComponent<Image>());
            card.sizeDelta = new Vector2(CuteCardWidth, card.sizeDelta.y);
            if (card.TryGetComponent(out VerticalLayoutGroup layout))
            {
                layout.padding = new RectOffset(66, 66, 100, 58); // top band leaves room for the plank
                layout.spacing = 20f;
            }
            if (_plankTitle == null)
            {
                _plankTitle = UITheme.MakeCutePlank(card, "", 430f, 108f, 0f, 38);
                UITheme.AddCuteTape(card, 2, new Vector2(0f, 1f), new Vector2(22f, -34f), 30f, 100f);
                UITheme.AddCuteTape(card, 1, new Vector2(1f, 0f), new Vector2(-36f, 30f), -25f, 100f);
            }

            // Titles move onto the plank.
            foreach (string name in new[] { "TitleLabel", "SubTitle", "ConnectingTitle", "SettingsTitle" })
            {
                Text text = FindText(name);
                if (text == null) continue;
                text.gameObject.SetActive(false);
                UITheme.SetPreferredHeight(text.gameObject, 0f);
            }

            CuteButton(_hostRoomMenuButton, UIButtonStyle.Pink);
            CuteButton(_settingsMenuButton, UIButtonStyle.White);
            CuteButton(_quitGameButton, UIButtonStyle.White);
            CuteButton(_hostButton, UIButtonStyle.Pink);
            CuteButton(_joinButton, UIButtonStyle.Blue);
            CuteButton(_backFromHostRoomButton, UIButtonStyle.White, CuteSmallButtonHeight);
            CuteButton(_confirmJoinButton, UIButtonStyle.Pink);
            CuteButton(_backFromJoinButton, UIButtonStyle.White, CuteSmallButtonHeight);
            CuteButton(_backFromSettingsButton, UIButtonStyle.White, CuteSmallButtonHeight);
            CuteButton(_cancelConnectingButton, UIButtonStyle.White, CuteSmallButtonHeight);
            if (_pasteCodeButton != null)
            {
                UITheme.StyleButton(_pasteCodeButton, UIButtonStyle.WhiteShort);
                if (_pasteCodeButton.TryGetComponent(out LayoutElement paste)) { paste.preferredWidth = 118f; paste.preferredHeight = 76f; }
                ((RectTransform)_pasteCodeButton.transform).sizeDelta = new Vector2(118f, 76f); // InputRow does not control widths
                foreach (Text label in _pasteCodeButton.GetComponentsInChildren<Text>(true)) label.fontSize = 26;
            }

            if (_roomCodeInput != null && _roomCodeInput.targetGraphic is Image well)
            {
                UITheme.StyleShape(well, UITheme.SmallSprite, UITheme.CuteWell);
                if (_roomCodeInput.TryGetComponent(out LayoutElement input)) { input.preferredWidth = 360f; input.preferredHeight = 76f; }
                ((RectTransform)_roomCodeInput.transform).sizeDelta = new Vector2(360f, 76f);
                if (_roomCodeInput.placeholder is Text hint) hint.fontSize = 24;
                if (_roomCodeInput.transform.parent.TryGetComponent(out LayoutElement row)) row.preferredHeight = 76f;
                if (_roomCodeInput.textComponent != null) _roomCodeInput.textComponent.fontSize = 32;
            }

            foreach (SpinnerAnimator spinner in GetComponentsInChildren<SpinnerAnimator>(true))
                if (spinner.TryGetComponent(out Image ring)) ring.color = UITheme.CuteAccent;

            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                if (text == _plankTitle || text == _statusMessageLabel) continue;
                if (text.GetComponentInParent<Button>(true) != null || text.GetComponentInParent<InputField>(true) != null) continue;
                if (text.transform.parent != null && text.transform.parent.name == "CutePlank") continue;
                UITheme.StyleCuteText(text, text.name == "ConnectingSub" || text.name == "BlankNote");
                text.fontSize = text.name == "ConnectingSub" ? 20 : 26;
                if (text.TryGetComponent(out LayoutElement element) && element.preferredHeight > 0f)
                    element.preferredHeight = Mathf.Max(element.preferredHeight, text.fontSize + 12f);
            }
        }

        private static void CuteButton(Button button, UIButtonStyle style, float height = CuteButtonHeight)
        {
            if (button == null) return;
            UITheme.StyleButton(button, style);
            if (button.TryGetComponent(out LayoutElement element)) { element.preferredHeight = height; element.minHeight = height; }
            foreach (Text label in button.GetComponentsInChildren<Text>(true)) label.fontSize = Mathf.Max(label.fontSize, 30);
        }

        private void UpdatePlank(MenuState state)
        {
            if (_plankTitle == null) return;
            switch (state)
            {
                case MenuState.HostRoom:   _plankTitle.text = "เลือกโหมดการเล่น"; break;
                case MenuState.Join:       _plankTitle.text = "เข้าห้องด้วยรหัส"; break;
                case MenuState.Settings:   _plankTitle.text = "ตั้งค่า"; break;
                case MenuState.Connecting: _plankTitle.text = "กำลังเข้าเกม"; break;
                default:                   _plankTitle.text = "DON'T DROP IT"; break;
            }
        }

        /// <summary>Applies one Thai copy set to both serialized and runtime-built lobby hierarchies.</summary>
        private void LocalizeThai()
        {
            SetNamedText("TitleLabel", "DON'T DROP IT");

            Text subtitle = FindText("SubTitleLabel");
            if (subtitle != null)
            {
                subtitle.text = "";
                subtitle.gameObject.SetActive(false);
                UITheme.SetPreferredHeight(subtitle.gameObject, 0f);
            }

            SetButtonText(_hostRoomMenuButton, "เล่นเกม");
            SetButtonText(_settingsMenuButton, "ตั้งค่า");
            SetButtonText(_quitGameButton, "ออกจากเกม");
            SetNamedText("SubTitle", "เลือกโหมดการเล่น");
            SetButtonText(_hostButton, "สร้างห้อง");
            SetButtonText(_joinButton, "เข้าห้องด้วยรหัส");
            SetButtonText(_backFromHostRoomButton, "ย้อนกลับ");
            SetNamedText("JoinTitle", "กรอกรหัสห้อง 6 หลัก");
            SetButtonText(_pasteCodeButton, "วาง");
            SetButtonText(_confirmJoinButton, "เข้าห้อง");
            SetButtonText(_backFromJoinButton, "ย้อนกลับ");
            SetNamedText("SettingsTitle", "ตั้งค่า");
            SetNamedText("BlankNote", "การตั้งค่าเสียงและการควบคุมกำลังพัฒนา");
            SetButtonText(_backFromSettingsButton, "ย้อนกลับ");
            SetNamedText("ConnectingTitle", "กำลังเข้าเกม");
            SetNamedText("ConnectingSub", "กำลังเตรียมระบบเครือข่ายและเปิดห้อง");
            SetButtonText(_cancelConnectingButton, "ยกเลิก");

            if (_roomCodeInput != null && _roomCodeInput.placeholder is Text placeholder)
                placeholder.text = "รหัสห้อง";
        }

        private Text FindText(string objectName)
        {
            foreach (Text text in GetComponentsInChildren<Text>(true))
                if (text.name == objectName) return text;
            return null;
        }

        private void SetNamedText(string objectName, string value)
        {
            Text text = FindText(objectName);
            if (text != null) text.text = value;
        }

        private static void SetButtonText(Button button, string value)
        {
            if (button == null) return;
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.text = value;
        }

        /// <summary>
        /// Card fits its content: sub-panels drop their fixed 360 px reservation, the "breathing" spacer
        /// slots (e.g. MainSpacer between SETTINGS and QUIT GAME) are collapsed, and the empty status line
        /// only takes space when it has text. Runtime-only, so Lobby.unity needs no re-save.
        /// </summary>
        private void CompactLayout(RectTransform card)
        {
            var layout = card.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
            }
            foreach (GameObject panel in new[] { _mainPanel, _hostRoomPanel, _joinPanel, _settingsPanel, _connectingPanel })
            {
                if (panel == null) continue;
                UITheme.SetPreferredHeight(panel, -1f);
                if (panel.TryGetComponent(out VerticalLayoutGroup panelLayout))
                {
                    panelLayout.childControlHeight = true;
                    panelLayout.childForceExpandHeight = false;
                }
                foreach (Transform child in panel.transform)
                    if (child.name.EndsWith("Spacer")) child.gameObject.SetActive(false);
            }
            if (_statusMessageLabel != null) UITheme.SetPreferredHeight(_statusMessageLabel.gameObject, -1f); // 0 when empty
            UITheme.FitHeightToContent(card.gameObject);
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
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
                _waitingForHost = false;
                _fadingOut = true;
                StartCoroutine(FadeOutAndHide());
                return;
            }
            // Once hosting succeeds, fading menu input must not cancel the live session.
            if (_fadingOut) return;

            var manager = SteamLobbyManager.Instance;
            if (_waitingForHost && manager != null && !manager.IsStartingHost && !IsSessionActive())
            {
                _waitingForHost = false;
                ShowPanel(MenuState.HostRoom);
                SetStatus(manager.StatusMessage, true);
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
                    ? LocalizeStatus(SteamLobbyManager.Instance.StatusMessage) : "กำลังเชื่อมต่อ";
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
            _waitingForHost = false;
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
            if (mgr == null)
            {
                SetStatus("ระบบห้องยังไม่พร้อม กรุณาลองอีกครั้ง", true);
                return;
            }
            if (mgr.IsStartingHost) return;
            ShowPanel(MenuState.Connecting);
            _waitingForHost = true;
            if (_connectingLabel != null) _connectingLabel.text = "กำลังสร้างห้อง...";
            mgr.HostLobby(4);
        }

        private void OnConfirmJoin()
        {
            var mgr = SteamLobbyManager.Instance;
            if (mgr == null || !mgr.IsSteamMultiplayerAvailable)
            {
                SetStatus("Steam ไม่ได้เปิดอยู่", true);
                return;
            }
            string code = _roomCodeInput != null ? _roomCodeInput.text.Trim().ToUpperInvariant() : "";
            if (string.IsNullOrEmpty(code)) { SetStatus("กรุณากรอกรหัสห้อง", true); return; }
            ShowPanel(MenuState.Connecting);
            if (_connectingLabel != null) _connectingLabel.text = "กำลังเข้าห้อง: " + code;
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
            UpdatePlank(state);
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
            bool steamReady = mgr != null && mgr.IsSteamMultiplayerAvailable;
            bool canHost = mgr != null && !mgr.IsStartingHost;

            if (_hostRoomMenuButton != null) _hostRoomMenuButton.interactable = canHost;
            if (_hostButton         != null) _hostButton.interactable         = canHost;
            if (_joinButton         != null) _joinButton.interactable         = mgr != null;
            if (_confirmJoinButton  != null) _confirmJoinButton.interactable  = mgr != null;

            if (_steamStatusLabel != null)
            {
                _steamStatusLabel.gameObject.SetActive(false); // Kept completely hidden as requested
            }

            if (_statusMessageLabel != null)
            {
                if (mgr != null && !string.IsNullOrEmpty(mgr.StatusMessage) && mgr.StatusMessage != "Ready" && !mgr.StatusMessage.StartsWith("Steam Connected"))
                {
                    _statusMessageLabel.text = LocalizeStatus(mgr.StatusMessage);
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
            _statusMessageLabel.text  = LocalizeStatus(msg);
            _statusMessageLabel.color = isError ? UITheme.Danger : UITheme.CuteInkSoft;
        }

        private static string LocalizeStatus(string status)
        {
            if (string.IsNullOrEmpty(status)) return "";
            if (status == "Ready" || status.StartsWith("Steam Connected")) return "";
            if (status.StartsWith("Room Created! Code:")) return "สร้างห้องแล้ว รหัส:" + status.Substring("Room Created! Code:".Length);
            if (status.StartsWith("Connecting to Room")) return "กำลังเชื่อมต่อห้อง" + status.Substring("Connecting to Room".Length);
            if (status.StartsWith("Joining Room:")) return "กำลังเข้าห้อง:" + status.Substring("Joining Room:".Length);
            if (status.StartsWith("Entered Room:")) return "เข้าห้องแล้ว:" + status.Substring("Entered Room:".Length);
            if (status.StartsWith("Searching for Room:")) return "กำลังค้นหาห้อง:" + status.Substring("Searching for Room:".Length);
            if (status.StartsWith("New Room Code:")) return "รหัสห้องใหม่:" + status.Substring("New Room Code:".Length);
            if (status == "Creating room..." || status == "Creating Steam Lobby...") return "กำลังสร้างห้อง";
            if (status == "Starting offline game...") return "กำลังเริ่มเกมออฟไลน์";
            if (status == "Playing offline") return "กำลังเล่นแบบออฟไลน์";
            if (status.Contains("not found")) return "ไม่พบห้อง กรุณาตรวจรหัสแล้วลองใหม่";
            if (status.Contains("Steam is not running") || status.StartsWith("Steam unavailable")) return "Steam ไม่พร้อม แต่ยังสร้างห้องออฟไลน์ได้";
            if (status.Contains("closing")) return "ระบบกำลังปิดการเชื่อมต่อ กรุณารอสักครู่";
            if (status.Contains("could not") || status.Contains("Failed") || status.Contains("Error") || status.Contains("invalid"))
                return "เชื่อมต่อไม่สำเร็จ กรุณาลองอีกครั้ง";
            return status;
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

            // Palette (UITheme roles; ApplyTheme in Awake finalises the styling)


            const UITextRole accentCyan     = UITextRole.Accent;
            const UIButtonStyle btnPlay     = UIButtonStyle.Primary;
            const UIButtonStyle btnJoin     = UIButtonStyle.Secondary;
            const UIButtonStyle btnConfirm  = UIButtonStyle.Primary;
            const UIButtonStyle btnSettings = UIButtonStyle.Secondary;
            const UIButtonStyle btnQuit     = UIButtonStyle.Secondary;
            const UIButtonStyle btnBack     = UIButtonStyle.Secondary;
            const UITextRole textPrimary    = UITextRole.Primary;
            const UITextRole textSecondary  = UITextRole.Secondary;

            // Background
            var bg = ProceduralUIUtility.MakeImage(gameObject, "Background", UITheme.Backdrop);
            ProceduralUIUtility.StretchFull(bg.GetComponent<RectTransform>());

            // Center Card (460 x 520)
            var card   = ProceduralUIUtility.MakePanel(gameObject, "LobbyCard", new Vector2(460, 520), Vector2.zero);
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
            LE(ProceduralUIUtility.MakeText(card, "TitleLabel", "DON'T DROP IT", 32, FontStyle.Bold, textPrimary, TextAnchor.MiddleCenter).gameObject, 40f);
            LE(ProceduralUIUtility.MakeText(card, "SubTitleLabel", "", 11, FontStyle.Bold, accentCyan, TextAnchor.MiddleCenter).gameObject, 0f);

            _steamStatusLabel   = null;
            _statusMessageLabel = ProceduralUIUtility.MakeText(card, "StatusMessageLabel", "", 12, FontStyle.Italic, UITextRole.Danger, TextAnchor.MiddleCenter);
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

            var (_, pBtn) = ProceduralUIUtility.MakeButton(_mainPanel, "HostRoomMenuButton", "เล่นเกม", btnPlay, 58, 16);
            _hostRoomMenuButton = pBtn;
            var (_, sBtn) = ProceduralUIUtility.MakeButton(_mainPanel, "SettingsMenuButton", "ตั้งค่า", btnSettings, 52, 15);
            _settingsMenuButton = sBtn;

            // Controlled breathing spacer before quit button
            var mSpacer = ProceduralUIUtility.MakeEmpty(_mainPanel, "MainSpacer");
            var mSpLE = mSpacer.AddComponent<LayoutElement>();
            mSpLE.preferredHeight = 32;
            mSpLE.flexibleHeight = 0;

            var (_, qBtn) = ProceduralUIUtility.MakeButton(_mainPanel, "QuitGameButton", "ออกจากเกม", btnQuit, 48, 14);
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

            var subT1 = ProceduralUIUtility.MakeText(_hostRoomPanel, "SubTitle", "เลือกโหมดการเล่น", 13, FontStyle.Bold, accentCyan, TextAnchor.MiddleCenter);
            var subT1LE = subT1.gameObject.AddComponent<LayoutElement>();
            subT1LE.preferredHeight = 26;
            subT1LE.flexibleHeight = 0;

            var (_, hBtn) = ProceduralUIUtility.MakeButton(_hostRoomPanel, "HostButton", "สร้างห้อง", btnPlay, 58, 16);
            _hostButton = hBtn;
            var (_, jBtn) = ProceduralUIUtility.MakeButton(_hostRoomPanel, "JoinButton", "เข้าห้องด้วยรหัส", btnJoin, 54, 15);
            _joinButton = jBtn;

            // Controlled breathing spacer before Back button
            var hrSpacer = ProceduralUIUtility.MakeEmpty(_hostRoomPanel, "HostSpacer");
            var hrSpLE = hrSpacer.AddComponent<LayoutElement>();
            hrSpLE.preferredHeight = 28;
            hrSpLE.flexibleHeight = 0;

            var (_, bBtn) = ProceduralUIUtility.MakeButton(_hostRoomPanel, "BackFromHostRoomButton", "ย้อนกลับ", btnBack, 48, 14);
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

            var jTitle = ProceduralUIUtility.MakeText(_joinPanel, "JoinTitle", "กรอกรหัสห้อง 6 หลัก", 13, FontStyle.Bold, accentCyan, TextAnchor.MiddleCenter);
            var jTitleLE = jTitle.gameObject.AddComponent<LayoutElement>();
            jTitleLE.preferredHeight = 26;
            jTitleLE.flexibleHeight = 0;

            var inputRow = ProceduralUIUtility.MakeEmpty(_joinPanel, "InputRow");
            var inRowLE = inputRow.AddComponent<LayoutElement>();
            inRowLE.preferredHeight = 54;
            inRowLE.flexibleHeight = 0;
            var inHL = inputRow.AddComponent<HorizontalLayoutGroup>();
            inHL.spacing = 8; inHL.childControlWidth = false; inHL.childControlHeight = true; inHL.childForceExpandWidth = false; inHL.childForceExpandHeight = true;

            var (inGO, cInp) = ProceduralUIUtility.MakeInputField(inputRow, "RoomCodeInput", "รหัสห้อง", 54);
            _roomCodeInput = cInp;
            var inRT = inGO.GetComponent<RectTransform>();
            inRT.sizeDelta = new Vector2(270, 54);
            var inLE = inGO.AddComponent<LayoutElement>();
            inLE.preferredWidth = 270; inLE.preferredHeight = 54;

            var (pstGO, pstBtn) = ProceduralUIUtility.MakeButton(inputRow, "PasteCodeButton", "วาง", btnSettings, 54, 13);
            _pasteCodeButton = pstBtn;
            var pstRT = pstGO.GetComponent<RectTransform>();
            pstRT.sizeDelta = new Vector2(110, 54);
            var pstLE = pstGO.GetComponent<LayoutElement>();
            if (pstLE != null) { pstLE.preferredWidth = 110; pstLE.preferredHeight = 54; }

            var (_, cnfBtn) = ProceduralUIUtility.MakeButton(_joinPanel, "ConfirmJoinButton", "เข้าห้อง", btnConfirm, 54, 15);
            _confirmJoinButton = cnfBtn;
            var cnfLE = cnfBtn.GetComponent<LayoutElement>();
            if (cnfLE != null) { cnfLE.preferredHeight = 54; cnfLE.flexibleHeight = 0; }

            // Controlled breathing spacer before Back button
            var jnSpacer = ProceduralUIUtility.MakeEmpty(_joinPanel, "JoinSpacer");
            var jnSpLE = jnSpacer.AddComponent<LayoutElement>();
            jnSpLE.preferredHeight = 28;
            jnSpLE.flexibleHeight = 0;

            var (_, bckBtn) = ProceduralUIUtility.MakeButton(_joinPanel, "BackFromJoinButton", "ย้อนกลับ", btnBack, 48, 14);
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

            var stTitle = ProceduralUIUtility.MakeText(_settingsPanel, "SettingsTitle", "ตั้งค่า", 18, FontStyle.Bold, accentCyan, TextAnchor.MiddleCenter);
            var stTitleLE = stTitle.gameObject.AddComponent<LayoutElement>();
            stTitleLE.preferredHeight = 28;
            stTitleLE.flexibleHeight = 0;

            var blank = ProceduralUIUtility.MakeEmpty(_settingsPanel, "BlankArea");
            var blankLE = blank.AddComponent<LayoutElement>();
            blankLE.preferredHeight = 110;
            blankLE.flexibleHeight = 0;
            ProceduralUIUtility.StretchFull(ProceduralUIUtility.MakeText(blank, "BlankNote", "การตั้งค่าเสียงและการควบคุมกำลังพัฒนา", 13, FontStyle.Italic, textSecondary, TextAnchor.MiddleCenter).GetComponent<RectTransform>());

            // Controlled breathing spacer before Back button
            var stSpacer = ProceduralUIUtility.MakeEmpty(_settingsPanel, "SettingsSpacer");
            var stSpLE = stSpacer.AddComponent<LayoutElement>();
            stSpLE.preferredHeight = 28;
            stSpLE.flexibleHeight = 0;

            var (_, bStgBtn) = ProceduralUIUtility.MakeButton(_settingsPanel, "BackFromSettingsButton", "ย้อนกลับ", btnBack, 48, 14);
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

            var sp = ProceduralUIUtility.MakeImage(spinnerContainer, "SpinnerRing", UITheme.Primary, ProceduralUIUtility.GetSpinnerRingSprite());
            var spRT = sp.GetComponent<RectTransform>();
            spRT.anchorMin = new Vector2(0.5f, 0.5f);
            spRT.anchorMax = new Vector2(0.5f, 0.5f);
            spRT.pivot = new Vector2(0.5f, 0.5f);
            spRT.sizeDelta = new Vector2(58, 58);
            spRT.anchoredPosition = Vector2.zero;
            sp.gameObject.AddComponent<SpinnerAnimator>();

            // Title
            var titleText = ProceduralUIUtility.MakeText(_connectingPanel, "ConnectingTitle", "กำลังเข้าเกม", 17, FontStyle.Bold, textPrimary, TextAnchor.MiddleCenter);
            var titleLE = titleText.gameObject.AddComponent<LayoutElement>();
            titleLE.preferredHeight = 28;
            titleLE.flexibleHeight = 0;

            // Dynamic status label
            _connectingLabel = ProceduralUIUtility.MakeText(_connectingPanel, "ConnectingLabel", "กำลังสร้างห้อง", 15, FontStyle.Normal, accentCyan, TextAnchor.MiddleCenter);
            var connLblLE = _connectingLabel.gameObject.AddComponent<LayoutElement>();
            connLblLE.preferredHeight = 24;
            connLblLE.flexibleHeight = 0;

            // Subtitle
            var subText = ProceduralUIUtility.MakeText(_connectingPanel, "ConnectingSub", "กำลังเตรียมระบบเครือข่ายและเปิดห้อง", 12, FontStyle.Italic, textSecondary, TextAnchor.MiddleCenter);
            var subTextLE = subText.gameObject.AddComponent<LayoutElement>();
            subTextLE.preferredHeight = 22;
            subTextLE.flexibleHeight = 0;

            // Controlled breathing spacer before cancel button
            var bottomSpacer = ProceduralUIUtility.MakeEmpty(_connectingPanel, "BottomSpacer");
            var btmSpacerLE = bottomSpacer.AddComponent<LayoutElement>();
            btmSpacerLE.preferredHeight = 24;
            btmSpacerLE.flexibleHeight = 0;

            // Cancel button
            var (_, cancelBtn) = ProceduralUIUtility.MakeButton(_connectingPanel, "CancelConnectingButton", "ยกเลิก", btnBack, 48, 14);
            _cancelConnectingButton = cancelBtn;
            var cancelLE = cancelBtn.GetComponent<LayoutElement>();
            if (cancelLE != null) { cancelLE.preferredHeight = 48; cancelLE.flexibleHeight = 0; }

            _connectingPanel.SetActive(false);

            Debug.Log("[LobbyUI] Runtime self-build complete.");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

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
