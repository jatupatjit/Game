using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Co-op drawbridge (George 2026-11-19, idea 10): the bridge stands raised over a gap; two players each hold
    /// Interact (E) at one of the two crank wheels to lower it. With only one crank turning nothing moves; when both
    /// let go before it is down, it slowly swings back up. Fully down, it latches and stays down. A solo session
    /// needs one crank (demo mode). The host decides; every screen swings the deck from the synced progress.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class CoopDrawbridge : NetworkBehaviour
    {
        [Tooltip("Hinge at the near edge; the deck is its child and points along the hinge's +Z when down.")]
        [SerializeField] private Transform _hinge;
        [Tooltip("The deck's kinematic Rigidbody (moved with the hinge).")]
        [SerializeField] private Rigidbody _deck;
        [Tooltip("Crank wheels (spin with the progress); players hold Interact near them.")]
        [SerializeField] private Transform[] _cranks;
        [SerializeField] private Renderer[] _crankLights;
        [SerializeField] private Material _idleMaterial;
        [SerializeField] private Material _activeMaterial;
        [Tooltip("Ropes from the deck tip to the top of the posts (scaled cylinders, optional).")]
        [SerializeField] private Transform[] _ropes;
        [SerializeField] private Transform[] _ropeTops;
        [SerializeField] private Transform[] _ropeEnds;
        [SerializeField] private TextMesh _label;
        [SerializeField, Range(0f, 90f)] private float _raisedAngle = 80f;
        [Tooltip("Seconds to lower the bridge with both cranks turning.")]
        [SerializeField, Min(.5f)] private float _lowerSeconds = 4.5f;
        [Tooltip("Seconds it takes to swing back up from fully down when nobody turns (it latches once fully down).")]
        [SerializeField, Min(.5f)] private float _raiseSeconds = 9f;
        [SerializeField, Min(.5f)] private float _crankReach = 1.8f;
        [SerializeField, Min(0f)] private float _crankTurns = 3f;

        private const float HoldTimeout = .35f, MaxReportedOffset = 3f;
        private readonly NetworkVariable<float> _progress = new NetworkVariable<float>(0f);
        // Bit per crank being turned now (for the lights and the sign on every screen); bit 7 = latched down.
        private readonly NetworkVariable<byte> _turning = new NetworkVariable<byte>(0);
        private readonly ulong[] _holder = { ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue };
        private readonly float[] _heldUntil = new float[4];
        private float _nextSend, _shown = -1f;
        private bool _wasHeld;
        private byte _shownTurning = byte.MaxValue;
        private Quaternion _hingeRest;
        private Quaternion[] _crankRest;

        public float Progress => IsSpawned ? _progress.Value : 0f;
        public bool IsDown => IsSpawned && (_turning.Value & 0x80) != 0;

        private void Awake()
        {
            if (_hinge != null) _hingeRest = _hinge.localRotation;
            _crankRest = new Quaternion[_cranks != null ? _cranks.Length : 0];
            for (int i = 0; i < _crankRest.Length; i++) if (_cranks[i] != null) _crankRest[i] = _cranks[i].localRotation;
        }

        // ── Input (every machine, for its own player) ──
        private void Update()
        {
            if (!IsSpawned || !IsClient || _cranks == null) return;
            var player = NetworkManager.LocalClient?.PlayerObject;
            int crank = player != null ? NearestCrank(player.transform.position) : -1;
            bool held = crank >= 0 && GameSettings.Held(BindRole.Interact) && !PauseMenu.IsPaused &&
                        !MissionFailUI.IsVisible && !ExpeditionHUD.BlocksGameplayInput;
            if (held && (!_wasHeld || Time.unscaledTime >= _nextSend))
            {
                TurnCrankRpc((byte)crank, player.transform.position);
                _nextSend = Time.unscaledTime + .15f;
            }
            _wasHeld = held;
        }

        private int NearestCrank(Vector3 position)
        {
            int best = -1;
            float bestSq = _crankReach * _crankReach;
            for (int i = 0; i < _cranks.Length; i++)
            {
                if (_cranks[i] == null) continue;
                Vector3 d = _cranks[i].position - position;
                d.y *= .5f;
                if (d.sqrMagnitude < bestSq) { bestSq = d.sqrMagnitude; best = i; }
            }
            return best;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void TurnCrankRpc(byte crank, Vector3 holderPosition, RpcParams rpc = default)
        {
            if (crank >= _cranks.Length || crank >= _holder.Length) return;
            if (!NetworkManager.ConnectedClients.TryGetValue(rpc.Receive.SenderClientId, out var client) || client.PlayerObject == null) return;
            Vector3 seen = client.PlayerObject.transform.position;
            Vector3 position = float.IsFinite(holderPosition.x) && float.IsFinite(holderPosition.y) && float.IsFinite(holderPosition.z) &&
                (holderPosition - seen).sqrMagnitude <= MaxReportedOffset * MaxReportedOffset ? holderPosition : seen;
            if ((position - _cranks[crank].position).sqrMagnitude > (_crankReach + .6f) * (_crankReach + .6f)) return;
            // One player turns one crank: drop this player from any other crank.
            for (int i = 0; i < _holder.Length; i++) if (i != crank && _holder[i] == client.ClientId) _heldUntil[i] = 0f;
            _holder[crank] = client.ClientId;
            _heldUntil[crank] = Time.time + HoldTimeout;
        }

        private void FixedUpdate()
        {
            if (IsSpawned && IsServer) Simulate(Time.fixedDeltaTime);
            float progress = Progress;
            if (_hinge != null)
            {
                float angle = Mathf.Lerp(-_raisedAngle, 0f, Mathf.SmoothStep(0f, 1f, progress));
                Quaternion local = _hingeRest * Quaternion.Euler(angle, 0f, 0f);
                _hinge.localRotation = local;
                if (_deck != null && _deck.isKinematic)
                {
                    _deck.MovePosition(_deck.transform.position);
                    _deck.MoveRotation(_deck.transform.rotation);
                }
            }
            for (int i = 0; _cranks != null && i < _cranks.Length; i++)
                if (_cranks[i] != null) _cranks[i].localRotation = _crankRest[i] * Quaternion.Euler(0f, 0f, progress * 360f * _crankTurns * (i % 2 == 0 ? 1f : -1f));
            byte turning = IsSpawned ? _turning.Value : (byte)0;
            if (turning != _shownTurning || Mathf.Abs(progress - _shown) > .02f) ShowState(turning, progress);
        }

        private void Simulate(float dt)
        {
            if ((_turning.Value & 0x80) != 0) return; // latched down
            int players = 0;
            foreach (var c in NetworkManager.ConnectedClientsList) if (c.PlayerObject != null && c.PlayerObject.IsSpawned) players++;
            byte bits = 0;
            int distinct = 0;
            ulong first = ulong.MaxValue;
            for (int i = 0; i < _cranks.Length && i < _holder.Length; i++)
            {
                if (Time.time > _heldUntil[i]) continue;
                bits |= (byte)(1 << i);
                if (_holder[i] != first) { distinct++; if (first == ulong.MaxValue) first = _holder[i]; }
            }
            int required = players <= 1 ? 1 : Mathf.Min(2, _cranks.Length);
            float p = _progress.Value;
            if (distinct >= required) p += dt / _lowerSeconds;
            else if (bits == 0) p -= dt / _raiseSeconds;
            p = Mathf.Clamp01(p);
            if (p >= 1f)
            {
                bits |= 0x80;
                GameRoom.ServerFeed("สะพานชักลงแล้ว! ข้ามไปได้เลย", new Color(.55f, .85f, 1f));
            }
            if (!Mathf.Approximately(_progress.Value, p)) _progress.Value = p;
            if (_turning.Value != bits) _turning.Value = bits;
        }

        private void ShowState(byte turning, float progress)
        {
            bool started = _shownTurning != byte.MaxValue;
            byte before = started ? _shownTurning : (byte)0;
            _shownTurning = turning;
            _shown = progress;
            for (int i = 0; _crankLights != null && i < _crankLights.Length; i++)
                if (_crankLights[i] != null) _crankLights[i].sharedMaterial = (turning & (1 << i)) != 0 || (turning & 0x80) != 0 ? _activeMaterial : _idleMaterial;
            if (started && (turning & 0x80) != 0 && (before & 0x80) == 0)
                GameplayFeedback.Play(GameplayFeedback.Cue.Impact, _deck != null ? _deck.position : transform.position, 1f);
            else if ((turning & 0x7F) != 0 && (before & 0x7F) == 0)
                GameplayFeedback.Play(GameplayFeedback.Cue.Click, transform.position, .7f);
            if (_label == null) return;
            int count = 0;
            for (int i = 0; i < 7; i++) if ((turning & (1 << i)) != 0) count++;
            string key = GameSettings.KeyName(BindRole.Interact);
            _label.text = (turning & 0x80) != 0 ? "สะพานลงแล้ว ข้ามไปได้เลย" :
                $"กด {key} ค้างที่ล้อหมุน 2 คนพร้อมกัน\nเพื่อลดสะพาน ({count}/2)  {Mathf.RoundToInt(progress * 100f)}%";
        }

        private void LateUpdate()
        {
            if (_ropes == null) return;
            for (int i = 0; i < _ropes.Length; i++)
            {
                if (_ropes[i] == null || _ropeTops == null || i >= _ropeTops.Length || _ropeEnds == null || i >= _ropeEnds.Length) continue;
                Vector3 a = _ropeTops[i].position, b = _ropeEnds[i].position;
                Vector3 d = b - a;
                if (d.sqrMagnitude < 1e-4f) continue;
                _ropes[i].position = (a + b) * .5f;
                _ropes[i].rotation = Quaternion.FromToRotation(Vector3.up, d);
                Vector3 parent = _ropes[i].parent != null ? _ropes[i].parent.lossyScale : Vector3.one;
                _ropes[i].localScale = new Vector3(.06f / parent.x, d.magnitude * .5f / parent.y, .06f / parent.z);
            }
        }
    }
}
