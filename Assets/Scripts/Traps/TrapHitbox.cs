using System.Collections.Generic;
using CoopGame.CarrySystem;
using CoopGame.Player;
using UnityEngine;

namespace CoopGame.Traps
{
    /// <summary>
    /// The part of a trap that hurts. A player it touches is knocked down and flung (decided on that player's own
    /// machine, through the existing Knockdown of RagdollCharacter / ActiveRagdoll). A crate it touches loses HP and is
    /// pushed (decided on the server, through FragileCargo.ApplyDamageServer).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class TrapHitbox : MonoBehaviour
    {
        public enum PushDirection
        {
            [Tooltip("The way the trap part is moving at the touch point")] Motion,
            [Tooltip("Straight out from this part's centre (bumpers)")] AwayFromCenter,
            [Tooltip("This part's forward (blue) axis")] Forward,
            [Tooltip("Straight up (spikes)")] Up,
        }

        [Header("Player")]
        [SerializeField] private PushDirection _direction = PushDirection.Motion;
        [Tooltip("Speed (m/s) the player is flung at")]
        [SerializeField, Min(0f)] private float _launchSpeed = 7f;
        [Tooltip("Extra upward speed (m/s) so the player leaves the ground")]
        [SerializeField, Min(0f)] private float _upwardSpeed = 3f;
        [Tooltip("Motion only: below this part speed (m/s) a touch does nothing, so leaning on a slow pole is safe")]
        [SerializeField, Min(0f)] private float _minPartSpeed = .6f;
        [SerializeField] private bool _knockDown = true;
        [Tooltip("Turns the fling speed into a ragdoll impulse (about the body mass that takes the hit)")]
        [SerializeField, Min(1f)] private float _ragdollMass = 45f;
        [Tooltip("A player who is NOT carrying a crate is thrown with the whole body: fling speed x this")]
        [SerializeField, Min(0f)] private float _freePlayerFlingScale = 1.5f;
        [Tooltip("Smallest upward speed (m/s) for a player who is not carrying, so they always leave the ground")]
        [SerializeField, Min(0f)] private float _freePlayerMinUpward = 5f;

        [Header("Cargo")]
        [SerializeField, Min(0)] private int _cargoDamage = 10;
        [Tooltip("Speed (m/s) added to a crate that is hit")]
        [SerializeField, Min(0f)] private float _cargoLaunchSpeed = 3f;
        [SerializeField] private string _damageReason = "Trap";

        [Header("Timing")]
        [Tooltip("Seconds before the same player or crate can be hit again by this part")]
        [SerializeField, Min(.1f)] private float _cooldown = .8f;
        [Tooltip("Optional: only hurts while this object is on (blinking flames)")]
        [SerializeField] private GameObject _onlyWhileActive;
        [Tooltip("Metres added around the collider when looking for touches")]
        [SerializeField, Min(0f)] private float _margin = .12f;

        private Collider _collider;
        private Matrix4x4 _previous;
        private bool _hasPrevious;
        private float _nextPlayerHit;
        private readonly Dictionary<FragileCargo, float> _nextCargoHit = new();
        private static readonly Collider[] s_hits = new Collider[64];
        private static readonly HashSet<FragileCargo> s_cargoThisStep = new();

        public void Configure(PushDirection direction, float launchSpeed, float upwardSpeed, int cargoDamage, string reason)
        {
            _direction = direction; _launchSpeed = launchSpeed; _upwardSpeed = upwardSpeed;
            _cargoDamage = cargoDamage; _damageReason = reason;
        }

        private void Awake() => _collider = GetComponent<Collider>();

        private void OnDisable() => _hasPrevious = false;

        private void FixedUpdate()
        {
            Matrix4x4 current = transform.localToWorldMatrix;
            if (_collider == null || !_collider.enabled || (_onlyWhileActive != null && !_onlyWhileActive.activeInHierarchy))
            {
                _hasPrevious = false;
                return;
            }
            if (!_hasPrevious) { _previous = current; _hasPrevious = true; return; }

            int count = Overlap();
            var local = TrapClock.LocalPlayer;
            bool checkPlayer = local != null && Time.time >= _nextPlayerHit;
            var ragdoll = checkPlayer ? local.GetComponent<RagdollCharacter>() : null;
            var activeRagdoll = checkPlayer ? local.GetComponent<ActiveRagdoll>() : null;
            bool authority = TrapClock.IsAuthority && _cargoDamage >= 0;
            bool playerDone = !checkPlayer;
            s_cargoThisStep.Clear();

            for (int i = 0; i < count; i++)
            {
                Collider other = s_hits[i];
                if (other == null || other == _collider) continue;
                if (!playerDone && IsLocalPlayer(other, local, ragdoll, activeRagdoll))
                {
                    if (TryPush(other.bounds.center, current, out Vector3 velocity))
                    {
                        HitPlayer(local, ragdoll, activeRagdoll, velocity, other);
                        _nextPlayerHit = Time.time + _cooldown;
                    }
                    playerDone = true;
                }
                if (authority)
                {
                    var cargo = other.attachedRigidbody != null ? other.attachedRigidbody.GetComponent<FragileCargo>() : null;
                    if (cargo == null) cargo = other.GetComponentInParent<FragileCargo>();
                    if (cargo != null && s_cargoThisStep.Add(cargo)) HitCargo(cargo, current);
                }
            }
            System.Array.Clear(s_hits, 0, count);
            _previous = current;
        }

        private int Overlap()
        {
            const int mask = Physics.DefaultRaycastLayers;
            const QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore;
            Vector3 scale = transform.lossyScale;
            Vector3 abs = new(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            switch (_collider)
            {
                case BoxCollider box:
                    return Physics.OverlapBoxNonAlloc(transform.TransformPoint(box.center),
                        Vector3.Scale(box.size * .5f, abs) + Vector3.one * _margin, s_hits, transform.rotation, mask, triggers);
                case SphereCollider sphere:
                    return Physics.OverlapSphereNonAlloc(transform.TransformPoint(sphere.center),
                        sphere.radius * Mathf.Max(abs.x, Mathf.Max(abs.y, abs.z)) + _margin, s_hits, mask, triggers);
                case CapsuleCollider capsule:
                {
                    Vector3 dir = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                    float axisScale = capsule.direction == 0 ? abs.x : capsule.direction == 1 ? abs.y : abs.z;
                    float radiusScale = capsule.direction == 0 ? Mathf.Max(abs.y, abs.z) : capsule.direction == 1 ? Mathf.Max(abs.x, abs.z) : Mathf.Max(abs.x, abs.y);
                    float radius = capsule.radius * radiusScale;
                    float half = Mathf.Max(0f, capsule.height * .5f * axisScale - radius);
                    Vector3 center = transform.TransformPoint(capsule.center);
                    Vector3 worldDir = transform.TransformDirection(dir).normalized;
                    return Physics.OverlapCapsuleNonAlloc(center - worldDir * half, center + worldDir * half,
                        radius + _margin, s_hits, mask, triggers);
                }
                default:
                {
                    Bounds b = _collider.bounds;
                    return Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * _margin, s_hits, Quaternion.identity, mask, triggers);
                }
            }
        }

        private static bool IsLocalPlayer(Collider other, Component player, RagdollCharacter ragdoll, ActiveRagdoll activeRagdoll)
        {
            // Full ragdoll: only the limbs count (they are what gets thrown).
            if (ragdoll != null && ragdoll.isActiveAndEnabled && ragdoll.FullRagdoll) return ragdoll.OwnsCollider(other);
            if (ragdoll != null && ragdoll.OwnsCollider(other)) return true;
            if (activeRagdoll != null && activeRagdoll.OwnsCollider(other)) return true;
            return other.transform.IsChildOf(player.transform);
        }

        /// <summary>Fling velocity at a touch point, or false if this part is too slow there to hurt.</summary>
        private bool TryPush(Vector3 point, Matrix4x4 current, out Vector3 velocity)
        {
            Vector3 dir;
            switch (_direction)
            {
                case PushDirection.Motion:
                {
                    Vector3 before = _previous.MultiplyPoint3x4(current.inverse.MultiplyPoint3x4(point));
                    Vector3 partVelocity = (point - before) / Mathf.Max(Time.fixedDeltaTime, 1e-4f);
                    float speed = partVelocity.magnitude;
                    if (speed < _minPartSpeed || speed > 60f) { velocity = Vector3.zero; return false; }
                    dir = partVelocity / speed;
                    break;
                }
                case PushDirection.AwayFromCenter:
                    dir = point - _collider.bounds.center;
                    dir.y = Mathf.Max(0f, dir.y);
                    dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward;
                    break;
                case PushDirection.Forward: dir = transform.forward; break;
                default: dir = Vector3.up; break;
            }
            velocity = dir * _launchSpeed + Vector3.up * _upwardSpeed;
            return true;
        }

        private void HitPlayer(Component player, RagdollCharacter ragdoll, ActiveRagdoll activeRagdoll, Vector3 velocity, Collider touched)
        {
            if (ragdoll != null && ragdoll.isActiveAndEnabled && ragdoll.FullRagdoll)
            {
                if (ragdoll.HoldingCargo)
                {
                    // Carrying: unchanged (falls, and the existing knockdown lets go of the crate).
                    if (_knockDown) ragdoll.Knockdown(velocity * _ragdollMass);
                    return;
                }
                // Not carrying: go limp, then throw every body part at the same speed so the whole player flies.
                if (_knockDown) ragdoll.Knockdown(Vector3.zero);
                Vector3 fling = new(velocity.x * _freePlayerFlingScale, 0f, velocity.z * _freePlayerFlingScale);
                fling.y = Mathf.Max(velocity.y * _freePlayerFlingScale, _freePlayerMinUpward);
                FlingBodies(ragdoll, touched, fling);
                return;
            }
            // Animated player (option A): fling the controller, then let the visual ragdoll fall.
            var movement = player.GetComponent<PlayerMovement>();
            if (movement != null) movement.ApplyImpulse(velocity);
            if (_knockDown && activeRagdoll != null) activeRagdoll.Knockdown(velocity * _ragdollMass);
        }

        private static readonly List<Rigidbody> s_bodies = new();

        /// <summary>Sets the same velocity change on every limb of the local ragdoll (its physics root is a scene root).</summary>
        private static void FlingBodies(RagdollCharacter ragdoll, Collider touched, Vector3 velocity)
        {
            if (touched == null || !ragdoll.OwnsCollider(touched)) return;
            touched.transform.root.GetComponentsInChildren(s_bodies);
            foreach (var body in s_bodies)
            {
                if (body == null || body.isKinematic) continue;
                var col = body.GetComponent<Collider>();
                if (col != null && !ragdoll.OwnsCollider(col)) continue;
                body.AddForce(velocity, ForceMode.VelocityChange);
            }
            s_bodies.Clear();
        }

        private void HitCargo(FragileCargo cargo, Matrix4x4 current)
        {
            if (cargo.IsDestroyed || cargo.IsSecured) return;
            if (_nextCargoHit.TryGetValue(cargo, out float next) && Time.time < next) return;
            if (!TryPush(cargo.transform.position, current, out Vector3 velocity)) return;
            _nextCargoHit[cargo] = Time.time + _cooldown;
            if (_cargoDamage > 0) cargo.ApplyDamageServer(_cargoDamage, _damageReason);
            var body = cargo.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic && _cargoLaunchSpeed > 0f && _launchSpeed > 0f)
                body.AddForce(velocity * (_cargoLaunchSpeed / Mathf.Max(_launchSpeed, .01f)), ForceMode.VelocityChange);
        }
    }
}
