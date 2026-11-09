using CoopGame.Network;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Level start: the crate stays hidden and frozen at its spawn point (up in the air) until every connected player
    /// has picked a card, then the server lets it fall (e.g. onto the start hay pile). Once released it stays released
    /// for the rest of the level. Put it on the crate's NetworkObject.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CargoDropIntro : NetworkBehaviour
    {
        [Tooltip("Small delay (s) after the last card pick before the crate drops")]
        [SerializeField, Min(0f)] private float _dropDelay = .6f;

        private readonly NetworkVariable<bool> _released = new(false);
        private Rigidbody _body;
        private Vector3 _holdPosition;
        private Quaternion _holdRotation;
        private float _readyTime = -1f;
        private int _shown = -1;

        public bool Released => IsSpawned && _released.Value;

        private void Awake() => _body = GetComponent<Rigidbody>();

        public override void OnNetworkSpawn()
        {
            _holdPosition = transform.position;
            _holdRotation = transform.rotation;
            _shown = -1;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned) return;
            if (IsServer && !_released.Value) ServerTick();
            int state = _released.Value ? 1 : 0;
            if (state != _shown) Apply(state == 1);
        }

        private void ServerTick()
        {
            // Hold the crate in the air until released.
            if (_body != null)
            {
                _body.position = _holdPosition; _body.rotation = _holdRotation;
                if (!_body.isKinematic) { _body.linearVelocity = Vector3.zero; _body.angularVelocity = Vector3.zero; }
            }
            if (!AllPicked()) { _readyTime = -1f; return; }
            if (_readyTime < 0f) _readyTime = Time.time + _dropDelay;
            if (Time.time >= _readyTime) _released.Value = true;
        }

        private bool AllPicked()
        {
            int players = SessionPlayers.Count(NetworkManager);
            var everyone = PlayerExpeditionState.Everyone;
            if (players == 0 || everyone.Count < players) return false;
            for (int i = 0; i < everyone.Count; i++)
                if (everyone[i] != null && everyone[i].IsSpawned && everyone[i].Card.Value == 0) return false;
            return true;
        }

        private void Apply(bool released)
        {
            _shown = released ? 1 : 0;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = released;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = released;
            if (IsServer && _body != null)
            {
                _body.useGravity = released;
                if (released) { _body.linearVelocity = Vector3.zero; _body.WakeUp(); }
            }
        }
    }
}
