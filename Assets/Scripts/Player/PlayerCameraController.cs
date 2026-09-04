using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

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
        [SerializeField] private float _minPitch = -35f;

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
        [SerializeField] private float _pivotHeightOffset = 1.6f;

        [Tooltip("Sphere radius used to check for environmental occlusion")]
        [SerializeField] private float _collisionRadius = 0.22f;

        [Tooltip("Layer mask containing obstacles that the camera should collide with")]
        [SerializeField] private LayerMask _obstacleLayers = ~0;

        // Current spherical coordinate angles and distances
        private float _yaw = 0f;
        private float _pitch = 15f;
        private float _desiredDistance;
        private float _currentDistance;

        // Cached colliders on this player to ignore during occlusion check
        private readonly HashSet<Collider> _selfColliders = new HashSet<Collider>();

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
        /// Reference to the Camera component controlled by this script.
        /// </summary>
        public Camera PlayerCamera => _playerCamera;

        private void Awake()
        {
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
        }

        private void Start()
        {
            // Decouple camera from player hierarchy at runtime to prevent feedback loops
            if (_playerCamera != null && _playerCamera.transform.parent != null)
            {
                _playerCamera.transform.SetParent(null);
            }
        }

        public void SetOwnershipState(bool isOwner)
        {
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
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public void UpdateLookInput(Vector2 lookDelta)
        {
            _yaw += lookDelta.x * _sensitivity;
            _pitch -= lookDelta.y * _sensitivity;
            _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
        }

        private void Update()
        {
            HandleZoomInput();
        }

        /// <summary>
        /// Handles mouse wheel zooming and quick 1st/3rd person toggle ('V' key or Middle Click).
        /// </summary>
        private void HandleZoomInput()
        {
            // 1. Mouse Scroll Wheel Zoom
            float scroll = 0f;
            if (Mouse.current != null)
            {
                scroll = Mouse.current.scroll.ReadValue().y;
            }

            if (Mathf.Abs(scroll) > 0.01f)
            {
                // Scroll up (positive) -> Zoom IN towards 1st person
                // Scroll down (negative) -> Zoom OUT towards 3rd person
                float delta = -Mathf.Sign(scroll) * _scrollSensitivity;
                _desiredDistance = Mathf.Clamp(_desiredDistance + delta, _firstPersonDistance, _maxZoomDistance);
            }

            // 2. Quick Toggle Key ('V' or Middle Mouse Button)
            bool toggleRequested = false;
            if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
            {
                toggleRequested = true;
            }
            else if (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame)
            {
                toggleRequested = true;
            }

            if (toggleRequested)
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

            // 3. Smoothly interpolate zoom distance
            _currentDistance = Mathf.Lerp(_currentDistance, _desiredDistance, Time.deltaTime * _zoomSpeed);

            // 4. Handle First-Person vs Third-Person View
            bool inFirstPerson = _currentDistance < _firstPersonThreshold;
            UpdatePlayerBodyVisibility(inFirstPerson);

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
                    if (_selfColliders.Contains(hit.collider)) continue;
                    if (hit.collider.transform.root == transform.root) continue;

                    if (hit.distance < closestValidHitDistance)
                    {
                        closestValidHitDistance = hit.distance;
                    }
                }

                if (closestValidHitDistance < float.MaxValue)
                {
                    targetDistance = Mathf.Clamp(closestValidHitDistance - _collisionRadius, _firstPersonDistance, _currentDistance);
                }

                finalPosition = pivotPosition + (desiredCameraDirection * targetDistance);
            }

            _playerCamera.transform.position = finalPosition;
            _playerCamera.transform.rotation = targetRotation;
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

        private void OnDestroy()
        {
            if (_playerCamera != null)
            {
                Destroy(_playerCamera.gameObject);
            }
        }
    }
}
