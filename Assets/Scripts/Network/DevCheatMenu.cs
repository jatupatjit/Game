using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame.Network
{
    /// <summary>
    /// Developer cheat panel (F2), Editor and Development builds only. Buttons send requests to the host through
    /// <see cref="PlayerExpeditionState.DevCheatRpc"/>; the host acts and every screen gets the state and a log line.
    /// While open the cursor is free and camera look / mouse grab are ignored, but the player can still walk.
    /// Opens with F2 or the backquote key (` under Esc), because the Editor can swallow F-keys.
    /// </summary>
    public sealed class DevCheatMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        private Vector2 _scroll;
        private GUIStyle _box, _title, _section, _logStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            IsOpen = false;
            if (!DevCheats.Allowed) return;
            var go = new GameObject("DevCheatMenu") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<DevCheatMenu>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.f2Key.wasPressedThisFrame || keyboard.backquoteKey.wasPressedThisFrame)) SetOpen(!IsOpen);
            if (IsOpen && (Cursor.lockState != CursorLockMode.None || !Cursor.visible))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// <summary>Opens or closes the panel (F2; also used by the test probe).</summary>
        public static void SetOpen(bool open)
        {
            if (IsOpen == open) return;
            IsOpen = open;
            if (!open && !PauseMenu.IsPaused)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private static PlayerExpeditionState LocalState()
        {
            var manager = NetworkManager.Singleton;
            var player = manager != null && manager.IsListening && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            return player != null ? player.GetComponent<PlayerExpeditionState>() : null;
        }

        /// <summary>Sends one request to the host (also used by the test probe).</summary>
        public static bool Send(DevCheat cheat, int value = 0)
        {
            var state = LocalState();
            if (state == null || !state.IsSpawned) return false;
            state.DevCheatRpc(cheat, value);
            return true;
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            Styles();
            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var area = new Rect(20, 190, 700, 820);
            GUI.Box(area, GUIContent.none, _box);
            GUILayout.BeginArea(new Rect(area.x + 14, area.y + 10, area.width - 28, area.height - 20));
            GUILayout.Label("DEV CHEATS  (F2 หรือ ` ปิด)", _title);
            if (LocalState() == null) { GUILayout.Label("ยังไม่ได้เชื่อมต่อเกม"); GUILayout.EndArea(); return; }
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("ด่าน", _section);
            if (GUILayout.Button("ข้ามไปด่านถัดไป")) Send(DevCheat.NextLevel);
            GUILayout.BeginHorizontal();
            string[] levels = DevCheats.LevelNames();
            for (int i = 0; i < levels.Length; i++)
                if (GUILayout.Button(levels[i])) Send(DevCheat.LoadLevel, i);
            GUILayout.EndHorizontal();

            var checkpoints = CheckpointManager.Instance;
            if (checkpoints != null && checkpoints.IsSpawned)
            {
                GUILayout.Label("เช็คพอยต์ (ตอนนี้: " + (checkpoints.Current != null ? checkpoints.Current.Number.ToString() : "-") + ")", _section);
                GUILayout.BeginHorizontal();
                for (int i = 1; i <= checkpoints.PadCount; i++)
                    if (GUILayout.Button("ไป " + i)) Send(DevCheat.JumpCheckpoint, i);
                GUILayout.EndHorizontal();
                if (GUILayout.Button("รีเช็คพอยต์ (กลับไปแท่นล่าสุด)")) Send(DevCheat.ResetCheckpoint);
            }

            GUILayout.Label("ลังและผู้เล่น", _section);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("เติม HP ลังเต็ม")) Send(DevCheat.RefillCrate);
            if (GUILayout.Button(DevCheats.GodMode ? "ลังไม่เสียหาย: เปิด" : "ลังไม่เสียหาย: ปิด")) Send(DevCheat.GodMode, DevCheats.GodMode ? 0 : 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(DevCheats.InfiniteStamina ? "Stamina ไม่จำกัด: เปิด" : "Stamina ไม่จำกัด: ปิด"))
                Send(DevCheat.InfiniteStamina, DevCheats.InfiniteStamina ? 0 : 1);
            if (GUILayout.Button("+100 เงิน")) Send(DevCheat.AddCoins, 100);
            if (GUILayout.Button("+1000")) Send(DevCheat.AddCoins, 1000);
            GUILayout.EndHorizontal();

            GUILayout.Label("จำลองน้ำหนัก (ทุกเครื่อง)", _section);
            GUILayout.Label("Preset น้ำหนัก: " + CarrySystem.CarryWeightPreset.Name);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < CarrySystem.CarryWeightPreset.Names.Length; i++)
                if (GUILayout.Button(CarrySystem.CarryWeightPreset.Names[i])) Send(DevCheat.WeightPreset, i);
            GUILayout.EndHorizontal();
            GUILayout.Label("เพิ่มมวลลัง: +" + DevCheats.ExtraMass + " kg");
            GUILayout.BeginHorizontal();
            foreach (int kg in new[] { 0, 10, 25, 50 })
                if (GUILayout.Button(kg == 0 ? "ปกติ" : "+" + kg)) Send(DevCheat.ExtraMass, kg);
            GUILayout.EndHorizontal();
            GUILayout.Label("นับเป็นผู้ยก: " + (DevCheats.SimCarriers == 0 ? "ตามจริง" : DevCheats.SimCarriers + " คน"));
            GUILayout.BeginHorizontal();
            for (int n = 0; n <= 4; n++)
                if (n != 1 && GUILayout.Button(n == 0 ? "ตามจริง" : n + " คน")) Send(DevCheat.SimCarriers, n);
            GUILayout.EndHorizontal();

            GUILayout.Label("ประวัติการใช้", _section);
            for (int i = DevCheats.Log.Count - 1; i >= 0; i--) GUILayout.Label(DevCheats.Log[i], _logStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void Styles()
        {
            if (_box != null) return;
            var dark = new Texture2D(1, 1);
            dark.SetPixel(0, 0, new Color(.08f, .09f, .12f, .92f));
            dark.Apply();
            _box = new GUIStyle(GUI.skin.box) { normal = { background = dark } };
            Font font = UITheme.Font;
            GUI.skin.label.font = GUI.skin.button.font = font;
            GUI.skin.label.fontSize = GUI.skin.button.fontSize = 18;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, .8f, .3f) } };
            _section = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, normal = { textColor = new Color(.5f, .85f, 1f) } };
            _logStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, normal = { textColor = new Color(.85f, .85f, .85f) } };
        }
    }
}
