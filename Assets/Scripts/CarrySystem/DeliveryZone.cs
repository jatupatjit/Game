using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace CoopGame.CarrySystem
{
    public enum DeliveryDetectionMode
    {
        [Tooltip("Only the specific GameObject assigned in Target Delivery Object will trigger the delivery.")]
        SpecificObjectOnly,

        [Tooltip("If Target Delivery Object is set, checks that object. If left empty, automatically detects any CarryableObject or FragileCargo.")]
        SpecificObjectOrCarryable,

        [Tooltip("Detects any CarryableObject or FragileCargo.")]
        AnyCarryable,

        [Tooltip("Detects any GameObject with the specified Tag.")]
        ByTag
    }

    /// <summary>
    /// Circular Delivery Zone that displays a glowing circular ring effect.
    /// Effect is Red around the circle when waiting, and turns Green when the target delivery object is inside.
    /// The target delivery object can be configured directly in the Inspector.
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    public class DeliveryZone : MonoBehaviour
    {
        [Header("Delivery Target (Configure in Inspector)")]
        [Tooltip("The specific GameObject that must be delivered into this zone. Drag any GameObject here. If left empty under 'SpecificObjectOrCarryable' mode, it will accept any carryable package.")]
        [SerializeField] private GameObject _targetDeliveryObject;

        [Tooltip("How the zone identifies the delivery object.")]
        [SerializeField] private DeliveryDetectionMode _detectionMode = DeliveryDetectionMode.SpecificObjectOrCarryable;

        [Tooltip("Tag to check if Detection Mode is set to 'ByTag'.")]
        [SerializeField] private string _targetTag = "Carryable";

        [Header("Zone Dimensions")]
        [Tooltip("Radius of the circular delivery zone in world units.")]
        [Range(0.5f, 20f)]
        [SerializeField] private float _radius = 3.5f;

        [Tooltip("Height of the trigger detection volume.")]
        [Range(0.5f, 10f)]
        [SerializeField] private float _triggerHeight = 3.0f;

        [Header("Color Effect Settings")]
        [Tooltip("Glowing effect color when waiting for delivery (Red around circle).")]
        [ColorUsage(true, true)]
        [SerializeField] private Color _waitingRedColor = new Color(1.0f, 0.15f, 0.15f, 1.0f);

        [Tooltip("Glowing effect color when object is inside the zone (Green).")]
        [ColorUsage(true, true)]
        [SerializeField] private Color _deliveredGreenColor = new Color(0.15f, 1.0f, 0.35f, 1.0f);

        [Tooltip("Speed of transition between red and green.")]
        [Range(1f, 20f)]
        [SerializeField] private float _colorTransitionSpeed = 8.0f;

        [Header("Delivery Rules")]
        [Tooltip("If true, once the target object enters the zone, it remains delivered (Green) permanently. If false, it turns back Red if the object is removed.")]
        [SerializeField] private bool _lockDeliveryOnceEntered = false;

        [Tooltip("Optional: secure and lock the object's rigidbody in place when delivered.")]
        [SerializeField] private bool _freezeObjectOnDelivery = false;

        [Header("Audio & Visual References (Auto-created if empty)")]
        [SerializeField] private Renderer _ringRenderer;
        [SerializeField] private TextMesh _zoneLabel;
        [SerializeField] private ParticleSystem _perimeterParticles;
        [SerializeField] private ParticleSystem _successBurstParticles;
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _deliverySuccessSfx;

        [Header("Events")]
        public UnityEvent<GameObject> OnTargetEntered;
        public UnityEvent<GameObject> OnTargetExited;
        public UnityEvent<GameObject> OnDeliverySuccess;
        public UnityEvent<bool> OnZoneStateChanged; // true = green/inside, false = red/waiting

        // Runtime state
        private readonly HashSet<Collider> _activeCollidersInZone = new HashSet<Collider>();
        private bool _isDelivered = false;
        private bool _isTargetInside = false;
        private Color _currentColor;
        private CapsuleCollider _triggerCollider;
        private MaterialPropertyBlock _propBlock;
        private Material _generatedRingMaterial;
        private Rigidbody _pendingFreeze;
        private DeliveryPoint _deliveryPoint;
        private float _nextTargetSearchTime;
        private static readonly Predicate<Collider> DestroyedCollider = collider => collider == null;
        private bool _labelDelivered;
        private Camera _viewCamera;
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor");

        public GameObject TargetDeliveryObject
        {
            get => _targetDeliveryObject;
            set => _targetDeliveryObject = value;
        }

        public DeliveryDetectionMode DetectionMode
        {
            get => _detectionMode;
            set => _detectionMode = value;
        }

        public float Radius
        {
            get => _radius;
            set
            {
                _radius = Mathf.Max(0.5f, value);
                UpdateColliderAndVisualScale();
            }
        }

        public bool IsTargetInside => _isTargetInside;
        public bool IsDelivered => _deliveryPoint != null ? _deliveryPoint.IsDelivered : _isDelivered;

        private void Awake()
        {
            _deliveryPoint = GetComponent<DeliveryPoint>();
            _propBlock = new MaterialPropertyBlock();
            _currentColor = _waitingRedColor;

            EnsureTriggerCollider();
            EnsureVisuals();
            ApplyColorImmediate(_waitingRedColor);
        }

        private void Start()
        {
            if (_targetDeliveryObject == null)
            {
                AutoDetectTargetDeliveryObject();
            }
        }

        private void AutoDetectTargetDeliveryObject()
        {
            // Explicit targets and tag/any-item modes must keep their configured semantics.
            if (_detectionMode != DeliveryDetectionMode.SpecificObjectOrCarryable ||
                _targetDeliveryObject != null || Time.unscaledTime < _nextTargetSearchTime)
                return;
            _nextTargetSearchTime = Time.unscaledTime + 1f;
            var cargo = FindAnyObjectByType<FragileCargo>();
            if (cargo != null)
            {
                _targetDeliveryObject = cargo.gameObject;
                return;
            }

            var carryable = FindAnyObjectByType<CarryableObject>();
            if (carryable != null)
            {
                _targetDeliveryObject = carryable.gameObject;
            }
        }

        private void OnEnable()
        {
            EnsureTriggerCollider();
            EnsureVisuals();
            UpdateColliderAndVisualScale();
            ApplyColorImmediate(IsDelivered || _isTargetInside ? _deliveredGreenColor : _waitingRedColor);
        }

        private void OnDisable()
        {
            _activeCollidersInZone.Clear();
            _isTargetInside = false;
            _pendingFreeze = null;
        }

        private void OnDestroy()
        {
            if (_generatedRingMaterial != null)
            {
                if (Application.isPlaying) Destroy(_generatedRingMaterial);
                else DestroyImmediate(_generatedRingMaterial);
                _generatedRingMaterial = null;
            }
        }

        private void Update()
        {
            bool shouldBeGreen = _deliveryPoint != null
                ? _deliveryPoint.IsDelivered : _isDelivered || _isTargetInside;
            if (_zoneLabel != null && _labelDelivered != shouldBeGreen)
            {
                _labelDelivered = shouldBeGreen;
                _zoneLabel.text = shouldBeGreen ? "ส่งลังแล้ว ไปที่ประตูได้เลย" :
                    "ถึงแล้ว วางลังในวงนี้เลย";
            }
            Color targetColor = shouldBeGreen ? _deliveredGreenColor : _waitingRedColor;

            if (Application.isPlaying)
            {
                _currentColor = Color.Lerp(_currentColor, targetColor, Time.deltaTime * _colorTransitionSpeed);
            }
            else
            {
                _currentColor = targetColor;
                UpdateColliderAndVisualScale();
            }

            ApplyCurrentColor();
        }

        private void LateUpdate()
        {
            if (_zoneLabel == null) return;
            var localCamera = CoopGame.Player.PlayerCameraController.LocalInstance;
            if (localCamera != null) _viewCamera = localCamera.PlayerCamera;
            else if (_viewCamera == null) _viewCamera = Camera.main;
            if (_viewCamera == null) return;
            Vector3 away = _zoneLabel.transform.position - _viewCamera.transform.position;
            if (away.sqrMagnitude > 0.001f)
                _zoneLabel.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        private void FixedUpdate()
        {
            AutoDetectTargetDeliveryObject();
            RefreshZoneTargetStatus(_targetDeliveryObject);
            if (_pendingFreeze == null) return;
            if (Unity.Netcode.NetworkManager.Singleton != null &&
                Unity.Netcode.NetworkManager.Singleton.IsListening &&
                !Unity.Netcode.NetworkManager.Singleton.IsServer)
            {
                _pendingFreeze = null;
                return;
            }

            _pendingFreeze.linearVelocity = Vector3.zero;
            _pendingFreeze.angularVelocity = Vector3.zero;
            _pendingFreeze.isKinematic = true;
            _pendingFreeze = null;
        }

        private void OnValidate()
        {
            if (_radius < 0.5f) _radius = 0.5f;
            if (_triggerHeight < 0.5f) _triggerHeight = 0.5f;
        }

        private void EnsureTriggerCollider()
        {
            if (_triggerCollider == null)
            {
                _triggerCollider = GetComponent<CapsuleCollider>();
                if (_triggerCollider == null)
                {
                    _triggerCollider = gameObject.AddComponent<CapsuleCollider>();
                }
            }

            _triggerCollider.isTrigger = true;
            _triggerCollider.direction = 1; // Y-Axis
            _triggerCollider.radius = _radius;
            _triggerCollider.height = _triggerHeight;
            _triggerCollider.center = new Vector3(0f, _triggerHeight * 0.5f, 0f);
        }

        private void UpdateColliderAndVisualScale()
        {
            if (_triggerCollider != null)
            {
                _triggerCollider.radius = _radius;
                _triggerCollider.height = _triggerHeight;
                _triggerCollider.center = new Vector3(0f, _triggerHeight * 0.5f, 0f);
            }

            if (_ringRenderer != null)
            {
                // Quad: flat on ground (rotated 90 on X), diameter is 2 * radius in X and Y
                _ringRenderer.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                _ringRenderer.transform.localScale = new Vector3(_radius * 2.0f, _radius * 2.0f, 1.0f);
                _ringRenderer.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            }

            if (_perimeterParticles != null)
            {
                _perimeterParticles.gameObject.SetActive(false);
            }
        }

        public void EnsureVisuals()
        {
            // 1. Create or bind ground ring renderer (Quad mesh)
            if (_ringRenderer == null)
            {
                Transform existingRing = transform.Find("Zone_Circle_Visual");
                if (existingRing != null)
                {
                    _ringRenderer = existingRing.GetComponent<Renderer>();
                }
                else
                {
                    GameObject ringObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    ringObj.name = "Zone_Circle_Visual";
                    ringObj.transform.SetParent(transform, false);
                    ringObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    ringObj.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                    ringObj.transform.localScale = new Vector3(_radius * 2.0f, _radius * 2.0f, 1.0f);

                    Collider quadCol = ringObj.GetComponent<Collider>();
                    if (quadCol != null)
                    {
                        if (Application.isPlaying) Destroy(quadCol);
                        else DestroyImmediate(quadCol);
                    }

                    _ringRenderer = ringObj.GetComponent<Renderer>();
                }
            }

            // Ensure Quad mesh is present on the visual ring
            if (_ringRenderer != null)
            {
                MeshFilter mf = _ringRenderer.GetComponent<MeshFilter>();
                if (mf == null) mf = _ringRenderer.gameObject.AddComponent<MeshFilter>();
                if (mf.sharedMesh == null || mf.sharedMesh.name.Contains("Cylinder"))
                {
                    GameObject tempQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    mf.sharedMesh = tempQuad.GetComponent<MeshFilter>().sharedMesh;
                    if (Application.isPlaying) Destroy(tempQuad);
                    else DestroyImmediate(tempQuad);
                }

                // Ensure material with DontDropIt/DeliveryZoneRing shader is assigned
                Material ringMat = _ringRenderer.sharedMaterial;
                if (ringMat == null || ringMat.shader == null || ringMat.shader.name != "DontDropIt/DeliveryZoneRing")
                {
#if UNITY_EDITOR
                    Material assetMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/DeliveryZone_Mat.mat");
                    if (assetMat != null)
                    {
                        _ringRenderer.sharedMaterial = assetMat;
                        ringMat = assetMat;
                    }
#endif
                    if (ringMat == null || ringMat.shader == null || ringMat.shader.name != "DontDropIt/DeliveryZoneRing")
                    {
                        Shader ringShader = Shader.Find("DontDropIt/DeliveryZoneRing");
                        if (ringShader != null)
                        {
                            Material newMat = new Material(ringShader);
                            newMat.name = "DeliveryZone_Ring_RuntimeMat";
                            _ringRenderer.sharedMaterial = newMat;
                            _generatedRingMaterial = newMat;
                        }
                    }
                }
            }

            // 2. Disable smoke particle systems (user requested removal of smoke effects)
            if (_perimeterParticles != null)
            {
                _perimeterParticles.gameObject.SetActive(false);
            }
            else
            {
                Transform existingPS = transform.Find("Perimeter_Particles");
                if (existingPS != null)
                {
                    existingPS.gameObject.SetActive(false);
                }
            }

            // 3. Audio Source
            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
                if (_audioSource == null)
                {
                    _audioSource = gameObject.AddComponent<AudioSource>();
                    _audioSource.playOnAwake = false;
                    _audioSource.spatialBlend = 0.8f;
                    _audioSource.minDistance = 2f;
                    _audioSource.maxDistance = 20f;
                }
            }

            UpdateColliderAndVisualScale();
        }

        private void ApplyColorImmediate(Color color)
        {
            _currentColor = color;
            ApplyCurrentColor();
        }

        private void ApplyCurrentColor()
        {
            if (_ringRenderer != null)
            {
                // Update color without accessing Renderer.material on a prefab asset.
                if (_propBlock == null)
                    _propBlock = new MaterialPropertyBlock();

                _ringRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetColor(ColorPropId, _currentColor);
                _propBlock.SetColor(BaseColorPropId, _currentColor);
                _ringRenderer.SetPropertyBlock(_propBlock);
            }

            if (_perimeterParticles != null)
            {
                var main = _perimeterParticles.main;
                main.startColor = _currentColor;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsTargetCollider(other)) return;

            _activeCollidersInZone.Add(other);
            RefreshZoneTargetStatus(other.gameObject);
        }

        private void OnTriggerExit(Collider other)
        {
            if (_activeCollidersInZone.Remove(other))
            {
                RefreshZoneTargetStatus(other != null ? other.gameObject : gameObject);
            }
        }

        private void RefreshZoneTargetStatus(GameObject contextObj)
        {
            // Prune any destroyed colliders
            _activeCollidersInZone.RemoveWhere(DestroyedCollider);

            bool previousInside = _isTargetInside;

            bool hasColliders = _activeCollidersInZone.Count > 0;
            bool isInsideByDist = false;

            if (_targetDeliveryObject != null)
            {
                isInsideByDist = ContainsPosition(_targetDeliveryObject.transform.position);
            }

            // An assigned target uses a single geometric rule on both host and clients.
            // This also clears stale overlaps after teleport, disable, or respawn.
            _isTargetInside = _targetDeliveryObject != null ? isInsideByDist : hasColliders;

            if (_isTargetInside && !previousInside)
            {
                // Target object entered zone!
                string objName = contextObj != null ? contextObj.name : (_targetDeliveryObject != null ? _targetDeliveryObject.name : "Target");
                Debug.Log($"<color=lime><b>[DeliveryZone] Target entered delivery zone:</b> {objName}</color>");

                OnTargetEntered?.Invoke(contextObj);
                OnZoneStateChanged?.Invoke(true);

                if (_deliverySuccessSfx != null && _audioSource != null)
                {
                    _audioSource.PlayOneShot(_deliverySuccessSfx);
                }

                // DeliveryPoint owns networked success, score, and cargo locking.
                if (_deliveryPoint == null && _lockDeliveryOnceEntered && !_isDelivered)
                {
                    _isDelivered = true;
                    OnDeliverySuccess?.Invoke(contextObj);

                    if (_freezeObjectOnDelivery && contextObj != null)
                    {
                        _pendingFreeze = contextObj.GetComponentInParent<Rigidbody>();
                    }
                }
            }
            else if (!_isTargetInside && previousInside)
            {
                // Target object left zone!
                string objName = contextObj != null ? contextObj.name : "Target";
                Debug.Log($"<color=orange>[DeliveryZone] Target exited delivery zone: {objName}</color>");
                OnTargetExited?.Invoke(contextObj);
                OnZoneStateChanged?.Invoke(false);
            }
        }

        public bool ContainsPosition(Vector3 worldPosition)
        {
            Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
            return localPosition.x * localPosition.x + localPosition.z * localPosition.z <= _radius * _radius &&
                localPosition.y >= -0.1f && localPosition.y <= _triggerHeight;
        }

        /// <summary>
        /// Validates whether the specified collider belongs to the target delivery object.
        /// </summary>
        public bool IsTargetCollider(Collider col)
        {
            if (col == null) return false;

            switch (_detectionMode)
            {
                case DeliveryDetectionMode.SpecificObjectOnly:
                    if (_targetDeliveryObject == null) return false;
                    return col.gameObject == _targetDeliveryObject 
                        || col.transform.IsChildOf(_targetDeliveryObject.transform)
                        || _targetDeliveryObject.transform.IsChildOf(col.transform);

                case DeliveryDetectionMode.SpecificObjectOrCarryable:
                    if (_targetDeliveryObject != null)
                    {
                        return col.gameObject == _targetDeliveryObject 
                            || col.transform.IsChildOf(_targetDeliveryObject.transform)
                            || _targetDeliveryObject.transform.IsChildOf(col.transform);
                    }
                    // Auto-fallback: any carryable item in scene
                    return col.GetComponentInParent<CarryableObject>() != null || col.GetComponentInParent<FragileCargo>() != null;

                case DeliveryDetectionMode.AnyCarryable:
                    return col.GetComponentInParent<CarryableObject>() != null || col.GetComponentInParent<FragileCargo>() != null;

                case DeliveryDetectionMode.ByTag:
                    return col.CompareTag(_targetTag) || (col.transform.parent != null && col.transform.parent.CompareTag(_targetTag));

                default:
                    return false;
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = (_isDelivered || _isTargetInside) ? Color.green : Color.red;
            Vector3 center = transform.position;

            // Draw circular ground boundary
            int segments = 36;
            float step = 360f / segments;
            Vector3 prevPoint = center + new Vector3(Mathf.Cos(0) * _radius, 0.05f, Mathf.Sin(0) * _radius);

            for (int i = 1; i <= segments; i++)
            {
                float rad = i * step * Mathf.Deg2Rad;
                Vector3 nextPoint = center + new Vector3(Mathf.Cos(rad) * _radius, 0.05f, Mathf.Sin(rad) * _radius);
                Gizmos.DrawLine(prevPoint, nextPoint);
                prevPoint = nextPoint;
            }

            // Draw center point and upper circle
            Gizmos.DrawWireSphere(center + Vector3.up * 0.1f, 0.3f);
        }
    }
}
