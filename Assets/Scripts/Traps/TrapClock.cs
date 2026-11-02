using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Traps
{
    /// <summary>
    /// Shared clock for always-on traps. Every machine computes the same trap pose from the server's time, so traps
    /// need no NetworkObject and no per-frame network traffic. Damage to cargo is decided on the server only;
    /// knockdown is decided on the machine that owns the player.
    /// </summary>
    public static class TrapClock
    {
        private static double s_offset;
        private static int s_frame = -1;
        private static bool s_hasOffset;

        public static bool IsNetworked
        {
            get { var nm = NetworkManager.Singleton; return nm != null && nm.IsListening; }
        }

        /// <summary>Server time in seconds (local time when not networked). Smooth inside FixedUpdate.</summary>
        public static double Now
        {
            get
            {
                if (!IsNetworked) { s_hasOffset = false; return Time.timeAsDouble; }
                if (s_frame != Time.frameCount)
                {
                    s_frame = Time.frameCount;
                    double target = NetworkManager.Singleton.ServerTime.Time - Time.timeAsDouble;
                    if (!s_hasOffset || System.Math.Abs(target - s_offset) > .25) s_offset = target;
                    else s_offset += (target - s_offset) * .05;
                    s_hasOffset = true;
                }
                return Time.timeAsDouble + s_offset;
            }
        }

        /// <summary>True on the server, or when playing without a network session.</summary>
        public static bool IsAuthority => !IsNetworked || NetworkManager.Singleton.IsServer;

        public static NetworkObject LocalPlayer => IsNetworked ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;

        /// <summary>Stable 0..1 value from a position and a salt (same on every machine).</summary>
        public static float Hash01(Vector3 position, int salt)
        {
            return Hash01(Mathf.RoundToInt(position.x * 10f), Mathf.RoundToInt(position.y * 10f),
                Mathf.RoundToInt(position.z * 10f), salt);
        }

        public static float Hash01(int a, int b, int c, int salt)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                h = (h ^ (uint)c) * 16777619u;
                h = (h ^ (uint)salt) * 16777619u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
