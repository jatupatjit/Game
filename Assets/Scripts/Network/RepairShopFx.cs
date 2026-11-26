using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Looks of the repair shop (no text signs): a green "+" over the workshop that turns and bobs while the free repair
    /// is still there, a ring round the pad that fills while the crate rests on it, and the pad's "+" inlay pulsing.
    /// Reads <see cref="RepairStation"/> state only (same on every screen).
    /// </summary>
    public sealed class RepairShopFx : MonoBehaviour
    {
        [SerializeField] private RepairStation _station;
        [SerializeField] private Transform _sign;
        [SerializeField] private Transform _padPlus;
        [SerializeField, Min(.5f)] private float _ringRadius = 2.15f;
        [SerializeField] private Material _ringMaterial;

        private LineRenderer _ring;
        private Vector3 _signRest;
        private const int Segments = 48;

        private void Start()
        {
            if (_sign != null) _signRest = _sign.localPosition;
            if (_station == null) return;
            var go = new GameObject("ProgressRing");
            go.transform.SetParent(_station.PadTransform, false);
            go.transform.localPosition = Vector3.up * .2f;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _ring = go.AddComponent<LineRenderer>();
            _ring.useWorldSpace = false;
            _ring.alignment = LineAlignment.TransformZ;
            _ring.widthMultiplier = .18f;
            _ring.sharedMaterial = _ringMaterial;
            _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ring.startColor = _ring.endColor = new Color(.45f, 1f, .5f);
            _ring.positionCount = 0;
        }

        private void LateUpdate()
        {
            if (_station == null) return;
            bool used = _station.IsUsed;
            float t = Time.time;
            if (_sign != null)
            {
                _sign.gameObject.SetActive(!used);
                _sign.localPosition = _signRest + Vector3.up * Mathf.Sin(t * 2.2f) * .15f;
                _sign.localRotation = Quaternion.Euler(0f, t * 70f, 0f);
            }
            if (_padPlus != null) _padPlus.localScale = Vector3.one * (used ? 1f : 1f + .06f * Mathf.Sin(t * 4f));
            if (_ring == null) return;
            float p = used ? 0f : _station.RestProgress;
            int n = p <= 0f ? 0 : Mathf.Max(2, Mathf.CeilToInt(Segments * p) + 1);
            _ring.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * .5f - (i / (float)Segments) * Mathf.PI * 2f; // clockwise from the front
                if (i == n - 1) a = Mathf.PI * .5f - p * Mathf.PI * 2f;
                _ring.SetPosition(i, new Vector3(Mathf.Cos(a) * _ringRadius, Mathf.Sin(a) * _ringRadius, 0f));
            }
        }
    }
}
