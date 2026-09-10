using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// PauseMenu - ESC key opens an animated pause overlay during an active game session.
    /// Uses standard Unity UI (no TextMeshPro required).
    /// Buttons: Continue, Settings (volume/fullscreen), Quit to Menu (with confirm dialog).
    ///
    /// Setup: Right-click the PauseMenu component → "Build Pause Menu UI (Editor Only)"
    /// </summary>
    [DisallowMultipleComponent]
    public class PauseMenu : MonoBehaviour
    {
        [Header("Root Panel")]
        [SerializeField] private CanvasGroup _pauseRoot;

        [Header("Main Pause Buttons")]
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _quitToMenuButton;

        [Header("Settings Panel")]
        [SerializeField] private GameObject _settingsPanel;
        [SerializeField] private Button     _backFromSettingsButton;

        [Header("Settings Controls (Optional)")]
        [SerializeField] private Slider _masterVolumeSlider;
        [SerializeField] private Slider _sfxVolumeSlider;
        [SerializeField] private Slider _musicVolumeSlider;
        [SerializeField] private Toggle _fullscreenToggle;

        [Header("Overlay")]
        [SerializeField] private Image _backgroundOverlay;

        [Header("Confirm Quit Dialog")]
        [SerializeField] private GameObject _confirmDialog;
        [SerializeField] private Button     _confirmQuitButton;
        [SerializeField] private Button     _cancelQuitButton;

        [Header("Settings")]
        [SerializeField] private float _fadeSpeed = 5f;
        [Tooltip("Only allow pause when a network session is active")]
        [SerializeField] private bool _requireActiveSession = false;

        private bool _isPaused     = false;
        private bool _settingsOpen = false;

        // ───────────────────────────────────────────────────────────────────────

        private void Start()
        {
            if (_continueButton         != null) _continueButton.onClick.AddListener(OnContinueClicked);
            if (_settingsButton         != null) _settingsButton.onClick.AddListener(OnSettingsClicked);
            if (_quitToMenuButton       != null) _quitToMenuButton.onClick.AddListener(OnQuitToMenuClicked);
            if (_backFromSettingsButton != null) _backFromSettingsButton.onClick.AddListener(OnBackFromSettings);
            if (_confirmQuitButton      != null) _confirmQuitButton.onClick.AddListener(OnConfirmQuit);
            if (_cancelQuitButton       != null) _cancelQuitButton.onClick.AddListener(OnCancelQuit);

            // Restore saved volume
            if (_masterVolumeSlider != null)
            {
                _masterVolumeSlider.value = PlayerPrefs.GetFloat("MasterVolume", 1f);
                _masterVolumeSlider.onValueChanged.AddListener(v =>
                {
                    PlayerPrefs.SetFloat("MasterVolume", v);
                    AudioListener.volume = v;
                });
                AudioListener.volume = _masterVolumeSlider.value;
            }

            if (_fullscreenToggle != null)
            {
                _fullscreenToggle.isOn = Screen.fullScreen;
                _fullscreenToggle.onValueChanged.AddListener(v => Screen.fullScreen = v);
            }

            HidePause(instant: true);
            HideSettingsPanel();
            HideConfirmDialog();
        }

        private void Update()
        {
            bool escPressed = false;

            // New Input System
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                escPressed = UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
            }

            if (!escPressed) return;

            // Don't pause if no session is active (and setting is on)
            if (_requireActiveSession && !IsSessionActive()) return;

            if (_settingsOpen)
                OnBackFromSettings();
            else if (_isPaused)
                OnContinueClicked();
            else
                ShowPause();
        }

        // ──────────────────────────────────────────── Pause Toggle ──────────────

        public void ShowPause()
        {
            if (_isPaused) return;
            _isPaused = true;

            HideSettingsPanel();
            HideConfirmDialog();

            StopAllCoroutines();
            StartCoroutine(FadeTo(1f));

            if (_pauseRoot != null)
            {
                _pauseRoot.interactable   = true;
                _pauseRoot.blocksRaycasts = true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        public void HidePause(bool instant = false)
        {
            _isPaused = false;
            HideSettingsPanel();
            HideConfirmDialog();

            if (instant)
            {
                if (_pauseRoot != null)
                {
                    _pauseRoot.alpha          = 0f;
                    _pauseRoot.interactable   = false;
                    _pauseRoot.blocksRaycasts = false;
                }
                return;
            }

            StopAllCoroutines();
            StartCoroutine(FadeTo(0f));

            // Re-lock cursor when resuming game
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        // ──────────────────────────────────────────── Button Handlers ──────────

        private void OnContinueClicked() => HidePause();

        private void OnSettingsClicked()
        {
            _settingsOpen = true;
            ShowSettingsPanel();
        }

        private void OnBackFromSettings()
        {
            _settingsOpen = false;
            HideSettingsPanel();
        }

        private void OnQuitToMenuClicked()
        {
            if (_confirmDialog != null)
            {
                ShowConfirmDialog();
            }
            else
            {
                OnConfirmQuit();
            }
        }

        private void OnConfirmQuit()
        {
            HideConfirmDialog();
            HidePause(instant: true);

            var mgr = SteamLobbyManager.Instance;
            if (mgr != null)
            {
                mgr.DisconnectAndReturnToLobby();
            }
            else
            {
                // Fallback if no SteamLobbyManager
                var nm = NetworkManager.Singleton;
                if (nm != null && (nm.IsClient || nm.IsServer))
                    nm.Shutdown();

                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            }
        }

        private void OnCancelQuit() => HideConfirmDialog();

        // ──────────────────────────────────────────── Settings Panel ───────────

        private void ShowSettingsPanel()
        {
            if (_settingsPanel != null) _settingsPanel.SetActive(true);
            SetMainButtonsVisible(false);
        }

        private void HideSettingsPanel()
        {
            _settingsOpen = false;
            if (_settingsPanel != null) _settingsPanel.SetActive(false);
            SetMainButtonsVisible(true);
        }

        private void SetMainButtonsVisible(bool visible)
        {
            if (_continueButton   != null) _continueButton.gameObject.SetActive(visible);
            if (_settingsButton   != null) _settingsButton.gameObject.SetActive(visible);
            if (_quitToMenuButton != null) _quitToMenuButton.gameObject.SetActive(visible);
        }

        // ──────────────────────────────────────────── Confirm Dialog ───────────

        private void ShowConfirmDialog()
        {
            if (_confirmDialog != null) _confirmDialog.SetActive(true);
        }

        private void HideConfirmDialog()
        {
            if (_confirmDialog != null) _confirmDialog.SetActive(false);
        }

        // ──────────────────────────────────────────── Fade ──────────────────────

        private bool IsSessionActive()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && (nm.IsClient || nm.IsServer || nm.IsHost);
        }

        private IEnumerator FadeTo(float target)
        {
            if (_pauseRoot == null) yield break;

            while (Mathf.Abs(_pauseRoot.alpha - target) > 0.01f)
            {
                _pauseRoot.alpha = Mathf.MoveTowards(
                    _pauseRoot.alpha, target, Time.unscaledDeltaTime * _fadeSpeed);
                yield return null;
            }
            _pauseRoot.alpha = target;

            if (target < 0.5f)
            {
                _pauseRoot.interactable   = false;
                _pauseRoot.blocksRaycasts = false;
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Build Pause Menu UI (Editor Only)")]
        private void BuildPauseMenuInEditor() => PauseMenuBuilder.Build(this);
#endif
    }
}
