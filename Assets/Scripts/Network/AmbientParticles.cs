using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Soft floating dust and drifting leaves in a box around this object (or around the camera with
    /// <see cref="_followCamera"/>). Builds its own particle systems on Awake, so it can be dropped into any level as a
    /// prefab (Assets/Prefabs/FX/AmbientParticles.prefab) or added at runtime (VisualPolish adds one per level).
    /// </summary>
    public sealed class AmbientParticles : MonoBehaviour
    {
        [SerializeField] private bool _followCamera = true;
        [SerializeField] private Vector3 _area = new Vector3(40f, 12f, 40f);
        [SerializeField, Range(0, 400)] private int _dustCount = 160;
        [SerializeField, Range(0, 100)] private int _leafCount = 24;
        [SerializeField] private Color _dustColor = new Color(1f, .96f, .85f, .35f);
        [SerializeField] private Color _leafColor = new Color(.55f, .75f, .3f, .9f);

        public bool FollowCamera { get => _followCamera; set => _followCamera = value; }

        private void Awake()
        {
            var material = Resources.Load<Material>("FeedbackDust");
            if (material == null) return;
            Build("Dust", material, _dustCount, _dustColor, new Vector2(.03f, .07f), new Vector3(.15f, .05f, .1f), 14f, 0f);
            Build("Leaves", material, _leafCount, _leafColor, new Vector2(.08f, .14f), new Vector3(.8f, -.35f, .4f), 9f, 360f);
        }

        private void Build(string name, Material material, int count, Color color, Vector2 size, Vector3 drift, float life, float spin)
        {
            if (count <= 0) return;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = life;
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = color;
            main.maxParticles = count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, spin * Mathf.Deg2Rad);
            main.prewarm = true;
            var emission = ps.emission;
            emission.rateOverTime = count / life;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = _area;
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(drift.x * .5f, drift.x);
            velocity.y = new ParticleSystem.MinMaxCurve(drift.y, -drift.y * .3f);
            velocity.z = new ParticleSystem.MinMaxCurve(-drift.z, drift.z);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = .3f;
            noise.frequency = .25f;
            if (spin > 0f)
            {
                var rotation = ps.rotationOverLifetime;
                rotation.enabled = true;
                rotation.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
            }
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .2f), new GradientAlphaKey(1f, .8f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        private void LateUpdate()
        {
            if (!_followCamera) return;
            var view = Camera.main;
            if (view != null) transform.position = view.transform.position;
        }
    }
}
