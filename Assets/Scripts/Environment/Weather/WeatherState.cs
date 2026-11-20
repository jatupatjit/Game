using UnityEngine;

namespace CoopGame.Weather
{
    public enum WeatherKind : byte { None, Wind, Rain, GoldenCrate }

    /// <summary>
    /// The weather event running right now, read by gameplay code (crate damage, stamina drain, foot traction).
    /// Written every frame by <see cref="WeatherDirector"/>; every machine works it out from the server clock, so all
    /// players see the same event at the same moment without any extra network traffic. No director in the scene =
    /// clear weather (everything at its normal value).
    /// </summary>
    public static class WeatherState
    {
        /// <summary>Event in effect now (None between events).</summary>
        public static WeatherKind Active { get; internal set; }
        /// <summary>Event that starts soon (warning banner), None otherwise.</summary>
        public static WeatherKind Coming { get; internal set; }
        /// <summary>Seconds until <see cref="Coming"/> starts.</summary>
        public static float ComingIn { get; internal set; }
        /// <summary>Seconds left of <see cref="Active"/>.</summary>
        public static float TimeLeft { get; internal set; }
        /// <summary>0..1 how strong the active event is (fades in and out at its ends).</summary>
        public static float Strength { get; internal set; }

        /// <summary>Foot grip multiplier on the ground (rain makes it slippery). 1 = normal.</summary>
        public static float Traction => Active == WeatherKind.Rain ? Mathf.Lerp(1f, s_rainTraction, Strength) : 1f;
        /// <summary>Golden crate event: a crate at <paramref name="point"/> (inside a golden <see cref="WeatherZone"/>) takes no damage.</summary>
        public static bool CrateProtectedAt(Vector3 point) => Active == WeatherKind.GoldenCrate && WeatherZone.Inside(WeatherKind.GoldenCrate, point);
        /// <summary>Golden crate event: a player at <paramref name="point"/> (inside a golden zone) spends no stamina.</summary>
        public static bool FreeStaminaAt(Vector3 point) => Active == WeatherKind.GoldenCrate && WeatherZone.Inside(WeatherKind.GoldenCrate, point);

        internal static float s_rainTraction = .35f;

        public static string Name(WeatherKind kind) => kind switch
        {
            WeatherKind.Wind => "ลมกระโชก",
            WeatherKind.Rain => "ฝนตก พื้นลื่น",
            WeatherKind.GoldenCrate => "เขตลังทองคำ",
            _ => "",
        };

        public static string Hint(WeatherKind kind) => kind switch
        {
            WeatherKind.Wind => "ลมแรงตรงจุดที่มีเส้นลม จับลังให้แน่น",
            WeatherKind.Rain => "เท้าลื่น เดินช้าๆ เลี้ยวกว้างๆ",
            WeatherKind.GoldenCrate => "ในเขตประกายทอง ลังไม่เสียหาย แบกไม่เหนื่อย!",
            _ => "",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Clear()
        {
            Active = Coming = WeatherKind.None;
            ComingIn = TimeLeft = Strength = 0f;
        }
    }
}
