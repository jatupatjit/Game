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
        public const byte Here = 0, Help = 1, Danger = 2, Wait = 3, ThisWay = 4;
        public static readonly string[] Labels = { "มานี่!", "ช่วยยก!", "ระวัง!", "รอก่อน!", "ไปทางนี้!" };
        public static readonly Color[] Colors = { new Color(.35f, .8f, 1f), new Color(.45f, 1f, .45f), new Color(1f, .4f, .3f), new Color(1f, .85f, .3f), new Color(.8f, .55f, 1f) };
        private const float PinSeconds = 4f, MaxDistance = 3000f, SkyDistance = 60f, Cooldown = .4f; // Ping any distance
        private static readonly string[] IconNames = { "PingHere", "PingHelp", "PingDanger" };
        private static readonly Sprite[] IconCache = new Sprite[3];

        /// <summary>Hand-drawn icon for Here / Help / Danger (Resources/UI/HandDrawn), else null.</summary>
        public static Sprite Icon(int kind)
        {
            if (kind < 0 || kind >= IconNames.Length) return null;
            if (IconCache[kind] == null) IconCache[kind] = Resources.Load<Sprite>("UI/HandDrawn/" + IconNames[kind]);
            return IconCache[kind];
        }
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
            int kind = CoopGame.Network.GameSettings.Pressed(CoopGame.Network.BindRole.PingHere) ? Here : CoopGame.Network.GameSettings.Pressed(CoopGame.Network.BindRole.PingHelp) ? Help : CoopGame.Network.GameSettings.Pressed(CoopGame.Network.BindRole.PingWarn) ? Danger : -1;
            bool wave = CoopGame.Network.GameSettings.Pressed(CoopGame.Network.BindRole.Wave);
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
                ? hit.point : ray.GetPoint(SkyDistance);
            state.PingRpc(kind, point);
            return true;
        }

        /// <summary>Every machine: show a pin at <paramref name="point"/> from <paramref name="from"/>.
        /// Seen from any distance, never hidden behind islands (drawn on a screen overlay at the
        /// spot), and when the spot is off screen or behind the camera an arrow at the screen edge points to it.</summary>
        public static void Show(byte kind, Vector3 point, string from)
        {
            if (kind >= Labels.Length) return;
            ShownCount++;
            var overlay = Overlay();
            var root = new GameObject("Ping " + Labels[kind], typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(overlay, false);
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(240f, 200f);
            rect.pivot = new Vector2(.5f, 0f);

            var marker = new GameObject("Marker", typeof(RectTransform), typeof(Image));
            marker.transform.SetParent(root.transform, false);
            var markerRect = (RectTransform)marker.transform;
            markerRect.anchorMin = markerRect.anchorMax = new Vector2(.5f, 0f);
            var markerImage = marker.GetComponent<Image>();
            markerImage.raycastTarget = false;
            var icon = Icon(kind);
            if (icon != null)
            {
                markerImage.sprite = icon;
                markerImage.preserveAspect = true;
                markerRect.sizeDelta = new Vector2(84f, 84f);
                markerRect.anchoredPosition = new Vector2(0f, 42f);
            }
            else
            {
                markerRect.sizeDelta = new Vector2(34f, 34f);
                markerRect.anchoredPosition = new Vector2(0f, 26f);
                markerRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                markerImage.color = Colors[kind];
            }

            var label = UITheme.MakeText(root.transform, "Label", Labels[kind] + "\n<size=20>" + from + "</size>", 32, UITextRole.Primary, TextAnchor.MiddleCenter);
            label.supportRichText = true;
            label.color = Colors[kind];
            UITheme.AddOutline(label, 2f);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(.5f, 0f);
            labelRect.sizeDelta = new Vector2(240f, 80f);
            labelRect.anchoredPosition = new Vector2(0f, icon != null ? 128f : 84f);

            var distance = UITheme.MakeText(root.transform, "Distance", "", 20, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.AddOutline(distance, 2f);
            var distanceRect = (RectTransform)distance.transform;
            distanceRect.anchorMin = distanceRect.anchorMax = new Vector2(.5f, 0f);
            distanceRect.sizeDelta = new Vector2(200f, 30f);
            distanceRect.anchoredPosition = new Vector2(0f, -6f);

            var arrow = new GameObject("EdgeArrow", typeof(RectTransform), typeof(Image));
            arrow.transform.SetParent(root.transform, false);
            var arrowRect = (RectTransform)arrow.transform;
            arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(.5f, 0f);
            arrowRect.sizeDelta = new Vector2(44f, 44f);
            var arrowImage = arrow.GetComponent<Image>();
            arrowImage.sprite = ArrowSprite();
            arrowImage.color = Colors[kind];
            arrowImage.raycastTarget = false;
            arrow.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, .8f);
            arrow.SetActive(false);

            root.AddComponent<PingPin>().Init(point, PinSeconds, distance, label.gameObject, arrowRect);
            GameplayFeedback.Play(GameplayFeedback.Cue.Click, point);
        }

        private static RectTransform _overlay;

        /// <summary>One screen-space canvas for every pin (under the menus, over the world).</summary>
        private static RectTransform Overlay()
        {
            if (_overlay != null) return _overlay;
            var go = new GameObject("PingOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            _overlay = (RectTransform)go.transform;
            return _overlay;
        }

        private static Sprite _arrow;

        /// <summary>A small filled triangle pointing up (+Y).</summary>
        private static Sprite ArrowSprite()
        {
            if (_arrow != null) return _arrow;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float half = (1f - y / (float)(size - 1)) * (size * .5f - 2f); // wide at the bottom, point at the top
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Abs(x - (size - 1) * .5f);
                    float a = Mathf.Clamp01(half - d + .5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            _arrow = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
            return _arrow;
        }

        /// <summary>Places the pin on the screen over its world spot every frame; clamps it to the screen edge with an
        /// arrow when the spot is off screen or behind the camera; bobs and fades out.</summary>
        private sealed class PingPin : MonoBehaviour
        {
            private const float EdgeMargin = 70f;
            private float _end, _start;
            private Vector3 _point;
            private CanvasGroup _group;
            private Text _distance;
            private GameObject _label;
            private RectTransform _arrow, _rect;

            public void Init(Vector3 point, float seconds, Text distance, GameObject label, RectTransform arrow)
            {
                _point = point;
                _start = Time.time;
                _end = Time.time + seconds;
                _group = GetComponent<CanvasGroup>();
                _group.blocksRaycasts = false;
                _group.interactable = false;
                _distance = distance;
                _label = label;
                _arrow = arrow;
                _rect = (RectTransform)transform;
                Place();
            }

            private void LateUpdate()
            {
                if (Time.time >= _end) { Destroy(gameObject); return; }
                Place();
                _group.alpha = Mathf.Clamp01((_end - Time.time) / .6f);
            }

            private void Place()
            {
                var view = Camera.main;
                var canvas = _rect.parent as RectTransform;
                if (view == null || canvas == null) { _group.alpha = 0f; return; }
                Vector3 world = _point + Vector3.up * (.3f + .1f * Mathf.Sin((Time.time - _start) * 4f));
                Vector3 screen = view.WorldToScreenPoint(world);
                bool behind = screen.z < 0f;
                if (behind) { screen.x = Screen.width - screen.x; screen.y = Screen.height - screen.y; }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out Vector2 local);
                Vector2 half = canvas.rect.size * .5f - new Vector2(EdgeMargin, EdgeMargin);
                bool off = behind || Mathf.Abs(local.x) > half.x || local.y > half.y - 60f || local.y < -half.y;
                if (off)
                {
                    // Push the point out along its direction from the screen centre, then clamp it to the edge box.
                    Vector2 dir = local.sqrMagnitude > 1f ? local.normalized : Vector2.down;
                    if (behind && dir.y > 0f) dir.y = -dir.y; // behind you: arrow along the bottom/sides, not the top
                    float scale = Mathf.Min(half.x / Mathf.Max(Mathf.Abs(dir.x), 1e-4f), (half.y - 60f) / Mathf.Max(Mathf.Abs(dir.y), 1e-4f));
                    local = dir * scale;
                    _arrow.anchoredPosition = dir * 52f + new Vector2(0f, 60f);
                    _arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
                    local.y -= 60f; // the pin's pivot is its bottom; keep the icon itself on the edge
                }
                if (_arrow.gameObject.activeSelf != off) _arrow.gameObject.SetActive(off);
                if (_label.activeSelf == off) _label.SetActive(!off);
                _rect.anchoredPosition = local;
                _rect.localScale = Vector3.one * (off ? .85f : 1f);
                float metres = Vector3.Distance(view.transform.position, _point);
                string text = Mathf.RoundToInt(metres) + " ม.";
                if (_distance.text != text) _distance.text = text;
            }
        }
    }
}
