using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Event feed (like a kill feed, on the left side of the screen): "P1 George ทำลังกระแทก -12", "P2 ล้ม", ...
    /// The newest line is on top; each slides in, stays ~5 s, then fades and slides away. Lines come from the host
    /// through <see cref="GameRoom.ServerFeed"/>; <see cref="Post"/> shows a line on this machine only.
    /// </summary>
    public sealed class EventFeed : MonoBehaviour
    {
        private const int MaxLines = 6;
        private const float LineSeconds = 5f, FadeSeconds = .7f, SlideIn = .18f, LineHeight = 46f;

        private sealed class Line
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public float Born;
            public float Y;
        }

        private static EventFeed _instance;
        private readonly List<Line> _lines = new();
        private RectTransform _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("EventFeed");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<EventFeed>();
            _instance.Build();
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 140;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            _root = new GameObject("Feed", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(transform, false);
            _root.anchorMin = _root.anchorMax = new Vector2(0f, .5f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = new Vector2(24f, -20f); // below the top-left player list
            _root.sizeDelta = new Vector2(620f, 400f);
        }

        /// <summary>Show a feed line on this machine.</summary>
        public static void Post(string text, Color color)
        {
            if (_instance == null || string.IsNullOrEmpty(text)) return;
            _instance.Add(text, color);
        }

        private void Add(string text, Color color)
        {
            var go = new GameObject("Line", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            var bg = go.GetComponent<Image>();
            bg.color = new Color(.08f, .07f, .1f, .72f);
            bg.raycastTarget = false;

            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            var barRect = (RectTransform)bar.transform;
            barRect.SetParent(rect, false);
            barRect.anchorMin = new Vector2(0f, 0f); barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, .5f);
            barRect.sizeDelta = new Vector2(7f, 0f);
            var barImage = bar.GetComponent<Image>();
            barImage.color = color; barImage.raycastTarget = false;

            var label = UITheme.MakeText(rect, "Text", text, 24, UITextRole.Primary, TextAnchor.MiddleLeft);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.raycastTarget = false;
            label.color = Color.Lerp(Color.white, color, .35f);
            UITheme.AddOutline(label);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(18f, 0f); labelRect.offsetMax = new Vector2(-12f, 0f);
            float width = Mathf.Clamp(label.preferredWidth + 34f, 160f, 620f);
            rect.sizeDelta = new Vector2(width, LineHeight - 6f);

            _lines.Insert(0, new Line { Rect = rect, Group = go.GetComponent<CanvasGroup>(), Born = Time.unscaledTime, Y = 0f });
            while (_lines.Count > MaxLines)
            {
                Destroy(_lines[_lines.Count - 1].Rect.gameObject);
                _lines.RemoveAt(_lines.Count - 1);
            }
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                var line = _lines[i];
                if (line.Rect == null) { _lines.RemoveAt(i); continue; }
                float age = now - line.Born;
                if (age > LineSeconds + FadeSeconds) { Destroy(line.Rect.gameObject); _lines.RemoveAt(i); continue; }
                float targetY = -i * LineHeight;
                line.Y = Mathf.Lerp(line.Y, targetY, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
                float x = 0f, alpha = 1f;
                if (age < SlideIn) { float t = age / SlideIn; x = -60f * (1f - t); alpha = t; }
                else if (age > LineSeconds) { float t = (age - LineSeconds) / FadeSeconds; x = -80f * t * t; alpha = 1f - t; }
                line.Rect.anchoredPosition = new Vector2(x, line.Y);
                line.Group.alpha = alpha;
            }
            bool hide = PhotoMode.IsOn || TitleScreen.IsVisible;
            if (_root.gameObject.activeSelf == hide) _root.gameObject.SetActive(!hide);
        }
    }
}
