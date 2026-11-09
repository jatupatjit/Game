using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Playtest switch for how heavy carried cargo feels. Index 0 is the original behaviour.
    /// The value only matters on the process that simulates cargo physics (the host / server).
    /// </summary>
    public static class CarryWeightPreset
    {
        public static readonly string[] Names = { "0 Current", "1 Slightly heavier", "2 Heavier" };
        // Multiplier on the share of cargo weight the hands bear. Multi-carrier gets a bit more.
        private static readonly float[] SoloScale = { 1f, 2f, 3f };
        private static readonly float[] CoopScale = { 1f, 2.4f, 3.6f };

        public static int Index { get; private set; }
        public static string Name => Names[Index];

        public static void Set(int index)
        {
            Index = Mathf.Clamp(index, 0, Names.Length - 1);
            Debug.Log("[CarryWeightPreset] " + Name);
        }

        public static void Cycle() => Set((Index + 1) % Names.Length);

        public static float HandLoadScale(bool multiCarrier) => (multiCarrier ? CoopScale : SoloScale)[Index];

        // F6 was removed (2026-11-08): the preset is now changed only from the dev cheat panel (F2).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetIndex() => Index = 0;
    }
}
