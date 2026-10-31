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

        /// <summary>
        /// Replaces the scene-built (or runtime-built) card with the cute kit layout at runtime, so Lobby.unity
        /// needs no re-save. Behaviour is untouched; the text references are re-pointed at the new card.
        /// </summary>
        internal void ApplyTheme()
        {
            BuildCuteCard();
        }

        // ── Cute kit lobby card (Canva page 68): blue room-code block + light player list with 4 slots ──
        private const int MaxSlots = 4;
        private const float CuteWidth = 340f, CodeBlockHeight = 124f, RowHeight = 46f, RowGap = 8f;
        private readonly Image[] _rowBg = new Image[MaxSlots], _rowIcon = new Image[MaxSlots], _rowFace = new Image[MaxSlots];
        private readonly Text[] _rowName = new Text[MaxSlots], _rowHost = new Text[MaxSlots];
        private bool _cuteBuilt;

        private void BuildCuteCard()
        {
            if (_cuteBuilt) return;
            _cuteBuilt = true;
            // Replace the old dark card (scene-built or runtime-built) with the kit layout; the text fields are re-pointed.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject old = transform.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }
            _copyButton = null; _copyFeedbackText = null; _playerNamesText = null;

            var root = new GameObject("CuteRoomCard", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = rootRect.anchorMax = rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = new Vector2(24f, -24f);
            float listHeight = 62f + MaxSlots * RowHeight + (MaxSlots - 1) * RowGap + 22f;
            rootRect.sizeDelta = new Vector2(CuteWidth, CodeBlockHeight + listHeight - 18f);

            // Player list (drawn first so the code block overlaps its top).
            Image list = Panel(root.transform, "PlayerList", UITheme.CutePlayerListSprite,
                new Vector2(10f, -(CodeBlockHeight - 18f)), new Vector2(CuteWidth - 20f, listHeight));
            _playerCountText = UITheme.MakeText(list.transform, "PlayerCountText", "PLAYERS (1/4)", 17, UITextRole.OnLight);
            UITheme.StyleCuteText(_playerCountText, true);
            UITheme.SetWeight(_playerCountText, UITheme.FontMedium);
            Place(_playerCountText.rectTransform, new Vector2(22f, -26f), new Vector2(CuteWidth - 60f, 28f));
            for (int i = 0; i < MaxSlots; i++)
            {
                float y = -62f - i * (RowHeight + RowGap);
                _rowBg[i] = Panel(list.transform, "Row" + (i + 1), UITheme.CutePlayerRowSprite(false), new Vector2(12f, y), new Vector2(CuteWidth - 44f, RowHeight));
                _rowIcon[i] = Panel(_rowBg[i].transform, "Icon", UITheme.CutePlayerIcon(false), new Vector2(8f, -5f), new Vector2(36f, 36f));
                _rowIcon[i].type = Image.Type.Simple;
                _rowFace[i] = Panel(_rowIcon[i].transform, "Face", UITheme.FaceForClient((ulong)i), new Vector2(5f, -5f), new Vector2(26f, 26f));
                _rowFace[i].type = Image.Type.Simple; _rowFace[i].preserveAspect = true;
                _rowName[i] = UITheme.MakeText(_rowBg[i].transform, "Name", "Waiting...", 17, UITextRole.OnLight);
                UITheme.StyleCuteText(_rowName[i]);
                _rowName[i].horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(_rowName[i].rectTransform, new Vector2(54f, -RowHeight * 0.5f + 1f), new Vector2(180f, 30f), true);
                _rowHost[i] = UITheme.MakeText(_rowBg[i].transform, "Host", "HOST", 14, UITextRole.OnLight, TextAnchor.MiddleRight);
                UITheme.SetWeight(_rowHost[i], UITheme.FontSemiBold);
                _rowHost[i].color = new Color(0.85f, 0.55f, 0f);
                Place(_rowHost[i].rectTransform, new Vector2(CuteWidth - 44f - 70f, -RowHeight * 0.5f + 1f), new Vector2(58f, 24f), true);
            }
            UITheme.AddCuteTape(list.transform, 6, new Vector2(0f, 0f), new Vector2(4f, 10f), 35f, 64f);

            // Room code block.
            Image code = Panel(root.transform, "RoomCodeBlock", UITheme.CuteRoomCodeSprite, Vector2.zero, new Vector2(CuteWidth, CodeBlockHeight));
            Text heading = UITheme.MakeText(code.transform, "HeaderTitle", "ROOM CODE", 16, UITextRole.Secondary);
            heading.color = new Color(0.82f, 0.88f, 1f);
            Place(heading.rectTransform, new Vector2(24f, -30f), new Vector2(200f, 24f), true);
            _roomCodeText = UITheme.MakeText(code.transform, "RoomCodeText", "------", 40, UITextRole.Primary);
            UITheme.SetWeight(_roomCodeText, UITheme.FontSemiBold);
            _roomCodeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(_roomCodeText.rectTransform, new Vector2(24f, -72f), new Vector2(220f, 48f), true);
            Image badge = Panel(code.transform, "ModeBadge", UITheme.Kit("CuteButtonGreen"), new Vector2(CuteWidth - 116f, -46f), new Vector2(96f, 42f));
            _modeLabel = UITheme.MakeText(badge.transform, "ModeLabel", "HOST", 16, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.SetWeight(_modeLabel, UITheme.FontSemiBold);
            ProceduralUIUtility.StretchFull(_modeLabel.rectTransform);
            _modeLabel.rectTransform.offsetMin = new Vector2(0f, 3f);
            UITheme.AddCuteTape(code.transform, 5, new Vector2(0f, 1f), new Vector2(10f, -8f), 35f, 62f);
        }

        private static Image Panel(Transform parent, string name, Sprite sprite, Vector2 topLeft, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
            Image image = go.GetComponent<Image>();
            UITheme.StyleShape(image, sprite, Color.white);
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Top-left placement; <paramref name="midLeft"/> pins the left-centre at <paramref name="pos"/> instead.</summary>
        private static void Place(RectTransform rect, Vector2 pos, Vector2 size, bool midLeft = false)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = midLeft ? new Vector2(0f, 0.5f) : new Vector2(0f, 1f);
            rect.anchoredPosition = midLeft ? pos : pos + new Vector2(0f, size.y * 0.5f);
            rect.sizeDelta = size;
        }

        /// <summary>Fills the 4 player slots: local player shows "You", the host row a HOST tag, empty slots "Waiting...".</summary>
        private void RefreshRows(List<string> names, bool local)
        {
            if (!_cuteBuilt) return;
            var mgr = SteamLobbyManager.Instance;
            string self = mgr != null && !local && mgr.IsSteamInitialized ? mgr.SteamPlayerName : null;
            var nm = NetworkManager.Singleton;
            bool selfIsHost = nm != null && nm.IsHost;
            int selfIndex = -1;
            for (int i = 0; i < names.Count && selfIndex < 0; i++)
                if (self != null && StripHost(names[i]) == self) selfIndex = i;
            if (selfIndex < 0 && (local || selfIsHost) && names.Count > 0) selfIndex = 0;

            for (int i = 0; i < MaxSlots; i++)
            {
                if (_rowBg[i] == null) continue;
                bool joined = i < names.Count;
                bool you = joined && i == selfIndex;
                _rowBg[i].sprite = UITheme.CutePlayerRowSprite(you);
                _rowIcon[i].sprite = UITheme.CutePlayerIcon(joined);
                _rowFace[i].enabled = joined;
                _rowName[i].text = !joined ? "Waiting..." : you ? "You" : StripHost(names[i]);
                _rowName[i].color = joined ? UITheme.CuteInk : UITheme.CuteInkSoft;
                _rowHost[i].enabled = joined && names[i].EndsWith(" (Host)");
            }
        }

        private static string StripHost(string name) =>
            name.EndsWith(" (Host)") ? name.Substring(0, name.Length - " (Host)".Length) : name;

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
                _modeLabel.text = local ? "SOLO" : nm.IsHost ? "HOST" : (nm.IsServer ? "SERVER" : "CLIENT");

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
            RefreshRows(names, local);

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
