using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame.Network
{
    /// <summary>A nearby player holds E to open the gate for eight seconds; the team relays cargo through it.</summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class TeamLeverGate : NetworkBehaviour
    {
        [SerializeField] private Transform _lever;
        [SerializeField] private GameObject _barrier;
        [SerializeField] private BoxCollider _safetyVolume;
        [SerializeField] private TextMesh _label;
        [SerializeField] private FragileCargo _cargo;
        [SerializeField, Min(1f)] private float _openSeconds = 8f;
        private readonly NetworkVariable<bool> _open = new(false);
        private float _openUntil, _nextSend;
        private bool _wasHeld, _visualOpen;
        public bool IsOpen => _open.Value;
        public bool IsLocalNear
        {
            get
            {
                var player = NetworkManager != null ? NetworkManager.LocalClient?.PlayerObject : null;
                return player != null && _lever != null &&
                    (player.transform.position - _lever.position).sqrMagnitude <= 9f;
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsClient || _lever == null) return;
            bool held = IsLocalNear && Keyboard.current != null && Keyboard.current.eKey.isPressed &&
                !PauseMenu.IsPaused && !MissionFailUI.IsVisible && !ExpeditionHUD.BlocksGameplayInput;
            if (held && (!_wasHeld || Time.unscaledTime >= _nextSend))
            {
                HoldLeverRpc();
                _nextSend = Time.unscaledTime + .2f;
            }
            _wasHeld = held;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void HoldLeverRpc(RpcParams parameters = default)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(parameters.Receive.SenderClientId, out var client) ||
                client.PlayerObject == null || _lever == null ||
                (client.PlayerObject.transform.position - _lever.position).sqrMagnitude > 9f) return;
            _openUntil = Time.unscaledTime + _openSeconds;
        }

        private bool PassageOccupied()
        {
            if (_safetyVolume == null) return false;
            Bounds bounds = _safetyVolume.bounds;
            if (_cargo != null && bounds.Contains(_cargo.transform.position)) return true;
            var clients = NetworkManager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
                if (clients[i].PlayerObject != null && bounds.Contains(clients[i].PlayerObject.transform.position)) return true;
            return false;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned) return;
            if (IsServer) _open.Value = Time.unscaledTime < _openUntil || (_open.Value && PassageOccupied());
            if (_barrier != null) _barrier.SetActive(!_open.Value);
            if (_visualOpen != _open.Value)
            {
                _visualOpen = _open.Value;
                if (_visualOpen) GameplayFeedback.Play(GameplayFeedback.Cue.Portal, transform.position);
            }
            if (_label != null) _label.text = _open.Value ? "GATE OPEN — PASS THE CRATE" :
                "HOLD E AT THE LEVER\nTEAMMATES CARRY THROUGH";
        }
    }
}

