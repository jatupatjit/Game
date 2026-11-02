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
        private Button _restartButton, _lobbyButton;
        private Text _status, _hpValue, _timeValue, _coinValue;
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
            _restartButton.interactable = _lobbyButton.interactable = canRestart;
            _status.text = canRestart ? "ลังเสียหายจน HP เหลือ 0" : "ลังเสียหายจน HP เหลือ 0 • รอ Host เลือก";
            var run = FindAnyObjectByType<CoopGame.Network.LevelRunState>();
            float seconds = run != null && run.IsSpawned ? run.ElapsedSeconds : 0f;
            _hpValue.text = "0/100";
            _timeValue.text = seconds > 0f ? $"{(int)seconds / 60:00}:{(int)seconds % 60:00}" : "--:--";
            _coinValue.text = "--";

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

            _restartButton.interactable = _lobbyButton.interactable = false;
            _status.text = "กำลังเริ่มด่านใหม่...";
        }

        /// <summary>Host (or offline): everyone back to the Lobby, the session stays connected.</summary>
        private void OnLobbyClicked()
        {
            const string lobby = CoopGame.Player.PlayerAppearance.LobbySceneName;
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening)
            {
                if (!manager.IsServer || manager.SceneManager == null) return;
                if (manager.SceneManager.LoadScene(lobby, LoadSceneMode.Single) != SceneEventProgressStatus.Started)
                {
                    _status.text = "กลับ Lobby ไม่สำเร็จ ลองอีกครั้ง";
                    return;
                }
            }
            else SceneManager.LoadScene(lobby, LoadSceneMode.Single);
            _restartButton.interactable = _lobbyButton.interactable = false;
            _status.text = "กำลังกลับ Lobby...";
        }

        /// <summary>Cute kit result card (George's mockup 2026-11-02), matching MissionCompleteUI: red plank title,
        /// beige card with cargo HP / time / coins, "ลองใหม่" and "กลับ Lobby" on the bottom edge.</summary>
        private void BuildUI()
        {
            _overlay = new GameObject("Failure Overlay", typeof(RectTransform), typeof(Image));
            _overlay.transform.SetParent(transform, false);
            RectTransform full = _overlay.GetComponent<RectTransform>();
            full.anchorMin = Vector2.zero;
            full.anchorMax = Vector2.one;
            full.offsetMin = Vector2.zero;
            full.offsetMax = Vector2.zero;
            _overlay.GetComponent<Image>().color = UITheme.Overlay;

            GameObject panel = new GameObject("Failure Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(_overlay.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(700f, 400f);
            panelRect.anchoredPosition = new Vector2(0f, -10f);
            UITheme.StyleCuteCard(panel.GetComponent<Image>());
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(70, 70, 92, 96);
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text title = UITheme.MakeCutePlank(panel.transform, "ส่งพัสดุไม่สำเร็จ", 500f, 118f, 0f, 44);
            UITheme.SetWeight(title, UITheme.FontBold);
            title.transform.parent.GetComponent<Image>().color = new Color(1f, 0.62f, 0.62f); // wood tinted red
            UITheme.AddCuteTape(panel.transform, 3, new Vector2(0f, 1f), new Vector2(26f, -44f), 30f, 100f);
            UITheme.AddCuteTape(panel.transform, 7, new Vector2(1f, 0f), new Vector2(-28f, 42f), -30f, 100f);

            _hpValue = CoopGame.Network.MissionCompleteUI.StatRow(panel.transform, "พัสดุ HP");
            _hpValue.color = UITheme.Danger;
            _timeValue = CoopGame.Network.MissionCompleteUI.StatRow(panel.transform, "เวลา");
            _coinValue = CoopGame.Network.MissionCompleteUI.StatRow(panel.transform, "เหรียญที่ได้รับ", false);
            _status = UITheme.MakeText(panel.transform, "Status", "", 22, UITextRole.OnLight, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(_status, true);
            UITheme.SetPreferredHeight(_status.gameObject, 34f);

            GameObject buttons = new GameObject("Buttons", typeof(RectTransform), typeof(LayoutElement));
            buttons.transform.SetParent(panel.transform, false);
            buttons.GetComponent<LayoutElement>().ignoreLayout = true;
            RectTransform buttonsRect = buttons.GetComponent<RectTransform>();
            buttonsRect.anchorMin = buttonsRect.anchorMax = new Vector2(.5f, 0f);
            buttonsRect.sizeDelta = Vector2.zero;
            _restartButton = UITheme.MakeButton(buttons.transform, "Restart Level", "ลองใหม่", UIButtonStyle.Pink, new Vector2(320f, 92f), 32);
            _restartButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(-175f, -34f);
            _restartButton.onClick.AddListener(OnRestartClicked);
            _lobbyButton = UITheme.MakeButton(buttons.transform, "Back To Lobby", "กลับ Lobby", UIButtonStyle.White, new Vector2(320f, 92f), 32);
            _lobbyButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(175f, -34f);
            _lobbyButton.onClick.AddListener(OnLobbyClicked);
            _overlay.SetActive(false);
        }
    }
}
