using CoopGame.CarrySystem;
using UnityEngine;

namespace CoopGame.DevTools
{
    /// <summary>
    /// Editor/host test helper: a stand-in second carrier (no player, just a transform) that holds a crate on the
    /// server and streams a fixed walking direction every physics step. Used to test the two-carrier rules alone
    /// (tug-of-war jolt, Hard sudden drop) without a second machine. Add it, set Cargo/Direction, it attaches on
    /// Start; Release() lets go like a carrier pressing F.
    /// </summary>
    public sealed class FakeCarrier : MonoBehaviour
    {
        public CarryableObject Cargo;
        public ulong ClientId = 77;
        public Vector3 Direction;

        public bool Attached { get; private set; }

        private void Start()
        {
            if (Cargo == null) return;
            Vector3 local = Cargo.transform.InverseTransformPoint(transform.position);
            Attached = Cargo.TryAttachCarrier(ClientId, transform, null, local + Vector3.right * .2f, local - Vector3.right * .2f, out _);
            Debug.Log("[FakeCarrier] attached=" + Attached);
        }

        private void FixedUpdate()
        {
            if (!Attached || Cargo == null) return;
            if (!Cargo.HasCarrier(ClientId)) { Attached = false; Debug.Log("[FakeCarrier] let go by the crate"); return; }
            Vector3 heading = Cargo.transform.position - transform.position; heading.y = 0f;
            Cargo.UpdateCarrierInput(ClientId, Vector3.ClampMagnitude(Direction, 1f), heading.normalized, CarryableObject.NeutralHoldHeight, true, true);
            transform.position += Vector3.ClampMagnitude(Direction, 1f) * (2f * Time.fixedDeltaTime);
        }

        public void Release()
        {
            if (Cargo != null && Cargo.HasCarrier(ClientId)) Cargo.DetachCarrier(ClientId);
            Attached = false;
        }
    }
}
