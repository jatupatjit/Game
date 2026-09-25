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

            if (_portalEntryZone == null)
                _portalEntryZone = GetComponentInChildren<Collider>(true);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _portalActivated.OnValueChanged += OnPortalActivatedChanged;
            ApplyPortalState(_portalActivated.Value);
            StartCoroutine(BindLocalPlayerInput());
        }

        public override void OnNetworkDespawn()
        {
            _portalActivated.OnValueChanged -= OnPortalActivatedChanged;
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

        [Rpc(SendTo.Server, RequireOwnership = false)]
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

            if (string.IsNullOrWhiteSpace(_destinationSceneName) ||
                !Application.CanStreamedLevelBeLoaded(_destinationSceneName))
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

            SceneEventProgressStatus status = manager.SceneManager.LoadScene(_destinationSceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[LobbyPortalGate] NGO failed to start scene load for '{_destinationSceneName}': {status}.", this);
                _sceneLoadRequested = false;
            }
        }

        private void OnPortalActivatedChanged(bool previousValue, bool newValue)
        {
            ApplyPortalState(newValue);
        }

        private void ApplyPortalState(bool active)
        {
            if (_portalVisualRoot != null)
                _portalVisualRoot.SetActive(active);
        }

        private void OnValidate()
        {
            _interactionDistance = Mathf.Max(0.5f, _interactionDistance);
            _entryLoadDelay = Mathf.Max(0f, _entryLoadDelay);
        }
    }
}
