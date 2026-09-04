using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using CoopGame.CarrySystem;

namespace CoopGame.Player
{
    /// <summary>
    /// PlayerStaminaUI generates and manages a modern, responsive HUD overlay
    /// displaying the player's physical Stamina bar and Throwing charge meter.
    /// 
    /// Enhancements:
    /// 1. Procedural White Sprite Generation:
    ///    Guarantees that Image.Type.Filled and RectTransform clipping work 100% without missing sprite references.
    /// 2. Smooth Continuous Drain & Lerp (ลดลงเรื่อยๆ อย่างนุ่มนวล):
    ///    The fill bar smoothly glides down continuously, coupled with a delayed ghost-trail bar.
    /// 3. Rich Dynamic Colors (การไล่ระดับสีสวยงามตลอดการลด):
    ///    - High (>60%): Vibrant Lime Green
    ///    - Medium (30% - 60%): Warm Amber / Gold
    ///    - Low (10% - 30%): Bright Orange
    ///    - Critical (<10%): Deep Crimson Red
    ///    - Exhausted: Rapid pulsing red alert
    /// 4. Dual Clipping Engine:
    ///    Updates both fillAmount AND RectTransform.anchorMax so the bar physically shrinks visibly in all Unity versions.
    /// </summary>
    [RequireComponent(typeof(PlayerStamina))]
    [DisallowMultipleComponent]
    public class PlayerStaminaUI : MonoBehaviour
    {
        [Header("Colors & Visuals")]
        [SerializeField] private Color _colorHigh = new Color(0.15f, 0.92f, 0.38f, 0.95f);    // Lime Green
        [SerializeField] private Color _colorMedium = new Color(1.00f, 0.76f, 0.05f, 0.95f);  // Amber Gold
        [SerializeField] private Color _colorOrange = new Color(1.00f, 0.40f, 0.10f, 0.95f);  // Coral Orange
        [SerializeField] private Color _colorLow = new Color(1.00f, 0.15f, 0.15f, 0.95f);     // Crimson Red
        [SerializeField] private Color _colorGhost = new Color(1.0f, 1.0f, 1.0f, 0.35f);      // Soft Trail
        [SerializeField] private Color _colorThrowCharge = new Color(0.0f, 0.90f, 1.0f, 0.95f); // Neon Cyan
        [SerializeField] private Color _colorThrowFull = new Color(1.0f, 0.85f, 0.15f, 1.0f);   // Gold Flash

        [Header("Smooth Drain Animation Settings")]
        [Tooltip("Speed of the smooth stamina bar decreasing glide")]
        [SerializeField] private float _smoothDrainSpeed = 8.0f;

        [Tooltip("Speed of the ghost trail bar catching up")]
        [SerializeField] private float _ghostDrainSpeed = 1.8f;

        [Header("Fade Settings")]
        [Tooltip("Fade out speed when returning to full stamina")]
        [SerializeField] private float _fadeSpeed = 3.0f;

        [Tooltip("Delay in seconds after reaching full stamina before fading out")]
        [SerializeField] private float _fadeDelay = 1.2f;

        // Cached references
        private PlayerStamina _stamina;
        private PlayerCarry _playerCarry;

        // Runtime UI elements
        private Canvas _canvas;
        private CanvasGroup _hudCanvasGroup;
        private RectTransform _staminaFillRect;
        private Image _staminaFillImage;
        private RectTransform _staminaGhostRect;
        private Image _staminaGhostImage;
        private RectTransform _throwFillRect;
        private Image _throwFillImage;
        private GameObject _throwBarContainer;

        // Animation state
        private float _displayedStamina = 1.0f;
        private float _ghostStamina = 1.0f;
        private float _fullStaminaTimer = 0f;
        private float _targetAlpha = 1.0f;

        // Cached procedural white sprite
        private static Sprite _sharedWhiteSprite;

        private void Awake()
        {
            _stamina = GetComponent<PlayerStamina>();
            _playerCarry = GetComponent<PlayerCarry>();

            // Do not create HUD on remote proxies
            NetworkObject netObj = GetComponent<NetworkObject>();
            if (netObj != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !netObj.IsOwner)
            {
                enabled = false;
                return;
            }

            EnsureWhiteSprite();
            CreateProceduralHUD();
        }

        private void Start()
        {
            NetworkObject netObj = GetComponent<NetworkObject>();
            if (netObj != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !netObj.IsOwner)
            {
                if (_hudCanvasGroup != null && _hudCanvasGroup.gameObject != null)
                {
                    Destroy(_hudCanvasGroup.gameObject);
                }
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_hudCanvasGroup != null && _hudCanvasGroup.gameObject != null)
            {
                Destroy(_hudCanvasGroup.gameObject);
            }
        }

        private void EnsureWhiteSprite()
        {
            if (_sharedWhiteSprite == null)
            {
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    name = "T_WhiteSquare_Procedural"
                };
                tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
                tex.Apply();
                _sharedWhiteSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        private void Update()
        {
            if (_stamina == null) return;

            float targetNormStamina = _stamina.NormalizedStamina;
            bool isExhausted = _stamina.IsExhausted;
            bool isChargingThrow = (_playerCarry != null && _playerCarry.IsChargingThrow);
            float throwCharge = (_playerCarry != null) ? _playerCarry.CurrentThrowCharge : 0f;

            // 1. Smoothly glide displayed stamina down/up continuously
            _displayedStamina = Mathf.MoveTowards(_displayedStamina, targetNormStamina, Time.deltaTime * _smoothDrainSpeed);
            _displayedStamina = Mathf.Clamp01(_displayedStamina);

            // 2. Ghost trail bar lags behind to visually emphasize continuous stamina consumption
            if (_ghostStamina < _displayedStamina)
            {
                _ghostStamina = _displayedStamina;
            }
            else
            {
                _ghostStamina = Mathf.MoveTowards(_ghostStamina, _displayedStamina, Time.deltaTime * _ghostDrainSpeed);
            }

            // 3. Update Stamina Fill Width and Percentage
            if (_staminaFillImage != null)
            {
                _staminaFillImage.fillAmount = _displayedStamina;
                if (_staminaFillRect != null)
                {
                    _staminaFillRect.anchorMax = new Vector2(_displayedStamina, 1.0f);
                }

                // Dynamic Multi-tier Color Palette
                if (isExhausted)
                {
                    // Rapid red pulse during exhaustion
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 14.0f);
                    Color pulseColor = _colorLow;
                    pulseColor.a = pulse;
                    _staminaFillImage.color = pulseColor;
                }
                else
                {
                    _staminaFillImage.color = EvaluateStaminaColor(_displayedStamina);
                }
            }

            // 4. Update Ghost Trail Width
            if (_staminaGhostImage != null && _staminaGhostRect != null)
            {
                _staminaGhostImage.fillAmount = _ghostStamina;
                _staminaGhostRect.anchorMax = new Vector2(_ghostStamina, 1.0f);
            }

            // 5. Update Throw Charge Bar
            if (_throwBarContainer != null && _throwFillImage != null)
            {
                _throwBarContainer.SetActive(isChargingThrow);
                if (isChargingThrow)
                {
                    _throwFillImage.fillAmount = throwCharge;
                    if (_throwFillRect != null)
                    {
                        _throwFillRect.anchorMax = new Vector2(throwCharge, 1.0f);
                    }

                    _throwFillImage.color = (throwCharge >= 0.98f)
                        ? Color.Lerp(_colorThrowCharge, _colorThrowFull, Mathf.PingPong(Time.time * 8f, 1f))
                        : _colorThrowCharge;
                }
            }

            // 6. Smart Auto-Fade: Visible when active/straining, hidden when idle at 100%
            if (targetNormStamina < 0.999f || isExhausted || isChargingThrow || _ghostStamina < 0.999f)
            {
                _targetAlpha = 1.0f;
                _fullStaminaTimer = 0f;
            }
            else
            {
                _fullStaminaTimer += Time.deltaTime;
                if (_fullStaminaTimer >= _fadeDelay)
                {
                    _targetAlpha = 0.0f;
                }
            }

            if (_hudCanvasGroup != null)
            {
                _hudCanvasGroup.alpha = Mathf.MoveTowards(_hudCanvasGroup.alpha, _targetAlpha, Time.deltaTime * _fadeSpeed);
            }
        }

        /// <summary>
        /// Smoothly blends through 4 distinct color stages as stamina decreases continuously:
        /// 1.0 to 0.6: Lime Green -> Amber Gold
        /// 0.6 to 0.3: Amber Gold -> Coral Orange
        /// 0.3 to 0.0: Coral Orange -> Crimson Red
        /// </summary>
        private Color EvaluateStaminaColor(float normalizedStamina)
        {
            if (normalizedStamina > 0.60f)
            {
                float t = (normalizedStamina - 0.60f) / 0.40f;
                return Color.Lerp(_colorMedium, _colorHigh, t);
            }
            else if (normalizedStamina > 0.30f)
            {
                float t = (normalizedStamina - 0.30f) / 0.30f;
                return Color.Lerp(_colorOrange, _colorMedium, t);
            }
            else
            {
                float t = normalizedStamina / 0.30f;
                return Color.Lerp(_colorLow, _colorOrange, t);
            }
        }

        private void CreateProceduralHUD()
        {
            // 1. Locate or create Canvas
            _canvas = FindAnyObjectByType<Canvas>();
            if (_canvas == null)
            {
                GameObject canvasObj = new GameObject("HUD_Canvas");
                _canvas = canvasObj.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 50;

                CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;

                canvasObj.AddComponent<GraphicRaycaster>();
            }

            // 2. Main HUD Container anchored at Bottom Center
            GameObject hudRoot = new GameObject("PlayerStaminaHUD");
            hudRoot.transform.SetParent(_canvas.transform, false);

            RectTransform hudRect = hudRoot.AddComponent<RectTransform>();
            hudRect.anchorMin = new Vector2(0.5f, 0.0f);
            hudRect.anchorMax = new Vector2(0.5f, 0.0f);
            hudRect.pivot = new Vector2(0.5f, 0.0f);
            hudRect.anchoredPosition = new Vector2(0f, 45f);
            hudRect.sizeDelta = new Vector2(280f, 60f);

            _hudCanvasGroup = hudRoot.AddComponent<CanvasGroup>();
            _hudCanvasGroup.alpha = 0.0f; // Start hidden, fades in on exertion

            // 3. Stamina Bar Background
            GameObject stamBgObj = new GameObject("StaminaBar_BG");
            stamBgObj.transform.SetParent(hudRoot.transform, false);

            RectTransform stamBgRect = stamBgObj.AddComponent<RectTransform>();
            stamBgRect.anchorMin = new Vector2(0.5f, 0.0f);
            stamBgRect.anchorMax = new Vector2(0.5f, 0.0f);
            stamBgRect.pivot = new Vector2(0.5f, 0.0f);
            stamBgRect.anchoredPosition = new Vector2(0f, 0f);
            stamBgRect.sizeDelta = new Vector2(260f, 18f);

            Image stamBgImg = stamBgObj.AddComponent<Image>();
            stamBgImg.sprite = GetOrCreateWhiteSprite();
            stamBgImg.color = new Color(0.04f, 0.06f, 0.10f, 0.85f);

            // 4. Ghost Trail Image (Lags behind to show recent stamina loss)
            GameObject stamGhostObj = new GameObject("StaminaBar_Ghost");
            stamGhostObj.transform.SetParent(stamBgObj.transform, false);

            _staminaGhostRect = stamGhostObj.AddComponent<RectTransform>();
            _staminaGhostRect.anchorMin = Vector2.zero;
            _staminaGhostRect.anchorMax = Vector2.one;
            _staminaGhostRect.offsetMin = new Vector2(2f, 2f);
            _staminaGhostRect.offsetMax = new Vector2(-2f, -2f);

            _staminaGhostImage = stamGhostObj.AddComponent<Image>();
            _staminaGhostImage.sprite = GetOrCreateWhiteSprite();
            _staminaGhostImage.type = Image.Type.Filled;
            _staminaGhostImage.fillMethod = Image.FillMethod.Horizontal;
            _staminaGhostImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            _staminaGhostImage.color = _colorGhost;

            // 5. Stamina Fill Image (Main foreground bar)
            GameObject stamFillObj = new GameObject("StaminaBar_Fill");
            stamFillObj.transform.SetParent(stamBgObj.transform, false);

            _staminaFillRect = stamFillObj.AddComponent<RectTransform>();
            _staminaFillRect.anchorMin = Vector2.zero;
            _staminaFillRect.anchorMax = Vector2.one;
            _staminaFillRect.offsetMin = new Vector2(2f, 2f);
            _staminaFillRect.offsetMax = new Vector2(-2f, -2f);

            _staminaFillImage = stamFillObj.AddComponent<Image>();
            _staminaFillImage.sprite = GetOrCreateWhiteSprite();
            _staminaFillImage.type = Image.Type.Filled;
            _staminaFillImage.fillMethod = Image.FillMethod.Horizontal;
            _staminaFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            _staminaFillImage.color = _colorHigh;

            // 6. Throw Charge Bar Container & Background
            _throwBarContainer = new GameObject("ThrowBar_Container");
            _throwBarContainer.transform.SetParent(hudRoot.transform, false);

            RectTransform throwBgRect = _throwBarContainer.AddComponent<RectTransform>();
            throwBgRect.anchorMin = new Vector2(0.5f, 0.0f);
            throwBgRect.anchorMax = new Vector2(0.5f, 0.0f);
            throwBgRect.pivot = new Vector2(0.5f, 0.0f);
            throwBgRect.anchoredPosition = new Vector2(0f, 24f);
            throwBgRect.sizeDelta = new Vector2(260f, 12f);

            Image throwBgImg = _throwBarContainer.AddComponent<Image>();
            throwBgImg.sprite = GetOrCreateWhiteSprite();
            throwBgImg.color = new Color(0.04f, 0.06f, 0.09f, 0.85f);

            // 7. Throw Charge Fill Image
            GameObject throwFillObj = new GameObject("ThrowBar_Fill");
            throwFillObj.transform.SetParent(_throwBarContainer.transform, false);

            _throwFillRect = throwFillObj.AddComponent<RectTransform>();
            _throwFillRect.anchorMin = Vector2.zero;
            _throwFillRect.anchorMax = Vector2.one;
            _throwFillRect.offsetMin = new Vector2(2f, 2f);
            _throwFillRect.offsetMax = new Vector2(-2f, -2f);

            _throwFillImage = throwFillObj.AddComponent<Image>();
            _throwFillImage.sprite = GetOrCreateWhiteSprite();
            _throwFillImage.type = Image.Type.Filled;
            _throwFillImage.fillMethod = Image.FillMethod.Horizontal;
            _throwFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            _throwFillImage.color = _colorThrowCharge;

            _throwBarContainer.SetActive(false);
        }

        private Sprite GetOrCreateWhiteSprite()
        {
            EnsureWhiteSprite();
            return _sharedWhiteSprite;
        }
    }
}
