using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>Two different connected players hold the pads to permanently open this scene gate.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class CoopPressureGate : NetworkBehaviour
    {
        [SerializeField] private BoxCollider _leftPad;
        [SerializeField] private BoxCollider _rightPad;
        [SerializeField] private GameObject _blocker;
        [SerializeField] private Renderer _leftIndicator;
        [SerializeField] private Renderer _rightIndicator;
        [SerializeField] private Material _idleMaterial;
        [SerializeField] private Material _activeMaterial;
        [SerializeField] private Material _disabledMaterial;
        [SerializeField] private GameObject _openIndicator;
        [SerializeField] private TextMesh _statusText;
        [SerializeField, Min(0.1f)] private float _holdSeconds = 1.5f;

        private const byte Left = 1;
        private const byte Right = 2;
        private const byte Open = 4;
        private readonly NetworkVariable<byte> _state = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> _required = new(1);
        private readonly NetworkVariable<byte> _progress = new(0);
        private int _lastRequired;
        private float _holdTimer;
        public int RequiredPadCount => _required.Value;
        public int ProgressPercent => _progress.Value;
        private byte _visualState = byte.MaxValue;

        public bool IsOpen => IsSpawned && (_state.Value & Open) != 0;

        public override void OnNetworkSpawn()
        {
            _holdTimer = 0f;
            _visualState = byte.MaxValue;
        }

        public override void OnNetworkDespawn()
        {
            _holdTimer = 0f;
            _visualState = byte.MaxValue;
        }

        private void FixedUpdate()
        {
            if (IsSpawned && IsServer && (_state.Value & Open) == 0)
                EvaluatePlayers();

            // Collider activation is applied in the physics tick on every peer.
            byte state = IsSpawned ? _state.Value : (byte)0;
            if (_visualState != state || _lastRequired != RequiredPadCount)
                ApplyState(state);
        }

        private void EvaluatePlayers()
        {
            if (_leftPad == null || _rightPad == null || NetworkManager == null)
                return;

            int players = SessionPlayers.Count(NetworkManager);
            byte required = (byte)(players > 1 ? 2 : 1);
            if (_required.Value != required) { _required.Value = required; _holdTimer = 0f; }
            bool powered = SessionPlayers.Pads(NetworkManager, _leftPad, _rightPad, required, out bool left, out bool right);
            byte state = (byte)((left ? Left : 0) | (right ? Right : 0));
            _holdTimer = players > 0 && powered ? _holdTimer + Time.fixedDeltaTime : 0f;
            _progress.Value = (byte)Mathf.Clamp(Mathf.FloorToInt(_holdTimer / _holdSeconds * 100f), 0, 100);
            if (_holdTimer >= _holdSeconds) { state |= Open; _progress.Value = 100; }
            if (_state.Value != state) _state.Value = state;
        }

        private void ApplyState(byte state)
        {
            _visualState = state;
            _lastRequired = RequiredPadCount;
            bool open = (state & Open) != 0;
            if (_blocker != null) _blocker.SetActive(!open);
            if (_openIndicator != null) _openIndicator.SetActive(open);
            if (_leftIndicator != null)
                _leftIndicator.sharedMaterial = (state & Left) != 0 || open ? _activeMaterial : _idleMaterial;
            if (_rightIndicator != null)
                _rightIndicator.sharedMaterial = RequiredPadCount == 1 ? _disabledMaterial : (state & Right) != 0 || open ? _activeMaterial : _idleMaterial;
            if (_statusText != null)
                _statusText.text = open ? "ประตูเปิดแล้ว" : RequiredPadCount == 1 ?
                    "ยืนบนแผ่นสีฟ้าสักครู่" : "ช่วยกันเหยียบคนละแผ่น";
        }
    }
}
