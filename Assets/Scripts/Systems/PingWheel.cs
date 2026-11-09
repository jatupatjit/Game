using CoopGame.CarrySystem;
using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Middle mouse ping (like a hero-shooter ping): tap = smart ping where you look ("ช่วยยก!" on the crate,
    /// "มานี่!" elsewhere); hold = a wheel, move the mouse toward a slice and release to choose (pins and emotes).
    /// Z / X / T / G keep working. While the wheel is open the player stands still (<see cref="SpectateMode.BlocksInput"/>).
    /// </summary>
    public sealed class PingWheel : MonoBehaviour
    {
        private const float HoldSeconds = .22f, SelectRadius = 40f;
        private static readonly string[] Slices = { "มานี่!", "ช่วยยก!", "ระวัง!", "รอก่อน!", "ไปทางนี้!", "โบกมือ", "เชียร์" };

        public static bool IsOpen { get; private set; }

        private float _pressedAt = -1f;
        private Vector2 _aim;
        private int _selected = -1;
        private GameObject _wheel;
        private Image[] _sliceImages;
        private Text _center;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            IsOpen = false;
            var go = new GameObject("PingWheel");
            DontDestroyOnLoad(go);
            go.AddComponent<PingWheel>().Build();
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 260;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            _wheel = new GameObject("Wheel", typeof(RectTransform));
            _wheel.transform.SetParent(transform, false);
            var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            ring.transform.SetParent(_wheel.transform, false);
            ((RectTransform)ring.transform).sizeDelta = new Vector2(150f, 150f);
            ring.GetComponent<Image>().color = new Color(0f, 0f, 0f, .55f);
            _center = UITheme.MakeText(_wheel.transform, "Center", "", 26, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.AddOutline(_center);
            ((RectTransform)_center.transform).sizeDelta = new Vector2(220f, 60f);
            _sliceImages = new Image[Slices.Length];
            for (int i = 0; i < Slices.Length; i++)
            {
                float angle = 90f - i * 360f / Slices.Length;
                Vector2 at = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * 250f;
                var slice = new GameObject("Slice" + i, typeof(RectTransform), typeof(Image));
                slice.transform.SetParent(_wheel.transform, false);
                var rect = (RectTransform)slice.transform;
                rect.sizeDelta = new Vector2(170f, 64f);
                rect.anchoredPosition = at;
                _sliceImages[i] = slice.GetComponent<Image>();
                var label = UITheme.MakeText(rect, "Label", Slices[i], 26, UITextRole.Primary, TextAnchor.MiddleCenter);
                UITheme.AddOutline(label);
                ((RectTransform)label.transform).sizeDelta = rect.sizeDelta;
                if (i < PlayerPings.Labels.Length) label.color = PlayerPings.Colors[i];
            }
            _wheel.SetActive(false);
        }

        private static bool Blocked() => PauseMenu.IsPaused || DevCheatMenu.IsOpen || PhotoMode.IsOn || TitleScreen.IsVisible ||
            ExpeditionHUD.BlocksGameplayInput || MissionFailUI.IsVisible || LevelMap.IsOpen || Cosmetics.IsOpen ||
            LobbyCharacterCustomizationUI.IsVisible || SpectateMode.IsOn;

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (Blocked()) { Close(false); _pressedAt = -1f; return; }
            if (mouse.middleButton.wasPressedThisFrame) { _pressedAt = Time.unscaledTime; _aim = Vector2.zero; _selected = -1; }
            if (_pressedAt < 0f) return;
            if (mouse.middleButton.isPressed)
            {
                if (!IsOpen && Time.unscaledTime - _pressedAt >= HoldSeconds) { IsOpen = true; _wheel.SetActive(true); }
                if (IsOpen) Steer(mouse.delta.ReadValue());
                return;
            }
            // released
            if (IsOpen) Close(true);
            else SmartPing();
            _pressedAt = -1f;
        }

        private void Steer(Vector2 delta)
        {
            _aim = Vector2.ClampMagnitude(_aim + delta, 200f);
            _selected = -1;
            if (_aim.magnitude >= SelectRadius)
            {
                float angle = Mathf.Atan2(_aim.y, _aim.x) * Mathf.Rad2Deg;
                float fromTop = Mathf.Repeat(90f - angle, 360f);
                float step = 360f / Slices.Length;
                _selected = Mathf.RoundToInt(fromTop / step) % Slices.Length;
            }
            for (int i = 0; i < _sliceImages.Length; i++)
                _sliceImages[i].color = i == _selected ? new Color(.99f, .4f, .53f, .9f) : new Color(.1f, .1f, .14f, .75f);
            _center.text = _selected >= 0 ? Slices[_selected] : "เลื่อนเมาส์เลือก";
        }

        private void Close(bool execute)
        {
            if (!IsOpen) return;
            IsOpen = false;
            _wheel.SetActive(false);
            if (!execute || _selected < 0) return;
            if (_selected < PlayerPings.Labels.Length) PlayerPings.Ping((byte)_selected);
            else
            {
                var ragdoll = LocalRagdoll();
                if (ragdoll != null) ragdoll.PlayEmote(_selected == 5 ? RagdollCharacter.EmoteWave : RagdollCharacter.EmoteCheer, 2.2f);
            }
        }

        private static RagdollCharacter LocalRagdoll()
        {
            var manager = NetworkManager.Singleton;
            var player = manager != null && manager.IsListening && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            return player != null ? player.GetComponent<RagdollCharacter>() : null;
        }

        /// <summary>Tap: "ช่วยยก!" when looking at a crate, otherwise "มานี่!".</summary>
        private static void SmartPing()
        {
            var view = Camera.main;
            byte kind = PlayerPings.Here;
            if (view != null)
            {
                int mask = Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("PlayerRagdoll"));
                if (Physics.Raycast(view.transform.position, view.transform.forward, out RaycastHit hit, 80f, mask, QueryTriggerInteraction.Ignore) &&
                    hit.collider.GetComponentInParent<CarryableObject>() != null)
                    kind = PlayerPings.Help;
            }
            PlayerPings.Ping(kind);
        }
    }
}
