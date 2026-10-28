using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// Feeds the character's Animator from gameplay state so clips can be dropped into
    /// <c>PlayerCharacter.controller</c> without code changes. The owner writes the parameters;
    /// an owner-authority <see cref="NetworkAnimator"/> on the player root copies them to everyone.
    ///
    /// Until the controller has at least one clip the Animator stays disabled, so the
    /// procedural arms, legs and hand IK keep the bind pose they were tuned on.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]
    public sealed class PlayerAnimatorDriver : NetworkBehaviour
    {
        // Parameter names in PlayerCharacter.controller. Keep in sync with PlayerCharacterSetup.
        public static readonly int SpeedId = Animator.StringToHash("Speed");
        public static readonly int MoveXId = Animator.StringToHash("MoveX");
        public static readonly int MoveZId = Animator.StringToHash("MoveZ");
        public static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");
        public static readonly int GroundedId = Animator.StringToHash("Grounded");
        public static readonly int SprintingId = Animator.StringToHash("Sprinting");
        public static readonly int CarryingId = Animator.StringToHash("Carrying");
        public static readonly int CarryHandsId = Animator.StringToHash("CarryHands");
        public static readonly int ClimbingId = Animator.StringToHash("Climbing");
        public static readonly int ChargingThrowId = Animator.StringToHash("ChargingThrow");
        public static readonly int JumpId = Animator.StringToHash("Jump");
        public static readonly int ThrowId = Animator.StringToHash("Throw");

        [Tooltip("Seconds to smooth Speed/MoveX/MoveZ so blend trees do not flicker")]
        [SerializeField, Min(0f)] private float _dampTime = 0.1f;

        private CharacterRig _rig;
        private PlayerMovement _movement;
        private PlayerInputReader _input;
        private Wallclimb _wallclimb;
        private CoopGame.CarrySystem.PlayerCarry _carry;
        private NetworkAnimator _networkAnimator;
        private bool _wasGrounded = true;
        private bool _jumpQueued;
        private bool _throwQueued;
        private const string UpperBodyLayerName = "UpperBody";
        private const float UpperBodyThrowHold = 0.6f;
        private int _upperBodyLayer = -1;
        private float _upperBodyHold;

        public Animator Animator => _rig != null ? _rig.Animator : null;

        private void Awake()
        {
            _rig = GetComponent<CharacterRig>();
            _movement = GetComponent<PlayerMovement>();
            _input = GetComponent<PlayerInputReader>();
            _wallclimb = GetComponent<Wallclimb>();
            _carry = GetComponent<CoopGame.CarrySystem.PlayerCarry>();
            _networkAnimator = GetComponent<NetworkAnimator>();
            if (_carry != null) _carry.Thrown += OnThrown;
            RefreshAnimatorEnabled();
        }

        public override void OnDestroy()
        {
            if (_carry != null) _carry.Thrown -= OnThrown;
            base.OnDestroy();
        }

        /// <summary>Turns the Animator on only when its controller has clips to play.</summary>
        public void RefreshAnimatorEnabled()
        {
            Animator animator = Animator;
            if (animator == null) return;
            var controller = animator.runtimeAnimatorController;
            bool hasClips = controller != null && controller.animationClips.Length > 0;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (animator.enabled != hasClips) animator.enabled = hasClips;
        }

        private bool HasLocalAuthority => !IsSpawned || IsOwner;

        private void OnThrown() => _throwQueued = true;

        private void Update()
        {
            Animator animator = Animator;
            if (animator == null || !animator.enabled || animator.runtimeAnimatorController == null) return;
            if (!HasLocalAuthority) return; // NetworkAnimator writes remote players' parameters.

            float dt = Time.deltaTime;
            Vector3 velocity = _movement != null ? _movement.Velocity : Vector3.zero;
            Vector3 localVelocity = transform.InverseTransformDirection(new Vector3(velocity.x, 0f, velocity.z));
            float speed = _movement != null ? _movement.CurrentSpeed : localVelocity.magnitude;
            bool grounded = _movement == null || _movement.IsGrounded;
            bool climbing = _wallclimb != null && _wallclimb.IsClimbing;
            bool carrying = _carry != null && _carry.IsCarrying;
            int hands = _carry == null ? 0 : (_carry.LeftHandGripping ? 1 : 0) + (_carry.RightHandGripping ? 1 : 0);

            if (_wasGrounded && !grounded && velocity.y > 1f && !climbing) _jumpQueued = true;
            _wasGrounded = grounded;

            animator.SetFloat(SpeedId, speed, _dampTime, dt);
            animator.SetFloat(MoveXId, localVelocity.x, _dampTime, dt);
            animator.SetFloat(MoveZId, localVelocity.z, _dampTime, dt);
            animator.SetFloat(VerticalSpeedId, velocity.y);
            animator.SetBool(GroundedId, grounded);
            animator.SetBool(SprintingId, _input != null && _input.SprintHeld && speed > 0.1f);
            animator.SetBool(CarryingId, carrying);
            animator.SetInteger(CarryHandsId, hands);
            animator.SetBool(ClimbingId, climbing);
            animator.SetBool(ChargingThrowId, _carry != null && _carry.IsChargingThrow);

            if (_jumpQueued) { Trigger(JumpId); _jumpQueued = false; }
            if (_throwQueued) { Trigger(ThrowId); _throwQueued = false; _upperBodyHold = UpperBodyThrowHold; }

            // The upper-body layer only covers carry and throw; fade it out otherwise.
            if (_upperBodyLayer < 0) _upperBodyLayer = animator.GetLayerIndex(UpperBodyLayerName);
            if (_upperBodyLayer > 0)
            {
                _upperBodyHold = Mathf.Max(0f, _upperBodyHold - dt);
                bool active = carrying || (_carry != null && _carry.IsChargingThrow) || _upperBodyHold > 0f;
                float weight = Mathf.MoveTowards(animator.GetLayerWeight(_upperBodyLayer), active ? 1f : 0f, dt / 0.2f);
                animator.SetLayerWeight(_upperBodyLayer, weight);
            }
        }

        private void Trigger(int id)
        {
            if (IsSpawned && _networkAnimator != null) _networkAnimator.SetTrigger(id);
            else Animator.SetTrigger(id);
        }
    }
}
