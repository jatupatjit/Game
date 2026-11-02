using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// End-of-delivery result card (cute kit, George's mockup 2026-11-02): 1-3 stars over a wooden plank title, a
    /// beige card with cargo HP / time / coins rows, an optional shop row, then "เดินต่อ" and the host-only
    /// "กลับ Lobby พร้อมกัน". Presentation only: <see cref="ExpeditionHUD"/> owns the data and the button actions.
    /// <see cref="ExtraContent"/> is an empty layout slot under the stat rows for per-player stats / awards.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionCompleteUI : MonoBehaviour
    {
        private static MissionCompleteUI _instance;
        private GameObject _root;
        private readonly Image[] _stars = new Image[3];
        private Text _title, _hpValue, _timeValue, _coinValue, _wallet;
        private GameObject _shopRow;
        private readonly Button[] _buy = new Button[2];
        private Button _continue, _toLobby;
        private Text _toLobbyLabel;

        public static bool IsVisible => _instance != null && _instance._root != null && _instance._root.activeSelf;

        /// <summary>Vertical layout slot under the stat rows (empty, hidden while it has no children).</summary>
        public Transform ExtraContent { get; private set; }

        public static MissionCompleteUI EnsureInstance()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("MissionCompleteUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            return go.AddComponent<MissionCompleteUI>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 220; // above ExpeditionHUD (210), below MissionFailUI (300)
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            Build();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>Button actions; buy receives the item id (1 elixir, 2 padding).</summary>
        public void Bind(UnityAction onContinue, UnityAction onToLobby, UnityAction<byte> onBuy)
        {
            _continue.onClick.RemoveAllListeners();
            _continue.onClick.AddListener(onContinue);
            _toLobby.onClick.RemoveAllListeners();
            if (onToLobby != null) _toLobby.onClick.AddListener(onToLobby);
            for (byte i = 0; i < _buy.Length; i++)
            {
                byte id = (byte)(i + 1);
                _buy[i].onClick.RemoveAllListeners();
                _buy[i].onClick.AddListener(() => onBuy?.Invoke(id));
            }
        }

        public void SetVisible(bool visible)
        {
            if (_root.activeSelf != visible) _root.SetActive(visible);
            if (visible && ExtraContent.gameObject.activeSelf != ExtraContent.childCount > 0)
                ExtraContent.gameObject.SetActive(ExtraContent.childCount > 0);
        }

        /// <summary>Result numbers. <paramref name="seconds"/> &lt; 0 shows "--:--".</summary>
        public void SetResult(int hp, int maxHp, float seconds, int coins, int stars)
        {
            _hpValue.text = $"{hp}/{maxHp}";
            _timeValue.text = seconds < 0f ? "--:--" : $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";
            _coinValue.text = coins > 0 ? "+" + coins : coins.ToString();
            for (int i = 0; i < _stars.Length; i++)
                _stars[i].color = i < stars ? Color.white : StarEmpty;
        }

        /// <summary>Shop row (levels with an exit portal). Null wallet hides it.</summary>
        public void SetShop(int? wallet, bool canBuyElixir, bool canBuyPadding)
        {
            bool show = wallet.HasValue;
            if (_shopRow.activeSelf != show) _shopRow.SetActive(show);
            if (!show) return;
            _wallet.text = $"เงิน {wallet.Value} • ซื้อไอเทมก่อนเข้าประตู";
            _buy[0].interactable = canBuyElixir;
            _buy[1].interactable = canBuyPadding;
        }

        /// <summary>Host-only Lobby button (last level). Hidden when <paramref name="show"/> is false.</summary>
        public void SetLobbyButton(bool show, bool interactable, string label)
        {
            if (_toLobby.gameObject.activeSelf != show) _toLobby.gameObject.SetActive(show);
            _toLobby.interactable = interactable;
            if (_toLobbyLabel.text != label) _toLobbyLabel.text = label;
            // one button centred, two side by side
            ((RectTransform)_continue.transform).anchoredPosition = new Vector2(show ? -190f : 0f, ButtonY);
        }

        // ── Layout ───────────────────────────────────────────────────────────
        private const float CardWidth = 700f, ButtonY = -34f;
        private static readonly Color StarEmpty = new Color(1f, 1f, 1f, 0.28f);
        private static readonly Color RowLine = new Color(0.18f, 0.23f, 0.45f, 0.12f);

        private void Build()
        {
            _root = new GameObject("Mission Complete", typeof(RectTransform), typeof(Image));
            _root.transform.SetParent(transform, false);
            Stretch((RectTransform)_root.transform);
            _root.GetComponent<Image>().color = Color.clear; // ExpeditionHUD's modal already dims the game; this only blocks clicks

            // Card: fits its rows; plank, stars, tapes and buttons hang off its edges.
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            card.transform.SetParent(_root.transform, false);
            var cardRect = (RectTransform)card.transform;
            cardRect.sizeDelta = new Vector2(CardWidth, 400f);
            cardRect.anchoredPosition = new Vector2(0f, -20f);
            UITheme.StyleCuteCard(card.GetComponent<Image>());
            var layout = card.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(70, 70, 92, 92);
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            card.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = UITheme.MakeCutePlank(card.transform, "ส่งพัสดุสำเร็จ", 470f, 118f, 0f, 46);
            UITheme.SetWeight(_title, UITheme.FontBold);
            BuildStars(card.transform);
            UITheme.AddCuteTape(card.transform, 4, new Vector2(0f, 0f), new Vector2(24f, 40f), 35f, 104f);
            UITheme.AddCuteTape(card.transform, 6, new Vector2(1f, 1f), new Vector2(-30f, -46f), -30f, 100f);

            _hpValue = StatRow(card.transform, "พัสดุ HP");
            _timeValue = StatRow(card.transform, "เวลา");
            _coinValue = StatRow(card.transform, "เหรียญที่ได้รับ", false);

            var extra = new GameObject("ExtraContent", typeof(RectTransform), typeof(VerticalLayoutGroup));
            extra.transform.SetParent(card.transform, false);
            var extraLayout = extra.GetComponent<VerticalLayoutGroup>();
            extraLayout.spacing = 6f;
            extraLayout.childControlWidth = extraLayout.childControlHeight = true;
            extraLayout.childForceExpandWidth = true;
            extraLayout.childForceExpandHeight = false;
            extra.SetActive(false);
            ExtraContent = extra.transform;

            BuildShop(card.transform);

            // Buttons straddle the card's bottom edge.
            var buttons = new GameObject("Buttons", typeof(RectTransform), typeof(LayoutElement));
            buttons.transform.SetParent(card.transform, false);
            buttons.GetComponent<LayoutElement>().ignoreLayout = true;
            var buttonsRect = (RectTransform)buttons.transform;
            buttonsRect.anchorMin = buttonsRect.anchorMax = new Vector2(.5f, 0f);
            buttonsRect.sizeDelta = Vector2.zero;
            _continue = UITheme.MakeButton(buttons.transform, "Continue", "เดินต่อ / ENTER", UIButtonStyle.Pink, new Vector2(350f, 92f), 32);
            _toLobby = UITheme.MakeButton(buttons.transform, "ToLobby", "กลับ Lobby พร้อมกัน", UIButtonStyle.White, new Vector2(350f, 92f), 30);
            _toLobbyLabel = _toLobby.GetComponentInChildren<Text>(true);
            ((RectTransform)_continue.transform).anchoredPosition = new Vector2(0f, ButtonY);
            ((RectTransform)_toLobby.transform).anchoredPosition = new Vector2(190f, ButtonY);
            _toLobby.gameObject.SetActive(false);

            _root.SetActive(false);
        }

        private void BuildStars(Transform card)
        {
            var row = new GameObject("Stars", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(card, false);
            row.GetComponent<LayoutElement>().ignoreLayout = true;
            var rect = (RectTransform)row.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, 92f);
            rect.sizeDelta = Vector2.zero;
            for (int i = 0; i < 3; i++)
            {
                var star = new GameObject("Star" + (i + 1), typeof(RectTransform), typeof(Image));
                star.transform.SetParent(row.transform, false);
                var starRect = (RectTransform)star.transform;
                bool middle = i == 1;
                starRect.sizeDelta = Vector2.one * (middle ? 132f : 108f);
                starRect.anchoredPosition = new Vector2((i - 1) * 118f, middle ? 14f : -6f);
                starRect.localRotation = Quaternion.Euler(0f, 0f, (1 - i) * 12f);
                _stars[i] = star.GetComponent<Image>();
                _stars[i].sprite = StarSprite();
                _stars[i].preserveAspect = true;
                _stars[i].raycastTarget = false;
            }
        }

        /// <summary>Label left, value right, thin rule under it. Returns the value text.</summary>
        internal static Text StatRow(Transform card, string label, bool rule = true)
        {
            var row = new GameObject(label, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(card, false);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(6, 6, 0, 0);
            UITheme.SetPreferredHeight(row, 64f);
            Text name = UITheme.MakeText(row.transform, "Label", label, 34, UITextRole.OnLight);
            UITheme.StyleCuteText(name);
            UITheme.SetWeight(name, UITheme.FontMedium);
            Text value = UITheme.MakeText(row.transform, "Value", "--", 36, UITextRole.OnLight, TextAnchor.MiddleRight);
            UITheme.StyleCuteText(value);
            UITheme.SetWeight(value, UITheme.FontSemiBold);
            if (rule)
            {
                var line = new GameObject("Rule", typeof(RectTransform), typeof(Image));
                line.transform.SetParent(card, false);
                line.GetComponent<Image>().color = RowLine;
                line.GetComponent<Image>().raycastTarget = false;
                UITheme.SetPreferredHeight(line, 2f);
            }
            return value;
        }

        private void BuildShop(Transform card)
        {
            _shopRow = new GameObject("Shop", typeof(RectTransform), typeof(VerticalLayoutGroup));
            _shopRow.transform.SetParent(card, false);
            var layout = _shopRow.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 18, 0);
            layout.spacing = 8f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            _wallet = UITheme.MakeText(_shopRow.transform, "Wallet", "", 22, UITextRole.OnLight, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(_wallet, true);
            UITheme.SetPreferredHeight(_wallet.gameObject, 30f);
            var buttons = new GameObject("Items", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            buttons.transform.SetParent(_shopRow.transform, false);
            var row = buttons.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 14f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = false;
            UITheme.SetPreferredHeight(buttons, 76f);
            _buy[0] = UITheme.MakeButton(buttons.transform, "BuyElixir", "Stamina Elixir • 40", UIButtonStyle.Blue, new Vector2(270f, 76f), 22);
            _buy[1] = UITheme.MakeButton(buttons.transform, "BuyPadding", "Cargo Padding • 60", UIButtonStyle.Blue, new Vector2(270f, 76f), 22);
            foreach (Button buy in _buy) UITheme.SetPreferredHeight(buy.gameObject, 76f);
            _shopRow.SetActive(false);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        // ── Star art (no star sprite in the kit): yellow five-point star with a dark rim and a soft highlight ──
        private static Sprite _star;

        public static Sprite StarSprite()
        {
            if (_star != null) return _star;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
            };
            var outer = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float angle = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float radius = i % 2 == 0 ? 60f : 27f;
                outer[i] = new Vector2(64f + Mathf.Cos(angle) * radius, 61f + Mathf.Sin(angle) * radius);
            }
            Color rim = new Color(0.45f, 0.27f, 0.05f), fill = new Color(1f, 0.79f, 0.16f), shine = new Color(1f, 0.93f, 0.55f);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + .5f, y + .5f);
                float d = SignedDistance(p, outer); // negative inside
                float alpha = Mathf.Clamp01(.5f - d);
                if (alpha <= 0f) { pixels[y * size + x] = Color.clear; continue; }
                Color c = d > -5f ? rim : (p.y > 72f && p.x < 70f ? shine : fill);
                if (d <= -5f && d > -6f) c = Color.Lerp(rim, fill, -5f - d);
                c.a = alpha;
                pixels[y * size + x] = c;
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _star = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
            return _star;
        }

        private static float SignedDistance(Vector2 p, Vector2[] poly)
        {
            float best = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside ? -best : best;
        }
    }
}
