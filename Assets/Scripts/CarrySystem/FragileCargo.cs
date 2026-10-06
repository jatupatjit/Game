using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
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

        [Tooltip("Minimum time between any two HP losses, including impacts and dragging")]
        [SerializeField, Min(0f)] private float _damageCooldownSeconds = 1.25f;

        [Tooltip("Ignore settling collisions and drag immediately after spawning or returning to the start")]
        [SerializeField, Min(0f)] private float _spawnDamageGraceSeconds = 1.25f;

        [Tooltip("Let existing surface contacts settle when the first player picks the cargo up")]
        [SerializeField, Min(0f)] private float _pickupDamageGraceSeconds = 0.8f;

        [Tooltip("Require continuous real ground contact and sliding before applying scrape damage")]
        [SerializeField, Min(0.1f)] private float _minimumScrapeDuration = 0.5f;

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
        private float _damageEnabledAtFixedTime;
        private float _nextDamageAllowedAtFixedTime;
        private float _pickupGraceEndsAtFixedTime;
        private float _pickupFallbackGraceEndsAtFixedTime;
        private float _scrapeStartedAtFixedTime = -1f;
        private bool _isDestroyed = false;
        private MeshRenderer[] _meshRenderers;
        private Color[] _originalColors;

        private struct PhysicsContactSample
        {
            public Collider Other;
            public Vector3 Point;
            public Vector3 Normal;
            public Vector3 RelativeVelocity;
            public bool IsImpact;
        }

        // Collision callbacks only capture data. The next FixedUpdate evaluates
        // impact and scrape damage without allocating contact arrays each tick.
        private readonly PhysicsContactSample[] _physicsContacts = new PhysicsContactSample[64];
        private readonly Collider[] _recentContactColliders = new Collider[16];
        private readonly Collider[] _pickupProtectedColliders = new Collider[16];
        private int _physicsContactCount;
        private int _recentContactColliderCount;
        private int _pickupProtectedColliderCount;

        // Events
        public event Action<int, int> OnHPChanged;
        public static event Action<FragileCargo> OnCargoDestroyedGlobal;
        public static event Action<FragileCargo, int> OnCargoDamagedGlobal;
        public static event Action<FragileCargo> OnCargoRespawnedGlobal;

        public int MaxHP => _maxHP;
        public bool IsDestroyed => _isDestroyed;
        public override bool CanBeCarried => !_isDestroyed && base.CanBeCarried;
        private readonly NetworkVariable<bool> _padding = new(false);
        public bool HasPadding => _padding.Value;
        public void AddPadding() { if (IsServer) _padding.Value = true; }
        private int _queuedDamage;
        private string _queuedReason;
        private bool _respawnPending;

        protected override void Awake()
        {
            base.Awake();

            MissionFailUI.EnsureInstance();

            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _damageEnabledAtFixedTime = Time.fixedTime + _spawnDamageGraceSeconds;
            _nextDamageAllowedAtFixedTime = _damageEnabledAtFixedTime;

            if (GetComponent<FragileCargoHealthUI>() == null)
                gameObject.AddComponent<FragileCargoHealthUI>();

            _meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
            CacheOriginalColors();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                CurrentHP.Value = _maxHP;
                _damageEnabledAtFixedTime = Time.fixedTime + _spawnDamageGraceSeconds;
                _nextDamageAllowedAtFixedTime = _damageEnabledAtFixedTime;
                ResetPhysicsContactState();
            }

            CurrentHP.OnValueChanged += HandleHPValueChanged;
            // NGO applies the spawn snapshot without raising OnValueChanged.
            GetComponent<FragileCargoHealthUI>()?.InitializeHealth(CurrentHP.Value, _maxHP);
            // A late join can arrive during the destruction-to-despawn window.
            // Hydrate the existing state without presenting it as a new hit.
            if (!IsServer && CurrentHP.Value <= 0)
                HandleHPValueChanged(CurrentHP.Value, CurrentHP.Value);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            CurrentHP.OnValueChanged -= HandleHPValueChanged;
            ResetPhysicsContactState();
        }

        private void HandleHPValueChanged(int previousValue, int newValue)
        {
            OnHPChanged?.Invoke(newValue, _maxHP);

            if (newValue > 0 && newValue < previousValue)
            {
                StartCoroutine(FlashWhiteRoutine());
            }

            // The server completes detachment, physics shutdown and the fail event in
            // TriggerDestructionServer. Clients only start their presentation here.
            bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (newValue <= 0 && !_isDestroyed && networked && !IsServer)
            {
                _isDestroyed = true;
                ExecuteDestructionVisuals();
                MissionFailUI.EnsureInstance().Show();
            }
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();

            bool isNetworked = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
            if (isNetworked && !IsServer) return;

            if (_isDestroyed || IsSecured)
            {
                _physicsContactCount = 0;
                _recentContactColliderCount = 0;
                _scrapeStartedAtFixedTime = -1f;
                return;
            }
            if (_queuedDamage > 0)
            {
                int damage = _queuedDamage; _queuedDamage = 0;
                ApplyQueuedDamage(damage, _queuedReason);
                if (_isDestroyed) return;
            }

            // 1. Check Kill-Z Out of Bounds
            if (_respawnPending || transform.position.y < _killZ)
            {
                _respawnPending = false;
                ExecuteRespawn();
                return;
            }

            // 2. Evaluate the previous physics simulation's actual contacts.
            ProcessPhysicsContacts();
        }

        protected override void OnCarrierAttached(bool firstCarrier)
        {
            base.OnCarrierAttached(firstCarrier);
            if (_isDestroyed || IsSecured) return;

            float now = Time.fixedTime;
            if (firstCarrier || now >= _pickupGraceEndsAtFixedTime)
                _pickupProtectedColliderCount = 0;

            for (int i = 0; i < _recentContactColliderCount; i++)
                AddUniqueCollider(_pickupProtectedColliders, ref _pickupProtectedColliderCount, _recentContactColliders[i]);
            for (int i = 0; i < _physicsContactCount; i++)
                AddUniqueCollider(_pickupProtectedColliders, ref _pickupProtectedColliderCount, _physicsContacts[i].Other);

            float grace = firstCarrier ? _pickupDamageGraceSeconds : Mathf.Min(0.2f, _pickupDamageGraceSeconds);
            _pickupGraceEndsAtFixedTime = Mathf.Max(_pickupGraceEndsAtFixedTime, now + grace);

            // A pickup before the first collision sample has a short fallback;
            // known old contacts get grace while new wall/obstacle hits still hurt.
            _pickupFallbackGraceEndsAtFixedTime = _pickupProtectedColliderCount == 0
                ? now + Mathf.Min(0.15f, grace)
                : now;
            _scrapeStartedAtFixedTime = -1f;
            if (firstCarrier) _isThrown = false;
        }

        public override void ThrowObject(ulong clientId, Vector3 linearVelocity, Vector3 angularVelocity)
        {
            if (_isDestroyed || IsSecured ||
                (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer)) return;
            base.ThrowObject(clientId, linearVelocity, angularVelocity);

            // Mark cargo as thrown: next hard impact deals -20 HP
            _isThrown = true;
            _pickupGraceEndsAtFixedTime = Time.fixedTime;
            _pickupFallbackGraceEndsAtFixedTime = Time.fixedTime;
            _pickupProtectedColliderCount = 0;
        }

        private void OnCollisionEnter(Collision collision)
        {
            RecordPhysicsContacts(collision, true);
        }

        private void OnCollisionStay(Collision collision)
        {
            RecordPhysicsContacts(collision, false);
        }

        private void RecordPhysicsContacts(Collision collision, bool isImpact)
        {
            if (_isDestroyed || IsSecured ||
                (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer)) return;

            // Player impact is never cargo damage, including untagged child limbs.
            Collider other = collision.collider;
            if (other == null || other.GetComponentInParent<CoopGame.Player.NetworkPlayer>() != null ||
                other.CompareTag("Player") || other.transform.root.CompareTag("Player")) return;

            Vector3 relativeVelocity = collision.relativeVelocity;
            for (int i = 0; i < collision.contactCount && _physicsContactCount < _physicsContacts.Length; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                _physicsContacts[_physicsContactCount++] = new PhysicsContactSample
                {
                    Other = other,
                    Point = contact.point,
                    Normal = contact.normal,
                    RelativeVelocity = relativeVelocity,
                    IsImpact = isImpact
                };
            }
        }

        private void ProcessPhysicsContacts()
        {
            int contactCount = _physicsContactCount;
            _physicsContactCount = 0;
            // Sleeping bodies do not receive OnCollisionStay. Keep their last
            // real support surfaces available for the next pickup's settle grace.
            if (contactCount > 0 || _rigidbody == null || !_rigidbody.IsSleeping())
                _recentContactColliderCount = 0;
            float now = Time.fixedTime;
            bool canTakePhysicsDamage = now >= _damageEnabledAtFixedTime && now >= _nextDamageAllowedAtFixedTime;
            float strongestImpactSpeed = 0f;
            Collider strongestImpactCollider = null;
            float scrapeSpeed = 0f;
            float colliderBottom = GetSolidColliderBottom();

            for (int i = 0; i < contactCount; i++)
            {
                PhysicsContactSample sample = _physicsContacts[i];
                if (sample.Other == null || !sample.Other.enabled) continue;
                AddUniqueCollider(_recentContactColliders, ref _recentContactColliderCount, sample.Other);

                if (sample.IsImpact && canTakePhysicsDamage && !IsPickupSettlingContact(sample.Other, now))
                {
                    // Tangential walking/sliding speed is not an impact into a surface.
                    float normalSpeed = Mathf.Abs(Vector3.Dot(sample.RelativeVelocity, sample.Normal));
                    if (normalSpeed > strongestImpactSpeed)
                    {
                        strongestImpactSpeed = normalSpeed;
                        strongestImpactCollider = sample.Other;
                    }
                }

                // Use actual support contact near the cargo's measured collider
                // bottom. Merely carrying close to a floor is never scraping.
                if (_rigidbody != null && !float.IsPositiveInfinity(colliderBottom) &&
                    sample.Normal.y >= 0.55f && sample.Point.y <= colliderBottom + 0.12f)
                {
                    Vector3 relativeVelocity = _rigidbody.GetPointVelocity(sample.Point);
                    Rigidbody surfaceBody = sample.Other.attachedRigidbody;
                    if (surfaceBody != null && surfaceBody != _rigidbody)
                        relativeVelocity -= surfaceBody.GetPointVelocity(sample.Point);
                    float tangentialSpeed = Vector3.ProjectOnPlane(relativeVelocity, sample.Normal).magnitude;
                    scrapeSpeed = Mathf.Max(scrapeSpeed, tangentialSpeed);
                }
            }

            if (strongestImpactCollider != null && strongestImpactSpeed >= _minImpactVelocity &&
                now >= _lastImpactTime + _impactInvulnerabilityDuration)
            {
                _lastImpactTime = now;
                int damage = _isThrown ? _throwImpactDamage : _normalImpactDamage;
                _isThrown = false;
                ApplyQueuedDamage(damage, $"Impact with '{strongestImpactCollider.name}' (Normal speed: {strongestImpactSpeed:F1} m/s)");
            }

            if (_isDestroyed || now < _damageEnabledAtFixedTime || now < _pickupGraceEndsAtFixedTime ||
                scrapeSpeed < _minDragVelocity)
            {
                _scrapeStartedAtFixedTime = -1f;
                return;
            }

            if (_scrapeStartedAtFixedTime < 0f) _scrapeStartedAtFixedTime = now;
            if (now - _scrapeStartedAtFixedTime < _minimumScrapeDuration ||
                now < _nextDamageAllowedAtFixedTime || now < _lastDragDamageTime + _dragDamageInterval) return;

            _lastDragDamageTime = now;
            ApplyQueuedDamage(_dragDamage, $"Surface dragging (Speed: {scrapeSpeed:F1} m/s)");
        }

        private bool IsPickupSettlingContact(Collider other, float now)
        {
            if (now >= _pickupGraceEndsAtFixedTime) return false;
            if (now < _pickupFallbackGraceEndsAtFixedTime) return true;
            for (int i = 0; i < _pickupProtectedColliderCount; i++)
                if (_pickupProtectedColliders[i] == other) return true;
            return false;
        }

        private float GetSolidColliderBottom()
        {
            float bottom = float.PositiveInfinity;
            if (_ownColliders != null)
            {
                for (int i = 0; i < _ownColliders.Length; i++)
                {
                    Collider own = _ownColliders[i];
                    if (own != null && own.enabled && !own.isTrigger && own.attachedRigidbody == _rigidbody)
                        bottom = Mathf.Min(bottom, own.bounds.min.y);
                }
            }
            return bottom;
        }

        private static void AddUniqueCollider(Collider[] buffer, ref int count, Collider collider)
        {
            if (collider == null) return;
            for (int i = 0; i < count; i++)
                if (buffer[i] == collider) return;
            if (count < buffer.Length) buffer[count++] = collider;
        }

        private void ResetPhysicsContactState()
        {
            _physicsContactCount = 0;
            _recentContactColliderCount = 0;
            _pickupProtectedColliderCount = 0;
            _pickupGraceEndsAtFixedTime = 0f;
            _pickupFallbackGraceEndsAtFixedTime = 0f;
            _scrapeStartedAtFixedTime = -1f;
        }

        /// <summary>
        /// Server-authoritative damage application.
        /// </summary>
        public void ApplyDamageServer(int damageAmount, string reason = "")
        {
            if (_isDestroyed || IsSecured || damageAmount <= 0 ||
                (IsSpawned && !IsServer) || Time.fixedTime < _nextDamageAllowedAtFixedTime) return;
            if (damageAmount > _queuedDamage) { _queuedDamage = damageAmount; _queuedReason = reason; }
        }

        private void ApplyQueuedDamage(int damageAmount, string reason)
        {
            if (_isDestroyed || damageAmount <= 0) return;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer) return;
            if (Time.fixedTime < _nextDamageAllowedAtFixedTime) return;

            if (HasPadding) { damageAmount = Mathf.CeilToInt(damageAmount * .5f); _padding.Value = false; }
            if (HasGuardCard()) damageAmount = Mathf.CeilToInt(damageAmount * .5f);
            damageAmount = Mathf.Min(damageAmount, CurrentHP.Value);
            int previousHP = CurrentHP.Value;
            int newHP = Mathf.Max(0, CurrentHP.Value - damageAmount);
            if (newHP == CurrentHP.Value) return;
            _nextDamageAllowedAtFixedTime = Time.fixedTime + _damageCooldownSeconds;
            CurrentHP.Value = newHP;
            // Offline cargo never subscribes through OnNetworkSpawn. Networked
            // cargo already emits this once from NetworkVariable.OnValueChanged.
            if (!IsSpawned) HandleHPValueChanged(previousHP, newHP);

            Debug.Log($"[FragileCargo] '{name}' took {damageAmount} damage ({reason}). Remaining HP: {newHP}/{_maxHP}");

            OnCargoDamagedGlobal?.Invoke(this, damageAmount);
            if (IsSpawned) PlayImpactFxClientRpc(transform.position);
            else PlayImpactFeedback(transform.position);

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

            ExecuteDestructionVisuals();
            MissionFailUI.EnsureInstance().Show();
            if (IsSpawned) TriggerDestructionClientRpc(transform.position);
            else PlayDestructionFeedback(transform.position);
            OnCargoDestroyedGlobal?.Invoke(this);
            StartCoroutine(RemoveDestroyedCargoRoutine());
        }

        private IEnumerator RemoveDestroyedCargoRoutine()
        {
            yield return new WaitForSecondsRealtime(0.8f);
            if (IsSpawned && IsServer)
            {
                NetworkObject.Despawn(true);
                if (this != null) gameObject.SetActive(false);
            }
            else if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                Destroy(gameObject);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PlayImpactFxClientRpc(Vector3 impactPoint)
        {
            PlayImpactFeedback(impactPoint);
        }

        private void PlayImpactFeedback(Vector3 impactPoint)
        {
            if (_impactFxPrefab == null) CoopGame.Network.GameplayFeedback.Burst(impactPoint, new Color(.75f, .57f, .32f));
            if (_impactFxPrefab != null)
            {
                Instantiate(_impactFxPrefab, impactPoint, Quaternion.identity);
            }

            if (_impactSound == null) CoopGame.Network.GameplayFeedback.Play(CoopGame.Network.GameplayFeedback.Cue.Impact, impactPoint);
            if (_impactSound != null)
            {
                AudioSource.PlayClipAtPoint(_impactSound, impactPoint, 1.0f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TriggerDestructionClientRpc(Vector3 destructionPoint)
        {
            if (!_isDestroyed)
            {
                _isDestroyed = true;
                ExecuteDestructionVisuals();
            }

            MissionFailUI.EnsureInstance().Show();

            PlayDestructionFeedback(destructionPoint);
        }

        private void PlayDestructionFeedback(Vector3 destructionPoint)
        {

            if (_destructionFxPrefab != null)
            {
                Instantiate(_destructionFxPrefab, destructionPoint, Quaternion.identity);
            }
            else
            {
                // Procedural smoke & flash explosion fallback
                SpawnProceduralSmokeVFX(destructionPoint);
            }

            if (_destructionSound == null) CoopGame.Network.GameplayFeedback.Play(CoopGame.Network.GameplayFeedback.Cue.Broken, destructionPoint);
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

            FragileCargoHealthUI healthUI = GetComponent<FragileCargoHealthUI>();
            if (healthUI != null) healthUI.Hide();
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
            if ((!IsSpawned || IsServer) && !_isDestroyed && !IsSecured) _respawnPending = true;
        }

        private void ExecuteRespawn()
        {
            bool isNetworked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (isNetworked && !IsServer) return;
            DetachAllCarriers();

            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }

            NetworkTransform networkTransform = GetComponent<NetworkTransform>();
            if (isNetworked && networkTransform != null)
                networkTransform.Teleport(_spawnPosition, _spawnRotation, transform.localScale);
            else
                transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);

            _rigidbody?.WakeUp();

            _isThrown = false;
            _lastImpactTime = Time.fixedTime;
            _lastDragDamageTime = Time.fixedTime;
            ResetPhysicsContactState();
            _damageEnabledAtFixedTime = Time.fixedTime + _spawnDamageGraceSeconds;
            _nextDamageAllowedAtFixedTime = _damageEnabledAtFixedTime;
            Debug.Log($"[FragileCargo] '{name}' fell out of bounds (< {_killZ}m). Respawned at {_spawnPosition}.");

            OnCargoRespawnedGlobal?.Invoke(this);
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
