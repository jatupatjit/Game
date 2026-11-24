using System.Collections.Generic;
using CoopGame.CarrySystem;
using CoopGame.Traps;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Weather
{
    /// <summary>
    /// Weather events for one level: every <see cref="_period"/> seconds one event (wind, rain, golden crate) runs for a
    /// while after a short warning banner. Which event and when comes from the server clock and a seed, so every machine
    /// works out the same schedule by itself: nothing is sent over the network and no NetworkObject is needed.
    /// Effects: wind = <see cref="WeatherWindZone"/> spots push; rain = slippery feet (<see cref="WeatherState.Traction"/>,
    /// read by RagdollCharacter) + rain around the camera + dimmer sun; golden crate = crates take no damage and carrying
    /// costs no stamina (read by FragileCargo / PlayerStamina) + gold sparkles and light on every crate.
    /// Put one in a level scene (Tools/Expedition/Add Weather To Open Scene) and tune it in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeatherDirector : MonoBehaviour
    {
        [Header("Schedule (seconds of server time)")]
        [Tooltip("Quiet time from the start of the session before the first event")]
        [SerializeField, Min(0f)] private float _firstDelay = 45f;
        [Tooltip("One event slot every this many seconds")]
        [SerializeField, Min(10f)] private float _period = 75f;
        [Tooltip("Show the weather banner (countdown before an event and time left during it). Off for now.")]
        [SerializeField] private bool _showBanner = false;
        [Tooltip("Warning banner before an event starts")]
        [SerializeField, Min(0f)] private float _warning = 5f;
        [Tooltip("Share of slots that stay clear (0 = an event every slot)")]
        [SerializeField, Range(0f, .9f)] private float _clearChance = .2f;
        [SerializeField] private int _seed = 2026;

        [Header("Events (weight 0 = off)")]
        [Tooltip("Needs at least one WeatherWindZone in the scene")]
        [SerializeField, Min(0f)] private float _windWeight = 1f;
        [SerializeField, Min(1f)] private float _windSeconds = 20f;
        [SerializeField, Min(0f)] private float _rainWeight = 1f;
        [SerializeField, Min(1f)] private float _rainSeconds = 25f;
        [Tooltip("Foot grip in full rain (1 = normal)")]
        [SerializeField, Range(.1f, 1f)] private float _rainTraction = .35f;
        [SerializeField, Min(0f)] private float _goldenWeight = .6f;
        [SerializeField, Min(1f)] private float _goldenSeconds = 12f;

        private const float FadeSeconds = 1.5f;

        private ParticleSystem _rain;
        private Light _sun;
        private float _sunBase = -1f;
        private readonly Dictionary<FragileCargo, GameObject> _gold = new();
        private float _nextGoldScan;

        // Banner
        private GameObject _bannerRoot;
        private Image _bannerBack;
        private TextMeshProUGUI _bannerTitle, _bannerHint;

        private static Material s_particleMaterial;
        public static Material ParticleMaterial => s_particleMaterial != null ? s_particleMaterial : s_particleMaterial = Resources.Load<Material>("FeedbackDust");

        public static WeatherDirector Current { get; private set; }

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this) Current = null;
            WeatherState.Clear();
            if (_sun != null && _sunBase >= 0f) _sun.intensity = _sunBase;
            foreach (var pair in _gold) if (pair.Value != null) Destroy(pair.Value);
            _gold.Clear();
            if (_bannerRoot != null) Destroy(_bannerRoot);
            if (_rain != null) Destroy(_rain.gameObject);
        }

        /// <summary>Event for slot <paramref name="slot"/> (same answer on every machine).</summary>
        public WeatherKind KindForSlot(long slot)
        {
            uint h = Hash((uint)_seed ^ (uint)slot * 2654435761u);
            float roll = (h & 0xFFFF) / 65535f;
            if (roll < _clearChance) return WeatherKind.None;
            float wind = WeatherWindZone.All.Count > 0 ? _windWeight : 0f;
            float golden = WeatherZone.AnyOf(WeatherKind.GoldenCrate) ? _goldenWeight : 0f;
            float total = wind + _rainWeight + golden;
            if (total <= 0f) return WeatherKind.None;
            float pick = ((h >> 16) & 0xFFFF) / 65536f * total;
            if (pick < wind) return WeatherKind.Wind;
            if (pick < wind + _rainWeight) return WeatherKind.Rain;
            return WeatherKind.GoldenCrate;
        }

        public float Seconds(WeatherKind kind) => kind switch
        {
            WeatherKind.Wind => _windSeconds,
            WeatherKind.Rain => _rainSeconds,
            WeatherKind.GoldenCrate => _goldenSeconds,
            _ => 0f,
        };

        private static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
            return x;
        }

        private void Update()
        {
            WeatherState.s_rainTraction = _rainTraction;
            double now = TrapClock.Now - _firstDelay;
            WeatherKind active = WeatherKind.None, coming = WeatherKind.None;
            float comingIn = 0f, timeLeft = 0f, strength = 0f;
            if (now >= 0)
            {
                long slot = (long)(now / _period);
                float t = (float)(now - slot * (double)_period);
                WeatherKind kind = KindForSlot(slot);
                float length = Mathf.Min(Seconds(kind), _period - _warning - 1f);
                if (kind != WeatherKind.None)
                {
                    if (t < _warning) { coming = kind; comingIn = _warning - t; }
                    else if (t < _warning + length)
                    {
                        active = kind;
                        float into = t - _warning;
                        timeLeft = length - into;
                        strength = Mathf.Clamp01(Mathf.Min(into, timeLeft) / FadeSeconds);
                    }
                }
            }
            WeatherState.Active = active;
            WeatherState.Coming = coming;
            WeatherState.ComingIn = comingIn;
            WeatherState.TimeLeft = timeLeft;
            WeatherState.Strength = strength;

            UpdateRain(active == WeatherKind.Rain ? strength : 0f);
            UpdateGold(active == WeatherKind.GoldenCrate ? strength : 0f);
            UpdateBanner();
        }

        // ---------------- Rain ----------------

        private void UpdateRain(float amount)
        {
            if (_sun == null) _sun = RenderSettings.sun;
            if (_sun != null)
            {
                if (_sunBase < 0f) _sunBase = _sun.intensity;
                _sun.intensity = _sunBase * Mathf.Lerp(1f, .55f, amount);
            }
            if (amount <= 0f && _rain == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            if (_rain == null) BuildRain();
            _rain.transform.position = cam.transform.position + Vector3.up * 9f;
            var emission = _rain.emission;
            emission.rateOverTime = 900f * amount;
        }

        private void BuildRain()
        {
            var go = new GameObject("WeatherRain");
            _rain = go.AddComponent<ParticleSystem>();
            _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _rain.main;
            main.loop = true;
            main.startLifetime = 1.1f;
            main.startSpeed = 18f;
            main.startSize = new ParticleSystem.MinMaxCurve(.012f, .022f);
            main.startColor = new Color(.75f, .85f, 1f, .45f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1200;
            var shape = _rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // local +Z (emit direction) = down
            shape.scale = new Vector3(26f, 26f, .5f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = .05f;
            renderer.lengthScale = 1f;
            renderer.sharedMaterial = ParticleMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // No particle collision (cheap): drops live just long enough to reach the ground under the camera.
            _rain.Play();
        }

        // ---------------- Golden crate ----------------

        private void UpdateGold(float eventAmount)
        {
            if (eventAmount > 0f && Time.time >= _nextGoldScan)
            {
                _nextGoldScan = Time.time + .5f;
                foreach (var cargo in FindObjectsByType<FragileCargo>(FindObjectsSortMode.None))
                    if (!_gold.ContainsKey(cargo) && WeatherZone.Inside(WeatherKind.GoldenCrate, cargo.transform.position)) _gold[cargo] = BuildGold(cargo);
            }
            List<FragileCargo> dead = null;
            foreach (var pair in _gold)
            {
                if (pair.Key == null || pair.Value == null) { (dead ??= new()).Add(pair.Key); continue; }
                // Only crates standing in a golden zone glow (same rule as the damage check in FragileCargo).
                float amount = WeatherZone.Inside(WeatherKind.GoldenCrate, pair.Key.transform.position) ? eventAmount : 0f;
                var light = pair.Value.GetComponent<Light>();
                light.intensity = 3f * amount * (.85f + .15f * Mathf.Sin(Time.time * 6f));
                var ps = pair.Value.GetComponent<ParticleSystem>();
                var emission = ps.emission;
                emission.rateOverTime = 40f * amount;
                var shell = pair.Value.transform.childCount > 0 ? pair.Value.transform.GetChild(0).GetComponent<MeshRenderer>() : null;
                if (shell != null)
                {
                    shell.enabled = amount > 0f;
                    shell.material.SetColor("_Color", new Color(1f, .8f, .2f, .42f * amount * (.75f + .25f * Mathf.Sin(Time.time * 5f))));
                }
                if (amount <= 0f && ps.particleCount == 0) { Destroy(pair.Value); (dead ??= new()).Add(pair.Key); }
            }
            if (dead != null) foreach (var c in dead) _gold.Remove(c);
        }

        private static GameObject BuildGold(FragileCargo cargo)
        {
            var go = new GameObject("GoldenCrateGlow");
            go.transform.SetParent(cargo.transform, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, .78f, .25f);
            light.range = 4f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = .9f;
            main.startSpeed = .6f;
            main.startSize = new ParticleSystem.MinMaxCurve(.06f, .14f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .85f, .3f), new Color(1f, .97f, .7f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -.15f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            var bounds = new Bounds(cargo.transform.position, Vector3.zero);
            foreach (var r in cargo.GetComponentsInChildren<Renderer>()) if (!(r is ParticleSystemRenderer)) bounds.Encapsulate(r.bounds);
            go.transform.position = bounds.center;
            shape.scale = bounds.size + Vector3.one * .1f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ParticleMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();

            // Gold see-through shell hugging the crate (crate's own box, follows its rotation).
            var shellGo = new GameObject("Outline_GoldShell"); // "Outline" prefix: GameplayHighlights gives it no x-ray copy
            shellGo.transform.SetParent(go.transform, false);
            Bounds local = LocalBounds(cargo.transform);
            shellGo.transform.SetParent(cargo.transform, false);
            shellGo.transform.localPosition = local.center;
            shellGo.transform.localScale = local.size + Vector3.one * .08f;
            shellGo.transform.SetParent(go.transform, true);
            shellGo.AddComponent<MeshFilter>().sharedMesh = ColoredCube;
            var shellRenderer = shellGo.AddComponent<MeshRenderer>();
            var template = Resources.Load<Material>("ThrowPreview");
            if (template != null)
            {
                var mat = new Material(template);
                mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                mat.SetFloat("_DepthPull", 0f);
                shellRenderer.sharedMaterial = mat;
            }
            shellRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shellRenderer.receiveShadows = false;
            return go;
        }

        /// <summary>Box around the crate's own meshes in its local space (skips particles, labels, the chicken).</summary>
        private static Bounds LocalBounds(Transform crate)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var mf in crate.GetComponentsInChildren<MeshFilter>())
            {
                // Renderers may still be hidden just after a spawn, so enabled is not checked; overlay copies are skipped.
                if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null) continue;
                string n = mf.name;
                if (n.Contains("XRay") || n.Contains("Crack") || n.Contains("Gold") || n.Contains("Outline") || n.Contains("Glow")) continue;
                if (mf.GetComponentInParent<ChickenOnCrate>() != null && mf.GetComponentInParent<ChickenOnCrate>().transform != crate) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = crate.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            if (!any && crate.TryGetComponent(out BoxCollider box)) return new Bounds(box.center, box.size);
            return any ? b : new Bounds(Vector3.zero, Vector3.one);
        }

        private static Mesh s_cube;
        private static Mesh ColoredCube
        {
            get
            {
                if (s_cube != null) return s_cube;
                s_cube = Instantiate(Resources.GetBuiltinResource<Mesh>("Cube.fbx"));
                var colors = new Color[s_cube.vertexCount];
                for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
                s_cube.colors = colors;
                return s_cube;
            }
        }

        // ---------------- Banner ----------------

        /// <summary>Banner on/off for this level (inspector toggle; can also be flipped at runtime).</summary>
        public bool ShowBanner { get => _showBanner; set => _showBanner = value; }

        private void UpdateBanner()
        {
            WeatherKind show = WeatherState.Active != WeatherKind.None ? WeatherState.Active : WeatherState.Coming;
            if (show == WeatherKind.None || !ShowBanner) { if (_bannerRoot != null) _bannerRoot.SetActive(false); return; }
            if (_bannerRoot == null) BuildBanner();
            _bannerRoot.SetActive(true);
            bool coming = WeatherState.Active == WeatherKind.None;
            _bannerTitle.text = coming
                ? $"{WeatherState.Name(show)} อีก {Mathf.CeilToInt(WeatherState.ComingIn)} วิ!"
                : $"{WeatherState.Name(show)}  {Mathf.CeilToInt(WeatherState.TimeLeft)} วิ";
            _bannerHint.text = WeatherState.Hint(show);
            Color c = show switch
            {
                WeatherKind.Wind => new Color(.32f, .62f, .86f, .92f),
                WeatherKind.Rain => new Color(.28f, .36f, .58f, .92f),
                _ => new Color(.93f, .68f, .12f, .94f),
            };
            if (coming) c.a *= .75f + .25f * Mathf.Sin(Time.time * 8f);
            _bannerBack.color = c;
        }

        private void BuildBanner()
        {
            _bannerRoot = new GameObject("WeatherBanner");
            var canvas = _bannerRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = _bannerRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;

            var back = new GameObject("Back", typeof(RectTransform));
            back.transform.SetParent(_bannerRoot.transform, false);
            var rt = (RectTransform)back.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, 1f);
            rt.pivot = new Vector2(.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -150f);
            rt.sizeDelta = new Vector2(560f, 92f);
            _bannerBack = back.AddComponent<Image>();
            _bannerBack.sprite = UITheme.SmallSprite;
            _bannerBack.type = Image.Type.Sliced;
            _bannerBack.raycastTarget = false;

            _bannerTitle = Label(back.transform, "Title", 34f, new Vector2(0f, -8f), new Vector2(540f, 46f), FontStyles.Bold);
            _bannerHint = Label(back.transform, "Hint", 22f, new Vector2(0f, -52f), new Vector2(540f, 32f), FontStyles.Normal);
        }

        private static TextMeshProUGUI Label(Transform parent, string name, float size, Vector2 pos, Vector2 box, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, 1f);
            rt.pivot = new Vector2(.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var text = go.AddComponent<TextMeshProUGUI>();
            if (UITheme.TmpFont != null) text.font = UITheme.TmpFont;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
    }
}
