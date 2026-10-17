using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>Local presentation of replicated bridge state; it never authorizes deployment.</summary>
    [DisallowMultipleComponent]
    public sealed class RelayBridgeHUD : MonoBehaviour
    {
        private CoopRelayBridge _bridge;
        private GameObject _panel;
        private Text _status;
        private Image _fill;
        private Transform _player;
        private float _refreshAt;
        private int _display = -1;
        private void Awake()
        {
            _bridge = GetComponent<CoopRelayBridge>();
            var canvas = new GameObject("Relay progress HUD", typeof(Canvas), typeof(CanvasScaler));
            canvas.transform.SetParent(transform, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<Canvas>().sortingOrder = 205;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            _panel = new GameObject("Progress", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(canvas.transform, false);
            var rect = _panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0, -30);
            rect.sizeDelta = new Vector2(520, 115);
            var background = _panel.GetComponent<Image>();
            UITheme.StylePanel(background); background.raycastTarget = false;
            var label = new GameObject("Status", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(_panel.transform, false);
            var textRect = label.GetComponent<RectTransform>();
            textRect.sizeDelta = new Vector2(490, 65); textRect.anchoredPosition = new Vector2(0, 15);
            _status = label.GetComponent<Text>();
            UITheme.StyleText(_status, UITextRole.Primary);
            _status.verticalOverflow = VerticalWrapMode.Overflow;
            _status.fontSize = 23; _status.alignment = TextAnchor.MiddleCenter; _status.raycastTarget = false;
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(_panel.transform, false);
            var barRect = bar.GetComponent<RectTransform>(); barRect.sizeDelta = new Vector2(470, 15); barRect.anchoredPosition = new Vector2(0, -35);
            UITheme.StyleTrack(bar.GetComponent<Image>()); bar.GetComponent<Image>().raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(bar.transform, false);
            _fill = fill.GetComponent<Image>(); UITheme.StyleFill(_fill, UITheme.Primary); _fill.raycastTarget = false;
            var fillRect = fill.GetComponent<RectTransform>(); fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = new Vector2(0, 1);
            fillRect.offsetMin = new Vector2(2f, 2f); fillRect.offsetMax = new Vector2(-2f, -2f);
            _panel.SetActive(false);
        }
        private void Update()
        {
            if (Time.unscaledTime < _refreshAt) return;
            _refreshAt = Time.unscaledTime + .1f;
            var manager = NetworkManager.Singleton;
            if (_player == null && manager != null && manager.LocalClient?.PlayerObject != null)
                _player = manager.LocalClient.PlayerObject.transform;
            bool visible = _bridge != null && _bridge.IsSpawned && _player != null &&
                Vector3.SqrMagnitude(_player.position - transform.position) < 35f * 35f &&
                !PauseMenu.IsPaused && !ExpeditionHUD.BlocksGameplayInput;
            if (_panel.activeSelf != visible) _panel.SetActive(visible);
            if (!visible) return;
            int display = _bridge.ProgressPercent | (_bridge.RequiredPadCount << 8) | (_bridge.OccupiedPadCount << 10);
            if (_display == display) return;
            _display = display;
            _status.text = _bridge.IsDeployed ? "สะพานพร้อมแล้ว ลงไปช่วยขนลังได้เลย" :
                $"กำลังสร้างสะพาน {_bridge.ProgressPercent}%\nยืนบนแผ่น {_bridge.OccupiedPadCount}/{_bridge.RequiredPadCount} จุด";
            UITheme.SetBarFill(_fill, _bridge.ProgressPercent * .01f);
        }
    }
}
