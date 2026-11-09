using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Hold Tab: the scoreboard. Top line: level, difficulty, time, crate HP. One row per player: level, name, ping,
    /// carry time, crate drops/hits, damage to the crate, knockdowns, falls, throws/catches, coins this level.
    /// In the Lobby the host also gets a "เตะ" (kick) button per player; the cursor is freed while Tab is held there.
    /// </summary>
    public sealed class Scoreboard : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        private static readonly string[] Headers = { "Lv", "ผู้เล่น", "ปิง", "ยกลัง", "ทำหล่น/ชน", "ดาเมจลัง", "ล้ม", "ตกแมพ", "ปา/รับ", "เงิน", "" };
        private static readonly float[] Widths = { 70f, 300f, 90f, 110f, 130f, 120f, 80f, 100f, 100f, 100f, 110f };

        private GameObject _root;
        private Text _top;
        private RectTransform _rows;
        private float _nextRefresh;
        private bool _cursorFreed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            IsOpen = false;
            var go = new GameObject("Scoreboard");
            DontDestroyOnLoad(go);
            go.AddComponent<Scoreboard>().Build();
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();
            _root = new GameObject("Board", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)_root.transform;
            rect.SetParent(transform, false);
            float width = 0f; foreach (float w in Widths) width += w;
            rect.sizeDelta = new Vector2(width + 60f, 560f);
            rect.anchoredPosition = new Vector2(0f, 60f);
            _root.GetComponent<Image>().color = new Color(.08f, .07f, .12f, .88f);
            _top = UITheme.MakeText(rect, "Top", "", 26, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.AddOutline(_top);
            ((RectTransform)_top.transform).anchoredPosition = new Vector2(0f, 240f);
            ((RectTransform)_top.transform).sizeDelta = new Vector2(width, 44f);
            var header = Row(rect, new Vector2(0f, 190f), new Color(1f, 1f, 1f, .08f));
            for (int c = 0; c < Headers.Length; c++) Cell(header, c, Headers[c], new Color(1f, .85f, .55f), 20);
            _rows = new GameObject("Rows", typeof(RectTransform)).GetComponent<RectTransform>();
            _rows.SetParent(rect, false);
            _rows.anchoredPosition = Vector2.zero;
            var hint = UITheme.MakeText(rect, "Hint", "ปล่อย Tab เพื่อปิด", 18, UITextRole.Secondary, TextAnchor.MiddleCenter);
            ((RectTransform)hint.transform).anchoredPosition = new Vector2(0f, -255f);
            ((RectTransform)hint.transform).sizeDelta = new Vector2(width, 30f);
            _root.SetActive(false);
        }

        private static RectTransform Row(Transform parent, Vector2 position, Color color)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            float width = 0f; foreach (float w in Widths) width += w;
            rect.sizeDelta = new Vector2(width, 52f);
            rect.anchoredPosition = position;
            var image = go.GetComponent<Image>();
            image.color = color; image.raycastTarget = false;
            return rect;
        }

        private static float ColumnX(int column)
        {
            float total = 0f; foreach (float w in Widths) total += w;
            float x = -total * .5f;
            for (int i = 0; i < column; i++) x += Widths[i];
            return x + Widths[column] * .5f;
        }

        private static Text Cell(RectTransform row, int column, string value, Color color, int size = 22)
        {
            var text = UITheme.MakeText(row, "C" + column, value, size, UITextRole.Primary, column == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
            text.color = color; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var r = (RectTransform)text.transform;
            r.sizeDelta = new Vector2(Widths[column] - 8f, 48f);
            r.anchoredPosition = new Vector2(ColumnX(column), 0f);
            return text;
        }

        private static bool Blocked() => PauseMenu.IsPaused || TitleScreen.IsVisible || PhotoMode.IsOn || DevCheatMenu.IsOpen ||
                                         LevelMap.IsOpen || Cosmetics.IsOpen || LobbyCharacterCustomizationUI.IsVisible;

        private void Update()
        {
            var keyboard = Keyboard.current;
            var manager = NetworkManager.Singleton;
            bool inSession = manager != null && manager.IsListening;
            bool want = keyboard != null && keyboard.tabKey.isPressed && inSession && !Blocked();
            if (want != IsOpen)
            {
                IsOpen = want;
                _root.SetActive(want);
                _nextRefresh = 0f;
                bool lobbyHost = want && manager.IsServer && SceneManager.GetActiveScene().name == "Lobby";
                if (lobbyHost && !_cursorFreed)
                {
                    _cursorFreed = true;
                    if (EventSystem.current == null) new GameObject("Scoreboard EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                    // The player camera re-locks the cursor every frame unless told through SetCursorLock.
                    var cam = CoopGame.Player.PlayerCameraController.LocalInstance;
                    if (cam != null) cam.SetCursorLock(false);
                    else { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
                }
                else if (!want && _cursorFreed)
                {
                    _cursorFreed = false;
                    var cam = CoopGame.Player.PlayerCameraController.LocalInstance;
                    if (cam != null) cam.SetCursorLock(true);
                    else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
                }
            }
            if (!IsOpen || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + .25f;
            Refresh(manager);
        }

        private void Refresh(NetworkManager manager)
        {
            var mission = FindAnyObjectByType<LevelMission>();
            var run = LevelRunState.Instance;
            string scene = SceneManager.GetActiveScene().name;
            string hp = mission != null && mission.Cargo != null ? $"HP ลัง {mission.Cargo.CurrentHP.Value}/{mission.Cargo.MaxHP}" : "";
            float seconds = run != null && run.IsSpawned ? run.ElapsedSeconds : LevelResults.LevelSeconds();
            string time = $"{(int)(seconds / 60f):00}:{(int)(seconds % 60f):00}";
            var d = GameRoom.Current;
            _top.text = $"{LevelMap.DisplayName(scene).Replace("\n", " ")}   •   ความยาก {d.Name} ({GameRoom.CrateNames[GameRoom.CrateKind]})   •   เวลา {time}   {(hp.Length > 0 ? "•   " + hp : "")}";

            foreach (Transform child in _rows) Destroy(child.gameObject);
            var players = LevelResults.Players();
            bool lobbyHost = manager.IsServer && scene == "Lobby";
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                var s = p.Stats.Value;
                bool me = p.IsOwner;
                var row = Row(_rows, new Vector2(0f, 130f - i * 58f), me ? new Color(.99f, .4f, .53f, .25f) : new Color(1f, 1f, 1f, i % 2 == 0 ? .05f : .1f));
                Color white = Color.white;
                int ping = p.OwnerClientId == NetworkManager.ServerClientId ? 0 : s.PingMs;
                Cell(row, 0, p.PlayerLevel.Value.ToString(), new Color(.6f, .85f, 1f));
                Cell(row, 1, LevelResults.NameOf(p) + (p.OwnerClientId == NetworkManager.ServerClientId ? "  (Host)" : ""), white);
                Cell(row, 2, p.OwnerClientId == NetworkManager.ServerClientId ? "-" : ping + "", ping > 150 ? new Color(1f, .5f, .4f) : white);
                Cell(row, 3, Mathf.RoundToInt(s.CarrySeconds) + " วิ", white);
                Cell(row, 4, s.CrateHits.ToString(), s.CrateHits > 0 ? new Color(1f, .6f, .5f) : white);
                Cell(row, 5, s.CrateDamage.ToString(), s.CrateDamage > 0 ? new Color(1f, .6f, .5f) : white);
                Cell(row, 6, s.Knockdowns.ToString(), white);
                Cell(row, 7, s.Falls.ToString(), white);
                Cell(row, 8, s.Throws + "/" + s.Catches, white);
                Cell(row, 9, (s.Coins >= 0 ? "+" : "") + s.Coins, new Color(1f, .85f, .3f));
                if (lobbyHost && p.OwnerClientId != NetworkManager.ServerClientId)
                {
                    ulong id = p.OwnerClientId;
                    string name = LevelResults.NameOf(p);
                    var kick = UITheme.MakeButton(row, "Kick", "เตะ", UIButtonStyle.Danger, new Vector2(96f, 42f), 20);
                    ((RectTransform)kick.transform).anchoredPosition = new Vector2(ColumnX(10), 0f);
                    kick.onClick.AddListener(() => Kick(id, name));
                }
            }
        }

        private static void Kick(ulong clientId, string name)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || clientId == NetworkManager.ServerClientId) return;
            GameRoom.ServerFeed(name + " ถูกเตะออกจากห้อง", new Color(1f, .4f, .4f));
            manager.DisconnectClient(clientId, "ถูก Host เตะออกจากห้อง");
        }
    }
}
