#if UNITY_EDITOR || DEBUG
using System;
using System.IO;
using System.Reflection;
using CoopGame.CarrySystem;
using CoopGame.Player;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.Network
{
    /// <summary>Opt-in development-player probe for repeatable localhost host/client checks.</summary>
    [DefaultExecutionOrder(500)]
    public sealed class ExpeditionNetworkProbe : MonoBehaviour
    {
        [Serializable] private sealed class Snapshot
        {
            public bool connected, cameraOwned, cargoSecured, failed, gateOpen, padded, failureUI;
            public string scene, error, command;
            public int hp, carriers, coins, card, slot1, slot2, slot3, score;
            public float stamina;
            public Vector3 cargoPosition, playerPosition;
        }
        private string _directory, _lastCommand, _error;
        private float _nextPoll;
        private Vector3 _teleport;
        private bool _teleportPending;
        private Action _fixedAction;
        public void QueuePhysicsAction(Action action) => _fixedAction += action;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (!Application.isEditor && !Debug.isDebugBuild) return;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] != "--expedition-probe") continue;
                var probe = new GameObject("ExpeditionNetworkProbe").AddComponent<ExpeditionNetworkProbe>();
                DontDestroyOnLoad(probe.gameObject);
                probe._directory = args[i + 1];
                Directory.CreateDirectory(probe._directory);
                Application.logMessageReceived += probe.RecordError;
                NetworkManager manager = NetworkManager.Singleton;
                SteamLobbyManager.EnsureInstance().SetTransportType(SteamLobbyManager.TransportType.UnityTransport);
                var transport = manager.GetComponent<UnityTransport>();
                transport.SetConnectionData("127.0.0.1", 7788);
                manager.NetworkConfig.NetworkTransport = transport;
                manager.StartClient();
                return;
            }
        }

        private void RecordError(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) _error = message;
        }
        private void OnDestroy() => Application.logMessageReceived -= RecordError;
        private void Update()
        {
            if (_directory == null || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + .25f;
            try
            {
                string path = Path.Combine(_directory, "command.txt");
                if (File.Exists(path))
                {
                    string command = File.ReadAllText(path).Trim();
                    if (command != _lastCommand) { _lastCommand = command; Execute(command); }
                }
                SaveSnapshot();
            }
            catch (Exception error) { _error = error.Message; }
        }

        private void Execute(string command)
        {
            string[] parts = command.Split('|');
            if (parts.Length < 2) return;
            var manager = NetworkManager.Singleton;
            if (parts[1] == "quit") { Application.Quit(); return; }
            if (parts[1] == "disconnect") { manager.Shutdown(); return; }
            var player = manager.LocalClient?.PlayerObject;
            if (player == null) return;
            var state = player.GetComponent<PlayerExpeditionState>();
            var carry = player.GetComponent<PlayerCarry>();
            switch (parts[1])
            {
                case "card": state.SelectCardRpc(byte.Parse(parts[2])); break;
                case "buy": state.BuyItemRpc(byte.Parse(parts[2])); break;
                case "use": state.UseItemRpc(int.Parse(parts[2])); break;
                case "lever":
                    var gate = FindAnyObjectByType<TeamLeverGate>();
                    if (gate != null) typeof(TeamLeverGate).GetMethod("HoldLeverRpc", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(gate, new object[] { default(RpcParams) });
                    break;
                case "teleport":
                    _teleport = new Vector3(float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture));
                    _teleportPending = true; break;
                case "grab":
                    var cargo = FindAnyObjectByType<FragileCargo>();
                    if (cargo == null) break;
                    carry.enabled = false;
                    typeof(PlayerCarry).GetMethod("RequestGrabServerRpc", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(carry,
                        new object[] { cargo.NetworkObjectId, new Vector3(-.25f, 0, -.35f), new Vector3(.25f, 0, -.35f), true, true });
                    break;
                case "drop": carry.DropForRespawn(); carry.enabled = true; break;
            }
        }

        private void FixedUpdate()
        {
            var action = _fixedAction;
            _fixedAction = null;
            action?.Invoke();
            if (!_teleportPending) return;
            var player = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (player == null) return;
            _teleportPending = false;
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = _teleport;
            player.GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(_teleport, player.transform.rotation, player.transform.localScale);
            controller.enabled = true;
            player.GetComponent<PlayerMovement>().ResetVelocity();
        }

        private void SaveSnapshot()
        {
            var manager = NetworkManager.Singleton;
            var player = manager.LocalClient?.PlayerObject;
            var cargo = FindAnyObjectByType<FragileCargo>();
            var mission = FindAnyObjectByType<LevelMission>();
            var state = player != null ? player.GetComponent<PlayerExpeditionState>() : null;
            var camera = player != null ? player.GetComponent<PlayerCameraController>() : null;
            var gate = FindAnyObjectByType<TeamLeverGate>();
            var snapshot = new Snapshot
            {
                connected = manager.IsConnectedClient, scene = SceneManager.GetActiveScene().name,
                error = _error, command = _lastCommand,
                cameraOwned = camera != null && camera == PlayerCameraController.LocalInstance && camera.PlayerCamera != null && camera.PlayerCamera.isActiveAndEnabled,
                hp = cargo != null ? cargo.CurrentHP.Value : -1,
                carriers = cargo != null ? cargo.CurrentCarrierCount : 0,
                cargoSecured = cargo != null && cargo.IsSecured,
                padded = cargo != null && cargo.HasPadding,
                gateOpen = gate != null && gate.IsOpen,
                failureUI = MissionFailUI.IsVisible,
                score = mission != null && mission.Delivery != null ? mission.Delivery.LastDeliveryResult.FinalScore : 0,
                stamina = player != null ? player.GetComponent<PlayerStamina>().CurrentStamina : 0,
                failed = mission != null && mission.Phase.Value == 3,
                cargoPosition = cargo != null ? cargo.transform.position : Vector3.zero,
                playerPosition = player != null ? player.transform.position : Vector3.zero,
                coins = state != null ? state.Coins.Value : -1, card = state != null ? state.Card.Value : 0,
                slot1 = state != null ? state.Slot1.Value : 0, slot2 = state != null ? state.Slot2.Value : 0, slot3 = state != null ? state.Slot3.Value : 0
            };
            File.WriteAllText(Path.Combine(_directory, "snapshot.json"), JsonUtility.ToJson(snapshot, true));
        }
    }
}
#endif
