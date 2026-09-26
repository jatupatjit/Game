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
                _modeLabel.text = manager.IsHost ? "● HOST" : manager.IsServer ? "● SERVER" : "● CLIENT";

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
