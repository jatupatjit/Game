using System;
using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Reusable server-clock controller for medieval prototype mechanisms.
    /// Rigidbody poses and cargo contact checks execute only in FixedUpdate.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class MedievalMechanismHazard : NetworkBehaviour
    {
        public enum MechanismKind { RisingPosts, BarrelRamp }

        [Header("Mechanism")]
        [SerializeField] private MechanismKind _kind;
        [SerializeField] private Rigidbody[] _movingBodies = Array.Empty<Rigidbody>();
        [Tooltip("Local resting position for each moving body. Auto-filled from the prefab when omitted.")]
        [SerializeField] private Vector3[] _restLocalPositions = Array.Empty<Vector3>();
        [SerializeField] private Vector3 _travelVector = new(0f, 1.4f, 0f);
        [SerializeField, Min(0f)] private float _rollDegrees = 720f;

        [Header("Cycle")]
        [SerializeField, Min(.25f)] private float _safeSeconds = 4f;
        [SerializeField, Min(.25f)] private float _warningSeconds = 1.5f;
        [SerializeField, Min(.5f)] private float _activeSeconds = 3f;
        [SerializeField, Min(0f)] private float _phaseOffset;

        [Header("Cargo Impact")]
        [SerializeField] private FragileCargo _cargo;
        [SerializeField] private BoxCollider _hitZone;
        [SerializeField, Min(0)] private int _damage = 10;

        [Header("Feedback")]
        [SerializeField] private Renderer _indicator;
        [SerializeField] private Color _safeColor = new(.15f, .85f, .35f);
        [SerializeField] private Color _warningColor = new(1f, .65f, .05f);
        [SerializeField] private Color _activeColor = new(1f, .18f, .05f);

        private readonly NetworkVariable<double> _epoch = new(0);
        private readonly NetworkVariable<byte> _phase = new(0);
        private readonly Collider[] _overlaps = new Collider[32];
        private Quaternion[] _restLocalRotations = Array.Empty<Quaternion>();
        private MaterialPropertyBlock _properties;
        private long _lastDamageCycle = -1;
        private byte _lastShownPhase = byte.MaxValue;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public MechanismKind Kind => _kind;
        public byte Phase => IsSpawned ? _phase.Value : (byte)0;

        private void Awake()
        {
            _properties = new MaterialPropertyBlock();
            _restLocalRotations = new Quaternion[_movingBodies.Length];
            _restLocalPositions = new Vector3[_movingBodies.Length];
            for (int i = 0; i < _movingBodies.Length; i++)
            {
                if (_movingBodies[i] == null) continue;
                _restLocalRotations[i] = Quaternion.Inverse(transform.rotation) * _movingBodies[i].rotation;
                _restLocalPositions[i] = transform.InverseTransformPoint(_movingBodies[i].position);
            }
        }

        public override void OnNetworkSpawn()
        {
            _lastDamageCycle = -1;
            _lastShownPhase = byte.MaxValue;
            for (int i = 0; i < _movingBodies.Length; i++)
            {
                Rigidbody body = _movingBodies[i];
                if (body == null) continue;
                body.isKinematic = true;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            if (!IsServer) return;
            if (_cargo == null) _cargo = FindAnyObjectByType<FragileCargo>();
            _epoch.Value = NetworkManager.ServerTime.Time;
            _phase.Value = 0;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || _movingBodies.Length == 0) return;

            double duration = _safeSeconds + _warningSeconds + _activeSeconds;
            double elapsed = Math.Max(0, NetworkManager.ServerTime.Time - _epoch.Value + _phaseOffset);
            long cycle = (long)Math.Floor(elapsed / duration);
            double within = elapsed - cycle * duration;
            byte next = within < _safeSeconds ? (byte)0 : within < _safeSeconds + _warningSeconds ? (byte)1 : (byte)2;
            if (IsServer && _phase.Value != next) _phase.Value = next;

            float progress = Mathf.Clamp01((float)((within - _safeSeconds - _warningSeconds) / _activeSeconds));
            ApplyPose(Phase, progress);
            if (IsServer && Phase == 2 && cycle != _lastDamageCycle && TouchesCargo())
            {
                _lastDamageCycle = cycle;
                if (_damage > 0) _cargo.ApplyDamageServer(_damage, _kind == MechanismKind.BarrelRamp ? "Rolling barrel" : "Rising posts");
            }
            Present(Phase);
        }

        private void ApplyPose(byte phase, float progress)
        {
            if (_kind == MechanismKind.BarrelRamp)
            {
                Rigidbody body = _movingBodies[0];
                if (body == null) return;
                Vector3 rest = Rest(0);
                float t = phase == 2 ? Mathf.SmoothStep(0f, 1f, progress) : 0f;
                body.MovePosition(transform.TransformPoint(rest + _travelVector * t));
                Quaternion restRotation = _restLocalRotations.Length > 0 ? _restLocalRotations[0] : Quaternion.identity;
                body.MoveRotation(transform.rotation * Quaternion.AngleAxis(_rollDegrees * t, Vector3.right) * restRotation);
                return;
            }

            for (int i = 0; i < _movingBodies.Length; i++)
            {
                Rigidbody body = _movingBodies[i];
                if (body == null) continue;
                float offset = _movingBodies.Length > 1 ? i / (float)(_movingBodies.Length - 1) : 0f;
                float wave = phase == 2 ? Mathf.Clamp01(Mathf.Sin((progress * 1.4f - offset * .7f) * Mathf.PI)) : 0f;
                body.MovePosition(transform.TransformPoint(Rest(i) + _travelVector * wave));
            }
        }

        private Vector3 Rest(int index) => index < _restLocalPositions.Length ? _restLocalPositions[index] : Vector3.zero;

        private bool TouchesCargo()
        {
            if (_cargo == null || _cargo.IsDestroyed || _cargo.IsSecured || _hitZone == null || !_hitZone.enabled) return false;
            Vector3 scale = _hitZone.transform.lossyScale;
            Vector3 half = Vector3.Scale(_hitZone.size * .5f,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            int count = Physics.OverlapBoxNonAlloc(_hitZone.transform.TransformPoint(_hitZone.center), half,
                _overlaps, _hitZone.transform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < count; i++)
                if (_overlaps[i] != null && _overlaps[i].GetComponentInParent<FragileCargo>() == _cargo) { found = true; break; }
            Array.Clear(_overlaps, 0, count);
            return found;
        }

        private void Present(byte phase)
        {
            if (_lastShownPhase == phase || _indicator == null) return;
            _lastShownPhase = phase;
            _indicator.GetPropertyBlock(_properties);
            _properties.SetColor(BaseColor, phase == 0 ? _safeColor : phase == 1 ? _warningColor : _activeColor);
            _indicator.SetPropertyBlock(_properties);
        }
    }
}
