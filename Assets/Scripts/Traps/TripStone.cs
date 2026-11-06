using CoopGame.Player;
using UnityEngine;

namespace CoopGame.Traps
{
    /// <summary>
    /// A small stone on the path. A player who walks into it faster than a careful step trips forward (the existing
    /// Knockdown, decided on that player's own machine like <see cref="TrapHitbox"/>); a carried crate drops with the fall.
    /// Walking slowly or stepping around it is safe. The stone's own collider just bumps crates.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TripStone : MonoBehaviour
    {
        [Tooltip("Feet closer than this (m, flat) to the stone centre count as stepping on it")]
        [SerializeField, Min(.1f)] private float _radius = .45f;
        [Tooltip("Players slower than this (m/s) step over safely")]
        [SerializeField, Min(0f)] private float _minSpeed = 1.6f;
        [Tooltip("Forward speed (m/s) of the stumble")]
        [SerializeField, Min(0f)] private float _tripSpeed = 3.2f;
        [SerializeField, Min(0f)] private float _upwardSpeed = 1.2f;
        [SerializeField, Min(1f)] private float _ragdollMass = 45f;
        [Tooltip("Seconds before this stone can trip the same player again")]
        [SerializeField, Min(.1f)] private float _cooldown = 2.5f;

        // Player pivot sits about this far above the feet.
        private const float FeetBelowPivot = 1.05f;

        private Vector3 _lastPosition;
        private bool _hasLast;
        private float _nextTrip;

        private void OnDisable() => _hasLast = false;

        private void FixedUpdate()
        {
            var player = TrapClock.LocalPlayer;
            if (player == null) { _hasLast = false; return; }
            Vector3 position = player.transform.position;
            Vector3 velocity = _hasLast ? (position - _lastPosition) / Mathf.Max(Time.fixedDeltaTime, 1e-4f) : Vector3.zero;
            _lastPosition = position;
            _hasLast = true;
            if (Time.time < _nextTrip) return;

            Vector3 feet = position - Vector3.up * FeetBelowPivot;
            Vector3 offset = feet - transform.position;
            if (Mathf.Abs(offset.y) > .6f) return;
            offset.y = 0f;
            if (offset.sqrMagnitude > _radius * _radius) return;
            velocity.y = 0f;
            float speed = velocity.magnitude;
            // Faster than a careful step, and moving into the stone rather than away from it.
            if (speed < _minSpeed || speed > 20f || Vector3.Dot(velocity, -offset) < 0f && offset.sqrMagnitude > .01f) return;

            _nextTrip = Time.time + _cooldown;
            Vector3 fling = velocity / speed * _tripSpeed + Vector3.up * _upwardSpeed;
            var ragdoll = player.GetComponent<RagdollCharacter>();
            if (ragdoll != null && ragdoll.isActiveAndEnabled && ragdoll.FullRagdoll)
            {
                ragdoll.Knockdown(fling * _ragdollMass);
                return;
            }
            var movement = player.GetComponent<PlayerMovement>();
            if (movement != null) movement.ApplyImpulse(fling);
            var activeRagdoll = player.GetComponent<ActiveRagdoll>();
            if (activeRagdoll != null) activeRagdoll.Knockdown(fling * _ragdollMass);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, .6f, .2f, .8f);
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, _radius);
        }
#endif
    }
}
