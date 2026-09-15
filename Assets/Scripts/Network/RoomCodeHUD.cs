using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// RoomCodeHUD - Top-left overlay showing Room Code & connected player names list during an active session.
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
        [SerializeField] private Text        _playerNamesText;
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

            var mgr = SteamLobbyManager.Instance;
            if (mgr != null)
            {
                mgr.OnLobbyMembersChanged += RefreshHUDContent;
            }
        }

        private void OnDestroy()
        {
            var mgr = SteamLobbyManager.Instance;
            if (mgr != null)
            {
                mgr.OnLobbyMembersChanged -= RefreshHUDContent;
            }
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

        public void RefreshHUDContent()
        {
            var nm  = NetworkManager.Singleton;
            var mgr = SteamLobbyManager.Instance;

            if (_roomCodeText != null)
                _roomCodeText.text = (mgr != null && !string.IsNullOrEmpty(mgr.CurrentRoomCode))
                    ? mgr.CurrentRoomCode : "------";

            if (_modeLabel != null && nm != null)
                _modeLabel.text = nm.IsHost ? "● HOST" : (nm.IsServer ? "● SERVER" : "● CLIENT");

            var names = mgr != null ? mgr.GetCurrentPlayerNames() : new List<string>();
            int count = names.Count;
            if (count == 0 && nm != null) count = nm.ConnectedClientsIds.Count;
            if (count == 0) count = 1;

            if (_playerCountText != null)
            {
                _playerCountText.text = $"PLAYERS ({count}/4)";
            }

            if (_playerNamesText != null)
            {
                if (names.Count > 0)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < names.Count; i++)
                    {
                        sb.Append("• ").Append(names[i]);
                        if (i < names.Count - 1) sb.Append("\n");
                    }
                    _playerNamesText.text = sb.ToString();
                }
                else
                {
                    _playerNamesText.text = "• Connecting...";
                }
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
            Text btnLabel = _copyButton != null ? _copyButton.GetComponentInChildren<Text>() : null;
            string originalText = btnLabel != null ? btnLabel.text : "📋 Copy Code";

            if (_copyFeedbackText != null)
            {
                _copyFeedbackText.gameObject.SetActive(true);
                _copyFeedbackText.text = "✓ Copied!";
            }
            if (btnLabel != null)
            {
                btnLabel.text = "✓ Copied!";
            }

            yield return new WaitForSecondsRealtime(1.5f);

            if (_copyFeedbackText != null)
                _copyFeedbackText.gameObject.SetActive(false);
            if (btnLabel != null)
                btnLabel.text = originalText;
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

            // Top-left HUD Card (250 x 190)
            var card = ProceduralUIUtility.MakePanel(gameObject, "RoomCodePanel", new Vector2(250, 190), Vector2.zero, ProceduralUIUtility.PanelDark);
            var cardRT = card.GetComponent<RectTransform>();
            cardRT.anchorMin = cardRT.anchorMax = new Vector2(0f, 1f);
            cardRT.pivot     = new Vector2(0f, 1f);
            cardRT.anchoredPosition = new Vector2(20f, -20f);

            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(16, 16, 12, 12);
            vl.spacing = 6;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            vl.childAlignment = TextAnchor.UpperLeft;

            // 1. Header Row (Room code tag + Host/Client tag)
            var headerRow = ProceduralUIUtility.MakeEmpty(card, "HeaderRow");
            var headerRowLE = headerRow.AddComponent<LayoutElement>();
            headerRowLE.preferredHeight = 18;
            var headerHL = headerRow.AddComponent<HorizontalLayoutGroup>();
            headerHL.childControlWidth = false;
            headerHL.childControlHeight = true;
            headerHL.childForceExpandWidth = false;
            headerHL.spacing = 8;

            var headerTitle = ProceduralUIUtility.MakeText(headerRow, "HeaderTitle", "ROOM CODE", 10, FontStyle.Bold, ProceduralUIUtility.AccentCyan, TextAnchor.MiddleLeft);
            var htRT = headerTitle.GetComponent<RectTransform>();
            htRT.sizeDelta = new Vector2(90, 18);
            var htLE = headerTitle.gameObject.AddComponent<LayoutElement>();
            htLE.preferredWidth = 90;

            _modeLabel = ProceduralUIUtility.MakeText(headerRow, "ModeLabel", "● HOST", 10, FontStyle.Bold, ProceduralUIUtility.AccentEmerald, TextAnchor.MiddleRight);
            var mtRT = _modeLabel.GetComponent<RectTransform>();
            mtRT.sizeDelta = new Vector2(110, 18);
            var mtLE = _modeLabel.gameObject.AddComponent<LayoutElement>();
            mtLE.preferredWidth = 110;

            // 2. Room code text
            _roomCodeText = ProceduralUIUtility.MakeText(card, "RoomCodeText", "------", 24, FontStyle.Bold, ProceduralUIUtility.TextPrimary, TextAnchor.MiddleLeft);
            var codeLE = _roomCodeText.gameObject.AddComponent<LayoutElement>();
            codeLE.preferredHeight = 30;

            // 3. Divider line
            var div = ProceduralUIUtility.MakeImage(card, "Divider", ProceduralUIUtility.HexColor("#334155"));
            var divLE = div.gameObject.AddComponent<LayoutElement>();
            divLE.preferredHeight = 1;

            // 4. Players section title
            _playerCountText = ProceduralUIUtility.MakeText(card, "PlayerCountText", "PLAYERS (1/4)", 10, FontStyle.Bold, ProceduralUIUtility.AccentCyan, TextAnchor.MiddleLeft);
            var countLE = _playerCountText.gameObject.AddComponent<LayoutElement>();
            countLE.preferredHeight = 16;

            // 5. Player names list
            _playerNamesText = ProceduralUIUtility.MakeText(card, "PlayerNamesText", "• Player (Host)", 11, FontStyle.Normal, ProceduralUIUtility.TextPrimary, TextAnchor.UpperLeft);
            var namesLE = _playerNamesText.gameObject.AddComponent<LayoutElement>();
            namesLE.preferredHeight = 44;

            // 6. Copy button row
            var copyRow = ProceduralUIUtility.MakeEmpty(card, "CopyRow");
            var copyRowLE = copyRow.AddComponent<LayoutElement>();
            copyRowLE.preferredHeight = 28;
            var copyHL = copyRow.AddComponent<HorizontalLayoutGroup>();
            copyHL.spacing = 8;
            copyHL.childControlWidth = false;
            copyHL.childControlHeight = true;

            var (copyBtnGO, copyBtn) = ProceduralUIUtility.MakeButton(copyRow, "CopyButton", "📋 Copy Code", ProceduralUIUtility.ButtonSlate, 28, 11);
            var copyBtnRT = copyBtnGO.GetComponent<RectTransform>();
            copyBtnRT.sizeDelta = new Vector2(105, 28);
            var copyBtnLE = copyBtnGO.GetComponent<LayoutElement>();
            if (copyBtnLE != null) copyBtnLE.preferredWidth = 105;
            _copyButton = copyBtn;

            _copyFeedbackText = ProceduralUIUtility.MakeText(copyRow, "CopyFeedbackText", "✓ Copied!", 11, FontStyle.Bold, ProceduralUIUtility.AccentEmerald, TextAnchor.MiddleLeft);
            var fbRT = _copyFeedbackText.GetComponent<RectTransform>();
            fbRT.sizeDelta = new Vector2(80, 28);
            var fbLE = _copyFeedbackText.gameObject.AddComponent<LayoutElement>();
            fbLE.preferredWidth = 80;
            _copyFeedbackText.gameObject.SetActive(false);

            Debug.Log("[RoomCodeHUD] Runtime self-build complete.");
        }

#if UNITY_EDITOR
        [ContextMenu("Build Room Code HUD (Editor Only)")]
        private void BuildHUDInEditor() => RoomCodeHUDBuilder.Build(this);
#endif
    }
}
