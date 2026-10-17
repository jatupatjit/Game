using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using CoopGame.CarrySystem;

namespace CoopGame.Player
{
    /// <summary>
    /// PlayerStaminaUI generates and manages the local player's Stamina bar and Throw charge meter.
    /// Visuals come from <see cref="UITheme"/> (low-poly block): chamfered track, stamina green that
    /// blends to the danger red-orange below <see cref="UITheme.LowRatio"/>, pulsing danger when exhausted,
    /// orange throw charge. The fill glides smoothly and a ghost trail shows recent loss.
    /// </summary>
    [RequireComponent(typeof(PlayerStamina))]
    [DisallowMultipleComponent]
    public class PlayerStaminaUI : NetworkBehaviour
    {
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
        private PlayerMovement _movement;
        private Wallclimb _wallClimb;
        private PlayerInputReader _inputReader;

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
        private float _fullStaminaTimer = -2.5f; // Show for ~3.7s on spawn so player sees stamina bar
        private float _targetAlpha = 1.0f;
        private bool _isHudCreated = false;

        private void Awake()
        {
            _stamina = GetComponent<PlayerStamina>();
            _playerCarry = GetComponent<PlayerCarry>();
            _movement = GetComponent<PlayerMovement>();
            _wallClimb = GetComponent<Wallclimb>();
            _inputReader = GetComponent<PlayerInputReader>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (!IsOwner)
            {
                if (_hudCanvasGroup != null && _hudCanvasGroup.gameObject != null)
                {
                    Destroy(_hudCanvasGroup.gameObject);
                }
                enabled = false;
                return;
            }

            EnsureHUDCreated();
        }

        private void Start()
        {
            // If offline / singleplayer (not using Netcode or not spawned as network object), create HUD for local player
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                EnsureHUDCreated();
            }
        }

        private void EnsureHUDCreated()
        {
            if (_isHudCreated && _canvas != null && _hudCanvasGroup != null) return;
            _isHudCreated = true;

            CreateProceduralHUD();
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _isHudCreated = false;
            if (_hudCanvasGroup != null && _hudCanvasGroup.gameObject != null)
            {
                Destroy(_hudCanvasGroup.gameObject);
            }
        }

        public override void OnDestroy()
        {
            if (_hudCanvasGroup != null && _hudCanvasGroup.gameObject != null)
            {
                Destroy(_hudCanvasGroup.gameObject);
            }
            base.OnDestroy();
        }

        private void Update()
        {
            // A scene transition destroys the scene-owned canvas while the network player survives.
            if (IsSpawned && IsOwner && _isHudCreated && (_canvas == null || _hudCanvasGroup == null))
                EnsureHUDCreated();

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
                UITheme.SetBarFill(_staminaFillImage, _displayedStamina);

                if (isExhausted)
                {
                    // Rapid danger pulse during exhaustion
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 14.0f);
                    Color pulseColor = UITheme.Danger;
                    pulseColor.a = pulse;
                    _staminaFillImage.color = pulseColor;
                }
                else
                {
                    _staminaFillImage.color = UITheme.Meter(_displayedStamina, UITheme.Stamina);
                }
            }

            // 4. Update Ghost Trail Width
            if (_staminaGhostImage != null)
            {
                UITheme.SetBarFill(_staminaGhostImage, _ghostStamina);
            }

            // 5. Update Throw Charge Bar
            if (_throwBarContainer != null && _throwFillImage != null)
            {
                _throwBarContainer.SetActive(isChargingThrow);
                if (isChargingThrow)
                {
                    UITheme.SetBarFill(_throwFillImage, throwCharge);

                    _throwFillImage.color = (throwCharge >= 0.98f)
                        ? Color.Lerp(UITheme.Primary, UITheme.TextPrimary, 0.6f * Mathf.PingPong(Time.time * 8f, 1f))
                        : UITheme.Primary;
                }
            }

            // 6. Smart Auto-Fade: Visible when active/straining, hidden when idle at 100%
            bool isSprinting = (_movement != null && _movement.CurrentSpeed > 0.1f && (_inputReader != null && _inputReader.SprintHeld));
            bool isClimbing = (_wallClimb != null && _wallClimb.IsClimbing);
            bool isCarrying = (_playerCarry != null && _playerCarry.IsCarrying);

            if (targetNormStamina < 0.999f || isExhausted || isChargingThrow || _ghostStamina < 0.999f || isSprinting || isClimbing || isCarrying)
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

        private void CreateProceduralHUD()
        {
            // 1. Locate or create dedicated Player HUD Canvas
            GameObject canvasObj = GameObject.Find("PlayerHUD_Canvas");
            if (canvasObj == null)
            {
                canvasObj = new GameObject("PlayerHUD_Canvas");
                _canvas = canvasObj.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 50;

                CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;

                canvasObj.AddComponent<GraphicRaycaster>();
            }
            else
            {
                _canvas = canvasObj.GetComponent<Canvas>();
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
            UITheme.StyleTrack(stamBgImg);
            stamBgImg.raycastTarget = false;

            // 4. Ghost Trail Image (Lags behind to show recent stamina loss)
            GameObject stamGhostObj = new GameObject("StaminaBar_Ghost");
            stamGhostObj.transform.SetParent(stamBgObj.transform, false);

            _staminaGhostRect = stamGhostObj.AddComponent<RectTransform>();
            _staminaGhostRect.anchorMin = Vector2.zero;
            _staminaGhostRect.anchorMax = Vector2.one;
            _staminaGhostRect.offsetMin = new Vector2(2f, 2f);
            _staminaGhostRect.offsetMax = new Vector2(-2f, -2f);

            _staminaGhostImage = stamGhostObj.AddComponent<Image>();
            UITheme.StyleFill(_staminaGhostImage, UITheme.Ghost);
            _staminaGhostImage.raycastTarget = false;

            // 5. Stamina Fill Image (Main foreground bar)
            GameObject stamFillObj = new GameObject("StaminaBar_Fill");
            stamFillObj.transform.SetParent(stamBgObj.transform, false);

            _staminaFillRect = stamFillObj.AddComponent<RectTransform>();
            _staminaFillRect.anchorMin = Vector2.zero;
            _staminaFillRect.anchorMax = Vector2.one;
            _staminaFillRect.offsetMin = new Vector2(2f, 2f);
            _staminaFillRect.offsetMax = new Vector2(-2f, -2f);

            _staminaFillImage = stamFillObj.AddComponent<Image>();
            UITheme.StyleFill(_staminaFillImage, UITheme.Stamina);
            _staminaFillImage.raycastTarget = false;

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
            UITheme.StyleTrack(throwBgImg);
            throwBgImg.raycastTarget = false;

            // 7. Throw Charge Fill Image
            GameObject throwFillObj = new GameObject("ThrowBar_Fill");
            throwFillObj.transform.SetParent(_throwBarContainer.transform, false);

            _throwFillRect = throwFillObj.AddComponent<RectTransform>();
            _throwFillRect.anchorMin = Vector2.zero;
            _throwFillRect.anchorMax = Vector2.one;
            _throwFillRect.offsetMin = new Vector2(2f, 2f);
            _throwFillRect.offsetMax = new Vector2(-2f, -2f);

            _throwFillImage = throwFillObj.AddComponent<Image>();
            UITheme.StyleFill(_throwFillImage, UITheme.Primary);
            _throwFillImage.raycastTarget = false;

            _throwBarContainer.SetActive(false);
        }
    }
}
