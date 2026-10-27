using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Hold-pose carry (2026-10-27 rewrite). While carried, the cargo is not supported by hand springs any more:
    /// it moves with the carriers' averaged walking intent, and the server works out where every carrier's hands
    /// should be (chest height, a fixed distance in front of the body) and steers the Rigidbody so each grip lands
    /// there. The cargo stays level, so it
    /// cannot pitch out of reach, and stays a non-kinematic body, so walls still stop it and impacts still
    /// damage it through <see cref="FragileCargo"/>. Each carrier's own movement is held near their stance
    /// by <see cref="PlayerCarry.ConstrainCarryDisplacement"/>, which keeps the body out of the crate.
    /// </summary>
    public partial class CarryableObject
    {
        /// <summary>Horizontal distance from a carrier's body centre to the middle of their grip.</summary>
        public const float HoldReach = .36f;
        /// <summary>Grip height relative to the carrier's root (the capsule centre): a little below the shoulders.</summary>
        public const float HoldHeightOffset = -.07f;

        // Mouse-wheel lift. A carrier streams NeutralHoldHeight + steps * HoldStepHeight as their hold height;
        // the grip anchor moves by the difference. Lowering is shorter: the arms (0.40 m) run out of reach sooner.
        public const float NeutralHoldHeight = 1.3f;
        public const float HoldStepHeight = .06f;
        public const int HoldRaiseSteps = 4, HoldLowerSteps = 2;

        // Fraction of the remaining pose error closed per physics step, and the ramp-in after a grab.
        private const float HoldTrackGain = .35f;
        private const float HoldGroundGain = .12f;
        // Softer than the height hold: the crate eases round a turn instead of snapping to the carriers' heading.
        private const float HoldTurnGain = .15f;
        private const float HoldSettleSeconds = .35f;
        private const float HoldMaxSpeed = 12f;
        private const float HoldMaxAngularSpeed = 10f;

        /// <summary>Middle of this carrier's active grips in cargo-local space (scaled).</summary>
        private Vector3 GripMidLocal(CarrierInfo carrier, Vector3 objectScale)
        {
            Vector3 left = Vector3.Scale(carrier.LocalContactLeft, objectScale);
            Vector3 right = Vector3.Scale(carrier.LocalContactRight, objectScale);
            if (carrier.LeftHandActive && carrier.RightHandActive) return (left + right) * .5f;
            return carrier.LeftHandActive ? left : right;
        }

        private static Vector3 FlatHeading(Vector3 heading, Transform fallback)
        {
            heading.y = 0f;
            if (heading.sqrMagnitude < .001f) heading = fallback.forward;
            heading.y = 0f;
            return heading.sqrMagnitude < .001f ? Vector3.forward : heading.normalized;
        }

        /// <summary>Where this carrier wants the middle of their grip: in front of the body at chest height.</summary>
        private Vector3 HoldAnchor(CarrierInfo carrier, bool isNetworked, out Vector3 heading)
        {
            Transform player = carrier.PlayerTransform;
            Vector3 position = player.position;
            // A remote carrier's transform is one network delay old; use where their client really has them.
            if (TryGetRemoteCarrierMotion(carrier, isNetworked, out Vector3 lead, out _)) position += lead;
            heading = FlatHeading(carrier.FacingHeading, player);
            float lift = Mathf.Clamp(carrier.HoldHeight - NeutralHoldHeight,
                -HoldLowerSteps * HoldStepHeight, HoldRaiseSteps * HoldStepHeight);
            return position + heading * HoldReach + Vector3.up * (HoldHeightOffset + lift);
        }

        /// <summary>Server, FixedUpdate: target pose from the carriers, then velocities that close the gap.</summary>
        private void DriveHeldPose(bool isNetworked)
        {
            Vector3 objectScale = transform.lossyScale;

            // Yaw: best rotation of the grips (cargo-local, flat) onto the carriers' anchors around their centres.
            // With one carrier, turn the gripped face back towards them.
            int count = 0;
            Vector3 anchorCentre = Vector3.zero, gripCentre = Vector3.zero, soloHeading = Vector3.forward;
            float earliestAttach = float.MaxValue;
            foreach (var carrier in _activeCarriers.Values)
            {
                if (carrier.PlayerTransform == null || (!carrier.LeftHandActive && !carrier.RightHandActive)) continue;
                carrier.HoldAnchor = HoldAnchor(carrier, isNetworked, out soloHeading);
                carrier.HoldGrip = GripMidLocal(carrier, objectScale);
                anchorCentre += carrier.HoldAnchor;
                gripCentre += carrier.HoldGrip;
                earliestAttach = Mathf.Min(earliestAttach, carrier.AttachedAtFixedTime);
                count++;
            }
            if (count == 0) return;
            anchorCentre /= count;
            gripCentre /= count;

            float a = 0f, b = 0f;
            foreach (var carrier in _activeCarriers.Values)
            {
                if (carrier.PlayerTransform == null || (!carrier.LeftHandActive && !carrier.RightHandActive)) continue;
                Vector3 g = count == 1 ? carrier.HoldGrip : carrier.HoldGrip - gripCentre;
                Vector3 w = count == 1 ? -soloHeading : carrier.HoldAnchor - anchorCentre;
                // Quaternion.Euler(0, y, 0) maps (x, z) to (x cos y + z sin y, -x sin y + z cos y).
                a += w.x * g.x + w.z * g.z;
                b += w.x * g.z - w.z * g.x;
            }
            float currentYaw = _rigidbody.rotation.eulerAngles.y;
            float yaw = a * a + b * b > 1e-6f ? Mathf.Atan2(b, a) * Mathf.Rad2Deg : currentYaw;
            Quaternion targetRotation = Quaternion.Euler(0f, yaw, 0f);

            // Position: every grip at its anchor, averaged over the carriers. Uses the cargo's current yaw, so while
            // turning the crate swings round with the turn it is making instead of jumping to the final pose.
            Quaternion currentYawRotation = Quaternion.Euler(0f, currentYaw, 0f);
            Vector3 targetPosition = Vector3.zero;
            foreach (var carrier in _activeCarriers.Values)
            {
                if (carrier.PlayerTransform == null || (!carrier.LeftHandActive && !carrier.RightHandActive)) continue;
                targetPosition += carrier.HoldAnchor - currentYawRotation * carrier.HoldGrip;
            }
            targetPosition /= count;

            // Ease in after a grab so a resting crate is lifted, not snapped, into the hands.
            float settle = Mathf.SmoothStep(.15f, 1f, Mathf.Clamp01((Time.fixedTime - earliestAttach) / HoldSettleSeconds));
            float gain = HoldTrackGain * settle;
            float dt = Time.fixedDeltaTime;

            // The team's walking intent moves the cargo (averaged, so a carrier who stops halves the pace and is
            // drawn along); the pose error is closed on top, firmly in height, gently across the ground.
            Vector3 intent = Vector3.zero;
            foreach (var carrier in _activeCarriers.Values)
                if (carrier.PlayerTransform != null && (carrier.LeftHandActive || carrier.RightHandActive) &&
                    Time.fixedTime - carrier.InputReceivedAt <= InputStaleSeconds)
                    intent += carrier.InputDirection;
            intent /= count;
            intent.y = 0f;
            intent *= 5f * GetSpeedMultiplier();

            Vector3 error = targetPosition - _rigidbody.position;
            Vector3 velocity = intent * settle + new Vector3(error.x * HoldGroundGain, error.y * HoldTrackGain, error.z * HoldGroundGain) * (settle / dt);

            _rigidbody.useGravity = false;
            _rigidbody.constraints = RigidbodyConstraints.None;
            _rigidbody.linearVelocity = Vector3.ClampMagnitude(velocity, HoldMaxSpeed);

            Quaternion delta = targetRotation * Quaternion.Inverse(_rigidbody.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            Vector3 angular = float.IsFinite(axis.x) && Mathf.Abs(angle) > .01f
                ? axis * (angle * Mathf.Deg2Rad * HoldTurnGain * settle / dt) : Vector3.zero;
            _rigidbody.angularVelocity = Vector3.ClampMagnitude(angular, HoldMaxAngularSpeed);
        }

        /// <summary>Where a carrier facing <paramref name="heading"/> stands so their grip middle is at
        /// <paramref name="gripMidWorld"/>. Used by the owner's movement limit.</summary>
        public static Vector3 StanceFor(Vector3 gripMidWorld, Vector3 heading) =>
            gripMidWorld - heading * HoldReach;
    }
}
