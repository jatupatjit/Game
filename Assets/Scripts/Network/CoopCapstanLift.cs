using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Co-op capstan: players walk clockwise around the pole inside the walk ring to wind the rope and raise the
    /// wooden platform; walking the other way lowers it. With 2+ players in the session it needs 2 different
    /// walkers for full speed (one walker turns it slowly); a solo session runs at full speed (demo mode).
    /// When nobody walks, the platform slides back to the last ratchet notch, so the top is a stable stop.
    /// The server measures the walkers; every peer moves the platform and pole from the same synced progress.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class CoopCapstanLift : NetworkBehaviour
    {
        [SerializeField] private Transform _pole;
        [Tooltip("Rotating part (pole cap + push bars); its spin is driven by the platform progress.")]
        [SerializeField] private Transform _hub;
        [SerializeField] private Rigidbody _platform;
        [Tooltip("How far the platform rises (local to this object)")]
        [SerializeField] private Vector3 _rise = new(0f, 5f, 0f);
        [SerializeField, Min(.1f)] private float _innerRadius = 1f;
        [SerializeField, Min(.2f)] private float _outerRadius = 3f;
        [Tooltip("Feet must be within this height of the pole base to count as walking the ring")]
        [SerializeField, Min(.1f)] private float _ringHeight = 1.5f;
        [Tooltip("Platform speed in m/s with a full team walking (slow keeps the crate on board)")]
        [SerializeField, Min(.05f)] private float _speed = .7f;
        [Tooltip("Speed multiplier when fewer walkers than required push")]
        [SerializeField, Range(0f, 1f)] private float _shortHandedFactor = .3f;
        [Tooltip("Speed in m/s the platform slips back to the last notch when nobody walks")]
        [SerializeField, Min(.05f)] private float _slipSpeed = .5f;
        [SerializeField, Min(1)] private int _notches = 4;
        [Tooltip("Walking speed around the pole (degrees per second) that counts as pushing")]
        [SerializeField, Min(1f)] private float _minAngularSpeed = 25f;
        [Tooltip("Walking speed round the pole (degrees per second) that gives the full platform speed")]
        [SerializeField, Min(5f)] private float _fullSpeedDegrees = 45f;
        [Tooltip("How far (degrees) the push bar stays ahead of the walker, so it never passes through them")]
        [SerializeField, Range(10f, 40f)] private float _barLead = 24f;
        [Tooltip("Rope from the pulley beam down to the platform; stretched to follow it")]
        [SerializeField] private Transform _platformRope;
        [SerializeField] private Transform _ropeTop;
        [SerializeField] private Renderer[] _ringIndicators;
        [SerializeField] private Material _idleMaterial;
        [SerializeField] private Material _activeMaterial;
        [SerializeField] private TextMesh _statusText;

        private const float PushMemory = .15f;
        private const float BarSpacing = 90f;

        private readonly NetworkVariable<float> _progress = new(0f);
        private readonly NetworkVariable<byte> _walkers = new(0);
        private readonly NetworkVariable<byte> _required = new(1);
        // Pole angle (degrees, unwrapped). It follows the walkers and holds still when nobody pushes (a pawl).
        private readonly NetworkVariable<float> _hubAngle = new(0f);
        private readonly Dictionary<ulong, float> _lastAngle = new();
        private readonly Dictionary<ulong, float> _lastPush = new();
        private readonly Dictionary<ulong, int> _pushDirection = new();
        private readonly List<ulong> _stale = new();
        private Vector3 _rest;
        private Quaternion _hubRest;
        private float _shown;
        private float _hubShown;
        private int _visualState = -1;

        public float Progress => _progress.Value;
        public int Walkers => _walkers.Value;
        public int RequiredWalkers => _required.Value;
        public bool AtTop => _progress.Value >= 1f;

        /// <summary>True when a point (a player's feet) is on the walk ring; padding widens it by metres.</summary>
        public bool IsNearRing(Vector3 feet, float padding = 0f)
        {
            Vector3 offset = feet - (_pole != null ? _pole.position : transform.position);
            float flat = new Vector2(offset.x, offset.z).magnitude;
            return flat <= _outerRadius + padding && Mathf.Abs(offset.y) <= _ringHeight + padding;
        }

        private void Awake()
        {
            if (_hub != null) _hubRest = _hub.localRotation;
            if (_platform == null) return;
            _rest = transform.InverseTransformPoint(_platform.position);
            _platform.isKinematic = true;
            _platform.useGravity = false;
            _platform.interpolation = RigidbodyInterpolation.Interpolate;
        }

        public override void OnNetworkSpawn()
        {
            _shown = _progress.Value;
            _hubShown = _hubAngle.Value;
            _visualState = -1;
            _lastAngle.Clear();
            _lastPush.Clear();
            _pushDirection.Clear();
        }

        private void FixedUpdate()
        {
            if (IsSpawned && IsServer) ServerTick();

            float target = IsSpawned ? _progress.Value : 0f;
            float distance = Mathf.Max(.01f, _rise.magnitude);
            // Clients ease toward the synced value so network ticks don't step the platform.
            _shown = Mathf.MoveTowards(_shown, target, Mathf.Max(_speed, _slipSpeed) * 1.5f / distance * Time.fixedDeltaTime);
            if (_platform != null)
                _platform.MovePosition(transform.TransformPoint(_rest + _rise * _shown));
            if (_hub != null)
            {
                _hubShown = Mathf.MoveTowards(_hubShown, IsSpawned ? _hubAngle.Value : 0f, 540f * Time.fixedDeltaTime);
                _hub.localRotation = _hubRest * Quaternion.Euler(0f, _hubShown, 0f);
            }
            if (_platformRope != null && _ropeTop != null && _platform != null)
            {
                // Rope ends on the platform's top face at its rest position plus the current rise.
                Vector3 bottom = transform.TransformPoint(_rest + _rise * _shown) + Vector3.up * _platform.transform.lossyScale.y * .5f;
                Vector3 top = _ropeTop.position;
                _platformRope.position = (top + bottom) * .5f;
                _platformRope.rotation = Quaternion.FromToRotation(Vector3.up, top - bottom);
                Vector3 scale = _platformRope.localScale;
                scale.y = Mathf.Max(.01f, (top - bottom).magnitude * .5f / Mathf.Max(.0001f, _platformRope.parent != null ? _platformRope.parent.lossyScale.y : 1f));
                _platformRope.localScale = scale;
            }

            int state = (IsSpawned ? _walkers.Value : 0) * 1000 + (IsSpawned ? _required.Value : 1) * 100 +
                Mathf.RoundToInt(target * 20f);
            if (state != _visualState) ApplyVisual(state);
        }

        private void ServerTick()
        {
            var manager = NetworkManager;
            if (manager == null) return;
            int players = SessionPlayers.Count(manager);
            byte required = (byte)(players > 1 ? 2 : 1);
            int up = 0, down = 0;
            float upLead = float.NaN, downLead = float.NaN;
            float now = Time.time;
            Vector3 centre = _pole != null ? _pole.position : transform.position;

            _stale.Clear();
            foreach (var id in _lastAngle.Keys) _stale.Add(id);
            var clients = manager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                var player = clients[i].PlayerObject;
                ulong id = clients[i].ClientId;
                if (player == null || !player.IsSpawned || !player.TryGetComponent<CharacterController>(out var controller))
                    continue;
                Vector3 feet = player.transform.TransformPoint(controller.center - Vector3.up * controller.height * .5f);
                Vector3 offset = feet - centre;
                float flat = new Vector2(offset.x, offset.z).magnitude;
                if (flat < _innerRadius || flat > _outerRadius || Mathf.Abs(offset.y) > _ringHeight)
                    continue;
                _stale.Remove(id);
                // Bearing grows clockwise seen from above, the winding direction.
                float angle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
                if (_lastAngle.TryGetValue(id, out float last))
                {
                    float speed = Mathf.DeltaAngle(last, angle) / Time.fixedDeltaTime;
                    if (Mathf.Abs(speed) >= _minAngularSpeed)
                    {
                        _lastPush[id] = now;
                        _pushDirection[id] = speed > 0f ? 1 : -1;
                    }
                }
                _lastAngle[id] = angle;
                if (_lastPush.TryGetValue(id, out float pushed) && now - pushed <= PushMemory)
                {
                    // The walker furthest along in the push direction carries the bar.
                    if (_pushDirection[id] > 0) { up++; upLead = float.IsNaN(upLead) ? angle : Lead(upLead, angle, 1f); }
                    else { down++; downLead = float.IsNaN(downLead) ? angle : Lead(downLead, angle, -1f); }
                }
            }
            for (int i = 0; i < _stale.Count; i++)
            {
                _lastAngle.Remove(_stale[i]);
                _lastPush.Remove(_stale[i]);
                _pushDirection.Remove(_stale[i]);
            }

            int net = up - down;
            int walkers = Mathf.Abs(net);
            float distance = Mathf.Max(.01f, _rise.magnitude);
            float progress = _progress.Value;
            float hub = _hubAngle.Value;
            if (walkers > 0)
            {
                float sign = Mathf.Sign(net);
                float lead = sign > 0f ? upLead : downLead;
                // Put the nearest bar just ahead of the leading walker (bars repeat every 90 degrees).
                float desired = lead + sign * _barLead;
                float gap = Mathf.Repeat(desired - hub + BarSpacing * .5f, BarSpacing) - BarSpacing * .5f;
                float step = Mathf.Clamp(gap, -540f * Time.fixedDeltaTime, 540f * Time.fixedDeltaTime);
                // The pole only turns the way they push; never backwards under them.
                if (step * sign < 0f) step = 0f;
                hub += step;
                float factor = walkers >= required ? 1f : _shortHandedFactor;
                float metres = Mathf.Abs(step) / _fullSpeedDegrees * _speed * factor;
                float cap = _speed * factor * 1.25f * Time.fixedDeltaTime;
                progress = Mathf.Clamp01(progress + sign * Mathf.Min(metres, cap) / distance);
            }
            else
            {
                float notch = Mathf.Floor(progress * _notches + .001f) / _notches;
                progress = Mathf.MoveTowards(progress, notch, _slipSpeed / distance * Time.fixedDeltaTime);
            }

            if (!Mathf.Approximately(_progress.Value, progress)) _progress.Value = progress;
            if (!Mathf.Approximately(_hubAngle.Value, hub)) _hubAngle.Value = hub;
            if (_walkers.Value != (byte)Mathf.Max(up, down)) _walkers.Value = (byte)Mathf.Max(up, down);
            if (_required.Value != required) _required.Value = required;
        }

        private static float Lead(float a, float b, float sign) => Mathf.DeltaAngle(a, b) * sign > 0f ? b : a;

        private void ApplyVisual(int state)
        {
            _visualState = state;
            int walkers = state / 1000;
            int required = state / 100 % 10;
            int percent = state % 100 * 5;
            if (_ringIndicators != null && _idleMaterial != null && _activeMaterial != null)
                for (int i = 0; i < _ringIndicators.Length; i++)
                    if (_ringIndicators[i] != null) _ringIndicators[i].sharedMaterial = walkers > 0 ? _activeMaterial : _idleMaterial;
            if (_statusText == null) return;
            if (percent >= 100)
                _statusText.text = "แท่นถึงข้างบนแล้ว\nเดินย้อนทางเพื่อลดแท่นลง";
            else if (walkers == 0)
                _statusText.text = required > 1 ? "ช่วยกัน 2 คน เดินดันคานรอบเสา\nตามเข็มนาฬิกาเพื่อยกแท่น" :
                    "เดินดันคานรอบเสาตามเข็มนาฬิกา\nเพื่อยกแท่น";
            else if (walkers < required)
                _statusText.text = "หมุนช้า... ให้อีกคนมาช่วยดัน\n" + percent + "%";
            else
                _statusText.text = "กำลังยก! " + percent + "%";
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 centre = _pole != null ? _pole.position : transform.position;
            Gizmos.color = new Color(1f, .8f, .2f, .8f);
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.DrawWireDisc(centre, Vector3.up, _innerRadius);
            UnityEditor.Handles.DrawWireDisc(centre, Vector3.up, _outerRadius);
            if (_platform == null) return;
            Gizmos.color = new Color(.2f, .9f, .4f, .8f);
            Vector3 from = Application.isPlaying ? transform.TransformPoint(_rest) : _platform.position;
            Gizmos.DrawLine(from, from + transform.TransformVector(_rise));
            Gizmos.DrawWireCube(from + transform.TransformVector(_rise), _platform.transform.lossyScale);
        }
#endif
    }
}
