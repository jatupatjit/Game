using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// ProceduralPlayerLegs animates the rigged skeleton legs (Thigh -> UpperLeg -> Foot)
    /// of Rigged_character_.fbx using procedural stride cycles, knee lift, pelvis bounce,
    /// and full 4-stage procedural jump/airborne/landing squash dynamics.
    /// 
    /// Features:
    /// 1. True Rigged Leg Locomotion (No Animation Clips Required):
    ///    - Rotates UpperLeg (thigh) and Foot (shin/knee) in anatomical swing planes.
    ///    - Stride frequency and amplitude dynamically scale with movement speed (walk vs sprint).
    ///    - Pelvis sway (lateral roll) and sprint lean for dynamic game feel.
    /// 2. Natural Knee Flex & Foot Clearance:
    ///    - Bends knee during swing phase to lift feet smoothly off the ground.
    /// 3. Procedural Jump & Airborne Dynamics:
    ///    - Airborne Knee Tuck: Tucks knees and lifts feet when leaping/airborne.
    ///    - Dynamic Landing Squash: Absorbs vertical impact on landing by flexing knees and lowering pelvis.
    /// 4. 100% Smooth Rest Restoration:
    ///    - Blends smoothly back to initial rest rotations when stopping.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public class ProceduralPlayerLegs : MonoBehaviour
    {
        [Header("Rigged Leg Transforms")]
        [SerializeField] private Transform _characterVisual;
        [SerializeField] private Transform _upperLegL;
        [SerializeField] private Transform _footL;
        [SerializeField] private Transform _upperLegR;
        [SerializeField] private Transform _footR;
        [SerializeField] private Transform _hip;

        [Header("Locomotion Animation Settings")]
        [Tooltip("Overall animation walk speed multiplier. Adjust this slider in Inspector to make the walk animation slower or faster!")]
        [Range(0.1f, 3.0f)]
        [SerializeField] private float _walkAnimationSpeed = 1.0f;

        [Tooltip("Base stride cycle cadence (full steps per second) at standard walk speed")]
        [Range(0.5f, 4.0f)]
        [SerializeField] private float _baseStrideFrequency = 2.0f;

        [Tooltip("Maximum thigh swing angle in degrees")]
        [Range(10.0f, 60.0f)]
        [SerializeField] private float _maxThighAngle = 28.0f;

        [Tooltip("Maximum knee lift bend angle in degrees")]
        [Range(10.0f, 60.0f)]
        [SerializeField] private float _maxKneeAngle = 32.0f;

        [Tooltip("Pelvis vertical bobbing amplitude in meters")]
        [Range(0.0f, 0.08f)]
        [SerializeField] private float _pelvisBobAmplitude = 0.02f;

        [Header("Jump & Airborne Settings")]
        [Tooltip("Knee tuck angle in degrees when airborne")]
        [Range(10.0f, 60.0f)]
        [SerializeField] private float _jumpKneeTuckAngle = 36.0f;

        [Tooltip("Thigh forward swing angle in degrees when airborne")]
        [Range(5.0f, 40.0f)]
        [SerializeField] private float _jumpThighAngle = 18.0f;

        [Tooltip("Maximum vertical squash of pelvis upon landing impact in meters")]
        [Range(0.01f, 0.12f)]
        [SerializeField] private float _maxLandingSquash = 0.05f;

        [Tooltip("Knee flexion angle upon landing impact in degrees")]
        [Range(10.0f, 50.0f)]
        [SerializeField] private float _landingKneeBendAngle = 24.0f;

        [Header("Locomotion Polish Settings")]
        [Tooltip("Hip lateral roll angle during walking")]
        [Range(0.0f, 10.0f)]
        [SerializeField] private float _hipSwayAngle = 2.5f;

        [Tooltip("Forward torso lean angle when sprinting")]
        [Range(0.0f, 20.0f)]
        [SerializeField] private float _sprintLeanAngle = 8.0f;

        public float WalkAnimationSpeed
        {
            get => _walkAnimationSpeed;
            set => _walkAnimationSpeed = Mathf.Max(0.01f, value);
        }

        public float BaseStrideFrequency
        {
            get => _baseStrideFrequency;
            set => _baseStrideFrequency = Mathf.Max(0.1f, value);
        }

        // Cached rest local rotations
        private Quaternion _initUpperLegRotL = Quaternion.identity;
        private Quaternion _initFootRotL = Quaternion.identity;
        private Quaternion _initUpperLegRotR = Quaternion.identity;
        private Quaternion _initFootRotR = Quaternion.identity;
        private Vector3 _initHipLocalPos = Vector3.zero;
        private Quaternion _initHipLocalRot = Quaternion.identity;

        private bool _bonesInitialized = false;
        private float _cyclePhase = 0f;
        private float _strideWeight = 0f;
        private float _smoothedSpeed;
        private float _speedSmoothVelocity;

        // Jump & landing dynamics state
        private bool _wasGrounded = true;
        private float _lastVerticalVelocity = 0f;
        private float _landingSquashWeight = 0f;
        private float _airborneTuckWeight = 0f;

        public float CyclePhase => _cyclePhase;
        public float StrideWeight => _strideWeight;
        public float SmoothedSpeed => _smoothedSpeed;
        public float LandingSquashWeight => _landingSquashWeight;
        public float AirborneTuckWeight => _airborneTuckWeight;

        // Cached player components
        private PlayerMovement _movement;
        private Wallclimb _wallclimb;

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            _wallclimb = GetComponent<Wallclimb>();
            LocateBones();
        }

        private void Start()
        {
            LocateBones();
        }

        public void SetCharacterVisual(Transform visual)
        {
            _characterVisual = visual;
            _bonesInitialized = false;
            LocateBones();
        }

        public void LocateBones()
        {
            if (_characterVisual == null)
            {
                _characterVisual = transform.Find("CharacterVisual");
            }

            if (_characterVisual != null)
            {
                _upperLegL = FindChildRecursive(_characterVisual, "UpperLeg.L");
                _footL = FindChildRecursive(_characterVisual, "Foot.L");

                _upperLegR = FindChildRecursive(_characterVisual, "UpperLeg.R");
                _footR = FindChildRecursive(_characterVisual, "Foot.R");

                _hip = FindChildRecursive(_characterVisual, "Hip");

                if (!_bonesInitialized && _upperLegL != null && _upperLegR != null)
                {
                    _initUpperLegRotL = _upperLegL.localRotation;
                    if (_footL != null) _initFootRotL = _footL.localRotation;

                    _initUpperLegRotR = _upperLegR.localRotation;
                    if (_footR != null) _initFootRotR = _footR.localRotation;

                    if (_hip != null)
                    {
                        _initHipLocalPos = _hip.localPosition;
                        _initHipLocalRot = _hip.localRotation;
                    }

                    _bonesInitialized = true;
                }
            }
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void LateUpdate()
        {
            if (_upperLegL == null || _upperLegR == null)
            {
                LocateBones();
                if (_upperLegL == null || _upperLegR == null) return;
            }

            float deltaTime = Time.deltaTime;
            bool isGrounded = (_movement != null) && _movement.IsGrounded;
            bool isClimbing = (_wallclimb != null) && _wallclimb.IsClimbing;
            float verticalVel = _movement != null ? _movement.Velocity.y : 0f;

            // Handle landing and airborne transitions
            if (isGrounded)
            {
                if (!_wasGrounded && !isClimbing)
                {
                    float impactSpeed = Mathf.Abs(_lastVerticalVelocity);
                    if (impactSpeed > 2.0f)
                    {
                        _landingSquashWeight = Mathf.Clamp01(impactSpeed / 12.0f);
                    }
                }
                _airborneTuckWeight = Mathf.MoveTowards(_airborneTuckWeight, 0f, deltaTime * 10.0f);
                _landingSquashWeight = Mathf.MoveTowards(_landingSquashWeight, 0f, deltaTime * 4.5f);
            }
            else if (!isClimbing)
            {
                _airborneTuckWeight = Mathf.MoveTowards(_airborneTuckWeight, 1f, deltaTime * 8.0f);
                _lastVerticalVelocity = verticalVel;
                _landingSquashWeight = Mathf.MoveTowards(_landingSquashWeight, 0f, deltaTime * 6.0f);
            }
            else
            {
                _airborneTuckWeight = Mathf.MoveTowards(_airborneTuckWeight, 0f, deltaTime * 10.0f);
                _landingSquashWeight = Mathf.MoveTowards(_landingSquashWeight, 0f, deltaTime * 10.0f);
            }
            _wasGrounded = isGrounded;

            float targetSpeed = isGrounded && !isClimbing && _movement != null
                ? _movement.CurrentSpeed : 0f;
            _smoothedSpeed = Mathf.SmoothDamp(_smoothedSpeed, targetSpeed,
                ref _speedSmoothVelocity, 0.12f, Mathf.Infinity, deltaTime);

            // Fade the gait in with actual speed
            float targetWeight = isGrounded && !isClimbing
                ? Mathf.InverseLerp(0.15f, 1.5f, _smoothedSpeed) : 0f;
            _strideWeight = Mathf.MoveTowards(_strideWeight, targetWeight, deltaTime * 6.0f);

            // 1. Reset to base rest rotation before applying transforms
            _upperLegL.localRotation = _initUpperLegRotL;
            if (_footL != null) _footL.localRotation = _initFootRotL;
            _upperLegR.localRotation = _initUpperLegRotR;
            if (_footR != null) _footR.localRotation = _initFootRotR;

            if (_strideWeight > 0.001f)
            {
                // Advance stride cycle phase proportional to character speed and walk animation speed slider
                if (_smoothedSpeed > 0.1f)
                {
                    float speedFactor = Mathf.Clamp(_smoothedSpeed / 5.0f, 0.3f, 1.6f);
                    float stepFreq = _baseStrideFrequency * speedFactor * _walkAnimationSpeed;
                    _cyclePhase = Mathf.Repeat(_cyclePhase + stepFreq * deltaTime * (Mathf.PI * 2.0f),
                        Mathf.PI * 2.0f);
                }

                float sinL = Mathf.Sin(_cyclePhase);
                float sinR = -sinL;

                // Thigh swing (Pitch around character right axis)
                float thighAngleL = sinL * _maxThighAngle * _strideWeight;
                float thighAngleR = sinR * _maxThighAngle * _strideWeight;

                // Knee bend (Lift foot when swinging forward)
                float kneeAngleL = Mathf.Clamp(sinL, 0f, 1f) * _maxKneeAngle * _strideWeight;
                float kneeAngleR = Mathf.Clamp(sinR, 0f, 1f) * _maxKneeAngle * _strideWeight;

                // Add landing squash knee flexion if any
                if (_landingSquashWeight > 0.001f)
                {
                    float squashKnee = _landingSquashWeight * _landingKneeBendAngle;
                    kneeAngleL += squashKnee;
                    kneeAngleR += squashKnee;
                }

                // Apply World Pitch Rotations for Left Leg
                _upperLegL.rotation = Quaternion.AngleAxis(thighAngleL, transform.right) * _upperLegL.rotation;
                if (_footL != null)
                {
                    _footL.rotation = Quaternion.AngleAxis(-kneeAngleL, transform.right) * _footL.rotation;
                }

                // Apply World Pitch Rotations for Right Leg
                _upperLegR.rotation = Quaternion.AngleAxis(thighAngleR, transform.right) * _upperLegR.rotation;
                if (_footR != null)
                {
                    _footR.rotation = Quaternion.AngleAxis(-kneeAngleR, transform.right) * _footR.rotation;
                }
            }
            else if (_airborneTuckWeight > 0.001f)
            {
                // Airborne dynamics: tuck knees and angle thighs based on vertical velocity
                float tuckProg = _airborneTuckWeight;
                float kneeTuck = tuckProg * _jumpKneeTuckAngle;
                float thighTuck = tuckProg * _jumpThighAngle;

                if (verticalVel < -1.0f)
                {
                    // Falling: legs extend downward to prepare for impact
                    float fallFactor = Mathf.Clamp01((-verticalVel - 1.0f) / 12.0f);
                    kneeTuck = Mathf.Lerp(kneeTuck, 10.0f, fallFactor);
                    thighTuck = Mathf.Lerp(thighTuck, 5.0f, fallFactor);
                }

                _upperLegL.rotation = Quaternion.AngleAxis(thighTuck, transform.right) * _upperLegL.rotation;
                if (_footL != null) _footL.rotation = Quaternion.AngleAxis(-kneeTuck, transform.right) * _footL.rotation;

                _upperLegR.rotation = Quaternion.AngleAxis(thighTuck, transform.right) * _upperLegR.rotation;
                if (_footR != null) _footR.rotation = Quaternion.AngleAxis(-kneeTuck, transform.right) * _footR.rotation;
            }
            else if (_landingSquashWeight > 0.001f)
            {
                // Landing impact squash when landing while standing still
                float squashKnee = _landingSquashWeight * _landingKneeBendAngle;
                _upperLegL.rotation = Quaternion.AngleAxis(squashKnee * 0.4f, transform.right) * _upperLegL.rotation;
                if (_footL != null) _footL.rotation = Quaternion.AngleAxis(-squashKnee, transform.right) * _footL.rotation;

                _upperLegR.rotation = Quaternion.AngleAxis(squashKnee * 0.4f, transform.right) * _upperLegR.rotation;
                if (_footR != null) _footR.rotation = Quaternion.AngleAxis(-squashKnee, transform.right) * _footR.rotation;
            }
            else
            {
                // Smoothly restore rest pose
                _upperLegL.localRotation = Quaternion.Slerp(_upperLegL.localRotation, _initUpperLegRotL, deltaTime * 12.0f);
                if (_footL != null) _footL.localRotation = Quaternion.Slerp(_footL.localRotation, _initFootRotL, deltaTime * 12.0f);

                _upperLegR.localRotation = Quaternion.Slerp(_upperLegR.localRotation, _initUpperLegRotR, deltaTime * 12.0f);
                if (_footR != null) _footR.localRotation = Quaternion.Slerp(_footR.localRotation, _initFootRotR, deltaTime * 12.0f);
            }

            // Hip position and rotation polish (bobbing, sway, sprint lean, landing squash)
            if (_hip != null)
            {
                float bob = Mathf.Abs(Mathf.Sin(_cyclePhase)) * _pelvisBobAmplitude * _strideWeight;
                float squashDrop = _landingSquashWeight * _maxLandingSquash;
                float airLift = _airborneTuckWeight * 0.015f;
                _hip.localPosition = _initHipLocalPos + Vector3.up * (bob - squashDrop + airLift);

                float sway = Mathf.Sin(_cyclePhase) * _hipSwayAngle * _strideWeight;
                float sprintLean = Mathf.Clamp01((_smoothedSpeed - 3.5f) / 4.5f) * _sprintLeanAngle * _strideWeight;
                Quaternion dynamicHipRot = _initHipLocalRot * Quaternion.AngleAxis(sway, Vector3.forward) * Quaternion.AngleAxis(sprintLean, Vector3.right);
                _hip.localRotation = Quaternion.Slerp(_hip.localRotation, dynamicHipRot, deltaTime * 15.0f);
            }
        }
    }
}
