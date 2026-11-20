using CoopGame.CarrySystem;
using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// George's hand-drawn hand icons on the wall spot each hand would climb (local player only, visual only).
    /// Open hand = that hand can grab there (<see cref="Wallclimb.TryGetGrabPreview"/>); fist = that hand holds the wall
    /// there (<see cref="Wallclimb.TryGetGripPoint"/>, left/right click). Nothing is shown when no wall is in reach.
    /// Replaces the small white sphere marker of <see cref="GameplayHighlights"/>.
    /// Sprites: Resources/UI/HandDrawn/Hand{Open,Fist}{Left,Right}.
    /// </summary>
    public sealed class HandGripHUD : MonoBehaviour
    {
        // Icon about the size of a real hand on the wall (WorldSize m), kept readable near and far.
        private const float IconSize = 40f, WorldSize = .2f, MinSize = 24f, MaxSize = 48f;

        /// <summary>True while the hand sprites are loaded, so the sphere grab markers stay hidden.</summary>
        public static bool ShowsGrabSpots { get; private set; }
        /// <summary>For tests: what each hand icon shows now (0 hidden, 1 open, 2 fist).</summary>
        public static int LeftState { get; private set; }
        public static int RightState { get; private set; }

        private Sprite _openL, _openR, _fistL, _fistR;
        private RectTransform _canvasRect;
        private Image _left, _right;
        private float _leftPop, _rightPop;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            LeftState = RightState = 0;
            var go = new GameObject("HandGripHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<HandGripHUD>().Build();
        }

        private void Build()
        {
            _openL = Resources.Load<Sprite>("UI/HandDrawn/HandOpenLeft");
            _openR = Resources.Load<Sprite>("UI/HandDrawn/HandOpenRight");
            _fistL = Resources.Load<Sprite>("UI/HandDrawn/HandFistLeft");
            _fistR = Resources.Load<Sprite>("UI/HandDrawn/HandFistRight");
            ShowsGrabSpots = _openL != null && _openR != null && _fistL != null && _fistR != null;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            _canvasRect = (RectTransform)transform;
            _left = MakeHand("LeftHand", _openL);
            _right = MakeHand("RightHand", _openR);
        }

        private Image MakeHand(string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(IconSize, IconSize);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            go.SetActive(false);
            return image;
        }

        private static bool Hidden() => PauseMenu.IsPaused || PhotoMode.IsOn || TitleScreen.IsVisible || MissionFailUI.IsVisible ||
            LevelMap.IsOpen || UIPanels.ShopOpen || LobbyCharacterCustomizationUI.IsVisible || SpectateMode.IsOn || ExpeditionHUD.BlocksGameplayInput;

        private void LateUpdate()
        {
            var manager = NetworkManager.Singleton;
            var player = manager != null && manager.IsListening && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            var climb = player != null && ShowsGrabSpots && !Hidden() ? player.GetComponent<Wallclimb>() : null;
            var view = Camera.main;
            LeftState = Place(_left, climb, view, true, _openL, _fistL, ref _leftPop, LeftState);
            RightState = Place(_right, climb, view, false, _openR, _fistR, ref _rightPop, RightState);
        }

        private int Place(Image image, Wallclimb climb, Camera view, bool left, Sprite open, Sprite fist, ref float pop, int previous)
        {
            int state = 0;
            Vector3 point = default;
            if (climb != null && view != null)
            {
                if (climb.TryGetGripPoint(left, out point, out _)) state = 2;
                else if (climb.TryGetGrabPreview(left, out point, out _)) state = 1;
            }
            Vector3 screen = state != 0 ? view.WorldToScreenPoint(point) : default;
            if (state != 0 && screen.z <= .05f) state = 0;
            if (image.gameObject.activeSelf != (state != 0)) image.gameObject.SetActive(state != 0);
            if (state == 0) return 0;

            if (state != previous) pop = 1f;
            pop = Mathf.MoveTowards(pop, 0f, Time.unscaledDeltaTime * 6f);
            image.sprite = state == 2 ? fist : open;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out Vector2 local);
            image.rectTransform.anchoredPosition = local + _canvasRect.rect.size * .5f;
            float pixels = view.orthographic ? IconSize :
                WorldSize / (2f * screen.z * Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad)) * _canvasRect.rect.height;
            float size = Mathf.Clamp(pixels, MinSize, MaxSize);
            image.rectTransform.sizeDelta = new Vector2(size, size);
            image.rectTransform.localScale = Vector3.one * ((state == 2 ? 1f : .9f) + pop * .2f);
            image.color = new Color(1f, 1f, 1f, state == 2 ? 1f : .85f);
            return state;
        }
    }
}
