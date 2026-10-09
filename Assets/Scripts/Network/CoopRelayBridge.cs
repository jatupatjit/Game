using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>Players charge separate pads together to permanently deploy a safe cargo bridge.</summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class CoopRelayBridge : NetworkBehaviour
    {
        [SerializeField] private BoxCollider _leftPad;
        [SerializeField] private BoxCollider _rightPad;
        [SerializeField] private GameObject _bridge;
        [SerializeField] private TextMesh _label;
        [SerializeField, Min(.1f)] private float _chargeSeconds = 2f;
        private readonly NetworkVariable<bool> _deployed = new(false);
        private readonly NetworkVariable<byte> _progress = new(0);
        private float _charge;
        private int _lastDisplay = -1;
        public bool IsDeployed => _deployed.Value;

        public override void OnNetworkSpawn()
        {
            _lastDisplay = -1;
            _charge = 0f;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned) return;
            if (IsServer && !_deployed.Value)
            {
                int playerCount = 0;
                bool left = false, right = false;
                var clients = NetworkManager.ConnectedClientsList;
                for (int i = 0; i < clients.Count; i++)
                {
                    var player = clients[i].PlayerObject;
                    if (player == null || !player.IsSpawned) continue;
                    playerCount++;
                    Vector3 feet = player.transform.position - Vector3.up;
                    left |= Contains(_leftPad, feet);
                    right |= Contains(_rightPad, feet);
                }
                // Solo sessions can use either pad. Co-op requires both banks;
                // a third and fourth player can keep supporting the crate.
                bool powered = playerCount > 1 ? left && right : playerCount == 1 && (left || right);
                _charge = powered ? _charge + Time.fixedDeltaTime : 0f;
                byte progress = (byte)Mathf.Clamp(Mathf.FloorToInt(_charge / _chargeSeconds * 100f), 0, 100);
                if (_progress.Value != progress) _progress.Value = progress;
                if (_charge >= _chargeSeconds) _deployed.Value = true;
            }
            // Activation changes colliders only on the physics step, on every peer.
            if (_bridge != null && _bridge.activeSelf != _deployed.Value)
                _bridge.SetActive(_deployed.Value);
            int display = _deployed.Value ? 101 : _progress.Value / 10;
            if (_label != null && display != _lastDisplay)
            {
                _lastDisplay = display;
                _label.text = _deployed.Value ? "สะพานเปิดแล้ว ไปต่อได้เลย" :
                    "แบ่งกันยืนบนแผ่นสีฟ้า แล้วรอสักครู่\n" + (_progress.Value / 10 * 10) + "%  |  มาคนเดียว ยืนจุดไหนก็ได้";
            }
        }

        private static bool Contains(BoxCollider pad, Vector3 feet)
        {
            if (pad == null) return false;
            Vector3 local = pad.transform.InverseTransformPoint(feet) - pad.center;
            Vector3 half = pad.size * .5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
    }
}
