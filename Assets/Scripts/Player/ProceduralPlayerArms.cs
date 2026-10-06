using System;
using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// ProceduralPlayerArms drives the actual rigged skeleton arms (UpperArm -> LowerArm -> Wrist)
    /// and finger bones of Rigged_character_.fbx using analytic Two-Bone Inverse Kinematics (IK)
    /// with bounded carry-contact attachment and natural procedural finger flexion / fist clenching (การกำมือ).
    /// 
    /// Features:
    /// 1. Pole-directed two-bone IK with a continuous anterior reference for stable shoulders.
    /// 2. Selective Finger Grip (กำมือเฉพาะเวลาปีนหรือยกของจริง):
    ///    - When raising arms in air / aiming: Hands stay OPEN and relaxed (แบมือ).
    ///    - When ACTUALLY climbing a wall or carrying an object: Fingers smoothly CLENCH A FIST (กำมือ).
    /// 3. Solid Palm Integrity:
    ///    - Metacarpal bones (handL..., handR...) stay locked at rest pose, preserving solid palm topology.
    ///    - Only knuckle (joint1) and distal (joint2) phalanges curl along natural -X hinge axis.
    /// 4. Smooth Natural Rest & Reach:
    ///    - When idle (weight = 0), bones sit 100% in natural rest pose beside hips with open hands.
    ///    - When reaching / gripping, smoothly blends from rest pose to target with zero snapping.
    /// 5. Full Multiplayer & Color Tinting:
    ///    - Tints the SkinnedMeshRenderer (body + gloves) to match player ID colors.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(110)]
    public class ProceduralPlayerArms : MonoBehaviour
    {
        [System.Serializable]
        public class FingerPhalanges
        {
            public string fingerName;
            public Transform joint1; // e.g. L.Index1 (Knuckle)
            public Transform joint2; // e.g. L.Index2 (Distal)
            public bool isThumb = false;
            public bool isLeftHand = true;

            private Quaternion _initRot1 = Quaternion.identity;
            private Quaternion _initRot2 = Quaternion.identity;
            private bool _initialized = false;

            public void CacheInitialRotations()
            {
                if (joint1 != null) _initRot1 = joint1.localRotation;
                if (joint2 != null) _initRot2 = joint2.localRotation;
                _initialized = true;
            }

            public void ApplyCurl(float gripWeight, float maxAngle)
            {
                if (!_initialized) CacheInitialRotations();

                if (gripWeight <= 0.001f)
                {
                    if (joint1 != null) joint1.localRotation = _initRot1;
                    if (joint2 != null) joint2.localRotation = _initRot2;
                    return;
                }

                float angle = maxAngle * gripWeight;

                if (isThumb)
                {
                    // Thumb folds inward and across palm
                    float thumbAngle1 = angle * 0.70f;
                    float thumbAngle2 = angle * 0.85f;

                    Vector3 curlAxis1 = isLeftHand ? new Vector3(-1f, 0f, -0.30f).normalized : new Vector3(-1f, 0f, 0.30f).normalized;
                    Vector3 curlAxis2 = new Vector3(-1f, 0f, 0f);

                    if (joint1 != null) joint1.localRotation = _initRot1 * Quaternion.AngleAxis(thumbAngle1, curlAxis1);
                    if (joint2 != null) joint2.localRotation = _initRot2 * Quaternion.AngleAxis(thumbAngle2, curlAxis2);
                }
                else
                {
                    // Clean, natural local -X rotation (hinge flexion into palm)
                    float curl1 = angle * 0.70f;
                    float curl2 = angle * 0.95f;

                    if (joint1 != null) joint1.localRotation = _initRot1 * Quaternion.Euler(-curl1, 0f, 0f);
                    if (joint2 != null) joint2.localRotation = _initRot2 * Quaternion.Euler(-curl2, 0f, 0f);
                }
            }
        }

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

        [Header("Hand Grab Animations (Rigged_Hand_character)")]
        [Tooltip("Finger-only clip for the player's left hand (-X): Armature|LeftGrab, rig Wrist.R")]
        [SerializeField] private AnimationClip _leftGrabClip;

        [Tooltip("Finger-only clip for the player's right hand (+X): Armature|RightGrab, rig Wrist.L")]
        [SerializeField] private AnimationClip _rightGrabClip;

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

        // Finger joint chains (4 fingers per hand)
        private FingerPhalanges[] _leftFingers;
        private FingerPhalanges[] _rightFingers;

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

        // Exact initial local rotations for rest pose from Rigged_character_.fbx
        private Quaternion _initUpperRotL = new Quaternion(0.04446f, 0.04762f, 0.18977f, 0.97966f);
        private Quaternion _initLowerRotL = new Quaternion(0.04222f, 0.03320f, 0.02967f, 0.99812f);
        private Quaternion _initWristRotL = new Quaternion(0.04539f, -0.00575f, -0.01139f, 0.99889f);

        private Quaternion _initUpperRotR = new Quaternion(0.04816f, -0.05083f, -0.18329f, 0.98056f);
        private Quaternion _initLowerRotR = new Quaternion(0.04553f, -0.02868f, 0.00119f, 0.99855f);
        private Quaternion _initWristRotR = new Quaternion(0.02008f, 0.01109f, -0.05448f, 0.99825f);

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
                _characterVisual = transform.Find("CharacterVisual");
            }

            if (_characterVisual != null)
            {
                _skinnedMeshRenderer = _characterVisual.GetComponentInChildren<SkinnedMeshRenderer>();

                // Find UpperArm bones in the hierarchy
                Transform boneL = FindChildRecursive(_characterVisual, "UpperArmL");
                Transform boneR = FindChildRecursive(_characterVisual, "UpperArmR");

                // Spatially map player's left arm (-X) and right arm (+X)
                if (boneL != null && boneR != null)
                {
                    Transform leftBone = (transform.InverseTransformPoint(boneL.position).x < transform.InverseTransformPoint(boneR.position).x) ? boneL : boneR;
                    Transform rightBone = (leftBone == boneL) ? boneR : boneL;

                    _upperArmL = leftBone;
                    _lowerArmL = (_upperArmL.childCount > 0) ? _upperArmL.GetChild(0) : FindChildRecursive(_characterVisual, "LowerArm.R");
                    _wristL = (_lowerArmL != null && _lowerArmL.childCount > 0) ? _lowerArmL.GetChild(0) : FindChildRecursive(_characterVisual, "Wrist.R");

                    _upperArmR = rightBone;
                    _lowerArmR = (_upperArmR.childCount > 0) ? _upperArmR.GetChild(0) : FindChildRecursive(_characterVisual, "LowerArm.L");
                    _wristR = (_lowerArmR != null && _lowerArmR.childCount > 0) ? _lowerArmR.GetChild(0) : FindChildRecursive(_characterVisual, "Wrist.L");
                }
                else
                {
                    if (_upperArmL == null) _upperArmL = FindChildRecursive(_characterVisual, "UpperArmR") ?? FindChildRecursive(_characterVisual, "UpperArmL");
                    if (_lowerArmL == null) _lowerArmL = FindChildRecursive(_characterVisual, "LowerArm.R") ?? FindChildRecursive(_characterVisual, "LowerArm.L");
                    if (_wristL == null) _wristL = FindChildRecursive(_characterVisual, "Wrist.R") ?? FindChildRecursive(_characterVisual, "Wrist.L");

                    if (_upperArmR == null) _upperArmR = FindChildRecursive(_characterVisual, "UpperArmL") ?? FindChildRecursive(_characterVisual, "UpperArmR");
                    if (_lowerArmR == null) _lowerArmR = FindChildRecursive(_characterVisual, "LowerArm.L") ?? FindChildRecursive(_characterVisual, "LowerArm.R");
                    if (_wristR == null) _wristR = FindChildRecursive(_characterVisual, "Wrist.L") ?? FindChildRecursive(_characterVisual, "Wrist.R");
                }

                if (!_bonesInitialized && _upperArmL != null && _upperArmR != null)
                {
                    _initUpperRotL = _upperArmL.localRotation;
                    if (_lowerArmL != null) _initLowerRotL = _lowerArmL.localRotation;
                    if (_wristL != null) _initWristRotL = _wristL.localRotation;
                    if (_lowerArmL != null) _restLowerPositionL = _lowerArmL.localPosition;
                    if (_wristL != null) _restWristPositionL = _wristL.localPosition;

                    _initUpperRotR = _upperArmR.localRotation;
                    if (_lowerArmR != null) _initLowerRotR = _lowerArmR.localRotation;
                    if (_wristR != null) _initWristRotR = _wristR.localRotation;
                    if (_lowerArmR != null) _restLowerPositionR = _lowerArmR.localPosition;
                    if (_wristR != null) _restWristPositionR = _wristR.localPosition;

                    _neutralShoulderLocalL = transform.InverseTransformPoint(_upperArmL.position);
                    _neutralShoulderLocalR = transform.InverseTransformPoint(_upperArmR.position);
                    _naturalReachLocalL = _lowerArmL != null && _wristL != null
                        ? transform.InverseTransformVector(_lowerArmL.position - _upperArmL.position).magnitude +
                          transform.InverseTransformVector(_wristL.position - _lowerArmL.position).magnitude
                        : 0f;
                    _naturalReachLocalR = _lowerArmR != null && _wristR != null
                        ? transform.InverseTransformVector(_lowerArmR.position - _upperArmR.position).magnitude +
                          transform.InverseTransformVector(_wristR.position - _lowerArmR.position).magnitude
                        : 0f;

                    // Natural hanging rest rotations with comfortable clearance from hips (~28 deg)
                    _restUpperRotL = _initUpperRotL * Quaternion.Euler(0f, 0f, 28.0f);
                    _restUpperRotR = _initUpperRotR * Quaternion.Euler(0f, 0f, -28.0f);

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
        }

        private void LocateFingerBones()
        {
            if (_characterVisual == null) return;

            // Locate Left Hand finger phalanges (under _wristL)
            _leftFingers = new FingerPhalanges[]
            {
                CreateFingerPhalanges(_wristL, "Index", false, true),
                CreateFingerPhalanges(_wristL, "Middle", false, true),
                CreateFingerPhalanges(_wristL, "Pinky", false, true),
                CreateFingerPhalanges(_wristL, "thumb", true, true)
            };

            // Locate Right Hand finger phalanges (under _wristR)
            _rightFingers = new FingerPhalanges[]
            {
                CreateFingerPhalanges(_wristR, "Index", false, false),
                CreateFingerPhalanges(_wristR, "Middle", false, false),
                CreateFingerPhalanges(_wristR, "Pinky", false, false),
                CreateFingerPhalanges(_wristR, "thumb", true, false)
            };

            foreach (var f in _leftFingers) if (f != null) f.CacheInitialRotations();
            foreach (var f in _rightFingers) if (f != null) f.CacheInitialRotations();

#if UNITY_EDITOR
            if (_leftGrabClip == null)
            {
                _leftGrabClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/HandGrab_Left.anim");
            }
            if (_rightGrabClip == null)
            {
                _rightGrabClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/HandGrab_Right.anim");
            }
#endif
        }

        private FingerPhalanges CreateFingerPhalanges(Transform wrist, string keyword, bool isThumb, bool isLeft)
        {
            if (wrist == null) return null;

            // Find joint 1 and joint 2 by name under the wrist
            Transform j1 = FindChildContaining(wrist, keyword + "1");
            Transform j2 = FindChildContaining(wrist, keyword + "2");

            return new FingerPhalanges
            {
                fingerName = keyword,
                joint1 = j1,
                joint2 = j2,
                isThumb = isThumb,
                isLeftHand = isLeft
            };
        }

        private static Transform FindChildContaining(Transform parent, string keyword)
        {
            if (parent == null) return null;
            if (parent.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return parent;
            foreach (Transform child in parent)
            {
                Transform found = FindChildContaining(child, keyword);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return parent;
            foreach (Transform child in parent)
            {
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
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

            if (_walkSwingWeight > 0.001f)
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

                // Apply upper arm swing
                Quaternion restWorldL = transform.rotation * _restUpperRotL;
                Quaternion idleWorldL = Quaternion.AngleAxis(swingAngleL, transform.right) * Quaternion.AngleAxis(-lateralL, transform.forward) * restWorldL;
                idleUpperL = Quaternion.Inverse(transform.rotation) * idleWorldL;

                Quaternion restWorldR = transform.rotation * _restUpperRotR;
                Quaternion idleWorldR = Quaternion.AngleAxis(swingAngleR, transform.right) * Quaternion.AngleAxis(lateralR, transform.forward) * restWorldR;
                idleUpperR = Quaternion.Inverse(transform.rotation) * idleWorldR;

                // Natural forearm elbow flexion when arm swings forward
                float elbowFlexL = Mathf.Max(0f, -sin) * 16.0f * _walkSwingWeight;
                float elbowFlexR = Mathf.Max(0f, sin) * 16.0f * _walkSwingWeight;

                Quaternion restWorldMidL = transform.rotation * _initLowerRotL;
                Quaternion idleWorldMidL = Quaternion.AngleAxis(elbowFlexL, transform.right) * restWorldMidL;
                idleLowerL = Quaternion.Inverse(transform.rotation) * idleWorldMidL;

                Quaternion restWorldMidR = transform.rotation * _initLowerRotR;
                Quaternion idleWorldMidR = Quaternion.AngleAxis(elbowFlexR, transform.right) * restWorldMidR;
                idleLowerR = Quaternion.Inverse(transform.rotation) * idleWorldMidR;
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
            if (_inputReader != null)
            {
                if (_inputReader.GrabLeftHeld || _inputReader.InteractHeld)
                {
                    leftActive = true; // Reaching arm to aim
                }
                if (_inputReader.GrabRightHeld || _inputReader.InteractHeld)
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
                    idleUpperL, idleLowerL, _initWristRotL, _leftWeight, true);
            }
            else
            {
                SolveTwoBoneIK(
                    _upperArmL, _lowerArmL, _wristL,
                    _targetLeftPos, _targetLeftRot, LeftOverrideWrist, poleL,
                    _leftUpperLen, _leftLowerLen,
                    idleUpperL, idleLowerL, _initWristRotL,
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
                    idleUpperR, idleLowerR, _initWristRotR, _rightWeight, false);
            }
            else
            {
                SolveTwoBoneIK(
                    _upperArmR, _lowerArmR, _wristR,
                    _targetRightPos, _targetRightRot, RightOverrideWrist, poleR,
                    _rightUpperLen, _rightLowerLen,
                    idleUpperR, idleLowerR, _initWristRotR,
                    _rightWeight,
                    false
                );
            }

            // 3. Apply Anatomically Correct Finger Flexion / Fist Clench (การกำมือ)
            UpdateFingerGrips(leftGripping, rightGripping, deltaTime);
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

            Quaternion solvedLowerRotation = Quaternion.LookRotation(lowerForward, lowerDirection);
            Quaternion neutralWrist = solvedLowerRotation * baseRotTip;
            Quaternion wristRotation = neutralWrist;
            float rotationMagnitude = Quaternion.Dot(targetRot, targetRot);
            if (IsFinite(targetRot) && IsFinite(rotationMagnitude) && rotationMagnitude > 0.0001f)
            {
                wristRotation = LimitCarryWristRotation(neutralWrist, Quaternion.Normalize(targetRot), out float forearmTwist);
                // Rotate about the forearm's length, keeping elbow and contact
                // positions fixed while sharing pronation away from the wrist seam.
                solvedLowerRotation = Quaternion.AngleAxis(forearmTwist, lowerDirection) * solvedLowerRotation;
            }

            root.rotation = Quaternion.Slerp(restUpperRotation,
                Quaternion.LookRotation(upperForward, upperDirection), weight);
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

        private void UpdateFingerGrips(bool leftGripping, bool rightGripping, float deltaTime)
        {
            // Persistent grip states hold the final pose. Releasing or reversing
            // a hand advances from its current pose without restarting the clip.
            float step = Mathf.Max(0f, deltaTime) / Mathf.Max(0.01f, _gripTransitionSeconds);
            _currentLeftGrip = Mathf.MoveTowards(_currentLeftGrip, leftGripping ? 1f : 0f, step);
            _currentRightGrip = Mathf.MoveTowards(_currentRightGrip, rightGripping ? 1f : 0f, step);
            ApplyFingerGrips();
        }

        private void ApplyFingerGrips()
        {
            EnsureGrabSamplingAnimator();

            // 1. Left Hand: Sample custom grab animation from Rigged_Hand_character
            if (_leftGrabClip != null && _characterVisual != null)
            {
                float evalTime = Mathf.Clamp01(_currentLeftGrip) * _leftGrabClip.length;
                _leftGrabClip.SampleAnimation(_characterVisual.gameObject, evalTime);
            }
            else if (_leftFingers != null)
            {
                foreach (var f in _leftFingers)
                {
                    if (f != null) f.ApplyCurl(_currentLeftGrip, f.isThumb ? _thumbCurlAngle : _fingerCurlAngle);
                }
            }

            // 2. Right Hand: Sample custom grab animation from Rigged_Hand_character
            if (_rightGrabClip != null && _characterVisual != null)
            {
                float evalTime = Mathf.Clamp01(_currentRightGrip) * _rightGrabClip.length;
                _rightGrabClip.SampleAnimation(_characterVisual.gameObject, evalTime);
            }
            else if (_rightFingers != null)
            {
                foreach (var f in _rightFingers)
                {
                    if (f != null) f.ApplyCurl(_currentRightGrip, f.isThumb ? _thumbCurlAngle : _fingerCurlAngle);
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
                return;

            // This controller-free Animator supports explicit finger sampling only.
            // Keep it active offscreen/headless; procedural poses still run in LateUpdate.
            // An Animator already supplied by the rig keeps its controller and settings.
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
            Quaternion targetWorldRoot = Quaternion.LookRotation(upperBoneFwd, desiredUpperDir);

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
            Quaternion targetWorldMid = Quaternion.LookRotation(lowerBoneFwd, desiredLowerDir);

            mid.rotation = Quaternion.Slerp(restWorldMid, targetWorldMid, weight);

            // 3. Wrist / Palm Surface rotation (only when actively gripping surface)
            if (overrideWrist && targetRot != Quaternion.identity && weight > 0.01f)
            {
                tip.rotation = Quaternion.Slerp(tip.rotation, targetRot, weight);
            }
        }
    }
}
