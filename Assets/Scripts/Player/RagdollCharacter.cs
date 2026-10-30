using System.Collections.Generic;
using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// The full-physics player (default since 2026-10-30; untick Full Ragdoll for the old animated player).
    ///
    /// The owner simulates 16 jointed bodies. Every body is held up and moved by the same acceleration (so the figure
    /// walks as one), the hips and chest are torqued upright toward the camera heading, and the joint drives chase the
    /// pose of a hidden animated clone (KayKit idle/walk/run/jump, playback rate matched to the body's speed). Falling
    /// hard or tipping over turns the drives off until the body stands back up.
    ///
    /// Carry: left click lifts a crate, right click sets it down, Q throws. The crate attaches this player as a carrier
    /// and steers itself with the tuned hold-pose code (<see cref="CarryableObject"/>); the server owns that, so a
    /// client asks for it by RPC. The arms reach the grips by two-bone IK from a still hold pose.
    ///
    /// Climb: <see cref="Wallclimb"/> (left mouse = left hand, right mouse = right hand) drives the climb exactly as in
    /// the old player, moving the root with the CharacterController. While it climbs the bodies are kinematic and posed
    /// from the root: the body hangs from the grips, the arms reach Wallclimb's hand targets by IK.
    ///
    /// The player root follows the hips, so the camera, UI, checkpoints and respawn keep working. Other machines draw
    /// the owner's pose from <see cref="RagdollPoseSync"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(40)] // after Wallclimb's FixedUpdate; LateUpdate before the camera (60)
    public sealed class RagdollCharacter : NetworkBehaviour
    {
        public const int BoneCount = 16;
        private static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.UpperChest, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
        };
        private static readonly int[] Parents = { -1, 0, 1, 2, 2, 4, 5, 2, 7, 8, 0, 10, 11, 0, 13, 14 };
        private static readonly float[] Masses = { 12f, 8f, 10f, 5f, 2.5f, 1.5f, .6f, 2.5f, 1.5f, .6f, 7f, 4f, 1.2f, 7f, 4f, 1.2f };
        // Arms are light and swing fast at run speed; somewhat stiffer drives keep them closer to the animation. What is
        // left (~28 deg at sprint cadence) is the two-physics-step delay of chasing the clone, not a smaller swing.
        private static readonly float[] SpringScale = { 1f, 1.5f, 1.5f, 1f, ArmSpring, ArmSpring * 2f, 8f, ArmSpring, ArmSpring * 2f, 8f, 1f, 1f, 2f, 1f, 1f, 2f };
        private const float ArmSpring = 3f;
        private const int Hips = 0, Chest = 2, Head = 3, ArmL = 4, ArmR = 7, HandL = 6, HandR = 9, LegL = 10, LegR = 13;

        [Header("Mode")]
        [Tooltip("On: the full physics player. Off: the old animated player with the visual ragdoll (option A).")]
        [SerializeField] private bool _fullRagdoll = true;

        [Header("Movement")]
        [SerializeField] private float _walkSpeed = 4f;
        [SerializeField] private float _sprintSpeed = 6.5f;
        [SerializeField] private float _acceleration = 25f;
        [SerializeField] private float _airAcceleration = 6f;
        [SerializeField] private float _jumpSpeed = 5.5f;
        [Tooltip("Stamina per second while sprinting (same as PlayerMovement)")]
        [SerializeField] private float _sprintStaminaDrain = 15f;

        [Header("Balance")]
        [SerializeField] private float _standSpring = 120f;
        [SerializeField] private float _standDamper = 14f;
        [SerializeField] private float _uprightSpring = 900f;
        [SerializeField] private float _uprightDamper = 60f;

        [Header("Joints")]
        [SerializeField] private float _jointSpring = 1400f;
        [SerializeField] private float _jointDamper = 20f;

        [Header("Animated Clone")]
        [Tooltip("Hidden copy of the character that plays the animations; the joints chase its pose. Empty = procedural walk pose.")]
        [SerializeField] private GameObject _cloneModel;
        [SerializeField] private RuntimeAnimatorController _cloneController;
        [Tooltip("Body speed (m/s) at which the walk clip is fully blended in")]
        [SerializeField] private float _walkBlendSpeed = 1.2f;
        [Tooltip("Body speed (m/s) at which the run clip is fully blended in")]
        [SerializeField] private float _runBlendSpeed = 4f;
        [Tooltip("Speed (m/s) the walk clip's feet travel at playback rate 1")]
        [SerializeField] private float _walkClipSpeed = .9f;
        [Tooltip("Speed (m/s) the run clip's feet travel at playback rate 1")]
        [SerializeField] private float _runClipSpeed = 1.7f;
        [SerializeField] private float _maxMoveRate = 2.4f;
        [Tooltip("Replaces the controller's idle clip (empty = Idle_A). Scenes/AnimationPreview shows every option.")]
        [SerializeField] private AnimationClip _idleClip;
        [Tooltip("Replaces the controller's walk clip (empty = Walking_A)")]
        [SerializeField] private AnimationClip _walkClip;
        [Tooltip("Replaces the controller's run clip (empty = Running_A)")]
        [SerializeField] private AnimationClip _runClip;

        [Header("Carry (left click lift, right click place, Q throw)")]
        [Tooltip("How far from the body centre a crate may be to lift it")]
        [SerializeField] private float _carryPickupReach = 1.1f;
        [Tooltip("Half the distance between the hands on a wide crate's near face")]
        [SerializeField] private float _carryHandSpread = .2f;
        [Tooltip("Largest force (N) each hand spring uses to stay on the crate")]
        [SerializeField] private float _carryHandForce = 150f;
        [Tooltip("How hard the body is drawn back to the crate when it lags behind (m/s^2)")]
        [SerializeField] private float _carryLeash = 20f;
        [SerializeField] private float _throwSpeed = 8f;
        [SerializeField] private float _throwStaminaCost = 8f;

        [Header("First Person")]
        [Tooltip("In first person the owner sees only their forearms and hands")]
        [SerializeField] private bool _firstPersonHands = true;

        [Header("Crate Contact")]
        [Tooltip("The body (not the hands) treats cargo as immovable; a bump only nudges it at this speed (m/s)")]
        [SerializeField] private float _bumpNudgeSpeed = .3f;
        [Tooltip("Largest impulse (N s) a free hand may give cargo per contact point")]
        [SerializeField] private float _handMaxImpulse = .4f;

        [Header("Walk Pose (no clone)")]
        [SerializeField] private float _strideFrequency = 1.8f;
        [SerializeField] private float _thighSwing = 30f;
        [SerializeField] private float _kneeBend = 40f;
        [SerializeField] private float _armSwing = 25f;

        [Header("Knockdown")]
        [SerializeField] private float _knockdownFallSpeed = 12f;
        [SerializeField] private float _knockdownTilt = 70f;
        [SerializeField] private float _knockdownSeconds = 1.5f;
        [SerializeField] private float _standUpSeconds = .8f;

        public bool FullRagdoll => _fullRagdoll;
        public bool IsKnockedDown => Time.time < _knockdownUntil;
        public bool IsGrounded { get; private set; }
        public bool IsClimbing => _climbing;
        public bool HoldingCargo => _carry != null;
        public CarryableObject CarriedCargo => _carry;
        public bool OwnsCollider(Collider c) => _physicsRoot != null && c != null && c.transform.IsChildOf(_physicsRoot.transform);

        private CharacterRig _rig;
        private CharacterController _controller;
        private PlayerInputReader _input;
        private PlayerCameraController _camera;
        private RagdollPoseSync _sync;
        private Wallclimb _wallclimb;
        private ProceduralPlayerArms _ikTargets; // owns the hand target transforms Wallclimb moves
        private PlayerStamina _stamina;
        private CoopGame.Network.PlayerExpeditionState _expedition;
        private readonly Transform[] _bones = new Transform[BoneCount];
        private bool _bonesReady;

        // Owner physics
        private GameObject _physicsRoot;
        private readonly Rigidbody[] _bodies = new Rigidbody[BoneCount];
        private readonly ConfigurableJoint[] _joints = new ConfigurableJoint[BoneCount];
        private readonly Collider[] _colliders = new Collider[BoneCount];
        private readonly Quaternion[] _restRelative = new Quaternion[BoneCount];
        private readonly Quaternion[] _restLocal = new Quaternion[BoneCount]; // world rotation in the upright facing frame
        private readonly Quaternion[] _target = new Quaternion[BoneCount];
        private readonly Vector3[] _anchorInParent = new Vector3[BoneCount]; // joint position in the parent body's frame
        private readonly Vector3[] _boneAxis = new Vector3[BoneCount];       // local direction toward the next bone (arms)
        private readonly float[] _boneLength = new float[BoneCount];
        private float _standHeight, _hipsAboveRoot;
        private float _phase, _strideWeight, _knockdownUntil = -1f, _standUpUntil = -1f, _airSpeed;
        private Vector3 _lastRootSet;
        private bool _hasRootSet, _jumpQueued, _wasGrounded = true;
        private int _airJumpsUsed;
        private float _appliedStrength = -1f;
        private int _limbLayer;
        private Vector3 _holdSpot;
        private bool _holding;
        private const float HoldSpring = 30f;

        // Animated clone (owner)
        private Animator _clone;
        private readonly Transform[] _cloneBones = new Transform[BoneCount];
        private readonly Quaternion[] _holdArmRel = new Quaternion[BoneCount]; // idle arm pose relative to the chest
        private bool _hasHoldPose;
        private float _nextBumpTime;
        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int MoveRateParam = Animator.StringToHash("MoveRate");
        private static readonly int GroundedParam = Animator.StringToHash("Grounded");
        private static readonly int MoveXParam = Animator.StringToHash("MoveX");
        private static readonly int MoveZParam = Animator.StringToHash("MoveZ");

        // Climb (owner): Wallclimb moves the root, the bodies are posed kinematically
        private bool _climbing;
        private float _climbLift;
        private int _seenBoostCount;
        private readonly Vector3[] _fk = new Vector3[BoneCount];
        private const float OldPlayerGravity = 24f; // PlayerMovement._gravity
        private float _airborneUntil;

        // Carry (owner side)
        private CarryableObject _carry;
        private Vector3 _carryLocalL, _carryLocalR;
        private readonly ConfigurableJoint[] _carryHand = new ConfigurableJoint[2];
        private CarryableObject _ignoredAfterCarry;
        private bool _carryRequestPending;
        private int _liftSteps;
        // Carry (server side, for this player)
        private CarryableObject _serverCarry;

        // First-person hands (owner)
        private SkinnedMeshRenderer _fpHands;
        private Transform _fpCollapse;
        private Renderer[] _visualRenderers;
        private UnityEngine.Rendering.ShadowCastingMode[] _visualShadowModes;
        private bool _fpShown;
        private MaterialPropertyBlock _fpBlock;

        // Remote drawing
        private readonly Quaternion[] _remoteLocal = new Quaternion[BoneCount];

        private bool IsLocal => !IsSpawned || IsOwner;
        private bool HasAuthority => !IsSpawned || IsServer;
        private ulong CarrierId => IsSpawned ? OwnerClientId : 0UL;

        private void Awake()
        {
            _rig = GetComponent<CharacterRig>();
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputReader>();
            _camera = GetComponent<PlayerCameraController>();
            _sync = GetComponent<RagdollPoseSync>();
            _wallclimb = GetComponent<Wallclimb>();
            _ikTargets = GetComponent<ProceduralPlayerArms>();
            _stamina = GetComponent<PlayerStamina>();
            _expedition = GetComponent<CoopGame.Network.PlayerExpeditionState>();
            if (_fullRagdoll) SwitchOffAnimatedPlayer();
        }

        /// <summary>The old movement/carry and the option-A visual ragdoll step aside; Wallclimb stays for climbing.</summary>
        private void SwitchOffAnimatedPlayer()
        {
            void Off(Behaviour b) { if (b != null) b.enabled = false; }
            Off(GetComponent<PlayerMovement>());
            Off(GetComponent<PlayerCarry>());
            Off(_ikTargets); // its hand target transforms stay; Wallclimb moves them, the arm IK here follows them
            Off(GetComponent<ProceduralPlayerLegs>());
            Off(GetComponent<ActiveRagdoll>());
            Off(GetComponent<Unity.Netcode.Components.NetworkAnimator>());
            Off(GetComponent<PlayerAnimatorDriver>());
            if (_controller != null) _controller.enabled = false;
            foreach (var capsule in GetComponents<CapsuleCollider>()) capsule.enabled = false;
            var visual = transform.Find(CharacterRig.VisualName);
            if (visual != null)
            {
                Off(visual.GetComponent<BonelessCharacterPhysics>());
                var animator = visual.GetComponent<Animator>();
                if (animator != null) animator.enabled = false;
            }
            if (_ikTargets != null) _ikTargets.EnsureTargetNodesCreated();
        }

        private bool EnsureBones()
        {
            if (_bonesReady) return true;
            if (_rig == null || !_rig.IsHumanoid) return false;
            Animator animator = _rig.Animator;
            for (int i = 0; i < BoneCount; i++)
            {
                Transform bone = animator.GetBoneTransform(Bones[i]);
                if (bone == null && Bones[i] == HumanBodyBones.UpperChest) bone = animator.GetBoneTransform(HumanBodyBones.Chest);
                if (bone == null) return false;
                _bones[i] = bone;
            }
            _bones[ArmL] = _rig.UpperArmL; _bones[ArmL + 1] = _rig.LowerArmL; _bones[ArmL + 2] = _rig.HandL;
            _bones[ArmR] = _rig.UpperArmR; _bones[ArmR + 1] = _rig.LowerArmR; _bones[ArmR + 2] = _rig.HandR;
            _bonesReady = true;
            return true;
        }

        public override void OnNetworkDespawn()
        {
            ServerDropCarry();
            DestroyPhysics();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            DestroyPhysics();
            base.OnDestroy();
        }

        private bool Blocked =>
            CoopGame.Network.PauseMenu.IsPaused || MissionFailUI.IsVisible || CoopGame.Network.ExpeditionHUD.BlocksGameplayInput;

        private bool Exhausted => _stamina != null && _stamina.IsExhausted;
        private float Card(CoopGame.Network.CardEffect effect) => _expedition != null ? _expedition.CardMagnitude(effect) : 1f;

        // ───────────────────────── Input (owner) ─────────────────────────

        private void Update()
        {
            if (!_fullRagdoll || !IsLocal || _input == null) return;
            if (_input.JumpTriggered && !_climbing) _jumpQueued = true;
            bool canAct = !IsKnockedDown && !Blocked && !_climbing;

            if (_carry != null)
            {
                if (_input.ThrowTriggered && canAct) RequestThrow();
                else if (_input.GrabRightPressed && canAct) RequestPlace();
                if (_input.ScrollStep != 0)
                    _liftSteps = Mathf.Clamp(_liftSteps + _input.ScrollStep, -CarryableObject.HoldLowerSteps, CarryableObject.HoldRaiseSteps);

                // Holding drains stamina like the old carry; running out drops the crate.
                if (_stamina != null && _carry != null) // placing/throwing may just have ended the carry
                {
                    _stamina.DrainStaminaContinuous(false, _carry.TotalMass, _carry.CurrentCarrierCount, _carry.StaminaDrainMultiplier);
                    if (_stamina.IsExhausted) RequestDrop();
                }
            }
            else if (_input.GrabLeftPressed && canAct && !Exhausted && !_carryRequestPending &&
                     (_wallclimb == null || !_wallclimb.IsClimbing) && FindAimedCargo(out CarryableObject cargo))
            {
                RequestCarry(cargo);
            }
        }

        // ───────────────────────── Physics (owner) ─────────────────────────

        private void FixedUpdate()
        {
            if (!_fullRagdoll) return;
            if (HasAuthority) ServerWatchCarry();
            if (!IsLocal || !EnsureBones()) return;
            if (_physicsRoot == null) BuildPhysics();

            float dt = Time.fixedDeltaTime;
            Vector2 move = _input != null ? _input.MoveInput : Vector2.zero;
            if (Blocked) move = Vector2.zero;
            float yaw = _camera != null ? _camera.Yaw : transform.eulerAngles.y;
            float pitch = _camera != null ? _camera.Pitch : 0f;
            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
            RefreshCargoColliders();

            if (UpdateClimbMode(pitch, dt)) return;
            // Respawn/teleport helpers re-enable the CharacterController; outside a climb it must stay off.
            if (_controller != null && _controller.enabled) _controller.enabled = false;
            DetectTeleport();

            bool moving = move.sqrMagnitude > .01f;
            bool sprint = _input != null && _input.SprintHeld && moving && !Exhausted;
            if (sprint && _stamina != null) _stamina.ConsumeActionStamina(_sprintStaminaDrain * dt);
            Vector3 desired = facing * new Vector3(move.x, 0f, move.y);
            desired = Vector3.ClampMagnitude(desired, 1f) * (sprint ? _sprintSpeed : _walkSpeed);
            if (_carry != null)
                desired *= _carry.GetSpeedMultiplier() * Card(CoopGame.Network.CardEffect.FastCarrier) * (Exhausted ? .5f : 1f);

            Vector3 hipsVel = _bodies[Hips].linearVelocity;
            Vector3 flatVel = Flat(hipsVel);

            // Ground: sphere cast from the hips; the limb layer is left out of the mask.
            IsGrounded = Physics.SphereCast(_bodies[Hips].position + Vector3.up * .1f, .18f, Vector3.down, out RaycastHit ground,
                _standHeight + .45f, Physics.DefaultRaycastLayers & ~(1 << _limbLayer), QueryTriggerInteraction.Ignore)
                && Time.time >= _airborneUntil;
            if (IsGrounded) _airJumpsUsed = 0;
            DetectHardLanding(hipsVel.y);

            bool limp = IsKnockedDown;
            float recover = !limp && Time.time < _standUpUntil ? 1f - (_standUpUntil - Time.time) / _standUpSeconds : 1f;
            UpdateDrives(limp ? 0f : Mathf.SmoothStep(.15f, 1f, recover));

            if (_clone != null) UpdateClonePose(flatVel, facing, IsGrounded);
            else UpdateTargetPose(flatVel.magnitude, facing, dt);
            UpdateArms(pitch, facing, physics: true);

            if (!limp)
            {
                // Hold the body up and move it as one: the same acceleration on every limb.
                Vector3 accel = Vector3.zero;
                if (IsGrounded)
                {
                    float height = ground.distance + .18f - .1f; // cast radius, cast start above the hips
                    accel.y = -Physics.gravity.y + (_standHeight - height) * _standSpring - hipsVel.y * _standDamper;
                    accel.y = Mathf.Max(0f, accel.y) * recover;
                }
                float gain = IsGrounded ? _acceleration : _airAcceleration;
                // Standing still: hold the spot, so the slippery feet do not let the body creep.
                Vector3 hold = Vector3.zero;
                bool still = IsGrounded && desired == Vector3.zero && flatVel.sqrMagnitude < .25f;
                Vector3 hipsFlat = Flat(_bodies[Hips].position);
                if (!still || !_holding || (hipsFlat - _holdSpot).sqrMagnitude > .25f) { _holdSpot = hipsFlat; _holding = still; }
                else hold = (_holdSpot - hipsFlat) * HoldSpring;
                accel += Vector3.ClampMagnitude((desired - flatVel) * 6f + hold, gain) + CarryLeash(facing);
                for (int i = 0; i < BoneCount; i++) _bodies[i].AddForce(accel, ForceMode.Acceleration);

                if (_jumpQueued) TryJump();

                // Stay upright and turn to the camera heading (hips and chest follow the target pose).
                Upright(Hips, recover);
                Upright(Chest, recover * .6f);
            }
            _jumpQueued = false;

            // Tipped over (pushed, hit) -> go limp.
            if (!limp && Vector3.Angle(_bodies[Chest].transform.up, Vector3.up) > _knockdownTilt && recover >= 1f)
                Knockdown(Vector3.zero);

            for (int i = 1; i < BoneCount; i++)
            {
                Quaternion desiredRel = Quaternion.Inverse(_target[Parents[i]]) * _target[i];
                _joints[i].targetRotation = Quaternion.Inverse(desiredRel) * _restRelative[i];
            }

            UpdateCarry(move, facing, hipsVel);
        }

        private void TryJump()
        {
            int extraJumps = _expedition != null && _expedition.HasCard(CoopGame.Network.CardEffect.DoubleJump)
                ? CoopGame.Network.CardCatalog.DoubleJumpExtraJumps : 0;
            if (!IsGrounded)
            {
                if (_airJumpsUsed >= extraJumps) return;
                _airJumpsUsed++;
            }
            // HIGH JUMPER scales the jump height, so the take-off speed scales with its square root.
            float speed = _jumpSpeed * Mathf.Sqrt(Mathf.Max(.1f, Card(CoopGame.Network.CardEffect.HighJumper)));
            if (_carry != null) speed *= .7f;
            for (int i = 0; i < BoneCount; i++)
            {
                Vector3 v = _bodies[i].linearVelocity;
                _bodies[i].linearVelocity = new Vector3(v.x, Mathf.Max(v.y, 0f) + speed, v.z);
            }
        }

        private void Upright(int index, float strength)
        {
            // Desired orientation: the target pose, which is upright and faces the camera heading.
            Rigidbody body = _bodies[index];
            Quaternion delta = _target[index] * Quaternion.Inverse(body.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (!float.IsFinite(axis.x) || Mathf.Abs(angle) < .01f) axis = Vector3.zero;
            Vector3 torque = axis * (angle * Mathf.Deg2Rad * _uprightSpring) - body.angularVelocity * _uprightDamper;
            body.AddTorque(torque * strength * body.mass / 10f, ForceMode.Force);
        }

        // ───────────────────────── Target pose ─────────────────────────

        /// <summary>Procedural fallback when there is no clone: rest pose plus a simple walk cycle.</summary>
        private void UpdateTargetPose(float speed, Quaternion facing, float dt)
        {
            float targetWeight = IsGrounded ? Mathf.InverseLerp(.2f, 2f, speed) : 0f;
            _strideWeight = Mathf.MoveTowards(_strideWeight, targetWeight, dt * 5f);
            _phase = Mathf.Repeat(_phase + dt * _strideFrequency * Mathf.Clamp(speed / _walkSpeed, .5f, 1.6f) * Mathf.PI * 2f, Mathf.PI * 2f);
            float s = Mathf.Sin(_phase) * _strideWeight;
            Vector3 right = facing * Vector3.right;
            for (int i = 0; i < BoneCount; i++) _target[i] = facing * _restLocal[i];
            SwingChain(LegL, -s * _thighSwing, right);
            SwingChain(LegR, s * _thighSwing, right);
            BendJoint(LegL + 1, Mathf.Max(0f, s) * _kneeBend, right);
            BendJoint(LegR + 1, Mathf.Max(0f, -s) * _kneeBend, right);
            SwingChain(ArmL, s * _armSwing, right);
            SwingChain(ArmR, -s * _armSwing, right);
        }

        /// <summary>
        /// Target world rotations from the animated clone (EP2 "animated clone"): its pose, turned to the heading.
        /// The clip blend and playback rate follow the body's real speed so the steps match the ground covered.
        /// </summary>
        private void UpdateClonePose(Vector3 flatVel, Quaternion facing, bool grounded)
        {
            // The body keeps facing the camera; the 2D blend picks run/strafe/backwards from the local move direction.
            float speed = flatVel.magnitude;
            Vector3 local = Quaternion.Inverse(facing) * flatVel;
            float run = Mathf.InverseLerp(_walkBlendSpeed, _runBlendSpeed, speed);
            float clipSpeed = Mathf.Lerp(_walkClipSpeed, _runClipSpeed, run);
            float rate = speed > .3f ? Mathf.Clamp(speed / clipSpeed, .6f, _maxMoveRate) : 1f;
            _clone.SetFloat(SpeedParam, speed);
            _clone.SetFloat(MoveXParam, local.x);
            _clone.SetFloat(MoveZParam, local.z);
            _clone.SetFloat(MoveRateParam, rate);
            _clone.SetBool(GroundedParam, grounded);
            for (int i = 0; i < BoneCount; i++) _target[i] = facing * _cloneBones[i].rotation;
        }

        private void SwingChain(int root, float angle, Vector3 axis)
        {
            Quaternion r = Quaternion.AngleAxis(angle, axis);
            for (int i = root; i < root + 3; i++) _target[i] = r * _target[i];
        }

        private void BendJoint(int joint, float angle, Vector3 axis)
        {
            Quaternion r = Quaternion.AngleAxis(angle, axis);
            for (int i = joint; i < joint + 2; i++) _target[i] = r * _target[i];
        }

        // ───────────────────────── Arms ─────────────────────────
        // Arms that hold or reach something start from the still idle arm pose (so the run cycle's swing and twist
        // never leak into them) and are bent onto their target by two-bone IK. Free arms keep the animation.

        private void UpdateArms(float pitch, Quaternion facing, bool physics)
        {
            Vector3 right = facing * Vector3.right;
            for (int h = 0; h < 2; h++)
            {
                int upper = h == 0 ? ArmL : ArmR;
                float side = h == 0 ? -1f : 1f;
                Vector3 shoulder = physics ? _bodies[upper].position : _fk[upper];
                bool hasTarget = ArmTarget(h, shoulder, out Vector3 target, out Vector3 pole, facing);
                if (!hasTarget) continue;
                if (_hasHoldPose)
                    for (int i = upper; i < upper + 3; i++) _target[i] = _target[Chest] * _holdArmRel[i];
                if (pole == Vector3.zero) pole = -Vector3.up + right * side * .6f - facing * Vector3.forward * .3f;
                SolveArm(upper, shoulder, target, pole);
            }
            _target[Head] = Quaternion.AngleAxis(Mathf.Clamp(pitch, -40f, 40f) * .4f, right) * _target[Head];
        }

        /// <summary>Where a hand should be: on its crate grip, on its climbing hold, or reaching for one.</summary>
        private bool ArmTarget(int h, Vector3 shoulder, out Vector3 target, out Vector3 pole, Quaternion facing)
        {
            pole = Vector3.zero;
            target = default;
            if (_carry != null)
            {
                target = _carry.transform.TransformPoint(h == 0 ? _carryLocalL : _carryLocalR);
                return true;
            }
            if (IsKnockedDown || _wallclimb == null || _ikTargets == null) return false;
            bool gripping = h == 0 ? _wallclimb.LeftHandGripping : _wallclimb.RightHandGripping;
            bool reaching = _input != null && !Blocked && (h == 0 ? _input.GrabLeftHeld : _input.GrabRightHeld);
            if (!gripping && !reaching) return false;
            Transform hand = h == 0 ? _ikTargets.LeftHand : _ikTargets.RightHand;
            if (hand == null) return false;
            target = hand.position;
            // Climbing elbows point down and out, away from the wall.
            pole = -Vector3.up * 1f + (facing * Vector3.right) * (h == 0 ? -.7f : .7f) - facing * Vector3.forward * .4f;
            return true;
        }

        /// <summary>Two-bone IK on the target rotations: upper arm to the elbow, forearm to the hand (twist kept).</summary>
        private void SolveArm(int upper, Vector3 shoulder, Vector3 target, Vector3 pole)
        {
            int lower = upper + 1, hand = upper + 2;
            float a = _boneLength[upper], b = _boneLength[lower];
            Vector3 toTarget = target - shoulder;
            float d = Mathf.Clamp(toTarget.magnitude, .02f, (a + b) * .999f);
            Vector3 dir = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : Vector3.down;
            float cos = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            bend.Normalize();
            Vector3 elbow = shoulder + dir * (a * cos) + bend * (a * Mathf.Sqrt(1f - cos * cos));
            Vector3 wrist = shoulder + dir * d;

            Quaternion r1 = Quaternion.FromToRotation(_target[upper] * _boneAxis[upper], elbow - shoulder);
            _target[upper] = r1 * _target[upper];
            _target[lower] = r1 * _target[lower];
            _target[hand] = r1 * _target[hand];
            Quaternion r2 = Quaternion.FromToRotation(_target[lower] * _boneAxis[lower], wrist - elbow);
            _target[lower] = r2 * _target[lower];
            _target[hand] = r2 * _target[hand];
        }

        // ───────────────────────── Climb (Wallclimb) ─────────────────────────

        /// <summary>
        /// While Wallclimb holds a wall or pulls up, it moves the root through the CharacterController (the old
        /// climb, unchanged). The bodies turn kinematic and are posed from the root each step: the clone's air pose,
        /// hanging from the grips, arms reaching Wallclimb's hand targets. Returns true while climbing.
        /// </summary>
        private bool UpdateClimbMode(float pitch, float dt)
        {
            bool climbing = _wallclimb != null && _wallclimb.isActiveAndEnabled && _wallclimb.IsClimbing && !IsKnockedDown;
            if (climbing && !_climbing) EnterClimb();
            else if (!climbing && _climbing) ExitClimb(dt);
            if (!_climbing) return false;

            IsGrounded = false;
            Quaternion facing = Quaternion.Euler(0f, transform.eulerAngles.y, 0f); // Wallclimb turns the root to the wall
            if (_clone != null) UpdateClonePose(Vector3.zero, facing, false);
            else UpdateTargetPose(0f, facing, dt);

            // Hang the body from the grips: lift it as far as the arms need to reach their holds.
            Vector3 hips = transform.position - Vector3.up * _hipsAboveRoot;
            ForwardKinematics(hips + Vector3.up * _climbLift);
            float need = 0f;
            for (int h = 0; h < 2; h++)
            {
                bool gripping = h == 0 ? _wallclimb.LeftHandGripping : _wallclimb.RightHandGripping;
                Transform hand = _ikTargets != null ? (h == 0 ? _ikTargets.LeftHand : _ikTargets.RightHand) : null;
                if (!gripping || hand == null) continue;
                int upper = h == 0 ? ArmL : ArmR;
                Vector3 shoulder = _fk[upper] - Vector3.up * _climbLift;
                Vector3 to = hand.position - shoulder;
                float reach = (_boneLength[upper] + _boneLength[upper + 1]) * .95f;
                float flat = Flat(to).magnitude;
                need = Mathf.Max(need, to.y - Mathf.Sqrt(Mathf.Max(0f, reach * reach - flat * flat)));
            }
            _climbLift = Mathf.MoveTowards(_climbLift, Mathf.Clamp(need, 0f, 1.2f), dt * 4f);
            ForwardKinematics(hips + Vector3.up * _climbLift);
            UpdateArms(pitch, facing, physics: false);
            ForwardKinematics(hips + Vector3.up * _climbLift);

            bool jump = (_fk[Hips] - _bodies[Hips].position).sqrMagnitude > 1f; // respawn/teleport mid-climb
            for (int i = 0; i < BoneCount; i++)
            {
                if (jump) { _bodies[i].position = _fk[i]; _bodies[i].rotation = _target[i]; }
                else { _bodies[i].MovePosition(_fk[i]); _bodies[i].MoveRotation(_target[i]); }
            }
            _lastRootSet = transform.position;
            return true;
        }

        private void ForwardKinematics(Vector3 hips)
        {
            _fk[Hips] = hips;
            for (int i = 1; i < BoneCount; i++) _fk[i] = _fk[Parents[i]] + _target[Parents[i]] * _anchorInParent[i];
        }

        private void EnterClimb()
        {
            _climbing = true;
            _climbLift = 0f;
            _seenBoostCount = _wallclimb.BoostCount;
            if (_carry != null) RequestDrop();
            if (_controller != null && !_controller.enabled) _controller.enabled = true;
            foreach (var body in _bodies) body.isKinematic = true;
        }

        private void ExitClimb(float dt)
        {
            _climbing = false;
            Vector3 velocity = Vector3.ClampMagnitude(_controller != null ? _controller.velocity : Vector3.zero, 8f);
            if (_wallclimb != null && _wallclimb.BoostCount != _seenBoostCount)
            {
                // Wallclimb's boost is tuned for the old player's -24 gravity; scale the rise to the same height here.
                Vector3 boost = _wallclimb.LastBoostImpulse;
                velocity += new Vector3(boost.x, boost.y * Mathf.Sqrt(-Physics.gravity.y / OldPlayerGravity), boost.z);
                // Right after the boost the hips are level with the wall top; its edge must not count as ground
                // (the stand spring's damper would eat the rise).
                _airborneUntil = Time.time + .35f;
            }
            if (_controller != null) _controller.enabled = false;
            foreach (var body in _bodies)
            {
                body.isKinematic = false;
                body.linearVelocity = velocity;
                body.angularVelocity = Vector3.zero;
            }
            _holding = false;
            _airSpeed = 0f;
            _wasGrounded = false;
        }

        // ───────────────────────── Carry ─────────────────────────
        // The old hold-pose carry: the crate attaches this player as a carrier and steers itself in front of the
        // player root (CarryableObject.HoldPose), so it stays level and steady while the body walks as physics. The
        // server owns attach/place/throw (a client asks by RPC); the owner's hands spring onto the grips of the crate
        // as it sees it, and the body is drawn back if it outruns the crate. Two players may carry one crate.

        private readonly RaycastHit[] _aimHits = new RaycastHit[8];
        private readonly Collider[] _cargoNear = new Collider[16];

        /// <summary>Crate under the crosshair, or failing that the nearest one in front, within lifting reach.</summary>
        private bool FindAimedCargo(out CarryableObject cargo)
        {
            cargo = null;
            if (_physicsRoot == null) return false;
            Vector3 body = _bodies[Hips].position + Vector3.up * .3f;
            Camera view = _camera != null ? _camera.PlayerCamera : null;
            if (view != null)
            {
                Ray ray = new Ray(view.transform.position, view.transform.forward);
                int hits = Physics.SphereCastNonAlloc(ray, .15f, _aimHits, 8f, ~(1 << _limbLayer), QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                for (int i = 0; i < hits; i++)
                {
                    var c = _aimHits[i].collider.GetComponentInParent<CarryableObject>();
                    if (c == null || !c.CanBeCarried || _aimHits[i].distance >= nearest) continue;
                    if (Flat(_aimHits[i].collider.ClosestPoint(body) - body).magnitude > _carryPickupReach) continue;
                    nearest = _aimHits[i].distance;
                    cargo = c;
                }
                if (cargo != null) return true;
            }
            Vector3 forward = _camera != null ? _camera.HorizontalForward : transform.forward;
            int count = Physics.OverlapSphereNonAlloc(body + forward * .5f, _carryPickupReach * .7f, _cargoNear, ~(1 << _limbLayer), QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var c = _cargoNear[i].GetComponentInParent<CarryableObject>();
                if (c == null || !c.CanBeCarried) continue;
                Vector3 to = Flat(_cargoNear[i].ClosestPoint(body) - body);
                if (to.magnitude > _carryPickupReach || Vector3.Dot(to.normalized, forward) < .3f || to.sqrMagnitude >= best) continue;
                best = to.sqrMagnitude;
                cargo = c;
            }
            return cargo != null;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private void RequestCarry(CarryableObject cargo)
        {
            if (!ComputeGrip(cargo, out Vector3 left, out Vector3 right)) return;
            Vector3 localL = cargo.transform.InverseTransformPoint(left);
            Vector3 localR = cargo.transform.InverseTransformPoint(right);
            _carryRequestPending = true;
            if (HasAuthority) ServerStartCarry(cargo, localL, localR);
            else StartCarryRpc(cargo.NetworkObject, localL, localR);
        }

        private void RequestPlace()
        {
            if (HasAuthority) ServerPlace(_colliders);
            else PlaceCarryRpc();
        }

        private void RequestThrow()
        {
            float pitch = _camera != null ? _camera.Pitch : 0f;
            float yaw = _camera != null ? _camera.Yaw : transform.eulerAngles.y;
            Vector3 dir = Quaternion.Euler(Mathf.Clamp(pitch - 20f, -60f, 30f), yaw, 0f) * Vector3.forward;
            Vector3 velocity = dir * _throwSpeed + Flat(_bodies[Hips].linearVelocity) * .5f;
            if (_stamina != null) _stamina.ConsumeStamina(_throwStaminaCost);
            if (HasAuthority) ServerThrow(velocity);
            else ThrowCarryRpc(velocity);
        }

        private void RequestDrop()
        {
            if (_carry == null) return;
            if (HasAuthority) ServerDropCarry();
            else DropCarryRpc();
        }

        /// <summary>Owner: the server attached this player to the crate (or refused).</summary>
        private void OnCarryStarted(CarryableObject cargo, Vector3 localL, Vector3 localR, bool ok)
        {
            _carryRequestPending = false;
            if (!ok || cargo == null || _physicsRoot == null) return;
            _carry = cargo;
            _carryLocalL = localL;
            _carryLocalR = localR;
            _liftSteps = 0;
            if (_ignoredAfterCarry != cargo) IgnoreCargo(_ignoredAfterCarry, false);
            _ignoredAfterCarry = null;
            IgnoreCargo(cargo, true);
            var cargoBody = cargo.GetComponent<Rigidbody>();
            AttachCarryHand(0, cargoBody, cargo.transform.TransformPoint(localL));
            AttachCarryHand(1, cargoBody, cargo.transform.TransformPoint(localR));
        }

        /// <summary>Owner: the server let go of the crate for this player (placed, thrown, dropped, delivered).</summary>
        private void OnCarryEnded()
        {
            for (int h = 0; h < 2; h++)
            {
                if (_carryHand[h] != null) Destroy(_carryHand[h]);
                _carryHand[h] = null;
            }
            // Collisions come back once the body has stepped clear, so a set-down crate is not shoved.
            if (_carry != null) _ignoredAfterCarry = _carry;
            _carry = null;
        }

        private void UpdateCarry(Vector2 move, Quaternion facing, Vector3 hipsVel)
        {
            RestoreCargoCollisionWhenClear();
            if (_carry == null) return;
            Vector3 heading = facing * Vector3.forward;
            Vector3 direction = Vector3.ClampMagnitude(facing * new Vector3(move.x, 0f, move.y), 1f);
            float height = CarryableObject.NeutralHoldHeight + _liftSteps * CarryableObject.HoldStepHeight;
            if (HasAuthority) ServerCarryInput(direction, heading, height, transform.position, Flat(hipsVel));
            else CarryInputRpc(direction, heading, height, transform.position, Flat(hipsVel));
        }

        /// <summary>Pulls the body back toward the crate's grips when it gets ahead of (or away from) them.</summary>
        private Vector3 CarryLeash(Quaternion facing)
        {
            if (_carry == null) return Vector3.zero;
            Vector3 grips = (_carry.transform.TransformPoint(_carryLocalL) + _carry.transform.TransformPoint(_carryLocalR)) * .5f;
            Vector3 anchor = transform.position + facing * Vector3.forward * CarryableObject.HoldReach;
            Vector3 error = Flat(grips - anchor);
            float slack = error.magnitude - .12f;
            return slack > 0f ? Vector3.ClampMagnitude(error.normalized * slack * _carryLeash * 4f, _carryLeash) : Vector3.zero;
        }

        private void AttachCarryHand(int h, Rigidbody cargoBody, Vector3 grip)
        {
            Rigidbody hand = _bodies[h == 0 ? HandL : HandR];
            var joint = hand.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = cargoBody;
            joint.anchor = hand.transform.InverseTransformPoint(hand.position + (hand.rotation * Vector3.up) * .06f);
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedAnchor = cargoBody.transform.InverseTransformPoint(grip);
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
            // Soft and force-capped: the hands stay on the crate without the crate and body yanking each other.
            var drive = new JointDrive { positionSpring = 1500f, positionDamper = 60f, maximumForce = _carryHandForce };
            joint.xDrive = joint.yDrive = joint.zDrive = drive;
            joint.enableCollision = false;
            _carryHand[h] = joint;
        }

        private void IgnoreCargo(CarryableObject cargo, bool ignore)
        {
            if (cargo == null) return;
            foreach (var c in cargo.GetComponentsInChildren<Collider>())
                foreach (var own in _colliders)
                    if (own != null && c != null) Physics.IgnoreCollision(own, c, ignore);
        }

        private void RestoreCargoCollisionWhenClear()
        {
            if (_ignoredAfterCarry == null) return;
            foreach (var cargoCollider in _ignoredAfterCarry.GetComponentsInChildren<Collider>())
            {
                Bounds cargoBounds = cargoCollider.bounds;
                cargoBounds.Expand(.05f);
                foreach (var own in _colliders)
                    if (own != null && own.bounds.Intersects(cargoBounds)) return;
            }
            IgnoreCargo(_ignoredAfterCarry, false);
            _ignoredAfterCarry = null;
        }

        /// <summary>
        /// Two-hand grip on the face nearest the player, centred, low on the face (like PlayerCarry): narrow cargo is
        /// hugged by its side faces, wider cargo is held on the near face with the hands spread.
        /// </summary>
        private bool ComputeGrip(CarryableObject cargo, out Vector3 left, out Vector3 right)
        {
            left = right = Vector3.zero;
            Collider[] colliders = cargo.GetComponentsInChildren<Collider>();
            Bounds bounds = default;
            bool any = false;
            foreach (var c in colliders)
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                if (!any) { bounds = c.bounds; any = true; } else bounds.Encapsulate(c.bounds);
            }
            if (!any) return false;
            Vector3 center = bounds.center;
            Vector3 toPlayer = Flat(_bodies[Hips].position - center);
            if (toPlayer.sqrMagnitude < 1e-4f) toPlayer = -transform.forward;
            Vector3 axisA = Flat(cargo.transform.right).normalized, axisB = Flat(cargo.transform.forward).normalized;
            if (axisA.sqrMagnitude < .5f) axisA = Vector3.right;
            if (axisB.sqrMagnitude < .5f) axisB = Vector3.forward;
            float dotA = Vector3.Dot(axisA, toPlayer), dotB = Vector3.Dot(axisB, toPlayer);
            Vector3 approach = Mathf.Abs(dotA) >= Mathf.Abs(dotB) ? axisA * Mathf.Sign(dotA) : axisB * Mathf.Sign(dotB);
            Vector3 side = Vector3.Cross(Vector3.up, -approach).normalized; // player's right when facing the crate
            float height = Mathf.Min(center.y, Mathf.Lerp(bounds.min.y, bounds.max.y, .3f));
            Vector3 grip = new Vector3(center.x, height, center.z);

            float far = bounds.extents.magnitude + 1f;
            Vector3 SurfaceFrom(Vector3 direction, Vector3 origin) => ClosestOn(colliders, origin + direction * far);
            Vector3 leftSide = SurfaceFrom(-side, grip), rightSide = SurfaceFrom(side, grip);
            float halfWidth = Vector3.Dot(rightSide - leftSide, side) * .5f;
            Vector3 front = SurfaceFrom(approach, grip);
            if (halfWidth <= .36f)
            {
                Vector3 inset = Vector3.Project(front - grip, approach) - approach * .07f;
                left = SurfaceFrom(-side, grip + inset);
                right = SurfaceFrom(side, grip + inset);
            }
            else
            {
                float spread = Mathf.Min(_carryHandSpread, halfWidth - .05f);
                left = SurfaceFrom(approach, grip - side * spread);
                right = SurfaceFrom(approach, grip + side * spread);
            }
            return true;
        }

        private static Vector3 ClosestOn(Collider[] colliders, Vector3 point)
        {
            Vector3 best = point;
            float bestSqr = float.MaxValue;
            foreach (var c in colliders)
            {
                if (c == null || !c.enabled || c.isTrigger || (c is MeshCollider m && !m.convex)) continue;
                Vector3 p = c.ClosestPoint(point);
                float sqr = (p - point).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = p; }
            }
            return best;
        }

        // ───────────────────────── Carry: server side ─────────────────────────

        private void ServerStartCarry(CarryableObject cargo, Vector3 localL, Vector3 localR)
        {
            bool ok = cargo != null && _serverCarry == null &&
                      (cargo.transform.position - transform.position).sqrMagnitude < 4f * 4f && // plausible reach
                      cargo.TryAttachCarrier(CarrierId, transform, null, localL, localR, out _);
            if (ok) _serverCarry = cargo;
            if (IsLocal) OnCarryStarted(cargo, localL, localR, ok);
            else CarryStartedRpc(cargo != null ? cargo.NetworkObject : default(NetworkObjectReference), localL, localR, ok);
        }

        private void ServerCarryInput(Vector3 direction, Vector3 heading, float height, Vector3 position, Vector3 velocity)
        {
            if (_serverCarry == null) return;
            _serverCarry.UpdateCarrierInput(CarrierId, Vector3.ClampMagnitude(direction, 1f), Vector3.ClampMagnitude(heading, 1f),
                Mathf.Clamp(height, .3f, 2.5f), true, true, position, velocity, 0f);
        }

        private void ServerPlace(Collider[] carrierColliders)
        {
            if (_serverCarry != null) _serverCarry.TryPlaceGently(CarrierId, carrierColliders);
            ServerWatchCarry();
        }

        private void ServerThrow(Vector3 velocity)
        {
            if (_serverCarry == null) return;
            _serverCarry.ThrowObject(CarrierId, Vector3.ClampMagnitude(velocity, CarryableObject.BaseThrowLimit), Random.insideUnitSphere * 2f);
            ServerWatchCarry();
        }

        private void ServerDropCarry()
        {
            if (_serverCarry != null && _serverCarry.HasCarrier(CarrierId)) _serverCarry.DetachCarrier(CarrierId);
            ServerWatchCarry();
        }

        /// <summary>Server: tells the owner as soon as this player no longer carries (however that happened).</summary>
        private void ServerWatchCarry()
        {
            if (_serverCarry != null && _serverCarry.HasCarrier(CarrierId) && !_serverCarry.IsSecured) return;
            bool hadCarry = _serverCarry != null;
            _serverCarry = null;
            if (IsLocal) { if (_carry != null) OnCarryEnded(); }
            else if (hadCarry) CarryEndedRpc();
        }

        [Rpc(SendTo.Server)]
        private void StartCarryRpc(NetworkObjectReference cargoRef, Vector3 localL, Vector3 localR, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId) return;
            ServerStartCarry(cargoRef.TryGet(out NetworkObject o) ? o.GetComponent<CarryableObject>() : null, localL, localR);
        }

        [Rpc(SendTo.Owner)]
        private void CarryStartedRpc(NetworkObjectReference cargoRef, Vector3 localL, Vector3 localR, bool ok)
        {
            OnCarryStarted(cargoRef.TryGet(out NetworkObject o) ? o.GetComponent<CarryableObject>() : null, localL, localR, ok);
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        private void CarryInputRpc(Vector3 direction, Vector3 heading, float height, Vector3 position, Vector3 velocity, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId == OwnerClientId) ServerCarryInput(direction, heading, height, position, velocity);
        }

        [Rpc(SendTo.Server)]
        private void PlaceCarryRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId == OwnerClientId) ServerPlace(null);
        }

        [Rpc(SendTo.Server)]
        private void ThrowCarryRpc(Vector3 velocity, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId == OwnerClientId) ServerThrow(velocity);
        }

        [Rpc(SendTo.Server)]
        private void DropCarryRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId == OwnerClientId) ServerDropCarry();
        }

        [Rpc(SendTo.Owner)]
        private void CarryEndedRpc() => OnCarryEnded();

        // ───────────────────────── Drawing ─────────────────────────

        private void LateUpdate()
        {
            if (!_fullRagdoll || !EnsureBones()) return;
            if (IsLocal)
            {
                if (_physicsRoot == null) return;
                if (!_climbing)
                {
                    DetectTeleport(); // a teleport made after this frame's physics step
                    FollowHipsWithRoot();
                }
                _bones[Hips].position = _bodies[Hips].transform.position;
                for (int i = 0; i < BoneCount; i++) _bones[i].rotation = _bodies[i].transform.rotation;
                if (_sync != null) _sync.PublishDrawnPose(_bones, _rig.Visual);
                UpdateFirstPersonHands();
            }
            else if (_sync != null && _sync.TrySampleRemotePose(_remoteLocal, out Vector3 hipsLocal, out Quaternion hipsRot))
            {
                Transform visual = _rig.Visual;
                _bones[Hips].position = visual.TransformPoint(hipsLocal);
                _bones[Hips].rotation = visual.rotation * hipsRot;
                for (int i = 1; i < BoneCount; i++) _bones[i].localRotation = _remoteLocal[i];
            }
        }

        // ───────────────────────── First-person hands ─────────────────────────
        // In first person the owner's body is shadow-only and a second copy of the body mesh is drawn in which every
        // bone except the forearms, hands and fingers is collapsed onto the camera position. Geometry collapsed onto
        // the eye projects to nothing, so only the forearms and hands show, following the physics arms. Only the
        // owner's machine does this; everyone else sees the full body.

        private void UpdateFirstPersonHands()
        {
            bool fp = _firstPersonHands && _camera != null && _camera.isActiveAndEnabled && _camera.BodyHidden;
            if (fp && _fpHands == null && !BuildFirstPersonHands()) fp = false;
            if (fp == _fpShown && !fp) return;
            _fpShown = fp;
            for (int i = 0; i < _visualRenderers.Length; i++)
                if (_visualRenderers[i] != null)
                    _visualRenderers[i].shadowCastingMode = fp ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : _visualShadowModes[i];
            if (_fpHands == null) return;
            _fpHands.enabled = fp;
            if (!fp) return;
            // Same colour as the body (PlayerAppearance tints it through a property block).
            _fpBlock ??= new MaterialPropertyBlock();
            foreach (var smr in _rig.Visual.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (smr != _fpHands && smr.sharedMesh == _fpHands.sharedMesh) { smr.GetPropertyBlock(_fpBlock); _fpHands.SetPropertyBlock(_fpBlock); break; }
        }

        private bool BuildFirstPersonHands()
        {
            if (_camera == null || _camera.PlayerCamera == null || _rig.Visual == null) return false;
            SkinnedMeshRenderer body = null;
            foreach (var smr in _rig.Visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null && (body == null || smr.sharedMesh.vertexCount > body.sharedMesh.vertexCount)) body = smr;
            if (body == null) return false;

            _fpCollapse = new GameObject("FirstPersonCollapse").transform;
            _fpCollapse.SetParent(_camera.PlayerCamera.transform, false);
            _fpCollapse.localScale = Vector3.one * 1e-4f;

            Transform lowerL = _bones[ArmL + 1], lowerR = _bones[ArmR + 1];
            Transform[] bones = body.bones;
            var kept = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                Transform b = bones[i];
                bool forearmOrHand = b != null && (b == lowerL || b == lowerR || b.IsChildOf(lowerL) || b.IsChildOf(lowerR));
                kept[i] = forearmOrHand ? b : _fpCollapse;
            }

            var go = new GameObject("FirstPersonHands") { layer = body.gameObject.layer };
            go.transform.SetParent(body.transform.parent, false);
            go.transform.SetLocalPositionAndRotation(body.transform.localPosition, body.transform.localRotation);
            go.transform.localScale = body.transform.localScale;
            _fpHands = go.AddComponent<SkinnedMeshRenderer>();
            _fpHands.sharedMesh = body.sharedMesh;
            _fpHands.sharedMaterials = body.sharedMaterials;
            _fpHands.rootBone = body.rootBone;
            _fpHands.bones = kept;
            _fpHands.updateWhenOffscreen = true;
            _fpHands.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _fpHands.enabled = false;

            var renderers = new List<Renderer>();
            foreach (var r in _rig.Visual.GetComponentsInChildren<Renderer>(true)) if (r != _fpHands) renderers.Add(r);
            _visualRenderers = renderers.ToArray();
            _visualShadowModes = new UnityEngine.Rendering.ShadowCastingMode[_visualRenderers.Length];
            // The camera may already have made the body shadow-only this frame; "shown" means casting shadows.
            for (int i = 0; i < _visualRenderers.Length; i++)
                _visualShadowModes[i] = _visualRenderers[i].shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                    ? UnityEngine.Rendering.ShadowCastingMode.On : _visualRenderers[i].shadowCastingMode;
            return true;
        }

        /// <summary>The player root (camera, UI, NetworkTransform, checkpoints) stands under the hips.</summary>
        private void FollowHipsWithRoot()
        {
            Vector3 hips = _bodies[Hips].transform.position;
            float yaw = _camera != null ? _camera.Yaw : transform.eulerAngles.y;
            Vector3 root = new Vector3(hips.x, hips.y + _hipsAboveRoot, hips.z);
            transform.SetPositionAndRotation(root, Quaternion.Euler(0f, yaw, 0f));
            _lastRootSet = root;
            _hasRootSet = true;
        }

        /// <summary>Someone else moved the root (respawn, checkpoint, probe teleport): bring the body along.</summary>
        private void DetectTeleport()
        {
            if (!_hasRootSet) return;
            Vector3 delta = transform.position - _lastRootSet;
            if (delta.sqrMagnitude < .25f) return;
            foreach (var body in _bodies)
            {
                body.position += delta;
                body.transform.position += delta;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            RequestDrop();
            _lastRootSet = transform.position;
            _holding = false;
            _airSpeed = 0f;
            _wasGrounded = true;
        }

        // ───────────────────────── Build ─────────────────────────

        private void BuildPhysics()
        {
            _physicsRoot = new GameObject($"RagdollB_{name}") { hideFlags = HideFlags.DontSave };
            // Lives as long as the player, across level loads (built mid-load it would otherwise belong to no scene).
            DontDestroyOnLoad(_physicsRoot);
            int layer = LayerMask.NameToLayer(LimbLayerName);
            if (layer < 0) layer = LayerMask.NameToLayer("Ignore Raycast");
            _limbLayer = layer;
            var slippery = new PhysicsMaterial("RagdollSlippery") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
            var hands = new PhysicsMaterial("RagdollHands") { dynamicFriction = .15f, staticFriction = .2f, frictionCombine = PhysicsMaterialCombine.Minimum };

            Quaternion facing = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            for (int i = 0; i < BoneCount; i++)
            {
                var go = new GameObject(Bones[i].ToString()) { layer = layer, tag = "Player" };
                go.transform.SetParent(_physicsRoot.transform, false);
                go.transform.SetPositionAndRotation(_bones[i].position, _bones[i].rotation);
                var body = go.AddComponent<Rigidbody>();
                body.mass = Masses[i];
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.solverIterations = 14;
                body.solverVelocityIterations = 6;
                body.maxAngularVelocity = 25f;
                body.linearDamping = .1f;
                body.angularDamping = 1f;
                _bodies[i] = body;
                _restLocal[i] = Quaternion.Inverse(facing) * _bones[i].rotation;
                _colliders[i] = BuildCollider(go, i);
                bool hand = i == HandL || i == HandR;
                _colliders[i].sharedMaterial = hand ? hands : slippery;
                _colliders[i].hasModifiableContacts = true;
                (hand ? s_handColliders : s_bodyColliders).Add(_colliders[i].GetEntityId());
                if (!hand) go.AddComponent<RagdollLimbContact>().Owner = this;
            }
            RegisterContactModification();

            for (int i = 1; i < BoneCount; i++)
            {
                Rigidbody parent = _bodies[Parents[i]];
                var joint = _bodies[i].gameObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = parent;
                joint.axis = Vector3.right;
                joint.secondaryAxis = Vector3.up;
                joint.anchor = Vector3.zero;
                joint.autoConfigureConnectedAnchor = false;
                joint.connectedAnchor = parent.transform.InverseTransformPoint(_bodies[i].position);
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
                joint.rotationDriveMode = RotationDriveMode.Slerp;
                joint.enableCollision = false;
                joint.projectionMode = JointProjectionMode.PositionAndRotation;
                joint.projectionDistance = .05f;
                _joints[i] = joint;
                _restRelative[i] = Quaternion.Inverse(parent.rotation) * _bodies[i].rotation;
                _anchorInParent[i] = joint.connectedAnchor;
            }
            // Arm segment axes and lengths for the IK (upper arm -> forearm -> hand).
            foreach (int upper in new[] { ArmL, ArmR })
                for (int i = upper; i < upper + 2; i++)
                {
                    Vector3 toNext = _bodies[i + 1].position - _bodies[i].position;
                    _boneLength[i] = Mathf.Max(.05f, toNext.magnitude);
                    _boneAxis[i] = Quaternion.Inverse(_bodies[i].rotation) * toNext.normalized;
                }

            var ownColliders = GetComponentsInChildren<Collider>(true);
            for (int a = 0; a < BoneCount; a++)
            {
                for (int b = a + 1; b < BoneCount; b++) Physics.IgnoreCollision(_colliders[a], _colliders[b]);
                foreach (var own in ownColliders) Physics.IgnoreCollision(_colliders[a], own); // root CharacterController, capsule
            }

            // Standing height: hips above the lowest foot point.
            float footY = Mathf.Min(_bones[LegL + 2].position.y, _bones[LegR + 2].position.y) - .08f;
            _standHeight = _bones[Hips].position.y - footY;
            _hipsAboveRoot = transform.position.y - _bones[Hips].position.y;
            _lastRootSet = transform.position;
            _hasRootSet = true;
            _appliedStrength = -1f;
            UpdateDrives(1f);
            BuildClone();
        }

        /// <summary>The clone's controller with the Inspector's idle/walk/run choices swapped in.</summary>
        private RuntimeAnimatorController WithChosenClips(RuntimeAnimatorController controller)
        {
            if (_idleClip == null && _walkClip == null && _runClip == null) return controller;
            var overrides = new AnimatorOverrideController(controller);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrides.GetOverrides(pairs);
            for (int i = 0; i < pairs.Count; i++)
            {
                // Only the forward idle/walk/run slots; strafe and backwards clips stay as they are.
                string n = pairs[i].Key.name;
                AnimationClip chosen = n.StartsWith("Idle") ? _idleClip
                    : n.StartsWith("Walking") && !n.Contains("Backwards") ? _walkClip
                    : n.StartsWith("Running") && !n.Contains("Strafe") ? _runClip : null;
                AnimationClip original = pairs[i].Key;
                if (chosen != null) pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, chosen);
            }
            overrides.ApplyOverrides(pairs);
            return overrides;
        }

        /// <summary>Hidden copy of the character with its own Animator; only its bone rotations are read.</summary>
        private void BuildClone()
        {
            if (_cloneModel == null || _cloneController == null) return;
            GameObject go = Instantiate(_cloneModel, _physicsRoot.transform, false);
            go.name = "AnimatedClone";
            Transform visual = _rig.Visual;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Inverse(transform.rotation) * visual.rotation;
            go.transform.localScale = visual.lossyScale;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var c in go.GetComponentsInChildren<Camera>(true)) Destroy(c);
            foreach (var l in go.GetComponentsInChildren<Light>(true)) Destroy(l);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);

            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();
            animator.avatar = _rig.Animator.avatar;
            animator.runtimeAnimatorController = WithChosenClips(_cloneController);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Fixed;

            // Same skeleton as the player, so bones are matched by name (the rig may remap the arms).
            var byName = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) byName.TryAdd(t.name, t);
            for (int i = 0; i < BoneCount; i++)
            {
                if (!byName.TryGetValue(_bones[i].name, out _cloneBones[i]))
                {
                    Debug.LogWarning($"[RagdollCharacter] Clone has no bone '{_bones[i].name}'; using the procedural pose.");
                    Destroy(go);
                    return;
                }
            }
            _clone = animator;

            // The still idle pose of the arms, relative to the chest: holding and reaching start from it.
            animator.Update(0f);
            Quaternion chest = Quaternion.Inverse(_cloneBones[Chest].rotation);
            for (int i = ArmL; i <= HandR; i++) _holdArmRel[i] = chest * _cloneBones[i].rotation;
            _hasHoldPose = true;
        }

        private Collider BuildCollider(GameObject go, int index)
        {
            Transform bone = _bones[index];
            Vector3 toEnd = SegmentEnd(index) - bone.position;
            Vector3 local = go.transform.InverseTransformVector(toEnd);
            float length = local.magnitude;
            HumanBodyBones kind = Bones[index];
            if (kind == HumanBodyBones.Head)
            {
                var sphere = go.AddComponent<SphereCollider>();
                sphere.radius = Mathf.Max(.12f, length * .5f);
                sphere.center = local.normalized * sphere.radius;
                return sphere;
            }
            if (index == Hips || index == 1 || index == Chest)
            {
                var box = go.AddComponent<BoxCollider>();
                float width = Vector3.Distance(_rig.UpperArmL.position, _rig.UpperArmR.position) * .75f;
                box.size = new Vector3(width, Mathf.Max(.12f, length), width * .65f);
                box.center = local * .5f;
                return box;
            }
            var capsule = go.AddComponent<CapsuleCollider>();
            Vector3 abs = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
            capsule.direction = abs.x > abs.y && abs.x > abs.z ? 0 : abs.y > abs.z ? 1 : 2;
            bool hand = index == HandL || index == HandR;
            bool foot = index == LegL + 2 || index == LegR + 2;
            bool leg = index == LegL || index == LegL + 1 || index == LegR || index == LegR + 1;
            float r = hand ? .06f : foot ? .07f : leg ? .09f : .065f;
            capsule.radius = r;
            capsule.height = Mathf.Max(length, r * 2f + .01f);
            capsule.center = local * .5f;
            return capsule;
        }

        private Vector3 SegmentEnd(int index)
        {
            for (int i = index + 1; i < BoneCount; i++)
                if (Parents[i] == index && i != ArmL && i != ArmR && i != LegL && i != LegR) return _bones[i].position;
            if (index == Hips) return _bones[1].position;
            Transform best = null;
            float bestSqr = 0f;
            foreach (Transform child in _bones[index])
            {
                float sqr = (child.position - _bones[index].position).sqrMagnitude;
                if (sqr > bestSqr) { bestSqr = sqr; best = child; }
            }
            return best != null ? best.position : _bones[index].position + _bones[index].up * .1f;
        }

        private void UpdateDrives(float strength)
        {
            strength = Mathf.Max(strength, .04f);
            if (Mathf.Abs(strength - _appliedStrength) < .02f) return;
            _appliedStrength = strength;
            for (int i = 1; i < BoneCount; i++)
            {
                float m = SubtreeMass(i) * SpringScale[i];
                _joints[i].slerpDrive = new JointDrive
                {
                    positionSpring = _jointSpring * m * strength,
                    positionDamper = _jointDamper * m * Mathf.Max(strength, .3f),
                    maximumForce = float.MaxValue,
                };
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

        // ───────────────────────── Knockdown ─────────────────────────

        private void DetectHardLanding(float verticalSpeed)
        {
            if (!IsGrounded) _airSpeed = Mathf.Min(_airSpeed, verticalSpeed);
            else if (!_wasGrounded && -_airSpeed >= _knockdownFallSpeed) Knockdown(Vector3.zero);
            if (IsGrounded) _airSpeed = 0f;
            _wasGrounded = IsGrounded;
        }

        /// <summary>Owner: go limp, then stand back up. Hazards may call this with an impulse.</summary>
        public void Knockdown(Vector3 impulse)
        {
            if (!IsLocal || _physicsRoot == null || IsKnockedDown || _climbing) return;
            _knockdownUntil = Time.time + _knockdownSeconds;
            _standUpUntil = _knockdownUntil + _standUpSeconds;
            RequestDrop();
            if (impulse != Vector3.zero)
            {
                _bodies[Hips].AddForce(impulse * .5f, ForceMode.Impulse);
                _bodies[Chest].AddForce(impulse * .5f, ForceMode.Impulse);
            }
        }

        private void DestroyPhysics()
        {
            OnCarryEnded();
            if (_fpHands != null) Destroy(_fpHands.gameObject);
            if (_fpCollapse != null) Destroy(_fpCollapse.gameObject);
            _fpHands = null;
            foreach (var c in _colliders)
            {
                if (c == null) continue;
                s_bodyColliders.Remove(c.GetEntityId());
                s_handColliders.Remove(c.GetEntityId());
            }
            if (_physicsRoot != null) Destroy(_physicsRoot);
            _physicsRoot = null;
            _clone = null;
            _climbing = false;
        }

        // ───────────────────────── Crate contact ─────────────────────────
        // The torso and legs see cargo as immovable (contact mass scaling), so walking into a crate cannot launch
        // it; a bump gives it a small capped nudge instead. Free hands touch cargo with a capped impulse.

        public const string LimbLayerName = "PlayerRagdoll";
        private static readonly HashSet<EntityId> s_bodyColliders = new HashSet<EntityId>();
        private static readonly HashSet<EntityId> s_handColliders = new HashSet<EntityId>();
        private static readonly HashSet<EntityId> s_cargoColliders = new HashSet<EntityId>();
        private static float s_handMaxImpulse = .4f;
        private static float s_nextCargoRefresh;
        private static bool s_contactHooked;

        private void RegisterContactModification()
        {
            s_handMaxImpulse = _handMaxImpulse;
            s_nextCargoRefresh = 0f;
            if (s_contactHooked) return;
            s_contactHooked = true;
            Physics.ContactModifyEvent += ModifyContacts;
            Physics.ContactModifyEventCCD += ModifyContacts;
        }

        /// <summary>Main thread, before the physics step: the sets are only read while the step runs.</summary>
        private static void RefreshCargoColliders()
        {
            if (Time.time < s_nextCargoRefresh) return;
            s_nextCargoRefresh = Time.time + .5f;
            s_cargoColliders.Clear();
            foreach (var cargo in FindObjectsByType<CarryableObject>())
                foreach (var c in cargo.GetComponentsInChildren<Collider>())
                    s_cargoColliders.Add(c.GetEntityId());
        }

        private static void ModifyContacts(PhysicsScene scene, Unity.Collections.NativeArray<ModifiableContactPair> pairs)
        {
            for (int i = 0; i < pairs.Length; i++)
            {
                ModifiableContactPair pair = pairs[i];
                EntityId a = pair.colliderEntityId, b = pair.otherColliderEntityId;
                bool aCargo = s_cargoColliders.Contains(a), bCargo = s_cargoColliders.Contains(b);
                if (!aCargo && !bCargo) continue;
                if ((s_bodyColliders.Contains(a) && bCargo) || (s_bodyColliders.Contains(b) && aCargo))
                {
                    ModifiableMassProperties mass = pair.massProperties;
                    if (bCargo) { mass.otherInverseMassScale = 0f; mass.otherInverseInertiaScale = 0f; }
                    else { mass.inverseMassScale = 0f; mass.inverseInertiaScale = 0f; }
                    pair.massProperties = mass;
                    pairs[i] = pair;
                }
                else if ((s_handColliders.Contains(a) && bCargo) || (s_handColliders.Contains(b) && aCargo))
                {
                    for (int j = 0; j < pair.contactCount; j++) pair.SetMaxImpulse(j, s_handMaxImpulse);
                    pairs[i] = pair;
                }
            }
        }

        /// <summary>A torso or leg touched something: if it is loose cargo, nudge it a little (host/offline only).</summary>
        internal void OnLimbBump(Rigidbody limb, Collision collision)
        {
            Rigidbody body = collision.rigidbody;
            if (body == null || body.isKinematic || Time.time < _nextBumpTime || !HasAuthority) return;
            var cargo = body.GetComponentInParent<CarryableObject>();
            if (cargo == null || cargo == _carry) return;
            Vector3 dir = body.worldCenterOfMass - limb.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            _nextBumpTime = Time.time + .3f;
            Vector3 flat = Flat(body.linearVelocity);
            Vector3 nudge = dir.normalized * _bumpNudgeSpeed;
            if (flat.magnitude < _bumpNudgeSpeed) body.AddForce(nudge - Vector3.Project(flat, dir), ForceMode.VelocityChange);
            body.AddTorque(Vector3.up * Random.Range(-.4f, .4f), ForceMode.VelocityChange);
        }

        // ───────────────────────── Diagnostics ─────────────────────────

        public Vector3 HipsPosition => _physicsRoot != null ? _bodies[Hips].position : (_bonesReady ? _bones[Hips].position : transform.position);
        public Vector3 HeadPosition => _physicsRoot != null ? _bodies[Head].position : (_bonesReady ? _bones[Head].position : transform.position);
        public string GripState =>
            $"carry:{(_carry != null ? _carry.name : "-")} climb:{(_climbing ? "yes" : "no")} " +
            $"L:{(_wallclimb != null && _wallclimb.LeftHandGripping ? "wall" : "-")} R:{(_wallclimb != null && _wallclimb.RightHandGripping ? "wall" : "-")}";
    }
}
