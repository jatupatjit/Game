using UnityEngine;

namespace CoopGame.Weather
{
    /// <summary>
    /// A small cloth flag on a pole that shows the wind of the <see cref="WeatherWindZone"/> it stands in: it streams
    /// out and flutters while the zone blows and hangs down during a calm, so players can read the gust rhythm before
    /// stepping onto a windy bridge. Visual only (the zone's gust comes from the server clock, so every peer agrees).
    /// Outside any zone the flag just hangs.
    /// </summary>
    public sealed class WindFlag : MonoBehaviour
    {
        [Tooltip("Pivot at the pole top; the cloth hangs from it along its local +X")]
        [SerializeField] private Transform _cloth;
        [SerializeField, Min(0f)] private float _flutterSpeed = 11f;

        private WeatherWindZone _zone;
        private float _nextSearch, _shown;

        private void LateUpdate()
        {
            if (_cloth == null) return;
            if (Time.time >= _nextSearch)
            {
                _nextSearch = Time.time + 1f;
                _zone = FindZone();
            }
            float gust = _zone != null ? _zone.Gust : 0f;
            _shown = Mathf.MoveTowards(_shown, gust, Time.deltaTime * (gust > _shown ? 4f : 1.5f));

            // Stream away from the wind source when it blows, hang straight down in a calm.
            Vector3 wind = _zone != null ? _zone.WorldDirection : transform.forward;
            wind.y = 0f;
            if (wind.sqrMagnitude < 1e-4f) wind = transform.forward;
            float t = Time.time + transform.position.x * .37f;
            float flutter = Mathf.Sin(t * _flutterSpeed) * 14f * _shown + Mathf.Sin(t * _flutterSpeed * 2.3f) * 5f * _shown;
            float lift = Mathf.Sqrt(_shown); // a light gust already lifts it most of the way
            float droop = Mathf.Lerp(80f, 2f, lift) + Mathf.Sin(t * 1.7f) * 3f;
            Quaternion yaw = Quaternion.LookRotation(Vector3.Cross(wind.normalized, Vector3.up), Vector3.up); // local +X along the wind
            _cloth.rotation = yaw * Quaternion.Euler(0f, flutter, -droop);
        }

        private WeatherWindZone FindZone()
        {
            Vector3 p = transform.position;
            foreach (var zone in WeatherWindZone.All)
            {
                if (zone == null) continue;
                var box = zone.GetComponent<BoxCollider>();
                if (box == null) continue;
                Vector3 local = zone.transform.InverseTransformPoint(p) - box.center;
                Vector3 half = box.size * .5f + Vector3.one * 2f; // flags on the end posts may stand just outside
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z) return zone;
            }
            return null;
        }
    }
}
