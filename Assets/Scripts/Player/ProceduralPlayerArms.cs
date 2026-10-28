using System;
using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// ProceduralPlayerArms drives the player model's arms (upper arm -> forearm -> hand) and
    /// fingers with analytic two-bone IK, bounded carry-contact attachment and a procedural
    /// fist (การกำมือ). Bones come from <see cref="CharacterRig"/>, so any Humanoid model works;
    /// each bone's own axes are measured at bind pose instead of assumed.
    ///
    /// Features:
    /// 1. Pole-directed two-bone IK with a continuous anterior reference for stable shoulders.
    /// 2. Fingers close only while really climbing or carrying; reaching keeps the hand open.
    /// 3. Each finger joint curls around the axis its avatar closes it on.
    /// 4. At weight 0 the arms hang beside the hips (or play the Animator's clip once one exists)
    ///    and reaching blends from that pose without snapping.
    /// 5. Tints the body SkinnedMeshRenderer to the player's colour.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(110)]
    public class ProceduralPlayerArms : MonoBehaviour
    {
        [Header("Rigged Skeleton Transforms")]
        [SerializeField] private Transform _characterVisual;
        [SerializeField] private Transform _upperArmL;
        [SerializeField] private Transform _lowerArmL;
        [SerializeField] private Transform _wristL;

        [SerializeField] private Transform _upperArmR;
        [SerializeField] private Transform _lowerArmR;
        [SerializeField] private Transform _wristR;

        [Header("IK Targets (World Space)")]
        [SerializeField] private Transform _leftTargetTransform;
        [SerializeField] private Transform _rightTargetTransform;

        [Header("IK Weights")]
        [Range(0f, 1f)]
        [SerializeField] private float _leftWeight = 0f;

        [Range(0f, 1f)]
        [SerializeField] private float _rightWeight = 0f;

        [Header("Hand Grab Clips (old non-Humanoid rig only)")]
        [Tooltip("Finger-only clip for the left hand. Ignored on a Humanoid model, which curls procedurally.")]
        [SerializeField] private AnimationClip _leftGrabClip;

        [Tooltip("Finger-only clip for the right hand. Ignored on a Humanoid model, which curls procedurally.")]
        [SerializeField] private AnimationClip _rightGrabClip;

        [Header("Rest Pose")]
        [Tooltip("Degrees the idle arms hang out from the body's vertical")]
        [SerializeField, Range(0f, 40f)] private float _restArmSpread = 12f;

        [Header("Finger Grip & Fist Settings (การกำมือ)")]
        [Tooltip("Max curl angle in degrees for Index, Middle, and Pinky fingers when gripping/lifting (procedural fallback)")]
        [SerializeField] private float _fingerCurlAngle = 48.0f;

        [Tooltip("Max curl angle in degrees for Thumb when gripping (procedural fallback)")]
        [SerializeField] private float _thumbCurlAngle = 32.0f;

        [Tooltip("Seconds to fully close or open the fingers; a reversed grip continues from the current pose")]
        [SerializeField, Min(0.01f)] private float _gripTransitionSeconds = 0.25f;

        [Header("Carry Wrist Comfort")]
        [Tooltip("Maximum wrist bend from the rig's neutral hand pose while carrying")]
        [SerializeField, Range(0f, 60f)] private float _carryWristBendLimit = 35f;
        [Tooltip("Maximum hand twist; part of this rotation is distributed along the forearm")]
        [SerializeField, Range(0f, 90f)] private float _carryWristTwistLimit = 70f;

        // Finger chains come from the rig, with each joint's measured curl axis.
        private CharacterRig _rig;
        private CharacterRig.Finger[] _leftFingers;
        private CharacterRig.Finger[] _rightFingers;

        // Bone-local offset between the IK frame (LookRotation(anterior, bone direction))
        // and the bone's own axes, measured at bind pose. Identity on a rig whose bones
        // already point +Y along the limb with +Z to the front.
        private Quaternion _axisFixUpperL = Quaternion.identity;
        private Quaternion _axisFixLowerL = Quaternion.identity;
        private Quaternion _axisFixUpperR = Quaternion.identity;
        private Quaternion _axisFixLowerR = Quaternion.identity;
        // Hand targets are LookRotation(back of hand, fingers); this turns them into the hand bone's axes.
        private Quaternion _handTargetFixL = Quaternion.identity;
        private Quaternion _handTargetFixR = Quaternion.identity;

        // Current grip weights (0 = open hand, 1 = clenched fist)
        private float _currentLeftGrip = 0f;
        private float _currentRightGrip = 0f;

        public AnimationClip LeftGrabClip { get => _leftGrabClip; set => _leftGrabClip = value; }
        public AnimationClip RightGrabClip { get => _rightGrabClip; set => _rightGrabClip = value; }

        // Wrist override flags (only align wrist rotation when actively gripping surfaces)
        public bool LeftOverrideWrist { get; set; } = false;
        public bool RightOverrideWrist { get; set; } = false;

        // Base bone lengths
        private float _leftUpperLen = 0.23f;
        private float _leftLowerLen = 0.17f;
        private float _rightUpperLen = 0.23f;
        private float _rightLowerLen = 0.17f;

        // Carry attachment may extend the rendered arm slightly. Restore imported
        // offsets before every pose so a grip never changes the rig's rest lengths.
        private Vector3 _restLowerPositionL;
        private Vector3 _restWristPositionL;
        private Vector3 _restLowerPositionR;
        private Vector3 _restWristPositionR;
        private const float MaxCarryArmExtension = 1.25f;
        private const float CarryReachMargin = 0.005f;
        private Vector3 _neutralShoulderLocalL;
        private Vector3 _neutralShoulderLocalR;
        private float _naturalReachLocalL;
        private float _naturalReachLocalR;

        // Bind-pose local rotations, read from the model in LocateBones.
        private Quaternion _initUpperRotL = Quaternion.identity;
        private Quaternion _initLowerRotL = Quaternion.identity;
        private Quaternion _initWristRotL = Quaternion.identity;

        private Quaternion _initUpperRotR = Quaternion.identity;
        private Quaternion _initLowerRotR = Quaternion.identity;
        private Quaternion _initWristRotR = Quaternion.identity;

        private bool _bonesInitialized = false;

        // Dynamic targets
        private Vector3 _targetLeftPos;
        private Quaternion _targetLeftRot = Quaternion.identity;
        private Vector3 _targetRightPos;
        private Quaternion _targetRightRot = Quaternion.identity;

        // Renderers & Material Property Block for tinting
        private SkinnedMeshRenderer _skinnedMeshRenderer;
        private MaterialPropertyBlock _propBlock;
        private Animator _grabSamplingAnimator;

        public Transform LeftHand => _leftTargetTransform != null ? _leftTargetTransform : transform;
        public Transform RightHand => _rightTargetTransform != null ? _rightTargetTransform : transform;
        public Transform WristLeft => _wristL;
        public Transform WristRight => _wristR;
        public Vector3 LeftShoulderPosition => _upperArmL != null ? _upperArmL.position : transform.position;
        public Vector3 RightShoulderPosition => _upperArmR != null ? _upperArmR.position : transform.position;
        public float LeftCarryReach => GetCarryReach(true);
        public float RightCarryReach => GetCarryReach(false);

        public float LeftWeight { get => _leftWeight; set => _leftWeight = Mathf.Clamp01(value); }
        public float RightWeight { get => _rightWeight; set => _rightWeight = Mathf.Clamp01(value); }
        public float LeftGripWeight => _currentLeftGrip;
        public float RightGripWeight => _currentRightGrip;

        // Natural rest offsets beside hips in Player local space matching Rigged_character_.fbx
        public static readonly Vector3 LeftHandRestLocal = new Vector3(-0.43f, -0.30f, 0.01f);
        public static readonly Vector3 RightHandRestLocal = new Vector3(0.41f, -0.29f, 0.01f);

        // Natural relaxed rest rotations (arms hang comfortably beside hips with clean clearance)
        private Quaternion _restUpperRotL = Quaternion.identity;
        private Quaternion _restUpperRotR = Quaternion.identity;

        // Cached movement reference & walk swing
        private PlayerMovement _movement;
        private Wallclimb _wallclimb;
        private CoopGame.CarrySystem.PlayerCarry _playerCarry;
        private PlayerInputReader _inputReader;
        private ProceduralPlayerLegs _legs;
        private Unity.Netcode.NetworkObject _networkObject;
        private float _armCyclePhase = 0f;
        private float _walkSwingWeight = 0f;
        private float _requestedLeftWeight = 1f;
        private float _requestedRightWeight = 1f;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            _movement = GetComponent<PlayerMovement>();
            _wallclimb = GetComponent<Wallclimb>();
            _playerCarry = GetComponent<CoopGame.CarrySystem.PlayerCarry>();
            _inputReader = GetComponent<PlayerInputReader>();
            _legs = GetComponent<ProceduralPlayerLegs>();
            _networkObject = GetComponent<Unity.Netcode.NetworkObject>();
            _rig = GetComponent<CharacterRig>();
            if (_rig == null) _rig = gameObject.AddComponent<CharacterRig>();
            EnsureTargetNodesCreated();
            LocateBones();
        }

        private void Start()
        {
            if (_movement == null) _movement = GetComponent<PlayerMovement>();
            if (_wallclimb == null) _wallclimb = GetComponent<Wallclimb>();
            if (_playerCarry == null) _playerCarry = GetComponent<CoopGame.CarrySystem.PlayerCarry>();
            if (_inputReader == null) _inputReader = GetComponent<PlayerInputReader>();
            if (_legs == null) _legs = GetComponent<ProceduralPlayerLegs>();
            EnsureTargetNodesCreated();
            LocateBones();
        }

        public void SetCharacterVisual(Transform visual)
        {
            RestoreArmJointOffsets();
            _characterVisual = visual;
            _bonesInitialized = false;
            if (_rig == null) _rig = GetComponent<CharacterRig>();
            if (_rig == null) _rig = gameObject.AddComponent<CharacterRig>();
            _rig.SetVisual(visual);
            LocateBones();
        }

        private void OnDisable()
        {
            RestoreArmJointOffsets();
        }

        /// <summary>Rendered shoulder and bounded carry reach without using stretched joint offsets.</summary>
        public bool TryGetCarryArmGeometry(bool left, out Vector3 shoulder, out float reach)
        {
            Transform upper = left ? _upperArmL : _upperArmR;
            Transform lower = left ? _lowerArmL : _lowerArmR;
            Transform wrist = left ? _wristL : _wristR;
            shoulder = upper != null ? upper.position : transform.position;
            reach = GetCarryReach(left);
            return _bonesInitialized && upper != null && lower != null && wrist != null;
        }

        /// <summary>
        /// Physics geometry captured before procedural visual motion. FixedUpdate
        /// support and movement use this neutral pose instead of a rendered shoulder.
        /// </summary>
        public bool TryGetCarryPhysicsGeometry(bool left, out Vector3 shoulder, out float reach)
        {
            shoulder = transform.TransformPoint(left ? _neutralShoulderLocalL : _neutralShoulderLocalR);
            float naturalReachLocal = left ? _naturalReachLocalL : _naturalReachLocalR;
            Vector3 rootScale = transform.lossyScale;
            float maximumScale = Mathf.Max(Mathf.Abs(rootScale.x),
                Mathf.Max(Mathf.Abs(rootScale.y), Mathf.Abs(rootScale.z)));
            reach = Mathf.Max(CarryReachMargin,
                naturalReachLocal * maximumScale * MaxCarryArmExtension - CarryReachMargin);
            return _bonesInitialized && naturalReachLocal > 0.0001f && IsFinite(shoulder) && IsFinite(reach);
        }

        private float GetCarryReach(bool left)
        {
            Transform upper = left ? _upperArmL : _upperArmR;
            Transform lower = left ? _lowerArmL : _lowerArmR;
            if (!_bonesInitialized || upper == null || lower == null)
                return (((left ? _leftUpperLen + _leftLowerLen : _rightUpperLen + _rightLowerLen) *
                    MaxCarryArmExtension) - CarryReachMargin);

            Vector3 lowerOffset = left ? _restLowerPositionL : _restLowerPositionR;
            Vector3 wristOffset = left ? _restWristPositionL : _restWristPositionR;
            float upperLength = upper.TransformVector(lowerOffset).magnitude;
            float lowerLength = lower.TransformVector(wristOffset).magnitude;
            return Mathf.Max(CarryReachMargin, ((upperLength + lowerLength) * MaxCarryArmExtension) - CarryReachMargin);
        }

        private void RestoreArmJointOffsets()
        {
            if (!_bonesInitialized) return;
            if (_lowerArmL != null) _lowerArmL.localPosition = _restLowerPositionL;
            if (_wristL != null) _wristL.localPosition = _restWristPositionL;
            if (_lowerArmR != null) _lowerArmR.localPosition = _restLowerPositionR;
            if (_wristR != null) _wristR.localPosition = _restWristPositionR;
        }

        public void EnsureTargetNodesCreated()
        {
            if (_leftTargetTransform == null)
            {
                Transform existing = transform.Find("IKTarget_Left");
                if (existing != null)
                {
                    _leftTargetTransform = existing;
                }
                else
                {
                    GameObject go = new GameObject("IKTarget_Left");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = LeftHandRestLocal;
                    _leftTargetTransform = go.transform;
                }
            }

            if (_rightTargetTransform == null)
            {
                Transform existing = transform.Find("IKTarget_Right");
                if (existing != null)
                {
                    _rightTargetTransform = existing;
                }
                else
                {
                    GameObject go = new GameObject("IKTarget_Right");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = RightHandRestLocal;
                    _rightTargetTransform = go.transform;
                }
            }
        }

        public void LocateBones()
        {
            RestoreArmJointOffsets();
            if (_characterVisual == null)
            {
                _characterVisual = transform.Find(CharacterRig.VisualName);
            }
            if (_characterVisual == null) return;
            if (_rig == null) _rig = GetComponent<CharacterRig>();
            if (_rig == null) _rig = gameObject.AddComponent<CharacterRig>();
            if (_rig.Visual != _characterVisual) _rig.SetVisual(_characterVisual);

            Renderer body = _rig.BodyRenderer;
            _skinnedMeshRenderer = body as SkinnedMeshRenderer;
            if (_skinnedMeshRenderer == null) _skinnedMeshRenderer = _characterVisual.GetComponentInChildren<SkinnedMeshRenderer>();
            if (_skinnedMeshRenderer != null)
                _skinnedMeshRenderer.updateWhenOffscreen = true;

            if (!_bonesInitialized)
            {
                _upperArmL = _rig.UpperArmL;
                _lowerArmL = _rig.LowerArmL;
                _wristL = _rig.HandL;
                _upperArmR = _rig.UpperArmR;
                _lowerArmR = _rig.LowerArmR;
                _wristR = _rig.HandR;
            }

            if (!_bonesInitialized && _upperArmL != null && _upperArmR != null &&
                _lowerArmL != null && _lowerArmR != null && _wristL != null && _wristR != null)
            {
                _initUpperRotL = _upperArmL.localRotation;
                _initLowerRotL = _lowerArmL.localRotation;
                _initWristRotL = _wristL.localRotation;
                _restLowerPositionL = _lowerArmL.localPosition;
                _restWristPositionL = _wristL.localPosition;

                _initUpperRotR = _upperArmR.localRotation;
                _initLowerRotR = _lowerArmR.localRotation;
                _initWristRotR = _wristR.localRotation;
                _restLowerPositionR = _lowerArmR.localPosition;
                _restWristPositionR = _wristR.localPosition;

                _axisFixUpperL = MeasureAxisFix(_upperArmL, _lowerArmL, _wristL, out _axisFixLowerL);
                _axisFixUpperR = MeasureAxisFix(_upperArmR, _lowerArmR, _wristR, out _axisFixLowerR);
                _handTargetFixL = _rig.MeasureHandTargetFix(true);
                _handTargetFixR = _rig.MeasureHandTargetFix(false);

                // Arms hang beside the hips, whatever pose (T, A) the model was exported in.
                _restUpperRotL = HangingUpperRotation(_upperArmL, _lowerArmL, -1f);
                _restUpperRotR = HangingUpperRotation(_upperArmR, _lowerArmR, 1f);

                // Physics geometry from the hanging pose, measured with the bind pose restored after.
                Quaternion bindL = _upperArmL.localRotation, bindR = _upperArmR.localRotation;
                _upperArmL.localRotation = _restUpperRotL;
                _upperArmR.localRotation = _restUpperRotR;
                _neutralShoulderLocalL = transform.InverseTransformPoint(_upperArmL.position);
                _neutralShoulderLocalR = transform.InverseTransformPoint(_upperArmR.position);
                _naturalReachLocalL = transform.InverseTransformVector(_lowerArmL.position - _upperArmL.position).magnitude +
                                      transform.InverseTransformVector(_wristL.position - _lowerArmL.position).magnitude;
                _naturalReachLocalR = transform.InverseTransformVector(_lowerArmR.position - _upperArmR.position).magnitude +
                                      transform.InverseTransformVector(_wristR.position - _lowerArmR.position).magnitude;
                _upperArmL.localRotation = bindL;
                _upperArmR.localRotation = bindR;

                _bonesInitialized = true;
            }

            if (_upperArmL != null && _lowerArmL != null && _wristL != null)
            {
                _leftUpperLen = Vector3.Distance(_upperArmL.position, _lowerArmL.position);
                _leftLowerLen = Vector3.Distance(_lowerArmL.position, _wristL.position);
                if (_leftUpperLen < 0.05f) _leftUpperLen = 0.23f;
                if (_leftLowerLen < 0.05f) _leftLowerLen = 0.17f;
            }

            if (_upperArmR != null && _lowerArmR != null && _wristR != null)
            {
                _rightUpperLen = Vector3.Distance(_upperArmR.position, _lowerArmR.position);
                _rightLowerLen = Vector3.Distance(_lowerArmR.position, _wristR.position);
                if (_rightUpperLen < 0.05f) _rightUpperLen = 0.23f;
                if (_rightLowerLen < 0.05f) _rightLowerLen = 0.17f;
            }

            LocateFingerBones();
        }

        /// <summary>
        /// The IK builds bone rotations as LookRotation(anterior, limb direction). Measure, at bind
        /// pose, the fixed local rotation that turns that frame into this bone's real axes.
        /// </summary>
        private Quaternion MeasureAxisFix(Transform upper, Transform lower, Transform wrist, out Quaternion lowerFix)
        {
            Vector3 upperDir = (lower.position - upper.position).normalized;
            Vector3 lowerDir = (wrist.position - lower.position).normalized;
            Vector3 upperFwd = AnteriorReference(upperDir);
            Vector3 lowerFwd = Vector3.ProjectOnPlane(upperFwd, lowerDir);
            if (lowerFwd.sqrMagnitude < 0.001f) lowerFwd = AnteriorReference(lowerDir);
            lowerFix = Quaternion.Inverse(Quaternion.LookRotation(lowerFwd.normalized, lowerDir)) * lower.rotation;
            return Quaternion.Inverse(Quaternion.LookRotation(upperFwd, upperDir)) * upper.rotation;
        }

        /// <summary>Same anterior reference the IK solvers use for an upper arm pointing along <paramref name="direction"/>.</summary>
        private Vector3 AnteriorReference(Vector3 direction)
        {
            float verticalBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.95f, Mathf.Abs(direction.y)));
            Vector3 verticalRef = direction.y >= 0f ? -transform.forward : transform.forward;
            Vector3 anterior = Vector3.ProjectOnPlane(Vector3.Slerp(transform.up, verticalRef, verticalBlend), direction);
            if (anterior.sqrMagnitude < 0.001f) anterior = Vector3.ProjectOnPlane(transform.forward, direction);
            if (anterior.sqrMagnitude < 0.001f) anterior = Vector3.ProjectOnPlane(transform.right, direction);
            return anterior.normalized;
        }

        /// <summary>Upper-arm local rotation that hangs the arm <see cref="_restArmSpread"/> degrees out from vertical.</summary>
        private Quaternion HangingUpperRotation(Transform upper, Transform lower, float side)
        {
            Vector3 bindDir = (lower.position - upper.position).normalized;
            float spread = _restArmSpread * Mathf.Deg2Rad;
            Vector3 hangDir = transform.TransformDirection(new Vector3(side * Mathf.Sin(spread), -Mathf.Cos(spread), 0f));
            Quaternion world = Quaternion.FromToRotation(bindDir, hangDir) * upper.rotation;
            return upper.parent != null ? Quaternion.Inverse(upper.parent.rotation) * world : world;
        }

        private void LocateFingerBones()
        {
            if (_rig == null) return;
            _leftFingers = _rig.FingersL;
            _rightFingers = _rig.FingersR;

            // Finger clips were authored on the old rig's bone names; a Humanoid model curls procedurally.
            if (_rig.IsHumanoid)
            {
                _leftGrabClip = null;
                _rightGrabClip = null;
            }
        }

        /// <summary>
        /// Sets target position and rotation for the Left Hand.
        /// </summary>
        public void SetLeftHandTarget(Vector3 worldPos, Quaternion worldRot = default, float weight = 1.0f, bool overrideWrist = false)
        {
            _targetLeftPos = worldPos;
            _targetLeftRot = worldRot;
            _requestedLeftWeight = Mathf.Clamp01(weight);
            LeftOverrideWrist = overrideWrist;
            if (_leftTargetTransform != null)
            {
                _leftTargetTransform.position = worldPos;
                if (overrideWrist) _leftTargetTransform.rotation = worldRot;
            }
        }

        /// <summary>
        /// Sets target position and rotation for the Right Hand.
        /// </summary>
        public void SetRightHandTarget(Vector3 worldPos, Quaternion worldRot = default, float weight = 1.0f, bool overrideWrist = false)
        {
            _targetRightPos = worldPos;
            _targetRightRot = worldRot;
            _requestedRightWeight = Mathf.Clamp01(weight);
            RightOverrideWrist = overrideWrist;
            if (_rightTargetTransform != null)
            {
                _rightTargetTransform.position = worldPos;
                if (overrideWrist) _rightTargetTransform.rotation = worldRot;
            }
        }

        /// <summary>
        /// Tints the character SkinnedMeshRenderer (body and gloves) with player ID color.
        /// </summary>
        public void SetArmColor(Color color)
        {
            if (_skinnedMeshRenderer == null && _characterVisual != null)
            {
                _skinnedMeshRenderer = _characterVisual.GetComponentInChildren<SkinnedMeshRenderer>();
            }

            if (_skinnedMeshRenderer != null)
            {
                if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
                _skinnedMeshRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetColor("_BaseColor", color);
                _propBlock.SetColor("_Color", color);
                _skinnedMeshRenderer.SetPropertyBlock(_propBlock);
            }
        }

        public void LocateVisualReferences() => LocateBones();
        public void EnsureArmRenderersCreated() => LocateBones();
        public void SetHands(Transform leftHand, Transform rightHand)
        {
            _leftTargetTransform = leftHand;
            _rightTargetTransform = rightHand;
        }

        private void LateUpdate()
        {
            RestoreArmJointOffsets();
            if (_upperArmL == null || _upperArmR == null)
            {
                LocateBones();
                if (_upperArmL == null || _upperArmR == null) return;
            }

            // Sample cargo after the visual body's interpolation, bob and lean.
            // A FixedUpdate target would lag the displayed object on render frames.
            if (_playerCarry != null) _playerCarry.RefreshCarryHandTargets();
            bool leftCarryAttached = _playerCarry != null && _playerCarry.LeftHandGripping;
            bool rightCarryAttached = _playerCarry != null && _playerCarry.RightHandGripping;

            float deltaTime = Time.deltaTime;
            float currentSpeed = _legs != null ? _legs.SmoothedSpeed
                : (_movement != null ? _movement.CurrentSpeed : 0f);
            bool isGrounded = (_movement != null) && _movement.IsGrounded;
            bool isClimbing = (_wallclimb != null) && _wallclimb.IsClimbing;

            // Locomotion swing calculation (world pitch rotation around transform.right to eliminate bone-roll twist)
            float targetSwingWeight = _legs != null ? _legs.StrideWeight
                : (isGrounded && !isClimbing ? Mathf.InverseLerp(0.15f, 1.5f, currentSpeed) : 0f);
            _walkSwingWeight = Mathf.MoveTowards(_walkSwingWeight, targetSwingWeight, deltaTime * 8.0f);

            Quaternion idleUpperL = _restUpperRotL;
            Quaternion idleUpperR = _restUpperRotR;
            Quaternion idleLowerL = _initLowerRotL;
            Quaternion idleLowerR = _initLowerRotR;
            Quaternion idleWristL = _initWristRotL;
            Quaternion idleWristR = _initWristRotR;

            // A playing clip owns the idle pose; IK reaches out from wherever the clip put the arm.
            bool animated = _rig != null && _rig.HasAnimation;
            if (animated)
            {
                idleUpperL = _upperArmL.localRotation;
                idleUpperR = _upperArmR.localRotation;
                idleLowerL = _lowerArmL.localRotation;
                idleLowerR = _lowerArmR.localRotation;
                idleWristL = _wristL.localRotation;
                idleWristR = _wristR.localRotation;
            }
            else if (_walkSwingWeight > 0.001f)
            {
                if (_legs != null)
                {
                    _armCyclePhase = _legs.CyclePhase;
                }
                else if (currentSpeed > 0.1f)
                {
                    float speedFactor = Mathf.Clamp(currentSpeed / 5.0f, 0.3f, 1.6f);
                    _armCyclePhase = Mathf.Repeat(_armCyclePhase + 2.0f * speedFactor * deltaTime *
                        (Mathf.PI * 2.0f), Mathf.PI * 2.0f);
                }

                float sin = Mathf.Sin(_armCyclePhase);

                // Keep the arm swing restrained and synchronized to the opposite leg.
                float maxSwingAngle = Mathf.Lerp(22.0f, 36.0f, Mathf.Clamp01((currentSpeed - 2.0f) / 6.0f));
                float swingAngleL = -sin * maxSwingAngle * _walkSwingWeight;
                float swingAngleR = sin * maxSwingAngle * _walkSwingWeight;

                // Subtle organic lateral roll (arms flare slightly outward on backswing)
                float lateralL = Mathf.Clamp01(sin) * 3.0f * _walkSwingWeight;
                float lateralR = Mathf.Clamp01(-sin) * 3.0f * _walkSwingWeight;

                // Swing in world space around the body's axes, then back into each bone's parent space
                // (a shoulder or clavicle parent is rarely aligned with the player root).
                Quaternion parentL = _upperArmL.parent != null ? _upperArmL.parent.rotation : transform.rotation;
                Quaternion idleWorldL = Quaternion.AngleAxis(swingAngleL, transform.right) *
                    Quaternion.AngleAxis(-lateralL, transform.forward) * (parentL * _restUpperRotL);
                idleUpperL = Quaternion.Inverse(parentL) * idleWorldL;

                Quaternion parentR = _upperArmR.parent != null ? _upperArmR.parent.rotation : transform.rotation;
                Quaternion idleWorldR = Quaternion.AngleAxis(swingAngleR, transform.right) *
                    Quaternion.AngleAxis(lateralR, transform.forward) * (parentR * _restUpperRotR);
                idleUpperR = Quaternion.Inverse(parentR) * idleWorldR;

                // Natural forearm elbow flexion when arm swings forward
                float elbowFlexL = Mathf.Max(0f, -sin) * 16.0f * _walkSwingWeight;
                float elbowFlexR = Mathf.Max(0f, sin) * 16.0f * _walkSwingWeight;

                Quaternion idleWorldMidL = Quaternion.AngleAxis(elbowFlexL, transform.right) * (idleWorldL * _initLowerRotL);
                idleLowerL = Quaternion.Inverse(idleWorldL) * idleWorldMidL;

                Quaternion idleWorldMidR = Quaternion.AngleAxis(elbowFlexR, transform.right) * (idleWorldR * _initLowerRotR);
                idleLowerR = Quaternion.Inverse(idleWorldR) * idleWorldMidR;
            }

            // 1. Arm Reaching / Aiming IK Weights
            bool leftActive = false;
            bool rightActive = false;

            // 2. Fist Clenching / Gripping: ONLY when ACTUALLY climbing or ACTUALLY carrying!
            bool leftGripping = false;
            bool rightGripping = false;

            // Climbing check
            if (_wallclimb != null)
            {
                if (_wallclimb.LeftHandGripping || _wallclimb.IsPullingUp)
                {
                    leftActive = true;
                    leftGripping = true;
                }
                if (_wallclimb.RightHandGripping || _wallclimb.IsPullingUp)
                {
                    rightActive = true;
                    rightGripping = true;
                }
            }

            // Carrying check
            if (_playerCarry != null)
            {
                if (_playerCarry.LeftHandGripping)
                {
                    leftActive = true;
                    leftGripping = true;
                }
                else if (_playerCarry.IsCarrying && _playerCarry.LeftHandReaching)
                {
                    leftActive = true;
                }
                else if (_playerCarry.LeftHandReaching)
                {
                    leftActive = true; // Reaching in air: fingers stay open!
                }

                if (_playerCarry.RightHandGripping)
                {
                    rightActive = true;
                    rightGripping = true;
                }
                else if (_playerCarry.IsCarrying && _playerCarry.RightHandReaching)
                {
                    rightActive = true;
                }
                else if (_playerCarry.RightHandReaching)
                {
                    rightActive = true; // Reaching in air: fingers stay open!
                }
            }

            // Manual user grab input (reaching arms to aim)
            if (_inputReader != null && (_networkObject == null || !_networkObject.IsSpawned || _networkObject.IsOwner))
            {
                // Left click lifts with both hands; E (interact) reaches with the right hand only.
                if (_inputReader.GrabLeftHeld)
                {
                    leftActive = true; // Reaching arm to aim
                }
                if (_inputReader.GrabLeftHeld || _inputReader.GrabRightHeld || _inputReader.InteractHeld)
                {
                    rightActive = true; // Reaching arm to aim
                }
            }

            float targetWeightL = leftActive ? _requestedLeftWeight : 0.0f;
            float targetWeightR = rightActive ? _requestedRightWeight : 0.0f;
            _leftWeight = Mathf.MoveTowards(_leftWeight, targetWeightL, deltaTime * (targetWeightL > 0.5f ? 15.0f : 10.0f));
            _rightWeight = Mathf.MoveTowards(_rightWeight, targetWeightR, deltaTime * (targetWeightR > 0.5f ? 15.0f : 10.0f));

            // Sync targets if set via transform
            if (_leftTargetTransform != null)
            {
                _targetLeftPos = _leftTargetTransform.position;
                _targetLeftRot = _leftTargetTransform.rotation;
            }
            if (_rightTargetTransform != null)
            {
                _targetRightPos = _rightTargetTransform.position;
                _targetRightRot = _rightTargetTransform.rotation;
            }

            // 1. Solve Left Arm IK (Elbow bends outward to the left, slightly back/down)
            bool isOverheadL = (_targetLeftPos.y > _upperArmL.position.y + 0.15f);
            Vector3 bendHintL = -transform.right * 0.5f - (isOverheadL ? transform.forward * 0.05f : (transform.forward * 0.25f + transform.up * 0.15f));
            Vector3 poleL = _upperArmL.position + bendHintL;
            if (leftCarryAttached)
            {
                SolveCarryTwoBoneIK(_upperArmL, _lowerArmL, _wristL,
                    _targetLeftPos, _targetLeftRot, poleL,
                    idleUpperL, idleLowerL, idleWristL, _leftWeight, true);
            }
            else
            {
                SolveTwoBoneIK(
                    _upperArmL, _lowerArmL, _wristL,
                    _targetLeftPos, _targetLeftRot, LeftOverrideWrist, poleL,
                    _leftUpperLen, _leftLowerLen,
                    idleUpperL, idleLowerL, idleWristL,
                    _leftWeight,
                    true
                );
            }

            // 2. Solve Right Arm IK (Elbow bends outward to the right, slightly back/down)
            bool isOverheadR = (_targetRightPos.y > _upperArmR.position.y + 0.15f);
            Vector3 bendHintR = transform.right * 0.5f - (isOverheadR ? transform.forward * 0.05f : (transform.forward * 0.25f + transform.up * 0.15f));
            Vector3 poleR = _upperArmR.position + bendHintR;
            if (rightCarryAttached)
            {
                SolveCarryTwoBoneIK(_upperArmR, _lowerArmR, _wristR,
                    _targetRightPos, _targetRightRot, poleR,
                    idleUpperR, idleLowerR, idleWristR, _rightWeight, false);
            }
            else
            {
                SolveTwoBoneIK(
                    _upperArmR, _lowerArmR, _wristR,
                    _targetRightPos, _targetRightRot, RightOverrideWrist, poleR,
                    _rightUpperLen, _rightLowerLen,
                    idleUpperR, idleLowerR, idleWristR,
                    _rightWeight,
                    false
                );
            }

            // 3. Apply Anatomically Correct Finger Flexion / Fist Clench (การกำมือ)
            UpdateFingerGrips(leftGripping, rightGripping, deltaTime, animated);
        }

        /// <summary>
        /// Keeps a confirmed carry grip on its object-local contact. Joint positions
        /// are corrected only for this pose and reset before the next rendered frame.
        /// </summary>
        private void SolveCarryTwoBoneIK(
            Transform root, Transform mid, Transform tip,
            Vector3 targetPos, Quaternion targetRot, Vector3 polePos,
            Quaternion baseRotRoot, Quaternion baseRotMid, Quaternion baseRotTip,
            float weight, bool isLeftArm)
        {
            if (root == null || mid == null || tip == null) return;

            root.localRotation = baseRotRoot;
            mid.localRotation = baseRotMid;
            tip.localRotation = baseRotTip;
            if (weight <= 0.001f || !IsFinite(targetPos)) return;

            Vector3 shoulder = root.position;
            Vector3 restElbow = mid.position;
            Vector3 restWrist = tip.position;
            Quaternion restUpperRotation = root.rotation;
            Quaternion restLowerRotation = mid.rotation;
            Quaternion restWristRotation = tip.rotation;

            // Measure after restoring the rig so body squash and mesh scale are
            // accounted for, without feeding last frame's carry extension back in.
            float upperLength = Vector3.Distance(shoulder, restElbow);
            float lowerLength = Vector3.Distance(restElbow, restWrist);
            float naturalReach = upperLength + lowerLength;
            if (upperLength < 0.0001f || lowerLength < 0.0001f || !IsFinite(naturalReach)) return;

            Vector3 toTarget = targetPos - shoulder;
            float rawDistance = toTarget.magnitude;
            if (!IsFinite(rawDistance)) return;
            Vector3 direction = rawDistance > 0.0001f ? toTarget / rawDistance : transform.forward;
            float extension = Mathf.Clamp((rawDistance + CarryReachMargin) / naturalReach,
                1f, MaxCarryArmExtension);
            upperLength *= extension;
            lowerLength *= extension;

            float minReach = Mathf.Abs(upperLength - lowerLength) + CarryReachMargin;
            float maxReach = upperLength + lowerLength - CarryReachMargin;
            if (maxReach <= minReach) return;
            float distance = Mathf.Clamp(rawDistance, minReach, maxReach);
            Vector3 reachableTarget = shoulder + direction * distance;

            float cosAngle = Mathf.Clamp((upperLength * upperLength + distance * distance - lowerLength * lowerLength) /
                (2f * upperLength * distance), -1f, 1f);
            float angle = Mathf.Acos(cosAngle) * Mathf.Rad2Deg;
            Vector3 poleDirection = polePos - shoulder;
            Vector3 planeNormal = Vector3.Cross(direction, poleDirection);
            if (planeNormal.sqrMagnitude < 0.0001f)
                planeNormal = Vector3.Cross(direction, isLeftArm ? -transform.right : transform.right);
            if (planeNormal.sqrMagnitude < 0.0001f)
                planeNormal = Vector3.Cross(direction, transform.up);
            if (planeNormal.sqrMagnitude < 0.0001f)
                planeNormal = Vector3.Cross(direction, transform.forward);
            planeNormal.Normalize();

            Vector3 upperDirection = Quaternion.AngleAxis(angle, planeNormal) * direction;
            float verticalBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.55f, 0.95f, Mathf.Abs(upperDirection.y)));
            Vector3 verticalReference = upperDirection.y >= 0f ? -transform.forward : transform.forward;
            Vector3 anteriorReference = Vector3.Slerp(transform.up, verticalReference, verticalBlend);
            Vector3 upperForward = Vector3.ProjectOnPlane(anteriorReference, upperDirection);
            if (upperForward.sqrMagnitude < 0.001f)
                upperForward = Vector3.ProjectOnPlane(transform.forward, upperDirection);
            if (upperForward.sqrMagnitude < 0.001f)
                upperForward = Vector3.ProjectOnPlane(transform.right, upperDirection);
            upperForward.Normalize();

            Vector3 elbow = shoulder + upperDirection * upperLength;
            Vector3 lowerDirection = (reachableTarget - elbow).normalized;
            Vector3 lowerForward = Vector3.ProjectOnPlane(upperForward, lowerDirection);
            if (lowerForward.sqrMagnitude < 0.001f)
                lowerForward = Vector3.ProjectOnPlane(transform.up, lowerDirection);
            if (lowerForward.sqrMagnitude < 0.001f)
                lowerForward = Vector3.ProjectOnPlane(transform.right, lowerDirection);
            lowerForward.Normalize();

            Quaternion fixUpper = isLeftArm ? _axisFixUpperL : _axisFixUpperR;
            Quaternion fixLower = isLeftArm ? _axisFixLowerL : _axisFixLowerR;
            Quaternion solvedLowerRotation = Quaternion.LookRotation(lowerForward, lowerDirection) * fixLower;
            Quaternion neutralWrist = solvedLowerRotation * baseRotTip;
            Quaternion wristRotation = neutralWrist;
            float rotationMagnitude = Quaternion.Dot(targetRot, targetRot);
            if (IsFinite(targetRot) && IsFinite(rotationMagnitude) && rotationMagnitude > 0.0001f)
            {
                Quaternion handFix = isLeftArm ? _handTargetFixL : _handTargetFixR;
                wristRotation = LimitCarryWristRotation(neutralWrist, Quaternion.Normalize(targetRot) * handFix, out float forearmTwist);
                // Rotate about the forearm's length, keeping elbow and contact
                // positions fixed while sharing pronation away from the wrist seam.
                solvedLowerRotation = Quaternion.AngleAxis(forearmTwist, lowerDirection) * solvedLowerRotation;
            }

            root.rotation = Quaternion.Slerp(restUpperRotation,
                Quaternion.LookRotation(upperForward, upperDirection) * fixUpper, weight);
            mid.position = Vector3.Lerp(restElbow, elbow, weight);
            mid.rotation = Quaternion.Slerp(restLowerRotation,
                solvedLowerRotation, weight);
            tip.position = Vector3.Lerp(restWrist, reachableTarget, weight);
            tip.rotation = Quaternion.Slerp(restWristRotation, wristRotation, weight);
        }

        private Quaternion LimitCarryWristRotation(Quaternion neutral, Quaternion desired, out float forearmTwist)
        {
            // Split swing (bend) from twist around this rig's +Y hand axis.
            // A surface-facing target can otherwise bend a horizontal arm's
            // wrist by 90 degrees when its requested fingers point straight up.
            Quaternion relative = Quaternion.Inverse(neutral) * desired;
            if (relative.w < 0f)
                relative = new Quaternion(-relative.x, -relative.y, -relative.z, -relative.w);
            float twistLength = Mathf.Sqrt(relative.y * relative.y + relative.w * relative.w);
            Quaternion twist = twistLength > 0.0001f
                ? new Quaternion(0f, relative.y / twistLength, 0f, relative.w / twistLength)
                : Quaternion.identity;
            Quaternion swing = relative * Quaternion.Inverse(twist);
            float twistAngle = Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(twist.y, twist.w) * Mathf.Rad2Deg);
            twistAngle = Mathf.Clamp(twistAngle, -_carryWristTwistLimit, _carryWristTwistLimit);
            forearmTwist = twistAngle * .65f;
            return neutral * Quaternion.RotateTowards(Quaternion.identity, swing, _carryWristBendLimit) *
                Quaternion.AngleAxis(twistAngle, Vector3.up);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        private static bool IsFinite(Quaternion value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);

        private void UpdateFingerGrips(bool leftGripping, bool rightGripping, float deltaTime, bool animated)
        {
            // Persistent grip states hold the final pose. Releasing or reversing
            // a hand advances from its current pose without restarting the clip.
            float step = Mathf.Max(0f, deltaTime) / Mathf.Max(0.01f, _gripTransitionSeconds);
            _currentLeftGrip = Mathf.MoveTowards(_currentLeftGrip, leftGripping ? 1f : 0f, step);
            _currentRightGrip = Mathf.MoveTowards(_currentRightGrip, rightGripping ? 1f : 0f, step);
            ApplyFingerGrips(animated);
        }

        private void ApplyFingerGrips(bool animated)
        {
            EnsureGrabSamplingAnimator();

            if (_leftGrabClip != null && _characterVisual != null)
                _leftGrabClip.SampleAnimation(_characterVisual.gameObject, Mathf.Clamp01(_currentLeftGrip) * _leftGrabClip.length);
            else
                CurlFingers(_leftFingers, _currentLeftGrip, animated);

            if (_rightGrabClip != null && _characterVisual != null)
                _rightGrabClip.SampleAnimation(_characterVisual.gameObject, Mathf.Clamp01(_currentRightGrip) * _rightGrabClip.length);
            else
                CurlFingers(_rightFingers, _currentRightGrip, animated);
        }

        /// <summary>
        /// Closes every joint of each finger around its measured curl axis. The base is the bind
        /// pose, or this frame's clip pose when an animation is playing (an open hand then leaves the clip alone).
        /// </summary>
        private void CurlFingers(CharacterRig.Finger[] fingers, float grip, bool animated)
        {
            if (fingers == null) return;
            if (animated && grip <= 0.001f) return;
            foreach (var f in fingers)
            {
                float maxAngle = (f.IsThumb ? _thumbCurlAngle : _fingerCurlAngle) * grip;
                for (int j = 0; j < f.Joints.Length; j++)
                {
                    Transform joint = f.Joints[j];
                    if (joint == null) continue;
                    Quaternion baseRotation = animated ? joint.localRotation : f.RestRotations[j];
                    float share = j == 0 ? 0.70f : (f.IsThumb ? 0.85f : 0.95f);
                    joint.localRotation = grip <= 0.001f
                        ? baseRotation
                        : baseRotation * Quaternion.AngleAxis(maxAngle * share, f.CurlAxes[j]);
                }
            }
        }

        private void EnsureGrabSamplingAnimator()
        {
            // Editor sampling also works without an Animator, but standalone players
            // require one on the exact root passed to SampleAnimation for non-Legacy clips.
            if (!Application.isPlaying || _characterVisual == null ||
                ((_leftGrabClip == null || _leftGrabClip.legacy) &&
                 (_rightGrabClip == null || _rightGrabClip.legacy)))
                return;

            if (_grabSamplingAnimator != null && _grabSamplingAnimator.transform == _characterVisual)
                return;

            _grabSamplingAnimator = _characterVisual.GetComponent<Animator>();
            if (_grabSamplingAnimator != null)
            {
                // Remote hands must keep their sampled pose even when the rig was
                // outside this peer's camera in the preceding frame.
                _grabSamplingAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                return;
            }

            // This controller-free Animator supports explicit finger sampling only.
            // Keep it active offscreen/headless; procedural poses still run in LateUpdate.
            // An Animator already supplied by the rig keeps its controller and timing.
            _grabSamplingAnimator = _characterVisual.gameObject.AddComponent<Animator>();
            _grabSamplingAnimator.applyRootMotion = false;
            _grabSamplingAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _grabSamplingAnimator.updateMode = AnimatorUpdateMode.Normal;
        }

        /// <summary>
        /// Rotation-based analytic two-bone IK with a pole-directed elbow and stable anterior axes.
        /// </summary>
        private void SolveTwoBoneIK(
            Transform root, Transform mid, Transform tip,
            Vector3 targetPos, Quaternion targetRot, bool overrideWrist, Vector3 polePos,
            float l1, float l2,
            Quaternion baseRotRoot, Quaternion baseRotMid, Quaternion baseRotTip,
            float weight,
            bool isLeftArm)
        {
            if (root == null || mid == null || tip == null) return;

            if (weight <= 0.001f)
            {
                // Pure relaxed rest / walk swing pose (no IK applied)
                root.localRotation = baseRotRoot;
                mid.localRotation = baseRotMid;
                tip.localRotation = baseRotTip;
                return;
            }

            // Anti-penetration constraint: Target must always stay in front of the torso plane and not cross the chest
            Vector3 localTarget = transform.InverseTransformPoint(targetPos);
            if (localTarget.z < 0.08f)
            {
                localTarget.z = 0.08f;
            }
            if (isLeftArm)
            {
                if (localTarget.x > -0.05f) localTarget.x = -0.05f;
            }
            else
            {
                if (localTarget.x < 0.05f) localTarget.x = 0.05f;
            }
            targetPos = transform.TransformPoint(localTarget);

            root.localRotation = baseRotRoot;
            mid.localRotation = baseRotMid;
            tip.localRotation = baseRotTip;

            Quaternion restWorldRoot = root.rotation;
            Quaternion restWorldMid = mid.rotation;

            Vector3 shoulderPos = root.position;
            float totalLen = l1 + l2;

            Vector3 toTarget = targetPos - shoulderPos;
            float rawDistance = toTarget.magnitude;
            Vector3 dirTarget = rawDistance > 0.0001f ? toTarget / rawDistance : transform.forward;
            float minReach = Mathf.Abs(l1 - l2) + 0.005f;
            float maxReach = totalLen - 0.005f;
            float dist = Mathf.Clamp(rawDistance, minReach, maxReach);
            Vector3 reachableTarget = shoulderPos + dirTarget * dist;

            // Law of Cosines angles
            float cosA = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
            float angleA = Mathf.Acos(cosA) * Mathf.Rad2Deg;

            // Stable bend plane normal from outward pole
            Vector3 toPole = (polePos - shoulderPos).normalized;
            Vector3 planeNormal = Vector3.Cross(dirTarget, toPole);
            if (planeNormal.sqrMagnitude < 0.0001f)
            {
                planeNormal = isLeftArm ? -transform.up : transform.up;
            }
            planeNormal.Normalize();

            // 1. Desired Upper Arm direction (bending outward along bend plane)
            // The pole already encodes left or right; reversing the angle folds one elbow inward.
            Vector3 desiredUpperDir = Quaternion.AngleAxis(angleA, planeNormal) * dirTarget;

            // Natural anterior reference (bicep / front of arm faces forward/upward, eliminating axial shoulder twist)
            float verticalBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.55f, 0.95f, Mathf.Abs(desiredUpperDir.y)));
            Vector3 verticalRef = desiredUpperDir.y >= 0f ? -transform.forward : transform.forward;
            Vector3 anteriorRef = Vector3.Slerp(transform.up, verticalRef, verticalBlend);
            Vector3 upperBoneFwd = Vector3.ProjectOnPlane(anteriorRef, desiredUpperDir);
            if (upperBoneFwd.sqrMagnitude < 0.001f)
            {
                upperBoneFwd = Vector3.ProjectOnPlane(transform.forward, desiredUpperDir);
            }
            if (upperBoneFwd.sqrMagnitude < 0.001f)
                upperBoneFwd = Vector3.ProjectOnPlane(transform.right, desiredUpperDir);
            upperBoneFwd.Normalize();
            Quaternion targetWorldRoot = Quaternion.LookRotation(upperBoneFwd, desiredUpperDir) *
                (isLeftArm ? _axisFixUpperL : _axisFixUpperR);

            root.rotation = Quaternion.Slerp(restWorldRoot, targetWorldRoot, weight);

            // 2. Desired Lower Arm direction
            Vector3 desiredElbowPos = shoulderPos + desiredUpperDir * l1;
            Vector3 desiredLowerDir = (reachableTarget - desiredElbowPos).normalized;

            Vector3 lowerBoneFwd = Vector3.ProjectOnPlane(upperBoneFwd, desiredLowerDir);
            if (lowerBoneFwd.sqrMagnitude < 0.001f)
            {
                lowerBoneFwd = Vector3.ProjectOnPlane(transform.up, desiredLowerDir);
            }
            if (lowerBoneFwd.sqrMagnitude < 0.001f)
                lowerBoneFwd = Vector3.ProjectOnPlane(transform.right, desiredLowerDir);
            lowerBoneFwd.Normalize();
            Quaternion targetWorldMid = Quaternion.LookRotation(lowerBoneFwd, desiredLowerDir) *
                (isLeftArm ? _axisFixLowerL : _axisFixLowerR);

            mid.rotation = Quaternion.Slerp(restWorldMid, targetWorldMid, weight);

            // 3. Wrist / Palm Surface rotation (only when actively gripping surface)
            if (overrideWrist && targetRot != Quaternion.identity && weight > 0.01f)
            {
                tip.rotation = Quaternion.Slerp(tip.rotation,
                    targetRot * (isLeftArm ? _handTargetFixL : _handTargetFixR), weight);
            }
        }
    }
}
