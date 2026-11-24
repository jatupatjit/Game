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
            public RectTransform StaminaFill;
            public Image StaminaFillImage;
            public UITooltip Tooltip;
        }

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
            var bar = new GameObject("StaminaBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            bar.transform.SetParent(col.transform, false);
            bar.GetComponent<LayoutElement>().preferredHeight = 9f;
            var barImage = bar.GetComponent<Image>();
            barImage.sprite = UITheme.FillSprite; barImage.type = Image.Type.Sliced;
            barImage.color = new Color(0f, 0f, 0f, .35f);
            barImage.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(bar.transform, false);
            row.StaminaFill = (RectTransform)fill.transform;
            row.StaminaFill.anchorMin = Vector2.zero; row.StaminaFill.anchorMax = Vector2.one;
            row.StaminaFill.offsetMin = row.StaminaFill.offsetMax = Vector2.zero;
            row.StaminaFillImage = fill.GetComponent<Image>();
            row.StaminaFillImage.sprite = UITheme.FillSprite; row.StaminaFillImage.type = Image.Type.Sliced;
            row.StaminaFillImage.raycastTarget = false;
            row.Card = Icon(go.transform, "CardIcon");
            row.Card.raycastTarget = true; // hover target
            row.Tooltip = row.Card.gameObject.AddComponent<UITooltip>();
            row.Tooltip.Side = new Vector2(1f, 0f);
            return row;
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
                float stamina = player.StaminaPercent.Value / 100f;
                row.StaminaFill.anchorMax = new Vector2(Mathf.Max(.001f, stamina), 1f);
                row.StaminaFillImage.color = stamina > .5f ? new Color(.45f, .85f, .45f) : stamina > .2f ? new Color(1f, .78f, .3f) : new Color(1f, .4f, .35f);
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
