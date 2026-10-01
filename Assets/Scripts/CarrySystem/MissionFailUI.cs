using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoopGame.CarrySystem
{
    /// <summary>Local presentation of the server-authoritative cargo failure.</summary>
    [DisallowMultipleComponent]
    public sealed class MissionFailUI : MonoBehaviour
    {
        private static MissionFailUI _instance;
        private GameObject _overlay;
        private Button _restartButton;
        private Text _status;
        private bool _shown;

        public static bool IsVisible => _instance != null && _instance._shown;

        public static MissionFailUI EnsureInstance()
        {
            if (_instance != null) return _instance;
            GameObject ui = new GameObject("MissionFailUI", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(MissionFailUI));
            return ui.GetComponent<MissionFailUI>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            Canvas canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            CanvasScaler scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            BuildUI();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public void Show()
        {
            if (_shown) return;
            CoopGame.Network.PauseMenu.Instance?.HidePause(instant: true);
            _shown = true;
            _overlay.SetActive(true);

            if (EventSystem.current == null)
                new GameObject("MissionFail EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            NetworkManager manager = NetworkManager.Singleton;
            bool canRestart = manager == null || !manager.IsListening || manager.IsServer;
            _restartButton.gameObject.SetActive(canRestart);
            _status.text = canRestart ? "ลังเสียหายจน HP เหลือ 0\nเริ่มด่านใหม่เพื่อรับลังและลองอีกครั้ง" :
                "ลังเสียหายจน HP เหลือ 0\nกำลังรอ Host เริ่มด่านใหม่...";

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            CoopGame.Player.PlayerCameraController.LocalInstance?.SetCursorLock(false);
        }

        private void OnRestartClicked()
        {
            string scenePath = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(scenePath) || !Application.CanStreamedLevelBeLoaded(scenePath))
            {
                _status.text = "This level is unavailable. Check Build Settings.";
                return;
            }

            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening)
            {
                if (!manager.IsServer || manager.SceneManager == null) return;
                SceneEventProgressStatus status = manager.SceneManager.LoadScene(scenePath, LoadSceneMode.Single);
                if (status != SceneEventProgressStatus.Started)
                {
                    _status.text = "Restart failed. Please try again.";
                    Debug.LogError($"[MissionFailUI] Level restart failed: {status}.");
                    return;
                }
            }
            else
            {
                SceneManager.LoadScene(scenePath, LoadSceneMode.Single);
            }

            _restartButton.interactable = false;
            _status.text = "Restarting level...";
        }

        private void BuildUI()
        {
            Font font = Resources.Load<Font>("Fonts/GameThai") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _overlay = new GameObject("Failure Overlay", typeof(RectTransform), typeof(Image));
            _overlay.transform.SetParent(transform, false);
            RectTransform full = _overlay.GetComponent<RectTransform>();
            full.anchorMin = Vector2.zero;
            full.anchorMax = Vector2.one;
            full.offsetMin = Vector2.zero;
            full.offsetMax = Vector2.zero;
            _overlay.GetComponent<Image>().color = new Color(0.02f, 0.04f, 0.07f, 0.86f);

            GameObject panel = new GameObject("Failure Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_overlay.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(650f, 350f);
            panel.GetComponent<Image>().color = new Color(0.09f, 0.13f, 0.2f, 0.97f);

            CreateText("Title", panel.transform, font, "ภารกิจล้มเหลว", 52,
                new Vector2(0f, 94f), new Vector2(580f, 76f), new Color(1f, 0.35f, 0.3f));
            _status = CreateText("Status", panel.transform, font, "The item was destroyed.", 26,
                new Vector2(0f, 15f), new Vector2(580f, 95f), Color.white);

            GameObject buttonObject = new GameObject("Restart Level", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(panel.transform, false);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchoredPosition = new Vector2(0f, -92f);
            buttonRect.sizeDelta = new Vector2(310f, 68f);
            buttonObject.GetComponent<Image>().color = new Color(0.95f, 0.55f, 0.16f);
            _restartButton = buttonObject.GetComponent<Button>();
            buttonObject.AddComponent<UIButtonHover>();
            _restartButton.onClick.AddListener(OnRestartClicked);
            CreateText("Button Label", buttonObject.transform, font, "เริ่มด่านใหม่", 27,
                Vector2.zero, buttonRect.sizeDelta, new Color(0.08f, 0.08f, 0.1f));
            _overlay.SetActive(false);
        }

        private static Text CreateText(string name, Transform parent, Font font, string value,
            int fontSize, Vector2 position, Vector2 size, Color color)
        {
            GameObject objectWithText = new GameObject(name, typeof(RectTransform), typeof(Text));
            objectWithText.transform.SetParent(parent, false);
            RectTransform rect = objectWithText.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Text label = objectWithText.GetComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = color;
            label.raycastTarget = false;
            label.text = value;
            return label;
        }
    }
}
