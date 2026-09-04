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
    /// - Co-op Carry (2+ Players): The object floats at the midpoint between carriers.
    /// - Server Authoritative: The Host drives the Rigidbody via linearVelocity to respect wall/obstacle collisions.
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
        [SerializeField] private float _carryForwardDistance = 1.3f;

        [Tooltip("Height above the player's ground position (waist/chest level)")]
        [SerializeField] private float _carryHeightOffset = 0.8f;

        [Header("Co-op Movement Settings")]
        [Tooltip("Speed multiplier applied to player when carrying alone (heavy feel)")]
        [SerializeField] private float _soloSpeedMultiplier = 0.6f;

        [Tooltip("Speed multiplier applied to players when carrying together (synergy)")]
        [SerializeField] private float _coopSpeedMultiplier = 0.9f;

        [Tooltip("Rotation alignment speed in degrees per second")]
        [SerializeField] private float _rotationSpeed = 360.0f;

        // Cached components
        private Rigidbody _rigidbody;
        private Collider[] _ownColliders;
        private CarryableOutline _outline;

        public CarryableOutline Outline => _outline;

        // Server-side Carrier Registry
        private class CarrierInfo
        {
            public ulong ClientId;
            public int SocketIndex;
            public Transform PlayerTransform;
            public PlayerMovement Movement;
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

        // Synchronized carrier count
        public NetworkVariable<int> SyncedCarrierCount { get; } = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ICarryable implementation
        public bool CanBeCarried => _activeCarriers.Count < MaxCarriers;
        public int CurrentCarrierCount => (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            ? Mathf.Max(1, SyncedCarrierCount.Value)
            : Mathf.Max(1, _activeCarriers.Count);
        public int MaxCarriers => (_sockets != null && _sockets.Length > 0) ? _sockets.Length : 4;
        public float TotalMass => (_rigidbody != null) ? _rigidbody.mass : 10.0f;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
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

            if (IsServer)
            {
                _rigidbody.isKinematic = false;
                _rigidbody.useGravity = true;
            }
            else
            {
                _rigidbody.isKinematic = true;
                _rigidbody.useGravity = false;
            }
        }

        private void FixedUpdate()
        {
            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
            if (isNetworked && !IsServer) return;

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

            int carrierCount = Mathf.Max(1, _activeCarriers.Count);

            foreach (var carrier in _activeCarriers.Values)
            {
                if (carrier.PlayerTransform == null) continue;

                Transform pTransform = carrier.PlayerTransform;
                float height = (carrier.HoldHeight > 0.05f) ? carrier.HoldHeight : _carryHeightOffset;

                // Smoothly ease hold distance so there is no sudden snap
                carrier.CurrentHoldDistance = Mathf.MoveTowards(carrier.CurrentHoldDistance, _carryForwardDistance, 2.5f * Time.fixedDeltaTime);

                Vector3 heading = (carrier.FacingHeading.sqrMagnitude > 0.01f) 
                    ? carrier.FacingHeading 
                    : pTransform.forward;
                heading.y = 0f;
                if (heading.sqrMagnitude > 0.001f) heading.Normalize();
                else heading = pTransform.forward;

                Vector3 camRight = Quaternion.Euler(0f, 90f, 0f) * heading;
                float handSpread = 0.22f;

                // Target positions in world space where the player's hands are lifting the contact points
                Vector3 centerTarget = pTransform.position + (heading * carrier.CurrentHoldDistance) + (Vector3.up * height);
                Vector3 targetHandL = centerTarget - (camRight * handSpread);
                Vector3 targetHandR = centerTarget + (camRight * handSpread);

                // Current positions of the contact points on the object in world space
                Vector3 currentWorldL = _rigidbody.transform.TransformPoint(carrier.LocalContactLeft);
                Vector3 currentWorldR = _rigidbody.transform.TransformPoint(carrier.LocalContactRight);

                bool hasLeft = carrier.LeftHandActive;
                bool hasRight = carrier.RightHandActive;

                if (!hasLeft && !hasRight) continue;

                int activeHands = (hasLeft ? 1 : 0) + (hasRight ? 1 : 0);
                float gravityCounterForce = (_rigidbody.mass * Mathf.Abs(Physics.gravity.y)) / (activeHands * carrierCount);
                Vector3 gravComp = Vector3.up * (gravityCounterForce * 1.15f);

                // 1. Spring-damper force at Left Hand contact point (if left hand active)
                if (hasLeft)
                {
                    Vector3 deltaL = (targetHandL - currentWorldL);
                    Vector3 velL = _rigidbody.GetPointVelocity(currentWorldL);
                    Vector3 forceL = (deltaL * 160f) - (velL * 15f) + gravComp;
                    forceL = Vector3.ClampMagnitude(forceL, _rigidbody.mass * 45f);
                    _rigidbody.AddForceAtPosition(forceL, currentWorldL, ForceMode.Force);
                }

                // 2. Spring-damper force at Right Hand contact point (if right hand active)
                if (hasRight)
                {
                    Vector3 deltaR = (targetHandR - currentWorldR);
                    Vector3 velR = _rigidbody.GetPointVelocity(currentWorldR);
                    Vector3 forceR = (deltaR * 160f) - (velR * 15f) + gravComp;
                    forceR = Vector3.ClampMagnitude(forceR, _rigidbody.mass * 45f);
                    _rigidbody.AddForceAtPosition(forceR, currentWorldR, ForceMode.Force);
                }

                // Natural physics damping like Human Fall Flat (allows 100% free swinging, tilting, and dragging)
                float dampFactor = Mathf.Max(2.5f, _rotationSpeed * 0.01f);
                _rigidbody.AddTorque(-_rigidbody.angularVelocity * dampFactor, ForceMode.Acceleration);
            }

            _rigidbody.linearVelocity = Vector3.ClampMagnitude(_rigidbody.linearVelocity, 12.0f);
        }

        public void UpdateCarrierHandState(ulong clientId, Vector3 localContactLeft, Vector3 localContactRight, bool leftActive, bool rightActive)
        {
            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
            if (isNetworked && !IsServer) return;

            if (_activeCarriers.TryGetValue(clientId, out CarrierInfo data))
            {
                if (leftActive) data.LocalContactLeft = localContactLeft;
                if (rightActive) data.LocalContactRight = localContactRight;
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
            if (_activeCarriers.Count >= MaxCarriers) return false;

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

            if (socketIndex < 0) socketIndex = 0;

            // Calculate initial distance to avoid sudden snapping
            float initialDist = _carryForwardDistance;
            if (playerTransform != null)
            {
                Vector3 toObj = transform.position - playerTransform.position;
                toObj.y = 0f;
                initialDist = Mathf.Clamp(toObj.magnitude, 0.7f, _carryForwardDistance);
            }

            _occupiedSockets.Add(socketIndex);
            _activeCarriers[clientId] = new CarrierInfo
            {
                ClientId = clientId,
                SocketIndex = socketIndex,
                PlayerTransform = playerTransform,
                Movement = playerMovement,
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

            // Ignore collision between player and carried object
            SetCarrierCollisionIgnore(playerTransform, true);

            // Wake up Rigidbody with gravity active
            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = false;
                _rigidbody.useGravity = true;
                _rigidbody.WakeUp();
            }

            Debug.Log($"[CarryableObject] Client {clientId} attached to '{name}' at Left: {localContactLeft} | Right: {localContactRight} | PhysX Gravity: ON");
            return true;
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

        public void DetachCarrier(ulong clientId)
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

                SetCarrierCollisionIgnore(data.PlayerTransform, false);

                _occupiedSockets.Remove(data.SocketIndex);
                _activeCarriers.Remove(clientId);

                if (isNetworked)
                {
                    SyncedCarrierCount.Value = _activeCarriers.Count;
                }

                UpdateAllCarriersSpeedMultiplier();

                if (_rigidbody != null && _activeCarriers.Count == 0)
                {
                    _rigidbody.useGravity = true;
                    _rigidbody.constraints = RigidbodyConstraints.None;
                    _rigidbody.WakeUp();
                }

                Debug.Log($"[CarryableObject] Client {clientId} detached. Remaining: {_activeCarriers.Count}");
            }
        }

        /// <summary>
        /// Detaches the carrier and applies launch velocity and angular tumbling impulse.
        /// Server-authoritative physics execution.
        /// </summary>
        public void ThrowObject(ulong clientId, Vector3 linearVelocity, Vector3 angularVelocity)
        {
            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
            if (isNetworked && !IsServer) return;

            DetachCarrier(clientId);

            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = false;
                _rigidbody.useGravity = true;
                _rigidbody.constraints = RigidbodyConstraints.None;
                _rigidbody.linearVelocity = linearVelocity;
                _rigidbody.angularVelocity = angularVelocity;
                _rigidbody.WakeUp();

                Debug.Log($"[CarryableObject] Client {clientId} threw '{name}' with Velocity: {linearVelocity} (Speed: {linearVelocity.magnitude:F1} m/s)");
            }
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
                data.LeftHandActive = leftHandActive;
                data.RightHandActive = rightHandActive;
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

        private void SetCarrierCollisionIgnore(Transform playerTransform, bool ignore)
        {
            if (playerTransform == null) return;

            Collider[] playerColliders = playerTransform.GetComponentsInChildren<Collider>(true);
            if (_ownColliders == null || _ownColliders.Length == 0)
            {
                _ownColliders = GetComponentsInChildren<Collider>(true);
            }

            foreach (var pCol in playerColliders)
            {
                foreach (var oCol in _ownColliders)
                {
                    if (pCol != null && oCol != null)
                    {
                        Physics.IgnoreCollision(pCol, oCol, ignore);
                    }
                }
            }
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
