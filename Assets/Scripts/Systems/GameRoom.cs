using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Room-wide settings and messages. The difficulty is chosen by any player in the Lobby level map (no vote: the
    /// last pick wins and everyone is told who picked what). It is kept on the server and copied into every player's
    /// <see cref="PlayerExpeditionState.RoomDifficulty"/> so every machine (and late joiners) can read it.
    /// Each difficulty swaps the level crate's behaviour for one of the five crate variants (<see cref="CargoDifficulty"/>)
    /// and multiplies coins and XP.
    /// </summary>
    public static class GameRoom
    {
        public const byte EffectBlast = 1, EffectCluck = 2, EffectBombWarn = 3, EffectTopple = 4, EffectRestack = 5,
            EffectChickenEscape = 6, EffectChickenReset = 7;

        public readonly struct DifficultyDef
        {
            public readonly string Name, Crate, Detail;
            public readonly float Reward;
            public readonly Color Color;
            public DifficultyDef(string name, string crate, string detail, float reward, Color color)
            { Name = name; Crate = crate; Detail = detail; Reward = reward; Color = color; }
        }

        /// <summary>Three difficulties (George 2026-11-09). Each level start draws one crate from the difficulty's set.</summary>
        public static readonly DifficultyDef[] Difficulties =
        {
            new("ง่าย", "ลังไม้", "ลังธรรมดา", 1f, new Color(.45f, .85f, .5f)),
            new("ปกติ", "สุ่ม: ลังไก่ / ลังซ้อนสูง / ลังเหล็ก", "ลังมีนิสัยพิเศษ สุ่มใหม่ทุกด่าน", 1.4f, new Color(1f, .82f, .3f)),
            new("ยาก", "สุ่ม: ลังแก้ว / ลังระเบิด", "ลังเปราะหรือระเบิดได้ สุ่มใหม่ทุกด่าน", 2f, new Color(.95f, .4f, .4f)),
        };

        /// <summary>Crate kinds used by <see cref="CargoDifficulty"/>: 0 wood, 1 chicken, 2 tall stack, 3 glass, 4 heavy, 5 explosive.</summary>
        public static readonly string[] CrateNames = { "ลังไม้", "ลังไก่", "ลังซ้อนสูง", "ลังแก้ว", "ลังเหล็ก", "ลังระเบิด" };
        public static readonly string[] CrateDetails =
        {
            "ลังธรรมดา", "เลี้ยงไก่บนลัง: เอียง/กระชากแรงไก่โมโห โมโหเต็มไก่หนี ส่งถึงพร้อมไก่ได้โบนัส x1.5", "ออกตัว/หยุดแรงๆ หรือทำตก ลังข้างบนร่วง เสีย HP",
            "กระแทกแล้วเสีย HP มากขึ้น 60%", "ต้องยก 2 คน (คนเดียวลากได้แต่ลื่นไถล)", "ทำตก 2 ครั้งระเบิดทันที คนใกล้ๆ กระเด็น"
        };
        private static readonly byte[][] CratePool = { new byte[] { 0 }, new byte[] { 1, 2, 4 }, new byte[] { 3, 5 } };

        // Packed room value: low 4 bits difficulty, high 4 bits this level's crate kind.
        private static byte _serverPacked;

        private static byte Packed
        {
            get
            {
                var manager = NetworkManager.Singleton;
                if (manager != null && manager.IsListening && manager.IsServer) return _serverPacked;
                foreach (var p in PlayerExpeditionState.Everyone)
                    if (p != null && p.IsSpawned) return p.RoomDifficulty.Value;
                return 0;
            }
        }

        /// <summary>The room difficulty on this machine: 0 easy, 1 normal, 2 hard.</summary>
        public static byte Difficulty => (byte)Mathf.Min(Packed & 0x0F, Difficulties.Length - 1);
        /// <summary>This level's crate kind (0-5), drawn by the host from the difficulty's set.</summary>
        public static byte CrateKind => (byte)Mathf.Min(Packed >> 4, CrateNames.Length - 1);

        public static DifficultyDef Current => Difficulties[Mathf.Clamp(Difficulty, 0, Difficulties.Length - 1)];
        public static float RewardMultiplier => Current.Reward;

        private static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer;

        /// <summary>Server: set the difficulty and tell everyone who chose it.</summary>
        public static void ServerSetDifficulty(byte difficulty, string byName)
        {
            if (!IsServer) return;
            difficulty = (byte)Mathf.Clamp(difficulty, 0, Difficulties.Length - 1);
            ServerWrite(difficulty, DrawCrate(difficulty));
            var d = Difficulties[difficulty];
            ServerFeed($"{byName} เลือกความยาก: {d.Name} ({d.Crate})", d.Color);
        }

        private static byte DrawCrate(byte difficulty)
        {
            var pool = CratePool[Mathf.Clamp(difficulty, 0, CratePool.Length - 1)];
            return pool[Random.Range(0, pool.Length)];
        }

        private static void ServerWrite(byte difficulty, byte crate)
        {
            _serverPacked = (byte)(difficulty | (crate << 4));
            foreach (var p in PlayerExpeditionState.Everyone)
                if (p != null && p.IsSpawned) p.RoomDifficulty.Value = _serverPacked;
        }

        /// <summary>Server: a level started; draw this level's crate from the difficulty's set and say which.</summary>
        public static void ServerNewLevel()
        {
            if (!IsServer) return;
            byte difficulty = (byte)(_serverPacked & 0x0F);
            byte crate = DrawCrate(difficulty);
            ServerWrite(difficulty, crate);
            if (difficulty > 0) ServerFeed($"ลังรอบนี้: {CrateNames[crate]} — {CrateDetails[crate]}", Difficulties[difficulty].Color);
        }

        /// <summary>Server: a new player object spawned; give it the room values.</summary>
        public static void ServerInitPlayer(PlayerExpeditionState player)
        {
            if (IsServer && player != null) player.RoomDifficulty.Value = _serverPacked;
        }

        private static PlayerExpeditionState AnySpawned()
        {
            foreach (var p in PlayerExpeditionState.Everyone)
                if (p != null && p.IsSpawned) return p;
            return null;
        }

        /// <summary>Server: show a line in everyone's event feed.</summary>
        public static void ServerFeed(string text, Color color)
        {
            if (!IsServer) return;
            var p = AnySpawned();
            if (p == null) { EventFeed.Post(text, color); return; }
            var line = new FixedString128Bytes();
            // FixedString128 holds 125 UTF-8 bytes; Thai is 3 bytes a letter, so trim to fit.
            while (System.Text.Encoding.UTF8.GetByteCount(text) > FixedString128Bytes.UTF8MaxLengthInBytes) text = text.Substring(0, text.Length - 1);
            line = text;
            p.FeedRpc(line, color);
        }

        /// <summary>True when a spawned player can carry room messages (feed, effects) to every machine.</summary>
        public static bool HasMessenger => AnySpawned() != null;

        /// <summary>Server: play a one-shot effect on every machine.</summary>
        public static void ServerEffect(byte kind, Vector3 at)
        {
            if (!IsServer) return;
            var p = AnySpawned();
            if (p != null) p.EffectRpc(kind, at);
        }

        /// <summary>Every machine: the effect itself (called from <see cref="PlayerExpeditionState.EffectRpc"/>).</summary>
        public static void PlayEffect(byte kind, Vector3 at)
        {
            if (kind == EffectCluck) { GameplayFeedback.Play(GameplayFeedback.Cue.Click, at, .8f); return; }
            if (kind == EffectBombWarn || kind == EffectTopple || kind == EffectRestack) { CargoDifficulty.PlayRoomEffect(kind, at); return; }
            if (kind == EffectChickenEscape || kind == EffectChickenReset) { CoopGame.CarrySystem.ChickenOnCrate.PlayRoomEffect(kind, at); return; }
            if (kind != EffectBlast) return;
            GameplayFeedback.Burst(at, new Color(1f, .55f, .15f), 50, 6f, .3f);
            GameplayFeedback.Burst(at, new Color(.35f, .3f, .28f), 30, 2.5f, .45f);
            GameplayFeedback.Play(GameplayFeedback.Cue.Broken, at);
            CameraShake.Add(at, 1f);
            var manager = NetworkManager.Singleton;
            var player = manager != null && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            if (player == null || !player.TryGetComponent(out CoopGame.Player.RagdollCharacter ragdoll)) return;
            const float radius = 4.5f, impulse = 450f;
            Vector3 away = ragdoll.HipsPosition - at;
            float distance = away.magnitude;
            if (distance > radius) return;
            Vector3 direction = (Vector3.ProjectOnPlane(away, Vector3.up).normalized + Vector3.up * .6f).normalized;
            ragdoll.Knockdown(direction * impulse * (1f - distance / radius * .5f));
        }

        /// <summary>Display name of the player whose machine sent an RPC.</summary>
        public static string NameOfClient(ulong clientId)
        {
            foreach (var p in PlayerExpeditionState.Everyone)
                if (p != null && p.IsSpawned && p.OwnerClientId == clientId) return LevelResults.NameOf(p);
            return "P" + (clientId + 1);
        }
    }
}
