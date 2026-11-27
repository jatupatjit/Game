using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Blame pop-ups (rule A3, <see cref="MischiefSettings.BlamePopups"/>): when the host blames a player for crate
    /// damage (<see cref="LevelResults"/>), every screen shows a banner at the top with that player's face and
    /// "name ทำลังพัง -X HP", and a red "-X HP" mark floats over that player's head for a few seconds (more hits add up).
    /// Called on every machine through <see cref="PlayerExpeditionState.BlameRpc"/>.
    /// </summary>
    public sealed class BlamePopup : MonoBehaviour
    {
        private const float BannerSeconds = 2.6f;

        private sealed class Mark
        {
            public ulong Client;
            public int Damage;
            public float Until, Born;
            public RectTransform Rect;
            public Text Label;
        }

        private static BlamePopup _instance;
        private RectTransform _root, _banner;
        private CanvasGroup _bannerGroup;
        private Image _bannerFace;
        private Text _bannerText;
        private float _bannerBorn = -10f;
        private string _bannerNames;
        private int _bannerDamage;
        private readonly List<Mark> _marks = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("BlamePopup");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BlamePopup>();
            _instance.Build();
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 141;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            _root = new GameObject("Root", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(transform, false);
            _root.anchorMin = Vector2.zero; _root.anchorMax = Vector2.one;
            _root.offsetMin = _root.offsetMax = Vector2.zero;

            var banner = new GameObject("Banner", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            _banner = (RectTransform)banner.transform;
            _banner.SetParent(_root, false);
            _banner.anchorMin = _banner.anchorMax = new Vector2(.5f, 1f);
            _banner.pivot = new Vector2(.5f, 1f);
            _banner.anchoredPosition = new Vector2(0f, -150f);
            _banner.sizeDelta = new Vector2(420f, 56f);
            var bg = banner.GetComponent<Image>();
            UITheme.StyleShape(bg, UITheme.SmallSprite, new Color(.32f, .06f, .07f, .92f));
            bg.raycastTarget = false;
            _bannerGroup = banner.GetComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;

            var face = new GameObject("Face", typeof(RectTransform), typeof(Image));
            var faceRect = (RectTransform)face.transform;
            faceRect.SetParent(_banner, false);
            faceRect.anchorMin = faceRect.anchorMax = new Vector2(0f, .5f);
            faceRect.pivot = new Vector2(0f, .5f);
            faceRect.anchoredPosition = new Vector2(9f, 0f);
            faceRect.sizeDelta = new Vector2(42f, 42f);
            _bannerFace = face.GetComponent<Image>();
            _bannerFace.preserveAspect = true;
            _bannerFace.raycastTarget = false;

            _bannerText = UITheme.MakeText(_banner, "Text", "", 22, UITextRole.Primary, TextAnchor.MiddleLeft);
            _bannerText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _bannerText.raycastTarget = false;
            UITheme.SetWeight(_bannerText, UITheme.FontBold);
            UITheme.AddOutline(_bannerText);
            var textRect = (RectTransform)_bannerText.transform;
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(60f, 0f); textRect.offsetMax = new Vector2(-12f, 0f);
        }

        /// <summary>Every machine: the host blamed this player for <paramref name="damage"/> crate HP.</summary>
        public static void Show(ulong client, int damage)
        {
            if (_instance == null || damage <= 0 || !MischiefSettings.Current.BlamePopups) return;
            _instance.Add(client, damage);
        }

        private void Add(ulong client, int damage)
        {
            float now = Time.unscaledTime;
            string name = GameRoom.NameOfClient(client);
            // Two carriers blamed for the same hit share one banner.
            if (now - _bannerBorn < .3f && _bannerNames != null && !_bannerNames.Contains(name)) _bannerNames += " + " + name;
            else { _bannerNames = name; _bannerDamage = 0; }
            _bannerDamage = Mathf.Max(_bannerDamage, damage);
            _bannerBorn = now;
            _bannerText.text = $"{_bannerNames} ทำลังพัง <color=#FF6B5E>-{_bannerDamage} HP</color>";
            _bannerText.supportRichText = true;
            float width = Mathf.Clamp(_bannerText.preferredWidth + 80f, 240f, 760f);
            _banner.sizeDelta = new Vector2(width, 56f);
            StyleFace(_bannerFace, client);
            GameplayFeedback.Play(GameplayFeedback.Cue.BodyThud, Camera.main != null ? Camera.main.transform.position : Vector3.zero, .7f);

            Mark mark = _marks.Find(m => m.Client == client && m.Rect != null);
            if (mark == null)
            {
                var go = new GameObject("Mark", typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.SetParent(_root, false);
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(.5f, 0f);
                rect.sizeDelta = new Vector2(220f, 60f);
                var label = UITheme.MakeText(rect, "Text", "", 28, UITextRole.Primary, TextAnchor.LowerCenter);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.raycastTarget = false;
                label.color = new Color(1f, .3f, .25f);
                UITheme.SetWeight(label, UITheme.FontBold);
                UITheme.AddOutline(label);
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
                mark = new Mark { Client = client, Rect = rect, Label = label };
                _marks.Add(mark);
            }
            mark.Damage += damage;
            mark.Born = now;
            mark.Until = now + MischiefSettings.Current.BlameMarkSeconds;
            mark.Label.text = "-" + mark.Damage + " HP";
        }

        private static void StyleFace(Image image, ulong client)
        {
            foreach (var p in PlayerExpeditionState.Everyone)
            {
                if (p == null || !p.IsSpawned || p.OwnerClientId != client) continue;
                var appearance = p.GetComponent<CoopGame.Player.PlayerAppearance>();
                if (appearance != null)
                {
                    UITheme.StyleIcon(image, UITheme.Kit("FaceGrey"));
                    image.color = Color.Lerp(appearance.CurrentBodyColor, Color.white, .15f);
                    return;
                }
            }
            UITheme.StyleIcon(image, UITheme.FaceForClient(client));
            image.color = Color.white;
        }

        private static bool TryHead(ulong client, out Vector3 head)
        {
            foreach (var p in PlayerExpeditionState.Everyone)
            {
                if (p == null || !p.IsSpawned || p.OwnerClientId != client) continue;
                head = p.TryGetComponent(out CoopGame.Player.RagdollCharacter rc) ? rc.HeadPosition : p.transform.position + Vector3.up * 1.7f;
                return true;
            }
            head = default;
            return false;
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            bool hide = PhotoMode.IsOn || TitleScreen.IsVisible;
            if (_root.gameObject.activeSelf == hide) _root.gameObject.SetActive(!hide);

            // Banner: pops in big, settles, fades out.
            float age = now - _bannerBorn;
            float alpha = age < BannerSeconds ? 1f : 1f - Mathf.Clamp01((age - BannerSeconds) / .5f);
            _bannerGroup.alpha = alpha;
            float pop = age < .15f ? Mathf.Lerp(1.2f, 1f, age / .15f) : 1f;
            float shake = age < .35f ? Mathf.Sin(age * 70f) * 6f * (1f - age / .35f) : 0f;
            _banner.localScale = Vector3.one * pop;
            _banner.anchoredPosition = new Vector2(shake, -150f);

            Camera cam = Camera.main;
            var canvasRect = (RectTransform)transform;
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                Mark mark = _marks[i];
                if (mark.Rect == null || now > mark.Until || !TryHead(mark.Client, out Vector3 head))
                {
                    if (mark.Rect != null) Destroy(mark.Rect.gameObject);
                    _marks.RemoveAt(i);
                    continue;
                }
                Vector3 screen = cam != null ? cam.WorldToScreenPoint(head + Vector3.up * .45f) : new Vector3(-999f, -999f, -1f);
                bool visible = screen.z > 0f;
                mark.Rect.gameObject.SetActive(visible);
                if (!visible) continue;
                float scale = canvasRect.rect.width / Mathf.Max(1f, Screen.width);
                float bob = Mathf.Sin(now * 4f) * 6f;
                mark.Rect.anchoredPosition = new Vector2(screen.x * scale, screen.y * scale + bob);
                float markAge = now - mark.Born;
                mark.Rect.localScale = Vector3.one * (markAge < .15f ? Mathf.Lerp(1.6f, 1f, markAge / .15f) : 1f);
                var c = mark.Label.color;
                c.a = Mathf.Clamp01((mark.Until - now) / .6f);
                mark.Label.color = c;
            }
        }
    }
}
