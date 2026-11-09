using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Free cargo repair shop on an optional branch. The team PLACES the cargo on the pad (resting, not carried) and,
    /// after a short wait, the cargo gets HP back once per run. A full-HP cargo does not use the shop up. The server
    /// decides; every peer shows the same state. Optional branch root: the shop does nothing while that branch is hidden.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class RepairStation : NetworkBehaviour
    {
        [SerializeField] private FragileCargo _cargo;
        [SerializeField, Min(1)] private int _healAmount = 30;
        [Tooltip("Pad centre. Defaults to this transform.")]
        [SerializeField] private Transform _pad;
        [SerializeField, Min(.5f)] private float _halfExtent = 2.2f;
        [SerializeField, Min(.1f)] private float _maxCargoHeight = 2.4f;
        [SerializeField, Min(.1f)] private float _restSeconds = 1.5f;
        [SerializeField, Min(.01f)] private float _restSpeed = .35f;
        [Tooltip("If set, the shop only works while this branch is shown.")]
        [SerializeField] private GameObject _branchRoot;
        [SerializeField] private Renderer[] _lamps;
        [SerializeField] private Material _readyMaterial;
        [SerializeField] private Material _usedMaterial;
        [SerializeField] private TextMesh _label;

        private readonly NetworkVariable<bool> _used = new(false);
        private Rigidbody _cargoBody;
        private float _restTimer;
        private float _fullNoticeUntil;
        private int _shown = -1;

        public bool IsUsed => IsSpawned && _used.Value;

        public override void OnNetworkSpawn() { _shown = -1; _restTimer = 0f; }

        private Transform Pad => _pad != null ? _pad : transform;

        private void FixedUpdate()
        {
            if (!IsSpawned) return;
            if (IsServer && !_used.Value && _cargo != null && (_branchRoot == null || _branchRoot.activeInHierarchy)) ServerTick();
            int state = _used.Value ? 1 : 0;
            if (state == _shown) return;
            bool justUsed = _shown == 0 && state == 1;
            _shown = state;
            if (_lamps != null)
                for (int i = 0; i < _lamps.Length; i++)
                    if (_lamps[i] != null) _lamps[i].sharedMaterial = state == 1 ? _usedMaterial : _readyMaterial;
            if (_label != null)
                _label.text = state == 1 ? "ร้านช่างลัง\nซ่อมแล้ว +" + _healAmount + " HP" : "ร้านช่างลัง ซ่อมฟรี\nวางลังบนแท่น +" + _healAmount + " HP";
            if (justUsed)
            {
                GameplayFeedback.Play(GameplayFeedback.Cue.Checkpoint, Pad.position);
                GameplayFeedback.Celebrate(Pad.position + Vector3.up * .6f, new Color(.4f, 1f, .5f));
            }
        }

        private void ServerTick()
        {
            if (!CargoResting()) { _restTimer = 0f; return; }
            _restTimer += Time.fixedDeltaTime;
            if (_restTimer < _restSeconds) return;
            if (_cargo.CurrentHP.Value >= _cargo.MaxHP)
            {
                if (Time.time >= _fullNoticeUntil) { _fullNoticeUntil = Time.time + 4f; FullNoticeRpc(); }
                return;
            }
            if (_cargo.HealServer(_healAmount) > 0) _used.Value = true;
        }

        [Rpc(SendTo.Everyone)]
        private void FullNoticeRpc()
        {
            if (_label != null && !_used.Value) _label.text = "ร้านช่างลัง\nลังเต็มแล้ว ยังไม่ต้องซ่อม";
            CancelInvoke(nameof(ResetLabel));
            Invoke(nameof(ResetLabel), 3f);
        }

        private void ResetLabel() => _shown = -1;

        private bool CargoResting()
        {
            if (_cargo.IsDestroyed || _cargo.IsSecured || _cargo.IsCarried || _cargo.IsPlacing) return false;
            if (_cargoBody == null) _cargoBody = _cargo.GetComponent<Rigidbody>();
            Vector3 local = Pad.InverseTransformPoint(_cargo.ApproximateBoundsCenter);
            if (Mathf.Abs(local.x) > _halfExtent || Mathf.Abs(local.z) > _halfExtent) return false;
            if (local.y < -.25f || local.y > _maxCargoHeight) return false;
            return _cargoBody == null || _cargoBody.linearVelocity.sqrMagnitude <= _restSpeed * _restSpeed;
        }
    }
}
