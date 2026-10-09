using System;
using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>Server-clock obstacle cycle. Physics, contact sampling and collision changes stay on the fixed step.</summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class NetworkRouteHazard : NetworkBehaviour
    {
        public enum HazardKind { SwingHammer, Crusher, CollapsingDeck }
        [SerializeField] private HazardKind _kind;
        [SerializeField] private Rigidbody _primary;
        [SerializeField] private Rigidbody _secondary;
        [SerializeField] private BoxCollider _hitA;
        [SerializeField] private BoxCollider _hitB;
        [SerializeField] private BoxCollider _restoreVolume;
        [SerializeField] private Renderer _indicator;
        [SerializeField] private Renderer _surface;
        [SerializeField] private TextMesh _label;
        [SerializeField] private FragileCargo _cargo;
        [SerializeField] private Vector3 _primaryRest;
        [SerializeField] private Vector3 _secondaryRest;
        [SerializeField] private float _travel = 2.3f;
        [SerializeField] private float _swingAngle = 65f;
        [SerializeField, Min(1f)] private float _safeSeconds = 6f;
        [SerializeField, Min(.5f)] private float _warningSeconds = 1.75f;
        [SerializeField, Min(1f)] private float _activeSeconds = 2.5f;
        [SerializeField, Min(1)] private int _damage = 10;
        private readonly NetworkVariable<double> _epoch = new(0);
        private readonly NetworkVariable<byte> _phase = new(0);
        private readonly Collider[] _overlaps = new Collider[64];
        private MaterialPropertyBlock _properties;
        private long _hitCycle = -1;
        private int _lastDisplay = -1;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public HazardKind Kind => _kind;
        public byte Phase => IsSpawned ? _phase.Value : (byte)0;
        // These moving solids use a scripted hit. The cargo must not also charge impact/drag damage for that same contact.
        public bool OwnsContactDamage => _kind != HazardKind.CollapsingDeck;
        public bool OwnsColliderContact(Collider collider) => OwnsContactDamage && collider != null &&
            (collider.attachedRigidbody == _primary || (_secondary != null && collider.attachedRigidbody == _secondary));

        private void Awake() => _properties = new MaterialPropertyBlock();

        public override void OnNetworkSpawn()
        {
            _lastDisplay = -1;
            _hitCycle = -1;
            if (IsServer) { _epoch.Value = NetworkManager.ServerTime.Time; _phase.Value = 0; }
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || _primary == null) return;
            double duration = _safeSeconds + _warningSeconds + _activeSeconds;
            double elapsed = Math.Max(0, NetworkManager.ServerTime.Time - _epoch.Value);
            long cycle = (long)Math.Floor(elapsed / duration);
            double within = elapsed - cycle * duration;
            if (IsServer)
            {
                byte next = within < _safeSeconds ? (byte)0 : within < _safeSeconds + _warningSeconds ? (byte)1 : (byte)2;
                if (_kind == HazardKind.CollapsingDeck && (_phase.Value == 3 || (_phase.Value == 2 && next == 0)))
                {
                    if (RestoreAreaOccupied()) next = 3;
                    else { _epoch.Value = NetworkManager.ServerTime.Time; next = 0; within = 0; cycle = 0; }
                }
                if (_phase.Value != next) _phase.Value = next;
            }

            float active = Mathf.Clamp01((float)((within - _safeSeconds - _warningSeconds) / _activeSeconds));
            ApplyPhysicsPose(Phase, active);
            if (IsServer && Phase == 2 && _kind != HazardKind.CollapsingDeck && cycle != _hitCycle &&
                _cargo != null && !_cargo.IsDestroyed && !_cargo.IsSecured && (TouchesCargo(_hitA) || TouchesCargo(_hitB)))
            {
                _hitCycle = cycle;
                _cargo.ApplyDamageServer(_damage, _kind == HazardKind.SwingHammer ? "Swing hammer" : "Stone crusher");
            }
            Present(Phase);
        }

        private void ApplyPhysicsPose(byte phase, float active)
        {
            if (_kind == HazardKind.SwingHammer)
            {
                // Park outside the lane during safe/warning; sweep out and back once per activation.
                float angle = phase == 2 ? _swingAngle * Mathf.Cos(active * Mathf.PI * 2f) : _swingAngle;
                _primary.MoveRotation(transform.rotation * Quaternion.AngleAxis(angle, Vector3.forward));
            }
            else if (_kind == HazardKind.Crusher)
            {
                float extension = phase == 2 ? Mathf.Sin(active * Mathf.PI) * _travel : 0f;
                _primary.MovePosition(transform.TransformPoint(_primaryRest + Vector3.right * extension));
                if (_secondary != null) _secondary.MovePosition(transform.TransformPoint(_secondaryRest - Vector3.right * extension));
            }
            else
            {
                bool dropped = phase >= 2;
                if (_hitA != null) _hitA.enabled = !dropped;
                _primary.MovePosition(transform.TransformPoint(_primaryRest - Vector3.up * (dropped ? .8f : 0f)));
            }
        }

        private bool TouchesCargo(BoxCollider box)
        {
            if (box == null || !box.enabled) return false;
            Vector3 scale = box.transform.lossyScale;
            Vector3 half = Vector3.Scale(box.size * .5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            int count = Physics.OverlapBoxNonAlloc(box.transform.TransformPoint(box.center), half + Vector3.one * .08f,
                _overlaps, box.transform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < count; i++)
                if (_overlaps[i] != null && _overlaps[i].GetComponentInParent<FragileCargo>() == _cargo) { found = true; break; }
            Array.Clear(_overlaps, 0, count);
            return found;
        }

        private bool RestoreAreaOccupied()
        {
            if (_restoreVolume == null) return true; // Missing safety wiring must not restore a floor through players.
            // Test actual cargo/player extents too; a crate can overlap the opening with its centre already outside it.
            Vector3 scale = _restoreVolume.transform.lossyScale;
            Vector3 half = Vector3.Scale(_restoreVolume.size * .5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            int count = Physics.OverlapBoxNonAlloc(_restoreVolume.transform.TransformPoint(_restoreVolume.center),half,
                _overlaps,_restoreVolume.transform.rotation,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            bool occupied = count == _overlaps.Length; // A saturated query must fail closed.
            for (int i = 0; i < count && !occupied; i++)
                if (_overlaps[i] != null && (_overlaps[i].GetComponentInParent<FragileCargo>() != null ||
                    _overlaps[i].GetComponentInParent<CoopGame.Player.NetworkPlayer>() != null)) occupied = true;
            Array.Clear(_overlaps,0,count);
            if (occupied) return true;
            if (_cargo != null && !_cargo.IsDestroyed && Contains(_cargo.transform.position)) return true;
            var clients = NetworkManager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                var player = clients[i].PlayerObject;
                if (player != null && player.IsSpawned && Contains(player.transform.position)) return true;
            }
            return false;
        }

        private bool Contains(Vector3 point)
        {
            Vector3 local = _restoreVolume.transform.InverseTransformPoint(point) - _restoreVolume.center;
            Vector3 half = _restoreVolume.size * .5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        private void Present(byte phase)
        {
            if (_lastDisplay == phase) return;
            _lastDisplay = phase;
            Color color = phase == 0 ? new Color(.15f,.85f,.35f) :
                phase == 1 ? new Color(1f,.65f,.05f) : new Color(1f,.12f,.05f);
            Paint(_indicator,color);
            Paint(_surface,color);
            if (_label == null) return;
            if (_kind == HazardKind.SwingHammer)
                _label.text = phase == 0 ? "ค้อนหยุดแล้ว ขนลังผ่านได้" : phase == 1 ? "ค้อนจะเหวี่ยงแล้ว รอก่อน" : "รอค้อนผ่าน แล้วค่อยไป";
            else if (_kind == HazardKind.Crusher)
                _label.text = phase == 0 ? "ทางเปิดแล้ว ขนลังผ่านได้" : phase == 1 ? "หินจะหนีบแล้ว ถอยมารอก่อน" : "รอหินแยก แล้วค่อยไป";
            else
                _label.text = phase == 0 ? "ข้ามได้เลย อย่าหยุดกลางสะพาน" : phase == 1 ? "พื้นจะยุบแล้ว ถอยไปจุดพัก" :
                    phase == 3 ? "ออกจากใต้สะพานก่อน\nพื้นถึงจะกลับมา" : "พื้นยุบแล้ว ใช้ทางลาดข้างสะพาน";
        }

        private void Paint(Renderer target,Color color)
        {
            if (target == null) return;
            target.GetPropertyBlock(_properties);
            _properties.SetColor(BaseColor,color);
            target.SetPropertyBlock(_properties);
        }
    }
}
