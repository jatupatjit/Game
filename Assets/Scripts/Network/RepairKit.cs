using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// One-use repair kit for optional branch islands: +20 cargo HP (capped at MaxHP). The cargo has to be brought to
    /// the kit (that is the risk of the detour) and a player has to stand at it. A kit is never consumed when it
    /// cannot help (cargo undamaged), and the main route never requires it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class RepairKit : NetworkBehaviour
    {
        [SerializeField] private FragileCargo _cargo;
        [SerializeField, Min(1)] private int _healAmount = 20;
        [SerializeField, Min(.5f)] private float _playerRadius = 2.2f;
        [SerializeField, Min(.5f)] private float _cargoRadius = 7f;
        [Tooltip("Visual root hidden once the kit is used.")]
        [SerializeField] private GameObject _visual;
        [SerializeField] private TextMesh _label;

        private readonly NetworkVariable<bool> _used = new(false);
        private int _shown = -1;
        private float _nextLabelAt;

        public bool IsUsed => IsSpawned && _used.Value;
        public int HealAmount => _healAmount;

        public override void OnNetworkSpawn() => _shown = -1;

        private void FixedUpdate()
        {
            if (!IsSpawned) return;
            if (IsServer && !_used.Value && _cargo != null && !_cargo.IsDestroyed && !_cargo.IsSecured) ServerTick();
            int state = _used.Value ? 1 : 0;
            if (state != _shown)
            {
                _shown = state;
                if (_visual != null) _visual.SetActive(state == 0);
                if (_label != null && state == 1) _label.text = "ใช้ชุดซ่อมแล้ว";
            }
        }

        private bool PlayerNear()
        {
            var clients = NetworkManager.ConnectedClientsList;
            float r2 = _playerRadius * _playerRadius;
            for (int i = 0; i < clients.Count; i++)
            {
                var player = clients[i].PlayerObject;
                if (player == null || !player.IsSpawned) continue;
                Vector3 d = player.transform.position - transform.position;
                if (Mathf.Abs(d.y) > 3f) continue;
                d.y = 0f;
                if (d.sqrMagnitude <= r2) return true;
            }
            return false;
        }

        private void ServerTick()
        {
            if (!PlayerNear()) return;
            Vector3 c = _cargo.transform.position - transform.position;
            if (c.sqrMagnitude > _cargoRadius * _cargoRadius) return;
            if (_cargo.CurrentHP.Value >= _cargo.MaxHP)
            {
                if (LevelRunState.Instance != null && Time.unscaledTime >= _nextLabelAt)
                {
                    _nextLabelAt = Time.unscaledTime + 4f;
                    LevelRunState.Instance.PostNotice(LevelNotice.KitFull);
                }
                return;
            }
            if (_cargo.HealServer(_healAmount) <= 0) return;
            _used.Value = true;
            if (LevelRunState.Instance != null) LevelRunState.Instance.PostNotice(LevelNotice.RepairKit);
        }
    }
}
