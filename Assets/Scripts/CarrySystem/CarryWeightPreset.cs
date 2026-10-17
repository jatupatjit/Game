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
                    _flashUntil = Time.unscaledTime + 3f;
                }
            }

            private GUIStyle _chip;

            private void OnGUI()
            {
                // Always faintly visible when non-default, bright for a few seconds after switching.
                if (Index == 0 && Time.unscaledTime > _flashUntil) return;
                bool flashing = Time.unscaledTime < _flashUntil;
                if (_chip == null)
                {
                    // Small themed chip: soil panel, chamfered, GameThai, left-aligned single line.
                    _chip = new GUIStyle(UITheme.ImguiLabel)
                    {
                        alignment = TextAnchor.MiddleLeft, wordWrap = false, fontSize = 14,
                        padding = new RectOffset(10, 10, 2, 2),
                        border = new RectOffset(11, 11, 11, 11)
                    };
                    _chip.normal.background = UITheme.ImguiTexture(UITheme.Panel, true);
                }
                Color previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, flashing ? 1f : .7f);
                string value = UITheme.RichHex(flashing ? UITheme.Primary : UITheme.TextSecondary);
                GUI.Label(new Rect(8f, Screen.height - 32f, 300f, 26f), $"Carry weight [F6]: <color={value}>{Name}</color>", _chip);
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
