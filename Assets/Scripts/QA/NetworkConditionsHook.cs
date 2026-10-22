#if UNITY_EDITOR || DEBUG
using System;
using System.IO;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.QA
{
    /// <summary>
    /// QA hook: puts Multiplayer Tools' Network Simulator (transport-level delay, jitter and packet loss)
    /// on the NetworkManager object. Off by default: with no configuration nothing is added and normal play
    /// is unchanged. Compiled only in the Editor and development builds (the simulator itself needs the same).
    ///
    /// Configuration sources (first one found wins):
    ///   1. command line:  -netsim-delay-ms 100 -netsim-jitter-ms 30 -netsim-loss-pct 2
    ///   2. file "netsim.cfg" next to the executable (or in the project root in the Editor), same switches or
    ///      "key=value" lines, e.g. "-netsim-delay-ms 100" or "delay-ms=100".
    ///   3. from code in the Editor: NetworkConditionsHook.Apply(delayMs, jitterMs, lossPct).
    ///
    /// The simulator applies the values to this peer's transport driver. Run it on both host and client for
    /// symmetric conditions. The effective values are logged as "[NetSim] ...".
    /// </summary>
    public static class NetworkConditionsHook
    {
        public const string ConfigFileName = "netsim.cfg";

        public static int DelayMs { get; private set; }
        public static int JitterMs { get; private set; }
        public static int LossPct { get; private set; }
        public static bool Active { get; private set; }

        /// <summary>The simulator component, or null when conditions are off.</summary>
        public static NetworkSimulator Simulator { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (!TryReadConfiguration(out int delay, out int jitter, out int loss)) return;
            Apply(delay, jitter, loss);
        }

        /// <summary>Sets (or updates) the simulated conditions. All zeros switches the simulator to a clean link.</summary>
        public static void Apply(int delayMs, int jitterMs, int lossPct)
        {
            DelayMs = Mathf.Max(0, delayMs);
            JitterMs = Mathf.Max(0, jitterMs);
            LossPct = Mathf.Clamp(lossPct, 0, 100);
            Active = DelayMs > 0 || JitterMs > 0 || LossPct > 0;
            var host = new GameObject("NetworkConditionsHookRunner").AddComponent<Runner>();
            UnityEngine.Object.DontDestroyOnLoad(host.gameObject);
            host.Begin();
        }

        public static bool TryReadConfiguration(out int delayMs, out int jitterMs, out int lossPct)
        {
            delayMs = jitterMs = lossPct = 0;
            bool found = false;
            string[] args = Environment.GetCommandLineArgs();
            found |= ParseTokens(args, ref delayMs, ref jitterMs, ref lossPct);
            if (!found)
            {
                string dir = Application.isEditor ? Path.GetDirectoryName(Application.dataPath) : Path.GetDirectoryName(Application.dataPath);
                string path = Path.Combine(dir ?? ".", ConfigFileName);
                if (File.Exists(path))
                {
                    var tokens = new System.Collections.Generic.List<string>();
                    foreach (string raw in File.ReadAllLines(path))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line[0] == '#') continue;
                        int eq = line.IndexOf('=');
                        if (eq > 0) { tokens.Add("-netsim-" + line.Substring(0, eq).Trim()); tokens.Add(line.Substring(eq + 1).Trim()); }
                        else tokens.AddRange(line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                    }
                    found = ParseTokens(tokens.ToArray(), ref delayMs, ref jitterMs, ref lossPct);
                }
            }
            return found;
        }

        private static bool ParseTokens(string[] args, ref int delayMs, ref int jitterMs, ref int lossPct)
        {
            bool found = false;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (!int.TryParse(args[i + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value)) continue;
                switch (args[i])
                {
                    case "-netsim-delay-ms": delayMs = value; found = true; break;
                    case "-netsim-jitter-ms": jitterMs = value; found = true; break;
                    case "-netsim-loss-pct": lossPct = value; found = true; break;
                }
            }
            return found;
        }

        private sealed class Runner : MonoBehaviour
        {
            public void Begin() => StartCoroutine(Install());

            private System.Collections.IEnumerator Install()
            {
                float deadline = Time.realtimeSinceStartup + 30f;
                while (NetworkManager.Singleton == null && Time.realtimeSinceStartup < deadline) yield return null;
                var manager = NetworkManager.Singleton;
                if (manager == null) { Debug.LogError("[NetSim] No NetworkManager found; simulated conditions NOT applied."); Destroy(gameObject); yield break; }
                var simulator = manager.GetComponent<NetworkSimulator>();
                if (simulator == null) simulator = manager.gameObject.AddComponent<NetworkSimulator>();
                simulator.ConnectionPreset = NetworkSimulatorPreset.Create("QA " + DelayMs + "ms/" + JitterMs + "ms/" + LossPct + "%",
                    "QA conditions", DelayMs, JitterMs, 0, LossPct);
                simulator.enabled = Active;
                Simulator = simulator;
                var preset = simulator.ConnectionPreset;
                Debug.Log($"[NetSim] effective: delay={preset.PacketDelayMs}ms jitter={preset.PacketJitterMs}ms loss={preset.PacketLossPercent}% " +
                          $"enabled={simulator.enabled} listening={manager.IsListening} (applied to this peer's transport; run on host and client for symmetric conditions)");
                // The simulator pushes its values whenever a transport driver registers. Log driver registration once.
                yield return new WaitUntil(() => manager.IsListening);
                yield return null;
                Debug.Log($"[NetSim] transport up: simulator available={simulator.IsAvailable} connected={simulator.IsConnected}");
                Destroy(gameObject);
            }
        }
    }
}
#endif
