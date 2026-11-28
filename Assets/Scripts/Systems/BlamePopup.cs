using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Blame pop-ups (rule A3, <see cref="MischiefSettings.BlamePopups"/>): when the host blames a player for crate
    /// damage (<see cref="LevelResults"/>), a popup springs up over the crate on every screen, in place of the
    /// crate's own "-X" damage number: the player's face, "name ทำลังพัง" and a big red "-X HP" (George 2026-11-28:
    /// only at the crate, not over every blamed player). A hit blamed on two carriers is one popup with both names. It pops in with a
    /// shake, rises and fades out; more hits while it shows add up and restart it. A crate behind the camera gets
    /// the popup clamped to the screen edge, so everyone still sees who did it.
    /// (Was a banner at the top of the screen; George 2026-11-28 asked for a popup like the crate's HP drop.)
    /// Called on every machine through <see cref="PlayerExpeditionState.BlameRpc"/>.
    /// </summary>
    public sealed class BlamePopup : MonoBehaviour
    {
        private const float PopSeconds = .18f;
        private const float RisePixels = 70f;
        private const float FadeSeconds = .6f;
        private const float EdgeMargin = 110f;

        private sealed class Popup
        {
            public ulong Client;
            public int Damage;
            public readonly List<string> Names = new();
            public float Born, Until;
            public RectTransform Rect;
            public CanvasGroup Group;
            public Image Face;
            public Text Name, Amount;
        }

        private static BlamePopup _instance;
        private RectTransform _root;
        private readonly List<Popup> _popups = new();

        /// <summary>Texts of the popups showing now ("name|-X HP"), for tests.</summary>
        public static string[] Showing
        {
            get
            {
                if (_instance == null) return new string[0];
                var list = new List<string>();
                foreach (var p in _instance._popups)
                    if (p.Rect != null && p.Rect.gameObject.activeSelf) list.Add(p.Name.text + "|" + p.Amount.text);
                return list.ToArray();
            }
        }

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
        }

        /// <summary>Every machine: the host blamed this player for <paramref name="damage"/> crate HP.</summary>
        public static void Show(ulong client, int damage)
        {
            if (_instance == null || damage <= 0 || !MischiefSettings.Current.BlamePopups) return;
            _instance.Add(client, damage);
        }

        /// <summary>When the last blame popup was raised (unscaled time). The crate hides its own plain "-X" then.</summary>
        public static float LastShownAt { get; private set; } = -10f;

        private void Add(ulong client, int damage)
        {
            float now = Time.unscaledTime;
            string name = GameRoom.NameOfClient(client);
            Popup popup = _popups.Count > 0 ? _popups[0] : null;
            if (popup != null && popup.Rect != null && now < popup.Until)
            {
                // The same hit blamed on two carriers (RPCs ~together): one popup, both names, the hit once.
                if (now - popup.Born < .3f && !popup.Names.Contains(name)) { popup.Names.Add(name); popup.Damage = Mathf.Max(popup.Damage, damage); }
                else { if (!popup.Names.Contains(name)) popup.Names.Add(name); popup.Damage += damage; }
            }
            else
            {
                foreach (var old in _popups) if (old.Rect != null) Destroy(old.Rect.gameObject);
                _popups.Clear();
                popup = MakePopup(client);
                popup.Names.Add(name);
                popup.Damage = damage;
                _popups.Add(popup);
                StyleFace(popup.Face, client);
            }
            popup.Born = now;
            popup.Until = now + Mathf.Max(2.4f, MischiefSettings.Current.BlameMarkSeconds);
            popup.Name.text = string.Join(" + ", popup.Names) + " ทำลังพัง";
            popup.Amount.text = "-" + popup.Damage + " HP";
            LastShownAt = now;
            GameplayFeedback.Play(GameplayFeedback.Cue.BodyThud, Camera.main != null ? Camera.main.transform.position : Vector3.zero, .7f);
        }

        private Popup MakePopup(ulong client)
        {
            var go = new GameObject("Blame " + client, typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_root, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(.5f, 0f);
            rect.sizeDelta = new Vector2(360f, 150f);
            var group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // Bottom up: big red amount, the name line, the face on top.
            var amount = Label(rect, "Amount", 46, new Color(1f, .3f, .25f), 0f, 58f);
            var name = Label(rect, "Name", 24, Color.white, 56f, 34f);

            var face = new GameObject("Face", typeof(RectTransform), typeof(Image));
            var faceRect = (RectTransform)face.transform;
            faceRect.SetParent(rect, false);
            faceRect.anchorMin = faceRect.anchorMax = new Vector2(.5f, 0f);
            faceRect.pivot = new Vector2(.5f, 0f);
            faceRect.anchoredPosition = new Vector2(0f, 92f);
            faceRect.sizeDelta = new Vector2(46f, 46f);
            var faceImage = face.GetComponent<Image>();
            faceImage.preserveAspect = true;
            faceImage.raycastTarget = false;

            return new Popup { Client = client, Rect = rect, Group = group, Face = faceImage, Name = name, Amount = amount };
        }

        private static Text Label(RectTransform parent, string name, int size, Color color, float bottom, float height)
        {
            var text = UITheme.MakeText(parent, name, "", size, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.SetWeight(text, UITheme.FontBold);
            UITheme.AddOutline(text);
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var r = (RectTransform)text.transform;
            r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(.5f, 0f);
            r.anchoredPosition = new Vector2(0f, bottom);
            r.sizeDelta = new Vector2(0f, height);
            return text;
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

        private static CoopGame.CarrySystem.FragileCargo _crate;

        /// <summary>Above the level's crate: top of its drawn box + 1.6 m (clear of HP bar, compass and distance).</summary>
        private static bool TryCrate(out Vector3 above)
        {
            above = default;
            if (_crate == null || !_crate.isActiveAndEnabled)
            {
                var mission = FindAnyObjectByType<LevelMission>();
                _crate = mission != null && mission.Cargo != null ? mission.Cargo : FindAnyObjectByType<CoopGame.CarrySystem.FragileCargo>();
            }
            if (_crate == null) return false;
            Bounds b = new Bounds(_crate.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in _crate.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            above = new Vector3(b.center.x, b.max.y + 1.6f, b.center.z);
            return true;
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

            Camera cam = Camera.main;
            Rect area = _root.rect;
            float scale = area.width / Mathf.Max(1f, Screen.width);
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                Popup popup = _popups[i];
                if (popup.Rect == null || now > popup.Until || !TryCrate(out Vector3 head) || cam == null)
                {
                    if (popup.Rect != null) Destroy(popup.Rect.gameObject);
                    _popups.RemoveAt(i);
                    continue;
                }

                // Over the crate (above its HP bar, compass and distance); behind the camera or off screen it is
                // held at the screen edge.
                Vector3 screen = cam.WorldToScreenPoint(head);
                Vector2 pos = new Vector2(screen.x, screen.y) * scale;
                if (screen.z < 0f) pos = new Vector2(area.width - pos.x, EdgeMargin);
                pos.x = Mathf.Clamp(pos.x, EdgeMargin * 1.6f, area.width - EdgeMargin * 1.6f);
                pos.y = Mathf.Clamp(pos.y, EdgeMargin * .5f, area.height - EdgeMargin * 1.8f);

                float age = now - popup.Born;
                float life = popup.Until - popup.Born;
                float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / life)) * RisePixels;
                float shake = age < .35f ? Mathf.Sin(age * 70f) * 7f * (1f - age / .35f) : 0f;
                popup.Rect.anchoredPosition = pos + new Vector2(shake, rise);
                popup.Rect.localScale = Vector3.one * (age < PopSeconds ? Mathf.Lerp(1.5f, 1f, age / PopSeconds) : 1f);
                popup.Group.alpha = Mathf.Clamp01((popup.Until - now) / FadeSeconds);
            }
        }
    }
}
