using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CoopGame.Player
{
    /// <summary>
    /// PlayerCameraController manages the 3rd-person follow/orbit camera and 1st-person view.
    /// 
    /// Features:
    /// 1. First-Person (1st Person) and Third-Person (3rd Person) Seamless Zooming:
    ///    - Mouse Scroll Wheel: Smoothly zooms between 1st Person (0.0m) and 3rd Person (up to 6.0m).
    ///    - Quick Toggle Key: Press 'V' or Middle Mouse Button to instantly toggle between 1st Person and 3rd Person!
    ///    - In 1st Person (Distance < 0.3m): Camera sits at eye level. Player body mesh becomes shadows-only
    ///      to prevent clipping, while procedural hands remain fully visible for precision object placement!
    /// 2. Obstacle Collision Avoidance:
    ///    - Smoothly clips forward if geometry blocks the view, and ignores the player's own colliders.
    /// 3. Stability:
    ///    - Decoupled from player transform at runtime to eliminate rotation jitter.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(60)]
    public class PlayerCameraController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The actual Camera component to control.")]
        [SerializeField] private Camera _playerCamera;

        [Tooltip("The AudioListener attached to the camera.")]
        [SerializeField] private AudioListener _audioListener;

        [Tooltip("Transform representing the camera's focus pivot point (player head/eyes).")]
        [SerializeField] private Transform _cameraPivot;

        [Tooltip("The main mesh renderer of the player body (hidden in 1st person to prevent clipping).")]
        [SerializeField] private Renderer _playerBodyRenderer;

        [Header("Orbit & Sensitivity")]
        [Tooltip("Sensitivity multiplier for mouse/right-stick input")]
        [SerializeField] private float _sensitivity = 0.2f;

        [Tooltip("Minimum vertical angle in degrees (looking up)")]
        [SerializeField] private float _minPitch = -65f;

        [Tooltip("Maximum vertical angle in degrees (looking down)")]
        [SerializeField] private float _maxPitch = 70f;

        [Header("1st & 3rd Person Zoom Distances")]
        [Tooltip("1st Person Distance (0m = exactly at eye level)")]
        [SerializeField] private float _firstPersonDistance = 0.0f;

        [Tooltip("Default 3rd Person follow distance")]
        [SerializeField] private float _defaultThirdPersonDistance = 3.5f;

        [Tooltip("Maximum 3rd Person zoom-out distance")]
        [SerializeField] private float _maxZoomDistance = 6.0f;

        [Tooltip("Threshold below which the camera switches to First-Person view")]
        [SerializeField] private float _firstPersonThreshold = 0.35f;

        [Tooltip("Zoom transition speed")]
        [SerializeField] private float _zoomSpeed = 8.0f;

        [Tooltip("Scroll sensitivity for mouse wheel zoom")]
        [SerializeField] private float _scrollSensitivity = 0.6f;

        [Header("Height & Collision Avoidance")]
        [Tooltip("Height offset above the pivot transform (eye level)")]
        [SerializeField] private float _pivotHeightOffset = 0.50f;

        [Tooltip("Sphere radius used to check for environmental occlusion")]
        [SerializeField] private float _collisionRadius = 0.22f;

        [Tooltip("Layer mask containing obstacles that the camera should collide with")]
        [SerializeField] private LayerMask _obstacleLayers = ~0;

        // Current spherical coordinate angles and distances
        private float _yaw = 0f;
        private float _pitch = 15f;
        private float _desiredDistance;
        private float _currentDistance;
        private float _resolvedDistance; // _currentDistance after wall occlusion

        // Cached colliders on this player to ignore during occlusion check
        private readonly HashSet<Collider> _selfColliders = new HashSet<Collider>();
        private CoopGame.CarrySystem.PlayerCarry _playerCarry;
        private BonelessCharacterPhysics _bodyMotion;
        private ActiveRagdoll _ragdoll;
        private RagdollCharacter _fullRagdoll;

        /// <summary>
        /// Current vertical pitch angle in degrees (-35 = looking up, +70 = looking down).
        /// Used by the carry system to dynamically raise and lower carried objects with mouse pitch.
        /// </summary>
        public float Pitch => _pitch;

        /// <summary>
        /// Current horizontal yaw angle in degrees.
        /// </summary>
        public float Yaw => _yaw;

        /// <summary>
        /// True if currently in First-Person perspective.
        /// </summary>
        public bool IsFirstPerson => _currentDistance < _firstPersonThreshold;

        /// <summary>True while the camera is in first person or pressed into the body by a wall (the body is hidden).</summary>
        public bool BodyHidden { get; private set; }

        /// <summary>
        /// Camera forward vector projected onto the horizontal X-Z plane.
        /// Derived purely from _yaw to guarantee stability with zero feedback jitter from player movement.
        /// </summary>
        public Vector3 HorizontalForward => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;

        /// <summary>
        /// Camera right vector projected onto the horizontal X-Z plane.
        /// </summary>
        public Vector3 HorizontalRight => Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;

        /// <summary>
        /// Camera forward vector in full 3D pitch/yaw space.
        /// </summary>
        public Vector3 AimForward => Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;

        /// <summary>
        /// Reference to the Camera component controlled by this script.
        /// </summary>
        public Camera PlayerCamera => _playerCamera;

        private void Awake()
        {
            _ragdoll = GetComponentInParent<ActiveRagdoll>();
            _fullRagdoll = GetComponentInParent<RagdollCharacter>();
            if (_playerCamera == null)
            {
                _playerCamera = GetComponentInChildren<Camera>(true);
            }

            if (_audioListener == null && _playerCamera != null)
            {
                _audioListener = _playerCamera.GetComponent<AudioListener>();
            }

            if (_cameraPivot == null)
            {
                _cameraPivot = transform;
            }

            if (_playerBodyRenderer == null)
            {
                _playerBodyRenderer = GetComponent<Renderer>();
            }

            _desiredDistance = _defaultThirdPersonDistance;
            _currentDistance = _defaultThirdPersonDistance;
            _yaw = transform.eulerAngles.y;

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider col in colliders)
            {
                _selfColliders.Add(col);
            }

            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null)
            {
                _selfColliders.Add(cc);
            }

            _playerCarry = GetComponentInParent<CoopGame.CarrySystem.PlayerCarry>() ?? GetComponent<CoopGame.CarrySystem.PlayerCarry>();
            _bodyMotion = GetComponentInChildren<BonelessCharacterPhysics>(true);
        }

        // Cursor lock state
        private bool _isCursorLocked = true;

        public bool IsCursorLocked => _isCursorLocked;

        public void SetPlayerBodyRenderer(Renderer rend) => _playerBodyRenderer = rend;

        public static PlayerCameraController LocalInstance { get; private set; }

        public void SetOwnershipState(bool isOwner)
        {
            if (isOwner)
            {
                LocalInstance = this;
                if (_playerCamera != null)
                {
                    // The player survives NGO single-scene loads. Its detached camera must
                    // survive too, or LateUpdate will keep a reference to a destroyed Camera.
                    _playerCamera.transform.SetParent(null);
                    DontDestroyOnLoad(_playerCamera.gameObject);
                }

                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneLoaded += OnSceneLoaded;
                DisableSceneCameras();
            }
            else
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                if (LocalInstance == this)
                {
                    SetCursorLock(false);
                    LocalInstance = null;
                }
            }

            if (_playerCamera != null)
            {
                _playerCamera.enabled = isOwner;
                if (isOwner)
                {
                    _playerCamera.tag = "MainCamera";
                }
            }

            if (_audioListener != null)
            {
                _audioListener.enabled = isOwner;
            }

            enabled = isOwner;

            if (isOwner)
            {
                SetCursorLock(true);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (LocalInstance == this)
                DisableSceneCameras();
        }

        private void DisableSceneCameras()
        {
            Camera[] cameras = FindObjectsByType<Camera>();
            foreach (Camera camera in cameras)
            {
                if (camera != _playerCamera && camera.gameObject.name.Contains("Main Camera"))
                    camera.gameObject.SetActive(false);
            }
        }



#if UNITY_EDITOR
        private static System.Type _gameViewType;
        private static System.Reflection.MethodInfo _allowCursorLockMethod;
#endif

        public static void ForceEditorCursorLockIfNeeded(bool locked)
        {
#if UNITY_EDITOR
            if (!locked) return;
            try
            {
                if (_gameViewType == null)
                    _gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
                if (_gameViewType != null && _allowCursorLockMethod == null)
                    _allowCursorLockMethod = _gameViewType.GetMethod("AllowCursorLockAndHide",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (_gameViewType != null && _allowCursorLockMethod != null)
                {
                    var views = Resources.FindObjectsOfTypeAll(_gameViewType);
                    foreach (var v in views)
                        _allowCursorLockMethod.Invoke(v, new object[] { true });
                }
            }
            catch { }
#endif
        }

        public void SetCursorLock(bool locked)
        {
            _isCursorLocked = locked;
            ForceEditorCursorLockIfNeeded(locked);
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public void UpdateLookInput(Vector2 lookDelta)
        {
            // Do not rotate camera when cursor is unlocked (e.g. pause menu or UI active)
            if (Cursor.lockState != CursorLockMode.Locked) return;
            _isCursorLocked = true;

            _yaw += lookDelta.x * _sensitivity;
            _pitch -= lookDelta.y * _sensitivity;
            _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
        }

        private void Update()
        {
            HandleCursorToggle();
            HandleZoomInput();
        }

        /// <summary>
        /// Handles cursor locking. If PauseMenu is active, PauseMenu handles ESC.
        /// If cursor was unlocked and player clicks back into the game viewport (not on UI), re-lock cursor.
        /// </summary>
        private void HandleCursorToggle()
        {
            if (CoopGame.CarrySystem.MissionFailUI.IsVisible ||
                CoopGame.Network.ExpeditionHUD.BlocksGameplayInput ||
                CoopGame.Network.LobbyCharacterCustomizationUI.IsVisible ||
                CoopGame.Network.PauseMenu.IsPaused) return;

            if (_isCursorLocked && (Cursor.lockState != CursorLockMode.Locked || Cursor.visible))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            // If game is not paused and cursor is currently unlocked, clicking on the game viewport re-locks cursor
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                {
                    if (UnityEngine.EventSystems.EventSystem.current == null ||
                        !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                    {
                        SetCursorLock(true);
                        return;
                    }
                }
            }

            // Fallback ESC toggle if no PauseMenu exists in scene
            if (CoopGame.Network.PauseMenu.Instance == null)
            {
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    SetCursorLock(!_isCursorLocked);
                }
            }
        }

        /// <summary>
        /// Handles mouse wheel zooming and quick 1st/3rd person toggle ('V' key).
        /// Note: 'Q' key is dedicated to Throwing mechanics to prevent conflict.
        /// </summary>
        private void HandleZoomInput()
        {
            // 1. Mouse Scroll Wheel Zoom
            float scroll = 0f;
            if (Mouse.current != null)
            {
                scroll = Mouse.current.scroll.ReadValue().y;
            }

            // While carrying, the wheel raises/lowers the cargo instead (PlayerCarry.CalculateHoldHeight).
            if (_playerCarry != null && _playerCarry.IsCarrying) scroll = 0f;
            if (_fullRagdoll != null && _fullRagdoll.FullRagdoll && _fullRagdoll.HoldingCargo) scroll = 0f;

            if (Mathf.Abs(scroll) > 0.01f)
            {
                // Scroll up (positive) -> Zoom IN towards 1st person
                // Scroll down (negative) -> Zoom OUT towards 3rd person
                float delta = -Mathf.Sign(scroll) * _scrollSensitivity;
                _desiredDistance = Mathf.Clamp(_desiredDistance + delta, _firstPersonDistance, _maxZoomDistance);
            }

            // 2. Quick Toggle Key ('V' key)
            if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
            {
                // Toggle between 1st Person (0m) and 3rd Person (3.5m)
                if (_desiredDistance < 1.0f)
                {
                    _desiredDistance = _defaultThirdPersonDistance;
                }
                else
                {
                    _desiredDistance = _firstPersonDistance;
                }
            }
        }

        private void LateUpdate()
        {
            if (_playerCamera == null || _cameraPivot == null) return;

            // 1. Calculate target orientation
            Quaternion targetRotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // 2. Eye/Pivot position
            Vector3 pivotPosition = _cameraPivot.position + (Vector3.up * _pivotHeightOffset);
            if (_bodyMotion == null)
                _bodyMotion = GetComponentInChildren<BonelessCharacterPhysics>(true);
            if (_bodyMotion != null && _bodyMotion.isActiveAndEnabled && _bodyMotion.HasRenderedRootPosition)
                pivotPosition += _bodyMotion.RenderedRootPosition - transform.position;

            // Full physics body: the root bobs with the hips, so follow it smoothed, and keep the pivot itself
            // out of walls (backing into a wall puts the body right against it).
            bool physicsBody = _fullRagdoll != null && _fullRagdoll.FullRagdoll;
            if (physicsBody)
            {
                pivotPosition = SmoothPivot(pivotPosition);
                pivotPosition = PushOutOfWalls(pivotPosition, _collisionRadius);
                if (_playerCamera.nearClipPlane > _ragdollNearClip) _playerCamera.nearClipPlane = _ragdollNearClip;
            }

            // 3. Smoothly interpolate zoom distance
            _currentDistance = Mathf.Lerp(_currentDistance, _desiredDistance, Time.deltaTime * _zoomSpeed);

            // 4. Handle First-Person vs Third-Person View
            bool inFirstPerson = _currentDistance < _firstPersonThreshold;
            bool hideBody = inFirstPerson;

            Vector3 finalPosition;

            if (inFirstPerson)
            {
                // In 1st Person: camera sits directly at eye level with zero backward offset
                finalPosition = pivotPosition;
            }
            else
            {
                // In 3rd Person: check for obstacle occlusion and position behind player
                Vector3 desiredCameraDirection = targetRotation * -Vector3.forward;

                float targetDistance = _currentDistance;
                Ray ray = new Ray(pivotPosition, desiredCameraDirection);
                RaycastHit[] hits = Physics.SphereCastAll(ray, _collisionRadius, _currentDistance, _obstacleLayers, QueryTriggerInteraction.Ignore);

                float closestValidHitDistance = float.MaxValue;
                foreach (RaycastHit hit in hits)
                {
                    if (IsIgnoredObstacle(hit.collider)) continue;

                    if (hit.distance < closestValidHitDistance)
                    {
                        closestValidHitDistance = hit.distance;
                    }
                }

                if (closestValidHitDistance < float.MaxValue)
                {
                    targetDistance = Mathf.Clamp(closestValidHitDistance - _collisionRadius, _firstPersonDistance, _currentDistance);
                }

                // Pull in instantly so the camera never clips a wall, but ease back out so it
                // does not snap when sliding past an edge.
                if (targetDistance < _resolvedDistance || _resolvedDistance <= 0f) _resolvedDistance = targetDistance;
                else _resolvedDistance = Mathf.Lerp(_resolvedDistance, targetDistance, Time.deltaTime * _zoomSpeed);
                // A wall may squeeze the camera into the body even while zoomed out.
                hideBody = _resolvedDistance < _firstPersonThreshold;

                finalPosition = pivotPosition + (desiredCameraDirection * _resolvedDistance);
                if (physicsBody) finalPosition = PushOutOfWalls(finalPosition, _collisionRadius);
            }

            if (inFirstPerson) _resolvedDistance = _currentDistance;
            BodyHidden = hideBody;
            UpdatePlayerBodyVisibility(hideBody);
            _playerCamera.transform.position = finalPosition;
            _playerCamera.transform.rotation = targetRotation;
        }

        private bool IsIgnoredObstacle(Collider collider)
        {
            if (_selfColliders.Contains(collider)) return true;
            if (collider.transform.root == transform.root) return true;
            // The knocked-down body's own limbs (they live outside the player hierarchy).
            if (_ragdoll != null && _ragdoll.OwnsCollider(collider)) return true;
            if (_fullRagdoll != null && _fullRagdoll.OwnsCollider(collider)) return true;

            // Ignore any carried object belonging to this player
            if (_playerCarry == null)
            {
                _playerCarry = GetComponentInParent<CoopGame.CarrySystem.PlayerCarry>() ?? GetComponent<CoopGame.CarrySystem.PlayerCarry>();
            }
            if (_playerCarry != null && _playerCarry.CurrentCarryable != null &&
                collider.transform.root == _playerCarry.CurrentCarryable.transform.root) return true;

            // Ignore any CarryableObject currently being carried
            var carryable = collider.GetComponentInParent<CoopGame.CarrySystem.CarryableObject>();
            return carryable != null && carryable.IsCarried;
        }

        [Header("Full Physics Body")]
        [Tooltip("Seconds the pivot lags the hips horizontally / vertically")]
        [SerializeField] private float _ragdollPivotSmoothXZ = .05f;
        [SerializeField] private float _ragdollPivotSmoothY = .12f;
        [SerializeField] private float _ragdollNearClip = .05f;

        private Vector3 _smoothedPivot, _pivotVelocity;
        private bool _hasSmoothedPivot;
        private readonly Collider[] _wallOverlaps = new Collider[16];
        private SphereCollider _wallProbe;

        private Vector3 SmoothPivot(Vector3 target)
        {
            if (!_hasSmoothedPivot || (target - _smoothedPivot).sqrMagnitude > 4f) // first frame or teleport
            {
                _smoothedPivot = target;
                _pivotVelocity = Vector3.zero;
                _hasSmoothedPivot = true;
                return target;
            }
            float dt = Time.deltaTime;
            _smoothedPivot.x = Mathf.SmoothDamp(_smoothedPivot.x, target.x, ref _pivotVelocity.x, _ragdollPivotSmoothXZ, Mathf.Infinity, dt);
            _smoothedPivot.z = Mathf.SmoothDamp(_smoothedPivot.z, target.z, ref _pivotVelocity.z, _ragdollPivotSmoothXZ, Mathf.Infinity, dt);
            _smoothedPivot.y = Mathf.SmoothDamp(_smoothedPivot.y, target.y, ref _pivotVelocity.y, _ragdollPivotSmoothY, Mathf.Infinity, dt);
            return _smoothedPivot;
        }

        /// <summary>Moves a sphere of this radius at the point out of any wall it overlaps.</summary>
        private Vector3 PushOutOfWalls(Vector3 point, float radius)
        {
            if (_wallProbe == null)
            {
                // ComputePenetration needs an enabled collider; the probe is a trigger parked far away and only its
                // shape is used.
                var go = new GameObject("CameraWallProbe") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                go.transform.position = new Vector3(0f, -10000f, 0f);
                _wallProbe = go.AddComponent<SphereCollider>();
                _wallProbe.isTrigger = true;
            }
            _wallProbe.radius = radius;
            for (int pass = 0; pass < 3; pass++)
            {
                int count = Physics.OverlapSphereNonAlloc(point, radius, _wallOverlaps, _obstacleLayers, QueryTriggerInteraction.Ignore);
                bool moved = false;
                for (int i = 0; i < count; i++)
                {
                    Collider c = _wallOverlaps[i];
                    if (c == null || IsIgnoredObstacle(c)) continue;
                    if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue; // loose props, cargo
                    if (Physics.ComputePenetration(_wallProbe, point, Quaternion.identity, c, c.transform.position, c.transform.rotation,
                            out Vector3 direction, out float distance))
                    {
                        point += direction * (distance + .01f);
                        moved = true;
                    }
                }
                if (!moved) break;
            }
            return point;
        }

        /// <summary>
        /// In First-Person view, hides the player capsule mesh so it doesn't clip the camera,
        /// while keeping shadows active and hands fully visible!
        /// </summary>
        private void UpdatePlayerBodyVisibility(bool inFirstPerson)
        {
            if (_playerBodyRenderer != null)
            {
                _playerBodyRenderer.shadowCastingMode = inFirstPerson 
                    ? ShadowCastingMode.ShadowsOnly 
                    : ShadowCastingMode.On;
            }
        }

        private void OnDisable()
        {
            // Remote camera teardown must not change this process's local input state.
            if (LocalInstance == this)
                SetCursorLock(false);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (LocalInstance == this)
            {
                SetCursorLock(false);
                LocalInstance = null;
            }
            if (_playerCamera != null)
            {
                Destroy(_playerCamera.gameObject);
            }
            if (_wallProbe != null) Destroy(_wallProbe.gameObject);
        }
    }
}
