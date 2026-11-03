using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>Rebindable keyboard/mouse controls (the gameplay actions in <see cref="Player.PlayerInputReader"/>).</summary>
    public enum BindRole { Forward, Back, Left, Right, Jump, Sprint, Interact, Throw, GrabLeft, GrabRight }

    /// <summary>
    /// This PC's settings (PlayerPrefs, never sent over the network), applied when the game starts.
    /// Sound: master = AudioListener, SFX scales every game sound, music is kept for when the game has music.
    /// </summary>
    public static class GameSettings
    {
        private const string KMaster = "MasterVolume", KSfx = "SfxVolume", KMusic = "MusicVolume";
        private const string KSensitivity = "MouseSensitivity", KInvertY = "InvertY", KFov = "FieldOfView";
        private const string KQuality = "QualityPreset", KWidth = "ScreenWidth", KHeight = "ScreenHeight", KMode = "ScreenMode";
        private const string KVSync = "VSync", KFrameCap = "FrameCap", KBind = "Bind_";

        public const float DefaultFov = 60f;
        public static readonly int[] FrameCaps = { 0, 30, 60, 120, 144 };
        public static readonly string[] QualityNames = { "ต่ำ", "กลาง", "สูง" };

        private static readonly string[] DefaultBindings =
        {
            "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d", "<Keyboard>/space",
            "<Keyboard>/leftShift", "<Keyboard>/e", "<Keyboard>/q", "<Mouse>/leftButton", "<Mouse>/rightButton",
        };

        /// <summary>Raised after any setting changes (input readers re-apply their bindings).</summary>
        public static event Action Changed;

        public static float MasterVolume { get => PlayerPrefs.GetFloat(KMaster, 1f); set { SetFloat(KMaster, value); AudioListener.volume = value; } }
        public static float SfxVolume { get => PlayerPrefs.GetFloat(KSfx, 1f); set => SetFloat(KSfx, value); }
        public static float MusicVolume { get => PlayerPrefs.GetFloat(KMusic, 1f); set => SetFloat(KMusic, value); }
        /// <summary>Multiplies the camera's own mouse sensitivity.</summary>
        public static float MouseSensitivity { get => PlayerPrefs.GetFloat(KSensitivity, 1f); set => SetFloat(KSensitivity, value); }
        public static bool InvertY { get => PlayerPrefs.GetInt(KInvertY, 0) == 1; set => SetInt(KInvertY, value ? 1 : 0); }
        public static float FieldOfView { get => PlayerPrefs.GetFloat(KFov, DefaultFov); set => SetFloat(KFov, value); }
        public static int Quality { get => Mathf.Clamp(PlayerPrefs.GetInt(KQuality, 2), 0, 2); set { SetInt(KQuality, value); ApplyQuality(); } }
        public static bool VSync { get => PlayerPrefs.GetInt(KVSync, 0) == 1; set { SetInt(KVSync, value ? 1 : 0); ApplyFrameRate(); } }
        /// <summary>Frames per second limit, 0 = no limit.</summary>
        public static int FrameCap { get => PlayerPrefs.GetInt(KFrameCap, 0); set { SetInt(KFrameCap, value); ApplyFrameRate(); } }

        public static FullScreenMode ScreenMode => (FullScreenMode)PlayerPrefs.GetInt(KMode, (int)Screen.fullScreenMode);
        public static Vector2Int ScreenSize => new Vector2Int(PlayerPrefs.GetInt(KWidth, Screen.width), PlayerPrefs.GetInt(KHeight, Screen.height));

        public static void SetScreen(int width, int height, FullScreenMode mode)
        {
            PlayerPrefs.SetInt(KWidth, width);
            PlayerPrefs.SetInt(KHeight, height);
            PlayerPrefs.SetInt(KMode, (int)mode);
            PlayerPrefs.Save();
            Screen.SetResolution(width, height, mode);
            Changed?.Invoke();
        }

        public static string Binding(BindRole role) => PlayerPrefs.GetString(KBind + role, DefaultBindings[(int)role]);
        public static string DefaultBinding(BindRole role) => DefaultBindings[(int)role];

        /// <summary>Binds <paramref name="role"/> to <paramref name="path"/>; a role already on that key takes the old key.</summary>
        public static void SetBinding(BindRole role, string path)
        {
            string old = Binding(role);
            foreach (BindRole other in Enum.GetValues(typeof(BindRole)))
                if (other != role && Binding(other) == path) PlayerPrefs.SetString(KBind + other, old);
            PlayerPrefs.SetString(KBind + role, path);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Points the keyboard/mouse bindings of <paramref name="action"/> at this PC's keys. Matches the
        /// binding's own (default) path, so it can be called again after every change.</summary>
        public static void ApplyBindings(InputAction action)
        {
            if (action == null) return;
            for (int i = 0; i < action.bindings.Count; i++)
            {
                string path = action.bindings[i].path;
                int role = Array.IndexOf(DefaultBindings, path);
                if (role < 0) continue;
                string bound = Binding((BindRole)role);
                if (bound == path) action.RemoveBindingOverride(i);
                else action.ApplyBindingOverride(i, bound);
            }
        }

        /// <summary>Short Thai/English label for a binding path ("W", "Space", "คลิกซ้าย").</summary>
        public static string KeyLabel(string path)
        {
            switch (path)
            {
                case "<Mouse>/leftButton": return "คลิกซ้าย";
                case "<Mouse>/rightButton": return "คลิกขวา";
                case "<Mouse>/middleButton": return "คลิกกลาง";
                case "<Keyboard>/space": return "Spacebar";
            }
            return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
        }

        public static void ResetAudio() { MasterVolume = 1f; SfxVolume = 1f; MusicVolume = 1f; }
        public static void ResetCamera() { MouseSensitivity = 1f; InvertY = false; FieldOfView = DefaultFov; }

        public static void ResetGraphics()
        {
            PlayerPrefs.DeleteKey(KWidth); PlayerPrefs.DeleteKey(KHeight); PlayerPrefs.DeleteKey(KMode);
            Quality = 2; VSync = false; FrameCap = 0;
        }

        public static void ResetBindings()
        {
            foreach (BindRole role in Enum.GetValues(typeof(BindRole))) PlayerPrefs.DeleteKey(KBind + role);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        private static void SetFloat(string key, float value) { PlayerPrefs.SetFloat(key, value); PlayerPrefs.Save(); Changed?.Invoke(); }
        private static void SetInt(string key, int value) { PlayerPrefs.SetInt(key, value); PlayerPrefs.Save(); Changed?.Invoke(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplyAtStartup()
        {
            AudioListener.volume = MasterVolume;
            ApplyQuality();
            ApplyFrameRate();
            // Test windows are sized on the command line; a saved resolution must not resize them.
            bool sizedByCommandLine = Array.Exists(Environment.GetCommandLineArgs(), a => a == "-screen-width" || a == "--expedition-probe");
            if (!Application.isEditor && !sizedByCommandLine && PlayerPrefs.HasKey(KWidth))
                Screen.SetResolution(ScreenSize.x, ScreenSize.y, ScreenMode);
            var runtime = new GameObject("GameSettings");
            UnityEngine.Object.DontDestroyOnLoad(runtime);
            runtime.hideFlags = HideFlags.HideInHierarchy;
            runtime.AddComponent<GameSettingsRuntime>();
        }

        private static void ApplyFrameRate()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FrameCap > 0 ? FrameCap : -1;
        }

        // Quality: "high" is the project's own pipeline asset; lower presets use a runtime copy with a smaller render
        // scale, shorter shadows and no MSAA. The asset itself is never changed, and the original is put back on quit
        // (in the Editor, play mode changes to QualitySettings would otherwise stay).
        private static RenderPipelineAsset _originalPipeline;
        private static bool _pipelineSaved;
        private static UniversalRenderPipelineAsset _lowered;

        private static void ApplyQuality()
        {
            if (!_pipelineSaved)
            {
                _originalPipeline = QualitySettings.renderPipeline;
                _pipelineSaved = true;
                Application.quitting += () => { if (_lowered != null) QualitySettings.renderPipeline = _originalPipeline; };
            }
            var source = (_originalPipeline != null ? _originalPipeline : GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            if (source == null) return;
            int preset = Quality;
            if (preset >= 2)
            {
                if (_lowered != null) QualitySettings.renderPipeline = _originalPipeline;
                return;
            }
            if (_lowered == null) { _lowered = UnityEngine.Object.Instantiate(source); _lowered.name = source.name + " (settings)"; }
            _lowered.renderScale = Mathf.Min(source.renderScale, preset == 0 ? .7f : .85f);
            _lowered.shadowDistance = source.shadowDistance * (preset == 0 ? .35f : .6f);
            _lowered.msaaSampleCount = preset == 0 ? 1 : Mathf.Min(source.msaaSampleCount, 2);
            QualitySettings.renderPipeline = _lowered;
        }
    }

    /// <summary>Keeps the player camera's field of view on the saved value.</summary>
    internal sealed class GameSettingsRuntime : MonoBehaviour
    {
        private void LateUpdate()
        {
            if (Player.PlayerCameraController.LocalInstance == null) return;
            Camera camera = Camera.main;
            if (camera != null) camera.fieldOfView = GameSettings.FieldOfView;
        }
    }

    /// <summary>
    /// Settings window (cute kit card, Thai labels), opened from the main menu, the title screen and the pause menu.
    /// Tabs: mouse and camera, graphics, sound, keys. Every change applies and saves at once.
    /// </summary>
    public sealed class SettingsMenu : MonoBehaviour
    {
        public static bool IsOpen => _instance != null && _instance._root.activeSelf;
        /// <summary>Frame the window was closed (Esc handlers elsewhere ignore that frame's key).</summary>
        public static int ClosedFrame { get; private set; } = -1;

        private static SettingsMenu _instance;
        private GameObject _root;
        private RectTransform _content;
        private readonly List<Button> _tabs = new List<Button>();
        private int _tab;
        private Action _onClosed;
        private Text _waitingLabel;
        private bool _rebinding;
        private int _rebindEndedFrame = -1;

        private static readonly string[] TabNames = { "เมาส์และกล้อง", "กราฟิก", "เสียง", "ปุ่มกด" };
        private static readonly string[] RoleNames = { "เดินหน้า", "ถอยหลัง", "เดินซ้าย", "เดินขวา", "กระโดด", "วิ่ง", "ใช้ / กด", "โยน", "ยกลัง / มือซ้าย", "วางลัง / มือขวา" };

        public static void Open(Action onClosed = null)
        {
            if (_instance == null)
            {
                var go = new GameObject("SettingsMenu");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<SettingsMenu>();
                _instance.Build();
            }
            _instance._onClosed = onClosed;
            _instance._root.SetActive(true);
            _instance.ShowTab(_instance._tab);
            EnsureEventSystem();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public static void Close()
        {
            if (!IsOpen) return;
            _instance._root.SetActive(false);
            ClosedFrame = Time.frameCount;
            Action closed = _instance._onClosed;
            _instance._onClosed = null;
            closed?.Invoke();
        }

        /// <summary>Test probe: open on a tab.</summary>
        public static void ShowTabForProbe(int tab) { Open(); _instance.ShowTab(tab); }

        private void Update()
        {
            if (!IsOpen) return;
            if (Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            var keyboard = Keyboard.current;
            if (!_rebinding && _rebindEndedFrame != Time.frameCount && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
        }

        // ───────────────────────── Layout ─────────────────────────

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000; // over the pause menu, the title screen and the room-code HUD
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root", typeof(RectTransform), typeof(Image));
            _root.transform.SetParent(transform, false);
            ProceduralUIUtility.StretchFull((RectTransform)_root.transform);
            _root.GetComponent<Image>().color = UITheme.Overlay;

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(_root.transform, false);
            var cardRect = (RectTransform)card.transform;
            cardRect.sizeDelta = new Vector2(1180f, 800f);
            cardRect.anchoredPosition = new Vector2(0f, -20f);
            UITheme.StyleCuteCard(card.GetComponent<Image>());
            UITheme.MakeCutePlank(card.transform, "ตั้งค่า", 360f, 110f, 0f, 46);
            UITheme.AddCuteTape(card.transform, 3, new Vector2(0f, 1f), new Vector2(40f, -40f), 30f, 96f);
            UITheme.AddCuteTape(card.transform, 6, new Vector2(1f, 1f), new Vector2(-40f, -40f), -30f, 96f);

            var tabs = Row(card.transform, "Tabs", new Vector2(1060f, 74f), new Vector2(0f, -120f), 14f);
            for (int i = 0; i < TabNames.Length; i++)
            {
                int tab = i;
                Button button = UITheme.MakeButton(tabs, "Tab" + i, TabNames[i], UIButtonStyle.White, new Vector2(250f, 70f), 26);
                button.onClick.AddListener(() => ShowTab(tab));
                _tabs.Add(button);
            }

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(card.transform, false);
            _content = (RectTransform)content.transform;
            _content.anchorMin = _content.anchorMax = new Vector2(.5f, 1f);
            _content.pivot = new Vector2(.5f, 1f);
            _content.sizeDelta = new Vector2(1040f, 470f);
            _content.anchoredPosition = new Vector2(0f, -178f);

            var bottom = Row(card.transform, "Bottom", new Vector2(1040f, 80f), Vector2.zero, 30f);
            var bottomRect = (RectTransform)bottom;
            bottomRect.anchorMin = bottomRect.anchorMax = new Vector2(.5f, 0f);
            bottomRect.anchoredPosition = new Vector2(0f, 78f);
            Button reset = UITheme.MakeButton(bottom, "Reset", "คืนค่าเริ่มต้น", UIButtonStyle.White, new Vector2(300f, 76f), 26);
            reset.onClick.AddListener(ResetTab);
            Button done = UITheme.MakeButton(bottom, "Done", "เสร็จ", UIButtonStyle.Pink, new Vector2(300f, 80f), 30);
            done.onClick.AddListener(Close);
            _root.SetActive(false);
        }

        private void ShowTab(int tab)
        {
            _tab = tab;
            _rebinding = false;
            for (int i = 0; i < _tabs.Count; i++)
            {
                UITheme.StyleButton(_tabs[i], i == tab ? UIButtonStyle.Pink : UIButtonStyle.White);
                foreach (Text label in _tabs[i].GetComponentsInChildren<Text>(true)) label.fontSize = 26;
            }
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            switch (tab)
            {
                case 0: BuildCamera(); break;
                case 1: BuildGraphics(); break;
                case 2: BuildAudio(); break;
                default: BuildKeys(); break;
            }
        }

        private void ResetTab()
        {
            switch (_tab)
            {
                case 0: GameSettings.ResetCamera(); break;
                case 1: GameSettings.ResetGraphics(); break;
                case 2: GameSettings.ResetAudio(); break;
                default: GameSettings.ResetBindings(); break;
            }
            ShowTab(_tab);
        }

        private void BuildAudio()
        {
            Transform list = Column(_content, 0f, 1040f);
            SliderRow(list, "เสียงรวม", 0f, 1f, GameSettings.MasterVolume, v => GameSettings.MasterVolume = v, Percent);
            SliderRow(list, "เสียงเอฟเฟกต์", 0f, 1f, GameSettings.SfxVolume, v => GameSettings.SfxVolume = v, Percent);
            SliderRow(list, "เพลง", 0f, 1f, GameSettings.MusicVolume, v => GameSettings.MusicVolume = v, Percent);
            Note(list, "ตอนนี้เกมยังไม่มีเพลงประกอบ ค่านี้จะใช้เมื่อใส่เพลงแล้ว");
        }

        private void BuildCamera()
        {
            Transform list = Column(_content, 0f, 1040f);
            SliderRow(list, "ความไวเมาส์", .2f, 3f, GameSettings.MouseSensitivity, v => GameSettings.MouseSensitivity = v, v => v.ToString("0.0") + "x");
            ChoiceRow(list, "สลับแกนเมาส์ขึ้นลง", new[] { "ปิด", "เปิด" }, GameSettings.InvertY ? 1 : 0, i => GameSettings.InvertY = i == 1);
            SliderRow(list, "มุมกล้อง (FOV)", 50f, 100f, GameSettings.FieldOfView, v => GameSettings.FieldOfView = Mathf.Round(v), v => Mathf.Round(v) + "°");
        }

        private void BuildGraphics()
        {
            Transform list = Column(_content, 0f, 1040f);
            ChoiceRow(list, "คุณภาพกราฟิก", GameSettings.QualityNames, GameSettings.Quality, i => GameSettings.Quality = i);

            var sizes = new List<Vector2Int>();
            foreach (Resolution r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (!sizes.Contains(size)) sizes.Add(size);
            }
            Vector2Int current = GameSettings.ScreenSize;
            if (!sizes.Contains(current)) sizes.Add(current);
            sizes.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var labels = sizes.ConvertAll(s => s.x + " x " + s.y).ToArray();
            ChoiceRow(list, "ความละเอียดจอ", labels, sizes.IndexOf(current),
                i => GameSettings.SetScreen(sizes[i].x, sizes[i].y, GameSettings.ScreenMode));

            var modes = new[] { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
            int mode = Mathf.Max(0, Array.IndexOf(modes, GameSettings.ScreenMode));
            ChoiceRow(list, "โหมดหน้าจอ", new[] { "เต็มจอ", "เต็มจอแบบหน้าต่าง", "หน้าต่าง" }, mode,
                i => GameSettings.SetScreen(GameSettings.ScreenSize.x, GameSettings.ScreenSize.y, modes[i]));

            ChoiceRow(list, "V-Sync", new[] { "ปิด", "เปิด" }, GameSettings.VSync ? 1 : 0, i => GameSettings.VSync = i == 1);
            var caps = Array.ConvertAll(GameSettings.FrameCaps, c => c == 0 ? "ไม่จำกัด" : c + " FPS");
            ChoiceRow(list, "จำกัดเฟรมเรต", caps, Mathf.Max(0, Array.IndexOf(GameSettings.FrameCaps, GameSettings.FrameCap)),
                i => GameSettings.FrameCap = GameSettings.FrameCaps[i]);
        }

        private void BuildKeys()
        {
            var roles = (BindRole[])Enum.GetValues(typeof(BindRole));
            Transform left = Column(_content, -262f, 510f), right = Column(_content, 262f, 510f);
            for (int i = 0; i < roles.Length; i++)
                KeyRow(i < 5 ? left : right, roles[i]);
            Note(_content, "กดช่องปุ่มแล้วกดปุ่มใหม่ (Esc = ยกเลิก)", -420f);
        }

        // ───────────────────────── Rows ─────────────────────────

        private static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";

        private static Transform Column(Transform parent, float x, float width)
        {
            var go = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.sizeDelta = new Vector2(width, 470f);
            rect.anchoredPosition = new Vector2(x, 0f);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return go.transform;
        }

        private static Transform Row(Transform parent, string name, Vector2 size, Vector2 position, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return go.transform;
        }

        private static Transform LineRow(Transform list, string label, float labelWidth = 360f)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(list, false);
            go.GetComponent<LayoutElement>().preferredHeight = 76f;
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            Text text = UITheme.MakeText(go.transform, "Label", label, 30, UITextRole.OnLight);
            UITheme.StyleCuteText(text);
            text.rectTransform.sizeDelta = new Vector2(labelWidth, 60f);
            return go.transform;
        }

        private static void Note(Transform parent, string message, float y = 0f)
        {
            Text text = UITheme.MakeText(parent, "Note", message, 22, UITextRole.OnLight, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(text, true);
            if (parent.GetComponent<VerticalLayoutGroup>() != null)
            {
                text.gameObject.AddComponent<LayoutElement>().preferredHeight = 50f;
                return;
            }
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.sizeDelta = new Vector2(1000f, 40f);
            rect.anchoredPosition = new Vector2(0f, y);
        }

        private static void SliderRow(Transform list, string label, float min, float max, float value, Action<float> set, Func<float, string> format)
        {
            Transform row = LineRow(list, label);
            var go = new GameObject("Slider", typeof(RectTransform));
            go.transform.SetParent(row, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(470f, 34f);

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(go.transform, false);
            var bg = (RectTransform)background.transform;
            bg.anchorMin = new Vector2(0f, .25f); bg.anchorMax = new Vector2(1f, .75f);
            bg.offsetMin = bg.offsetMax = Vector2.zero;
            var fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            var fa = (RectTransform)fillArea.transform;
            fa.anchorMin = new Vector2(0f, .25f); fa.anchorMax = new Vector2(1f, .75f);
            fa.offsetMin = new Vector2(6f, 0f); fa.offsetMax = new Vector2(-6f, 0f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            var handleArea = new GameObject("HandleArea", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            var ha = (RectTransform)handleArea.transform;
            ha.anchorMin = Vector2.zero; ha.anchorMax = Vector2.one;
            ha.offsetMin = new Vector2(14f, 0f); ha.offsetMax = new Vector2(-14f, 0f);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = (RectTransform)handle.transform;
            handleRect.sizeDelta = new Vector2(30f, 0f);

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            UITheme.StyleSlider(slider);
            slider.handleRect.GetComponent<Image>().color = UITheme.CuteInk;

            Text readout = UITheme.MakeText(row, "Value", format(value), 28, UITextRole.OnLight, TextAnchor.MiddleRight);
            UITheme.StyleCuteText(readout);
            readout.rectTransform.sizeDelta = new Vector2(120f, 60f);
            slider.onValueChanged.AddListener(v => { set(v); readout.text = format(v); });
        }

        private static void ChoiceRow(Transform list, string label, string[] options, int index, Action<int> set)
        {
            Transform row = LineRow(list, label);
            int current = Mathf.Clamp(index, 0, options.Length - 1);
            Button previous = UITheme.MakeButton(row, "Previous", "<", UIButtonStyle.White, new Vector2(70f, 62f), 30);
            var box = new GameObject("Value", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(row, false);
            ((RectTransform)box.transform).sizeDelta = new Vector2(330f, 58f);
            UITheme.StyleShape(box.GetComponent<Image>(), UITheme.SmallSprite, new Color(1f, 1f, 1f, .55f));
            Text value = UITheme.MakeText(box.transform, "Label", options[current], 28, UITextRole.OnLight, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(value);
            ProceduralUIUtility.StretchFull(value.rectTransform);
            Button next = UITheme.MakeButton(row, "Next", ">", UIButtonStyle.White, new Vector2(70f, 62f), 30);
            void Step(int delta)
            {
                current = (current + delta + options.Length) % options.Length;
                value.text = options[current];
                set(current);
            }
            previous.onClick.AddListener(() => Step(-1));
            next.onClick.AddListener(() => Step(1));
        }

        private void KeyRow(Transform list, BindRole role)
        {
            Transform row = LineRow(list, RoleNames[(int)role], 250f);
            Button key = UITheme.MakeButton(row, "Key", GameSettings.KeyLabel(GameSettings.Binding(role)), UIButtonStyle.White, new Vector2(230f, 62f), 26);
            Text label = key.GetComponentInChildren<Text>();
            key.onClick.AddListener(() => StartRebind(role, label));
        }

        private void StartRebind(BindRole role, Text label)
        {
            if (_rebinding) return;
            _rebinding = true;
            _waitingLabel = label;
            label.text = "กดปุ่ม...";
            InputSystem.onAnyButtonPress.CallOnce(control =>
            {
                if (!_rebinding || _waitingLabel != label) return;
                _rebinding = false;
                _rebindEndedFrame = Time.frameCount;
                bool escape = control is UnityEngine.InputSystem.Controls.KeyControl k && k.keyCode == Key.Escape;
                string name = control.device is Mouse && control.name == "press" ? "leftButton" : control.name;
                if (!escape && (control.device is Keyboard || control.device is Mouse))
                    GameSettings.SetBinding(role, "<" + (control.device is Keyboard ? "Keyboard" : "Mouse") + ">/" + name);
                ShowTab(_tab); // swapped keys show on both rows
            });
        }

        private static void EnsureEventSystem()
        {
            var current = UnityEngine.EventSystems.EventSystem.current;
            if (current == null)
            {
                new GameObject("Settings EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
                return;
            }
            if (current.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null &&
                current.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>() == null)
                current.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
    }
}
