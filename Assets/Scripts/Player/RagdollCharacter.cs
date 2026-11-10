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
    public sealed partial class RagdollCharacter : NetworkBehaviour
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

        [Header("Carry (F tap lift / place, hold F catch, hold Q aim + charge, release throw)")]
        [Tooltip("How far from the body centre a crate may be to lift it")]
        [SerializeField] private float _carryPickupReach = 1.1f;
        [Tooltip("Half the distance between the hands on a wide crate's near face")]
        [SerializeField] private float _carryHandSpread = .2f;
        [Tooltip("Largest force (N) each hand spring uses to stay on the crate")]
        [SerializeField] private float _carryHandForce = 150f;
        [Tooltip("How hard the body is drawn back to the crate when it lags behind (m/s^2)")]
        [SerializeField] private float _carryLeash = 20f;
        [Tooltip("Throw speed of a quick tap of Q (m/s)")]
        [SerializeField] private float _throwMinSpeed = 4.5f;
        [Tooltip("Throw speed after holding Q for the full charge time (m/s)")]
        [SerializeField] private float _throwMaxSpeed = 13.5f;
        [Tooltip("Seconds of holding Q to reach the full throw speed")]
        [SerializeField] private float _throwChargeTime = 1f;
        [SerializeField] private float _throwStaminaCost = 8f;

        [Header("Climb (Wallclimb)")]
        [Tooltip("How fast Wallclimb pulls the body toward its hang point (the old player used 6 m/s)")]
        [SerializeField] private float _climbPullSpeed = 1.8f;
        [Tooltip("How far a grab may reach upward from the shoulder toward the crosshair (1 = the arm's own length)")]
        [SerializeField] private float _climbReachScale = 1.6f;
        [Tooltip("How far a grab may reach sideways from the shoulder (1 = the arm's own length)")]
        [SerializeField] private float _climbSideReachScale = 1.4f;
        [Tooltip("How far a grab may reach downward from the shoulder (1 = the arm's own length)")]
        [SerializeField] private float _climbDownReachScale = 1.1f;
        [Tooltip("Hanging by one hand, A/D toward the free hand swings the body this far along the wall (1 = the arm's length), so the free hand reaches further")]
        [SerializeField] private float _climbShiftDistance = .9f;
        [Tooltip("How fast that sideways swing moves (m/s)")]
        [SerializeField] private float _climbShiftSpeed = 1.2f;
        [Tooltip("Opacity of your own body while climbing, so the wall behind it shows (gloves stay solid; only on your screen)")]
        [SerializeField, Range(0f, 1f)] private float _climbFadeAlpha = .3f;
        [Tooltip("Opacity of your own body while holding Q to aim a throw (only on your screen; the crate goes to 0.2)")]
        [SerializeField, Range(0f, 1f)] private float _aimBodyFadeAlpha = .5f;
        [Tooltip("Seconds to fade the body out when a climb starts and back in when it ends")]
        [SerializeField] private float _climbFadeTime = .2f;
        [Tooltip("Transparent copy of the body material used while climbing")]
        [SerializeField] private Material _climbFadeBody;
        [Tooltip("Transparent copy of the face material used while climbing")]
        [SerializeField] private Material _climbFadeFace;

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

        [Header("Relaxed Arms")]
        [Tooltip("Free arms (not holding or reaching) are turned this many degrees in toward the body. The idle/walk "
            + "clips were made for a slimmer rig and hold this chibi's arms out stiffly; the clip's forward/back swing is kept.")]
        [SerializeField, Range(0f, 40f)] private float _relaxedArmTuck = 18f;

        [Header("Knockdown")]
        [SerializeField] private float _knockdownFallSpeed = 12f;
        [SerializeField] private float _knockdownTilt = 70f;
        [SerializeField] private float _knockdownSeconds = 1.5f;
        [SerializeField] private float _standUpSeconds = .8f;
        [Header("Fall Respawn Slump")]
        [Tooltip("After falling off the map the body appears this high above the pad and drops onto it limp.")]
        [SerializeField, Min(0f)] private float _respawnDropHeight = .8f;
        [Tooltip("Seconds limp after the fall respawn (the drop included), before the get-up starts.")]
        [SerializeField, Min(0f)] private float _respawnLieSeconds = 1.6f;
        private float _activeStandUpSeconds = .8f, _moveLockedUntil = -1f;
        private bool _respawnSlumpPending;

        /// <summary>Owner, after a fall respawn: drop onto the pad limp, lie a moment, then play the get-up.</summary>
        public void RespawnSlump()
        {
            if (!IsLocal || !_fullRagdoll) return;
            _respawnSlumpPending = true;
        }

        private void ApplyRespawnSlump()
        {
            _respawnSlumpPending = false;
            if (_physicsRoot == null || _climbing) return;
            Vector3 lift = Vector3.up * _respawnDropHeight;
            foreach (var body in _bodies)
            {
                body.position += lift;
                body.transform.position += lift;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            transform.position += lift;
            _lastRootSet = transform.position;
            _knockdownUntil = Time.time + _respawnLieSeconds;
            _standUpUntil = _knockdownUntil; // no physics stand-up: the get-up poses take over
            _moveLockedUntil = float.MaxValue; // until the get-up finishes
            _getUpPending = true;
            // A little forward tip so the limp body falls on its front, where the get-up starts.
            Vector3 forward = Quaternion.Euler(0f, _facingYaw, 0f) * Vector3.forward;
            _bodies[Chest].AddForce(forward * 1.2f, ForceMode.VelocityChange);
            _bodies[Head].AddForce(forward * 1.2f, ForceMode.VelocityChange);
        }
        [Tooltip("While knocked down on the ground, horizontal speed fades at this rate (1/s), so a fall does not slide far on the frictionless limbs.")]
        [SerializeField, Min(0f)] private float _knockdownGroundDrag = 4f;
        [Tooltip("Seconds after a knockdown before the ground drag starts (lets the hit or trip still throw the body a little).")]
        [SerializeField, Min(0f)] private float _knockdownSlideGrace = .15f;

        public bool FullRagdoll => _fullRagdoll;
        public const byte EmoteWave = 1, EmoteCheer = 2;
        private byte _emote;
        private float _emoteUntil;

        /// <summary>Owner: wave (right arm) or cheer (both arms up) for a while, when the hands are free.</summary>
        public void PlayEmote(byte emote, float seconds)
        {
            if (!IsLocal) return;
            _emote = emote;
            _emoteUntil = Time.time + seconds;
        }

        /// <summary>Owner: the crate a left click would lift now (null while carrying, climbing or unable to act).</summary>
        public CarryableObject AimedCargo { get; private set; }
        /// <summary>The crate this player carries: the owner's own view, the server's on the host, else the synced one.</summary>
        public CarryableObject CarriedCargo =>
            !_fullRagdoll ? null :
            IsLocal && _carry != null ? _carry :
            HasAuthority && _serverCarry != null ? _serverCarry :
            IsSpawned && _carriedCargo.Value.TryGet(out NetworkObject o) ? o.GetComponent<CarryableObject>() : null;
        public bool IsKnockedDown => Time.time < _knockdownUntil;
        public bool IsGrounded { get; private set; }
        public bool IsClimbing => _climbing;
        public bool HoldingCargo => _carry != null;
        /// <summary>Holding a crate, or a lift is on its way to the server: Wallclimb must not take a wall meanwhile.</summary>
        public bool BlocksClimbing => _carry != null || (_carryRequestPending && Time.time <= _carryRequestTimeout);
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
        // Carry stance (CarryStance): the hips keep CarryBodyClearance (flat) outside the carried crate's box, input
        // toward the crate fades out over the last CarryStanceApproach before that, and the push back (per m inside,
        // per m/s closing) beats the walk acceleration (_acceleration 25) so walking cannot shove the body into it.
        private const float CarryBodyClearance = .20f, CarryStanceApproach = .06f;
        private const float CarryPushSpring = 400f, CarryPushDamper = 10f, CarryPushMax = 60f;
        // Others' screens: a carrier's drawn hips stay this far (flat) outside their crate (KeepDrawnBodyOutOfCargo).
        private const float DrawnBodyClearance = .28f, DrawnBodyHoldMax = .45f, DrawnBodyMaxShift = .9f;

        // Animated clone (owner)
        private Animator _clone;
        private readonly Transform[] _cloneBones = new Transform[BoneCount];
        private readonly Quaternion[] _holdArmRel = new Quaternion[BoneCount]; // idle arm pose relative to the chest
        private Vector3 _holdChestForwardLocal = Vector3.forward; // the idle pose's forward in the chest bone's frame
        private bool _hasHoldPose;
        private float _nextBumpTime;
        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int MoveRateParam = Animator.StringToHash("MoveRate");
        private static readonly int GroundedParam = Animator.StringToHash("Grounded");
        private static readonly int MoveXParam = Animator.StringToHash("MoveX");
        private static readonly int MoveZParam = Animator.StringToHash("MoveZ");

        // Climb (owner): Wallclimb moves the root, the bodies are posed kinematically
        private bool _climbing;
        private int _seenBoostCount;
        private readonly Vector3[] _fk = new Vector3[BoneCount];
        private Vector3 _hipsForwardLocal = Vector3.forward; // the body's forward in the hips bone's frame
        private float _facingYaw, _slowTurnUntil;           // where the body faces (turns at a capped rate)
        private bool _slowTurnUntilLanded;
        private const float TurnRate = 720f, SlowTurnRate = 240f; // deg/s
        // Carrying turns the body (and the crate, which is held in front of the body, not the camera) slower: a fast
        // mouse flick swung the crate round faster than the body and the hands dragged the body off (1.4 m slide and
        // a dropped crate at 540 deg/s, 2026-11-05). The camera still turns freely.
        private const float CarryTurnRate = 200f;
        private const float CarryStopSpeed = .25f, CarryBrake = 12f;
        private const float OldPlayerGravity = 24f; // PlayerMovement._gravity
        private float _airborneUntil, _boostSteerUntil;

        // Carry (owner side)
        private CarryableObject _carry;
        private Vector3 _carryLocalL, _carryLocalR;
        private readonly ConfigurableJoint[] _carryHand = new ConfigurableJoint[2];
        private CarryableObject _ignoredAfterCarry;
        private float _ignoredAfterCarrySince;
        private bool _carryRequestPending, _dropRequested, _throwCostPending;
        private float _carryRequestTimeout;
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
            DestroyCatchThrowVisuals();
            DestroyRemoteProxy();
            DestroyPhysics();
            base.OnDestroy();
        }

        private bool Blocked =>
            CoopGame.Network.PauseMenu.IsPaused || MissionFailUI.IsVisible || CoopGame.Network.ExpeditionHUD.BlocksGameplayInput ||
            CoopGame.Network.PhotoMode.IsOn || CoopGame.Network.TitleScreen.IsVisible ||
            CoopGame.Network.LevelMap.IsOpen || CoopGame.Network.Cosmetics.IsOpen;

        private bool Exhausted => _stamina != null && _stamina.IsExhausted;
        private float Card(CoopGame.Network.CardEffect effect) => _expedition != null ? _expedition.CardMagnitude(effect) : 1f;

        // ───────────────────────── Input (owner) ─────────────────────────

        private void Update()
        {
            if (!_fullRagdoll || !IsLocal || _input == null) return;
            if (_input.JumpTriggered && !_climbing) _jumpQueued = true;
            bool canAct = !IsKnockedDown && !Blocked && !_climbing && !_gettingUp;
            AimedCargo = _carry == null && canAct && !Exhausted && (_wallclimb == null || !_wallclimb.IsClimbing) &&
                         FindAimedCargo(out CarryableObject aimed) ? aimed : null;

            UpdateCatchInput(canAct);
            UpdateThrownCrateHit();
            if (_carry != null)
            {
                if (UpdateThrowAim(canAct)) { }
                // F sets the crate down (George 2026-11-05: the mouse buttons are for climbing only).
                else if (_input.CarryPressed && canAct) RequestPlace();
                if (_input.ScrollStep != 0)
                    _liftSteps = Mathf.Clamp(_liftSteps + _input.ScrollStep, -CarryableObject.HoldLowerSteps, CarryableObject.HoldRaiseSteps);

                // Holding drains stamina like the old carry; running out drops the crate.
                if (_stamina != null && _carry != null) // placing/throwing may just have ended the carry
                {
                    _stamina.DrainStaminaContinuous(false, _carry.TotalMass, _carry.CurrentCarrierCount, _carry.StaminaDrainMultiplier);
                    if (_stamina.IsExhausted) RequestDrop(true);
                }
            }
            else if ((_carryTapReleased || (_input != null && _input.CarryPressed)) && Time.time >= _nextPickupAllowedTime && AimedCargo != null && (!_carryRequestPending || Time.time > _carryRequestTimeout))
            {
                RequestCarry(AimedCargo);
            }
            else if (CatchReady && (!_carryRequestPending || Time.time > _carryRequestTimeout) && FindCatchableCargo(out CarryableObject flying))
            {
                RequestCarry(flying);
            }
        }

        // ───────────────────────── Physics (owner) ─────────────────────────

        private void FixedUpdate()
        {
            if (!_fullRagdoll) return;
            if (HasAuthority) ServerWatchCarry();
            RegisterSpawned();
            if (!IsLocal) { UpdateRemoteProxy(); return; }
            if (!EnsureBones()) return;
            if (_physicsRoot == null) BuildPhysics();

            float dt = Time.fixedDeltaTime;
            Vector2 move = _input != null ? _input.MoveInput : Vector2.zero;
            if (Blocked || Time.time < _moveLockedUntil) move = Vector2.zero;
            float yaw = _camera != null ? _camera.Yaw : transform.eulerAngles.y;
            float pitch = _camera != null ? _camera.Pitch : 0f;
            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
            RefreshCargoColliders();

            if (UpdateClimbMode(pitch, dt)) return;
            // Respawn/teleport helpers re-enable the CharacterController; outside a climb it must stay off.
            if (_controller != null && _controller.enabled) _controller.enabled = false;
            DetectTeleport();
            if (_respawnSlumpPending) ApplyRespawnSlump();
            if (UpdateGetUp()) return;

            bool moving = move.sqrMagnitude > .01f;
            // No sprinting while holding a crate (George 2026-11-09: sprint + carry kept stamina from draining).
            bool sprint = _input != null && _input.SprintHeld && moving && !Exhausted && _carry == null;
            if (sprint && _stamina != null) _stamina.ConsumeActionStamina(_sprintStaminaDrain * dt);
            Vector3 desired = facing * new Vector3(move.x, 0f, move.y);
            desired = Vector3.ClampMagnitude(desired, 1f) * (sprint ? _sprintSpeed : _walkSpeed);
            if (_carry != null)
                desired *= _carry.GetSpeedMultiplier() * Card(CoopGame.Network.CardEffect.FastCarrier) * (Exhausted ? .5f : 1f);

            Vector3 hipsVel = _bodies[Hips].linearVelocity;
            Vector3 flatVel = Flat(hipsVel);
            Vector3 carryPush = Vector3.zero;
            // Carrying together (George 2026-11-09): every carrier walks with the crate, and the crate moves with the
            // team's averaged input. Same speed in every direction, and a carrier who stops slows the team (instead of
            // sliding after it on their own).
            bool teamCarry = _carry != null && _carry.CurrentCarrierCount >= 2;
            if (teamCarry) desired = CarriedCargoVelocity();
            if (_carry != null) desired = CarryStance(desired, moving, flatVel, out carryPush);

            // Ground: sphere cast from the hips; the limb layer is left out of the mask.
            IsGrounded = Physics.SphereCast(_bodies[Hips].position + Vector3.up * .1f, .18f, Vector3.down, out RaycastHit ground,
                _standHeight + .45f, Physics.DefaultRaycastLayers & ~(1 << _limbLayer), QueryTriggerInteraction.Ignore)
                && Time.time >= _airborneUntil;
            if (IsGrounded) _airJumpsUsed = 0;
            DetectHardLanding(hipsVel.y);

            bool limp = IsKnockedDown;
            float recover = !limp && Time.time < _standUpUntil ? 1f - (_standUpUntil - Time.time) / _activeStandUpSeconds : 1f;
            UpdateDrives(limp ? 0f : Mathf.SmoothStep(.15f, 1f, recover));
            if (limp && IsGrounded && Time.time > _knockdownUntil - _knockdownSeconds + _knockdownSlideGrace)
            {
                // Limbs have no friction (so walking never snags); while lying down, bleed off the slide instead.
                float keep = Mathf.Exp(-_knockdownGroundDrag * dt);
                for (int b = 0; b < _bodies.Length; b++)
                {
                    if (_bodies[b] == null) continue;
                    Vector3 v = _bodies[b].linearVelocity;
                    v.x *= keep; v.z *= keep;
                    _bodies[b].linearVelocity = v;
                }
            }

            // The body turns to the camera heading at a capped rate (slower just after a climb), so letting go of a
            // wall and grabbing again never snaps it round between the wall and the camera.
            // After letting go of a wall the slow turn lasts until the body has landed (plus a moment).
            if (_slowTurnUntilLanded)
            {
                if (IsGrounded) { _slowTurnUntilLanded = false; _slowTurnUntil = Mathf.Max(_slowTurnUntil, Time.time + .4f); }
                else _slowTurnUntil = Mathf.Max(_slowTurnUntil, Time.time + .1f);
            }
            bool slowTurn = Time.time < _slowTurnUntil;
            float turnRate = slowTurn ? SlowTurnRate : TurnRate;
            if (_carry != null) turnRate = Mathf.Min(turnRate, CarryTurnRate);
            // Carrying together: the body keeps facing the crate (the camera still looks around freely), so turning
            // the camera away can no longer swing the grips round and twist the arms.
            float bodyYaw = yaw;
            if (teamCarry)
            {
                Vector3 toCrate = Flat(CarryGripMid() - _bodies[Hips].position);
                if (toCrate.sqrMagnitude > .0025f) bodyYaw = Quaternion.LookRotation(toCrate).eulerAngles.y;
            }
            _facingYaw = Mathf.MoveTowardsAngle(_facingYaw, bodyYaw, turnRate * dt);
            Quaternion bodyFacing = Quaternion.Euler(0f, _facingYaw, 0f);
            if (_clone != null) UpdateClonePose(flatVel, bodyFacing, IsGrounded);
            else UpdateTargetPose(flatVel.magnitude, bodyFacing, dt);
            UpdateArms(pitch, bodyFacing, physics: true);
            ApplyJumpPose(bodyFacing, hipsVel.y, limp || _carry != null || Time.time < _emoteUntil, dt);

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
                float gain = IsGrounded || Time.time < _boostSteerUntil ? _acceleration : _airAcceleration;
                // Standing still: hold the spot, so the slippery feet do not let the body creep.
                Vector3 hold = Vector3.zero;
                // Carrying alone without walking: the spot is held even when the crate swinging round a turn drags
                // the hands (the body used to give way after half a metre and slide off with the crate).
                bool carryHold = IsGrounded && !moving && _carry != null && _carry.CurrentCarrierCount < 2;
                bool still = IsGrounded && desired == Vector3.zero && (carryHold || flatVel.sqrMagnitude < .25f);
                Vector3 hipsFlat = Flat(_bodies[Hips].position);
                // Just after letting go of a key the carry hold waits for the body to stop before it pins the spot:
                // pinning it at the release point pulled the body back after it coasted on (A/D slid back ~0.2 m).
                bool coasting = carryHold && !_holding && flatVel.sqrMagnitude > CarryStopSpeed * CarryStopSpeed;
                if (carryHold && coasting) desired = Vector3.zero;
                if (!still || !_holding || coasting || (!carryHold && (hipsFlat - _holdSpot).sqrMagnitude > .25f))
                { _holdSpot = hipsFlat; _holding = still && !coasting; }
                else hold = (_holdSpot - hipsFlat) * HoldSpring;
                // Carrying, no input: brake firmly so the body stops where the key was let go instead of coasting.
                if (carryHold && coasting) hold = -flatVel * CarryBrake;
                accel += Vector3.ClampMagnitude((desired - flatVel) * 6f + hold, gain) + CarryLeash(facing) + carryPush;
                for (int i = 0; i < BoneCount; i++) _bodies[i].AddForce(accel, ForceMode.Acceleration);

                if (_jumpQueued && Time.time >= _moveLockedUntil) TryJump();

                // Stay upright and turn to the camera heading (hips and chest follow the target pose). Just after
                // letting go of a wall the turn is softer, so the landing cannot flick the body round.
                float uprightScale = Time.time < _slowTurnUntil ? .35f : 1f;
                Upright(Hips, recover * uprightScale);
                Upright(Chest, recover * .6f * uprightScale);
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

            UpdateCarry(move, facing, bodyFacing, hipsVel);
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
            _jumpPoseActive = true;
            _jumpPoseLeftGroundAt = Time.time;
        }

        // ───────────────────────── Jump pose ─────────────────────────
        // George 2026-11-11 (photo of a jumping child): in the air after a jump, arms up in a V with the elbows a
        // little bent, knees bent with the feet tucked behind, head up a little. Eased in on the way up and back out
        // while falling, so the legs are straight again before landing. Not while carrying, climbing or knocked down.
        [Header("Jump Pose")]
        [Tooltip("How far the jump pose goes (0 = old clip pose only, 1 = full pose).")]
        [SerializeField, Range(0f, 1f)] private float _jumpPoseAmount = 1f;
        [Tooltip("Falling faster than this (m/s) eases the legs back down for the landing.")]
        [SerializeField, Min(0f)] private float _jumpPoseReleaseFallSpeed = 1.5f;
        private bool _jumpPoseActive;
        private float _jumpPoseWeight, _jumpPoseLeftGroundAt;
        private readonly Quaternion[] _jumpPose = new Quaternion[BoneCount];

        private void ApplyJumpPose(Quaternion facing, float verticalSpeed, bool blocked, float dt)
        {
            if (IsGrounded && Time.time > _jumpPoseLeftGroundAt + .15f) _jumpPoseActive = false;
            bool want = _jumpPoseActive && !blocked && !IsGrounded && verticalSpeed > -_jumpPoseReleaseFallSpeed;
            if (_jumpPoseActive && !blocked && Time.time < _jumpPoseLeftGroundAt + .15f) want = true; // take-off frames
            _jumpPoseWeight = Mathf.MoveTowards(_jumpPoseWeight, want ? 1f : 0f, dt * (want ? 6f : 5f));
            float w = Mathf.SmoothStep(0f, 1f, _jumpPoseWeight) * _jumpPoseAmount;
            if (w <= 0f) return;
            if (!_hasGetUpAxes) BuildGetUpAxes();

            Vector3 F = facing * Vector3.forward, S = facing * Vector3.right, U = Vector3.up;
            void Aim(int i, Vector3 dir)
            {
                Quaternion rest = facing * _restLocal[i];
                _jumpPose[i] = Quaternion.FromToRotation(rest * _getUpAxis[i], dir.normalized) * rest;
            }
            for (int i = 0; i < BoneCount; i++) _jumpPose[i] = _target[i];
            Vector3 torso = U - F * .06f;
            Aim(1, torso); Aim(Chest, torso);
            Aim(Head, U - F * .12f);
            Vector3 upperL = U * .55f - S * .85f + F * .1f, foreL = U * .9f - S * .45f + F * .05f;
            Vector3 upperR = U * .55f + S * .85f + F * .1f, foreR = U * .9f + S * .45f + F * .05f;
            Aim(ArmL, upperL); Aim(ArmL + 1, foreL); Aim(HandL, foreL);   // hands straight on from the forearms
            Aim(ArmR, upperR); Aim(ArmR + 1, foreR); Aim(HandR, foreR);
            // Palms turned in toward the head (a little forward), like the photo; the shortest-arc aim left them
            // facing forward, which read as a bent-back wrist. Forearm and hand roll together (no wrist twist).
            RollPalm(ArmL + 1, foreL, S * .8f + F * .35f, facing);
            RollPalm(HandL, foreL, S * .8f + F * .35f, facing);
            RollPalm(ArmR + 1, foreR, -S * .8f + F * .35f, facing);
            RollPalm(HandR, foreR, -S * .8f + F * .35f, facing);
            Aim(LegL, -U * .9f + F * .35f - S * .18f); Aim(LegL + 1, -U * .35f - F * .95f - S * .05f); Aim(LegL + 2, -U * .8f - F * .6f);
            Aim(LegR, -U * .9f + F * .35f + S * .18f); Aim(LegR + 1, -U * .35f - F * .95f + S * .05f); Aim(LegR + 2, -U * .8f - F * .6f);
            for (int i = 1; i < BoneCount; i++) _target[i] = Quaternion.Slerp(_target[i], _jumpPose[i], w);
        }

        /// <summary>
        /// Rolls _jumpPose[i] about its aim direction so the palm faces <paramref name="palmToward"/>. The palm is taken
        /// as the side of the hand that faced the body in the rest pose (arms hanging, palms in).
        /// </summary>
        private void RollPalm(int i, Vector3 aim, Vector3 palmToward, Quaternion facing)
        {
            bool left = i <= HandL;
            Quaternion rest = facing * _restLocal[i];
            Vector3 restPalmWorld = facing * (left ? Vector3.right : Vector3.left); // toward the body when hanging
            Vector3 palmLocal = Quaternion.Inverse(rest) * restPalmWorld;
            Vector3 axis = aim.normalized;
            Vector3 cur = Vector3.ProjectOnPlane(_jumpPose[i] * palmLocal, axis);
            Vector3 want = Vector3.ProjectOnPlane(palmToward, axis);
            if (cur.sqrMagnitude < 1e-4f || want.sqrMagnitude < 1e-4f) return;
            _jumpPose[i] = Quaternion.AngleAxis(Vector3.SignedAngle(cur, want, axis), axis) * _jumpPose[i];
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

        // ───────────────────────── Get-up after a fall respawn ─────────────────────────
        // George 2026-11-11: five key poses (lying face down, push up on the hands, kneel on one knee, push up with
        // the bottom high, stand). The bodies turn kinematic and are posed like the climb: each bone aimed along a
        // direction in the side plane of the body, the keys joined by a Catmull-Rom curve so the motion flows through
        // them, the ragdoll's real lying pose blended into the first key, then physics takes over standing.
        // Angles (degrees): 0 = straight down, 90 = forward, 180 = up, -90 = back.
        // torso, head, thighL, shinL, footL, thighR, shinR, footR, upperArmL, forearmL, upperArmR, forearmR
        private static readonly float[][] GetUpKeys =
        {
            new[] {  90f, 100f, -90f, -90f, -10f, -90f, -90f, -10f, -50f, 20f, -50f, 20f }, // 1 lying face down
            new[] { 122f, 140f, -95f, -92f, -10f, -95f, -92f, -10f,  10f,  8f,  10f,  8f }, // 2 push up on the hands
            new[] { 170f, 178f,   0f, -90f,-100f,  85f,   0f,  90f,  15f, 35f,  30f, 75f }, // 3 kneel on one knee
            new[] { 112f, 125f,  12f,   2f,  90f,  16f,   4f,  90f,  10f, 12f,  10f, 12f }, // 4 push up, bottom high
            new[] { 180f, 180f,   0f,   0f,  90f,   0f,   0f,  90f,   6f, 12f,   6f, 12f }, // 5 stand
        };
        [Header("Fall Respawn Get-up")]
        [Tooltip("Seconds to blend from the lying ragdoll into the first key pose.")]
        [SerializeField, Min(.05f)] private float _getUpBlendIn = .4f;
        [Tooltip("Seconds from one key pose to the next (four steps).")]
        [SerializeField, Min(.1f)] private float _getUpStepSeconds = .45f;
        [Tooltip("Seconds to hand the standing pose back to the walking animation.")]
        [SerializeField, Min(0f)] private float _getUpBlendOut = .25f;

        private readonly Quaternion[] _getUpFrom = new Quaternion[BoneCount];
        private readonly Vector3[] _getUpAxis = new Vector3[BoneCount];
        private readonly float[] _getUpAngles = new float[12];
        private bool _getUpPending, _gettingUp, _hasGetUpAxes;
        private float _getUpStart;
        private Vector3 _getUpFromHips, _getUpHipsXZ;
        private Quaternion _getUpFacing;

        public bool GettingUp => _gettingUp;

        /// <summary>Each body's local direction toward the next bone in the upright rest pose (head: up, feet: forward).</summary>
        private void BuildGetUpAxes()
        {
            Quaternion facing = Quaternion.identity;
            var rest = new Quaternion[BoneCount];
            for (int i = 0; i < BoneCount; i++) rest[i] = facing * _restLocal[i];
            _target.CopyTo(_getUpFrom, 0);
            for (int i = 0; i < BoneCount; i++) _target[i] = rest[i];
            ForwardKinematics(Vector3.zero);
            for (int i = 0; i < BoneCount; i++)
            {
                Vector3 dir;
                if (i == Head) dir = Vector3.up;
                else if (i == LegL + 2 || i == LegR + 2) dir = Vector3.forward;
                else if (i == HandL || i == HandR) dir = _fk[i] - _fk[i - 1];
                else
                {
                    int child = System.Array.IndexOf(Parents, i);
                    dir = child > 0 ? _fk[child] - _fk[i] : Vector3.up;
                }
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.up;
                _getUpAxis[i] = Quaternion.Inverse(rest[i]) * dir.normalized;
            }
            _getUpFrom.CopyTo(_target, 0);
            _hasGetUpAxes = true;
        }

        private static Vector3 SideDir(Quaternion facing, float degrees, float outward = 0f)
        {
            float a = degrees * Mathf.Deg2Rad;
            return facing * new Vector3(outward, -Mathf.Cos(a), Mathf.Sin(a)).normalized;
        }

        /// <summary>Pose angles at time <paramref name="t"/> (seconds after the first key), Catmull-Rom through the keys.</summary>
        private void SampleGetUpAngles(float t)
        {
            int last = GetUpKeys.Length - 1;
            float u = Mathf.Clamp(t / _getUpStepSeconds, 0f, last);
            int k = Mathf.Min(Mathf.FloorToInt(u), last - 1);
            float s = u - k;
            float[] p0 = GetUpKeys[Mathf.Max(0, k - 1)], p1 = GetUpKeys[k], p2 = GetUpKeys[k + 1], p3 = GetUpKeys[Mathf.Min(last, k + 2)];
            float s2 = s * s, s3 = s2 * s;
            for (int j = 0; j < 12; j++)
                _getUpAngles[j] = .5f * (2f * p1[j] + (-p0[j] + p2[j]) * s + (2f * p0[j] - 5f * p1[j] + 4f * p2[j] - p3[j]) * s2
                                         + (-p0[j] + 3f * p1[j] - 3f * p2[j] + p3[j]) * s3);
        }

        /// <summary>Fills _target with the get-up pose for the current angles.</summary>
        private void GetUpTargets(Quaternion facing)
        {
            float[] a = _getUpAngles;
            void Aim(int i, Vector3 dir)
            {
                Quaternion rest = facing * _restLocal[i];
                _target[i] = Quaternion.FromToRotation(rest * _getUpAxis[i], dir) * rest;
            }
            Vector3 torso = SideDir(facing, a[0]);
            Aim(Hips, torso); Aim(1, torso); Aim(Chest, torso);
            Aim(Head, SideDir(facing, a[1]));
            Aim(LegL, SideDir(facing, a[2], -.08f)); Aim(LegL + 1, SideDir(facing, a[3])); Aim(LegL + 2, SideDir(facing, a[4]));
            Aim(LegR, SideDir(facing, a[5], .08f)); Aim(LegR + 1, SideDir(facing, a[6])); Aim(LegR + 2, SideDir(facing, a[7]));
            Aim(ArmL, SideDir(facing, a[8], -.35f)); Aim(ArmL + 1, SideDir(facing, a[9], -.15f)); Aim(HandL, SideDir(facing, a[9], -.15f));
            Aim(ArmR, SideDir(facing, a[10], .35f)); Aim(ArmR + 1, SideDir(facing, a[11], .15f)); Aim(HandR, SideDir(facing, a[11], .15f));
        }

        private void StartGetUp()
        {
            _getUpPending = false;
            if (!_hasGetUpAxes) BuildGetUpAxes();
            // Face the way the head lies, so key 1 (face down, head forward) starts from where the body is.
            Vector3 headDir = Flat(_bodies[Head].position - _bodies[Hips].position);
            float yaw = headDir.sqrMagnitude > .0025f ? Quaternion.LookRotation(headDir).eulerAngles.y : _facingYaw;
            _getUpFacing = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < BoneCount; i++) _getUpFrom[i] = _bodies[i].rotation;
            _getUpFromHips = _bodies[Hips].position;
            _getUpHipsXZ = Flat(_getUpFromHips);
            _getUpStart = Time.time;
            _gettingUp = true;
            RequestDrop();
            foreach (var body in _bodies)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }

        /// <summary>Owner: poses the kinematic body through the get-up. Returns true while it plays.</summary>
        private bool UpdateGetUp()
        {
            if (_getUpPending && !IsKnockedDown && !_climbing && _physicsRoot != null) StartGetUp();
            if (!_gettingUp) return false;

            float t = Time.time - _getUpStart;
            float keysTime = _getUpStepSeconds * (GetUpKeys.Length - 1);
            float blendIn = Mathf.SmoothStep(0f, 1f, t / _getUpBlendIn);
            float poseTime = Mathf.Max(0f, t - _getUpBlendIn);
            SampleGetUpAngles(Mathf.Min(poseTime, keysTime));
            IsGrounded = true;
            GetUpTargets(_getUpFacing);
            if (blendIn < 1f)
                for (int i = 0; i < BoneCount; i++) _target[i] = Quaternion.Slerp(_getUpFrom[i], _target[i], blendIn);

            // Lowest point of the posed body on the ground under the hips.
            ForwardKinematics(Vector3.zero);
            float lowest = float.MaxValue;
            for (int i = 0; i < BoneCount; i++) lowest = Mathf.Min(lowest, _fk[i].y - (i == HandL || i == HandR ? .03f : .05f));
            float groundY = _getUpFromHips.y - .3f;
            if (Physics.Raycast(_getUpFromHips + Vector3.up * .5f, Vector3.down, out RaycastHit hit, 3f,
                    Physics.DefaultRaycastLayers & ~(1 << _limbLayer), QueryTriggerInteraction.Ignore))
                groundY = hit.point.y;
            Vector3 hips = new Vector3(_getUpHipsXZ.x, groundY - lowest, _getUpHipsXZ.z);
            if (blendIn < 1f) hips = Vector3.Lerp(_getUpFromHips, hips, blendIn);
            ForwardKinematics(hips);
            for (int i = 0; i < BoneCount; i++)
            {
                _bodies[i].MovePosition(_fk[i]);
                _bodies[i].MoveRotation(_target[i]);
            }
            _facingYaw = _getUpFacing.eulerAngles.y;

            if (t >= _getUpBlendIn + keysTime + _getUpBlendOut) FinishGetUp();
            return true;
        }

        private void FinishGetUp()
        {
            _gettingUp = false;
            foreach (var body in _bodies)
            {
                body.isKinematic = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            _standUpUntil = Time.time; // standing already: full strength, no physics stand-up
            _moveLockedUntil = Time.time;
            _holding = false;
            _airSpeed = 0f;
            _wasGrounded = true;
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
            if (_carry != null && _hasHoldPose) FaceUpperBodyForward(facing);
            for (int h = 0; h < 2; h++)
            {
                int upper = h == 0 ? ArmL : ArmR;
                float side = h == 0 ? -1f : 1f;
                Vector3 shoulder = physics ? _bodies[upper].position : _fk[upper];
                bool hasTarget = ArmTarget(h, shoulder, out Vector3 target, out Vector3 pole, facing);
                if (!hasTarget)
                {
                    // Hanging on the wall, a free arm hangs relaxed instead of playing the jump clip's flailing.
                    if (!physics && _hasHoldPose)
                        for (int i = upper; i < upper + 3; i++) _target[i] = _target[Chest] * _holdArmRel[i];
                    // Otherwise it plays the clip, turned in toward the body (about the facing axis, so the clip's
                    // forward/back swing stays as it is).
                    else if (physics && EmoteTarget(h, shoulder, facing, out Vector3 emoteTarget, out Vector3 emotePole))
                        SolveArm(upper, shoulder, emoteTarget, emotePole);
                    else if (physics && _relaxedArmTuck > 0f)
                        SwingChain(upper, -side * _relaxedArmTuck, facing * Vector3.forward);
                    continue;
                }
                if (_hasHoldPose)
                    for (int i = upper; i < upper + 3; i++) _target[i] = _target[Chest] * _holdArmRel[i];
                // Carrying: elbows out to the sides, a little back and down. (Mostly-down flipped the elbow when the
                // crate was held low, with the arms then pointing along the hint.)
                if (pole == Vector3.zero) pole = right * side - facing * Vector3.forward * .5f - Vector3.up * .4f;
                SolveArm(upper, shoulder, target, pole);
            }
            _target[Head] = Quaternion.AngleAxis(Mathf.Clamp(pitch, -40f, 40f) * .4f, right) * _target[Head];
        }

        /// <summary>
        /// Carrying: the strafe and run clips twist the chest up to ~50 deg off the heading, and the holding arms are
        /// built from the chest, so on A/D their elbows swung in across the crate. The chest keeps facing the crate
        /// (spine takes half the correction); hips and legs keep the clip.
        /// </summary>
        private void FaceUpperBodyForward(Quaternion facing)
        {
            Vector3 chestForward = Flat(_target[Chest] * _holdChestForwardLocal);
            if (chestForward.sqrMagnitude < 1e-4f) return;
            float twist = Vector3.SignedAngle(chestForward, facing * Vector3.forward, Vector3.up);
            Quaternion full = Quaternion.AngleAxis(twist, Vector3.up), half = Quaternion.AngleAxis(twist * .5f, Vector3.up);
            _target[Hips + 1] = half * _target[Hips + 1];
            _target[Chest] = full * _target[Chest];
            _target[Head] = full * _target[Head];
        }

        /// <summary>Hand target of a wave (right hand swings side to side above the shoulder) or cheer (both up).</summary>
        private bool EmoteTarget(int h, Vector3 shoulder, Quaternion facing, out Vector3 target, out Vector3 pole)
        {
            target = pole = Vector3.zero;
            if (Time.time >= _emoteUntil || _emote == 0 || IsKnockedDown || _climbing || _carry != null) return false;
            if (_emote == EmoteWave && h == 0) return false;
            float side = h == 0 ? -1f : 1f;
            Vector3 right = facing * Vector3.right, forward = facing * Vector3.forward;
            float t = Time.time;
            target = _emote == EmoteWave
                ? shoulder + Vector3.up * .36f + right * (side * .1f + .12f * Mathf.Sin(t * 11f)) + forward * .08f
                : shoulder + Vector3.up * (.38f + .05f * Mathf.Sin(t * 10f + h)) + right * side * .14f + forward * .05f;
            pole = right * side - forward * .3f;
            return true;
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
            bool reaching = _input != null && !Blocked && !CoopGame.Network.DevCheatMenu.IsOpen && (h == 0 ? _input.GrabLeftHeld : _input.GrabRightHeld);
            if (!gripping && !reaching) return false;
            Transform hand = h == 0 ? _ikTargets.LeftHand : _ikTargets.RightHand;
            if (hand == null) return false;
            target = hand.position;
            // Climbing elbows point out to the side and back from the wall. (Mostly-down made the elbow flip round
            // when a hand reached down, since the arm then points along the hint.) A hand on the wall never points
            // out-and-back, so this hint never lines up with the arm.
            pole = (facing * Vector3.right) * (h == 0 ? -1f : 1f) - facing * Vector3.forward * .6f - Vector3.up * .3f;
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

            // Turn the upper arm as a whole frame (its axis onto the elbow, its bend side onto the forearm's side),
            // not by the shortest swing: a hand above the head is ~180 deg from the hold pose, where the shortest
            // swing picks an arbitrary roll and the shoulder twists.
            Vector3 upperAxis = _target[upper] * _boneAxis[upper];
            Vector3 foreSide = Vector3.ProjectOnPlane(_target[lower] * _boneAxis[lower], upperAxis);
            Vector3 newForeSide = Vector3.ProjectOnPlane(wrist - elbow, elbow - shoulder);
            Quaternion r1 = foreSide.sqrMagnitude > 1e-4f && newForeSide.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(elbow - shoulder, newForeSide) * Quaternion.Inverse(Quaternion.LookRotation(upperAxis, foreSide))
                : Quaternion.FromToRotation(upperAxis, elbow - shoulder);
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
            // Face into the wall from the held faces only, turning at a capped rate: a re-grab, or a hand on a top
            // edge or side face, must not snap the body round.
            float? wallYaw = ClimbFacingYaw();
            if (wallYaw.HasValue)
                _facingYaw = Mathf.MoveTowardsAngle(_facingYaw, wallYaw.Value, SlowTurnRate * dt);
            Quaternion facing = Quaternion.Euler(0f, _facingYaw, 0f);
            if (_clone != null) UpdateClonePose(Vector3.zero, facing, false);
            else UpdateTargetPose(0f, facing, dt);
            // The airborne clip twists the hips back and forth; hanging on a wall the body keeps facing the wall.
            Vector3 hipsForward = Flat(_target[Hips] * _hipsForwardLocal);
            if (hipsForward.sqrMagnitude > 1e-4f)
            {
                Quaternion untwist = Quaternion.FromToRotation(hipsForward.normalized, facing * Vector3.forward);
                for (int i = 0; i < BoneCount; i++) _target[i] = untwist * _target[i];
            }

            // The body stays exactly where Wallclimb puts the root, as in the old player; the arms reach up toward
            // the holds as far as they go. (Lifting the body to the holds made it float and outrun the camera.)
            Vector3 hips = transform.position - Vector3.up * _hipsAboveRoot;
            ForwardKinematics(hips);
            UpdateArms(pitch, facing, physics: false);
            ForwardKinematics(hips);

            bool jump = (_fk[Hips] - _bodies[Hips].position).sqrMagnitude > 1f; // respawn/teleport mid-climb
            for (int i = 0; i < BoneCount; i++)
            {
                if (jump) { _bodies[i].position = _fk[i]; _bodies[i].rotation = _target[i]; }
                else { _bodies[i].MovePosition(_fk[i]); _bodies[i].MoveRotation(_target[i]); }
            }
            _lastRootSet = transform.position;
            return true;
        }

        /// <summary>
        /// Heading into the wall from the faces the hands hold. A hand that re-grabs on a face turned well away from
        /// the one the body already faces (a corner, the side of a pillar) does not turn it; only when both hands are
        /// on the new face does the body turn to it. Top surfaces never count.
        /// </summary>
        private float? ClimbFacingYaw()
        {
            Vector3 sum = Vector3.zero, any = Vector3.zero;
            for (int h = 0; h < 2; h++)
            {
                bool gripping = h == 0 ? _wallclimb.LeftHandGripping : _wallclimb.RightHandGripping;
                Vector3 n = h == 0 ? _wallclimb.LeftGripNormal : _wallclimb.RightGripNormal;
                if (!gripping || Mathf.Abs(n.y) > .7f) continue;
                Vector3 into = Flat(-n).normalized;
                any += into;
                float delta = Mathf.Abs(Mathf.DeltaAngle(_facingYaw, Quaternion.LookRotation(into).eulerAngles.y));
                if (delta < 50f) sum += into;
            }
            Vector3 dir = sum.sqrMagnitude > .01f ? sum : any;
            if (dir.sqrMagnitude < .01f) return null;
            return Quaternion.LookRotation(dir).eulerAngles.y;
        }

        private void ForwardKinematics(Vector3 hips)
        {
            _fk[Hips] = hips;
            for (int i = 1; i < BoneCount; i++) _fk[i] = _fk[Parents[i]] + _target[Parents[i]] * _anchorInParent[i];
        }

        private void EnterClimb()
        {
            _climbing = true;
            _seenBoostCount = _wallclimb.BoostCount;
            if (_carry != null) RequestDrop();
            if (_controller != null && !_controller.enabled) _controller.enabled = true;
            foreach (var body in _bodies) body.isKinematic = true;
        }

        private void ExitClimb(float dt)
        {
            _climbing = false;
            _slowTurnUntil = Time.time + .8f;
            _slowTurnUntilLanded = true;
            // Letting go hands the body back to physics calmly: like the old player, the climb's pull speed is not
            // carried over (it made the body shoot up at the wall top). A little sideways drift is kept.
            Vector3 pull = _controller != null ? _controller.velocity : Vector3.zero;
            Vector3 velocity = Vector3.ClampMagnitude(Flat(pull), 1.5f) + Vector3.up * Mathf.Min(0f, pull.y);
            if (_wallclimb != null && _wallclimb.BoostCount != _seenBoostCount)
            {
                // Wallclimb's boost is tuned for the old player's -24 gravity; scale the rise to the same height here.
                // The boost replaces the pull speed, it is not added to it.
                Vector3 boost = _wallclimb.LastBoostImpulse;
                velocity = new Vector3(boost.x, boost.y * Mathf.Sqrt(-Physics.gravity.y / OldPlayerGravity), boost.z);
                // Right after the boost the hips are level with the wall top; its edge must not count as ground
                // (the stand spring's damper would eat the rise).
                _airborneUntil = Time.time + .35f;
                _boostSteerUntil = Time.time + .6f; // full air control to get over the top, like the old player
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

        // Client carrier: the crate replica's measured ground speed (CarriedCargoVelocity).
        private CarryableObject _cargoReplicaOf;
        private Vector3 _cargoReplicaLast, _cargoReplicaVel;

        private void SampleCargoReplica()
        {
            if (_carry == null || HasAuthority) { _cargoReplicaOf = null; _cargoReplicaVel = Vector3.zero; return; }
            Vector3 position = CarryGripMid();
            if (_cargoReplicaOf == _carry && Time.deltaTime > 1e-4f)
                _cargoReplicaVel = Vector3.Lerp(_cargoReplicaVel, Flat(position - _cargoReplicaLast) / Time.deltaTime,
                    1f - Mathf.Exp(-Time.deltaTime / .1f));
            _cargoReplicaOf = _carry;
            _cargoReplicaLast = position;
        }

        /// <summary>
        /// Others' screens: a team-mate's streamed pose and the crate arrive with different delays (on the host the
        /// crate is current, the team-mate ~0.2 s old), so a carrier walking with the crate is drawn sunk into it
        /// (crate coming toward them) or trailing behind it (crate going away). The drawn body of a carrier is kept
        /// at its hold distance from the crate's box. Drawing only: the physics and the network state are untouched.
        /// </summary>
        private void KeepDrawnBodyOutOfCargo()
        {
            if (!_carriedCargo.Value.TryGet(out NetworkObject cargoObject) ||
                !cargoObject.TryGetComponent(out CarryableObject cargo)) return;
            Vector3 hips = _bones[Hips].position;
            Vector3 shift = HoldBandShift(cargo, hips, DrawnBodyClearance, DrawnBodyHoldMax);
            if (shift != Vector3.zero) _bones[Hips].position = hips + Vector3.ClampMagnitude(shift, DrawnBodyMaxShift);
        }

        private static readonly Dictionary<CarryableObject, Bounds> s_cargoLocalBounds = new();

        /// <summary>Flat move that puts <paramref name="point"/> between <paramref name="min"/> and
        /// <paramref name="max"/> outside the crate's box (its colliders in crate space, all heights).</summary>
        private static Vector3 HoldBandShift(CarryableObject cargo, Vector3 point, float min, float max)
        {
            if (!s_cargoLocalBounds.TryGetValue(cargo, out Bounds box))
            {
                bool any = false;
                foreach (var c in cargo.GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger) continue;
                    Bounds world = c.bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 corner = world.center + Vector3.Scale(world.extents,
                            new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                        if (c is BoxCollider b) corner = b.transform.TransformPoint(b.center + Vector3.Scale(b.size * .5f,
                            new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)));
                        Vector3 local = cargo.transform.InverseTransformPoint(corner);
                        if (!any) { box = new Bounds(local, Vector3.zero); any = true; } else box.Encapsulate(local);
                    }
                }
                if (!any) return Vector3.zero;
                s_cargoLocalBounds[cargo] = box;
            }
            Transform t = cargo.transform;
            Vector3 p = t.InverseTransformPoint(point) - box.center;
            Vector3 e = box.extents;
            if (Mathf.Abs(p.x) >= e.x || Mathf.Abs(p.z) >= e.z)
            {
                Vector3 surface = t.TransformPoint(box.center + new Vector3(Mathf.Clamp(p.x, -e.x, e.x), p.y, Mathf.Clamp(p.z, -e.z, e.z)));
                Vector3 outward = Flat(point - surface);
                float d = outward.magnitude;
                if (d > 1e-4f)
                {
                    if (d < min) return outward * ((min - d) / d);
                    return d > max ? outward * ((max - d) / d) : Vector3.zero;
                }
            }
            // Inside the box: out through the nearest side.
            Vector3 scale = t.lossyScale;
            float inX = e.x + min / Mathf.Max(1e-3f, Mathf.Abs(scale.x)) - Mathf.Abs(p.x);
            float inZ = e.z + min / Mathf.Max(1e-3f, Mathf.Abs(scale.z)) - Mathf.Abs(p.z);
            Vector3 localMove = inX < inZ ? new Vector3(Mathf.Sign(p.x) * inX, 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(p.z) * inZ);
            return Flat(t.TransformVector(localMove));
        }

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
                int hits = Physics.SphereCastNonAlloc(ray, .15f, _aimHits, 8f, ~(1 << _limbLayer), QueryTriggerInteraction.Collide);
                float nearest = float.MaxValue;
                for (int i = 0; i < hits; i++)
                {
                    var c = _aimHits[i].collider.GetComponentInParent<CarryableObject>();
                    if (c == null || !c.CanBeCarried || _aimHits[i].distance >= nearest) continue;
                    float maxReach = (c is CoopGame.CarrySystem.SeeSawWeight) ? _carryPickupReach * 1.5f : _carryPickupReach;
                    if (Flat(_aimHits[i].collider.ClosestPoint(body) - body).magnitude > maxReach) continue;
                    nearest = _aimHits[i].distance;
                    cargo = c;
                }
                if (cargo != null) return true;
            }
            Vector3 forward = _camera != null ? _camera.HorizontalForward : transform.forward;
            int count = Physics.OverlapSphereNonAlloc(body + forward * .5f, _carryPickupReach * 1.2f, _cargoNear, ~(1 << _limbLayer), QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var c = _cargoNear[i].GetComponentInParent<CarryableObject>();
                if (c == null || !c.CanBeCarried) continue;
                Vector3 to = Flat(_cargoNear[i].ClosestPoint(body) - body);
                float maxReach = (c is CoopGame.CarrySystem.SeeSawWeight) ? _carryPickupReach * 1.5f : _carryPickupReach;
                if (to.magnitude > maxReach || Vector3.Dot(to.normalized, forward) < .2f || to.sqrMagnitude >= best) continue;
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
            _carryRequestTimeout = Time.time + 2f; // an unanswered request never blocks lifting for good
            if (HasAuthority) ServerStartCarry(cargo, localL, localR);
            else StartCarryRpc(cargo.NetworkObject, localL, localR);
        }

        private void RequestPlace()
        {
            _carryPressedAt = -1f;
            _carryTapReleased = false;
            _nextPickupAllowedTime = Time.time + 0.45f;
            _throwCostPending = false;
            if (HasAuthority) ServerPlace(_colliders);
            else PlaceCarryRpc();
        }

        private void RequestThrow(float charge)
        {
            Vector3 velocity = ThrowVelocity(charge);
            _throwCostPending = true; // stamina is paid when the server has really let go (OnCarryEnded)
            if (HasAuthority) ServerThrow(velocity);
            else ThrowCarryRpc(velocity);
        }

        private void RequestDrop(bool exhausted = false)
        {
            // Asked once per carry: running out of stamina calls this every frame until the server lets go.
            if (_carry == null || _dropRequested) return;
            _dropRequested = true;
            _throwCostPending = false;
            if (HasAuthority) ServerDropCarry(exhausted);
            else DropCarryRpc(exhausted);
        }

        /// <summary>Owner: the server attached this player to the crate (or refused).</summary>
        private void OnCarryStarted(CarryableObject cargo, Vector3 localL, Vector3 localR, bool ok)
        {
            _carryRequestPending = false;
            if (!ok || cargo == null || _physicsRoot == null) return;
            _carry = cargo;
            _dropRequested = false;
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
            if (_carry != null) { _ignoredAfterCarry = _carry; _ignoredAfterCarrySince = Time.time; }
            if (_carry != null && _throwCostPending && _stamina != null) _stamina.ConsumeStamina(_throwStaminaCost);
            _throwCostPending = false;
            _carry = null;
            _carryPressedAt = -1f;
            _carryTapReleased = false;
            _nextPickupAllowedTime = Mathf.Max(_nextPickupAllowedTime, Time.time + 0.45f);
        }

        private void UpdateCarry(Vector2 move, Quaternion facing, Quaternion bodyFacing, Vector3 hipsVel)
        {
            RestoreCargoCollisionWhenClear();
            if (_carry == null) return;
            // The crate is held in front of the body (which turns at CarryTurnRate), walking input stays camera-relative.
            Vector3 heading = bodyFacing * Vector3.forward;
            Vector3 direction = Vector3.ClampMagnitude(facing * new Vector3(move.x, 0f, move.y), 1f);
            float height = CarryableObject.NeutralHoldHeight + _liftSteps * CarryableObject.HoldStepHeight;
            if (HasAuthority) ServerCarryInput(direction, heading, height, transform.position, Flat(hipsVel));
            else CarryInputRpc(direction, heading, height, transform.position, Flat(hipsVel));
        }

        private Vector3 CarryGripMid() =>
            (_carry.transform.TransformPoint(_carryLocalL) + _carry.transform.TransformPoint(_carryLocalR)) * .5f;

        /// <summary>The crate's ground speed as this machine has it (the server body on the host, the replica's
        /// per-frame motion on a client).</summary>
        private Vector3 CarriedCargoVelocity()
        {
            // The grips' own speed, not the crate centre's: a turning crate swings its centre round while the
            // grips stay in front of the carrier.
            if (HasAuthority && _carry.TryGetComponent(out Rigidbody body)) return Flat(body.GetPointVelocity(CarryGripMid()));
            return _cargoReplicaOf == _carry ? _cargoReplicaVel : Vector3.zero;
        }

        /// <summary>
        /// Owner, while carrying: the crate leads (it moves with the team's averaged input). The body and its own
        /// crate do not collide, so walking could push the body into the crate: input toward the grips fades out
        /// as the body reaches its hold distance, and a body closer than that is pushed back harder than walking
        /// pushes in.
        /// </summary>
        private Vector3 CarryStance(Vector3 desired, bool hasInput, Vector3 flatVel, out Vector3 push)
        {
            push = Vector3.zero;
            Vector3 cargoVel = CarriedCargoVelocity();
            // No "follow the crate" when idle: the crate moves where this body faces, so following it (alone, or
            // turning while a partner holds the other end) made the body chase its own crate and slide off. A
            // stopped partner is drawn along by CarryLeash instead.
            // Kept out of the crate's box itself, not away from the grips: while turning the crate swings round and
            // its gripped face sweeps past close to the hips, which a grip-distance test took for clipping and
            // pushed the body away (it slid off while turning in place).
            Vector3 hips = _bodies[Hips].position;
            Vector3 outward = HoldBandShift(_carry, hips, CarryBodyClearance + CarryStanceApproach, float.MaxValue);
            float gap = outward.magnitude;
            if (gap < 1e-4f) return desired;
            Vector3 dir = -outward / gap; // toward the crate
            float into = Vector3.Dot(desired - cargoVel, dir);
            if (into > 0f) desired -= dir * (into * Mathf.Clamp01(gap / CarryStanceApproach));
            float overlap = gap - CarryStanceApproach;
            if (overlap > 0f)
            {
                float closing = Mathf.Max(0f, Vector3.Dot(flatVel - cargoVel, dir));
                push = -dir * Mathf.Min(overlap * CarryPushSpring + closing * CarryPushDamper, CarryPushMax);
            }
            return desired;
        }

        /// <summary>Pulls the body back toward the crate's grips when it gets ahead of (or away from) them.</summary>
        private Vector3 CarryLeash(Quaternion facing)
        {
            if (_carry == null) return Vector3.zero;
            // Only the distance to the grips is kept. (Holding the grips in front of the camera dragged the body
            // sideways whenever the camera turned faster than the crate, so turning in place made it slide.)
            Vector3 toGrips = Flat(CarryGripMid() - _bodies[Hips].position);
            float distance = toGrips.magnitude;
            float slack = Mathf.Abs(distance - CarryableObject.HoldReach) - .12f;
            if (slack <= 0f || distance < 1e-3f) return Vector3.zero;
            return toGrips / distance * Mathf.Sign(distance - CarryableObject.HoldReach) * Mathf.Min(slack * _carryLeash * 4f, _carryLeash);
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
            // Knocked down while carrying: the limp body used to fall into the crate and stay inside it, because
            // collisions only came back once nothing overlapped (George 2026-11-09). Restore them right after the fall
            // starts so physics pushes the body out instead.
            bool fallen = IsKnockedDown && Time.time > _ignoredAfterCarrySince + .12f;
            if (!fallen)
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
                if (c == null || !c.enabled) continue;
                if (c.isTrigger && c.GetComponentInParent<SeeSawWeight>() == null) continue;
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
                if (c == null || !c.enabled) continue;
                if (c.isTrigger && c.GetComponentInParent<SeeSawWeight>() == null) continue;
                if (c is MeshCollider m && !m.convex) continue;
                Vector3 p = (c is BoxCollider || c is SphereCollider || c is CapsuleCollider) ? c.ClosestPoint(point) : c.bounds.ClosestPoint(point);
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
            if (ok && IsSpawned) CueRpc(CoopGame.Network.GameplayFeedback.Cue.Lift, cargo.transform.position);
            if (ok && IsSpawned) _carriedCargo.Value = cargo.NetworkObject;
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
            if (_serverCarry != null && _serverCarry.TryPlaceGently(CarrierId, carrierColliders) && IsSpawned)
                CueRpc(CoopGame.Network.GameplayFeedback.Cue.Place, _serverCarry.transform.position);
            ServerWatchCarry();
        }

        private void ServerThrow(Vector3 velocity)
        {
            if (_serverCarry == null) return;
            if (IsSpawned) CueRpc(CoopGame.Network.GameplayFeedback.Cue.Throw, _serverCarry.transform.position);
            _serverCarry.ThrowObject(CarrierId, Vector3.ClampMagnitude(velocity, CarryableObject.BaseThrowLimit), Random.insideUnitSphere * 2f);
            ServerWatchCarry();
        }

        private void ServerDropCarry(bool exhausted = false)
        {
            var dropped = _serverCarry;
            if (_serverCarry != null && _serverCarry.HasCarrier(CarrierId)) _serverCarry.DetachCarrier(CarrierId);
            else dropped = null;
            if (exhausted && dropped is FragileCargo fragile) fragile.ApplyExhaustedDropServer();
            ServerWatchCarry();
        }

        /// <summary>Server: tells the owner as soon as this player no longer carries (however that happened).</summary>
        private void ServerWatchCarry()
        {
            if (_serverCarry != null && _serverCarry.HasCarrier(CarrierId) && !_serverCarry.IsSecured) return;
            bool hadCarry = _serverCarry != null;
            _serverCarry = null;
            if (hadCarry && IsSpawned) _carriedCargo.Value = default;
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
        private void DropCarryRpc(bool exhausted, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId == OwnerClientId) ServerDropCarry(exhausted);
        }

        [Rpc(SendTo.Owner)]
        private void CarryEndedRpc() => OnCarryEnded();

        // ───────────────────────── Sound and effects ─────────────────────────

        /// <summary>Carry and knockdown cues for every screen (server or owner sends; nothing gameplay-related).</summary>
        [Rpc(SendTo.Everyone)]
        private void CueRpc(CoopGame.Network.GameplayFeedback.Cue cue, Vector3 at) => PlayCue(cue, at);

        private static void PlayCue(CoopGame.Network.GameplayFeedback.Cue cue, Vector3 at)
        {
            CoopGame.Network.GameplayFeedback.Play(cue, at);
            if (cue == CoopGame.Network.GameplayFeedback.Cue.Knockdown)
            {
                CoopGame.Network.GameplayFeedback.Burst(at, new Color(1f, .95f, .7f), 18, 3f, .12f);
                CoopGame.Network.CameraShake.Add(at, .8f);
            }
        }

        private byte _shownFistBits;

        /// <summary>Every screen: a hand that closes on a wall hold (synced fist bits, not while carrying) clicks.</summary>
        private void CueClimbGrab(byte bits)
        {
            byte closed = (byte)(bits & ~_shownFistBits);
            _shownFistBits = bits;
            if (closed == 0 || !_fullRagdoll || !_bonesReady) return;
            if (IsLocal ? _carry != null : !_carriedCargo.Value.Equals(default(NetworkObjectReference))) return;
            int hand = (closed & 1) != 0 ? HandL : HandR;
            CoopGame.Network.GameplayFeedback.Play(CoopGame.Network.GameplayFeedback.Cue.ClimbGrab, _bones[hand].position);
        }

        // ───────────────────────── Drawing ─────────────────────────

        private void LateUpdate()
        {
            if (!_fullRagdoll || !EnsureBones()) return;
            UpdateCatchGlow();
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
                LiftClavicles(); // after publishing: every screen lifts its own drawn clavicles the same way
                SampleCargoReplica();
                // Fists while a hand holds a wall or a crate; other players read the same state.
                bool left = _carry != null || (_wallclimb != null && _wallclimb.LeftHandGripping);
                bool right = _carry != null || (_wallclimb != null && _wallclimb.RightHandGripping);
                byte bits = (byte)((left ? 1 : 0) | (right ? 2 : 0));
                if (IsSpawned && _fistBits.Value != bits) _fistBits.Value = bits;
                CueClimbGrab(bits);
                DrawFists(left, right);
                UpdateFirstPersonHands();
                UpdateClimbFade();
            }
            else if (_sync != null && _sync.TrySampleRemotePose(_remoteLocal, out Vector3 hipsLocal, out Quaternion hipsRot))
            {
                Transform visual = _rig.Visual;
                _bones[Hips].position = visual.TransformPoint(hipsLocal);
                _bones[Hips].rotation = visual.rotation * hipsRot;
                for (int i = 1; i < BoneCount; i++) _bones[i].localRotation = _remoteLocal[i];
                LiftClavicles();
                KeepDrawnBodyOutOfCargo();
                CueClimbGrab(_fistBits.Value);
                DrawFists((_fistBits.Value & 1) != 0, (_fistBits.Value & 2) != 0);
            }
        }

        // ───────────────────────── Clavicles ─────────────────────────
        // The physics body has no clavicles, so raising an arm above the shoulder only turns the upper arm and the
        // shoulder skin collapses (George 2026-11-11: arms twisted, shoulders shrank in the jump pose). Drawing only:
        // the clavicle lifts with the arm's elevation along the torso and the arm keeps its drawn direction.
        [Header("Clavicles (drawing)")]
        [Tooltip("Clavicle lift per degree the upper arm points above shoulder level (0 = off).")]
        [SerializeField, Range(0f, 1f)] private float _clavicleLiftShare = .9f;
        [SerializeField, Range(0f, 60f)] private float _clavicleLiftMax = 30f;
        private Transform _clavicleL, _clavicleR;
        private Quaternion _clavicleRestL, _clavicleRestR;
        private bool _claviclesReady;

        private void LiftClavicles()
        {
            if (_clavicleLiftShare <= 0f || _rig == null || _rig.Animator == null) return;
            if (!_claviclesReady)
            {
                _claviclesReady = true;
                _clavicleL = _rig.Animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
                _clavicleR = _rig.Animator.GetBoneTransform(HumanBodyBones.RightShoulder);
                if (_clavicleL != null) _clavicleRestL = _clavicleL.localRotation;
                if (_clavicleR != null) _clavicleRestR = _clavicleR.localRotation;
            }
            Vector3 torsoUp = _bones[Head].position - _bones[Chest].position;
            Vector3 forward = Vector3.ProjectOnPlane(_rig.Visual.forward, torsoUp);
            if (torsoUp.sqrMagnitude < 1e-6f || forward.sqrMagnitude < 1e-4f) return;
            torsoUp.Normalize(); forward.Normalize();
            for (int h = 0; h < 2; h++)
            {
                Transform clavicle = h == 0 ? _clavicleL : _clavicleR;
                if (clavicle == null) continue;
                int upper = h == 0 ? ArmL : ArmR;
                Quaternion upperWorld = _bones[upper].rotation, foreWorld = _bones[upper + 1].rotation, handWorld = _bones[upper + 2].rotation;
                clavicle.localRotation = h == 0 ? _clavicleRestL : _clavicleRestR;
                Vector3 arm = _bones[upper + 1].position - _bones[upper].position;
                if (arm.sqrMagnitude < 1e-6f) continue;
                float elevation = Mathf.Asin(Mathf.Clamp(Vector3.Dot(arm.normalized, torsoUp), -1f, 1f)) * Mathf.Rad2Deg;
                float lift = Mathf.Clamp(elevation * _clavicleLiftShare, 0f, _clavicleLiftMax);
                if (lift > 0f)
                    // Left side lifts with a negative turn about forward, right side with a positive one.
                    clavicle.rotation = Quaternion.AngleAxis(h == 0 ? -lift : lift, forward) * clavicle.rotation;
                _bones[upper].rotation = upperWorld;
                _bones[upper + 1].rotation = foreWorld;
                _bones[upper + 2].rotation = handWorld;
            }
        }

        // ───────────────────────── Fists ─────────────────────────

        [Header("Fists")]
        [Tooltip("Finger curl (degrees) of a hand holding a wall or a crate")]
        [SerializeField] private float _fistFingerAngle = 100f;
        [SerializeField] private float _fistThumbAngle = 50f;
        private readonly NetworkVariable<byte> _fistBits = new NetworkVariable<byte>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        // The crate this player carries (server-written), so other screens can draw the body at the crate.
        private readonly NetworkVariable<NetworkObjectReference> _carriedCargo = new NetworkVariable<NetworkObjectReference>();
        private float _fistL, _fistR;

        /// <summary>Closes the fingers of a holding hand into a fist (and opens them again), eased over ~0.1 s.</summary>
        private void DrawFists(bool left, bool right)
        {
            float step = Time.deltaTime * 10f;
            _fistL = Mathf.MoveTowards(_fistL, left ? 1f : 0f, step);
            _fistR = Mathf.MoveTowards(_fistR, right ? 1f : 0f, step);
            CurlFingers(_rig.FingersL, _fistL);
            CurlFingers(_rig.FingersR, _fistR);
        }

        private void CurlFingers(CharacterRig.Finger[] fingers, float grip)
        {
            if (fingers == null) return;
            foreach (var f in fingers)
            {
                float angle = (f.IsThumb ? _fistThumbAngle : _fistFingerAngle) * grip;
                for (int j = 0; j < f.Joints.Length; j++)
                {
                    Transform joint = f.Joints[j];
                    if (joint == null) continue;
                    joint.localRotation = grip <= .001f ? f.RestRotations[j]
                        : f.RestRotations[j] * Quaternion.AngleAxis(angle * (j == 0 ? .7f : f.IsThumb ? .85f : .95f), f.CurlAxes[j]);
                }
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

        // ───────────────────────── Climb fade ─────────────────────────
        // While climbing, the owner's own body turns see-through on their screen so the wall and holds behind it
        // show. Body and face swap to transparent copies of their materials (the alpha rides on the colour property
        // block PlayerAppearance already sets); the gloves keep their solid material. Others see the body as usual.

        private Renderer _fadeBodyRenderer, _fadeFaceRenderer;
        private Material[] _fadeBodySolid, _fadeBodySeeThrough;
        private Material _fadeFaceSolid;
        private float _fadeAlpha = 1f;
        private bool _fadeSwapped;
        private MaterialPropertyBlock _fadeBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void UpdateClimbFade()
        {
            // Climbing fades the body most; aiming a throw fades it less (the crate is ghosted harder, ThrowPreview).
            float target = _fpShown || _climbFadeBody == null ? 1f : _climbing ? _climbFadeAlpha : _aimingThrow ? _aimBodyFadeAlpha : 1f;
            if (!_fadeSwapped && target >= 1f) { _fadeAlpha = 1f; return; }
            if (_fadeBodyRenderer == null && !FindFadeRenderers()) return;
            _fadeAlpha = Mathf.MoveTowards(_fadeAlpha, target, Time.deltaTime / Mathf.Max(.01f, _climbFadeTime));
            bool seeThrough = _fadeAlpha < .999f;
            if (seeThrough != _fadeSwapped)
            {
                _fadeSwapped = seeThrough;
                _fadeBodyRenderer.sharedMaterials = seeThrough ? _fadeBodySeeThrough : _fadeBodySolid;
                if (_fadeFaceRenderer != null && _climbFadeFace != null)
                    _fadeFaceRenderer.sharedMaterial = seeThrough ? _climbFadeFace : _fadeFaceSolid;
            }
            SetFadeAlpha(_fadeBodyRenderer, seeThrough ? _fadeAlpha : 1f);
            if (_fadeFaceRenderer != null) SetFadeAlpha(_fadeFaceRenderer, seeThrough ? _fadeAlpha : 1f);
        }

        private bool FindFadeRenderers()
        {
            _fadeBodyRenderer = _rig.BodyRenderer;
            if (_fadeBodyRenderer == null) return false;
            // The see-through copy is named after the solid material it replaces (Mat_chBODY -> Mat_chBODY_ClimbFade).
            _fadeBodySolid = _fadeBodyRenderer.sharedMaterials;
            _fadeBodySeeThrough = (Material[])_fadeBodySolid.Clone();
            for (int i = 0; i < _fadeBodySeeThrough.Length; i++)
                if (_fadeBodySeeThrough[i] != null && _climbFadeBody.name.StartsWith(_fadeBodySeeThrough[i].name + "_"))
                    _fadeBodySeeThrough[i] = _climbFadeBody;
            _fadeFaceRenderer = _rig.FaceRenderer;
            if (_fadeFaceRenderer != null) _fadeFaceSolid = _fadeFaceRenderer.sharedMaterial;
            return true;
        }

        private void SetFadeAlpha(Renderer renderer, float alpha)
        {
            _fadeBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_fadeBlock);
            Color color = _fadeBlock.HasColor(BaseColorId) ? _fadeBlock.GetColor(BaseColorId) : Color.white;
            if (Mathf.Approximately(color.a, alpha)) return;
            color.a = alpha;
            _fadeBlock.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(_fadeBlock);
        }

        /// <summary>The player root (camera, UI, NetworkTransform, checkpoints) stands under the hips.</summary>
        private void FollowHipsWithRoot()
        {
            Vector3 hips = _bodies[Hips].transform.position;
            // Lying down: the root keeps its yaw. Following the camera here made other players see the fallen body
            // spin with this player's mouse (the hips are sent relative to the root, and the root arrives late).
            float yaw = IsKnockedDown || Time.time < _standUpUntil ? transform.eulerAngles.y : _camera != null ? _camera.Yaw : transform.eulerAngles.y;
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
            if (_gettingUp) FinishGetUp();
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
            _hipsForwardLocal = Quaternion.Inverse(_bodies[Hips].rotation) * (facing * Vector3.forward);
            _facingYaw = facing.eulerAngles.y;
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

            // Climb with this body's real shoulder height and arm length (Wallclimb's defaults suit a taller player).
            if (_wallclimb != null)
            {
                float shoulder = (_bodies[ArmL].position.y + _bodies[ArmR].position.y) * .5f - transform.position.y;
                float arm = _boneLength[ArmL] + _boneLength[ArmL + 1] + .06f; // + half a hand to the palm
                _wallclimb.ConfigureBody(shoulder, arm, _climbPullSpeed, _hipsAboveRoot + _standHeight,
                    _climbReachScale, _climbSideReachScale, _climbDownReachScale, _climbShiftDistance, _climbShiftSpeed);
            }
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
            _holdChestForwardLocal = chest * Vector3.forward;
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
            if (!IsLocal || _physicsRoot == null || IsKnockedDown || _climbing || _gettingUp) return;
            _knockdownUntil = Time.time + _knockdownSeconds;
            _activeStandUpSeconds = _standUpSeconds;
            _standUpUntil = _knockdownUntil + _activeStandUpSeconds;
            RequestDrop();
            if (IsSpawned && _expedition != null) _expedition.ReportEventRpc(CoopGame.Network.LevelResults.EventKnockdown);
            Vector3 hit = _bodies[Chest].position;
            if (IsSpawned) CueRpc(CoopGame.Network.GameplayFeedback.Cue.Knockdown, hit);
            else PlayCue(CoopGame.Network.GameplayFeedback.Cue.Knockdown, hit);
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
