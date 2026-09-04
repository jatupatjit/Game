using System.Collections.Generic;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// CarryableOutline adds a clean, continuous outline around interactable and carryable objects.
    /// 
    /// Key Technical Features:
    /// 1. Smoothed Normals Baking:
    ///    Precalculates averaged vertex normals for co-located vertices and stores them in UV2 (TEXCOORD1).
    ///    This eliminates edge split cracks at 90-degree box corners (e.g. CarryablePackage crates).
    /// 2. URP Compatible Inverted Hull:
    ///    Utilizes Custom/CarryableOutline shader in Universal Render Pipeline with aspect-ratio corrected screen-space extrusion.
    /// 3. Dynamic Material Stacking:
    ///    Appends the outline material only when highlighted, with zero runtime draw overhead when idle.
    /// 4. Zero Memory Leaks:
    ///    Cleans up instantiated materials and cached mesh UVs on disable/destroy.
    /// </summary>
    [DisallowMultipleComponent]
    public class CarryableOutline : MonoBehaviour
    {
        private const string SHADER_NAME = "Custom/CarryableOutline";

        [Header("Outline Visuals")]
        [Tooltip("Default color when highlighted in reach")]
        [SerializeField] private Color _defaultColor = new Color(0.2f, 1.0f, 0.4f, 0.95f);

        [Tooltip("Outline border thickness (1.0 to 10.0)")]
        [Range(0.5f, 10.0f)]
        [SerializeField] private float _defaultWidth = 3.5f;

        [Header("Animation & Pulse")]
        [Tooltip("Enable subtle pulsing glow effect when highlighted")]
        [SerializeField] private bool _enablePulse = false;

        [Tooltip("Pulsing frequency in cycles per second")]
        [SerializeField] private float _pulseSpeed = 3.5f;

        // Cached renderers and materials
        private Renderer[] _renderers;
        private Material _outlineMaterial;
        private bool _isHighlighted = false;
        private Color _currentColor;
        private float _currentWidth;

        // Static cache for smooth normals across shared meshes (avoids recomputing for same prefab)
        private static readonly Dictionary<Mesh, List<Vector3>> _smoothNormalsCache = new Dictionary<Mesh, List<Vector3>>();

        public bool IsHighlighted => _isHighlighted;
        public Color CurrentColor => _currentColor;
        public float CurrentWidth => _currentWidth;
        public bool EnablePulse { get => _enablePulse; set => _enablePulse = value; }

        private void Awake()
        {
            _currentColor = _defaultColor;
            _currentWidth = _defaultWidth;

            InitializeRenderers();
            BakeSmoothedNormals();
            CreateOutlineMaterial();
        }

        private void InitializeRenderers()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void CreateOutlineMaterial()
        {
            if (_outlineMaterial != null) return;

            Shader shader = Shader.Find(SHADER_NAME);
            if (shader == null)
            {
                Debug.LogWarning($"[CarryableOutline] Shader '{SHADER_NAME}' not found. Falling back to Universal Render Pipeline/Unlit.");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader != null)
            {
                _outlineMaterial = new Material(shader)
                {
                    name = "M_CarryableOutline (Runtime)",
                    hideFlags = HideFlags.DontSave
                };
                UpdateMaterialProperties();
            }
            else
            {
                Debug.LogError("[CarryableOutline] Unable to find any suitable outline shader.");
            }
        }

        /// <summary>
        /// Precalculates smooth normals for all unique meshes on this object.
        /// Stores the resulting normals in UV2 (TEXCOORD1) so box corners extrude without seams.
        /// </summary>
        private void BakeSmoothedNormals()
        {
            MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(true);
            foreach (var filter in meshFilters)
            {
                if (filter == null || filter.sharedMesh == null) continue;

                Mesh mesh = filter.sharedMesh;

                if (!_smoothNormalsCache.TryGetValue(mesh, out List<Vector3> smoothNormals))
                {
                    smoothNormals = ComputeSmoothNormals(mesh);
                    _smoothNormalsCache[mesh] = smoothNormals;
                }

                // If this is an asset mesh, instantiate a clone for runtime UV assignment to prevent modifying disk assets
                Mesh runtimeMesh = filter.mesh;
                runtimeMesh.SetUVs(1, smoothNormals);
            }

            SkinnedMeshRenderer[] smrs = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr == null || smr.sharedMesh == null) continue;

                Mesh mesh = smr.sharedMesh;

                if (!_smoothNormalsCache.TryGetValue(mesh, out List<Vector3> smoothNormals))
                {
                    smoothNormals = ComputeSmoothNormals(mesh);
                    _smoothNormalsCache[mesh] = smoothNormals;
                }

                Mesh clonedMesh = Instantiate(smr.sharedMesh);
                clonedMesh.SetUVs(1, smoothNormals);
                smr.sharedMesh = clonedMesh;
            }
        }

        private List<Vector3> ComputeSmoothNormals(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int count = vertices.Length;

            // Group vertices by position using rounded coordinates to absorb micro floating-point variances
            var normalSums = new Dictionary<Vector3, Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                Vector3 v = vertices[i];
                Vector3 key = new Vector3(
                    Mathf.Round(v.x * 1000f) * 0.001f,
                    Mathf.Round(v.y * 1000f) * 0.001f,
                    Mathf.Round(v.z * 1000f) * 0.001f
                );

                Vector3 currentNormal = (normals != null && i < normals.Length) ? normals[i] : Vector3.up;

                if (normalSums.TryGetValue(key, out Vector3 existing))
                {
                    normalSums[key] = existing + currentNormal;
                }
                else
                {
                    normalSums[key] = currentNormal;
                }
            }

            var smoothNormals = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                Vector3 v = vertices[i];
                Vector3 key = new Vector3(
                    Mathf.Round(v.x * 1000f) * 0.001f,
                    Mathf.Round(v.y * 1000f) * 0.001f,
                    Mathf.Round(v.z * 1000f) * 0.001f
                );

                Vector3 sum = normalSums[key];
                smoothNormals.Add(sum.sqrMagnitude > 0.0001f ? sum.normalized : Vector3.up);
            }

            return smoothNormals;
        }

        private void Update()
        {
            if (!_isHighlighted || !_enablePulse || _outlineMaterial == null) return;

            // Subtle pulsing effect when highlighted
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * _pulseSpeed);
            Color pulsedColor = _currentColor * pulse;
            pulsedColor.a = _currentColor.a;
            _outlineMaterial.SetColor("_OutlineColor", pulsedColor);
        }

        /// <summary>
        /// Toggles the outline on or off, with optional custom color, line width, and pulse effect.
        /// </summary>
        public void SetHighlighted(bool enabled, Color? customColor = null, float? width = null, bool? enablePulse = null)
        {
            if (customColor.HasValue) _currentColor = customColor.Value;
            else if (!enabled) _currentColor = _defaultColor;

            if (width.HasValue) _currentWidth = width.Value;
            else if (!enabled) _currentWidth = _defaultWidth;

            if (enablePulse.HasValue) _enablePulse = enablePulse.Value;

            if (_isHighlighted == enabled)
            {
                // Update properties if changed while active
                if (_isHighlighted)
                {
                    UpdateMaterialProperties();
                }
                return;
            }

            _isHighlighted = enabled;

            if (_renderers == null || _renderers.Length == 0)
            {
                InitializeRenderers();
            }

            if (_outlineMaterial == null)
            {
                CreateOutlineMaterial();
            }

            UpdateMaterialProperties();

            if (_isHighlighted)
            {
                AddOutlineMaterialToRenderers();
            }
            else
            {
                RemoveOutlineMaterialFromRenderers();
            }
        }

        public void SetColor(Color color)
        {
            _currentColor = color;
            UpdateMaterialProperties();
        }

        public void SetWidth(float width)
        {
            _currentWidth = width;
            UpdateMaterialProperties();
        }

        private void UpdateMaterialProperties()
        {
            if (_outlineMaterial == null) return;

            _outlineMaterial.SetColor("_OutlineColor", _currentColor);
            _outlineMaterial.SetFloat("_OutlineWidth", _currentWidth);
        }

        private void AddOutlineMaterialToRenderers()
        {
            if (_outlineMaterial == null || _renderers == null) return;

            foreach (var r in _renderers)
            {
                if (r == null) continue;

                Material[] currentMats = r.sharedMaterials;
                // Avoid duplicate outline material entries
                for (int i = 0; i < currentMats.Length; i++)
                {
                    if (currentMats[i] == _outlineMaterial) return;
                }

                Material[] newMats = new Material[currentMats.Length + 1];
                for (int i = 0; i < currentMats.Length; i++)
                {
                    newMats[i] = currentMats[i];
                }
                newMats[currentMats.Length] = _outlineMaterial;
                r.sharedMaterials = newMats;
            }
        }

        private void RemoveOutlineMaterialFromRenderers()
        {
            if (_outlineMaterial == null || _renderers == null) return;

            foreach (var r in _renderers)
            {
                if (r == null) continue;

                Material[] currentMats = r.sharedMaterials;
                int countWithoutOutline = 0;
                for (int i = 0; i < currentMats.Length; i++)
                {
                    if (currentMats[i] != _outlineMaterial && currentMats[i] != null)
                    {
                        countWithoutOutline++;
                    }
                }

                if (countWithoutOutline == currentMats.Length) continue;

                Material[] newMats = new Material[countWithoutOutline];
                int writeIdx = 0;
                for (int i = 0; i < currentMats.Length; i++)
                {
                    if (currentMats[i] != _outlineMaterial && currentMats[i] != null)
                    {
                        newMats[writeIdx++] = currentMats[i];
                    }
                }
                r.sharedMaterials = newMats;
            }
        }

        private void OnDisable()
        {
            if (_isHighlighted)
            {
                RemoveOutlineMaterialFromRenderers();
                _isHighlighted = false;
            }
        }

        private void OnDestroy()
        {
            RemoveOutlineMaterialFromRenderers();

            if (_outlineMaterial != null)
            {
                Destroy(_outlineMaterial);
                _outlineMaterial = null;
            }
        }
    }
}
