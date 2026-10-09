using System;
using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>Warns before a flame pulse; damage is at most once per activation, on the server.</summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class TimedCargoTrap : NetworkBehaviour
    {
        [SerializeField] private BoxCollider _zone;
        [SerializeField] private GameObject _flames;
        [SerializeField] private Renderer _marker;
        [SerializeField] private TextMesh _label;
        [SerializeField] private FragileCargo _cargo;
        [SerializeField, Min(.5f)] private float _safeSeconds = 4f;
        [SerializeField, Min(.5f)] private float _warningSeconds = 1f;
        [SerializeField, Min(.5f)] private float _activeSeconds = 1.5f;
        [SerializeField, Min(0f)] private float _phaseOffset;
        [SerializeField, Min(1)] private int _damage = 10;
        private readonly NetworkVariable<byte> _phase = new(0);
        private readonly NetworkVariable<double> _epoch = new(0);
        private readonly Collider[] _overlaps = new Collider[32];
        private MaterialPropertyBlock _properties;
        private long _lastDamageCycle = -1;
        private int _lastDisplay = -1;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public byte Phase => IsSpawned ? _phase.Value : (byte)0;
        public int DamagePerActivation => _damage;

        private void Awake() => _properties = new MaterialPropertyBlock();

        public override void OnNetworkSpawn()
        {
            _lastDisplay = -1;
            _lastDamageCycle = -1;
            if (IsServer) { _epoch.Value = NetworkManager.ServerTime.Time; _phase.Value = 0; }
        }

        private void FixedUpdate()
        {
            if (IsSpawned && IsServer)
            {
                double duration = _safeSeconds + _warningSeconds + _activeSeconds;
                double elapsed = Math.Max(0, NetworkManager.ServerTime.Time - _epoch.Value + _phaseOffset);
                long cycle = (long)Math.Floor(elapsed / duration);
                double within = elapsed - cycle * duration;
                byte phase = within < _safeSeconds ? (byte)0 : within < _safeSeconds + _warningSeconds ? (byte)1 : (byte)2;
                if (_phase.Value != phase) _phase.Value = phase;
                if (phase == 2 && cycle != _lastDamageCycle && _zone != null && _zone.enabled &&
                    _zone.gameObject.activeInHierarchy && _cargo != null && !_cargo.IsDestroyed && !_cargo.IsSecured)
                {
                    Vector3 scale = _zone.transform.lossyScale;
                    Vector3 half = Vector3.Scale(_zone.size * .5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                    int count = Physics.OverlapBoxNonAlloc(_zone.transform.TransformPoint(_zone.center), half, _overlaps,
                        _zone.transform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; i++)
                    {
                        if (_overlaps[i] == null || _overlaps[i].GetComponentInParent<FragileCargo>() != _cargo) continue;
                        _lastDamageCycle = cycle;
                        _cargo.ApplyDamageServer(_damage, "Timed flame trap");
                        break;
                    }
                    // Do not retain despawned colliders until the next pulse.
                    Array.Clear(_overlaps, 0, count);
                }
            }
            byte display = Phase;
            if (_flames != null && _flames.activeSelf != (display == 2)) _flames.SetActive(display == 2);
            if (_lastDisplay == display) return;
            _lastDisplay = display;
            if (_marker != null)
            {
                _marker.GetPropertyBlock(_properties);
                _properties.SetColor(BaseColor, display == 0 ? new Color(.15f, .85f, .35f) :
                    display == 1 ? new Color(1f, .65f, .05f) : new Color(1f, .12f, .05f));
                _marker.SetPropertyBlock(_properties);
            }
            if (_label != null) _label.text = display == 0 ? "ไฟหยุดแล้ว ขนลังข้ามได้" :
                display == 1 ? "ไฟจะพุ่งแล้ว ถอยมารอก่อน" : "รอไฟหยุด แล้วค่อยไป";
        }
    }
}
