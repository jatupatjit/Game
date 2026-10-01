using System;
using CoopGame.CarrySystem;
using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.Network
{
    /// <summary>Unlocks after this delivery point succeeds and moves the session through NGO scene management.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class DeliveryExitPortal : NetworkBehaviour
    {
        [SerializeField] private DeliveryPoint _deliveryPoint;
        [SerializeField] private BoxCollider _entryVolume;
        [SerializeField] private GameObject _portalVisual;
        [SerializeField] private GameObject _closedBarrier;
        [SerializeField] private TextMesh _statusText;
        [SerializeField] private string _destinationSceneName = "Level02";
        [SerializeField, Min(0.1f)] private float _entryDelaySeconds = 0.5f;

        private const byte Locked = 0;
        private const byte Ready = 1;
        private const byte Loading = 2;
        private const byte Unavailable = 3;
        private readonly NetworkVariable<byte> _state = new(
            Locked, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private byte _visualState = byte.MaxValue;
        private string _destinationPath;
        private string _readyText;
        private string _loadingText;
        private float _transitionAt;
        private float _retryAfter;
        private bool _loadStarted;

        public bool IsOpen => IsSpawned && (_state.Value == Ready || _state.Value == Loading);

        private void Awake()
        {
            _readyText = "PORTAL OPEN\nWALK IN TO " + _destinationSceneName;
            _loadingText = "TRAVELLING TO\n" + _destinationSceneName;
        }

        public override void OnNetworkSpawn()
        {
            _visualState = byte.MaxValue;
            _transitionAt = 0f;
            _retryAfter = 0f;
            _loadStarted = false;
            if (IsServer) _destinationPath = FindDestinationPath();
        }

        public override void OnNetworkDespawn()
        {
            _loadStarted = false;
            _visualState = byte.MaxValue;
        }

        private void FixedUpdate()
        {
            if (IsSpawned && IsServer) EvaluateTransition();
            byte state = IsSpawned ? _state.Value : Locked;
            if (_visualState != state) ApplyState(state);
        }

        private void EvaluateTransition()
        {
            if (_loadStarted || MissionFailUI.IsVisible) return;

            if (_state.Value == Locked && _deliveryPoint != null && _deliveryPoint.IsDelivered)
            {
                if (string.IsNullOrEmpty(_destinationPath) || _entryVolume == null ||
                    !NetworkManager.NetworkConfig.EnableSceneManagement || NetworkManager.SceneManager == null)
                {
                    _state.Value = Unavailable;
                    Debug.LogError($"[DeliveryExitPortal] Cannot open portal to '{_destinationSceneName}'. " +
                        "Check the entry volume, enabled Build Settings scene and NGO scene management.", this);
                    return;
                }
                _state.Value = Ready;
                _retryAfter = Time.unscaledTime + 0.75f;
            }

            if (_state.Value == Ready && Time.unscaledTime >= _retryAfter && HasPlayerInside())
            {
                _state.Value = Loading;
                _transitionAt = Time.unscaledTime + _entryDelaySeconds;
            }

            if (_state.Value != Loading || Time.unscaledTime < _transitionAt) return;

            // A player who entered may have disconnected or left during the short entry delay.
            if (!HasPlayerInside())
            {
                _state.Value = Ready;
                return;
            }

            SceneEventProgressStatus result = NetworkManager.SceneManager.LoadScene(
                _destinationPath, LoadSceneMode.Single);
            if (result == SceneEventProgressStatus.Started)
            {
                _loadStarted = true;
                return;
            }

            _state.Value = Ready;
            _retryAfter = Time.unscaledTime + 2f;
            Debug.LogWarning($"[DeliveryExitPortal] Scene transition could not start: {result}. Will retry on entry.", this);
        }

        private bool HasPlayerInside()
        {
            if (_entryVolume == null || !_entryVolume.enabled || !_entryVolume.gameObject.activeInHierarchy)
                return false;

            var clients = NetworkManager.ConnectedClientsList;
            Vector3 half = _entryVolume.size * 0.5f;
            for (int i = 0; i < clients.Count; i++)
            {
                NetworkObject player = clients[i].PlayerObject;
                if (player == null || !player.IsSpawned || !player.TryGetComponent<NetworkPlayer>(out _))
                    continue;
                // Remote controllers are disabled, so inspect their replicated positions.
                Vector3 local = _entryVolume.transform.InverseTransformPoint(player.transform.position)
                    - _entryVolume.center;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z)
                    return true;
            }
            return false;
        }

        private void ApplyState(byte state)
        {
            if (_visualState != byte.MaxValue && _visualState == Locked && (state == Ready || state == Loading))
                GameplayFeedback.Play(GameplayFeedback.Cue.Portal, transform.position);
            _visualState = state;
            bool open = state == Ready || state == Loading;
            if (_portalVisual != null) _portalVisual.SetActive(open);
            if (_closedBarrier != null) _closedBarrier.SetActive(!open);
            if (_statusText != null)
                _statusText.text = state == Loading ? _loadingText : state == Ready ? _readyText :
                    state == Unavailable ? "DESTINATION UNAVAILABLE\nCONTACT THE HOST" :
                    "DELIVER THE CARGO\nTO OPEN THE PORTAL";
        }

        private string FindDestinationPath()
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(path),
                        _destinationSceneName, StringComparison.Ordinal) && Application.CanStreamedLevelBeLoaded(i))
                    return path;
            }
            return null;
        }
    }
}
