#if UNITY_EDITOR || DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using CoopGame.CarrySystem;
using CoopGame.Network;
using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace CoopGame.QA
{
    /// <summary>
    /// QA bot for the two-player carry under simulated transport conditions (see NetworkConditionsHook).
    /// Runs in a development build as host or client (started by the ExpeditionNetworkProbe network start,
    /// "--expedition-probe dir --expedition-role host|client"). Enabled only by "-qa-netsim-carry sessionDir".
    /// The host drives the phases and writes sessionDir/result.json; the client follows timed commands in
    /// sessionDir/ctl and writes sessionDir/client.json. Pass/fail thresholds live in the runner script.
    /// Args: -qa-role host|client  -qa-expect-oneway-ms N  -qa-nocomp (client forces its measured transit to 0).
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class NetworkConditionsCarryQa : MonoBehaviour
    {
        private const int Setup = 0, Stand = 1, Walk = 2, Sprint = 3, Stop = 4, Release = 5, Regrab = 6, Finish = 7;
        private static readonly string[] PhaseNames = { "setup", "stand", "walk", "sprint", "stop", "release", "regrab", "finish" };
        private const float GripGap = 1.45f;
        private const float WalkTarget = 10f, SprintTarget = 10f;
        private static readonly DateTime Epoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static double Now() => (DateTime.UtcNow - Epoch).TotalSeconds;

        // ---- configuration ----
        private string _dir, _ctl;
        private bool _host, _noComp;
        private float _expectOneWayMs;

        // ---- shared state ----
        private int _phase;
        private readonly List<(double at, string cmd, string args)> _pending = new();
        private int _nextCtl = 1;
        private int _ctlSeq;
        private Keyboard _keyboard;
        private Key[] _keys = Array.Empty<Key>();
        private readonly List<string> _errors = new();
        private int _errorCount;
        private bool _disconnected;
        private string _abort;
        private bool _intendedRelease;
        private int _drops;
        private bool _wasCarrying;
        private Action _fixedAction;
        private FieldInfo _yawField, _pitchField, _oneWayField;

        // ---- recording ----
        private struct HostSample { public double t; public int phase; public Vector3 pos; public Quaternion rot; public float ratio; public int carriers; public bool hostHold, clientHold; }
        private struct CrateView { public double t; public Vector3 pos; }
        private struct ClientSample { public double t; public float ratio, oneWay; }
        private struct HandSample { public double t; public float remote, own; } // largest hand error among gripping hands, -1 when none
        private readonly List<HostSample> _hostSamples = new();
        private readonly List<CrateView> _crateViews = new();
        private readonly List<ClientSample> _clientSamples = new();
        private readonly List<HandSample> _handSamples = new();
        private readonly double[] _phaseStart = new double[8];

        // crate geometry (host)
        private Vector3 _boxCenter, _boxHalf, _boxScale;
        private int _longAxis;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            string[] args = Environment.GetCommandLineArgs();
            string dir = null, role = null; bool noComp = false; float expect = 0f;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-qa-netsim-carry" && i + 1 < args.Length) dir = args[i + 1];
                else if (args[i] == "-qa-role" && i + 1 < args.Length) role = args[i + 1];
                else if (args[i] == "-qa-nocomp") noComp = true;
                else if (args[i] == "-qa-expect-oneway-ms" && i + 1 < args.Length) float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out expect);
            }
            if (dir == null || (role != "host" && role != "client")) return;
            var qa = new GameObject("NetworkConditionsCarryQa").AddComponent<NetworkConditionsCarryQa>();
            DontDestroyOnLoad(qa.gameObject);
            qa._dir = dir; qa._ctl = Path.Combine(dir, "ctl"); qa._host = role == "host"; qa._noComp = noComp; qa._expectOneWayMs = expect;
            Directory.CreateDirectory(qa._ctl);
            qa.StartCoroutine(qa._host ? qa.HostRoutine() : qa.ClientRoutine());
        }

        private void Awake()
        {
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _keyboard = InputSystem.AddDevice<Keyboard>("QaNetSimKeyboard");
            // This process is a scripted test peer: keep physical keyboard/mouse/gamepad out of it.
            foreach (var d in InputSystem.devices.ToArray())
                if (d != _keyboard && d.enabled && (d is Keyboard || d is Mouse || d is Gamepad)) InputSystem.DisableDevice(d);
            Application.logMessageReceived += OnLog;
            const BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            _yawField = typeof(PlayerCameraController).GetField("_yaw", f);
            _pitchField = typeof(PlayerCameraController).GetField("_pitch", f);
            _oneWayField = typeof(PlayerCarry).GetField("_measuredOneWay", f);
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
        }

        private void OnLog(string message, string trace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            _errorCount++;
            if (_errors.Count < 8) _errors.Add(message.Length > 220 ? message.Substring(0, 220) : message);
        }

        // ------------------------------------------------------------------ per-frame plumbing
        private void Update()
        {
            if (_keyboard != null) InputSystem.QueueStateEvent(_keyboard, new KeyboardState(_keys));
            if (!_host) PollControlFiles();
            double now = Now();
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].at > now) continue;
                var c = _pending[i]; _pending.RemoveAt(i);
                try { Execute(c.cmd, c.args); } catch (Exception e) { Debug.LogError("[QA] command " + c.cmd + " failed: " + e); }
            }
            RecordCrateView();
            TryPickCard();
        }

        private void LateUpdate() => RecordHands();

        private byte _cardId;
        private float _nextCardTry;
        /// <summary>The card pick is a normal part of the stage start (modal blocks input). Take the first offered card that does not touch carrying, so every scenario carries under the same rules.</summary>
        private void TryPickCard()
        {
            if (_cardId != 0 || Time.unscaledTime < _nextCardTry) return;
            _nextCardTry = Time.unscaledTime + .5f;
            var manager = NetworkManager.Singleton;
            var local = manager != null && manager.IsListening ? manager.LocalClient?.PlayerObject : null;
            if (local == null || SceneManager.GetActiveScene().name != "Level01") return;
            var state = local.GetComponent<PlayerExpeditionState>();
            if (state == null || !state.IsSpawned) return;
            if (state.Card.Value != 0) { _cardId = state.Card.Value; return; }
            uint offer = state.Offer.Value;
            if (!CardCatalog.HasOffer(offer)) return;
            byte pick = 0;
            foreach (byte preferred in new byte[] { 2, 3, 4, 8, 9, 10, 7, 12, 11 })
                for (int s = 0; s < CardCatalog.OfferSize && pick == 0; s++) if (CardCatalog.OfferAt(offer, s) == preferred) pick = preferred;
            if (pick == 0) pick = CardCatalog.OfferAt(offer, 0);
            Debug.Log($"[QA] offer {CardCatalog.OfferAt(offer, 0)},{CardCatalog.OfferAt(offer, 1)},{CardCatalog.OfferAt(offer, 2)} -> pick {pick}");
            state.SelectCardRpc(pick);
        }
        private int ClientCardId()
        {
            try { string[] p = File.ReadAllText(Path.Combine(_dir, "client_status.txt")).Split(','); return int.Parse(p[5]); } catch { return 0; }
        }

        private void FixedUpdate()
        {
            var act = _fixedAction; _fixedAction = null; act?.Invoke();
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) return;
            var local = manager.LocalClient?.PlayerObject;
            var carry = local != null ? local.GetComponent<PlayerCarry>() : null;
            if (carry == null) return;
            if (!_host && _noComp && _oneWayField != null) _oneWayField.SetValue(carry, 0f); // partial mutation: client transit measurement forced to 0
            bool carrying = carry.IsCarrying;
            if (_phase >= Stand && _phase <= Stop && _wasCarrying && !carrying && !_intendedRelease)
            {
                _drops++;
                Debug.LogWarning($"[QA] UNINTENDED RELEASE on {(_host ? "host" : "client")} in phase {PhaseNames[_phase]}");
            }
            _wasCarrying = carrying;
            if (_host) RecordHostSample(manager, carry);
            else
            {
                float oneWay = _oneWayField != null ? (float)_oneWayField.GetValue(carry) : -1f;
                _clientSamples.Add(new ClientSample { t = Now(), ratio = carry.LastCarryAllowedRatio, oneWay = oneWay });
            }
        }

        private FragileCargo FindCargo() => FindObjectsByType<FragileCargo>().FirstOrDefault(c => c.gameObject.scene.name == "Level01");

        private void RecordHostSample(NetworkManager manager, PlayerCarry hostCarry)
        {
            var cargo = FindCargo(); if (cargo == null) return;
            var rb = cargo.GetComponent<Rigidbody>();
            PlayerCarry other = null;
            foreach (var o in manager.SpawnManager.SpawnedObjectsList)
                if (o.IsPlayerObject && !o.IsOwner) other = o.GetComponent<PlayerCarry>();
            _hostSamples.Add(new HostSample { t = Now(), phase = _phase, pos = rb.position, rot = rb.rotation, ratio = hostCarry.LastCarryAllowedRatio,
                carriers = cargo.CurrentCarrierCount, hostHold = hostCarry.IsCarrying, clientHold = other != null && other.IsCarrying });
        }

        private void RecordCrateView()
        {
            if (_phase < Stand) return;
            var cargo = FindCargo(); if (cargo == null) return;
            _crateViews.Add(new CrateView { t = Now(), pos = cargo.transform.position });
        }

        private void RecordHands()
        {
            var manager = NetworkManager.Singleton;
            if (_phase < Stand || manager == null || !manager.IsListening || manager.SpawnManager == null) return;
            float remote = -1f, own = -1f;
            foreach (var o in manager.SpawnManager.SpawnedObjectsList)
            {
                if (!o.IsPlayerObject) continue;
                var carry = o.GetComponent<PlayerCarry>(); var arms = o.GetComponent<ProceduralPlayerArms>();
                if (carry == null || arms == null) continue;
                float worst = -1f;
                if (arms.WristLeft != null && carry.TryGetRenderedGripContact(true, out Vector3 cl)) worst = Mathf.Max(worst, Vector3.Distance(arms.WristLeft.position, cl));
                if (arms.WristRight != null && carry.TryGetRenderedGripContact(false, out Vector3 cr)) worst = Mathf.Max(worst, Vector3.Distance(arms.WristRight.position, cr));
                if (o.IsOwner) own = worst; else remote = worst;
            }
            _handSamples.Add(new HandSample { t = Now(), remote = remote, own = own });
        }

        // ------------------------------------------------------------------ commands
        private void Broadcast(string cmd, string args, float delay = .12f)
        {
            double at = Now() + delay;
            string path = Path.Combine(_ctl, "c" + (++_ctlSeq) + ".txt");
            File.WriteAllText(path + ".tmp", at.ToString("R", CultureInfo.InvariantCulture) + "\n" + cmd + "\n" + args);
            File.Move(path + ".tmp", path);
            _pending.Add((at, cmd, args));
        }

        private void PollControlFiles()
        {
            string path = Path.Combine(_ctl, "c" + _nextCtl + ".txt");
            while (File.Exists(path))
            {
                string[] lines;
                try { lines = File.ReadAllLines(path); } catch (IOException) { return; }
                if (lines.Length < 2) return;
                _pending.Add((double.Parse(lines[0], CultureInfo.InvariantCulture), lines[1], lines.Length > 2 ? lines[2] : ""));
                _nextCtl++;
                path = Path.Combine(_ctl, "c" + _nextCtl + ".txt");
            }
        }

        private void Execute(string cmd, string args)
        {
            var manager = NetworkManager.Singleton;
            var local = manager != null ? manager.LocalClient?.PlayerObject : null;
            switch (cmd)
            {
                case "SETUP":
                {
                    // args: crateX,crateY,crateZ,qx,qy,qz,qw|hostX,hostY,hostZ,hostYaw|cliX,cliY,cliZ,cliYaw
                    string[] groups = args.Split('|');
                    float[] crate = Floats(groups[0]);
                    _intendedRelease = true; _phase = Setup;
                    if (_host)
                    {
                        _fixedAction += () =>
                        {
                            var cargo = FindCargo(); if (cargo == null) return;
                            var rb = cargo.GetComponent<Rigidbody>();
                            rb.position = new Vector3(crate[0], crate[1], crate[2]); rb.rotation = new Quaternion(crate[3], crate[4], crate[5], crate[6]);
                            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                        };
                    }
                    float[] me = Floats(groups[_host ? 1 : 2]);
                    TeleportLocal(local, new Vector3(me[0], me[1], me[2]), me[3]);
                    break;
                }
                case "GRAB":
                {
                    var cargo = FindCargo();
                    if (cargo != null && local != null) { _intendedRelease = false; local.GetComponent<PlayerCarry>().InitiateGrab(cargo, true, true); }
                    break;
                }
                case "YAW": SetYaw(local, Floats(args)[0]); break;
                case "PHASE":
                {
                    string[] a = args.Split('|'); // phase|KEYS
                    _phase = int.Parse(a[0], CultureInfo.InvariantCulture);
                    _phaseStart[_phase] = Now();
                    _keys = a.Length > 1 && a[1].Length > 0 ? a[1].Split(',').Select(k => (Key)Enum.Parse(typeof(Key), k, true)).ToArray() : Array.Empty<Key>();
                    if (_phase == Stand) _intendedRelease = false;
                    break;
                }
                case "RELEASE":
                    _intendedRelease = true; _keys = Array.Empty<Key>();
                    if (local != null) local.GetComponent<PlayerCarry>().DropForRespawn();
                    break;
                case "END":
                    _keys = Array.Empty<Key>();
                    if (!_host) WriteClientResult(Floats(args));
                    break;
                case "QUIT": Application.Quit(); break;
            }
        }

        private static float[] Floats(string csv) => csv.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        private void SetYaw(NetworkObject local, float yaw)
        {
            if (local == null) return;
            var view = local.GetComponent<PlayerCameraController>();
            _yawField.SetValue(view, yaw); _pitchField.SetValue(view, 0f);
        }

        private void TeleportLocal(NetworkObject player, Vector3 position, float yaw)
        {
            if (player == null) return;
            SetYaw(player, yaw);
            _fixedAction += () =>
            {
                var controller = player.GetComponent<CharacterController>();
                controller.enabled = false;
                player.transform.position = position;
                player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                player.GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(position, player.transform.rotation, player.transform.localScale);
                controller.enabled = true;
                player.GetComponent<PlayerMovement>().ResetVelocity();
            };
        }

        // ------------------------------------------------------------------ client
        private IEnumerator ClientRoutine()
        {
            while (true)
            {
                var manager = NetworkManager.Singleton;
                var local = manager != null && manager.IsConnectedClient ? manager.LocalClient?.PlayerObject : null;
                var cargo = FindCargo();
                float oneWay = -1f, rtt = -1f;
                if (local != null && _oneWayField != null) oneWay = (float)_oneWayField.GetValue(local.GetComponent<PlayerCarry>());
                if (manager != null && manager.IsConnectedClient && manager.NetworkConfig.NetworkTransport != null)
                    rtt = manager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
                bool ready = local != null && cargo != null && SceneManager.GetActiveScene().name == "Level01";
                string status = string.Join(",", ready ? 1 : 0, F(oneWay * 1000f), F(rtt), _drops, _phase, _cardId);
                try { File.WriteAllText(Path.Combine(_dir, "client_status.txt"), status); } catch (IOException) { }
                if (manager != null && !manager.IsListening && _phase > Setup) { _disconnected = true; }
                yield return new WaitForSecondsRealtime(.2f);
            }
        }

        private void WriteClientResult(float[] boundaries)
        {
            // boundaries: utc of stand,walk,sprint,stop,release phase starts, then end
            var r = new ClientResult();
            r.errorCount = _errorCount; r.errors = _errors.ToArray(); r.drops = _drops; r.disconnected = _disconnected;
            r.nocomp = _noComp;
            double[] b = _phaseStart; FillBoundaries(b);
            r.walk = ClientWindow(b, Walk); r.sprint = ClientWindow(b, Sprint); r.stand = ClientWindow(b, Stand);
            // measured one-way delay at the end of the stand phase (last 1.5 s)
            var tail = _clientSamples.Where(s => s.t >= b[Walk] - 1.5 && s.t < b[Walk] && s.oneWay >= 0).Select(s => s.oneWay * 1000f).ToArray();
            r.oneWayMsStand = tail.Length > 0 ? tail.Average() : -1f;
            var all = _clientSamples.Where(s => s.t >= b[Stand] && s.t < b[Release] && s.oneWay >= 0).Select(s => s.oneWay * 1000f).ToArray();
            r.oneWayMsMean = all.Length > 0 ? all.Average() : -1f; r.oneWayMsMax = all.Length > 0 ? all.Max() : -1f;
            File.WriteAllText(Path.Combine(_dir, "crateview_client.csv"),
                string.Join("\n", _crateViews.Select(v => v.t.ToString("R", CultureInfo.InvariantCulture) + "," + F(v.pos.x) + "," + F(v.pos.y) + "," + F(v.pos.z))));
            r.hands = HandStats(b, true).Concat(HandStats(b, false)).ToArray();
            File.WriteAllText(Path.Combine(_dir, "client.json"), JsonUtility.ToJson(r, true));
        }

        private double[] _phaseBoundaries = new double[8];
        private static void FillBoundaries(double[] b)
        {
            if (b[Finish] == 0) b[Finish] = Math.Max(b[Release], Math.Max(b[Stop], Now()));
            for (int i = Release; i >= Stand; i--) if (b[i] == 0) b[i] = b[i + 1];
        }

        [Serializable] private sealed class Window { public float ratioMean = -1, ratioMin = -1; }
        [Serializable] private sealed class HandPhase { public string phase; public int frames; public float pctOver5cm, median, p90, max; }
        [Serializable] private sealed class ClientResult
        {
            public int errorCount, drops; public string[] errors; public bool disconnected, nocomp;
            public Window stand, walk, sprint; public float oneWayMsStand, oneWayMsMean, oneWayMsMax; public HandPhase[] hands;
        }

        private Window ClientWindow(double[] b, int phase)
        {
            double t0 = b[phase], t1 = b[phase + 1];
            double s0 = t0 + (t1 - t0) * .35;
            var rs = _clientSamples.Where(s => s.t >= s0 && s.t < t1).Select(s => s.ratio).ToArray();
            return new Window { ratioMean = rs.Length > 0 ? rs.Average() : -1f, ratioMin = rs.Length > 0 ? rs.Min() : -1f };
        }

        private HandPhase[] HandStats(double[] b, bool remote)
        {
            var list = new List<HandPhase>();
            foreach (int p in new[] { Stand, Walk, Sprint, Stop })
            {
                double t0 = b[p] + .5, t1 = b[p + 1];
                var errs = _handSamples.Where(s => s.t >= t0 && s.t < t1).Select(s => remote ? s.remote : s.own).Where(e => e >= 0).OrderBy(e => e).ToArray();
                list.Add(new HandPhase { phase = PhaseNames[p] + (remote ? "_remote" : "_own"), frames = errs.Length,
                    pctOver5cm = errs.Length > 0 ? 100f * errs.Count(e => e > .05f) / errs.Length : -1f,
                    median = errs.Length > 0 ? errs[errs.Length / 2] : -1f, p90 = errs.Length > 0 ? errs[(int)(errs.Length * .9f)] : -1f, max = errs.Length > 0 ? errs[errs.Length - 1] : -1f });

            }
            return list.ToArray();
        }

        // ------------------------------------------------------------------ host
        private bool ClientStatus(out bool ready, out float oneWayMs, out float rttMs, out int drops)
        {
            ready = false; oneWayMs = rttMs = -1f; drops = 0;
            try
            {
                string[] p = File.ReadAllText(Path.Combine(_dir, "client_status.txt")).Split(',');
                ready = p[0] == "1"; oneWayMs = float.Parse(p[1], CultureInfo.InvariantCulture); rttMs = float.Parse(p[2], CultureInfo.InvariantCulture); drops = int.Parse(p[3]);
                return true;
            }
            catch { return false; }
        }

        private IEnumerator WaitFor(Func<bool> condition, float timeout, string abortReason)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (_abort != null) yield break;
                if (Time.realtimeSinceStartup > end) { _abort = abortReason; yield break; }
                yield return null;
            }
        }

        private void MarkPhase(int phase, string keys)
        {
            _phaseBoundaries[phase] = Now() + .12;
            Broadcast("PHASE", phase + "|" + keys);
        }

        private IEnumerator HostRoutine()
        {
            var manager = NetworkManager.Singleton;
            while (manager == null) { yield return null; manager = NetworkManager.Singleton; }
            // --- preconditions: two peers, Level01, crate, two players
            yield return WaitFor(() => manager.IsServer && manager.ConnectedClientsIds.Count == 2, 90f, "PRECONDITION: 2 players did not connect");
            if (_abort == null)
            {
                manager.OnClientDisconnectCallback += id => { _disconnected = true; };
                yield return new WaitForSecondsRealtime(1f);
                manager.SceneManager.LoadScene("Level01", LoadSceneMode.Single);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == "Level01" && FindCargo() != null && FindCargo().IsSpawned &&
                    manager.SpawnManager.SpawnedObjectsList.Count(o => o.IsPlayerObject) == 2 && manager.LocalClient?.PlayerObject != null, 60f, "PRECONDITION: Level01/crate/2 players not ready");
            }
            if (_abort == null) yield return WaitFor(() => ClientStatus(out bool ready, out _, out _, out _) && ready, 60f, "PRECONDITION: client not ready in Level01");
            if (_abort == null) yield return WaitFor(() => _cardId != 0 && ClientCardId() != 0, 30f, "PRECONDITION: cards not selected");
            if (_abort == null) yield return new WaitForSecondsRealtime(3f);

            var result = new HostResult { scenario = Path.GetFileName(_dir), expectOneWayMs = _expectOneWayMs, nocomp = _noComp };
            result.sim = new SimInfo { delayMs = NetworkConditionsHook.DelayMs, jitterMs = NetworkConditionsHook.JitterMs, lossPct = NetworkConditionsHook.LossPct, active = NetworkConditionsHook.Active };
            FragileCargo crate = _abort == null ? FindCargo() : null;
            if (crate != null)
            {
                var box = crate.GetComponentsInChildren<BoxCollider>().Where(b => !b.isTrigger).OrderByDescending(b => b.size.x * b.size.y * b.size.z).First();
                // the collider may be rotated relative to the cargo: take its extent in the cargo frame
                Matrix4x4 toCargo = crate.transform.worldToLocalMatrix * box.transform.localToWorldMatrix;
                Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
                for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 corner = toCargo.MultiplyPoint3x4(box.center + Vector3.Scale(box.size * .5f, new Vector3(sx, sy, sz)));
                    lo = Vector3.Min(lo, corner); hi = Vector3.Max(hi, corner);
                }
                _boxScale = crate.transform.lossyScale; _boxCenter = (lo + hi) * .5f; _boxHalf = Vector3.Scale((hi - lo) * .5f, _boxScale);
                // long axis in horizontal rest orientation (cargo may have an unusual local frame)
                _longAxis = _boxHalf.x > _boxHalf.z ? 0 : 2;
                result.crateHalfExtents = _boxHalf; result.crateMassKg = crate.GetComponent<Rigidbody>().mass;
                result.carriersRequiredFlag = crate.RequiresTwoCarriers;
            }

            if (_abort == null)
            {
                // --- SETUP: long axis along +Z, crate at (0, y, -5), carriers at opposite ends
                Vector3 localLong = _longAxis == 0 ? Vector3.right : Vector3.forward;
                Quaternion q = Quaternion.FromToRotation(localLong, Vector3.forward);
                float half = _boxHalf[_longAxis];
                float gap = half + .45f;
                Vector3 c = new Vector3(0f, 0.65f, -5f);
                _phaseBoundaries[Setup] = Now();
                Broadcast("SETUP", string.Join(",", F(c.x), F(c.y), F(c.z), F(q.x), F(q.y), F(q.z), F(q.w)) + "|" +
                    string.Join(",", F(0), F(1.05f), F(c.z - gap), F(0)) + "|" + string.Join(",", F(0), F(1.05f), F(c.z + gap), F(180)));
                yield return new WaitForSecondsRealtime(2.5f);
                var rb = crate.GetComponent<Rigidbody>();
                result.setupCrate = rb.position; result.setupCratePitchDeg = Pitch(rb.rotation);
                var hostPlayer = manager.LocalClient.PlayerObject;
                result.setupHostPos = hostPlayer.transform.position;
                if (Mathf.Abs(rb.position.z + 5f) > .3f || Mathf.Abs(rb.position.x) > .3f) _abort = "PRECONDITION: crate not at setup position";
                result.hp0 = crate.CurrentHP.Value; result.cardHost = _cardId; result.cardClient = ClientCardId();

                // --- GRAB both
                if (_abort == null)
                {
                    Broadcast("GRAB", "");
                    yield return WaitFor(() => crate.CurrentCarrierCount == 2 && HostHolds() && ClientHolds(), 6f, "PRECONDITION: two carriers did not grip");
                    result.carriersAfterGrab = crate.CurrentCarrierCount;
                    Broadcast("YAW", "0");
                }
                // --- STAND (also the settle time for the transit measurement)
                if (_abort == null)
                {
                    MarkPhase(Stand, "");
                    yield return new WaitForSecondsRealtime(4f);
                    if (_abort == null && ClientStatus(out _, out float ow, out float rtt, out _))
                    {
                        result.preOneWayMs = ow; result.preTransportRttMs = rtt;
                        float lo = _expectOneWayMs <= 0 ? 0f : _expectOneWayMs * .6f - 5f;
                        float hi = _expectOneWayMs <= 0 ? 25f : _expectOneWayMs * 1.6f + 10f;
                        result.simActiveCheck = ow >= lo && ow <= hi;
                        if (!result.simActiveCheck) _abort = $"PRECONDITION: simulator not active as configured (measured one-way {ow:F1} ms, expected {_expectOneWayMs:F0} ms, window {lo:F0}-{hi:F0})";
                    }
                    else if (_abort == null) _abort = "PRECONDITION: no client status for transit check";
                }
                // --- WALK 10 m
                Vector3 start = rb.position;
                if (_abort == null)
                {
                    yield return new WaitForSecondsRealtime(.2f);
                    start = rb.position;
                    MarkPhase(Walk, "W");
                    float tEnd = Time.realtimeSinceStartup + 9f;
                    while (Flat(rb.position - start).magnitude < WalkTarget && Time.realtimeSinceStartup < tEnd && !Dropped()) yield return null;
                    result.walkReached = Flat(rb.position - start).magnitude >= WalkTarget;
                }
                // --- SPRINT 10 m
                if (_abort == null && !Dropped())
                {
                    Vector3 s2 = rb.position;
                    MarkPhase(Sprint, "W,LeftShift");
                    float tEnd = Time.realtimeSinceStartup + 8f;
                    while (Flat(rb.position - s2).magnitude < SprintTarget && Time.realtimeSinceStartup < tEnd && !Dropped()) yield return null;
                    result.sprintReached = Flat(rb.position - s2).magnitude >= SprintTarget;
                }
                // --- STOP
                if (_abort == null)
                {
                    MarkPhase(Stop, "");
                    yield return new WaitForSecondsRealtime(3f);
                }
                // --- RELEASE then reposition and REGRAB
                if (_abort == null)
                {
                    result.carriersBeforeRelease = crate.CurrentCarrierCount; result.hpStop = crate.CurrentHP.Value;
                    MarkPhase(Release, "");
                    Broadcast("RELEASE", "", .05f);
                    float t0 = Time.realtimeSinceStartup;
                    yield return WaitFor(() => crate.CurrentCarrierCount == 0 && !HostHolds() && !ClientHolds(), 3f, "RELEASE: carriers did not release");
                    result.releaseOk = _abort == null; result.releaseSeconds = Time.realtimeSinceStartup - t0;
                    if (_abort == null || _abort.StartsWith("RELEASE")) { _abort = null; }
                    yield return new WaitForSecondsRealtime(.5f);
                    Broadcast("SETUP", string.Join(",", F(c.x), F(c.y), F(c.z), F(q.x), F(q.y), F(q.z), F(q.w)) + "|" +
                        string.Join(",", F(0), F(1.05f), F(c.z - gap), F(0)) + "|" + string.Join(",", F(0), F(1.05f), F(c.z + gap), F(180)));
                    yield return new WaitForSecondsRealtime(2.5f);
                    MarkPhase(Regrab, "");
                    Broadcast("GRAB", "", .05f);
                    t0 = Time.realtimeSinceStartup;
                    yield return WaitFor(() => crate.CurrentCarrierCount == 2 && HostHolds() && ClientHolds(), 5f, "REGRAB: two carriers did not re-grip");
                    result.regrabOk = _abort == null; result.regrabSeconds = Time.realtimeSinceStartup - t0;
                    if (_abort != null && _abort.StartsWith("REGRAB")) { result.regrabNote = _abort; _abort = null; }
                    yield return new WaitForSecondsRealtime(1.5f);
                }
            }

            // --- FINISH
            MarkPhase(Finish, "");
            FillBoundaries(_phaseBoundaries);
            var fcrate = FindCargo();
            result.hp1 = fcrate != null ? fcrate.CurrentHP.Value : -1;
            // client writes its part, host writes its part, then both quit
            Broadcast("END", "0", .4f);
            yield return new WaitForSecondsRealtime(1.2f);
            float waitEnd = Time.realtimeSinceStartup + 10f;
            while (!File.Exists(Path.Combine(_dir, "client.json")) && Time.realtimeSinceStartup < waitEnd) yield return null;
            ComputeHostResult(result);
            result.abort = _abort; result.errorCount = _errorCount; result.errors = _errors.ToArray(); result.disconnected = _disconnected;
            result.hostDrops = _drops;
            File.WriteAllText(Path.Combine(_dir, "result.json"), JsonUtility.ToJson(result, true));
            Broadcast("QUIT", "", .5f);
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }

        private bool Dropped() => _drops > 0 || (ClientStatus(out _, out _, out _, out int d) && d > 0);
        private bool HostHolds() { var p = NetworkManager.Singleton.LocalClient?.PlayerObject; return p != null && p.GetComponent<PlayerCarry>().IsCarrying; }
        private bool ClientHolds()
        {
            foreach (var o in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
                if (o.IsPlayerObject && !o.IsOwner) return o.GetComponent<PlayerCarry>().IsCarrying;
            return false;
        }
        private static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        private float Pitch(Quaternion rot)
        {
            Vector3 axis = rot * (_longAxis == 0 ? Vector3.right : Vector3.forward);
            return Mathf.Asin(Mathf.Clamp(Mathf.Abs(axis.y), 0f, 1f)) * Mathf.Rad2Deg;
        }
        private (float hostEnd, float clientEnd) EndHeights(Vector3 pos, Quaternion rot)
        {
            float[] ends = new float[2];
            Vector3 centerWorld = pos + rot * Vector3.Scale(_boxCenter, _boxScale);
            float z0 = 0, z1 = 0;
            for (int e = 0; e < 2; e++)
            {
                float sign = e == 0 ? -1f : 1f; float low = float.MaxValue; float zc = 0;
                for (int a = -1; a <= 1; a += 2)
                    for (int b = -1; b <= 1; b += 2)
                    {
                        Vector3 local = Vector3.zero;
                        int other1 = _longAxis == 0 ? 1 : 0, other2 = _longAxis == 2 ? 1 : 2;
                        local[_longAxis] = sign * _boxHalf[_longAxis];
                        local[other1] = a * _boxHalf[other1]; local[other2] = b * _boxHalf[other2];
                        Vector3 w = centerWorld + rot * local;
                        low = Mathf.Min(low, w.y); zc += w.z * .25f;
                    }
                ends[e] = low; if (e == 0) z0 = zc; else z1 = zc;
            }
            return z0 < z1 ? (ends[0], ends[1]) : (ends[1], ends[0]); // host stands at the low-z end
        }

        // ------------------------------------------------------------------ host analysis
        [Serializable] private sealed class SimInfo { public int delayMs, jitterMs, lossPct; public bool active; }
        [Serializable] private sealed class PhaseStats
        {
            public string phase; public float seconds, distance, speed, pitchMax, pitchMean, hostEndMin, clientEndMin, hostRatioMean, hostRatioMin;
        }
        [Serializable] private sealed class HostResult
        {
            public string scenario, abort; public bool nocomp; public float expectOneWayMs;
            public SimInfo sim; public bool simActiveCheck; public float preOneWayMs, preTransportRttMs;
            public Vector3 crateHalfExtents, setupCrate, setupHostPos; public float crateMassKg, setupCratePitchDeg; public bool carriersRequiredFlag;
            public int carriersAfterGrab, carriersBeforeRelease, cardHost, cardClient;
            public bool walkReached, sprintReached, releaseOk, regrabOk; public float releaseSeconds, regrabSeconds; public string regrabNote;
            public int hp0, hpStop, hp1, hostDrops, errorCount; public string[] errors; public bool disconnected;
            public PhaseStats[] phases; public HandPhase[] hands;
            public float stopPeak, stopCoast, stopPitchMax;
            public float diffRestMax, diffMovingMean, diffMovingMax; public int minCarriersInWalkSprint;
            public ClientResult client;
            public bool clientFileFound;
        }

        private void ComputeHostResult(HostResult r)
        {
            double[] b = _phaseBoundaries;
            var phases = new List<PhaseStats>();
            foreach (int p in new[] { Stand, Walk, Sprint, Stop })
            {
                double t0 = b[p], t1 = b[p + 1];
                if (t1 <= t0) { phases.Add(new PhaseStats { phase = PhaseNames[p] }); continue; }
                double s0 = p == Stand || p == Stop ? t0 + (t1 - t0) * .5 : t0 + (t1 - t0) * .35;
                var steady = _hostSamples.Where(s => s.t >= s0 && s.t < t1).ToList();
                var whole = _hostSamples.Where(s => s.t >= t0 && s.t < t1).ToList();
                var st = new PhaseStats { phase = PhaseNames[p], seconds = (float)(t1 - t0) };
                if (steady.Count > 1)
                {
                    st.distance = Flat(steady[steady.Count - 1].pos - steady[0].pos).magnitude;
                    st.speed = st.distance / (float)Math.Max(.05, steady[steady.Count - 1].t - steady[0].t);
                    st.pitchMax = steady.Max(s => Pitch(s.rot)); st.pitchMean = steady.Average(s => Pitch(s.rot));
                    st.hostEndMin = steady.Min(s => EndHeights(s.pos, s.rot).hostEnd); st.clientEndMin = steady.Min(s => EndHeights(s.pos, s.rot).clientEnd);
                    st.hostRatioMean = steady.Average(s => s.ratio); st.hostRatioMin = steady.Min(s => s.ratio);
                }
                phases.Add(st);
            }
            r.phases = phases.ToArray();
            // stop transient: crate travel after the stop command
            var stopS = _hostSamples.Where(s => s.t >= b[Stop] && s.t < b[Stop + 1]).ToList();
            if (stopS.Count > 0)
            {
                r.stopPeak = stopS.Max(s => Flat(s.pos - stopS[0].pos).magnitude);
                r.stopCoast = Flat(stopS[stopS.Count - 1].pos - stopS[0].pos).magnitude;
                r.stopPitchMax = stopS.Max(s => Pitch(s.rot));
            }
            var move = _hostSamples.Where(s => s.t >= b[Walk] && s.t < b[Stop]).ToList();
            r.minCarriersInWalkSprint = move.Count > 0 ? move.Min(s => s.carriers) : -1;
            // host-vs-client crate position (host physics vs client rendered replica), matched by wall-clock time
            var hs = _hostSamples.Where(s => s.t >= b[Stand]).ToList();
            var diffRest = new List<float>(); var diffMove = new List<float>();
            string clientCsv = Path.Combine(_dir, "crateview_client.csv");
            if (File.Exists(clientCsv) && hs.Count > 2)
            {
                foreach (string line in File.ReadAllLines(clientCsv))
                {
                    string[] p = line.Split(',');
                    if (p.Length < 4) continue;
                    double t = double.Parse(p[0], CultureInfo.InvariantCulture);
                    if (t < hs[0].t || t > hs[hs.Count - 1].t) continue;
                    int i = hs.FindIndex(s => s.t >= t); if (i <= 0) continue;
                    float a = (float)((t - hs[i - 1].t) / Math.Max(1e-6, hs[i].t - hs[i - 1].t));
                    Vector3 host = Vector3.Lerp(hs[i - 1].pos, hs[i].pos, Mathf.Clamp01(a));
                    Vector3 view = new Vector3(float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture));
                    float d = Vector3.Distance(host, view);
                    // at rest = last 1.0 s of the stand and of the stop phases
                    bool rest = (t >= b[Walk] - 1.0 && t < b[Walk]) || (t >= b[Stop + 1] - 1.0 && t < b[Stop + 1]);
                    bool moving = t >= b[Walk] + .5 && t < b[Stop];
                    if (rest) diffRest.Add(d); else if (moving) diffMove.Add(d);
                }
                r.diffRestMax = diffRest.Count > 0 ? diffRest.Max() : -1f;
                r.diffMovingMean = diffMove.Count > 0 ? diffMove.Average() : -1f; r.diffMovingMax = diffMove.Count > 0 ? diffMove.Max() : -1f;
            }
            else { r.diffRestMax = r.diffMovingMean = r.diffMovingMax = -1f; }
            r.hands = HandStats(b, true).Concat(HandStats(b, false)).ToArray();
            string clientJson = Path.Combine(_dir, "client.json");
            if (File.Exists(clientJson)) { r.client = JsonUtility.FromJson<ClientResult>(File.ReadAllText(clientJson)); r.clientFileFound = true; }
        }
    }
}
#endif
