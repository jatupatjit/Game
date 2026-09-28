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
        public bool IsDelivered => _isDelivered;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            _currentColor = _waitingRedColor;

            EnsureTriggerCollider();
            EnsureVisuals();
            ApplyColorImmediate(_waitingRedColor);
        }

        private void OnEnable()
        {
            EnsureTriggerCollider();
            EnsureVisuals();
            UpdateColliderAndVisualScale();
            ApplyColorImmediate(_isTargetInside || _isDelivered ? _deliveredGreenColor : _waitingRedColor);
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
            if (Application.isPlaying)
            {
                // Real-time position check for the specific delivery object (ensures detection even if carried or physics-disabled)
                if (_targetDeliveryObject != null)
                {
                    Vector3 zonePos = transform.position;
                    Vector3 objPos = _targetDeliveryObject.transform.position;
                    float horizontalDist = Vector2.Distance(new Vector2(zonePos.x, zonePos.z), new Vector2(objPos.x, objPos.z));
                    float heightDiff = Mathf.Abs(objPos.y - zonePos.y);

                    bool isInsideByDistance = (horizontalDist <= _radius) && (heightDiff <= _triggerHeight + 1.5f);
                    if (isInsideByDistance != _isTargetInside)
                    {
                        RefreshZoneTargetStatus(_targetDeliveryObject);
                    }
                }
            }

            bool shouldBeGreen = _isDelivered || _isTargetInside;
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

        private void FixedUpdate()
        {
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
                // Cylinder: radius in X/Z, very flat Y thickness to look like a disc
                _ringRenderer.transform.localScale = new Vector3(_radius, 0.01f, _radius);
                _ringRenderer.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            }

            if (_perimeterParticles != null)
            {
                var shape = _perimeterParticles.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = _radius * 0.98f;
            }
        }

        public void EnsureVisuals()
        {
            // 1. Create or bind ground ring renderer
            if (_ringRenderer == null)
            {
                Transform existingRing = transform.Find("Zone_Circle_Visual");
                if (existingRing != null)
                {
                    _ringRenderer = existingRing.GetComponent<Renderer>();
                }
                else
                {
                    GameObject ringObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    ringObj.name = "Zone_Circle_Visual";
                    ringObj.transform.SetParent(transform, false);
                    ringObj.transform.localRotation = Quaternion.identity;
                    ringObj.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                    // Cylinder radius = 1, height = 2 in Unity. Scale X/Z = radius, Y = flat disc thickness.
                    ringObj.transform.localScale = new Vector3(_radius, 0.01f, _radius);

                    // Remove mesh collider on visual cylinder
                    Collider quadCol = ringObj.GetComponent<Collider>();
                    if (quadCol != null)
                    {
                        if (Application.isPlaying) Destroy(quadCol);
                        else DestroyImmediate(quadCol);
                    }

                    _ringRenderer = ringObj.GetComponent<Renderer>();

                    // Find or create material with custom shader
                    Shader ringShader = Shader.Find("DontDropIt/DeliveryZoneRing");
                    if (ringShader != null)
                    {
                        Material mat = new Material(ringShader);
                        mat.name = "DeliveryZone_Ring_RuntimeMat";
                        _ringRenderer.sharedMaterial = mat;
                        _generatedRingMaterial = mat;
                    }
                }
            }

            // 2. Create or bind perimeter particles
            if (_perimeterParticles == null)
            {
                Transform existingPS = transform.Find("Perimeter_Particles");
                if (existingPS != null)
                {
                    _perimeterParticles = existingPS.GetComponent<ParticleSystem>();
                }
                else
                {
                    GameObject psObj = new GameObject("Perimeter_Particles");
                    psObj.transform.SetParent(transform, false);
                    psObj.transform.localPosition = new Vector3(0f, 0.05f, 0f);

                    _perimeterParticles = psObj.AddComponent<ParticleSystem>();
                    var main = _perimeterParticles.main;
                    main.startLifetime = 1.2f;
                    main.startSpeed = 0.5f;
                    main.startSize = 0.12f;
                    main.startColor = _waitingRedColor;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    main.playOnAwake = true;

                    var emission = _perimeterParticles.emission;
                    emission.rateOverTime = 25f;

                    var shape = _perimeterParticles.shape;
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = _radius * 0.98f;
                    shape.rotation = new Vector3(-90f, 0f, 0f);

                    var colorOverLife = _perimeterParticles.colorOverLifetime;
                    colorOverLife.enabled = true;
                    Gradient grad = new Gradient();
                    grad.SetKeys(
                        new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
                        new GradientAlphaKey[] { new GradientAlphaKey(0.0f, 0.0f), new GradientAlphaKey(0.8f, 0.3f), new GradientAlphaKey(0.0f, 1.0f) }
                    );
                    colorOverLife.color = grad;

                    ParticleSystemRenderer psRenderer = psObj.GetComponent<ParticleSystemRenderer>();
                    if (psRenderer != null)
                    {
                        psRenderer.sharedMaterial = GetOrCreateParticleMaterial();
                    }
                }
            }

            if (_perimeterParticles != null)
            {
                ParticleSystemRenderer psRenderer = _perimeterParticles.GetComponent<ParticleSystemRenderer>();
                if (psRenderer != null)
                {
                    if (psRenderer.sharedMaterial == null || 
                        psRenderer.sharedMaterial.shader == null || 
                        psRenderer.sharedMaterial.shader.name == "DontDropIt/DeliveryZoneRing" ||
                        psRenderer.sharedMaterial.shader.name == "Hidden/InternalErrorShader")
                    {
                        psRenderer.sharedMaterial = GetOrCreateParticleMaterial();
                    }
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

        private Material GetOrCreateParticleMaterial()
        {
#if UNITY_EDITOR
            Material assetMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ZoneParticle_Mat.mat");
            if (assetMat != null) return assetMat;
#endif
            Shader pShader = Shader.Find("DontDropIt/ZoneParticle");
            if (pShader == null) pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (pShader == null) pShader = Shader.Find("Particles/Standard Unlit");

            if (pShader != null)
            {
                Material runtimeMat = new Material(pShader);
                runtimeMat.name = "ZoneParticle_RuntimeMat";
                return runtimeMat;
            }
            return null;
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
            _activeCollidersInZone.RemoveWhere(c => c == null);

            bool previousInside = _isTargetInside;

            bool hasColliders = _activeCollidersInZone.Count > 0;
            bool isInsideByDist = false;

            if (_targetDeliveryObject != null)
            {
                Vector3 zonePos = transform.position;
                Vector3 objPos = _targetDeliveryObject.transform.position;
                float horizontalDist = Vector2.Distance(new Vector2(zonePos.x, zonePos.z), new Vector2(objPos.x, objPos.z));
                float heightDiff = Mathf.Abs(objPos.y - zonePos.y);
                isInsideByDist = (horizontalDist <= _radius) && (heightDiff <= _triggerHeight + 1.5f);
            }

            _isTargetInside = hasColliders || isInsideByDist;

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

                if (_successBurstParticles != null)
                {
                    _successBurstParticles.Play();
                }

                if (_lockDeliveryOnceEntered && !_isDelivered)
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
                if (!_lockDeliveryOnceEntered)
                {
                    string objName = contextObj != null ? contextObj.name : "Target";
                    Debug.Log($"<color=orange>[DeliveryZone] Target exited delivery zone: {objName}</color>");
                    OnTargetExited?.Invoke(contextObj);
                    OnZoneStateChanged?.Invoke(false);
                }
            }
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
                        || _targetDeliveryObject.transform.IsChildOf(col.transform)
                        || col.transform.root.gameObject == _targetDeliveryObject.transform.root.gameObject;

                case DeliveryDetectionMode.SpecificObjectOrCarryable:
                    if (_targetDeliveryObject != null)
                    {
                        return col.gameObject == _targetDeliveryObject 
                            || col.transform.IsChildOf(_targetDeliveryObject.transform)
                            || _targetDeliveryObject.transform.IsChildOf(col.transform)
                            || col.transform.root.gameObject == _targetDeliveryObject.transform.root.gameObject;
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
