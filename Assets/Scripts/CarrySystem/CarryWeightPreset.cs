using UnityEngine;
using UnityEngine.InputSystem;

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

        private sealed class Driver : MonoBehaviour
        {
            private float _flashUntil;

            private void Update()
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.f6Key.wasPressedThisFrame)
                {
                    Cycle();
                    _flashUntil = Time.unscaledTime + 1.25f;
                }
            }

            private GUIStyle _chip;

            private void OnGUI()
            {
                // This is a playtest-only confirmation, so it disappears quickly for every preset.
                if (Time.unscaledTime > _flashUntil) return;
                if (_chip == null)
                {
                    // Small themed chip: soil panel, chamfered, GameThai, left-aligned single line.
                    _chip = new GUIStyle(UITheme.ImguiLabel)
                    {
                        alignment = TextAnchor.MiddleLeft, wordWrap = false, fontSize = 14,
                        padding = new RectOffset(16, 16, 4, 4),
                        border = UITheme.ImguiPanelBorder
                    };
                    _chip.normal.background = UITheme.ImguiPanelTexture;
                }
                Color previous = GUI.color;
                GUI.color = Color.white;
                string value = UITheme.RichHex(UITheme.Primary);
                GUI.Label(new Rect(Screen.width * 0.5f - 165f, 8f, 330f, 48f), $"Carry weight [F6]: <color={value}>{Name}</color>", _chip);
                GUI.color = previous;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateDriver()
        {
            Index = 0;
            if (!Debug.isDebugBuild) return; // editor and development builds only
            var go = new GameObject("CarryWeightPresetDriver") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Driver>();
        }
    }
}
