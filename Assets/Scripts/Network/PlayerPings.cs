using CoopGame.CarrySystem;
using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Co-op pings and emotes. Z / X / T drop a "มานี่!" / "ช่วยยก!" / "ระวัง!" pin where the crosshair points; every
    /// screen shows it for a few seconds (sent through <see cref="PlayerExpeditionState.PingRpc"/>). G waves; everyone
    /// cheers when the crate is delivered (<see cref="LevelResults.Ready"/>). Emotes are ragdoll arm poses
    /// (<see cref="RagdollCharacter.PlayEmote"/>), so others see them through the normal pose stream.
    /// </summary>
    public sealed class PlayerPings : MonoBehaviour
    {
        public const byte Here = 0, Help = 1, Danger = 2;
        private static readonly string[] Labels = { "มานี่!", "ช่วยยก!", "ระวัง!" };
        private static readonly Color[] Colors = { new Color(.35f, .8f, 1f), new Color(.45f, 1f, .45f), new Color(1f, .4f, .3f) };
        private const float PinSeconds = 4f, MaxDistance = 80f, Cooldown = .4f;
        private float _nextPing;

        /// <summary>For tests: pins shown on this machine so far.</summary>
        public static int ShownCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            ShownCount = 0;
            var go = new GameObject("PlayerPings") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<PlayerPings>();
        }

        private void OnEnable() => LevelResults.Ready += Cheer;
        private void OnDisable() => LevelResults.Ready -= Cheer;

        private static RagdollCharacter LocalRagdoll(out PlayerExpeditionState state)
        {
            state = null;
            var manager = NetworkManager.Singleton;
            var player = manager != null && manager.IsListening && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            if (player == null) return null;
            state = player.GetComponent<PlayerExpeditionState>();
            return player.GetComponent<RagdollCharacter>();
        }

        private static void Cheer()
        {
            var ragdoll = LocalRagdoll(out _);
            if (ragdoll != null) ragdoll.PlayEmote(RagdollCharacter.EmoteCheer, 2.5f);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || PauseMenu.IsPaused || DevCheatMenu.IsOpen || PhotoMode.IsOn || TitleScreen.IsVisible || ExpeditionHUD.BlocksGameplayInput || MissionFailUI.IsVisible) return;
            int kind = keyboard.zKey.wasPressedThisFrame ? Here : keyboard.xKey.wasPressedThisFrame ? Help : keyboard.tKey.wasPressedThisFrame ? Danger : -1;
            bool wave = keyboard.gKey.wasPressedThisFrame;
            if (kind < 0 && !wave) return;
            var ragdoll = LocalRagdoll(out var state);
            if (wave && ragdoll != null) ragdoll.PlayEmote(RagdollCharacter.EmoteWave, 2f);
            if (kind >= 0) Send((byte)kind, state);
        }

        /// <summary>Drops a pin of <paramref name="kind"/> where the camera looks (also used by the test probe).</summary>
        public static bool Ping(byte kind)
        {
            LocalRagdoll(out var state);
            return Send(kind, state);
        }

        private static float _lastSent;

        private static bool Send(byte kind, PlayerExpeditionState state)
        {
            var view = Camera.main;
            if (state == null || !state.IsSpawned || view == null || Time.unscaledTime < _lastSent + Cooldown) return false;
            _lastSent = Time.unscaledTime;
            var ray = new Ray(view.transform.position, view.transform.forward);
            int mask = Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("PlayerRagdoll"));
            Vector3 point = Physics.Raycast(ray, out RaycastHit hit, MaxDistance, mask, QueryTriggerInteraction.Ignore)
                ? hit.point : ray.GetPoint(20f);
            state.PingRpc(kind, point);
            return true;
        }

        /// <summary>Every machine: show a pin at <paramref name="point"/> from <paramref name="from"/>.</summary>
        public static void Show(byte kind, Vector3 point, string from)
        {
            if (kind > Danger) return;
            ShownCount++;
            var root = new GameObject("Ping " + Labels[kind]);
            root.transform.position = point;
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 50;
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(320f, 160f);
            rect.pivot = new Vector2(.5f, 0f);
            var marker = new GameObject("Marker", typeof(RectTransform), typeof(Image));
            marker.transform.SetParent(root.transform, false);
            var markerRect = (RectTransform)marker.transform;
            markerRect.sizeDelta = new Vector2(46f, 46f);
            markerRect.anchoredPosition = new Vector2(0f, -55f);
            markerRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            marker.GetComponent<Image>().color = Colors[kind];
            var label = UITheme.MakeText(root.transform, "Label", Labels[kind] + "\n<size=26>" + from + "</size>", 44, UITextRole.Primary, TextAnchor.MiddleCenter);
            label.supportRichText = true;
            label.color = Colors[kind];
            UITheme.AddOutline(label);
            var labelRect = (RectTransform)label.transform;
            labelRect.sizeDelta = new Vector2(320f, 110f);
            labelRect.anchoredPosition = new Vector2(0f, 20f);
            root.AddComponent<PingPin>().Init(PinSeconds);
            GameplayFeedback.Play(GameplayFeedback.Cue.Click, point);
        }

        /// <summary>Faces the camera, keeps a readable size at any distance, bobs and fades out.</summary>
        private sealed class PingPin : MonoBehaviour
        {
            private float _end, _start;
            private Vector3 _base;
            private CanvasGroup _group;

            public void Init(float seconds)
            {
                _start = Time.time;
                _end = Time.time + seconds;
                _base = transform.position;
                _group = gameObject.AddComponent<CanvasGroup>();
            }

            private void LateUpdate()
            {
                if (Time.time >= _end) { Destroy(gameObject); return; }
                var view = Camera.main;
                if (view == null) return;
                float distance = Vector3.Distance(view.transform.position, _base);
                transform.localScale = Vector3.one * Mathf.Clamp(distance * .0016f, .004f, .06f);
                transform.position = _base + Vector3.up * (.4f + .1f * Mathf.Sin((Time.time - _start) * 4f));
                transform.rotation = view.transform.rotation;
                _group.alpha = Mathf.Clamp01((_end - Time.time) / .6f);
            }
        }
    }
}
