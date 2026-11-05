using Unity.Netcode;
using UnityEngine;
using CoopGame.Obstacles;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Counterweight object specifically designed for See-Saw gameplay puzzles.
    /// 
    /// Gameplay Behavior:
    /// - Can be lifted, carried, placed gently (Right-Click), or thrown (Q) just like standard delivery packages.
    /// - Solo liftable: 1 player can lift and carry it.
    /// - High mass: Weighs down the see-saw (e.g. 2.5x player mass). When placed on Side A, 1 player can safely
    ///   cross Side B to a high ledge without Side B tilting down!
    /// - Plank Anchoring: When placed or dropped on the See-Saw plank, it securely anchors to that spot so it
    ///   will NOT slide or roll off down the incline.
    /// - Repositionable: A player can walk up to the anchored weight at any time and lift it again to change its position!
    /// </summary>
    [DisallowMultipleComponent]
    public class SeeSawWeight : CarryableObject
    {
        [Header("See-Saw Weight Mechanics")]
        [Tooltip("Relative torque mass applied to the see-saw plank (1 player = 1.0). Default 2.5 ensures 1 player cannot counteract it.")]
        [SerializeField] private float _weightMass = 2.5f;

        [Tooltip("Audio clip played when locking/anchoring onto the see-saw plank.")]
        [SerializeField] private AudioClip _anchorLatchSound;

        [Tooltip("Audio clip played when lifting off the see-saw plank.")]
        [SerializeField] private AudioClip _unanchorSound;

        [Header("Visual Anchor Glow / Feedback")]
        [SerializeField] private GameObject _anchorVisualIndicator;

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

        public float WeightMass => _weightMass;
        public bool IsAnchored => _isAnchored.Value;

        protected override void Awake()
        {
            base.Awake();
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

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _isAnchored.OnValueChanged += HandleAnchorChanged;
            UpdateAnchorVisuals(_isAnchored.Value);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _isAnchored.OnValueChanged -= HandleAnchorChanged;
        }

        private void HandleAnchorChanged(bool previousValue, bool newValue)
        {
            UpdateAnchorVisuals(newValue);
            if (newValue)
            {
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

            // When anchored, lock position and rotation relative to the plank
            if (_isAnchored.Value)
            {
                ResolveAnchoredTransform();
                return;
            }

            // On Server: If dropped or resting on a see-saw plank, auto-anchor when settled
            if (IsServer && !IsCarried && !IsPlacing && !_isAnchored.Value)
            {
                CheckAutoAnchorOnPlank();
            }
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

            Ray ray = new Ray(transform.position + Vector3.up * 0.2f, Vector3.down);
            if (Physics.SphereCast(ray, 0.3f, out RaycastHit hit, 0.9f, ~0, QueryTriggerInteraction.Ignore))
            {
                SeeSawPlank plank = hit.collider.GetComponentInParent<SeeSawPlank>();
                if (plank != null && plank.IsWithinPlankBounds(hit.point))
                {
                    AnchorToPlank(plank, hit.point);
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
            float heightOffset = 0.35f;
            if (_ownColliders != null && _ownColliders.Length > 0 && _ownColliders[0] != null)
            {
                heightOffset = _ownColliders[0].bounds.extents.y;
            }

            Vector3 snappedWorld = plank.ProjectToPlankSurface(worldPosition, heightOffset);
            Vector3 localPos = pTransform.InverseTransformPoint(snappedWorld);
            Quaternion localRot = Quaternion.Inverse(pTransform.rotation) * transform.rotation;

            _anchoredPlank = plank;
            _anchoredPlankTransform = pTransform;

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

            if (_isAnchored.Value)
            {
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

                UpdateAnchorVisuals(false);
                if (_unanchorSound != null && _audioSource != null)
                {
                    _audioSource.PlayOneShot(_unanchorSound, 0.75f);
                }
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

        /// <summary>
        /// Custom placement handler. If placing onto a See-Saw plank, anchors cleanly to the plank surface.
        /// Otherwise falls back to normal gentle ground placement.
        /// </summary>
        public override bool TryPlaceGently(ulong carrierId, Collider[] extraCarrierColliders = null)
        {
            if ((IsSpawned && !IsServer) || IsSecured || IsPlacing || !HasCarrier(carrierId)) return false;

            // Check if placing onto a SeeSawPlank directly below the carrier/weight
            Ray ray = new Ray(transform.position + Vector3.up * 0.2f, Vector3.down);
            if (Physics.SphereCast(ray, 0.35f, out RaycastHit hit, 2.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                SeeSawPlank plank = hit.collider.GetComponentInParent<SeeSawPlank>();
                if (plank != null && plank.IsWithinPlankBounds(hit.point))
                {
                    DetachCarrier(carrierId);
                    AnchorToPlank(plank, hit.point);
                    return true;
                }
            }

            // Normal ground placement fallback
            return base.TryPlaceGently(carrierId, extraCarrierColliders);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServer && IsSpawned) return;
            if (IsCarried || _isAnchored.Value) return;

            SeeSawPlank plank = collision.collider.GetComponentInParent<SeeSawPlank>();
            if (plank != null && plank.IsWithinPlankBounds(collision.contacts[0].point))
            {
                // When dropped or thrown onto plank, anchor on impact
                AnchorToPlank(plank, collision.contacts[0].point);
            }
        }
    }
}
