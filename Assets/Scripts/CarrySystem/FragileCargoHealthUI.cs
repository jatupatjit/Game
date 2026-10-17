using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.CarrySystem
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FragileCargo))]
    public sealed class FragileCargoHealthUI : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _heightAboveItem = 0.35f;
        [SerializeField, Min(0.1f)] private float _damageTextDuration = 1.25f;
        [SerializeField, Min(0f)] private float _damageRise = 35f;

        private FragileCargo _cargo;
        private Canvas _canvas;
        private Transform _billboard;
        private MeshRenderer[] _renderers;
        private Collider _collider;
        private Image _fill;
        private Text _label;
        private Text _damageText;
        private RectTransform _damageRect;
        private Camera _camera;
        private int _lastHP;
        private float _damageTimeLeft;

        private void Awake()
        {
            _cargo = GetComponent<FragileCargo>();
            _renderers = GetComponentsInChildren<MeshRenderer>(true);
            _collider = GetComponent<Collider>();
            CreateUI();
        }

        private void OnEnable()
        {
            if (_cargo != null)
            {
                _cargo.OnHPChanged += Refresh;
                InitializeHealth(_cargo.CurrentHP.Value, _cargo.MaxHP);
            }
        }

        internal void InitializeHealth(int hp, int maxHP)
        {
            // Spawn synchronization is an initial snapshot, not a damage event.
            _lastHP = hp;
            _damageTimeLeft = 0f;
            if (_damageText != null) _damageText.enabled = false;
            Refresh(hp, maxHP);
        }

        private void OnDisable()
        {
            if (_cargo != null) _cargo.OnHPChanged -= Refresh;
        }

        public void Hide()
        {
            if (_canvas != null) _canvas.enabled = false;
            enabled = false;
        }

        private void LateUpdate()
        {
            if (_billboard == null) return;

            float top = transform.position.y;
            bool foundRenderer = false;
            for (int i = 0; i < _renderers.Length; i++)
            {
                MeshRenderer renderer = _renderers[i];
                if (renderer == null || !renderer.enabled) continue;
                top = Mathf.Max(top, renderer.bounds.max.y);
                foundRenderer = true;
            }
            if (!foundRenderer && _collider != null)
                top = Mathf.Max(top, _collider.bounds.max.y);

            Vector3 itemPosition = transform.position;
            _billboard.position = new Vector3(itemPosition.x, top + _heightAboveItem, itemPosition.z);

            if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
            if (_camera != null)
                _billboard.rotation = _camera.transform.rotation;

            if (_damageTimeLeft > 0f)
            {
                _damageTimeLeft = Mathf.Max(0f, _damageTimeLeft - Time.deltaTime);
                float progress = 1f - _damageTimeLeft / _damageTextDuration;
                _damageRect.anchoredPosition = new Vector2(0f, 36f + progress * _damageRise);
                Color color = _damageText.color;
                color.a = Mathf.Clamp01(_damageTimeLeft / (_damageTextDuration * 0.5f));
                _damageText.color = color;
                if (_damageTimeLeft == 0f) _damageText.enabled = false;
            }
        }

        private void Refresh(int hp, int maxHP)
        {
            if (_fill == null || _label == null) return;
            if (hp < _lastHP)
            {
                _damageText.text = $"-{_lastHP - hp}";
                _damageText.color = UITheme.Danger;
                _damageText.enabled = true;
                _damageRect.anchoredPosition = new Vector2(0f, 36f);
                _damageTimeLeft = _damageTextDuration;
            }
            _lastHP = hp;
            float ratio = maxHP > 0 ? Mathf.Clamp01((float)hp / maxHP) : 0f;
            UITheme.SetBarFill(_fill, ratio);
            _fill.color = UITheme.Meter(ratio, UITheme.Primary);
            _label.text = $"ITEM  {hp}/{maxHP}";
        }

        private void CreateUI()
        {
            GameObject root = new GameObject("Item HP", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(transform, false);
            _billboard = root.transform;
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _canvas = canvas;
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(180f, 38f);
            rect.localScale = Vector3.one * 0.005f;

            GameObject back = new GameObject("Background", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(root.transform, false);
            RectTransform backRect = back.GetComponent<RectTransform>();
            backRect.anchorMin = Vector2.zero;
            backRect.anchorMax = Vector2.one;
            backRect.offsetMin = Vector2.zero;
            backRect.offsetMax = Vector2.zero;
            UITheme.StylePanel(back.GetComponent<Image>(), small: true);
            back.GetComponent<Image>().raycastTarget = false;

            // Chamfered track in the same rect the old fill used; the sliced fill shrinks by anchor.
            GameObject trackObject = new GameObject("Track", typeof(RectTransform), typeof(Image));
            trackObject.transform.SetParent(back.transform, false);
            RectTransform trackRect = trackObject.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(0.05f, 0.1f);
            trackRect.anchorMax = new Vector2(0.95f, 0.43f);
            trackRect.offsetMin = Vector2.zero;
            trackRect.offsetMax = Vector2.zero;
            UITheme.StyleTrack(trackObject.GetComponent<Image>());
            trackObject.GetComponent<Image>().raycastTarget = false;

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(trackObject.transform, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);
            _fill = fillObject.GetComponent<Image>();
            UITheme.StyleFill(_fill, UITheme.Primary);
            _fill.raycastTarget = false;

            GameObject textObject = new GameObject("HP Label", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(back.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0.45f);
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            _label = textObject.GetComponent<Text>();
            UITheme.StyleText(_label, UITextRole.Primary);
            _label.fontSize = 18;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.verticalOverflow = VerticalWrapMode.Overflow; // Sarabun's taller line must not truncate
            _label.raycastTarget = false;

            GameObject damageObject = new GameObject("Damage Amount", typeof(RectTransform), typeof(Text));
            damageObject.transform.SetParent(root.transform, false);
            _damageRect = damageObject.GetComponent<RectTransform>();
            _damageRect.anchorMin = new Vector2(0.5f, 0.5f);
            _damageRect.anchorMax = new Vector2(0.5f, 0.5f);
            _damageRect.sizeDelta = new Vector2(160f, 48f);
            _damageRect.anchoredPosition = new Vector2(0f, 36f);
            _damageText = damageObject.GetComponent<Text>();
            _damageText.font = _label.font;
            _damageText.fontSize = 32;
            _damageText.fontStyle = FontStyle.Bold;
            _damageText.alignment = TextAnchor.MiddleCenter;
            _damageText.verticalOverflow = VerticalWrapMode.Overflow;
            _damageText.raycastTarget = false;
            _damageText.enabled = false;
            Outline outline = damageObject.AddComponent<Outline>();
            outline.effectColor = UITheme.Outline;
            outline.effectDistance = new Vector2(2f, -2f);
        }
    }
}
