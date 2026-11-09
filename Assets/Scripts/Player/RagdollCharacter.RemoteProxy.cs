using Unity.Netcode;
using UnityEngine;
using CoopGame.CarrySystem;

namespace CoopGame.Player
{
    public partial class RagdollCharacter
    {
        // Other players' ragdolls are simulated only on their own machine, so here they had no collider at all and
        // walking (or carrying) went straight through them (George 2026-11-09). A kinematic capsule follows the
        // remote player's hips so local bodies and crates bump into them. It ignores the crate that player carries
        // (it sits inside their arms) and turns off while they are knocked down.
        private const float RemoteProxyRadius = .28f, RemoteProxyHeight = 1.3f, RemoteProxyLift = .15f;
        private GameObject _remoteProxy;
        private Rigidbody _remoteProxyBody;
        private CapsuleCollider _remoteProxyCollider;
        private CarryableObject _remoteProxyIgnored;
        private PlayerAppearance _remoteAppearance;

        /// <summary>Non-owner, FixedUpdate: keep the stand-in capsule on the remote player's hips.</summary>
        private void UpdateRemoteProxy()
        {
            if (!IsSpawned || !EnsureBones() || _bones[Hips] == null) return;
            if (_remoteProxy == null)
            {
                int layer = LayerMask.NameToLayer(LimbLayerName);
                _remoteProxy = new GameObject($"RemoteBodyProxy_{name}") { hideFlags = HideFlags.DontSave };
                DontDestroyOnLoad(_remoteProxy);
                if (layer >= 0) _remoteProxy.layer = layer;
                _remoteProxyBody = _remoteProxy.AddComponent<Rigidbody>();
                _remoteProxyBody.isKinematic = true;
                _remoteProxyBody.interpolation = RigidbodyInterpolation.None;
                _remoteProxyCollider = _remoteProxy.AddComponent<CapsuleCollider>();
                _remoteProxyCollider.radius = RemoteProxyRadius;
                _remoteProxyCollider.height = RemoteProxyHeight;
                _remoteProxyCollider.direction = 1;
                _remoteProxy.transform.position = _bones[Hips].position + Vector3.up * RemoteProxyLift;
                _remoteAppearance = GetComponent<PlayerAppearance>();
            }

            bool down = _remoteAppearance != null && _remoteAppearance.IsDizzy;
            if (_remoteProxyCollider.enabled == down) _remoteProxyCollider.enabled = !down;
            _remoteProxyBody.MovePosition(_bones[Hips].position + Vector3.up * RemoteProxyLift);

            CarryableObject carried = _carriedCargo.Value.TryGet(out NetworkObject cargoObject) ? cargoObject.GetComponent<CarryableObject>() : null;
            if (carried != _remoteProxyIgnored)
            {
                SetRemoteProxyIgnore(_remoteProxyIgnored, false);
                SetRemoteProxyIgnore(carried, true);
                _remoteProxyIgnored = carried;
            }
        }

        private void SetRemoteProxyIgnore(CarryableObject cargo, bool ignore)
        {
            if (cargo == null || _remoteProxyCollider == null) return;
            foreach (var c in cargo.GetComponentsInChildren<Collider>())
                if (c != null) Physics.IgnoreCollision(_remoteProxyCollider, c, ignore);
        }

        private void DestroyRemoteProxy()
        {
            if (_remoteProxy != null) Destroy(_remoteProxy);
            _remoteProxy = null;
            _remoteProxyIgnored = null;
        }
    }
}
