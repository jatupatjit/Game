using CoopGame.Network;
using UnityEngine;

namespace CoopGame.Traps
{
    /// <summary>
    /// Co-op lift: one player climbs up and presses the switch (<see cref="ClimbSwitchGate"/>, key E); the platform
    /// where the team waits with the crate then rises to the next path and stays there.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoopSwitchLift : MonoBehaviour
    {
        [SerializeField] private ClimbSwitchGate _switch;
        [SerializeField] private Rigidbody _platform;
        [Tooltip("How far the platform rises (local to this object)")]
        [SerializeField] private Vector3 _rise = new(0f, 6f, 0f);
        [Tooltip("Rising speed in m/s (slow keeps the crate on board)")]
        [SerializeField, Min(.1f)] private float _speed = 1.2f;

        private Vector3 _rest;
        private float _progress;

        private void Awake()
        {
            if (_platform == null) return;
            _rest = transform.InverseTransformPoint(_platform.position);
            _platform.isKinematic = true;
            _platform.useGravity = false;
            _platform.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void FixedUpdate()
        {
            if (_platform == null) return;
            float target = _switch != null && _switch.IsOpen ? 1f : 0f;
            float distance = Mathf.Max(.01f, _rise.magnitude);
            if (_progress == target) return;
            _progress = Mathf.MoveTowards(_progress, target, _speed / distance * Time.fixedDeltaTime);
            float eased = Mathf.SmoothStep(0f, 1f, _progress);
            _platform.MovePosition(transform.TransformPoint(_rest + _rise * eased));
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_platform == null) return;
            Gizmos.color = new Color(.2f, .9f, .4f, .8f);
            Vector3 from = Application.isPlaying ? transform.TransformPoint(_rest) : _platform.position;
            Gizmos.DrawLine(from, from + transform.TransformVector(_rise));
            Gizmos.DrawWireCube(from + transform.TransformVector(_rise), _platform.transform.lossyScale);
        }
#endif
    }
}
