using System.Collections.Generic;
using UnityEngine;

namespace CoopGame.Weather
{
    /// <summary>
    /// A hand-placed area where one weather event takes effect (George 2026-11-17: every event happens in placed areas,
    /// only rain covers the whole map). Wind uses <see cref="WeatherWindZone"/>; this box is for the other kinds:
    /// GoldenCrate = crates inside take no damage and players inside spend no stamina while the event runs.
    /// Same box on every machine, and the event clock is the server's, so every screen agrees.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WeatherZone : MonoBehaviour
    {
        public static readonly List<WeatherZone> All = new();

        [SerializeField] private WeatherKind _kind = WeatherKind.GoldenCrate;

        private BoxCollider _box;
        private ParticleSystem _sparkles;

        public WeatherKind Kind => _kind;

        public static bool AnyOf(WeatherKind kind)
        {
            foreach (var zone in All) if (zone._kind == kind) return true;
            return false;
        }

        /// <summary>True when <paramref name="point"/> is inside an enabled zone of <paramref name="kind"/>.</summary>
        public static bool Inside(WeatherKind kind, Vector3 point)
        {
            foreach (var zone in All) if (zone._kind == kind && zone.Contains(point)) return true;
            return false;
        }

        public bool Contains(Vector3 point)
        {
            if (_box == null) _box = GetComponent<BoxCollider>();
            Vector3 local = transform.InverseTransformPoint(point) - _box.center;
            Vector3 half = _box.size * .5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        private void Awake()
        {
            _box = GetComponent<BoxCollider>();
            _box.isTrigger = true;
        }

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() => All.Remove(this);

        private void Update()
        {
            bool on = WeatherState.Active == _kind;
            bool coming = WeatherState.Coming == _kind;
            if (!on && !coming && _sparkles == null) return;
            if (_sparkles == null) _sparkles = BuildSparkles();
            var emission = _sparkles.emission;
            Vector3 size = Vector3.Scale(_box.size, transform.lossyScale);
            float area = Mathf.Clamp(size.x * size.z / 40f, .5f, 6f);
            emission.rateOverTime = (on ? 14f * WeatherState.Strength : coming ? 4f : 0f) * area;
        }

        private ParticleSystem BuildSparkles()
        {
            var go = new GameObject("ZoneSparkles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = _box.center - new Vector3(0f, _box.size.y * .5f, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // emit upward
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 2.2f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(.4f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(.06f, .14f);
            main.startColor = _kind == WeatherKind.GoldenCrate
                ? new ParticleSystem.MinMaxGradient(new Color(1f, .82f, .25f), new Color(1f, .96f, .65f))
                : new ParticleSystem.MinMaxGradient(Color.white);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(_box.size.x, _box.size.z, .1f); // rotated: local y of the emitter = zone z
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = WeatherDirector.ParticleMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = _kind == WeatherKind.GoldenCrate ? new Color(1f, .8f, .2f, .2f) : new Color(1f, 1f, 1f, .15f);
            Gizmos.DrawCube(box.center, box.size);
        }
#endif
    }
}
