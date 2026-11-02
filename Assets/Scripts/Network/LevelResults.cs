using System;
using System.Collections.Generic;
using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.Network
{
    /// <summary>One player's numbers for the current level (synced on <see cref="PlayerExpeditionState.Stats"/>).</summary>
    public struct RunStats : INetworkSerializeByMemcpy, IEquatable<RunStats>
    {
        public float CarrySeconds, CarryDistance, LevelSeconds;
        public int CrateHits, Falls, Knockdowns;

        public bool Equals(RunStats o) => CarrySeconds.Equals(o.CarrySeconds) && CarryDistance.Equals(o.CarryDistance) &&
            LevelSeconds.Equals(o.LevelSeconds) && CrateHits == o.CrateHits && Falls == o.Falls && Knockdowns == o.Knockdowns;
        public override bool Equals(object obj) => obj is RunStats o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(CarrySeconds, CarryDistance, LevelSeconds, CrateHits, Falls, Knockdowns);
    }

    public readonly struct LevelAward
    {
        public readonly string Title, Player, Detail;
        public LevelAward(string title, string player, string detail) { Title = title; Player = player; Detail = detail; }
    }

    /// <summary>
    /// Level results data for the end-of-level screen (the UI itself is MissionCompleteUI). The host tracks each
    /// player's carry time and distance, crate hits they caused (carrying, or let go of within 3 s), falls and
    /// knockdowns (reported by the owner), and freezes the numbers with the level time on delivery. Every machine
    /// reads the same synced numbers: <see cref="Players"/>, <see cref="Awards"/>, <see cref="Stars"/>,
    /// <see cref="IsReady"/> / <see cref="Ready"/>.
    /// </summary>
    public sealed class LevelResults : MonoBehaviour
    {
        public const byte EventFall = 1, EventKnockdown = 2;
        private const float BlameSeconds = 3f, FlushInterval = .5f;

        /// <summary>Every machine: the level was delivered and the frozen numbers are in.</summary>
        public static event Action Ready;
        public static bool IsReady { get; private set; }

        private static LevelResults _instance;
        private readonly Dictionary<ulong, RunStats> _live = new();
        private readonly Dictionary<ulong, float> _lastCarried = new();
        private Vector3 _lastCargoPosition;
        private bool _hasCargoPosition, _frozen;
        private float _levelStart, _nextFlush;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("LevelResults") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LevelResults>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            FragileCargo.OnCargoDamagedGlobal += OnCargoDamaged;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            FragileCargo.OnCargoDamagedGlobal -= OnCargoDamaged;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            IsReady = false;
            _live.Clear();
            _lastCarried.Clear();
            _hasCargoPosition = _frozen = false;
            _levelStart = Time.time;
            if (!IsServer()) return;
            foreach (var player in PlayerExpeditionState.Everyone)
                if (player != null && player.IsSpawned) player.Stats.Value = default;
        }

        private static bool IsServer() => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer;

        private RunStats Live(ulong id) => _live.TryGetValue(id, out RunStats s) ? s : default;

        /// <summary>Host: an owner reported a fall or knockdown (PlayerExpeditionState.ReportEventRpc).</summary>
        public static void ServerReport(PlayerExpeditionState player, byte kind)
        {
            if (_instance == null || _instance._frozen || player == null) return;
            RunStats s = _instance.Live(player.OwnerClientId);
            if (kind == EventFall) s.Falls++;
            else if (kind == EventKnockdown) s.Knockdowns++;
            _instance._live[player.OwnerClientId] = s;
            _instance._nextFlush = 0f;
        }

        private void OnCargoDamaged(FragileCargo cargo, int damage)
        {
            if (!IsServer() || _frozen || cargo == null) return;
            bool any = false;
            foreach (var player in PlayerExpeditionState.Everyone)
                if (player != null && cargo.HasCarrier(player.OwnerClientId)) { Blame(player.OwnerClientId); any = true; }
            if (any) return;
            foreach (var pair in _lastCarried)
                if (Time.time - pair.Value <= BlameSeconds) Blame(pair.Key);
        }

        private void Blame(ulong id)
        {
            RunStats s = Live(id);
            s.CrateHits++;
            _live[id] = s;
            _nextFlush = 0f;
        }

        private void FixedUpdate()
        {
            if (!IsServer() || _frozen) return;
            var mission = FindAnyObjectByType<LevelMission>();
            var cargo = mission != null ? mission.Cargo : null;
            if (cargo != null)
            {
                Vector3 position = cargo.transform.position;
                Vector3 step = _hasCargoPosition ? position - _lastCargoPosition : Vector3.zero;
                step.y = 0f;
                _lastCargoPosition = position;
                _hasCargoPosition = true;
                float moved = step.magnitude < 2f ? step.magnitude : 0f; // a respawn teleport is not carrying
                foreach (var player in PlayerExpeditionState.Everyone)
                {
                    if (player == null || !cargo.HasCarrier(player.OwnerClientId)) continue;
                    RunStats s = Live(player.OwnerClientId);
                    s.CarrySeconds += Time.fixedDeltaTime;
                    s.CarryDistance += moved;
                    _live[player.OwnerClientId] = s;
                    _lastCarried[player.OwnerClientId] = Time.time;
                }
            }
            bool delivered = mission != null && mission.IsSpawned && mission.Phase.Value >= 2;
            if (delivered) _frozen = true;
            if (!delivered && Time.time < _nextFlush) return;
            _nextFlush = Time.time + FlushInterval;
            float levelSeconds = Time.time - _levelStart;
            foreach (var player in PlayerExpeditionState.Everyone)
            {
                if (player == null || !player.IsSpawned) continue;
                RunStats s = Live(player.OwnerClientId);
                s.LevelSeconds = levelSeconds;
                if (!player.Stats.Value.Equals(s)) player.Stats.Value = s;
            }
        }

        private void Update()
        {
            if (IsReady) return;
            var mission = FindAnyObjectByType<LevelMission>();
            if (mission == null || !mission.IsSpawned || mission.Phase.Value < 2) return;
            // Wait for the host's frozen numbers (the level time is set from the first flush on).
            foreach (var player in PlayerExpeditionState.Everyone)
                if (player != null && player.IsSpawned && player.Stats.Value.LevelSeconds <= 0f) return;
            IsReady = true;
            Ready?.Invoke();
        }

        // ───────────────────────── Reading (every machine) ─────────────────────────

        public static string NameOf(PlayerExpeditionState player) =>
            "P" + (player.OwnerClientId + 1) + (player.DisplayName.Value.Length > 0 ? " " + player.DisplayName.Value : "");

        public static List<PlayerExpeditionState> Players()
        {
            var list = new List<PlayerExpeditionState>();
            foreach (var player in PlayerExpeditionState.Everyone)
                if (player != null && player.IsSpawned) list.Add(player);
            list.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
            return list;
        }

        /// <summary>Level time in seconds (same for everyone), 0 until known.</summary>
        public static float LevelSeconds()
        {
            foreach (var player in Players()) return player.Stats.Value.LevelSeconds;
            return 0f;
        }

        /// <summary>1-3 stars from the share of crate HP left (3: 80%+, 2: 50%+, 1 otherwise; glass has less HP).</summary>
        public static int Stars(int hp, int maxHp = 100)
        {
            float share = maxHp > 0 ? hp / (float)maxHp : 0f;
            return share >= .8f ? 3 : share >= .5f ? 2 : 1;
        }

        /// <summary>Fun awards: each goes to the player with the most of one thing (none when nobody has any).</summary>
        public static List<LevelAward> Awards()
        {
            var awards = new List<LevelAward>();
            var players = Players();
            void Best(string title, Func<RunStats, float> value, Func<float, string> detail)
            {
                PlayerExpeditionState best = null;
                float top = 0f;
                foreach (var p in players)
                {
                    float v = value(p.Stats.Value);
                    if (v > top) { top = v; best = p; }
                }
                if (best != null) awards.Add(new LevelAward(title, NameOf(best), detail(top)));
            }
            Best("แขนเหล็ก", s => s.CarrySeconds, v => "ยกนานสุด " + Mathf.RoundToInt(v) + " วิ");
            Best("มือลั่น", s => s.CrateHits, v => "ทำลังกระแทก " + v + " ครั้ง");
            Best("นักบิน", s => s.Falls, v => "ตกแมพ " + v + " ครั้ง");
            Best("ตุ๊กตาล้มลุก", s => s.Knockdowns, v => "ล้ม " + v + " ครั้ง");
            return awards;
        }

        /// <summary>One-line summary of a player's numbers.</summary>
        public static string Line(RunStats s) =>
            $"ยก {Mathf.RoundToInt(s.CarrySeconds)} วิ {Mathf.RoundToInt(s.CarryDistance)} ม. | ชน {s.CrateHits} | ตก {s.Falls} | ล้ม {s.Knockdowns}";
    }
}
