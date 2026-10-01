using System;
using Unity.Netcode;
using UnityEngine;
using CoopGame.Player;
using CoopGame.CarrySystem;

/// <summary>
/// Wallclimb implements true two-handed physical climbing inspired by Human Fall Flat:
/// 
/// Controls & Mechanics:
/// 1. Independent Two-Handed Grip:
///    - Left Click (Hold) = Left Hand reaches and grips wall.
///    - Right Click (Hold) = Right Hand reaches and grips wall.
///    - Release button to release that hand.
/// 2. Look Up/Down to Lift/Lower Character:
///    - When holding onto a wall with at least one hand, looking UP (camera pitch up) pulls your character UP towards the hand!
///    - Looking DOWN lowers your character down.
///    - Hand-over-hand climbing: Hold left -> look up to hoist body up -> free right hand can now reach higher -> hold right -> release left -> repeat!
/// 3. Directional Aiming Constraints:
///    - Left Hand aims Up, Down, and Left (cannot cross far to the right).
///    - Right Hand aims Up, Down, and Right (cannot cross far to the left).
///    - Looking left and right allows you to reach further sideways and traverse along the wall.
/// 4. Wall Jump Boost:
///    - Pressing Space (Jump) while holding onto the wall triggers a straight UPWARD jump off the wall.
///    - Jump goes straight UP regardless of camera look direction.
/// 5. Edge Hold & Top-of-Wall Pull-Up:
///    - When hands are at the top edge, press Space bar to pull up onto the surface.
///    - No more auto-jump from looking up or pressing W — must be intentional Space press.
/// 6. Anti-Phasing Protection:
///    - Maintains a strict safe distance from wall colliders. Never teleports through colliders.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(CharacterController))]
public class Wallclimb : NetworkBehaviour
{
    [Header("Climbing Reach & Speed")]
    [Tooltip("Maximum physical reach distance from shoulders to wall surface.")]
    [SerializeField] private float _handReachDistance = 2.0f;

    [Tooltip("Stamina drained per second while holding with both hands (1.5f/s).")]
    [SerializeField] private float _twoHandStaminaDrain = 1.5f;

    [Tooltip("Stamina drained per second while holding with only one hand (3.5f/s).")]
    [SerializeField] private float _oneHandStaminaDrain = 3.5f;

    [Tooltip("Additional climbable layers. All solid world layers except Ignore Raycast and UI are scanned by default.")]
    [SerializeField] private LayerMask _wallLayers = 1 << 3;

    [Header("Pitch Lift Mechanics (Human Fall Flat Style)")]
    [Tooltip("Distance from grip point to chest when looking straight ahead.")]
    [SerializeField] private float _normalHangDistance = 0.95f;

    [Tooltip("Distance from grip point to chest when looking all the way up (hoisted high).")]
    [SerializeField] private float _minHangDistance = 0.35f;

    [Tooltip("Distance from grip point to chest when looking down (hanging low).")]
    [SerializeField] private float _maxHangDistance = 1.45f;

    [Tooltip("Speed at which body smoothly pulls towards target hang height.")]
    [SerializeField] private float _bodyPullSpeed = 10.0f;

    [Header("Wall Jump Boost")]
    [Tooltip("Upward velocity applied during wall jump boost.")]
    [SerializeField] private float _jumpBoostUp = 8.0f;

    [Tooltip("Outward / camera push velocity applied during wall jump boost.")]
    [SerializeField] private float _jumpBoostOut = 4.5f;

    [Tooltip("Stamina cost for executing a wall jump boost (10.0f).")]
    [SerializeField] private float _jumpBoostStaminaCost = 10.0f;

    [Header("Reticle & Marker Visuals")]
    [Tooltip("Visual marker for Left Hand contact on wall surface.")]
    [SerializeField] private Transform _leftClimbMarker;

    [Tooltip("Visual marker for Right Hand contact on wall surface.")]
    [SerializeField] private Transform _rightClimbMarker;

    [Tooltip("Color of the wall aim marker when ready to grip.")]
    [SerializeField] private Color _markerColorReady = new Color(0.1f, 1.0f, 0.4f, 0.95f);

    [Tooltip("Color of the wall marker when stamina is exhausted.")]
    [SerializeField] private Color _markerColorExhausted = new Color(1.0f, 0.2f, 0.2f, 0.95f);

    [Header("Hand Lateral Spacing & Scanning")]
    [Tooltip("Maximum scan distance for camera raycasts.")]
    [SerializeField] private float _maxScanDistance = 8.0f;

    [Tooltip("Lateral spacing between left and right hands.")]
    [SerializeField] private float _handLateralSpacing = 0.28f;

    [Tooltip("Duration of the pull-up and slide transition onto the ground.")]
    [SerializeField] private float _pullUpDuration = 0.48f;

    // Cached components
    private PlayerMovement _movement;
    private CharacterController _characterController;
    private PlayerInputReader _inputReader;
    private PlayerCameraController _cameraController;
    private PlayerStamina _stamina;
    private PlayerCarry _playerCarry;
    private ProceduralPlayerArms _procArms;
    private Camera _cachedCamera;

    // Hand Transforms
    private Transform _leftHand;
    private Transform _rightHand;
    private readonly Vector3 _leftHandRest = new Vector3(-0.35f, 0.5f, 0.1f);
    private readonly Vector3 _rightHandRest = new Vector3(0.35f, 0.5f, 0.1f);

    // Marker renderers and material
    private Renderer _leftMarkerRenderer;
    private Renderer _rightMarkerRenderer;
    private Material _markerMaterial;
    private MaterialPropertyBlock _markerPropBlock;

    // Two-handed gripping state
    private bool _leftHandGripping = false;
    private bool _rightHandGripping = false;
    private Vector3 _leftGripPoint;
    private Vector3 _rightGripPoint;
    private Vector3 _leftGripNormal = Vector3.back;
    private Vector3 _rightGripNormal = Vector3.back;
    private Collider _leftGripCollider;
    private Collider _rightGripCollider;
    private Vector3 _leftGripLocalPoint;
    private Vector3 _rightGripLocalPoint;
    private Vector3 _leftGripLocalNormal;
    private Vector3 _rightGripLocalNormal;

    // Aim hits
    private bool _canGrabLeft = false;
    private bool _canGrabRight = false;
    private RaycastHit _leftAimHit;
    private RaycastHit _rightAimHit;
    private bool _isAimingAtWall = false;
    private bool _jumpPending;
    private readonly RaycastHit[] _surfaceHits = new RaycastHit[24];

    // Pull-up state
    private bool _isPullingUp = false;
    private float _pullUpTimer = 0f;
    private Vector3 _pullUpStartPos;
    private Vector3 _pullUpTargetPos;
    private Vector3 _pullUpLedgePoint;

    // Cooldown & Netcode
    private float _slipCooldownTimer = 0f;

    // Jump boost "require fresh press" — blocks re-grip until player releases & re-presses the button
    private bool _leftRequireFreshPress = false;
    private bool _rightRequireFreshPress = false;

    private float _leftHandCooldown = 0f;
    private float _rightHandCooldown = 0f;
    [Tooltip("How long after releasing a hand before it can grip again.")]
    [SerializeField] private float _handReleaseCooldown = 0.25f;

    // Bitmask for network sync: bit 0 = Left hand gripping, bit 1 = Right hand gripping
    private readonly NetworkVariable<byte> _netClimbHandState = new NetworkVariable<byte>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    // Public Events
    public event Action OnClimbStarted;
    public event Action OnClimbEnded;
    public event Action OnWallJumpBoost;
    public event Action OnClimbSlipped;

    // Public Properties
    public bool IsClimbing => IsOwner 
        ? (_leftHandGripping || _rightHandGripping || _isPullingUp) 
        : (_netClimbHandState.Value != 0);
    public bool LeftHandGripping => IsOwner 
        ? _leftHandGripping 
        : ((_netClimbHandState.Value & (1 << 0)) != 0);
    public bool RightHandGripping => IsOwner 
        ? _rightHandGripping 
        : ((_netClimbHandState.Value & (1 << 1)) != 0);
    public bool IsPullingUp => IsOwner 
        ? _isPullingUp 
        : ((_netClimbHandState.Value & (1 << 2)) != 0);
    public bool IsAimingAtWall => _isAimingAtWall;
    public LayerMask WallLayers => _wallLayers | (Physics.DefaultRaycastLayers & ~(1 << 5));

    private void Awake()
    {
        _movement = GetComponent<PlayerMovement>();
        _characterController = GetComponent<CharacterController>();
        _inputReader = GetComponent<PlayerInputReader>();
        _cameraController = GetComponent<PlayerCameraController>();
        _stamina = GetComponent<PlayerStamina>();
        _playerCarry = GetComponent<PlayerCarry>();
        _procArms = GetComponent<ProceduralPlayerArms>();

        EnsureMarkersCreated();
    }

    private void Start()
    {
        ResolveHandReferences();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ResolveHandReferences();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        HideMarkers();
        if (IsClimbing)
        {
            ReleaseAllGrips();
        }
    }

    private void OnDisable()
    {
        HideMarkers();
        if (IsClimbing)
        {
            ReleaseAllGrips();
        }
    }

    public override void OnDestroy()
    {
        try
        {
            base.OnDestroy();
        }
        catch { }

        if (_leftClimbMarker != null) Destroy(_leftClimbMarker.gameObject);
        if (_rightClimbMarker != null) Destroy(_rightClimbMarker.gameObject);
    }

    private void ResolveHandReferences()
    {
        if (_procArms == null) _procArms = GetComponent<ProceduralPlayerArms>();
        if (_procArms != null)
        {
            _procArms.EnsureTargetNodesCreated();
            _leftHand = _procArms.LeftHand;
            _rightHand = _procArms.RightHand;
        }
        else if (_playerCarry != null)
        {
            _playerCarry.EnsureVisualHandsCreated();
            _leftHand = _playerCarry.LeftHand;
            _rightHand = _playerCarry.RightHand;
        }

        if (_leftHand == null)
        {
            Transform existingLeft = transform.Find("IKTarget_Left") ?? transform.Find("VisualHand_Left");
            if (existingLeft != null) _leftHand = existingLeft;
        }

        if (_rightHand == null)
        {
            Transform existingRight = transform.Find("IKTarget_Right") ?? transform.Find("VisualHand_Right");
            if (existingRight != null) _rightHand = existingRight;
        }
    }

    private void Update()
    {
        bool isLocalOwner = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? IsOwner : true;

        if (!isLocalOwner)
        {
            UpdateProxyVisuals();
            return;
        }

        if (_leftClimbMarker == null || _rightClimbMarker == null)
            EnsureMarkersCreated();

        _jumpPending |= _inputReader != null && _inputReader.JumpTriggered;

        if (_isPullingUp)
        {
            UpdatePullUpVisuals();
            return;
        }

        UpdateHandMarkers();
        UpdateHandVisuals();
    }

    private void FixedUpdate()
    {
        bool isLocalOwner = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? IsOwner : true;
        if (!isLocalOwner)
            return;
        if (CoopGame.Network.PauseMenu.IsPaused)
        {
            _jumpPending = false;
            return;
        }

        if (_slipCooldownTimer > 0f)
        {
            _slipCooldownTimer -= Time.fixedDeltaTime;
        }

        if (_leftHandCooldown > 0f)
        {
            _leftHandCooldown -= Time.fixedDeltaTime;
        }

        if (_rightHandCooldown > 0f)
        {
            _rightHandCooldown -= Time.fixedDeltaTime;
        }

        // Clear "require fresh press" as soon as the button is physically released
        bool leftHeld  = (_inputReader != null) && (_inputReader.GrabLeftHeld  || _inputReader.InteractHeld);
        bool rightHeld = (_inputReader != null) && (_inputReader.GrabRightHeld || _inputReader.InteractHeld);
        if (_leftRequireFreshPress  && !leftHeld)  _leftRequireFreshPress  = false;
        if (_rightRequireFreshPress && !rightHeld) _rightRequireFreshPress = false;

        // 1. If pulling up onto top surface:
        if (_isPullingUp)
        {
            if (_jumpPending)
            {
                _jumpPending = false;
                ExecuteWallJumpBoost();
                return;
            }
            UpdatePullUp();
            SyncNetworkState();
            return;
        }

        RefreshGripAnchors();

        // 2. Scan for walls independently for Left and Right hands
        ScanHandAims(out _canGrabLeft, out _canGrabRight, out _leftAimHit, out _rightAimHit);
        _isAimingAtWall = (_canGrabLeft || _canGrabRight || _leftHandGripping || _rightHandGripping);

        // 3. Read cached player inputs
        bool leftClick = (_inputReader != null) && (_inputReader.GrabLeftHeld || _inputReader.InteractHeld);
        bool rightClick = (_inputReader != null) && (_inputReader.GrabRightHeld || _inputReader.InteractHeld);
        bool carryingItem = (_playerCarry != null && _playerCarry.IsCarrying);

        if (carryingItem || _slipCooldownTimer > 0f)
        {
            if (IsClimbing) ReleaseAllGrips();
            _jumpPending = false;
            return;
        }

        // Block re-gripping right after a wall jump boost until player releases & re-presses the button
        // (no timer — purely input-state driven so re-pressing always works)

        // 5. Left Hand Grip / Release (Gripping is contact-driven when arm reaches the wall)
        if (!leftClick && _leftHandGripping)
        {
            _leftHandGripping = false;
            _leftHandCooldown = _handReleaseCooldown;
            Debug.Log($"[Wallclimb] Client {OwnerClientId} released LEFT hand.");
        }

        // 6. Right Hand Grip / Release (Gripping is contact-driven when arm reaches the wall)
        if (!rightClick && _rightHandGripping)
        {
            _rightHandGripping = false;
            _rightHandCooldown = _handReleaseCooldown;
            Debug.Log($"[Wallclimb] Client {OwnerClientId} released RIGHT hand.");
        }

        // The visual hand approaches the contact in Update; only the physics step commits a grip.
        Vector3 chestPos = transform.position + Vector3.up * 1.15f;
        if (leftClick && !_leftHandGripping && _canGrabLeft && !_leftRequireFreshPress && _leftHandCooldown <= 0f)
        {
            Vector3 shoulder = chestPos - transform.right * (_handLateralSpacing * 0.5f);
            if (Vector3.Distance(shoulder, _leftAimHit.point) <= 1.05f ||
                (_leftHand != null && Vector3.Distance(_leftHand.position, _leftAimHit.point) <= 0.25f))
                GripLeftHand(_leftAimHit);
        }
        if (rightClick && !_rightHandGripping && _canGrabRight && !_rightRequireFreshPress && _rightHandCooldown <= 0f)
        {
            Vector3 shoulder = chestPos + transform.right * (_handLateralSpacing * 0.5f);
            if (Vector3.Distance(shoulder, _rightAimHit.point) <= 1.05f ||
                (_rightHand != null && Vector3.Distance(_rightHand.position, _rightAimHit.point) <= 0.25f))
                GripRightHand(_rightAimHit);
        }

        // 7. Process active climbing physics
        if (_leftHandGripping || _rightHandGripping)
        {
            // Set PlayerMovement climbing flag to disable downward gravity
            if (_movement != null)
            {
                _movement.IsClimbing = true;
            }

            // Check for Wall Jump Boost (Space bar)
            if (_jumpPending)
            {
                _jumpPending = false;
                ExecuteWallJumpBoost();
                return;
            }

            // Execute Human Fall Flat physics & pitch lifting
            ExecuteClimbPhysics();
        }
        else
        {
            if (_movement != null && _movement.IsClimbing)
            {
                _movement.IsClimbing = false;
            }
        }

        _jumpPending = false;

        // 8. Synchronize network state for proxies
        SyncNetworkState();
    }

    /// <summary>
    /// Scans independently for Left Hand and Right Hand aim points on climbable walls.
    /// Left hand aims: Up, Down, and Left (cannot cross far to the right).
    /// Right hand aims: Up, Down, and Right (cannot cross far to the left).
    /// </summary>
    private void ScanHandAims(out bool canLeft, out bool canRight, out RaycastHit leftHit, out RaycastHit rightHit)
    {
        canLeft = false;
        canRight = false;
        leftHit = default;
        rightHit = default;

        LocateCamera();

        Vector3 chestPos = transform.position + Vector3.up * 1.15f;
        GetAimDirections(out Vector3 leftAimDir, out Vector3 rightAimDir);

        bool notExhausted = (_stamina == null || !_stamina.IsExhausted);

        // -------------------------------------------------------------
        // LEFT HAND AIM:
        // -------------------------------------------------------------
        Vector3 leftShoulder = chestPos - transform.right * (_handLateralSpacing * 0.5f);

        if (TryFindSurfaceHit(leftShoulder, leftAimDir, false, out RaycastHit lHit) ||
            (_characterController != null && !_characterController.isGrounded &&
             TryFindSurfaceHit(leftShoulder, Vector3.ProjectOnPlane(leftAimDir, Vector3.up).normalized, true, out lHit)))
        {
            float dist = Vector3.Distance(leftShoulder, lHit.point);
            if (dist <= _handReachDistance && notExhausted)
            {
                canLeft = true;
                leftHit = lHit;
            }
        }

        // -------------------------------------------------------------
        // RIGHT HAND AIM:
        // -------------------------------------------------------------
        Vector3 rightShoulder = chestPos + transform.right * (_handLateralSpacing * 0.5f);

        if (TryFindSurfaceHit(rightShoulder, rightAimDir, false, out RaycastHit rHit) ||
            (_characterController != null && !_characterController.isGrounded &&
             TryFindSurfaceHit(rightShoulder, Vector3.ProjectOnPlane(rightAimDir, Vector3.up).normalized, true, out rHit)))
        {
            float dist = Vector3.Distance(rightShoulder, rHit.point);
            if (dist <= _handReachDistance && notExhausted)
            {
                canRight = true;
                rightHit = rHit;
            }
        }
    }

    private bool TryFindSurfaceHit(Vector3 origin, Vector3 direction, bool forgiving, out RaycastHit result)
    {
        result = default;
        if (direction.sqrMagnitude < 0.01f)
            return false;

        Ray ray = new Ray(origin, direction);
        int mask = WallLayers;
        float distance = Mathf.Min(_maxScanDistance, _handReachDistance);
        int count = forgiving
            ? Physics.SphereCastNonAlloc(ray, 0.12f, _surfaceHits, distance, mask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(ray, _surfaceHits, distance, mask, QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _surfaceHits[i];
            Collider col = hit.collider;
            if (col == null || col.transform.root == transform.root ||
                col.gameObject.layer == 5 ||
                col.GetComponentInParent<NetworkPlayer>() != null ||
                col.GetComponentInParent<CarryableObject>() != null ||
                (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) ||
                Mathf.Abs(hit.normal.y) > 0.7f)
                continue;

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                result = hit;
            }
        }
        return result.collider != null;
    }

    /// <summary>
    /// Computes safe aim directions for Left and Right hands.
    /// Strictly prevents arms from reaching backwards behind the torso,
    /// and smoothly redirects overhead or backward look into lifting straight up into the sky.
    /// </summary>
    private void GetAimDirections(out Vector3 aimDirL, out Vector3 aimDirR)
    {
        float pitch = (_cameraController != null) ? _cameraController.Pitch : 0f;
        float yaw = (_cameraController != null) ? _cameraController.Yaw : transform.eulerAngles.y;
        Vector3 camAimDir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

        Vector3 localAim = transform.InverseTransformDirection(camAimDir);

        // Anti-backward & sky-lift: arms never reach behind the character's back
        if (localAim.z < 0.1f || pitch < -5f)
        {
            if (pitch < -5f)
            {
                // Looking up: smoothly blend towards pure overhead reach into the sky
                float upFactor = Mathf.Clamp01(-pitch / 55f);
                localAim.y = Mathf.Lerp(localAim.y, 0.96f, upFactor);
                localAim.z = Mathf.Lerp(Mathf.Max(0.12f, localAim.z), 0.22f, upFactor);
            }
            else
            {
                // Aiming behind while standing: redirect to sky lift instead of reaching behind the spine
                localAim.y = Mathf.Max(localAim.y, 0.88f);
                localAim.z = 0.15f;
            }
        }

        // Left Hand Aim Direction (flared slightly left, never crossing right midline)
        Vector3 localAimL = localAim;
        localAimL.x = Mathf.Min(localAimL.x, 0.02f) - 0.08f;
        localAimL.Normalize();
        aimDirL = transform.TransformDirection(localAimL);

        // Right Hand Aim Direction (flared slightly right, never crossing left midline)
        Vector3 localAimR = localAim;
        localAimR.x = Mathf.Max(localAimR.x, -0.02f) + 0.08f;
        localAimR.Normalize();
        aimDirR = transform.TransformDirection(localAimR);
    }

    private void GripLeftHand(RaycastHit hit)
    {
        _leftHandGripping = true;
        _leftGripCollider = hit.collider;
        _leftGripLocalPoint = hit.collider.transform.InverseTransformPoint(hit.point);
        _leftGripLocalNormal = hit.collider.transform.InverseTransformDirection(hit.normal);
        _leftGripPoint = hit.point;
        _leftGripNormal = hit.normal;
        OnClimbStarted?.Invoke();
        Debug.Log($"[Wallclimb] Client {OwnerClientId} gripped wall with LEFT hand at {_leftGripPoint:F2}");
    }

    private void GripRightHand(RaycastHit hit)
    {
        _rightHandGripping = true;
        _rightGripCollider = hit.collider;
        _rightGripLocalPoint = hit.collider.transform.InverseTransformPoint(hit.point);
        _rightGripLocalNormal = hit.collider.transform.InverseTransformDirection(hit.normal);
        _rightGripPoint = hit.point;
        _rightGripNormal = hit.normal;
        OnClimbStarted?.Invoke();
        Debug.Log($"[Wallclimb] Client {OwnerClientId} gripped wall with RIGHT hand at {_rightGripPoint:F2}");
    }

    private void RefreshGripAnchors()
    {
        if (_leftHandGripping)
        {
            if (_leftGripCollider == null || !_leftGripCollider.enabled || !_leftGripCollider.gameObject.activeInHierarchy)
                _leftHandGripping = false;
            else
            {
                _leftGripPoint = _leftGripCollider.transform.TransformPoint(_leftGripLocalPoint);
                _leftGripNormal = _leftGripCollider.transform.TransformDirection(_leftGripLocalNormal).normalized;
            }
        }

        if (_rightHandGripping)
        {
            if (_rightGripCollider == null || !_rightGripCollider.enabled || !_rightGripCollider.gameObject.activeInHierarchy)
                _rightHandGripping = false;
            else
            {
                _rightGripPoint = _rightGripCollider.transform.TransformPoint(_rightGripLocalPoint);
                _rightGripNormal = _rightGripCollider.transform.TransformDirection(_rightGripLocalNormal).normalized;
            }
        }
    }

    /// <summary>
    /// Executes physical body hoisting/lowering based on W / S input (W = hoist UP, S = lower DOWN).
    /// </summary>
    private void ExecuteClimbPhysics()
    {
        float deltaTime = Time.fixedDeltaTime;

        // 1. Stamina Drain: One-handed hang strains muscles ~3x faster than two-handed!
        if (_stamina != null)
        {
            float drainRate = (_leftHandGripping && _rightHandGripping) ? _twoHandStaminaDrain : _oneHandStaminaDrain;
            _stamina.ConsumeStamina(drainRate * deltaTime);

            if (_stamina.IsExhausted)
            {
                TriggerSlipAndFall();
                return;
            }
        }

        Vector3 camRight = (_cameraController != null) ? _cameraController.HorizontalRight : transform.right;

        // 2. Determine anchor point and wall normal
        Vector3 anchor;
        Vector3 wallNormal;

        if (_leftHandGripping && _rightHandGripping)
        {
            anchor = (_leftGripPoint + _rightGripPoint) * 0.5f;
            wallNormal = ((_leftGripNormal + _rightGripNormal) * 0.5f).normalized;
        }
        else if (_leftHandGripping)
        {
            anchor = _leftGripPoint + camRight * 0.20f;
            wallNormal = _leftGripNormal;
        }
        else
        {
            anchor = _rightGripPoint - camRight * 0.20f;
            wallNormal = _rightGripNormal;
        }

        // 3. Calculate target body height: W or Looking UP hoists body UP; S or Looking DOWN lowers body
        float moveY = (_inputReader != null) ? _inputReader.MoveInput.y : 0f;
        float pitch = (_cameraController != null) ? _cameraController.Pitch : 0f;
        float currentHangDist;

        float upPull = 0f;
        if (moveY > 0.05f) upPull = Mathf.Max(upPull, moveY);
        if (pitch < -5f) upPull = Mathf.Max(upPull, Mathf.Clamp01(-pitch / 50f));

        float downPull = 0f;
        if (moveY < -0.05f) downPull = Mathf.Max(downPull, -moveY);
        if (pitch > 15f) downPull = Mathf.Max(downPull, Mathf.Clamp01((pitch - 15f) / 45f));

        if (upPull > 0.05f)
        {
            // Pulls body UP towards hands! (min hang distance)
            currentHangDist = Mathf.Lerp(_normalHangDistance, _minHangDistance, upPull);
        }
        else if (downPull > 0.05f)
        {
            // Lowers body away from hands! (max hang distance)
            currentHangDist = Mathf.Lerp(_normalHangDistance, _maxHangDistance, downPull);
        }
        else
        {
            // Neutral: Hang at natural default distance without camera pitch interference
            currentHangDist = _normalHangDistance;
        }

        // Target body position:
        Vector3 targetBodyPos;
        targetBodyPos.y = anchor.y - currentHangDist;

        // Anti-phasing standoff distance (keeps capsule strictly ~0.53m from wall face)
        float radius = (_characterController != null) ? _characterController.radius : 0.5f;
        float safeStandoff = radius + 0.04f;
        Vector3 targetHoriz = new Vector3(anchor.x, 0f, anchor.z) + wallNormal * safeStandoff;
        targetBodyPos.x = targetHoriz.x;
        targetBodyPos.z = targetHoriz.z;

        // Smooth displacement towards target body position
        Vector3 toTarget = targetBodyPos - transform.position;
        Vector3 moveStep = toTarget * (_bodyPullSpeed * deltaTime);
        moveStep = Vector3.ClampMagnitude(moveStep, 6.0f * deltaTime);

        if (_characterController != null && _characterController.enabled)
        {
            _characterController.Move(moveStep);
        }

        // Smoothly face towards the wall
        Vector3 faceDir = -wallNormal;
        faceDir.y = 0f;
        if (faceDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(faceDir.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, deltaTime * 12f);
        }
    }

    /// <summary>
    /// Executes a Wall Jump off the wall when Space bar is pressed.
    /// Always jumps straight UP along the wall with a slight forward nudge — no outward kick-off!
    /// </summary>
    private void ExecuteWallJumpBoost()
    {
        Vector3 wallNormal = (_leftHandGripping ? _leftGripNormal : _rightGripNormal);
        Vector3 wallForward = -wallNormal;
        wallForward.y = 0f;
        if (wallForward.sqrMagnitude > 0.01f)
        {
            wallForward.Normalize();
        }
        else
        {
            wallForward = transform.forward;
            wallForward.y = 0f;
            wallForward.Normalize();
        }

        // Release both hands and cancel any pull-up state
        _leftHandGripping = false;
        _rightHandGripping = false;
        _isPullingUp = false;

        // Always jump STRAIGHT UP with slight forward nudge
        Vector3 boostImpulse = Vector3.up * _jumpBoostUp + wallForward * 1.5f;

        // Allow re-gripping immediately so player can grab a higher spot at the peak of the jump
        _leftRequireFreshPress = false;
        _rightRequireFreshPress = false;
        _leftHandCooldown = 0.12f;  // Brief delay so it doesn't re-grip the exact same spot immediately
        _rightHandCooldown = 0.12f;

        if (_movement != null)
        {
            _movement.IsClimbing = false;
            _movement.ApplyImpulse(boostImpulse);
        }

        if (_stamina != null)
        {
            _stamina.ConsumeStamina(_jumpBoostStaminaCost);
        }

        OnWallJumpBoost?.Invoke();
        Debug.Log($"[Wallclimb] Client {OwnerClientId} WALL JUMP BOOST (straight up): impulse={boostImpulse}");
    }

    /// <summary>
    /// Checks if hands are at the top edge of the wall and player presses Space bar to pull up.
    /// Requires explicit Space bar press — no auto-jump from looking up or pressing W.
    /// </summary>
    private bool CheckForTopLedgePullUp()
    {
        Vector3 wallNormal = (_leftHandGripping ? _leftGripNormal : _rightGripNormal);
        Vector3 wallForward = -wallNormal;
        wallForward.y = 0f;
        wallForward.Normalize();

        // Downward raycast probe starting high above the player to find top flat ledge
        float probeHighY = transform.position.y + 2.6f;
        float radius = (_characterController != null) ? _characterController.radius : 0.5f;
        Vector3 probeOrigin = new Vector3(transform.position.x, probeHighY, transform.position.z)
                              + wallForward * (radius + 0.40f);
        int count = Physics.RaycastNonAlloc(probeOrigin, Vector3.down, _surfaceHits, 3.2f,
                                            WallLayers, QueryTriggerInteraction.Ignore);
        float handHeight = Mathf.Max(
            _leftHandGripping ? _leftGripPoint.y : float.NegativeInfinity,
            _rightHandGripping ? _rightGripPoint.y : float.NegativeInfinity);
        float nearest = float.MaxValue;
        RaycastHit ledgeHit = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _surfaceHits[i];
            Collider col = hit.collider;
            if (col == null || col.transform.root == transform.root || col.gameObject.layer == 5 ||
                col.GetComponentInParent<NetworkPlayer>() != null ||
                col.GetComponentInParent<CarryableObject>() != null ||
                (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) ||
                hit.normal.y < 0.7f || handHeight < hit.point.y - 0.55f ||
                hit.point.y < transform.position.y - 0.2f)
                continue;

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                ledgeHit = hit;
            }
        }
        if (ledgeHit.collider == null)
            return false;

        StartPullUp(ledgeHit, wallForward);
        return true;
    }

    private void StartPullUp(RaycastHit ledgeHit, Vector3 wallForward)
    {
        _isPullingUp = true;
        _pullUpTimer = 0f;
        _pullUpStartPos = transform.position;
        _pullUpLedgePoint = ledgeHit.point;

        float radius = (_characterController != null) ? _characterController.radius : 0.5f;
        Vector3 targetXZ = new Vector3(ledgeHit.point.x, 0f, ledgeHit.point.z) + wallForward * (radius + 0.20f);
        _pullUpTargetPos = new Vector3(targetXZ.x, ledgeHit.point.y + 0.05f, targetXZ.z);

        // Release physical wall grip points so hands can slide onto ground
        _leftHandGripping = false;
        _rightHandGripping = false;

        HideMarkers();
        OnWallJumpBoost?.Invoke(); // Consume the same buffered jump in NetworkPlayer.
        Debug.Log($"[Wallclimb] Client {OwnerClientId} PULLING UP onto top surface at Y={_pullUpTargetPos.y:F2}");
    }

    private void UpdatePullUp()
    {
        _pullUpTimer += Time.fixedDeltaTime;
        float t = Mathf.Clamp01(_pullUpTimer / Mathf.Max(0.01f, _pullUpDuration));

        // Vertical arc: rises smoothly above the ledge
        float heightArc = Mathf.Sin(t * Mathf.PI) * 0.12f;
        float currentY = Mathf.Lerp(_pullUpStartPos.y, _pullUpTargetPos.y, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t * 1.25f))) + heightArc;

        // Horizontal movement: slides forward onto the surface
        float horizT = Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, (t - 0.2f) / 0.8f));
        Vector3 startHoriz = new Vector3(_pullUpStartPos.x, 0f, _pullUpStartPos.z);
        Vector3 targetHoriz = new Vector3(_pullUpTargetPos.x, 0f, _pullUpTargetPos.z);
        Vector3 currentHoriz = Vector3.Lerp(startHoriz, targetHoriz, horizT);

        Vector3 desiredPos = new Vector3(currentHoriz.x, currentY, currentHoriz.z);
        Vector3 displacement = desiredPos - transform.position;

        if (_characterController != null && _characterController.enabled)
        {
            _characterController.Move(displacement);
        }

        if (t >= 1.0f)
        {
            _isPullingUp = false;
            if (_movement != null) _movement.IsClimbing = false;
            Debug.Log($"[Wallclimb] Client {OwnerClientId} successfully landed on top of the wall!");
        }
    }

    private void UpdatePullUpVisuals()
    {
        float t = Mathf.Clamp01(_pullUpTimer / Mathf.Max(0.01f, _pullUpDuration));
        Vector3 camFwd = (_cameraController != null) ? _cameraController.HorizontalForward : transform.forward;
        Vector3 camRight = (_cameraController != null) ? _cameraController.HorizontalRight : transform.right;

        // Animate hands sliding onto the top surface.
        if (_leftHand != null && _rightHand != null)
        {
            float handSlide = Mathf.Lerp(0f, 0.35f, t);
            Vector3 handBase = _pullUpLedgePoint + camFwd * handSlide + Vector3.up * 0.02f;
            Vector3 worldLeft = handBase - camRight * (_handLateralSpacing * 0.6f);
            Vector3 worldRight = handBase + camRight * (_handLateralSpacing * 0.6f);

            _leftHand.position = Vector3.Lerp(_leftHand.position, worldLeft, Time.deltaTime * 25f);
            _rightHand.position = Vector3.Lerp(_rightHand.position, worldRight, Time.deltaTime * 25f);
        }

    }

    /// <summary>
    /// Updates the visual transforms of Left and Right hands.
    /// In Human Fall Flat style:
    /// - Holding mouse button lifts and extends the arm along the 3D camera look direction (pitch & yaw).
    /// - Looking up lifts arms high above the head; looking down lowers them to the ground.
    /// - When the hand reaches physical contact with a wall surface, it grips the wall!
    /// - Gripping hand anchors directly to grip point on the wall.
    /// - Releasing button releases the grip and smoothly lowers the arm to rest.
    /// </summary>
    private void UpdateHandVisuals()
    {
        ResolveHandReferences();
        if (_leftHand == null || _rightHand == null) return;

        // If carrying an item, PlayerCarry controls the hands — do not interfere
        if (_playerCarry != null && _playerCarry.IsCarrying) return;

        bool leftHeld = (_inputReader != null) && (_inputReader.GrabLeftHeld || _inputReader.InteractHeld);
        bool rightHeld = (_inputReader != null) && (_inputReader.GrabRightHeld || _inputReader.InteractHeld);

        Vector3 chestPos = transform.position + Vector3.up * 1.15f;
        GetAimDirections(out Vector3 aimDirL, out Vector3 aimDirR);

        float pitch = (_cameraController != null) ? _cameraController.Pitch : 0f;
        float pitchRad = pitch * Mathf.Deg2Rad;
        // When looking down (positive pitch), arm reaches further toward feet; up = overhead reach
        float reachDist = 1.35f + Mathf.Clamp(pitchRad, -0.3f, 0.8f) * 0.4f;
        Vector3 restL = (_procArms != null) ? ProceduralPlayerArms.LeftHandRestLocal : _leftHandRest;
        Vector3 restR = (_procArms != null) ? ProceduralPlayerArms.RightHandRestLocal : _rightHandRest;

        // Calculate body-anchored shoulders
        Vector3 leftShoulder = chestPos - transform.right * (_handLateralSpacing * 0.5f);
        Vector3 rightShoulder = chestPos + transform.right * (_handLateralSpacing * 0.5f);

        // =========================================================================
        // LEFT HAND:
        // =========================================================================
        if (_leftHandGripping)
        {
            _leftHand.position = _leftGripPoint;
            if (_leftGripNormal.sqrMagnitude > 0.01f)
            {
                _leftHand.rotation = Quaternion.LookRotation(_leftGripNormal, Vector3.up);
            }
            if (_procArms != null)
            {
                _procArms.SetLeftHandTarget(_leftHand.position, _leftHand.rotation, 1.0f, true);
            }
        }
        else if (leftHeld)
        {
            Vector3 targetPos;
            Quaternion targetRot;

            if (_canGrabLeft)
            {
                // Wall detected along aim ray — reach towards the wall surface
                targetPos = _leftAimHit.point;
                targetRot = Quaternion.LookRotation(_leftAimHit.normal, Vector3.up);

                _leftHand.position = Vector3.Lerp(_leftHand.position, targetPos, Time.deltaTime * 24f);
                if (_leftAimHit.normal.sqrMagnitude > 0.01f)
                {
                    _leftHand.rotation = Quaternion.Slerp(_leftHand.rotation, targetRot, Time.deltaTime * 20f);
                }

            }
            else
            {
                // Free air reach: follows pitch/aim — never reaches behind body, lifts straight up to sky overhead
                targetPos = leftShoulder + aimDirL * reachDist;
                // Keep hand from clipping underground
                if (targetPos.y < transform.position.y + 0.05f)
                    targetPos.y = transform.position.y + 0.05f;
                Vector3 upHintL = (aimDirL.y > 0.88f) ? transform.forward : Vector3.up;
                targetRot = Quaternion.LookRotation(upHintL, aimDirL);

                _leftHand.position = Vector3.Lerp(_leftHand.position, targetPos, Time.deltaTime * 20f);
                _leftHand.rotation = Quaternion.Slerp(_leftHand.rotation, targetRot, Time.deltaTime * 18f);
            }

            if (_procArms != null)
            {
                _procArms.SetLeftHandTarget(_leftHand.position, _leftHand.rotation, 1.0f, false);
            }
        }
        else
        {
            // Button released: smoothly return arm to natural rest pose beside hips
            _leftHand.localPosition = Vector3.Lerp(_leftHand.localPosition, restL, Time.deltaTime * 14f);
            _leftHand.localRotation = Quaternion.Slerp(_leftHand.localRotation, Quaternion.identity, Time.deltaTime * 14f);
            if (_procArms != null)
            {
                _procArms.LeftWeight = Mathf.MoveTowards(_procArms.LeftWeight, 0f, Time.deltaTime * 10f);
            }
        }

        // =========================================================================
        // RIGHT HAND:
        // =========================================================================
        if (_rightHandGripping)
        {
            _rightHand.position = _rightGripPoint;
            if (_rightGripNormal.sqrMagnitude > 0.01f)
            {
                _rightHand.rotation = Quaternion.LookRotation(_rightGripNormal, Vector3.up);
            }
            if (_procArms != null)
            {
                _procArms.SetRightHandTarget(_rightHand.position, _rightHand.rotation, 1.0f, true);
            }
        }
        else if (rightHeld)
        {
            Vector3 targetPos;
            Quaternion targetRot;

            if (_canGrabRight)
            {
                // Wall detected along aim ray — reach towards the wall surface
                targetPos = _rightAimHit.point;
                targetRot = Quaternion.LookRotation(_rightAimHit.normal, Vector3.up);

                _rightHand.position = Vector3.Lerp(_rightHand.position, targetPos, Time.deltaTime * 24f);
                if (_rightAimHit.normal.sqrMagnitude > 0.01f)
                {
                    _rightHand.rotation = Quaternion.Slerp(_rightHand.rotation, targetRot, Time.deltaTime * 20f);
                }

            }
            else
            {
                // Free air reach: follows pitch/aim — never reaches behind body, lifts straight up to sky overhead
                targetPos = rightShoulder + aimDirR * reachDist;
                // Keep hand from clipping underground
                if (targetPos.y < transform.position.y + 0.05f)
                    targetPos.y = transform.position.y + 0.05f;
                Vector3 upHintR = (aimDirR.y > 0.88f) ? transform.forward : Vector3.up;
                targetRot = Quaternion.LookRotation(upHintR, aimDirR);

                _rightHand.position = Vector3.Lerp(_rightHand.position, targetPos, Time.deltaTime * 20f);
                _rightHand.rotation = Quaternion.Slerp(_rightHand.rotation, targetRot, Time.deltaTime * 18f);
            }

            if (_procArms != null)
            {
                _procArms.SetRightHandTarget(_rightHand.position, _rightHand.rotation, 1.0f, false);
            }
        }
        else
        {
            // Button released: smoothly return arm to natural rest pose beside hips
            _rightHand.localPosition = Vector3.Lerp(_rightHand.localPosition, restR, Time.deltaTime * 14f);
            _rightHand.localRotation = Quaternion.Slerp(_rightHand.localRotation, Quaternion.identity, Time.deltaTime * 14f);
            if (_procArms != null)
            {
                _procArms.RightWeight = Mathf.MoveTowards(_procArms.RightWeight, 0f, Time.deltaTime * 10f);
            }
        }
    }

    /// <summary>
    /// Updates Left and Right reticle markers on the wall surface.
    /// </summary>
    private void UpdateHandMarkers()
    {
        if (_leftClimbMarker == null || _rightClimbMarker == null) return;

        bool carryingItem = (_playerCarry != null && _playerCarry.IsCarrying);
        if (carryingItem || _isPullingUp)
        {
            HideMarkers();
            return;
        }

        bool isExhausted = (_stamina != null && _stamina.IsExhausted);
        Color col = isExhausted ? _markerColorExhausted : _markerColorReady;

        // LEFT MARKER:
        if (_leftHandGripping)
        {
            _leftClimbMarker.gameObject.SetActive(true);
            _leftClimbMarker.position = _leftGripPoint + _leftGripNormal * 0.012f;
            _leftClimbMarker.rotation = Quaternion.FromToRotation(Vector3.up, _leftGripNormal);
        }
        else if (_canGrabLeft)
        {
            _leftClimbMarker.gameObject.SetActive(true);
            _leftClimbMarker.position = _leftAimHit.point + _leftAimHit.normal * 0.012f;
            _leftClimbMarker.rotation = Quaternion.FromToRotation(Vector3.up, _leftAimHit.normal);
        }
        else
        {
            _leftClimbMarker.gameObject.SetActive(false);
        }

        // RIGHT MARKER:
        if (_rightHandGripping)
        {
            _rightClimbMarker.gameObject.SetActive(true);
            _rightClimbMarker.position = _rightGripPoint + _rightGripNormal * 0.012f;
            _rightClimbMarker.rotation = Quaternion.FromToRotation(Vector3.up, _rightGripNormal);
        }
        else if (_canGrabRight)
        {
            _rightClimbMarker.gameObject.SetActive(true);
            _rightClimbMarker.position = _rightAimHit.point + _rightAimHit.normal * 0.012f;
            _rightClimbMarker.rotation = Quaternion.FromToRotation(Vector3.up, _rightAimHit.normal);
        }
        else
        {
            _rightClimbMarker.gameObject.SetActive(false);
        }

        SetMarkersColor(col);
    }

    private void TriggerSlipAndFall()
    {
        Debug.Log($"[Wallclimb] Client {OwnerClientId} EXHAUSTED! Hands slipped from wall.");
        _slipCooldownTimer = 1.0f;
        ReleaseAllGrips();
        OnClimbSlipped?.Invoke();
    }

    private void ReleaseAllGrips()
    {
        _leftHandGripping = false;
        _rightHandGripping = false;
        _isPullingUp = false;

        if (_movement != null)
        {
            _movement.IsClimbing = false;
        }

        SyncNetworkState();
        OnClimbEnded?.Invoke();
    }

    public void ResetForRespawn()
    {
        if (!IsOwner) return;
        if (IsClimbing || (_movement != null && _movement.IsClimbing))
            ReleaseAllGrips();
        else
            SyncNetworkState();

        _jumpPending = false;
        _pullUpTimer = 0f;
        _canGrabLeft = false;
        _canGrabRight = false;
        _isAimingAtWall = false;
        _leftAimHit = default;
        _rightAimHit = default;
        _leftGripCollider = null;
        _rightGripCollider = null;
        _leftRequireFreshPress = _inputReader != null && (_inputReader.GrabLeftHeld || _inputReader.InteractHeld);
        _rightRequireFreshPress = _inputReader != null && (_inputReader.GrabRightHeld || _inputReader.InteractHeld);
        HideMarkers();
    }

    private void SyncNetworkState()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            byte mask = 0;
            if (_leftHandGripping) mask |= 1 << 0;
            if (_rightHandGripping) mask |= 1 << 1;
            if (_isPullingUp) mask |= 1 << 2;
            _netClimbHandState.Value = mask;
        }
    }

    private void UpdateProxyVisuals()
    {
        if (_leftHand == null || _rightHand == null) return;

        byte mask = _netClimbHandState.Value;
        bool leftGrip = (mask & (1 << 0)) != 0;
        bool rightGrip = (mask & (1 << 1)) != 0;

        Vector3 targetLeft = leftGrip ? new Vector3(-_handLateralSpacing, 1.25f, 0.52f) : _leftHandRest;
        Vector3 targetRight = rightGrip ? new Vector3(_handLateralSpacing, 1.25f, 0.52f) : _rightHandRest;

        Quaternion climbRot = Quaternion.LookRotation(-transform.forward, Vector3.up);

        _leftHand.localPosition = Vector3.Lerp(_leftHand.localPosition, targetLeft, Time.deltaTime * 20f);
        _rightHand.localPosition = Vector3.Lerp(_rightHand.localPosition, targetRight, Time.deltaTime * 20f);

        if (_procArms != null)
        {
            if (leftGrip) _procArms.SetLeftHandTarget(_leftHand.position, climbRot, 1.0f, true);
            if (rightGrip) _procArms.SetRightHandTarget(_rightHand.position, climbRot, 1.0f, true);
        }
    }

    private void HideMarkers()
    {
        if (_leftClimbMarker != null) _leftClimbMarker.gameObject.SetActive(false);
        if (_rightClimbMarker != null) _rightClimbMarker.gameObject.SetActive(false);
    }

    private void SetMarkersColor(Color color)
    {
        if (_markerPropBlock == null)
        {
            _markerPropBlock = new MaterialPropertyBlock();
        }

        _markerPropBlock.SetColor("_BaseColor", color);
        _markerPropBlock.SetColor("_Color", color);

        if (_leftMarkerRenderer != null) _leftMarkerRenderer.SetPropertyBlock(_markerPropBlock);
        if (_rightMarkerRenderer != null) _rightMarkerRenderer.SetPropertyBlock(_markerPropBlock);

        if (_markerMaterial != null)
        {
            _markerMaterial.SetColor("_BaseColor", color);
            _markerMaterial.SetColor("_Color", color);
            _markerMaterial.color = color;
        }
    }

    private void EnsureMarkersCreated()
    {
        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit")
                           ?? Shader.Find("Universal Render Pipeline/Lit")
                           ?? Shader.Find("Unlit/Color")
                           ?? Shader.Find("Sprites/Default");

        if (_markerMaterial == null && unlitShader != null)
        {
            _markerMaterial = new Material(unlitShader);
        }

        if (_leftClimbMarker == null)
        {
                GameObject lObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                lObj.name = "LeftWallClimbMarker";
                lObj.transform.SetParent(transform, false);
                lObj.transform.localScale = new Vector3(0.14f, 0.003f, 0.14f);
            Collider col = lObj.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            _leftMarkerRenderer = lObj.GetComponent<Renderer>();
            if (_leftMarkerRenderer != null)
            {
                _leftMarkerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _leftMarkerRenderer.receiveShadows = false;
                if (_markerMaterial != null) _leftMarkerRenderer.material = _markerMaterial;
            }
            _leftClimbMarker = lObj.transform;
        }
        else
        {
            _leftMarkerRenderer = _leftClimbMarker.GetComponent<Renderer>();
        }

        if (_rightClimbMarker == null)
        {
                GameObject rObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                rObj.name = "RightWallClimbMarker";
                rObj.transform.SetParent(transform, false);
                rObj.transform.localScale = new Vector3(0.14f, 0.003f, 0.14f);
            Collider col = rObj.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            _rightMarkerRenderer = rObj.GetComponent<Renderer>();
            if (_rightMarkerRenderer != null)
            {
                _rightMarkerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _rightMarkerRenderer.receiveShadows = false;
                if (_markerMaterial != null) _rightMarkerRenderer.material = _markerMaterial;
            }
            _rightClimbMarker = rObj.transform;
        }
        else
        {
            _rightMarkerRenderer = _rightClimbMarker.GetComponent<Renderer>();
        }

        SetMarkersColor(_markerColorReady);
        HideMarkers();
    }

    private void LocateCamera()
    {
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
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 chestPos = transform.position + Vector3.up * 1.15f;
        Gizmos.DrawWireSphere(chestPos, _handReachDistance);

        if (_leftHandGripping)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(_leftGripPoint, 0.1f);
        }
        if (_rightHandGripping)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(_rightGripPoint, 0.1f);
        }
    }
}
