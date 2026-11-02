using UnityEngine;

namespace CoopGame.Traps
{
    /// <summary>
    /// A never-ending stream of rocks. Roll: rocks bounce down from Start to End. Drop: rocks fall at random spots
    /// inside the drop area. Every machine computes the same rocks from <see cref="TrapClock"/>, so no rock needs a
    /// NetworkObject. Each rock carries a <see cref="TrapHitbox"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrapRockStream : MonoBehaviour
    {
        public enum StreamMode { Roll, Drop }

        [SerializeField] private StreamMode _mode = StreamMode.Roll;
        [Tooltip("Rocks reused in turn. Needs enough to cover one trip: trip seconds / interval + 1")]
        [SerializeField] private Rigidbody[] _rocks = System.Array.Empty<Rigidbody>();
        [Tooltip("Seconds between rocks")]
        [SerializeField, Min(.2f)] private float _interval = 1.6f;
        [Tooltip("Random extra delay per rock (seconds, smaller than the interval)")]
        [SerializeField, Min(0f)] private float _jitter = .6f;
        [Tooltip("Rock size range (diameter scale)")]
        [SerializeField] private Vector2 _sizeRange = new(.8f, 1.4f);

        [Header("Roll")]
        [SerializeField] private Transform _start;
        [SerializeField] private Transform _end;
        [Tooltip("Seconds for a rock to roll from Start to End")]
        [SerializeField, Min(.3f)] private float _rollSeconds = 3.2f;
        [SerializeField, Min(0f)] private float _bounceHeight = .45f;
        [SerializeField, Min(0)] private int _bounces = 5;

        [Header("Drop")]
        [Tooltip("Size of the area rocks land in (local, centred on this object; rocks start at its top)")]
        [SerializeField] private Vector3 _dropArea = new(4f, 10f, 6f);
        [Tooltip("Seconds a rock lies on the ground before it vanishes")]
        [SerializeField, Min(0f)] private float _restSeconds = .8f;

        private long[] _slotEmission;
        private bool[] _slotVisible;
        private Renderer[][] _renderers;
        private Collider[][] _colliders;
        private float _salt;

        private void Awake()
        {
            int n = _rocks.Length;
            _slotEmission = new long[n];
            _slotVisible = new bool[n];
            _renderers = new Renderer[n][];
            _colliders = new Collider[n][];
            for (int i = 0; i < n; i++)
            {
                _slotEmission[i] = long.MinValue;
                _slotVisible[i] = true;
                if (_rocks[i] == null) continue;
                _rocks[i].isKinematic = true;
                _rocks[i].useGravity = false;
                _rocks[i].interpolation = RigidbodyInterpolation.Interpolate;
                _renderers[i] = _rocks[i].GetComponentsInChildren<Renderer>(true);
                _colliders[i] = _rocks[i].GetComponentsInChildren<Collider>(true);
                SetVisible(i, false);
            }
            _salt = TrapClock.Hash01(transform.position, 3) * 1000f;
        }

        private float Life => _mode == StreamMode.Roll ? _rollSeconds :
            Mathf.Sqrt(2f * _dropArea.y / 9.81f) + _restSeconds;

        private double EmitTime(long i) => i * (double)_interval + _jitter * Hash(i, 1);

        private float Hash(long i, int salt) => TrapClock.Hash01((int)(i & 0x7fffffff), (int)_salt, salt, 91);

        private void FixedUpdate()
        {
            int n = _rocks.Length;
            if (n == 0) return;
            double t = TrapClock.Now;
            long newest = (long)System.Math.Floor(t / _interval);
            float life = Life;
            for (int slot = 0; slot < n; slot++)
            {
                if (_rocks[slot] == null) continue;
                long i = newest - (((newest - slot) % n) + n) % n;
                if (EmitTime(i) > t) i -= n;
                double age = t - EmitTime(i);
                if (age < 0 || age > life) { SetVisible(slot, false); continue; }
                bool fresh = _slotEmission[slot] != i;
                if (fresh)
                {
                    _slotEmission[slot] = i;
                    float size = Mathf.Lerp(_sizeRange.x, _sizeRange.y, Hash(i, 2));
                    _rocks[slot].transform.localScale = Vector3.one * size;
                    SetVisible(slot, false); // re-enable after the teleport so the hitbox sees no jump
                }
                Pose(i, (float)age, _rocks[slot].transform.localScale.x * .5f, out Vector3 position, out Quaternion rotation);
                if (fresh) { _rocks[slot].position = position; _rocks[slot].rotation = rotation; _rocks[slot].transform.SetPositionAndRotation(position, rotation); }
                else { _rocks[slot].MovePosition(position); _rocks[slot].MoveRotation(rotation); }
                SetVisible(slot, !fresh || _slotVisible[slot]);
            }
        }

        private void LateUpdate()
        {
            for (int slot = 0; slot < _rocks.Length; slot++)
                if (_rocks[slot] != null && !_slotVisible[slot] && _slotEmission[slot] != long.MinValue)
                {
                    double age = TrapClock.Now - EmitTime(_slotEmission[slot]);
                    if (age >= 0 && age <= Life) SetVisible(slot, true);
                }
        }

        private void Pose(long i, float age, float radius, out Vector3 position, out Quaternion rotation)
        {
            if (_mode == StreamMode.Roll && _start != null && _end != null)
            {
                float f = Mathf.Clamp01(age / _rollSeconds);
                Vector3 path = _end.position - _start.position;
                Vector3 up = transform.up;
                float bounce = _bounces > 0 ? Mathf.Abs(Mathf.Sin(f * _bounces * Mathf.PI)) * _bounceHeight * (1f - f * .5f) : 0f;
                Vector3 side = Vector3.Cross(up, path.normalized) * ((Hash(i, 3) - .5f) * .6f);
                position = _start.position + path * f + up * (radius + bounce) + side;
                Vector3 rollAxis = Vector3.Cross(up, path).normalized;
                float rolled = path.magnitude * f / Mathf.Max(radius, .05f) * Mathf.Rad2Deg;
                rotation = Quaternion.AngleAxis(rolled, rollAxis) * Quaternion.Euler(Hash(i, 4) * 360f, Hash(i, 5) * 360f, 0f);
            }
            else
            {
                Vector3 local = new((Hash(i, 3) - .5f) * _dropArea.x, _dropArea.y * .5f, (Hash(i, 4) - .5f) * _dropArea.z);
                float fall = .5f * 9.81f * age * age;
                local.y = Mathf.Max(-_dropArea.y * .5f + radius, local.y - fall);
                position = transform.TransformPoint(local);
                rotation = Quaternion.Euler(Hash(i, 5) * 360f + age * 200f, Hash(i, 6) * 360f, age * 90f);
            }
        }

        private void SetVisible(int slot, bool visible)
        {
            if (_slotVisible[slot] == visible) return;
            _slotVisible[slot] = visible;
            if (_renderers[slot] != null) foreach (var r in _renderers[slot]) r.enabled = visible;
            if (_colliders[slot] != null) foreach (var c in _colliders[slot]) c.enabled = visible;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, .35f, .1f, .8f);
            if (_mode == StreamMode.Roll && _start != null && _end != null) Gizmos.DrawLine(_start.position, _end.position);
            else { Gizmos.matrix = transform.localToWorldMatrix; Gizmos.DrawWireCube(Vector3.zero, _dropArea); }
        }
#endif
    }
}
