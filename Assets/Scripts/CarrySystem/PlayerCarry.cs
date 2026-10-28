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
        [SerializeField] private float _normalLiftHeight = 1.3f;

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
        private CoopGame.Network.PlayerExpeditionState _expedition;
        /// <summary>Launch speed (m/s, before body momentum) of the last throw; for tuning/verification.</summary>
        public float LastThrowSpeed { get; private set; }
        /// <summary>Raised on the owner when a throw leaves the hands (animation hook).</summary>
        public event System.Action Thrown;
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
        // Two-hand pickup grip: how low on the cargo (0 bottom, 1 top), how far apart on a wide face,
        // and the widest cargo whose side faces both palms can reach.
        private const float GripLowFraction = .3f;
        private const float MaxGripSpread = .34f;
        private const float SideGripMaxHalfWidth = .36f;
        private const float SideGripInset = .07f;
        private const float PickupLeanAllowance = .4f;
        // Allow a small owner-position replication delay at server acceptance.
        private const float ServerContactSlack = .15f;
        private float _currentHoldHeight = 1.05f;
        private int _liftSteps;
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
        // A client's cargo is a kinematic replica rendered one network delay behind the server's.
        private Unity.Netcode.Components.NetworkTransform _cargoNetworkTransform;
        private Vector3 _cargoReplicaVelocity;
        private Vector3 _cargoReplicaLastPosition;
        private float _cargoReplicaMovedAt;
        private float _cargoReplicaSampledAt = -1f;
        private float _cargoReplicaYawRate; // deg/s, measured on the replica
        private Quaternion _cargoReplicaLastRotation = Quaternion.identity;
        private float _cargoReplicaRotatedAt;
        private Vector3 _lastStreamPosition;
        private bool _streamPositionValid;
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
        // Slower than free walking so a held load swings round with some weight.
        private const float SoloCarryTurnSpeed = 140f;
        // 1/s: how quickly the carry facing closes the remaining angle (about 90% in 0.25 s).
        private const float CarryTurnSmoothing = 9f;

        /// <summary>The vector with its height removed (not normalised, unlike <see cref="Flat"/>).</summary>
        private static Vector3 Horizontal(Vector3 v) => new(v.x, 0f, v.z);

        /// <summary>Flat unit direction; forward when the flat part is ~zero.</summary>
        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > .0001f ? v.normalized : Vector3.forward;
        }

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
        // Left click is the two-handed lift, so it raises both arms; E (interact) reaches with the right hand only.
        public bool LeftHandReaching => HasLocalInput
            ? (_inputReader != null && _inputReader.GrabLeftHeld)
            : ((_netHandState.Value & (1 << 2)) != 0);
        public bool RightHandReaching => HasLocalInput
            ? (_inputReader != null && (_inputReader.GrabLeftHeld || _inputReader.GrabRightHeld || _inputReader.InteractHeld))
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

        /// <summary>Where a carrier stands relative to the cargo, in the cargo's yaw frame, as its owner
        /// simulates it. Replicated presentation-only data: physics and validation never read it.</summary>
        public struct CarryBodyPose : INetworkSerializable, IEquatable<CarryBodyPose>
        {
            public bool Valid;
            public ulong CargoId;
            public Vector2 Offset;
            public float Yaw;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Valid);
                serializer.SerializeValue(ref CargoId);
                serializer.SerializeValue(ref Offset);
                serializer.SerializeValue(ref Yaw);
            }

            public bool Equals(CarryBodyPose other) => Valid == other.Valid && CargoId == other.CargoId &&
                Offset.Equals(other.Offset) && Yaw.Equals(other.Yaw);
        }

        // Every peer draws its own interpolated copy of the carrier and of the cargo, and the two copies are
        // sampled at different times (owner vs server transforms, different send phases, interpolated vs
        // raw poses). Drawing a remote carrier relative to the cargo as drawn here keeps its hands on it.
        private readonly NetworkVariable<CarryBodyPose> _netCarryBodyPose = new NetworkVariable<CarryBodyPose>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );
        private const float CarryPoseSendDistance = .003f;
        private const float CarryPoseSendYaw = .3f;
        private const float RemoteCarryPoseBlendSeconds = .2f;
        // Larger corrections mean a teleport or respawn, not replication timing; keep the plain proxy then.
        private const float MaxRemoteCarryRenderCorrection = 2.5f;
        private float _remoteCarryPoseWeight;
        private Vector3 _remoteCarryCorrection;
        private float _remoteCarryYawCorrection;

        private void Awake()
        {
            _inputReader = GetComponent<PlayerInputReader>();
            _movement = GetComponent<PlayerMovement>();
            _characterController = GetComponent<CharacterController>();
            _bodyCapsule = GetComponent<CapsuleCollider>();
            _cameraController = GetComponent<PlayerCameraController>();
            _stamina = GetComponent<PlayerStamina>();
            _expedition = GetComponent<CoopGame.Network.PlayerExpeditionState>();
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
            // NetworkPlayer can attach the procedural rig after this component's
            // Awake. Bind it on every peer, including remote player proxies.
            EnsureVisualHandsCreated();
            // NetworkPlayer may have created the visual rig after our Awake.
            _playerColliders = GetComponentsInChildren<Collider>(true);
            _gripRevision = _netGripState.Value.Revision;
            _acceptedGripRevision = 0;
            _releasePending = false;
            _pendingServerGripKind = 0;
            if (IsServer && _netGripState.Value.Attached) PublishRelease();

            _netGripState.OnValueChanged += OnGripStateChanged;
            ApplyGripState(_netGripState.Value);

            if (IsOwner)
            {
                s_localCarrier = this;
                if (!IsServer && GetComponent<CargoDrawShifter>() == null) gameObject.AddComponent<CargoDrawShifter>();
                if (GetComponent<PlayerStaminaUI>() == null)
                {
                    gameObject.AddComponent<PlayerStaminaUI>();
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            _netGripState.OnValueChanged -= OnGripStateChanged;
            if (s_localCarrier == this) s_localCarrier = null;
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

        // A co-op client's cargo replica is drawn one network delay behind the server, so the carrier's own body
        // looks sunk into it while walking towards it. While this player carries, CargoDrawShifter draws the cargo
        // where the server has it now (the same prediction the movement limit uses).
        private const float CargoRenderShiftLimit = .6f;

        private static PlayerCarry s_localCarrier;

        /// <summary>Extra offset to add to points on <paramref name="cargo"/> read from its transform this frame. Zero
        /// once <see cref="CargoDrawShifter"/> has moved the transform itself; before that (or with no shifter) the
        /// shift a client carrier's drawing uses, so a team-mate's hands and body stay on the drawn crate.</summary>
        public static Vector3 CargoDrawShift(CarryableObject cargo) =>
            !CargoDrawShifter.Applied && s_localCarrier != null && cargo != null && s_localCarrier._currentCarryable == cargo
                ? s_localCarrier.ClientCargoLagOffset() : Vector3.zero;

        /// <summary>Client carrier only: how far the server's cargo is ahead of its replica (flat, clamped).</summary>
        internal Vector3 ClientCargoLagOffset()
        {
            if (!IsNetworked || !IsOwner || IsServer || _currentCargoRigidbody == null || !IsCarrying) return Vector3.zero;
            return Vector3.ClampMagnitude(Horizontal(PredictedServerCargoPosition() - _currentCargoRigidbody.position), CargoRenderShiftLimit);
        }

        private bool _placeIntent, _serverPlaceIntent;
        private float _placeFeedbackUntil;
        public string PlacementFeedback => Time.unscaledTime < _placeFeedbackUntil ? "ตรงนี้ยังวางไม่ได้" : null;

        private void FixedUpdate()
        {
            if (_placeIntent)
            {
                _placeIntent = false;
                if (IsNetworked) RequestPlaceServerRpc();
                else _serverPlaceIntent = true;
            }
            if (_serverPlaceIntent && (IsServer || !IsNetworked))
            {
                _serverPlaceIntent = false;
                bool success = _currentCarryable != null && _currentCarryable.TryPlaceGently(OwnerClientId);
                if (success) { PublishRelease(); ReleaseCarryState(); }
                if (IsNetworked) PlaceResultClientRpc(success);
                else if (!success) _placeFeedbackUntil = Time.unscaledTime + 2f;
            }
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
            // A newly accepted/predicted grip must suppress body contacts before
            // this physics step, rather than waiting for the next FixedUpdate.
            FlushCollisionChanges();
            if (_currentCarryable == null || !IsCarrying) { _hasCarryFacing = false; PublishCarryBodyPose(false); return; }
            Vector3 direction = Vector3.zero;
            if (HasLocalInput)
            {
                Vector3 forward = _cameraController != null ? _cameraController.HorizontalForward : transform.forward;
                Vector3 right = _cameraController != null ? _cameraController.HorizontalRight : transform.right;
                Vector2 input = _inputReader != null ? _inputReader.MoveInput : Vector2.zero;
                direction = Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
            }
            // A solo carrier faces the camera's heading while walking: A/D strafe and S backs up with the cargo
            // held still in front, only turning the camera turns the cargo. Co-op carriers keep facing the shared
            // cargo from their own side.
            bool soloSteering = HasLocalInput && _currentCarryable.CurrentCarrierCount <= 1;
            float turnSpeed = CarryFacingTurnSpeed;
            if (soloSteering)
            {
                Vector3 cameraHeading = _cameraController != null ? _cameraController.HorizontalForward : transform.forward;
                if (direction.sqrMagnitude > .01f) _carryFacingTarget = Flat(cameraHeading);
                else if (!_hasCarryFacing) _carryFacingTarget = Flat(transform.forward);
                turnSpeed = SoloCarryTurnSpeed;
            }
            else
            {
                Vector3 cargoPos = _currentCargoRigidbody != null ? PredictedServerCargoPosition() : _currentCarryable.transform.position;
                Vector3 toCargo = cargoPos - transform.position;
                toCargo.y = 0f;
                if (toCargo.sqrMagnitude > .04f) _carryFacingTarget = toCargo.normalized;
                if (!IsServer) turnSpeed = 360f;
            }
            if (!_hasCarryFacing)
            {
                _carryFacing = _carryFacingTarget.sqrMagnitude > .01f ? _carryFacingTarget : transform.forward;
                _hasCarryFacing = true;
            }
            else
            {
                // Ease in and out of a turn (exponential approach) instead of a constant-rate snap; the turn
                // speed stays the upper limit.
                float easedStep = Vector3.Angle(_carryFacing, _carryFacingTarget) * (1f - Mathf.Exp(-CarryTurnSmoothing * Time.fixedDeltaTime));
                float step = Mathf.Min(easedStep, turnSpeed * Time.fixedDeltaTime);
                _carryFacing = Vector3.RotateTowards(_carryFacing, _carryFacingTarget, step * Mathf.Deg2Rad, 0f).normalized;
            }
            if (!HasLocalInput) return;
            // Carry traction aims at the speed this player actually requests (sprint, tiredness,
            // charge, cards), not a fixed walk speed, so cargo and carrier targets agree.
            float cargoSpeed = 5f * _currentCarryable.GetSpeedMultiplier();
            if (soloSteering && !_currentCarryable.IsSoloDragging && _movement != null && cargoSpeed > .01f)
            {
                // Alone, the cargo moves with the body's real (accelerated) velocity. The raw input reverses
                // instantly on A <-> D while the body takes ~0.5 s to turn round, which pulled the cargo
                // sideways out of the hands and then yanked the body after it.
                direction = Vector3.ClampMagnitude(Horizontal(_movement.Velocity) / cargoSpeed, 2f);
            }
            else if (_movement != null && cargoSpeed > .01f && direction.sqrMagnitude > .0001f)
                direction *= Mathf.Clamp(_movement.RequestedSpeed / cargoSpeed, .25f, 2f);
            // Report where this simulation really has the carrier: the server only sees a late copy.
            Vector3 position = transform.position;
            Vector3 velocity = _streamPositionValid ? (position - _lastStreamPosition) / Time.fixedDeltaTime : Vector3.zero;
            _lastStreamPosition = position;
            _streamPositionValid = true;
            if (IsNetworked) StreamCarrierInputServerRpc(direction, _carryFacing, _currentHoldHeight, position, velocity, OneWayDelayToServer);
            else _currentCarryable.UpdateCarrierInput(OwnerClientId, direction, _carryFacing,
                _currentHoldHeight, _leftHandGripping, _rightHandGripping, position, velocity, OneWayDelayToServer);
            PublishCarryBodyPose(true);
            float excess = _leftHandGripping ? GripOverreach(true) : 0f;
            if (_rightHandGripping) excess = Mathf.Max(excess, GripOverreach(false));
            bool coopCarry = _currentCarryable != null && _currentCarryable.CurrentCarrierCount >= 2;
            float overreachThreshold = coopCarry ? .26f : .2f;
            float overreachLimit = coopCarry ? .95f : .65f;
            _overreachTime = excess > overreachThreshold ? _overreachTime + Time.fixedDeltaTime : 0f;
            if (_overreachTime > overreachLimit && Time.time - _attachedAt > 1.1f) DropForRespawn();
            LogCarryDiagnostics(excess);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private float _nextDiagAt;
        private Vector3 _diagLastPosition;

        /// <summary>Once a second while this player walks with cargo: why a carrier is slow (reach limit, replica
        /// lag, speed multipliers). Read the lines from Player.log; same line format on host and client.</summary>
        private void LogCarryDiagnostics(float overreach)
        {
            if (Time.unscaledTime < _nextDiagAt || _inputReader == null || _movement == null) return;
            if (_inputReader.MoveInput.sqrMagnitude < .01f) { _diagLastPosition = transform.position; return; }
            float dt = Time.unscaledTime - (_nextDiagAt - 1f);
            _nextDiagAt = Time.unscaledTime + 1f;
            Vector3 moved = transform.position - _diagLastPosition; moved.y = 0f;
            _diagLastPosition = transform.position;
            string cargo = _currentCargoRigidbody != null
                ? $"cargoSpeed={Vector3.ProjectOnPlane(_currentCargoRigidbody.linearVelocity, Vector3.up).magnitude:F2} replicaVel={_cargoReplicaVelocity.magnitude:F2} yawRate={_cargoReplicaYawRate:F0} rigidbodyKinematic={_currentCargoRigidbody.isKinematic}"
                : "cargo=none";
            Debug.Log($"[CarryDiag] role={(IsServer ? "host" : "client")} carriers={_currentCarryable.CurrentCarrierCount} playerSpeed={(dt > .1f ? moved.magnitude / dt : 0f):F2} " +
                      $"requested={_movement.RequestedSpeed:F2} speedMult={_movement.SpeedMultiplier:F2} allowedRatio={LastCarryAllowedRatio:F2} overreach={overreach:F2} " +
                      $"lead={(IsServer ? 0f : ReplicaLeadSeconds()):F3} oneWay={OneWayDelayToServer:F3} {cargo}");
        }
#else
        private void LogCarryDiagnostics(float overreach) { }
#endif

        private void Update()
        {
            UpdateTransitPing();
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
                _stamina.DrainStaminaContinuous(!(_leftHandGripping && _rightHandGripping), _currentCarryable.TotalMass, _currentCarryable.CurrentCarrierCount, _currentCarryable.StaminaDrainMultiplier);
                if (_stamina.IsExhausted) { DropForRespawn(); return; }
            }
            if (CoopGame.Network.PauseMenu.IsPaused || MissionFailUI.IsVisible || CoopGame.Network.ExpeditionHUD.BlocksGameplayInput) return;
            if (_leftMarker == null || _rightMarker == null) EnsureDualMarkersCreated();
            _currentHoldHeight = CalculateHoldHeight();
            UpdateCarryableOutline(_aimedCarryable, _canGrabAimed);
            _throwReleaseCooldown = Mathf.Max(0f, _throwReleaseCooldown - Time.deltaTime);
            if (!_inputReader.IsGrabbing) _requireGrabRelease = false;
            _canAttemptGrab = !_requireGrabRelease && _throwReleaseCooldown <= 0f && !_grabPending && !_releasePending;
            // Left click picks up with both hands; right click sets it down.
            if (!IsCarrying && !_grabPending && _inputReader.GrabLeftPressed) _pendingDualToggle = true;
            if (IsCarrying && _currentCarryable != null)
            {
                if (_movement != null)
                {
                    float speed = _currentCarryable.GetSpeedMultiplier();
                    if (_isChargingThrow) speed *= .75f;
                    if (_stamina != null && _stamina.IsExhausted) speed *= .5f;
                    if (_expedition != null) speed *= _expedition.CardMagnitude(CoopGame.Network.CardEffect.FastCarrier);
                    _movement.SpeedMultiplier = speed;
                }
                if (_inputReader.GrabRightPressed && !_releasePending)
                {
                    _placeIntent = true;
                    _isChargingThrow = false;
                    _currentThrowCharge = 0f;
                }
                else if (_inputReader.ThrowHeld)
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
                if (_currentCarryable == null && CaptureCenteredGrip(requested)) SendGrabState(requested);
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
            // Hold-pose carry: the mouse wheel raises or lowers the held cargo in steps; each grab starts level.
            if (IsCarrying && _currentCarryable != null && !_currentCarryable.IsSoloDragging)
            {
                if (_inputReader != null && _inputReader.ScrollStep != 0)
                    _liftSteps = Mathf.Clamp(_liftSteps + _inputReader.ScrollStep, -CarryableObject.HoldLowerSteps, CarryableObject.HoldRaiseSteps);
                return CarryableObject.NeutralHoldHeight + _liftSteps * CarryableObject.HoldStepHeight;
            }
            _liftSteps = 0;
            if (_cameraController == null) return _normalLiftHeight;
            float pitch = _cameraController.Pitch;
            // The default third-person camera looks slightly down. Treat that
            // viewing angle as a normal carry, so simply looking ahead does not
            // lower the crate into the floor while walking.
            return pitch < 0f ? Mathf.Lerp(_normalLiftHeight, _maxLiftHeight, Mathf.Clamp01(-pitch / 55f))
                : Mathf.Lerp(_normalLiftHeight, _minLiftHeight, Mathf.Clamp01((pitch - 25f) / 35f));
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
            if (_currentCarryable == null)
            {
                if (!CaptureCenteredGrip(target)) return;
            }
            else
            {
                if (left) CaptureHandContact(target, true);
                if (right) CaptureHandContact(target, false);
            }
            SendGrabState(target);
        }

        /// <summary>Fixed two-hand grip on the face nearest the player, symmetric about the cargo centre.</summary>
        private bool TryComputeCenteredGrip(CarryableObject target, out Vector3 leftPoint, out Vector3 leftNormal,
            out Vector3 rightPoint, out Vector3 rightNormal)
        {
            leftPoint = rightPoint = Vector3.zero;
            leftNormal = rightNormal = Vector3.up;
            Vector3 center = target.ApproximateBoundsCenter;
            Vector3 toPlayer = transform.position - center;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < .0001f) toPlayer = -transform.forward;
            Vector3 axisA = FlatAxis(target.transform.right, Vector3.right);
            Vector3 axisB = FlatAxis(target.transform.forward, Vector3.forward);
            float dotA = Vector3.Dot(axisA, toPlayer), dotB = Vector3.Dot(axisB, toPlayer);
            Vector3 approach = Mathf.Abs(dotA) >= Mathf.Abs(dotB) ? axisA * Mathf.Sign(dotA) : axisB * Mathf.Sign(dotB);
            Vector3 rightDir = Vector3.Cross(Vector3.up, -approach).normalized;

            // Stay horizontally centred; hold low on the face (GripLowFraction of its height, so the cargo rides
            // on the hands) and rise toward the top edge only as far as needed for both hands to reach.
            // Never above the middle of the cargo: the hands go no higher than its centre (George, 2026-10-28).
            float top = center.y;
            float low = Mathf.Min(top, Mathf.Lerp(BoundsBottom(target), BoundsTop(target), GripLowFraction));
            for (int step = 0; step <= 8; step++)
            {
                Vector3 grip = new Vector3(center.x, Mathf.Lerp(low, top, step / 8f), center.z);
                if (TryGripAtHeight(target, grip, approach, rightDir,
                    out leftPoint, out leftNormal, out rightPoint, out rightNormal)) return true;
            }
            return false;
        }

        private bool TryGripAtHeight(CarryableObject target, Vector3 grip, Vector3 approach, Vector3 rightDir,
            out Vector3 leftPoint, out Vector3 leftNormal, out Vector3 rightPoint, out Vector3 rightNormal)
        {
            // Narrow items get a proportionally narrower grip so both palms stay on the face.
            bool sidesFound = FindSurfaceContact(target, grip, -rightDir, grip, out Vector3 leftSide, out _) &
                FindSurfaceContact(target, grip, rightDir, grip, out Vector3 rightSide, out _);
            float halfWidth = sidesFound ? Vector3.Dot(rightSide - leftSide, rightDir) * .5f : .3f;
            rightPoint = Vector3.zero; rightNormal = Vector3.up;
            leftPoint = Vector3.zero; leftNormal = Vector3.up;

            // Cargo no wider than the arms can hug: palms on the two side faces, just behind the near edge.
            if (sidesFound && halfWidth <= SideGripMaxHalfWidth)
            {
                FindSurfaceContact(target, grip, approach, grip, out Vector3 front, out _);
                Vector3 inset = -approach * SideGripInset + Vector3.Project(front - grip, approach);
                if (FindSurfaceContact(target, grip + inset - rightDir * halfWidth, -rightDir, grip, out leftPoint, out leftNormal) &&
                    FindSurfaceContact(target, grip + inset + rightDir * halfWidth, rightDir, grip, out rightPoint, out rightNormal) &&
                    IsContactReachable(true, leftPoint, rightDir, true) && IsContactReachable(false, rightPoint, rightDir, true))
                    return true;
            }

            // Wider cargo: the near face, hands out toward its left and right edges.
            float spread = Mathf.Min(MaxGripSpread, Mathf.Max(halfWidth * .75f, halfWidth - .05f));
            return FindSurfaceContact(target, grip - rightDir * spread, approach, grip, out leftPoint, out leftNormal) &&
                FindSurfaceContact(target, grip + rightDir * spread, approach, grip, out rightPoint, out rightNormal) &&
                IsContactReachable(true, leftPoint, rightDir, true) && IsContactReachable(false, rightPoint, rightDir, true);
        }

        private static float BoundsBottom(CarryableObject target)
        {
            float bottom = float.PositiveInfinity;
            var colliders = target.CollisionColliders;
            if (colliders != null)
                foreach (var col in colliders)
                    if (col != null && col.enabled && !col.isTrigger) bottom = Mathf.Min(bottom, col.bounds.min.y);
            return float.IsInfinity(bottom) ? target.ApproximateBoundsCenter.y : bottom;
        }

        private static float BoundsTop(CarryableObject target)
        {
            float top = float.NegativeInfinity;
            var colliders = target.CollisionColliders;
            if (colliders != null)
                foreach (var col in colliders)
                    if (col != null && col.enabled && !col.isTrigger) top = Mathf.Max(top, col.bounds.max.y);
            return float.IsInfinity(top) ? target.ApproximateBoundsCenter.y : top;
        }

        private static Vector3 FlatAxis(Vector3 axis, Vector3 fallback)
        {
            axis.y = 0f;
            return axis.sqrMagnitude > .01f ? axis.normalized : fallback;
        }

        private bool IsContactReachable(bool left, Vector3 point, Vector3 rightDir = default, bool leanDown = false)
        {
            GetArmGeometry(left, out Vector3 shoulder, out float reach);
            // A pickup grip below the shoulders may count a torso lean toward reach (the server's ValidateContact
            // allows the same); the hold pose then lifts the cargo up to the hands.
            if (leanDown) shoulder = Vector3.MoveTowards(shoulder, point, Mathf.Clamp(shoulder.y - point.y, 0f, PickupLeanAllowance));
            float maximum = reach + LocalContactSlack;
            float maxSqr = maximum * maximum;
            if ((point - shoulder).sqrMagnitude <= maxSqr) return true;
            if (rightDir.sqrMagnitude > .001f)
            {
                GetArmGeometry(!left, out Vector3 otherShoulder, out _);
                Vector3 chestCenter = (shoulder + otherShoulder) * .5f;
                Vector3 facingShoulder = chestCenter + rightDir.normalized * (left ? -.18f : .18f);
                if ((point - facingShoulder).sqrMagnitude <= maxSqr) return true;
            }
            return false;
        }

        private bool CaptureCenteredGrip(CarryableObject target)
        {
            if (target == null || !TryComputeCenteredGrip(target, out Vector3 leftPoint, out Vector3 leftNormal,
                out Vector3 rightPoint, out Vector3 rightNormal)) return false;
            SetHandContact(target, true, leftPoint, leftNormal);
            SetHandContact(target, false, rightPoint, rightNormal);
            return true;
        }

        private void SetHandContact(CarryableObject target, bool left, Vector3 contact, Vector3 surfaceNormal)
        {
            Vector3 fingers = Vector3.ProjectOnPlane(Vector3.up, surfaceNormal);
            if (fingers.sqrMagnitude < .001f) fingers = Vector3.ProjectOnPlane(transform.forward, surfaceNormal);
            Quaternion localRot = Quaternion.Inverse(target.transform.rotation) * Quaternion.LookRotation(surfaceNormal, fingers.normalized);
            Vector3 localPoint = target.transform.InverseTransformPoint(contact);
            if (left) { _leftHandGripping = true; _currentLocalContactLeft = localPoint; _currentLocalRotationLeft = localRot; }
            else { _rightHandGripping = true; _currentLocalContactRight = localPoint; _currentLocalRotationRight = localRot; }
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
                        bool centered = TryComputeCenteredGrip(carryable, out leftAimPoint, out leftAimNormal,
                            out rightAimPoint, out rightAimNormal);
                        leftReachable = rightReachable = centered;
                        reachableHit = centered;
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
            if (IsNetworked && !IsOwner)
            {
                // Reconcile presentation from the atomic server snapshot. Local
                // collision/respawn cleanup must not erase a friend's grip pose.
                CargoGripState state = _netGripState.Value;
                if (!state.Attached) return;
                if (_currentCarryable == null || _currentCarryable.NetworkObjectId != state.CargoId)
                    ApplyGripState(state);
                _currentLocalContactLeft = state.LeftPoint;
                _currentLocalContactRight = state.RightPoint;
                _currentLocalRotationLeft = state.LeftRotation;
                _currentLocalRotationRight = state.RightRotation;
            }
            if (_currentCarryable == null || !IsCarrying) return;
            // A confirmed cargo attachment takes priority over an older replicated
            // climbing flag, which can arrive on a different network tick.
            Transform cargo = _currentCarryable.transform;
            // A team-mate's hands go where this peer draws the crate (the owner's own hands get the same shift
            // inside AdjustClientVisualHandTarget).
            Vector3 drawShift = IsNetworked && !IsOwner ? CargoDrawShift(_currentCarryable) : Vector3.zero;
            if (LeftHandGripping)
            {
                Vector3 leftTarget = AdjustClientVisualHandTarget(true, cargo.TransformPoint(_currentLocalContactLeft)) + drawShift;
                ApplyHandTarget(true, leftTarget, cargo.rotation * _currentLocalRotationLeft);
            }
            else RelaxFreeHand(true);
            if (RightHandGripping)
            {
                Vector3 rightTarget = AdjustClientVisualHandTarget(false, cargo.TransformPoint(_currentLocalContactRight)) + drawShift;
                ApplyHandTarget(false, rightTarget, cargo.rotation * _currentLocalRotationRight);
            }
            else RelaxFreeHand(false);
        }

        private Vector3 AdjustClientVisualHandTarget(bool left, Vector3 worldContact)
        {
            if (!IsNetworked || !IsOwner || IsServer) return worldContact;

            // Same shift as the cargo's own drawing (CargoDrawShifter), so the hands stay on the drawn crate.
            worldContact += CargoDrawShift(_currentCarryable);

            GetArmGeometry(left, out Vector3 shoulder, out float reach);
            GetBodyClearance(out Vector3 bodyCenter, out float bodyRadius);
            Vector3 flatForward = Flat(transform.forward);
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;

            // Keep hands outside the carrier's torso cylinder so replica lag never folds elbows backward into the chest.
            Vector3 fromBody = worldContact - bodyCenter;
            Vector3 flatFromBody = new Vector3(fromBody.x, 0f, fromBody.z);
            float minBodyDist = bodyRadius + 0.06f;
            if (flatFromBody.sqrMagnitude < minBodyDist * minBodyDist)
            {
                Vector3 pushDir = flatFromBody.sqrMagnitude > 0.0001f ? flatFromBody.normalized : flatForward;
                if (Vector3.Dot(pushDir, flatForward) < 0.25f)
                    pushDir = (flatForward + pushDir * 0.5f).normalized;
                worldContact.x = bodyCenter.x + pushDir.x * minBodyDist;
                worldContact.z = bodyCenter.z + pushDir.z * minBodyDist;
            }

            // Clamp shoulder-to-hand distance within comfortable bent-elbow range so arms never hyper-extend straight (1.25x) or collapse.
            Vector3 fromShoulder = worldContact - shoulder;
            float dist = fromShoulder.magnitude;
            float minArmDist = reach * 0.48f;
            float maxArmDist = reach * 0.80f;
            if (dist > maxArmDist && dist > 0.0001f)
                worldContact = shoulder + fromShoulder * (maxArmDist / dist);
            else if (dist < minArmDist)
            {
                Vector3 dir = dist > 0.0001f ? fromShoulder / dist : flatForward;
                worldContact = shoulder + dir * minArmDist;
            }

            return worldContact;
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

        /// <summary>Fraction (0-1) of the requested walk step the reach/body constraint allowed last step; read by tests.</summary>
        public float LastCarryAllowedRatio { get; private set; } = 1f;

        /// <summary>FixedUpdate movement constraint; wall collisions are still resolved by CharacterController.Move.</summary>
        public Vector3 ConstrainCarryDisplacement(Vector3 displacement)
        {
            if (_currentCarryable == null || !IsCarrying || !HasLocalInput) return displacement;
            Vector3 original = displacement;
            Vector3 requestedHorizontal = new(original.x, 0f, original.z);
            float requestedSqr = requestedHorizontal.sqrMagnitude;
            // Grip/cargo motion may limit walking, but must never walk the player.
            if (!_currentCarryable.IsSoloDragging && TryGetCarryFacing(out Vector3 facing))
            {
                // Hold-pose carry: the cargo moves with the team's walking intent and each carrier keeps to their
                // stance. A carrier left outside it (a partner who stopped) is drawn back at walking pace.
                ConstrainToHoldStance(Flat(facing).normalized, ref displacement);
                Vector3 horizontal = Vector3.ClampMagnitude(Horizontal(displacement),
                    Mathf.Max(Mathf.Sqrt(requestedSqr), StancePullSpeed * Time.fixedDeltaTime));
                LastCarryAllowedRatio = requestedSqr < .0000001f ? 1f
                    : Mathf.Clamp01(Vector3.Dot(horizontal, requestedHorizontal) / requestedSqr);
                LogStanceCorrection(requestedHorizontal, horizontal, facing);
                return horizontal + Vector3.up * original.y;
            }
            if (requestedSqr < .0000001f) return original;
            for (int pass = 0; pass < 3; pass++)
            {
                if (_leftHandGripping) ConstrainHand(true, ref displacement);
                if (_rightHandGripping) ConstrainHand(false, ref displacement);
                ConstrainBodyOverlap(ref displacement);
            }
            float allowed = Mathf.Clamp01(Vector3.Dot(displacement, requestedHorizontal) / requestedSqr);
            LastCarryAllowedRatio = allowed;
            return requestedHorizontal * allowed + Vector3.up * original.y;
        }

        private void ConstrainHand(bool left, ref Vector3 displacement)
        {
            GetArmGeometry(left, out Vector3 shoulder, out float reach);
            float effectiveReach = HasCarrierSlack() ? reach + .18f : reach;
            Vector3 anchor = GetPhysicalContact(left);
            Vector3 proposed = shoulder + displacement;
            float vertical = proposed.y - anchor.y;
            float horizontalReach = Mathf.Sqrt(Mathf.Max(.0025f, effectiveReach * effectiveReach * .96f - vertical * vertical));
            Vector3 delta = proposed - anchor;
            delta.y = 0f;
            if (delta.sqrMagnitude > horizontalReach * horizontalReach)
                displacement += Vector3.ClampMagnitude(delta, horizontalReach) - delta;
        }

        // Hold-pose carry: how far a carrier may stray from their stance behind the grip. Stepping towards the
        // cargo is allowed much less than stepping back or sideways, so the body never enters the crate.
        private const float StanceSlack = .22f;
        private const float StanceSoloSideSlack = .05f;
        private const float StanceNetworkSlack = .15f;
        private const float StanceTowardCargo = .03f;
        private const float StancePullSpeed = 4f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private float _nextStanceLogAt;

        /// <summary>Diagnostics: a stance step that moves a carrier who asked to stand still (at most 5 lines/s).</summary>
        private void LogStanceCorrection(Vector3 requested, Vector3 applied, Vector3 facing)
        {
            if (requested.sqrMagnitude > .0001f || applied.sqrMagnitude < .0004f || Time.unscaledTime < _nextStanceLogAt) return;
            _nextStanceLogAt = Time.unscaledTime + .2f;
            Vector3 grip = (GetPhysicalContact(true) + GetPhysicalContact(false)) * .5f;
            Vector3 stance = CarryableObject.StanceFor(grip, Flat(facing).normalized);
            Debug.Log($"[StanceDiag] pos={transform.position:F3} stance={stance:F3} grip={grip:F3} facing={facing:F2} " +
                      $"applied={applied:F3} L={_leftHandGripping} R={_rightHandGripping} contactL={_currentLocalContactLeft:F2} contactR={_currentLocalContactRight:F2}");
        }
#else
        private void LogStanceCorrection(Vector3 requested, Vector3 applied, Vector3 facing) { }
#endif

        /// <summary>Hold-pose carry: keep this carrier near the place behind their grip from which the hands reach
        /// it at chest height (<see cref="CarryableObject.HoldReach"/>). A step outside the limits is cut back to them.</summary>
        private void ConstrainToHoldStance(Vector3 facing, ref Vector3 displacement)
        {
            if (facing.sqrMagnitude < .5f) return;
            Vector3 grip;
            if (_leftHandGripping && _rightHandGripping) grip = (GetPhysicalContact(true) + GetPhysicalContact(false)) * .5f;
            else if (_leftHandGripping) grip = GetPhysicalContact(true);
            else if (_rightHandGripping) grip = GetPhysicalContact(false);
            else return;

            Vector3 stance = CarryableObject.StanceFor(grip, facing);
            Vector3 offset = Horizontal(transform.position + displacement - stance);
            float along = Vector3.Dot(offset, facing);
            Vector3 side = offset - facing * along;
            float slack = StanceSlack + (HasCarrierSlack() ? StanceNetworkSlack : 0f);
            // Carrying alone, the body stays squarely behind the grip: A/D carries the cargo across with the
            // player instead of letting it slide to one side of the body.
            float sideSlack = _currentCarryable.CurrentCarrierCount <= 1
                ? StanceSoloSideSlack + (HasCarrierSlack() ? StanceNetworkSlack : 0f) : slack;
            Vector3 limited = facing * Mathf.Clamp(along, -slack, StanceTowardCargo) + Vector3.ClampMagnitude(side, sideSlack);
            displacement += limited - offset;
        }

        /// <summary>A co-op client limits its steps against a predicted cargo with this slack. The host carrier in a
        /// co-op team gets the same slack: without it the host is held to the real cargo (which only moves once the
        /// springs pull it) while the client walks at full speed, so the team squeezes together or tears apart.</summary>
        private bool HasCarrierSlack() =>
            IsNetworked && (!IsServer || _currentCarryable.CurrentCarrierCount > 1);

        private void ConstrainBodyOverlap(ref Vector3 displacement)
        {
            if (_bodyCapsule == null || !_bodyCapsule.enabled || _currentCargoColliders == null) return;
            Transform cargo = _currentCarryable.transform;
            Quaternion poseCorrection = _currentCargoRigidbody != null
                ? _currentCargoRigidbody.rotation * Quaternion.Inverse(cargo.rotation) : Quaternion.identity;
            Vector3 cargoPosition = _currentCargoRigidbody != null ? PredictedServerCargoPosition() : cargo.position;
            // A co-op host pressed against the cargo would be stopped until the other carrier's pull starts it,
            // while the client walks on and tears the grip: treat the cargo as already moving with the host's walk.
            // Only the body limit leads; the hand reach still uses the real cargo.
            if (IsServer && HasCarrierSlack() && _movement != null)
                cargoPosition += new Vector3(_movement.Velocity.x, 0f, _movement.Velocity.z) * CoopHostLeadSeconds;
            float replicaTolerance = HasCarrierSlack() ? .12f : 0f;
            for (int i = 0; i < _currentCargoColliders.Length; i++)
            {
                Collider collider = _currentCargoColliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                Vector3 colliderPosition = cargoPosition + poseCorrection * (collider.transform.position - cargo.position);
                Quaternion colliderRotation = poseCorrection * collider.transform.rotation;
                if (!Physics.ComputePenetration(_bodyCapsule, transform.position + displacement, transform.rotation,
                    collider, colliderPosition, colliderRotation, out Vector3 direction, out float depth)) continue;

                float effectiveDepth = depth - replicaTolerance;
                if (effectiveDepth <= 0f) continue;

                // Use the finite collider volume, including top grips and rotated
                // crates. CharacterController.Move resolves floors and overhead hits.
                Vector3 horizontal = new Vector3(direction.x, 0f, direction.z);
                float horizontalLength = horizontal.magnitude;
                if (horizontalLength > .15f)
                    displacement += horizontal / horizontalLength * ((effectiveDepth + .01f) / horizontalLength);
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

        /// <summary>Owner, FixedUpdate: publishes where this body stands relative to the cargo pose that its
        /// own reach and body limits use (the predicted server cargo on a co-op client), so the published
        /// relation is one in which the hands reach.</summary>
        private void PublishCarryBodyPose(bool carrying)
        {
            if (!IsNetworked || !IsOwner) return;
            CarryBodyPose current = _netCarryBodyPose.Value;
            if (!carrying || _currentCarryable == null || _currentCargoRigidbody == null)
            {
                if (current.Valid) _netCarryBodyPose.Value = default;
                return;
            }
            float cargoYaw = FlatYaw(_currentCargoRigidbody.rotation);
            Vector3 offset = Quaternion.Euler(0f, -cargoYaw, 0f) * (transform.position - PredictedServerCargoPosition());
            var pose = new CarryBodyPose
            {
                Valid = true,
                CargoId = _currentCarryable.NetworkObjectId,
                Offset = new Vector2(offset.x, offset.z),
                Yaw = Mathf.DeltaAngle(cargoYaw, transform.eulerAngles.y)
            };
            if (!Finite(new Vector3(pose.Offset.x, pose.Offset.y, pose.Yaw))) return;
            if (current.Valid && current.CargoId == pose.CargoId &&
                (current.Offset - pose.Offset).sqrMagnitude < CarryPoseSendDistance * CarryPoseSendDistance &&
                Mathf.Abs(Mathf.DeltaAngle(current.Yaw, pose.Yaw)) < CarryPoseSendYaw) return;
            _netCarryBodyPose.Value = pose;
        }

        /// <summary>
        /// Remote carriers only, called once per rendered frame by the body visual before the arms solve.
        /// Moves the drawn body (never the networked root, which physics and the server's lead measurement
        /// read) to the owner's published place relative to the cargo as drawn on this peer, blending in and
        /// out over <see cref="RemoteCarryPoseBlendSeconds"/>. Returns false when nothing is applied.
        /// </summary>
        public bool ApplyRemoteCarryRenderPose(ref Vector3 position, ref Quaternion rotation)
        {
            if (!IsNetworked || IsOwner) { _remoteCarryPoseWeight = 0f; return false; }
            CarryBodyPose pose = _netCarryBodyPose.Value;
            CargoGripState grip = _netGripState.Value;
            bool active = pose.Valid && grip.Attached && pose.CargoId == grip.CargoId &&
                _currentCarryable != null && _currentCarryable.NetworkObjectId == pose.CargoId;
            if (active)
            {
                Transform cargo = _currentCarryable.transform;
                float cargoYaw = FlatYaw(cargo.rotation);
                Vector3 target = cargo.position + CargoDrawShift(_currentCarryable) +
                    Quaternion.Euler(0f, cargoYaw, 0f) * new Vector3(pose.Offset.x, 0f, pose.Offset.y);
                Vector3 correction = target - position;
                correction.y = 0f;
                active = correction.sqrMagnitude <= MaxRemoteCarryRenderCorrection * MaxRemoteCarryRenderCorrection;
                if (active)
                {
                    _remoteCarryCorrection = correction;
                    _remoteCarryYawCorrection = Mathf.DeltaAngle(rotation.eulerAngles.y, cargoYaw + pose.Yaw);
                }
            }
            _remoteCarryPoseWeight = Mathf.MoveTowards(_remoteCarryPoseWeight, active ? 1f : 0f,
                Time.deltaTime / RemoteCarryPoseBlendSeconds);
            if (_remoteCarryPoseWeight <= 0f) return false;
            position += _remoteCarryCorrection * _remoteCarryPoseWeight;
            rotation = Quaternion.AngleAxis(_remoteCarryYawCorrection * _remoteCarryPoseWeight, Vector3.up) * rotation;
            return true;
        }

        /// <summary>A gripping hand's contact on the cargo as drawn this frame (the IK target).</summary>
        public bool TryGetRenderedGripContact(bool left, out Vector3 contact)
        {
            contact = default;
            if (_currentCarryable == null || !(left ? LeftHandGripping : RightHandGripping)) return false;
            Vector3 local;
            if (IsNetworked && !IsOwner)
            {
                CargoGripState state = _netGripState.Value;
                if (!state.Attached || state.CargoId != _currentCarryable.NetworkObjectId) return false;
                local = left ? state.LeftPoint : state.RightPoint;
            }
            else local = left ? _currentLocalContactLeft : _currentLocalContactRight;
            contact = _currentCarryable.transform.TransformPoint(local) + CargoDrawShift(_currentCarryable);
            return true;
        }

        private static float FlatYaw(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < .0001f)
            {
                forward = rotation * Vector3.up;
                forward.y = 0f;
            }
            return forward.sqrMagnitude < .0001f ? 0f : Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        private Vector3 GetPhysicalContact(bool left)
        {
            Vector3 local = left ? _currentLocalContactLeft : _currentLocalContactRight;
            if (_currentCargoRigidbody == null) return _currentCarryable.transform.TransformPoint(local);
            return PredictedServerCargoPosition() + PredictedServerCargoRotation() * Vector3.Scale(local, _currentCarryable.transform.lossyScale);
        }

        /// <summary>Cargo position for reach/body limits: where the server has the cargo now. On a client the
        /// cargo is a replica one network delay behind the server (solo or co-op); a carrier walking away from it would
        /// look over-reached and have every step cut, so it is extrapolated. The server simulates the cargo
        /// itself (including kinematic delivery/placement states), and offline carry is unchanged.</summary>
        private Vector3 PredictedServerCargoPosition()
        {
            Vector3 position = _currentCargoRigidbody.position;
            if (IsServer || !_currentCarryable.IsSpawned) return position;
            SampleCargoReplica();
            Vector3 leadVelocity = _cargoReplicaVelocity;
            if (HasLocalInput && _movement != null)
            {
                Vector3 localVel = new Vector3(_movement.Velocity.x, 0f, _movement.Velocity.z);
                float carrierShare = _currentCarryable.CurrentCarrierCount > 1 ? .75f : 1f;
                Vector3 expectedVel = localVel * carrierShare;
                if (Vector3.Dot(expectedVel, leadVelocity) < expectedVel.sqrMagnitude * .6f)
                    leadVelocity = Vector3.Lerp(leadVelocity, expectedVel, .75f);
            }
            return position + leadVelocity * ReplicaLeadSeconds();
        }

        private const float CoopHostLeadSeconds = .12f;

        /// <summary>Cargo rotation for the same reach limits. A turning cargo (a solo carrier steering it, or a
        /// team swinging it round) sits one network delay behind on a client, so the grip points swing away from
        /// the hands: at 140 deg/s and 0.13 s that is about 18 deg, 0.2 m at the grip, enough to count as
        /// over-reach and let go. The yaw is extrapolated the same way as the position.</summary>
        private Quaternion PredictedServerCargoRotation()
        {
            Quaternion rotation = _currentCargoRigidbody.rotation;
            if (IsServer || !_currentCarryable.IsSpawned) return rotation;
            SampleCargoReplica();
            if (Mathf.Abs(_cargoReplicaYawRate) < .5f) return rotation;
            return Quaternion.AngleAxis(_cargoReplicaYawRate * ReplicaLeadSeconds(), Vector3.up) * rotation;
        }

        private float ReplicaLeadSeconds() =>
            NetworkMotionDelay.Estimate(NetworkManager.Singleton, _cargoNetworkTransform, OneWayDelayToServer);

        private void SampleCargoReplica()
        {
            if (_cargoReplicaSampledAt == Time.fixedTime) return;
            _cargoReplicaSampledAt = Time.fixedTime;
            // The replica moves once per rendered frame; measure over actual changes.
            Vector3 position = _currentCargoRigidbody.position;
            Vector3 moved = position - _cargoReplicaLastPosition;
            if (moved.sqrMagnitude > 1e-8f)
            {
                float span = Mathf.Max(Time.fixedDeltaTime, Time.fixedTime - _cargoReplicaMovedAt);
                _cargoReplicaVelocity = Vector3.Lerp(_cargoReplicaVelocity, Vector3.ClampMagnitude(moved / span, MaxReplicaCargoSpeed), .5f);
                _cargoReplicaLastPosition = position;
                _cargoReplicaMovedAt = Time.fixedTime;
            }
            else if (Time.fixedTime - _cargoReplicaMovedAt > ReplicaStillSeconds) _cargoReplicaVelocity = Vector3.zero;

            Quaternion rotation = _currentCargoRigidbody.rotation;
            (rotation * Quaternion.Inverse(_cargoReplicaLastRotation)).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (Mathf.Abs(angle) > .02f)
            {
                float span = Mathf.Max(Time.fixedDeltaTime, Time.fixedTime - _cargoReplicaRotatedAt);
                float yawRate = Mathf.Clamp(angle * axis.y / span, -MaxReplicaYawRate, MaxReplicaYawRate);
                _cargoReplicaYawRate = Mathf.Lerp(_cargoReplicaYawRate, yawRate, .5f);
                _cargoReplicaLastRotation = rotation;
                _cargoReplicaRotatedAt = Time.fixedTime;
            }
            else if (Time.fixedTime - _cargoReplicaRotatedAt > ReplicaStillSeconds) _cargoReplicaYawRate = 0f;
        }

        private const float MaxReplicaCargoSpeed = 12f;
        private const float MaxReplicaYawRate = 360f;
        private const float ReplicaStillSeconds = .2f;

        // ---- One-way transit to the server, measured in-game (transports such as Facepunch report no RTT) ----
        private const float TransitPingInterval = .5f;
        private const float TransitSmoothing = .2f;
        private const float MaxPlausibleRoundTrip = 2f;
        private float _measuredOneWay = -1f;
        private float _nextTransitPingAt;

        /// <summary>Seconds a message takes from this owner to the server; 0 on the server itself.</summary>
        private float OneWayDelayToServer => IsServer ? 0f
            : _measuredOneWay >= 0f ? _measuredOneWay
            : NetworkMotionDelay.HalfRoundTrip(NetworkManager.Singleton, NetworkManager.ServerClientId);

        private void UpdateTransitPing()
        {
            if (!IsNetworked || !IsOwner || IsServer || Time.unscaledTime < _nextTransitPingAt) return;
            _nextTransitPingAt = Time.unscaledTime + TransitPingInterval;
            TransitPingServerRpc(Time.realtimeSinceStartup);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        private void TransitPingServerRpc(float sentAt) => TransitPongRpc(sentAt);

        [Rpc(SendTo.Owner, Delivery = RpcDelivery.Unreliable)]
        private void TransitPongRpc(float sentAt)
        {
            float roundTrip = Time.realtimeSinceStartup - sentAt;
            if (!float.IsFinite(roundTrip) || roundTrip < 0f || roundTrip > MaxPlausibleRoundTrip) return;
            float oneWay = Mathf.Min(roundTrip * .5f, NetworkMotionDelay.MaxDelay);
            _measuredOneWay = _measuredOneWay < 0f ? oneWay : Mathf.Lerp(_measuredOneWay, oneWay, TransitSmoothing);
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
            GetArmGeometry(!left, out Vector3 otherShoulder, out _);
            // Same torso-lean allowance as the owner's pickup check, for grips below the shoulders.
            float lean = Mathf.Clamp(shoulder.y - point.y, 0f, PickupLeanAllowance);
            shoulder = Vector3.MoveTowards(shoulder, point, lean);
            otherShoulder = Vector3.MoveTowards(otherShoulder, point, lean);
            Vector3 chestCenter = (shoulder + otherShoulder) * .5f;
            Vector3 toCargo = cargo.ApproximateBoundsCenter - transform.position;
            toCargo.y = 0f;
            Vector3 facingRight = toCargo.sqrMagnitude > .0001f ? Vector3.Cross(Vector3.up, toCargo.normalized).normalized : transform.right;
            Vector3 facingShoulder = chestCenter + facingRight * (left ? -.18f : .18f);
            float maximumDistance = reach + ServerContactSlack + .10f;
            float maxSqr = maximumDistance * maximumDistance;
            if ((point - shoulder).sqrMagnitude > maxSqr && (point - facingShoulder).sqrMagnitude > maxSqr) return false;
            Quaternion worldRot = cargo.transform.rotation * rotation.normalized;
            Vector3 refShoulder = (point - facingShoulder).sqrMagnitude < (point - shoulder).sqrMagnitude ? facingShoulder : shoulder;
            if (!FindSurfaceContact(cargo, point, worldRot * Vector3.forward, refShoulder, out Vector3 surface, out Vector3 normal) ||
                Vector3.Distance(point, surface) > .15f ||
                ((surface - shoulder).sqrMagnitude > maxSqr && (surface - facingShoulder).sqrMagnitude > maxSqr)) return false;
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
        private void RequestPlaceServerRpc()
        {
            if (_currentCarryable != null && _currentCarryable.HasCarrier(OwnerClientId))
                _serverPlaceIntent = true;
        }

        [Rpc(SendTo.Owner)]
        private void PlaceResultClientRpc(bool success)
        {
            if (!success) _placeFeedbackUntil = Time.unscaledTime + 2f;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestDropServerRpc()
        {
            _pendingServerGripKind = 0;
            _serverPlaceIntent = false;
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
        private void StreamCarrierInputServerRpc(Vector3 direction, Vector3 heading, float height, Vector3 position, Vector3 velocity, float oneWayDelay)
        {
            if (_currentCarryable == null || !Finite(direction) || !Finite(heading) || !float.IsFinite(height) ||
                !Finite(position) || !Finite(velocity) || !float.IsFinite(oneWayDelay)) return;
            // The reported motion only offsets/damps this carrier's own support targets and is bounded server-side
            // against the server's view of this player.
            _currentCarryable.UpdateCarrierInput(OwnerClientId, Vector3.ClampMagnitude(direction, 2f), Vector3.ClampMagnitude(heading, 1f),
                Mathf.Clamp(height, .3f, 2.5f), LeftHandGripping, RightHandGripping, position, velocity, oneWayDelay);
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
            EnsureVisualHandsCreated();
            _currentCarryable = carryable;
            CoopGame.Network.GameplayFeedback.Play(CoopGame.Network.GameplayFeedback.Cue.Lift, carryable.transform.position);
            _assignedSocketIndex = socketIndex;
            _attachedAt = Time.time;
            _overreachTime = 0f;
            _currentCargoColliders = carryable.GetComponentsInChildren<Collider>(true);
            _currentCargoRigidbody = carryable.GetComponent<Rigidbody>();
            _cargoNetworkTransform = carryable.GetComponent<Unity.Netcode.Components.NetworkTransform>();
            _cargoReplicaVelocity = Vector3.zero;
            _cargoReplicaLastPosition = _currentCargoRigidbody != null ? _currentCargoRigidbody.position : carryable.transform.position;
            _cargoReplicaMovedAt = Time.fixedTime;
            _cargoReplicaYawRate = 0f;
            _cargoReplicaLastRotation = _currentCargoRigidbody != null ? _currentCargoRigidbody.rotation : carryable.transform.rotation;
            _cargoReplicaRotatedAt = Time.fixedTime;
            _cargoReplicaSampledAt = -1f;
            _streamPositionValid = false;

            if (_movement != null)
            {
                _movement.SpeedMultiplier = carryable.GetSpeedMultiplier();
            }

            // Apply the same carrier/cargo collision policy on every peer.
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
            for (int i = _collisionRestores.Count - 1; i >= 0; i--)
            {
                Collider oldCargo = _collisionRestores[i];
                bool reattached = _currentCargoColliders != null &&
                    System.Array.IndexOf(_currentCargoColliders, oldCargo) >= 0 && IsCarrying;
                if (reattached || SetCollisionPair(oldCargo, false)) _collisionRestores.RemoveAt(i);
            }
            if (_currentCargoColliders == null || (!IsCarrying && !_collisionIgnorePending)) return;
            _collisionIgnorePending = false;
            // Collider enable/disable can clear PhysX's ignore state. Keep the
            // carrier's pair exemption valid throughout the attachment on all peers.
            for (int i = 0; i < _currentCargoColliders.Length; i++)
                SetCollisionPair(_currentCargoColliders[i], true);
        }

        private bool SetCollisionPair(Collider cargoCollider, bool ignore)
        {
            if (cargoCollider == null || _playerColliders == null) return true;
            bool completed = true;
            for (int i = 0; i < _playerColliders.Length; i++)
            {
                Collider playerCollider = _playerColliders[i];
                if (playerCollider != null && !playerCollider.isTrigger && !cargoCollider.isTrigger)
                {
                    if (!ignore && !CarryableObject.CanRestoreCarryCollision(playerCollider, cargoCollider))
                    { completed = false; continue; }
                    Physics.IgnoreCollision(playerCollider, cargoCollider,
                        ignore && CarryableObject.ShouldIgnoreCarryCollision(playerCollider));
                }
            }
            return completed;
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
            float throwSpeed = Mathf.Lerp(_minThrowSpeed, _maxThrowSpeed, charge) *
                (_expedition != null ? _expedition.CardMagnitude(CoopGame.Network.CardEffect.LongThrow) : 1f);
            LastThrowSpeed = throwSpeed;
            Thrown?.Invoke();
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
