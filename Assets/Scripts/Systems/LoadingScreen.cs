using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Loading screen while a networked scene loads (level start, back to Lobby, joining a room mid-level).
    /// The UIKit carrier icon walks around a circle in the middle, "กำลังโหลด x/y ผู้เล่น" counts players that
    /// finished loading, and a small tip sits below. Stays up until everyone has loaded (or a timeout), min 1.5 s.
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        private const float MinSeconds = 1.5f, MaxSeconds = 25f;

        public static readonly string[] Tips =
        {
            "ยกลังสองคนเหนื่อยช้ากว่ายกคนเดียว",
            "กด F ค้างเพื่อเตรียมรับลังที่เพื่อนปามา",
            "กด Q ค้างเพื่อชาร์จปา ยิ่งค้างนานยิ่งไปไกล",
            "วางลังบนกองฟางก่อนพัก จะไม่เสีย HP",
            "สตามิน่าหมดตอนยกคนเดียว ลังจะหล่นและเสีย HP",
            "กดเมาส์กลางเพื่อ ping กดค้างเพื่อเปิดวงล้อ",
            "กด Tab ค้างเพื่อดูว่าใครทำลังหล่นบ่อยที่สุด",
            "กด O เพื่อดูมุมกล้องของเพื่อน",
            "กด M ใน Lobby เพื่อเลือกด่านและความยาก",
            "ความยากสูงขึ้น ได้เงินและ XP มากขึ้น (สูงสุด x2)",
            "ถุงมือหนังช่วยให้ยกลังเหนื่อยช้าลง ซื้อได้ที่ร้าน (K)",
            "ลังไก่จะดิ้น จับให้แน่นแล้วเดินช้าๆ",
            "ลังซ้อนสูงไม่ชอบการออกตัวแรงๆ",
            "ลังระเบิด... อย่าทำหล่นใกล้เพื่อน",
            "ล้มแล้วรอสักครู่ ตัวละครจะลุกขึ้นเอง",
            "ปีนกำแพงด้วยเมาส์ซ้าย/ขวา สลับมือขึ้นไป",
            "กด V เพื่อสลับมุมกล้องบุคคลที่ 1 / 3",
        };

        private const float WalkRadius = 52f, WalkDegreesPerSecond = 110f;

        private GameObject _root;
        private Text _tip, _title, _count;
        private const int DotCount = 12;
        private RectTransform _orbit, _walker;
        private RectTransform[] _dots;
        private float _shownAt = -1f, _hideAt = -1f, _walkAngle;
        private bool _counting;
        private readonly System.Collections.Generic.HashSet<ulong> _loaded = new();
        private NetworkSceneManager _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("LoadingScreen");
            DontDestroyOnLoad(go);
            go.AddComponent<LoadingScreen>().Build();
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            _root = new GameObject("Loading", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)_root.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            _root.GetComponent<Image>().color = new Color(.12f, .14f, .25f, 1f);
            _title = UITheme.MakeText(rect, "Title", "กำลังโหลด...", 52, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.AddOutline(_title, 2f);
            ((RectTransform)_title.transform).anchoredPosition = new Vector2(0f, 230f);
            ((RectTransform)_title.transform).sizeDelta = new Vector2(1200f, 90f);

            // A small dotted circle; the carrier walks along the dots (feet on the dots, head out), and the dots
            // behind it run from big to small like a trail.
            var orbitCenter = new GameObject("DotCircle", typeof(RectTransform)).GetComponent<RectTransform>();
            orbitCenter.SetParent(rect, false);
            orbitCenter.anchoredPosition = new Vector2(0f, 40f);
            orbitCenter.sizeDelta = Vector2.zero;
            var dotSprite = MakeDotSprite();
            _dots = new RectTransform[DotCount];
            for (int i = 0; i < DotCount; i++)
            {
                var dot = new GameObject("Dot" + i, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                dot.SetParent(orbitCenter, false);
                float a = i * Mathf.PI * 2f / DotCount;
                dot.anchoredPosition = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * WalkRadius;
                var img = dot.GetComponent<Image>();
                img.sprite = dotSprite;
                img.color = UITheme.Primary;
                img.raycastTarget = false;
                _dots[i] = dot;
            }
            _orbit = new GameObject("Orbit", typeof(RectTransform)).GetComponent<RectTransform>();
            _orbit.SetParent(orbitCenter, false);
            _orbit.sizeDelta = Vector2.zero;
            var walker = new GameObject("Carrier", typeof(RectTransform), typeof(Image));
            _walker = (RectTransform)walker.transform;
            _walker.SetParent(_orbit, false);
            _walker.pivot = new Vector2(.34f, .05f); // the feet (left part of the icon) stand on the planet
            _walker.sizeDelta = new Vector2(70f, 67f); // IconCarrier 174x166
            var icon = walker.GetComponent<Image>();
            icon.sprite = Resources.Load<Sprite>("UIKit/IconCarrier");
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            if (icon.sprite == null) icon.color = UITheme.Primary;

            _count = UITheme.MakeText(rect, "PlayersLoaded", "", 30, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.AddOutline(_count, 1.5f);
            ((RectTransform)_count.transform).anchoredPosition = new Vector2(0f, -110f);
            ((RectTransform)_count.transform).sizeDelta = new Vector2(800f, 50f);

            _tip = UITheme.MakeText(rect, "Tip", "", 24, UITextRole.Primary, TextAnchor.MiddleCenter);
            ((RectTransform)_tip.transform).anchoredPosition = new Vector2(0f, -165f);
            ((RectTransform)_tip.transform).sizeDelta = new Vector2(1100f, 44f);
            _tip.color = new Color(1f, .9f, .6f);
            _root.SetActive(false);
        }

        public void Show(string title, bool countPlayers = true)
        {
            _title.text = title;
            _counting = countPlayers;
            _loaded.Clear();
            RefreshCount();
            _tip.text = "ทิป: " + Tips[Random.Range(0, Tips.Length)];
            _shownAt = Time.unscaledTime;
            _hideAt = -1f;
            _root.SetActive(true);
        }

        /// <summary>The carrier walks clockwise around the planet (always the same way, head out) with a little waddle.</summary>
        private void AnimateWalker()
        {
            float step = Time.unscaledTime * 11f;
            _walkAngle = Mathf.Repeat(_walkAngle + WalkDegreesPerSecond * Time.unscaledDeltaTime, 360f);
            _orbit.localRotation = Quaternion.Euler(0f, 0f, -_walkAngle);
            _walker.anchoredPosition = new Vector2(0f, WalkRadius + 2f + Mathf.Abs(Mathf.Sin(step)) * 4f);
            _walker.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(step) * 5f);
            // Dots: biggest just behind the carrier, getting smaller one by one further back.
            for (int i = 0; i < DotCount; i++)
            {
                float behind = Mathf.Repeat(_walkAngle - i * 360f / DotCount, 360f) / 360f; // 0 = right under the carrier
                float size = Mathf.Lerp(16f, 4f, behind);
                _dots[i].sizeDelta = new Vector2(size, size);
            }
        }

        /// <summary>Soft round dot, drawn once at runtime.</summary>
        private static Sprite MakeDotSprite()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size * .5f - 1f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + .5f - size * .5f, dy = y + .5f - size * .5f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + .5f)));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
        }

        private void Hide() => _hideAt = Mathf.Max(Time.unscaledTime, _shownAt + MinSeconds);

        /// <summary>Players in the room: the server knows them all; a client counts the replicated player states.</summary>
        private static int RoomPlayers()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return 1;
            if (manager.IsServer) return Mathf.Max(1, manager.ConnectedClientsIds.Count);
            int n = 0;
            foreach (var p in PlayerExpeditionState.Everyone) if (p != null && p.IsSpawned) n++;
            return Mathf.Max(1, n);
        }

        private void RefreshCount()
        {
            if (_count == null) return;
            int total = RoomPlayers();
            _count.text = _counting ? $"กำลังโหลด {Mathf.Min(_loaded.Count, total)}/{total} ผู้เล่น" : "กำลังโหลด...";
        }

        private void OnSceneEvent(SceneEvent e)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;
            bool local = e.ClientId == manager.LocalClientId;
            switch (e.SceneEventType)
            {
                case SceneEventType.Load when local:
                    Show(e.SceneName == "Lobby" ? "กลับ Lobby..." : "กำลังไป " + LevelMap.DisplayName(e.SceneName).Replace("\n", " "));
                    break;
                case SceneEventType.Synchronize when local:
                    Show("กำลังเข้าห้อง...", false);
                    break;
                case SceneEventType.LoadComplete:
                    _loaded.Add(e.ClientId);
                    RefreshCount();
                    // Wait for everyone; if the "all done" event never reaches us, give up after a few seconds.
                    if (local && _hideAt < 0f) _hideAt = Mathf.Max(Time.unscaledTime + 8f, _shownAt + MinSeconds);
                    break;
                case SceneEventType.LoadEventCompleted:
                    if (e.ClientsThatCompleted != null) foreach (var id in e.ClientsThatCompleted) _loaded.Add(id);
                    RefreshCount();
                    Hide();
                    break;
                case SceneEventType.SynchronizeComplete when local:
                    Hide();
                    break;
            }
        }

        private void Update()
        {
            var manager = NetworkManager.Singleton;
            var sceneManager = manager != null && manager.IsListening ? manager.SceneManager : null;
            if (sceneManager != _hooked)
            {
                if (_hooked != null) _hooked.OnSceneEvent -= OnSceneEvent;
                _hooked = sceneManager;
                if (_hooked != null) _hooked.OnSceneEvent += OnSceneEvent;
            }
            if (!_root.activeSelf) return;
            AnimateWalker();
            if (Time.frameCount % 20 == 0) RefreshCount();
            float now = Time.unscaledTime;
            bool timeout = now - _shownAt > MaxSeconds;
            if ((_hideAt >= 0f && now >= _hideAt) || timeout || sceneManager == null && now - _shownAt > MinSeconds)
            {
                _root.SetActive(false);
                _shownAt = _hideAt = -1f;
            }
        }
    }
}
