using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// RoomCodeHUD - Top-left overlay showing Room Code during an active session.
    /// Self-builds at runtime if editor build was not done (_hudGroup == null).
    /// Fades in when network session is active, fades out when disconnected.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public class RoomCodeHUD : MonoBehaviour
    {
        [Header("Canvas")]
        [SerializeField] private Canvas      _canvas;

        [Header("HUD References")]
        [SerializeField] private CanvasGroup _hudGroup;
        [SerializeField] private Text        _roomCodeText;
        [SerializeField] private Text        _modeLabel;
        [SerializeField] private Text        _playerCountText;
        [SerializeField] private Button      _copyButton;
        [SerializeField] private Text        _copyFeedbackText;

        [Header("Settings")]
        [SerializeField] private float _fadeSpeed      = 4f;
        [SerializeField] private float _updateInterval = 0.5f;

        private float     _updateTimer;
        private bool      _wasActive = false;
        private Coroutine _copyFeedbackCoroutine;

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (_canvas == null) _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();

            if (_hudGroup == null)
                BuildRuntime();
        }

        private void Start()
        {
            if (_copyButton != null)
                _copyButton.onClick.AddListener(OnCopyClicked);

            if (_hudGroup != null)
            {
                _hudGroup.alpha          = 0f;
                _hudGroup.interactable   = false;
                _hudGroup.blocksRaycasts = false;
            }

            if (_copyFeedbackText != null)
                _copyFeedbackText.gameObject.SetActive(false);
        }

        private void Update()
        {
            _updateTimer -= Time.unscaledDeltaTime;
            if (_updateTimer > 0f) return;
            _updateTimer = _updateInterval;

            bool sessionActive = IsSessionActive();

            if (sessionActive != _wasActive)
            {
                _wasActive = sessionActive;
                StopAllCoroutines();
                if (_copyFeedbackText != null) _copyFeedbackText.gameObject.SetActive(false);
                StartCoroutine(FadeTo(sessionActive ? 1f : 0f));
            }

            if (sessionActive) RefreshHUDContent();
        }

        // ─────────────────────────────────────────── Content ──────────────────

        private void RefreshHUDContent()
        {
            var nm  = NetworkManager.Singleton;
            var mgr = SteamLobbyManager.Instance;

            if (_roomCodeText != null)
                _roomCodeText.text = (mgr != null && !string.IsNullOrEmpty(mgr.CurrentRoomCode))
                    ? mgr.CurrentRoomCode : "------";

            if (_modeLabel != null && nm != null)
                _modeLabel.text = nm.IsHost ? "HOST" : (nm.IsServer ? "SERVER" : "CLIENT");

            if (_playerCountText != null && nm != null)
            {
                bool isHostOrServer = nm.IsHost || nm.IsServer;
                _playerCountText.gameObject.SetActive(isHostOrServer);
                if (isHostOrServer)
                    _playerCountText.text = "Players: " + nm.ConnectedClientsIds.Count;
            }
        }

        // ─────────────────────────────────────────── Copy Button ──────────────

        private void OnCopyClicked()
        {
            SteamLobbyManager.Instance?.CopyRoomCodeToClipboard();
            if (_copyFeedbackCoroutine != null) StopCoroutine(_copyFeedbackCoroutine);
            _copyFeedbackCoroutine = StartCoroutine(ShowCopyFeedback());
        }

        private IEnumerator ShowCopyFeedback()
        {
            if (_copyFeedbackText == null) yield break;
            _copyFeedbackText.gameObject.SetActive(true);
            _copyFeedbackText.text = "Copied!";
            yield return new WaitForSecondsRealtime(1.5f);
            _copyFeedbackText.gameObject.SetActive(false);
        }

        // ─────────────────────────────────────────── Fade ─────────────────────

        private bool IsSessionActive()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && (nm.IsClient || nm.IsServer || nm.IsHost);
        }

        private IEnumerator FadeTo(float target)
        {
            if (_hudGroup == null) yield break;
            _hudGroup.interactable = false; _hudGroup.blocksRaycasts = false;
            while (Mathf.Abs(_hudGroup.alpha - target) > 0.01f)
            {
                _hudGroup.alpha = Mathf.MoveTowards(_hudGroup.alpha, target, Time.unscaledDeltaTime * _fadeSpeed);
                yield return null;
            }
            _hudGroup.alpha = target;
            if (target > 0.5f) { _hudGroup.interactable = true; _hudGroup.blocksRaycasts = true; RefreshHUDContent(); }
        }

        // ─────────────────────────────────── Runtime Self-Builder ─────────────

        private void BuildRuntime()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            if (_canvas == null) _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight  = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            _hudGroup = GetComponent<CanvasGroup>();
            if (_hudGroup == null) _hudGroup = gameObject.AddComponent<CanvasGroup>();
            _hudGroup.alpha = 0f;

            Color bgDark    = H("#111827DD");
            Color accent    = H("#00D9FF");
            Color textPri   = H("#F9FAFB");
            Color textSec   = H("#9CA3AF");
            Color btnCopy   = H("#1D4ED8");

            // Top-left card
            var card = new GameObject("RoomCodePanel");
            card.transform.SetParent(gameObject.transform, false);
            var cardRT = card.AddComponent<RectTransform>();
            cardRT.anchorMin = cardRT.anchorMax = new Vector2(0f, 1f);
            cardRT.pivot     = new Vector2(0f, 1f);
            cardRT.anchoredPosition = new Vector2(16f, -16f);
            cardRT.sizeDelta = new Vector2(230, 110);
            card.AddComponent<Image>().color = bgDark;

            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(12, 12, 8, 8);
            vl.spacing = 3; vl.childControlWidth = true; vl.childControlHeight = false;
            vl.childForceExpandWidth = true;

            // Mode label
            _modeLabel = Txt(card, "ModeLabel", "HOST", 11, FontStyle.Bold, textSec, TextAnchor.MiddleLeft);
            _modeLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 16;

            // Room code
            _roomCodeText = Txt(card, "RoomCodeText", "------", 24, FontStyle.Bold, accent, TextAnchor.MiddleLeft);
            _roomCodeText.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;

            // Player count
            _playerCountText = Txt(card, "PlayerCountText", "Players: 1", 11, FontStyle.Normal, textSec, TextAnchor.MiddleLeft);
            _playerCountText.gameObject.AddComponent<LayoutElement>().preferredHeight = 15;

            // Copy row
            var copyRow = new GameObject("CopyRow");
            copyRow.transform.SetParent(card.transform, false);
            copyRow.AddComponent<RectTransform>().sizeDelta = new Vector2(0, 26);
            copyRow.AddComponent<LayoutElement>().preferredHeight = 26;
            var hl = copyRow.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 8; hl.childControlWidth = false; hl.childControlHeight = true;

            // Copy button
            var copyBtnGO = new GameObject("CopyButton");
            copyBtnGO.transform.SetParent(copyRow.transform, false);
            copyBtnGO.AddComponent<RectTransform>().sizeDelta = new Vector2(100, 24);
            var cImg = copyBtnGO.AddComponent<Image>(); cImg.color = btnCopy;
            _copyButton = copyBtnGO.AddComponent<Button>();
            var cCol = _copyButton.colors;
            cCol.normalColor = btnCopy; cCol.highlightedColor = btnCopy * 1.3f;
            cCol.pressedColor = btnCopy * 0.75f; _copyButton.colors = cCol;
            _copyButton.targetGraphic = cImg;
            var copyLbl = Txt(copyBtnGO, "Label", "Copy Code", 11, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            Stretch(copyLbl.GetComponent<RectTransform>());

            // Copy feedback
            _copyFeedbackText = Txt(copyRow, "CopyFeedbackText", "Copied!", 11, FontStyle.Bold, accent, TextAnchor.MiddleLeft);
            _copyFeedbackText.GetComponent<RectTransform>().sizeDelta = new Vector2(60, 24);
            _copyFeedbackText.gameObject.SetActive(false);

            Debug.Log("[RoomCodeHUD] Runtime self-build complete.");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out Color c); return c; }

        private static Font BFont()
        {
            Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return f != null ? f : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static Text Txt(GameObject p, string n, string t, int sz, FontStyle fs, Color c, TextAnchor al)
        {
            var go = new GameObject(n); go.transform.SetParent(p.transform, false);
            go.AddComponent<RectTransform>().sizeDelta = new Vector2(200, 20);
            var tx = go.AddComponent<Text>();
            tx.font = BFont(); tx.text = t; tx.fontSize = sz; tx.fontStyle = fs;
            tx.color = c; tx.alignment = al;
            tx.horizontalOverflow = HorizontalWrapMode.Overflow;
            tx.verticalOverflow   = VerticalWrapMode.Overflow;
            return tx;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

#if UNITY_EDITOR
        [ContextMenu("Build Room Code HUD (Editor Only)")]
        private void BuildHUDInEditor() => RoomCodeHUDBuilder.Build(this);
#endif
    }
}
