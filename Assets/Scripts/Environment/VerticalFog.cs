using UnityEngine;

/// <summary>
/// Height fog that gets thicker the lower you go, so the void under the sky islands fades into fog.
/// Purely visual and local to each player's camera: nothing here is networked.
/// Put one in a scene and tune it in the Inspector; changes show live in Scene and Game view.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class VerticalFog : MonoBehaviour
{
    [SerializeField] private Shader _shader;

    [Header("Look")]
    [SerializeField] private Color _fogColor = new Color(0.72f, 0.82f, 0.95f, 1f);
    [SerializeField] private Color _sunColor = new Color(1f, 0.93f, 0.8f, 1f);
    [SerializeField, Range(0f, 1f)] private float _sunGlow = 0.35f;
    [SerializeField, Range(0f, 1f)] private float _maxOpacity = 1f;

    [Header("Height")]
    [Tooltip("World Y where the fog has its base density. Below it the fog thickens fast, above it thins out.")]
    [SerializeField] private float _fogHeight = 0f;
    [Tooltip("Density at Fog Height. Higher = thicker everywhere.")]
    [SerializeField, Min(0f)] private float _density = 0.02f;
    [Tooltip("How quickly the fog thins out going up. Higher = sharper fog top.")]
    [SerializeField, Min(0.0001f)] private float _falloff = 0.08f;

    [Header("Distance")]
    [Tooltip("Metres in front of the camera that stay fog-free, so the player and crate stay sharp.")]
    [SerializeField, Min(0f)] private float _clearDistance = 6f;
    [Tooltip("How far the sky counts as, for fog over empty sky and the void.")]
    [SerializeField, Min(1f)] private float _skyDistance = 1500f;

    private static readonly int FogColorId = Shader.PropertyToID("_FogColor");
    private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
    private static readonly int SunGlowId = Shader.PropertyToID("_SunGlow");
    private static readonly int FogHeightId = Shader.PropertyToID("_FogHeight");
    private static readonly int DensityId = Shader.PropertyToID("_Density");
    private static readonly int FalloffId = Shader.PropertyToID("_Falloff");
    private static readonly int StartDistanceId = Shader.PropertyToID("_StartDistance");
    private static readonly int MaxOpacityId = Shader.PropertyToID("_MaxOpacity");
    private static readonly int SkyDistanceId = Shader.PropertyToID("_SkyDistance");

    private Mesh _mesh;
    private Material _material;

    private void OnEnable()
    {
        if (_shader == null) _shader = Shader.Find("DontDropIt/VerticalFog");
        if (_shader == null) { Debug.LogWarning("[VerticalFog] Shader DontDropIt/VerticalFog not found.", this); return; }

        _mesh = BuildScreenQuad();
        _material = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave, name = "VerticalFog (runtime)" };

        GetComponent<MeshFilter>().sharedMesh = _mesh;
        var mr = GetComponent<MeshRenderer>();
        mr.sharedMaterial = _material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        mr.allowOcclusionWhenDynamic = false;
        Apply();
    }

    private void OnDisable()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh == _mesh) mf.sharedMesh = null;
        var mr = GetComponent<MeshRenderer>();
        if (mr != null && mr.sharedMaterial == _material) mr.sharedMaterial = null;
        DestroyAsset(_mesh);
        DestroyAsset(_material);
        _mesh = null;
        _material = null;
    }

    private void OnValidate() => Apply();

    private void LateUpdate() => Apply();

    private void Apply()
    {
        if (_material == null) return;
        _material.SetColor(FogColorId, _fogColor);
        _material.SetColor(SunColorId, _sunColor);
        _material.SetFloat(SunGlowId, _sunGlow);
        _material.SetFloat(FogHeightId, _fogHeight);
        _material.SetFloat(DensityId, _density);
        _material.SetFloat(FalloffId, _falloff);
        _material.SetFloat(StartDistanceId, _clearDistance);
        _material.SetFloat(MaxOpacityId, _maxOpacity);
        _material.SetFloat(SkyDistanceId, _skyDistance);
    }

    private static Mesh BuildScreenQuad()
    {
        // The shader ignores positions and uses the UVs as screen coordinates. Huge bounds keep it from being culled.
        var mesh = new Mesh { name = "VerticalFogQuad", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[] { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000000f);
        return mesh;
    }

    private static void DestroyAsset(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
    }
}
