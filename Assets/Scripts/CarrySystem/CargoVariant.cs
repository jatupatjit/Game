using CoopGame.Network;
using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    public enum CargoKind : byte { Normal, Glass, Heavy, TallStack, Chicken, Explosive }

    /// <summary>
    /// Extra behaviour for crate variants (prefabs in Assets/Prefabs/Items/Variants, built on CarryablePackage):
    /// Glass scores more (low HP is set on its FragileCargo), Heavy needs two carriers (CarryableObject flag; one
    /// player alone slips and slides, see RagdollCharacter.HeavySoloSlip), TallStack sways and its upper boxes fall
    /// off when jerked hard or dropped, Chicken carries a hen that gets angry and runs off (ChickenOnCrate), Explosive blows up when dropped twice and
    /// knocks nearby players down. Gameplay runs on the host; the look (sway, falling boxes, jitter, blast) on every screen.
    /// </summary>
    [DefaultExecutionOrder(100)] // after CarryableObject drives the held pose, so its nudges are not overwritten
    [RequireComponent(typeof(FragileCargo))]
    public sealed class CargoVariant : NetworkBehaviour
    {
        [SerializeField] private CargoKind _kind;
        [Tooltip("Delivery score is multiplied by this (glass pays more).")]
        [SerializeField, Min(.1f)] private float _scoreMultiplier = 1f;

        [Header("Tall stack")]
        [Tooltip("Sudden speed changes (m/s) add up in a wobble meter that drains by _wobbleDrain per second; " +
            "past this the top box falls off and the crate takes damage. One start or stop adds about 4.5.")]
        [SerializeField] private float _wobbleLimit = 12f;
        [SerializeField] private float _wobbleDrain = 4f;
        [Tooltip("HP lost each time a box falls off the stack.")]
        [SerializeField] private int _toppleDamage = 6;
        [SerializeField] private float _swayDegrees = 9f;


        [Header("Explosive")]
        [Tooltip("Drops (let go without a gentle F set-down, counted when it lands) before it blows up (George 2026-11-12: 2).")]
        [SerializeField, Min(1)] private int _dropsToExplode = 2;
        [Tooltip("Tall stack: a hit of at least this much damage with nobody holding it knocks a box off (placing costs less).")]
        [SerializeField] private int _dropDamage = 10;
        [Tooltip("The blast breaks the crate (HP 0: checkpoint restart or mission fail).")]
        [SerializeField] private bool _blastBreaksCrate = true;
        [SerializeField] private float _blastRadius = 4.5f;
        [SerializeField] private float _blastImpulse = 450f;

        public CargoKind Kind => _kind;
        /// <summary>Tall stack: how close the wobble meter is to tipping a box off (0..1, host).</summary>
        public float Wobble01 => _wobbleLimit > 0f ? Mathf.Clamp01(_wobble / _wobbleLimit) : 0f;
        public float ScoreMultiplier => _scoreMultiplier;
        /// <summary>Explosive: drops so far (every screen).</summary>
        public int Drops => _drops.Value;

        // Tall stack: boxes fallen off (top first); explosive: drops so far. Server writes, every screen shows.
        private readonly NetworkVariable<byte> _fallen = new();
        private readonly NetworkVariable<byte> _drops = new();

        private FragileCargo _cargo;
        private Rigidbody _body;
        private Transform[] _pieces;
        private Collider[] _colliders;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private Vector3 _lastVelocity, _lastPosition, _seenVelocity, _seenAccel;
        private float _wobble, _nextHiss;
        private bool _blastPending, _wasCarried, _dropPending, _hitSinceRelease;
        private float _releasedAt;

        private void Awake()
        {
            _cargo = GetComponent<FragileCargo>();
            _body = GetComponent<Rigidbody>();
            var pieces = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in transform) if (child.name.StartsWith("Stack")) pieces.Add(child);
            if (_kind == CargoKind.Chicken)
            {
                // George 2026-11-12: the old white chicken (and its random crate jerks) is replaced by the hen in a nest.
                var old = transform.Find("Chicken");
                if (old != null) old.gameObject.SetActive(false);
                if (!TryGetComponent(out ChickenOnCrate _)) gameObject.AddComponent<ChickenOnCrate>();
            }
            _pieces = pieces.ToArray();
            _colliders = GetComponentsInChildren<Collider>(true);
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
            _lastPosition = transform.position;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                FragileCargo.OnCargoDamagedGlobal += OnDamaged;
                FragileCargo.OnCargoRespawnedGlobal += OnRespawned;
            }
            _fallen.OnValueChanged += OnFallenChanged;
            _drops.OnValueChanged += OnDropsChanged;
            if (_kind == CargoKind.TallStack) CrateStackPieces.Show(_pieces, _fallen.Value, Vector3.zero, Vector3.zero, _colliders, false);
        }

        public override void OnNetworkDespawn()
        {
            FragileCargo.OnCargoDamagedGlobal -= OnDamaged;
            FragileCargo.OnCargoRespawnedGlobal -= OnRespawned;
            _fallen.OnValueChanged -= OnFallenChanged;
            _drops.OnValueChanged -= OnDropsChanged;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || _cargo.IsSecured || _cargo.IsDestroyed) return;
            if (_blastPending)
            {
                _blastPending = false;
                BlastRpc(transform.position);
                if (_blastBreaksCrate) _cargo.BreakNowServer("Bomb crate dropped " + _dropsToExplode + " times");
                return;
            }
            Vector3 velocity = _body.linearVelocity;
            Vector3 change = Vector3.ProjectOnPlane(velocity - _lastVelocity, Vector3.up);
            _lastVelocity = velocity;
            TrackDrop(velocity);
            if (!_cargo.IsCarried) { _wobble = 0f; return; }
            if (_kind == CargoKind.TallStack && _fallen.Value < _pieces.Length)
            {
                _wobble = Mathf.Max(0f, _wobble - _wobbleDrain * Time.fixedDeltaTime) + change.magnitude;
                if (_wobble > _wobbleLimit)
                {
                    _wobble = 0f;
                    _fallen.Value++;
                    _cargo.ApplyDamageServer(_toppleDamage, "Tall stack jerked: a box fell off");
                }
            }
        }

        // Every screen: the stack's upper pieces lean against the motion, a chicken's head bobs, a dropped bomb flashes.
        private void Update()
        {
            if (Time.deltaTime <= 0f) return;
            Vector3 velocity = (transform.position - _lastPosition) / Time.deltaTime;
            _lastPosition = transform.position;
            _seenAccel = (velocity - _seenVelocity) / Time.deltaTime;
            _seenVelocity = Vector3.Lerp(_seenVelocity, velocity, 1f - Mathf.Exp(-Time.deltaTime * 8f));
            if (_kind == CargoKind.Explosive) UpdateBombWarning();
            if (_pieces.Length == 0) return;
            Vector3 local = transform.InverseTransformDirection(Vector3.ProjectOnPlane(_seenAccel, Vector3.up)) * .02f;
            for (int i = 0; i < _pieces.Length; i++)
            {
                if (!_pieces[i].gameObject.activeSelf) continue;
                float weight = (i + 1f) / _pieces.Length;
                Quaternion target = _kind == CargoKind.Chicken
                    ? Quaternion.Euler(12f * Mathf.Sin(Time.time * 9f + i), 20f * Mathf.Sin(Time.time * 3.7f), 0f)
                    : Quaternion.Euler(Mathf.Clamp(local.z, -1f, 1f) * _swayDegrees * weight, 0f, -Mathf.Clamp(local.x, -1f, 1f) * _swayDegrees * weight);
                _pieces[i].localRotation = Quaternion.Slerp(_pieces[i].localRotation, target, 1f - Mathf.Exp(-Time.deltaTime * 10f));
            }
        }

        // After the first drop the bomb flashes red and hisses, faster the closer it is to going off.
        private void UpdateBombWarning()
        {
            int drops = _drops.Value;
            if (drops <= 0 || _renderers == null) return;
            float urgency = Mathf.Clamp01(drops / (float)Mathf.Max(1, _dropsToExplode - 1));
            float pulse = .5f + .5f * Mathf.Sin(Time.time * Mathf.Lerp(6f, 14f, urgency));
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", Color.Lerp(BaseColorOf(r), new Color(1f, .15f, .05f), pulse * .8f));
                r.SetPropertyBlock(_block);
            }
            if (Time.time >= _nextHiss)
            {
                _nextHiss = Time.time + Mathf.Lerp(.8f, .4f, urgency);
                GameplayFeedback.Play(GameplayFeedback.Cue.Click, transform.position, .5f);
            }
        }

        private static Color BaseColorOf(Renderer r)
        {
            var m = r.sharedMaterial;
            return m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
        }

        private void ClearBombWarning()
        {
            if (_renderers == null) return;
            foreach (var r in _renderers) if (r != null) { r.GetPropertyBlock(_block); _block.Clear(); r.SetPropertyBlock(_block); }
        }

        private void OnFallenChanged(byte previous, byte current)
        {
            if (_kind != CargoKind.TallStack) return;
            // Boxes fly off against the jerk (a sudden stop throws them forward).
            Vector3 push = -Vector3.ProjectOnPlane(_seenAccel, Vector3.up);
            CrateStackPieces.Show(_pieces, current, _seenVelocity, push, _colliders, current > previous);
        }

        private void OnDropsChanged(byte previous, byte current)
        {
            if (current == 0) ClearBombWarning();
        }

        // Host: a let-go that is not a gentle F set-down (out of stamina, knocked down, thrown and not caught) is a
        // drop once the crate comes to rest on something; catching it again first cancels it. (George 2026-11-12:
        // dropping from carry height often does no damage, so drops are counted from the release, not from HP.)
        private void TrackDrop(Vector3 velocity)
        {
            bool carried = _cargo.IsCarried;
            if (_wasCarried && !carried && !_cargo.IsPlacing)
            {
                _dropPending = true;
                _hitSinceRelease = false;
                _releasedAt = Time.time;
            }
            _wasCarried = carried;
            if (!_dropPending) return;
            if (carried || _cargo.IsPlacing) { _dropPending = false; return; }
            if (Time.time - _releasedAt < .3f || velocity.sqrMagnitude > .36f) return;
            _dropPending = false;
            OnDropped();
        }

        private void OnDropped()
        {
            // The stack loses its top box on a drop, unless a hard hit already knocked one off on the way down.
            if (_kind == CargoKind.TallStack && !_hitSinceRelease && _fallen.Value < _pieces.Length) _fallen.Value++;
            if (_kind != CargoKind.Explosive || _blastPending) return;
            _drops.Value = (byte)Mathf.Min(255, _drops.Value + 1);
            if (_drops.Value >= _dropsToExplode)
            {
                _blastPending = true;
                GameRoom.ServerFeed("ลังระเบิดตกครั้งที่ " + _drops.Value + " — ตูม!", new Color(1f, .45f, .2f));
            }
            else GameRoom.ServerFeed("ลังระเบิดตก " + _drops.Value + "/" + _dropsToExplode + " — ตกอีกครั้งระเบิด!", new Color(1f, .7f, .25f));
        }

        // Host: a hard hit with nobody holding the stack (dropped, thrown, knocked off a ledge) knocks a box off.
        private void OnDamaged(FragileCargo cargo, int damage)
        {
            if (cargo != _cargo || _cargo.IsCarried || damage < _dropDamage) return;
            if (_kind == CargoKind.TallStack && _fallen.Value < _pieces.Length) { _fallen.Value++; _hitSinceRelease = true; }
        }

        private void OnRespawned(FragileCargo cargo)
        {
            if (cargo != _cargo) return;
            _fallen.Value = 0;
            _drops.Value = 0;
            _blastPending = _dropPending = false;
        }

        /// <summary>Every screen: boom, and the local player is knocked away if close.</summary>
        [Rpc(SendTo.Everyone)]
        private void BlastRpc(Vector3 at)
        {
            GameplayFeedback.Burst(at, new Color(1f, .55f, .15f), 50, 6f, .3f);
            GameplayFeedback.Burst(at, new Color(.35f, .3f, .28f), 30, 2.5f, .45f);
            GameplayFeedback.Play(GameplayFeedback.Cue.Broken, at);
            CameraShake.Add(at, 1f);
            var manager = NetworkManager.Singleton;
            var player = manager != null && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            if (player == null || !player.TryGetComponent(out RagdollCharacter ragdoll)) return;
            Vector3 away = ragdoll.HipsPosition - at;
            float distance = away.magnitude;
            if (distance > _blastRadius) return;
            Vector3 direction = (Vector3.ProjectOnPlane(away, Vector3.up).normalized + Vector3.up * .6f).normalized;
            ragdoll.Knockdown(direction * _blastImpulse * (1f - distance / _blastRadius * .5f));
        }
    }
}
