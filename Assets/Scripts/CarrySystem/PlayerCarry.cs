using System;
using Unity.Netcode;
using UnityEngine;
using CoopGame.Player;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Independent toggle grips anchored to cargo-local surface points.
    /// Update caches input, FixedUpdate resolves grabs and support intent,
    /// and LateUpdate attaches rendered palms after body interpolation.
    /// Physics and validated grip snapshots remain server authoritative.
    /// </summary>
    [RequireComponent(typeof(PlayerInputReader))]
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-20)]
    public class PlayerCarry : NetworkBehaviour
    {
        [Header("Interaction & Facing Settings")]
        [Tooltip("Object search distance. Each grip is also limited by the actual arm reach.")]
        [SerializeField] private float _grabContactDistance = 1.6f;

        [Tooltip("Maximum distance to scan surfaces for the aim reticle")]
        [SerializeField] private float _maxScanDistance = 12.0f;

        [Tooltip("Minimum dot product to allow grabbing (0.0 = 90 degrees frontal cone)")]
        [SerializeField] private float _minFacingDot = 0.0f;

        [Tooltip("Layer mask for raycasting interactable and environmental surfaces")]
        [SerializeField] private LayerMask _scanLayers = ~0;

        [Header("Dynamic Vertical Lift Range (Mouse Up/Down)")]
        [Tooltip("Maximum lift height when looking all the way up (meters above feet)")]
        [SerializeField] private float _maxLiftHeight = 2.2f;

        [Tooltip("Normal carry height when looking straight ahead")]
        [SerializeField] private float _normalLiftHeight = 1.05f;

        [Tooltip("Minimum carry height when looking down into carts/ground")]
        [SerializeField] private float _minLiftHeight = 0.45f;

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
        private CapsuleCollider _bodyCapsule;
        private PlayerCameraController _cameraController;
        private PlayerStamina _stamina;
        private Collider[] _playerColliders;
        private Camera _cachedCamera;
        private Wallclimb _wallClimb;
        private ProceduralPlayerArms _procArms;

        // Marker renderers and materials
        private Renderer _leftMarkerRenderer;
        private Renderer _rightMarkerRenderer;
        private Material _markerMaterial;
        private MaterialPropertyBlock _markerPropBlock;

        // Confirmed or locally pending independent toggle grips.
        private bool _leftHandGripping = false;
        private bool _rightHandGripping = false;
        private CarryableObject _currentCarryable = null;
        private int _assignedSocketIndex = -1;

        // Cached surface contact points from dual markers (World space)
        private Vector3 _lastAimLeftPoint = Vector3.zero;
        private Vector3 _lastAimRightPoint = Vector3.zero;

        // Cached surface contact points in carried object local space
        private Vector3 _currentLocalContactLeft = new Vector3(-0.25f, 0f, -0.35f);
        private Vector3 _currentLocalContactRight = new Vector3(0.25f, 0f, -0.35f);
        private Quaternion _currentLocalRotationLeft = Quaternion.identity;
        private Quaternion _currentLocalRotationRight = Quaternion.identity;
        private CarryableObject _aimedCarryable;
        private Vector3 _lastAimLeftNormal = Vector3.up;
        private Vector3 _lastAimRightNormal = Vector3.up;
        private const float LocalContactSlack = .04f;
        // Allow a small owner-position replication delay at server acceptance.
        private const float ServerContactSlack = .15f;
        private float _currentHoldHeight = 1.05f;
        private bool _grabPending;
        private bool _releasePending;
        private byte _pendingToggleMask;
        private bool _pendingDualToggle;
        private CarryableObject _requestedCargo;
        private byte _requestedHands;
        private CargoGripState _pendingServerGrip;
        private byte _pendingServerGripKind;
        private uint _gripRevision;
        private uint _acceptedGripRevision;
        private uint _grabRequestId;
        private uint _pendingServerRequestId;
        private float _grabRequestedAt;
        private float _attachedAt;
        private float _overreachTime;
        private Collider[] _currentCargoColliders;
        private Rigidbody _currentCargoRigidbody;
        private readonly System.Collections.Generic.List<Collider> _collisionRestores = new(8);
        private bool _collisionIgnorePending;

        private bool IsNetworked => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        private bool HasLocalInput => !IsNetworked || IsOwner;

        // One authoritative snapshot avoids combining a new cargo ID with stale
        // contacts, and includes everything late joiners need to render the grip.
        public struct CargoGripState : INetworkSerializable, IEquatable<CargoGripState>
        {
            public bool Attached;
            public uint Revision;
            public ulong CargoId;
            public int SocketIndex;
            public byte Hands;
            public Vector3 LeftPoint, RightPoint;
            public Quaternion LeftRotation, RightRotation;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Attached);
                serializer.SerializeValue(ref Revision);
                serializer.SerializeValue(ref CargoId);
                serializer.SerializeValue(ref SocketIndex);
                serializer.SerializeValue(ref Hands);
                serializer.SerializeValue(ref LeftPoint);
                serializer.SerializeValue(ref RightPoint);
                serializer.SerializeValue(ref LeftRotation);
                serializer.SerializeValue(ref RightRotation);
            }

            public bool Equals(CargoGripState other) => Attached == other.Attached && Revision == other.Revision && CargoId == other.CargoId &&
                SocketIndex == other.SocketIndex && Hands == other.Hands && LeftPoint.Equals(other.LeftPoint) &&
                RightPoint.Equals(other.RightPoint) && LeftRotation.Equals(other.LeftRotation) && RightRotation.Equals(other.RightRotation);
        }

        // Hand rest offsets (local to player)
        private readonly Vector3 _leftHandRest = new Vector3(-0.35f, 0.5f, 0.1f);
        private readonly Vector3 _rightHandRest = new Vector3(0.35f, 0.5f, 0.1f);

        // Marker Colors (Liftable = Green, Not Liftable = Red)
        private readonly Color _colorLiftableGreen = new Color(0.1f, 1.0f, 0.25f, 0.95f);  // Bright Green
        private readonly Color _colorNotLiftableRed = new Color(1.0f, 0.15f, 0.15f, 0.95f); // Bright Red

        // Diagnostics & Logging Timers
        private bool _wasAimingAtReachable = false;

        // Outline tracking
        private CarryableObject _currentlyHighlightedCarryable = null;

        // Throwing state
        private float _currentThrowCharge = 0.0f;
        private bool _isChargingThrow = false;
        private float _throwReleaseCooldown = 0.0f;
        private bool _requireGrabRelease = false;
        private bool _canAttemptGrab = true;
        private readonly RaycastHit[] _aimHits = new RaycastHit[64];
        private readonly Collider[] _nearbyColliders = new Collider[64];

        // Stable inward-facing pose while carrying. The target is sampled from the
        // replicated cargo transform and applied in FixedUpdate so movement and IK
        // never chase a different heading on render frames.
        private Vector3 _carryFacing = Vector3.forward;
        private Vector3 _carryFacingTarget = Vector3.forward;
        private bool _hasCarryFacing;
        private const float CarryFacingTurnSpeed = 900f;

        // Exposed properties
        public bool IsCarrying => HasLocalInput
            ? (_leftHandGripping || _rightHandGripping)
            : (_netGripState.Value.Attached && (_netGripState.Value.Hands & 1) != 0 ||
                _netGripState.Value.Attached && (_netGripState.Value.Hands & 2) != 0);
        public bool LeftHandGripping => HasLocalInput
            ? _leftHandGripping
            : (_netGripState.Value.Attached && (_netGripState.Value.Hands & 1) != 0);
        public bool RightHandGripping => HasLocalInput
            ? _rightHandGripping
            : (_netGripState.Value.Attached && (_netGripState.Value.Hands & 2) != 0);
        public bool LeftHandReaching => HasLocalInput
            ? (_inputReader != null && (_inputReader.GrabLeftHeld || _inputReader.InteractHeld))
            : ((_netHandState.Value & (1 << 2)) != 0);
        public bool RightHandReaching => HasLocalInput
            ? (_inputReader != null && (_inputReader.GrabRightHeld || _inputReader.InteractHeld))
            : ((_netHandState.Value & (1 << 3)) != 0);
        public CarryableObject CurrentCarryable => _currentCarryable;
        public float CurrentThrowCharge => _currentThrowCharge;
        public bool IsChargingThrow => HasLocalInput
            ? _isChargingThrow
            : ((_netHandState.Value & (1 << 4)) != 0);
        public PlayerStamina Stamina => _stamina;
        public Transform LeftHand => _leftHand;
        public Transform RightHand => _rightHand;

        /// <summary>True when the player has a stable direction toward the carried object.</summary>
        public bool TryGetCarryFacing(out Vector3 facing)
        {
            if (_hasCarryFacing && _currentCarryable != null && IsCarrying)
            {
                facing = _carryFacing;
                return facing.sqrMagnitude > 0.01f;
            }

            facing = Vector3.zero;
            return false;
        }

        // Replicated Hand Gestures & Gripping state for Remote Player Proxies
        // Confirmed gripping hands are stored in _netGripState.
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

        // Atomic, server-authoritative surface attachment for every peer, including late joiners.
        private readonly NetworkVariable<CargoGripState> _netGripState = new NetworkVariable<CargoGripState>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private void Awake()
        {
            _inputReader = GetComponent<PlayerInputReader>();
            _movement = GetComponent<PlayerMovement>();
            _characterController = GetComponent<CharacterController>();
            _bodyCapsule = GetComponent<CapsuleCollider>();
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
            _procArms = GetComponent<ProceduralPlayerArms>();

            EnsureVisualHandsCreated();
            EnsureDualMarkersCreated();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _gripRevision = _netGripState.Value.Revision;
            _acceptedGripRevision = 0;
            _releasePending = false;
            _pendingServerGripKind = 0;
            if (IsServer && _netGripState.Value.Attached) PublishRelease();

            _netGripState.OnValueChanged += OnGripStateChanged;
            ApplyGripState(_netGripState.Value);

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
            _netGripState.OnValueChanged -= OnGripStateChanged;
            if (IsServer && _currentCarryable != null) _currentCarryable.DetachCarrier(OwnerClientId);
            _pendingServerGripKind = 0;
            _grabRequestId++;
            _releasePending = false;
            HideMarkers();
            ClearOutlineHighlight();

            ReleaseCarryState();
            base.OnNetworkDespawn();
        }

        private void OnGripStateChanged(CargoGripState previous, CargoGripState current) => ApplyGripState(current);

        private void ApplyGripState(CargoGripState state)
        {
            if (state.Revision < _acceptedGripRevision) return;
            if (state.Attached && HasLocalInput && _releasePending) return;
            _acceptedGripRevision = state.Revision;
            _grabPending = false;
            if (!state.Attached) { _releasePending = false; ReleaseCarryState(); return; }
            if (_currentCarryable != null && _currentCarryable.NetworkObjectId != state.CargoId) ReleaseCarryState();
            _currentLocalContactLeft = state.LeftPoint;
            _currentLocalContactRight = state.RightPoint;
            _currentLocalRotationLeft = state.LeftRotation;
            _currentLocalRotationRight = state.RightRotation;
            _leftHandGripping = (state.Hands & 1) != 0;
            _rightHandGripping = (state.Hands & 2) != 0;
            _assignedSocketIndex = state.SocketIndex;
            ResolveCarriedObject(state.CargoId);
        }

        private void ResolveCarriedObject(ulong netId)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null &&
                NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(netId, out NetworkObject netObj))
            {
                CarryableObject carryable = netObj.GetComponent<CarryableObject>();
                if (carryable != null)
                {
                    if (_currentCarryable != carryable) OnGrabSuccessful(carryable, _assignedSocketIndex);
                }
            }
        }

        private void OnDisable()
        {
            ClearOutlineHighlight();
            _hasCarryFacing = false;
            _carryFacingTarget = Vector3.forward;
        }

        private void FixedUpdate()
        {
            FlushCollisionChanges();
            if (IsServer && _pendingServerGripKind != 0)
            {
                byte kind = _pendingServerGripKind;
                CargoGripState request = _pendingServerGrip;
                _pendingServerGripKind = 0;
                if (kind == 1) ProcessServerGrab(request);
                else ProcessServerHandChange(request);
            }
            if (HasLocalInput)
            {
                UpdatePersistentAimMarker(out _aimedCarryable, out _canGrabAimed);
                ProcessGrabIntent();
            }
            if (_currentCarryable == null || !IsCarrying) { _hasCarryFacing = false; return; }
            Vector3 toCargo = (_currentCargoRigidbody != null ? _currentCargoRigidbody.position : _currentCarryable.transform.position) - transform.position;
            toCargo.y = 0f;
            if (toCargo.sqrMagnitude > .0025f) _carryFacingTarget = toCargo.normalized;
            if (!_hasCarryFacing)
            {
                _carryFacing = _carryFacingTarget.sqrMagnitude > .01f ? _carryFacingTarget : transform.forward;
                _hasCarryFacing = true;
            }
            else _carryFacing = Vector3.RotateTowards(_carryFacing, _carryFacingTarget,
                CarryFacingTurnSpeed * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f).normalized;
            if (!HasLocalInput) return;
            Vector3 forward = _cameraController != null ? _cameraController.HorizontalForward : transform.forward;
            Vector3 right = _cameraController != null ? _cameraController.HorizontalRight : transform.right;
            Vector2 input = _inputReader != null ? _inputReader.MoveInput : Vector2.zero;
            Vector3 direction = Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
            if (IsNetworked) StreamCarrierInputServerRpc(direction, _carryFacing, _currentHoldHeight);
            else _currentCarryable.UpdateCarrierInput(OwnerClientId, direction, _carryFacing,
                _currentHoldHeight, _leftHandGripping, _rightHandGripping);
            float excess = _leftHandGripping ? GripOverreach(true) : 0f;
            if (_rightHandGripping) excess = Mathf.Max(excess, GripOverreach(false));
            _overreachTime = excess > .2f ? _overreachTime + Time.fixedDeltaTime : 0f;
            if (_overreachTime > .65f && Time.time - _attachedAt > 1.1f) DropForRespawn();
        }

        private void Update()
        {
            if (_netGripState.Value.Attached && _currentCarryable == null && IsNetworked && !_releasePending)
                ResolveCarriedObject(_netGripState.Value.CargoId);
            if (!HasLocalInput || _inputReader == null) return;
            if (_currentCarryable != null && !_currentCarryable.gameObject.activeInHierarchy) DropForRespawn();
            if (_grabPending && Time.unscaledTime - _grabRequestedAt > 3f)
            {
                _grabRequestId++;
                if (IsNetworked) { _releasePending = true; RequestDropServerRpc(); }
                ReleaseCarryState();
            }
            // Menus block new input while held cargo still requires support.
            if (IsCarrying && _currentCarryable != null && _stamina != null)
            {
                _stamina.DrainStaminaContinuous(!(_leftHandGripping && _rightHandGripping), _currentCarryable.TotalMass, _currentCarryable.CurrentCarrierCount);
                if (_stamina.IsExhausted) { DropForRespawn(); return; }
            }
            if (CoopGame.Network.PauseMenu.IsPaused || MissionFailUI.IsVisible || CoopGame.Network.ExpeditionHUD.BlocksGameplayInput) return;
            if (_leftMarker == null || _rightMarker == null) EnsureDualMarkersCreated();
            _currentHoldHeight = CalculateHoldHeight();
            UpdateCarryableOutline(_aimedCarryable, _canGrabAimed);
            _throwReleaseCooldown = Mathf.Max(0f, _throwReleaseCooldown - Time.deltaTime);
            if (!_inputReader.IsGrabbing) _requireGrabRelease = false;
            _canAttemptGrab = !_requireGrabRelease && _throwReleaseCooldown <= 0f && !_grabPending && !_releasePending;
            if (_inputReader.InteractPressed)
            {
                _pendingDualToggle = !_pendingDualToggle;
            }
            else
            {
                bool left = _inputReader.GrabLeftPressed, right = _inputReader.GrabRightPressed;
                if (left) _pendingToggleMask ^= 1;
                if (right) _pendingToggleMask ^= 2;
            }
            if (IsCarrying && _currentCarryable != null)
            {
                if (_movement != null)
                {
                    float speed = _currentCarryable.GetSpeedMultiplier();
                    if (_isChargingThrow) speed *= .75f;
                    if (_stamina != null && _stamina.IsExhausted) speed *= .5f;
                    if (_currentCarryable.CurrentCarrierCount >= 2 && TryGetComponent<CoopGame.Network.PlayerExpeditionState>(out var state) && state.Card.Value == 3) speed *= 1.15f;
                    _movement.SpeedMultiplier = speed;
                }
                if (_inputReader.ThrowHeld)
                {
                    _isChargingThrow = true;
                    _currentThrowCharge = Mathf.Clamp01(_currentThrowCharge + Time.deltaTime / Mathf.Max(.05f, _maxChargeTime));
                }
                else if (_isChargingThrow)
                {
                    Vector3 forward = _cameraController != null ? _cameraController.HorizontalForward : transform.forward;
                    Vector3 right = _cameraController != null ? _cameraController.HorizontalRight : transform.right;
                    ExecuteThrow(forward, right);
                }
            }
            if (IsNetworked)
            {
                byte mask = 0;
                if (LeftHandReaching) mask |= 1 << 2;
                if (RightHandReaching) mask |= 1 << 3;
                if (_isChargingThrow) mask |= 1 << 4;
                if (_netHandState.Value != mask) _netHandState.Value = mask;
                if (Mathf.Abs(_netHoldHeight.Value - _currentHoldHeight) > .015f) _netHoldHeight.Value = _currentHoldHeight;
            }
        }

        private bool _canGrabAimed;

        private void ProcessGrabIntent()
        {
            _canAttemptGrab = !_requireGrabRelease && _throwReleaseCooldown <= 0f && !_grabPending && !_releasePending;
            if (_grabPending)
            {
                byte predictedHands = (byte)((_leftHandGripping ? 1 : 0) | (_rightHandGripping ? 2 : 0));
                if (_pendingDualToggle || (_pendingToggleMask & predictedHands) != 0)
                {
                    _pendingDualToggle = false;
                    _pendingToggleMask = 0;
                    DropForRespawn();
                }
                return;
            }
            byte toggle = _pendingToggleMask;
            bool dual = _pendingDualToggle;
            CarryableObject requested = _requestedCargo;
            byte hands = _requestedHands;
            _pendingToggleMask = 0;
            _pendingDualToggle = false;
            _requestedCargo = null;
            _requestedHands = 0;
            if (_releasePending || _grabPending || CoopGame.Network.PauseMenu.IsPaused || MissionFailUI.IsVisible ||
                CoopGame.Network.ExpeditionHUD.BlocksGameplayInput) return;
            if (requested != null)
            {
                if ((hands & 1) != 0) CaptureHandContact(requested, true);
                if ((hands & 2) != 0) CaptureHandContact(requested, false);
                SendGrabState(requested);
            }
            else if (dual)
            {
                if (_leftHandGripping || _rightHandGripping) DropForRespawn();
                else TryToggleGrab(true, true);
            }
            else if (toggle == 3)
            {
                if (!_leftHandGripping && !_rightHandGripping) TryToggleGrab(true, true);
                else if (_leftHandGripping && _rightHandGripping) DropForRespawn();
                else if (_currentCarryable != null)
                {
                    bool releasingLeft = _leftHandGripping;
                    if (CaptureHandContact(_currentCarryable, !releasingLeft))
                    {
                        if (releasingLeft) _leftHandGripping = false; else _rightHandGripping = false;
                        SyncCarrierHandState();
                    }
                }
            }
            else
            {
                if ((toggle & 1) != 0) ToggleHand(true);
                if ((toggle & 2) != 0) ToggleHand(false);
            }
        }

        private float CalculateHoldHeight()
        {
            if (_cameraController == null) return _normalLiftHeight;
            float pitch = _cameraController.Pitch;
            return pitch < 0f ? Mathf.Lerp(_normalLiftHeight, _maxLiftHeight, Mathf.Clamp01(-pitch / 55f))
                : Mathf.Lerp(_normalLiftHeight, _minLiftHeight, Mathf.Clamp01(pitch / 60f));
        }

        private void ToggleHand(bool left)
        {
            if (_grabPending || _releasePending) return;
            if (left ? _leftHandGripping : _rightHandGripping)
            {
                if (left) _leftHandGripping = false; else _rightHandGripping = false;
                if (!_leftHandGripping && !_rightHandGripping) DropForRespawn();
                else SyncCarrierHandState();
            }
            else TryToggleGrab(left, !left);
        }

        private void TryToggleGrab(bool left, bool right)
        {
            if (!_canAttemptGrab || _grabPending || _releasePending || (_wallClimb != null && _wallClimb.IsClimbing) || (_stamina != null && _stamina.IsExhausted)) return;
            CarryableObject target = _currentCarryable != null ? _currentCarryable : (_canGrabAimed ? _aimedCarryable : null);
            if (target == null)
            {
                target = FindNearbyCarryable();
                if (target == null) return;
            }
            if (left) CaptureHandContact(target, true);
            if (right) CaptureHandContact(target, false);
            SendGrabState(target);
        }

        private void GrabSingleHand(CarryableObject target, bool isLeft)
        {
            if (_grabPending || target == null || (_currentCarryable != null && _currentCarryable != target)) return;
            if (_stamina != null && _stamina.IsExhausted) return;
            if (CaptureHandContact(target, isLeft)) SendGrabState(target);
        }

        private bool CaptureHandContact(CarryableObject target, bool left)
        {
            if (left ? _leftHandGripping : _rightHandGripping) return true;
            GetArmGeometry(left, out Vector3 shoulder, out _);
            Vector3 point = target == _aimedCarryable ? (left ? _lastAimLeftPoint : _lastAimRightPoint) : shoulder;
            Vector3 normal = target == _aimedCarryable ? (left ? _lastAimLeftNormal : _lastAimRightNormal) : shoulder - target.transform.position;
            if (!TryResolveReachableContact(target, left, point, normal, out Vector3 contact, out Vector3 surfaceNormal)) return false;
            Vector3 fingers = Vector3.ProjectOnPlane(Vector3.up, surfaceNormal);
            if (fingers.sqrMagnitude < .001f) fingers = Vector3.ProjectOnPlane(transform.forward, surfaceNormal);
            Quaternion localRot = Quaternion.Inverse(target.transform.rotation) * Quaternion.LookRotation(surfaceNormal, fingers.normalized);
            Vector3 localPoint = target.transform.InverseTransformPoint(contact);
            if (left) { _leftHandGripping = true; _currentLocalContactLeft = localPoint; _currentLocalRotationLeft = localRot; }
            else { _rightHandGripping = true; _currentLocalContactRight = localPoint; _currentLocalRotationRight = localRot; }
            return true;
        }

        private void SendGrabState(CarryableObject target)
        {
            if (!_leftHandGripping && !_rightHandGripping) return;
            if (_currentCarryable != null) { SyncCarrierHandState(); return; }
            if (IsNetworked)
            {
                if (!target.IsSpawned) { ReleaseCarryState(); return; }
                _grabPending = true;
                _grabRequestId++;
                _grabRequestedAt = Time.unscaledTime;
                RequestGrabServerRpc(_grabRequestId, target.NetworkObjectId, _currentLocalContactLeft, _currentLocalContactRight,
                    _currentLocalRotationLeft, _currentLocalRotationRight, _leftHandGripping, _rightHandGripping);
            }
            else if (target.TryAttachCarrier(OwnerClientId, transform, _movement, _currentLocalContactLeft,
                _currentLocalContactRight, _leftHandGripping, _rightHandGripping, out int socketIndex)) OnGrabSuccessful(target, socketIndex);
            else ReleaseCarryState();
        }

        private bool TryResolveReachableContact(CarryableObject target, bool left, Vector3 point, Vector3 normal,
            out Vector3 contact, out Vector3 surfaceNormal)
        {
            GetArmGeometry(left, out Vector3 shoulder, out float reach);
            float maximumDistance = reach + LocalContactSlack;
            float maximumSquared = maximumDistance * maximumDistance;
            if (FindSurfaceContact(target, point, normal, shoulder, out contact, out surfaceNormal) &&
                (contact - shoulder).sqrMagnitude <= maximumSquared) return true;

            // A third-person camera can hit a distant top face while the player
            // stands beside the crate. Acquire the reachable near face instead.
            return FindSurfaceContact(target, shoulder, shoulder - target.transform.position, shoulder,
                out contact, out surfaceNormal) && (contact - shoulder).sqrMagnitude <= maximumSquared;
        }

        private static bool FindSurfaceContact(CarryableObject target, Vector3 point, Vector3 normal,
            Vector3 reference, out Vector3 contact, out Vector3 surfaceNormal)
        {
            contact = point; surfaceNormal = Vector3.up;
            var colliders = target.CollisionColliders;
            if (colliders == null) return false;
            float nearest = float.MaxValue; bool found = false;
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider col = colliders[i];
                if (col == null || !col.enabled || col.isTrigger || !col.gameObject.activeInHierarchy) continue;
                Vector3 outward = normal.sqrMagnitude > .001f ? normal.normalized : (reference - col.bounds.center).normalized;
                if (outward.sqrMagnitude < .001f) outward = Vector3.up;
                float distance = col.bounds.size.magnitude + .2f;
                if (!col.Raycast(new Ray(point + outward * distance, -outward), out RaycastHit hit, distance * 2f))
                {
                    Vector3 toward = point - reference;
                    if (toward.sqrMagnitude < .001f) toward = col.bounds.center - reference;
                    if (!col.Raycast(new Ray(reference, toward.normalized), out hit, toward.magnitude + distance)) continue;
                }
                float score = (hit.point - point).sqrMagnitude;
                if (score >= nearest) continue;
                nearest = score; contact = hit.point; surfaceNormal = hit.normal; found = true;
            }
            return found;
        }

        private void SyncCarrierHandState()
        {
            if (_currentCarryable == null) return;
            if (IsNetworked) SyncHandStateServerRpc(_currentLocalContactLeft, _currentLocalContactRight,
                _currentLocalRotationLeft, _currentLocalRotationRight, _leftHandGripping, _rightHandGripping);
            else _currentCarryable.UpdateCarrierHandState(OwnerClientId, _currentLocalContactLeft,
                _currentLocalContactRight, _leftHandGripping, _rightHandGripping);
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
            int hitCount = Physics.RaycastNonAlloc(aimRay, _aimHits, _maxScanDistance,
                _scanLayers, QueryTriggerInteraction.Ignore);

            RaycastHit validHit = default;
            bool foundValidHit = false;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit h = _aimHits[i];
                // Skip self colliders (avoids hitting player capsule in 3rd person)
                if (h.collider.transform.root == transform.root) continue;
                if (_playerColliders != null && System.Array.IndexOf(_playerColliders, h.collider) >= 0) continue;
                if (_leftMarker != null && (h.collider.transform == _leftMarker || h.collider.transform.IsChildOf(_leftMarker))) continue;
                if (_rightMarker != null && (h.collider.transform == _rightMarker || h.collider.transform.IsChildOf(_rightMarker))) continue;
                if (_persistentMarker != null && (h.collider.transform == _persistentMarker || h.collider.transform.IsChildOf(_persistentMarker))) continue;

                if (h.distance < nearestDistance)
                {
                    nearestDistance = h.distance;
                    validHit = h;
                    foundValidHit = true;
                }
            }

            Vector3 leftAimPoint = Vector3.zero;
            Vector3 rightAimPoint = Vector3.zero;
            Vector3 leftAimNormal = Vector3.up;
            Vector3 rightAimNormal = Vector3.up;
            bool leftReachable = false, rightReachable = false;
            bool reachableHit = false;

            if (foundValidHit)
            {
                CarryableObject carryable = validHit.collider.GetComponentInParent<CarryableObject>();
                if (carryable == null) carryable = validHit.collider.GetComponent<CarryableObject>();

                if (carryable != null && (carryable.CanBeCarried || carryable == _currentCarryable))
                {
                    GetArmGeometry(true, out Vector3 aimShoulderL, out _);
                    GetArmGeometry(false, out Vector3 aimShoulderR, out _);
                    Vector3 playerChest = (aimShoulderL + aimShoulderR) * .5f;
                    float distToHit = Vector3.Distance(playerChest, validHit.point);
                    float distToBounds = Vector3.Distance(playerChest, validHit.collider.bounds.ClosestPoint(playerChest));
                    float effectiveDist = Mathf.Min(distToHit, distToBounds);

                    if (effectiveDist <= _grabContactDistance)
                    {
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

                        leftReachable = TryResolveReachableContact(carryable, true, leftAimPoint, validHit.normal,
                            out leftAimPoint, out leftAimNormal);
                        rightReachable = TryResolveReachableContact(carryable, false, rightAimPoint, validHit.normal,
                            out rightAimPoint, out rightAimNormal);
                        reachableHit = leftReachable || rightReachable;
                        if (reachableHit) aimedCarryable = carryable;
                        canGrabAimed = reachableHit && !isExhausted;
                        _lastAimLeftPoint = leftAimPoint;
                        _lastAimRightPoint = rightAimPoint;
                        _lastAimLeftNormal = leftAimNormal;
                        _lastAimRightNormal = rightAimNormal;

                        if (reachableHit && !_wasAimingAtReachable)
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
                _leftMarker.rotation = Quaternion.FromToRotation(Vector3.up, _currentCarryable.transform.rotation * _currentLocalRotationLeft * Vector3.forward);
            }
            else if (leftReachable)
            {
                _leftMarker.gameObject.SetActive(true);
                _leftMarker.position = leftAimPoint + leftAimNormal * 0.012f;
                if (leftAimNormal.sqrMagnitude > 0.01f)
                {
                    _leftMarker.rotation = Quaternion.FromToRotation(Vector3.up, leftAimNormal);
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
                _rightMarker.rotation = Quaternion.FromToRotation(Vector3.up, _currentCarryable.transform.rotation * _currentLocalRotationRight * Vector3.forward);
            }
            else if (rightReachable)
            {
                _rightMarker.gameObject.SetActive(true);
                _rightMarker.position = rightAimPoint + rightAimNormal * 0.012f;
                if (rightAimNormal.sqrMagnitude > 0.01f)
                {
                    _rightMarker.rotation = Quaternion.FromToRotation(Vector3.up, rightAimNormal);
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

        private CarryableObject FindNearbyCarryable()
        {
            GetArmGeometry(true, out Vector3 nearbyShoulderL, out _);
            GetArmGeometry(false, out Vector3 nearbyShoulderR, out _);
            Vector3 chestPos = (nearbyShoulderL + nearbyShoulderR) * .5f;
            int colliderCount = Physics.OverlapSphereNonAlloc(chestPos, _grabContactDistance,
                _nearbyColliders, _scanLayers, QueryTriggerInteraction.Ignore);

            CarryableObject closestCarryable = null;
            float closestDistanceSqr = float.MaxValue;
            Vector3 viewForward = (_cameraController != null) ? _cameraController.HorizontalForward : transform.forward;

            for (int i = 0; i < colliderCount; i++)
            {
                Collider col = _nearbyColliders[i];
                if (col == null) continue;
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

            return closestCarryable;
        }

        // External callers queue intent; collider queries execute in FixedUpdate.
        public void InitiateGrab(CarryableObject target) => InitiateGrab(target, true, false);
        public void InitiateGrab(CarryableObject target, bool leftActive, bool rightActive)
        {
            if (!HasLocalInput || target == null || _releasePending || _grabPending ||
                (_currentCarryable != null && _currentCarryable != target)) return;
            _requestedCargo = target;
            _requestedHands |= (byte)((leftActive ? 1 : 0) | (rightActive ? 2 : 0));
        }
        public void TryGrabNearbyObject() => TryGrabNearbyObject(true, false);
        public void TryGrabNearbyObject(bool leftActive, bool rightActive)
        {
            if (!HasLocalInput) return;
            _pendingToggleMask |= (byte)((leftActive ? 1 : 0) | (rightActive ? 2 : 0));
        }

        private void LateUpdate()
        {
            if (_procArms != null && _currentCarryable != null && IsCarrying) return;
            if (HasLocalInput) UpdateVisualHands(_currentHoldHeight);
            else UpdateVisualHandsProxy();
        }

        // Called again by the arm solver after interpolated body motion, so both
        // wrists consume the cargo pose rendered in this exact frame.
        public void RefreshCarryHandTargets()
        {
            if (_currentCarryable == null || !IsCarrying) return;
            if (_wallClimb != null && _wallClimb.IsClimbing) return;
            Transform cargo = _currentCarryable.transform;
            if (LeftHandGripping)
                ApplyHandTarget(true, cargo.TransformPoint(_currentLocalContactLeft), cargo.rotation * _currentLocalRotationLeft);
            else RelaxFreeHand(true);
            if (RightHandGripping)
                ApplyHandTarget(false, cargo.TransformPoint(_currentLocalContactRight), cargo.rotation * _currentLocalRotationRight);
            else RelaxFreeHand(false);
        }

        private void ApplyHandTarget(bool left, Vector3 position, Quaternion rotation)
        {
            Transform hand = left ? _leftHand : _rightHand;
            if (hand != null) hand.SetPositionAndRotation(position, rotation);
            if (_procArms != null)
            {
                if (left) _procArms.SetLeftHandTarget(position, rotation, 1f, true);
                else _procArms.SetRightHandTarget(position, rotation, 1f, true);
            }
            Transform marker = left ? _leftMarker : _rightMarker;
            if (HasLocalInput && marker != null)
            {
                Vector3 normal = rotation * Vector3.forward;
                marker.gameObject.SetActive(true);
                marker.SetPositionAndRotation(position + normal * .012f, Quaternion.FromToRotation(Vector3.up, normal));
            }
        }

        private void RelaxFreeHand(bool left)
        {
            Transform hand = left ? _leftHand : _rightHand;
            if (hand == null) return;
            bool reaching = left ? LeftHandReaching : RightHandReaching;
            Vector3 rest = _procArms != null ? (left ? ProceduralPlayerArms.LeftHandRestLocal : ProceduralPlayerArms.RightHandRestLocal)
                : (left ? _leftHandRest : _rightHandRest);
            Vector3 local = reaching ? new Vector3(left ? -.25f : .25f, HasLocalInput ? _currentHoldHeight : _netHoldHeight.Value, .5f) : rest;
            hand.localPosition = Vector3.Lerp(hand.localPosition, local, 1f - Mathf.Exp(-18f * Time.deltaTime));
            if (_procArms == null) return;
            if (reaching)
            {
                if (left) _procArms.SetLeftHandTarget(hand.position, Quaternion.identity, .9f, false);
                else _procArms.SetRightHandTarget(hand.position, Quaternion.identity, .9f, false);
            }
            else if (left) _procArms.LeftWeight = Mathf.MoveTowards(_procArms.LeftWeight, 0f, Time.deltaTime * 10f);
            else _procArms.RightWeight = Mathf.MoveTowards(_procArms.RightWeight, 0f, Time.deltaTime * 10f);
        }

        private void UpdateVisualHands(float currentHeight)
        {
            if (_currentCarryable != null && IsCarrying) { RefreshCarryHandTargets(); return; }
            if (_wallClimb != null) return;
            RelaxFreeHand(true);
            RelaxFreeHand(false);
        }

        private void GetArmGeometry(bool left, out Vector3 shoulder, out float reach)
        {
            if (_procArms != null && _procArms.TryGetCarryPhysicsGeometry(left, out shoulder, out reach)) return;
            shoulder = transform.TransformPoint(new Vector3(left ? -.18f : .18f, 1.25f, 0f));
            reach = .5f;
        }

        public bool TryGetCarrySupportTargets(float height, Vector3 heading, out Vector3 left, out Vector3 right)
        {
            GetArmGeometry(true, out Vector3 shoulderL, out float reachL);
            GetArmGeometry(false, out Vector3 shoulderR, out float reachR);
            heading.y = 0f;
            heading = heading.sqrMagnitude > .001f ? heading.normalized : transform.forward;
            float footY = _characterController != null
                ? transform.TransformPoint(_characterController.center - Vector3.up * (_characterController.height * .5f)).y
                : transform.position.y;
            GetBodyClearance(out Vector3 bodyCenter, out float bodyRadius);
            bool leftValid = TryBuildSupportTarget(shoulderL, reachL, heading, footY + height, bodyCenter, bodyRadius, out left);
            bool rightValid = TryBuildSupportTarget(shoulderR, reachR, heading, footY + height, bodyCenter, bodyRadius, out right);
            return leftValid && rightValid;
        }

        private void GetBodyClearance(out Vector3 center, out float radius)
        {
            Vector3 scale = transform.lossyScale;
            float horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            center = _characterController != null ? transform.TransformPoint(_characterController.center) : transform.position;
            radius = _characterController != null
                ? (_characterController.radius + _characterController.skinWidth) * horizontalScale + .015f
                : .35f;
        }

        private static bool TryBuildSupportTarget(Vector3 shoulder, float reach, Vector3 heading, float desiredY,
            Vector3 bodyCenter, float bodyRadius, out Vector3 target)
        {
            Vector3 offset = shoulder - bodyCenter;
            offset.y = 0f;
            float alongHeading = Vector3.Dot(offset, heading);
            float lateralSquared = Mathf.Max(0f, offset.sqrMagnitude - alongHeading * alongHeading);
            float minimumForward = Mathf.Max(.05f,
                Mathf.Sqrt(Mathf.Max(0f, bodyRadius * bodyRadius - lateralSquared)) - alongHeading);
            float maximumReach = reach * .94f;
            target = shoulder + heading * minimumForward;
            if (minimumForward > maximumReach) return false;

            float forwardDistance = Mathf.Clamp(reach * .65f, minimumForward, maximumReach);
            float verticalReach = Mathf.Sqrt(Mathf.Max(0f, maximumReach * maximumReach - forwardDistance * forwardDistance));
            target = shoulder + heading * forwardDistance;
            // A low requested height must not shorten forward clearance and pull
            // the contact into the torso. Clamp height within the remaining reach.
            target.y = Mathf.Clamp(desiredY, shoulder.y - verticalReach, shoulder.y + verticalReach);
            return true;
        }

        private float GripOverreach(bool left)
        {
            GetArmGeometry(left, out Vector3 shoulder, out float reach);
            Vector3 contact = GetPhysicalContact(left);
            return Vector3.Distance(shoulder, contact) - reach;
        }

        /// <summary>FixedUpdate movement constraint; wall collisions are still resolved by CharacterController.Move.</summary>
        public Vector3 ConstrainCarryDisplacement(Vector3 displacement)
        {
            if (_currentCarryable == null || !IsCarrying || !HasLocalInput) return displacement;
            Vector3 original = displacement;
            for (int pass = 0; pass < 3; pass++)
            {
                if (_leftHandGripping) ConstrainHand(true, ref displacement);
                if (_rightHandGripping) ConstrainHand(false, ref displacement);
                ConstrainBodyOverlap(ref displacement);
            }
            Vector3 correction = displacement - original;
            correction.y = 0f;
            float requestedHorizontal = new Vector2(original.x, original.z).magnitude;
            correction = Vector3.ClampMagnitude(correction, requestedHorizontal + 6f * Time.fixedDeltaTime);
            return original + correction;
        }

        private void ConstrainHand(bool left, ref Vector3 displacement)
        {
            GetArmGeometry(left, out Vector3 shoulder, out float reach);
            Vector3 anchor = GetPhysicalContact(left);
            Vector3 proposed = shoulder + displacement;
            float vertical = proposed.y - anchor.y;
            float horizontalReach = Mathf.Sqrt(Mathf.Max(.0025f, reach * reach * .96f - vertical * vertical));
            Vector3 delta = proposed - anchor;
            delta.y = 0f;
            if (delta.sqrMagnitude > horizontalReach * horizontalReach)
                displacement += Vector3.ClampMagnitude(delta, horizontalReach) - delta;
        }

        private void ConstrainBodyOverlap(ref Vector3 displacement)
        {
            if (_bodyCapsule == null || !_bodyCapsule.enabled || _currentCargoColliders == null) return;
            Transform cargo = _currentCarryable.transform;
            Quaternion poseCorrection = _currentCargoRigidbody != null
                ? _currentCargoRigidbody.rotation * Quaternion.Inverse(cargo.rotation) : Quaternion.identity;
            Vector3 cargoPosition = _currentCargoRigidbody != null ? _currentCargoRigidbody.position : cargo.position;
            for (int i = 0; i < _currentCargoColliders.Length; i++)
            {
                Collider collider = _currentCargoColliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                Vector3 colliderPosition = cargoPosition + poseCorrection * (collider.transform.position - cargo.position);
                Quaternion colliderRotation = poseCorrection * collider.transform.rotation;
                if (!Physics.ComputePenetration(_bodyCapsule, transform.position + displacement, transform.rotation,
                    collider, colliderPosition, colliderRotation, out Vector3 direction, out float depth)) continue;

                // Use the finite collider volume, including top grips and rotated
                // crates. CharacterController.Move resolves floors and overhead hits.
                Vector3 horizontal = new Vector3(direction.x, 0f, direction.z);
                float horizontalLength = horizontal.magnitude;
                if (horizontalLength > .15f)
                    displacement += horizontal / horizontalLength * ((depth + .01f) / horizontalLength);
            }
        }

        /// <summary>
        /// Animates procedural hands for remote proxy players across the network.
        /// Uses replicated cargo-local contacts for remote palms and free-hand reaches.
        /// </summary>
        private void UpdateVisualHandsProxy()
        {
            if (_currentCarryable != null && IsCarrying) { RefreshCarryHandTargets(); return; }
            if (_wallClimb != null && _wallClimb.IsClimbing) return;
            // Wallclimb owns its own reaching pose. Do not overwrite it.
            if (_wallClimb != null) return;
            RelaxFreeHand(true);
            RelaxFreeHand(false);
        }

        private Vector3 GetPhysicalContact(bool left)
        {
            Vector3 local = left ? _currentLocalContactLeft : _currentLocalContactRight;
            if (_currentCargoRigidbody == null) return _currentCarryable.transform.TransformPoint(local);
            return _currentCargoRigidbody.position + _currentCargoRigidbody.rotation * Vector3.Scale(local, _currentCarryable.transform.lossyScale);
        }

        private static bool Finite(Quaternion q) => float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w) &&
            q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > .0001f;

        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        #region Server RPCs

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestGrabServerRpc(uint requestId, ulong id, Vector3 left, Vector3 right, Quaternion leftRot, Quaternion rightRot, bool leftActive, bool rightActive)
        {
            if (_pendingServerGripKind != 0) { NotifyGrabResultClientRpc(requestId, false, default); return; }
            _pendingServerRequestId = requestId;
            _pendingServerGrip = new CargoGripState { CargoId = id, LeftPoint = left, RightPoint = right,
                LeftRotation = leftRot, RightRotation = rightRot, Hands = (byte)((leftActive ? 1 : 0) | (rightActive ? 2 : 0)) };
            _pendingServerGripKind = 1;
        }

        private void ProcessServerGrab(CargoGripState request)
        {
            bool leftActive = (request.Hands & 1) != 0, rightActive = (request.Hands & 2) != 0;
            Vector3 left = request.LeftPoint, right = request.RightPoint;
            Quaternion leftRot = request.LeftRotation, rightRot = request.RightRotation;
            if (_currentCarryable != null || (!leftActive && !rightActive) ||
                !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(request.CargoId, out NetworkObject obj))
            { NotifyGrabResultClientRpc(_pendingServerRequestId, false, default); return; }
            CarryableObject cargo = obj.GetComponent<CarryableObject>();
            if (cargo == null || !cargo.CanBeCarried || (_stamina != null && _stamina.IsExhausted) ||
                !ValidateContact(cargo, leftActive, true, ref left, ref leftRot) || !ValidateContact(cargo, rightActive, false, ref right, ref rightRot) ||
                !cargo.TryAttachCarrier(OwnerClientId, transform, _movement, left, right, leftActive, rightActive, out int socket))
            { NotifyGrabResultClientRpc(_pendingServerRequestId, false, default); return; }
            PublishGrip(cargo, socket, left, right, leftRot, rightRot, leftActive, rightActive);
            NotifyGrabResultClientRpc(_pendingServerRequestId, true, _netGripState.Value);
        }

        private bool ValidateContact(CarryableObject cargo, bool active, bool left, ref Vector3 local, ref Quaternion rotation)
        {
            if (!active) { local = Vector3.zero; rotation = Quaternion.identity; return true; }
            if (!Finite(local) || !Finite(rotation)) return false;
            Vector3 point = cargo.transform.TransformPoint(local);
            GetArmGeometry(left, out Vector3 shoulder, out float reach);
            float maximumDistance = reach + ServerContactSlack;
            if ((point - shoulder).sqrMagnitude > maximumDistance * maximumDistance) return false;
            Quaternion worldRot = cargo.transform.rotation * rotation.normalized;
            if (!FindSurfaceContact(cargo, point, worldRot * Vector3.forward, shoulder, out Vector3 surface, out Vector3 normal) ||
                Vector3.Distance(point, surface) > .15f ||
                (surface - shoulder).sqrMagnitude > maximumDistance * maximumDistance) return false;
            Vector3 fingers = Vector3.ProjectOnPlane(worldRot * Vector3.up, normal);
            if (fingers.sqrMagnitude < .001f) fingers = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (fingers.sqrMagnitude < .001f) fingers = Vector3.ProjectOnPlane(Vector3.forward, normal);
            local = cargo.transform.InverseTransformPoint(surface);
            rotation = Quaternion.Inverse(cargo.transform.rotation) * Quaternion.LookRotation(normal, fingers.normalized);
            return true;
        }

        private void PublishGrip(CarryableObject cargo, int socket, Vector3 left, Vector3 right, Quaternion leftRot, Quaternion rightRot, bool leftActive, bool rightActive)
        {
            _netGripState.Value = new CargoGripState
            {
                Attached = true, Revision = ++_gripRevision, CargoId = cargo.NetworkObjectId, SocketIndex = socket,
                Hands = (byte)((leftActive ? 1 : 0) | (rightActive ? 2 : 0)),
                LeftPoint = left, RightPoint = right, LeftRotation = leftRot, RightRotation = rightRot
            };
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SyncHandStateServerRpc(Vector3 left, Vector3 right, Quaternion leftRot, Quaternion rightRot, bool leftActive, bool rightActive)
        {
            _pendingServerGrip = new CargoGripState { LeftPoint = left, RightPoint = right,
                LeftRotation = leftRot, RightRotation = rightRot, Hands = (byte)((leftActive ? 1 : 0) | (rightActive ? 2 : 0)) };
            _pendingServerGripKind = 2;
        }

        private void ProcessServerHandChange(CargoGripState request)
        {
            if (_currentCarryable == null || !_currentCarryable.HasCarrier(OwnerClientId)) return;
            bool leftActive = (request.Hands & 1) != 0, rightActive = (request.Hands & 2) != 0;
            if (!leftActive && !rightActive) { RequestDropServerRpc(); return; }
            Vector3 left = request.LeftPoint, right = request.RightPoint;
            Quaternion leftRot = request.LeftRotation, rightRot = request.RightRotation;
            CargoGripState previous = _netGripState.Value;
            if (leftActive && (previous.Hands & 1) != 0) { left = previous.LeftPoint; leftRot = previous.LeftRotation; }
            else if (!ValidateContact(_currentCarryable, leftActive, true, ref left, ref leftRot)) { RestoreGripClientRpc(previous); return; }
            if (rightActive && (previous.Hands & 2) != 0) { right = previous.RightPoint; rightRot = previous.RightRotation; }
            else if (!ValidateContact(_currentCarryable, rightActive, false, ref right, ref rightRot)) { RestoreGripClientRpc(previous); return; }
            _currentCarryable.UpdateCarrierHandState(OwnerClientId, left, right, leftActive, rightActive);
            PublishGrip(_currentCarryable, previous.SocketIndex, left, right, leftRot, rightRot, leftActive, rightActive);
        }

        [Rpc(SendTo.Owner)]
        private void RestoreGripClientRpc(CargoGripState state) => ApplyGripState(state);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestDropServerRpc()
        {
            _pendingServerGripKind = 0;
            if (_currentCarryable != null) _currentCarryable.DetachCarrier(OwnerClientId);
            PublishRelease();
            ReleaseCarryState();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestThrowServerRpc(Vector3 linearVelocity, Vector3 angularVelocity)
        {
            if (_currentCarryable == null || !Finite(linearVelocity) || !Finite(angularVelocity) || !_currentCarryable.HasCarrier(OwnerClientId)) return;
            PlayThrowClientRpc(_currentCarryable.transform.position);
            _currentCarryable.ThrowObject(OwnerClientId, Vector3.ClampMagnitude(linearVelocity, 25f), Vector3.ClampMagnitude(angularVelocity, 12f));
            PublishRelease();
            ReleaseCarryState();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        private void StreamCarrierInputServerRpc(Vector3 direction, Vector3 heading, float height)
        {
            if (_currentCarryable == null || !Finite(direction) || !Finite(heading) || !float.IsFinite(height)) return;
            _currentCarryable.UpdateCarrierInput(OwnerClientId, Vector3.ClampMagnitude(direction, 1f), Vector3.ClampMagnitude(heading, 1f),
                Mathf.Clamp(height, .3f, 2.5f), LeftHandGripping, RightHandGripping);
        }

        #endregion

        #region Client RPCs

        [Rpc(SendTo.ClientsAndHost)]
        private void PlayThrowClientRpc(Vector3 position)
        {
            CoopGame.Network.GameplayFeedback.Play(CoopGame.Network.GameplayFeedback.Cue.Throw, position);
        }

        [Rpc(SendTo.Owner)]
        private void NotifyGrabResultClientRpc(uint requestId, bool success, CargoGripState state)
        {
            if (!IsOwner || requestId != _grabRequestId) return;
            _grabPending = false;
            if (success) ApplyGripState(state);
            else ReleaseCarryState();
        }

        private void PublishRelease()
        {
            _netGripState.Value = new CargoGripState { Revision = ++_gripRevision };
        }

        #endregion

        private void OnGrabSuccessful(CarryableObject carryable, int socketIndex)
        {
            _currentCarryable = carryable;
            CoopGame.Network.GameplayFeedback.Play(CoopGame.Network.GameplayFeedback.Cue.Lift, carryable.transform.position);
            _assignedSocketIndex = socketIndex;
            _attachedAt = Time.time;
            _overreachTime = 0f;
            _currentCargoColliders = carryable.GetComponentsInChildren<Collider>(true);
            _currentCargoRigidbody = carryable.GetComponent<Rigidbody>();

            if (_movement != null)
            {
                _movement.SpeedMultiplier = carryable.GetSpeedMultiplier();
            }

            // The torso remains solid; only auxiliary hand colliders are ignored.
            SetLocalCollisionIgnore(_currentCarryable, true);

            Debug.Log($"[PlayerCarry] Client {OwnerClientId} attached to '{_currentCarryable.name}' (Socket #{socketIndex}) | Gravity: ACTIVE");
        }

        private void SetLocalCollisionIgnore(CarryableObject target, bool ignore)
        {
            if (target == null) return;
            if (ignore) { _collisionIgnorePending = true; return; }
            if (_currentCargoColliders != null)
                for (int i = 0; i < _currentCargoColliders.Length; i++)
                    if (_currentCargoColliders[i] != null) _collisionRestores.Add(_currentCargoColliders[i]);
            _currentCargoColliders = null;
            _collisionIgnorePending = false;
        }

        private void FlushCollisionChanges()
        {
            for (int i = 0; i < _collisionRestores.Count; i++)
                SetCollisionPair(_collisionRestores[i], false);
            _collisionRestores.Clear();
            if (!_collisionIgnorePending || _currentCargoColliders == null) return;
            _collisionIgnorePending = false;
            for (int i = 0; i < _currentCargoColliders.Length; i++)
                SetCollisionPair(_currentCargoColliders[i], true);
        }

        private void SetCollisionPair(Collider cargoCollider, bool ignore)
        {
            if (cargoCollider == null || _playerColliders == null) return;
            for (int i = 0; i < _playerColliders.Length; i++)
            {
                Collider playerCollider = _playerColliders[i];
                if (playerCollider != null && !playerCollider.isTrigger && !cargoCollider.isTrigger)
                    Physics.IgnoreCollision(playerCollider, cargoCollider,
                        ignore && CarryableObject.ShouldIgnoreCarryCollision(playerCollider));
            }
        }

        private void ReleaseCarryState()
        {
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
            _currentCargoRigidbody = null;
            _hasCarryFacing = false;
            _carryFacingTarget = Vector3.forward;
            _assignedSocketIndex = -1;
            _isChargingThrow = false;
            _currentThrowCharge = 0f;
            _grabPending = false;
            _overreachTime = 0f;
            _pendingToggleMask = 0;
            _pendingDualToggle = false;
            _requestedCargo = null;
            _requestedHands = 0;
            _currentLocalContactLeft = new Vector3(-0.25f, 0f, -0.35f);
            _currentLocalContactRight = new Vector3(0.25f, 0f, -0.35f);

            if (_movement != null && HasLocalInput)
            {
                _movement.SpeedMultiplier = 1.0f;
            }

        }

        public void DropForRespawn()
        {
            if (!HasLocalInput || _releasePending || (_currentCarryable == null && !_grabPending)) return;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
            {
                _grabRequestId++;
                _releasePending = true;
                RequestDropServerRpc();
            }
            else
                _currentCarryable?.DetachCarrier(OwnerClientId);
            ReleaseCarryState();
        }

        public void ForcedDropFromCargo(CarryableObject cargo)
        {
            bool isNetworked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if ((isNetworked && !IsServer) || _currentCarryable != cargo) return;
            if (IsSpawned && IsServer) PublishRelease();
            ReleaseCarryState();
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
                _releasePending = true;
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
            if (_procArms == null) _procArms = GetComponent<ProceduralPlayerArms>();
            if (_procArms != null)
            {
                _procArms.EnsureTargetNodesCreated();
                _leftHand = _procArms.LeftHand;
                _rightHand = _procArms.RightHand;
                return;
            }

            if (_leftHand == null)
            {
                Transform existing = transform.Find("IKTarget_Left") ?? transform.Find("VisualHand_Left");
                if (existing != null)
                {
                    _leftHand = existing;
                }
                else
                {
                    GameObject lh = new GameObject("IKTarget_Left");
                    lh.transform.SetParent(transform, false);
                    lh.transform.localPosition = _leftHandRest;
                    _leftHand = lh.transform;
                }
            }

            if (_rightHand == null)
            {
                Transform existing = transform.Find("IKTarget_Right") ?? transform.Find("VisualHand_Right");
                if (existing != null)
                {
                    _rightHand = existing;
                }
                else
                {
                    GameObject rh = new GameObject("IKTarget_Right");
                    rh.transform.SetParent(transform, false);
                    rh.transform.localPosition = _rightHandRest;
                    _rightHand = rh.transform;
                }
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
                lObj.transform.SetParent(transform, false);
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
                rObj.transform.SetParent(transform, false);
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
