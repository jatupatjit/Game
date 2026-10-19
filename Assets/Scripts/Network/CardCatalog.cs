using System.Collections.Generic;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>One entry per card; the effect is applied by the code that owns the scaled quantity (see the table in GAME_RULES.md).</summary>
    public enum CardEffect : byte
    {
        None, StrongArms, LongThrow, HighJumper, Endurance, Teamwork, SteadyHands, LightStep,
        DoubleJump, QuickRecovery, Climber, LastMile, CloseSupport, FastCarrier
    }

    public readonly struct CardDef
    {
        public readonly byte Id;
        public readonly string Key, Name, Description;
        public readonly CardEffect Effect;
        public readonly float Magnitude;
        public CardDef(byte id, string key, string name, string description, CardEffect effect, float magnitude)
        { Id = id; Key = key; Name = name; Description = description; Effect = effect; Magnitude = magnitude; }
    }

    /// <summary>
    /// Single source of truth for the 13 cards: id (1..13, 0 = no card), art key, names, Thai text, effect and
    /// magnitude. Replicated state stores only the id. Also owns the random three-card offer encoding.
    /// </summary>
    public static class CardCatalog
    {
        // Effect magnitudes (multipliers unless noted). Every card reads its number from here.
        public const float StrongArmsWeight = .6f;      // carrier weight load and carry stamina drain x0.6
        public const float LongThrowSpeed = 1.4f;       // throw min/max speed x1.4
        public const float HighJumperHeight = 1.2f;     // jump height x1.2
        public const float EnduranceDrain = .6f;        // solo carry stamina drain x0.6
        public const float TeamworkWeight = .8f;        // carrier weight load x0.8 while 2+ carry
        public const float SteadyHandsWobble = .5f;     // cargo wobble x0.5 (see CarryableObject.SteadyHandsExtraDamping)
        public const float LightStepCost = .8f;         // sprint and wall-jump stamina cost x0.8
        public const int DoubleJumpExtraJumps = 1;      // extra air jumps (count)
        public const float QuickRecoveryRegen = 1.3f;   // stamina regen x1.3 while not carrying
        public const float ClimberSpeed = 1.25f;        // climb pull speed x1.25
        public const float LastMileWeight = .7f;        // carrier weight load x0.7 near the delivery point
        public const float LastMileRadius = 12f;        // metres; default of CarryableObject._lastMileRadius
        public const float CloseSupportDrain = .8f;     // carrier stamina drain x0.8 near a holder
        public const float CloseSupportRadius = 6f;     // metres; default of PlayerExpeditionState._closeSupportRadius
        public const float FastCarrierSpeed = 1.15f;    // carry walking speed x1.15

        public const int OfferSize = 3;

        private static readonly CardDef[] Cards =
        {
            new CardDef(1, "strong_arms", "STRONG ARMS", "ของที่ยกเบาขึ้น 40%", CardEffect.StrongArms, StrongArmsWeight),
            new CardDef(2, "long_throw", "LONG THROW", "ปาพัสดุไกลขึ้น 40%", CardEffect.LongThrow, LongThrowSpeed),
            new CardDef(3, "high_jumper", "HIGH JUMPER", "กระโดดสูงขึ้น 20%", CardEffect.HighJumper, HighJumperHeight),
            new CardDef(4, "endurance", "ENDURANCE", "ใช้ Stamina ตอนแบกของคนเดียว ลดช้าลง 40%", CardEffect.Endurance, EnduranceDrain),
            new CardDef(5, "teamwork", "TEAMWORK", "ถ้ามีผู้เล่น 2 คนขึ้นไปช่วยกันยก น้ำหนักของเบาขึ้น 20%", CardEffect.Teamwork, TeamworkWeight),
            new CardDef(6, "steady_hands", "STEADY HANDS", "พัสดุสั่น/แกว่งขณะถือ ลดลง 50%", CardEffect.SteadyHands, SteadyHandsWobble),
            new CardDef(7, "light_step", "LIGHT STEP", "ใช้ Stamina จากการวิ่ง/กระโดด ลดลง 20%", CardEffect.LightStep, LightStepCost),
            new CardDef(8, "double_jump", "DOUBLE JUMP", "กระโดดกลางอากาศได้เพิ่ม 1 ครั้ง", CardEffect.DoubleJump, DoubleJumpExtraJumps),
            new CardDef(9, "quick_recovery", "QUICK RECOVERY", "Stamina ฟื้นเร็วขึ้น 30% เมื่อไม่ได้แบกของ", CardEffect.QuickRecovery, QuickRecoveryRegen),
            new CardDef(10, "climber", "CLIMBER", "ความเร็วในการปีนเพิ่มขึ้น 25%", CardEffect.Climber, ClimberSpeed),
            new CardDef(11, "last_mile", "LAST MILE", "เมื่อพัสดุอยู่ใกล้จุดส่งของ ของเบาขึ้น 30%", CardEffect.LastMile, LastMileWeight),
            new CardDef(12, "close_support", "CLOSE SUPPORT", "เมื่ออยู่ใกล้ผู้เล่นที่กำลังแบกของ stamina ของคนๆ นั้นลดช้าลง 20%", CardEffect.CloseSupport, CloseSupportDrain),
            new CardDef(13, "fast_carrier", "FAST CARRIER", "ความเร็วการเดินขณะแบกของเพิ่ม 15%", CardEffect.FastCarrier, FastCarrierSpeed),
        };

        private static readonly CardDef None = new CardDef(0, null, "CHOOSE A CARD", "ยังไม่ได้เลือกการ์ด", CardEffect.None, 1f);

        public static int Count => Cards.Length;
        public static CardDef Get(byte id) => id >= 1 && id <= Cards.Length ? Cards[id - 1] : None;
        public static bool IsValid(byte id) => id >= 1 && id <= Cards.Length;
        public static string Name(byte id) => Get(id).Name;
        public static string Description(byte id) => Get(id).Description;
        /// <summary>Tooltip text: name, then the Thai effect text.</summary>
        public static string Tooltip(byte id) => id == 0 ? None.Description : Get(id).Name + "\n" + Get(id).Description;

        // ── Art ──────────────────────────────────────────────────────────────
        private const string ArtFolder = "Cards/";
        /// <summary>Share of the card height (from the top) holding the illustration; the rest is the baked name/effect strip.</summary>
        public const float IllustrationFraction = .78f;
        private static readonly Dictionary<byte, Sprite> ArtCache = new Dictionary<byte, Sprite>();
        private static readonly Dictionary<byte, Sprite> IconCache = new Dictionary<byte, Sprite>();

        /// <summary>Full card image (illustration + baked name/effect strip), null for id 0 or missing art.</summary>
        public static Sprite Art(byte id)
        {
            if (!IsValid(id)) return null;
            if (ArtCache.TryGetValue(id, out Sprite cached) && cached != null) return cached;
            Sprite sprite = Resources.Load<Sprite>(ArtFolder + Get(id).Key);
            if (sprite == null) Debug.LogWarning("[CardCatalog] Missing card art Resources/" + ArtFolder + Get(id).Key + " (run Tools/UI Kit/Rebuild Card Art)");
            ArtCache[id] = sprite;
            return sprite;
        }

        /// <summary>Small icon: the illustration area of the card (top part, no baked text). Null for id 0.</summary>
        public static Sprite Icon(byte id)
        {
            if (IconCache.TryGetValue(id, out Sprite cached) && cached != null) return cached;
            Sprite art = Art(id);
            if (art == null) return null;
            Rect r = art.rect;
            float cut = r.height * (1f - IllustrationFraction);
            Sprite icon = Sprite.Create(art.texture, new Rect(r.x, r.y + cut, r.width, r.height - cut), new Vector2(.5f, .5f), art.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            icon.name = Get(id).Key + "_icon";
            IconCache[id] = icon;
            return icon;
        }

        // ── Random offer (server draws, three distinct ids packed in a uint: id0 | id1<<8 | id2<<16) ──
        public static uint Draw(System.Random rng)
        {
            var pool = new List<byte>(Cards.Length);
            for (byte i = 1; i <= Cards.Length; i++) pool.Add(i);
            uint packed = 0;
            for (int i = 0; i < OfferSize; i++)
            {
                int pick = rng.Next(pool.Count);
                packed |= (uint)pool[pick] << (8 * i);
                pool.RemoveAt(pick);
            }
            return packed;
        }

        public static byte OfferAt(uint packed, int slot) => slot >= 0 && slot < OfferSize ? (byte)((packed >> (8 * slot)) & 0xFF) : (byte)0;
        public static bool IsOffered(uint packed, byte id)
        {
            if (!IsValid(id)) return false;
            for (int i = 0; i < OfferSize; i++) if (OfferAt(packed, i) == id) return true;
            return false;
        }
        public static bool HasOffer(uint packed) => OfferAt(packed, 0) != 0;
    }
}
