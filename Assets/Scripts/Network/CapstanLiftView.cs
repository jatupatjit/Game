using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Picture-in-picture view of a capstan lift for the player turning it. While the local player stands on (or
    /// next to) the walk ring, a small screen in the left of the HUD (below the player list, which grows to 4 rows) shows the lift from a fixed wide camera, with
    /// a progress bar, so the walkers can see where the platform is. Purely local: each peer checks its own player,
    /// nothing is synced. The camera renders only while the view is on screen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CapstanLiftView : MonoBehaviour
    {
        [SerializeField] private CoopCapstanLift _lift;
        [Tooltip("Camera placed at the viewing angle; it renders into a texture only while the view is shown.")]
        [SerializeField] private Camera _camera;
        [Tooltip("Metres beyond the walk ring that still count as being at the capstan")]
        [SerializeField, Min(0f)] private float _padding = 1.5f;
        [Tooltip("Seconds the view stays after the player steps off the ring")]
        [SerializeField, Min(0f)] private float _hideDelay = 1f;
        [SerializeField, Min(.5f)] private float _fadeSpeed = 5f;
        [SerializeField] private Vector2Int _resolution = new(640, 360);
        [Tooltip("Width of the view on a 1920x1080 reference screen")]
        [SerializeField, Min(160f)] private float _width = 440f;
        [SerializeField] private Vector2 _offset = new(24f, -300f);
        [SerializeField] private string _title = "แท่นยก";

        private RenderTexture _texture;
        private CanvasGroup _group;
        private Image _fill;
        private Text _percent;
        private float _lastNear = float.NegativeInfinity;
        private int _shownPercent = -1;

        /// <summary>True while the view is on screen (tests and screenshots).</summary>
        public bool Showing => _group != null && _group.gameObject.activeSelf;

        private void Awake()
        {
            if (_lift == null) _lift = GetComponentInParent<CoopCapstanLift>();
            if (_camera != null)
            {
                _camera.enabled = false;
                if (_camera.TryGetComponent<AudioListener>(out var listener)) listener.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_group != null) Destroy(_group.gameObject);
            if (_texture != null) { _texture.Release(); Destroy(_texture); }
        }

        private void LateUpdate()
        {
            if (_lift == null || _camera == null) return;
            if (LocalPlayerNearRing()) _lastNear = Time.unscaledTime;
            bool show = Time.unscaledTime - _lastNear <= _hideDelay;

            if (show && _group == null) Build();
            if (_group == null) return;
            _group.alpha = Mathf.MoveTowards(_group.alpha, show ? 1f : 0f, _fadeSpeed * Time.unscaledDeltaTime);
            bool visible = show || _group.alpha > 0f;
            if (_group.gameObject.activeSelf != visible) _group.gameObject.SetActive(visible);
            if (_camera.enabled != visible) _camera.enabled = visible;
            if (!visible) return;

            float progress = Mathf.Clamp01(_lift.Progress);
            UITheme.SetBarFill(_fill, progress);
            int percent = Mathf.RoundToInt(progress * 100f);
            if (percent == _shownPercent) return;
            _shownPercent = percent;
            _percent.text = percent >= 100 ? "ถึงข้างบนแล้ว" : percent + "%";
            _percent.color = percent >= 100 ? UITheme.Positive : UITheme.TextPrimary;
        }

        private bool LocalPlayerNearRing()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || manager.LocalClient == null) return false;
            var player = manager.LocalClient.PlayerObject;
            if (player == null || !player.IsSpawned || !player.TryGetComponent<CharacterController>(out var controller))
                return false;
            Vector3 feet = player.transform.TransformPoint(controller.center - Vector3.up * controller.height * .5f);
            return _lift.IsNearRing(feet, _padding);
        }

        private void Build()
        {
            _texture = new RenderTexture(_resolution.x, _resolution.y, 24) { name = "CapstanLiftView", antiAliasing = 2 };
            _camera.targetTexture = _texture;

            var root = new GameObject("CapstanLiftViewHUD", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            _group = root.GetComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            const float pad = 10f, header = 36f, bar = 14f;
            float viewHeight = _width * _resolution.y / _resolution.x;
            Image panel = UITheme.MakePanel(root.transform, "Panel", new Vector2(_width + pad * 2f, viewHeight + header + bar + pad * 3f), true);
            var rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = _offset;

            Text title = UITheme.MakeText(panel.transform, "Title", _title, 22, UITextRole.Primary);
            Place(title.rectTransform, new Vector2(pad + 4f, -pad), new Vector2(_width * .6f, header));
            UITheme.SetWeight(title, UITheme.FontSemiBold);
            _percent = UITheme.MakeText(panel.transform, "Percent", "0%", 22, UITextRole.Primary, TextAnchor.MiddleRight);
            Place(_percent.rectTransform, new Vector2(pad + _width * .4f - 4f, -pad), new Vector2(_width * .6f, header));
            UITheme.SetWeight(_percent, UITheme.FontSemiBold);

            var viewGo = new GameObject("View", typeof(RectTransform), typeof(RawImage));
            viewGo.transform.SetParent(panel.transform, false);
            var view = viewGo.GetComponent<RawImage>();
            view.texture = _texture;
            view.raycastTarget = false;
            Place(view.rectTransform, new Vector2(pad, -pad - header), new Vector2(_width, viewHeight));

            Image track = UITheme.MakeBar(panel.transform, "Progress", new Vector2(_width, bar), UITheme.Stamina, out _fill, 2f);
            Place(track.rectTransform, new Vector2(pad, -pad * 2f - header - viewHeight), new Vector2(_width, bar));
            root.SetActive(false);
        }

        private static void Place(RectTransform rect, Vector2 topLeft, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
        }
    }
}
