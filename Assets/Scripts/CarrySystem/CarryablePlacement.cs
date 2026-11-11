using Unity.Netcode;
using UnityEngine;
using CoopGame.Obstacles;

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
        private Collider[] _placingCarrierColliders, _placingExtraColliders;
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

        // Called only in PlayerCarry.FixedUpdate after server ownership validation (and by the full-physics player,
        // whose ragdoll limbs live outside the player hierarchy and are passed as extraCarrierColliders).
        public virtual bool TryPlaceGently(ulong carrierId, Collider[] extraCarrierColliders = null)
        {
            if ((IsSpawned && !IsServer) || IsSecured || IsPlacing || !HasCarrier(carrierId)) return false;
            _placingExtraColliders = extraCarrierColliders;
            if (_activeCarriers.Count > 1)
            {
                // When one co-op carrier sets their side down first, capture the floor below and protect
                // the crate briefly so the remaining carrier's transition never slams or scrapes for damage.
                _placingCarrierColliders = _activeCarriers.TryGetValue(carrierId, out var coPlacer) ? coPlacer.PlayerColliders : null;
                if (TryPlacementTarget(out _))
                    _supportProtectionEnd = Mathf.Max(_supportProtectionEnd, Time.fixedTime + .65f);
                _placingCarrierColliders = _placingExtraColliders = null;
                DetachCarrier(carrierId);
                return true;
            }
            // The carrier setting it down stands against the cargo; their own body must not
            // block the spot (their collision with it stays ignored until they step apart).
            _placingCarrierColliders = _activeCarriers.TryGetValue(carrierId, out var placer) ? placer.PlayerColliders : null;
            if (!TryPlacementTarget(out Vector3 target)) { _placingCarrierColliders = _placingExtraColliders = null; return false; }
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

        // Full-physics players' ragdoll limbs are neither floor nor an obstacle for a set-down (an arm under the
        // crate used to make the placement fail).
        private static int PlacementMask
        {
            get
            {
                int limbs = LayerMask.NameToLayer(CoopGame.Player.RagdollCharacter.LimbLayerName);
                return limbs >= 0 ? ~(1 << limbs) : ~0;
            }
        }

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

        // The crate's solid box in a frame that turns with the crate around the world up axis only. Sampling the
        // world AABB instead made a crate turned at an angle test points well outside its real footprint, so it
        // could never be set down on a narrow surface such as a cart bed placed at an angle in the level.
        private Bounds SolidBoxYawFrame(out Quaternion yaw)
        {
            yaw = Quaternion.Euler(0f, _rigidbody.rotation.eulerAngles.y, 0f);
            Quaternion inverse = Quaternion.Inverse(yaw);
            Vector3 origin = _rigidbody.position;
            Bounds local = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            for (int i = 0; i < _ownColliders.Length; i++)
            {
                Collider col = _ownColliders[i];
                if (col == null || !col.enabled || col.isTrigger || !Own(col)) continue;
                Vector3 c, e;
                Transform t;
                if (col is BoxCollider box) { c = box.center; e = box.size * .5f; t = box.transform; }
                else { c = col.bounds.center; e = col.bounds.extents; t = null; }
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = c + new Vector3((k & 1) == 0 ? -e.x : e.x, (k & 2) == 0 ? -e.y : e.y, (k & 4) == 0 ? -e.z : e.z);
                    if (t != null) corner = t.TransformPoint(corner);
                    Vector3 p = inverse * (corner - origin);
                    if (first) { local = new Bounds(p, Vector3.zero); first = false; }
                    else local.Encapsulate(p);
                }
            }
            return local;
        }

        private bool TryPlacementTarget(out Vector3 target)
        {
            target = _rigidbody.position;
            _placementFloorCount = 0;
            Bounds bounds = SolidBounds();
            if (bounds.size.sqrMagnitude < .001f) return false;
            Bounds box = SolidBoxYawFrame(out Quaternion yaw);
            Vector3 boxCenter = _rigidbody.position + yaw * box.center;
            float boxBottom = boxCenter.y - box.extents.y;

            // Direct check for See-Saw plank support:
            // When standing on or placing over a tilting See-Saw plank, standard flat floor corner raycasts
            // fail because of the incline (highest - lowest > 0.12m) and tilted normals.
            // We project directly to the plank top surface if a SeeSawPlank is beneath.
            if (TrySeeSawPlankTarget(bounds, out target))
            {
                return true;
            }

            float highest = float.NegativeInfinity, lowest = float.PositiveInfinity;
            // All four inset corners and the centre must be supported. A narrow ledge is not a safe placement.
            for (int sample = 0; sample < 5; sample++)
            {
                Vector3 origin = new(boxCenter.x, boxBottom + .08f, boxCenter.z);
                if (sample > 0)
                    origin += yaw * new Vector3(((sample & 1) == 0 ? -1 : 1) * box.extents.x * .85f, 0f,
                        ((sample & 2) == 0 ? -1 : 1) * box.extents.z * .85f);
                int count = Physics.RaycastNonAlloc(origin, Vector3.down, _placeHits, 1.58f, PlacementMask, QueryTriggerInteraction.Ignore);
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
            target.y += highest + .025f - boxBottom;
            if (target.y > _rigidbody.position.y + .1f || _rigidbody.position.y - target.y > 1.5f) return false;
            return PlacementClear(target - _rigidbody.position);
        }

        private bool TrySeeSawPlankTarget(Bounds bounds, out Vector3 target)
        {
            target = _rigidbody.position;
            Vector3 origin = new(bounds.center.x, bounds.min.y + .15f, bounds.center.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _placeHits, 2.5f, PlacementMask, QueryTriggerInteraction.Ignore);
            if (count == 0) return false;

            RaycastHit nearest = default;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (!Own(_placeHits[i].collider) && !IsPlacingCarrier(_placeHits[i].collider) && _placeHits[i].distance < distance)
                {
                    nearest = _placeHits[i];
                    distance = nearest.distance;
                }
            }

            Collider floor = nearest.collider;
            if (floor == null) return false;

            SeeSawPlank plank = floor.GetComponentInParent<SeeSawPlank>();
            if (plank == null || !plank.IsWithinPlankBounds(nearest.point)) return false;

            _placementFloors[0] = floor;
            _placementFloorCount = 1;

            target = plank.ProjectToPlankSurface(nearest.point, bounds.extents.y + 0.02f);
            if (target.y > _rigidbody.position.y + .25f || _rigidbody.position.y - target.y > 2.2f) return false;

            // Check clearance above plank surface using slightly inset bounds
            Vector3 offset = target - _rigidbody.position;
            Vector3 clearHalfExtents = Vector3.Max(bounds.extents * 0.85f, Vector3.one * 0.05f);
            int overlapCount = Physics.OverlapBoxNonAlloc(bounds.center + offset, clearHalfExtents, _placeOverlaps, plank.PlankTransform.rotation, PlacementMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlapCount; i++)
            {
                if (!Own(_placeOverlaps[i]) && !IsPlacingCarrier(_placeOverlaps[i]) && _placeOverlaps[i] != floor && !_placeOverlaps[i].transform.IsChildOf(plank.PlankTransform))
                    return false;
            }

            return true;
        }

        private bool PlacementClear(Vector3 offset)
        {
            Bounds box = SolidBoxYawFrame(out Quaternion yaw);
            int count = Physics.OverlapBoxNonAlloc(_rigidbody.position + yaw * box.center + offset,
                Vector3.Max(box.extents - Vector3.one * .015f, Vector3.one * .01f),
                _placeOverlaps, yaw, PlacementMask, QueryTriggerInteraction.Ignore);
            if (count == _placeOverlaps.Length) return false;
            for (int i = 0; i < count; i++)
                if (!Own(_placeOverlaps[i]) && !IsPlacingCarrier(_placeOverlaps[i])) return false;
            return true;
        }

        private bool IsPlacingCarrier(Collider collider) =>
            (_placingCarrierColliders != null && System.Array.IndexOf(_placingCarrierColliders, collider) >= 0) ||
            (_placingExtraColliders != null && System.Array.IndexOf(_placingExtraColliders, collider) >= 0);

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
            valid &= PlacementClear(next - _rigidbody.position);
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
            _placingCarrierColliders = _placingExtraColliders = null;
            _rigidbody.useGravity = true;
            _supportProtectionEnd = completed ? Time.fixedTime + .35f : -1f;
            if (!completed) _placementFloorCount = 0;
            if (completed) OnPlacementCompleted();
        }

        /// <summary>Server: a gentle F placement finished on its floor.</summary>
        protected virtual void OnPlacementCompleted() { }

        /// <summary>True when the floor the crate was just placed on is a hay bale.</summary>
        protected bool PlacedOnCushion()
        {
            for (int i = 0; i < _placementFloorCount; i++)
                if (_placementFloors[i] != null && HayCushion.IsCushion(_placementFloors[i])) return true;
            return false;
        }

        protected bool IsGentleSupportContact(Collider other, Vector3 normal, Vector3 point)
        {
            if (_throwPending || Time.fixedTime >= _supportProtectionEnd) return false;
            for (int i = 0; i < _placementFloorCount; i++)
                if (_placementFloors[i] == other) return true;
            if (other != null && other.GetComponentInParent<SeeSawPlank>() != null) return true;
            if (normal.y < .8660254f || point.y > SolidBounds().min.y + .12f) return false;
            return false;
        }
    }
}
