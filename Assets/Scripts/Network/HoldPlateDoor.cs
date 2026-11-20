using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Hold-button door (George 2026-11-19, idea 9): a portcullis opens only while a player stands on one of its
    /// floor buttons (one on each side), so one player holds it while the others carry the crate through; a button
    /// on the far side lets them open it again for the holder. Released, it waits a moment and closes, but never on
    /// a player or a crate in the doorway. In a solo session one press keeps it open for a while (demo mode).
    /// The host reads who stands where; every screen moves the gate from the synced state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class HoldPlateDoor : NetworkBehaviour
    {
        [Tooltip("Floor buttons (box = where feet count). Either one opens the door.")]
        [SerializeField] private BoxCollider[] _plates;
        [Tooltip("Button tops that sink while pressed (same order as the plates).")]
        [SerializeField] private Transform[] _plateTops;
        [SerializeField] private Renderer[] _plateLights;
        [SerializeField] private Material _idleMaterial;
        [SerializeField] private Material _activeMaterial;
        [Tooltip("The portcullis (with a kinematic Rigidbody); it rises by Open Height.")]
        [SerializeField] private Rigidbody _gate;
        [SerializeField, Min(.5f)] private float _openHeight = 3.2f;
        [SerializeField, Min(.1f)] private float _openSpeed = 3f;
        [SerializeField, Min(.1f)] private float _closeSpeed = 1.4f;
        [Tooltip("Seconds the door stays open after the last player steps off a button.")]
        [SerializeField, Min(0f)] private float _closeDelay = .8f;
        [Tooltip("Solo session: seconds a press keeps the door open.")]
        [SerializeField, Min(0f)] private float _soloOpenSeconds = 7f;
        [Tooltip("The doorway: while a player or a crate is inside, the door does not close.")]
        [SerializeField] private BoxCollider _passage;
        [SerializeField] private TextMesh _label;

        private const byte OpenBit = 1;
        private readonly NetworkVariable<byte> _state = new NetworkVariable<byte>(0);
        // Which buttons are pressed (bit per plate), for the button looks on every screen.
        private readonly NetworkVariable<byte> _pressed = new NetworkVariable<byte>(0);
        private float _openUntil, _lift;
        private Vector3 _gateRest;
        private byte _shownPressed = byte.MaxValue;
        private bool _shownOpen;
        private Vector3[] _topRest;
        private readonly Collider[] _overlap = new Collider[16];

        public bool IsOpen => IsSpawned && (_state.Value & OpenBit) != 0;

        private void Awake()
        {
            if (_gate != null) _gateRest = _gate.transform.localPosition;
            _topRest = new Vector3[_plateTops != null ? _plateTops.Length : 0];
            for (int i = 0; i < _topRest.Length; i++) if (_plateTops[i] != null) _topRest[i] = _plateTops[i].localPosition;
        }

        private void FixedUpdate()
        {
            if (IsSpawned && IsServer) Evaluate();
            bool open = IsOpen;
            float target = open ? 1f : 0f;
            _lift = Mathf.MoveTowards(_lift, target, Time.fixedDeltaTime * (open ? _openSpeed : _closeSpeed) / _openHeight);
            if (_gate != null)
            {
                Vector3 local = _gateRest + Vector3.up * (_openHeight * Mathf.SmoothStep(0f, 1f, _lift));
                Vector3 world = _gate.transform.parent != null ? _gate.transform.parent.TransformPoint(local) : local;
                if (_gate.isKinematic) _gate.MovePosition(world); else _gate.transform.position = world;
            }
            if (open != _shownOpen)
            {
                _shownOpen = open;
                GameplayFeedback.Play(open ? GameplayFeedback.Cue.Portal : GameplayFeedback.Cue.Impact, _gate != null ? _gate.position : transform.position, .6f);
            }
            byte pressed = IsSpawned ? _pressed.Value : (byte)0;
            if (pressed != _shownPressed) ShowButtons(pressed);
        }

        private void Evaluate()
        {
            var manager = NetworkManager;
            byte pressed = 0;
            int players = 0;
            foreach (var client in manager.ConnectedClientsList)
            {
                var player = client.PlayerObject;
                if (player == null || !player.IsSpawned) continue;
                players++;
                Vector3 feet = Feet(player);
                for (int i = 0; _plates != null && i < _plates.Length && i < 8; i++)
                    if (Inside(_plates[i], feet)) pressed |= (byte)(1 << i);
            }
            if (_pressed.Value != pressed) _pressed.Value = pressed;
            bool open = (_state.Value & OpenBit) != 0;
            if (pressed != 0) _openUntil = Time.time + (players <= 1 ? _soloOpenSeconds : _closeDelay);
            bool next = pressed != 0 || Time.time < _openUntil || (open && PassageOccupied());
            byte state = (byte)(next ? OpenBit : 0);
            if (_state.Value != state) _state.Value = state;
        }

        private static Vector3 Feet(NetworkObject player)
        {
            if (player.TryGetComponent(out CharacterController controller))
                return player.transform.TransformPoint(controller.center - Vector3.up * controller.height * .5f);
            return player.transform.position;
        }

        private static bool Inside(BoxCollider box, Vector3 point)
        {
            if (box == null || !box.gameObject.activeInHierarchy) return false;
            Vector3 p = box.transform.InverseTransformPoint(point) - box.center;
            Vector3 half = box.size * .5f;
            return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;
        }

        private bool PassageOccupied()
        {
            if (_passage == null) return false;
            foreach (var client in NetworkManager.ConnectedClientsList)
                if (client.PlayerObject != null && (Inside(_passage, client.PlayerObject.transform.position) || Inside(_passage, Feet(client.PlayerObject)))) return true;
            Transform t = _passage.transform;
            int count = Physics.OverlapBoxNonAlloc(t.TransformPoint(_passage.center), Vector3.Scale(_passage.size * .5f, t.lossyScale),
                _overlap, t.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (_overlap[i] != null && _overlap[i].GetComponentInParent<CoopGame.CarrySystem.CarryableObject>() != null) return true;
            return false;
        }

        private void ShowButtons(byte pressed)
        {
            bool anyPressedBefore = _shownPressed != byte.MaxValue && _shownPressed != 0;
            _shownPressed = pressed;
            for (int i = 0; _plateTops != null && i < _plateTops.Length; i++)
            {
                bool down = (pressed & (1 << i)) != 0;
                if (_plateTops[i] != null) _plateTops[i].localPosition = _topRest[i] + Vector3.down * (down ? .07f : 0f);
                if (_plateLights != null && i < _plateLights.Length && _plateLights[i] != null)
                    _plateLights[i].sharedMaterial = down ? _activeMaterial : _idleMaterial;
            }
            if (pressed != 0 && !anyPressedBefore) GameplayFeedback.Play(GameplayFeedback.Cue.Click, transform.position, .8f);
            if (_label != null)
                _label.text = pressed != 0 ? "มีคนกดปุ่มอยู่ ประตูเปิด\nรีบขนลังผ่านไปเลย" : "ยืนบนปุ่มค้างไว้ ประตูจะเปิด\nปล่อยแล้วประตูปิด";
        }
    }
}
