using Unity.Netcode;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Thrown-crate state for catching (2026-11-05). From the throw until the crate slows down on landing (or a
    /// carrier takes it, or <see cref="MaxFlightTime"/> passes) the crate is "in flight", and a player holding the
    /// lift key nearby may catch it (<see cref="CoopGame.Player.RagdollCharacter"/>). Server-written, every screen reads it.
    /// </summary>
    public partial class CarryableObject
    {
        private const float MaxFlightTime = 4f, LandedSpeed = 1.5f, MinFlightTime = .15f;

        private readonly NetworkVariable<bool> _inFlight = new(false);
        private bool _inFlightOffline;
        private float _flightStart;

        /// <summary>True from a throw until the crate lands or is caught.</summary>
        public bool InFlight => IsSpawned ? _inFlight.Value : _inFlightOffline;

        private void SetInFlight(bool value)
        {
            _inFlightOffline = value;
            if (IsSpawned && IsServer && _inFlight.Value != value) _inFlight.Value = value;
        }

        /// <summary>Server, FixedUpdate: the throw velocity was just applied.</summary>
        private void BeginFlight()
        {
            _flightStart = Time.fixedTime;
            SetInFlight(true);
        }

        /// <summary>Server, FixedUpdate: ends the flight on landing, a catch or a timeout.</summary>
        private void TickFlight()
        {
            if (!_inFlightOffline) return;
            float age = Time.fixedTime - _flightStart;
            if (_activeCarriers.Count > 0 || IsSecured || age > MaxFlightTime ||
                (age > MinFlightTime && _rigidbody.linearVelocity.sqrMagnitude < LandedSpeed * LandedSpeed))
                SetInFlight(false);
        }
    }
}
