using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Loading screen while a networked scene loads (level start, back to Lobby, joining a room mid-level).
    /// The UIKit carrier icon walks along a straight dotted path in the middle, "กำลังโหลด x/y ผู้เล่น" counts players that
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
            "ถุงมือหนังช่วยให้ยกลังเหนื่อยช้าลง ซื้อได้ที่ร้านอุปกรณ์ในล็อบบี้",
            "ลังไก่จะดิ้น จับให้แน่นแล้วเดินช้าๆ",
            "ลังซ้อนสูงไม่ชอบการออกตัวแรงๆ",
            "ลังระเบิด... อย่าทำหล่นใกล้เพื่อน",
            "ล้มแล้วรอสักครู่ ตัวละครจะลุกขึ้นเอง",
            "ปีนกำแพงด้วยเมาส์ซ้าย/ขวา สลับมือขึ้นไป",
            "กด V เพื่อสลับมุมกล้องบุคคลที่ 1 / 3",
        };

        private const float PathLength = 560f, WalkPixelsPerSecond = 190f, EdgeFade = 60f;

        private GameObject _root;
        private Text _tip, _title, _count;
        private const int DotCount = 15;
        private RectTransform _orbit, _walker;
        private RectTransform[] _dots;
        private Image[] _dotImages;
        private Image _walkerIcon;
        private float _shownAt = -1f, _hideAt = -1f, _walkX;
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

            // A straight dotted path; the carrier walks along it left to right (feet on the dots), the dots behind it
            // run from big to small like a trail, and it fades out at the end and walks in again from the start.
            var track = new GameObject("DotPath", typeof(RectTransform)).GetComponent<RectTransform>();
            track.SetParent(rect, false);
            track.anchoredPosition = new Vector2(0f, 10f);
            track.sizeDelta = Vector2.zero;
            var dotSprite = MakeDotSprite();
            _dots = new RectTransform[DotCount];
            _dotImages = new Image[DotCount];
            for (int i = 0; i < DotCount; i++)
            {
                var dot = new GameObject("Dot" + i, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                dot.SetParent(track, false);
                dot.anchoredPosition = new Vector2(DotX(i), 0f);
                var img = dot.GetComponent<Image>();
                img.sprite = dotSprite;
                img.color = UITheme.Primary;
                img.raycastTarget = false;
                _dots[i] = dot;
                _dotImages[i] = img;
            }
            _orbit = track;
            var walker = new GameObject("Carrier", typeof(RectTransform), typeof(Image));
            _walker = (RectTransform)walker.transform;
            _walker.SetParent(_orbit, false);
            var icon = walker.GetComponent<Image>();
            _walkerIcon = icon;
            // Hand-drawn carrier (faces left, so mirrored to walk right); old UIKit icon as fallback.
            icon.sprite = Resources.Load<Sprite>("UI/HandDrawn/PoseCarry");
            if (icon.sprite != null)
            {
                _walker.pivot = new Vector2(.5f, .04f); // feet at the bottom middle of PoseCarry 472x512
                _walker.sizeDelta = new Vector2(110f, 120f);
                _walker.localScale = new Vector3(-1f, 1f, 1f);
            }
            else
            {
                _walker.pivot = new Vector2(.34f, .05f); // the feet (left part of the icon) stand on the path
                _walker.sizeDelta = new Vector2(70f, 67f); // IconCarrier 174x166
                icon.sprite = Resources.Load<Sprite>("UIKit/IconCarrier");
            }
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

        private static float DotX(int i) => -PathLength * .5f + PathLength * i / (DotCount - 1);

        /// <summary>The carrier walks left to right along the path with a little waddle, fades out at the end and
        /// walks in again from the start.</summary>
        private void AnimateWalker()
        {
            float step = Time.unscaledTime * 11f;
            _walkX = Mathf.Repeat(_walkX + WalkPixelsPerSecond * Time.unscaledDeltaTime, PathLength);
            float x = -PathLength * .5f + _walkX;
            float fade = Mathf.Clamp01(Mathf.Min(_walkX, PathLength - _walkX) / EdgeFade);
            _walker.anchoredPosition = new Vector2(x, 6f + Mathf.Abs(Mathf.Sin(step)) * 4f);
            _walker.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(step) * 5f);
            if (_walkerIcon != null) { var c = _walkerIcon.color; c.a = fade; _walkerIcon.color = c; }
            // Dots: biggest just behind the carrier, getting smaller further back; the path ahead is small and faint.
            for (int i = 0; i < DotCount; i++)
            {
                float behind = x - DotX(i);
                float size = behind >= 0f ? Mathf.Lerp(16f, 6f, behind / (PathLength * .6f)) : 6f;
                _dots[i].sizeDelta = new Vector2(size, size);
                var col = _dotImages[i].color;
                col.a = behind >= 0f ? 1f : .45f;
                _dotImages[i].color = col;
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
