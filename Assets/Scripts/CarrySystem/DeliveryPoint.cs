using System;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// DeliveryPoint marks the final destination (e.g. Castle Entrance) for the stage.
    /// 
    /// Specification Requirements:
    /// 1. Trigger zone detecting FragileCargo.
    /// 2. Score Formula: Remaining HP * Multiplier
    ///    - Stage 1: Multiplier = 2x
    ///    - Stage 2: Multiplier = 3x
    ///    - Stage 3: Multiplier = 5x
    /// 3. Score Summary & Handoff data pipeline for the Shop System (3 item slots).
    /// 4. Celebratory effects and Server-authoritative validation.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public class DeliveryPoint : NetworkBehaviour
    {
        [System.Serializable]
        public struct DeliveryResult : INetworkSerializable
        {
            public int StageNumber;
            public int RemainingHP;
            public int Multiplier;
            public int FinalScore;
            public int CoinsEarned;
            public bool IsSuccess;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref StageNumber);
                serializer.SerializeValue(ref RemainingHP);
                serializer.SerializeValue(ref Multiplier);
                serializer.SerializeValue(ref FinalScore);
                serializer.SerializeValue(ref CoinsEarned);
                serializer.SerializeValue(ref IsSuccess);
            }
        }

        [System.Serializable]
        public class ShopSlotData
        {
            public string ItemId;
            public string ItemName;
            public string Description;
            public int Price;
            public Sprite Icon;
        }

        [Header("Stage Configuration")]
        [Tooltip("Current stage number (1, 2, or 3)")]
        [Range(1, 3)]
        [SerializeField] private int _stageNumber = 1;

        [Tooltip("Multiplier per stage: Stage 1 = 2x, Stage 2 = 3x, Stage 3 = 5x")]
        [SerializeField] private int _stage1Multiplier = 2;
        [SerializeField] private int _stage2Multiplier = 3;
        [SerializeField] private int _stage3Multiplier = 5;

        [Header("Shop Catalog (Inventory has 3 slots)")]
        [Tooltip("Items available in the Shop upon stage completion")]
        [SerializeField] private ShopSlotData[] _shopSlots = new ShopSlotData[2]
        {
            new ShopSlotData { ItemId = "stamina_elixir", ItemName = "Stamina Elixir", Description = "Restore full stamina once", Price = 40 },
            new ShopSlotData { ItemId = "cargo_padding", ItemName = "Cargo Padding", Description = "Reduce the next cargo hit by 50%; use while carrying", Price = 60 }
        };

        [Header("Celebration & Audio Visuals")]
        [Tooltip("Particle system triggered on successful delivery")]
        [SerializeField] private ParticleSystem _celebrationFx;

        [Tooltip("Audio clip played upon delivery success")]
        [SerializeField] private AudioClip _deliveryFanfareClip;

        [Tooltip("Zone indicator color in scene")]
        [SerializeField] private Color _zoneColor = new Color(0.2f, 1.0f, 0.4f, 0.4f);

        // State
        private bool _isDelivered = false;
        private FragileCargo _pendingCargo;
        private Collider _deliveryCollider;
        private readonly NetworkVariable<bool> _syncedDelivered = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private DeliveryResult _lastDeliveryResult;
        private readonly NetworkVariable<DeliveryResult> _syncedResult = new(default);
        public override void OnNetworkSpawn()
        {
            _syncedResult.OnValueChanged += ReceiveResult;
            _lastDeliveryResult = _syncedResult.Value;
        }

        public override void OnNetworkDespawn() => _syncedResult.OnValueChanged -= ReceiveResult;

        private void ReceiveResult(DeliveryResult previous, DeliveryResult result)
        {
            _lastDeliveryResult = result;
            if (!result.IsSuccess) return;
            OnDelivered?.Invoke(result);
            OnDeliverySuccessGlobal?.Invoke(result);
        }

        // Events
        public static event Action<DeliveryResult> OnDeliverySuccessGlobal;
        public event Action<DeliveryResult> OnDelivered;

        public int CurrentStage
        {
            get => _stageNumber;
            set => _stageNumber = Mathf.Clamp(value, 1, 3);
        }

        public int CurrentMultiplier
        {
            get
            {
                return _stageNumber switch
                {
                    1 => _stage1Multiplier,
                    2 => _stage2Multiplier,
                    3 => _stage3Multiplier,
                    _ => 2
                };
            }
        }

        public DeliveryResult LastDeliveryResult => _lastDeliveryResult;
        public ShopSlotData[] AvailableShopSlots => _shopSlots;
        public bool IsDelivered => IsSpawned ? _syncedDelivered.Value : _isDelivered;

        private void Awake()
        {
            // Ensure collider is configured as a trigger
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
                _deliveryCollider = col;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
            if (isNetworked && !IsServer) return;

            if (_isDelivered || _pendingCargo != null) return;

            // Check if entered object is FragileCargo
            FragileCargo cargo = other.GetComponentInParent<FragileCargo>();
            if (cargo == null || cargo.IsDestroyed) return;

            _pendingCargo = cargo;
        }

        private void FixedUpdate()
        {
            if (_pendingCargo == null || _isDelivered) return;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer) return;

            FragileCargo cargo = _pendingCargo;
            _pendingCargo = null;
            Collider cargoCollider = cargo.GetComponentInChildren<Collider>();
            if (cargo.IsDestroyed || cargo.CurrentHP.Value <= 0 ||
                _deliveryCollider == null || cargoCollider == null ||
                !_deliveryCollider.bounds.Intersects(cargoCollider.bounds))
                return;

            ProcessDelivery(cargo);
        }

        /// <summary>
        /// Server-authoritative delivery validation and scoring calculation.
        /// </summary>
        private void ProcessDelivery(FragileCargo cargo)
        {
            _isDelivered = true;

            int remainingHP = cargo.CurrentHP.Value;
            int multiplier = CurrentMultiplier;
            int finalScore = remainingHP * multiplier;
            int coinsEarned = finalScore; // 1:1 score to currency conversion for the Shop

            _lastDeliveryResult = new DeliveryResult
            {
                StageNumber = _stageNumber,
                RemainingHP = remainingHP,
                Multiplier = multiplier,
                FinalScore = finalScore,
                CoinsEarned = coinsEarned,
                IsSuccess = true
            };

            Debug.Log($"<color=lime><b>[DeliveryPoint] STAGE {_stageNumber} DELIVERED!</b></color> " +
                      $"Remaining HP: {remainingHP} | Multiplier: {multiplier}x | Score: {finalScore} | Coins: {coinsEarned}");

            // Detach carriers and secure the cargo at delivery spot
            cargo.SecureForDelivery();

            // Fire events
            if (IsSpawned && IsServer)
            {
                _syncedResult.Value = _lastDeliveryResult;
                _syncedDelivered.Value = true;
            }
            else ReceiveResult(default, _lastDeliveryResult);

            // Broadcast celebration to all clients
            if (IsSpawned) DeliverySuccessRpc(_lastDeliveryResult, transform.position);
            else PlayDeliveryFeedback(transform.position);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DeliverySuccessRpc(DeliveryResult result, Vector3 deliveryPoint)
        {
            PlayDeliveryFeedback(deliveryPoint);
        }

        private void PlayDeliveryFeedback(Vector3 deliveryPoint)
        {
            if (_celebrationFx != null)
            {
                _celebrationFx.Play();
            }
            else
            {
                CoopGame.Network.GameplayFeedback.Burst(deliveryPoint + Vector3.up, Color.yellow, 60);
            }

            if (_deliveryFanfareClip != null)
            {
                AudioSource.PlayClipAtPoint(_deliveryFanfareClip, deliveryPoint, 1.0f);
            }
            else CoopGame.Network.GameplayFeedback.Play(CoopGame.Network.GameplayFeedback.Cue.Delivered, deliveryPoint);
        }

        private void SpawnProceduralConfetti(Vector3 point)
        {
            GameObject confettiObj = new GameObject("Delivery_Confetti_VFX");
            confettiObj.transform.position = point + Vector3.up * 1.0f;

            ParticleSystem ps = confettiObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 2.5f;
            main.startSpeed = 6.0f;
            main.startSize = 0.35f;
            main.startColor = new ParticleSystem.MinMaxGradient(Color.yellow, Color.cyan);
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 60) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.5f;

            ps.Play();
            Destroy(confettiObj, 4.0f);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _zoneColor;
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                if (col is BoxCollider box)
                {
                    Gizmos.DrawCube(box.center, box.size);
                    Gizmos.DrawWireCube(box.center, box.size);
                }
                else if (col is SphereCollider sphere)
                {
                    Gizmos.DrawSphere(sphere.center, sphere.radius);
                }
            }
        }
    }
}
