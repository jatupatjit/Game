using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// Lift key, catching and the aimed throw.
    /// F: a tap (released within <see cref="_catchHoldTime"/>) lifts the aimed crate; holding it readies a catch: the
    /// body glows on every screen and a crate thrown by anyone that comes within <see cref="_catchRadius"/> is taken
    /// straight into the hands (the normal lift request, so the server checks it as usual; a crate caught before it
    /// lands takes no throw damage, see <see cref="FragileCargo"/>).
    /// Q while carrying: holding aims and charges (min to max speed over <see cref="_throwChargeTime"/>) and shows the
    /// path the crate will fly (<see cref="ThrowPreview"/>, this player's screen only); releasing throws, F cancels.
    /// F while carrying (not aiming) sets the crate down; the mouse buttons only climb.
    /// </summary>
    public sealed partial class RagdollCharacter
    {
        [Header("Catch (hold F)")]
        [Tooltip("Seconds F must be held before it readies a catch instead of lifting on release")]
        [SerializeField] private float _catchHoldTime = .25f;
        [Tooltip("A thrown crate this close to the chest (m) is caught")]
        [SerializeField] private float _catchRadius = 1.8f;
        [SerializeField] private Color _catchGlowColor = new Color(1f, .8f, .1f, 1f);

        // Owner-written so every screen draws the glow; the server only reads it.
        private readonly NetworkVariable<bool> _catchReadyNet = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private bool _catchReadyLocal, _carryTapReleased;
        private float _carryPressedAt = -1f;
        private float _nextPickupAllowedTime;
        private readonly Collider[] _catchNear = new Collider[16];
        // Your own throw cannot be caught straight back out of the air for a moment.
        private const float OwnThrowCatchDelay = .6f;
        private CarryableObject _lastThrown;
        private float _lastThrownAt;

        private bool _aimingThrow;
        private float _aimStart;
        private ThrowPreview _throwPreview;
        private CatchGlow _catchGlow;

        private bool CatchReady => IsLocal ? _catchReadyLocal : _catchReadyNet.Value;

        // Where this player's throw will land / is landing, for every screen (xyz = point, w: 0 none, 1 aiming,
        // 2 thrown and still in the air). Owner-written; team-mates draw the ring so they can run there and catch.
        private readonly NetworkVariable<Vector4> _throwMarkNet = new NetworkVariable<Vector4>(Vector4.zero,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private const float ThrowMarkMaxFlight = 4f;
        private Vector3 _throwMarkPoint;
        private ThrowPreview _teamMark;

        [Header("Hit by a thrown crate (not holding F)")]
        [Tooltip("A flying crate this close to the chest (m) knocks the player back")]
        [SerializeField] private float _crateHitRadius = .75f;
        [Tooltip("The crate must fly at least this fast (m/s) to knock someone over")]
        [SerializeField] private float _crateHitMinSpeed = 4f;
        [Tooltip("Knock-back speed (m/s) at the slowest and fastest hit")]
        [SerializeField] private Vector2 _crateHitPush = new Vector2(3f, 6f);
        private CarryableObject _flyingNear;
        private Vector3 _flyingNearPos;
        private float _flyingNearTime, _crateHitCooldownUntil;

        /// <summary>Owner, Update: a thrown crate that reaches this player while they are not readying a catch (hold F)
        /// knocks them back along its flight. The crate's speed is measured here from its drawn position, so it works
        /// the same on the host and on clients.</summary>
        private void UpdateThrownCrateHit()
        {
            if (_physicsRoot == null || IsKnockedDown || _climbing || Time.time < _crateHitCooldownUntil) { _flyingNear = null; return; }
            Vector3 chest = _bodies[Hips].position + Vector3.up * .3f;
            int count = Physics.OverlapSphereNonAlloc(chest, 3f, _catchNear, ~(1 << _limbLayer), QueryTriggerInteraction.Ignore);
            CarryableObject nearest = null;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var c = _catchNear[i].GetComponentInParent<CarryableObject>();
                if (c == null || !c.InFlight || c == _carry) continue;
                if (c == _lastThrown && Time.time - _lastThrownAt < OwnThrowCatchDelay) continue;
                float d = (_catchNear[i].ClosestPoint(chest) - chest).sqrMagnitude;
                if (d >= best) continue;
                best = d; nearest = c;
            }
            if (nearest == null) { _flyingNear = null; return; }
            Vector3 pos = nearest.transform.position;
            bool tracked = nearest == _flyingNear && Time.time > _flyingNearTime;
            Vector3 velocity = tracked ? (pos - _flyingNearPos) / (Time.time - _flyingNearTime) : Vector3.zero;
            if (tracked)
            {
                // Swept check: at a low frame rate a fast crate can jump past the chest between two frames, so also
                // measure the chest's distance to the path the crate's surface travelled since last frame.
                Vector3 surfaceOffset = pos - (chest + (best > 0f ? Mathf.Sqrt(best) : 0f) * (pos - chest).normalized);
                Vector3 seg = pos - _flyingNearPos;
                float t = seg.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(chest - _flyingNearPos, seg) / seg.sqrMagnitude) : 1f;
                float swept = Mathf.Max(0f, Vector3.Distance(chest, _flyingNearPos + seg * t) - surfaceOffset.magnitude);
                best = Mathf.Min(best, swept * swept);
            }
            _flyingNear = nearest; _flyingNearPos = pos; _flyingNearTime = Time.time;
            if (!tracked || CatchReady || _carry != null) return; // holding F catches it instead
            float speed = velocity.magnitude;
            if (speed < _crateHitMinSpeed || best > _crateHitRadius * _crateHitRadius) return;
            Vector3 dir = Flat(velocity);
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : -transform.forward;
            float push = Mathf.Lerp(_crateHitPush.x, _crateHitPush.y, Mathf.InverseLerp(_crateHitMinSpeed, 20f, speed));
            _crateHitCooldownUntil = Time.time + 1.5f;
            _flyingNear = null;
            Knockdown((dir * push + Vector3.up * 1.5f) * 45f);
        }

        /// <summary>Owner: holding Q to aim a throw (the charge bar shows while true).</summary>
        public bool AimingThrow => _aimingThrow;
        /// <summary>Owner: where the aimed throw would land (the camera keeps it on screen while aiming up).</summary>
        public bool TryGetThrowLanding(out Vector3 point)
        {
            point = _throwMarkPoint;
            return _aimingThrow && _throwPreview != null && _throwPreview.Visible && _throwPreview.Landed;
        }

        /// <summary>Owner: 0..1 throw charge while aiming.</summary>
        public float ThrowChargeNow => _aimingThrow ? ThrowCharge : 0f;

        /// <summary>Owner, Update: reads F into a tap (lift) or a held catch.</summary>
        private void UpdateCatchInput(bool canAct)
        {
            _carryTapReleased = false;
            if (_input.CarryPressed)
            {
                // When already carrying an object, pressing F is intent to PLACE it down, not to lift.
                if (_carry != null) _carryPressedAt = -1f;
                else _carryPressedAt = Time.time;
            }
            bool held = _input.CarryHeld && _carryPressedAt >= 0f;
            if (_input.CarryReleased && _carryPressedAt >= 0f)
            {
                _carryTapReleased = Time.time - _carryPressedAt < _catchHoldTime;
                _carryPressedAt = -1f;
            }
            if (!_input.CarryHeld && !_input.CarryReleased) _carryPressedAt = -1f;
            bool ready = held && Time.time - _carryPressedAt >= _catchHoldTime && canAct && _carry == null && !Exhausted &&
                         (_wallclimb == null || !_wallclimb.IsClimbing);
            _catchReadyLocal = ready;
            if (IsSpawned && _catchReadyNet.Value != ready) _catchReadyNet.Value = ready;
        }

        /// <summary>A thrown crate (still in flight, free to take) within catching range of the chest.</summary>
        private bool FindCatchableCargo(out CarryableObject cargo)
        {
            cargo = null;
            if (_physicsRoot == null) return false;
            Vector3 chest = _bodies[Hips].position + Vector3.up * .3f;
            int count = Physics.OverlapSphereNonAlloc(chest, _catchRadius, _catchNear, ~(1 << _limbLayer), QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var c = _catchNear[i].GetComponentInParent<CarryableObject>();
                if (c == null || !c.InFlight || !c.CanBeCarried) continue;
                if (c == _lastThrown && Time.time - _lastThrownAt < OwnThrowCatchDelay) continue;
                float d = (_catchNear[i].ClosestPoint(chest) - chest).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                cargo = c;
            }
            return cargo != null;
        }

        /// <summary>Owner, Update while carrying: hold Q to aim/charge, release to throw, F cancels.
        /// True while the throw key is in use (so F is not also a place).</summary>
        private bool UpdateThrowAim(bool canAct)
        {
            if (!_aimingThrow)
            {
                if (!(_input.ThrowTriggered && canAct)) return false;
                _aimingThrow = true;
                _aimStart = Time.time;
            }
            if (!canAct || _input.CarryPressed)
            {
                CancelThrowAim();
                return true;
            }
            float charge = ThrowCharge;
            if (!_input.ThrowHeld)
            {
                CancelThrowAim();
                _lastThrown = _carry;
                _lastThrownAt = Time.time;
                RequestThrow(charge);
                return true;
            }
            DrawThrowPreview(charge);
            return true;
        }

        private float ThrowCharge => _throwChargeTime > 1e-3f ? Mathf.Clamp01((Time.time - _aimStart) / _throwChargeTime) : 1f;

        private void CancelThrowAim()
        {
            _aimingThrow = false;
            if (_throwPreview != null) _throwPreview.Hide();
        }

        /// <summary>The launch velocity the server is asked for at this charge (also what the preview flies).</summary>
        private Vector3 ThrowVelocity(float charge)
        {
            float pitch = _camera != null ? _camera.Pitch : 0f;
            float yaw = _camera != null ? _camera.Yaw : transform.eulerAngles.y;
            Vector3 dir = Quaternion.Euler(Mathf.Clamp(pitch - 20f, -60f, 30f), yaw, 0f) * Vector3.forward;
            float speed = Mathf.Lerp(_throwMinSpeed, _throwMaxSpeed, charge) * Card(CoopGame.Network.CardEffect.LongThrow);
            return dir * speed + Flat(_bodies[Hips].linearVelocity) * .5f;
        }

        private void DrawThrowPreview(float charge)
        {
            if (_carry == null) return;
            if (_throwPreview == null) _throwPreview = ThrowPreview.Create(transform);
            _throwPreview.Show(_carry, ThrowVelocity(charge), charge, _limbLayer);
            if (_throwPreview.Landed) _throwMarkPoint = _throwPreview.LandingPoint;
        }

        /// <summary>Owner, Update: publishes the landing spot while aiming and while the thrown crate flies.</summary>
        private void PublishThrowMark()
        {
            if (!IsSpawned) return;
            Vector4 want = Vector4.zero;
            if (_aimingThrow && _throwPreview != null && _throwPreview.Visible && _throwPreview.Landed)
                want = new Vector4(_throwMarkPoint.x, _throwMarkPoint.y, _throwMarkPoint.z, 1f);
            else if (_lastThrown != null && Time.time - _lastThrownAt < ThrowMarkMaxFlight &&
                     (Time.time - _lastThrownAt < .3f || _lastThrown.InFlight) && _throwMarkNet.Value.w > 0f)
                want = new Vector4(_throwMarkPoint.x, _throwMarkPoint.y, _throwMarkPoint.z, 2f);
            Vector4 now = _throwMarkNet.Value;
            if (now.w != want.w || (want.w > 0f && ((Vector3)want - (Vector3)now).sqrMagnitude > .05f * .05f))
                _throwMarkNet.Value = want;
        }

        /// <summary>Every screen, LateUpdate: the landing ring others see (and the thrower too once the crate is in the
        /// air; while aiming the thrower already has the full preview).</summary>
        private void UpdateTeamThrowMark()
        {
            Vector4 mark = _throwMarkNet.Value;
            bool show = IsSpawned && (mark.w > 1.5f || (mark.w > .5f && !IsLocal));
            if (!show)
            {
                if (_teamMark != null && _teamMark.Visible) _teamMark.Hide();
                return;
            }
            if (_teamMark == null) _teamMark = ThrowPreview.Create(transform);
            _teamMark.ShowLandingOnly(mark);
        }

        /// <summary>Every screen, LateUpdate: the catch glow follows the (owner-written) ready state.</summary>
        private void UpdateCatchGlow()
        {
            bool on = CatchReady && !IsKnockedDown;
            if (_catchGlow == null)
            {
                if (!on || _rig == null || _rig.Visual == null) return;
                _catchGlow = CatchGlow.Create(_rig.Visual, _catchGlowColor);
            }
            _catchGlow.SetOn(on);
            if (IsLocal && _carry == null && _aimingThrow) CancelThrowAim(); // the crate left the hands mid-aim
        }

        private void DestroyCatchThrowVisuals()
        {
            if (_throwPreview != null) Destroy(_throwPreview.gameObject);
            if (_catchGlow != null) Destroy(_catchGlow);
            if (_teamMark != null) Destroy(_teamMark.gameObject);
            _teamMark = null;
            _throwPreview = null;
            _catchGlow = null;
        }
    }
}
