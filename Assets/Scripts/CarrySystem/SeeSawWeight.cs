using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using CoopGame.Obstacles;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Counterweight object specifically designed for See-Saw gameplay puzzles.
    /// 
    /// Gameplay Behavior:
    /// - Can be lifted, carried, placed gently (Right-Click or 'F' key), or thrown (Q).
    /// - Solo liftable: 1 player can lift and carry it.
    /// - Adjustable weight: Inspector or script-accessible WeightMass property, synchronized across network.
    /// - High mass: Weighs down the see-saw (e.g. 2.5x player mass). When placed on Side A, 1 player can safely
    ///   cross Side B to a high ledge without Side B tilting down!
    /// - Plank Anchoring: When placed or dropped on the See-Saw plank, it securely anchors to that spot so it
    ///   will NOT slide or roll off down the incline.
    /// - Repositionable: A player can walk up to the anchored weight at any time and lift it again to change its position!
    /// - Pickup Debounce: When put down, it will NOT be automatically picked back up by itself.
    /// - Fall Respawn: If it falls off or into a pit, it automatically respawns at its designated spawn point GameObject.
    /// </summary>
    [DisallowMultipleComponent]
    public class SeeSawWeight : CarryableObject
    {
        [Header("See-Saw Weight Mechanics")]
        [Tooltip("Weight applied to See-Saw plank (1 player = 1.0). Default is 15.0 so it holds down the see-saw against multiple players and cargo!")]
        [SerializeField, Range(0.5f, 100f)] private float _weightMass = 15.0f;

        [Tooltip("Audio clip played when locking/anchoring onto the see-saw plank.")]
        [SerializeField] private AudioClip _anchorLatchSound;

        [Tooltip("Audio clip played when lifting off the see-saw plank.")]
        [SerializeField] private AudioClip _unanchorSound;

        [Header("Respawn Settings")]
        [Tooltip("Empty GameObject / Transform where this weight respawns if it falls. If not assigned, defaults to initial spawn position or a GameObject named 'SeeSawWeight_SpawnPoint'.")]
        [SerializeField] private Transform _respawnPoint;

        [Tooltip("Y-position below which the weight is considered fallen and respawns.")]
        [SerializeField] private float _fallKillY = -12f;

        [Tooltip("Audio clip played when the weight respawns after falling.")]
        [SerializeField] private AudioClip _respawnSound;

        [Header("Pickup Debounce")]
        [Tooltip("Seconds after being placed or dropped before the weight can be picked up again.")]
        [SerializeField] private float _postPlacePickupCooldown = 0.5f;

        [Header("Visual Anchor Glow / Feedback")]
        [SerializeField] private GameObject _anchorVisualIndicator;

        // Synchronized weight mass
        private readonly NetworkVariable<float> _syncedWeightMass = new NetworkVariable<float>(
            15.0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // Synchronized anchor state
        private readonly NetworkVariable<bool> _isAnchored = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly NetworkVariable<NetworkObjectReference> _anchoredPlankRef = new NetworkVariable<NetworkObjectReference>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly NetworkVariable<Vector3> _anchoredLocalPos = new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly NetworkVariable<Quaternion> _anchoredLocalRot = new NetworkVariable<Quaternion>(
            Quaternion.identity,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private SeeSawPlank _anchoredPlank;
        private Transform _anchoredPlankTransform;
        private AudioSource _audioSource;
        private Vector3 _initialPosition;
        private Quaternion _initialRotation;
        private float _pickupAllowedAfterTime = 0f;

        /// <summary>
        /// Gets or sets the weight multiplier of this counterweight.
        /// 1.0 = equal to 1 player's weight. 2.5 = 2.5x player weight.
        /// Synchronized across network when changed on server/host.
        /// </summary>
        public float WeightMass
        {
            get => (IsSpawned && NetworkManager != null && NetworkManager.IsListening) ? _syncedWeightMass.Value : _weightMass;
            set
            {
                _weightMass = Mathf.Max(0.1f, value);
                if (IsServer && IsSpawned)
                {
                    _syncedWeightMass.Value = _weightMass;
                }
                if (_rigidbody != null)
                {
                    _rigidbody.mass = Mathf.Max(5f, _weightMass * 8f);
                }
            }
        }

        public bool IsAnchored => _isAnchored.Value;

        /// <summary>
        /// Empty GameObject or Transform where the weight respawns if it falls.
        /// </summary>
        public Transform RespawnPoint
        {
            get => _respawnPoint;
            set => _respawnPoint = value;
        }

        /// <summary>
        /// Can always be picked up immediately after putting it down.
        /// </summary>
        public override bool CanBeCarried => base.CanBeCarried;

        public void SetPickupCooldown(float duration = 0.5f) { }

        /// <summary>
        /// Public helper to change weight at runtime from other scripts or triggers.
        /// </summary>
        public void SetWeight(float newWeight)
        {
            WeightMass = newWeight;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _weightMass = Mathf.Max(0.1f, _weightMass);
            if (_rigidbody != null)
            {
                _rigidbody.mass = Mathf.Max(5f, _weightMass * 8f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 target = _respawnPoint != null ? _respawnPoint.position : (Application.isPlaying ? _initialPosition : transform.position);
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            Gizmos.DrawWireCube(target, Vector3.one * 0.7f);
            Gizmos.DrawLine(transform.position, target);
        }
#endif

        protected override void Awake()
        {
            base.Awake();
            _initialPosition = transform.position;
            _initialRotation = transform.rotation;

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.playOnAwake = false;
                _audioSource.spatialBlend = 1f;
                _audioSource.minDistance = 2f;
                _audioSource.maxDistance = 25f;
            }
        }

        private void Start()
        {
            // If no respawn point was assigned, attempt to find an empty GameObject designated for it
            if (_respawnPoint == null)
            {
                GameObject found = GameObject.Find("SeeSawWeight_SpawnPoint");
                if (found == null) found = GameObject.Find("SeeSawWeightSpawnPoint");
                if (found != null)
                {
                    _respawnPoint = found.transform;
                }
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                _syncedWeightMass.Value = _weightMass;
            }
            _isAnchored.OnValueChanged += HandleAnchorChanged;
            UpdateAnchorVisuals(_isAnchored.Value);
            SetPhaseThrough(_isAnchored.Value);
            if (_isAnchored.Value) IgnoreAllCurrentPlayers();
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _isAnchored.OnValueChanged -= HandleAnchorChanged;
        }

        private void HandleAnchorChanged(bool previousValue, bool newValue)
        {
            UpdateAnchorVisuals(newValue);
            SetPhaseThrough(newValue);
            if (newValue)
            {
                IgnoreAllCurrentPlayers();
                if (_rigidbody != null)
                {
                    _rigidbody.isKinematic = true;
                    _rigidbody.useGravity = false;
                }
                if (_anchorLatchSound != null && _audioSource != null)
                {
                    _audioSource.PlayOneShot(_anchorLatchSound, 0.8f);
                }
            }
            else if (previousValue)
            {
                RestoreAllIgnoredCollisions();
                if (_rigidbody != null && (IsServer || !IsSpawned))
                {
                    _rigidbody.isKinematic = false;
                    _rigidbody.useGravity = true;
                }
                if (_unanchorSound != null && _audioSource != null)
                {
                    _audioSource.PlayOneShot(_unanchorSound, 0.7f);
                }
            }
        }

        private readonly Collider[] _nearbyScanBuffer = new Collider[64];
        private readonly HashSet<Collider> _ignoredColliders = new HashSet<Collider>();

        /// <summary>
        /// When anchored on the See-Saw plank, dynamically ignores collision with nearby players
        /// and objects so they can phase directly through the weight without any collision snagging,
        /// while keeping the weight's colliders solid so pickup raycasts, ClosestPoint, and grip math
        /// work with 100% precision.
        /// </summary>
        private void UpdateAnchoredPhasing()
        {
            if (!_isAnchored.Value) return;

            int count = Physics.OverlapSphereNonAlloc(transform.position, 3.2f, _nearbyScanBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider other = _nearbyScanBuffer[i];
                IgnoreCollisionWith(other);
            }
        }

        private void IgnoreAllCurrentPlayers()
        {
            var ragdolls = Object.FindObjectsByType<CoopGame.Player.RagdollCharacter>(FindObjectsSortMode.None);
            for (int p = 0; p < ragdolls.Length; p++)
            {
                if (ragdolls[p] == null) continue;
                Collider[] cols = ragdolls[p].GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    IgnoreCollisionWith(cols[i]);
                }
            }

            var movements = Object.FindObjectsByType<CoopGame.Player.PlayerMovement>(FindObjectsSortMode.None);
            for (int p = 0; p < movements.Length; p++)
            {
                if (movements[p] != null)
                {
                    Collider[] cols = movements[p].GetComponentsInChildren<Collider>(true);
                    for (int i = 0; i < cols.Length; i++)
                    {
                        IgnoreCollisionWith(cols[i]);
                    }
                }
            }
        }

        private void IgnoreCollisionWith(Collider other)
        {
            if (other == null || IsOwnCollider(other)) return;
            if (_anchoredPlankTransform != null && other.transform.IsChildOf(_anchoredPlankTransform)) return;

            if (_ignoredColliders.Add(other))
            {
                for (int c = 0; c < _ownColliders.Length; c++)
                {
                    if (_ownColliders[c] != null)
                    {
                        Physics.IgnoreCollision(_ownColliders[c], other, true);
                    }
                }
            }
        }

        private void RestoreAllIgnoredCollisions()
        {
            if (_ignoredColliders.Count == 0) return;

            foreach (var other in _ignoredColliders)
            {
                if (other != null && _ownColliders != null)
                {
                    for (int c = 0; c < _ownColliders.Length; c++)
                    {
                        if (_ownColliders[c] != null)
                        {
                            Physics.IgnoreCollision(_ownColliders[c], other, false);
                        }
                    }
                }
            }
            _ignoredColliders.Clear();
        }

        private void SetPhaseThrough(bool phase)
        {
            if (_ownColliders == null || _ownColliders.Length == 0)
            {
                _ownColliders = GetComponentsInChildren<Collider>(true);
            }

            for (int i = 0; i < _ownColliders.Length; i++)
            {
                if (_ownColliders[i] != null)
                {
                    _ownColliders[i].isTrigger = phase;
                }
            }

            if (phase)
            {
                UpdateAnchoredPhasing();
            }
            else
            {
                RestoreAllIgnoredCollisions();
            }
        }

        private void UpdateAnchorVisuals(bool anchored)
        {
            if (_anchorVisualIndicator != null)
            {
                _anchorVisualIndicator.SetActive(anchored);
            }
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();

            // 1. Fall check: If fallen below kill threshold, respawn back at spawn point
            if ((IsServer || !IsSpawned) && transform.position.y < _fallKillY)
            {
                ExecuteRespawn();
                return;
            }

            // 2. When anchored, lock position and rotation relative to the plank and enforce phase-through
            if (_isAnchored.Value)
            {
                ResolveAnchoredTransform();
                SetPhaseThrough(true);
                return;
            }

            // 3. On Server: If dropped or resting on a see-saw plank, auto-anchor when settled
            if (IsServer && !IsCarried && !IsPlacing && !_isAnchored.Value)
            {
                CheckAutoAnchorOnPlank();
            }
        }

        /// <summary>
        /// Respawns this counterweight at its designated spawn point empty GameObject.
        /// </summary>
        public void Respawn()
        {
            if (IsSpawned && !IsServer)
            {
                RequestRespawnServerRpc();
                return;
            }
            ExecuteRespawn();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestRespawnServerRpc()
        {
            ExecuteRespawn();
        }

        private void ExecuteRespawn()
        {
            bool isNetworked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (isNetworked && !IsServer) return;

            // 1. Detach all carriers
            DetachAllCarriers();

            // 2. Unanchor from plank
            Unanchor();

            // 3. Reset physics velocities
            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }

            // 4. Resolve spawn destination
            Vector3 targetPosition = _respawnPoint != null ? _respawnPoint.position : _initialPosition;
            Quaternion targetRotation = _respawnPoint != null ? _respawnPoint.rotation : _initialRotation;

            // 5. Teleport across network or locally
            NetworkTransform netTransform = GetComponent<NetworkTransform>();
            if (isNetworked && netTransform != null)
            {
                netTransform.Teleport(targetPosition, targetRotation, transform.localScale);
            }
            else
            {
                transform.SetPositionAndRotation(targetPosition, targetRotation);
            }

            if (_rigidbody != null)
            {
                _rigidbody.position = targetPosition;
                _rigidbody.rotation = targetRotation;
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.WakeUp();
            }

            // 6. Enforce pickup cooldown on respawn
            SetPickupCooldown(0.8f);

            // 7. Audio & RPC sync
            if (_respawnSound != null && _audioSource != null)
            {
                _audioSource.PlayOneShot(_respawnSound, 0.9f);
            }

            if (isNetworked)
            {
                PlayRespawnFeedbackClientRpc(targetPosition);
            }

            Debug.Log($"[SeeSawWeight] '{name}' fell out of bounds (< {_fallKillY}m). Respawned at {targetPosition}.");
        }

        [ClientRpc]
        private void PlayRespawnFeedbackClientRpc(Vector3 pos)
        {
            if (!IsServer && _respawnSound != null && _audioSource != null)
            {
                _audioSource.PlayOneShot(_respawnSound, 0.9f);
            }
            SetPhaseThrough(false);
            SetPickupCooldown(0.8f);
        }

        /// <summary>
        /// Follows the anchored plank's motion rigidly without physics drift.
        /// </summary>
        private void ResolveAnchoredTransform()
        {
            if (_anchoredPlankTransform == null)
            {
                if (_anchoredPlankRef.Value.TryGet(out NetworkObject netObj) && netObj != null)
                {
                    _anchoredPlank = netObj.GetComponent<SeeSawPlank>();
                    _anchoredPlankTransform = _anchoredPlank != null ? _anchoredPlank.PlankTransform : netObj.transform;
                }
            }

            if (_anchoredPlankTransform != null)
            {
                Vector3 worldTarget = _anchoredPlankTransform.TransformPoint(_anchoredLocalPos.Value);
                Quaternion rotTarget = _anchoredPlankTransform.rotation * _anchoredLocalRot.Value;

                if (_rigidbody != null)
                {
                    if (!_rigidbody.isKinematic)
                    {
                        _rigidbody.isKinematic = true;
                        _rigidbody.useGravity = false;
                    }
                    _rigidbody.MovePosition(worldTarget);
                    _rigidbody.MoveRotation(rotTarget);
                }

                transform.position = worldTarget;
                transform.rotation = rotTarget;
            }
        }

        /// <summary>
        /// Automatically anchors to a SeeSawPlank if resting steadily on its surface.
        /// </summary>
        private void CheckAutoAnchorOnPlank()
        {
            if (_rigidbody == null) return;
            if (_rigidbody.linearVelocity.sqrMagnitude > 0.8f) return;

            RaycastHit[] hits = Physics.RaycastAll(transform.position + Vector3.up * 0.3f, Vector3.down, 1.2f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (IsOwnCollider(hits[i].collider)) continue;
                SeeSawPlank plank = hits[i].collider.GetComponentInParent<SeeSawPlank>();
                if (plank != null && plank.IsWithinPlankBounds(hits[i].point))
                {
                    AnchorToPlank(plank, hits[i].point);
                    break;
                }
            }
        }

        /// <summary>
        /// Anchors this weight to a see-saw plank at the specified world position.
        /// Called on Server.
        /// </summary>
        public void AnchorToPlank(SeeSawPlank plank, Vector3 worldPosition)
        {
            if (!IsServer && IsSpawned) return;
            if (plank == null) return;

            Transform pTransform = plank.PlankTransform;
            float heightOffset = 0.4f;
            if (_ownColliders != null && _ownColliders.Length > 0 && _ownColliders[0] != null)
            {
                heightOffset = _ownColliders[0].bounds.extents.y;
            }

            Vector3 snappedWorld = plank.ProjectToPlankSurface(worldPosition, heightOffset);
            Vector3 localPos = pTransform.InverseTransformPoint(snappedWorld);
            Quaternion localRot = Quaternion.Inverse(pTransform.rotation) * transform.rotation;

            _anchoredPlank = plank;
            _anchoredPlankTransform = pTransform;
            plank.RegisterAnchoredWeight(this);

            if (IsSpawned)
            {
                _anchoredPlankRef.Value = plank.NetworkObject;
                _anchoredLocalPos.Value = localPos;
                _anchoredLocalRot.Value = localRot;
                _isAnchored.Value = true;
            }

            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = true;
                _rigidbody.useGravity = false;
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }

            transform.position = snappedWorld;
            UpdateAnchorVisuals(true);
            SetPhaseThrough(true);
            IgnoreAllCurrentPlayers();
            SetPickupCooldown(_postPlacePickupCooldown);

            if (_anchorLatchSound != null && _audioSource != null)
            {
                _audioSource.PlayOneShot(_anchorLatchSound, 0.85f);
            }
        }

        /// <summary>
        /// Unanchors this weight from the see-saw plank so it can be carried or repositioned freely.
        /// </summary>
        public void Unanchor()
        {
            if (IsSpawned && !IsServer) return;

            if (_isAnchored.Value || _anchoredPlank != null)
            {
                if (_anchoredPlank != null)
                {
                    _anchoredPlank.UnregisterAnchoredWeight(this);
                }

                if (IsSpawned)
                {
                    _isAnchored.Value = false;
                    _anchoredPlankRef.Value = default;
                }
                _anchoredPlank = null;
                _anchoredPlankTransform = null;

                if (_rigidbody != null)
                {
                    _rigidbody.isKinematic = false;
                    _rigidbody.useGravity = true;
                }

                SetPhaseThrough(false);
                UpdateAnchorVisuals(false);
                if (_unanchorSound != null && _audioSource != null)
                {
                    _audioSource.PlayOneShot(_unanchorSound, 0.75f);
                }
            }
        }

        protected virtual void OnDisable()
        {
            RestoreAllIgnoredCollisions();
            if (_anchoredPlank != null)
            {
                _anchoredPlank.UnregisterAnchoredWeight(this);
            }
        }

        /// <summary>
        /// When a player grabs/lifts the weight, automatically un-anchor so it can be carried freely.
        /// </summary>
        protected override void OnCarrierAttached(bool firstCarrier)
        {
            base.OnCarrierAttached(firstCarrier);
            Unanchor();
        }

        protected override void OnCarrierDetached(int remainingCarriers)
        {
            base.OnCarrierDetached(remainingCarriers);
            SetPickupCooldown(_postPlacePickupCooldown);
        }

        private bool IsOwnCollider(Collider col)
        {
            if (col == null) return false;
            if (_rigidbody != null && col.attachedRigidbody == _rigidbody) return true;
            if (_ownColliders != null)
            {
                for (int i = 0; i < _ownColliders.Length; i++)
                {
                    if (_ownColliders[i] == col) return true;
                }
            }
            return false;
        }

        private bool IsCarrierOrPlayerCollider(Collider col, Collider[] extraCarrierColliders)
        {
            if (col == null) return false;
            if (extraCarrierColliders != null && System.Array.IndexOf(extraCarrierColliders, col) >= 0) return true;
            if (col.CompareTag("Player")) return true;
            if (col.GetComponentInParent<CoopGame.Player.NetworkPlayer>() != null ||
                col.GetComponentInParent<CoopGame.Player.PlayerMovement>() != null) return true;
            return false;
        }

        private Transform GetCarrierTransform(ulong carrierId)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClients.TryGetValue(carrierId, out var client) && client.PlayerObject != null)
            {
                return client.PlayerObject.transform;
            }
            return null;
        }

        /// <summary>
        /// Custom placement handler. Works with 'F' key and Right-Click.
        /// If placing onto a See-Saw plank, anchors cleanly to the plank surface.
        /// Otherwise falls back to normal gentle ground placement.
        /// </summary>
        public override bool TryPlaceGently(ulong carrierId, Collider[] extraCarrierColliders = null)
        {
            if ((IsSpawned && !IsServer) || IsSecured || IsPlacing || !HasCarrier(carrierId)) return false;

            SeeSawPlank plank = null;
            Vector3 placePoint = transform.position;

            // 1. Raycast down from under the weight (filtering out self and carrier colliders)
            RaycastHit[] hits = Physics.RaycastAll(transform.position + Vector3.up * 0.4f, Vector3.down, 3.5f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (IsOwnCollider(hits[i].collider) || IsCarrierOrPlayerCollider(hits[i].collider, extraCarrierColliders)) continue;
                SeeSawPlank found = hits[i].collider.GetComponentInParent<SeeSawPlank>();
                if (found != null && found.IsWithinPlankBounds(hits[i].point))
                {
                    plank = found;
                    placePoint = hits[i].point;
                    break;
                }
            }

            // 2. If weight is held in front, check if the carrier player is standing on or near a SeeSawPlank
            if (plank == null)
            {
                Transform carrierTrans = GetCarrierTransform(carrierId);
                if (carrierTrans != null)
                {
                    RaycastHit[] playerHits = Physics.RaycastAll(carrierTrans.position + Vector3.up * 0.5f, Vector3.down, 3.5f, ~0, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < playerHits.Length; i++)
                    {
                        if (IsOwnCollider(playerHits[i].collider) || IsCarrierOrPlayerCollider(playerHits[i].collider, extraCarrierColliders)) continue;
                        SeeSawPlank found = playerHits[i].collider.GetComponentInParent<SeeSawPlank>();
                        if (found != null)
                        {
                            plank = found;
                            Vector3 forwardSpot = carrierTrans.position + carrierTrans.forward * 0.9f;
                            placePoint = plank.ProjectToPlankSurface(forwardSpot);
                            break;
                        }
                    }
                }
            }

            if (plank != null && plank.IsWithinPlankBounds(placePoint))
            {
                DetachCarrier(carrierId);
                AnchorToPlank(plank, placePoint);
                SetPickupCooldown(_postPlacePickupCooldown);
                return true;
            }

            // Normal ground placement fallback
            bool placed = base.TryPlaceGently(carrierId, extraCarrierColliders);
            if (placed)
            {
                SetPickupCooldown(_postPlacePickupCooldown);
            }
            return placed;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_isAnchored.Value)
            {
                if (collision.collider != null)
                {
                    IgnoreCollisionWith(collision.collider);
                }
                return;
            }

            if (!IsServer && IsSpawned) return;
            if (IsCarried) return;

            SeeSawPlank plank = collision.collider.GetComponentInParent<SeeSawPlank>();
            if (plank != null && plank.IsWithinPlankBounds(collision.contacts[0].point))
            {
                // When dropped or thrown onto plank, anchor on impact
                AnchorToPlank(plank, collision.contacts[0].point);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer && IsSpawned) return;
            if (other == null) return;
            if (other.CompareTag("KillZone") || other.CompareTag("Hazard") || other.CompareTag("Death"))
            {
                ExecuteRespawn();
            }
            else
            {
                string n = other.name.ToLowerInvariant();
                if (n.Contains("killzone") || n.Contains("deathzone") || n.Contains("fallzone"))
                {
                    ExecuteRespawn();
                }
            }
        }
    }
}
