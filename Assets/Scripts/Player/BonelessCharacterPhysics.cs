using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// BonelessCharacterPhysics gives unrigged characters a dynamic, gelatinous,
    /// floppy physical feel inspired by Human Fall Flat.
    /// 
    /// Features:
    /// 1. Dynamic Squash & Stretch:
    ///    - Air hang / fall elongates the body vertically (volume-preserving X/Z contraction).
    ///    - Ground landing triggers an elastic spring squash and jelly rebound.
    /// 2. Boneless Inertial Lean & Tilt:
    ///    - Leans into acceleration and motion vectors.
    ///    - Banks and twists with angular turning velocity.
    ///    - Overshoots and springs back when stopping (boneless momentum).
    /// 3. Iconic Waddle & Hip Bob:
    ///    - Procedural side-to-side roll sway and rhythmic vertical bobbing during movement.
    ///    - Scales dynamically with walking and sprinting speeds.
    /// 4. Carrying & Climbing Physics Reactivity:
    ///    - Strains and leans backward when carrying heavy objects.
    ///    - Tilts towards gripping hands when wall-climbing.
    /// </summary>
    [DisallowMultipleComponent]
    public class BonelessCharacterPhysics : MonoBehaviour
    {
        [Header("Squash & Stretch (Elastic Spring)")]
        [Tooltip("Spring stiffness for jelly squash and stretch recovery")]
        [SerializeField] private float _squashSpringStiffness = 180f;

        [Tooltip("Spring damping factor (higher = less oscillation, lower = bouncier jelly)")]
        [SerializeField] private float _squashDamping = 12f;

        [Tooltip("Maximum squash compression on hard landing (e.g. 0.3 = 30% squash)")]
        [SerializeField] private float _maxLandSquash = 0.35f;

        [Tooltip("Stretch multiplier while falling in mid-air")]
        [SerializeField] private float _airStretchMultiplier = 0.015f;

        [Tooltip("Maximum mid-air stretch")]
        [SerializeField] private float _maxAirStretch = 0.25f;

        [Header("Waddle & Gait (Human Fall Flat Walk)")]
        [Tooltip("Side-to-side roll angle (degrees) during walking")]
        [SerializeField] private float _waddleRollAngle = 11.0f;

        [Tooltip("Vertical bobbing amplitude (meters) while walking")]
        [SerializeField] private float _gaitBobAmount = 0.08f;

        [Tooltip("Frequency cadence of the waddle cycle")]
        [SerializeField] private float _waddleFrequency = 7.5f;

        [Header("Inertial Lean & Tilt")]
        [Tooltip("Forward/backward lean angle into movement velocity")]
        [SerializeField] private float _velocityLeanAngle = 14.0f;

        [Tooltip("Sideways banking angle when turning or strafing")]
        [SerializeField] private float _bankAngle = 10.0f;

        [Tooltip("Spring smoothing speed for lean transitions")]
        [SerializeField] private float _leanSmoothing = 10.0f;

        [Header("Carry & Climb Reactivity")]
        [Tooltip("Backward lean angle when carrying an object")]
        [SerializeField] private float _carryLeanBackAngle = 8.0f;

        [Tooltip("Body swing angle when hanging on a wall with one hand")]
        [SerializeField] private float _climbHangSwingAngle = 12.0f;

        // Parent / Player references
        private PlayerMovement _movement;
        private CharacterController _characterController;
        private CoopGame.CarrySystem.PlayerCarry _playerCarry;
        private Wallclimb _wallClimb;
        private Transform _rootTransform;

        // Base local transform caches
        private Vector3 _baseLocalPosition;
        private Quaternion _baseLocalRotation;
        private Vector3 _baseLocalScale;

        // Spring-mass state for Squash & Stretch (1.0 = normal Y scale)
        private float _currentScaleY = 1.0f;
        private float _scaleYVelocity = 0.0f;

        // Inertial lean & tilt state
        private Vector2 _currentTilt = Vector2.zero; // x = pitch, y = roll
        private Vector2 _targetTilt = Vector2.zero;
        private float _waddleTimer = 0.0f;

        // Velocity tracking
        private Vector3 _lastRootPosition;
        private float _lastRootYaw;
        private bool _wasGrounded = true;

        private void Awake()
        {
            _rootTransform = transform.parent != null ? transform.parent : transform;
            _movement = _rootTransform.GetComponent<PlayerMovement>();
            _characterController = _rootTransform.GetComponent<CharacterController>();
            _playerCarry = _rootTransform.GetComponent<CoopGame.CarrySystem.PlayerCarry>();
            _wallClimb = _rootTransform.GetComponent<Wallclimb>();

            _baseLocalPosition = transform.localPosition;
            _baseLocalRotation = transform.localRotation;
            _baseLocalScale = transform.localScale;

            _lastRootPosition = _rootTransform.position;
            _lastRootYaw = _rootTransform.eulerAngles.y;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0.0001f) return;

            // Calculate actual velocities relative to root orientation
            Vector3 worldDisplacement = (_rootTransform.position - _lastRootPosition) / dt;
            _lastRootPosition = _rootTransform.position;

            Vector3 localVelocity = _rootTransform.InverseTransformDirection(worldDisplacement);
            float currentSpeed = new Vector2(localVelocity.x, localVelocity.z).magnitude;

            float currentYaw = _rootTransform.eulerAngles.y;
            float angularYawDelta = Mathf.DeltaAngle(_lastRootYaw, currentYaw) / dt;
            _lastRootYaw = currentYaw;

            bool isGrounded = (_characterController != null) ? _characterController.isGrounded : true;

            // ─────────────────────────────────────────────────────────────────
            // 1. Squash & Stretch Physics (Spring Oscillator)
            // ─────────────────────────────────────────────────────────────────
            // Landing impact detection
            if (isGrounded && !_wasGrounded)
            {
                float fallSpeed = Mathf.Abs(worldDisplacement.y);
                float impact = Mathf.Clamp(fallSpeed * 0.04f, 0.05f, _maxLandSquash);
                _scaleYVelocity -= impact * 28f; // Compress downward
            }

            float targetScaleY = 1.0f;
            if (!isGrounded)
            {
                // In air: stretch vertically proportional to vertical speed
                float airStretch = Mathf.Clamp(-worldDisplacement.y * _airStretchMultiplier, -_maxAirStretch, _maxAirStretch);
                targetScaleY = 1.0f + airStretch;
            }

            // Spring-mass equation: F = -k*(x - target) - c*v
            float displacement = _currentScaleY - targetScaleY;
            float springForce = -(_squashSpringStiffness * displacement) - (_squashDamping * _scaleYVelocity);
            _scaleYVelocity += springForce * dt;
            _currentScaleY += _scaleYVelocity * dt;
            _currentScaleY = Mathf.Clamp(_currentScaleY, 0.45f, 1.6f);

            // Volume-preserving scale: X & Z scale inversely with Y
            float horizontalScale = 1.0f / Mathf.Sqrt(Mathf.Max(0.1f, _currentScaleY));
            transform.localScale = new Vector3(
                _baseLocalScale.x * horizontalScale,
                _baseLocalScale.y * _currentScaleY,
                _baseLocalScale.z * horizontalScale
            );

            // ─────────────────────────────────────────────────────────────────
            // 2. Human Fall Flat Waddle & Hip Bob
            // ─────────────────────────────────────────────────────────────────
            float rollWaddle = 0.0f;
            float verticalBob = 0.0f;

            if (isGrounded && currentSpeed > 0.15f)
            {
                float speedRatio = Mathf.Clamp01(currentSpeed / 6.0f);
                _waddleTimer += dt * _waddleFrequency * (0.6f + speedRatio * 0.8f);

                // Side to side waddle roll
                rollWaddle = Mathf.Sin(_waddleTimer) * _waddleRollAngle * speedRatio;
                // Vertical bob (two bobs per full step cycle)
                verticalBob = Mathf.Abs(Mathf.Sin(_waddleTimer)) * _gaitBobAmount * speedRatio;
            }
            else
            {
                // Smoothly decay waddle phase back to zero
                rollWaddle = Mathf.Lerp(rollWaddle, 0f, dt * 10f);
                verticalBob = Mathf.Lerp(verticalBob, 0f, dt * 10f);
            }

            // Apply vertical bobbing offset + ground squash offset (keeps feet on ground when squashed)
            float squashHeightOffset = (_currentScaleY - 1.0f) * 0.5f;
            transform.localPosition = _baseLocalPosition + Vector3.up * (verticalBob + squashHeightOffset);

            // ─────────────────────────────────────────────────────────────────
            // 3. Inertial Torso Lean & Tilt
            // ─────────────────────────────────────────────────────────────────
            // Forward/backward lean based on forward velocity
            float forwardLean = Mathf.Clamp(localVelocity.z * 1.5f, -_velocityLeanAngle, _velocityLeanAngle);

            // Sideways bank based on strafing + turning angular velocity
            float turnBank = Mathf.Clamp(angularYawDelta * 0.05f, -15f, 15f);
            float strafeBank = Mathf.Clamp(-localVelocity.x * 1.5f, -_bankAngle, _bankAngle);
            float totalBank = strafeBank + turnBank;

            // Carry reaction: Lean backward when carrying heavy objects
            if (_playerCarry != null && _playerCarry.IsCarrying)
            {
                forwardLean -= _carryLeanBackAngle;
            }

            // Climb reaction: Swing towards gripping hand
            if (_wallClimb != null && _wallClimb.IsClimbing)
            {
                if (_wallClimb.LeftHandGripping && !_wallClimb.RightHandGripping)
                {
                    totalBank += _climbHangSwingAngle;
                }
                else if (_wallClimb.RightHandGripping && !_wallClimb.LeftHandGripping)
                {
                    totalBank -= _climbHangSwingAngle;
                }
            }

            _targetTilt = new Vector2(forwardLean, totalBank);
            _currentTilt = Vector2.Lerp(_currentTilt, _targetTilt, dt * _leanSmoothing);

            // Combine base rotation with inertial lean and waddle roll
            Quaternion leanRot = Quaternion.Euler(_currentTilt.x, 0f, _currentTilt.y + rollWaddle);
            transform.localRotation = _baseLocalRotation * leanRot;

            _wasGrounded = isGrounded;
        }

        /// <summary>
        /// External impulse to cause a momentary jelly wobble (e.g. on jumping, punch, or impact).
        /// </summary>
        public void AddJellyImpulse(float squashImpulse, float tiltImpulse = 0f)
        {
            _scaleYVelocity += squashImpulse;
            _currentTilt.x += tiltImpulse;
        }
    }
}
