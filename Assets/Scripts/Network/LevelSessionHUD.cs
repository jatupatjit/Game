using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// In-level player list (UI kit mockup 3): dark rounded panel top-left, one row per player with the
    /// round face in that player's body colour, the name ("You" for the local player) and the icon of the
    /// card that player chose. Hovering a card icon shows the card's ability (UITooltip). Reads only
    /// replicated state (PlayerExpeditionState.Card / DisplayName, OwnerClientId).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class LevelSessionHUD : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _hudGroup;
        // Scene-serialized text of the previous session card; kept (hidden) so the scenes need no re-save.
        [SerializeField] private Text _modeLabel;
        [SerializeField] private Text _playerCountText;
        [SerializeField] private Text _playerNamesText;
        [SerializeField, Min(0.1f)] private float _refreshInterval = 0.2f;

        private const float CardWidth = 340f, RowHeight = 54f, IconSize = 48f;
        private const int NameFontSize = 21;

        private RectTransform _panel;
        private RectTransform _rows;
        private readonly List<Row> _rowPool = new List<Row>();
        private float _nextRefreshTime;
        private bool _hudVisible = true;

        private sealed class Row
        {
            public GameObject Root;
            public Image Face, Card;
            public Text Name;
            public RectTransform StaminaFill, StaminaChip;
            public Image StaminaFillImage, StaminaChipImage, StaminaBorder, StaminaGloss, StaminaBolt;
            public UITooltip Tooltip;
            // Shown = drawn fill (eases to Target), Chip = pale trail that waits then follows a drop.
            public float Target = 1f, Shown = 1f, Chip = 1f, ChipHoldUntil;
            public ulong Client = ulong.MaxValue;
        }

        private static readonly Color StaminaFull = new(.38f, .88f, .42f), StaminaMid = new(1f, .80f, .28f), StaminaLow = new(1f, .36f, .30f);
        private static readonly Color BarBorder = new(.07f, .07f, .10f, .95f), BarTrack = new(.16f, .16f, .21f, 1f);

        private void Awake()
        {
            if (_hudGroup == null)
                _hudGroup = GetComponent<CanvasGroup>();
            if (_hudGroup != null)
            {
                _hudGroup.interactable = false;
                _hudGroup.blocksRaycasts = true; // card icons need hover; every other graphic ignores raycasts
            }
            Build();
        }

        private void Build()
        {
            foreach (Image image in GetComponentsInChildren<Image>(true))
            {
                image.raycastTarget = false;
                if (_panel == null && !image.name.Contains("Divider")) _panel = image.rectTransform;
            }
            if (_panel == null)
            {
                var panelGo = new GameObject("SessionPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
                panelGo.transform.SetParent(transform, false);
                _panel = (RectTransform)panelGo.transform;
                _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 1f);
                _panel.pivot = new Vector2(0f, 1f);
                _panel.anchoredPosition = new Vector2(20f, -20f);
                panelGo.GetComponent<Image>().raycastTarget = false;
            }
            UITheme.StylePanel(_panel.GetComponent<Image>());
            foreach (Transform child in _panel) child.gameObject.SetActive(false); // old header/count/names

            var rows = new GameObject("PlayerRows", typeof(RectTransform), typeof(VerticalLayoutGroup));
            rows.transform.SetParent(_panel, false);
            _rows = (RectTransform)rows.transform;
            var layout = rows.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            UITheme.CompactCard(_panel, CardWidth);
            var cardLayout = _panel.GetComponent<VerticalLayoutGroup>();
            if (cardLayout != null) cardLayout.padding = new RectOffset(18, 18, 16, 16);
        }

        private Row CreateRow()
        {
            var go = new GameObject("PlayerRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(_rows, false);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            UITheme.SetPreferredHeight(go, RowHeight);

            var row = new Row { Root = go };
            row.Face = Icon(go.transform, "Face");
            // Name with that player's stamina bar under it.
            var col = new GameObject("NameCol", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            col.transform.SetParent(go.transform, false);
            var colLayout = col.GetComponent<VerticalLayoutGroup>();
            colLayout.spacing = 3f;
            colLayout.childAlignment = TextAnchor.MiddleLeft;
            colLayout.childControlWidth = colLayout.childControlHeight = true;
            colLayout.childForceExpandWidth = true;
            colLayout.childForceExpandHeight = false;
            var colElement = col.GetComponent<LayoutElement>();
            colElement.flexibleWidth = 1f;
            colElement.preferredHeight = RowHeight;
            row.Name = UITheme.MakeText(col.transform, "Name", "", NameFontSize, UITextRole.Primary);
            row.Name.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.SetWeight(row.Name, UITheme.FontSemiBold);
            var nameElement = row.Name.gameObject.AddComponent<LayoutElement>();
            nameElement.preferredHeight = 26f;
            BuildStaminaBar(col.transform, row);
            row.Card = Icon(go.transform, "CardIcon");
            row.Card.raycastTarget = true; // hover target
            row.Tooltip = row.Card.gameObject.AddComponent<UITooltip>();
            row.Tooltip.Side = new Vector2(1f, 0f);
            return row;
        }

        /// <summary>Stamina bar: dark rim, dark track, pale "chip" trail behind the coloured fill (shows what was just
        /// spent), a gloss strip on the top half of the fill and quarter ticks drawn over everything.</summary>
        private static void BuildStaminaBar(Transform parent, Row row)
        {
            Sprite round = ProceduralUIUtility.GetRoundedSprite(6, 0);
            var line = new GameObject("StaminaLine", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            line.transform.SetParent(parent, false);
            line.GetComponent<LayoutElement>().preferredHeight = 16f;
            var lineLayout = line.GetComponent<HorizontalLayoutGroup>();
            lineLayout.spacing = 4f;
            lineLayout.childAlignment = TextAnchor.MiddleLeft;
            lineLayout.childControlWidth = lineLayout.childControlHeight = true;
            lineLayout.childForceExpandWidth = lineLayout.childForceExpandHeight = false;

            var bolt = new GameObject("Bolt", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            bolt.transform.SetParent(line.transform, false);
            var boltElement = bolt.GetComponent<LayoutElement>();
            boltElement.preferredWidth = boltElement.minWidth = 14f;
            boltElement.preferredHeight = boltElement.minHeight = 16f;
            row.StaminaBolt = Shape(bolt, BoltSprite(), StaminaFull);
            row.StaminaBolt.type = Image.Type.Simple;
            row.StaminaBolt.preserveAspect = true;

            var bar = new GameObject("StaminaBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            bar.transform.SetParent(line.transform, false);
            var barElement = bar.GetComponent<LayoutElement>();
            barElement.preferredHeight = 15f;
            barElement.flexibleWidth = 1f;
            row.StaminaBorder = Shape(bar, round, BarBorder);

            var track = Child(bar.transform, "Track", round, BarTrack, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var chip = Child(track, "Chip", round, new Color(1f, .97f, .88f, .9f), Vector2.zero, Vector2.zero);
            row.StaminaChip = (RectTransform)chip;
            row.StaminaChipImage = chip.GetComponent<Image>();
            var fill = Child(track, "Fill", round, StaminaFull, Vector2.zero, Vector2.zero);
            row.StaminaFill = (RectTransform)fill;
            row.StaminaFillImage = fill.GetComponent<Image>();
            var gloss = Child(fill, "Gloss", round, new Color(1f, 1f, 1f, .32f), new Vector2(2f, 0f), new Vector2(-2f, -1f));
            var glossRect = (RectTransform)gloss;
            glossRect.anchorMin = new Vector2(0f, .55f);
            row.StaminaGloss = gloss.GetComponent<Image>();
            for (int i = 1; i < 4; i++)
            {
                var tick = Child(track, "Tick" + i, null, new Color(0f, 0f, 0f, .45f), new Vector2(-1f, 1f), new Vector2(1f, -1f));
                var tickRect = (RectTransform)tick;
                tickRect.anchorMin = new Vector2(i * .25f, 0f);
                tickRect.anchorMax = new Vector2(i * .25f, 1f);
            }
        }

        private static Sprite s_bolt;

        /// <summary>Lightning bolt icon (white, tinted with the stamina colour), drawn once.</summary>
        private static Sprite BoltSprite()
        {
            if (s_bolt != null) return s_bolt;
            const int w = 28, h = 32;
            Vector2[] poly = { new(.66f, 1f), new(.12f, .44f), new(.46f, .44f), new(.32f, 0f), new(.88f, .58f), new(.54f, .58f) };
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // 4 sub-samples per pixel for soft edges.
                    int hits = 0;
                    for (int s = 0; s < 4; s++)
                        if (Inside(poly, new Vector2((x + .25f + .5f * (s & 1)) / w, (y + .25f + .5f * (s >> 1)) / h))) hits++;
                    pixels[y * w + x] = new Color32(255, 255, 255, (byte)(hits * 255 / 4));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            s_bolt = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100f);
            return s_bolt;
        }

        private static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        private static Image Shape(GameObject go, Sprite sprite, Color color)
        {
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Transform Child(Transform parent, string name, Sprite sprite, Color color, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
            Shape(go, sprite, color);
            return go.transform;
        }

        private static Color StaminaColor(float v) =>
            v > .5f ? Color.Lerp(StaminaMid, StaminaFull, (v - .5f) / .5f) : Color.Lerp(StaminaLow, StaminaMid, Mathf.Clamp01((v - .15f) / .35f));

        /// <summary>Every frame: ease the fill toward the replicated value, hold the chip trail 0.4 s then let it
        /// slide down, blink the fill and redden the rim when low.</summary>
        private void AnimateStamina()
        {
            float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
            foreach (Row row in _rowPool)
            {
                if (row.Root == null || !row.Root.activeSelf) continue;
                row.Shown = Mathf.MoveTowards(row.Shown, row.Target, dt * Mathf.Max(.35f, Mathf.Abs(row.Target - row.Shown) * 6f));
                if (row.Shown >= row.Chip) { row.Chip = row.Shown; row.ChipHoldUntil = now + .4f; }
                else if (now >= row.ChipHoldUntil) row.Chip = Mathf.MoveTowards(row.Chip, row.Shown, dt * .7f);

                row.StaminaFill.anchorMax = new Vector2(Mathf.Max(.001f, row.Shown), 1f);
                row.StaminaChip.anchorMax = new Vector2(Mathf.Max(.001f, row.Chip), 1f);
                bool empty = row.Shown < .02f;
                if (row.StaminaFillImage.enabled == empty) row.StaminaFillImage.enabled = !empty;
                row.StaminaChipImage.enabled = row.Chip - row.Shown > .005f;

                Color c = StaminaColor(row.Shown);
                bool low = row.Shown < .25f;
                float blink = low ? .5f + .5f * Mathf.Sin(now * 10f) : 0f;
                if (low) c = Color.Lerp(c, Color.white, blink * .35f);
                row.StaminaFillImage.color = c;
                row.StaminaBolt.color = empty ? StaminaLow : c;
                row.StaminaBorder.color = low ? Color.Lerp(BarBorder, new Color(.85f, .15f, .12f, 1f), blink) : BarBorder;
            }
        }

        private static Image Icon(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var element = go.GetComponent<LayoutElement>();
            element.preferredWidth = element.minWidth = IconSize;
            element.preferredHeight = element.minHeight = IconSize;
            var image = go.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private void Update()
        {
            if (CoopGame.Network.GameSettings.Pressed(CoopGame.Network.BindRole.Scoreboard))
                _hudVisible = !_hudVisible;

            NetworkManager manager = NetworkManager.Singleton;
            bool connected = manager != null && manager.IsListening;
            bool shown = connected && _hudVisible && !ExpeditionHUD.OverlayOpen;
            if (_hudGroup != null)
            {
                _hudGroup.alpha = shown ? 1f : 0f;
                _hudGroup.blocksRaycasts = shown;
            }

            if (connected) AnimateStamina();
            if (!connected || Time.unscaledTime < _nextRefreshTime)
                return;
            _nextRefreshTime = Time.unscaledTime + _refreshInterval;
            Refresh();
        }

        private void Refresh()
        {
            if (_rows == null) return;
            var players = new List<PlayerExpeditionState>(FindObjectsByType<PlayerExpeditionState>());
            players.RemoveAll(p => p == null || !p.IsSpawned);
            players.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

            for (int i = 0; i < players.Count; i++)
            {
                if (i >= _rowPool.Count) _rowPool.Add(CreateRow());
                Row row = _rowPool[i];
                PlayerExpeditionState player = players[i];
                byte card = player.Card.Value;
                if (!row.Root.activeSelf) row.Root.SetActive(true);
                var appearance = player.GetComponent<CoopGame.Player.PlayerAppearance>();
                UITheme.StyleIcon(row.Face, appearance != null ? UITheme.FaceForColor(appearance.CurrentBodyColor) : UITheme.FaceForClient(player.OwnerClientId));
                float stamina = Mathf.Clamp01(player.StaminaPercent.Value / 100f);
                row.Target = stamina;
                if (row.Client != player.OwnerClientId)
                {
                    row.Client = player.OwnerClientId;
                    row.Shown = row.Chip = stamina; // a reused row must not animate from the previous player's value
                }
                string name = player.DisplayName.Value.ToString();
                if (string.IsNullOrEmpty(name)) name = $"Player {player.OwnerClientId + 1}";
                if (player.IsOwner) name += " (You)";
                row.Name.text = name;
                UITheme.StyleIcon(row.Card, UITheme.CardIcon(card));
                row.Card.raycastTarget = true;
                row.Tooltip.Text = CardCatalog.Tooltip(card);
            }
            for (int i = players.Count; i < _rowPool.Count; i++)
                if (_rowPool[i].Root.activeSelf) _rowPool[i].Root.SetActive(false);
        }
    }
}
