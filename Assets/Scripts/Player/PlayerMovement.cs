using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// PlayerMovement handles 3D character movement, jumping, gravity, and orientation.
    /// 
    /// Adheres to SOLID Single Responsibility Principle (SRP):
    /// - Responsible ONLY for translating high-level input vectors into character physics displacements.
    /// - Decoupled from hardware input reading (provided via methods or PlayerInputReader).
    /// - Decoupled from networking logic (NetworkPlayer decides when to execute this component).
    /// - Provides extensible hooks (e.g. SpeedMultiplier) for Carry and Stamina systems in Steps 2 & 3.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("Movement Speeds")]
        [Tooltip("Base walking speed in meters per second")]
        [SerializeField] private float _walkSpeed = 5.0f;

        [Tooltip("Sprint speed in meters per second")]
        [SerializeField] private float _sprintSpeed = 8.0f;

        [Tooltip("Acceleration rate to reach target horizontal speed")]
        [SerializeField] private float _acceleration = 12.0f;

        [Tooltip("Deceleration rate when stopping")]
        [SerializeField] private float _deceleration = 16.0f;

        [Header("Turning & Orientation")]
        [Tooltip("Rotation speed in degrees per second towards movement direction")]
        [SerializeField] private float _rotationSpeed = 720.0f;

        [Header("Jumping & Gravity (Crisp Platforming Physics)")]
        [Tooltip("Maximum jump height in meters")]
        [SerializeField] private float _jumpHeight = 1.6f;

        [Tooltip("Custom downward gravity acceleration (m/s^2). Default Unity -9.81 feels floaty for platforming.")]
        [SerializeField] private float _gravity = -24.0f;

        [Tooltip("Maximum downward falling velocity")]
        [SerializeField] private float _terminalVelocity = -40.0f;

        [Tooltip("Small downward grounding velocity to ensure character stays glued to slopes/stairs")]
        [SerializeField] private float _groundStickForce = -2.5f;

        [Header("Platforming Timers")]
        [Tooltip("Coyote time: grace period to jump shortly after walking off an edge")]
        [SerializeField] private float _coyoteTime = 0.15f;

        [Tooltip("Jump buffer: registers jump command shortly before landing on ground")]
        [SerializeField] private float _jumpBufferTime = 0.15f;

        // Cached components
        private CharacterController _characterController;
        private PlayerStamina _stamina;

        // Current velocity vectors
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;

        // Platforming timer counters
        private float _coyoteTimer;
        private float _jumpBufferTimer;

        /// <summary>
        /// External multiplier to scale movement speed.
        /// Essential for Steps 2 & 3:
        /// - 1.0f = Normal speed
        /// - 0.7f = Carrying object with co-op partner
        /// - 0.4f = Carrying object alone (heavy penalty)
        /// - 0.2f = Exhausted / 0 stamina
        /// </summary>
        public float SpeedMultiplier { get; set; } = 1.0f;

        /// <summary>
        /// Exposes whether the character is currently touching the ground.
        /// </summary>
        public bool IsGrounded => _characterController != null && _characterController.isGrounded;

        /// <summary>
        /// Current horizontal speed magnitude. Useful for animation blend trees.
        /// </summary>
        public float CurrentSpeed => _horizontalVelocity.magnitude;

        /// <summary>
        /// Total 3D movement velocity vector. Used to transfer momentum to thrown objects.
        /// </summary>
        public Vector3 Velocity => _horizontalVelocity + Vector3.up * _verticalVelocity;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _stamina = GetComponent<PlayerStamina>();
        }

        /// <summary>
        /// Call this every frame with the desired movement input, camera heading, and jump request.
        /// </summary>
        /// <param name="moveInput">Normalized 2D input from joystick / WASD (X = lateral, Y = forward/backward)</param>
        /// <param name="cameraForward">Horizontal forward vector of the camera</param>
        /// <param name="cameraRight">Horizontal right vector of the camera</param>
        /// <param name="isSprinting">True if sprint button is held</param>
        /// <param name="jumpRequested">True if jump button was pressed this frame</param>
        public void ProcessMovement(
            Vector2 moveInput,
            Vector3 cameraForward,
            Vector3 cameraRight,
            bool isSprinting,
            bool jumpRequested)
        {
            if (_characterController == null || !_characterController.enabled) return;

            float deltaTime = Time.deltaTime;

            // 1. Update Platforming Timers (Coyote time & Jump buffer)
            UpdateTimers(deltaTime, jumpRequested);

            // 2. Compute Camera-Relative Target Velocity
            Vector3 desiredMoveDirection = (cameraForward * moveInput.y + cameraRight * moveInput.x);
            if (desiredMoveDirection.sqrMagnitude > 1.0f)
            {
                desiredMoveDirection.Normalize();
            }

            // If exhausted, disable sprint speed boost
            bool canSprint = isSprinting && (_stamina == null || !_stamina.IsExhausted);
            float baseTargetSpeed = (canSprint ? _sprintSpeed : _walkSpeed) * SpeedMultiplier;
            Vector3 targetHorizontalVelocity = desiredMoveDirection * baseTargetSpeed;

            // 3. Smooth Acceleration / Deceleration
            float rate = (targetHorizontalVelocity.sqrMagnitude > 0.01f) ? _acceleration : _deceleration;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetHorizontalVelocity, rate * deltaTime);

            // 4. Smooth Rotation Towards Movement Direction
            if (desiredMoveDirection.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(desiredMoveDirection, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, _rotationSpeed * deltaTime);
            }

            // 5. Vertical Physics & Jumping
            ApplyVerticalPhysics(deltaTime);

            // 6. Combine and Apply Displacement to CharacterController
            Vector3 totalMovement = (_horizontalVelocity + Vector3.up * _verticalVelocity) * deltaTime;
            _characterController.Move(totalMovement);
        }

        private void UpdateTimers(float deltaTime, bool jumpRequested)
        {
            // Coyote timer: counts down when airborne, resets when grounded
            if (_characterController.isGrounded)
            {
                _coyoteTimer = _coyoteTime;
            }
            else
            {
                _coyoteTimer -= deltaTime;
            }

            // Jump buffer: registers button press and counts down
            if (jumpRequested)
            {
                _jumpBufferTimer = _jumpBufferTime;
            }
            else
            {
                _jumpBufferTimer -= deltaTime;
            }
        }

        private void ApplyVerticalPhysics(float deltaTime)
        {
            if (_characterController.isGrounded)
            {
                // While grounded, keep slight downward force to prevent bouncing on slopes
                if (_verticalVelocity < 0f)
                {
                    _verticalVelocity = _groundStickForce;
                }

                // Check if a buffered jump is ready to trigger
                if (_jumpBufferTimer > 0f)
                {
                    ExecuteJump();
                }
            }
            else
            {
                // While airborne: check coyote jump window
                if (_jumpBufferTimer > 0f && _coyoteTimer > 0f)
                {
                    ExecuteJump();
                }

                // Accelerate downward by gravity
                _verticalVelocity += _gravity * deltaTime;

                // Clamp to terminal falling velocity
                if (_verticalVelocity < _terminalVelocity)
                {
                    _verticalVelocity = _terminalVelocity;
                }
            }
        }

        /// <summary>
        /// Executes jump impulse using the kinematic formula: v = sqrt(2 * h * |g|)
        /// This ensures the character reaches the exact configured jump height regardless of gravity scale.
        /// </summary>
        private void ExecuteJump()
        {
            _verticalVelocity = Mathf.Sqrt(2.0f * _jumpHeight * Mathf.Abs(_gravity));
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
        }

        /// <summary>
        /// Helper to reset velocity when teleported or respawned.
        /// </summary>
        public void ResetVelocity()
        {
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }
    }
}
