using System;
using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>Server-authoritative static damage zone for spikes and other fixed hazards.</summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class NetworkCargoDamageZone : NetworkBehaviour
    {
        [Header("Damage Zone")]
        [SerializeField] private BoxCollider _zone;
        [SerializeField] private FragileCargo _cargo;
        [SerializeField, Min(1)] private int _damage = 10;
        [SerializeField, Min(.1f)] private float _cooldownSeconds = 1f;
        [SerializeField] private string _damageReason = "Wooden spikes";

        private readonly Collider[] _overlaps = new Collider[32];
        private double _nextDamageTime;

        public int Damage => _damage;
        public float CooldownSeconds => _cooldownSeconds;

        public override void OnNetworkSpawn()
        {
            _nextDamageTime = 0d;
            if (IsServer && _cargo == null) _cargo = FindAnyObjectByType<FragileCargo>();
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || _zone == null || !_zone.enabled ||
                _cargo == null || _cargo.IsDestroyed || _cargo.IsSecured ||
                NetworkManager.ServerTime.Time < _nextDamageTime)
                return;

            Vector3 scale = _zone.transform.lossyScale;
            Vector3 half = Vector3.Scale(_zone.size * .5f,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            int count = Physics.OverlapBoxNonAlloc(
                _zone.transform.TransformPoint(_zone.center),
                half,
                _overlaps,
                _zone.transform.rotation,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider candidate = _overlaps[i];
                if (candidate == null || candidate.GetComponentInParent<FragileCargo>() != _cargo) continue;
                _nextDamageTime = NetworkManager.ServerTime.Time + _cooldownSeconds;
                _cargo.ApplyDamageServer(_damage, _damageReason);
                break;
            }
            Array.Clear(_overlaps, 0, count);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_zone == null) _zone = GetComponentInChildren<BoxCollider>(true);
            _damage = Mathf.Max(1, _damage);
            _cooldownSeconds = Mathf.Max(.1f, _cooldownSeconds);
        }

        private void OnDrawGizmosSelected()
        {
            if (_zone == null) return;
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = _zone.transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, .15f, .05f, .22f);
            Gizmos.DrawCube(_zone.center, _zone.size);
            Gizmos.color = new Color(1f, .35f, .05f, .9f);
            Gizmos.DrawWireCube(_zone.center, _zone.size);
            Gizmos.matrix = previous;
        }
#endif
    }
}
