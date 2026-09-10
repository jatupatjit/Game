using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// RoomCodeHUD - Always-visible top-left overlay showing Room Code once in a session.
    /// Uses standard Unity UI Text (no TextMeshPro required).
    /// Fades in when network session is active, fades out when disconnected.
    ///
    /// Setup: Right-click the RoomCodeHUD component → "Build Room Code HUD (Editor Only)"
    /// </summary>
    [DisallowMultipleComponent]
    public class RoomCodeHUD : MonoBehaviour
    {
        [Header("HUD References")]
        [SerializeField] private CanvasGroup _hudGroup;
        [SerializeField] private Text        _roomCodeText;
        [SerializeField] private Text        _modeLabel;
        [SerializeField] private Text        _playerCountText;
        [SerializeField] private Button      _copyButton;
        [SerializeField] private Text        _copyFeedbackText;

        [Header("Settings")]
        [SerializeField] private float _fadeSpeed      = 4f;
        [SerializeField] private float _updateInterval = 0.5f;

        private float     _updateTimer;
        private bool      _wasActive = false;
        private Coroutine _copyFeedbackCoroutine;

        // ───────────────────────────────────────────────────────────────────────

        private void Start()
        {
            if (_copyButton != null)
                _copyButton.onClick.AddListener(OnCopyClicked);

            if (_hudGroup != null)
            {
                _hudGroup.alpha          = 0f;
                _hudGroup.interactable   = false;
                _hudGroup.blocksRaycasts = false;
            }

            if (_copyFeedbackText != null)
                _copyFeedbackText.gameObject.SetActive(false);
        }

        private void Update()
        {
            _updateTimer -= Time.unscaledDeltaTime;
            if (_updateTimer > 0f) return;
            _updateTimer = _updateInterval;

            bool sessionActive = IsSessionActive();

            if (sessionActive != _wasActive)
            {
                _wasActive = sessionActive;
                StopAllCoroutines();
                if (_copyFeedbackText != null)
                    _copyFeedbackText.gameObject.SetActive(false);
                StartCoroutine(FadeTo(sessionActive ? 1f : 0f));
            }

            if (sessionActive)
                RefreshHUDContent();
        }

        // ──────────────────────────────────────────── Content ───────────────────

        private void RefreshHUDContent()
        {
            var nm  = NetworkManager.Singleton;
            var mgr = SteamLobbyManager.Instance;

            if (_roomCodeText != null)
            {
                _roomCodeText.text = (mgr != null && !string.IsNullOrEmpty(mgr.CurrentRoomCode))
                    ? mgr.CurrentRoomCode : "------";
            }

            if (_modeLabel != null && nm != null)
            {
                _modeLabel.text = nm.IsHost ? "HOST" : (nm.IsServer ? "SERVER" : "CLIENT");
            }

            if (_playerCountText != null && nm != null)
            {
                bool isHostOrServer = nm.IsHost || nm.IsServer;
                _playerCountText.gameObject.SetActive(isHostOrServer);
                if (isHostOrServer)
                    _playerCountText.text = "Players: " + nm.ConnectedClientsIds.Count;
            }
        }

        // ──────────────────────────────────────────── Copy Button ───────────────

        private void OnCopyClicked()
        {
            SteamLobbyManager.Instance?.CopyRoomCodeToClipboard();

            if (_copyFeedbackCoroutine != null)
                StopCoroutine(_copyFeedbackCoroutine);
            _copyFeedbackCoroutine = StartCoroutine(ShowCopyFeedback());
        }

        private IEnumerator ShowCopyFeedback()
        {
            if (_copyFeedbackText == null) yield break;
            _copyFeedbackText.gameObject.SetActive(true);
            _copyFeedbackText.text = "Copied!";
            yield return new WaitForSecondsRealtime(1.5f);
            _copyFeedbackText.gameObject.SetActive(false);
        }

        // ──────────────────────────────────────────── Fade ──────────────────────

        private bool IsSessionActive()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && (nm.IsClient || nm.IsServer || nm.IsHost);
        }

        private IEnumerator FadeTo(float target)
        {
            if (_hudGroup == null) yield break;

            _hudGroup.interactable   = false;
            _hudGroup.blocksRaycasts = false;

            while (Mathf.Abs(_hudGroup.alpha - target) > 0.01f)
            {
                _hudGroup.alpha = Mathf.MoveTowards(
                    _hudGroup.alpha, target, Time.unscaledDeltaTime * _fadeSpeed);
                yield return null;
            }
            _hudGroup.alpha = target;

            if (target > 0.5f)
            {
                _hudGroup.interactable   = true;
                _hudGroup.blocksRaycasts = true;
                RefreshHUDContent();
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Build Room Code HUD (Editor Only)")]
        private void BuildHUDInEditor() => RoomCodeHUDBuilder.Build(this);
#endif
    }
}
