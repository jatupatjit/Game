#if UNITY_EDITOR || DEBUG
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CoopGame.CarrySystem;
using CoopGame.Player;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CoopGame.Network
{
    /// <summary>Opt-in development-player probe for repeatable localhost host/client checks.</summary>
    [DefaultExecutionOrder(500)]
    public sealed class ExpeditionNetworkProbe : MonoBehaviour
    {
        [Serializable] private sealed class PlayerSnapshot
        {
            public ulong owner;
            public int face;
            public bool carrying, samplingAnimatorReady, leftGripping, rightGripping;
            public float leftWeight, rightWeight, leftGrip, rightGrip;
            public Vector3 leftTarget, rightTarget, leftWrist, rightWrist;
        }
        [Serializable] private sealed class Snapshot
        {
            public float realtime, yaw, pitch, speed;
            public Vector2 moveInput;
            public bool grounded, climbing;
            public MechanismSnapshot[] mechanisms;
            public bool connected, cameraOwned, cargoSecured, failed, gateOpen, relayBridgeOpen, wallSwitchAOpen, wallSwitchBOpen, padded, failureUI, carrying, damagePopup;
            public byte trapAPhase, trapBPhase;
            public string scene, error, command, hpLabel, damageLabel, cursor;
            public int hp, carriers, coins, card, slot1, slot2, slot3, score, face;
            public float stamina, hpFill;
            public float overreachTime, leftOverreach, rightOverreach;
            public bool leftGripping, rightGripping;
            public Vector3 cargoPosition, playerPosition;
            public Vector3 cargoPhysicsPosition, cargoEuler, cargoPhysicsEuler;
            public Vector3 shoulderLeft, shoulderRight, contactLeft, contactRight;
            public float reachLeft, reachRight;
            public PlayerSnapshot[] players;
        }
        [Serializable] private sealed class MechanismSnapshot
        {
            public string name;
            public Vector3 position, euler, button, primary, secondary;
            public bool open, near;
            public int phase;
        }
        private string _directory, _lastCommand, _error;
        private Keyboard _probeKeyboard;
        private Mouse _probeMouse;
        private Key[] _keys = Array.Empty<Key>();
        private bool _leftMouse, _rightMouse;
        private float _inputUntil;
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
                Application.runInBackground = true;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                probe._probeKeyboard = InputSystem.AddDevice<Keyboard>("ExpeditionProbeKeyboard");
                probe._probeMouse = InputSystem.AddDevice<Mouse>("ExpeditionProbeMouse");
                Directory.CreateDirectory(probe._directory);
                Application.logMessageReceived += probe.RecordError;
                bool host = false;
                for (int j = 0; j + 1 < args.Length; j++)
                {
                    if (args[j] != "--expedition-role") continue;
                    if (args[j + 1] != "host" && args[j + 1] != "client")
                    {
                        Debug.LogError("[ExpeditionNetworkProbe] Role must be host or client.");
                        return;
                    }
                    host = args[j + 1] == "host";
                }
                probe.StartCoroutine(probe.StartLocalNetwork(host));
                return;
            }
        }

        private IEnumerator StartLocalNetwork(bool host)
        {
            // NGO's generated serializers also register after scene load. Let all
            // runtime initializers and component Start methods finish before spawning.
            yield return null;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (NetworkManager.Singleton == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            var manager = NetworkManager.Singleton;
            if (manager == null || manager.IsListening)
            {
                _error = "No idle NetworkManager available for local playtest.";
                Debug.LogError("[ExpeditionNetworkProbe] " + _error);
                yield break;
            }
            var transport = manager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                _error = "UnityTransport missing; local playtest cannot start.";
                Debug.LogError("[ExpeditionNetworkProbe] " + _error);
                yield break;
            }
            SteamLobbyManager.EnsureInstance().SetTransportType(SteamLobbyManager.TransportType.UnityTransport);
            transport.SetConnectionData("127.0.0.1", 7788);
            manager.NetworkConfig.NetworkTransport = transport;
            bool started = host ? manager.StartHost() : manager.StartClient();
            if (!started)
            {
                _error = "Local " + (host ? "host" : "client") + " failed to start.";
                Debug.LogError("[ExpeditionNetworkProbe] " + _error);
            }
        }

        private void RecordError(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) _error = message;
        }
        private void OnDestroy()
        {
            Application.logMessageReceived -= RecordError;
            if (_probeKeyboard != null) InputSystem.RemoveDevice(_probeKeyboard);
            if (_probeMouse != null) InputSystem.RemoveDevice(_probeMouse);
        }
        private void Update()
        {
            // Development-only virtual devices exercise the production input actions.
            // They do not move bodies, set grips or bypass server interaction validation.
            if (_probeKeyboard != null)
            {
                bool held = Time.unscaledTime < _inputUntil;
                InputSystem.QueueStateEvent(_probeKeyboard, new KeyboardState(held ? _keys : Array.Empty<Key>()));
                var mouse = new MouseState();
                mouse = mouse.WithButton(MouseButton.Left, held && _leftMouse).WithButton(MouseButton.Right, held && _rightMouse);
                InputSystem.QueueStateEvent(_probeMouse, mouse);
            }
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
                case "input":
                    _inputUntil = Time.unscaledTime + float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                    _keys = parts.Length > 3 && parts[3].Length > 0 ? parts[3].Split(',').Select(k => (Key)Enum.Parse(typeof(Key), k, true)).ToArray() : Array.Empty<Key>();
                    _leftMouse = parts.Length > 4 && parts[4].Contains("L");
                    _rightMouse = parts.Length > 4 && parts[4].Contains("R");
                    break;
                case "look":
                    var view = player.GetComponent<PlayerCameraController>();
                    typeof(PlayerCameraController).GetField("_yaw", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(view, float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
                    typeof(PlayerCameraController).GetField("_pitch", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(view, float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "scene":
                    if (manager.IsServer) manager.SceneManager.LoadScene(parts[2], LoadSceneMode.Single);
                    break;
                case "capture": ScreenCapture.CaptureScreenshot(Path.Combine(_directory, parts[2] + ".png")); break;
                case "cargo_setup":
                    // Explicit diagnostic setup, never counted as traversal evidence.
                    if (!manager.IsServer) break;
                    var setupPosition = new Vector3(float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture));
                    QueuePhysicsAction(() => {
                        var setupCargo = FindAnyObjectByType<FragileCargo>();
                        if (setupCargo == null) return;
                        var body = setupCargo.GetComponent<Rigidbody>();
                        body.position = setupPosition; body.rotation = Quaternion.identity;
                        body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
                    });
                    break;
                case "card": state.SelectCardRpc(byte.Parse(parts[2])); break;
                case "buy": state.BuyItemRpc(byte.Parse(parts[2])); break;
                case "use": state.UseItemRpc(int.Parse(parts[2])); break;
                case "face": PlayerAppearance.SaveLocal(int.Parse(parts[2])); break;
                case "face_rpc":
                    typeof(PlayerAppearance).GetMethod("SetFaceRpc", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(player.GetComponent<PlayerAppearance>(), new object[] { int.Parse(parts[2]), default(RpcParams) });
                    break;
                case "pause": PauseMenu.Instance?.ShowPause(); break;
                case "resume": PauseMenu.Instance?.HidePause(); break;
                case "left": carry.TryGrabNearbyObject(true, false); break;
                case "right": carry.TryGrabNearbyObject(false, true); break;
                case "lever":
                    var gate = FindAnyObjectByType<TeamLeverGate>();
                    if (gate != null) typeof(TeamLeverGate).GetMethod("HoldLeverRpc", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(gate, new object[] { default(RpcParams) });
                    break;
                case "switch":
                    var highSwitch = GameObject.Find(parts[2] == "B" ? "WallSwitch_B" : "WallSwitch_A")?.GetComponent<ClimbSwitchGate>();
                    if (highSwitch != null) typeof(ClimbSwitchGate).GetMethod("PressSwitchRpc", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(highSwitch, new object[] { default(RpcParams) });
                    break;
                case "teleport":
                    _teleport = new Vector3(float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture));
                    _teleportPending = true; break;
                case "grab":
                    var cargo = FindAnyObjectByType<FragileCargo>();
                    if (cargo == null) break;
                    // Exercise the same queued, contact-validated path used by gameplay.
                    carry.InitiateGrab(cargo, true, true);
                    break;
                case "drop": carry.DropForRespawn(); break;
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
            if (manager == null) return;
            var player = manager.LocalClient?.PlayerObject;
            var cargo = FindAnyObjectByType<FragileCargo>();
            var mission = FindAnyObjectByType<LevelMission>();
            var state = player != null ? player.GetComponent<PlayerExpeditionState>() : null;
            var camera = player != null ? player.GetComponent<PlayerCameraController>() : null;
            var gate = FindAnyObjectByType<TeamLeverGate>();
            var healthUI = cargo != null ? cargo.GetComponent<FragileCargoHealthUI>() : null;
            const BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
            var label = healthUI != null ? typeof(FragileCargoHealthUI).GetField("_label", privateInstance)?.GetValue(healthUI) as UnityEngine.UI.Text : null;
            var damage = healthUI != null ? typeof(FragileCargoHealthUI).GetField("_damageText", privateInstance)?.GetValue(healthUI) as UnityEngine.UI.Text : null;
            var fill = healthUI != null ? typeof(FragileCargoHealthUI).GetField("_fill", privateInstance)?.GetValue(healthUI) as UnityEngine.UI.Image : null;
            var snapshot = new Snapshot
            {
                realtime = Time.realtimeSinceStartup,
                yaw = camera != null ? camera.Yaw : 0, pitch = camera != null ? camera.Pitch : 0,
                speed = player != null ? player.GetComponent<PlayerMovement>().CurrentSpeed : 0,
                moveInput = player != null ? player.GetComponent<PlayerInputReader>().MoveInput : Vector2.zero,
                grounded = player != null && player.GetComponent<PlayerMovement>().IsGrounded,
                climbing = player != null && player.GetComponent<Wallclimb>().IsClimbing,
                mechanisms = FindObjectsByType<ClimbSwitchGate>().Select(g => new MechanismSnapshot {
                    name=g.name, position=g.transform.position, euler=g.transform.eulerAngles, open=g.IsOpen, near=g.IsLocalNear,
                    button=((Transform)typeof(ClimbSwitchGate).GetField("_button", privateInstance).GetValue(g)).position
                }).Concat(FindObjectsByType<NetworkRouteHazard>().Select(g => new MechanismSnapshot {
                    name=g.name, position=g.transform.position,euler=g.transform.eulerAngles,phase=g.Phase,
                    primary=((Rigidbody)typeof(NetworkRouteHazard).GetField("_primary",privateInstance).GetValue(g)).position,
                    secondary=typeof(NetworkRouteHazard).GetField("_secondary",privateInstance).GetValue(g) is Rigidbody second ? second.position : Vector3.zero
                })).Concat(FindObjectsByType<RotatingLogHazard>().Select(g => new MechanismSnapshot {
                    name=g.name,position=g.transform.position,euler=g.transform.eulerAngles,
                    primary=((Rigidbody)typeof(RotatingLogHazard).GetField("_logRigidbody",privateInstance).GetValue(g)).rotation.eulerAngles
                })).ToArray(),
                connected = manager.IsConnectedClient, scene = SceneManager.GetActiveScene().name,
                error = _error, command = _lastCommand,
                cameraOwned = camera != null && camera == PlayerCameraController.LocalInstance && camera.PlayerCamera != null && camera.PlayerCamera.isActiveAndEnabled,
                hp = cargo != null ? cargo.CurrentHP.Value : -1,
                hpLabel = label != null ? label.text : null, hpFill = fill != null ? fill.fillAmount : -1f,
                damageLabel = damage != null ? damage.text : null, damagePopup = damage != null && damage.enabled,
                cursor = Cursor.lockState.ToString(),
                carrying = player != null && player.GetComponent<PlayerCarry>().IsCarrying,
                face = player != null ? player.GetComponent<PlayerAppearance>().CurrentFace : -1,
                carriers = cargo != null ? cargo.CurrentCarrierCount : 0,
                cargoSecured = cargo != null && cargo.IsSecured,
                padded = cargo != null && cargo.HasPadding,
                gateOpen = gate != null && gate.IsOpen,
                relayBridgeOpen = FindAnyObjectByType<CoopRelayBridge>() is CoopRelayBridge relay && relay.IsDeployed,
                wallSwitchAOpen = GameObject.Find("WallSwitch_A")?.GetComponent<ClimbSwitchGate>()?.IsOpen ?? false,
                wallSwitchBOpen = GameObject.Find("WallSwitch_B")?.GetComponent<ClimbSwitchGate>()?.IsOpen ?? false,
                trapAPhase = GameObject.Find("FlameTrap_A")?.GetComponent<TimedCargoTrap>()?.Phase ?? 0,
                trapBPhase = GameObject.Find("FlameTrap_B")?.GetComponent<TimedCargoTrap>()?.Phase ?? 0,
                failureUI = MissionFailUI.IsVisible,
                score = mission != null && mission.Delivery != null ? mission.Delivery.LastDeliveryResult.FinalScore : 0,
                stamina = player != null ? player.GetComponent<PlayerStamina>().CurrentStamina : 0,
                leftGripping = player != null && player.GetComponent<PlayerCarry>().LeftHandGripping,
                rightGripping = player != null && player.GetComponent<PlayerCarry>().RightHandGripping,
                overreachTime = player != null ? (float)typeof(PlayerCarry).GetField("_overreachTime", privateInstance).GetValue(player.GetComponent<PlayerCarry>()) : 0,
                leftOverreach = GripOverreach(player, true), rightOverreach = GripOverreach(player, false),
                failed = mission != null && mission.Phase.Value == 3,
                cargoPosition = cargo != null ? cargo.transform.position : Vector3.zero,
                cargoPhysicsPosition = cargo != null ? cargo.GetComponent<Rigidbody>().position : Vector3.zero,
                cargoEuler = cargo != null ? cargo.transform.eulerAngles : Vector3.zero,
                cargoPhysicsEuler = cargo != null ? cargo.GetComponent<Rigidbody>().rotation.eulerAngles : Vector3.zero,
                playerPosition = player != null ? player.transform.position : Vector3.zero,
                coins = state != null ? state.Coins.Value : -1, card = state != null ? state.Card.Value : 0,
                slot1 = state != null ? state.Slot1.Value : 0, slot2 = state != null ? state.Slot2.Value : 0, slot3 = state != null ? state.Slot3.Value : 0,
                players = manager.SpawnManager == null ? Array.Empty<PlayerSnapshot>() : manager.SpawnManager.SpawnedObjectsList.Where(o => o.IsPlayerObject).Select(o => new PlayerSnapshot
                {
                    owner = o.OwnerClientId, face = o.GetComponent<PlayerAppearance>().CurrentFace,
                    carrying = o.GetComponent<PlayerCarry>().IsCarrying,
                    samplingAnimatorReady = o.GetComponentInChildren<Animator>() != null,
                    leftGripping = o.GetComponent<PlayerCarry>().LeftHandGripping,
                    rightGripping = o.GetComponent<PlayerCarry>().RightHandGripping,
                    leftWeight = o.GetComponent<ProceduralPlayerArms>().LeftWeight,
                    rightWeight = o.GetComponent<ProceduralPlayerArms>().RightWeight,
                    leftGrip = o.GetComponent<ProceduralPlayerArms>().LeftGripWeight,
                    rightGrip = o.GetComponent<ProceduralPlayerArms>().RightGripWeight,
                    leftTarget = o.GetComponent<ProceduralPlayerArms>().LeftHand.position,
                    rightTarget = o.GetComponent<ProceduralPlayerArms>().RightHand.position,
                    leftWrist = o.GetComponent<ProceduralPlayerArms>().WristLeft != null ? o.GetComponent<ProceduralPlayerArms>().WristLeft.position : Vector3.zero,
                    rightWrist = o.GetComponent<ProceduralPlayerArms>().WristRight != null ? o.GetComponent<ProceduralPlayerArms>().WristRight.position : Vector3.zero
                }).ToArray()
            };
            if (player != null)
            {
                var arms = player.GetComponent<ProceduralPlayerArms>();
                arms.TryGetCarryPhysicsGeometry(true, out snapshot.shoulderLeft, out snapshot.reachLeft);
                arms.TryGetCarryPhysicsGeometry(false, out snapshot.shoulderRight, out snapshot.reachRight);
                var carry = player.GetComponent<PlayerCarry>();
                if (carry.IsCarrying)
                {
                    var physicalContact = typeof(PlayerCarry).GetMethod("GetPhysicalContact", privateInstance);
                    snapshot.contactLeft = (Vector3)physicalContact.Invoke(carry, new object[] { true });
                    snapshot.contactRight = (Vector3)physicalContact.Invoke(carry, new object[] { false });
                }
            }
            File.WriteAllText(Path.Combine(_directory, "snapshot.json"), JsonUtility.ToJson(snapshot, true));
            File.AppendAllText(Path.Combine(_directory, "timeline.jsonl"), JsonUtility.ToJson(snapshot) + Environment.NewLine);
        }

        private static float GripOverreach(NetworkObject player, bool left)
        {
            if (player == null) return 0;
            var carry = player.GetComponent<PlayerCarry>();
            if (carry == null || !carry.IsCarrying) return 0;
            return (float)typeof(PlayerCarry).GetMethod("GripOverreach", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(carry, new object[] { left });
        }
    }
}
#endif
