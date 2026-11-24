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
        private const float PlacementAcceleration = 22f;
        private const float PlacementMaxSpeed = 5.5f;
        // Braking used only for the final approach, so a fast drop still lands without a slam.
        private const float PlacementLandingDeceleration = 30f;
        private Vector3 _placementTarget;

        // Snap-pad placement: the crate glides in 3D onto a pad's slot, aligns its yaw and is then locked there.
        private struct SnapCandidate
        {
            public bool Found;
            public float Dist2;
            public Vector3 Position;
            public Quaternion Rotation;
            public Rigidbody Follow;
            public Collider Floor;
            public bool Lock;
        }
        private bool _snapFound, _snapLock;               // result of the latest TryPlacementTarget
        private Quaternion _snapRotation = Quaternion.identity;
        private Rigidbody _snapFollow;
        private bool _snappedToPad;                       // the current/last placement used a snap pad
        private bool _lockedOnSnapPad;
        private Rigidbody _placementFollowBody, _lockedFollowBody;
        private Quaternion _placementTargetRot = Quaternion.identity, _placementLocalRot = Quaternion.identity;
        private Vector3 _placementLocalTarget;
        private Quaternion _lockedRotation = Quaternion.identity;
        private Vector3 _lockedPosition;                  // world position, or follow-body local when _lockedFollowBody is set
        private Quaternion _lockedLocalRot = Quaternion.identity;
        private bool _placementLockOnLand;
        private Collider[] _placingCarrierColliders, _placingExtraColliders;
        public bool SoloAssistActive => _soloAssist.Value;
        public bool IsPlacing => _placing.Value;
        public float StaminaDrainMultiplier => SoloAssistActive ? .65f : 1f;

        private void UpdateSessionProfile(bool networked)
        {
            // This assistance is specific to the old castle level (Level01 Old); Level02 keeps its existing balance.
            if (!networked || IsServer)
            {
                bool solo = gameObject.scene.name == "Level01 Old" &&
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
            if (!TryPlacementTarget(out Vector3 target) && !TryLenientPlacementTarget(out target))
            {
                // Nowhere tidy to set it: put it down anyway, right where it is, and let it settle without damage.
                _placingCarrierColliders = _placingExtraColliders = null;
                DetachCarrier(carrierId);
                _supportProtectionEnd = Mathf.Max(_supportProtectionEnd, Time.fixedTime + .6f);
                return true;
            }
            DetachCarrier(carrierId);
            _placementTarget = target;
            _snappedToPad = _snapFound;
            _placementLockOnLand = _snapFound && _snapLock;
            _placementFollowBody = _snapFound ? _snapFollow : null;
            _placementTargetRot = _snapRotation;
            if (_placementFollowBody != null)
            {
                Quaternion inverse = Quaternion.Inverse(_placementFollowBody.rotation);
                _placementLocalTarget = inverse * (target - _placementFollowBody.position);
                _placementLocalRot = inverse * _snapRotation;
            }
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
            _snapFound = false;
            Bounds bounds = SolidBounds();
            if (bounds.size.sqrMagnitude < .001f) return false;

            // Safe pads (hay, capstan lift, checkpoint, repair, CargoSnapPad) accept the crate from beside them:
            // it glides onto the pad's slot instead of needing a perfectly flat footprint under it.
            if (CargoSnapPad.SnapLockEnabled && TrySnapPadTarget(bounds, out Vector3 snapPos, out Quaternion snapRot, out Rigidbody snapFollow, out Collider snapFloor))
            {
                target = snapPos;
                _snapFound = true;
                _snapRotation = snapRot;
                _snapFollow = snapFollow;
                if (snapFloor != null) { _placementFloors[0] = snapFloor; _placementFloorCount = 1; }
                return true;
            }
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
                    (floor.attachedRigidbody != null && !floor.attachedRigidbody.isKinematic &&
                        !floor.attachedRigidbody.TryGetComponent<CargoCart>(out _)) || // a cart bed is a valid (moving) floor
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

        // Fallback when the strict check fails (bumpy ground, slope, ledge, step up): one ray under the centre, any
        // walkable floor up to 45 degrees, a little higher or lower than the crate. The crate glides there and then
        // physics settles it, so it can be set down almost anywhere.
        private bool TryLenientPlacementTarget(out Vector3 target)
        {
            target = _rigidbody.position;
            _snapFound = false;
            Bounds box = SolidBoxYawFrame(out Quaternion yaw);
            if (box.size.sqrMagnitude < .001f) return false;
            Vector3 boxCenter = _rigidbody.position + yaw * box.center;
            float boxBottom = boxCenter.y - box.extents.y;
            Vector3 origin = new(boxCenter.x, boxBottom + .7f, boxCenter.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _placeHits, 2.6f, PlacementMask, QueryTriggerInteraction.Ignore);
            RaycastHit nearest = default;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (!Own(_placeHits[i].collider) && !IsPlacingCarrier(_placeHits[i].collider) && _placeHits[i].distance < distance)
                { nearest = _placeHits[i]; distance = nearest.distance; }
            Collider floor = nearest.collider;
            if (floor == null || nearest.normal.y < .7f ||
                floor.GetComponentInParent<CoopGame.Network.NetworkRouteHazard>() != null ||
                floor.GetComponentInParent<CoopGame.Network.RotatingLogHazard>() != null ||
                floor.GetComponentInParent<CoopGame.Player.NetworkPlayer>() != null) return false;
            target.y += nearest.point.y + .06f - boxBottom;
            if (target.y > _rigidbody.position.y + .6f) return false;
            for (int lift = 0; lift < 4; lift++, target.y += .12f)
            {
                if (!PlacementClear(target - _rigidbody.position)) continue;
                _placementFloors[0] = floor;
                _placementFloorCount = 1;
                return true;
            }
            return false;
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
                if (!Own(_placeOverlaps[i]) && !IsPlacingCarrier(_placeOverlaps[i]) && !IsCartRail(_placeOverlaps[i])) return false;
            return true;
        }

        // A cart's low rails keep crates on the bed; they must not stop a crate being set down beside them.
        private static bool IsCartRail(Collider collider) =>
            collider != null && collider.attachedRigidbody != null &&
            collider.attachedRigidbody.TryGetComponent(out CargoCart cart) && cart.IsRail(collider);

        private bool IsPlacingCarrier(Collider collider) =>
            (_placingCarrierColliders != null && System.Array.IndexOf(_placingCarrierColliders, collider) >= 0) ||
            (_placingExtraColliders != null && System.Array.IndexOf(_placingExtraColliders, collider) >= 0);

        private bool TickGentlePlacement()
        {
            if (!IsPlacing) return false;
            bool valid = !IsSecured && _activeCarriers.Count == 0 && Time.fixedTime < _placementEnd;
            for (int i = 0; i < _placementFloorCount; i++)
                valid &= _placementFloors[i] != null && _placementFloors[i].enabled && _placementFloors[i].gameObject.activeInHierarchy;
            // A pad on a moving body (capstan deck, squashing hay) carries the slot with it during the glide.
            if (_snappedToPad && _placementFollowBody != null)
            {
                _placementTarget = _placementFollowBody.position + _placementFollowBody.rotation * _placementLocalTarget;
                _placementTargetRot = _placementFollowBody.rotation * _placementLocalRot;
            }
            float remaining = Vector3.Distance(_rigidbody.position, _placementTarget);
            float landingLimit = Mathf.Sqrt(2f * PlacementLandingDeceleration * remaining) + .3f;
            _placementSpeed = Mathf.Min(_placementSpeed + PlacementAcceleration * Time.fixedDeltaTime,
                PlacementMaxSpeed, landingLimit);
            Vector3 next = Vector3.MoveTowards(_rigidbody.position, _placementTarget, _placementSpeed * Time.fixedDeltaTime);
            // A snap pad ignores its own rails and edges; only other cargo can block the slot.
            valid &= _snappedToPad ? PlacementClearOfCargo(next - _rigidbody.position) : PlacementClear(next - _rigidbody.position);
            if (!valid) { EndPlacement(false); return false; }
            _rigidbody.useGravity = false;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.MovePosition(next);
            bool turned = true;
            if (_snappedToPad)
            {
                float angle = Quaternion.Angle(_rigidbody.rotation, _placementTargetRot);
                // Ease-out turn: fast when far off, slowing into the slot.
                float step = Mathf.Max(120f, angle * 8f) * Time.fixedDeltaTime;
                Quaternion nextRot = angle <= step ? _placementTargetRot : Quaternion.RotateTowards(_rigidbody.rotation, _placementTargetRot, step);
                _rigidbody.MoveRotation(nextRot);
                turned = angle <= .5f;
            }
            if (turned && (next - _placementTarget).sqrMagnitude < .000001f) EndPlacement(true);
            return true;
        }

        private void EndPlacement(bool completed)
        {
            _placing.Value = false;
            _placingCarrierColliders = _placingExtraColliders = null;
            _rigidbody.useGravity = true;
            _supportProtectionEnd = completed ? Time.fixedTime + .35f : -1f;
            if (!completed) { _placementFloorCount = 0; _snappedToPad = false; }
            if (completed && _snappedToPad && _placementLockOnLand) BeginSnapLock();
            if (completed) OnPlacementCompleted();
        }

        private void BeginSnapLock()
        {
            _lockedOnSnapPad = true;
            _lockedFollowBody = _placementFollowBody;
            if (_lockedFollowBody != null)
            {
                Quaternion inverse = Quaternion.Inverse(_lockedFollowBody.rotation);
                _lockedPosition = inverse * (_placementTarget - _lockedFollowBody.position);
                _lockedLocalRot = inverse * _placementTargetRot;
            }
            else
            {
                _lockedPosition = _placementTarget;
                _lockedRotation = _placementTargetRot;
            }
            _rigidbody.constraints = _lockedFollowBody == null
                ? RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ
                : RigidbodyConstraints.FreezeRotation;
        }

        /// <summary>Releases the snap-pad lock (pickup, throw, respawn, delivery).</summary>
        private void ClearSnapLock()
        {
            _snappedToPad = false;
            if (!_lockedOnSnapPad) return;
            _lockedOnSnapPad = false;
            _lockedFollowBody = null;
            if (_rigidbody != null) _rigidbody.constraints = RigidbodyConstraints.None;
        }

        // Server FixedUpdate hook while the crate sits locked on a snap pad. Y stays free so hay still squashes and
        // the capstan deck still carries the crate up and down; X/Z and yaw stay dead-centre on the slot.
        private bool TickSnapLock()
        {
            if (!_lockedOnSnapPad) return false;
            if (_activeCarriers.Count > 0 || IsSecured || _throwPending || _rigidbody.isKinematic)
            {
                ClearSnapLock();
                return false;
            }
            if (_lockedFollowBody != null)
            {
                Rigidbody follow = _lockedFollowBody;
                Vector3 slot = follow.position + follow.rotation * _lockedPosition;
                Vector3 position = _rigidbody.position;
                if ((new Vector2(slot.x - position.x, slot.z - position.z)).sqrMagnitude > .000004f)
                    _rigidbody.MovePosition(new Vector3(slot.x, position.y, slot.z));
                Quaternion rot = follow.rotation * _lockedLocalRot;
                if (Quaternion.Angle(_rigidbody.rotation, rot) > .25f) _rigidbody.MoveRotation(rot);
                Vector3 velocity = _rigidbody.linearVelocity;
                velocity.x = velocity.z = 0f;
                _rigidbody.linearVelocity = velocity;
            }
            _rigidbody.useGravity = true;
            _rigidbody.angularVelocity = Vector3.zero;
            return true;
        }

        // Only other cargo blocks a snap slot; the pad's rails, side rails and bale edges never do.
        private bool PlacementClearOfCargo(Vector3 offset)
        {
            Bounds box = SolidBoxYawFrame(out Quaternion yaw);
            int count = Physics.OverlapBoxNonAlloc(_rigidbody.position + yaw * box.center + offset,
                Vector3.Max(box.extents - Vector3.one * .06f, Vector3.one * .01f),
                _placeOverlaps, yaw, PlacementMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider other = _placeOverlaps[i];
                if (Own(other) || IsPlacingCarrier(other) || other.attachedRigidbody == null) continue;
                if (other.attachedRigidbody.GetComponent<CarryableObject>() != null) return false;
            }
            return true;
        }

        private static bool IsPadEdge(Collider collider)
        {
            string name = collider.name;
            return name.IndexOf("Rail", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("FrontBoard", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Top of the walkable surface under a point, ignoring the crate, the carrier, rails and other loose bodies.
        private bool TrySurfaceBelow(Vector3 from, float distance, out float topY, out Collider floor)
        {
            topY = 0f; floor = null;
            int count = Physics.RaycastNonAlloc(from, Vector3.down, _placeHits, distance, PlacementMask, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _placeHits[i];
                Collider col = hit.collider;
                if (col == null || hit.distance >= best || Own(col) || IsPlacingCarrier(col) || hit.normal.y < .7f || IsPadEdge(col)) continue;
                if (col.GetComponentInParent<CoopGame.Player.NetworkPlayer>() != null) continue;
                Rigidbody body = col.attachedRigidbody;
                if (body != null && !body.isKinematic) continue;
                best = hit.distance; topY = hit.point.y; floor = col;
            }
            return floor != null;
        }

        private void ConsiderSnap(ref SnapCandidate best, Bounds bounds, Vector3 center, Quaternion padRotation,
            float topY, float radius, float vertical, Rigidbody follow, Collider floor, bool lockOn, bool align)
        {
            float dx = bounds.center.x - center.x, dz = bounds.center.z - center.z;
            float dist2 = dx * dx + dz * dz;
            if (dist2 > radius * radius || (best.Found && dist2 >= best.Dist2)) return;
            if (Mathf.Abs(bounds.min.y - topY) > vertical) return;
            float pivotAboveBottom = _rigidbody.position.y - bounds.min.y;
            Quaternion rotation = Quaternion.identity;
            float currentYaw = _rigidbody.rotation.eulerAngles.y;
            if (align)
            {
                // Nearest allowed yaw: quarter turns, or half turns for a long crate so its length follows the pad's.
                float step = GetComponent<CarryGripEnds>() != null ? 180f : 90f;
                float padYaw = padRotation.eulerAngles.y;
                float turns = Mathf.Round(Mathf.DeltaAngle(padYaw, currentYaw) / step);
                rotation = Quaternion.Euler(0f, padYaw + turns * step, 0f);
            }
            else rotation = Quaternion.Euler(0f, currentYaw, 0f);
            best.Found = true;
            best.Dist2 = dist2;
            best.Position = new Vector3(center.x, topY + pivotAboveBottom + .02f, center.z);
            best.Rotation = rotation;
            best.Follow = follow;
            best.Floor = floor;
            best.Lock = lockOn;
        }

        /// <summary>
        /// Finds the nearest safe pad (CargoSnapPad, hay pad, capstan lift, checkpoint pad, repair station) in range of
        /// the crate and returns the slot to glide to: pad centre, level yaw-aligned rotation, and the moving body the
        /// slot rides on (if any). Allocation-free.
        /// </summary>
        private bool TrySnapPadTarget(Bounds bounds, out Vector3 targetPos, out Quaternion targetRot, out Rigidbody followBody, out Collider floorCol)
        {
            targetPos = _rigidbody.position; targetRot = _rigidbody.rotation; followBody = null; floorCol = null;
            SnapCandidate best = default;

            var pads = CargoSnapPad.Active;
            for (int i = 0; i < pads.Count; i++)
            {
                CargoSnapPad pad = pads[i];
                if (pad == null) continue;
                Transform point = pad.SnapPoint;
                Vector3 c = point.position;
                if (!TrySurfaceBelow(c + Vector3.up * 1.5f, 4.5f, out float top, out Collider floor)) { top = c.y; floor = null; }
                Rigidbody follow = floor != null && floor.attachedRigidbody != null ? floor.attachedRigidbody : null;
                ConsiderSnap(ref best, bounds, c, point.rotation, top, pad.SnapRadius, 2.6f, follow, floor, pad.LockOnLand, pad.AlignRotation);
            }

            var hays = HayCushion.Active;
            for (int i = 0; i < hays.Count; i++)
            {
                Rigidbody bale = hays[i] != null ? hays[i].TopBale : null;
                if (bale == null) continue;
                Collider col = bale.GetComponent<Collider>();
                float top = col != null && col.enabled ? col.bounds.max.y : bale.position.y + bale.transform.lossyScale.y * .5f;
                ConsiderSnap(ref best, bounds, bale.position, bale.rotation, top, 3f, 2.5f, bale, col, true, true);
            }

            var lifts = CoopGame.Network.CoopCapstanLift.Active;
            for (int i = 0; i < lifts.Count; i++)
            {
                Rigidbody deck = lifts[i] != null ? lifts[i].Platform : null;
                if (deck == null) continue;
                Collider col = deck.GetComponent<Collider>();
                float top = col != null && col.enabled ? col.bounds.max.y : deck.position.y + deck.transform.lossyScale.y * .5f;
                ConsiderSnap(ref best, bounds, deck.position, deck.rotation, top, 3.4f, 2.6f, deck, col, true, true);
            }

            var checkpoints = CoopGame.Network.CheckpointPad.Active;
            for (int i = 0; i < checkpoints.Count; i++)
            {
                CoopGame.Network.CheckpointPad pad = checkpoints[i];
                if (pad == null) continue;
                Transform point = pad.CargoPoint;
                Vector3 c = point.position;
                if (!TrySurfaceBelow(new Vector3(c.x, c.y + 1.5f, c.z), 4.5f, out float top, out Collider floor))
                { top = pad.transform.position.y + .08f; floor = null; }
                Rigidbody follow = floor != null && floor.attachedRigidbody != null ? floor.attachedRigidbody : null;
                ConsiderSnap(ref best, bounds, c, point.rotation, top, 3.4f, 2.6f, follow, floor, true, true);
            }

            var stations = CoopGame.Network.RepairStation.Active;
            for (int i = 0; i < stations.Count; i++)
            {
                CoopGame.Network.RepairStation station = stations[i];
                if (station == null || !station.IsActiveInBranch || station.IsUsed) continue;
                Transform point = station.PadTransform;
                Vector3 c = point.position;
                if (!TrySurfaceBelow(c + Vector3.up * 1.5f, 4.5f, out float top, out Collider floor)) { top = c.y + .08f; floor = null; }
                Rigidbody follow = floor != null && floor.attachedRigidbody != null ? floor.attachedRigidbody : null;
                ConsiderSnap(ref best, bounds, c, point.rotation, top, 3f, 2.6f, follow, floor, true, true);
            }

            if (!best.Found) return false;
            targetPos = best.Position; targetRot = best.Rotation; followBody = best.Follow; floorCol = best.Floor;
            _snapLock = best.Lock;
            // Only another crate in the slot refuses the placement.
            return PlacementClearOfCargo(targetPos - _rigidbody.position);
        }

        /// <summary>Server: a gentle F placement finished on its floor.</summary>
        protected virtual void OnPlacementCompleted() { }

        /// <summary>True when the floor the crate was just placed on is a hay bale or a cargo cart bed.</summary>
        protected bool PlacedOnCushion() => PlacedOnSafePad();

        /// <summary>True when the crate was set on a safe pad: a snap pad, hay, a cargo cart, the capstan lift,
        /// a checkpoint pad or a repair station. Safe pads never cost HP.</summary>
        protected bool PlacedOnSafePad()
        {
            if (_snappedToPad) return true;
            for (int i = 0; i < _placementFloorCount; i++)
            {
                Collider floor = _placementFloors[i];
                if (floor == null) continue;
                if (HayCushion.IsCushion(floor)) return true;
                Rigidbody body = floor.attachedRigidbody;
                if (body != null && body.TryGetComponent<CargoCart>(out _)) return true;
                if (!CargoSnapPad.SnapLockEnabled) continue; // lift, checkpoint, repair and snap pads cost HP while the switch is off
                if (CoopGame.Network.CoopCapstanLift.IsPlatformBody(body)) return true;
                if (floor.GetComponentInParent<CoopGame.Network.CoopCapstanLift>() != null ||
                    floor.GetComponentInParent<CoopGame.Network.CheckpointPad>() != null ||
                    floor.GetComponentInParent<CoopGame.Network.RepairStation>() != null ||
                    floor.GetComponentInParent<CargoSnapPad>() != null) return true;
            }
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
