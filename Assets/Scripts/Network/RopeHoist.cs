using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// A rope hoist at the top of a cliff. A team-mate down below
    /// sets the crate in the basket; someone who climbed up holds Interact (E) at the winch to haul it up. Needs at
    /// least 2 players in the session. Letting go half way drops
    /// the basket faster than the crate can fall, so the crate free-falls and takes normal impact damage when it lands.
    /// At the top the basket latches until the crate is lifted out, then winds itself back down.
    /// Server simulates; every peer moves the basket, rope and winch from the same synced progress.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class RopeHoist : NetworkBehaviour
    {
        [SerializeField] private Transform _winch;
        [Tooltip("Spinning wheel of the winch (turns with the rope).")]
        [SerializeField] private Transform _wheel;
        [SerializeField, Min(.5f)] private float _winchReach = 2f;
        [Tooltip("Kinematic basket body; its start position is the bottom stop.")]
        [SerializeField] private Rigidbody _basket;
        [Tooltip("How far the basket travels up (world units, along world up).")]
        [SerializeField, Min(.5f)] private float _height = 6f;
        [SerializeField, Min(1f)] private float _riseSeconds = 8f;
        [Tooltip("Basket fall acceleration when released (m/s^2); above gravity so the crate falls free of it.")]
        [SerializeField, Min(10f)] private float _dropAcceleration = 18f;
        [SerializeField, Min(1)] private int _requiredPlayers = 2;
        [Tooltip("Rope piece stretched from the pulley down to the basket.")]
        [SerializeField] private Transform _rope;
        [SerializeField] private Transform _pulley;
        [Tooltip("Rope attach point on the basket.")]
        [SerializeField] private Transform _basketTop;
        [Tooltip("Inside of the basket, used to see whether a crate is in it.")]
        [SerializeField] private BoxCollider _hold;
        [SerializeField] private TextMesh _label;
        [SerializeField] private Renderer _lamp;

        private const float HoldTimeout = .35f, MaxReportedOffset = 3f;
        private const byte Cranking = 1, Dropping = 2, NeedFriend = 4, AtTop = 8, Loaded = 16;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private readonly NetworkVariable<float> _progress = new(0f);
        private readonly NetworkVariable<byte> _state = new(0);
        private readonly Collider[] _overlap = new Collider[16];
        private MaterialPropertyBlock _block;
        private Vector3 _bottom;
        private float _heldUntil, _dropSpeed, _emptyAtTopSince = -1f, _nextSend, _shownProgress = -1f;
        private ulong _holder = ulong.MaxValue;
        private bool _wasHeld;
        private byte _shownState = byte.MaxValue;
        private Quaternion _wheelRest;

        public float Progress => IsSpawned ? _progress.Value : 0f;
        public bool IsDropping => IsSpawned && (_state.Value & Dropping) != 0;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (_basket != null) _bottom = _basket.position;
            if (_wheel != null) _wheelRest = _wheel.localRotation;
        }

        // Input: every machine, for its own player
        private void Update()
        {
            if (!IsSpawned || !IsClient || _winch == null) return;
            var player = NetworkManager.LocalClient?.PlayerObject;
            bool near = player != null && (player.transform.position - _winch.position).sqrMagnitude <= _winchReach * _winchReach;
            bool held = near && GameSettings.Held(BindRole.Interact) && !PauseMenu.IsPaused && !MissionFailUI.IsVisible &&
                        !ExpeditionHUD.BlocksGameplayInput && !UIPanels.AnyOpen;
            if (held && (!_wasHeld || Time.unscaledTime >= _nextSend))
            {
                CrankRpc(player.transform.position);
                _nextSend = Time.unscaledTime + .15f;
            }
            _wasHeld = held;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void CrankRpc(Vector3 holderPosition, RpcParams rpc = default)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(rpc.Receive.SenderClientId, out var client) || client.PlayerObject == null) return;
            Vector3 seen = client.PlayerObject.transform.position;
            Vector3 p = holderPosition;
            Vector3 position = float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z) &&
                               (p - seen).sqrMagnitude <= MaxReportedOffset * MaxReportedOffset ? p : seen;
            if ((position - _winch.position).sqrMagnitude > (_winchReach + .6f) * (_winchReach + .6f)) return;
            _holder = client.ClientId;
            _heldUntil = Time.time + HoldTimeout;
        }

        /// <summary>Test probe: hold (or let go of) the winch for <paramref name="seconds"/> from the server.</summary>
        public void ServerHoldFor(float seconds) { if (IsServer) { _holder = NetworkManager.LocalClientId; _heldUntil = Time.time + seconds; } }

        private bool CrateInBasket()
        {
            if (_hold == null) return false;
            Vector3 center = _hold.transform.TransformPoint(_hold.center);
            Vector3 half = Vector3.Scale(_hold.size * .5f, _hold.transform.lossyScale);
            int count = Physics.OverlapBoxNonAlloc(center, half, _overlap, _hold.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                // Counted while someone is lifting it out too, so the basket never pulls away under a held crate.
                if (_overlap[i] != null && _overlap[i].GetComponentInParent<CarryableObject>() != null) return true;
            }
            return false;
        }

        private void FixedUpdate()
        {
            if (IsSpawned && IsServer) Simulate(Time.fixedDeltaTime);
            float progress = Progress;
            if (_basket != null)
            {
                Vector3 target = _bottom + Vector3.up * (_height * progress);
                if (_basket.isKinematic) _basket.MovePosition(target); else _basket.position = target;
            }
            if (_wheel != null) _wheel.localRotation = _wheelRest * Quaternion.Euler(0f, 0f, progress * _height * 120f);
            StretchRope();
            byte state = IsSpawned ? _state.Value : (byte)0;
            if (state != _shownState || Mathf.Abs(progress - _shownProgress) > .02f) ShowState(state, progress);
        }

        private void Simulate(float dt)
        {
            int players = 0;
            foreach (var c in NetworkManager.ConnectedClientsList) if (c.PlayerObject != null && c.PlayerObject.IsSpawned) players++;
            bool enoughPlayers = players >= _requiredPlayers;
            bool held = Time.time <= _heldUntil;
            bool loaded = CrateInBasket();
            float p = _progress.Value;
            byte state = 0;
            if (!enoughPlayers) state |= NeedFriend;
            if (loaded) state |= Loaded;

            if (held && enoughPlayers && (_state.Value & Dropping) == 0)
            {
                _dropSpeed = 0f;
                p += dt / _riseSeconds;
                state |= Cranking;
            }
            else if (p >= 1f && loaded)
            {
                _emptyAtTopSince = -1f; // latched at the top until the crate is taken out
            }
            else if (p >= 1f)
            {
                if (_emptyAtTopSince < 0f) _emptyAtTopSince = Time.time;
                if (Time.time - _emptyAtTopSince > 3f) p -= dt / (_riseSeconds * .8f);
            }
            else if (p > 0f && loaded)
            {
                // Let go with a crate on board: the rope runs free.
                if ((_state.Value & Dropping) == 0) GameRoom.ServerFeed("ปล่อยรอก! ลังร่วงลงไป", new Color(1f, .5f, .35f));
                _dropSpeed += _dropAcceleration * dt;
                p -= _dropSpeed * dt / _height;
                state |= Dropping;
            }
            else if (p > 0f)
            {
                _dropSpeed = 0f;
                p -= dt / (_riseSeconds * .8f); // empty basket winds back down
            }
            if (p <= 0f) { p = 0f; _dropSpeed = 0f; state &= unchecked((byte)~Dropping); }
            if (p >= 1f) { p = 1f; state |= AtTop; }
            else _emptyAtTopSince = -1f;
            if (p != _progress.Value) _progress.Value = p; // exact: an "approximately 1" left below 1 would read as half way and drop the crate
            if (_state.Value != state) _state.Value = state;
        }

        /// <summary>The sign turns to whoever looks at it (readable from below and from the winch).</summary>
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (_label != null && cam != null) _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.transform.position);
        }

        private void StretchRope()
        {
            if (_rope == null || _pulley == null || _basketTop == null) return;
            Vector3 a = _pulley.position, b = _basketTop.position, d = b - a;
            if (d.sqrMagnitude < 1e-4f) return;
            _rope.position = (a + b) * .5f;
            _rope.rotation = Quaternion.FromToRotation(Vector3.up, d);
            Vector3 parent = _rope.parent != null ? _rope.parent.lossyScale : Vector3.one;
            _rope.localScale = new Vector3(.07f / parent.x, d.magnitude * .5f / parent.y, .07f / parent.z);
        }

        private void ShowState(byte state, float progress)
        {
            bool started = _shownState != byte.MaxValue;
            byte before = started ? _shownState : (byte)0;
            _shownState = state;
            _shownProgress = progress;
            if (started && (before & Dropping) != 0 && (state & Dropping) == 0 && _basket != null)
                GameplayFeedback.Play(GameplayFeedback.Cue.Impact, _basket.position, 1f);
            string key = GameSettings.KeyName(BindRole.Interact);
            string text;
            if ((state & Dropping) != 0) text = "ปล่อยรอกแล้ว! ลังร่วง";
            else if ((state & NeedFriend) != 0) text = "รอกนี้ต้องมีเพื่อนอย่างน้อย " + _requiredPlayers + " คน";
            else if ((state & AtTop) != 0) text = (state & Loaded) != 0 ? "ถึงข้างบนแล้ว ยกลังออกได้เลย" : "ตะกร้ากำลังกลับลงไป";
            else if ((state & Cranking) != 0) text = "กำลังดึงขึ้น " + Mathf.RoundToInt(progress * 100f) + "%\nห้ามปล่อย " + key + " กลางทาง!";
            else if ((state & Loaded) != 0) text = "ลังอยู่ในตะกร้าแล้ว\nคนข้างบนกด " + key + " ค้างที่รอก";
            else text = "วางลังในตะกร้า\nแล้วให้เพื่อนปีนขึ้นไปหมุนรอก";
            if (_label != null) _label.text = text;
            if (_lamp != null)
            {
                _lamp.GetPropertyBlock(_block);
                Color c = (state & Dropping) != 0 ? new Color(1f, .3f, .25f) : (state & Cranking) != 0 ? new Color(.3f, .95f, .4f) :
                          (state & NeedFriend) != 0 ? new Color(.6f, .6f, .6f) : new Color(1f, .75f, .2f);
                _block.SetColor(BaseColor, c);
                _lamp.SetPropertyBlock(_block);
            }
        }
    }
}
