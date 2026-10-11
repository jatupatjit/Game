using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>Separate players charge two pads, permanently opening a cargo passage.</summary>
    [RequireComponent(typeof(NetworkObject)), DisallowMultipleComponent]
    public sealed class CoopChargeGate : NetworkBehaviour
    {
        [SerializeField] private BoxCollider _leftPad;
        [SerializeField] private BoxCollider _rightPad;
        [SerializeField] private Renderer _leftLamp;
        [SerializeField] private Renderer _rightLamp;
        [SerializeField] private GameObject _barrier;
        [SerializeField, Min(.1f)] private float _chargeSeconds = 1.5f;
        private readonly NetworkVariable<bool> _open = new(false);
        private readonly NetworkVariable<byte> _state = new(0);
        private MaterialPropertyBlock _properties;
        private readonly NetworkVariable<byte> _required = new(1), _progress = new(0);
        public int RequiredPadCount => _required.Value;
        public int ProgressPercent => _progress.Value;
        private float _charge;
        private int _shown = -1;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public bool IsOpen => IsSpawned && _open.Value;

        private void Awake() => _properties = new MaterialPropertyBlock();
        public override void OnNetworkSpawn() { _charge = 0; _shown = -1; }

        private void FixedUpdate()
        {
            if (!IsSpawned) return;
            if (IsServer && !_open.Value)
            {
                int count = SessionPlayers.Count(NetworkManager);
                byte required = (byte)(count > 1 ? 2 : 1);
                if (_required.Value != required) { _required.Value = required; _charge = 0f; }
                bool powered = SessionPlayers.Pads(NetworkManager, _leftPad, _rightPad, required, out bool hasLeft, out bool hasRight);
                _charge = count > 0 && powered ? _charge + Time.fixedDeltaTime : 0f;
                _progress.Value = (byte)Mathf.Clamp(Mathf.FloorToInt(_charge / _chargeSeconds * 100f), 0, 100);
                byte state = (byte)((hasLeft ? 1 : 0) | (hasRight ? 2 : 0));
                if (_state.Value != state) _state.Value = state;
                if (_charge >= _chargeSeconds) { _progress.Value = 100; _open.Value = true; }
            }
            // Collider changes happen on the fixed step on every peer, including late joiners.
            if (_barrier != null && _barrier.activeSelf == _open.Value) _barrier.SetActive(!_open.Value);
            int shown = (_open.Value ? 4 : _state.Value) | (RequiredPadCount << 4);
            if (shown == _shown) return;
            _shown = shown;
            Paint(_leftLamp, _open.Value, (_state.Value & 1) != 0, false);
            Paint(_rightLamp, _open.Value, (_state.Value & 2) != 0, RequiredPadCount == 1);
        }

        private void Paint(Renderer lamp, bool open, bool occupied, bool disabled)
        {
            if (lamp == null) return;
            lamp.GetPropertyBlock(_properties);
            _properties.SetColor(BaseColor, disabled ? new Color(.32f,.36f,.39f) : open ? new Color(.1f,.9f,.3f) :
                occupied ? new Color(1f,.65f,.05f) : new Color(.1f,.55f,.95f));
            lamp.SetPropertyBlock(_properties);
        }
    }
}
