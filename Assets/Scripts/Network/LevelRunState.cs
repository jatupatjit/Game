using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>One-line HUD messages the server posts to every player (checkpoint saved, cargo fell, ...).</summary>
    public enum LevelNotice : byte
    {
        None = 0, CheckpointSaved = 1, CargoFell = 2, CargoRestart = 3, RepairKit = 4, SideSuccess = 5, SideMissed = 6, KitFull = 7
    }

    /// <summary>
    /// Play timer (counts up, HUD only) and the optional side mission (GAME_RULES section 7, type 1: reach the branch
    /// island within a time limit counted from leaving the start). Time never touches the delivery score and never
    /// fails the level; the only reward is a one-time coin bonus plus a star shown in the HUD.
    /// Server-authoritative; clients derive the elapsed time from the replicated start time and the NGO server clock.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class LevelRunState : NetworkBehaviour
    {
        public static LevelRunState Instance { get; private set; }

        [SerializeField] private FragileCargo _cargo;
        [SerializeField] private DeliveryPoint _delivery;
        [Tooltip("The timer starts when the cargo first leaves this radius around the start point.")]
        [SerializeField] private Transform _startPoint;
        [SerializeField, Min(1f)] private float _startRadius = 20f;
        [Tooltip("Branch island used by the side mission; a player inside this radius counts as arrived.")]
        [SerializeField] private Transform _branchPoint;
        [SerializeField, Min(1f)] private float _branchRadius = 9f;
        [SerializeField, Min(10f)] private float _sideLimitSeconds = 270f;
        [SerializeField, Min(0)] private int _bonusCoins = 50;

        private readonly NetworkVariable<byte> _timerState = new(0);          // 0 waiting, 1 running, 2 stopped
        private readonly NetworkVariable<double> _startTime = new(0);
        private readonly NetworkVariable<float> _finalSeconds = new(0f);
        private readonly NetworkVariable<byte> _sideState = new(0);           // 0 pending, 1 success, 2 missed
        private readonly NetworkVariable<float> _sideReachedAt = new(-1f);
        private readonly NetworkVariable<byte> _noticeKind = new(0);
        private readonly NetworkVariable<int> _noticeSeq = new(0);

        private void Awake() => Instance = this;

        public bool IsRunning => IsSpawned && _timerState.Value == 1;
        public bool HasStarted => IsSpawned && _timerState.Value != 0;
        public bool IsStopped => IsSpawned && _timerState.Value == 2;
        public float SideLimitSeconds => _sideLimitSeconds;
        public int BonusCoins => _bonusCoins;
        /// <summary>0 pending, 1 success, 2 missed (late or expired).</summary>
        public byte SideState => IsSpawned ? _sideState.Value : (byte)0;
        public float SideReachedAt => IsSpawned ? _sideReachedAt.Value : -1f;
        public int Stars => SideState == 1 ? 1 : 0;
        public LevelNotice Notice => IsSpawned ? (LevelNotice)_noticeKind.Value : LevelNotice.None;
        public int NoticeSequence => IsSpawned ? _noticeSeq.Value : 0;
        public Transform BranchPoint => _branchPoint;

        public float ElapsedSeconds
        {
            get
            {
                if (!IsSpawned) return 0f;
                if (_timerState.Value == 0) return 0f;
                if (_timerState.Value == 2) return _finalSeconds.Value;
                return Mathf.Max(0f, (float)(NetworkManager.ServerTime.Time - _startTime.Value));
            }
        }

        /// <summary>Server only: show a message on every HUD.</summary>
        public void PostNotice(LevelNotice kind)
        {
            if (!IsSpawned || !IsServer) return;
            _noticeKind.Value = (byte)kind;
            _noticeSeq.Value++;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer) return;
            if (_timerState.Value == 0)
            {
                if (_cargo != null && _startPoint != null)
                {
                    Vector3 d = _cargo.transform.position - _startPoint.position; d.y = 0f;
                    if (d.sqrMagnitude > _startRadius * _startRadius)
                    {
                        _startTime.Value = NetworkManager.ServerTime.Time;
                        _timerState.Value = 1;
                    }
                }
                return;
            }
            if (_timerState.Value == 1 && _delivery != null && _delivery.IsDelivered)
            {
                _finalSeconds.Value = ElapsedSeconds;
                _timerState.Value = 2;
            }
            if (_sideState.Value != 0 || _branchPoint == null) return;
            float elapsed = ElapsedSeconds;
            if (elapsed > _sideLimitSeconds)
            {
                // Too late: the bonus is gone for good; nothing else changes.
                _sideState.Value = 2;
                PostNotice(LevelNotice.SideMissed);
                return;
            }
            var clients = NetworkManager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                var player = clients[i].PlayerObject;
                if (player == null || !player.IsSpawned) continue;
                Vector3 d = player.transform.position - _branchPoint.position;
                if (Mathf.Abs(d.y) > 6f) continue;
                d.y = 0f;
                if (d.sqrMagnitude > _branchRadius * _branchRadius) continue;
                Reached(elapsed);
                return;
            }
        }

        private void Reached(float elapsed)
        {
            _sideReachedAt.Value = elapsed;
            _sideState.Value = 1;
            var clients = NetworkManager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                var player = clients[i].PlayerObject;
                if (player != null && player.TryGetComponent<PlayerExpeditionState>(out var state))
                    state.Coins.Value = Mathf.Min(1000000, state.Coins.Value + _bonusCoins);
            }
            PostNotice(LevelNotice.SideSuccess);
        }
    }
}
