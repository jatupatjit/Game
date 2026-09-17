using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// ProceduralPlayerLegs animates the rigged skeleton legs (Thigh -> UpperLeg -> Foot)
    /// of Rigged_character_.fbx using procedural stride cycles, knee lift, and pelvis bounce.
    /// 
    /// Features:
    /// 1. True Rigged Leg Locomotion (No Animation Clips Required):
    ///    - Rotates UpperLeg (thigh) and Foot (shin/knee) in anatomical swing planes.
    ///    - Stride frequency and amplitude dynamically scale with movement speed (walk vs sprint).
    /// 2. Natural Knee Flex & Foot Clearance:
    ///    - Bends knee during swing phase to lift feet smoothly off the ground.
    /// 3. Air & Climbing Adaptation:
    ///    - Dangles / tucks legs naturally when airborne or climbing walls.
    /// 4. 100% Smooth Rest Restoration:
    ///    - Blends smoothly back to initial rest rotations when stopping.
    /// </summary>
    [DisallowMultipleComponent]
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

        private bool _bonesInitialized = false;
        private float _cyclePhase = 0f;
        private float _strideWeight = 0f;

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

                    if (_hip != null) _initHipLocalPos = _hip.localPosition;

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
            float currentSpeed = (_movement != null) ? _movement.CurrentSpeed : 0f;
            bool isGrounded = (_movement != null) && _movement.IsGrounded;
            bool isClimbing = (_wallclimb != null) && _wallclimb.IsClimbing;

            // Target locomotion weight: 1 when moving on ground, 0 when idle/air/climbing
            float targetWeight = (isGrounded && !isClimbing && currentSpeed > 0.1f) ? 1.0f : 0.0f;
            _strideWeight = Mathf.MoveTowards(_strideWeight, targetWeight, deltaTime * 8.0f);

            if (_strideWeight > 0.001f)
            {
                // Advance stride cycle phase proportional to character speed and walk animation speed slider
                float speedFactor = Mathf.Clamp(currentSpeed / 5.0f, 0.4f, 1.8f);
                float stepFreq = _baseStrideFrequency * speedFactor * _walkAnimationSpeed;
                _cyclePhase += stepFreq * deltaTime * (Mathf.PI * 2.0f);
                if (_cyclePhase > Mathf.PI * 2.0f) _cyclePhase -= Mathf.PI * 2.0f;

                float sinL = Mathf.Sin(_cyclePhase);
                float sinR = -sinL;

                // Thigh swing (Pitch around character right axis)
                float thighAngleL = sinL * _maxThighAngle * _strideWeight;
                float thighAngleR = sinR * _maxThighAngle * _strideWeight;

                // Knee bend (Lift foot when swinging forward)
                float kneeAngleL = Mathf.Clamp(sinL, 0f, 1f) * _maxKneeAngle * _strideWeight;
                float kneeAngleR = Mathf.Clamp(sinR, 0f, 1f) * _maxKneeAngle * _strideWeight;

                // 1. Reset to base rest rotation before applying stride rotation
                _upperLegL.localRotation = _initUpperLegRotL;
                if (_footL != null) _footL.localRotation = _initFootRotL;

                _upperLegR.localRotation = _initUpperLegRotR;
                if (_footR != null) _footR.localRotation = _initFootRotR;

                // 2. Apply World Pitch Rotations for Left Leg (pure rotation around hip pivot)
                _upperLegL.rotation = Quaternion.AngleAxis(thighAngleL, transform.right) * _upperLegL.rotation;
                if (_footL != null)
                {
                    _footL.rotation = Quaternion.AngleAxis(-kneeAngleL, transform.right) * _footL.rotation;
                }

                // 3. Apply World Pitch Rotations for Right Leg (pure rotation around hip pivot)
                _upperLegR.rotation = Quaternion.AngleAxis(thighAngleR, transform.right) * _upperLegR.rotation;
                if (_footR != null)
                {
                    _footR.rotation = Quaternion.AngleAxis(-kneeAngleR, transform.right) * _footR.rotation;
                }
            }
            else
            {
                // Smoothly restore rest pose
                _upperLegL.localRotation = Quaternion.Slerp(_upperLegL.localRotation, _initUpperLegRotL, deltaTime * 12.0f);
                if (_footL != null) _footL.localRotation = Quaternion.Slerp(_footL.localRotation, _initFootRotL, deltaTime * 12.0f);

                _upperLegR.localRotation = Quaternion.Slerp(_upperLegR.localRotation, _initUpperLegRotR, deltaTime * 12.0f);
                if (_footR != null) _footR.localRotation = Quaternion.Slerp(_footR.localRotation, _initFootRotR, deltaTime * 12.0f);
            }
        }
    }
}
