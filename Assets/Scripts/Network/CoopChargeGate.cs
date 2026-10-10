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
        [SerializeField, Min(.1f)] private float _chargeSeconds = 3f;
        private readonly NetworkVariable<bool> _open = new(false);
        private readonly NetworkVariable<byte> _state = new(0);
        private MaterialPropertyBlock _properties;
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
                int count = 0;
                ulong left = ulong.MaxValue, right = ulong.MaxValue;
                var clients = NetworkManager.ConnectedClientsList;
                for (int i = 0; i < clients.Count; i++)
                {
                    var player = clients[i].PlayerObject;
                    if (player == null || !player.IsSpawned) continue;
                    count++;
                    Vector3 feet = player.transform.position - Vector3.up;
                    if (Contains(_leftPad, feet)) left = clients[i].ClientId;
                    if (Contains(_rightPad, feet)) right = clients[i].ClientId;
                }
                bool hasLeft = left != ulong.MaxValue, hasRight = right != ulong.MaxValue;
                bool powered = count == 1 ? hasLeft || hasRight : hasLeft && hasRight && left != right;
                _charge = powered ? _charge + Time.fixedDeltaTime : 0;
                byte state = (byte)((hasLeft ? 1 : 0) | (hasRight ? 2 : 0));
                if (_state.Value != state) _state.Value = state;
                if (_charge >= _chargeSeconds) _open.Value = true;
            }
            // Collider changes happen on the fixed step on every peer, including late joiners.
            if (_barrier != null && _barrier.activeSelf == _open.Value) _barrier.SetActive(!_open.Value);
            int shown = _open.Value ? 4 : _state.Value;
            if (shown == _shown) return;
            _shown = shown;
            Paint(_leftLamp, _open.Value, (_state.Value & 1) != 0);
            Paint(_rightLamp, _open.Value, (_state.Value & 2) != 0);
        }

        private static bool Contains(BoxCollider pad, Vector3 feet)
        {
            if (pad == null || !pad.enabled || !pad.gameObject.activeInHierarchy) return false;
            Vector3 p = pad.transform.InverseTransformPoint(feet) - pad.center;
            Vector3 h = pad.size * .5f;
            return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.y) <= h.y && Mathf.Abs(p.z) <= h.z;
        }

        private void Paint(Renderer lamp, bool open, bool occupied)
        {
            if (lamp == null) return;
            lamp.GetPropertyBlock(_properties);
            _properties.SetColor(BaseColor, open ? new Color(.1f,.9f,.3f) :
                occupied ? new Color(1f,.65f,.05f) : new Color(.1f,.55f,.95f));
            lamp.SetPropertyBlock(_properties);
        }
    }
}
