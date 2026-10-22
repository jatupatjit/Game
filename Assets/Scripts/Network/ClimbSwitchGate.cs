using CoopGame.CarrySystem;
using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame.Network
{
    /// <summary>A high switch latches a cargo passage open for the whole party.</summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class ClimbSwitchGate : NetworkBehaviour
    {
        [SerializeField] private Transform _button;
        [SerializeField] private GameObject _barrier;
        [SerializeField] private TextMesh _label;
        [SerializeField] private Renderer _indicator;
        [SerializeField] private LevelMission _mission;
        [SerializeField, Min(.25f)] private float _interactionDistance = 2.25f;
        [Tooltip("Player root must reach this height above the button; prevents pressing from the ground.")]
        [SerializeField] private float _requiredHeightOffset = .2f;
        private readonly NetworkVariable<bool> _open = new(false);
        private MaterialPropertyBlock _properties;
        private ulong _pendingSender;
        private Vector3 _pendingPosition;
        private bool _pressPending;
        // The server sees a remote player one network delay late (interpolated), so it may still have them
        // climbing below the button when they press. The press carries where the presser really is; the
        // server accepts it only near its own copy of that player.
        private const float MaxReportedOffset = 3f;
        private int _lastDisplay = -1;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public bool IsOpen => IsSpawned && _open.Value;
        public bool IsLocalNear => CanReach(NetworkManager != null ? NetworkManager.LocalClient?.PlayerObject : null);

        private void Awake() => _properties = new MaterialPropertyBlock();

        public override void OnNetworkSpawn()
        {
            _pressPending = false;
            _lastDisplay = -1;
        }

        public override void OnNetworkDespawn() => _pressPending = false;

        private bool CanReach(NetworkObject player) =>
            player != null && player.IsSpawned && CanReachFrom(player.transform.position);

        private bool CanReachFrom(Vector3 position)
        {
            return _button != null && position.y >= _button.position.y + _requiredHeightOffset &&
                (position - _button.position).sqrMagnitude <= _interactionDistance * _interactionDistance;
        }

        /// <summary>Server: the presser's reported position if plausible for its replicated copy, else that copy.</summary>
        private static Vector3 PresserPosition(NetworkObject player, Vector3 reported)
        {
            Vector3 seen = player.transform.position;
            return float.IsFinite(reported.x) && float.IsFinite(reported.y) && float.IsFinite(reported.z) &&
                (reported - seen).sqrMagnitude <= MaxReportedOffset * MaxReportedOffset ? reported : seen;
        }

        private void Update()
        {
            if (!IsSpawned || !IsClient || _open.Value || !IsLocalNear ||
                PauseMenu.IsPaused || MissionFailUI.IsVisible || ExpeditionHUD.BlocksGameplayInput) return;
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                PressSwitchRpc(NetworkManager.LocalClient.PlayerObject.transform.position);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void PressSwitchRpc(Vector3 presserPosition, RpcParams parameters = default)
        {
            if (_open.Value || (_mission != null && _mission.Phase.Value >= 2) ||
                !NetworkManager.ConnectedClients.TryGetValue(parameters.Receive.SenderClientId, out var client) ||
                client.PlayerObject == null || !client.PlayerObject.TryGetComponent<NetworkPlayer>(out _)) return;
            Vector3 position = PresserPosition(client.PlayerObject, presserPosition);
            if (!CanReachFrom(position)) return;
            // Revalidate and perform the sight query on the physics step.
            _pendingSender = parameters.Receive.SenderClientId;
            _pendingPosition = presserPosition;
            _pressPending = true;
        }

        private void FixedUpdate()
        {
            if (IsSpawned && IsServer && _pressPending)
            {
                _pressPending = false;
                if (!_open.Value && (_mission == null || _mission.Phase.Value < 2) &&
                    NetworkManager.ConnectedClients.TryGetValue(_pendingSender, out var client) && client.PlayerObject != null &&
                    CanReachFrom(PresserPosition(client.PlayerObject, _pendingPosition)))
                {
                    Vector3 from = PresserPosition(client.PlayerObject, _pendingPosition) + Vector3.up * .5f;
                    Vector3 direction = _button.position - from;
                    float distance = direction.magnitude;
                    if (distance <= .05f || !Physics.Raycast(from, direction / distance, distance - .05f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        _open.Value = true;
                }
            }
            bool open = IsOpen;
            if (_barrier != null && _barrier.activeSelf == open) _barrier.SetActive(!open);
            int display = open ? 2 : IsSpawned && IsLocalNear ? 1 : 0;
            if (display == _lastDisplay) return;
            _lastDisplay = display;
            if (_label != null) _label.text = open ? "เปิดแล้ว ลงไปช่วยเพื่อนขนลังได้เลย" :
                display == 1 ? "กด E เปิดประตูให้เพื่อน" : "ปีนขึ้นมา แล้วกด E ที่ปุ่ม";
            if (_indicator != null)
            {
                _indicator.GetPropertyBlock(_properties);
                _properties.SetColor(BaseColor, open ? new Color(.15f, .9f, .3f) : new Color(1f, .6f, .1f));
                _indicator.SetPropertyBlock(_properties);
            }
        }
    }
}
