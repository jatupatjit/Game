using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>Gameplay session status for Level01, without lobby or room-code controls.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class LevelSessionHUD : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _hudGroup;
        [SerializeField] private Text _modeLabel;
        [SerializeField] private Text _playerCountText;
        [SerializeField] private Text _playerNamesText;
        [SerializeField, Min(0.1f)] private float _refreshInterval = 0.5f;

        private readonly StringBuilder _namesBuilder = new StringBuilder(128);
        private SteamLobbyManager _lobbyManager;
        private float _nextRefreshTime;
        private bool _hudVisible = true;

        private void Awake()
        {
            if (_hudGroup == null)
                _hudGroup = GetComponent<CanvasGroup>();
            if (_hudGroup != null)
            {
                _hudGroup.interactable = false;
                _hudGroup.blocksRaycasts = false;
            }
            ApplyTheme();
        }

        // Compact, readable session card (sizes in 1080p reference pixels).
        private const float CardWidth = 300f;
        private const int HeaderFontSize = 19, CountFontSize = UITheme.CardLabelFontSize, NamesFontSize = 17;

        /// <summary>
        /// Restyles the scene-serialized session card with <see cref="UITheme"/> and re-lays it out at
        /// runtime: larger text, panel height fitted to its content, padding consistent with the other
        /// panels. Done here (not in the scenes) so Level01/Level02 need no re-save.
        /// </summary>
        private void ApplyTheme()
        {
            RectTransform panel = null;
            foreach (Image image in GetComponentsInChildren<Image>(true))
            {
                if (image.name.Contains("Divider"))
                {
                    image.color = UITheme.Track;
                    UITheme.SetPreferredHeight(image.gameObject, 2f);
                }
                else
                {
                    UITheme.StylePanel(image);
                    if (panel == null) panel = image.rectTransform;
                }
                image.raycastTarget = false;
            }
            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                bool names = text == _playerNamesText, count = text == _playerCountText;
                UITextRole role = text == _modeLabel ? UITextRole.Positive :
                    names ? UITextRole.Primary : count ? UITextRole.Secondary : UITextRole.Accent;
                UITheme.StyleText(text, role);
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.fontSize = names ? NamesFontSize : count ? CountFontSize : HeaderFontSize;
                text.fontStyle = names ? FontStyle.Normal : FontStyle.Bold;
                text.lineSpacing = 1f;
            }
            if (panel != null) CompactLayout(panel);
        }

        private void CompactLayout(RectTransform panel)
        {
            UITheme.CompactCard(panel, CardWidth);
            // Header row: title left, mode right, both sized by the row instead of fixed widths.
            Transform header = _modeLabel != null ? _modeLabel.transform.parent : null;
            if (header != null && header != panel.transform) UITheme.SpreadRow(header, 28f);
            if (_playerCountText != null) UITheme.SetPreferredHeight(_playerCountText.gameObject, 24f);
            if (_playerNamesText != null) UITheme.SetPreferredHeight(_playerNamesText.gameObject, -1f); // fit the list
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        }

        private void OnEnable()
        {
            _lobbyManager = SteamLobbyManager.Instance;
            if (_lobbyManager != null)
                _lobbyManager.OnLobbyMembersChanged += Refresh;
            _nextRefreshTime = 0f;
        }

        private void OnDisable()
        {
            if (_lobbyManager != null)
                _lobbyManager.OnLobbyMembersChanged -= Refresh;
            _lobbyManager = null;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
                _hudVisible = !_hudVisible;

            NetworkManager manager = NetworkManager.Singleton;
            bool connected = manager != null && manager.IsListening;
            if (_hudGroup != null)
                _hudGroup.alpha = connected && _hudVisible ? 1f : 0f;

            if (!connected || Time.unscaledTime < _nextRefreshTime)
                return;

            _nextRefreshTime = Time.unscaledTime + _refreshInterval;
            Refresh();
        }

        private void Refresh()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening)
                return;

            if (_lobbyManager == null)
            {
                _lobbyManager = SteamLobbyManager.Instance;
                if (_lobbyManager != null)
                    _lobbyManager.OnLobbyMembersChanged += Refresh;
            }

            if (_modeLabel != null)
                _modeLabel.text = _lobbyManager != null && _lobbyManager.IsLocalSession
                    ? "● SOLO" : manager.IsHost ? "● HOST" : manager.IsServer ? "● SERVER" : "● CLIENT";

            List<string> names = _lobbyManager != null ? _lobbyManager.GetCurrentPlayerNames() : null;
            int count = names != null && names.Count > 0 ? names.Count : manager.ConnectedClientsIds.Count;
            if (_playerCountText != null)
                _playerCountText.text = $"PLAYERS ({count})";

            if (_playerNamesText == null)
                return;

            _namesBuilder.Clear();
            if (names != null && names.Count > 0)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    if (i > 0) _namesBuilder.Append('\n');
                    _namesBuilder.Append("• ").Append(names[i]);
                }
            }
            else
            {
                foreach (ulong clientId in manager.ConnectedClientsIds)
                {
                    if (_namesBuilder.Length > 0) _namesBuilder.Append('\n');
                    _namesBuilder.Append("• Player ").Append(clientId + 1);
                }
            }
            _playerNamesText.text = _namesBuilder.ToString();
        }
    }
}
