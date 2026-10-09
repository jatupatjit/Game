using System;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Two-Player Timed Pressure Switch Gate.
    /// Requires two players to activate separate pressure plates within a short time window.
    /// Once synchronized, opens the gate / extends the bridge.
    /// All Rigidbody movement executes in FixedUpdate().
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class TwoPlayerTimedSwitchGate : NetworkBehaviour
    {
        [Header("Gate / Bridge Mechanism")]
        [SerializeField] private Rigidbody _gateRigidbody;
        [SerializeField] private Vector3 _closedPosition;
        [SerializeField] private Vector3 _openPosition;
        [SerializeField] private float _moveSpeed = 3.0f;

        [Header("Pressure Plates")]
        [SerializeField] private BoxCollider _plateACollider;
        [SerializeField] private BoxCollider _plateBCollider;
        [SerializeField] private Renderer _plateARenderer;
        [SerializeField] private Renderer _plateBRenderer;

        [Header("Co-op Timing")]
        [Tooltip("Max seconds allowed between activating Switch A and Switch B")]
        [SerializeField, Min(1f)] private float _syncWindowSeconds = 4.5f;

        [Header("Feedback")]
        [SerializeField] private TextMesh _statusLabel;
        [SerializeField] private Color _idleColor = new Color(0.2f, 0.6f, 1.0f);
        [SerializeField] private Color _primedColor = new Color(1.0f, 0.7f, 0.1f);
        [SerializeField] private Color _unlockedColor = new Color(0.15f, 0.85f, 0.35f);

        private readonly NetworkVariable<bool> _plateAActive = new(false);
        private readonly NetworkVariable<bool> _plateBActive = new(false);
        private readonly NetworkVariable<bool> _gateUnlocked = new(false);
        private readonly NetworkVariable<double> _plateATime = new(0);
        private readonly NetworkVariable<double> _plateBTime = new(0);

        private readonly Collider[] _overlaps = new Collider[16];
        private MaterialPropertyBlock _propBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public bool IsGateUnlocked => IsSpawned && _gateUnlocked.Value;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            if (_gateRigidbody != null)
            {
                _gateRigidbody.isKinematic = true;
                _gateRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        public override void OnNetworkSpawn()
        {
            _gateUnlocked.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
            _plateAActive.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
            _plateBActive.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
            UpdateVisuals();
        }

        private void FixedUpdate()
        {
            if (!IsSpawned) return;

            if (IsServer)
            {
                UpdateServerSwitches();
            }

            // Animate gate position smoothly in FixedUpdate
            if (_gateRigidbody != null)
            {
                Vector3 target = _gateUnlocked.Value ? _openPosition : _closedPosition;
                Vector3 current = _gateRigidbody.position;
                Vector3 next = Vector3.MoveTowards(current, target, _moveSpeed * Time.fixedDeltaTime);
                _gateRigidbody.MovePosition(next);
            }
        }

        private void UpdateServerSwitches()
        {
            if (_gateUnlocked.Value) return;

            bool occupiedA = IsPlateOccupied(_plateACollider);
            bool occupiedB = IsPlateOccupied(_plateBCollider);
            double now = NetworkManager.ServerTime.Time;

            if (occupiedA && !_plateAActive.Value)
            {
                _plateAActive.Value = true;
                _plateATime.Value = now;
            }

            if (occupiedB && !_plateBActive.Value)
            {
                _plateBActive.Value = true;
                _plateBTime.Value = now;
            }

            // Timeout checks
            if (_plateAActive.Value && !occupiedA && (now - _plateATime.Value > _syncWindowSeconds))
            {
                _plateAActive.Value = false;
            }

            if (_plateBActive.Value && !occupiedB && (now - _plateBTime.Value > _syncWindowSeconds))
            {
                _plateBActive.Value = false;
            }

            // Unlocked condition: Both switches active within time window
            if (_plateAActive.Value && _plateBActive.Value)
            {
                _gateUnlocked.Value = true;
            }
        }

        private bool IsPlateOccupied(BoxCollider plate)
        {
            if (plate == null) return false;
            Vector3 center = plate.transform.TransformPoint(plate.center);
            Vector3 halfExtents = Vector3.Scale(plate.size * 0.5f, plate.transform.lossyScale);

            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, _overlaps, plate.transform.rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            bool occupied = false;
            for (int i = 0; i < count; i++)
            {
                if (_overlaps[i] != null && _overlaps[i].GetComponentInParent<CoopGame.Player.NetworkPlayer>() != null)
                {
                    occupied = true;
                    break;
                }
            }
            Array.Clear(_overlaps, 0, count);
            return occupied;
        }

        private void UpdateVisuals()
        {
            if (_plateARenderer != null)
            {
                Color cA = _gateUnlocked.Value ? _unlockedColor : (_plateAActive.Value ? _primedColor : _idleColor);
                Paint(_plateARenderer, cA);
            }

            if (_plateBRenderer != null)
            {
                Color cB = _gateUnlocked.Value ? _unlockedColor : (_plateBActive.Value ? _primedColor : _idleColor);
                Paint(_plateBRenderer, cB);
            }

            if (_statusLabel != null)
            {
                if (_gateUnlocked.Value)
                {
                    _statusLabel.text = "สะพานเปิดแล้ว! ขนลังข้ามได้เลย";
                }
                else if (_plateAActive.Value && !_plateBActive.Value)
                {
                    _statusLabel.text = "สวิตช์ A พร้อม! ให้เพื่อนเหยียบสวิตช์ B ด่วน!";
                }
                else if (_plateBActive.Value && !_plateAActive.Value)
                {
                    _statusLabel.text = "สวิตช์ B พร้อม! ให้เพื่อนเหยียบสวิตช์ A ด่วน!";
                }
                else
                {
                    _statusLabel.text = "เหยียบสวิตช์ทั้งสองจุดพร้อมกันเพื่อเปิดสะพาน";
                }
            }
        }

        private void Paint(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(BaseColorId, c);
            r.SetPropertyBlock(_propBlock);
        }
    }
}
