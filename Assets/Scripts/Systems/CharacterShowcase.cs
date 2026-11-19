using CoopGame.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoopGame.Systems
{
    /// <summary>
    /// Showcase scene helper (Assets/Scenes/Test/CharacterShowcase): shows off the character's fall-respawn
    /// get-up on demand, for presentations and report clips. Lives only in that scene.
    /// Keys: 1 = drop and get up (dust, thud, dizzy stars, 5-pose get-up), 2 = drop and walk off halfway
    /// (skip by moving), 3 = repeat 1 every few seconds, 4 = drop in the
    /// middle of the trap ring (traps leave a player alone while lying and getting up). A panel shows the live state.
    /// Everything it triggers is the normal game code, so other players see the same.
    /// </summary>
    public sealed class CharacterShowcase : MonoBehaviour
    {
        [SerializeField, Min(3f)] private float _loopSeconds = 8f;
        [Tooltip("Where key 4 drops the player: the middle of the trap ring.")]
        [SerializeField] private Transform _trapSpot;

        private RagdollCharacter _player;
        private PlayerInputReader _input;
        private System.Reflection.PropertyInfo _moveProperty;
        private Text _status;
        private bool _loop, _autoWalk;
        private float _nextLoop, _walkFrom, _walkUntil;

        private void Start()
        {
            _moveProperty = typeof(PlayerInputReader).GetProperty(nameof(PlayerInputReader.MoveInput));
            BuildPanel();
        }

        private void Update()
        {
            if (_player == null && !FindLocalPlayer()) { SetStatus("รอตัวละครเกิด..."); return; }
            var keys = Keyboard.current;
            if (keys != null)
            {
                if (keys.digit1Key.wasPressedThisFrame) Drop(false);
                if (keys.digit2Key.wasPressedThisFrame) Drop(true);
                if (keys.digit3Key.wasPressedThisFrame) { _loop = !_loop; _nextLoop = Time.time; }
                if (keys.digit4Key.wasPressedThisFrame) DropInTrapRing();
            }
            if (_loop && Time.time >= _nextLoop) { Drop(false); _nextLoop = Time.time + _loopSeconds; }

            string state = _player.GettingUp ? "กำลังลุก" : _player.IsKnockedDown ? "นอนมึน" : "ยืน";
            SetStatus("สถานะ: " + state
                + "\nกันกับดัก: " + (_player.TrapImmune ? "<color=#7EDC95>เปิด</color>" : "ปิด")
                + "\nเล่นวน (3): " + (_loop ? "<color=#7EDC95>เปิด</color>" : "ปิด"));
        }

        // Runs after PlayerInputReader.Update, so the next physics step sees the walk input.
        private void LateUpdate()
        {
            if (!_autoWalk || _input == null || _moveProperty == null) return;
            if (Time.time >= _walkUntil) { _autoWalk = false; _moveProperty.SetValue(_input, Vector2.zero); return; }
            if (Time.time >= _walkFrom) _moveProperty.SetValue(_input, new Vector2(0f, 1f));
        }

        private void Drop(bool walkOff)
        {
            if (_player == null) return;
            _player.RespawnSlump();
            _autoWalk = walkOff;
            // The get-up starts ~1.6 s after the drop; half of it is done ~0.9 s later.
            _walkFrom = Time.time + 2.7f;
            _walkUntil = _walkFrom + 1.4f;
        }

        // Puts the player in the middle of the trap ring and drops them there in the same frame, so the
        // trap only ever meets a player who is lying or getting up (and leaves them alone until 1 s after).
        private void DropInTrapRing()
        {
            if (_player == null || _trapSpot == null) return;
            Vector3 position = _trapSpot.position + Vector3.up * 1.05f;
            Quaternion rotation = Quaternion.LookRotation(Vector3.forward);
            var controller = _player.GetComponent<CharacterController>();
            bool wasOn = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;
            _player.transform.SetPositionAndRotation(position, rotation);
            _player.GetComponent<Unity.Netcode.Components.NetworkTransform>()?.Teleport(position, rotation, _player.transform.localScale);
            if (controller != null) controller.enabled = wasOn;
            Drop(false);
        }

        private bool FindLocalPlayer()
        {
            foreach (var ragdoll in FindObjectsByType<RagdollCharacter>(FindObjectsInactive.Exclude))
            {
                if (!ragdoll.IsOwner) continue;
                _player = ragdoll;
                _input = ragdoll.GetComponent<PlayerInputReader>() ?? ragdoll.GetComponentInChildren<PlayerInputReader>();
                return true;
            }
            return false;
        }

        private void BuildPanel()
        {
            var canvasGo = new GameObject("ShowcaseCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;

            Image panel = UITheme.MakePanel(canvasGo.transform, "ShowcasePanel", new Vector2(600f, 340f));
            var rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -24f);

            Text title = UITheme.MakeText(panel.transform, "Title", "โชว์ตัวละคร: ตกแมพแล้วลุก", 28, UITextRole.Primary, TextAnchor.UpperLeft);
            Place(title.rectTransform, 22f, 18f, 46f);
            Text keys = UITheme.MakeText(panel.transform, "Keys",
                "[1]  ตกลงพื้น: ฝุ่น ตุ้บ ดาวมึน แล้วลุก 5 ท่า\n" +
                "[2]  ตกแล้วเดินออกตอนลุกได้ครึ่งทาง\n" +
                "[3]  เล่นท่า 1 วนไปเรื่อยๆ\n" +
                "[4]  ตกกลางวงกับดัก: กับดักไม่ตีตอนนอน/ลุก",
                21, UITextRole.Secondary, TextAnchor.UpperLeft);
            Place(keys.rectTransform, 22f, 70f, 140f);
            _status = UITheme.MakeText(panel.transform, "Status", "", 22, UITextRole.Primary, TextAnchor.UpperLeft);
            _status.supportRichText = true;
            Place(_status.rectTransform, 22f, 230f, 100f);
        }

        private static void Place(RectTransform rect, float left, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-left, -top);
        }

        private void SetStatus(string text)
        {
            if (_status != null && _status.text != text) _status.text = text;
        }
    }
}
