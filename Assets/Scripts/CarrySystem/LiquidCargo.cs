using CoopGame.Network;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Liquid crate: a glass water tank. Tilting it past a calm angle, hard starts and
    /// stops, and knocks spill water; the delivery score is multiplied by the water left (100% = full score).
    /// The host simulates the spill and syncs the fill; every screen draws the water level, a slosh, drips where it
    /// pours over the rim, and a small water bar over the tank.
    /// Prefab: Assets/Prefabs/Items/Variants/Cargo_Liquid (built by Tools > Cargo > Build Liquid Crate).
    /// </summary>
    [DefaultExecutionOrder(105)]
    [RequireComponent(typeof(FragileCargo))]
    public sealed class LiquidCargo : NetworkBehaviour
    {
        [Header("Spill (host)")]
        [Tooltip("Tilt (degrees) the water stays in.")]
        [SerializeField, Range(0f, 60f)] private float _calmTilt = 18f;
        [Tooltip("Percent of the water lost per second for each degree past the calm tilt.")]
        [SerializeField, Min(0f)] private float _spillPerDegree = .7f;
        [Tooltip("Sideways acceleration (m/s², of the smoothed velocity) that starts to slosh water out.")]
        [SerializeField, Min(0f)] private float _joltThreshold = 5f;
        [Tooltip("Percent lost per second for each m/s² past the jolt threshold.")]
        [SerializeField, Min(0f)] private float _joltSpill = 1.5f;
        [Tooltip("Percent lost per HP of impact damage (drops, bumps, traps).")]
        [SerializeField, Min(0f)] private float _spillPerDamage = 1.2f;

        [Header("Look")]
        [Tooltip("The water block inside the tank (scaled from the bottom by the fill).")]
        [SerializeField] private Transform _water;
        [Tooltip("Inside of the tank in the crate's local space: bottom Y and full-water height.")]
        [SerializeField] private float _bottomY = -.44f;
        [SerializeField] private float _fullHeight = .82f;
        [SerializeField, Range(0f, 30f)] private float _sloshDegrees = 10f;

        private readonly NetworkVariable<float> _fill = new NetworkVariable<float>(1f);
        private FragileCargo _cargo;
        private Rigidbody _body;
        private float _localFill = 1f, _serverFill = 1f, _shownFill = 1f, _nextDrip;
        private Vector3 _lastVelocity, _lastPosition, _seenVelocity, _seenAccel, _smoothAccel;
        private Vector3 _waterScale;
        private bool _warnedHalf, _warnedEmpty;
        private Transform _barRoot, _barFill;
        private Renderer _barFillRenderer;
        private static Material _lit;

        /// <summary>Water left, 0..1 (every screen).</summary>
        public float Fill01 => IsSpawned ? _fill.Value : _localFill;

        /// <summary>Delivery pay multiplier: the share of water left (1 for any other crate).</summary>
        public static float BonusFor(Component cargo) =>
            cargo != null && cargo.TryGetComponent(out LiquidCargo liquid) && liquid.enabled ? Mathf.Clamp01(liquid.Fill01) : 1f;

        private static bool HostSide => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;

        private void Awake()
        {
            _cargo = GetComponent<FragileCargo>();
            _body = GetComponent<Rigidbody>();
            _lastPosition = transform.position;
            if (_water != null) _waterScale = _water.localScale;
            FragileCargo.OnCargoDamagedGlobal += OnDamaged;
            FragileCargo.OnCargoRespawnedGlobal += OnRespawned;
            BuildBar();
        }

        public override void OnDestroy()
        {
            FragileCargo.OnCargoDamagedGlobal -= OnDamaged;
            FragileCargo.OnCargoRespawnedGlobal -= OnRespawned;
            if (_barRoot != null) Destroy(_barRoot.gameObject);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) { _serverFill = 1f; _fill.Value = 1f; }
            _shownFill = Fill01;
        }

        // Spill (host)

        private void FixedUpdate()
        {
            if (!HostSide || _cargo == null || _cargo.IsSecured || _cargo.IsDestroyed || _body == null) return;
            float dt = Time.fixedDeltaTime;
            // The held crate's physics velocity is spiky step to step; low-pass it first, then take the change.
            Vector3 velocity = Vector3.Lerp(_lastVelocity, _body.linearVelocity, 1f - Mathf.Exp(-dt * 6f));
            _smoothAccel = Vector3.ProjectOnPlane(velocity - _lastVelocity, Vector3.up) / dt;
            _lastVelocity = velocity;
            if (_serverFill <= 0f) return;

            float tilt = Vector3.Angle(transform.up, Vector3.up);
            float rate = Mathf.Max(0f, tilt - _calmTilt) * _spillPerDegree;
            if (_cargo.IsCarried) rate += Mathf.Clamp(_smoothAccel.magnitude - _joltThreshold, 0f, 8f) * _joltSpill;
            if (rate > 0f) Spill(rate * dt);
        }

        private void Spill(float percent)
        {
            if (percent <= 0f || _serverFill <= 0f) return;
            _serverFill = Mathf.Max(0f, _serverFill - percent / 100f);
            if (IsSpawned) { if (Mathf.Abs(_fill.Value - _serverFill) >= .002f || _serverFill <= 0f) _fill.Value = _serverFill; }
            else _localFill = _serverFill;
            if (!_warnedHalf && _serverFill < .5f)
            {
                _warnedHalf = true;
                GameRoom.ServerFeed("ลังน้ำหกไปครึ่งหนึ่งแล้ว! ระวังเอียง", new Color(.45f, .75f, 1f));
            }
            if (!_warnedEmpty && _serverFill <= 0f)
            {
                _warnedEmpty = true;
                GameRoom.ServerFeed("น้ำในลังหกหมดแล้ว! ส่งไปก็ไม่ได้คะแนน", new Color(1f, .45f, .35f));
            }
        }

        private void OnDamaged(FragileCargo cargo, int damage)
        {
            if (cargo != _cargo || !HostSide) return;
            Spill(damage * _spillPerDamage);
        }

        private void OnRespawned(FragileCargo cargo)
        {
            if (cargo != _cargo || !HostSide) return;
            _serverFill = 1f;
            _warnedHalf = _warnedEmpty = false;
            if (IsSpawned) _fill.Value = 1f; else _localFill = 1f;
        }

        // Look (every screen)

        private void Update()
        {
            if (Time.deltaTime <= 0f) return;
            Vector3 velocity = (transform.position - _lastPosition) / Time.deltaTime;
            _lastPosition = transform.position;
            _seenAccel = Vector3.Lerp(_seenAccel, (velocity - _seenVelocity) / Time.deltaTime, 1f - Mathf.Exp(-Time.deltaTime * 6f));
            _seenVelocity = Vector3.Lerp(_seenVelocity, velocity, 1f - Mathf.Exp(-Time.deltaTime * 8f));

            float fill = Fill01;
            if (fill < _shownFill - .0005f && Time.time >= _nextDrip) Drip(_shownFill - fill);
            _shownFill = Mathf.MoveTowards(_shownFill, fill, Time.deltaTime * .8f);

            if (_water != null)
            {
                float h = _fullHeight * Mathf.Max(.0001f, _shownFill);
                _water.gameObject.SetActive(_shownFill > .005f);
                _water.localScale = new Vector3(_waterScale.x, h, _waterScale.z);
                // Slosh against the motion, and lean back toward level when the tank tilts (a little; the box would poke out).
                Vector3 local = transform.InverseTransformDirection(Vector3.ProjectOnPlane(_seenAccel, Vector3.up)) * .06f;
                Quaternion slosh = Quaternion.Euler(Mathf.Clamp(local.z, -1f, 1f) * _sloshDegrees, 0f, -Mathf.Clamp(local.x, -1f, 1f) * _sloshDegrees);
                Quaternion level = Quaternion.FromToRotation(transform.up, Vector3.up);
                Quaternion toLevel = Quaternion.Inverse(transform.rotation) * level * transform.rotation;
                Quaternion target = Quaternion.Slerp(Quaternion.identity, toLevel, .35f) * slosh;
                _water.localRotation = Quaternion.Slerp(_water.localRotation, target, 1f - Mathf.Exp(-Time.deltaTime * 8f));
                _water.localPosition = new Vector3(0f, _bottomY + h * .5f, 0f);
            }
            UpdateBar(fill);
        }

        /// <summary>Water drops pour over the lowest rim edge.</summary>
        private void Drip(float lost)
        {
            _nextDrip = Time.time + .07f;
            Vector3 top = transform.TransformPoint(0f, .5f, 0f);
            Vector3 down = Vector3.ProjectOnPlane(Vector3.down, transform.up);
            Vector3 size = Vector3.Scale(transform.lossyScale, new Vector3(.5f, .5f, .5f));
            Vector3 rim = top + (down.sqrMagnitude > 1e-4f ? down.normalized * Mathf.Max(size.x, size.z) : Vector3.zero);
            int count = Mathf.Clamp(Mathf.RoundToInt(lost * 300f), 2, 14);
            GameplayFeedback.Burst(rim, new Color(.35f, .65f, 1f, .9f), count, 1.3f, .07f);
        }

        // Small water bar over the tank (above the HP label), blue going red when low.
        private void BuildBar()
        {
            // Its own root (not a child): the crate's outline and grip code only see the crate's own parts.
            _barRoot = new GameObject(name + "_WaterBar").transform;
            Part(_barRoot, Vector3.zero, new Vector3(.6f, .08f, .01f), new Color(.12f, .14f, .2f));
            _barFill = Part(_barRoot, new Vector3(0f, 0f, -.006f), new Vector3(.57f, .055f, .01f), new Color(.3f, .65f, 1f));
            _barFillRenderer = _barFill.GetComponent<Renderer>();
        }

        private void UpdateBar(float fill)
        {
            if (_barRoot == null) return;
            bool show = !_cargo.IsSecured && !_cargo.IsDestroyed;
            if (_barRoot.gameObject.activeSelf != show) _barRoot.gameObject.SetActive(show);
            if (!show) return;
            float top = transform.lossyScale.y * .5f;
            _barRoot.position = transform.position + Vector3.up * (top + .75f);
            var cam = Camera.main;
            if (cam != null) _barRoot.rotation = Quaternion.LookRotation(_barRoot.position - cam.transform.position);
            float shown = Mathf.Clamp01(_shownFill);
            _barFill.localScale = new Vector3(.57f * Mathf.Max(.001f, shown), .055f, .01f);
            _barFill.localPosition = new Vector3(-.285f * (1f - shown), 0f, -.006f);
            if (_barFillRenderer != null && _barFillRenderer.sharedMaterial != null)
                _barFillRenderer.sharedMaterial.SetColor("_BaseColor", shown > .35f ? new Color(.3f, .65f, 1f) : Color.Lerp(new Color(1f, .3f, .25f), new Color(.3f, .65f, 1f), shown / .35f));
        }

        private static Transform Part(Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(temp);
            var go = new GameObject("Bar", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (_lit == null) _lit = Resources.Load<Material>("CosmeticLit");
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (_lit != null) { var m = new Material(_lit); m.SetColor("_BaseColor", color); renderer.sharedMaterial = m; }
            return go.transform;
        }
    }
}
