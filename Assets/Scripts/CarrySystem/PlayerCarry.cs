using Unity.Netcode;
using UnityEngine;
using CoopGame.Player;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// PlayerCarry manages two-handed grab/drop physics and dynamic vertical lifting
    /// inspired by Human Fall Flat.
    /// 
    /// Features (Human Fall Flat Style):
    /// 1. True Independent Hands (มือซ้ายและมือขวาทำงานแยกกัน 100%):
    ///    - Left Click = Left Hand reaches & grabs.
    ///    - Right Click = Right Hand reaches & grabs.
    ///    - When holding with Right Hand, Left Hand remains completely free to aim and grab!
    ///    - Left Hand Marker NEVER disappears when Right Hand is used.
    /// 2. Dual Surface Markers (มาร์กเกอร์ 2 อัน):
    ///    - Free hand shows its marker on reachable surfaces in real time (GREEN).
    ///    - Gripping hand keeps its marker anchored to its physical grip point on the object.
    /// 3. Free Unrestricted Controls (ควบคุมอย่างอิสระ):
    ///    - Player moves at full 100% speed on WASD in all directions.
    ///    - Zero artificial rotation lock; the object swings, tilts, and pivots naturally on the hands.
    /// 4. Rich Diagnostics & Logging (Log ละเอียด):
    ///    - Logs reach, single-hand vs dual-hand grabs, active PhysX gravity, dynamic height, and releases.
    /// </summary>
    [RequireComponent(typeof(PlayerInputReader))]
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class PlayerCarry : NetworkBehaviour
    {
        [Header("Interaction & Facing Settings")]
        [Tooltip("Actual hand physical grab contact distance (hands reach the object)")]
        [SerializeField] private float _grabContactDistance = 1.6f;

        [Tooltip("Maximum distance to scan surfaces for the aim reticle")]
        [SerializeField] private float _maxScanDistance = 12.0f;

        [Tooltip("Minimum dot product to allow grabbing (0.0 = 90 degrees frontal cone)")]
        [SerializeField] private float _minFacingDot = 0.0f;

        [Tooltip("Layer mask for raycasting interactable and environmental surfaces")]
        [SerializeField] private LayerMask _scanLayers = ~0;

        [Header("Dynamic Vertical Lift Range (Mouse Up/Down)")]
        [Tooltip("Maximum lift height when looking all the way up (meters above feet)")]
        [SerializeField] private float _maxLiftHeight = 1.7f;

        [Tooltip("Normal carry height when looking straight ahead")]
        [SerializeField] private float _normalLiftHeight = 0.85f;

        [Tooltip("Minimum carry height when looking down into carts/ground")]
        [SerializeField] private float _minLiftHeight = 0.25f;

        [Header("Procedural Hands")]
        [Tooltip("Visual transform for Left Hand. Generated automatically if empty.")]
        [SerializeField] private Transform _leftHand;

        [Tooltip("Visual transform for Right Hand. Generated automatically if empty.")]
        [SerializeField] private Transform _rightHand;

        [Header("Two-Handed Surface Contact Markers")]
        [Tooltip("Visual disc marker for Left Hand contact point on object surface")]
        [SerializeField] private Transform _leftMarker;

        [Tooltip("Visual disc marker for Right Hand contact point on object surface")]
        [SerializeField] private Transform _rightMarker;

        [Tooltip("Legacy reference for single marker backward compatibility")]
        [SerializeField] private Transform _persistentMarker;

        [Header("Carryable Object Outline")]
        [Tooltip("Outline color when aiming at an object that can be lifted")]
        [SerializeField] private Color _aimOutlineColor = new Color(0.2f, 1.0f, 0.4f, 0.95f);

        [Tooltip("Outline color while actively holding the object")]
        [SerializeField] private Color _holdingOutlineColor = new Color(0.0f, 0.9f, 1.0f, 0.95f);

        [Tooltip("Outline line thickness")]
        [Range(1.0f, 10.0f)]
        [SerializeField] private float _outlineWidth = 3.5f;

        [Tooltip("Whether the outline should pulse gently when highlighted")]
        [SerializeField] private bool _enableOutlinePulse = false;

        [Header("Throwing Mechanics")]
        [Tooltip("Minimum throw launch speed when tapped")]
        [SerializeField] private float _minThrowSpeed = 4.5f;

        [Tooltip("Maximum throw launch speed at full charge")]
        [SerializeField] private float _maxThrowSpeed = 13.5f;

        [Tooltip("Time in seconds to reach full throw charge")]
        [SerializeField] private float _maxChargeTime = 1.0f;

        [Tooltip("Upward arc angle factor for ballistic lobbing")]
        [Range(0.1f, 0.8f)]
        [SerializeField] private float _throwUpwardArc = 0.35f;

        [Tooltip("Percentage of player character movement momentum transferred to throw")]
        [Range(0.0f, 1.0f)]
        [SerializeField] private float _momentumTransfer = 0.7f;

        [Tooltip("Stamina cost when executing a full-power throw")]
        [SerializeField] private float _maxThrowStaminaCost = 25.0f;

        // Cached components
        private PlayerInputReader _inputReader;
        private PlayerMovement _movement;
        private CharacterController _characterController;
        private PlayerCameraController _cameraController;
        private PlayerStamina _stamina;
        private Collider[] _playerColliders;
        private Camera _cachedCamera;
        private Wallclimb _wallClimb;

        // Marker renderers and materials
        private Renderer _leftMarkerRenderer;
        private Renderer _rightMarkerRenderer;
        private Material _markerMaterial;
        private MaterialPropertyBlock _markerPropBlock;

        // Independent hand gripping states (Human Fall Flat style)
        private bool _leftHandGripping = false;
        private bool _rightHandGripping = false;
        private CarryableObject _currentCarryable = null;
        private int _assignedSocketIndex = -1;

        // Cached surface contact points from dual markers (World space)
        private Vector3 _lastAimLeftPoint = Vector3.zero;
        private Vector3 _lastAimRightPoint = Vector3.zero;

        // Cached surface contact points in carried object local space
        private Vector3 _currentLocalContactLeft = new Vector3(-0.25f, 0f, -0.4f);
        private Vector3 _currentLocalContactRight = new Vector3(0.25f, 0f, -0.4f);

        // Hand rest offsets (local to player)
        private readonly Vector3 _leftHandRest = new Vector3(-0.35f, 0.5f, 0.1f);
        private readonly Vector3 _rightHandRest = new Vector3(0.35f, 0.5f, 0.1f);

        // Marker Colors (Liftable = Green, Not Liftable = Red)
        private readonly Color _colorLiftableGreen = new Color(0.1f, 1.0f, 0.25f, 0.95f);  // Bright Green
        private readonly Color _colorNotLiftableRed = new Color(1.0f, 0.15f, 0.15f, 0.95f); // Bright Red

        // Diagnostics & Logging Timers
        private float _carryLogTimer = 0f;
        private bool _wasAimingAtReachable = false;

        // Outline tracking
        private CarryableObject _currentlyHighlightedCarryable = null;

        // Throwing state
        private float _currentThrowCharge = 0.0f;
        private bool _isChargingThrow = false;
        private float _throwReleaseCooldown = 0.0f;
        private bool _requireGrabRelease = false;
        private bool _canAttemptGrab = true;

        // Exposed properties
        public bool IsCarrying => _leftHandGripping || _rightHandGripping;
        public bool LeftHandGripping => _leftHandGripping;
        public bool RightHandGripping => _rightHandGripping;
        public CarryableObject CurrentCarryable => _currentCarryable;
        public float CurrentThrowCharge => _currentThrowCharge;
        public bool IsChargingThrow => _isChargingThrow;
        public PlayerStamina Stamina => _stamina;
        public Transform LeftHand => _leftHand;
        public Transform RightHand => _rightHand;

        // Replicated Hand Gestures & Gripping state for Remote Player Proxies
        // bit 0 = Left hand gripping
        // bit 1 = Right hand gripping
        // bit 2 = Left hand reaching
        // bit 3 = Right hand reaching
        // bit 4 = Charging throw
        private readonly NetworkVariable<byte> _netHandState = new NetworkVariable<byte>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        // Synchronized vertical hold height (derived from camera pitch) so remote players see up/down gestures
        private readonly NetworkVariable<float> _netHoldHeight = new NetworkVariable<float>(
            0.85f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        // Synchronized NetworkObjectId of the carried object, ensuring 100% reliable state sync for all clients (including late joiners)
        private readonly NetworkVariable<ulong> _netCarriedObjectId = new NetworkVariable<ulong>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private void Awake()
        {
            _inputReader = GetComponent<PlayerInputReader>();
            _movement = GetComponent<PlayerMovement>();
            _characterController = GetComponent<CharacterController>();
            _cameraController = GetComponent<PlayerCameraController>();
            _stamina = GetComponent<PlayerStamina>();
            if (_stamina == null)
            {
                _stamina = gameObject.AddComponent<PlayerStamina>();
            }

            // In offline / singleplayer mode, add HUD directly
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                if (GetComponent<PlayerStaminaUI>() == null)
                {
                    gameObject.AddComponent<PlayerStaminaUI>();
                }
            }

            _playerColliders = GetComponentsInChildren<Collider>(true);
            _wallClimb = GetComponent<Wallclimb>();

            EnsureVisualHandsCreated();
            EnsureDualMarkersCreated();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            _netCarriedObjectId.OnValueChanged += OnCarriedObjectIdChanged;

            if (!IsOwner && _netCarriedObjectId.Value != 0)
            {
                ResolveCarriedObject(_netCarriedObjectId.Value);
            }

            if (IsOwner)
            {
                if (GetComponent<PlayerStaminaUI>() == null)
                {
                    gameObject.AddComponent<PlayerStaminaUI>();
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            _netCarriedObjectId.OnValueChanged -= OnCarriedObjectIdChanged;

            HideMarkers();
            ClearOutlineHighlight();

            if (IsCarrying)
            {
                ReleaseCarryState();
            }
        }

        private void OnCarriedObjectIdChanged(ulong previousValue, ulong newValue)
        {
            if (IsOwner) return;

            if (newValue != 0)
            {
                ResolveCarriedObject(newValue);
            }
            else
            {
                ReleaseCarryState();
            }
        }

        private void ResolveCarriedObject(ulong netId)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null &&
                NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(netId, out NetworkObject netObj))
            {
                CarryableObject carryable = netObj.GetComponent<CarryableObject>();
                if (carryable != null)
                {
                    _currentCarryable = carryable;
                }
            }
        }

        private void OnDisable()
        {
            ClearOutlineHighlight();
        }

        private void Update()
        {
            bool isLocalOwner = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? IsOwner : true;
            if (!isLocalOwner)
            {
                UpdateVisualHandsProxy();
                return;
            }

            // 1. Calculate dynamic lift height from camera pitch (Mouse Up / Down)
            float currentHoldHeight = _normalLiftHeight;
            if (_cameraController != null)
            {
                float pitch = _cameraController.Pitch;
                float normalizedPitch = (pitch < 0f) 
                    ? Mathf.InverseLerp(0f, -35f, pitch) 
                    : Mathf.InverseLerp(0f, 50f, pitch);
                currentHoldHeight = (pitch < 0f)
                    ? Mathf.Lerp(_normalLiftHeight, _maxLiftHeight, normalizedPitch)
                    : Mathf.Lerp(_normalLiftHeight, _minLiftHeight, normalizedPitch);
            }

            // 2. Camera forward and right vectors for movement & carrying
            Vector3 camForward = (_cameraController != null) ? _cameraController.HorizontalForward : transform.forward;
            Vector3 camRight = (_cameraController != null) ? _cameraController.HorizontalRight : transform.right;

            // 3. Update Dual Aim Markers (Handles both free aiming & locked grip markers)
            UpdatePersistentAimMarker(out CarryableObject aimedCarryable, out bool canGrabAimed);

            // 3.1 Update Outline Highlight on Aimed / Carried Object
            UpdateCarryableOutline(aimedCarryable, canGrabAimed);

            // 4.5. Grab attempt clearance and release requirement after throw
            if (_throwReleaseCooldown > 0f)
            {
                _throwReleaseCooldown -= Time.deltaTime;
            }

            // 5. Independent Hand Input Checking (Human Fall Flat style)
            bool leftClick = _inputReader.GrabLeftHeld || _inputReader.InteractHeld;
            bool rightClick = _inputReader.GrabRightHeld || _inputReader.InteractHeld;

            if (!leftClick && !rightClick)
            {
                _requireGrabRelease = false;
            }

            _canAttemptGrab = !_requireGrabRelease && (_throwReleaseCooldown <= 0f);

            // 4. Animate procedural hands to touch contact points or reach/rest
            UpdateVisualHands(currentHoldHeight);

            // Release hands when button is released
            if (!leftClick && _leftHandGripping)
            {
                _leftHandGripping = false;
                Debug.Log($"[PlayerCarry] Client {OwnerClientId} released LEFT hand.");
                if (_currentCarryable != null)
                {
                    SyncCarrierHandState();
                }
            }

            if (!rightClick && _rightHandGripping)
            {
                _rightHandGripping = false;
                Debug.Log($"[PlayerCarry] Client {OwnerClientId} released RIGHT hand.");
                if (_currentCarryable != null)
                {
                    SyncCarrierHandState();
                }
            }

            // Grab with Left Hand if clicked, not already gripping, and grab is allowed
            if (leftClick && !_leftHandGripping && _canAttemptGrab)
            {
                if (_wallClimb != null && _wallClimb.IsClimbing)
                {
                    // Wall climbing takes precedence over item grabbing
                }
                else if (canGrabAimed && aimedCarryable != null)
                {
                    GrabSingleHand(aimedCarryable, isLeft: true);
                }
                else if (_currentCarryable == null && (_wallClimb == null || !_wallClimb.IsAimingAtWall))
                {
                    TryGrabNearbyObjectSingleHand(isLeft: true);
                }
            }

            // Grab with Right Hand if clicked, not already gripping, and grab is allowed
            if (rightClick && !_rightHandGripping && _canAttemptGrab)
            {
                if (_wallClimb != null && _wallClimb.IsClimbing)
                {
                    // Wall climbing takes precedence over item grabbing
                }
                else if (canGrabAimed && aimedCarryable != null)
                {
                    GrabSingleHand(aimedCarryable, isLeft: false);
                }
                else if (_currentCarryable == null && (_wallClimb == null || !_wallClimb.IsAimingAtWall))
                {
                    TryGrabNearbyObjectSingleHand(isLeft: false);
                }
            }

            // 6. Handle active carrying state, stamina exertion, throw charging, or complete drop
            if (_leftHandGripping || _rightHandGripping)
            {
                // Full 100% free movement like Human Fall Flat!
                if (_movement != null)
                {
                    // If charging throw or exhausted, apply slight movement penalty
                    if (_isChargingThrow)
                    {
                        _movement.SpeedMultiplier = 0.75f;
                    }
                    else if (_stamina != null && _stamina.IsExhausted)
                    {
                        _movement.SpeedMultiplier = 0.5f;
                    }
                    else
                    {
                        _movement.SpeedMultiplier = 1.0f;
                    }
                }

                if (_currentCarryable != null)
                {
                    // 6.1 Continuous Stamina Drain & Exhaustion Slip
                    if (_stamina != null)
                    {
                        bool isOneHanded = !(_leftHandGripping && _rightHandGripping);
                        int carrierCount = _currentCarryable.CurrentCarrierCount;
                        _stamina.DrainStaminaContinuous(isOneHanded, _currentCarryable.TotalMass, carrierCount);

                        if (_stamina.IsExhausted)
                        {
                            Debug.Log($"[PlayerCarry] Client {OwnerClientId} EXHAUSTED! Hands slipped from '{_currentCarryable.name}'.");
                            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
                            {
                                RequestDropServerRpc();
                            }
                            else
                            {
                                _currentCarryable.DetachCarrier(OwnerClientId);
                                ReleaseCarryState();
                            }
                            return;
                        }
                    }

                    // 6.2 Throw Charging & Execution
                    bool throwHeld = _inputReader.ThrowHeld;
                    if (throwHeld && (_stamina == null || !_stamina.IsExhausted))
                    {
                        _isChargingThrow = true;
                        _currentThrowCharge = Mathf.Clamp01(_currentThrowCharge + Time.deltaTime / _maxChargeTime);
                    }
                    else if (_isChargingThrow)
                    {
                        ExecuteThrow(camForward, camRight);
                    }

                    Vector2 input = _inputReader.MoveInput;
                    Vector3 desiredWorldDir = (camForward * input.y + camRight * input.x);
                    if (desiredWorldDir.sqrMagnitude > 1.0f)
                    {
                        desiredWorldDir.Normalize();
                    }

                    if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
                    {
                        StreamCarrierInputServerRpc(desiredWorldDir, camForward, currentHoldHeight, _leftHandGripping, _rightHandGripping);
                    }
                    else
                    {
                        _currentCarryable.UpdateCarrierInput(OwnerClientId, desiredWorldDir, camForward, currentHoldHeight, _leftHandGripping, _rightHandGripping);
                    }

                    // Periodic diagnostic logging
                    _carryLogTimer += Time.deltaTime;
                    if (_carryLogTimer >= 2.0f)
                    {
                        _carryLogTimer = 0f;
                        Rigidbody rb = _currentCarryable.GetComponent<Rigidbody>();
                        Vector3 currentVel = (rb != null) ? rb.linearVelocity : Vector3.zero;
                        string handMode = (_leftHandGripping && _rightHandGripping) ? "BOTH HANDS" : (_leftHandGripping ? "LEFT HAND" : "RIGHT HAND");
                        int count = _currentCarryable.CurrentCarrierCount;
                        string coopInfo = (count > 1) ? $" | Co-op Carriers: {count} (Stamina Drain Reduced by Co-op Synergy!)" : "";
                        Debug.Log($"[PlayerCarry] Carrying '{_currentCarryable.name}' with {handMode} | LiftHeight: {currentHoldHeight:F2}m | Mass: {_currentCarryable.TotalMass:F1}kg{coopInfo} | PhysX Gravity: ACTIVE | Velocity: {currentVel.magnitude:F2}m/s");
                    }
                }
            }
            else
            {
                _isChargingThrow = false;
                _currentThrowCharge = 0f;

                // Both hands released: drop object completely
                if (_currentCarryable != null)
                {
                    if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
                    {
                        RequestDropServerRpc();
                    }
                    else
                    {
                        _currentCarryable.DetachCarrier(OwnerClientId);
                        ReleaseCarryState();
                    }
                }
            }

            // 7. Replicate Hand Mask to Remote Clients
            byte handMask = 0;
            if (_leftHandGripping) handMask |= (1 << 0);
            if (_rightHandGripping) handMask |= (1 << 1);
            if (leftClick) handMask |= (1 << 2);
            if (rightClick) handMask |= (1 << 3);
            if (_isChargingThrow) handMask |= (1 << 4);

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
            {
                if (_netHandState.Value != handMask)
                {
                    _netHandState.Value = handMask;
                }

                if (Mathf.Abs(_netHoldHeight.Value - currentHoldHeight) > 0.015f)
                {
                    _netHoldHeight.Value = currentHoldHeight;
                }
            }
        }

        private void GrabSingleHand(CarryableObject target, bool isLeft)
        {
            if (target == null) return;
            if (_stamina != null && _stamina.IsExhausted)
            {
                Debug.Log($"[PlayerCarry] Client {OwnerClientId} is exhausted! Cannot grab until stamina recovers.");
                return;
            }

            if (isLeft)
            {
                _leftHandGripping = true;
                if (_lastAimLeftPoint != Vector3.zero)
                {
                    _currentLocalContactLeft = target.transform.InverseTransformPoint(_lastAimLeftPoint);
                }
                else
                {
                    _currentLocalContactLeft = new Vector3(-0.25f, 0f, -0.4f);
                }
            }
            else
            {
                _rightHandGripping = true;
                if (_lastAimRightPoint != Vector3.zero)
                {
                    _currentLocalContactRight = target.transform.InverseTransformPoint(_lastAimRightPoint);
                }
                else
                {
                    _currentLocalContactRight = new Vector3(0.25f, 0f, -0.4f);
                }
            }

            string handName = isLeft ? "LEFT HAND" : "RIGHT HAND";
            Debug.Log($"[PlayerCarry] Client {OwnerClientId} grabbing '{target.name}' with {handName}. Contact L: {_currentLocalContactLeft:F2} | R: {_currentLocalContactRight:F2} | Gravity: ACTIVE");

            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned);

            if (_currentCarryable == null)
            {
                if (isNetworked)
                {
                    NetworkObject netObj = target.GetComponent<NetworkObject>();
                    if (netObj != null)
                    {
                        RequestGrabServerRpc(netObj.NetworkObjectId, _currentLocalContactLeft, _currentLocalContactRight, _leftHandGripping, _rightHandGripping);
                    }
                }
                else
                {
                    if (target.TryAttachCarrier(OwnerClientId, transform, _movement, _currentLocalContactLeft, _currentLocalContactRight, _leftHandGripping, _rightHandGripping, out int socketIndex))
                    {
                        OnGrabSuccessful(target, socketIndex);
                    }
                }
            }
            else
            {
                // Already attached: sync the newly added hand state
                SyncCarrierHandState();
            }
        }

        private void SyncCarrierHandState()
        {
            if (_currentCarryable == null) return;

            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned);
            if (isNetworked)
            {
                SyncHandStateServerRpc(_currentLocalContactLeft, _currentLocalContactRight, _leftHandGripping, _rightHandGripping);
            }
            else
            {
                _currentCarryable.UpdateCarrierHandState(OwnerClientId, _currentLocalContactLeft, _currentLocalContactRight, _leftHandGripping, _rightHandGripping);
            }
        }

        /// <summary>
        /// Updates the dual aim markers (Left Hand & Right Hand) on surfaces.
        /// Runs continuously so a free hand always shows where it can grab!
        /// </summary>
        private void UpdatePersistentAimMarker(out CarryableObject aimedCarryable, out bool canGrabAimed)
        {
            aimedCarryable = null;
            canGrabAimed = false;

            if (_leftMarker == null || _rightMarker == null) return;

            bool isExhausted = (_stamina != null && _stamina.IsExhausted);

            // Locate active player camera directly from PlayerCameraController
            if (_cameraController != null && _cameraController.PlayerCamera != null && _cameraController.PlayerCamera.isActiveAndEnabled)
            {
                _cachedCamera = _cameraController.PlayerCamera;
            }
            else if (_cachedCamera == null || !_cachedCamera.isActiveAndEnabled)
            {
                _cachedCamera = Camera.main;
                if (_cachedCamera == null)
                {
                    _cachedCamera = FindAnyObjectByType<Camera>();
                }
            }

            Vector3 rayOrigin;
            Vector3 rayDirection;

            if (_cachedCamera != null)
            {
                rayOrigin = _cachedCamera.transform.position;
                rayDirection = _cachedCamera.transform.forward;
            }
            else
            {
                rayOrigin = transform.position + Vector3.up * 1.5f;
                rayDirection = (_cameraController != null)
                    ? Quaternion.Euler(_cameraController.Pitch, _cameraController.Yaw, 0f) * Vector3.forward
                    : transform.forward;
            }

            // Raycast into the world, ignoring player capsule and aim markers
            Ray aimRay = new Ray(rayOrigin, rayDirection);
            RaycastHit[] hits = Physics.RaycastAll(aimRay, _maxScanDistance, _scanLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            RaycastHit validHit = default;
            bool foundValidHit = false;

            foreach (var h in hits)
            {
                // Skip self colliders (avoids hitting player capsule in 3rd person)
                if (h.collider.transform.root == transform.root) continue;
                if (_playerColliders != null && System.Array.IndexOf(_playerColliders, h.collider) >= 0) continue;
                if (_leftMarker != null && (h.collider.transform == _leftMarker || h.collider.transform.IsChildOf(_leftMarker))) continue;
                if (_rightMarker != null && (h.collider.transform == _rightMarker || h.collider.transform.IsChildOf(_rightMarker))) continue;
                if (_persistentMarker != null && (h.collider.transform == _persistentMarker || h.collider.transform.IsChildOf(_persistentMarker))) continue;

                validHit = h;
                foundValidHit = true;
                break;
            }

            Vector3 leftAimPoint = Vector3.zero;
            Vector3 rightAimPoint = Vector3.zero;
            bool reachableHit = false;

            if (foundValidHit)
            {
                CarryableObject carryable = validHit.collider.GetComponentInParent<CarryableObject>();
                if (carryable == null) carryable = validHit.collider.GetComponent<CarryableObject>();

                if (carryable != null && carryable.CanBeCarried)
                {
                    Vector3 playerChest = transform.position + Vector3.up * 1.0f;
                    float distToHit = Vector3.Distance(playerChest, validHit.point);
                    float distToBounds = Vector3.Distance(playerChest, validHit.collider.bounds.ClosestPoint(playerChest));
                    float effectiveDist = Mathf.Min(distToHit, distToBounds);

                    if (effectiveDist <= _grabContactDistance)
                    {
                        aimedCarryable = carryable;
                        canGrabAimed = !isExhausted;
                        reachableHit = true;

                        Vector3 camRight = (_cameraController != null) ? _cameraController.HorizontalRight : transform.right;
                        Vector3 surfaceLateral = Vector3.ProjectOnPlane(camRight, validHit.normal).normalized;
                        if (surfaceLateral.sqrMagnitude < 0.01f) surfaceLateral = transform.right;

                        float handSpacing = 0.22f;
                        leftAimPoint = validHit.point - surfaceLateral * handSpacing;
                        rightAimPoint = validHit.point + surfaceLateral * handSpacing;

                        Collider hitCol = validHit.collider;
                        if (hitCol != null && !(hitCol is MeshCollider mc && !mc.convex))
                        {
                            leftAimPoint = hitCol.ClosestPoint(leftAimPoint);
                            rightAimPoint = hitCol.ClosestPoint(rightAimPoint);
                        }

                        _lastAimLeftPoint = leftAimPoint;
                        _lastAimRightPoint = rightAimPoint;

                        if (!_wasAimingAtReachable)
                        {
                            _wasAimingAtReachable = true;
                            Debug.Log($"[PlayerCarry] Reach Detected: '{carryable.name}' in range ({effectiveDist:F2}m). Markers active (GREEN).");
                        }
                    }
                }
            }

            if (!reachableHit)
            {
                _wasAimingAtReachable = false;
            }

            // =========================================================================
            // UPDATE LEFT MARKER:
            // If gripping -> sticks to current carryable contact point.
            // If free -> shows reachable surface point in Green!
            // =========================================================================
            if (_leftHandGripping && _currentCarryable != null)
            {
                _leftMarker.gameObject.SetActive(true);
                _leftMarker.position = _currentCarryable.transform.TransformPoint(_currentLocalContactLeft);
                _leftMarker.rotation = Quaternion.FromToRotation(Vector3.up, _currentCarryable.transform.up);
            }
            else if (reachableHit)
            {
                _leftMarker.gameObject.SetActive(true);
                _leftMarker.position = leftAimPoint + validHit.normal * 0.012f;
                if (validHit.normal.sqrMagnitude > 0.01f)
                {
                    _leftMarker.rotation = Quaternion.FromToRotation(Vector3.up, validHit.normal);
                }
            }
            else
            {
                _leftMarker.gameObject.SetActive(false);
            }

            // =========================================================================
            // UPDATE RIGHT MARKER:
            // If gripping -> sticks to current carryable contact point.
            // If free -> shows reachable surface point in Green!
            // =========================================================================
            if (_rightHandGripping && _currentCarryable != null)
            {
                _rightMarker.gameObject.SetActive(true);
                _rightMarker.position = _currentCarryable.transform.TransformPoint(_currentLocalContactRight);
                _rightMarker.rotation = Quaternion.FromToRotation(Vector3.up, _currentCarryable.transform.up);
            }
            else if (reachableHit)
            {
                _rightMarker.gameObject.SetActive(true);
                _rightMarker.position = rightAimPoint + validHit.normal * 0.012f;
                if (validHit.normal.sqrMagnitude > 0.01f)
                {
                    _rightMarker.rotation = Quaternion.FromToRotation(Vector3.up, validHit.normal);
                }
            }
            else
            {
                _rightMarker.gameObject.SetActive(false);
            }

            SetMarkersColor(isExhausted ? _colorNotLiftableRed : _colorLiftableGreen);
        }

        private void UpdateCarryableOutline(CarryableObject aimedCarryable, bool canGrabAimed)
        {
            CarryableObject targetToHighlight = null;
            Color targetColor = _aimOutlineColor;

            if ((_leftHandGripping || _rightHandGripping) && _currentCarryable != null)
            {
                // When holding an object, keep outline active around the held object
                targetToHighlight = _currentCarryable;
                targetColor = _holdingOutlineColor;
            }
            else if (canGrabAimed && aimedCarryable != null && aimedCarryable.CanBeCarried)
            {
                // When aiming at an object within reach that can be lifted
                targetToHighlight = aimedCarryable;
                targetColor = _aimOutlineColor;
            }

            if (_currentlyHighlightedCarryable != targetToHighlight)
            {
                if (_currentlyHighlightedCarryable != null)
                {
                    _currentlyHighlightedCarryable.SetOutline(false);
                }

                _currentlyHighlightedCarryable = targetToHighlight;

                if (_currentlyHighlightedCarryable != null)
                {
                    _currentlyHighlightedCarryable.SetOutline(true, targetColor, _outlineWidth, _enableOutlinePulse);
                }
            }
            else if (_currentlyHighlightedCarryable != null)
            {
                // Ensure active color and pulse state match current state (aimed vs held)
                _currentlyHighlightedCarryable.SetOutline(true, targetColor, _outlineWidth, _enableOutlinePulse);
            }
        }

        private void ClearOutlineHighlight()
        {
            if (_currentlyHighlightedCarryable != null)
            {
                _currentlyHighlightedCarryable.SetOutline(false);
                _currentlyHighlightedCarryable = null;
            }
        }

        private void HideMarkers()
        {
            if (_leftMarker != null) _leftMarker.gameObject.SetActive(false);
            if (_rightMarker != null) _rightMarker.gameObject.SetActive(false);
            if (_persistentMarker != null) _persistentMarker.gameObject.SetActive(false);
        }

        private void SetMarkersColor(Color color)
        {
            if (_markerPropBlock == null)
            {
                _markerPropBlock = new MaterialPropertyBlock();
            }

            _markerPropBlock.SetColor("_BaseColor", color);
            _markerPropBlock.SetColor("_Color", color);

            if (_leftMarkerRenderer != null)
            {
                _leftMarkerRenderer.SetPropertyBlock(_markerPropBlock);
            }
            if (_rightMarkerRenderer != null)
            {
                _rightMarkerRenderer.SetPropertyBlock(_markerPropBlock);
            }

            if (_markerMaterial != null)
            {
                _markerMaterial.SetColor("_BaseColor", color);
                _markerMaterial.SetColor("_Color", color);
                _markerMaterial.color = color;
            }
        }

        private void TryGrabNearbyObjectSingleHand(bool isLeft)
        {
            Vector3 chestPos = transform.position + Vector3.up * 1.0f;
            Collider[] colliders = Physics.OverlapSphere(chestPos, _grabContactDistance, _scanLayers, QueryTriggerInteraction.Ignore);

            CarryableObject closestCarryable = null;
            float closestDistanceSqr = float.MaxValue;
            Vector3 viewForward = (_cameraController != null) ? _cameraController.HorizontalForward : transform.forward;

            foreach (var col in colliders)
            {
                if (col.transform.root == transform.root) continue;
                CarryableObject carryable = col.GetComponentInParent<CarryableObject>();
                if (carryable == null) carryable = col.GetComponent<CarryableObject>();
                if (carryable == null || !carryable.CanBeCarried) continue;

                Vector3 closestPoint = (col is MeshCollider mc && !mc.convex) 
                    ? col.bounds.ClosestPoint(chestPos) 
                    : col.ClosestPoint(chestPos);
                Vector3 toObject = (closestPoint - chestPos);
                float distanceSqr = toObject.sqrMagnitude;

                if (distanceSqr > _grabContactDistance * _grabContactDistance) continue;

                Vector3 dirToObject = toObject;
                dirToObject.y = 0f;
                if (dirToObject.sqrMagnitude > 0.001f)
                {
                    float forwardDot = Vector3.Dot(viewForward, dirToObject.normalized);
                    if (forwardDot < _minFacingDot) continue;
                }

                if (distanceSqr < closestDistanceSqr)
                {
                    closestDistanceSqr = distanceSqr;
                    closestCarryable = carryable;
                }
            }

            if (closestCarryable != null)
            {
                GrabSingleHand(closestCarryable, isLeft);
            }
        }

        // Backward compatibility overloads
        public void InitiateGrab(CarryableObject target) => GrabSingleHand(target, isLeft: true);
        public void InitiateGrab(CarryableObject target, bool leftActive, bool rightActive)
        {
            if (leftActive) GrabSingleHand(target, isLeft: true);
            if (rightActive) GrabSingleHand(target, isLeft: false);
        }
        public void TryGrabNearbyObject() => TryGrabNearbyObjectSingleHand(isLeft: true);
        public void TryGrabNearbyObject(bool leftActive, bool rightActive)
        {
            if (leftActive) TryGrabNearbyObjectSingleHand(isLeft: true);
            if (rightActive) TryGrabNearbyObjectSingleHand(isLeft: false);
        }

        private void UpdateVisualHands(float currentHeight)
        {
            if (_leftHand == null || _rightHand == null) return;
            if (_wallClimb != null && _wallClimb.IsClimbing) return;

            bool leftClick = _inputReader.GrabLeftHeld || _inputReader.InteractHeld;
            bool rightClick = _inputReader.GrabRightHeld || _inputReader.InteractHeld;

            Vector3 targetLeftPos;
            Vector3 targetRightPos;

            // Left Hand: if gripping, attach to box. Else if holding click and allowed, reach forward. Else rest.
            if (_leftHandGripping && _currentCarryable != null)
            {
                Vector3 worldLeft = _currentCarryable.transform.TransformPoint(_currentLocalContactLeft);
                targetLeftPos = transform.InverseTransformPoint(worldLeft);
            }
            else if (leftClick && _canAttemptGrab)
            {
                targetLeftPos = new Vector3(-0.25f, currentHeight, 0.75f);
            }
            else
            {
                targetLeftPos = _leftHandRest;
            }

            // Right Hand: if gripping, attach to box. Else if holding click and allowed, reach forward. Else rest.
            if (_rightHandGripping && _currentCarryable != null)
            {
                Vector3 worldRight = _currentCarryable.transform.TransformPoint(_currentLocalContactRight);
                targetRightPos = transform.InverseTransformPoint(worldRight);
            }
            else if (rightClick && _canAttemptGrab)
            {
                targetRightPos = new Vector3(0.25f, currentHeight, 0.75f);
            }
            else
            {
                targetRightPos = _rightHandRest;
            }

            // Wind-up animation when charging a throw
            if (_isChargingThrow)
            {
                float chargePull = _currentThrowCharge * 0.25f;
                targetLeftPos += new Vector3(0f, -0.1f * _currentThrowCharge, -chargePull);
                targetRightPos += new Vector3(0f, -0.1f * _currentThrowCharge, -chargePull);
            }

            _leftHand.localPosition = Vector3.Lerp(_leftHand.localPosition, targetLeftPos, Time.deltaTime * 30f);
            _rightHand.localPosition = Vector3.Lerp(_rightHand.localPosition, targetRightPos, Time.deltaTime * 30f);
        }

        /// <summary>
        /// Animates procedural hands for remote proxy players across the network.
        /// Replicates reaches, box grabbing, dynamic vertical lifting, and throw wind-ups so everyone sees natural physics gestures.
        /// </summary>
        private void UpdateVisualHandsProxy()
        {
            if (_leftHand == null || _rightHand == null) return;

            // Ensure carried object is resolved if NetworkVariable indicates an active carry
            if (_currentCarryable == null && _netCarriedObjectId.Value != 0)
            {
                ResolveCarriedObject(_netCarriedObjectId.Value);
            }

            byte mask = _netHandState.Value;
            bool leftGrip = (mask & (1 << 0)) != 0;
            bool rightGrip = (mask & (1 << 1)) != 0;
            bool leftReach = (mask & (1 << 2)) != 0;
            bool rightReach = (mask & (1 << 3)) != 0;
            bool chargingThrow = (mask & (1 << 4)) != 0;

            float remoteHoldHeight = _netHoldHeight.Value;

            Vector3 targetLeftPos;
            Vector3 targetRightPos;

            // Left Hand:
            if (leftGrip && _currentCarryable != null)
            {
                Vector3 objPos = _currentCarryable.transform.position;
                Vector3 toObjLocal = transform.InverseTransformPoint(objPos);
                float reachDist = Mathf.Clamp(toObjLocal.magnitude, 0.35f, 1.2f);
                Vector3 forwardNorm = toObjLocal.sqrMagnitude > 0.001f ? toObjLocal.normalized : Vector3.forward;
                targetLeftPos = forwardNorm * reachDist + new Vector3(-0.25f, 0f, 0f);
            }
            else if (leftReach || leftGrip)
            {
                // Naturally tracks remote player's look pitch/height up and down
                targetLeftPos = new Vector3(-0.25f, remoteHoldHeight, 0.75f);
            }
            else
            {
                targetLeftPos = _leftHandRest;
            }

            // Right Hand:
            if (rightGrip && _currentCarryable != null)
            {
                Vector3 objPos = _currentCarryable.transform.position;
                Vector3 toObjLocal = transform.InverseTransformPoint(objPos);
                float reachDist = Mathf.Clamp(toObjLocal.magnitude, 0.35f, 1.2f);
                Vector3 forwardNorm = toObjLocal.sqrMagnitude > 0.001f ? toObjLocal.normalized : Vector3.forward;
                targetRightPos = forwardNorm * reachDist + new Vector3(0.25f, 0f, 0f);
            }
            else if (rightReach || rightGrip)
            {
                // Naturally tracks remote player's look pitch/height up and down
                targetRightPos = new Vector3(0.25f, remoteHoldHeight, 0.75f);
            }
            else
            {
                targetRightPos = _rightHandRest;
            }

            if (chargingThrow)
            {
                targetLeftPos += new Vector3(0f, -0.08f, -0.2f);
                targetRightPos += new Vector3(0f, -0.08f, -0.2f);
            }

            _leftHand.localPosition = Vector3.Lerp(_leftHand.localPosition, targetLeftPos, Time.deltaTime * 20f);
            _rightHand.localPosition = Vector3.Lerp(_rightHand.localPosition, targetRightPos, Time.deltaTime * 20f);
        }

        #region Server RPCs

        [ServerRpc]
        private void RequestGrabServerRpc(ulong targetNetworkObjectId, Vector3 localContactLeft, Vector3 localContactRight, bool leftActive, bool rightActive)
        {
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject netObj))
            {
                return;
            }

            CarryableObject carryable = netObj.GetComponent<CarryableObject>();
            if (carryable == null) return;

            Collider col = carryable.GetComponentInChildren<Collider>();
            float dist = (col != null) 
                ? Vector3.Distance(transform.position, col.bounds.ClosestPoint(transform.position))
                : Vector3.Distance(transform.position, carryable.transform.position);

            // Tightened grab distance check with generous latency margin (max 2.2m instead of 7.0m)
            if (dist > _grabContactDistance + 0.6f) return;

            if (carryable.TryAttachCarrier(OwnerClientId, transform, _movement, localContactLeft, localContactRight, leftActive, rightActive, out int socketIndex))
            {
                _netCarriedObjectId.Value = targetNetworkObjectId;
                NotifyGrabResultClientRpc(targetNetworkObjectId, socketIndex, true);
            }
        }

        [ServerRpc]
        private void SyncHandStateServerRpc(Vector3 localContactLeft, Vector3 localContactRight, bool leftActive, bool rightActive)
        {
            if (_currentCarryable != null)
            {
                _currentCarryable.UpdateCarrierHandState(OwnerClientId, localContactLeft, localContactRight, leftActive, rightActive);
            }
        }

        [ServerRpc]
        private void RequestDropServerRpc()
        {
            if (_currentCarryable != null)
            {
                _currentCarryable.DetachCarrier(OwnerClientId);
            }

            _netCarriedObjectId.Value = 0;
            NotifyDropClientRpc();
        }

        [ServerRpc]
        private void RequestThrowServerRpc(Vector3 linearVelocity, Vector3 angularVelocity)
        {
            if (_currentCarryable != null)
            {
                _currentCarryable.ThrowObject(OwnerClientId, linearVelocity, angularVelocity);
            }

            _netCarriedObjectId.Value = 0;
            NotifyDropClientRpc();
        }

        [ServerRpc(Delivery = RpcDelivery.Unreliable)]
        private void StreamCarrierInputServerRpc(Vector3 worldMoveDirection, Vector3 forwardHeading, float holdHeight, bool leftActive, bool rightActive)
        {
            if (_currentCarryable != null)
            {
                _currentCarryable.UpdateCarrierInput(OwnerClientId, worldMoveDirection, forwardHeading, holdHeight, leftActive, rightActive);
            }
        }

        #endregion

        #region Client RPCs

        [ClientRpc]
        private void NotifyGrabResultClientRpc(ulong targetNetworkObjectId, int socketIndex, bool success)
        {
            if (!success) return;

            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject netObj))
            {
                CarryableObject carryable = netObj.GetComponent<CarryableObject>();
                if (carryable != null)
                {
                    OnGrabSuccessful(carryable, socketIndex);
                }
            }
        }

        [ClientRpc]
        private void NotifyDropClientRpc()
        {
            ReleaseCarryState();
        }

        #endregion

        private void OnGrabSuccessful(CarryableObject carryable, int socketIndex)
        {
            _currentCarryable = carryable;
            _assignedSocketIndex = socketIndex;
            _carryLogTimer = 0f;

            if (_movement != null)
            {
                _movement.SpeedMultiplier = 1.0f;
            }

            SetLocalCollisionIgnore(_currentCarryable, true);

            Debug.Log($"[PlayerCarry] Client {OwnerClientId} attached to '{_currentCarryable.name}' (Socket #{socketIndex}) | Gravity: ACTIVE");
        }

        private void SetLocalCollisionIgnore(CarryableObject target, bool ignore)
        {
            if (target == null) return;
            Collider[] targetColliders = target.GetComponentsInChildren<Collider>(true);
            foreach (var pCol in _playerColliders)
            {
                foreach (var tCol in targetColliders)
                {
                    if (pCol != null && tCol != null)
                    {
                        Physics.IgnoreCollision(pCol, tCol, ignore);
                    }
                }
            }
        }

        private void ReleaseCarryState()
        {
            string releasedName = (_currentCarryable != null) ? _currentCarryable.name : "Object";

            if (_currentCarryable != null)
            {
                SetLocalCollisionIgnore(_currentCarryable, false);
                if (_currentlyHighlightedCarryable == _currentCarryable)
                {
                    _currentCarryable.SetOutline(false);
                    _currentlyHighlightedCarryable = null;
                }
            }

            _leftHandGripping = false;
            _rightHandGripping = false;
            _currentCarryable = null;
            _assignedSocketIndex = -1;
            _isChargingThrow = false;
            _currentThrowCharge = 0f;
            _currentLocalContactLeft = new Vector3(-0.25f, 0f, -0.4f);
            _currentLocalContactRight = new Vector3(0.25f, 0f, -0.4f);

            if (_movement != null && IsOwner)
            {
                _movement.SpeedMultiplier = 1.0f;
            }

            Debug.Log($"[PlayerCarry] Client {OwnerClientId} released '{releasedName}'. Returning to free PhysX gravity & momentum.");
        }

        private void ExecuteThrow(Vector3 camForward, Vector3 camRight)
        {
            if (_currentCarryable == null)
            {
                _isChargingThrow = false;
                _currentThrowCharge = 0f;
                _leftHandGripping = false;
                _rightHandGripping = false;
                return;
            }

            CarryableObject thrownObject = _currentCarryable;
            float charge = _currentThrowCharge;
            _isChargingThrow = false;
            _currentThrowCharge = 0f;

            // Automatically release hand grips from the item
            _leftHandGripping = false;
            _rightHandGripping = false;
            _requireGrabRelease = true;
            _throwReleaseCooldown = 0.4f;

            // Ballistic trajectory: forward + upward arc
            Vector3 throwDir = (camForward + Vector3.up * _throwUpwardArc).normalized;
            float throwSpeed = Mathf.Lerp(_minThrowSpeed, _maxThrowSpeed, charge);
            Vector3 playerVel = (_movement != null) ? _movement.Velocity : Vector3.zero;
            Vector3 finalVelocity = throwDir * throwSpeed + playerVel * _momentumTransfer;

            // Natural rotational tumble
            Vector3 angularImpulse = camRight * UnityEngine.Random.Range(3f, 6f) + UnityEngine.Random.insideUnitSphere * 1.5f;

            // Consume stamina based on charge
            if (_stamina != null)
            {
                float staminaCost = Mathf.Lerp(8f, _maxThrowStaminaCost, charge);
                _stamina.ConsumeStamina(staminaCost);
            }

            Debug.Log($"[PlayerCarry] Client {OwnerClientId} THROWING '{thrownObject.name}' | Charge: {charge * 100f:F0}% | Speed: {finalVelocity.magnitude:F1} m/s");

            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned);
            if (isNetworked)
            {
                RequestThrowServerRpc(finalVelocity, angularImpulse);
                ReleaseCarryState();
            }
            else
            {
                thrownObject.ThrowObject(OwnerClientId, finalVelocity, angularImpulse);
                ReleaseCarryState();
            }
        }

        public void EnsureVisualHandsCreated()
        {
            if (_leftHand == null)
            {
                GameObject lh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lh.name = "VisualHand_Left";
                lh.transform.SetParent(transform);
                lh.transform.localPosition = _leftHandRest;
                lh.transform.localScale = Vector3.one * 0.2f;
                Destroy(lh.GetComponent<Collider>());
                _leftHand = lh.transform;
            }

            if (_rightHand == null)
            {
                GameObject rh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rh.name = "VisualHand_Right";
                rh.transform.SetParent(transform);
                rh.transform.localPosition = _rightHandRest;
                rh.transform.localScale = Vector3.one * 0.2f;
                Destroy(rh.GetComponent<Collider>());
                _rightHand = rh.transform;
            }
        }

        /// <summary>
        /// Creates the dual markers for Left Hand and Right Hand.
        /// </summary>
        private void EnsureDualMarkersCreated()
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") 
                               ?? Shader.Find("Universal Render Pipeline/Lit") 
                               ?? Shader.Find("Unlit/Color") 
                               ?? Shader.Find("Sprites/Default");

            if (_markerMaterial == null && unlitShader != null)
            {
                _markerMaterial = new Material(unlitShader);
            }

            // 1. Left Hand Marker
            if (_leftMarker == null)
            {
                GameObject lObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                lObj.name = "LeftHandAimMarker";
                lObj.transform.localScale = new Vector3(0.12f, 0.003f, 0.12f);
                Collider col = lObj.GetComponent<Collider>();
                if (col != null) DestroyImmediate(col);

                _leftMarkerRenderer = lObj.GetComponent<Renderer>();
                if (_leftMarkerRenderer != null)
                {
                    _leftMarkerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _leftMarkerRenderer.receiveShadows = false;
                    if (_markerMaterial != null) _leftMarkerRenderer.material = _markerMaterial;
                }
                _leftMarker = lObj.transform;
            }
            else
            {
                _leftMarkerRenderer = _leftMarker.GetComponent<Renderer>();
            }

            // 2. Right Hand Marker
            if (_rightMarker == null)
            {
                GameObject rObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                rObj.name = "RightHandAimMarker";
                rObj.transform.localScale = new Vector3(0.12f, 0.003f, 0.12f);
                Collider col = rObj.GetComponent<Collider>();
                if (col != null) DestroyImmediate(col);

                _rightMarkerRenderer = rObj.GetComponent<Renderer>();
                if (_rightMarkerRenderer != null)
                {
                    _rightMarkerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _rightMarkerRenderer.receiveShadows = false;
                    if (_markerMaterial != null) _rightMarkerRenderer.material = _markerMaterial;
                }
                _rightMarker = rObj.transform;
            }
            else
            {
                _rightMarkerRenderer = _rightMarker.GetComponent<Renderer>();
            }

            SetMarkersColor(_colorLiftableGreen);
            HideMarkers();
        }

        public override void OnDestroy()
        {
            try
            {
                base.OnDestroy();
            }
            catch { }

            if (_leftMarker != null) Destroy(_leftMarker.gameObject);
            if (_rightMarker != null) Destroy(_rightMarker.gameObject);
            if (_persistentMarker != null) Destroy(_persistentMarker.gameObject);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Vector3 chestPos = transform.position + Vector3.up * 1.0f;
            Gizmos.DrawWireSphere(chestPos, _grabContactDistance);

            Gizmos.color = Color.cyan;
            Vector3 fwd = transform.forward;
            Gizmos.DrawRay(chestPos, Quaternion.Euler(0f, 30f, 0f) * fwd * _grabContactDistance);
            Gizmos.DrawRay(chestPos, Quaternion.Euler(0f, -30f, 0f) * fwd * _grabContactDistance);
        }
    }
}
