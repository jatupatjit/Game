using System.Collections.Generic;
using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.Network
{
    public enum DevCheat : byte
    {
        NextLevel, LoadLevel, JumpCheckpoint, ResetCheckpoint, RefillCrate, AddCoins,
        InfiniteStamina, GodMode, WeightPreset, ExtraMass, SimCarriers
    }

    /// <summary>
    /// Developer cheats (Editor and Development builds only; <see cref="DevCheatMenu"/> is the panel). Every request goes
    /// to the host (<see cref="PlayerExpeditionState.DevCheatRpc"/>), which acts and broadcasts the resulting state and
    /// a log line to every machine. The toggles are read by the gameplay code through the static fields below.
    /// </summary>
    public static class DevCheats
    {
        public static bool Allowed => Application.isEditor || Debug.isDebugBuild;

        // Synced state (set on every machine by PlayerExpeditionState.DevStateRpc).
        public static bool InfiniteStamina { get; private set; }
        public static bool GodMode { get; private set; }
        /// <summary>Added to every crate's mass (stamina drain, weight feel). 0 = off.</summary>
        public static float ExtraMass { get; private set; }
        /// <summary>Counts each carried crate as this many carriers when more than the real number (speed, drain). 0 = off.</summary>
        public static int SimCarriers { get; private set; }

        public static readonly List<string> Log = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            InfiniteStamina = GodMode = false;
            ExtraMass = 0f;
            SimCarriers = 0;
            Log.Clear();
        }

        public static int EffectiveCarriers(int real) => SimCarriers > real ? SimCarriers : real;

        public static string[] LevelNames()
        {
            var names = new string[SceneManager.sceneCountInBuildSettings];
            for (int i = 0; i < names.Length; i++)
                names[i] = System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));
            return names;
        }

        /// <summary>Host: carries out one request from <paramref name="sender"/>.</summary>
        public static void ExecuteOnServer(DevCheat cheat, int value, PlayerExpeditionState sender)
        {
            var manager = NetworkManager.Singleton;
            if (!Allowed || manager == null || !manager.IsServer) return;
            string who = sender == null ? "?" : "P" + (sender.OwnerClientId + 1) + (sender.IsOwnedByServer ? " (Host)" : "") +
                (sender.DisplayName.Value.Length > 0 ? " " + sender.DisplayName.Value : "");
            string done = null;
            var cargo = Object.FindAnyObjectByType<FragileCargo>();
            var checkpoints = CheckpointManager.Instance;
            switch (cheat)
            {
                case DevCheat.NextLevel:
                case DevCheat.LoadLevel:
                {
                    string[] levels = LevelNames();
                    int index = cheat == DevCheat.NextLevel ? (SceneManager.GetActiveScene().buildIndex + 1) % levels.Length : value;
                    if (index < 0 || index >= levels.Length) return;
                    if (manager.SceneManager.LoadScene(levels[index], LoadSceneMode.Single) != SceneEventProgressStatus.Started) return;
                    done = "loaded " + levels[index];
                    break;
                }
                case DevCheat.JumpCheckpoint:
                    if (checkpoints == null || !checkpoints.DevJumpTo(value - 1)) return;
                    if (cargo != null) cargo.RespawnAtSpawnPoint();
                    done = "jumped to checkpoint " + value;
                    break;
                case DevCheat.ResetCheckpoint:
                    if (cargo != null) cargo.RespawnAtSpawnPoint();
                    if (checkpoints != null) checkpoints.DevSendPlayersBack();
                    done = checkpoints != null && checkpoints.Current != null ? "back to checkpoint " + checkpoints.Current.Number : "back to the start";
                    break;
                case DevCheat.RefillCrate:
                    if (cargo == null) return;
                    cargo.HealServer(cargo.MaxHP);
                    done = "refilled crate HP";
                    break;
                case DevCheat.AddCoins:
                    if (sender == null) return;
                    sender.Coins.Value = Mathf.Min(1000000, sender.Coins.Value + Mathf.Max(0, value));
                    done = "+" + value + " coins";
                    break;
                case DevCheat.InfiniteStamina: InfiniteStamina = value != 0; done = "infinite stamina " + OnOff(InfiniteStamina); break;
                case DevCheat.GodMode: GodMode = value != 0; done = "no crate damage " + OnOff(GodMode); break;
                case DevCheat.WeightPreset: CarryWeightPreset.Set(value); done = "weight preset " + CarryWeightPreset.Name; break;
                case DevCheat.ExtraMass: ExtraMass = Mathf.Clamp(value, 0, 100); done = "crate mass +" + ExtraMass + " kg"; break;
                case DevCheat.SimCarriers: SimCarriers = Mathf.Clamp(value, 0, 4); done = SimCarriers == 0 ? "simulated carriers off" : "simulate " + SimCarriers + " carriers"; break;
            }
            if (done == null || sender == null) return;
            sender.DevStateRpc(Flags(), CarryWeightPreset.Index, ExtraMass, SimCarriers, who + ": " + done);
        }

        private static string OnOff(bool on) => on ? "ON" : "OFF";

        private static byte Flags() => (byte)((InfiniteStamina ? 1 : 0) | (GodMode ? 2 : 0));

        /// <summary>Every machine: the host's resulting cheat state plus a log line.</summary>
        public static void ApplyState(byte flags, int weightPreset, float extraMass, int simCarriers, string line)
        {
            InfiniteStamina = (flags & 1) != 0;
            GodMode = (flags & 2) != 0;
            ExtraMass = extraMass;
            SimCarriers = simCarriers;
            if (CarryWeightPreset.Index != weightPreset) CarryWeightPreset.Set(weightPreset);
            Log.Add(line);
            if (Log.Count > 8) Log.RemoveAt(0);
            Debug.Log("[DevCheat] " + line);
        }
    }
}
