using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopGame.Network
{
    /// <summary>
    /// Short camera shake on knockdowns and hard crate hits. The offset is added just before the camera renders and
    /// removed right after, so the camera rig (PlayerCameraController) and gameplay never see it.
    /// </summary>
    public static class CameraShake
    {
        private const float Decay = 3.5f, MaxOffset = .25f, MaxRoll = 2.5f, Range = 18f;
        private static float _trauma;
        private static readonly Dictionary<Camera, (Vector3 offset, Quaternion roll)> Applied = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _trauma = 0f;
            Applied.Clear();
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.endCameraRendering -= End;
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering += End;
        }

        /// <summary>Adds shake for something at <paramref name="at"/> (0..1, fades with distance from the camera).</summary>
        public static void Add(Vector3 at, float strength)
        {
            var view = Camera.main;
            if (view == null || PhotoMode.IsOn) return;
            float falloff = 1f - Mathf.Clamp01(Vector3.Distance(view.transform.position, at) / Range);
            _trauma = Mathf.Clamp01(_trauma + Mathf.Clamp01(strength) * falloff);
        }

        private static void Begin(ScriptableRenderContext context, Camera camera)
        {
            if (_trauma <= 0f || camera != Camera.main) return;
            _trauma = Mathf.Max(0f, _trauma - Decay * Time.unscaledDeltaTime);
            float shake = _trauma * _trauma, t = Time.unscaledTime * 25f;
            var offset = new Vector3(Mathf.PerlinNoise(t, 0f) - .5f, Mathf.PerlinNoise(0f, t) - .5f, 0f) * (2f * MaxOffset * shake);
            var roll = Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(t, t) - .5f) * 2f * MaxRoll * shake);
            var transform = camera.transform;
            offset = transform.rotation * offset;
            transform.position += offset;
            transform.rotation *= roll;
            Applied[camera] = (offset, roll);
        }

        private static void End(ScriptableRenderContext context, Camera camera)
        {
            if (!Applied.TryGetValue(camera, out var applied)) return;
            Applied.Remove(camera);
            if (camera == null) return;
            camera.transform.rotation *= Quaternion.Inverse(applied.roll);
            camera.transform.position -= applied.offset;
        }
    }
}
