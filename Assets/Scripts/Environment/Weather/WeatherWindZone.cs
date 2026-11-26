using System.Collections.Generic;
using CoopGame.CarrySystem;
using CoopGame.Player;
using CoopGame.Traps;
using UnityEngine;

namespace CoopGame.Weather
{
    /// <summary>
    /// A spot where the wind blows during the "Wind" weather event (<see cref="WeatherDirector"/>). Between events it
    /// does nothing. Gusts come and go (same on every machine: driven by the server clock). Players are pushed on
    /// their own machine; crates are pushed on the server (same split as <see cref="TrapPushZone"/>). White streaks
    /// show where and which way it blows, a few even before the event so players can see the spot.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WeatherWindZone : MonoBehaviour
    {
        public static readonly List<WeatherWindZone> All = new();

        [Tooltip("Wind direction in this object's space")]
        [SerializeField] private Vector3 _direction = Vector3.forward;
        [Tooltip("Push on a ragdoll player at full gust (m/s²)")]
        [SerializeField, Min(0f)] private float _playerAcceleration = 18f;
        [Tooltip("Push on crates at full gust (m/s²)")]
        [SerializeField, Min(0f)] private float _cargoAcceleration = 9f;
        [Tooltip("Seconds per gust cycle (strong, then weaker)")]
        [SerializeField, Min(1f)] private float _gustPeriod = 3.5f;
        [Tooltip("Weakest part of a gust, as a share of full strength")]
        [SerializeField, Range(0f, 1f)] private float _gustLow = .45f;
        [Tooltip("Blow all the time in gusts, not only during the Wind weather event (e.g. the bridge, George 2026-11-27)")]
        [SerializeField] private bool _alwaysOn;

        private BoxCollider _box;
        private ParticleSystem _streaks;
        private static readonly Collider[] s_hits = new Collider[64];
        private static readonly HashSet<Rigidbody> s_bodies = new();

        public Vector3 WorldDirection => transform.TransformDirection(_direction).normalized;
        /// <summary>0..1 push right now (event strength x gust).</summary>
        public float Gust { get; private set; }

        private void Awake()
        {
            _box = GetComponent<BoxCollider>();
            _box.isTrigger = true;
        }

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() => All.Remove(this);

        private void Start() => BuildStreaks();

        private void FixedUpdate()
        {
            bool windEvent = WeatherState.Active == WeatherKind.Wind && WeatherState.Strength > 0f;
            if (!windEvent && !_alwaysOn) { Gust = 0f; return; }
            float phase = (float)(TrapClock.Now % _gustPeriod) / _gustPeriod;
            float wave = .5f + .5f * Mathf.Sin(phase * Mathf.PI * 2f);
            Gust = (windEvent ? Mathf.Max(WeatherState.Strength, _alwaysOn ? 1f : 0f) : 1f) * Mathf.Lerp(_gustLow, 1f, wave * wave);

            Vector3 scale = transform.lossyScale;
            Vector3 half = Vector3.Scale(_box.size * .5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            int count = Physics.OverlapBoxNonAlloc(transform.TransformPoint(_box.center), half, s_hits, transform.rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Vector3 dir = WorldDirection;
            var local = TrapClock.LocalPlayer;
            var ragdoll = local != null ? local.GetComponent<RagdollCharacter>() : null;
            bool fullRagdoll = ragdoll != null && ragdoll.isActiveAndEnabled && ragdoll.FullRagdoll;
            bool authority = TrapClock.IsAuthority;
            s_bodies.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider other = s_hits[i];
                if (other == null) continue;
                Rigidbody body = other.attachedRigidbody;
                if (fullRagdoll && ragdoll.OwnsCollider(other))
                {
                    if (body != null && !body.isKinematic && s_bodies.Add(body)) body.AddForce(dir * (_playerAcceleration * Gust), ForceMode.Acceleration);
                    continue;
                }
                if (authority && body != null && !body.isKinematic && body.GetComponent<FragileCargo>() != null && s_bodies.Add(body))
                    body.AddForce(dir * (_cargoAcceleration * Gust), ForceMode.Acceleration);
            }
            System.Array.Clear(s_hits, 0, count);
        }

        private void Update()
        {
            if (_streaks == null) return;
            var emission = _streaks.emission;
            bool blowing = WeatherState.Active == WeatherKind.Wind || _alwaysOn;
            float rate = blowing ? 6f + 24f * Gust
                : WeatherState.Coming == WeatherKind.Wind ? 4f : 1f;
            Vector3 size = Vector3.Scale(_box.size, transform.lossyScale);
            emission.rateOverTime = rate * Mathf.Clamp(size.x * size.y * size.z / 300f, .5f, 4f);
            var main = _streaks.main;
            main.startSpeed = blowing ? 10f + 8f * Gust : 4f;
        }

        private void BuildStreaks()
        {
            var go = new GameObject("WindStreaks");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = _box.center;
            go.transform.localRotation = Quaternion.LookRotation(_direction.normalized);
            _streaks = go.AddComponent<ParticleSystem>();
            _streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _streaks.main;
            main.loop = true;
            main.startLifetime = 1.1f;
            main.startSpeed = 4f;
            main.startSize = new ParticleSystem.MinMaxCurve(.015f, .03f);
            main.startColor = new Color(1f, 1f, 1f, .4f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var shape = _streaks.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            // Particles start on the upwind half of the box and fly along +Z (the wind).
            Vector3 local = Quaternion.Inverse(go.transform.localRotation) * Vector3.Scale(_box.size, Vector3.one);
            shape.scale = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z) * .5f);
            shape.position = new Vector3(0f, 0f, -Mathf.Abs(local.z) * .25f);
            var fade = _streaks.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.45f, .2f), new GradientAlphaKey(.45f, .7f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = .06f;
            renderer.lengthScale = 1f;
            renderer.sharedMaterial = WeatherDirector.ParticleMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _streaks.Play();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(.85f, .95f, 1f, .2f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(.85f, .95f, 1f, .9f);
            Gizmos.DrawRay(box.center, _direction.normalized * Mathf.Max(1f, box.size.z * .5f));
        }
#endif
    }
}
