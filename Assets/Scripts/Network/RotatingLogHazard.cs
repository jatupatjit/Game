using System;
using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Synchronized rotating log obstacle across NGO network.
    /// Uses ServerTime for deterministic, jitter-free client/host synchronization.
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
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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

            if (_logRigidbody != null)
            {
                _logRigidbody.isKinematic = true;
                _logRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _startServerTime.Value = NetworkManager.ServerTime.Time;
            }

            if (_indicatorRenderer != null)
            {
                _indicatorRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetColor(BaseColorId, _activeColor);
                _indicatorRenderer.SetPropertyBlock(_propBlock);
            }
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || _logRigidbody == null) return;

            // Deterministic angle from NGO ServerTime
            double elapsed = Math.Max(0, NetworkManager.ServerTime.Time - _startServerTime.Value);
            float dir = _reverseDirection ? -1f : 1f;
            float currentAngle = (float)(elapsed * _rotationSpeedDegrees * dir) % 360f;

            Vector3 localAxis = _axis switch
            {
                RotationAxis.X => Vector3.right,
                RotationAxis.Y => Vector3.up,
                _ => Vector3.forward
            };

            Quaternion targetRotation = transform.rotation * Quaternion.AngleAxis(currentAngle, localAxis);
            _logRigidbody.MoveRotation(targetRotation);

            // Server-authoritative cargo impact detection
            if (IsServer && _cargo != null && !_cargo.IsDestroyed && !_cargo.IsSecured && _impactZone != null)
            {
                CheckCargoImpact();
            }
        }

        private void CheckCargoImpact()
        {
            if (NetworkManager.ServerTime.Time - _lastDamageTime < 1.0) return;

            Vector3 halfExtents = Vector3.Scale(_impactZone.size * 0.5f, _impactZone.transform.lossyScale);
            int count = Physics.OverlapBoxNonAlloc(
                _impactZone.transform.TransformPoint(_impactZone.center),
                halfExtents,
                _overlapResults,
                _impactZone.transform.rotation,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < count; i++)
            {
                var col = _overlapResults[i];
                if (col != null && col.GetComponentInParent<FragileCargo>() == _cargo)
                {
                    _lastDamageTime = NetworkManager.ServerTime.Time;
                    _cargo.ApplyDamageServer(_cargoImpactDamage, "Rotating Log Hazard");
                    break;
                }
            }
            Array.Clear(_overlapResults, 0, count);
        }
    }
}
