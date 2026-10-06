using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CoopGame.Player;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// CarryableObject is the core physical object (e.g. egg, fragile crate) that players
    /// cooperatively transport.
    /// 
    /// Carry Physics Architecture:
    /// - Solo Carry (1 Player): The object floats in front of the player at waist height (_carryHeightOffset).
    ///   The player moves with their feet on the ground and turns with the mouse. The object stays in front of them.
    /// - Co-op Carry (2+ Players): Persistent surface grips share a spring-damper support force.
    /// - Server Authoritative: The Host applies contact-point forces while retaining world collisions.
    /// - Clients receive smooth interpolated positions via NetworkTransform.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public class CarryableObject : NetworkBehaviour, ICarryable
    {
        [Header("Carry Sockets (Multi-player attachment)")]
        [Tooltip("Transform locations where players attach in co-op (Front, Back, Left, Right).")]
        [SerializeField] private Transform[] _sockets;

        [Header("Hold Positioning (1 Player)")]
        [Tooltip("Distance in front of the player when held by 1 person")]
        [SerializeField] private float _carryForwardDistance = 0.65f;

        [Tooltip("Height above the player's ground position (waist/chest level)")]
        [SerializeField] private float _carryHeightOffset = 1.05f;

        [Header("Co-op Movement Settings")]
        [Tooltip("Speed multiplier applied to player when carrying alone (heavy feel)")]
        [SerializeField] private float _soloSpeedMultiplier = 0.6f;

        [Tooltip("Speed multiplier applied to players when carrying together (synergy)")]
        [SerializeField] private float _coopSpeedMultiplier = 0.9f;

        [Tooltip("Rotation alignment speed in degrees per second")]
        [SerializeField] private float _rotationSpeed = 360.0f;

        // Cached components
        protected Rigidbody _rigidbody;
        protected Collider[] _ownColliders;
        internal IReadOnlyList<Collider> CollisionColliders => _ownColliders;
        protected CarryableOutline _outline;

        public CarryableOutline Outline => _outline;

        // Server-side Carrier Registry
        private class CarrierInfo
        {
            public ulong ClientId;
            public int SocketIndex;
            public Transform PlayerTransform;
            public PlayerMovement Movement;
            public PlayerCarry Carry;
            public Collider[] PlayerColliders;
            public Vector3 PreviousPlayerPosition;
            public Vector3 PlayerVelocity;
            public float AttachedAtFixedTime;
            public Vector3 InitialSupportOffsetLeft;
            public Vector3 InitialSupportOffsetRight;
            public Vector3 InputDirection;
            public Vector3 FacingHeading;
            public float HoldHeight = 0.8f;
            public float CurrentHoldDistance = 1.2f;

            // Physical contact points in object local space
            public Vector3 LocalContactLeft = new Vector3(-0.25f, 0f, -0.4f);
            public Vector3 LocalContactRight = new Vector3(0.25f, 0f, -0.4f);
            public bool LeftHandActive = true;
            public bool RightHandActive = true;
        }

        private readonly Dictionary<ulong, CarrierInfo> _activeCarriers = new Dictionary<ulong, CarrierInfo>();
        private readonly HashSet<int> _occupiedSockets = new HashSet<int>();

        private struct CollisionChange
        {
            public Collider[] PlayerColliders;
            public bool Ignore;
        }

        private readonly List<CollisionChange> _pendingCollisionChanges = new(8);

        // Synchronized carrier count
        public NetworkVariable<int> SyncedCarrierCount { get; } = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ICarryable implementation
        private readonly NetworkVariable<bool> _secured = new(false);
        private readonly List<ulong> _staleCarriers = new(4);
        private bool _physicsSetupPending;
        private bool _throwPending;
        private Vector3 _throwVelocity;
        private Vector3 _throwAngularVelocity;
        private bool _deliveryPlacementPending;
        private Vector3 _deliveryPlacementPosition;
        private Quaternion _deliveryPlacementRotation = Quaternion.identity;
        public bool IsSecured => _secured.Value;
        public virtual bool CanBeCarried => !IsSecured && CurrentCarrierCount < MaxCarriers;
        public bool IsCarried => (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            ? (SyncedCarrierCount.Value > 0)
            : (_activeCarriers.Count > 0);
        public int CurrentCarrierCount => (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            ? SyncedCarrierCount.Value
            : _activeCarriers.Count;
        public int MaxCarriers => (_sockets != null && _sockets.Length > 0) ? _sockets.Length : 4;
        public float TotalMass => (_rigidbody != null) ? _rigidbody.mass : 10.0f;
        public float GetSpeedMultiplier() => CurrentCarrierCount >= 2 ? _coopSpeedMultiplier : _soloSpeedMultiplier;

        protected virtual void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _physicsSetupPending = true;
            _ownColliders = GetComponentsInChildren<Collider>(true);

            _outline = GetComponent<CarryableOutline>();
            if (_outline == null)
            {
                _outline = gameObject.AddComponent<CarryableOutline>();
            }

            if (_sockets == null || _sockets.Length == 0)
            {
                CreateFallbackSockets();
            }
        }

        /// <summary>
        /// Controls the visual outline highlight around this carryable object.
        /// </summary>
        /// <param name="active">Whether the outline is visible</param>
        /// <param name="color">Optional custom outline color</param>
        /// <param name="width">Optional custom outline thickness</param>
        /// <param name="enablePulse">Optional pulse animation toggle</param>
        public void SetOutline(bool active, Color? color = null, float? width = null, bool? enablePulse = null)
        {
            if (_outline == null)
            {
                _outline = GetComponent<CarryableOutline>();
                if (_outline == null)
                {
                    _outline = gameObject.AddComponent<CarryableOutline>();
                }
            }

            if (_outline != null)
            {
                _outline.SetHighlighted(active, color, width, enablePulse);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            _physicsSetupPending = true;
            if (IsServer) NetworkManager.OnClientDisconnectCallback += OnCarrierDisconnected;
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnCarrierDisconnected;
            if (IsServer && NetworkManager != null && NetworkManager.IsListening)
                DetachAllCarriers();
            _activeCarriers.Clear();
            _occupiedSockets.Clear();
            _staleCarriers.Clear();
            _throwPending = false;
            base.OnNetworkDespawn();
        }

        private void OnCarrierDisconnected(ulong clientId)
        {
            // The callback records intent; FixedUpdate performs physics changes.
            if (_activeCarriers.ContainsKey(clientId) && !_staleCarriers.Contains(clientId))
                _staleCarriers.Add(clientId);
        }

        public void SecureForDelivery()
        {
            if (IsSpawned && !IsServer) return;
            DetachAllCarriers();
            _secured.Value = true;
            _throwPending = false;
        }

        /// <summary>
        /// Queues the final delivery placement for the next physics tick. The
        /// server applies the Rigidbody move in FixedUpdate so delivery never
        /// teleports a live physics body from a trigger callback.
        /// </summary>
        public void QueueDeliveryPlacement(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (IsSpawned && !IsServer) return;

            _deliveryPlacementPosition = worldPosition;
            _deliveryPlacementRotation = worldRotation;
            _deliveryPlacementPending = true;
        }

        public bool HasCarrier(ulong clientId) => _activeCarriers.ContainsKey(clientId);

        public bool HasGuardCard()
        {
            foreach (var carrier in _activeCarriers.Values)
                if (carrier.PlayerTransform != null &&
                    carrier.PlayerTransform.TryGetComponent<CoopGame.Network.PlayerExpeditionState>(out var state) &&
                    state.Card.Value == 2) return true;
            return false;
        }

        protected virtual void FixedUpdate()
        {
            ApplyPendingCollisionChanges();
            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
            if (_physicsSetupPending)
            {
                _physicsSetupPending = false;
                _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                _rigidbody.isKinematic = isNetworked && !IsServer;
                _rigidbody.useGravity = !isNetworked || IsServer;
            }
            if (isNetworked && !IsServer) return;

            foreach (var pair in _activeCarriers)
                if ((pair.Value.PlayerTransform == null ||
                    (pair.Value.PlayerTransform.position - transform.position).sqrMagnitude > 36f) &&
                    !_staleCarriers.Contains(pair.Key))
                    _staleCarriers.Add(pair.Key);
            for (int i = 0; i < _staleCarriers.Count; i++)
            {
                ulong id = _staleCarriers[i];
                if (_activeCarriers.TryGetValue(id, out var carrier) && carrier.PlayerTransform != null &&
                    carrier.PlayerTransform.TryGetComponent<PlayerCarry>(out var carry)) carry.ForcedDropFromCargo(this);
                DetachCarrier(id);
            }
            _staleCarriers.Clear();

            if (_deliveryPlacementPending)
            {
                _deliveryPlacementPending = false;
                _rigidbody.isKinematic = true;
                _rigidbody.useGravity = false;
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.position = _deliveryPlacementPosition;
                _rigidbody.rotation = _deliveryPlacementRotation;
                _rigidbody.Sleep();
            }

            if (IsSecured)
            {
                if (!_rigidbody.isKinematic)
                {
                    _rigidbody.linearVelocity = Vector3.zero;
                    _rigidbody.angularVelocity = Vector3.zero;
                    _rigidbody.isKinematic = true;
                }
                return;
            }
            if (_throwPending)
            {
                _throwPending = false;
                _rigidbody.isKinematic = false;
                _rigidbody.useGravity = true;
                _rigidbody.constraints = RigidbodyConstraints.None;
                _rigidbody.linearVelocity = _throwVelocity;
                _rigidbody.angularVelocity = _throwAngularVelocity;
                _rigidbody.WakeUp();
            }

            // When no one is carrying: allow full PhysX gravity and free tumbling
            if (_activeCarriers.Count == 0)
            {
                _rigidbody.useGravity = true;
                _rigidbody.constraints = RigidbodyConstraints.None;
                return;
            }

            // =========================================================================
            // REALISTIC GRAVITY & TOUCH-POINT LIFTING (Human Fall Flat style)
            // =========================================================================
            _rigidbody.useGravity = true;
            _rigidbody.constraints = RigidbodyConstraints.None;

            int supportingCarrierCount = 0;
            foreach (var carrier in _activeCarriers.Values)
                if (carrier.PlayerTransform != null && (carrier.LeftHandActive || carrier.RightHandActive))
                    supportingCarrierCount++;

            if (supportingCarrierCount == 0) return;

            Vector3 objectScale = transform.lossyScale;

            foreach (var carrier in _activeCarriers.Values)
            {
                if (carrier.PlayerTransform == null) continue;

                Transform pTransform = carrier.PlayerTransform;
                Vector3 sampledVelocity = (pTransform.position - carrier.PreviousPlayerPosition) / Time.fixedDeltaTime;
                carrier.PreviousPlayerPosition = pTransform.position;
                carrier.PlayerVelocity = Vector3.ClampMagnitude(sampledVelocity, 12f);
                float height = (carrier.HoldHeight > 0.05f) ? carrier.HoldHeight : _carryHeightOffset;

                // Smoothly ease hold distance so there is no sudden snap
                carrier.CurrentHoldDistance = Mathf.MoveTowards(carrier.CurrentHoldDistance, _carryForwardDistance, 2.5f * Time.fixedDeltaTime);

                Vector3 heading = (carrier.FacingHeading.sqrMagnitude > 0.01f) 
                    ? carrier.FacingHeading 
                    : pTransform.forward;
                heading.y = 0f;
                if (heading.sqrMagnitude > 0.001f) heading.Normalize();
                else heading = pTransform.forward;

                Vector3 camRight = Vector3.Cross(Vector3.up, heading);
                float handSpread = 0.22f;

                // Target positions in world space where the player's hands are lifting the contact points
                Vector3 centerTarget = pTransform.position + (heading * carrier.CurrentHoldDistance) + (Vector3.up * height);
                Vector3 targetHandL = centerTarget - (camRight * handSpread);
                Vector3 targetHandR = centerTarget + (camRight * handSpread);

                // The player supplies targets inside the actual shoulder-to-wrist
                // reach envelope. These are support goals, not new surface grips.
                if (carrier.Carry != null && carrier.Carry.TryGetCarrySupportTargets(
                    height, heading, out Vector3 reachableLeft, out Vector3 reachableRight))
                {
                    targetHandL = reachableLeft;
                    targetHandR = reachableRight;
                }

                // Start at the captured contacts and ease into the support pose;
                // the first grab must not jerk a resting crate into the carrier.
                float settle = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((Time.fixedTime - carrier.AttachedAtFixedTime) / .35f));
                targetHandL = Vector3.Lerp(pTransform.position + carrier.InitialSupportOffsetLeft, targetHandL, settle);
                targetHandR = Vector3.Lerp(pTransform.position + carrier.InitialSupportOffsetRight, targetHandR, settle);

                // Physics reads the authoritative Rigidbody pose; rendered,
                // interpolated transforms are reserved for the visual hand lock.
                Vector3 currentWorldL = _rigidbody.position + _rigidbody.rotation * Vector3.Scale(carrier.LocalContactLeft, objectScale);
                Vector3 currentWorldR = _rigidbody.position + _rigidbody.rotation * Vector3.Scale(carrier.LocalContactRight, objectScale);

                bool hasLeft = carrier.LeftHandActive;
                bool hasRight = carrier.RightHandActive;

                if (!hasLeft && !hasRight) continue;

                int activeHands = (hasLeft ? 1 : 0) + (hasRight ? 1 : 0);
                float supportShare = 1f / (activeHands * supportingCarrierCount);
                float supportedMass = _rigidbody.mass * supportShare;
                Vector3 gravComp = -Physics.gravity * supportedMass;
                float springStrength = supportedMass * 32f;
                float dampingStrength = supportedMass * 10f;
                float maximumForce = supportedMass * 45f;

                // 1. Spring-damper force at Left Hand contact point (if left hand active)
                if (hasLeft)
                {
                    Vector3 deltaL = (targetHandL - currentWorldL);
                    Vector3 velL = _rigidbody.GetPointVelocity(currentWorldL);
                    Vector3 forceL = deltaL * springStrength + (carrier.PlayerVelocity - velL) * dampingStrength + gravComp;
                    forceL = Vector3.ClampMagnitude(forceL, maximumForce);
                    _rigidbody.AddForceAtPosition(forceL, currentWorldL, ForceMode.Force);
                }

                // 2. Spring-damper force at Right Hand contact point (if right hand active)
                if (hasRight)
                {
                    Vector3 deltaR = (targetHandR - currentWorldR);
                    Vector3 velR = _rigidbody.GetPointVelocity(currentWorldR);
                    Vector3 forceR = deltaR * springStrength + (carrier.PlayerVelocity - velR) * dampingStrength + gravComp;
                    forceR = Vector3.ClampMagnitude(forceR, maximumForce);
                    _rigidbody.AddForceAtPosition(forceR, currentWorldR, ForceMode.Force);
                }

            }

            // Apply damping once, so adding helpers does not multiply resistance.
            float dampFactor = Mathf.Max(2.5f, _rotationSpeed * 0.01f);
            _rigidbody.AddTorque(-_rigidbody.angularVelocity * dampFactor, ForceMode.Acceleration);

            _rigidbody.linearVelocity = Vector3.ClampMagnitude(_rigidbody.linearVelocity, 12.0f);
        }

        public void UpdateCarrierHandState(ulong clientId, Vector3 localContactLeft, Vector3 localContactRight, bool leftActive, bool rightActive)
        {
            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
            if (isNetworked && !IsServer) return;

            if (_activeCarriers.TryGetValue(clientId, out CarrierInfo data))
            {
                // A held contact is immutable until that hand releases. Updating
                // the other hand must never move an existing grip across the crate.
                if (leftActive && !data.LeftHandActive) data.LocalContactLeft = localContactLeft;
                if (rightActive && !data.RightHandActive) data.LocalContactRight = localContactRight;
                data.LeftHandActive = leftActive;
                data.RightHandActive = rightActive;
            }
        }

        #region ICarryable Server Methods

        public bool TryAttachCarrier(ulong clientId, Vector3 playerWorldPos, out int socketIndex)
        {
            Transform playerTransform = null;
            PlayerMovement playerMovement = null;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var netClient))
            {
                if (netClient.PlayerObject != null)
                {
                    playerTransform = netClient.PlayerObject.transform;
                    playerMovement = netClient.PlayerObject.GetComponent<PlayerMovement>();
                }
            }
            Vector3 defaultL = new Vector3(-0.25f, 0f, -0.4f);
            Vector3 defaultR = new Vector3(0.25f, 0f, -0.4f);
            return TryAttachCarrier(clientId, playerTransform, playerMovement, defaultL, defaultR, out socketIndex);
        }

        public bool TryAttachCarrier(ulong clientId, Transform playerTransform, PlayerMovement playerMovement, out int socketIndex)
        {
            Vector3 defaultL = new Vector3(-0.25f, 0f, -0.4f);
            Vector3 defaultR = new Vector3(0.25f, 0f, -0.4f);
            return TryAttachCarrier(clientId, playerTransform, playerMovement, defaultL, defaultR, out socketIndex);
        }

        public bool TryAttachCarrier(
            ulong clientId, 
            Transform playerTransform, 
            PlayerMovement playerMovement, 
            Vector3 localContactLeft, 
            Vector3 localContactRight, 
            out int socketIndex)
        {
            return TryAttachCarrier(clientId, playerTransform, playerMovement, localContactLeft, localContactRight, true, true, out socketIndex);
        }

        public bool TryAttachCarrier(
            ulong clientId, 
            Transform playerTransform, 
            PlayerMovement playerMovement, 
            Vector3 localContactLeft, 
            Vector3 localContactRight, 
            bool leftHandActive, 
            bool rightHandActive, 
            out int socketIndex)
        {
            socketIndex = -1;
            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
            if (isNetworked && !IsServer) return false;
            if (_activeCarriers.ContainsKey(clientId)) return false;
            if (!CanBeCarried || playerTransform == null || (!leftHandActive && !rightHandActive)) return false;

            if (_sockets == null || _sockets.Length == 0)
            {
                CreateFallbackSockets();
            }

            Vector3 playerWorldPos = (playerTransform != null) ? playerTransform.position : transform.position;

            // Find closest available socket
            float closestDistSqr = float.MaxValue;
            for (int i = 0; i < _sockets.Length; i++)
            {
                if (_sockets[i] == null) continue;
                if (_occupiedSockets.Contains(i)) continue;

                float distSqr = (_sockets[i].position - playerWorldPos).sqrMagnitude;
                if (distSqr < closestDistSqr)
                {
                    closestDistSqr = distSqr;
                    socketIndex = i;
                }
            }

            if (socketIndex < 0) return false;

            // Calculate initial distance to avoid sudden snapping
            float initialDist = _carryForwardDistance;
            if (playerTransform != null)
            {
                Vector3 toObj = _rigidbody.position - playerTransform.position;
                toObj.y = 0f;
                initialDist = Mathf.Clamp(toObj.magnitude, 0.15f, Mathf.Max(0.15f, _carryForwardDistance));
            }

            _occupiedSockets.Add(socketIndex);
            _activeCarriers[clientId] = new CarrierInfo
            {
                ClientId = clientId,
                SocketIndex = socketIndex,
                PlayerTransform = playerTransform,
                Movement = playerMovement,
                Carry = playerTransform.GetComponent<PlayerCarry>(),
                PlayerColliders = playerTransform.GetComponentsInChildren<Collider>(true),
                PreviousPlayerPosition = playerTransform.position,
                PlayerVelocity = Vector3.zero,
                AttachedAtFixedTime = Time.fixedTime,
                InitialSupportOffsetLeft = _rigidbody.position + _rigidbody.rotation *
                    Vector3.Scale(localContactLeft, transform.lossyScale) - playerTransform.position,
                InitialSupportOffsetRight = _rigidbody.position + _rigidbody.rotation *
                    Vector3.Scale(localContactRight, transform.lossyScale) - playerTransform.position,
                InputDirection = Vector3.zero,
                FacingHeading = (playerTransform != null) ? playerTransform.forward : Vector3.forward,
                HoldHeight = _carryHeightOffset,
                CurrentHoldDistance = initialDist,
                LocalContactLeft = localContactLeft,
                LocalContactRight = localContactRight,
                LeftHandActive = leftHandActive,
                RightHandActive = rightHandActive
            };

            if (isNetworked)
            {
                SyncedCarrierCount.Value = _activeCarriers.Count;
            }

            // Update player movement speeds based on carrier count
            UpdateAllCarriersSpeedMultiplier();

            // Keep the torso solid; auxiliary hand colliders must not fight
            // the grip springs at their attached surface contacts.
            SetCarrierCollisionIgnore(_activeCarriers[clientId].PlayerColliders, true);

            // Wake up Rigidbody with gravity active
            _physicsSetupPending = true;
            OnCarrierAttached(_activeCarriers.Count == 1);

            Debug.Log($"[CarryableObject] Client {clientId} attached to '{name}' at Left: {localContactLeft} | Right: {localContactRight} | PhysX Gravity: ON");
            return true;
        }

        /// <summary>Attachment lifecycle hook; called before the next support physics step.</summary>
        protected virtual void OnCarrierAttached(bool firstCarrier) { }

        // Shared by authority and owner prediction so collision policy cannot differ.
        internal static bool ShouldIgnoreCarryCollision(Collider playerCollider)
        {
            return !(playerCollider is CharacterController) &&
                playerCollider.GetComponent<PlayerMovement>() == null;
        }

        /// <summary>
        /// Retrieves the current world-space contact points where this carrier is holding the object.
        /// </summary>
        public bool GetCarrierContactPoints(ulong clientId, out Vector3 worldLeft, out Vector3 worldRight)
        {
            worldLeft = Vector3.zero;
            worldRight = Vector3.zero;
            if (_activeCarriers.TryGetValue(clientId, out CarrierInfo carrier))
            {
                worldLeft = transform.TransformPoint(carrier.LocalContactLeft);
                worldRight = transform.TransformPoint(carrier.LocalContactRight);
                return true;
            }
            return false;
        }

        public virtual void DetachCarrier(ulong clientId)
        {
            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
            if (isNetworked && !IsServer) return;

            if (_activeCarriers.TryGetValue(clientId, out CarrierInfo data))
            {
                // Restore player movement speed to normal
                if (data.Movement != null)
                {
                    data.Movement.SpeedMultiplier = 1.0f;
                }

                SetCarrierCollisionIgnore(data.PlayerColliders, false);

                _occupiedSockets.Remove(data.SocketIndex);
                _activeCarriers.Remove(clientId);

                if (isNetworked)
                {
                    SyncedCarrierCount.Value = _activeCarriers.Count;
                }

                UpdateAllCarriersSpeedMultiplier();


                Debug.Log($"[CarryableObject] Client {clientId} detached. Remaining: {_activeCarriers.Count}");
            }
        }

        /// <summary>
        /// Detaches all carriers currently holding this object.
        /// </summary>
        public virtual void DetachAllCarriers()
        {
            var carrierIds = new List<ulong>(_activeCarriers.Keys);
            foreach (var id in carrierIds)
            {
                PlayerCarry carry = _activeCarriers.TryGetValue(id, out CarrierInfo carrier) ? carrier.Carry : null;
                DetachCarrier(id);
                // Cached attachment references work offline and during disconnect teardown.
                if (carry != null) carry.ForcedDropFromCargo(this);
            }
        }

        /// <summary>
        /// Detaches the carrier and applies launch velocity and angular tumbling impulse.
        /// Server-authoritative physics execution.
        /// </summary>
        public virtual void ThrowObject(ulong clientId, Vector3 linearVelocity, Vector3 angularVelocity)
        {
            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
            if (isNetworked && !IsServer) return;

            DetachCarrier(clientId);

            if (IsSecured) return;
            _throwPending = true;
            _throwVelocity = Vector3.ClampMagnitude(linearVelocity, 20f);
            _throwAngularVelocity = Vector3.ClampMagnitude(angularVelocity, 12f);
        }

        public void UpdateCarrierInput(ulong clientId, Vector3 worldMoveDirection, Vector3 forwardHeading, float holdHeight)
        {
            UpdateCarrierInput(clientId, worldMoveDirection, forwardHeading, holdHeight, true, true);
        }

        public void UpdateCarrierInput(ulong clientId, Vector3 worldMoveDirection, Vector3 forwardHeading, float holdHeight, bool leftHandActive, bool rightHandActive)
        {
            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
            if (isNetworked && !IsServer) return;

            if (_activeCarriers.TryGetValue(clientId, out CarrierInfo data))
            {
                data.InputDirection = worldMoveDirection;
                data.FacingHeading = forwardHeading;
                data.HoldHeight = holdHeight;
                // Input streaming is unreliable. Only the reliable hand-state
                // transition may enable or release physical surface attachments.
            }
        }

        public Transform GetSocketTransform(int socketIndex)
        {
            if (_sockets != null && socketIndex >= 0 && socketIndex < _sockets.Length)
            {
                return _sockets[socketIndex];
            }
            return transform;
        }

        #endregion

        private void UpdateAllCarriersSpeedMultiplier()
        {
            float mult = (_activeCarriers.Count >= 2) ? _coopSpeedMultiplier : _soloSpeedMultiplier;
            foreach (var carrier in _activeCarriers.Values)
            {
                if (carrier.Movement != null)
                {
                    carrier.Movement.SpeedMultiplier = mult;
                }
            }
        }

        private void SetCarrierCollisionIgnore(Collider[] playerColliders, bool ignore)
        {
            if (playerColliders == null) return;

            _pendingCollisionChanges.Add(new CollisionChange
            {
                PlayerColliders = playerColliders,
                Ignore = ignore
            });
        }

        private void ApplyPendingCollisionChanges()
        {
            if (_pendingCollisionChanges.Count == 0) return;
            if (_ownColliders == null || _ownColliders.Length == 0)
            {
                _ownColliders = GetComponentsInChildren<Collider>(true);
            }

            for (int changeIndex = 0; changeIndex < _pendingCollisionChanges.Count; changeIndex++)
            {
                CollisionChange change = _pendingCollisionChanges[changeIndex];
                foreach (var pCol in change.PlayerColliders)
                {
                    foreach (var oCol in _ownColliders)
                    {
                        if (pCol != null && oCol != null && !pCol.isTrigger && !oCol.isTrigger)
                            Physics.IgnoreCollision(pCol, oCol,
                                change.Ignore && ShouldIgnoreCarryCollision(pCol));
                    }
                }
            }
            _pendingCollisionChanges.Clear();
        }

        private void CreateFallbackSockets()
        {
            _sockets = new Transform[4];
            Vector3[] localOffsets = new Vector3[]
            {
                new Vector3(0f, 0f, 1.2f),  // Front
                new Vector3(0f, 0f, -1.2f), // Back
                new Vector3(-1.2f, 0f, 0f), // Left
                new Vector3(1.2f, 0f, 0f)   // Right
            };

            string[] names = new string[] { "Socket_Front", "Socket_Back", "Socket_Left", "Socket_Right" };

            for (int i = 0; i < 4; i++)
            {
                GameObject socketObj = new GameObject(names[i]);
                socketObj.transform.SetParent(transform);
                socketObj.transform.localPosition = localOffsets[i];
                socketObj.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                _sockets[i] = socketObj.transform;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_sockets == null) return;
            Gizmos.color = Color.cyan;
            foreach (var socket in _sockets)
            {
                if (socket != null)
                {
                    Gizmos.DrawWireSphere(socket.position, 0.25f);
                    Gizmos.DrawRay(socket.position, socket.forward * 0.4f);
                }
            }
        }
    }
}
