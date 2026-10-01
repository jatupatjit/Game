using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>Replicates mission failure even after the destroyed cargo has despawned.</summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class LevelMission : NetworkBehaviour
    {
        [SerializeField] private FragileCargo _cargo;
        [SerializeField] private DeliveryPoint _delivery;
        [SerializeField] private DeliveryExitPortal _portal;
        [SerializeField] private string _objective = "Deliver the wooden crate to the castle";
        public readonly NetworkVariable<byte> Phase = new(0);
        public FragileCargo Cargo => _cargo;
        public DeliveryPoint Delivery => _delivery;
        public DeliveryExitPortal Portal => _portal;
        public string Objective => _objective;
        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || Phase.Value == 3) return;
            if (_delivery != null && _delivery.IsDelivered) Phase.Value = 2;
            else if (_cargo == null || _cargo.IsDestroyed || _cargo.CurrentHP.Value <= 0) Phase.Value = 3;
            else Phase.Value = _cargo.IsCarried ? (byte)1 : (byte)0;
        }
    }
}
