using System.Collections.Generic;
using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Level map in the Lobby (M): every level in the build with the best stars earned on this machine (saved in
    /// PlayerPrefs when a level is delivered). The host can start an unlocked level for everyone; a level unlocks
    /// once the one before it has a star. Test scenes (Scenes/Test) are left out.
    /// </summary>
    public sealed class LevelMap : MonoBehaviour
    {
        private const string Lobby = "Lobby", Training = "Tutorial";
        public static bool IsOpen { get; private set; }

        private GameObject _root;
        private Transform _list;
        private Text _footer, _difficultyText;
        private readonly System.Collections.Generic.List<Button> _difficultyButtons = new();
        private int _shownDifficulty = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            IsOpen = false;
            var go = new GameObject("LevelMap");
            DontDestroyOnLoad(go);
            go.AddComponent<LevelMap>().Build();
        }

        private void OnEnable() => LevelResults.Ready += SaveStars;
        private void OnDisable() => LevelResults.Ready -= SaveStars;

        public static int BestStars(string level) => SaveSystem.Stars(level); // local save file (was PlayerPrefs)

        /// <summary>Every machine, on delivery: keep the best stars for this level.</summary>
        private static void SaveStars()
        {
            var mission = FindAnyObjectByType<LevelMission>();
            if (mission == null || mission.Delivery == null) return;
            string level = SceneManager.GetActiveScene().name;
            int maxHp = mission.Cargo != null ? mission.Cargo.MaxHP : 100;
            int stars = LevelResults.Stars(mission.Delivery.LastDeliveryResult.RemainingHP, maxHp);
            SaveSystem.Current.levelsPlayed++;
            SaveSystem.SetBestStars(level, stars);
        }

        // Play order (George 2026-11-06: Level02 Sky Kingdom is the first level). Scenes not listed follow in build order.
        private static readonly string[] PlayOrder = { Training, "Level02 George", "Level02", "Level01" };

        /// <summary>Name shown on the map for a scene.</summary>
        public static string DisplayName(string scene) => scene switch
        {
            Training => "ฝึกซ้อม",
            "Level02 George" or "Level02" => "ด่าน 1\nอาณาจักรลอยฟ้า",
            "Level01" => "ด่าน 2\nปราสาท",
            _ => scene
        };

        /// <summary>Levels on the map: build scenes except the Lobby and test scenes, in play order.</summary>
        public static List<(int index, string name)> Levels()
        {
            var levels = new List<(int index, string name)>();
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (name != Lobby && !path.Contains("/Test/")) levels.Add((i, name));
            }
            int Rank(string n) { int r = System.Array.IndexOf(PlayOrder, n); return r < 0 ? PlayOrder.Length : r; }
            var sorted = new List<(int index, string name)>(levels);
            sorted.Sort((a, b) => Rank(a.name) != Rank(b.name) ? Rank(a.name).CompareTo(Rank(b.name)) : a.index.CompareTo(b.index));
            return sorted;
        }

        public static void SetOpen(bool open)
        {
            var map = FindAnyObjectByType<LevelMap>();
            if (map == null) return;
            IsOpen = open && SceneManager.GetActiveScene().name == Lobby;
            map._root.SetActive(IsOpen);
            if (IsOpen) map.Refresh();
            Cursor.lockState = IsOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = IsOpen;
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 250;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Map", typeof(RectTransform), typeof(Image));
            _root.transform.SetParent(transform, false);
            var dim = (RectTransform)_root.transform;
            dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one; dim.offsetMin = dim.offsetMax = Vector2.zero;
            _root.GetComponent<Image>().color = new Color(0f, 0f, 0f, .45f);

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(_root.transform, false);
            ((RectTransform)card.transform).sizeDelta = new Vector2(1180f, 720f);
            UITheme.StyleCuteCard(card.GetComponent<Image>(), true);
            UITheme.MakeCutePlank(card.transform, "แผนที่ด่าน", 420f, 96f, 6f, 40);
            UITheme.AddCuteTape(card.transform, 4, new Vector2(1f, 1f), new Vector2(-80f, -40f), -24f, 90f);

            var list = new GameObject("Levels", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            list.transform.SetParent(card.transform, false);
            var listRect = (RectTransform)list.transform;
            listRect.sizeDelta = new Vector2(1000f, 320f);
            listRect.anchoredPosition = new Vector2(0f, 70f);
            var layout = list.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 40f; layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            _list = list.transform;

            _footer = UITheme.MakeText(card.transform, "Footer", "", 22, UITextRole.Secondary, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(_footer);
            var footerRect = (RectTransform)_footer.transform;
            footerRect.sizeDelta = new Vector2(1000f, 40f);
            footerRect.anchoredPosition = new Vector2(0f, -315f);

            // Difficulty row (GameRoom): anyone can pick; the pick stands and everyone sees who chose what.
            for (int d = 0; d < GameRoom.Difficulties.Length; d++)
            {
                byte id = (byte)d;
                var def = GameRoom.Difficulties[d];
                var button = UITheme.MakeButton(card.transform, "Difficulty" + d, def.Name, UIButtonStyle.White, new Vector2(220f, 64f), 26);
                ((RectTransform)button.transform).anchoredPosition = new Vector2((d - (GameRoom.Difficulties.Length - 1) * .5f) * 240f, -170f);
                button.onClick.AddListener(() => ChooseDifficulty(id));
                _difficultyButtons.Add(button);
            }
            _difficultyText = UITheme.MakeText(card.transform, "DifficultyText", "", 22, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(_difficultyText);
            var difficultyRect = (RectTransform)_difficultyText.transform;
            difficultyRect.sizeDelta = new Vector2(1100f, 40f);
            difficultyRect.anchoredPosition = new Vector2(0f, -232f);
            _root.SetActive(false);
        }

        private void Refresh()
        {
            foreach (Transform child in _list) Destroy(child.gameObject);
            var manager = NetworkManager.Singleton;
            bool host = manager != null && manager.IsListening && manager.IsServer;
            int previousStars = 3;
            foreach (var (index, name) in Levels())
            {
                // The training ground has no delivery: always open, and it does not gate the next level.
                bool training = name == Training;
                int stars = BestStars(name);
                bool unlocked = training || previousStars > 0;
                if (!training) previousStars = stars;
                var node = new GameObject(name, typeof(RectTransform), typeof(Image));
                node.transform.SetParent(_list, false);
                ((RectTransform)node.transform).sizeDelta = new Vector2(280f, 300f);
                var image = node.GetComponent<Image>();
                UITheme.StyleCuteCard(image);
                image.color = unlocked ? Color.white : new Color(.75f, .75f, .75f);
                var title = UITheme.MakeText(node.transform, "Name", DisplayName(name), 30, UITextRole.Primary, TextAnchor.MiddleCenter);
                UITheme.StyleCuteText(title);
                UITheme.SetWeight(title, UITheme.FontBold);
                ((RectTransform)title.transform).anchoredPosition = new Vector2(0f, 95f);
                ((RectTransform)title.transform).sizeDelta = new Vector2(260f, 80f);
                for (int s = 0; s < 3; s++)
                {
                    var star = new GameObject("Star" + s, typeof(RectTransform), typeof(Image));
                    star.transform.SetParent(node.transform, false);
                    var rect = (RectTransform)star.transform;
                    rect.sizeDelta = new Vector2(62f, 62f);
                    rect.anchoredPosition = new Vector2((s - 1) * 70f, 20f + (s == 1 ? 12f : 0f));
                    var starImage = star.GetComponent<Image>();
                    starImage.sprite = MissionCompleteUI.StarSprite();
                    starImage.color = s < stars ? Color.white : new Color(.3f, .3f, .3f, .35f);
                }
                // Whoever picks a level starts it for everyone (George 2026-11-09: no vote).
                bool inSession = manager != null && manager.IsListening;
                string label = !unlocked ? "ล็อก" : "ไปด่านนี้";
                var go = UITheme.MakeButton(node.transform, "Go", label, unlocked && inSession ? UIButtonStyle.Pink : UIButtonStyle.White, new Vector2(220f, 72f), 26);
                ((RectTransform)go.transform).anchoredPosition = new Vector2(0f, -95f);
                go.interactable = unlocked && inSession;
                int level = index;
                go.onClick.AddListener(() => StartLevel(level));
            }
            _footer.text = "ใครกดเลือกด่านก็เริ่มด่านนั้นให้ทุกคนเลย • ได้ดาวอย่างน้อย 1 ดวงเพื่อปลดล็อกด่านถัดไป • M ปิด";
            _shownDifficulty = -1;
            RefreshDifficulty();
        }

        private void RefreshDifficulty()
        {
            int current = GameRoom.Difficulty;
            if (current == _shownDifficulty) return;
            _shownDifficulty = current;
            for (int d = 0; d < _difficultyButtons.Count; d++)
                UITheme.StyleButton(_difficultyButtons[d], d == current ? UIButtonStyle.Pink : UIButtonStyle.White);
            var def = GameRoom.Current;
            _difficultyText.text = $"ความยาก: {def.Name} — {def.Crate}: {def.Detail} • เงิน/XP x{def.Reward:0.0}";
        }

        private static void ChooseDifficulty(byte difficulty)
        {
            var local = LocalState();
            if (local != null) local.ChooseDifficultyRpc(difficulty);
        }

        private static PlayerExpeditionState LocalState()
        {
            var manager = NetworkManager.Singleton;
            var player = manager != null && manager.IsListening && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            return player != null ? player.GetComponent<PlayerExpeditionState>() : null;
        }

        private static void StartLevel(int buildIndex)
        {
            var local = LocalState();
            if (local != null) { local.ChooseLevelRpc(buildIndex); SetOpen(false); }
        }

        /// <summary>Server: <paramref name="byName"/> picked a level; start it for everyone and say so in the feed.</summary>
        public static void ServerStartLevel(int buildIndex, string byName)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || manager.SceneManager == null) return;
            if (SceneManager.GetActiveScene().name != Lobby) return;
            if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings) return;
            string name = System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(buildIndex));
            if (manager.SceneManager.LoadScene(name, LoadSceneMode.Single) == SceneEventProgressStatus.Started)
            {
                var d = GameRoom.Current;
                GameRoom.ServerFeed($"{byName} เลือกด่าน: {DisplayName(name).Replace("\n", " ")} ({d.Name})", d.Color);
                SetOpen(false);
            }
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            bool inLobby = SceneManager.GetActiveScene().name == Lobby;
            if (IsOpen && !inLobby) SetOpen(false);
            if (IsOpen && Time.frameCount % 15 == 0) RefreshDifficulty();
            if (keyboard == null || !inLobby || PauseMenu.IsPaused || TitleScreen.IsVisible || PhotoMode.IsOn) return;
            if (keyboard.mKey.wasPressedThisFrame) SetOpen(!IsOpen);
            else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame) SetOpen(false);
        }
    }
}
