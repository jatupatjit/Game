using CoopGame.CarrySystem;
using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Checkpoint pad (GAME_RULES section 7). It switches on when the cargo is PLACED on it (resting, not carried).
    /// An active pad is the team's respawn point (cargo falls and HP-zero restarts return here, HP is not restored)
    /// and a fast stamina refill zone. The server decides; every peer renders the same state. Stamina is
    /// owner-simulated, so each client refills only its own player.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class CheckpointPad : NetworkBehaviour
    {
        [SerializeField] private FragileCargo _cargo;
        [Tooltip("Position of this pad along the route (display only, 1 = first).")]
        [SerializeField, Min(1)] private int _number = 1;
        [Tooltip("Half size (m) of the square area on the pad where a resting cargo counts as placed.")]
        [SerializeField, Min(.5f)] private float _halfExtent = 2.2f;
        [SerializeField, Min(.1f)] private float _maxCargoHeight = 2.4f;
        [SerializeField, Min(.05f)] private float _restSeconds = .45f;
        [SerializeField, Min(.01f)] private float _restSpeed = .35f;
        [Tooltip("Radius (m) of the stamina refill zone around the pad centre while the pad is active.")]
        [SerializeField, Min(.5f)] private float _refillRadius = 3.6f;
        [Tooltip("Stamina recovery multiplier inside the refill zone (normal rate x this).")]
        [SerializeField, Min(1f)] private float _refillBoost = 3f;
        [SerializeField] private Transform _cargoPoint;
        [SerializeField] private Transform _playerPoint;
        [SerializeField] private Renderer[] _lamps;
        [SerializeField] private Material _inactiveMaterial;
        [SerializeField] private Material _activeMaterial;
        [SerializeField] private Transform _flag;
        [SerializeField] private float _flagDownY, _flagUpY = 1.9f;
        [SerializeField] private TextMesh _label;

        private readonly NetworkVariable<bool> _active = new(false);
        private Rigidbody _cargoBody;
        private float _restTimer;
        private bool _firedThisRest;
        private int _shown = -1;
        private PlayerStamina _localStamina;
        private Transform _localPlayer;

        public bool IsActive => IsSpawned && _active.Value;
        public int Number => _number;
        public Transform CargoPoint => _cargoPoint != null ? _cargoPoint : transform;
        public Transform PlayerPoint => _playerPoint != null ? _playerPoint : transform;
        public float RefillRadius => _refillRadius;

        public override void OnNetworkSpawn()
        {
            _shown = -1;
            _restTimer = 0f;
            _firedThisRest = false;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned) return;
            if (IsServer && _cargo != null) ServerTick();
            int state = _active.Value ? 1 : 0;
            if (state != _shown) ApplyVisual(state);
        }

        private void ServerTick()
        {
            if (CargoResting())
            {
                _restTimer += Time.fixedDeltaTime;
                if (!_firedThisRest && _restTimer >= _restSeconds)
                {
                    _firedThisRest = true;
                    _active.Value = true;
                    var manager = CheckpointManager.Instance;
                    if (manager != null) manager.OnPadPlaced(this, _cargo.CurrentHP.Value);
                }
            }
            else
            {
                _restTimer = 0f;
                _firedThisRest = false;
            }
        }

        private bool CargoResting()
        {
            if (_cargo.IsDestroyed || _cargo.IsSecured || _cargo.IsCarried || _cargo.IsPlacing) return false;
            if (_cargoBody == null) _cargoBody = _cargo.GetComponent<Rigidbody>();
            Vector3 local = transform.InverseTransformPoint(_cargo.ApproximateBoundsCenter);
            if (Mathf.Abs(local.x) > _halfExtent || Mathf.Abs(local.z) > _halfExtent) return false;
            if (local.y < -.25f || local.y > _maxCargoHeight) return false;
            return _cargoBody == null || _cargoBody.linearVelocity.sqrMagnitude <= _restSpeed * _restSpeed;
        }

        private void Update()
        {
            if (!IsSpawned || !_active.Value || _refillBoost <= 1f) return;
            if (_localPlayer == null)
            {
                var manager = NetworkManager;
                var player = manager != null && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
                if (player == null || !player.IsSpawned) return;
                _localPlayer = player.transform;
                _localStamina = player.GetComponent<PlayerStamina>();
            }
            if (_localStamina == null) return;
            Vector3 d = _localPlayer.position - transform.position;
            if (Mathf.Abs(d.y) > 3.5f) return;
            d.y = 0f;
            if (d.sqrMagnitude <= _refillRadius * _refillRadius && _localStamina.CurrentStamina < _localStamina.MaxStamina)
                _localStamina.RegenerateStamina(Time.deltaTime * _refillBoost);
        }

        private void ApplyVisual(int state)
        {
            _shown = state;
            bool on = state == 1;
            if (_lamps != null)
                for (int i = 0; i < _lamps.Length; i++)
                    if (_lamps[i] != null) _lamps[i].sharedMaterial = on ? _activeMaterial : _inactiveMaterial;
            if (_flag != null)
            {
                Vector3 p = _flag.localPosition; p.y = on ? _flagUpY : _flagDownY; _flag.localPosition = p;
            }
            if (_label != null)
                _label.text = on ? "เช็คพอยต์ " + _number + "\nเซฟแล้ว ฟื้นแรงที่นี่" : "เช็คพอยต์ " + _number + "\nวางลังบนแท่นเพื่อเซฟ";
        }
    }
}
