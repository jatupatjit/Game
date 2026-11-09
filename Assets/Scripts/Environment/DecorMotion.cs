using UnityEngine;

namespace CoopGame.SceneDecor
{
    /// <summary>
    /// Local-only visual motion for scene decoration (windmill sails, lighthouse lamp, clouds, balloons).
    /// No physics and no networking: every client runs it on its own, so it costs nothing on the network.
    /// </summary>
    [DisallowMultipleComponent]
    public class DecorMotion : MonoBehaviour
    {
        [Header("Spin")]
        [SerializeField] private Vector3 _spinAxis = Vector3.forward;
        [Tooltip("Degrees per second around the local spin axis. 0 = no spin.")]
        [SerializeField] private float _spinSpeed;

        [Header("Bob up and down")]
        [SerializeField] private float _bobHeight;
        [SerializeField] private float _bobPeriod = 6f;

        [Header("Drift (world space, goes back and forth over Drift Range metres)")]
        [SerializeField] private Vector3 _driftDirection = Vector3.right;
        [SerializeField] private float _driftSpeed;
        [SerializeField] private float _driftRange = 200f;

        private Vector3 _startLocal;
        private Vector3 _startWorld;
        private float _phase;

        private void Awake()
        {
            _startLocal = transform.localPosition;
            _startWorld = transform.position;
            // Different start phase per object so a group never moves in lockstep.
            _phase = Mathf.Abs(_startWorld.x * 0.37f + _startWorld.z * 0.11f) % 100f;
        }

        private void Update()
        {
            float t = Time.time + _phase;
            if (_spinSpeed != 0f) transform.Rotate(_spinAxis, _spinSpeed * Time.deltaTime, Space.Self);

            if (_driftSpeed != 0f && _driftRange > 0f)
            {
                float d = Mathf.PingPong(t * _driftSpeed, _driftRange) - _driftRange * 0.5f;
                Vector3 pos = _startWorld + _driftDirection.normalized * d;
                if (_bobHeight != 0f) pos.y += Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(.1f, _bobPeriod)) * _bobHeight;
                transform.position = pos;
            }
            else if (_bobHeight != 0f)
            {
                transform.localPosition = _startLocal + Vector3.up * (Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(.1f, _bobPeriod)) * _bobHeight);
            }
        }

        public void Configure(Vector3 spinAxis, float spinSpeed, float bobHeight, float bobPeriod, Vector3 driftDirection, float driftSpeed, float driftRange)
        {
            _spinAxis = spinAxis; _spinSpeed = spinSpeed;
            _bobHeight = bobHeight; _bobPeriod = bobPeriod;
            _driftDirection = driftDirection; _driftSpeed = driftSpeed; _driftRange = driftRange;
        }
    }
}
