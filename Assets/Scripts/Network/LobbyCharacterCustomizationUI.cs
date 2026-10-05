using CoopGame.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoopGame.Network
{
    [DisallowMultipleComponent]
    public sealed class LobbyCharacterCustomizationUI : MonoBehaviour
    {
        [SerializeField] private Texture2D[] _faceTextures;
        public static bool IsVisible { get; private set; }
        private static int _closedFrame = -1;
        public static bool BlocksPause => IsVisible || _closedFrame == Time.frameCount;
        private GameObject _panel;
        private RawImage _preview;
        private Text _styleLabel;
        private Text _status;
        private Outline[] _faceOutlines;
        private int _draft;
        private bool _restoreCursorLock;
        private Font _font;

        private void Awake()
        {
            if (!PlayerAppearance.IsLobby) { Destroy(gameObject); return; }
            Build();
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (IsVisible) Close(false);
        }

        private void OnSceneChanged(Scene previous, Scene next)
        {
            if (next.name != PlayerAppearance.LobbySceneName)
            {
                Close(true);
                Destroy(gameObject);
            }
        }

        private void Update()
        {
            if (!PlayerAppearance.IsLobby || PauseMenu.IsPaused) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            // Do not steal typing from room-code input fields.
            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.GetComponent<InputField>() != null) return;
            if (keyboard.cKey.wasPressedThisFrame) { if (IsVisible) Close(true); else Open(); }
            else if (IsVisible && keyboard.escapeKey.wasPressedThisFrame) Close(true);
        }

        public void Open()
        {
            if (!PlayerAppearance.IsLobby || PauseMenu.IsPaused || IsVisible) return;
            _draft = PlayerAppearance.LocalInstance != null ? PlayerAppearance.LocalInstance.CurrentFace : PlayerAppearance.SavedFace;
            _restoreCursorLock = Cursor.lockState == CursorLockMode.Locked;
            IsVisible = true;
            _panel.SetActive(true);
            PlayerCameraController.LocalInstance?.SetCursorLock(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Refresh();
        }

        public void Close(bool restoreCursor)
        {
            if (!IsVisible) return;
            IsVisible = false;
            _closedFrame = Time.frameCount;
            if (_panel != null) _panel.SetActive(false);
            if (restoreCursor && _restoreCursorLock && !PauseMenu.IsPaused)
            {
                PlayerCameraController.LocalInstance?.SetCursorLock(true);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void ChangeStyle(int direction)
        {
            _draft = (_draft + direction + PlayerAppearance.FaceCount) % PlayerAppearance.FaceCount;
            Refresh();
        }

        private void Refresh()
        {
            int style = _draft;
            _styleLabel.text = $"FACE {style + 1:00} / {PlayerAppearance.FaceCount}";
            _preview.texture = _faceTextures != null && style < _faceTextures.Length ? _faceTextures[style] : null;
            _preview.color = Color.white;
            for (int i = 0; i < _faceOutlines.Length; i++) _faceOutlines[i].enabled = i == style;
            _status.text = "LOBBY ONLY  /  Your face stays with you in the next level";
        }

        private void Save()
        {
            if (PlayerAppearance.SaveLocal(_draft)) Close(true);
        }

        private void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 450;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();
            var launch = Button(transform, "CUSTOMIZE FACE  [C]", new Vector2(260, 52), new Vector2(0, 0), Open);
            var launchRect = (RectTransform)launch.transform;
            launchRect.anchorMin = launchRect.anchorMax = new Vector2(0, 0);
            launchRect.pivot = new Vector2(0, 0);
            launchRect.anchoredPosition = new Vector2(24, 24);
            _panel = Box(transform, "FaceCustomizationOverlay", Vector2.zero, Vector2.zero, new Color(0, 0, 0, .65f));
            var overlayRect = (RectTransform)_panel.transform;
            overlayRect.anchorMin = Vector2.zero; overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
            var content = Box(_panel.transform, "FaceCustomization", new Vector2(660, 740), Vector2.zero, new Color(.07f, .1f, .16f, .99f));
            Label(content.transform, "CUSTOMIZE YOUR FACE", 28, new Vector2(580, 48), new Vector2(0, 318));
            var previewBox = Box(content.transform, "Preview", new Vector2(280, 256), new Vector2(0, 144), new Color(.2f, .6f, 1f));
            var previewGO = new GameObject("Eyes", typeof(RectTransform), typeof(RawImage));
            previewGO.transform.SetParent(previewBox.transform, false);
            var rect = (RectTransform)previewGO.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            _preview = previewGO.GetComponent<RawImage>(); _preview.raycastTarget = false;
            _styleLabel = Label(content.transform, "", 22, new Vector2(340, 44), new Vector2(0, -24));
            Button(content.transform, "<", new Vector2(58, 44), new Vector2(-230, -24), () => ChangeStyle(-1));
            Button(content.transform, ">", new Vector2(58, 44), new Vector2(230, -24), () => ChangeStyle(1));
            Label(content.transform, "CHOOSE A FACE", 16, new Vector2(300, 28), new Vector2(0, -76));
            _faceOutlines = new Outline[PlayerAppearance.FaceCount];
            for (int i = 0; i < _faceOutlines.Length; i++)
            {
                int index = i;
                var button = Button(content.transform, "", new Vector2(72, 66), new Vector2((i % 6 - 2.5f) * 88, -130 - i / 6 * 84), () =>
                {
                    _draft = index;
                    Refresh();
                });
                button.GetComponent<Image>().color = new Color(.2f, .6f, 1f);
                var thumbnail = new GameObject("FaceThumbnail", typeof(RectTransform), typeof(RawImage));
                thumbnail.transform.SetParent(button.transform, false);
                var thumbnailRect = (RectTransform)thumbnail.transform;
                thumbnailRect.anchorMin = Vector2.zero; thumbnailRect.anchorMax = Vector2.one;
                thumbnailRect.offsetMin = thumbnailRect.offsetMax = Vector2.zero;
                var thumbnailImage = thumbnail.GetComponent<RawImage>();
                thumbnailImage.texture = _faceTextures != null && i < _faceTextures.Length ? _faceTextures[i] : null;
                thumbnailImage.raycastTarget = false;
                var outline = button.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(.3f, .9f, 1f); outline.effectDistance = new Vector2(3, 3);
                _faceOutlines[i] = outline;
            }
            _status = Label(content.transform, "", 14, new Vector2(610, 34), new Vector2(0, -272));
            Button(content.transform, "CANCEL", new Vector2(230, 48), new Vector2(-128, -328), () => Close(true));
            Button(content.transform, "SAVE FACE", new Vector2(230, 48), new Vector2(128, -328), Save);
            _panel.SetActive(false);
        }

        private GameObject Box(Transform parent, string name, Vector2 size, Vector2 position, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.sizeDelta = size; rect.anchoredPosition = position;
            go.GetComponent<Image>().color = color;
            return go;
        }

        private Text Label(Transform parent, string text, int fontSize, Vector2 size, Vector2 position)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.sizeDelta = size; rect.anchoredPosition = position;
            var label = go.GetComponent<Text>(); label.font = _font; label.fontSize = fontSize;
            label.text = text; label.color = Color.white; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            return label;
        }

        private Button Button(Transform parent, string text, Vector2 size, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            var go = Box(parent, text, size, position, new Color(.17f, .24f, .34f));
            var button = go.AddComponent<Button>(); button.targetGraphic = go.GetComponent<Image>();
            button.onClick.AddListener(action);
            Label(go.transform, text, 18, size, Vector2.zero);
            return button;
        }
    }
}
