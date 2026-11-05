using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CoopGame.Player;
using CoopGame.CarrySystem;

namespace CoopGame.Obstacles
{
    /// <summary>
    /// Networked See-Saw (Teeter-totter) plank.
    /// 
    /// Multiplayer & Physics Specifications:
    /// - Two sides (Side A = -Z, Side B = +Z) pivoted around a central horizontal fulcrum (local X axis).
    /// - Every Player 1 to 4 has the EXACT SAME base weight unit (_playerTorqueMass = 1.0).
    /// - If any player is carrying an object (like the counterweight or crate), the carried object's weight
    ///   adds directly to their downward torque while on the plank.
    /// - Any object placed or resting on the plank (SeeSawWeight, delivery crates, physics rigidbodies)
    ///   exerts physical torque based on its mass and distance from the central pivot.
    /// - Host/Server is authoritative over the physics simulation and replicates the tilt angle via NetworkVariable.
    /// - All players (1 to 4) see the EXACT same tilt angle in real time with smooth interpolation.
    /// - Moving platform logic carries every local player smoothly with the angular motion so no one slips off.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public class SeeSawPlank : NetworkBehaviour
    {
        [Header("Plank Setup")]
        [Tooltip("The pivoting plank transform. If null, searches for a child named 'Plank' or uses this transform.")]
        [SerializeField] private Transform _plankTransform;

        [Tooltip("Rigidbody attached to the plank. Must be kinematic for synced network platform physics.")]
        [SerializeField] private Rigidbody _plankRigidbody;

        [Header("Dimensions & Pivot")]
        [Tooltip("Total length of the plank along its local Z axis in meters.")]
        [SerializeField] private float _plankLength = 10.0f;

        [Tooltip("Total width of the plank along its local X axis in meters.")]
        [SerializeField] private float _plankWidth = 2.4f;

        [Tooltip("Thickness of the plank along its local Y axis in meters.")]
        [SerializeField] private float _plankThickness = 0.25f;

        [Tooltip("Radius around the exact pivot where standing exerts minimal torque (deadzone).")]
        [SerializeField] private float _centerDeadzone = 0.4f;

        [Header("Tilt Settings")]
        [Tooltip("Maximum tilt angle in degrees.")]
        [SerializeField, Range(5f, 35f)] private float _maxTiltAngle = 16.0f;

        [Tooltip("Base speed in degrees per second at which the plank tilts under weight.")]
        [SerializeField, Min(1f)] private float _tiltSpeed = 26.0f;

        [Tooltip("Speed in degrees per second at which the plank returns to neutral (0°) when empty.")]
        [SerializeField, Min(0f)] private float _returnToCenterSpeed = 6.0f;

        [Tooltip("If true, returns to horizontal neutral (0°) when no weight is on either side. If false, maintains current tilt until stepped on.")]
        [SerializeField] private bool _autoCenterWhenEmpty = true;

        [Header("Weights & Physics Balance")]
        [Tooltip("Base torque weight unit for every single player (1 to 4). Guaranteed identical for all players.")]
        [SerializeField] private float _playerTorqueMass = 1.0f;

        [Tooltip("Torque threshold required to begin tilting.")]
        [SerializeField] private float _torqueThreshold = 0.1f;

        [Header("Audio & Feedback")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _impactSound;
        [SerializeField] private AudioClip _creakSound;

        // Synchronized tilt angle across the network (Server authoritative)
        private readonly NetworkVariable<float> _networkTiltAngle = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // Internal simulation state
        private float _currentAngle = 0f;
        private float _displayedAngle = 0f;
        private Quaternion _restRotation = Quaternion.identity;
        private Quaternion _lastPlankRotation = Quaternion.identity;
        private bool _hitLimitSoundPlayed = false;

        // Local player moving platform cache
        private CharacterController _localCharacterController;

        // Entity scanning collections
        private readonly HashSet<Transform> _evaluatedPlayers = new HashSet<Transform>();
        private readonly HashSet<Transform> _evaluatedObjects = new HashSet<Transform>();
        private readonly Collider[] _overlapBuffer = new Collider[48];

        public Transform PlankTransform => _plankTransform != null ? _plankTransform : transform;
        public float CurrentTiltAngle => _displayedAngle;
        public float MaxTiltAngle => _maxTiltAngle;
        public float PlankLength => _plankLength;
        public float PlankWidth => _plankWidth;
        public float PlankThickness => _plankThickness;

        private void Awake()
        {
            if (_plankTransform == null)
            {
                Transform childPlank = transform.Find("Plank");
                _plankTransform = childPlank != null ? childPlank : transform;
            }

            if (_plankRigidbody == null)
            {
                _plankRigidbody = _plankTransform.GetComponent<Rigidbody>();
                if (_plankRigidbody == null)
                {
                    _plankRigidbody = _plankTransform.gameObject.AddComponent<Rigidbody>();
                }
            }

            _plankRigidbody.isKinematic = true;
            _plankRigidbody.useGravity = false;
            _plankRigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            _restRotation = _plankTransform.localRotation;
            _lastPlankRotation = _plankTransform.rotation;

            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
            }
        }

        public override void OnNetworkSpawn()
        {
            _displayedAngle = _networkTiltAngle.Value;
            _currentAngle = _networkTiltAngle.Value;
            ApplyPlankRotation(_displayedAngle);
            _lastPlankRotation = _plankTransform.rotation;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            if (IsServer || !IsSpawned)
            {
                ServerSimulateTilt(dt);
                _displayedAngle = _currentAngle;
            }
            else
            {
                // Clients smoothly interpolate to authoritative server angle
                float targetAngle = _networkTiltAngle.Value;
                _displayedAngle = Mathf.Lerp(_displayedAngle, targetAngle, dt * 25f);
                if (Mathf.Abs(_displayedAngle - targetAngle) < 0.02f)
                {
                    _displayedAngle = targetAngle;
                }
            }

            // Compute delta rotation this physics step
            Quaternion prevRot = _plankTransform.rotation;
            ApplyPlankRotation(_displayedAngle);
            Quaternion newRot = _plankTransform.rotation;
            Quaternion deltaRot = newRot * Quaternion.Inverse(prevRot);

            // Apply moving platform motion to local player on this machine (Host or Client)
            ApplyLocalPlatformMotion(deltaRot);

            _lastPlankRotation = newRot;
        }

        /// <summary>
        /// Server authoritative torque calculation from all players (1 to 4) and objects on the plank.
        /// </summary>
        private void ServerSimulateTilt(float dt)
        {
            float netTorque = 0f;
            int entityCount = ScanEntitiesOnPlank(out netTorque);

            float targetAngle = _currentAngle;

            if (Mathf.Abs(netTorque) > _torqueThreshold)
            {
                // Net torque directs which side tilts down:
                // Positive torque (entities on +Z / Side B) tilts +Z DOWN -> positive angle
                // Negative torque (entities on -Z / Side A) tilts -Z DOWN -> negative angle
                targetAngle = Mathf.Sign(netTorque) * _maxTiltAngle;

                // Scale tilt speed by torque magnitude so heavy loads tilt decisively
                float torqueScale = Mathf.Clamp(Mathf.Abs(netTorque) / 2.0f, 0.6f, 1.8f);
                _currentAngle = Mathf.MoveTowards(_currentAngle, targetAngle, _tiltSpeed * torqueScale * dt);
            }
            else
            {
                if (_autoCenterWhenEmpty && entityCount == 0)
                {
                    // Gentle self-centering return toward neutral horizontal
                    _currentAngle = Mathf.MoveTowards(_currentAngle, 0f, _returnToCenterSpeed * dt);
                }
            }

            _currentAngle = Mathf.Clamp(_currentAngle, -_maxTiltAngle, _maxTiltAngle);

            // Limit collision feedback sound
            bool atLimit = Mathf.Abs(_currentAngle) >= (_maxTiltAngle - 0.2f);
            if (atLimit && !_hitLimitSoundPlayed)
            {
                _hitLimitSoundPlayed = true;
                PlayLimitImpactSound();
            }
            else if (!atLimit)
            {
                _hitLimitSoundPlayed = false;
            }

            if (IsSpawned)
            {
                _networkTiltAngle.Value = _currentAngle;
            }
        }

        /// <summary>
        /// Evaluates torque from ALL players (1-4, host and clients) and ANY objects on the plank.
        /// Guaranteed identical weight for every player.
        /// </summary>
        private int ScanEntitiesOnPlank(out float netTorque)
        {
            netTorque = 0f;
            _evaluatedPlayers.Clear();
            _evaluatedObjects.Clear();
            int entitiesFound = 0;

            // 1. EVALUATE ALL CONNECTED PLAYERS (Host + Clients 1 to 4)
            // Querying via Netcode guarantees EVERY connected player is evaluated with identical rules
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                var clients = NetworkManager.Singleton.ConnectedClientsList;
                for (int i = 0; i < clients.Count; i++)
                {
                    var netObj = clients[i].PlayerObject;
                    if (netObj != null && netObj.IsSpawned)
                    {
                        EvaluatePlayerOnPlank(netObj.transform, ref netTorque, ref entitiesFound);
                    }
                }
            }
            else
            {
                // Fallback for offline playtesting / test scenes
                var players = Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
                for (int i = 0; i < players.Length; i++)
                {
                    if (players[i] != null)
                    {
                        EvaluatePlayerOnPlank(players[i].transform, ref netTorque, ref entitiesFound);
                    }
                }
            }

            // 2. EVALUATE ALL OBJECTS RESTING ON THE PLANK
            Vector3 plankCenter = _plankTransform.position + _plankTransform.up * (_plankThickness * 0.5f + 0.8f);
            Vector3 halfExtents = new Vector3(_plankWidth * 0.5f + 0.15f, 0.9f, _plankLength * 0.5f);
            Quaternion orientation = _plankTransform.rotation;

            int hitCount = Physics.OverlapBoxNonAlloc(
                plankCenter,
                halfExtents,
                _overlapBuffer,
                orientation,
                ~0,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = _overlapBuffer[i];
                if (col == null) continue;
                if (col.transform.IsChildOf(_plankTransform)) continue; // ignore plank itself

                // Ignore players (already evaluated with 100% precision above)
                if (col.GetComponentInParent<NetworkPlayer>() != null || col.GetComponentInParent<PlayerMovement>() != null)
                    continue;

                // A. Check for SeeSawWeight
                SeeSawWeight weight = col.GetComponentInParent<SeeSawWeight>();
                if (weight != null)
                {
                    if (_evaluatedObjects.Add(weight.transform))
                    {
                        if (!weight.IsCarried)
                        {
                            entitiesFound++;
                            Vector3 localPos = _plankTransform.InverseTransformPoint(weight.transform.position);
                            if (Mathf.Abs(localPos.z) > _centerDeadzone)
                            {
                                netTorque += localPos.z * weight.WeightMass;
                            }
                        }
                    }
                    continue;
                }

                // B. Check for CarryableObject (delivery crates, fragile cargo, tall stacks, barrels)
                CarryableObject cargo = col.GetComponentInParent<CarryableObject>();
                if (cargo != null)
                {
                    if (_evaluatedObjects.Add(cargo.transform))
                    {
                        if (!cargo.IsCarried)
                        {
                            entitiesFound++;
                            Vector3 localPos = _plankTransform.InverseTransformPoint(cargo.transform.position);
                            if (Mathf.Abs(localPos.z) > _centerDeadzone)
                            {
                                float mass = cargo.TryGetComponent<Rigidbody>(out var crb) ? Mathf.Max(0.5f, crb.mass / 10f) : 1.0f;
                                netTorque += localPos.z * mass;
                            }
                        }
                    }
                    continue;
                }

                // C. Check for ANY other general Rigidbody (boxes, rocks, props)
                Rigidbody rb = col.attachedRigidbody;
                if (rb != null && !rb.isKinematic)
                {
                    if (_evaluatedObjects.Add(rb.transform))
                    {
                        entitiesFound++;
                        Vector3 localPos = _plankTransform.InverseTransformPoint(rb.worldCenterOfMass);
                        if (Mathf.Abs(localPos.z) > _centerDeadzone)
                        {
                            float mass = Mathf.Clamp(rb.mass / 10f, 0.2f, 4.0f);
                            netTorque += localPos.z * mass;
                        }
                    }
                }
            }

            return entitiesFound;
        }

        /// <summary>
        /// Checks a specific player to see if they are standing on the see-saw plank.
        /// Applies the exact same weight for Player 1, 2, 3, or 4.
        /// If the player is carrying an object, that object's mass is added to the downward torque.
        /// </summary>
        private void EvaluatePlayerOnPlank(Transform playerRoot, ref float netTorque, ref int entitiesFound)
        {
            if (playerRoot == null || !_evaluatedPlayers.Add(playerRoot)) return;

            Vector3 feetPos = playerRoot.position;
            Vector3 localPos = _plankTransform.InverseTransformPoint(feetPos);

            // Check if player is standing on the plank surface
            if (!IsLocalPointOnPlankSurface(localPos)) return;

            entitiesFound++;

            // Every player has identical base mass
            float totalWeight = _playerTorqueMass;

            // If player is carrying an object, add the carried object's weight!
            PlayerCarry carry = playerRoot.GetComponent<PlayerCarry>();
            if (carry != null && carry.IsCarrying && carry.CurrentCarryable != null)
            {
                if (carry.CurrentCarryable is SeeSawWeight sw)
                {
                    totalWeight += sw.WeightMass;
                }
                else if (carry.CurrentCarryable is CarryableObject co && co.TryGetComponent<Rigidbody>(out var crb))
                {
                    totalWeight += Mathf.Max(0.5f, crb.mass / 10f);
                }
                else
                {
                    totalWeight += 1.0f;
                }
            }

            if (Mathf.Abs(localPos.z) > _centerDeadzone)
            {
                netTorque += localPos.z * totalWeight;
            }
        }

        /// <summary>
        /// Moving platform support for the local player on this machine (Host or Client).
        /// Glues the character to the plank as it tilts so they ride it smoothly without sliding.
        /// </summary>
        private void ApplyLocalPlatformMotion(Quaternion deltaRot)
        {
            if (_localCharacterController == null || !_localCharacterController.enabled)
            {
                FindLocalCharacterController();
            }

            if (_localCharacterController != null && _localCharacterController.enabled)
            {
                Vector3 playerPos = _localCharacterController.transform.position;
                Vector3 localPos = _plankTransform.InverseTransformPoint(playerPos);

                if (IsLocalPointOnPlankSurface(localPos))
                {
                    Vector3 pivot = _plankTransform.position;
                    Vector3 rotatedTarget = pivot + deltaRot * (playerPos - pivot);
                    Vector3 delta = rotatedTarget - playerPos;

                    if (delta.sqrMagnitude > 0.000001f)
                    {
                        _localCharacterController.Move(delta);
                    }
                }
            }
        }

        private void FindLocalCharacterController()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
            {
                var localObj = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (localObj != null)
                {
                    _localCharacterController = localObj.GetComponent<CharacterController>();
                    return;
                }
            }

            var movements = Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
            for (int i = 0; i < movements.Length; i++)
            {
                if (movements[i].TryGetComponent<CharacterController>(out var cc) && cc.enabled)
                {
                    _localCharacterController = cc;
                    break;
                }
            }
        }

        private bool IsLocalPointOnPlankSurface(Vector3 localPos)
        {
            float halfL = _plankLength * 0.5f + 0.1f;
            float halfW = _plankWidth * 0.5f + 0.35f;

            return Mathf.Abs(localPos.x) <= halfW &&
                   Mathf.Abs(localPos.z) <= halfL &&
                   localPos.y >= -0.3f && localPos.y <= 2.2f;
        }

        private void ApplyPlankRotation(float angle)
        {
            Quaternion rot = _restRotation * Quaternion.Euler(angle, 0f, 0f);
            if (_plankRigidbody != null && _plankRigidbody.isKinematic)
            {
                _plankRigidbody.MoveRotation(rot);
            }
            else
            {
                _plankTransform.localRotation = rot;
            }
        }

        private void PlayLimitImpactSound()
        {
            if (_audioSource != null && _impactSound != null)
            {
                _audioSource.PlayOneShot(_impactSound, 0.75f);
            }
        }

        /// <summary>
        /// Snaps a given world position to the nearest point on the plank's top surface.
        /// </summary>
        public Vector3 ProjectToPlankSurface(Vector3 worldPoint, float heightOffset = 0f)
        {
            Vector3 local = _plankTransform.InverseTransformPoint(worldPoint);
            float halfL = _plankLength * 0.5f - 0.3f;
            float halfW = _plankWidth * 0.5f - 0.25f;

            local.x = Mathf.Clamp(local.x, -halfW, halfW);
            local.z = Mathf.Clamp(local.z, -halfL, halfL);
            local.y = (_plankThickness * 0.5f) + heightOffset;

            return _plankTransform.TransformPoint(local);
        }

        /// <summary>
        /// Checks if a world position is within the rectangular surface footprint of this see-saw plank.
        /// </summary>
        public bool IsWithinPlankBounds(Vector3 worldPoint)
        {
            Vector3 local = _plankTransform.InverseTransformPoint(worldPoint);
            return IsLocalPointOnPlankSurface(local);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Transform p = _plankTransform != null ? _plankTransform : transform;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.4f);
            Gizmos.matrix = Matrix4x4.TRS(
                p.position + p.up * (_plankThickness * 0.5f + 0.8f),
                p.rotation,
                Vector3.one
            );
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_plankWidth, 1.8f, _plankLength));

            // Draw deadzone
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.5f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_plankWidth, 1.8f, _centerDeadzone * 2f));
        }
#endif
    }
}
