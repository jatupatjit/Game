using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// A lever high on a climbable wall. A player climbs up and
    /// presses Interact (E) next to it; it latches ON for the whole party and stays on. Anything with a
    /// <see cref="SwitchTarget"/> pointing at this switch then opens on every screen: a gate rises, a bridge lowers,
    /// a platform slides out. Only reachable from up the wall (reach + height check), validated on the server.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class ClimbWallSwitch : NetworkBehaviour
    {
        [Tooltip("Where the hand presses (the lever's pivot).")]
        [SerializeField] private Transform _button;
        [Tooltip("Lever arm, tilted from Off to On.")]
        [SerializeField] private Transform _lever;
        [SerializeField] private Vector3 _leverOnEuler = new(0f, 0f, -70f);
        [SerializeField] private Renderer _indicator;
        [SerializeField] private TextMesh _label;
        [SerializeField, Min(.5f)] private float _reach = 2.6f;
        [Tooltip("The presser's root must be at least this far below/above the button (m): -1.4 means from the wall " +
                 "beside it or the ledge above, never from the ground under a wall taller than ~3.5 m.")]
        [SerializeField] private float _minHeightOffset = -1.4f;
        [SerializeField] private string _feedLine = "เปิดทางแล้ว! ขนลังผ่านไปได้เลย";

        private const float MaxReportedOffset = 3f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private readonly NetworkVariable<bool> _on = new(false);
        private MaterialPropertyBlock _block;
        private Quaternion _leverRest;
        private float _leverT;
        private int _shown = -1;
        private bool _pending;
        private ulong _pendingSender;
        private Vector3 _pendingPosition;

        /// <summary>True once someone pressed it (every machine).</summary>
        public bool IsOn => IsSpawned && _on.Value;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (_lever != null) _leverRest = _lever.localRotation;
        }

        private bool CanReachFrom(Vector3 position) =>
            _button != null && position.y >= _button.position.y + _minHeightOffset &&
            (position - _button.position).sqrMagnitude <= _reach * _reach;

        private bool LocalNear()
        {
            var player = NetworkManager != null ? NetworkManager.LocalClient?.PlayerObject : null;
            return player != null && player.IsSpawned && CanReachFrom(player.transform.position);
        }

        private void Update()
        {
            if (!IsSpawned || !IsClient || _on.Value) return;
            if (PauseMenu.IsPaused || MissionFailUI.IsVisible || ExpeditionHUD.BlocksGameplayInput || UIPanels.AnyOpen) return;
            if (!LocalNear() || !GameSettings.Pressed(BindRole.Interact)) return;
            PressRpc(NetworkManager.LocalClient.PlayerObject.transform.position);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void PressRpc(Vector3 presserPosition, RpcParams rpc = default)
        {
            if (_on.Value || !NetworkManager.ConnectedClients.TryGetValue(rpc.Receive.SenderClientId, out var client) ||
                client.PlayerObject == null) return;
            _pendingSender = client.ClientId;
            _pendingPosition = presserPosition;
            _pending = true;
        }

        /// <summary>Test probe / scripted press from the server.</summary>
        public void ServerForceOn()
        {
            if (IsServer && !_on.Value) { _on.Value = true; GameRoom.ServerFeed(_feedLine, new Color(.55f, 1f, .6f)); }
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (_label != null && cam != null) _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.transform.position);
        }

        private void FixedUpdate()
        {
            if (IsSpawned && IsServer && _pending)
            {
                _pending = false;
                if (!_on.Value && NetworkManager.ConnectedClients.TryGetValue(_pendingSender, out var client) && client.PlayerObject != null)
                {
                    // The server sees a remote climber one network delay late: accept the reported spot near its copy.
                    Vector3 seen = client.PlayerObject.transform.position;
                    Vector3 p = _pendingPosition;
                    bool plausible = float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z) &&
                                     (p - seen).sqrMagnitude <= MaxReportedOffset * MaxReportedOffset;
                    if (CanReachFrom(plausible ? p : seen)) ServerForceOn();
                }
            }

            bool on = IsOn;
            _leverT = Mathf.MoveTowards(_leverT, on ? 1f : 0f, Time.fixedDeltaTime * 3f);
            if (_lever != null) _lever.localRotation = _leverRest * Quaternion.Euler(_leverOnEuler * Mathf.SmoothStep(0f, 1f, _leverT));

            int display = on ? 2 : IsSpawned && IsClient && LocalNear() ? 1 : 0;
            if (display == _shown) return;
            if (_shown == 1 && display == 2) GameplayFeedback.Play(GameplayFeedback.Cue.Click, _button != null ? _button.position : transform.position, 1f);
            _shown = display;
            string key = GameSettings.KeyName(BindRole.Interact);
            if (_label != null)
                _label.text = display == 2 ? "เปิดแล้ว!" : display == 1 ? "กด " + key + " ดึงคันโยก" : "ปีนขึ้นมาดึงคันโยก";
            if (_indicator != null)
            {
                _indicator.GetPropertyBlock(_block);
                _block.SetColor(BaseColor, on ? new Color(.2f, .95f, .35f) : new Color(1f, .55f, .1f));
                _indicator.SetPropertyBlock(_block);
            }
        }
    }
}
