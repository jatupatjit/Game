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

        private const float CardWidth = 340f, RowHeight = 52f, IconSize = 46f;
        private const int NameFontSize = 20;

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
            public Image StaminaFillImage, StaminaChipImage;
            public UITooltip Tooltip;
            // Shown = drawn fill (eases to Target), Chip = pale trail that waits then follows a drop.
            public float Target = 1f, Shown = 1f, Chip = 1f, ChipHoldUntil;
            public ulong Client = ulong.MaxValue;
        }

        // Kit stamina bar (StaminaFrame sprite is 34 px tall) drawn at this scale in the player row.
        private const float BarScale = 22f / 34f;

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
            // Same dark chip as the other in-level HUD boxes (objective, run info, inventory).
            UITheme.StyleShape(_panel.GetComponent<Image>(), UITheme.SmallSprite, UITheme.SolidPanel);
            foreach (Transform child in _panel) child.gameObject.SetActive(false); // old header/count/names

            var rows = new GameObject("PlayerRows", typeof(RectTransform), typeof(VerticalLayoutGroup));
            rows.transform.SetParent(_panel, false);
            _rows = (RectTransform)rows.transform;
            var layout = rows.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            UITheme.CompactCard(_panel, CardWidth);
            var cardLayout = _panel.GetComponent<VerticalLayoutGroup>();
            if (cardLayout != null) cardLayout.padding = new RectOffset(14, 14, 12, 12);
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
            colLayout.spacing = 2f;
            colLayout.childAlignment = TextAnchor.MiddleLeft;
            colLayout.padding = new RectOffset(0, 0, 2, 2);
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
            nameElement.preferredHeight = 24f;
            var nameWrap = row.Name.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            nameWrap.effectColor = new Color(0f, 0f, 0f, .5f);
            nameWrap.effectDistance = new Vector2(1f, -1f);
            BuildStaminaBar(col.transform, row);
            row.Card = Icon(go.transform, "CardIcon");
            row.Card.raycastTarget = true; // hover target
            row.Tooltip = row.Card.gameObject.AddComponent<UITooltip>();
            row.Tooltip.Side = new Vector2(1f, 0f);
            return row;
        }

        /// <summary>Same kit bar as the big stamina bar (PlayerStaminaUI), scaled down: StaminaInner behind, the
        /// ghost trail and yellow fill inside the frame's window, StaminaFrame on top and the carrier icon on the
        /// left cap.</summary>
        private static void BuildStaminaBar(Transform parent, Row row)
        {
            var bar = new GameObject("StaminaBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            bar.transform.SetParent(parent, false);
            var barElement = bar.GetComponent<LayoutElement>();
            barElement.preferredHeight = barElement.minHeight = 34f * BarScale;
            barElement.flexibleWidth = 1f;
            var inner = bar.GetComponent<Image>();
            UITheme.StyleShape(inner, UITheme.StaminaInnerSprite, Color.white);
            inner.pixelsPerUnitMultiplier = 1f / BarScale;
            inner.raycastTarget = false;

            var window = new GameObject("StaminaBar_Window", typeof(RectTransform));
            window.transform.SetParent(bar.transform, false);
            var windowRect = (RectTransform)window.transform;
            windowRect.anchorMin = Vector2.zero; windowRect.anchorMax = Vector2.one;
            RectOffset w = UITheme.StaminaWindow;
            windowRect.offsetMin = new Vector2(w.left * BarScale, w.bottom * BarScale);
            windowRect.offsetMax = new Vector2(-w.right * BarScale, -w.top * BarScale);

            row.StaminaChipImage = FillImage(window.transform, "StaminaBar_Ghost", UITheme.Ghost);
            row.StaminaChip = row.StaminaChipImage.rectTransform;
            row.StaminaFillImage = FillImage(window.transform, "StaminaBar_Fill", UITheme.Stamina);
            row.StaminaFill = row.StaminaFillImage.rectTransform;

            var frame = new GameObject("StaminaBar_Frame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(bar.transform, false);
            var frameRect = (RectTransform)frame.transform;
            frameRect.anchorMin = Vector2.zero; frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = frameRect.offsetMax = Vector2.zero;
            var frameImage = frame.GetComponent<Image>();
            UITheme.StyleShape(frameImage, UITheme.StaminaFrameSprite, Color.white);
            frameImage.pixelsPerUnitMultiplier = 1f / BarScale;
            frameImage.raycastTarget = false;

            var icon = new GameObject("CarrierIcon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            icon.transform.SetParent(bar.transform, false);
            icon.GetComponent<LayoutElement>().ignoreLayout = true;
            var iconRect = (RectTransform)icon.transform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, .5f);
            // Kept within the bar's height (the big bar's 76 x 72 icon scaled down stuck up into the name).
            iconRect.anchoredPosition = new Vector2(w.left * BarScale * .5f, 1f);
            iconRect.sizeDelta = new Vector2(30f, 28f);
            UITheme.StyleIcon(icon.GetComponent<Image>(), UITheme.IconCarrier);
            icon.GetComponent<Image>().raycastTarget = false;
        }

        private static Image FillImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            UITheme.StyleFill(image, color);
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Every frame: ease the fill toward the replicated value, hold the ghost trail 0.4 s then let it
        /// slide down; kit colours (UITheme.Meter), red blink under 25%.</summary>
        private void AnimateStamina()
        {
            float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
            foreach (Row row in _rowPool)
            {
                if (row.Root == null || !row.Root.activeSelf) continue;
                row.Shown = Mathf.MoveTowards(row.Shown, row.Target, dt * Mathf.Max(.35f, Mathf.Abs(row.Target - row.Shown) * 6f));
                if (row.Shown >= row.Chip) { row.Chip = row.Shown; row.ChipHoldUntil = now + .4f; }
                else if (now >= row.ChipHoldUntil) row.Chip = Mathf.MoveTowards(row.Chip, row.Shown, dt * .7f);

                UITheme.SetBarFill(row.StaminaFillImage, row.Shown);
                UITheme.SetBarFill(row.StaminaChipImage, row.Chip - row.Shown > .005f ? row.Chip : 0f);

                Color c = UITheme.Meter(row.Shown, UITheme.Stamina);
                if (row.Shown < .25f)
                {
                    c = UITheme.Danger;
                    c.a = .45f + .55f * (.5f + .5f * Mathf.Sin(now * 12f));
                }
                row.StaminaFillImage.color = c;
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
