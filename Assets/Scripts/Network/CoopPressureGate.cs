using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>Two different connected players hold the pads to permanently open this scene gate.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class CoopPressureGate : NetworkBehaviour
    {
        [SerializeField] private BoxCollider _leftPad;
        [SerializeField] private BoxCollider _rightPad;
        [SerializeField] private GameObject _blocker;
        [SerializeField] private Renderer _leftIndicator;
        [SerializeField] private Renderer _rightIndicator;
        [SerializeField] private Material _idleMaterial;
        [SerializeField] private Material _activeMaterial;
        [SerializeField] private GameObject _openIndicator;
        [SerializeField] private TextMesh _statusText;
        [SerializeField, Min(0.1f)] private float _holdSeconds = 1.5f;

        private const byte Left = 1;
        private const byte Right = 2;
        private const byte Open = 4;
        private readonly NetworkVariable<byte> _state = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private float _holdTimer;
        private byte _visualState = byte.MaxValue;

        public bool IsOpen => IsSpawned && (_state.Value & Open) != 0;

        public override void OnNetworkSpawn()
        {
            _holdTimer = 0f;
            _visualState = byte.MaxValue;
        }

        public override void OnNetworkDespawn()
        {
            _holdTimer = 0f;
            _visualState = byte.MaxValue;
        }

        private void FixedUpdate()
        {
            if (IsSpawned && IsServer && (_state.Value & Open) == 0)
                EvaluatePlayers();

            // Collider activation is applied in the physics tick on every peer.
            byte state = IsSpawned ? _state.Value : (byte)0;
            if (_visualState != state)
                ApplyState(state);
        }

        private void EvaluatePlayers()
        {
            if (_leftPad == null || _rightPad == null || NetworkManager == null)
                return;

            ulong firstLeft = ulong.MaxValue;
            ulong firstRight = ulong.MaxValue;
            bool multipleLeft = false;
            bool multipleRight = false;
            var clients = NetworkManager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                var player = clients[i].PlayerObject;
                if (player == null || !player.IsSpawned ||
                    !player.TryGetComponent<NetworkPlayer>(out _) ||
                    !player.TryGetComponent<CharacterController>(out var controller))
                    continue;

                // Remote CharacterControllers are disabled. Use their replicated transforms,
                // rather than a physics overlap that would miss those players on the host.
                Vector3 feet = player.transform.TransformPoint(
                    controller.center - Vector3.up * (controller.height * 0.5f));
                ulong id = clients[i].ClientId;
                if (ContainsFeet(_leftPad, feet))
                {
                    if (firstLeft == ulong.MaxValue) firstLeft = id;
                    else if (firstLeft != id) multipleLeft = true;
                }
                if (ContainsFeet(_rightPad, feet))
                {
                    if (firstRight == ulong.MaxValue) firstRight = id;
                    else if (firstRight != id) multipleRight = true;
                }
            }

            byte state = 0;
            if (firstLeft != ulong.MaxValue) state |= Left;
            if (firstRight != ulong.MaxValue) state |= Right;
            bool twoPlayers = state == (Left | Right) &&
                (firstLeft != firstRight || multipleLeft || multipleRight);
            _holdTimer = twoPlayers ? _holdTimer + Time.fixedDeltaTime : 0f;
            if (_holdTimer >= _holdSeconds) state |= Open;
            if (_state.Value != state) _state.Value = state;
        }

        private static bool ContainsFeet(BoxCollider pad, Vector3 feet)
        {
            if (!pad.enabled || !pad.gameObject.activeInHierarchy) return false;
            Vector3 local = pad.transform.InverseTransformPoint(feet) - pad.center;
            Vector3 half = pad.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z &&
                local.y >= -half.y && local.y <= half.y;
        }

        private void ApplyState(byte state)
        {
            _visualState = state;
            bool open = (state & Open) != 0;
            if (_blocker != null) _blocker.SetActive(!open);
            if (_openIndicator != null) _openIndicator.SetActive(open);
            if (_leftIndicator != null)
                _leftIndicator.sharedMaterial = (state & Left) != 0 || open ? _activeMaterial : _idleMaterial;
            if (_rightIndicator != null)
                _rightIndicator.sharedMaterial = (state & Right) != 0 || open ? _activeMaterial : _idleMaterial;
            if (_statusText != null)
                _statusText.text = open ? "GATE OPEN\nBRING THE CARGO" :
                    (state & (Left | Right)) == (Left | Right) ? "HOLD BOTH PADS\nTOGETHER" :
                    "2 PLAYERS REQUIRED\nONE ON EACH PAD";
        }
    }
}
