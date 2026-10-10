using System;
using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Server-clock rotating log or timed wooden sweeper.
    /// Every peer derives its pose from the replicated start time.
    /// All Rigidbody movement strictly executes in FixedUpdate().
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class RotatingLogHazard : NetworkBehaviour
    {
        public enum RotationAxis { X, Y, Z }

        [Header("Hazard Configuration")]
        [SerializeField] private Rigidbody _logRigidbody;
        [SerializeField] private RotationAxis _axis = RotationAxis.Z;
        [SerializeField] private float _rotationSpeedDegrees = 90f; // degrees per second
        [SerializeField] private bool _reverseDirection = false;
        [Tooltip("Zero preserves continuous rotation; positive values add a parked waiting phase.")]
        [SerializeField, Min(0f)] private float _safeSeconds;
        [SerializeField, Min(.25f)] private float _warningSeconds = 2f;
        [SerializeField, Min(1f)] private float _sweepSeconds = 5f;
        [SerializeField] private float _parkAngle = 90f;

        [Header("Damage & Impact")]
        [SerializeField] private FragileCargo _cargo;
        [SerializeField, Min(1)] private int _cargoImpactDamage = 10;
        [SerializeField] private BoxCollider _impactZone;

        [Header("Visual Feedback")]
        [SerializeField] private Renderer _indicatorRenderer;
        [SerializeField] private Color _activeColor = new Color(1f, 0.4f, 0.1f);

        private readonly NetworkVariable<double> _startServerTime = new(0);
        private readonly Collider[] _overlapResults = new Collider[32];
        private MaterialPropertyBlock _propBlock;
        private double _lastDamageTime = 0;
        private long _lastHitCycle = -1;
        private bool _physicsReady;
        private int _shownPhase = -1;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        public bool OwnsColliderContact(Collider collider) => collider != null &&
            _logRigidbody != null && collider.attachedRigidbody == _logRigidbody;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            if (_logRigidbody == null)
            {
                _logRigidbody = GetComponent<Rigidbody>();
                if (_logRigidbody == null)
                {
                    _logRigidbody = GetComponentInChildren<Rigidbody>();
                }
            }
        }

        public override void OnNetworkSpawn()
        {
            _physicsReady = false;
            _lastDamageTime = double.NegativeInfinity;
            _lastHitCycle = -1;
            _shownPhase = -1;
            if (IsServer)
            {
                _startServerTime.Value = NetworkManager.ServerTime.Time;
            }
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || _logRigidbody == null) return;
            if (!_physicsReady)
            {
                _logRigidbody.isKinematic = true;
                _logRigidbody.useGravity = false;
                _logRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                _logRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                _physicsReady = true;
            }

            // Deterministic angle from NGO ServerTime
            double elapsed = Math.Max(0, NetworkManager.ServerTime.Time - _startServerTime.Value);
            float dir = _reverseDirection ? -1f : 1f;
            float currentAngle;
            int phase = 2;
            long cycle = -1;
            if (_safeSeconds > 0f)
            {
                double duration = _safeSeconds + _warningSeconds + _sweepSeconds;
                cycle = (long)Math.Floor(elapsed / duration);
                double within = elapsed - cycle * duration;
                phase = within < _safeSeconds ? 0 : within < _safeSeconds + _warningSeconds ? 1 : 2;
                currentAngle = _parkAngle + (phase == 2 ?
                    Mathf.Clamp01((float)((within - _safeSeconds - _warningSeconds) / _sweepSeconds)) * 360f * dir : 0f);
            }
            else currentAngle = (float)((elapsed * _rotationSpeedDegrees * dir) % 360.0);

            Vector3 localAxis = _axis switch
            {
                RotationAxis.X => Vector3.right,
                RotationAxis.Y => Vector3.up,
                _ => Vector3.forward
            };

            Quaternion targetRotation = transform.rotation * Quaternion.AngleAxis(currentAngle, localAxis);
            _logRigidbody.MoveRotation(targetRotation);
            Present(phase);

            // Server-authoritative cargo impact detection
            if (IsServer && phase == 2 && (_safeSeconds <= 0f || cycle != _lastHitCycle) &&
                _cargo != null && !_cargo.IsDestroyed && !_cargo.IsSecured && _impactZone != null &&
                _impactZone.enabled && _impactZone.gameObject.activeInHierarchy)
            {
                CheckCargoImpact(targetRotation, cycle);
            }
        }

        private void Present(int phase)
        {
            if (_shownPhase == phase) return;
            _shownPhase = phase;
            if (_indicatorRenderer == null) return;
            _indicatorRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(BaseColorId, phase == 0 ? new Color(.15f,.85f,.35f) :
                phase == 1 ? new Color(1f,.65f,.05f) : _activeColor);
            _indicatorRenderer.SetPropertyBlock(_propBlock);
        }

        private void CheckCargoImpact(Quaternion targetRotation, long cycle)
        {
            if (NetworkManager.ServerTime.Time - _lastDamageTime < 1.0) return;

            Vector3 scale = _impactZone.transform.lossyScale;
            Vector3 halfExtents = Vector3.Scale(_impactZone.size * 0.5f,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            // MoveRotation queues the new pose; query that pose rather than the previous rendered transform.
            Quaternion delta = targetRotation * Quaternion.Inverse(_logRigidbody.rotation);
            Vector3 center = _logRigidbody.position + delta *
                (_impactZone.transform.TransformPoint(_impactZone.center) - _logRigidbody.position);
            int count = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                _overlapResults,
                delta * _impactZone.transform.rotation,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < count; i++)
            {
                var col = _overlapResults[i];
                if (col != null && col.GetComponentInParent<FragileCargo>() == _cargo)
                {
                    _lastDamageTime = NetworkManager.ServerTime.Time;
                    _lastHitCycle = cycle;
                    _cargo.ApplyDamageServer(_cargoImpactDamage, "Rotating wooden beam");
                    break;
                }
            }
            Array.Clear(_overlapResults, 0, count);
        }
    }
}
