using System;
using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// Active ragdoll for the player model.
    ///
    /// The procedural/Animator pose on CharacterVisual is the <i>target</i>. On the owner, 16 physics bodies
    /// (hips, spine, chest, head, arms, legs) joined by ConfigurableJoints chase that target with slerp drives,
    /// the hips pulled to the animated hips by a spring. What is drawn is the physics pose, so the body lags,
    /// overshoots and wobbles. A hard landing (or <see cref="Knockdown"/>) drops the drives: the body falls limp,
    /// then stands back up.
    ///
    /// Gameplay never sees the physics pose: it is written into the bones at the end of LateUpdate (drawn) and
    /// the animated pose is put back before the next frame's FixedUpdate/Update, so movement, carry and climbing keep working on
    /// the animated skeleton they were tuned on. Bodies have colliders only while knocked down, so ground
    /// checks and aim rays never hit the player's own limbs.
    ///
    /// Other players' copies have no bodies: <see cref="RagdollPoseSync"/> streams the owner's drawn pose and
    /// this component draws it, so everyone sees the same movement.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(30000)]
    public sealed class ActiveRagdoll : MonoBehaviour
    {
        public const int BoneCount = 16;

        // Body bones, parents before children. Parent index -1 = the hips anchor.
        private static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.UpperChest, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
        };
        private static readonly int[] Parents = { -1, 0, 1, 2, 2, 4, 5, 2, 7, 8, 0, 10, 11, 0, 13, 14 };
        private static readonly float[] Masses = { 10f, 8f, 10f, 5f, 2.5f, 1.5f, .6f, 2.5f, 1.5f, .6f, 7f, 4f, 1.2f, 7f, 4f, 1.2f };
        // Per-joint stiffness: wrists and elbows held firmly so the swing reads from the shoulder, not a flapping hand.
        private static readonly float[] SpringScale = { 1f, 1f, 1f, 1f, 1f, 4f, 12f, 1f, 4f, 12f, 1f, 1f, 2f, 1f, 1f, 2f };
        private const int ArmL0 = 4, ArmR0 = 7; // upper arm indices; lower arm and hand follow

        [Header("Drives")]
        [Tooltip("Joint spring per kg of the moved body: how hard each limb chases the animated pose")]
        [SerializeField] private float _jointSpring = 1100f;
        [Tooltip("Joint damping per kg. Low (about 12) lets the limbs keep up with a big swing; high makes them lag")]
        [SerializeField] private float _jointDamper = 12f;
        [Tooltip("Spring holding the hips on the animated hips (N per metre)")]
        [SerializeField] private float _hipsPositionSpring = 30000f;
        [SerializeField] private float _hipsPositionDamper = 1500f;
        [SerializeField] private float _hipsRotationSpring = 12000f;
        [SerializeField] private float _hipsRotationDamper = 600f;

        [Header("Drawn pose")]
        [Tooltip("1 = draw the physics pose, 0 = draw the animation")]
        [SerializeField, Range(0f, 1f)] private float _physicsWeight = 1f;
        [Tooltip("Physics weight of an arm whose hand is gripping (cargo or wall), so the hand stays on its contact")]
        [SerializeField, Range(0f, 1f)] private float _grippingArmWeight = .15f;
        [Tooltip("Seconds of smoothing on the drawn wobble (0 = raw physics)")]
        [SerializeField, Range(0f, .2f)] private float _drawSmoothing = .025f;

        [Header("Knockdown")]
        [Tooltip("Landing speed (m/s) that knocks the player down")]
        [SerializeField] private float _knockdownFallSpeed = 12f;
        [SerializeField] private float _knockdownSeconds = 1.6f;
        [SerializeField] private float _standUpSeconds = .9f;
        [Tooltip("Share of the joint springs kept while limp (0 = rag)")]
        [SerializeField, Range(0f, 1f)] private float _limpSpringShare = .03f;

        private CharacterRig _rig;
        private PlayerMovement _movement;
        private CharacterController _controller;
        private CoopGame.CarrySystem.PlayerCarry _carry;
        private Wallclimb _wallclimb;
        private ProceduralPlayerArms _arms;
        private Unity.Netcode.NetworkObject _networkObject;

        private readonly Transform[] _bones = new Transform[BoneCount];
        private bool _ready;

        // Owner physics
        private GameObject _physicsRoot;
        private Rigidbody _anchor;
        private readonly Rigidbody[] _bodies = new Rigidbody[BoneCount];
        private readonly ConfigurableJoint[] _joints = new ConfigurableJoint[BoneCount];
        private readonly Collider[] _colliders = new Collider[BoneCount];
        private readonly Quaternion[] _restRelative = new Quaternion[BoneCount];
        private readonly Quaternion[] _stepDesired = new Quaternion[BoneCount];
        private readonly Quaternion[] _deviation = new Quaternion[BoneCount];
        private readonly Quaternion[] _prevDeviation = new Quaternion[BoneCount];
        private readonly Quaternion[] _smoothDeviation = InitIdentity();
        private Vector3 _smoothDevHipsPos;
        private Quaternion _smoothDevHipsRot = Quaternion.identity;

        private static Quaternion[] InitIdentity()
        {
            var q = new Quaternion[BoneCount];
            for (int i = 0; i < BoneCount; i++) q[i] = Quaternion.identity;
            return q;
        }
        private Vector3 _prevDevHipsPos;
        private Quaternion _prevDevHipsRot = Quaternion.identity;
        private readonly Quaternion[] _drawn = new Quaternion[BoneCount];
        private Vector3 _devHipsPos;
        private Quaternion _devHipsRot = Quaternion.identity;
        private bool _collidersOn;

        // Animated pose captured this frame (world), drives the joints in the next FixedUpdates.
        private readonly Quaternion[] _animRot = new Quaternion[BoneCount];
        private Vector3 _animHipsPos;
        private bool _hasAnimPose;

        // Restore after rendering
        private readonly Quaternion[] _savedLocalRot = new Quaternion[BoneCount];
        private Vector3 _savedHipsLocalPos;
        private bool _poseOverwritten;
        private static int _restoreSubscribers;
        private static event Action RestoreAll;

        // Knockdown
        private float _knockdownUntil = -1f;
        private float _standUpUntil = -1f;
        private bool _wasGrounded = true;
        private float _airVerticalSpeed;
        private bool _wasKnockedDown;
        private Vector3 _lastRootPosition;

        // Remote: drawn pose from the owner's stream
        private RagdollPoseSync _sync;
        private readonly Quaternion[] _remoteLocal = new Quaternion[BoneCount];

        public bool IsKnockedDown => Time.time < _knockdownUntil;
        public bool IsRecovering => !IsKnockedDown && Time.time < _standUpUntil;
        private bool IsLocallySimulated => _networkObject == null || !_networkObject.IsSpawned || _networkObject.IsOwner;

        private void Awake()
        {
            _rig = GetComponent<CharacterRig>();
            _movement = GetComponent<PlayerMovement>();
            _controller = GetComponent<CharacterController>();
            _carry = GetComponent<CoopGame.CarrySystem.PlayerCarry>();
            _wallclimb = GetComponent<Wallclimb>();
            _arms = GetComponent<ProceduralPlayerArms>();
            _networkObject = GetComponent<Unity.Netcode.NetworkObject>();
            _sync = GetComponent<RagdollPoseSync>();
        }

        private void OnEnable()
        {
            RestoreAll += RestoreAnimatedPose;
            _restoreSubscribers++;
            PoseRestorer.Ensure();
        }

        private void OnDisable()
        {
            RestoreAnimatedPose();
            RestoreAll -= RestoreAnimatedPose;
            _restoreSubscribers--;
            DestroyPhysics();
        }

        /// <summary>
        /// Puts every player's animated pose back before anything else runs in the next frame (FixedUpdate and
        /// Update), after the drawn pose has been rendered at the end of this one.
        /// </summary>
        [DefaultExecutionOrder(-32000)]
        private sealed class PoseRestorer : MonoBehaviour
        {
            private static PoseRestorer _instance;

            public static void Ensure()
            {
                if (_instance != null) return;
                var go = new GameObject("ActiveRagdollPoseRestorer") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<PoseRestorer>();
            }

            private void FixedUpdate() => RestoreAll?.Invoke();
            private void Update() => RestoreAll?.Invoke();
        }

        private bool EnsureBones()
        {
            if (_ready) return true;
            if (_rig == null || !_rig.IsHumanoid) return false;
            Animator animator = _rig.Animator;
            for (int i = 0; i < BoneCount; i++)
            {
                Transform bone = animator.GetBoneTransform(Bones[i]);
                if (bone == null && Bones[i] == HumanBodyBones.UpperChest) bone = animator.GetBoneTransform(HumanBodyBones.Chest);
                if (bone == null) return false;
                _bones[i] = bone;
            }
            // Keep the rig's left/right (it swaps mirrored rigs to the player's sides).
            _bones[ArmL0] = _rig.UpperArmL; _bones[ArmL0 + 1] = _rig.LowerArmL; _bones[ArmL0 + 2] = _rig.HandL;
            _bones[ArmR0] = _rig.UpperArmR; _bones[ArmR0 + 1] = _rig.LowerArmR; _bones[ArmR0 + 2] = _rig.HandR;
            _ready = true;
            return true;
        }

        /// <summary>Call when CharacterVisual is replaced.</summary>
        public void Rebuild()
        {
            RestoreAnimatedPose();
            DestroyPhysics();
            _ready = false;
            _hasAnimPose = false;
        }

        private void FixedUpdate()
        {
            if (!IsLocallySimulated || !_hasAnimPose || !EnsureBones()) return;
            if (_physicsRoot == null) BuildPhysics();

            DetectHardLanding();
            bool down = IsKnockedDown;
            if (_wasKnockedDown && !down && _controller != null && _controller.enabled)
            {
                // Stand up where the body fell. The body itself stays put: only the reference moves, so the
                // drawn pose does not jump.
                Vector3 before = transform.position;
                _controller.Move(KnockdownRootOffset());
                Vector3 moved = transform.position - before;
                _animHipsPos += moved;
                _devHipsPos -= moved;
                _prevDevHipsPos -= moved;
                _smoothDevHipsPos -= moved;
            }
            _wasKnockedDown = down;
            UpdateDrives();
            UpdateColliders();

            // Hips anchor rides the animated hips; every joint chases the animated relative rotation.
            // Remember what this step asked for: the drawn pose is the animation plus the physics' deviation
            // from these targets, so the body never trails the animation by a physics step.
            // Previous step kept so the drawn deviation is interpolated between physics steps, not stepped
            // at 50 Hz on a faster screen.
            _prevDevHipsPos = _devHipsPos;
            _prevDevHipsRot = _devHipsRot;
            System.Array.Copy(_deviation, _prevDeviation, BoneCount);
            _devHipsPos = _bodies[0].position - _anchor.position;
            _devHipsRot = Quaternion.Inverse(_anchor.rotation) * _bodies[0].rotation;
            for (int i = 1; i < BoneCount; i++)
            {
                Quaternion actual = Quaternion.Inverse(_bodies[Parents[i]].rotation) * _bodies[i].rotation;
                _deviation[i] = Quaternion.Inverse(_stepDesired[i]) * actual;
            }

            _anchor.MovePosition(_animHipsPos);
            _anchor.MoveRotation(_animRot[0]);
            for (int i = 1; i < BoneCount; i++)
            {
                Quaternion desired = Quaternion.Inverse(_animRot[Parents[i]]) * _animRot[i];
                _stepDesired[i] = desired;
                _joints[i].targetRotation = Quaternion.Inverse(desired) * _restRelative[i];
            }

            // A body thrown far from the player (teleport, respawn) is put back instead of dragged.
            if ((_bodies[0].position - _animHipsPos).sqrMagnitude > 9f) SnapBodiesToAnimation();
        }

        private void LateUpdate()
        {
            if (!EnsureBones()) return;

            // 1. The animated pose this frame, before anything is drawn over it.
            for (int i = 0; i < BoneCount; i++)
            {
                _animRot[i] = _bones[i].rotation;
                _savedLocalRot[i] = _bones[i].localRotation;
            }
            _animHipsPos = _bones[0].position;
            _savedHipsLocalPos = _bones[0].localPosition;
            _hasAnimPose = true;

            // 2. Draw the physics pose (owner) or the streamed pose (everyone else).
            DrawnSource = "animation";
            if (IsLocallySimulated)
            {
                if (_physicsRoot == null) return;
                DrawPhysicsPose();
                DrawnSource = "physics";
                if (_sync != null) _sync.PublishDrawnPose(_bones, _rig.Visual);
            }
            else if (_sync != null && _sync.TrySampleRemotePose(_remoteLocal, out Vector3 hipsLocal, out Quaternion hipsRot))
            {
                DrawRemotePose(hipsLocal, hipsRot);
                DrawnSource = "stream";
            }
            DrawnHeadPosition = _bones[3].position;
            DrawnHipsPosition = _bones[0].position;
        }

        /// <summary>Diagnostics: where this machine drew the head and hips last frame, and from what.</summary>
        public Vector3 DrawnHeadPosition { get; private set; }
        public Vector3 DrawnHipsPosition { get; private set; }
        public string DrawnSource { get; private set; } = "none";
        public string Diagnostics => _physicsRoot == null ? "no physics"
            : $"anim={_animHipsPos:F2} anchor={_anchor.position:F2} body={_bodies[0].position:F2} dev={_devHipsPos:F2} root={transform.position:F2} physRoot={_physicsRoot.transform.position:F2} parent={(_physicsRoot.transform.parent != null ? _physicsRoot.transform.parent.name : "-")}";

        private void DrawPhysicsPose()
        {
            float knockdownBlend = IsKnockedDown ? 1f
                : IsRecovering ? Mathf.SmoothStep(0f, 1f, (_standUpUntil - Time.time) / _standUpSeconds) : 0f;
            float weight = Mathf.Lerp(_physicsWeight, 1f, knockdownBlend);
            float leftArm = Mathf.Lerp(ArmWeight(true), 1f, knockdownBlend);
            float rightArm = Mathf.Lerp(ArmWeight(false), 1f, knockdownBlend);

            // Animation + weighted physics deviation, parents first (a knocked-down body is all deviation).
            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            // Light low-pass on top of the step interpolation: the drawn wobble flows instead of ticking
            // at the physics rate. A knockdown follows the bodies directly.
            float follow = knockdownBlend > 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(.001f, _drawSmoothing));
            _smoothDevHipsPos = Vector3.Lerp(_smoothDevHipsPos, Vector3.Lerp(_prevDevHipsPos, _devHipsPos, alpha), follow);
            _smoothDevHipsRot = Quaternion.Slerp(_smoothDevHipsRot, Quaternion.Slerp(_prevDevHipsRot, _devHipsRot, alpha), follow);
            Vector3 devPos = _smoothDevHipsPos;
            Quaternion devRot = _smoothDevHipsRot;

            _poseOverwritten = true;
            _drawn[0] = _animRot[0] * Quaternion.Slerp(Quaternion.identity, devRot, weight);
            _bones[0].position = _animHipsPos + devPos * weight;
            _bones[0].rotation = _drawn[0];
            for (int i = 1; i < BoneCount; i++)
            {
                float w = i >= ArmL0 && i < ArmL0 + 3 ? leftArm : i >= ArmR0 && i < ArmR0 + 3 ? rightArm : weight;
                Quaternion animRelative = Quaternion.Inverse(_animRot[Parents[i]]) * _animRot[i];
                _smoothDeviation[i] = Quaternion.Slerp(_smoothDeviation[i], Quaternion.Slerp(_prevDeviation[i], _deviation[i], alpha), follow);
                Quaternion deviation = _smoothDeviation[i];
                _drawn[i] = _drawn[Parents[i]] * animRelative * Quaternion.Slerp(Quaternion.identity, deviation, w);
                _bones[i].rotation = _drawn[i];
            }
        }

        private float ArmWeight(bool left)
        {
            float grip = _arms != null ? (left ? _arms.LeftGripWeight : _arms.RightGripWeight) : 0f;
            return Mathf.Lerp(_physicsWeight, _grippingArmWeight, Mathf.Clamp01(grip * 2f));
        }

        private void DrawRemotePose(Vector3 hipsLocal, Quaternion hipsRot)
        {
            Transform visual = _rig.Visual;
            // A remote arm on cargo or a wall keeps this machine's IK, so its hand meets the contact drawn here.
            bool keepLeft = _arms != null && _arms.LeftGripWeight > .5f;
            bool keepRight = _arms != null && _arms.RightGripWeight > .5f;

            _poseOverwritten = true;
            _bones[0].position = visual.TransformPoint(hipsLocal);
            _bones[0].rotation = visual.rotation * hipsRot;
            for (int i = 1; i < BoneCount; i++)
            {
                bool keep = (keepLeft && i >= ArmL0 && i < ArmL0 + 3) || (keepRight && i >= ArmR0 && i < ArmR0 + 3);
                if (keep) _bones[i].rotation = _animRot[i];
                else _bones[i].localRotation = _remoteLocal[i];
            }
        }

        private void RestoreAnimatedPose()
        {
            if (!_poseOverwritten || !_ready) return;
            _poseOverwritten = false;
            _bones[0].localPosition = _savedHipsLocalPos;
            for (int i = 0; i < BoneCount; i++) _bones[i].localRotation = _savedLocalRot[i];
        }

        // ───────────────────────── Owner physics ─────────────────────────

        private void BuildPhysics()
        {
            _physicsRoot = new GameObject($"ActiveRagdoll_{name}") { hideFlags = HideFlags.DontSave };
            int layer = LayerMask.NameToLayer("Ignore Raycast");

            var anchorGo = new GameObject("HipsAnchor");
            anchorGo.transform.SetParent(_physicsRoot.transform, false);
            // Everything from the bones as they are now: the player may have been moved (spawn teleport)
            // since the pose was captured in LateUpdate, and a mismatch here would become a permanent offset.
            anchorGo.transform.SetPositionAndRotation(_bones[0].position, _bones[0].rotation);
            _anchor = anchorGo.AddComponent<Rigidbody>();
            _anchor.isKinematic = true;

            for (int i = 0; i < BoneCount; i++)
            {
                var go = new GameObject(Bones[i].ToString());
                go.layer = layer;
                go.transform.SetParent(_physicsRoot.transform, false);
                go.transform.SetPositionAndRotation(_bones[i].position, _bones[i].rotation);
                var body = go.AddComponent<Rigidbody>();
                body.mass = Masses[i];
                body.interpolation = RigidbodyInterpolation.None; // drawn as a deviation from the animation
                body.solverIterations = 12;
                body.solverVelocityIterations = 4;
                body.maxAngularVelocity = 30f;
                body.linearDamping = .05f;
                body.angularDamping = .5f;
                _bodies[i] = body;
                _colliders[i] = BuildCollider(go, i);
                _colliders[i].enabled = false;
            }

            for (int i = 0; i < BoneCount; i++)
            {
                Rigidbody parent = Parents[i] < 0 ? _anchor : _bodies[Parents[i]];
                var joint = _bodies[i].gameObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = parent;
                joint.axis = Vector3.right;
                joint.secondaryAxis = Vector3.up;
                joint.anchor = Vector3.zero;
                // Fixed at the bind pose. Left automatic, Unity re-measures it whenever a drive changes, and a
                // player who has moved since (spawn teleport) would keep the body offset from the hips for good.
                joint.autoConfigureConnectedAnchor = false;
                joint.connectedAnchor = Parents[i] < 0 ? Vector3.zero : parent.transform.InverseTransformPoint(_bodies[i].position);
                joint.enableCollision = false;
                joint.rotationDriveMode = RotationDriveMode.Slerp;
                bool hips = i == 0;
                joint.xMotion = joint.yMotion = joint.zMotion = hips ? ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
                joint.projectionMode = JointProjectionMode.PositionAndRotation;
                joint.projectionDistance = .05f;
                _joints[i] = joint;
                _restRelative[i] = Quaternion.Inverse(parent.rotation) * _bodies[i].rotation;
                _stepDesired[i] = _restRelative[i];
                _deviation[i] = _prevDeviation[i] = Quaternion.identity;
            }

            for (int a = 0; a < BoneCount; a++)
            {
                for (int b = a + 1; b < BoneCount; b++) Physics.IgnoreCollision(_colliders[a], _colliders[b]);
                if (_controller != null) Physics.IgnoreCollision(_colliders[a], _controller);
                foreach (var own in GetComponentsInChildren<Collider>(true))
                    if (own != _controller) Physics.IgnoreCollision(_colliders[a], own);
            }
            ApplyDrives(1f);
        }

        private Collider BuildCollider(GameObject go, int index)
        {
            Transform bone = _bones[index];
            Transform t = go.transform;
            Vector3 toChild = ChildDirection(index, bone);
            Vector3 local = t.InverseTransformVector(toChild);
            float length = local.magnitude;
            HumanBodyBones kind = Bones[index];

            if (kind == HumanBodyBones.Head)
            {
                var sphere = go.AddComponent<SphereCollider>();
                float radius = Mathf.Max(.12f, length * .5f);
                sphere.radius = radius;
                sphere.center = local.normalized * radius;
                return sphere;
            }
            if (kind == HumanBodyBones.Hips || kind == HumanBodyBones.Spine || kind == HumanBodyBones.UpperChest)
            {
                var box = go.AddComponent<BoxCollider>();
                float width = Vector3.Distance(_rig.UpperArmL.position, _rig.UpperArmR.position) * .7f;
                box.size = new Vector3(width, Mathf.Max(.1f, length), width * .6f);
                box.center = local * .5f;
                return box;
            }

            var capsule = go.AddComponent<CapsuleCollider>();
            Vector3 abs = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
            capsule.direction = abs.x > abs.y && abs.x > abs.z ? 0 : abs.y > abs.z ? 1 : 2;
            bool hand = kind == HumanBodyBones.LeftHand || kind == HumanBodyBones.RightHand;
            bool foot = kind == HumanBodyBones.LeftFoot || kind == HumanBodyBones.RightFoot;
            bool leg = kind == HumanBodyBones.LeftUpperLeg || kind == HumanBodyBones.RightUpperLeg ||
                       kind == HumanBodyBones.LeftLowerLeg || kind == HumanBodyBones.RightLowerLeg;
            float r = hand ? .05f : foot ? .06f : leg ? .085f : .06f;
            capsule.radius = r;
            capsule.height = Mathf.Max(length, r * 2f + .01f);
            capsule.center = local * .5f;
            return capsule;
        }

        /// <summary>World vector from a body bone to the end of its segment.</summary>
        private Vector3 ChildDirection(int index, Transform bone)
        {
            for (int i = index + 1; i < BoneCount; i++)
                if (Parents[i] == index && Bones[i] != HumanBodyBones.LeftUpperArm && Bones[i] != HumanBodyBones.RightUpperArm &&
                    Bones[i] != HumanBodyBones.LeftUpperLeg && Bones[i] != HumanBodyBones.RightUpperLeg)
                    return _bones[i].position - bone.position;
            // Leaf (head, hand, foot) or hips: follow the longest child chain one level down.
            Transform best = null;
            float bestSqr = 0f;
            foreach (Transform child in bone)
            {
                float sqr = (child.position - bone.position).sqrMagnitude;
                if (sqr > bestSqr) { bestSqr = sqr; best = child; }
            }
            Vector3 v = best != null ? best.position - bone.position : bone.up * .1f;
            if (Bones[index] == HumanBodyBones.Hips) v = _bones[1].position - bone.position;
            return v;
        }

        private void ApplyDrives(float strength)
        {
            for (int i = 0; i < BoneCount; i++)
            {
                var joint = _joints[i];
                if (joint == null) continue;
                if (i == 0)
                {
                    var linear = new JointDrive { positionSpring = _hipsPositionSpring * strength, positionDamper = _hipsPositionDamper * Mathf.Max(strength, .05f), maximumForce = float.MaxValue };
                    joint.xDrive = joint.yDrive = joint.zDrive = linear;
                    joint.slerpDrive = new JointDrive { positionSpring = _hipsRotationSpring * strength, positionDamper = _hipsRotationDamper * Mathf.Max(strength, .05f), maximumForce = float.MaxValue };
                }
                else
                {
                    float subtree = SubtreeMass(i) * SpringScale[i];
                    joint.slerpDrive = new JointDrive { positionSpring = _jointSpring * subtree * strength, positionDamper = _jointDamper * subtree * Mathf.Max(strength, .2f), maximumForce = float.MaxValue };
                }
            }
        }

        private float SubtreeMass(int index)
        {
            float mass = Masses[index];
            for (int i = index + 1; i < BoneCount; i++)
            {
                int p = Parents[i];
                while (p > index) p = Parents[p];
                if (p == index) mass += Masses[i];
            }
            return mass;
        }

        private float _appliedStrength = -1f;

        private void UpdateDrives()
        {
            float strength = 1f;
            if (IsKnockedDown) strength = 0f;
            else if (IsRecovering) strength = Mathf.SmoothStep(0f, 1f, 1f - (_standUpUntil - Time.time) / _standUpSeconds);
            // Limp joints keep a little spring (hips none) so the body folds, not crumples.
            float applied = Mathf.Max(strength, _limpSpringShare);
            if (Mathf.Abs(applied - _appliedStrength) < .01f) return;
            _appliedStrength = applied;
            ApplyDrives(applied);
            if (strength <= 0f)
            {
                var none = new JointDrive { maximumForce = float.MaxValue };
                _joints[0].xDrive = _joints[0].yDrive = _joints[0].zDrive = none;
                _joints[0].slerpDrive = none;
            }
        }

        private void UpdateColliders()
        {
            bool on = IsKnockedDown || IsRecovering;
            if (on == _collidersOn) return;
            _collidersOn = on;
            for (int i = 0; i < BoneCount; i++) _colliders[i].enabled = on;
        }

        private void SnapBodiesToAnimation()
        {
            for (int i = 0; i < BoneCount; i++)
            {
                _bodies[i].position = _bones[i].position;
                _bodies[i].rotation = _animRot[i];
                _bodies[i].linearVelocity = Vector3.zero;
                _bodies[i].angularVelocity = Vector3.zero;
            }
            _bodies[0].position = _animHipsPos;
            _anchor.position = _animHipsPos;
            _anchor.rotation = _animRot[0];
            _devHipsPos = Vector3.zero;
            _devHipsRot = Quaternion.identity;
            for (int i = 0; i < BoneCount; i++) _deviation[i] = _prevDeviation[i] = Quaternion.identity;
            _prevDevHipsPos = Vector3.zero;
            _prevDevHipsRot = Quaternion.identity;
        }

        private void DestroyPhysics()
        {
            if (_physicsRoot != null) Destroy(_physicsRoot);
            _physicsRoot = null;
            _collidersOn = false;
            _appliedStrength = -1f;
        }

        // ───────────────────────── Knockdown ─────────────────────────

        private void DetectHardLanding()
        {
            if (_movement == null) return;
            // A teleport (respawn after falling off, checkpoint) is not a landing.
            if ((transform.position - _lastRootPosition).sqrMagnitude > 4f) { _airVerticalSpeed = 0f; _wasGrounded = true; }
            _lastRootPosition = transform.position;
            bool grounded = _movement.IsGrounded;
            if (!grounded) _airVerticalSpeed = Mathf.Min(_airVerticalSpeed, _movement.Velocity.y);
            else if (!_wasGrounded && -_airVerticalSpeed >= _knockdownFallSpeed)
                Knockdown(Vector3.down * (-_airVerticalSpeed * 2f));
            if (grounded) _airVerticalSpeed = 0f;
            _wasGrounded = grounded;
        }

        /// <summary>Owner: go limp for a moment (hazards may call this). Ignored while carrying or climbing.</summary>
        public void Knockdown(Vector3 impulse)
        {
            if (!IsLocallySimulated || _physicsRoot == null || IsKnockedDown) return;
            if ((_carry != null && _carry.IsCarrying) || (_wallclimb != null && _wallclimb.IsClimbing)) return;
            _knockdownUntil = Time.time + _knockdownSeconds;
            _standUpUntil = _knockdownUntil + _standUpSeconds;
            _bodies[0].AddForce(impulse * .5f, ForceMode.Impulse);
            _bodies[2].AddForce(impulse * .5f, ForceMode.Impulse);
            _bodies[2].AddTorque(Vector3.Cross(Vector3.up, transform.forward) * 4f, ForceMode.Impulse);
            Debug.Log($"[ActiveRagdoll] {name} knocked down ({impulse.magnitude:F1}).");
        }

        /// <summary>True for this player's own physics limbs (camera and other probes skip them).</summary>
        public bool OwnsCollider(Collider other) =>
            _physicsRoot != null && other != null && other.transform.IsChildOf(_physicsRoot.transform);

        /// <summary>Owner, as the body stands up: bring the player to where the body lies.</summary>
        public Vector3 KnockdownRootOffset()
        {
            if (_physicsRoot == null) return Vector3.zero;
            Vector3 offset = _bodies[0].position - _animHipsPos;
            offset.y = 0f;
            return Vector3.ClampMagnitude(offset, 2f);
        }
    }
}
