using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CoopGame.Network
{
    /// <summary>
    /// Presentation polish added at runtime to every level, without touching the scenes:
    /// a subtle global post-processing look (colour, bloom, vignette), ambient dust/leaves around the camera, and a
    /// short slow-motion beat when a crate is delivered (<see cref="DeliverySlowMo"/>).
    /// </summary>
    public sealed class VisualPolish : MonoBehaviour
    {
        private const float SlowScale = .35f, SlowSeconds = .7f, RecoverSeconds = .5f;
        private static VisualPolish _instance;
        private Volume _volume;
        private float _slowStart = -100f;
        private bool _slowing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("VisualPolish") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<VisualPolish>();
        }

        private void Awake()
        {
            BuildVolume();
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void BuildVolume()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(.1f);
            color.contrast.Override(8f);
            color.saturation.Override(12f);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(.35f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(.6f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(.22f);
            vignette.smoothness.Override(.45f);
            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 5f; // above the scenes' default volume, below anything a level adds on purpose
            _volume.sharedProfile = profile;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            Time.timeScale = 1f;
            _slowing = false;
            foreach (var camera in Camera.allCameras)
                if (camera.TryGetComponent(out UniversalAdditionalCameraData data)) data.renderPostProcessing = true;
            if (FindAnyObjectByType<AmbientParticles>() == null)
                new GameObject("AmbientParticles (runtime)").AddComponent<AmbientParticles>();
        }

        /// <summary>Every screen, on delivery: time slows briefly and eases back.</summary>
        public static void DeliverySlowMo()
        {
            if (_instance == null) return;
            _instance._slowStart = Time.unscaledTime;
            _instance._slowing = true;
        }

        private void Update()
        {
            var main = Camera.main;
            if (main != null && main.TryGetComponent(out UniversalAdditionalCameraData data) && !data.renderPostProcessing)
                data.renderPostProcessing = true;
            if (!_slowing) return;
            float t = Time.unscaledTime - _slowStart;
            if (t >= SlowSeconds + RecoverSeconds || PauseMenu.IsPaused) { Time.timeScale = 1f; _slowing = false; return; }
            Time.timeScale = t < SlowSeconds ? SlowScale : Mathf.Lerp(SlowScale, 1f, (t - SlowSeconds) / RecoverSeconds);
        }
    }
}
