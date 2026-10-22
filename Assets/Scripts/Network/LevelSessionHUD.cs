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
        [SerializeField, Min(0.1f)] private float _refreshInterval = 0.5f;

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
            if (_panel == null) return;
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
            row.Name = UITheme.MakeText(go.transform, "Name", "", NameFontSize, UITextRole.Primary);
            row.Name.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.SetWeight(row.Name, UITheme.FontSemiBold);
            var nameElement = row.Name.gameObject.AddComponent<LayoutElement>();
            nameElement.flexibleWidth = 1f;
            nameElement.preferredHeight = RowHeight;
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
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
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
                UITheme.StyleIcon(row.Face, UITheme.FaceForClient(player.OwnerClientId));
                string name = player.IsOwner ? "You" : player.DisplayName.Value.ToString();
                if (string.IsNullOrEmpty(name)) name = $"Player {player.OwnerClientId + 1}";
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
