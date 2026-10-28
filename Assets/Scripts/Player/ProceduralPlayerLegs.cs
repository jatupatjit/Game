using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// ProceduralPlayerLegs walks the player model's legs (thigh -> shin) without clips: a stride
    /// cycle whose rate and size follow movement speed, with the knee bending as the leg swings
    /// forward. Bones come from <see cref="CharacterRig"/>. Rotations are about the player's own
    /// right axis in world space, so they do not depend on how the model's bones are oriented.
    ///
    /// Once the Animator plays a clip (<see cref="CharacterRig.HasAnimation"/>) the legs are left
    /// to the clip; <see cref="CyclePhase"/> and <see cref="StrideWeight"/> still run for the arms.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public class ProceduralPlayerLegs : MonoBehaviour
    {
        [Header("Rigged Leg Transforms")]
        [SerializeField] private Transform _characterVisual;
        [SerializeField] private Transform _upperLegL;
        [SerializeField] private Transform _lowerLegL;
        [SerializeField] private Transform _upperLegR;
        [SerializeField] private Transform _lowerLegR;

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

        [Tooltip("Maximum knee bend angle in degrees while the leg swings forward")]
        [Range(10.0f, 60.0f)]
        [SerializeField] private float _maxKneeAngle = 32.0f;

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

        // Bind-pose local rotations
        private Quaternion _initUpperLegRotL = Quaternion.identity;
        private Quaternion _initLowerLegRotL = Quaternion.identity;
        private Quaternion _initUpperLegRotR = Quaternion.identity;
        private Quaternion _initLowerLegRotR = Quaternion.identity;

        private bool _bonesInitialized = false;
        private float _cyclePhase = 0f;
        private float _strideWeight = 0f;
        private float _smoothedSpeed;
        private float _speedSmoothVelocity;

        public float CyclePhase => _cyclePhase;
        public float StrideWeight => _strideWeight;
        public float SmoothedSpeed => _smoothedSpeed;

        private PlayerMovement _movement;
        private Wallclimb _wallclimb;
        private CharacterRig _rig;

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            _wallclimb = GetComponent<Wallclimb>();
            _rig = GetComponent<CharacterRig>();
            if (_rig == null) _rig = gameObject.AddComponent<CharacterRig>();
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
            if (_characterVisual == null) _characterVisual = transform.Find(CharacterRig.VisualName);
            if (_characterVisual == null) return;
            if (_rig == null) _rig = GetComponent<CharacterRig>();
            if (_rig == null) _rig = gameObject.AddComponent<CharacterRig>();
            if (_rig.Visual != _characterVisual) _rig.SetVisual(_characterVisual);
            if (_bonesInitialized) return;

            _upperLegL = _rig.UpperLegL;
            _lowerLegL = _rig.LowerLegL;
            _upperLegR = _rig.UpperLegR;
            _lowerLegR = _rig.LowerLegR;
            if (_upperLegL == null || _upperLegR == null) return;

            _initUpperLegRotL = _upperLegL.localRotation;
            _initUpperLegRotR = _upperLegR.localRotation;
            if (_lowerLegL != null) _initLowerLegRotL = _lowerLegL.localRotation;
            if (_lowerLegR != null) _initLowerLegRotR = _lowerLegR.localRotation;
            _bonesInitialized = true;
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
            float targetSpeed = isGrounded && !isClimbing && _movement != null
                ? _movement.CurrentSpeed : 0f;
            _smoothedSpeed = Mathf.SmoothDamp(_smoothedSpeed, targetSpeed,
                ref _speedSmoothVelocity, 0.12f, Mathf.Infinity, deltaTime);

            // Fade the gait in with actual speed, instead of switching it on at one threshold.
            float targetWeight = isGrounded && !isClimbing
                ? Mathf.InverseLerp(0.15f, 1.5f, _smoothedSpeed) : 0f;
            _strideWeight = Mathf.MoveTowards(_strideWeight, targetWeight, deltaTime * 6.0f);

            if (_smoothedSpeed > 0.1f && _strideWeight > 0.001f)
            {
                // Advance stride cycle phase proportional to character speed and walk animation speed slider
                float speedFactor = Mathf.Clamp(_smoothedSpeed / 5.0f, 0.3f, 1.6f);
                float stepFreq = _baseStrideFrequency * speedFactor * _walkAnimationSpeed;
                _cyclePhase = Mathf.Repeat(_cyclePhase + stepFreq * deltaTime * (Mathf.PI * 2.0f),
                    Mathf.PI * 2.0f);
            }

            if (_rig != null && _rig.HasAnimation) return; // The clip owns the legs.

            if (_strideWeight > 0.001f)
            {
                float sinL = Mathf.Sin(_cyclePhase);
                PoseLeg(_upperLegL, _lowerLegL, _initUpperLegRotL, _initLowerLegRotL, sinL);
                PoseLeg(_upperLegR, _lowerLegR, _initUpperLegRotR, _initLowerLegRotR, -sinL);
            }
            else
            {
                // Smoothly restore rest pose
                float t = deltaTime * 12.0f;
                _upperLegL.localRotation = Quaternion.Slerp(_upperLegL.localRotation, _initUpperLegRotL, t);
                _upperLegR.localRotation = Quaternion.Slerp(_upperLegR.localRotation, _initUpperLegRotR, t);
                if (_lowerLegL != null) _lowerLegL.localRotation = Quaternion.Slerp(_lowerLegL.localRotation, _initLowerLegRotL, t);
                if (_lowerLegR != null) _lowerLegR.localRotation = Quaternion.Slerp(_lowerLegR.localRotation, _initLowerLegRotR, t);
            }
        }

        /// <summary>
        /// <paramref name="phase"/> &lt; 0 swings the leg forward (a positive angle about the body's
        /// right axis carries the leg back). The knee bends only on the forward swing, lifting the foot.
        /// </summary>
        private void PoseLeg(Transform thigh, Transform shin, Quaternion thighRest, Quaternion shinRest, float phase)
        {
            float thighAngle = phase * _maxThighAngle * _strideWeight;
            float kneeAngle = Mathf.Clamp01(-phase) * _maxKneeAngle * _strideWeight;

            thigh.localRotation = thighRest;
            if (shin != null) shin.localRotation = shinRest;
            thigh.rotation = Quaternion.AngleAxis(thighAngle, transform.right) * thigh.rotation;
            if (shin != null) shin.rotation = Quaternion.AngleAxis(kneeAngle, transform.right) * shin.rotation;
        }
    }
}
