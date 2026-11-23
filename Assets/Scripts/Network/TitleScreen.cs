using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Main menu shown once when the game starts, over the Lobby menu: logo, a column of buttons (play, settings,
    /// how to play, quit) and a wooden "credits" sign in the corner. It never blocks a session: it hides as soon as a
    /// NetworkManager is listening (e.g. the automated test host).
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        private const string Art = "UI/MainMenu/";
        private const float LogoX = 330f, LogoY = -230f;   // logo centre from the top-left corner (1920x1080 layout)
        private const float ButtonX = 330f, FirstButtonY = -540f, ButtonGap = 128f;

        private static bool _shownOnce;
        private GameObject _root, _howTo, _credits;
        private RectTransform _logo;
        private bool _forced;

        public static bool IsVisible { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            IsVisible = false;
            if (_shownOnce) return;
            _shownOnce = true;
            var go = new GameObject("TitleScreen");
            DontDestroyOnLoad(go);
            go.AddComponent<TitleScreen>().Build();
        }

        /// <summary>Shows the title again (test probe / report shots).</summary>
        public static void ShowAgain()
        {
            var title = FindAnyObjectByType<TitleScreen>();
            if (title == null) { title = new GameObject("TitleScreen").AddComponent<TitleScreen>(); DontDestroyOnLoad(title.gameObject); title.Build(); }
            title._root.SetActive(true);
            title._forced = true;
            IsVisible = true;
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

            _root = new GameObject("Title", typeof(RectTransform), typeof(Image));
            _root.transform.SetParent(transform, false);
            Stretch((RectTransform)_root.transform);
            _root.GetComponent<Image>().color = new Color(.47f, .74f, .95f, 1f); // sky, behind the art if it is missing

            var art = Resources.Load<Sprite>(Art + "MenuBackground");
            if (art != null)
            {
                var bg = new GameObject("Background", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
                bg.transform.SetParent(_root.transform, false);
                var bgImage = bg.GetComponent<Image>();
                bgImage.sprite = art;
                bgImage.raycastTarget = false;
                var fitter = bg.GetComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = art.rect.width / art.rect.height;
            }

            var logo = new GameObject("Logo", typeof(RectTransform), typeof(Image));
            logo.transform.SetParent(_root.transform, false);
            _logo = (RectTransform)logo.transform;
            _logo.anchorMin = _logo.anchorMax = new Vector2(0f, 1f);
            _logo.sizeDelta = new Vector2(520f, 460f);
            _logo.anchoredPosition = new Vector2(LogoX, LogoY);
            var logoImage = logo.GetComponent<Image>();
            logoImage.raycastTarget = false;
            var logoSprite = Resources.Load<Sprite>(Art + "GameLogo");
            if (logoSprite != null) { logoImage.sprite = logoSprite; logoImage.preserveAspect = true; }
            else { logoImage.enabled = false; UITheme.MakeCutePlank(logo.transform, "DON'T DROP IT!", 500f, 130f, 0f, 56); }

            MenuButton("Play", "เล่นเกม", "Play", 0, true, Hide);
            MenuButton("Settings", "ตั้งค่า", "Gear", 1, false, () => SettingsMenu.Open());
            MenuButton("HowTo", "วิธีเล่น", "Book", 2, false, () => ShowPanel(_howTo));
            MenuButton("Quit", "ออกจากเกม", "Exit", 3, false, Application.Quit);
            CreditsSign();

            _howTo = InfoPanel("HowToPlay", "วิธีเล่น", HowToText());
            _credits = InfoPanel("Credits", "เครดิต", CreditsText());
            IsVisible = true;
        }

        /// <summary>One button of the left column: icon on the left, label in the middle.</summary>
        private void MenuButton(string name, string label, string icon, int row, bool main, UnityEngine.Events.UnityAction onClick)
        {
            var button = UITheme.MakeButton(_root.transform, name, label, main ? UIButtonStyle.Pink : UIButtonStyle.White, new Vector2(440f, 104f), 34);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(ButtonX, FirstButtonY - row * ButtonGap);
            button.onClick.AddListener(onClick);
            var sprite = Resources.Load<Sprite>(Art + "Icons/" + icon);
            if (sprite == null) return;
            var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(button.transform, false);
            var iconRect = (RectTransform)go.transform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, .5f);
            iconRect.sizeDelta = new Vector2(52f, 52f);
            iconRect.anchoredPosition = new Vector2(66f, 4f);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = main ? Color.white : UITheme.CuteInk;
        }

        /// <summary>Wooden sign in the bottom-right corner with the round mascot peeking over it.</summary>
        private void CreditsSign()
        {
            var sign = new GameObject("CreditsSign", typeof(RectTransform), typeof(Image), typeof(Button));
            sign.transform.SetParent(_root.transform, false);
            var rect = (RectTransform)sign.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(300f, 100f);
            rect.anchoredPosition = new Vector2(-200f, 80f);
            var image = sign.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f); // hit area; the plank below draws the sign
            sign.GetComponent<Button>().onClick.AddListener(() => ShowPanel(_credits));
            sign.AddComponent<UIButtonHover>();
            var peek = Resources.Load<Sprite>(Art + "Icons/Peek");
            if (peek != null)
            {
                var mascot = new GameObject("Mascot", typeof(RectTransform), typeof(Image));
                mascot.transform.SetParent(sign.transform, false);
                var m = (RectTransform)mascot.transform;
                m.sizeDelta = new Vector2(112f, 92f);
                m.anchoredPosition = new Vector2(96f, 56f);
                var mi = mascot.GetComponent<Image>();
                mi.sprite = peek; mi.preserveAspect = true; mi.raycastTarget = false;
            }
            var plank = UITheme.MakeCutePlank(sign.transform, "เครดิต", 280f, 88f, 0f, 34);
            if (plank != null) plank.raycastTarget = false;
            foreach (var g in sign.GetComponentsInChildren<Graphic>()) if (g.gameObject != sign) g.raycastTarget = false;
            // The plank helper places its board at the card top; centre it on the sign instead.
            foreach (Transform child in sign.transform)
                if (child.name != "Mascot" && child is RectTransform r) { r.anchorMin = r.anchorMax = new Vector2(.5f, .5f); r.anchoredPosition = Vector2.zero; }
        }

        private static string HowToText()
        {
            string K(BindRole role) => GameSettings.KeyName(role);
            return "ช่วยกันขนลังไปส่งให้ถึงปลายทาง ลังมี HP ถ้าตกหรือชนแรงจะเสีย HP\n\n" +
                   "เดิน  " + K(BindRole.Forward) + " " + K(BindRole.Left) + " " + K(BindRole.Back) + " " + K(BindRole.Right) +
                   "      กระโดด  " + K(BindRole.Jump) + "      วิ่ง  " + K(BindRole.Sprint) + "\n" +
                   "ยก / วางลัง  " + K(BindRole.Carry) + "      ค้าง " + K(BindRole.Carry) + " = รับลังที่เพื่อนโยน\n" +
                   "ค้าง " + K(BindRole.Throw) + " เล็ง ปล่อยเพื่อโยน      ปีน  คลิกซ้าย / คลิกขวา\n" +
                   "ใช้ / กด  " + K(BindRole.Interact) + "      มุมกล้อง  " + K(BindRole.CameraView) + "\n" +
                   "บอกเพื่อน  " + K(BindRole.PingHere) + " มานี่   " + K(BindRole.PingHelp) + " ช่วยยก   " + K(BindRole.PingWarn) + " ระวัง";
        }

        private static string CreditsText()
        {
            return "DON'T DROP IT!\n\n" +
                   "ทีมทำเกม\nGeorge\ntibet151\n\n" +
                   "ฟอนต์ Mitr (SIL Open Font License)\nท่าทางตัวละคร KayKit, Mixamo";
        }

        /// <summary>A cute card over the menu with a title, body text and a close button.</summary>
        private GameObject InfoPanel(string name, string title, string body)
        {
            var dim = new GameObject(name, typeof(RectTransform), typeof(Image));
            dim.transform.SetParent(_root.transform, false);
            Stretch((RectTransform)dim.transform);
            dim.GetComponent<Image>().color = UITheme.Overlay;
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(dim.transform, false);
            ((RectTransform)card.transform).sizeDelta = new Vector2(1000f, 640f);
            UITheme.StyleCuteCard(card.GetComponent<Image>(), true);
            UITheme.MakeCutePlank(card.transform, title, 360f, 96f, 30f, 36);
            var text = UITheme.MakeText(card.transform, "Body", body, 28, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(text);
            text.lineSpacing = 1.15f;
            var textRect = (RectTransform)text.transform;
            textRect.sizeDelta = new Vector2(900f, 420f);
            textRect.anchoredPosition = new Vector2(0f, 10f);
            var close = UITheme.MakeButton(card.transform, "Close", "ปิด", UIButtonStyle.White, new Vector2(240f, 80f), 30);
            ((RectTransform)close.transform).anchoredPosition = new Vector2(0f, -250f);
            close.onClick.AddListener(() => dim.SetActive(false));
            dim.SetActive(false);
            return dim;
        }

        private static void ShowPanel(GameObject panel)
        {
            if (panel != null) panel.SetActive(true);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private void Hide()
        {
            if (_howTo != null) _howTo.SetActive(false);
            if (_credits != null) _credits.SetActive(false);
            _root.SetActive(false);
            _forced = false;
            IsVisible = false;
        }

        private void Update()
        {
            if (!_root.activeSelf) return;
            var manager = NetworkManager.Singleton;
            if (!_forced && manager != null && manager.IsListening) { Hide(); return; }
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            bool panelOpen = (_howTo != null && _howTo.activeSelf) || (_credits != null && _credits.activeSelf);
            if (keyboard != null && panelOpen && keyboard.escapeKey.wasPressedThisFrame) { _howTo.SetActive(false); _credits.SetActive(false); }
            else if (!SettingsMenu.IsOpen && !panelOpen && keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) { Hide(); return; }
            _logo.anchoredPosition = new Vector2(LogoX, LogoY + 6f * Mathf.Sin(Time.unscaledTime * 2.2f));
            _logo.localRotation = Quaternion.Euler(0f, 0f, 1.5f * Mathf.Sin(Time.unscaledTime * 1.3f));
            if (Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
    }
}
