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

            if (_hudGroup == null || _roomCodeText == null)
                BuildRuntime();
            ApplyTheme();
            // The room code and player list now live in the Pause menu (PauseMenu room-code section).
            // The component stays in the scene (ConnectionHUD treats it as "modern HUD present"), but the
            // card itself is never shown.
            // Card is shown only in the Lobby scene; gameplay scenes never show the room code.
            bool lobby = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Lobby";
            if (!_showCard && !lobby)
            {
                if (_hudGroup != null) { _hudGroup.alpha = 0f; _hudGroup.interactable = false; _hudGroup.blocksRaycasts = false; }
                if (_canvas != null) _canvas.enabled = false;
                enabled = false;
            }
        }

        [Tooltip("Show the always-visible top-left card. Off: the code is shown in the Pause menu instead.")]
        [SerializeField] private bool _showCard = false;

        // Compact, readable card matching LevelSessionHUD (1080p reference pixels; labels ≈11 px at 720p).
        private const float CardWidth = 300f, CopyButtonWidth = 150f, CopyRowHeight = 38f;
        private const int CodeFontSize = 36, NamesFontSize = 17;

        /// <summary>
        /// Restyles the scene-built (or runtime-built) card with <see cref="UITheme"/> and compacts it at
        /// runtime (readable sizes, height fitted to content, standard card padding) so Lobby.unity needs
        /// no re-save. References and behaviour are untouched.
        /// </summary>
        internal void ApplyTheme()
        {
            RectTransform card = null;
            foreach (Image image in GetComponentsInChildren<Image>(true))
            {
                if (image.GetComponent<Button>() != null) continue;
                if (image.name == "Divider")
                {
                    image.color = UITheme.Track;
                    UITheme.SetPreferredHeight(image.gameObject, 2f);
                }
                else
                {
                    UITheme.StylePanel(image);
                    if (card == null) card = image.rectTransform;
                }
            }
            UITheme.ReplaceEmojiIn(transform);
            UITheme.StyleButton(_copyButton, UIButtonStyle.Secondary);
            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.lineSpacing = 1f;
                if (text.GetComponentInParent<Button>(true) != null)
                {
                    text.fontSize = UITheme.CardLabelFontSize;
                    continue;
                }
                UITextRole role = text == _modeLabel || text == _copyFeedbackText ? UITextRole.Positive :
                    text == _roomCodeText || text == _playerNamesText ? UITextRole.Primary : UITextRole.Accent;
                UITheme.StyleText(text, role);
                text.fontSize = text == _roomCodeText ? CodeFontSize : text == _playerNamesText ? NamesFontSize : UITheme.CardLabelFontSize;
                text.fontStyle = text == _playerNamesText ? FontStyle.Normal : FontStyle.Bold;
            }
            if (card != null) CompactLayout(card);
        }

        private void CompactLayout(RectTransform card)
        {
            UITheme.CompactCard(card, CardWidth);
            Transform header = _modeLabel != null ? _modeLabel.transform.parent : null;
            if (header != null && header != card.transform) UITheme.SpreadRow(header, 26f);
            if (_roomCodeText != null) UITheme.SetPreferredHeight(_roomCodeText.gameObject, CodeFontSize + 10f);
            if (_playerCountText != null) UITheme.SetPreferredHeight(_playerCountText.gameObject, 24f);
            if (_playerNamesText != null) UITheme.SetPreferredHeight(_playerNamesText.gameObject, -1f); // fit the list

            Transform copyRow = _copyButton != null ? _copyButton.transform.parent : null;
            if (copyRow != null && copyRow != card.transform)
            {
                UITheme.SetPreferredHeight(copyRow.gameObject, CopyRowHeight);
                SizeInRow((RectTransform)_copyButton.transform, CopyButtonWidth);
                if (_copyFeedbackText != null) SizeInRow(_copyFeedbackText.rectTransform, 120f);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
        }

        private static void SizeInRow(RectTransform rect, float width)
        {
            rect.sizeDelta = new Vector2(width, CopyRowHeight);
            var element = rect.GetComponent<LayoutElement>();
            if (element == null) element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = CopyRowHeight;
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

            bool local = mgr != null && mgr.IsLocalSession;
            if (_roomCodeText != null)
                _roomCodeText.text = local ? "OFFLINE" : (mgr != null && !string.IsNullOrEmpty(mgr.CurrentRoomCode))
                    ? mgr.CurrentRoomCode : "------";

            if (_modeLabel != null && nm != null)
                _modeLabel.text = local ? "● SOLO" : nm.IsHost ? "● HOST" : (nm.IsServer ? "● SERVER" : "● CLIENT");

            if (_copyButton != null)
            {
                // This always-visible card cannot be clicked (the cursor is locked in play), so it shows no Copy
                // button; copying lives in the pause menu. Collapse the whole row so no empty band is left.
                _copyButton.gameObject.SetActive(false);
                Transform copyRow = _copyButton.transform.parent;
                if (copyRow != null && copyRow != transform && copyRow.GetComponent<Image>() == null &&
                    copyRow.gameObject.activeSelf)
                    copyRow.gameObject.SetActive(false);
            }

            var names = mgr != null ? mgr.GetCurrentPlayerNames() : new List<string>();
            int count = names.Count;
            if (count == 0 && nm != null) count = nm.ConnectedClientsIds.Count;
            if (count == 0) count = 1;

            if (_playerCountText != null)
            {
                _playerCountText.text = $"PLAYERS ({count}/{(local ? 1 : 4)})";
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
            string originalText = btnLabel != null ? btnLabel.text : "Copy Code";

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
            var card = ProceduralUIUtility.MakePanel(gameObject, "RoomCodePanel", new Vector2(250, 190), Vector2.zero);
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

            var headerTitle = ProceduralUIUtility.MakeText(headerRow, "HeaderTitle", "ROOM CODE", 10, FontStyle.Bold, UITextRole.Accent, TextAnchor.MiddleLeft);
            var htRT = headerTitle.GetComponent<RectTransform>();
            htRT.sizeDelta = new Vector2(90, 18);
            var htLE = headerTitle.gameObject.AddComponent<LayoutElement>();
            htLE.preferredWidth = 90;

            _modeLabel = ProceduralUIUtility.MakeText(headerRow, "ModeLabel", "● HOST", 10, FontStyle.Bold, UITextRole.Positive, TextAnchor.MiddleRight);
            var mtRT = _modeLabel.GetComponent<RectTransform>();
            mtRT.sizeDelta = new Vector2(110, 18);
            var mtLE = _modeLabel.gameObject.AddComponent<LayoutElement>();
            mtLE.preferredWidth = 110;

            // 2. Room code text
            _roomCodeText = ProceduralUIUtility.MakeText(card, "RoomCodeText", "------", 24, FontStyle.Bold, UITextRole.Primary, TextAnchor.MiddleLeft);
            var codeLE = _roomCodeText.gameObject.AddComponent<LayoutElement>();
            codeLE.preferredHeight = 30;

            // 3. Divider line
            var div = ProceduralUIUtility.MakeImage(card, "Divider", UITheme.Track);
            var divLE = div.gameObject.AddComponent<LayoutElement>();
            divLE.preferredHeight = 1;

            // 4. Players section title
            _playerCountText = ProceduralUIUtility.MakeText(card, "PlayerCountText", "PLAYERS (1/4)", 10, FontStyle.Bold, UITextRole.Accent, TextAnchor.MiddleLeft);
            var countLE = _playerCountText.gameObject.AddComponent<LayoutElement>();
            countLE.preferredHeight = 16;

            // 5. Player names list
            _playerNamesText = ProceduralUIUtility.MakeText(card, "PlayerNamesText", "• Player (Host)", 11, FontStyle.Normal, UITextRole.Primary, TextAnchor.UpperLeft);
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

            var (copyBtnGO, copyBtn) = ProceduralUIUtility.MakeButton(copyRow, "CopyButton", "Copy Code", UIButtonStyle.Secondary, 28, 11);
            var copyBtnRT = copyBtnGO.GetComponent<RectTransform>();
            copyBtnRT.sizeDelta = new Vector2(105, 28);
            var copyBtnLE = copyBtnGO.GetComponent<LayoutElement>();
            if (copyBtnLE != null) copyBtnLE.preferredWidth = 105;
            _copyButton = copyBtn;

            _copyFeedbackText = ProceduralUIUtility.MakeText(copyRow, "CopyFeedbackText", "✓ Copied!", 11, FontStyle.Bold, UITextRole.Positive, TextAnchor.MiddleLeft);
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
