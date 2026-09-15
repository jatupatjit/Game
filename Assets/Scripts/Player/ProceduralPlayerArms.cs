using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// ProceduralPlayerArms drives the actual rigged skeleton arms (UpperArm -> LowerArm -> Wrist)
    /// of Rigged_character_.fbx using pure rotation-based Analytic Two-Bone Inverse Kinematics (IK).
    /// 
    /// Features:
    /// 1. True Rigged Bone Control (No Mesh Tearing / No Ribbon Stretch):
    ///    - Rotates only bone joints (UpperArm, LowerArm, Wrist) to preserve local bone hierarchy.
    ///    - Left Arm: UpperArmR -> LowerArm.R -> Wrist.R (spatial left at -X).
    ///    - Right Arm: UpperArmL -> LowerArm.L -> Wrist.L (spatial right at +X).
    /// 2. Smooth Natural Rest & Reach:
    ///    - When idle (weight = 0), bones sit 100% in natural rest pose beside hips.
    ///    - When reaching / gripping, smoothly blends from rest pose to target with zero snapping.
    /// 3. Palm Surface Alignment:
    ///    - Aligns wrist rotation to wall normal or carried item surface only when actively gripping.
    /// 4. Full Multiplayer & Color Tinting:
    ///    - Tints the SkinnedMeshRenderer (body + gloves) to match player ID colors.
    /// </summary>
    [DisallowMultipleComponent]
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

        // Wrist override flags (only align wrist rotation when actively gripping surfaces)
        public bool LeftOverrideWrist { get; set; } = false;
        public bool RightOverrideWrist { get; set; } = false;

        // Base bone lengths
        private float _leftUpperLen = 0.23f;
        private float _leftLowerLen = 0.17f;
        private float _rightUpperLen = 0.23f;
        private float _rightLowerLen = 0.17f;

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

        public Transform LeftHand => _leftTargetTransform != null ? _leftTargetTransform : transform;
        public Transform RightHand => _rightTargetTransform != null ? _rightTargetTransform : transform;
        public Transform WristLeft => _wristL;
        public Transform WristRight => _wristR;

        public float LeftWeight { get => _leftWeight; set => _leftWeight = Mathf.Clamp01(value); }
        public float RightWeight { get => _rightWeight; set => _rightWeight = Mathf.Clamp01(value); }

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
        private float _armCyclePhase = 0f;
        private float _walkSwingWeight = 0f;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            _movement = GetComponent<PlayerMovement>();
            _wallclimb = GetComponent<Wallclimb>();
            _playerCarry = GetComponent<CoopGame.CarrySystem.PlayerCarry>();
            _inputReader = GetComponent<PlayerInputReader>();
            EnsureTargetNodesCreated();
            LocateBones();
        }

        private void Start()
        {
            if (_movement == null) _movement = GetComponent<PlayerMovement>();
            if (_wallclimb == null) _wallclimb = GetComponent<Wallclimb>();
            if (_playerCarry == null) _playerCarry = GetComponent<CoopGame.CarrySystem.PlayerCarry>();
            if (_inputReader == null) _inputReader = GetComponent<PlayerInputReader>();
            EnsureTargetNodesCreated();
            LocateBones();
        }

        public void SetCharacterVisual(Transform visual)
        {
            _characterVisual = visual;
            _bonesInitialized = false;
            LocateBones();
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

                    _initUpperRotR = _upperArmR.localRotation;
                    if (_lowerArmR != null) _initLowerRotR = _lowerArmR.localRotation;
                    if (_wristR != null) _initWristRotR = _wristR.localRotation;

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

        /// <summary>
        /// Sets target position and rotation for the Left Hand.
        /// </summary>
        public void SetLeftHandTarget(Vector3 worldPos, Quaternion worldRot = default, float weight = 1.0f, bool overrideWrist = false)
        {
            _targetLeftPos = worldPos;
            _targetLeftRot = worldRot;
            _leftWeight = Mathf.Clamp01(weight);
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
            _rightWeight = Mathf.Clamp01(weight);
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
            if (_upperArmL == null || _upperArmR == null)
            {
                LocateBones();
                if (_upperArmL == null || _upperArmR == null) return;
            }

            float deltaTime = Time.deltaTime;
            float currentSpeed = (_movement != null) ? _movement.CurrentSpeed : 0f;
            bool isGrounded = (_movement != null) && _movement.IsGrounded;
            bool isClimbing = (_wallclimb != null) && _wallclimb.IsClimbing;

            // Locomotion swing calculation (world pitch rotation around transform.right to eliminate bone-roll twist)
            float targetSwingWeight = (isGrounded && !isClimbing && currentSpeed > 0.1f) ? 1.0f : 0.0f;
            _walkSwingWeight = Mathf.MoveTowards(_walkSwingWeight, targetSwingWeight, deltaTime * 6.0f);

            Quaternion idleUpperL = _restUpperRotL;
            Quaternion idleUpperR = _restUpperRotR;
            Quaternion idleLowerL = _initLowerRotL;
            Quaternion idleLowerR = _initLowerRotR;

            if (_walkSwingWeight > 0.001f)
            {
                float speedFactor = Mathf.Clamp(currentSpeed / 5.0f, 0.6f, 1.8f);
                _armCyclePhase += 5.2f * speedFactor * deltaTime * (Mathf.PI * 2.0f);
                if (_armCyclePhase > Mathf.PI * 2.0f) _armCyclePhase -= Mathf.PI * 2.0f;

                float sin = Mathf.Sin(_armCyclePhase);

                // Expressive, visible swing amplitude: ~36° on normal walk, ramping up to ~52° on sprint!
                float maxSwingAngle = Mathf.Lerp(36.0f, 52.0f, Mathf.Clamp01((currentSpeed - 2.0f) / 6.0f));
                float swingAngleL = -sin * maxSwingAngle * _walkSwingWeight;
                float swingAngleR = sin * maxSwingAngle * _walkSwingWeight;

                // Subtle organic lateral roll (arms flare slightly outward on backswing)
                float lateralL = Mathf.Clamp01(sin) * 5.0f * _walkSwingWeight;
                float lateralR = Mathf.Clamp01(-sin) * 5.0f * _walkSwingWeight;

                // Apply upper arm swing
                Quaternion restWorldL = transform.rotation * _restUpperRotL;
                Quaternion idleWorldL = Quaternion.AngleAxis(swingAngleL, transform.right) * Quaternion.AngleAxis(-lateralL, transform.forward) * restWorldL;
                idleUpperL = Quaternion.Inverse(transform.rotation) * idleWorldL;

                Quaternion restWorldR = transform.rotation * _restUpperRotR;
                Quaternion idleWorldR = Quaternion.AngleAxis(swingAngleR, transform.right) * Quaternion.AngleAxis(lateralR, transform.forward) * restWorldR;
                idleUpperR = Quaternion.Inverse(transform.rotation) * idleWorldR;

                // Natural forearm elbow flexion when arm swings forward
                float elbowFlexL = Mathf.Max(0f, -sin) * 20.0f * _walkSwingWeight;
                float elbowFlexR = Mathf.Max(0f, sin) * 20.0f * _walkSwingWeight;

                Quaternion restWorldMidL = transform.rotation * _initLowerRotL;
                Quaternion idleWorldMidL = Quaternion.AngleAxis(elbowFlexL, transform.right) * restWorldMidL;
                idleLowerL = Quaternion.Inverse(transform.rotation) * idleWorldMidL;

                Quaternion restWorldMidR = transform.rotation * _initLowerRotR;
                Quaternion idleWorldMidR = Quaternion.AngleAxis(elbowFlexR, transform.right) * restWorldMidR;
                idleLowerR = Quaternion.Inverse(transform.rotation) * idleWorldMidR;
            }

            // Dynamic IK weight blending based on climbing, carrying, or grab input
            bool leftActive = false;
            bool rightActive = false;

            if (_wallclimb != null)
            {
                if (_wallclimb.LeftHandGripping || _wallclimb.IsPullingUp) leftActive = true;
                if (_wallclimb.RightHandGripping || _wallclimb.IsPullingUp) rightActive = true;
            }

            if (_playerCarry != null)
            {
                if (_playerCarry.LeftHandGripping) leftActive = true;
                if (_playerCarry.RightHandGripping) rightActive = true;
            }

            if (_inputReader != null)
            {
                if (_inputReader.GrabLeftHeld || _inputReader.InteractHeld) leftActive = true;
                if (_inputReader.GrabRightHeld || _inputReader.InteractHeld) rightActive = true;
            }

            float targetWeightL = leftActive ? 1.0f : 0.0f;
            float targetWeightR = rightActive ? 1.0f : 0.0f;
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

            // 1. Solve Left Arm IK (Elbow bends outward to the left, back, and slightly down)
            Vector3 bendHintL = -transform.right * 0.5f - transform.forward * 0.3f - transform.up * 0.2f;
            Vector3 poleL = _upperArmL.position + bendHintL;
            SolveTwoBoneIK(
                _upperArmL, _lowerArmL, _wristL,
                _targetLeftPos, _targetLeftRot, LeftOverrideWrist, poleL,
                _leftUpperLen, _leftLowerLen,
                idleUpperL, idleLowerL, _initWristRotL,
                _leftWeight,
                true
            );

            // 2. Solve Right Arm IK (Elbow bends outward to the right, back, and slightly down)
            Vector3 bendHintR = transform.right * 0.5f - transform.forward * 0.3f - transform.up * 0.2f;
            Vector3 poleR = _upperArmR.position + bendHintR;
            SolveTwoBoneIK(
                _upperArmR, _lowerArmR, _wristR,
                _targetRightPos, _targetRightRot, RightOverrideWrist, poleR,
                _rightUpperLen, _rightLowerLen,
                idleUpperR, idleLowerR, _initWristRotR,
                _rightWeight,
                false
            );
        }

        /// <summary>
        /// Pure Rotation-Based Analytic Law of Cosines Two-Bone IK with direct forward-aligned orientation.
        /// Rotates only joints with zero translation, preserving rigged mesh topology with zero gimbal roll twist at all angles.
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

            // Anti-penetration constraint: Clamp target in front of the shoulder plane so arm never bends backwards through the body
            Vector3 localTarget = transform.InverseTransformPoint(targetPos);
            if (localTarget.z < -0.05f)
            {
                localTarget.z = -0.05f;
                targetPos = transform.TransformPoint(localTarget);
            }

            root.localRotation = baseRotRoot;
            mid.localRotation = baseRotMid;
            tip.localRotation = baseRotTip;

            Quaternion restWorldRoot = root.rotation;
            Quaternion restWorldMid = mid.rotation;

            Vector3 shoulderPos = root.position;
            float totalLen = l1 + l2;

            Vector3 toTarget = targetPos - shoulderPos;
            float dist = Mathf.Clamp(toTarget.magnitude, 0.001f, totalLen * 0.999f);
            Vector3 dirTarget = toTarget.normalized;

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
            float bendSign = isLeftArm ? 1.0f : -1.0f;
            Vector3 desiredUpperDir = Quaternion.AngleAxis(angleA * bendSign, planeNormal) * dirTarget;

            // Direct forward-aligned rotation construction to eliminate 180-degree flip singularity
            Vector3 upperBoneZ = Vector3.ProjectOnPlane(transform.forward, desiredUpperDir).normalized;
            if (upperBoneZ.sqrMagnitude < 0.001f)
            {
                upperBoneZ = Vector3.ProjectOnPlane(isLeftArm ? -transform.right : transform.right, desiredUpperDir).normalized;
            }
            Quaternion targetWorldRoot = Quaternion.LookRotation(upperBoneZ, desiredUpperDir);

            root.rotation = Quaternion.Slerp(restWorldRoot, targetWorldRoot, weight);

            // 2. Desired Lower Arm direction
            Vector3 desiredElbowPos = shoulderPos + desiredUpperDir * l1;
            Vector3 desiredLowerDir = (targetPos - desiredElbowPos).normalized;

            Vector3 lowerBoneZ = Vector3.ProjectOnPlane(transform.forward, desiredLowerDir).normalized;
            if (lowerBoneZ.sqrMagnitude < 0.001f)
            {
                lowerBoneZ = upperBoneZ;
            }
            Quaternion targetWorldMid = Quaternion.LookRotation(lowerBoneZ, desiredLowerDir);

            mid.rotation = Quaternion.Slerp(restWorldMid, targetWorldMid, weight);

            // 3. Wrist / Palm Surface rotation (only when actively gripping surface)
            if (overrideWrist && targetRot != Quaternion.identity && weight > 0.01f)
            {
                tip.rotation = Quaternion.Slerp(tip.rotation, targetRot, weight);
            }
        }
    }
}

