using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame.Network
{
    /// <summary>
    /// Photo mode for report and promo shots (P): every UI canvas hides, the camera flies freely (WASD, mouse,
    /// Space / Ctrl up and down, Shift faster) while the game keeps running, F12 saves a picture to the Photos folder
    /// next to the game (Editor: the project folder). P again returns to the normal camera and UI. Local only.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after the camera rig (PlayerCameraController, 60)
    public sealed class PhotoMode : MonoBehaviour
    {
        public static bool IsOn { get; private set; }
        public static string LastPhoto { get; private set; }

        private readonly List<Canvas> _hidden = new();
        private Vector3 _position;
        private float _yaw, _pitch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            IsOn = false;
            var go = new GameObject("PhotoMode") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PhotoMode>();
        }

        private static PhotoMode Instance;

        /// <summary>Turns photo mode on or off (P; also the test probe).</summary>
        public static void Set(bool on) { var mode = Instance; if (mode != null) mode.Toggle(on); }

        private void Toggle(bool on)
        {
            var view = Camera.main;
            if (IsOn == on || (on && view == null)) return;
            IsOn = on;
            if (on)
            {
                DevCheatMenu.SetOpen(false);
                _position = view.transform.position;
                Vector3 euler = view.transform.eulerAngles;
                _yaw = euler.y;
                _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
                _hidden.Clear();
                foreach (var canvas in FindObjectsByType<Canvas>())
                    if (canvas.isRootCanvas && canvas.enabled && canvas.renderMode != RenderMode.WorldSpace) { canvas.enabled = false; _hidden.Add(canvas); }
            }
            else
            {
                foreach (var canvas in _hidden) if (canvas != null) canvas.enabled = true;
                _hidden.Clear();
            }
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.pKey.wasPressedThisFrame && !PauseMenu.IsPaused && !TitleScreen.IsVisible) Toggle(!IsOn);
            if (IsOn && keyboard.f12Key.wasPressedThisFrame) Capture();
        }

        /// <summary>Saves the current frame (no UI) as Photos/photo_yyyyMMdd_HHmmss.png.</summary>
        public static string Capture()
        {
            string folder = System.IO.Path.Combine(Application.isEditor ? System.IO.Directory.GetCurrentDirectory()
                : System.IO.Path.GetDirectoryName(Application.dataPath), "Photos");
            System.IO.Directory.CreateDirectory(folder);
            LastPhoto = System.IO.Path.Combine(folder, "photo_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            ScreenCapture.CaptureScreenshot(LastPhoto);
            return LastPhoto;
        }

        private void LateUpdate()
        {
            if (!IsOn) return;
            var view = Camera.main;
            if (view == null) { Toggle(false); return; }
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            float dt = Time.unscaledDeltaTime;
            if (mouse != null)
            {
                Vector2 look = mouse.delta.ReadValue() * .12f;
                _yaw += look.x;
                _pitch = Mathf.Clamp(_pitch - look.y, -85f, 85f);
            }
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            if (keyboard != null)
            {
                Vector3 move = Vector3.zero;
                if (keyboard.wKey.isPressed) move += Vector3.forward;
                if (keyboard.sKey.isPressed) move += Vector3.back;
                if (keyboard.aKey.isPressed) move += Vector3.left;
                if (keyboard.dKey.isPressed) move += Vector3.right;
                Vector3 vertical = (keyboard.spaceKey.isPressed ? Vector3.up : Vector3.zero) + (keyboard.leftCtrlKey.isPressed ? Vector3.down : Vector3.zero);
                float speed = keyboard.leftShiftKey.isPressed ? 14f : 4f;
                _position += (rotation * move + vertical) * (speed * dt);
            }
            view.transform.SetPositionAndRotation(_position, rotation);
        }
    }
}
