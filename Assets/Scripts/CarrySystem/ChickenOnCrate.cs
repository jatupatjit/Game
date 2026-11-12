using System.Collections.Generic;
using CoopGame.Network;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// The chicken crate (George 2026-11-12): a brown hen sits in a twig nest on the lid. She slides about the lid with
    /// the crate's tilt and jolts but never falls off. Tilting, jerking, hitting the crate or pinning her against the
    /// edge fills her anger bar; smooth carrying calms her. At full anger she jumps off and runs away. Delivering the
    /// crate with the hen still aboard pays <see cref="Bonus"/>.
    /// The hen's motion and anger are worked out on every screen from the crate's motion (look only); the host alone
    /// decides the escape and the restart, sent to everyone through <see cref="GameRoom"/> room effects.
    /// Added at runtime by <see cref="CargoVariant"/> (Chicken prefab) and <see cref="CargoDifficulty"/> (level crate).
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class ChickenOnCrate : MonoBehaviour
    {
        public const float Bonus = 1.5f;

        // Tuning (anger per second unless noted; 100 = escape).
        private const float CalmTilt = 10f, TiltAnger = 3f;           // per degree past CalmTilt
        // Measured 2026-11-12: a carried crate's start/stop peaks at ~5 m/s² (the carry caps it), so one start or stop
        // adds ~8 and drains away, while jerking back and forth every ~0.35 s fills the bar in ~6-8 s.
        private const float JoltThreshold = 3.5f, JoltAnger = 10f;    // m/s² sideways; anger per jolt
        private const float EdgeAnger = 30f, CalmDown = 8f, CalmDelay = 1f, HitAnger = 25f;
        private const float Grip = .8f, HomePull = 2f, SlideDamping = 2.5f, EdgeMargin = .14f, LeanPerAccel = 6f;

        public bool Escaped { get; private set; }
        public float Anger01 => Mathf.Clamp01(_anger / 100f);

        /// <summary>Delivery pay multiplier for a crate: the bonus while its hen is still aboard.</summary>
        public static float BonusFor(Component cargo) =>
            cargo != null && cargo.TryGetComponent(out ChickenOnCrate hen) && hen.enabled && !hen.Escaped ? Bonus : 1f;

        private FragileCargo _cargo;
        private Transform _rig, _hen, _henBody, _head, _wingL, _wingR, _barRoot, _barFill, _alarm;
        private Renderer _barFillRenderer;
        private Vector2 _half = new(.4f, .4f);
        private Vector2 _pos, _vel, _lean;
        private Vector3 _lastPosition, _seenVelocity, _seenAccel;
        private float _anger, _facing, _nextCluck, _escapeTime, _lastAngerCue;
        private int _lastHp = -1;
        private bool _atEdge, _built, _wasJolting;
        private float _nextJolt, _lastUpset;
        private Vector3 _escapeFrom, _escapeDir;
        private static Material _lit;

        private static bool IsServer => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;

        private void Awake()
        {
            _cargo = GetComponent<FragileCargo>();
            _lastPosition = transform.position;
            FragileCargo.OnCargoRespawnedGlobal += OnRespawned;
        }

        private void OnDestroy()
        {
            FragileCargo.OnCargoRespawnedGlobal -= OnRespawned;
            if (_rig != null) Destroy(_rig.gameObject);
            if (_hen != null && _hen.parent == null) Destroy(_hen.gameObject);
        }

        private void OnEnable() { if (_rig != null) _rig.gameObject.SetActive(true); }
        private void OnDisable() { if (_rig != null) _rig.gameObject.SetActive(false); }

        private void Start() => Build();

        // ───────────────────────── Look ─────────────────────────

        private void Build()
        {
            if (_built) return;
            _built = true;
            // Lid size and height from the crate's drawn bounds (taken upright at spawn).
            Bounds b = default; bool any = false;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.name.StartsWith("Chicken") || r.transform.parent != null && r.transform.parent.name == "Chicken") continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            float top = any ? transform.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z)).y : .5f;
            if (any) _half = new Vector2(Mathf.Max(.2f, b.extents.x), Mathf.Max(.2f, b.extents.z));

            _rig = new GameObject("ChickenRig").transform;
            _rig.SetParent(transform, false);
            _rig.localPosition = new Vector3(0f, top, 0f);
            Vector3 s = transform.lossyScale;
            _rig.localScale = new Vector3(1f / Mathf.Max(.01f, s.x), 1f / Mathf.Max(.01f, s.y), 1f / Mathf.Max(.01f, s.z)); // rig in metres

            float size = Mathf.Clamp(Mathf.Min(_half.x, _half.y) * 1.1f, .35f, .7f); // hen length
            BuildNest(size);
            BuildHen(size);
            BuildBar(size);
        }

        private void BuildNest(float size)
        {
            var nest = new GameObject("Nest").transform;
            nest.SetParent(_rig, false);
            Color twig = new(.36f, .24f, .14f), twigDark = new(.27f, .17f, .1f);
            Part(nest, PrimitiveType.Cylinder, new Vector3(0f, .015f, 0f), new Vector3(size * 1.15f, .015f, size * 1.15f), Quaternion.identity, twigDark);
            const int twigs = 16;
            for (int i = 0; i < twigs; i++)
            {
                float a = i * Mathf.PI * 2f / twigs;
                float r = size * .55f;
                var at = new Vector3(Mathf.Cos(a) * r, .05f + (i % 2) * .03f, Mathf.Sin(a) * r);
                // A twig lies along the ring, a little tilted.
                var rot = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a))) * Quaternion.Euler(90f, 0f, (i % 3 - 1) * 12f);
                Part(nest, PrimitiveType.Cylinder, at, new Vector3(.035f, size * .25f, .035f), rot, i % 2 == 0 ? twig : twigDark);
            }
        }

        private void BuildHen(float size)
        {
            Color feather = new(.55f, .25f, .12f), featherDark = new(.38f, .16f, .08f), neck = new(.66f, .32f, .14f);
            Color red = new(.85f, .15f, .2f), beak = new(.95f, .72f, .35f);
            _hen = new GameObject("Hen").transform;
            _hen.SetParent(_rig, false);
            _henBody = new GameObject("Body").transform;
            _henBody.SetParent(_hen, false);
            float u = size; // hen length
            Part(_henBody, PrimitiveType.Sphere, new Vector3(0f, .3f * u, -.05f * u), new Vector3(.62f * u, .55f * u, .85f * u), Quaternion.identity, feather);
            // tail up at the back
            Part(_henBody, PrimitiveType.Sphere, new Vector3(0f, .55f * u, -.42f * u), new Vector3(.32f * u, .45f * u, .2f * u), Quaternion.Euler(-30f, 0f, 0f), featherDark);
            _wingL = Part(_henBody, PrimitiveType.Sphere, new Vector3(-.3f * u, .33f * u, -.08f * u), new Vector3(.08f * u, .32f * u, .55f * u), Quaternion.identity, featherDark);
            _wingR = Part(_henBody, PrimitiveType.Sphere, new Vector3(.3f * u, .33f * u, -.08f * u), new Vector3(.08f * u, .32f * u, .55f * u), Quaternion.identity, featherDark);
            // neck + head
            Part(_henBody, PrimitiveType.Sphere, new Vector3(0f, .58f * u, .26f * u), new Vector3(.3f * u, .42f * u, .3f * u), Quaternion.Euler(20f, 0f, 0f), neck);
            _head = new GameObject("Head").transform;
            _head.SetParent(_henBody, false);
            _head.localPosition = new Vector3(0f, .8f * u, .34f * u);
            Part(_head, PrimitiveType.Sphere, Vector3.zero, new Vector3(.24f * u, .24f * u, .26f * u), Quaternion.identity, neck);
            Part(_head, PrimitiveType.Cube, new Vector3(0f, -.02f * u, .17f * u), new Vector3(.07f * u, .06f * u, .12f * u), Quaternion.Euler(10f, 0f, 0f), beak);
            Part(_head, PrimitiveType.Sphere, new Vector3(0f, -.11f * u, .1f * u), new Vector3(.07f * u, .12f * u, .06f * u), Quaternion.identity, red); // wattle
            for (int i = 0; i < 3; i++) // comb
                Part(_head, PrimitiveType.Sphere, new Vector3(0f, .13f * u, (.07f - i * .07f) * u), new Vector3(.05f * u, .1f * u, .08f * u), Quaternion.identity, red);
            Part(_head, PrimitiveType.Sphere, new Vector3(-.1f * u, .03f * u, .08f * u), Vector3.one * .04f * u, Quaternion.identity, Color.black);
            Part(_head, PrimitiveType.Sphere, new Vector3(.1f * u, .03f * u, .08f * u), Vector3.one * .04f * u, Quaternion.identity, Color.black);
        }

        private void BuildBar(float size)
        {
            _barRoot = new GameObject("AngerBar").transform;
            _barRoot.SetParent(_rig, false);
            Part(_barRoot, PrimitiveType.Cube, Vector3.zero, new Vector3(.5f, .07f, .01f), Quaternion.identity, new Color(.15f, .12f, .1f));
            _barFill = Part(_barRoot, PrimitiveType.Cube, new Vector3(0f, 0f, -.006f), new Vector3(.47f, .05f, .01f), Quaternion.identity, Color.green);
            _barFillRenderer = _barFill.GetComponent<Renderer>();
            var alarm = new GameObject("Alarm", typeof(TextMesh));
            _alarm = alarm.transform;
            _alarm.SetParent(_barRoot, false);
            _alarm.localPosition = new Vector3(0f, .17f, 0f);
            var text = alarm.GetComponent<TextMesh>();
            text.text = "!"; text.anchor = TextAnchor.MiddleCenter; text.characterSize = .05f; text.fontSize = 64; text.fontStyle = FontStyle.Bold;
            text.color = new Color(1f, .25f, .15f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) { text.font = font; alarm.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
        }

        private static Transform Part(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Quaternion rotation, Color color)
        {
            var temp = GameObject.CreatePrimitive(type);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(temp);
            var go = new GameObject(type.ToString(), typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (_lit == null) _lit = Resources.Load<Material>("CosmeticLit");
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (_lit != null) { var m = new Material(_lit); m.SetColor("_BaseColor", color); renderer.sharedMaterial = m; }
            return go.transform;
        }

        // ───────────────────────── Motion and anger (every screen) ─────────────────────────

        private void Update()
        {
            if (!_built || Time.deltaTime <= 0f) return;
            float dt = Mathf.Min(Time.deltaTime, .05f);
            if (Escaped) { UpdateEscape(dt); return; }
            if (_cargo != null && _cargo.IsSecured) { _anger = Mathf.Max(0f, _anger - CalmDown * dt); UpdateLook(dt); return; }

            SlideOnLid(dt);
            UpdateAnger(dt);
            UpdateLook(dt);
        }

        // The crate's acceleration, sampled at the physics rate: the crate only moves on physics steps, so sampling
        // per rendered frame gave large one-frame spikes that, past the hen's grip, pinned her to the back edge.
        private void FixedUpdate()
        {
            if (!_built) return;
            float dt = Time.fixedDeltaTime;
            Vector3 velocity = (transform.position - _lastPosition) / dt;
            _lastPosition = transform.position;
            Vector3 accel = (velocity - _seenVelocity) / dt;
            _seenVelocity = velocity;
            if (velocity.sqrMagnitude > 400f) { _seenVelocity = Vector3.zero; accel = Vector3.zero; } // teleport / respawn
            _seenAccel = Vector3.Lerp(_seenAccel, Vector3.ClampMagnitude(accel, 30f), 1f - Mathf.Exp(-dt * 8f));
        }

        // The hen grips the lid; a push stronger than her grip (tilt or jolt) slides her, and she walks back home.
        private void SlideOnLid(float dt)
        {
            Vector3 up = transform.up;
            Vector3 felt = Physics.gravity - _seenAccel; // what the hen feels (gravity plus being shoved)
            Vector3 inPlane = Vector3.ProjectOnPlane(felt, up);
            Vector3 local = _rig.InverseTransformDirection(inPlane);
            Vector2 push = new(local.x, local.z);
            _lean = Vector2.Lerp(_lean, push, 1f - Mathf.Exp(-dt * 12f));
            float mag = push.magnitude;
            push = mag > Grip ? push * (1f - Grip / mag) : Vector2.zero;
            _vel += (push - _pos * HomePull) * dt;
            _vel *= Mathf.Exp(-SlideDamping * dt);
            _pos += _vel * dt;
            Vector2 limit = new(Mathf.Max(.02f, _half.x - EdgeMargin), Mathf.Max(.02f, _half.y - EdgeMargin));
            _atEdge = false;
            if (Mathf.Abs(_pos.x) > limit.x) { _pos.x = Mathf.Sign(_pos.x) * limit.x; if (_vel.x * _pos.x > 0f) _vel.x = 0f; _atEdge = true; }
            if (Mathf.Abs(_pos.y) > limit.y) { _pos.y = Mathf.Sign(_pos.y) * limit.y; if (_vel.y * _pos.y > 0f) _vel.y = 0f; _atEdge = true; }
        }

        private void UpdateAnger(float dt)
        {
            float tilt = Vector3.Angle(transform.up, Vector3.up);
            float jolt = Vector3.ProjectOnPlane(_seenAccel, Vector3.up).magnitude;
            // Each sharp start, stop or reversal is one jolt (they last ~0.1 s, so a per-second rate barely counted
            // them); tilt and being pinned at the edge add while they last; she calms after a moment of smooth carry.
            bool jolting = jolt > JoltThreshold;
            if (jolting && !_wasJolting && Time.time >= _nextJolt) { _anger += JoltAnger; _nextJolt = Time.time + .25f; _lastUpset = Time.time; }
            _wasJolting = jolting;
            float rise = Mathf.Max(0f, tilt - CalmTilt) * TiltAnger + (_atEdge ? EdgeAnger : 0f);
            if (rise > 0f) { _anger += rise * dt; _lastUpset = Time.time; }
            else if (Time.time - _lastUpset > CalmDelay) _anger = Mathf.Max(0f, _anger - CalmDown * dt);
            if (_cargo != null)
            {
                int hp = _cargo.CurrentHP.Value;
                if (_lastHp >= 0 && hp < _lastHp) _anger += HitAnger; // the crate got hit
                _lastHp = hp;
            }
            _anger = Mathf.Min(_anger, 100f);
            // Clucks get louder and quicker as she gets angrier.
            float level = Mathf.Floor(_anger / 25f);
            if (level > _lastAngerCue || (_atEdge && Time.time >= _nextCluck))
            {
                _nextCluck = Time.time + .6f;
                GameplayFeedback.Play(GameplayFeedback.Cue.Click, _hen.position, .4f + Anger01 * .6f);
            }
            _lastAngerCue = level;
            if (_anger >= 100f && IsServer) ServerEscape();
        }

        private void UpdateLook(float dt)
        {
            if (_hen == null) return;
            float speed = _vel.magnitude;
            _hen.localPosition = new Vector3(_pos.x, 0f, _pos.y);
            if (speed > .05f) _facing = Mathf.MoveTowardsAngle(_facing, Mathf.Atan2(_vel.x, _vel.y) * Mathf.Rad2Deg, 360f * dt);
            else _facing += Mathf.Sin(Time.time * .7f) * 20f * dt; // looks around
            // Leans back against the slide, wobbles when angry.
            float angryShake = Anger01 > .6f ? Mathf.Sin(Time.time * 30f) * 6f * Anger01 : 0f;
            // She leans with the shove she feels (lid frame) so every jolt shows, then faces her way.
            Quaternion lean = Quaternion.Euler(Mathf.Clamp(_lean.y * LeanPerAccel, -35f, 35f), 0f, Mathf.Clamp(-_lean.x * LeanPerAccel, -35f, 35f));
            _hen.localRotation = lean * Quaternion.Euler(-Mathf.Clamp(speed * 25f, 0f, 25f), _facing, angryShake);
            _henBody.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(Time.time * 12f)) * .03f * Mathf.Clamp01(speed * 3f), 0f);
            if (_head != null) _head.localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Sin(Time.time * (3f + Anger01 * 9f))) * 25f, 0f, 0f); // pecks
            float flap = (_atEdge || Anger01 > .6f) ? Mathf.Sin(Time.time * 28f) * 50f : 0f;
            if (_wingL != null) _wingL.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Abs(flap));
            if (_wingR != null) _wingR.localRotation = Quaternion.Euler(0f, 0f, Mathf.Abs(flap));
            // Anger bar above her, facing the camera.
            if (_barRoot != null)
            {
                _barRoot.position = _hen.position + Vector3.up * (Mathf.Min(_half.x, _half.y) * 1.3f + .35f);
                var cam = Camera.main;
                if (cam != null) _barRoot.rotation = Quaternion.LookRotation(_barRoot.position - cam.transform.position);
                float fill = Anger01;
                _barFill.localScale = new Vector3(.47f * Mathf.Max(.001f, fill), .05f, .01f);
                _barFill.localPosition = new Vector3(-.235f * (1f - fill), 0f, -.006f);
                Color c = fill < .5f ? Color.Lerp(new Color(.3f, .85f, .3f), new Color(1f, .85f, .2f), fill * 2f) : Color.Lerp(new Color(1f, .85f, .2f), new Color(1f, .2f, .15f), (fill - .5f) * 2f);
                if (_barFillRenderer != null && _barFillRenderer.sharedMaterial != null) _barFillRenderer.sharedMaterial.SetColor("_BaseColor", c);
                _alarm.gameObject.SetActive(fill > .7f && Mathf.Repeat(Time.time * 4f, 1f) < .6f);
            }
        }

        // ───────────────────────── Escape (host decides, every screen plays) ─────────────────────────

        private void ServerEscape()
        {
            if (Escaped) return;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && GameRoom.HasMessenger)
            {
                GameRoom.ServerEffect(GameRoom.EffectChickenEscape, transform.position);
                GameRoom.ServerFeed("ไก่โมโหจนหนีออกจากลังไปแล้ว! (อดโบนัส)", new Color(1f, .6f, .3f));
            }
            else PlayEscape();
        }

        private void PlayEscape()
        {
            if (Escaped || _hen == null) return;
            Escaped = true;
            _escapeTime = Time.time;
            _escapeFrom = _hen.position;
            Vector3 outward = _rig.TransformDirection(new Vector3(_pos.x, 0f, _pos.y));
            outward.y = 0f;
            if (outward.sqrMagnitude < .01f) { Vector2 r = Random.insideUnitCircle.normalized; outward = new Vector3(r.x, 0f, r.y); }
            _escapeDir = outward.normalized;
            _hen.SetParent(null, true);
            _hen.rotation = Quaternion.LookRotation(_escapeDir);
            if (_barRoot != null) _barRoot.gameObject.SetActive(false);
            GameplayFeedback.Play(GameplayFeedback.Cue.Click, _escapeFrom, 1f);
            GameplayFeedback.Burst(_escapeFrom, new Color(.6f, .3f, .15f), 18, 2.5f, .12f);
        }

        // Jumps off in an arc, then runs away flapping and shrinks out after a few seconds.
        private void UpdateEscape(float dt)
        {
            if (_hen == null) return;
            float t = Time.time - _escapeTime;
            const float jump = .7f;
            Vector3 p;
            if (t < jump)
            {
                float k = t / jump;
                p = _escapeFrom + _escapeDir * (k * 1.6f) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * .8f - k * Mathf.Max(0f, _escapeFrom.y - GroundBelow(_escapeFrom + _escapeDir * 1.6f)));
            }
            else
            {
                Vector3 landing = _escapeFrom + _escapeDir * 1.6f;
                landing.y = GroundBelow(landing);
                float run = t - jump;
                Vector3 side = Vector3.Cross(Vector3.up, _escapeDir) * Mathf.Sin(run * 3f) * .4f; // zig-zag
                p = landing + _escapeDir * run * 3f + side;
                p.y = GroundBelow(p) + Mathf.Abs(Mathf.Sin(run * 14f)) * .06f;
            }
            _hen.position = p;
            if (_wingL != null) _wingL.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Abs(Mathf.Sin(t * 30f)) * 60f);
            if (_wingR != null) _wingR.localRotation = Quaternion.Euler(0f, 0f, Mathf.Abs(Mathf.Sin(t * 30f)) * 60f);
            if (t > 3.5f)
            {
                float k = 1f - (t - 3.5f) / .5f;
                if (k <= 0f) { _hen.gameObject.SetActive(false); return; }
                _hen.localScale = Vector3.one * k;
            }
        }

        private static float GroundBelow(Vector3 at)
        {
            return Physics.Raycast(at + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point.y : at.y - 1f;
        }

        private void ResetHen()
        {
            Escaped = false;
            _anger = 0f; _pos = Vector2.zero; _vel = Vector2.zero; _lastHp = -1;
            if (_hen == null) return;
            _hen.gameObject.SetActive(true);
            _hen.SetParent(_rig, false);
            _hen.localScale = Vector3.one;
            if (_barRoot != null) _barRoot.gameObject.SetActive(true);
        }

        private void OnRespawned(FragileCargo cargo)
        {
            if (cargo != _cargo || !IsServer) return;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && GameRoom.HasMessenger)
                GameRoom.ServerEffect(GameRoom.EffectChickenReset, transform.position);
            else ResetHen();
        }

        /// <summary>Every machine: a room effect for the hen nearest <paramref name="at"/> (escape or back in the nest).</summary>
        public static void PlayRoomEffect(byte kind, Vector3 at)
        {
            ChickenOnCrate best = null; float bestDistance = float.MaxValue;
            foreach (var hen in FindObjectsByType<ChickenOnCrate>(FindObjectsSortMode.None))
            {
                if (!hen.enabled) continue;
                float d = (hen.transform.position - at).sqrMagnitude;
                if (d < bestDistance) { best = hen; bestDistance = d; }
            }
            if (best == null) return;
            if (kind == GameRoom.EffectChickenEscape) best.PlayEscape();
            else if (kind == GameRoom.EffectChickenReset) best.ResetHen();
        }
    }
}
