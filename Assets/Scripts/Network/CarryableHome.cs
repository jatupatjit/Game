using CoopGame.CarrySystem;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Puts a secondary carryable (heavy stone, practice sack) back where it started if it falls off the map, so a
    /// puzzle object can never be lost. Server only; the move itself runs on the physics step.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CarryableObject))]
    public sealed class CarryableHome : NetworkBehaviour
    {
        [SerializeField] private float _killY = -15f;
        private CarryableObject _carryable;
        private Rigidbody _body;
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private bool _captured;
        private bool _resetPending;

        /// <summary>Server: puts the object back at its starting pose on the next physics step.</summary>
        public void ResetNow() => _resetPending = true;

        public override void OnNetworkSpawn()
        {
            _carryable = GetComponent<CarryableObject>();
            _body = GetComponent<Rigidbody>();
            if (!_captured) { _homePosition = transform.position; _homeRotation = transform.rotation; _captured = true; }
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || _carryable == null) return;
            if (!_resetPending && transform.position.y >= _killY) return;
            _resetPending = false;
            _carryable.DetachAllCarriers();
            if (_body != null) { _body.linearVelocity = Vector3.zero; _body.angularVelocity = Vector3.zero; }
            var networkTransform = GetComponent<NetworkTransform>();
            if (networkTransform != null) networkTransform.Teleport(_homePosition, _homeRotation, transform.localScale);
            else transform.SetPositionAndRotation(_homePosition, _homeRotation);
            if (_body != null) _body.WakeUp();
        }
    }
}
