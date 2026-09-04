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
        [SerializeField] private float _grabContactDistance = 2.8f;

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

        // Cached components
        private PlayerInputReader _inputReader;
        private PlayerMovement _movement;
        private CharacterController _characterController;
        private PlayerCameraController _cameraController;
        private Collider[] _playerColliders;
        private Camera _cachedCamera;

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

        // Exposed properties
        public bool IsCarrying => _leftHandGripping || _rightHandGripping;
        public bool LeftHandGripping => _leftHandGripping;
        public bool RightHandGripping => _rightHandGripping;
        public CarryableObject CurrentCarryable => _currentCarryable;

        private void Awake()
        {
            _inputReader = GetComponent<PlayerInputReader>();
            _movement = GetComponent<PlayerMovement>();
            _characterController = GetComponent<CharacterController>();
            _cameraController = GetComponent<PlayerCameraController>();
            _playerColliders = GetComponentsInChildren<Collider>(true);

            EnsureVisualHandsCreated();
            EnsureDualMarkersCreated();
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            HideMarkers();

            if (IsCarrying)
            {
                ReleaseCarryState();
            }
        }

        private void Update()
        {
            bool isLocalOwner = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? IsOwner : true;
            if (!isLocalOwner) return;

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

            // 4. Animate procedural hands to touch contact points or reach/rest
            UpdateVisualHands(currentHoldHeight);

            // 5. Independent Hand Input Checking (Human Fall Flat style)
            bool leftClick = _inputReader.GrabLeftHeld || _inputReader.InteractHeld;
            bool rightClick = _inputReader.GrabRightHeld || _inputReader.InteractHeld;

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

            // Grab with Left Hand if clicked and pointing at reachable object
            if (leftClick && !_leftHandGripping)
            {
                if (canGrabAimed && aimedCarryable != null)
                {
                    GrabSingleHand(aimedCarryable, isLeft: true);
                }
                else if (_currentCarryable == null)
                {
                    TryGrabNearbyObjectSingleHand(isLeft: true);
                }
            }

            // Grab with Right Hand if clicked and pointing at reachable object
            if (rightClick && !_rightHandGripping)
            {
                if (canGrabAimed && aimedCarryable != null)
                {
                    GrabSingleHand(aimedCarryable, isLeft: false);
                }
                else if (_currentCarryable == null)
                {
                    TryGrabNearbyObjectSingleHand(isLeft: false);
                }
            }

            // 6. Handle active carrying state or complete drop
            if (_leftHandGripping || _rightHandGripping)
            {
                // Full 100% free movement like Human Fall Flat!
                if (_movement != null)
                {
                    _movement.SpeedMultiplier = 1.0f;
                }

                if (_currentCarryable != null)
                {
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
                        Debug.Log($"[PlayerCarry] Carrying '{_currentCarryable.name}' with {handMode} | LiftHeight: {currentHoldHeight:F2}m | Mass: {_currentCarryable.TotalMass:F1}kg | PhysX Gravity: ACTIVE | Velocity: {currentVel.magnitude:F2}m/s");
                    }
                }
            }
            else
            {
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
        }

        private void GrabSingleHand(CarryableObject target, bool isLeft)
        {
            if (target == null) return;

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
                        canGrabAimed = true;
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

            SetMarkersColor(_colorLiftableGreen);
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

            bool leftClick = _inputReader.GrabLeftHeld || _inputReader.InteractHeld;
            bool rightClick = _inputReader.GrabRightHeld || _inputReader.InteractHeld;

            Vector3 targetLeftPos;
            Vector3 targetRightPos;

            // Left Hand: if gripping, attach to box. Else if holding click, reach forward. Else rest.
            if (_leftHandGripping && _currentCarryable != null)
            {
                Vector3 worldLeft = _currentCarryable.transform.TransformPoint(_currentLocalContactLeft);
                targetLeftPos = transform.InverseTransformPoint(worldLeft);
            }
            else if (leftClick)
            {
                targetLeftPos = new Vector3(-0.25f, currentHeight, 0.75f);
            }
            else
            {
                targetLeftPos = _leftHandRest;
            }

            // Right Hand: if gripping, attach to box. Else if holding click, reach forward. Else rest.
            if (_rightHandGripping && _currentCarryable != null)
            {
                Vector3 worldRight = _currentCarryable.transform.TransformPoint(_currentLocalContactRight);
                targetRightPos = transform.InverseTransformPoint(worldRight);
            }
            else if (rightClick)
            {
                targetRightPos = new Vector3(0.25f, currentHeight, 0.75f);
            }
            else
            {
                targetRightPos = _rightHandRest;
            }

            _leftHand.localPosition = Vector3.Lerp(_leftHand.localPosition, targetLeftPos, Time.deltaTime * 30f);
            _rightHand.localPosition = Vector3.Lerp(_rightHand.localPosition, targetRightPos, Time.deltaTime * 30f);
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

            if (dist > _grabContactDistance * 2.5f) return;

            if (carryable.TryAttachCarrier(OwnerClientId, transform, _movement, localContactLeft, localContactRight, leftActive, rightActive, out int socketIndex))
            {
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
            }

            _leftHandGripping = false;
            _rightHandGripping = false;
            _currentCarryable = null;
            _assignedSocketIndex = -1;
            _currentLocalContactLeft = new Vector3(-0.25f, 0f, -0.4f);
            _currentLocalContactRight = new Vector3(0.25f, 0f, -0.4f);

            if (_movement != null && IsOwner)
            {
                _movement.SpeedMultiplier = 1.0f;
            }

            Debug.Log($"[PlayerCarry] Client {OwnerClientId} released '{releasedName}'. Returning to free PhysX gravity & momentum.");
        }

        private void EnsureVisualHandsCreated()
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
            base.OnDestroy();
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
