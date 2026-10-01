using System.Collections;
using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.Network
{
    /// <summary>
    /// Opens a shared portal when a nearby player presses Interact, then loads the destination
    /// for the whole NGO session when a player walks into the portal entry zone.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class LobbyPortalGate : NetworkBehaviour
    {
        [Header("Interaction")]
        [SerializeField, Min(0.5f)] private float _interactionDistance = 2.5f;
        [SerializeField] private Transform _interactionPoint;
        [SerializeField] private GameObject _interactionPrompt;

        [Header("Portal")]
        [SerializeField] private GameObject _portalVisualRoot;
        [SerializeField] private Collider _portalEntryZone;
        [SerializeField, Min(0f)] private float _entryLoadDelay = 0.25f;

        [Header("Destination")]
        [SerializeField] private string _destinationSceneName = "Level01";

        private readonly NetworkVariable<bool> _portalActivated = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private PlayerInputReader _localInputReader;
        private Transform _localPlayerTransform;
        private bool _sceneLoadRequested;

        private void Awake()
        {
            if (_interactionPoint == null)
                _interactionPoint = transform;

            if (_interactionPrompt == null && _interactionPoint.parent != null)
            {
                Transform prompt = _interactionPoint.parent.Find("Interaction Prompt");
                if (prompt != null)
                    _interactionPrompt = prompt.gameObject;
            }

            if (_portalEntryZone == null)
                _portalEntryZone = GetComponentInChildren<Collider>(true);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _portalActivated.OnValueChanged += OnPortalActivatedChanged;
            ApplyPortalState(_portalActivated.Value);
            SetInteractionPromptVisible(false);
            StartCoroutine(BindLocalPlayerInput());
        }

        private void Update()
        {
            bool canInteract = IsSpawned && !_portalActivated.Value &&
                               _localPlayerTransform != null && _interactionPoint != null;
            bool isNearby = canInteract &&
                            (_localPlayerTransform.position - _interactionPoint.position).sqrMagnitude <=
                            _interactionDistance * _interactionDistance;
            SetInteractionPromptVisible(isNearby);
        }

        public override void OnNetworkDespawn()
        {
            _portalActivated.OnValueChanged -= OnPortalActivatedChanged;
            SetInteractionPromptVisible(false);
            UnbindLocalPlayerInput();
            base.OnNetworkDespawn();
        }

        private void FixedUpdate()
        {
            if (!IsServer || !_portalActivated.Value || _sceneLoadRequested)
                return;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || _portalEntryZone == null)
                return;

            Bounds entryBounds = _portalEntryZone.bounds;
            foreach (NetworkClient client in manager.ConnectedClientsList)
            {
                NetworkObject playerObject = client.PlayerObject;
                if (playerObject != null && entryBounds.Contains(playerObject.transform.position))
                {
                    _sceneLoadRequested = true;
                    StartCoroutine(LoadDestinationAfterEntry(manager));
                    return;
                }
            }
        }

        private IEnumerator BindLocalPlayerInput()
        {
            var wait = new WaitForSeconds(0.25f);
            while (IsSpawned && _localInputReader == null)
            {
                NetworkManager manager = NetworkManager.Singleton;
                NetworkObject localPlayer = manager != null && manager.IsListening
                    ? manager.LocalClient?.PlayerObject
                    : null;

                if (localPlayer != null)
                {
                    _localPlayerTransform = localPlayer.transform;
                    _localInputReader = localPlayer.GetComponent<PlayerInputReader>();
                    if (_localInputReader != null)
                    {
                        _localInputReader.OnInteractPerformed += TryRequestActivate;
                        yield break;
                    }
                }

                yield return wait;
            }
        }

        private void UnbindLocalPlayerInput()
        {
            if (_localInputReader != null)
                _localInputReader.OnInteractPerformed -= TryRequestActivate;

            _localInputReader = null;
            _localPlayerTransform = null;
        }

        private void TryRequestActivate()
        {
            if (_localPlayerTransform == null || _portalActivated.Value || _interactionPoint == null)
                return;

            if (Vector3.Distance(_localPlayerTransform.position, _interactionPoint.position) > _interactionDistance)
                return;

            RequestActivatePortalRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestActivatePortalRpc(RpcParams rpcParams = default)
        {
            if (!IsServer || _portalActivated.Value)
                return;

            NetworkManager manager = NetworkManager.Singleton;
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (manager == null ||
                !manager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient sender) ||
                sender.PlayerObject == null)
                return;

            if (_interactionPoint == null ||
                Vector3.Distance(sender.PlayerObject.transform.position, _interactionPoint.position) > _interactionDistance)
                return;

            if (!TryGetDestinationScenePath(out _))
            {
                Debug.LogError($"[LobbyPortalGate] Destination scene '{_destinationSceneName}' is missing or not included in Build Settings.", this);
                return;
            }

            if (!manager.NetworkConfig.EnableSceneManagement || manager.SceneManager == null)
            {
                Debug.LogError("[LobbyPortalGate] NGO scene management must be enabled on NetworkManager.", this);
                return;
            }

            _portalActivated.Value = true;
        }

        private IEnumerator LoadDestinationAfterEntry(NetworkManager manager)
        {
            if (_entryLoadDelay > 0f)
                yield return new WaitForSecondsRealtime(_entryLoadDelay);

            if (manager == null || !manager.IsListening || !manager.IsServer || manager.SceneManager == null)
            {
                _sceneLoadRequested = false;
                yield break;
            }

            if (!TryGetDestinationScenePath(out string scenePath))
            {
                Debug.LogError($"[LobbyPortalGate] Destination scene '{_destinationSceneName}' is no longer available in Build Settings.", this);
                _sceneLoadRequested = false;
                yield break;
            }

            SceneEventProgressStatus status = manager.SceneManager.LoadScene(scenePath, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[LobbyPortalGate] NGO failed to start scene load for '{_destinationSceneName}': {status}.", this);
                _sceneLoadRequested = false;
            }
        }

        private void OnPortalActivatedChanged(bool previousValue, bool newValue)
        {
            ApplyPortalState(newValue);
            if (newValue)
                SetInteractionPromptVisible(false);
        }

        private void SetInteractionPromptVisible(bool visible)
        {
            if (_interactionPrompt != null && _interactionPrompt.activeSelf != visible)
                _interactionPrompt.SetActive(visible);
        }

        private void ApplyPortalState(bool active)
        {
            if (_portalVisualRoot != null)
                _portalVisualRoot.SetActive(active);
        }

        private bool TryGetDestinationScenePath(out string destinationPath)
        {
            destinationPath = null;
            if (string.IsNullOrWhiteSpace(_destinationSceneName))
                return false;

            for (int index = 0; index < SceneManager.sceneCountInBuildSettings; index++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(index);
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
                if (string.Equals(sceneName, _destinationSceneName, System.StringComparison.Ordinal) &&
                    Application.CanStreamedLevelBeLoaded(index))
                {
                    destinationPath = scenePath;
                    return true;
                }
            }

            return false;
        }

        private void OnValidate()
        {
            _interactionDistance = Mathf.Max(0.5f, _interactionDistance);
            _entryLoadDelay = Mathf.Max(0f, _entryLoadDelay);
        }
    }
}
