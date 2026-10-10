using System;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Networked collapsing stepping stone for precision parkour.
    /// Shakes upon player contact, drops after a brief warning, and respawns safely.
    /// Physics adjustments run strictly in FixedUpdate().
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class CollapsingSteppingStone : NetworkBehaviour
    {
        public enum StoneState : byte
        {
            Solid = 0,
            Warning = 1,
            Collapsed = 2
        }

        [Header("Timing")]
        [SerializeField, Min(0.1f)] private float _warningSeconds = 0.65f;
        [SerializeField, Min(0.5f)] private float _collapsedSeconds = 2.5f;

        [Header("Components")]
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private Collider _solidCollider;
        [SerializeField] private Renderer _stoneRenderer;
        [SerializeField] private Color _solidColor = new Color(0.7f, 0.7f, 0.7f);
        [SerializeField] private Color _warningColor = new Color(1.0f, 0.35f, 0.1f);
        [SerializeField] private Color _collapsedColor = new Color(0.3f, 0.1f, 0.1f, 0.2f);

        [Header("Motion")]
        [SerializeField] private float _shakeMagnitude = 0.04f;
        [SerializeField] private float _dropDistance = 6.0f;

        private readonly NetworkVariable<StoneState> _state = new(StoneState.Solid);
        private readonly NetworkVariable<double> _stateChangeTime = new(0);

        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private MaterialPropertyBlock _propBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();
            if (_solidCollider == null) _solidCollider = GetComponent<Collider>();
            if (_stoneRenderer == null) _stoneRenderer = GetComponentInChildren<Renderer>();

            _restPosition = transform.position;
            _restRotation = transform.rotation;

            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = true;
                _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        public override void OnNetworkSpawn()
        {
            _state.OnValueChanged += HandleStateChanged;
            ApplyVisualState(_state.Value);
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(StoneState oldState, StoneState newState)
        {
            ApplyVisualState(newState);
        }

        private void ApplyVisualState(StoneState state)
        {
            if (_stoneRenderer != null)
            {
                Color c = state switch
                {
                    StoneState.Warning => _warningColor,
                    StoneState.Collapsed => _collapsedColor,
                    _ => _solidColor
                };
                _stoneRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetColor(BaseColorId, c);
                _stoneRenderer.SetPropertyBlock(_propBlock);
            }

            if (_solidCollider != null)
            {
                _solidCollider.enabled = (state != StoneState.Collapsed);
            }
        }

        private void FixedUpdate()
        {
            if (!IsSpawned) return;

            StoneState currentState = _state.Value;
            double elapsed = NetworkManager.ServerTime.Time - _stateChangeTime.Value;

            if (IsServer)
            {
                UpdateServerState(currentState, elapsed);
            }

            UpdatePhysicsPose(currentState, (float)elapsed);
        }

        private void UpdateServerState(StoneState currentState, double elapsed)
        {
            if (currentState == StoneState.Warning)
            {
                if (elapsed >= _warningSeconds)
                {
                    _state.Value = StoneState.Collapsed;
                    _stateChangeTime.Value = NetworkManager.ServerTime.Time;
                }
            }
            else if (currentState == StoneState.Collapsed)
            {
                if (elapsed >= _collapsedSeconds)
                {
                    _state.Value = StoneState.Solid;
                    _stateChangeTime.Value = NetworkManager.ServerTime.Time;
                }
            }
        }

        private void UpdatePhysicsPose(StoneState state, float elapsed)
        {
            if (_rigidbody == null) return;

            if (state == StoneState.Solid)
            {
                _rigidbody.MovePosition(_restPosition);
                _rigidbody.MoveRotation(_restRotation);
            }
            else if (state == StoneState.Warning)
            {
                // Rapid high-frequency tremor
                float freq = 35f;
                float shakeX = Mathf.Sin(elapsed * freq) * _shakeMagnitude;
                float shakeZ = Mathf.Cos(elapsed * freq * 1.3f) * _shakeMagnitude;
                _rigidbody.MovePosition(_restPosition + new Vector3(shakeX, 0f, shakeZ));
            }
            else if (state == StoneState.Collapsed)
            {
                float t = Mathf.Clamp01(elapsed / 0.4f);
                float drop = Mathf.SmoothStep(0f, _dropDistance, t);
                _rigidbody.MovePosition(_restPosition - Vector3.up * drop);
            }
        }

        private void OnTriggerEnter(Collider other) => CheckTrigger(other);
        private void OnCollisionEnter(Collision collision) => CheckTrigger(collision.collider);

        private void CheckTrigger(Collider other)
        {
            if (!IsServer || !IsSpawned || _state.Value != StoneState.Solid) return;

            // Trigger when player or cargo touches
            bool isPlayer = other.GetComponentInParent<CoopGame.Player.NetworkPlayer>() != null;
            bool isCargo = other.GetComponentInParent<CoopGame.CarrySystem.FragileCargo>() != null;

            if (isPlayer || isCargo)
            {
                _state.Value = StoneState.Warning;
                _stateChangeTime.Value = NetworkManager.ServerTime.Time;
            }
        }
    }
}
