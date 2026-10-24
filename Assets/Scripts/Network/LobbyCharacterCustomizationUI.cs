using CoopGame.Player;
using Unity.Netcode;
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
        private Button _launchButton;
        private LobbyUI _lobbyUI;
        private Image _previewBoxImage;
        private RawImage _preview;
        private Text _styleLabel;
        private Text _status;
        private Image[] _faceBgImages;
        private Outline[] _faceOutlines;
        private Outline[] _colorOutlines;
        private int _draft;
        private int _draftColor;
        private bool _restoreCursorLock;
        private Font _font;
        private static Sprite _hangerSprite;

        private bool CanCustomize
        {
            get
            {
                var manager = NetworkManager.Singleton;
                var player = PlayerAppearance.LocalInstance;
                return PlayerAppearance.IsLobby && manager != null && manager.IsConnectedClient &&
                    !manager.ShutdownInProgress && player != null && player.IsSpawned && player.IsOwner &&
                    (_lobbyUI == null || !_lobbyUI.IsMenuVisible);
            }
        }

        private void Awake()
        {
            if (!PlayerAppearance.IsLobby) { Destroy(gameObject); return; }
            _lobbyUI = FindAnyObjectByType<LobbyUI>(FindObjectsInactive.Include);
            Build();
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (IsVisible) Close(false);
        }

        private void OnDisable()
        {
            Close(false);
            if (_launchButton != null) _launchButton.gameObject.SetActive(false);
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
            bool available = CanCustomize && !PauseMenu.IsPaused;
            if (_launchButton != null && _launchButton.gameObject.activeSelf != available)
                _launchButton.gameObject.SetActive(available);
            if (!available)
            {
                Close(false);
                return;
            }
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            // Do not steal typing from room-code input fields.
            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.GetComponent<InputField>() != null) return;
            if (keyboard.cKey.wasPressedThisFrame) { if (IsVisible) Cancel(); else Open(); }
            else if (IsVisible && keyboard.escapeKey.wasPressedThisFrame) Cancel();
        }

        public void Open()
        {
            if (!CanCustomize || PauseMenu.IsPaused || IsVisible) return;
            _draft = PlayerAppearance.LocalInstance != null ? PlayerAppearance.LocalInstance.CurrentFace : PlayerAppearance.SavedFace;
            _draftColor = PlayerAppearance.LocalInstance != null ? PlayerAppearance.LocalInstance.CurrentColor : PlayerAppearance.SavedColor;
            _restoreCursorLock = Cursor.lockState == CursorLockMode.Locked;
            IsVisible = true;
            _panel.SetActive(true);
            PlayerCameraController.LocalInstance?.SetCursorLock(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Refresh();
        }

        private void Cancel()
        {
            if (PlayerAppearance.LocalInstance != null)
            {
                PlayerAppearance.LocalInstance.ApplyFace(PlayerAppearance.LocalInstance.CurrentFace);
                PlayerAppearance.LocalInstance.ApplyColor(PlayerAppearance.LocalInstance.CurrentColor);
            }
            Close(true);
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
            Color bodyColor = PlayerAppearance.GetPastelColor(_draftColor);
            _styleLabel.text = $"ใบหน้า {style + 1:00} / {PlayerAppearance.FaceCount}";
            if (_previewBoxImage != null) _previewBoxImage.color = bodyColor;
            _preview.texture = _faceTextures != null && style < _faceTextures.Length ? _faceTextures[style] : null;
            _preview.color = Color.white;
            for (int i = 0; i < _faceOutlines.Length; i++)
            {
                _faceOutlines[i].enabled = i == style;
                if (_faceBgImages != null && i < _faceBgImages.Length && _faceBgImages[i] != null)
                    _faceBgImages[i].color = bodyColor;
            }
            if (_colorOutlines != null)
            {
                for (int i = 0; i < _colorOutlines.Length; i++)
                    _colorOutlines[i].enabled = i == _draftColor;
            }
            if (PlayerAppearance.LocalInstance != null)
            {
                PlayerAppearance.LocalInstance.ApplyFace(_draft);
                PlayerAppearance.LocalInstance.ApplyColor(_draftColor);
            }
            string colorName = PlayerAppearance.GetPastelColorName(_draftColor);
            _status.text = $"สีพาสเทล: {colorName} ({_draftColor + 1:00}/{PlayerAppearance.ColorCount:00})   •   เปลี่ยนได้เฉพาะในล็อบบี้";
        }

        private void Save()
        {
            if (!CanCustomize || PauseMenu.IsPaused) { Close(false); return; }
            if (PlayerAppearance.SaveLocal(_draft, _draftColor)) Close(true);
        }

        private void Build()
        {
            _font = UITheme.Font;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 450;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();

            _launchButton = BuildBottomLeftCustomizeButton(transform, Open);
            _launchButton.gameObject.SetActive(false);

            _panel = Box(transform, "FaceCustomizationOverlay", Vector2.zero, Vector2.zero, UITheme.Overlay);
            var overlayRect = (RectTransform)_panel.transform;
            overlayRect.anchorMin = Vector2.zero; overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
            var content = Box(_panel.transform, "FaceCustomization", new Vector2(680, 840), Vector2.zero, UITheme.PanelSolid);
            UITheme.StyleSolidPanel(content.GetComponent<Image>());
            Label(content.transform, "แต่งตัวละคร", 28, new Vector2(580, 44), new Vector2(0, 368)).color = UITheme.Primary;
            var previewBox = Box(content.transform, "Preview", new Vector2(240, 214), new Vector2(0, 224), UITheme.FacePreview);
            _previewBoxImage = previewBox.GetComponent<Image>();
            UITheme.StyleShape(_previewBoxImage, UITheme.SmallSprite, UITheme.FacePreview);
            var previewGO = new GameObject("Eyes", typeof(RectTransform), typeof(RawImage));
            previewGO.transform.SetParent(previewBox.transform, false);
            var rect = (RectTransform)previewGO.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            _preview = previewGO.GetComponent<RawImage>(); _preview.raycastTarget = false;
            _styleLabel = Label(content.transform, "", 22, new Vector2(340, 40), new Vector2(0, 84));
            Button(content.transform, "<", new Vector2(58, 42), new Vector2(-230, 84), () => ChangeStyle(-1));
            Button(content.transform, ">", new Vector2(58, 42), new Vector2(230, 84), () => ChangeStyle(1));
            Label(content.transform, "เลือกใบหน้า", 16, new Vector2(300, 26), new Vector2(0, 42)).color = UITheme.TextSecondary;
            _faceOutlines = new Outline[PlayerAppearance.FaceCount];
            _faceBgImages = new Image[PlayerAppearance.FaceCount];
            for (int i = 0; i < _faceOutlines.Length; i++)
            {
                int index = i;
                var button = Button(content.transform, "", new Vector2(72, 64), new Vector2((i % 6 - 2.5f) * 88, -6 - i / 6 * 76), () =>
                {
                    _draft = index;
                    Refresh();
                });
                var bgImage = button.GetComponent<Image>();
                UITheme.StyleShape(bgImage, UITheme.SmallSprite, UITheme.FacePreview);
                _faceBgImages[i] = bgImage;
                var tint = button.colors; tint.normalColor = tint.selectedColor = Color.white; tint.highlightedColor = UITheme.TextSecondary; tint.pressedColor = UITheme.Pressed(Color.white); button.colors = tint;
                var thumbnail = new GameObject("FaceThumbnail", typeof(RectTransform), typeof(RawImage));
                thumbnail.transform.SetParent(button.transform, false);
                var thumbnailRect = (RectTransform)thumbnail.transform;
                thumbnailRect.anchorMin = Vector2.zero; thumbnailRect.anchorMax = Vector2.one;
                thumbnailRect.offsetMin = thumbnailRect.offsetMax = Vector2.zero;
                var thumbnailImage = thumbnail.GetComponent<RawImage>();
                thumbnailImage.texture = _faceTextures != null && i < _faceTextures.Length ? _faceTextures[i] : null;
                thumbnailImage.raycastTarget = false;
                var outline = button.gameObject.AddComponent<Outline>();
                outline.effectColor = UITheme.Neon; outline.effectDistance = new Vector2(4, -4);
                _faceOutlines[i] = outline;
            }

            Label(content.transform, "เลือกสีตัวละคร", 16, new Vector2(420, 26), new Vector2(0, -144)).color = UITheme.TextSecondary;
            _colorOutlines = new Outline[PlayerAppearance.ColorCount];
            float colorCenterIndex = (PlayerAppearance.ColorCount - 1) * 0.5f;
            for (int i = 0; i < PlayerAppearance.ColorCount; i++)
            {
                int colorIndex = i;
                Color pastel = PlayerAppearance.GetPastelColor(i);
                var colorBtn = Button(content.transform, "", new Vector2(64, 50), new Vector2((i - colorCenterIndex) * 76f, -194), () =>
                {
                    _draftColor = colorIndex;
                    Refresh();
                });
                UITheme.StyleShape(colorBtn.GetComponent<Image>(), UITheme.SmallSprite, pastel);
                var tint = colorBtn.colors;
                tint.normalColor = tint.selectedColor = Color.white;
                tint.highlightedColor = UITheme.Hover(Color.white);
                tint.pressedColor = UITheme.Pressed(Color.white);
                colorBtn.colors = tint;
                var outline = colorBtn.gameObject.AddComponent<Outline>();
                outline.effectColor = UITheme.Neon;
                outline.effectDistance = new Vector2(4, -4);
                _colorOutlines[i] = outline;
            }

            _status = Label(content.transform, "", 15, new Vector2(610, 32), new Vector2(0, -256));
            _status.color = UITheme.TextSecondary;
            Button(content.transform, "ยกเลิก", new Vector2(230, 48), new Vector2(-128, -326), Cancel);
            UITheme.StyleButton(Button(content.transform, "บันทึก", new Vector2(230, 48), new Vector2(128, -326), Save), UIButtonStyle.Primary);
            _panel.SetActive(false);
        }

        private Button BuildBottomLeftCustomizeButton(Transform parent, UnityEngine.Events.UnityAction action)
        {
            Vector2 size = new Vector2(248, 62);
            var go = Box(parent, "CustomizeOutfitButton", size, Vector2.zero, Color.white);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 0);
            rect.pivot = new Vector2(0, 0);
            rect.anchoredPosition = new Vector2(24, 24);

            var button = go.AddComponent<Button>();
            var bgImage = go.GetComponent<Image>();
            button.targetGraphic = bgImage;
            button.onClick.AddListener(action);
            go.AddComponent<UIButtonHover>();

            Sprite pinkSprite = UITheme.ButtonCustomizePinkSprite != null
                ? UITheme.ButtonCustomizePinkSprite
                : UITheme.ButtonPrimarySprite;
            UITheme.StyleShape(bgImage, pinkSprite, Color.white);

            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = UITheme.Hover(Color.white);
            colors.selectedColor = Color.white;
            colors.pressedColor = UITheme.Pressed(Color.white);
            colors.disabledColor = UITheme.Disabled(Color.white);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var iconGo = new GameObject("HangerIcon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.sizeDelta = new Vector2(40, 34);
            iconRect.anchoredPosition = new Vector2(-74, 2);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.sprite = GetHangerSprite();
            iconImg.preserveAspect = true;
            iconImg.color = Color.white;
            iconImg.raycastTarget = false;

            var label = Label(go.transform, "เปลี่ยนชุด [C]", 22, new Vector2(168, 44), new Vector2(22, 2));
            label.font = UITheme.FontMedium;
            label.color = Color.white;
            UITheme.AddOutline(label, 1f);
            ThaiTextMeshProBridge.Attach(label);

            return button;
        }

        private static Sprite GetHangerSprite()
        {
            if (_hangerSprite != null) return _hangerSprite;
            const int w = 64, h = 56;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            var clear = new Color32[w * h];
            tex.SetPixels32(clear);

            void PlotDisk(float cx, float cy, float r)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r - 1));
                int x1 = Mathf.Min(w - 1, Mathf.CeilToInt(cx + r + 1));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r - 1));
                int y1 = Mathf.Min(h - 1, Mathf.CeilToInt(cy + r + 1));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(r + 0.5f - d);
                    if (a <= 0f) continue;
                    Color c = tex.GetPixel(x, y);
                    if (a > c.a) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            void DrawLine(Vector2 a, Vector2 b, float radius)
            {
                int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 2.5f);
                for (int i = 0; i <= steps; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)Mathf.Max(1, steps));
                    PlotDisk(p.x, p.y, radius);
                }
            }

            // Hanger hook arc at top center
            Vector2 hookCenter = new Vector2(32f, 43f);
            float hookR = 6.5f;
            for (int deg = -45; deg <= 215; deg += 4)
            {
                float rad = deg * Mathf.Deg2Rad;
                PlotDisk(hookCenter.x + Mathf.Cos(rad) * hookR, hookCenter.y + Mathf.Sin(rad) * hookR, 2.2f);
            }
            // Neck down to shoulder junction
            DrawLine(new Vector2(32f, 36.5f), new Vector2(32f, 31f), 2.2f);
            // Left and right shoulders
            DrawLine(new Vector2(32f, 31f), new Vector2(8f, 14f), 2.3f);
            DrawLine(new Vector2(32f, 31f), new Vector2(56f, 14f), 2.3f);
            // Rounded corners & bottom bar
            DrawLine(new Vector2(8f, 14f), new Vector2(10f, 10f), 2.3f);
            DrawLine(new Vector2(56f, 14f), new Vector2(54f, 10f), 2.3f);
            DrawLine(new Vector2(10f, 10f), new Vector2(54f, 10f), 2.3f);

            tex.Apply();
            _hangerSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            return _hangerSprite;
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
            label.text = text; label.color = UITheme.TextPrimary; label.verticalOverflow = VerticalWrapMode.Overflow; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            ThaiTextMeshProBridge.Attach(label);
            return label;
        }

        private Button Button(Transform parent, string text, Vector2 size, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            var go = Box(parent, text, size, position, Color.white);
            var button = go.AddComponent<Button>(); button.targetGraphic = go.GetComponent<Image>();
            button.onClick.AddListener(action);
            Label(go.transform, text, 18, size, Vector2.zero);
            UITheme.StyleButton(button, UIButtonStyle.Secondary);
            return button;
        }
    }
}
