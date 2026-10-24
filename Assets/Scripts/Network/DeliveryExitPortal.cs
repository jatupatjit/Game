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
        [SerializeField, Min(0.5f)] private float _coopPartialWaitSeconds = 5.0f;

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
        private bool _allPlayersWereInside;

        public bool IsOpen => IsSpawned && (_state.Value == Ready || _state.Value == Loading);

        private void Awake()
        {
            _readyText = "ส่งลังแล้ว ประตูเปิดแล้ว\nเดินเข้าไปได้เลย: " + _destinationSceneName;
            _loadingText = "รอสักครู่ กำลังไป " + _destinationSceneName;
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

            CountPlayersInside(out int insideCount, out int totalPlayers);
            bool allInside = totalPlayers > 0 && insideCount >= totalPlayers;

            if (_state.Value == Ready && Time.unscaledTime >= _retryAfter && insideCount > 0)
            {
                _state.Value = Loading;
                _allPlayersWereInside = allInside;
                _transitionAt = Time.unscaledTime + (allInside ? _entryDelaySeconds : _coopPartialWaitSeconds);
            }

            if (_state.Value == Loading)
            {
                if (insideCount <= 0)
                {
                    _state.Value = Ready;
                    _allPlayersWereInside = false;
                    return;
                }

                // Speed up transition immediately if the second co-op player joins inside the portal.
                if (allInside && !_allPlayersWereInside)
                {
                    _allPlayersWereInside = true;
                    _transitionAt = Mathf.Min(_transitionAt, Time.unscaledTime + _entryDelaySeconds);
                }

                if (_statusText != null)
                {
                    _statusText.text = allInside
                        ? _loadingText
                        : $"รอเพื่อนเข้าประตู ({insideCount}/{totalPlayers})...\nไป {_destinationSceneName} ใน {Mathf.CeilToInt(Mathf.Max(0f, _transitionAt - Time.unscaledTime))} วิ";
                }
            }

            if (_state.Value != Loading || Time.unscaledTime < _transitionAt) return;

            // A player who entered may have disconnected or left during the short entry delay.
            if (insideCount <= 0)
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

        private void CountPlayersInside(out int insideCount, out int totalPlayers)
        {
            insideCount = 0;
            totalPlayers = 0;
            if (_entryVolume == null || !_entryVolume.enabled || !_entryVolume.gameObject.activeInHierarchy ||
                NetworkManager == null)
                return;

            var clients = NetworkManager.ConnectedClientsList;
            Vector3 half = _entryVolume.size * 0.5f;
            for (int i = 0; i < clients.Count; i++)
            {
                NetworkObject player = clients[i].PlayerObject;
                if (player == null || !player.IsSpawned || !player.TryGetComponent<NetworkPlayer>(out _))
                    continue;
                totalPlayers++;
                // Remote controllers are disabled, so inspect their replicated positions.
                Vector3 local = _entryVolume.transform.InverseTransformPoint(player.transform.position)
                    - _entryVolume.center;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z)
                    insideCount++;
            }
        }

        private bool HasPlayerInside()
        {
            CountPlayersInside(out int insideCount, out _);
            return insideCount > 0;
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
                    state == Unavailable ? "ยังไปต่อไม่ได้\nให้เจ้าของห้องตรวจด่านถัดไปก่อน" :
                    "ส่งลังตรงจุดหมายก่อน\nแล้วประตูจะเปิด";
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
