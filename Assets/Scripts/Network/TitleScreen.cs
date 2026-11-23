using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Title screen shown once when the game starts, over the Lobby menu: game logo, "เริ่มเล่น" and "ออกจากเกม".
    /// It never blocks a session: it hides as soon as a NetworkManager is listening (e.g. the automated test host).
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        private static bool _shownOnce;
        private GameObject _root;
        private RectTransform _crate;

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

        private bool _forced;
        private const float LogoX = 400f, LogoY = -320f; // logo centre from the top-left corner (1920x1080 layout)

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
            var rootImage = _root.GetComponent<Image>();
            rootImage.color = new Color(.47f, .74f, .95f, 1f); // sky, behind the art while it loads / if it is missing

            // His main menu art (background + logo), Resources/UI/MainMenu.
            var art = Resources.Load<Sprite>("UI/MainMenu/MenuBackground");
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

            var logoSprite = Resources.Load<Sprite>("UI/MainMenu/GameLogo");
            var logo = new GameObject("Logo", typeof(RectTransform), typeof(Image));
            logo.transform.SetParent(_root.transform, false);
            _crate = (RectTransform)logo.transform;
            _crate.anchorMin = _crate.anchorMax = new Vector2(0f, 1f);
            _crate.pivot = new Vector2(.5f, .5f);
            _crate.sizeDelta = new Vector2(620f, 550f);
            _crate.anchoredPosition = new Vector2(LogoX, LogoY);
            var logoImage = logo.GetComponent<Image>();
            logoImage.raycastTarget = false;
            if (logoSprite != null) { logoImage.sprite = logoSprite; logoImage.preserveAspect = true; }
            else { logoImage.enabled = false; UITheme.MakeCutePlank(logo.transform, "DON'T DROP IT!", 600f, 140f, 0f, 64); }

            var start = UITheme.MakeButton(_root.transform, "Start", "เริ่มเล่น", UIButtonStyle.White, new Vector2(380f, 96f), 36);
            var startRect = (RectTransform)start.transform;
            startRect.anchorMin = startRect.anchorMax = new Vector2(0f, 1f);
            startRect.anchoredPosition = new Vector2(LogoX, LogoY - 400f);
            start.onClick.AddListener(Hide);
            var quit = UITheme.MakeButton(_root.transform, "Quit", "ออกจากเกม", UIButtonStyle.White, new Vector2(380f, 96f), 32);
            var quitRect = (RectTransform)quit.transform;
            quitRect.anchorMin = quitRect.anchorMax = new Vector2(0f, 1f);
            quitRect.anchoredPosition = new Vector2(LogoX, LogoY - 512f);
            quit.onClick.AddListener(Application.Quit);
            var settings = UITheme.MakeButton(_root.transform, "Settings", "ตั้งค่า", UIButtonStyle.White, new Vector2(240f, 84f), 30);
            var settingsRect = (RectTransform)settings.transform;
            settingsRect.anchorMin = settingsRect.anchorMax = new Vector2(1f, 1f);
            settingsRect.anchoredPosition = new Vector2(-170f, -80f);
            settings.onClick.AddListener(() => SettingsMenu.Open());

            var hint = UITheme.MakeText(_root.transform, "Hint", "Enter = เริ่ม", 22, UITextRole.Secondary, TextAnchor.MiddleCenter);
            var hintRect = (RectTransform)hint.transform;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(.5f, 0f);
            hintRect.sizeDelta = new Vector2(600f, 40f);
            hintRect.anchoredPosition = new Vector2(0f, 50f);
            IsVisible = true;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private void Hide()
        {
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
            if (!SettingsMenu.IsOpen && keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) { Hide(); return; }
            _crate.anchoredPosition = new Vector2(LogoX, LogoY + 8f * Mathf.Sin(Time.unscaledTime * 2.2f));
            _crate.localRotation = Quaternion.Euler(0f, 0f, 2f * Mathf.Sin(Time.unscaledTime * 1.3f));
            if (Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
    }
}
