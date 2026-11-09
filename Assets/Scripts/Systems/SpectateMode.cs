using System.Collections.Generic;
using CoopGame.CarrySystem;
using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Spectate (O, any time; C is the Lobby outfit screen): the camera follows a teammate instead of you; left / right click switches player,
    /// the mouse orbits, O again comes back. Your own character stands still while you watch (gameplay input is
    /// blocked through <see cref="BlocksInput"/>). Runs after the player camera so it simply takes over the view.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class SpectateMode : MonoBehaviour
    {
        public static bool IsOn { get; private set; }
        /// <summary>Gameplay input is blocked (spectating, or the ping wheel is open).</summary>
        public static bool BlocksInput => IsOn || PingWheel.IsOpen;

        private int _index;
        private float _yaw, _pitch = 15f, _distance = 6f;
        private Vector3 _smoothTarget;
        private GameObject _hud;
        private Text _label;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            IsOn = false;
            var go = new GameObject("SpectateMode");
            DontDestroyOnLoad(go);
            go.AddComponent<SpectateMode>().Build();
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 150;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            _hud = new GameObject("SpectateHud", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)_hud.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
            rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 150f);
            rect.sizeDelta = new Vector2(820f, 70f);
            _hud.GetComponent<Image>().color = new Color(.08f, .07f, .12f, .75f);
            _label = UITheme.MakeText(rect, "Label", "", 26, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.AddOutline(_label);
            ((RectTransform)_label.transform).sizeDelta = rect.sizeDelta;
            _hud.SetActive(false);
        }

        private static List<PlayerExpeditionState> Others()
        {
            var list = new List<PlayerExpeditionState>();
            foreach (var p in LevelResults.Players()) if (!p.IsOwner) list.Add(p);
            return list;
        }

        private static bool Blocked() => PauseMenu.IsPaused || TitleScreen.IsVisible || PhotoMode.IsOn || DevCheatMenu.IsOpen ||
            ExpeditionHUD.BlocksGameplayInput || MissionFailUI.IsVisible || LevelMap.IsOpen || Cosmetics.IsOpen ||
            LobbyCharacterCustomizationUI.IsVisible || PingWheel.IsOpen;

        private void Update()
        {
            var keyboard = Keyboard.current;
            var manager = NetworkManager.Singleton;
            bool inSession = manager != null && manager.IsListening && manager.LocalClient != null && manager.LocalClient.PlayerObject != null;
            if (IsOn && (!inSession || PhotoMode.IsOn || MissionFailUI.IsVisible)) SetOn(false);
            if (keyboard == null || !inSession) return;
            if (!IsOn && Blocked()) return;
            if (keyboard.oKey.wasPressedThisFrame && !PauseMenu.IsPaused) SetOn(!IsOn);
            if (!IsOn) return;
            var others = Others();
            if (others.Count == 0) { _label.text = "ยังไม่มีเพื่อนให้ดู • O ออก"; return; }
            var mouse = Mouse.current;
            if (mouse != null && !PauseMenu.IsPaused)
            {
                if (mouse.leftButton.wasPressedThisFrame) _index++;
                if (mouse.rightButton.wasPressedThisFrame) _index--;
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * .15f;
                _pitch = Mathf.Clamp(_pitch - delta.y * .15f, -20f, 70f);
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > .01f) _distance = Mathf.Clamp(_distance - Mathf.Sign(scroll) * .6f, 2.5f, 14f);
            }
            _index = (int)Mathf.Repeat(_index, others.Count);
            var target = others[_index];
            var carrying = FindTargetCargo(target);
            _label.text = $"กำลังดู: {LevelResults.NameOf(target)}{(carrying ? "  (กำลังยกลัง)" : "")}   •   คลิกซ้าย/ขวา เปลี่ยนคน   •   O ออก";
        }

        private static bool FindTargetCargo(PlayerExpeditionState target)
        {
            var mission = FindAnyObjectByType<LevelMission>();
            return mission != null && mission.Cargo != null && mission.Cargo.HasCarrier(target.OwnerClientId);
        }

        private void SetOn(bool on)
        {
            if (IsOn == on) return;
            IsOn = on;
            _hud.SetActive(on);
            if (on)
            {
                var view = Camera.main;
                if (view != null) { _yaw = view.transform.eulerAngles.y; _smoothTarget = view.transform.position; }
            }
        }

        private void LateUpdate()
        {
            if (!IsOn) return;
            var others = Others();
            var view = Camera.main;
            if (others.Count == 0 || view == null) return;
            var target = others[(int)Mathf.Repeat(_index, others.Count)];
            Vector3 focus = target.TryGetComponent(out RagdollCharacter ragdoll) ? ragdoll.HipsPosition : target.transform.position;
            focus += Vector3.up * .8f;
            _smoothTarget = Vector3.Lerp(_smoothTarget, focus, 1f - Mathf.Exp(-Time.deltaTime * 10f));
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 back = rotation * Vector3.back;
            float distance = _distance;
            int mask = Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("PlayerRagdoll"));
            if (Physics.SphereCast(_smoothTarget, .25f, back, out RaycastHit hit, _distance, mask, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(1f, hit.distance - .1f);
            view.transform.SetPositionAndRotation(_smoothTarget + back * distance, rotation);
        }
    }
}
