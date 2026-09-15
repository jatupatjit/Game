using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// PauseMenu - ESC key opens an animated pause overlay during an active game session.
    /// Self-builds at runtime if editor build was not done (_pauseRoot == null).
    /// Buttons: Continue, Settings, Quit to Menu (with confirm dialog).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public class PauseMenu : MonoBehaviour
    {
        [Header("Canvas & Root")]
        [SerializeField] private Canvas      _canvas;
        [SerializeField] private CanvasGroup _pauseRoot;
        [SerializeField] private GameObject  _pausePanel;

        [Header("Main Pause Buttons")]
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _quitToMenuButton;

        [Header("Settings Panel")]
        [SerializeField] private GameObject _settingsPanel;
        [SerializeField] private Button     _backFromSettingsButton;

        [Header("Settings Controls (Optional)")]
        [SerializeField] private Slider _masterVolumeSlider;
        [SerializeField] private Slider _sfxVolumeSlider;
        [SerializeField] private Slider _musicVolumeSlider;
        [SerializeField] private Toggle _fullscreenToggle;

        [Header("Overlay")]
        [SerializeField] private Image _backgroundOverlay;

        [Header("Confirm Quit Dialog")]
        [SerializeField] private GameObject _confirmDialog;
        [SerializeField] private Button     _confirmQuitButton;
        [SerializeField] private Button     _cancelQuitButton;

        [Header("Settings")]
        [SerializeField] private float _fadeSpeed = 5f;
        [Tooltip("Only allow pause when a network session is active")]
        [SerializeField] private bool _requireActiveSession = false;

        public static PauseMenu Instance { get; private set; }
        public static bool IsPaused { get; private set; } = false;

        private bool _isPaused     = false;
        private bool _settingsOpen = false;

        // ─────────────────────────────────────────────────────────────────────

        public static PauseMenu EnsureInstance()
        {
            if (Instance != null) return Instance;
            PauseMenu found = FindFirstObjectByType<PauseMenu>();
            if (found != null) { Instance = found; return Instance; }
            GameObject prefab = Resources.Load<GameObject>("PauseMenu");
            if (prefab != null)
            {
                GameObject go = Instantiate(prefab);
                go.name = "PauseMenu";
                Instance = go.GetComponent<PauseMenu>();
                return Instance;
            }
            return null;
        }

        private void Awake()
        {
            Instance = this;

            if (_canvas == null) _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();

            if (_pauseRoot == null)
                BuildRuntime();
        }

        private void OnDestroy()
        {
            if (Instance == this) { Instance = null; IsPaused = false; }
        }

        private void Start()
        {
            if (_continueButton         != null) _continueButton.onClick.AddListener(OnContinueClicked);
            if (_settingsButton         != null) _settingsButton.onClick.AddListener(OnSettingsClicked);
            if (_quitToMenuButton       != null) _quitToMenuButton.onClick.AddListener(OnQuitToMenuClicked);
            if (_backFromSettingsButton != null) _backFromSettingsButton.onClick.AddListener(OnBackFromSettings);
            if (_confirmQuitButton      != null) _confirmQuitButton.onClick.AddListener(OnConfirmQuit);
            if (_cancelQuitButton       != null) _cancelQuitButton.onClick.AddListener(OnCancelQuit);

            if (_masterVolumeSlider != null)
            {
                _masterVolumeSlider.value = PlayerPrefs.GetFloat("MasterVolume", 1f);
                _masterVolumeSlider.onValueChanged.AddListener(v => { PlayerPrefs.SetFloat("MasterVolume", v); AudioListener.volume = v; });
                AudioListener.volume = _masterVolumeSlider.value;
            }

            if (_fullscreenToggle != null)
            {
                _fullscreenToggle.isOn = Screen.fullScreen;
                _fullscreenToggle.onValueChanged.AddListener(v => Screen.fullScreen = v);
            }

            HidePause(instant: true);
            HideSettingsPanel();
            HideConfirmDialog();
        }

        private void Update()
        {
            bool escPressed = UnityEngine.InputSystem.Keyboard.current != null &&
                              UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
            if (!escPressed) return;
            if (_requireActiveSession && !IsSessionActive()) return;

            if (_settingsOpen) OnBackFromSettings();
            else if (_isPaused) OnContinueClicked();
            else ShowPause();
        }

        // ─────────────────────────────────────────── Pause Toggle ─────────────

        public void ShowPause()
        {
            if (_isPaused) return;
            _isPaused = true;
            IsPaused  = true;

            HideSettingsPanel();
            HideConfirmDialog();
            if (_pausePanel != null) _pausePanel.SetActive(true);

            StopAllCoroutines();
            StartCoroutine(FadeTo(1f));

            if (_pauseRoot != null) { _pauseRoot.interactable = true; _pauseRoot.blocksRaycasts = true; }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;

            CoopGame.Player.PlayerCameraController.LocalInstance?.SetCursorLock(false);
        }

        public void HidePause(bool instant = false)
        {
            _isPaused = false;
            IsPaused  = false;
            HideSettingsPanel();
            HideConfirmDialog();

            if (instant)
            {
                if (_pauseRoot != null) { _pauseRoot.alpha = 0f; _pauseRoot.interactable = false; _pauseRoot.blocksRaycasts = false; }
                return;
            }

            StopAllCoroutines();
            StartCoroutine(FadeTo(0f));

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
            CoopGame.Player.PlayerCameraController.LocalInstance?.SetCursorLock(true);
        }

        // ─────────────────────────────────────────── Button Handlers ──────────

        private void OnContinueClicked() => HidePause();

        private void OnSettingsClicked()  { _settingsOpen = true;  ShowSettingsPanel(); }
        private void OnBackFromSettings() { _settingsOpen = false; HideSettingsPanel(); }

        private void OnQuitToMenuClicked()
        {
            if (_confirmDialog != null) ShowConfirmDialog();
            else OnConfirmQuit();
        }

        private void OnConfirmQuit()
        {
            HideConfirmDialog();
            HidePause(instant: true);

            var mgr = SteamLobbyManager.Instance;
            if (mgr != null)
            {
                mgr.DisconnectAndReturnToLobby();
            }
            else
            {
                var nm = NetworkManager.Singleton;
                if (nm != null && (nm.IsClient || nm.IsServer)) nm.Shutdown();
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            }
        }

        private void OnCancelQuit() => HideConfirmDialog();

        // ─────────────────────────────────────────── Settings Panel ───────────

        private void ShowSettingsPanel()
        {
            _settingsOpen = true;
            if (_pausePanel != null) _pausePanel.SetActive(false);
            if (_settingsPanel != null) _settingsPanel.SetActive(true);
            SetMainButtonsVisible(false);
        }

        private void HideSettingsPanel()
        {
            _settingsOpen = false;
            if (_settingsPanel != null) _settingsPanel.SetActive(false);
            if (_pausePanel != null) _pausePanel.SetActive(true);
            SetMainButtonsVisible(true);
        }

        private void SetMainButtonsVisible(bool v)
        {
            if (_continueButton   != null) _continueButton.gameObject.SetActive(v);
            if (_settingsButton   != null) _settingsButton.gameObject.SetActive(v);
            if (_quitToMenuButton != null) _quitToMenuButton.gameObject.SetActive(v);
        }

        // ─────────────────────────────────────────── Confirm Dialog ───────────

        private void ShowConfirmDialog()
        {
            if (_pausePanel != null) _pausePanel.SetActive(false);
            if (_confirmDialog != null) _confirmDialog.SetActive(true);
        }

        private void HideConfirmDialog()
        {
            if (_confirmDialog != null) _confirmDialog.SetActive(false);
            if (_pausePanel != null) _pausePanel.SetActive(true);
        }

        // ─────────────────────────────────────────── Fade ─────────────────────

        private bool IsSessionActive()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && (nm.IsClient || nm.IsServer || nm.IsHost);
        }

        private IEnumerator FadeTo(float target)
        {
            if (_pauseRoot == null) yield break;
            while (Mathf.Abs(_pauseRoot.alpha - target) > 0.01f)
            {
                _pauseRoot.alpha = Mathf.MoveTowards(_pauseRoot.alpha, target, Time.unscaledDeltaTime * _fadeSpeed);
                yield return null;
            }
            _pauseRoot.alpha = target;
            if (target < 0.5f) { _pauseRoot.interactable = false; _pauseRoot.blocksRaycasts = false; }
        }

        // ─────────────────────────────────── Runtime Self-Builder ─────────────

        private void BuildRuntime()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            if (_canvas == null) _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 300;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight  = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            _pauseRoot = GetComponent<CanvasGroup>();
            if (_pauseRoot == null) _pauseRoot = gameObject.AddComponent<CanvasGroup>();
            _pauseRoot.alpha = 0f; _pauseRoot.interactable = false; _pauseRoot.blocksRaycasts = false;

            // Colors
            Color overlay    = H("#0A0E1ACC");
            Color panel      = H("#111827EE");
            Color accent     = H("#00D9FF");
            Color btnCont    = H("#059669");
            Color btnSet     = H("#1D4ED8");
            Color btnQuit    = H("#991B1B");
            Color btnBack    = H("#374151");
            Color btnConfirm = H("#991B1B");
            Color btnCancel  = H("#374151");
            Color textPri    = H("#F9FAFB");
            Color textSec    = H("#9CA3AF");

            // Dark overlay background
            _backgroundOverlay = Img(gameObject, "BackgroundOverlay", overlay);
            Stretch(_backgroundOverlay.GetComponent<RectTransform>());

            // Center pause card
            var panelGO = Panel(gameObject, "PausePanel", new Vector2(400, 380), panel);
            _pausePanel = panelGO;
            var pRT = panelGO.GetComponent<RectTransform>();
            pRT.anchorMin = pRT.anchorMax = new Vector2(0.5f, 0.5f);
            pRT.pivot = new Vector2(0.5f, 0.5f);
            pRT.anchoredPosition = Vector2.zero;
            var pvl = panelGO.AddComponent<VerticalLayoutGroup>();
            pvl.padding = new RectOffset(36, 36, 36, 36);
            pvl.spacing = 16; pvl.childControlWidth = true; pvl.childControlHeight = false;
            pvl.childForceExpandWidth = true; pvl.childAlignment = TextAnchor.UpperCenter;

            // Title
            LE(Txt(panelGO, "PauseTitle", "PAUSED", 32, FontStyle.Bold, textPri, TextAnchor.MiddleCenter).gameObject, 44f);
            // Divider
            LE(Img(panelGO, "Divider", accent).gameObject, 2f);

            // Main buttons
            _continueButton   = Btn(panelGO, "ContinueButton",   "CONTINUE",      btnCont, 50);
            _settingsButton   = Btn(panelGO, "SettingsButton",   "SETTINGS",      btnSet,  50);
            _quitToMenuButton = Btn(panelGO, "QuitToMenuButton", "QUIT TO MENU",  btnQuit, 50);

            // Settings sub-panel (separate child of canvas root, not inside pause card)
            _settingsPanel = Panel(gameObject, "SettingsPanel", new Vector2(420, 360), panel);
            var sRT = _settingsPanel.GetComponent<RectTransform>();
            sRT.anchorMin = sRT.anchorMax = new Vector2(0.5f, 0.5f);
            sRT.pivot = new Vector2(0.5f, 0.5f);
            sRT.anchoredPosition = Vector2.zero;
            var svl = _settingsPanel.AddComponent<VerticalLayoutGroup>();
            svl.padding = new RectOffset(36, 36, 36, 36);
            svl.spacing = 20; svl.childControlWidth = true; svl.childControlHeight = false;
            svl.childForceExpandWidth = true; svl.childAlignment = TextAnchor.UpperCenter;

            LE(Txt(_settingsPanel, "SettingsTitle", "SETTINGS", 24, FontStyle.Bold, accent, TextAnchor.MiddleCenter).gameObject, 36f);

            // Volume slider
            _masterVolumeSlider = RtSlider(_settingsPanel, "MasterVolume", "Master Volume", textSec, accent);

            // Fullscreen toggle
            _fullscreenToggle = RtToggle(_settingsPanel, "FullscreenToggle", "Fullscreen", textSec, accent);

            var blank = Empty(_settingsPanel, "BlankSpace");
            LE(blank, 20f);

            _backFromSettingsButton = Btn(_settingsPanel, "BackFromSettingsButton", "BACK", btnBack, 46);
            _settingsPanel.SetActive(false);

            // Confirm quit dialog
            _confirmDialog = Panel(gameObject, "ConfirmDialog", new Vector2(380, 220), panel);
            var cdRT = _confirmDialog.GetComponent<RectTransform>();
            cdRT.anchorMin = cdRT.anchorMax = new Vector2(0.5f, 0.5f);
            cdRT.pivot = new Vector2(0.5f, 0.5f);
            cdRT.anchoredPosition = Vector2.zero;
            var cdvl = _confirmDialog.AddComponent<VerticalLayoutGroup>();
            cdvl.padding = new RectOffset(30, 30, 30, 30);
            cdvl.spacing = 14; cdvl.childControlWidth = true; cdvl.childControlHeight = false;
            cdvl.childForceExpandWidth = true; cdvl.childAlignment = TextAnchor.UpperCenter;

            LE(Txt(_confirmDialog, "ConfirmTitle",   "Quit to Menu?",          18, FontStyle.Bold,   textPri, TextAnchor.MiddleCenter).gameObject, 28f);
            LE(Txt(_confirmDialog, "ConfirmSubtext", "You will lose progress.", 13, FontStyle.Italic, textSec, TextAnchor.MiddleCenter).gameObject, 22f);

            var dialogRow = Empty(_confirmDialog, "DialogBtnRow");
            LE(dialogRow, 46f);
            var dhl = dialogRow.AddComponent<HorizontalLayoutGroup>();
            dhl.spacing = 12; dhl.childControlWidth = true; dhl.childControlHeight = true;
            dhl.childForceExpandWidth = true;
            _confirmQuitButton = Btn(dialogRow, "ConfirmQuitButton", "QUIT",   btnConfirm, 46);
            _cancelQuitButton  = Btn(dialogRow, "CancelQuitButton",  "CANCEL", btnCancel,  46);
            _confirmDialog.SetActive(false);

            Debug.Log("[PauseMenu] Runtime self-build complete.");
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
            go.AddComponent<RectTransform>().sizeDelta = new Vector2(360, 30);
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
            Stretch(Txt(go, "Label", lbl, 15, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter).GetComponent<RectTransform>());
            return btn;
        }

        private static Slider RtSlider(GameObject p, string n, string label, Color textCol, Color fillCol)
        {
            var row = new GameObject(n); row.transform.SetParent(p.transform, false);
            row.AddComponent<RectTransform>().sizeDelta = new Vector2(0, 40);
            row.AddComponent<LayoutElement>().preferredHeight = 40;
            var rvl = row.AddComponent<VerticalLayoutGroup>();
            rvl.childControlWidth = true; rvl.childControlHeight = false;
            rvl.childForceExpandWidth = true; rvl.spacing = 2;

            var lbl = Txt(row, "Label", label, 11, FontStyle.Normal, textCol, TextAnchor.MiddleLeft);
            lbl.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 16);
            lbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 16;

            var sliderGO = new GameObject("Slider"); sliderGO.transform.SetParent(row.transform, false);
            sliderGO.AddComponent<RectTransform>().sizeDelta = new Vector2(0, 20);
            sliderGO.AddComponent<LayoutElement>().preferredHeight = 20;

            var bg = new GameObject("Background"); bg.transform.SetParent(sliderGO.transform, false);
            var bgRT = bg.AddComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0,0.25f); bgRT.anchorMax = new Vector2(1,0.75f);
            bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;
            bg.AddComponent<Image>().color = H("#374151");

            var fillArea = new GameObject("FillArea"); fillArea.transform.SetParent(sliderGO.transform, false);
            var faRT = fillArea.AddComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0,0.25f); faRT.anchorMax = new Vector2(1,0.75f);
            faRT.offsetMin = new Vector2(5,0); faRT.offsetMax = new Vector2(-5,0);

            var fill = new GameObject("Fill"); fill.transform.SetParent(fillArea.transform, false);
            var fillRT = fill.AddComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = new Vector2(1,1); fillRT.offsetMin = fillRT.offsetMax = Vector2.zero;
            fill.AddComponent<Image>().color = fillCol;

            var handle = new GameObject("Handle"); handle.transform.SetParent(sliderGO.transform, false);
            var hRT = handle.AddComponent<RectTransform>(); hRT.sizeDelta = new Vector2(20,0);
            handle.AddComponent<Image>().color = Color.white;

            var slider = sliderGO.AddComponent<Slider>();
            slider.fillRect   = fillRT;
            slider.handleRect = hRT;
            slider.minValue   = 0f; slider.maxValue = 1f; slider.value = 1f;
            slider.targetGraphic = handle.GetComponent<Image>();
            return slider;
        }

        private static Toggle RtToggle(GameObject p, string n, string label, Color textCol, Color checkCol)
        {
            var row = new GameObject(n); row.transform.SetParent(p.transform, false);
            row.AddComponent<RectTransform>().sizeDelta = new Vector2(0, 28);
            row.AddComponent<LayoutElement>().preferredHeight = 28;
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 8; hl.childControlWidth = false; hl.childControlHeight = true;
            hl.childAlignment = TextAnchor.MiddleLeft;

            var bg = new GameObject("Background"); bg.transform.SetParent(row.transform, false);
            bg.AddComponent<RectTransform>().sizeDelta = new Vector2(22,22);
            var bgImg = bg.AddComponent<Image>(); bgImg.color = H("#374151");

            var checkGO = new GameObject("Checkmark"); checkGO.transform.SetParent(bg.transform, false);
            var ckRT = checkGO.AddComponent<RectTransform>();
            ckRT.anchorMin = new Vector2(0.1f,0.1f); ckRT.anchorMax = new Vector2(0.9f,0.9f);
            ckRT.offsetMin = ckRT.offsetMax = Vector2.zero;
            var ckImg = checkGO.AddComponent<Image>(); ckImg.color = checkCol;

            var lbl = Txt(row, "Label", label, 13, FontStyle.Normal, textCol, TextAnchor.MiddleLeft);
            lbl.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 22);

            var toggle = row.AddComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic       = ckImg;
            toggle.isOn          = false;
            return toggle;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void LE(GameObject go, float height)
        {
            go.AddComponent<LayoutElement>().preferredHeight = height;
        }

#if UNITY_EDITOR
        [ContextMenu("Build Pause Menu UI (Editor Only)")]
        private void BuildPauseMenuInEditor() => PauseMenuBuilder.Build(this);
#endif
    }
}
