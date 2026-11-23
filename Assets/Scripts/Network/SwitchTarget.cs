using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Something a <see cref="ClimbWallSwitch"/> opens ("opens anything"): moves and/or turns this
    /// transform from its rest pose to the open pose once the switch is on, the same way on every screen (driven only
    /// by the switch's synced state, so no extra network traffic). Gate = move up, bridge = rotate down,
    /// platform = slide out. A kinematic Rigidbody on it is moved with MovePosition so riders and crates follow.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SwitchTarget : MonoBehaviour
    {
        [SerializeField] private ClimbWallSwitch _source;
        [Tooltip("Local position offset when fully open (gate: up, platform: out).")]
        [SerializeField] private Vector3 _openOffset;
        [Tooltip("Local rotation added when fully open (bridge: e.g. 90 on X to lower it).")]
        [SerializeField] private Vector3 _openEuler;
        [SerializeField, Min(.1f)] private float _seconds = 2.5f;
        [Tooltip("Optional objects switched off when open (e.g. a blocking collider).")]
        [SerializeField] private GameObject[] _hideWhenOpen;

        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private Rigidbody _body;
        private float _t;
        private bool _wasOpen;

        public ClimbWallSwitch Source { get => _source; set => _source = value; }

        private void Awake()
        {
            _restPosition = transform.localPosition;
            _restRotation = transform.localRotation;
            _body = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            bool open = _source != null && _source.IsOn;
            if (open != _wasOpen)
            {
                _wasOpen = open;
                if (open) GameplayFeedback.Play(GameplayFeedback.Cue.Impact, transform.position, .6f);
                if (_hideWhenOpen != null) foreach (var go in _hideWhenOpen) if (go != null) go.SetActive(!open);
            }
            float target = open ? 1f : 0f;
            if (Mathf.Approximately(_t, target)) return;
            _t = Mathf.MoveTowards(_t, target, Time.fixedDeltaTime / _seconds);
            float e = Mathf.SmoothStep(0f, 1f, _t);
            Vector3 local = _restPosition + _openOffset * e;
            Quaternion localRot = _restRotation * Quaternion.Euler(_openEuler * e);
            if (_body != null && _body.isKinematic)
            {
                Transform parent = transform.parent;
                _body.MovePosition(parent != null ? parent.TransformPoint(local) : local);
                _body.MoveRotation(parent != null ? parent.rotation * localRot : localRot);
            }
            else
            {
                transform.localPosition = local;
                transform.localRotation = localRot;
            }
        }
    }
}
