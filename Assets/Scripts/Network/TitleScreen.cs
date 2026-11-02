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
            _root.GetComponent<Image>().color = new Color(.47f, .74f, .95f, .96f); // sky

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(_root.transform, false);
            var cardRect = (RectTransform)card.transform;
            cardRect.sizeDelta = new Vector2(900f, 560f);
            cardRect.anchoredPosition = new Vector2(0f, -40f);
            UITheme.StyleCuteCard(card.GetComponent<Image>(), true);
            UITheme.AddCuteTape(card.transform, 5, new Vector2(1f, 1f), new Vector2(-90f, -50f), -25f, 110f);
            UITheme.AddCuteTape(card.transform, 2, new Vector2(0f, 0f), new Vector2(90f, 60f), 25f, 95f);

            UITheme.MakeCutePlank(card.transform, "DON'T DROP IT!", 680f, 150f, 8f, 72);
            var subtitle = UITheme.MakeText(card.transform, "Subtitle", "ช่วยกันขนลังไปส่ง อย่าให้หล่น!", 36, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(subtitle);
            var subtitleRect = (RectTransform)subtitle.transform;
            subtitleRect.sizeDelta = new Vector2(800f, 60f);
            subtitleRect.anchoredPosition = new Vector2(0f, 95f);

            var crate = new GameObject("Crate", typeof(RectTransform), typeof(Image));
            crate.transform.SetParent(card.transform, false);
            _crate = (RectTransform)crate.transform;
            _crate.sizeDelta = new Vector2(130f, 130f);
            _crate.anchoredPosition = new Vector2(0f, -20f);
            UITheme.StyleIcon(crate.GetComponent<Image>(), UITheme.IconCargoHud);

            var start = UITheme.MakeButton(card.transform, "Start", "เริ่มเล่น", UIButtonStyle.Pink, new Vector2(380f, 96f), 36);
            ((RectTransform)start.transform).anchoredPosition = new Vector2(-200f, -190f);
            start.onClick.AddListener(Hide);
            var quit = UITheme.MakeButton(card.transform, "Quit", "ออกจากเกม", UIButtonStyle.White, new Vector2(320f, 96f), 32);
            ((RectTransform)quit.transform).anchoredPosition = new Vector2(200f, -190f);
            quit.onClick.AddListener(Application.Quit);

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
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) { Hide(); return; }
            _crate.anchoredPosition = new Vector2(0f, -20f + 10f * Mathf.Sin(Time.unscaledTime * 2.2f));
            _crate.localRotation = Quaternion.Euler(0f, 0f, 6f * Mathf.Sin(Time.unscaledTime * 1.3f));
            if (Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
    }
}
