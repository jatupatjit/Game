using Unity.Netcode;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    public partial class CarryableObject
    {
        private readonly NetworkVariable<bool> _soloAssist = new(false);
        private readonly NetworkVariable<bool> _placing = new(false);
        private readonly RaycastHit[] _placeHits = new RaycastHit[32];
        private readonly Collider[] _placeOverlaps = new Collider[64];
        private readonly Collider[] _placementFloors = new Collider[5];
        private int _placementFloorCount;
        private float _baseMass, _placementEnd, _supportProtectionEnd, _placementSpeed;
        private const float PlacementAcceleration = 14f;
        private const float PlacementMaxSpeed = 4f;
        // Braking used only for the final approach, so a fast drop still lands without a slam.
        private const float PlacementLandingDeceleration = 30f;
        private Vector3 _placementTarget;
        public bool SoloAssistActive => _soloAssist.Value;
        public bool IsPlacing => _placing.Value;
        public float StaminaDrainMultiplier => SoloAssistActive ? .65f : 1f;

        private void UpdateSessionProfile(bool networked)
        {
            // This assistance is specific to Level01; Level02 keeps its existing balance.
            if (!networked || IsServer)
            {
                bool solo = gameObject.scene.name == "Level01" &&
                    (!networked || CoopGame.Network.SessionPlayers.Count(NetworkManager) == 1);
                if (_soloAssist.Value != solo) _soloAssist.Value = solo;
            }
            float mass = _baseMass * (SoloAssistActive ? .5f : 1f);
            if (!Mathf.Approximately(_rigidbody.mass, mass)) _rigidbody.mass = mass;
        }

        // Called only in PlayerCarry.FixedUpdate after server ownership validation.
        public bool TryPlaceGently(ulong carrierId)
        {
            if ((IsSpawned && !IsServer) || IsSecured || IsPlacing || !HasCarrier(carrierId)) return false;
            if (_activeCarriers.Count > 1) { DetachCarrier(carrierId); return true; }
            if (!TryPlacementTarget(out Vector3 target)) return false;
            DetachCarrier(carrierId);
            _placementTarget = target;
            _placementSpeed = 0f;
            _placing.Value = true;
            _throwPending = false;
            _placementEnd = Time.fixedTime + 2f;
            _supportProtectionEnd = _placementEnd + .35f;
            return true;
        }

        private bool Own(Collider collider) => collider != null && collider.attachedRigidbody == _rigidbody;

        private Bounds SolidBounds()
        {
            Bounds bounds = new Bounds(_rigidbody.position, Vector3.zero);
            bool first = true;
            for (int i = 0; i < _ownColliders.Length; i++)
            {
                Collider col = _ownColliders[i];
                if (col == null || !col.enabled || col.isTrigger || !Own(col)) continue;
                if (first) { bounds = col.bounds; first = false; }
                else bounds.Encapsulate(col.bounds);
            }
            return bounds;
        }

        private bool TryPlacementTarget(out Vector3 target)
        {
            target = _rigidbody.position;
            _placementFloorCount = 0;
            Bounds bounds = SolidBounds();
            if (bounds.size.sqrMagnitude < .001f) return false;
            float highest = float.NegativeInfinity, lowest = float.PositiveInfinity;
            // All four inset corners and the centre must be supported. A narrow ledge is not a safe placement.
            for (int sample = 0; sample < 5; sample++)
            {
                Vector3 origin = new(bounds.center.x, bounds.min.y + .08f, bounds.center.z);
                if (sample > 0)
                {
                    origin.x += ((sample & 1) == 0 ? -1 : 1) * bounds.extents.x * .85f;
                    origin.z += ((sample & 2) == 0 ? -1 : 1) * bounds.extents.z * .85f;
                }
                int count = Physics.RaycastNonAlloc(origin, Vector3.down, _placeHits, 1.58f, ~0, QueryTriggerInteraction.Ignore);
                if (count == _placeHits.Length) return false;
                RaycastHit nearest = default;
                float distance = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                    if (!Own(_placeHits[i].collider) && _placeHits[i].distance < distance)
                    { nearest = _placeHits[i]; distance = nearest.distance; }
                Collider floor = nearest.collider;
                if (floor == null || nearest.normal.y < .8660254f ||
                    (floor.attachedRigidbody != null && !floor.attachedRigidbody.isKinematic) ||
                    floor.GetComponentInParent<CoopGame.Network.NetworkRouteHazard>() != null ||
                    floor.GetComponentInParent<CoopGame.Network.RotatingLogHazard>() != null ||
                    floor.GetComponentInParent<CoopGame.Player.NetworkPlayer>() != null) return false;
                highest = Mathf.Max(highest, nearest.point.y);
                lowest = Mathf.Min(lowest, nearest.point.y);
                bool known = false;
                for (int i = 0; i < _placementFloorCount; i++) known |= _placementFloors[i] == floor;
                if (!known) _placementFloors[_placementFloorCount++] = floor;
            }
            // Keep the existing crate orientation. Reject support that would leave a corner hanging.
            if (highest - lowest > .12f) return false;
            target.y += highest + .025f - bounds.min.y;
            if (target.y > _rigidbody.position.y + .1f || _rigidbody.position.y - target.y > 1.5f) return false;
            return PlacementClear(bounds, target - _rigidbody.position);
        }

        private bool PlacementClear(Bounds bounds, Vector3 offset)
        {
            int count = Physics.OverlapBoxNonAlloc(bounds.center + offset,
                Vector3.Max(bounds.extents - Vector3.one * .015f, Vector3.one * .01f),
                _placeOverlaps, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            if (count == _placeOverlaps.Length) return false;
            for (int i = 0; i < count; i++)
                if (!Own(_placeOverlaps[i])) return false;
            return true;
        }

        private bool TickGentlePlacement()
        {
            if (!IsPlacing) return false;
            bool valid = !IsSecured && _activeCarriers.Count == 0 && Time.fixedTime < _placementEnd;
            for (int i = 0; i < _placementFloorCount; i++)
                valid &= _placementFloors[i] != null && _placementFloors[i].enabled && _placementFloors[i].gameObject.activeInHierarchy;
            float remaining = Vector3.Distance(_rigidbody.position, _placementTarget);
            float landingLimit = Mathf.Sqrt(2f * PlacementLandingDeceleration * remaining) + .3f;
            _placementSpeed = Mathf.Min(_placementSpeed + PlacementAcceleration * Time.fixedDeltaTime,
                PlacementMaxSpeed, landingLimit);
            Vector3 next = Vector3.MoveTowards(_rigidbody.position, _placementTarget, _placementSpeed * Time.fixedDeltaTime);
            valid &= PlacementClear(SolidBounds(), next - _rigidbody.position);
            if (!valid) { EndPlacement(false); return false; }
            _rigidbody.useGravity = false;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.MovePosition(next);
            if ((next - _placementTarget).sqrMagnitude < .000001f) EndPlacement(true);
            return true;
        }

        private void EndPlacement(bool completed)
        {
            _placing.Value = false;
            _rigidbody.useGravity = true;
            _supportProtectionEnd = completed ? Time.fixedTime + .35f : -1f;
            if (!completed) _placementFloorCount = 0;
        }

        protected bool IsGentleSupportContact(Collider other, Vector3 normal, Vector3 point)
        {
            if (_activeCarriers.Count > 0 || _throwPending || Time.fixedTime >= _supportProtectionEnd ||
                normal.y < .8660254f || point.y > SolidBounds().min.y + .12f) return false;
            for (int i = 0; i < _placementFloorCount; i++)
                if (_placementFloors[i] == other) return true;
            return false;
        }
    }
}
