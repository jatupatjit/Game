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
        private const string Lobby = "Lobby";
        public static bool IsOpen { get; private set; }

        private GameObject _root;
        private Transform _list;
        private Text _footer;

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

        public static int BestStars(string level) => PlayerPrefs.GetInt("stars_" + level, 0);

        /// <summary>Every machine, on delivery: keep the best stars for this level.</summary>
        private static void SaveStars()
        {
            var mission = FindAnyObjectByType<LevelMission>();
            if (mission == null || mission.Delivery == null) return;
            string level = SceneManager.GetActiveScene().name;
            int maxHp = mission.Cargo != null ? mission.Cargo.MaxHP : 100;
            int stars = LevelResults.Stars(mission.Delivery.LastDeliveryResult.RemainingHP, maxHp);
            if (stars <= BestStars(level)) return;
            PlayerPrefs.SetInt("stars_" + level, stars);
            PlayerPrefs.Save();
        }

        /// <summary>Levels on the map: build scenes except the Lobby and test scenes, in build order.</summary>
        public static List<(int index, string name)> Levels()
        {
            var levels = new List<(int, string)>();
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (name != Lobby && !path.Contains("/Test/")) levels.Add((i, name));
            }
            return levels;
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
            ((RectTransform)card.transform).sizeDelta = new Vector2(1100f, 560f);
            UITheme.StyleCuteCard(card.GetComponent<Image>(), true);
            UITheme.MakeCutePlank(card.transform, "แผนที่ด่าน", 420f, 96f, 6f, 40);
            UITheme.AddCuteTape(card.transform, 4, new Vector2(1f, 1f), new Vector2(-80f, -40f), -24f, 90f);

            var list = new GameObject("Levels", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            list.transform.SetParent(card.transform, false);
            var listRect = (RectTransform)list.transform;
            listRect.sizeDelta = new Vector2(1000f, 320f);
            listRect.anchoredPosition = new Vector2(0f, 10f);
            var layout = list.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 40f; layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            _list = list.transform;

            _footer = UITheme.MakeText(card.transform, "Footer", "", 22, UITextRole.Secondary, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(_footer);
            var footerRect = (RectTransform)_footer.transform;
            footerRect.sizeDelta = new Vector2(1000f, 40f);
            footerRect.anchoredPosition = new Vector2(0f, -230f);
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
                int stars = BestStars(name);
                bool unlocked = previousStars > 0;
                previousStars = stars;
                var node = new GameObject(name, typeof(RectTransform), typeof(Image));
                node.transform.SetParent(_list, false);
                ((RectTransform)node.transform).sizeDelta = new Vector2(280f, 300f);
                var image = node.GetComponent<Image>();
                UITheme.StyleCuteCard(image);
                image.color = unlocked ? Color.white : new Color(.75f, .75f, .75f);
                var title = UITheme.MakeText(node.transform, "Name", name, 32, UITextRole.Primary, TextAnchor.MiddleCenter);
                UITheme.StyleCuteText(title);
                UITheme.SetWeight(title, UITheme.FontBold);
                ((RectTransform)title.transform).anchoredPosition = new Vector2(0f, 95f);
                ((RectTransform)title.transform).sizeDelta = new Vector2(260f, 50f);
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
                string label = !unlocked ? "ล็อก" : host ? "ไปด่านนี้" : "รอ Host เลือก";
                var go = UITheme.MakeButton(node.transform, "Go", label, unlocked && host ? UIButtonStyle.Pink : UIButtonStyle.White, new Vector2(220f, 72f), 26);
                ((RectTransform)go.transform).anchoredPosition = new Vector2(0f, -95f);
                go.interactable = unlocked && host;
                int level = index;
                go.onClick.AddListener(() => StartLevel(level));
            }
            _footer.text = "ดาวที่ดีที่สุดบนเครื่องนี้ • ได้ดาวอย่างน้อย 1 ดวงเพื่อปลดล็อกด่านถัดไป • M ปิด";
        }

        private static void StartLevel(int buildIndex)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || manager.SceneManager == null) return;
            string name = System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(buildIndex));
            if (manager.SceneManager.LoadScene(name, LoadSceneMode.Single) == SceneEventProgressStatus.Started) SetOpen(false);
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            bool inLobby = SceneManager.GetActiveScene().name == Lobby;
            if (IsOpen && !inLobby) SetOpen(false);
            if (keyboard == null || !inLobby || PauseMenu.IsPaused || TitleScreen.IsVisible || PhotoMode.IsOn) return;
            if (keyboard.mKey.wasPressedThisFrame) SetOpen(!IsOpen);
            else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame) SetOpen(false);
        }
    }
}
