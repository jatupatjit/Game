using UnityEngine;

namespace CoopGame.Traps
{
    /// <summary>
    /// Moves one part of a trap forever, with no rest and no warning. The pose comes from <see cref="TrapClock"/>, so
    /// Host and Clients see the same position. Put it on the moving part (a kinematic Rigidbody is added if missing).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrapMotor : MonoBehaviour
    {
        public enum MotionMode
        {
            [Tooltip("Turns round and round (bars, spinners, turntables, rolling logs)")] Spin,
            [Tooltip("Swings back and forth (pendulums, axes, hammers)")] Swing,
            [Tooltip("Shoots out fast, holds, then pulls back (pistons, crushers, spikes)")] Piston,
            [Tooltip("Slides back and forth smoothly")] PingPong,
            [Tooltip("Slides one way, then jumps back to the start (barrels, conveyor ridges)")] Loop,
            [Tooltip("Turns objects on and off (flames)")] Blink,
        }

        [SerializeField] private MotionMode _mode = MotionMode.Spin;
        [Tooltip("Rotation axis in the parent's space (Spin, Swing, Loop roll)")]
        [SerializeField] private Vector3 _axis = Vector3.up;
        [Tooltip("Spin: degrees per second. Negative turns the other way.")]
        [SerializeField] private float _degreesPerSecond = 180f;
        [Tooltip("Swing: degrees to each side of the centre")]
        [SerializeField, Range(0f, 175f)] private float _swingAngle = 65f;
        [Tooltip("Swing: centre of the swing in degrees")]
        [SerializeField] private float _swingCenter;
        [Tooltip("Seconds for one full cycle (Swing, Piston, PingPong, Loop, Blink)")]
        [SerializeField, Min(.1f)] private float _period = 1.6f;
        [Tooltip("Distance and direction in the parent's space (Piston, PingPong, Loop)")]
        [SerializeField] private Vector3 _travel = new(0f, 0f, 2f);
        [Tooltip("Piston: part of the cycle spent shooting out (small = fast hit)")]
        [SerializeField, Range(.03f, .9f)] private float _outPortion = .12f;
        [Tooltip("Piston: part of the cycle held out before pulling back")]
        [SerializeField, Range(0f, .9f)] private float _holdPortion = .15f;
        [Tooltip("Loop: degrees rolled over one trip (barrels)")]
        [SerializeField] private float _loopRollDegrees;
        [Tooltip("Blink: part of the cycle the objects are on")]
        [SerializeField, Range(.05f, .95f)] private float _blinkOnPortion = .5f;
        [SerializeField] private GameObject[] _blinkObjects = System.Array.Empty<GameObject>();
        [Tooltip("Seconds added to the clock for this trap")]
        [SerializeField] private float _phaseOffset;
        [Tooltip("Add a phase from where the trap stands, so two copies never move in step")]
        [SerializeField] private bool _randomPhaseFromPosition = true;

        private Rigidbody _body;
        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private float _phase;
        private double _lastLoop = -1;
        private int _lastBlink = -1;

        public MotionMode Mode => _mode;

        private void Awake()
        {
            _restPosition = transform.localPosition;
            _restRotation = transform.localRotation;
            if (_mode != MotionMode.Blink)
            {
                _body = GetComponent<Rigidbody>();
                if (_body == null && GetComponentInChildren<Collider>() != null) _body = gameObject.AddComponent<Rigidbody>();
                if (_body != null)
                {
                    _body.isKinematic = true;
                    _body.useGravity = false;
                    _body.interpolation = RigidbodyInterpolation.Interpolate;
                    _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                }
            }
        }

        private void Start()
        {
            float span = _mode == MotionMode.Spin ? 360f / Mathf.Max(1f, Mathf.Abs(_degreesPerSecond)) : _period;
            _phase = _phaseOffset + (_randomPhaseFromPosition ? TrapClock.Hash01(transform.position, 17) * span : 0f);
            Apply(false);
        }

        private void FixedUpdate() => Apply(true);

        private void Apply(bool physics)
        {
            double t = TrapClock.Now + _phase;
            Vector3 axis = _axis.sqrMagnitude > 1e-6f ? _axis.normalized : Vector3.up;
            Vector3 position = _restPosition;
            Quaternion rotation = _restRotation;
            bool teleport = !physics;

            switch (_mode)
            {
                case MotionMode.Spin:
                    rotation = Quaternion.AngleAxis((float)((t * _degreesPerSecond) % 360.0), axis) * _restRotation;
                    break;
                case MotionMode.Swing:
                    rotation = Quaternion.AngleAxis(_swingCenter + _swingAngle * Mathf.Sin((float)(Frac(t / _period) * 2.0 * Mathf.PI)), axis) * _restRotation;
                    break;
                case MotionMode.Piston:
                    position = _restPosition + _travel * PistonCurve((float)Frac(t / _period));
                    break;
                case MotionMode.PingPong:
                    position = _restPosition + _travel * (.5f - .5f * Mathf.Cos((float)(Frac(t / _period) * 2.0 * Mathf.PI)));
                    break;
                case MotionMode.Loop:
                {
                    double cycle = System.Math.Floor(t / _period);
                    float u = (float)(t / _period - cycle);
                    if (cycle != _lastLoop) { teleport |= _lastLoop >= 0; _lastLoop = cycle; }
                    position = _restPosition + _travel * u;
                    if (_loopRollDegrees != 0f) rotation = Quaternion.AngleAxis(_loopRollDegrees * u, axis) * _restRotation;
                    break;
                }
                case MotionMode.Blink:
                {
                    int on = Frac(t / _period) < _blinkOnPortion ? 1 : 0;
                    if (on != _lastBlink)
                    {
                        _lastBlink = on;
                        foreach (var go in _blinkObjects) if (go != null) go.SetActive(on == 1);
                    }
                    return;
                }
            }

            Transform parent = transform.parent;
            if (_body != null && physics && !teleport)
            {
                _body.MovePosition(parent != null ? parent.TransformPoint(position) : position);
                _body.MoveRotation(parent != null ? parent.rotation * rotation : rotation);
            }
            else
            {
                transform.localPosition = position;
                transform.localRotation = rotation;
                if (_body != null) { _body.position = transform.position; _body.rotation = transform.rotation; }
            }
        }

        private float PistonCurve(float u)
        {
            float outEnd = _outPortion;
            float holdEnd = Mathf.Min(.98f, _outPortion + _holdPortion);
            if (u < outEnd) { float k = u / outEnd; return 1f - (1f - k) * (1f - k); }
            if (u < holdEnd) return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, (u - holdEnd) / (1f - holdEnd));
        }

        private static double Frac(double v) => v - System.Math.Floor(v);

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, .3f, .1f, .8f);
            Transform parent = transform.parent;
            Vector3 origin = transform.position;
            if (_mode == MotionMode.Piston || _mode == MotionMode.PingPong || _mode == MotionMode.Loop)
            {
                Vector3 end = parent != null ? parent.TransformPoint((Application.isPlaying ? _restPosition : transform.localPosition) + _travel) : origin + _travel;
                Gizmos.DrawLine(origin, end);
                Gizmos.DrawWireSphere(end, .15f);
            }
            else if (_mode == MotionMode.Spin || _mode == MotionMode.Swing)
            {
                Vector3 axis = parent != null ? parent.TransformDirection(_axis) : _axis;
                Gizmos.DrawRay(origin, axis.normalized * 1.5f);
            }
        }
#endif
    }
}
