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
///    - Pressing Space (Jump) while holding onto the wall triggers a powerful wall jump boost leaping off the wall!
/// 5. Edge Hold & Top-of-Wall Pull-Up:
///    - When reaching the top edge, hands hold onto the edge lip first.
///    - Looking up and pushing forward slides / pulls the character safely onto the top of the wall without phasing.
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

    [Tooltip("Stamina drained per second while holding with both hands.")]
    [SerializeField] private float _twoHandStaminaDrain = 8.0f;

    [Tooltip("Stamina drained per second while holding with only one hand.")]
    [SerializeField] private float _oneHandStaminaDrain = 22.0f;

    [Tooltip("Layer mask representing climbable walls.")]
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

    [Tooltip("Stamina cost for executing a wall jump boost.")]
    [SerializeField] private float _jumpBoostStaminaCost = 14.0f;

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

    // Aim hits
    private bool _canGrabLeft = false;
    private bool _canGrabRight = false;
    private RaycastHit _leftAimHit;
    private RaycastHit _rightAimHit;
    private bool _isAimingAtWall = false;

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
    public bool IsClimbing => (_leftHandGripping || _rightHandGripping || _isPullingUp);
    public bool LeftHandGripping => _leftHandGripping;
    public bool RightHandGripping => _rightHandGripping;
    public bool IsPullingUp => _isPullingUp;
    public bool IsAimingAtWall => _isAimingAtWall;
    public LayerMask WallLayers => _wallLayers;

    private void Awake()
    {
        _movement = GetComponent<PlayerMovement>();
        _characterController = GetComponent<CharacterController>();
        _inputReader = GetComponent<PlayerInputReader>();
        _cameraController = GetComponent<PlayerCameraController>();
        _stamina = GetComponent<PlayerStamina>();
        _playerCarry = GetComponent<PlayerCarry>();

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
        base.OnDestroy();
        if (_leftClimbMarker != null) Destroy(_leftClimbMarker.gameObject);
        if (_rightClimbMarker != null) Destroy(_rightClimbMarker.gameObject);
    }

    private void ResolveHandReferences()
    {
        if (_playerCarry != null)
        {
            _playerCarry.EnsureVisualHandsCreated();
            _leftHand = _playerCarry.LeftHand;
            _rightHand = _playerCarry.RightHand;
        }

        if (_leftHand == null)
        {
            Transform existingLeft = transform.Find("VisualHand_Left");
            if (existingLeft != null) _leftHand = existingLeft;
        }

        if (_rightHand == null)
        {
            Transform existingRight = transform.Find("VisualHand_Right");
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

        if (_slipCooldownTimer > 0f)
        {
            _slipCooldownTimer -= Time.deltaTime;
        }

        if (_leftHandCooldown > 0f)
        {
            _leftHandCooldown -= Time.deltaTime;
        }

        if (_rightHandCooldown > 0f)
        {
            _rightHandCooldown -= Time.deltaTime;
        }

        // Clear "require fresh press" as soon as the button is physically released
        bool leftHeld  = (_inputReader != null) && (_inputReader.GrabLeftHeld  || _inputReader.InteractHeld);
        bool rightHeld = (_inputReader != null) && (_inputReader.GrabRightHeld || _inputReader.InteractHeld);
        if (_leftRequireFreshPress  && !leftHeld)  _leftRequireFreshPress  = false;
        if (_rightRequireFreshPress && !rightHeld) _rightRequireFreshPress = false;

        // 1. If pulling up onto top surface:
        if (_isPullingUp)
        {
            UpdatePullUp();
            return;
        }

        // 2. Scan for walls independently for Left and Right hands
        ScanHandAims(out _canGrabLeft, out _canGrabRight, out _leftAimHit, out _rightAimHit);
        _isAimingAtWall = (_canGrabLeft || _canGrabRight || _leftHandGripping || _rightHandGripping);

        // 3. Update reticle markers
        UpdateHandMarkers();

        // 4. Read player inputs
        bool leftClick = (_inputReader != null) && (_inputReader.GrabLeftHeld || _inputReader.InteractHeld);
        bool rightClick = (_inputReader != null) && (_inputReader.GrabRightHeld || _inputReader.InteractHeld);
        bool carryingItem = (_playerCarry != null && _playerCarry.IsCarrying);

        if (carryingItem || _slipCooldownTimer > 0f)
        {
            if (IsClimbing) ReleaseAllGrips();
            return;
        }

        // Block re-gripping right after a wall jump boost until player releases & re-presses the button
        // (no timer — purely input-state driven so re-pressing always works)

        // 5. Left Hand Grip / Release
        if (leftClick && !_leftHandGripping && _canGrabLeft && !_leftRequireFreshPress && _leftHandCooldown <= 0f)
        {
            GripLeftHand(_leftAimHit);
        }
        else if (!leftClick && _leftHandGripping)
        {
            _leftHandGripping = false;
            _leftHandCooldown = _handReleaseCooldown;
            Debug.Log($"[Wallclimb] Client {OwnerClientId} released LEFT hand.");
        }

        // 6. Right Hand Grip / Release
        if (rightClick && !_rightHandGripping && _canGrabRight && !_rightRequireFreshPress && _rightHandCooldown <= 0f)
        {
            GripRightHand(_rightAimHit);
        }
        else if (!rightClick && _rightHandGripping)
        {
            _rightHandGripping = false;
            _rightHandCooldown = _handReleaseCooldown;
            Debug.Log($"[Wallclimb] Client {OwnerClientId} released RIGHT hand.");
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
            if (_inputReader != null && _inputReader.JumpTriggered)
            {
                ExecuteWallJumpBoost();
                return;
            }

            // Check for Top-of-Wall Pull-Up
            if (CheckForTopLedgePullUp())
            {
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

        // 8. Update hand visual transforms
        UpdateHandVisuals();

        // 9. Synchronize network state for proxies
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
        Vector3 camFwd = (_cameraController != null) ? _cameraController.HorizontalForward : transform.forward;
        Vector3 camRight = (_cameraController != null) ? _cameraController.HorizontalRight : transform.right;
        float pitch = (_cameraController != null) ? _cameraController.Pitch : 0f;
        float yaw = (_cameraController != null) ? _cameraController.Yaw : transform.eulerAngles.y;

        // Camera aim vector in 3D
        Vector3 camAimDir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

        bool notExhausted = (_stamina == null || !_stamina.IsExhausted);

        // -------------------------------------------------------------
        // LEFT HAND AIM (Up, Down, Left):
        // -------------------------------------------------------------
        Vector3 leftShoulder = chestPos - camRight * (_handLateralSpacing * 0.5f);
        Vector3 leftAimDir = camAimDir - camRight * 0.15f;
        // Constraint: Left hand cannot aim significantly to the right side!
        float leftRightDot = Vector3.Dot(leftAimDir, camRight);
        if (leftRightDot > 0.05f)
        {
            leftAimDir = Vector3.ProjectOnPlane(leftAimDir, camRight).normalized;
        }
        leftAimDir.Normalize();

        Ray leftRay = new Ray(leftShoulder, leftAimDir);
        if (Physics.Raycast(leftRay, out RaycastHit lHit, _maxScanDistance, _wallLayers, QueryTriggerInteraction.Ignore))
        {
            if (lHit.collider.transform.root != transform.root && Vector3.Angle(lHit.normal, Vector3.up) >= 45f)
            {
                float dist = Vector3.Distance(leftShoulder, lHit.point);
                if (dist <= _handReachDistance && notExhausted)
                {
                    canLeft = true;
                    leftHit = lHit;
                }
            }
        }

        // -------------------------------------------------------------
        // RIGHT HAND AIM (Up, Down, Right):
        // -------------------------------------------------------------
        Vector3 rightShoulder = chestPos + camRight * (_handLateralSpacing * 0.5f);
        Vector3 rightAimDir = camAimDir + camRight * 0.15f;
        // Constraint: Right hand cannot aim significantly to the left side!
        float rightLeftDot = Vector3.Dot(rightAimDir, -camRight);
        if (rightLeftDot > 0.05f)
        {
            rightAimDir = Vector3.ProjectOnPlane(rightAimDir, camRight).normalized;
        }
        rightAimDir.Normalize();

        Ray rightRay = new Ray(rightShoulder, rightAimDir);
        if (Physics.Raycast(rightRay, out RaycastHit rHit, _maxScanDistance, _wallLayers, QueryTriggerInteraction.Ignore))
        {
            if (rHit.collider.transform.root != transform.root && Vector3.Angle(rHit.normal, Vector3.up) >= 45f)
            {
                float dist = Vector3.Distance(rightShoulder, rHit.point);
                if (dist <= _handReachDistance && notExhausted)
                {
                    canRight = true;
                    rightHit = rHit;
                }
            }
        }
    }

    private void GripLeftHand(RaycastHit hit)
    {
        _leftHandGripping = true;
        _leftGripPoint = hit.point;
        _leftGripNormal = hit.normal;
        OnClimbStarted?.Invoke();
        Debug.Log($"[Wallclimb] Client {OwnerClientId} gripped wall with LEFT hand at {_leftGripPoint:F2}");
    }

    private void GripRightHand(RaycastHit hit)
    {
        _rightHandGripping = true;
        _rightGripPoint = hit.point;
        _rightGripNormal = hit.normal;
        OnClimbStarted?.Invoke();
        Debug.Log($"[Wallclimb] Client {OwnerClientId} gripped wall with RIGHT hand at {_rightGripPoint:F2}");
    }

    /// <summary>
    /// Executes Human Fall Flat physical body hoisting/lowering based on camera pitch.
    /// When looking UP, body pulls UP towards hands. When looking DOWN, body lowers.
    /// </summary>
    private void ExecuteClimbPhysics()
    {
        float deltaTime = Time.deltaTime;

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

        // 3. Calculate target body height from camera pitch (Mouse Up / Down)
        float pitch = (_cameraController != null) ? _cameraController.Pitch : 0f;
        float currentHangDist;

        if (pitch < 0f)
        {
            // Looking UP (pitch negative, -35): pulls body UP towards hands!
            float t = Mathf.InverseLerp(0f, -35f, pitch);
            currentHangDist = Mathf.Lerp(_normalHangDistance, _minHangDistance, t);
        }
        else
        {
            // Looking DOWN (pitch positive, +55): lowers body away from hands
            float t = Mathf.InverseLerp(0f, 55f, pitch);
            currentHangDist = Mathf.Lerp(_normalHangDistance, _maxHangDistance, t);
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
    /// Executes a powerful Wall Jump Boost off the wall when Space bar is pressed.
    /// </summary>
    private void ExecuteWallJumpBoost()
    {
        Vector3 camFwd = (_cameraController != null) ? _cameraController.HorizontalForward : transform.forward;
        Vector3 wallNormal = (_leftHandGripping ? _leftGripNormal : _rightGripNormal);

        // Release both hands — require player to release & re-press before gripping again
        _leftHandGripping = false;
        _rightHandGripping = false;
        _leftRequireFreshPress  = true;
        _rightRequireFreshPress = true;

        // Direction: Upward + away from wall in look direction
        Vector3 jumpDir = (Vector3.up * 1.35f + camFwd * 0.65f + wallNormal * 0.45f).normalized;
        Vector3 boostImpulse = jumpDir * _jumpBoostUp + wallNormal * _jumpBoostOut;

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
        Debug.Log($"[Wallclimb] Client {OwnerClientId} WALL JUMP BOOST executed! Impulse: {boostImpulse}");
    }

    /// <summary>
    /// Checks if hands are at the top edge of the wall and player is looking up/pushing forward to pull up.
    /// </summary>
    private bool CheckForTopLedgePullUp()
    {
        Vector3 wallNormal = (_leftHandGripping ? _leftGripNormal : _rightGripNormal);
        Vector3 wallForward = -wallNormal;
        wallForward.y = 0f;
        wallForward.Normalize();

        // Downward raycast probe starting high above the player to find top flat ledge
        float probeHighY = transform.position.y + 2.6f;
        Vector3 probeOrigin = new Vector3(transform.position.x, probeHighY, transform.position.z) + wallForward * 0.40f;

        if (Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit ledgeHit, 3.2f, _wallLayers | (1 << 0), QueryTriggerInteraction.Ignore))
        {
            if (Vector3.Angle(ledgeHit.normal, Vector3.up) <= 45f)
            {
                float ledgeHeight = ledgeHit.point.y;
                float handHeight = Mathf.Max(
                    _leftHandGripping ? _leftGripPoint.y : 0f,
                    _rightHandGripping ? _rightGripPoint.y : 0f
                );

                // If hands are within 0.25m of ledge height or above it:
                if (handHeight >= (ledgeHeight - 0.35f))
                {
                    float pitch = (_cameraController != null) ? _cameraController.Pitch : 0f;
                    bool moveForward = (_inputReader != null && _inputReader.MoveInput.y > 0.2f);
                    bool lookingUp = (pitch < -12f);

                    // If looking up or pressing forward, pull up onto the top!
                    if (lookingUp || moveForward)
                    {
                        StartPullUp(ledgeHit, wallForward);
                        return true;
                    }
                }
            }
        }

        return false;
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
        Debug.Log($"[Wallclimb] Client {OwnerClientId} PULLING UP onto top surface at Y={_pullUpTargetPos.y:F2}");
    }

    private void UpdatePullUp()
    {
        _pullUpTimer += Time.deltaTime;
        float t = Mathf.Clamp01(_pullUpTimer / Mathf.Max(0.01f, _pullUpDuration));

        Vector3 camFwd = (_cameraController != null) ? _cameraController.HorizontalForward : transform.forward;
        Vector3 camRight = (_cameraController != null) ? _cameraController.HorizontalRight : transform.right;

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

        // Animate hands sliding onto the top surface
        if (_leftHand != null && _rightHand != null)
        {
            float handSlide = Mathf.Lerp(0f, 0.35f, t);
            Vector3 handBase = _pullUpLedgePoint + camFwd * handSlide + Vector3.up * 0.02f;
            Vector3 worldLeft = handBase - camRight * (_handLateralSpacing * 0.6f);
            Vector3 worldRight = handBase + camRight * (_handLateralSpacing * 0.6f);

            _leftHand.position = Vector3.Lerp(_leftHand.position, worldLeft, Time.deltaTime * 25f);
            _rightHand.position = Vector3.Lerp(_rightHand.position, worldRight, Time.deltaTime * 25f);
        }

        if (t >= 1.0f)
        {
            _isPullingUp = false;
            if (_movement != null) _movement.IsClimbing = false;
            Debug.Log($"[Wallclimb] Client {OwnerClientId} successfully landed on top of the wall!");
        }
    }

    /// <summary>
    /// Updates the visual transforms of Left and Right hands.
    /// Gripping hand anchors directly to grip point on the wall.
    /// Free hand follows aim reach or rests.
    /// </summary>
    private void UpdateHandVisuals()
    {
        ResolveHandReferences();
        if (_leftHand == null || _rightHand == null) return;

        // LEFT HAND:
        if (_leftHandGripping)
        {
            _leftHand.position = _leftGripPoint;
        }
        else if (_leftHandCooldown <= 0f && !_leftRequireFreshPress && _canGrabLeft && _inputReader != null && (_inputReader.GrabLeftHeld || _inputReader.InteractHeld))
        {
            // Only reach toward wall when NOT on cooldown
            _leftHand.position = Vector3.Lerp(_leftHand.position, _leftAimHit.point, Time.deltaTime * 25f);
        }
        else
        {
            // On cooldown (or no wall) — pull hand back to rest
            _leftHand.localPosition = Vector3.Lerp(_leftHand.localPosition, _leftHandRest, Time.deltaTime * 18f);
        }

        // RIGHT HAND:
        if (_rightHandGripping)
        {
            _rightHand.position = _rightGripPoint;
        }
        else if (_rightHandCooldown <= 0f && !_rightRequireFreshPress && _canGrabRight && _inputReader != null && (_inputReader.GrabRightHeld || _inputReader.InteractHeld))
        {
            // Only reach toward wall when NOT on cooldown
            _rightHand.position = Vector3.Lerp(_rightHand.position, _rightAimHit.point, Time.deltaTime * 25f);
        }
        else
        {
            // On cooldown (or no wall) — pull hand back to rest
            _rightHand.localPosition = Vector3.Lerp(_rightHand.localPosition, _rightHandRest, Time.deltaTime * 18f);
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

        _leftHand.localPosition = Vector3.Lerp(_leftHand.localPosition, targetLeft, Time.deltaTime * 20f);
        _rightHand.localPosition = Vector3.Lerp(_rightHand.localPosition, targetRight, Time.deltaTime * 20f);
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
