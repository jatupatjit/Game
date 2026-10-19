using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// A pressure plate that a carryable object (a heavy stone) has to rest on. Resting there latches it: the bridge
    /// extends and the optional blocker disappears for the whole team. Used for the "place the cargo, swap roles"
    /// puzzle: the crate waits on a checkpoint pad while players deal with the stone.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class WeightPlate : NetworkBehaviour
    {
        [SerializeField] private CarryableObject _object;
        [SerializeField, Min(.3f)] private float _halfExtent = 1.5f;
        [SerializeField, Min(.1f)] private float _maxHeight = 1.9f;
        [SerializeField, Min(.05f)] private float _restSeconds = .6f;
        [SerializeField] private GameObject _bridge;
        [SerializeField] private GameObject _blocker;
        [SerializeField] private Renderer[] _lamps;
        [SerializeField] private Material _idleMaterial, _pressedMaterial;
        [SerializeField] private TextMesh _label;

        private readonly NetworkVariable<bool> _pressed = new(false);
        private float _restTimer;
        private int _shown = -1;
        private Rigidbody _body;
        private static readonly string Idle = "วางหินหนักบนแผ่นนี้\nสะพานจะยื่นออกมา";
        private static readonly string Done = "สะพานพร้อมแล้ว";

        public bool IsPressed => IsSpawned && _pressed.Value;

        public override void OnNetworkSpawn() { _shown = -1; _restTimer = 0f; }

        private void FixedUpdate()
        {
            if (!IsSpawned) return;
            if (IsServer && !_pressed.Value && _object != null) ServerTick();
            int state = _pressed.Value ? 1 : 0;
            if (state == _shown) return;
            _shown = state;
            bool on = state == 1;
            if (_bridge != null) _bridge.SetActive(on);
            if (_blocker != null) _blocker.SetActive(!on);
            if (_lamps != null)
                for (int i = 0; i < _lamps.Length; i++)
                    if (_lamps[i] != null) _lamps[i].sharedMaterial = on ? _pressedMaterial : _idleMaterial;
            if (_label != null) _label.text = on ? Done : Idle;
        }

        private void ServerTick()
        {
            bool resting = false;
            if (!_object.IsCarried && !_object.IsPlacing)
            {
                if (_body == null) _body = _object.GetComponent<Rigidbody>();
                Vector3 local = transform.InverseTransformPoint(_object.ApproximateBoundsCenter);
                resting = Mathf.Abs(local.x) <= _halfExtent && Mathf.Abs(local.z) <= _halfExtent &&
                    local.y >= -.3f && local.y <= _maxHeight &&
                    (_body == null || _body.linearVelocity.sqrMagnitude <= .16f);
            }
            _restTimer = resting ? _restTimer + Time.fixedDeltaTime : 0f;
            if (_restTimer >= _restSeconds) _pressed.Value = true;
        }
    }
}
