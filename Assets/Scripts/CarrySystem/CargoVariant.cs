using CoopGame.Network;
using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    public enum CargoKind : byte { Normal, Glass, Heavy, TallStack, Chicken, Explosive }

    /// <summary>
    /// Extra behaviour for crate variants (prefabs in Assets/Prefabs/Items/Variants, built on CarryablePackage):
    /// Glass scores more (low HP is set on its FragileCargo), Heavy needs two carriers (CarryableObject flag),
    /// TallStack sways and takes damage when jerked hard, Chicken jitters while carried, Explosive blows up on a hard
    /// hit and knocks nearby players down. Gameplay runs on the host; the look (sway, jitter, blast) on every screen.
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
            "past this the stack takes damage. One start or stop adds about 4.5.")]
        [SerializeField] private float _wobbleLimit = 12f;
        [SerializeField] private float _wobbleDrain = 4f;
        [SerializeField] private int _toppleDamage = 6;
        [SerializeField] private float _swayDegrees = 9f;

        [Header("Chicken")]
        [SerializeField] private float _jitterInterval = .45f;
        [SerializeField] private float _jitterSpeed = 1.6f;

        [Header("Explosive")]
        [Tooltip("A single hit of at least this much damage sets it off.")]
        [SerializeField] private int _explodeAtDamage = 12;
        [SerializeField] private float _blastRadius = 4.5f;
        [SerializeField] private float _blastImpulse = 450f;

        public CargoKind Kind => _kind;
        /// <summary>Tall stack: how close the wobble meter is to doing damage (0..1, host).</summary>
        public float Wobble01 => _wobbleLimit > 0f ? Mathf.Clamp01(_wobble / _wobbleLimit) : 0f;
        public float ScoreMultiplier => _scoreMultiplier;

        private FragileCargo _cargo;
        private Rigidbody _body;
        private Transform[] _pieces;
        private Vector3 _lastVelocity, _lastPosition, _seenVelocity;
        private float _nextJitter, _wobble, _nextBlast;

        private void Awake()
        {
            _cargo = GetComponent<FragileCargo>();
            _body = GetComponent<Rigidbody>();
            var pieces = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in transform) if (child.name.StartsWith("Stack") || child.name.StartsWith("Chicken")) pieces.Add(child);
            _pieces = pieces.ToArray();
            _lastPosition = transform.position;
        }

        public override void OnNetworkSpawn() { if (IsServer) FragileCargo.OnCargoDamagedGlobal += OnDamaged; }
        public override void OnNetworkDespawn() => FragileCargo.OnCargoDamagedGlobal -= OnDamaged;

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || _cargo.IsSecured || _cargo.IsDestroyed) return;
            Vector3 velocity = _body.linearVelocity;
            Vector3 change = Vector3.ProjectOnPlane(velocity - _lastVelocity, Vector3.up);
            _lastVelocity = velocity;
            if (!_cargo.IsCarried) { _wobble = 0f; return; }
            if (_kind == CargoKind.TallStack)
            {
                _wobble = Mathf.Max(0f, _wobble - _wobbleDrain * Time.fixedDeltaTime) + change.magnitude;
                if (_wobble > _wobbleLimit)
                {
                    _wobble = 0f;
                    _cargo.ApplyDamageServer(_toppleDamage, "Tall stack jerked");
                }
            }
            if (_kind == CargoKind.Chicken && Time.time >= _nextJitter)
            {
                _nextJitter = Time.time + _jitterInterval * Random.Range(.6f, 1.4f);
                Vector2 kick = Random.insideUnitCircle.normalized * _jitterSpeed;
                _body.linearVelocity += new Vector3(kick.x, Random.Range(.5f, 1.2f), kick.y);
                _body.angularVelocity += Random.insideUnitSphere * 2f;
                ChickenRpc();
            }
        }

        // Every screen: the stack's upper pieces lean against the motion, a chicken's head bobs.
        private void Update()
        {
            if (_pieces.Length == 0 || Time.deltaTime <= 0f) return;
            Vector3 velocity = (transform.position - _lastPosition) / Time.deltaTime;
            _lastPosition = transform.position;
            Vector3 accel = (velocity - _seenVelocity) / Time.deltaTime;
            _seenVelocity = Vector3.Lerp(_seenVelocity, velocity, 1f - Mathf.Exp(-Time.deltaTime * 8f));
            Vector3 local = transform.InverseTransformDirection(Vector3.ProjectOnPlane(accel, Vector3.up)) * .02f;
            for (int i = 0; i < _pieces.Length; i++)
            {
                float weight = (i + 1f) / _pieces.Length;
                Quaternion target = _kind == CargoKind.Chicken
                    ? Quaternion.Euler(12f * Mathf.Sin(Time.time * 9f + i), 20f * Mathf.Sin(Time.time * 3.7f), 0f)
                    : Quaternion.Euler(Mathf.Clamp(local.z, -1f, 1f) * _swayDegrees * weight, 0f, -Mathf.Clamp(local.x, -1f, 1f) * _swayDegrees * weight);
                _pieces[i].localRotation = Quaternion.Slerp(_pieces[i].localRotation, target, 1f - Mathf.Exp(-Time.deltaTime * 10f));
            }
        }

        [Rpc(SendTo.Everyone)]
        private void ChickenRpc() => GameplayFeedback.Play(GameplayFeedback.Cue.Click, transform.position, .8f);

        private void OnDamaged(FragileCargo cargo, int damage)
        {
            if (cargo != _cargo || _kind != CargoKind.Explosive || damage < _explodeAtDamage || Time.time < _nextBlast) return;
            _nextBlast = Time.time + 3f;
            BlastRpc(transform.position);
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
