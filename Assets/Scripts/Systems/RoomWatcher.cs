using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Joining mid-level and coming back after a drop.
    /// Host: posts "joined / left" lines in the event feed (a player who joins mid-level is synced into the running
    /// level by NGO and spawns at the last checkpoint). Client: when the connection drops without the player leaving
    /// on purpose, a panel offers "กลับเข้าห้อง" which returns to the Lobby cleanly and joins the same room code again.
    /// Coins, XP, hats and gear come back from the local save; this level's scoreboard numbers start again.
    /// </summary>
    public sealed class RoomWatcher : MonoBehaviour
    {
        private NetworkManager _hooked;
        private string _lastRoomCode = "";
        private string _pendingRejoin;
        private float _rejoinAt = -1f;
        private GameObject _panel;
        private Text _message;
        private Button _rejoin;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("RoomWatcher");
            DontDestroyOnLoad(go);
            go.AddComponent<RoomWatcher>().Build();
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 950;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();
            _panel = new GameObject("Dropped", typeof(RectTransform), typeof(Image));
            var dim = (RectTransform)_panel.transform;
            dim.SetParent(transform, false);
            dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one; dim.offsetMin = dim.offsetMax = Vector2.zero;
            _panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, .6f);
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(dim, false);
            ((RectTransform)card.transform).sizeDelta = new Vector2(820f, 420f);
            UITheme.StyleCuteCard(card.GetComponent<Image>());
            UITheme.MakeCutePlank(card.transform, "หลุดจากห้อง", 380f, 90f, 6f, 36);
            _message = UITheme.MakeText(card.transform, "Message", "", 26, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(_message);
            ((RectTransform)_message.transform).sizeDelta = new Vector2(740f, 140f);
            ((RectTransform)_message.transform).anchoredPosition = new Vector2(0f, 30f);
            _rejoin = UITheme.MakeButton(card.transform, "Rejoin", "กลับเข้าห้อง", UIButtonStyle.Pink, new Vector2(300f, 80f), 28);
            ((RectTransform)_rejoin.transform).anchoredPosition = new Vector2(-170f, -120f);
            _rejoin.onClick.AddListener(Rejoin);
            var leave = UITheme.MakeButton(card.transform, "Leave", "กลับ Lobby", UIButtonStyle.White, new Vector2(300f, 80f), 28);
            ((RectTransform)leave.transform).anchoredPosition = new Vector2(170f, -120f);
            leave.onClick.AddListener(() => { _pendingRejoin = null; Close(); ReturnToLobby(); });
            _panel.SetActive(false);
        }

        private void Hook(NetworkManager manager)
        {
            if (_hooked != null)
            {
                _hooked.OnClientConnectedCallback -= OnConnected;
                _hooked.OnClientDisconnectCallback -= OnDisconnected;
            }
            _hooked = manager;
            if (_hooked != null)
            {
                _hooked.OnClientConnectedCallback += OnConnected;
                _hooked.OnClientDisconnectCallback += OnDisconnected;
            }
        }

        private void OnConnected(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || clientId == NetworkManager.ServerClientId) return;
            bool midLevel = SceneManager.GetActiveScene().name != "Lobby";
            GameRoom.ServerFeed($"P{clientId + 1} เข้าห้อง{(midLevel ? "กลางด่าน" : "")}", new Color(.5f, 1f, .7f));
        }

        private void OnDisconnected(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;
            if (manager.IsServer)
            {
                if (clientId != NetworkManager.ServerClientId)
                    GameRoom.ServerFeed(GameRoom.NameOfClient(clientId) + " ออกจากห้อง", new Color(.75f, .75f, .8f));
                return;
            }
            // Client: our own connection dropped (the host left, the network failed, or we were kicked).
            if (clientId != manager.LocalClientId && clientId != NetworkManager.ServerClientId) return;
            string reason = manager.DisconnectReason;
            bool kicked = !string.IsNullOrEmpty(reason) && reason.Contains("เตะ");
            // Leaving on purpose (pause menu, back to Lobby) clears the room code before shutting down.
            // A kick always shows its message (also in the Editor's local test, which has no room code).
            var lobby = SteamLobbyManager.Instance;
            if (!kicked && lobby != null && string.IsNullOrEmpty(lobby.CurrentRoomCode)) return;
            string code = _lastRoomCode;
            _message.text = kicked ? "คุณถูก Host เตะออกจากห้อง"
                : "การเชื่อมต่อกับห้องหลุด" + (string.IsNullOrEmpty(code) ? "" : $"\nห้อง {code}") + "\nเงิน เลเวล และของที่ซื้อถูกเซฟไว้แล้ว";
            _rejoin.gameObject.SetActive(!kicked && !string.IsNullOrEmpty(code));
            _pendingRejoin = kicked ? null : code;
            Open();
        }

        private void Open()
        {
            _panel.SetActive(true);
            if (EventSystem.current == null) new GameObject("RoomWatcher EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var cam = CoopGame.Player.PlayerCameraController.LocalInstance;
            if (cam != null) cam.SetCursorLock(false);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        private void Close() => _panel.SetActive(false);

        private void Rejoin()
        {
            Close();
            if (string.IsNullOrEmpty(_pendingRejoin)) { ReturnToLobby(); return; }
            _rejoinAt = Time.unscaledTime + 2.5f; // after the Lobby reload below
            ReturnToLobby();
        }

        private static void ReturnToLobby()
        {
            var lobby = SteamLobbyManager.Instance;
            if (lobby != null) lobby.DisconnectAndReturnToLobby();
            else SceneManager.LoadScene("Lobby");
        }

        private void Update()
        {
            var manager = NetworkManager.Singleton;
            if (manager != _hooked) Hook(manager);
            var lobby = SteamLobbyManager.Instance;
            if (lobby != null && manager != null && manager.IsListening && !string.IsNullOrEmpty(lobby.CurrentRoomCode))
                _lastRoomCode = lobby.CurrentRoomCode;
            if (_rejoinAt > 0f && Time.unscaledTime >= _rejoinAt && SceneManager.GetActiveScene().name == "Lobby")
            {
                _rejoinAt = -1f;
                if (lobby != null && manager != null && !manager.IsListening && !string.IsNullOrEmpty(_pendingRejoin))
                    lobby.JoinLobbyByCode(_pendingRejoin);
                _pendingRejoin = null;
            }
        }
    }
}
