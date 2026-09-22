using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// FragileCargo represents the high-stakes physical package (egg, relic, or explosive crate)
    /// that players must collaboratively transport to the castle entrance.
    /// 
    /// Specification Requirements:
    /// 1. Inherits from CarryableObject (ICarryable).
    /// 2. MaxHP = 100 synchronized across network via NetworkVariable<int>.
    /// 3. Collision Damage Rules:
    ///    - Normal ground/obstacle impact: -15 HP (speed > threshold)
    ///    - Continuous drag/slide on ground: -5 HP (rate-limited)
    ///    - Throw impact: -20 HP (applied on first hard impact after being thrown)
    /// 4. Out of Bounds (transform.position.y < KillZ): Resets to initial spawn point.
    /// 5. Destruction (HP <= 0): White flash + smoke dissolve VFX, detaches carriers, triggers fail event.
    /// </summary>
    [DisallowMultipleComponent]
    public class FragileCargo : CarryableObject
    {
        [Header("Cargo Durability & HP")]
        [Tooltip("Maximum Hit Points of the cargo")]
        [SerializeField] private int _maxHP = 100;

        [Tooltip("Minimum impact velocity (m/s) required to inflict collision damage")]
        [SerializeField] private float _minImpactVelocity = 2.5f;

        [Tooltip("Damage dealt by normal ground or wall drop impact")]
        [SerializeField] private int _normalImpactDamage = 15;

        [Tooltip("Damage dealt on the first impact after being thrown by a player")]
        [SerializeField] private int _throwImpactDamage = 20;

        [Tooltip("Damage dealt per tick while sliding or scraping along rough surfaces")]
        [SerializeField] private int _dragDamage = 5;

        [Tooltip("Interval in seconds between drag damage ticks")]
        [SerializeField] private float _dragDamageInterval = 0.8f;

        [Tooltip("Minimum surface sliding velocity to register drag damage")]
        [SerializeField] private float _minDragVelocity = 0.8f;

        [Tooltip("Brief invulnerability buffer after an impact to prevent duplicate collision ticks")]
        [SerializeField] private float _impactInvulnerabilityDuration = 0.25f;

        [Header("Out of Bounds (Kill-Z)")]
        [Tooltip("Y-position below which the cargo is considered fallen and respawns")]
        [SerializeField] private float _killZ = -15.0f;

        [Tooltip("Initial spawn position (auto-captured on Awake)")]
        [SerializeField] private Vector3 _spawnPosition;
        [SerializeField] private Quaternion _spawnRotation;

        [Header("Visual Effects & Feedback")]
        [Tooltip("Optional particle system prefab spawned on damage impact")]
        [SerializeField] private ParticleSystem _impactFxPrefab;

        [Tooltip("Optional particle system prefab spawned on complete cargo destruction")]
        [SerializeField] private ParticleSystem _destructionFxPrefab;

        [Tooltip("Sound clip played when damaged")]
        [SerializeField] private AudioClip _impactSound;

        [Tooltip("Sound clip played on destruction")]
        [SerializeField] private AudioClip _destructionSound;

        // Synchronized Health
        public NetworkVariable<int> CurrentHP { get; } = new NetworkVariable<int>(
            100,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // State tracking
        private bool _isThrown = false;
        private float _lastImpactTime = -10f;
        private float _lastDragDamageTime = -10f;
        private bool _isDestroyed = false;
        private MeshRenderer[] _meshRenderers;
        private Color[] _originalColors;

        // Events
        public event Action<int, int> OnHPChanged;
        public static event Action<FragileCargo> OnCargoDestroyedGlobal;
        public static event Action<FragileCargo, int> OnCargoDamagedGlobal;
        public static event Action<FragileCargo> OnCargoRespawnedGlobal;

        public int MaxHP => _maxHP;
        public bool IsDestroyed => _isDestroyed;

        protected override void Awake()
        {
            base.Awake();

            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;

            _meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
            CacheOriginalColors();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                CurrentHP.Value = _maxHP;
            }

            CurrentHP.OnValueChanged += HandleHPValueChanged;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            CurrentHP.OnValueChanged -= HandleHPValueChanged;
        }

        private void HandleHPValueChanged(int previousValue, int newValue)
        {
            OnHPChanged?.Invoke(newValue, _maxHP);

            if (newValue < previousValue)
            {
                StartCoroutine(FlashWhiteRoutine());
            }

            if (newValue <= 0 && !_isDestroyed)
            {
                _isDestroyed = true;
                ExecuteDestructionVisuals();
            }
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();

            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
            if (isNetworked && !IsServer) return;

            if (_isDestroyed) return;

            // 1. Check Kill-Z Out of Bounds
            if (transform.position.y < _killZ)
            {
                RespawnAtSpawnPoint();
                return;
            }

            // 2. Check Continuous Drag / Scraping Damage
            CheckSurfaceDragDamage();
        }

        public override void ThrowObject(ulong clientId, Vector3 linearVelocity, Vector3 angularVelocity)
        {
            base.ThrowObject(clientId, linearVelocity, angularVelocity);

            // Mark cargo as thrown: next hard impact deals -20 HP
            _isThrown = true;
        }

        private void OnCollisionEnter(Collision collision)
        {
            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
            if (isNetworked && !IsServer) return;

            if (_isDestroyed) return;

            // Ignore collisions with player bodies while actively carried
            if (collision.gameObject.CompareTag("Player") && IsCarried)
            {
                return;
            }

            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed < _minImpactVelocity) return;

            // Debounce rapid multi-contact hits
            if (Time.time < _lastImpactTime + _impactInvulnerabilityDuration) return;
            _lastImpactTime = Time.time;

            int damage = _isThrown ? _throwImpactDamage : _normalImpactDamage;
            _isThrown = false; // Reset thrown state on first hard landing

            ApplyDamageServer(damage, $"Impact with '{collision.gameObject.name}' (Speed: {impactSpeed:F1} m/s)");
        }

        private void CheckSurfaceDragDamage()
        {
            if (_rigidbody == null || _isDestroyed) return;

            // Drag damage only triggers when moving horizontally while touching ground/surface
            float horizSpeed = new Vector2(_rigidbody.linearVelocity.x, _rigidbody.linearVelocity.z).magnitude;
            if (horizSpeed < _minDragVelocity) return;

            // Raycast downward to verify ground contact
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 0.65f))
            {
                if (Time.time >= _lastDragDamageTime + _dragDamageInterval)
                {
                    _lastDragDamageTime = Time.time;
                    ApplyDamageServer(_dragDamage, $"Surface dragging (Speed: {horizSpeed:F1} m/s)");
                }
            }
        }

        /// <summary>
        /// Server-authoritative damage application.
        /// </summary>
        public void ApplyDamageServer(int damageAmount, string reason = "")
        {
            if (_isDestroyed) return;

            int newHP = Mathf.Max(0, CurrentHP.Value - damageAmount);
            CurrentHP.Value = newHP;

            Debug.Log($"[FragileCargo] '{name}' took -{damageAmount} damage ({reason}). Remaining HP: {newHP}/{_maxHP}");

            OnCargoDamagedGlobal?.Invoke(this, damageAmount);
            PlayImpactFxClientRpc(transform.position);

            if (newHP <= 0)
            {
                TriggerDestructionServer();
            }
        }

        private void TriggerDestructionServer()
        {
            if (_isDestroyed) return;
            _isDestroyed = true;

            Debug.LogWarning($"[FragileCargo] '{name}' HAS BEEN DESTROYED! Triggering Mission Fail / Game Over.");

            // Detach any carriers immediately
            DetachAllCarriers();

            // Disable physics interaction
            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.isKinematic = true;
            }

            TriggerDestructionClientRpc(transform.position);
            OnCargoDestroyedGlobal?.Invoke(this);
        }

        [ClientRpc]
        private void PlayImpactFxClientRpc(Vector3 impactPoint)
        {
            if (_impactFxPrefab != null)
            {
                Instantiate(_impactFxPrefab, impactPoint, Quaternion.identity);
            }

            if (_impactSound != null)
            {
                AudioSource.PlayClipAtPoint(_impactSound, impactPoint, 1.0f);
            }
        }

        [ClientRpc]
        private void TriggerDestructionClientRpc(Vector3 destructionPoint)
        {
            ExecuteDestructionVisuals();

            if (_destructionFxPrefab != null)
            {
                Instantiate(_destructionFxPrefab, destructionPoint, Quaternion.identity);
            }
            else
            {
                // Procedural smoke & flash explosion fallback
                SpawnProceduralSmokeVFX(destructionPoint);
            }

            if (_destructionSound != null)
            {
                AudioSource.PlayClipAtPoint(_destructionSound, destructionPoint, 1.0f);
            }
        }

        private void ExecuteDestructionVisuals()
        {
            StartCoroutine(DissolveAndDeactivateRoutine());
        }

        private IEnumerator DissolveAndDeactivateRoutine()
        {
            // Rapid white flash
            float elapsed = 0f;
            float duration = 0.6f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                if (_meshRenderers != null)
                {
                    foreach (var mr in _meshRenderers)
                    {
                        if (mr != null && mr.material != null)
                        {
                            mr.material.color = Color.Lerp(Color.white, new Color(0.2f, 0.2f, 0.2f, 0f), t);
                        }
                    }
                }
                yield return null;
            }

            // Hide visuals
            if (_meshRenderers != null)
            {
                foreach (var mr in _meshRenderers)
                {
                    if (mr != null) mr.enabled = false;
                }
            }

            if (_ownColliders != null)
            {
                foreach (var col in _ownColliders)
                {
                    if (col != null) col.enabled = false;
                }
            }
        }

        private IEnumerator FlashWhiteRoutine()
        {
            if (_meshRenderers == null || _meshRenderers.Length == 0) yield break;

            // Flash bright white
            foreach (var mr in _meshRenderers)
            {
                if (mr != null && mr.material != null)
                {
                    mr.material.color = Color.white;
                }
            }

            yield return new WaitForSeconds(0.12f);

            // Restore original colors
            RestoreOriginalColors();
        }

        private void CacheOriginalColors()
        {
            if (_meshRenderers == null) return;
            _originalColors = new Color[_meshRenderers.Length];
            for (int i = 0; i < _meshRenderers.Length; i++)
            {
                if (_meshRenderers[i] != null && _meshRenderers[i].material != null)
                {
                    _originalColors[i] = _meshRenderers[i].material.color;
                }
            }
        }

        private void RestoreOriginalColors()
        {
            if (_meshRenderers == null || _originalColors == null) return;
            for (int i = 0; i < _meshRenderers.Length; i++)
            {
                if (_meshRenderers[i] != null && _meshRenderers[i].material != null && i < _originalColors.Length)
                {
                    _meshRenderers[i].material.color = _originalColors[i];
                }
            }
        }

        /// <summary>
        /// Resets the cargo to its initial spawn point when falling out of bounds.
        /// </summary>
        public void RespawnAtSpawnPoint()
        {
            DetachAllCarriers();

            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }

            transform.position = _spawnPosition;
            transform.rotation = _spawnRotation;

            _isThrown = false;
            Debug.Log($"[FragileCargo] '{name}' fell out of bounds (< {_killZ}m). Respawned at {_spawnPosition}.");

            OnCargoRespawnedGlobal?.Invoke(this);
            RespawnClientRpc(_spawnPosition);
        }

        [ClientRpc]
        private void RespawnClientRpc(Vector3 pos)
        {
            transform.position = pos;
            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Generates a standalone procedural smoke dissolve VFX if no particle prefab is supplied.
        /// </summary>
        private void SpawnProceduralSmokeVFX(Vector3 point)
        {
            GameObject smokeObj = new GameObject("Procedural_Smoke_VFX");
            smokeObj.transform.position = point;

            ParticleSystem ps = smokeObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 1.2f;
            main.startSpeed = 2.5f;
            main.startSize = 0.8f;
            main.startColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 30) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;

            ps.Play();
            Destroy(smokeObj, 2.5f);
        }
    }
}
