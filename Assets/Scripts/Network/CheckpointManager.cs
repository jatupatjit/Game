using CoopGame.CarrySystem;
using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Owns the team's current respawn point for a level with <see cref="CheckpointPad"/>s.
    /// - Cargo falling off the map respawns on the last active pad (HP unchanged, FragileCargo calls TryGetCargoRespawn).
    /// - Cargo HP reaching 0 restarts from the last active pad: HP is set back to the value it had when the pad was
    ///   activated (never below the minimum) and the whole team is sent to the pad, so no state is a dead end.
    /// - A player falling off the map respawns on the pad instead of the level start.
    /// Level02 can reuse it by adding the same components. Without this component every cargo rule stays as before.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class CheckpointManager : NetworkBehaviour
    {
        public static CheckpointManager Instance { get; private set; }

        private const float PlayerPivotHeight = 1.05f;

        [SerializeField] private CheckpointPad[] _pads;
        [Tooltip("HP the cargo restarts with when its HP reaches 0, at minimum (the HP remembered at the pad is used when higher).")]
        [SerializeField, Range(1, 100)] private int _restartMinHp = 25;
        [SerializeField] private bool _restartOnZeroHp = true;

        private readonly NetworkVariable<int> _current = new(-1);
        private readonly NetworkVariable<int> _savedHp = new(100);

        public bool RestartOnZeroHp => _restartOnZeroHp;
        public int CurrentIndex => _current.Value;
        public int SavedHp => _savedHp.Value;
        public int PadCount => _pads != null ? _pads.Length : 0;
        public CheckpointPad Current => _current.Value >= 0 && _pads != null && _current.Value < _pads.Length ? _pads[_current.Value] : null;
        public CheckpointPad PadAt(int index) => _pads != null && index >= 0 && index < _pads.Length ? _pads[index] : null;
        /// <summary>Server counters, for tests and tuning.</summary>
        public int RestartCount { get; private set; }
        public int CargoFallCount { get; private set; }
        public int LastRestartHp { get; private set; }

        // A destroyed instance compares equal to null (Unity), so a stale reference after a scene change is harmless.
        private void Awake() => Instance = this;

        /// <summary>Server: a pad has just been activated by the placed cargo.</summary>
        public void OnPadPlaced(CheckpointPad pad, int cargoHp)
        {
            if (!IsServer || !IsSpawned || _pads == null) return;
            int index = System.Array.IndexOf(_pads, pad);
            if (index < 0) return;
            bool isNew = _current.Value != index;
            _current.Value = index;
            _savedHp.Value = Mathf.Clamp(cargoHp, 1, 100);
            if (isNew && LevelRunState.Instance != null) LevelRunState.Instance.PostNotice(LevelNotice.CheckpointSaved);
        }

        public bool TryGetCargoRespawn(out Vector3 position, out Quaternion rotation)
        {
            var pad = Current;
            if (pad != null && pad.IsActive)
            {
                position = pad.CargoPoint.position;
                rotation = pad.CargoPoint.rotation;
                return true;
            }
            position = default; rotation = default;
            return false;
        }

        public bool TryGetPlayerRespawn(int slot, out Vector3 position, out Quaternion rotation)
        {
            var pad = Current;
            if (pad != null && pad.IsActive)
            {
                Transform t = pad.PlayerPoint;
                Vector3 offset = slot switch { 1 => new Vector3(1.8f, 0f, 0f), 2 => new Vector3(-1.8f, 0f, 0f), 3 => new Vector3(0f, 0f, -1.8f), _ => Vector3.zero };
                position = t.TransformPoint(offset);
                // The player's pivot sits about a metre above its feet (the level start spawns at ground + 1.05).
                // A pad's respawn point placed on the ground would embed the capsule in the island and drop the
                // player straight through it, so never respawn lower than that height above the pad surface.
                position.y = Mathf.Max(position.y, pad.transform.position.y + PlayerPivotHeight);
                rotation = t.rotation;
                return true;
            }
            position = default; rotation = default;
            return false;
        }

        /// <summary>Server: the cargo fell below the kill plane and is being put back on the last pad.</summary>
        public void NotifyCargoFell()
        {
            if (!IsServer) return;
            CargoFallCount++;
            if (LevelRunState.Instance != null) LevelRunState.Instance.PostNotice(LevelNotice.CargoFell);
        }

        /// <summary>Server: cargo HP reached 0. Returns the HP to restart with and sends the team to the pad.</summary>
        public int BeginRestart()
        {
            RestartCount++;
            int hp = Mathf.Clamp(Mathf.Max(_savedHp.Value, _restartMinHp), 1, 100);
            LastRestartHp = hp;
            if (LevelRunState.Instance != null) LevelRunState.Instance.PostNotice(LevelNotice.CargoRestart);
            if (IsSpawned) RestartPlayersRpc();
            return hp;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RestartPlayersRpc()
        {
            var manager = NetworkManager.Singleton;
            var player = manager != null && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            if (player != null && player.TryGetComponent<NetworkPlayer>(out var networkPlayer)) networkPlayer.RespawnAtCheckpoint();
        }
    }
}
