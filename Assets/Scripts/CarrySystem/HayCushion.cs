using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Hay bale pad: a crate that lands on or rests against it takes no impact or scrape damage
    /// (<see cref="FragileCargo"/> skips cushion colliders). The top bale squashes under the crate like real hay:
    /// a landing pushes it down with a little bounce, a resting crate keeps it pressed, and it springs back when the
    /// crate leaves. Visual + collider only, run the same on every peer from the crate's replicated position.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HayCushion : MonoBehaviour
    {
        [Tooltip("Top bale that squashes (needs a kinematic Rigidbody so the crate rides it down)")]
        [SerializeField] private Rigidbody _topBale;
        [Tooltip("Area above the top bale where a crate counts as sitting on it (local to the top bale)")]
        [SerializeField] private Vector3 _sensorSize = new(1.6f, 1.2f, 1.1f);
        [Tooltip("How far (m) a resting crate presses the hay down")]
        [SerializeField, Min(0f)] private float _restSink = .14f;
        [Tooltip("How far (m) a landing can press it at most")]
        [SerializeField, Min(0f)] private float _maxSink = .3f;
        [SerializeField, Min(1f)] private float _stiffness = 90f;
        [SerializeField, Min(0f)] private float _damping = 9f;
        [Tooltip("Sink speed (m/s) added per m/s of landing speed")]
        [SerializeField, Min(0f)] private float _landingKick = .35f;

        private static readonly Collider[] s_hits = new Collider[16];
        private Vector3 _restLocal;
        private Vector3 _restScale;
        private float _sink, _sinkSpeed;
        private bool _hadCrate;

        public static bool IsCushion(Collider collider) =>
            collider != null && collider.GetComponentInParent<HayCushion>() != null;

        private void Awake()
        {
            if (_topBale == null) return;
            _restLocal = _topBale.transform.localPosition;
            _restScale = _topBale.transform.localScale;
            _topBale.isKinematic = true;
            _topBale.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void FixedUpdate()
        {
            if (_topBale == null) return;
            Transform top = _topBale.transform;
            float height = top.lossyScale.y;
            Vector3 centre = top.position + Vector3.up * (height * .5f + _sensorSize.y * .5f);
            int count = Physics.OverlapBoxNonAlloc(centre, _sensorSize * .5f, s_hits, top.rotation, ~0, QueryTriggerInteraction.Ignore);
            Rigidbody crate = null;
            for (int i = 0; i < count && crate == null; i++)
            {
                var body = s_hits[i].attachedRigidbody;
                if (body != null && body.GetComponent<FragileCargo>() != null) crate = body;
            }
            System.Array.Clear(s_hits, 0, count);

            bool hasCrate = crate != null;
            // Landing: the crate's downward speed punches the hay in.
            if (hasCrate && !_hadCrate) _sinkSpeed += Mathf.Max(0f, -crate.linearVelocity.y) * _landingKick + .4f;
            _hadCrate = hasCrate;

            float target = hasCrate ? _restSink : 0f;
            float accel = (target - _sink) * _stiffness - _sinkSpeed * _damping;
            _sinkSpeed += accel * Time.fixedDeltaTime;
            _sink = Mathf.Clamp(_sink + _sinkSpeed * Time.fixedDeltaTime, -.05f, _maxSink);

            // Squash the bale from its bottom: lower the centre by half the sink, shorten by the sink, bulge a little.
            float squash = Mathf.Clamp01(1f - _sink / Mathf.Max(.01f, _restScale.y * (top.parent != null ? top.parent.lossyScale.y : 1f)));
            float bulge = 1f + (1f - squash) * .35f;
            top.localScale = new Vector3(_restScale.x * bulge, _restScale.y * squash, _restScale.z * bulge);
            Vector3 local = _restLocal - Vector3.up * (_restScale.y * (1f - squash) * .5f);
            _topBale.MovePosition(top.parent != null ? top.parent.TransformPoint(local) : local);
        }
    }
}
