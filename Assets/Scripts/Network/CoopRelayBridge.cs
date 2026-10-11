using Unity.Netcode;
using UnityEngine;
namespace CoopGame.Network
{
    [RequireComponent(typeof(NetworkObject)), DisallowMultipleComponent]
    public sealed class CoopRelayBridge : NetworkBehaviour
    {
        [SerializeField] private BoxCollider _leftPad, _rightPad;
        [SerializeField] private GameObject _bridge;
        [SerializeField] private TextMesh _label;
        [SerializeField, Min(.1f)] private float _chargeSeconds = 5f;
        [SerializeField] private Renderer _leftIndicator, _rightIndicator;
        [SerializeField] private Material _idleMaterial, _activeMaterial, _disabledMaterial;
        private readonly NetworkVariable<bool> _deployed = new(false);
        private readonly NetworkVariable<byte> _progress = new(0), _required = new(1), _occupied = new(0);
        private float _charge;
        private int _lastDisplay = -1;
        public bool IsDeployed => _deployed.Value;
        public int ProgressPercent => _progress.Value;
        public int RequiredPadCount => _required.Value;
        public int OccupiedPadCount => (_occupied.Value & 1) + ((_occupied.Value >> 1) & 1);
        public override void OnNetworkSpawn() { _lastDisplay = -1; _charge = 0f; }
        private void FixedUpdate()
        {
            if (!IsSpawned) return;
            if (IsServer && !_deployed.Value)
            {
                int players = SessionPlayers.Count(NetworkManager);
                _required.Value = (byte)(players > 1 ? 2 : 1);
                bool powered = SessionPlayers.Pads(NetworkManager, _leftPad, _rightPad, _required.Value, out bool left, out bool right);
                _occupied.Value = (byte)((left ? 1 : 0) | (right ? 2 : 0));
                if (players > 0 && powered) _charge = Mathf.Min(_chargeSeconds, _charge + Time.fixedDeltaTime);
                _progress.Value = (byte)Mathf.Clamp(Mathf.FloorToInt(_charge / _chargeSeconds * 100f), 0, 100);
                if (_charge >= _chargeSeconds) { _progress.Value = 100; _deployed.Value = true; }
            }
            if (_bridge != null && _bridge.activeSelf != IsDeployed) _bridge.SetActive(IsDeployed);
            int display = ProgressPercent | (RequiredPadCount << 8) | (_occupied.Value << 10) | (IsDeployed ? 1 << 12 : 0);
            if (display == _lastDisplay) return;
            _lastDisplay = display;
            if (_label != null) _label.text = IsDeployed ? "สะพานพร้อมแล้ว" : $"กำลังสร้างสะพาน {ProgressPercent}%";
            if (_leftIndicator != null) _leftIndicator.sharedMaterial = IsDeployed || (_occupied.Value & 1) != 0 ? _activeMaterial : _idleMaterial;
            if (_rightIndicator != null) _rightIndicator.sharedMaterial = RequiredPadCount == 1 ? _disabledMaterial : IsDeployed || (_occupied.Value & 2) != 0 ? _activeMaterial : _idleMaterial;
        }
    }
}
