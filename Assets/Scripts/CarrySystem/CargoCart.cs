using Unity.Netcode;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Wooden cart / sled with a flat bed and low rails that carries several crates.
    /// It is a normal <see cref="CarryableObject"/>: one player drags it by an end, two carry it, and
    /// crates are set on the bed with a gentle F placement (the bed counts as a valid floor, the rails never block it)
    /// and lifted off again with F. While nobody holds it, it runs downhill by itself. Crates resting on the bed are
    /// pulled along with it and never take impact or scrape damage from the cart (see FragileCargo).
    /// Server-side physics only; every step runs in FixedUpdate and allocates nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CargoCart : CarryableObject
    {
        [Header("Bed (passengers)")]
        [Tooltip("Trigger box over the bed; crates inside it ride along with the cart.")]
        [SerializeField] private BoxCollider _bed;
        [Tooltip("Low side/end rails. They keep crates on the bed but never block a gentle placement.")]
        [SerializeField] private Collider[] _rails;
        [Tooltip("Fraction (0..1) of a passenger's velocity difference to the cart removed each physics step.")]
        [SerializeField, Range(0f, 1f)] private float _passengerGrip = .25f;
        [Tooltip("Crates moving faster than this (m/s) relative to the cart are flying, not riding, and are left alone.")]
        [SerializeField, Min(0f)] private float _maxPassengerSpeed = 3f;

        [Header("Downhill run (nobody holding it)")]
        [Tooltip("Ground steeper than this (degrees) makes the cart run downhill.")]
        [SerializeField, Range(0f, 45f)] private float _minSlopeAngle = 5f;
        [Tooltip("Extra downhill acceleration as a fraction of gravity along the slope (on top of the low friction).")]
        [SerializeField, Min(0f)] private float _slideBoost = .35f;
        [SerializeField, Min(0f)] private float _maxSlideSpeed = 9f;
        [Tooltip("Distance below the cart's pivot that still counts as standing on the ground.")]
        [SerializeField, Min(0f)] private float _groundProbe = .55f;
        [SerializeField] private Vector3 _centerOfMass = new Vector3(0f, -.12f, 0f);

        private readonly RaycastHit[] _groundHits = new RaycastHit[8];
        private readonly Collider[] _bedOverlaps = new Collider[32];
        private readonly Rigidbody[] _seen = new Rigidbody[16];

        /// <summary>True for the cart's rail colliders (gentle placement ignores them).</summary>
        public bool IsRail(Collider collider)
        {
            if (_rails == null) return false;
            for (int i = 0; i < _rails.Length; i++)
                if (_rails[i] == collider) return true;
            return false;
        }

        protected override void Awake()
        {
            base.Awake();
            // Loaded crates raise the real centre of mass; keep the cart from tipping on slopes.
            if (_rigidbody != null) _rigidbody.centerOfMass = _centerOfMass;
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (networked && !IsServer) return;
            if (_rigidbody == null || _rigidbody.isKinematic || IsSecured || IsPlacing) return;
            if (!IsCarried) RunDownhill();
            CarryPassengers();
        }

        // Downhill run
        private void RunDownhill()
        {
            Vector3 origin = _rigidbody.position + Vector3.up * .2f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, _groundProbe + .2f,
                ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            Vector3 normal = Vector3.up;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _groundHits[i];
                if (hit.collider.attachedRigidbody == _rigidbody || hit.distance >= nearest) continue;
                // Crates riding the bed are not ground.
                if (hit.collider.attachedRigidbody != null && !hit.collider.attachedRigidbody.isKinematic) continue;
                nearest = hit.distance;
                normal = hit.normal;
            }
            if (float.IsPositiveInfinity(nearest) || normal.y < .5f) return;

            float angle = Mathf.Acos(Mathf.Clamp(normal.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (angle < _minSlopeAngle) return;

            Vector3 downhill = Vector3.ProjectOnPlane(Physics.gravity, normal);
            if (downhill.sqrMagnitude < 1e-6f) return;
            if (_rigidbody.IsSleeping()) _rigidbody.WakeUp();
            Vector3 direction = downhill.normalized;
            if (Vector3.Dot(_rigidbody.linearVelocity, direction) >= _maxSlideSpeed) return;
            _rigidbody.AddForce(downhill * _slideBoost, ForceMode.Acceleration);
        }

        // Passengers
        private void CarryPassengers()
        {
            if (_bed == null || _passengerGrip <= 0f) return;
            Transform bed = _bed.transform;
            Vector3 centre = bed.TransformPoint(_bed.center);
            Vector3 half = Vector3.Scale(_bed.size, bed.lossyScale) * .5f;
            int count = Physics.OverlapBoxNonAlloc(centre, half, _bedOverlaps, bed.rotation, ~0, QueryTriggerInteraction.Ignore);
            int seenCount = 0;
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = _bedOverlaps[i].attachedRigidbody;
                if (body == null || body == _rigidbody || body.isKinematic || IsSeen(body, seenCount)) continue;
                if (seenCount < _seen.Length) _seen[seenCount++] = body;
                if (!body.TryGetComponent(out CarryableObject crate) || crate is CargoCart ||
                    crate.IsCarried || crate.IsPlacing || crate.IsSecured) continue;

                // Horizontal speed relative to the cart under it: remove part of it so the crate rides smoothly.
                Vector3 relative = Vector3.ProjectOnPlane(body.linearVelocity - _rigidbody.GetPointVelocity(body.position), Vector3.up);
                if (relative.sqrMagnitude > _maxPassengerSpeed * _maxPassengerSpeed) continue;
                body.AddForce(-relative * _passengerGrip, ForceMode.VelocityChange);
            }
            for (int i = 0; i < seenCount; i++) _seen[i] = null;
        }

        private bool IsSeen(Rigidbody body, int seenCount)
        {
            for (int i = 0; i < seenCount; i++)
                if (_seen[i] == body) return true;
            return false;
        }
    }
}
