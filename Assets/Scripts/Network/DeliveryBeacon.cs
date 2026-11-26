using System.Collections.Generic;
using CoopGame.CarrySystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.Network
{
    /// <summary>
    /// Shows the team where the crate should go next, without any signboard text. The goal is the next checkpoint
    /// that is not saved yet, then the delivery spot. A gold ring always marks it softly; while anyone carries the
    /// crate it lights up: a light beam rises from it, ripples spread out and a chunky 3D gold arrow (bright face,
    /// cream inlay, dark amber edge) bobs and turns above it. Within <see cref="NearDistance"/> of the goal
    /// everything pulses faster. Hidden once delivered. Local visuals only (same on every peer).
    /// Added automatically to every level that has a <see cref="LevelMission"/>.
    /// </summary>
    public sealed class DeliveryBeacon : MonoBehaviour
    {
        private const float NearDistance = 15f;
        private const int RingSegments = 48;
        private const float BeamHeight = 16f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int BeamColor = Shader.PropertyToID("_Color");
        private static readonly Color Gold = new(1f, .80f, .22f, 1f);
        private static readonly Color Cream = new(1f, .97f, .78f, 1f);
        private static readonly Color Amber = new(.78f, .40f, .06f, 1f);
        private static readonly Color BeamTint = new(1f, .86f, .42f, .42f);

        private LevelMission _mission;
        private Transform _arrow, _beam;
        private LineRenderer _ring, _innerRing, _ripple;
        private Renderer _arrowRenderer, _beamRenderer;
        private MaterialPropertyBlock _block;

        private Object _targetKey;
        private Vector3 _spot;
        private float _radius, _arrowHeight, _reveal, _rippleT;
        private Vector3 _deliverySpot;
        private float _deliveryRadius;

        /// <summary>Where the beacon currently points (next checkpoint or the delivery spot). For tests.</summary>
        public Vector3 TargetSpot => _spot;
        public bool TargetIsCheckpoint => _targetKey is CheckpointPad;
        public float Reveal => _reveal;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var mission = root.GetComponentInChildren<LevelMission>(true);
                if (mission == null || mission.Delivery == null) continue;
                if (FindAnyObjectByType<DeliveryBeacon>() != null) return;
                var go = new GameObject("DeliveryBeacon");
                SceneManager.MoveGameObjectToScene(go, scene);
                go.AddComponent<DeliveryBeacon>()._mission = mission;
                return;
            }
        }

        private void Start()
        {
            _block = new MaterialPropertyBlock();
            var zone = _mission.Delivery.GetComponent<Collider>();
            Bounds b = zone != null ? zone.bounds : new Bounds(_mission.Delivery.transform.position, Vector3.one * 4f);
            // The visible surface inside the delivery volume (dais top, cart bed...).
            _deliverySpot = SurfaceBelow(new Vector3(b.center.x, b.max.y + 1f, b.center.z), b.size.y + 4f, new Vector3(b.center.x, b.min.y, b.center.z));
            _deliveryRadius = Mathf.Clamp(Mathf.Min(b.extents.x, b.extents.z) * .8f, 1.2f, 4f);

            // Vertex-coloured unlit material (URP Particles Unlit), so ring and arrow keep their colours in shade.
            var material = Resources.Load<Material>("BeaconRing") ?? Resources.Load<Material>("BeaconGold");
            _ring = Ring("Ring", .16f, material);
            _innerRing = Ring("InnerRing", .08f, material);
            _ripple = Ring("Ripple", .2f, material);
            _arrow = BuildArrow(material, out _arrowRenderer);
            var beamMaterial = Resources.Load<Material>("BeaconBeam");
            if (beamMaterial != null) _beam = BuildBeam(beamMaterial, out _beamRenderer);
        }

        private static Vector3 SurfaceBelow(Vector3 from, float distance, Vector3 fallback) =>
            Physics.Raycast(from, Vector3.down, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore) ? hit.point : fallback;

        /// <summary>The first checkpoint after the last saved one that is still in play, else the delivery spot.</summary>
        private void ResolveTarget()
        {
            CheckpointPad next = null;
            var manager = CheckpointManager.Instance;
            if (manager != null && manager.IsSpawned)
                for (int i = Mathf.Max(-1, manager.CurrentIndex) + 1; i < manager.PadCount; i++)
                {
                    var pad = manager.PadAt(i);
                    if (pad != null && pad.gameObject.activeInHierarchy && !pad.IsActive) { next = pad; break; }
                }

            Object key = next != null ? next : _mission.Delivery;
            if (key == _targetKey) return;
            _targetKey = key;
            if (next != null)
            {
                Vector3 p = next.CargoPoint.position;
                _spot = SurfaceBelow(p + Vector3.up * 2f, 5f, p);
                _radius = 1.5f;
                _arrowHeight = 3.3f;
            }
            else
            {
                _spot = _deliverySpot;
                _radius = _deliveryRadius;
                _arrowHeight = 3.8f;
            }
            SetCircle(_ring, _radius);
            SetCircle(_innerRing, _radius * .62f);
            SetCircle(_ripple, _radius);
            foreach (var line in new[] { _ring, _innerRing, _ripple }) line.transform.position = _spot + Vector3.up * .06f;
            if (_beam != null)
            {
                _beam.position = _spot;
                _beam.localScale = new Vector3(_radius * .75f, BeamHeight, _radius * .75f);
            }
        }

        private LineRenderer Ring(string name, float width, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // points laid out in local XY, turned to lie flat
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = RingSegments;
            line.widthMultiplier = width;
            line.sharedMaterial = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.TransformZ;
            return line;
        }

        private static void SetCircle(LineRenderer line, float radius)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * Mathf.PI * 2f / RingSegments;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
        }

        // Down-pointing arrow outline (counter-clockwise, tip at the origin, 1.3 tall).
        private static readonly Vector2[] ArrowShape =
        {
            new(0f, 0f), new(.6f, .66f), new(.24f, .66f), new(.24f, 1.3f), new(-.24f, 1.3f), new(-.24f, .66f), new(-.6f, .66f),
        };

        /// <summary>Extruded arrow mesh: gold faces, a smaller cream inlay on both faces and dark amber side walls,
        /// all in vertex colours. Pivot = tip, so the tip sits <see cref="_arrowHeight"/> above the goal.</summary>
        private Transform BuildArrow(Material material, out Renderer renderer)
        {
            const float depth = .3f, inlayLift = .006f;
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();

            void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color color)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f) (b, c) = (c, b);
                int s = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                colors.Add(color); colors.Add(color); colors.Add(color);
                tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
            }

            void Face(Vector2[] shape, float z, Vector3 normal, Color color)
            {
                Vector3 P(int i) => new Vector3(shape[i].x, shape[i].y, z);
                Tri(P(0), P(1), P(6), normal, color); // head
                Tri(P(2), P(3), P(4), normal, color); // shaft
                Tri(P(2), P(4), P(5), normal, color);
            }

            var inlay = new Vector2[ArrowShape.Length];
            var centre = new Vector2(0f, .62f);
            for (int i = 0; i < inlay.Length; i++) inlay[i] = centre + (ArrowShape[i] - centre) * .56f;

            float half = depth * .5f;
            Face(ArrowShape, -half, Vector3.back, Gold);
            Face(ArrowShape, half, Vector3.forward, Gold);
            Face(inlay, -half - inlayLift, Vector3.back, Cream);
            Face(inlay, half + inlayLift, Vector3.forward, Cream);
            for (int i = 0; i < ArrowShape.Length; i++)
            {
                Vector2 a = ArrowShape[i], b = ArrowShape[(i + 1) % ArrowShape.Length];
                Vector2 d = b - a;
                var n = new Vector3(d.y, -d.x, 0f).normalized;
                Vector3 a0 = new(a.x, a.y, -half), a1 = new(a.x, a.y, half), b0 = new(b.x, b.y, -half), b1 = new(b.x, b.y, half);
                Tri(a0, b0, b1, n, Amber);
                Tri(a0, b1, a1, n, Amber);
            }

            var mesh = new Mesh { name = "BeaconArrow" };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("GoalArrow", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            renderer = r;
            return go.transform;
        }

        /// <summary>Open tube 1 m radius, 1 m tall (uv.y 0 at the base, 1 at the top: the beam shader fades upward).</summary>
        private Transform BuildBeam(Material material, out Renderer renderer)
        {
            const int sides = 20;
            var verts = new Vector3[(sides + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[sides * 6];
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts[i * 2] = p; verts[i * 2 + 1] = p + Vector3.up;
                uvs[i * 2] = new Vector2(i / (float)sides, 0f); uvs[i * 2 + 1] = new Vector2(i / (float)sides, 1f);
                if (i == sides) break;
                int s = i * 2, t = i * 6;
                tris[t] = s; tris[t + 1] = s + 1; tris[t + 2] = s + 2;
                tris[t + 3] = s + 2; tris[t + 4] = s + 1; tris[t + 5] = s + 3;
            }
            var mesh = new Mesh { name = "BeaconBeam", vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateBounds();
            var go = new GameObject("GoalBeam", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material; // shader culls nothing, so both sides of the tube show
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            renderer = r;
            return go.transform;
        }

        private void LateUpdate()
        {
            if (_mission == null || _ring == null) return;
            var cargo = _mission.Cargo;
            bool delivered = _mission.Delivery == null || _mission.Delivery.IsDelivered || (cargo != null && cargo.IsSecured);
            bool alive = cargo != null && !cargo.IsDestroyed;
            bool show = !delivered;
            if (show) ResolveTarget();

            bool carried = show && alive && cargo.IsCarried;
            float dt = Time.deltaTime;
            _reveal = Mathf.MoveTowards(_reveal, carried ? 1f : 0f, dt * (carried ? 3.5f : 2.5f));

            if (_ring.gameObject.activeSelf != show)
            {
                _ring.gameObject.SetActive(show);
                _innerRing.gameObject.SetActive(show);
            }
            bool lit = show && _reveal > .001f;
            if (_arrow.gameObject.activeSelf != lit) _arrow.gameObject.SetActive(lit);
            if (_ripple.gameObject.activeSelf != lit) _ripple.gameObject.SetActive(lit);
            if (_beam != null && _beam.gameObject.activeSelf != lit) _beam.gameObject.SetActive(lit);
            if (!show) return;

            float t = Time.time;
            bool near = carried && Flat(cargo.transform.position - _spot).sqrMagnitude < NearDistance * NearDistance;
            float speed = near ? 7f : 2.6f;
            float pulse = .5f + .5f * Mathf.Sin(t * speed);

            // Ring: a soft marker while nobody carries, bright and pulsing while carried.
            float glow = Mathf.Lerp(.55f, near ? Mathf.Lerp(.95f, 1.9f, pulse) : Mathf.Lerp(.85f, 1.25f, pulse), _reveal);
            Color c = new Color(Gold.r * glow, Gold.g * glow, Gold.b * glow, 1f);
            _ring.startColor = _ring.endColor = c;
            _innerRing.startColor = _innerRing.endColor = c;
            _ring.widthMultiplier = Mathf.Lerp(.12f, near ? .26f : .18f, _reveal) * Mathf.Lerp(.9f, 1.15f, pulse);
            _ring.transform.localScale = Vector3.one * (near ? Mathf.Lerp(1f, 1.08f, pulse) : 1f);
            _innerRing.transform.localScale = Vector3.one * Mathf.Lerp(.85f, 1.1f, 1f - pulse);
            if (!lit) return;

            // Ripple: grows out of the ring and thins away, again and again.
            _rippleT = (_rippleT + dt / (near ? .8f : 1.4f)) % 1f;
            _ripple.transform.localScale = Vector3.one * Mathf.Lerp(.7f, 1.7f, _rippleT);
            _ripple.widthMultiplier = .22f * (1f - _rippleT) * _reveal;
            float rippleGlow = Mathf.Lerp(1.4f, .6f, _rippleT);
            _ripple.startColor = _ripple.endColor = new Color(Gold.r * rippleGlow, Gold.g * rippleGlow, Gold.b * rippleGlow, 1f);

            // Arrow: pops in with a small overshoot, bobs, turns slowly so it reads as 3D, flashes near the goal.
            float pop = _reveal < 1f ? EaseOutBack(_reveal) : 1f;
            float bob = Mathf.Sin(t * (near ? 6f : 3f)) * (near ? .35f : .25f);
            _arrow.position = _spot + Vector3.up * (_arrowHeight + bob);
            _arrow.rotation = Quaternion.Euler(0f, t * (near ? 140f : 70f), 0f);
            _arrow.localScale = Vector3.one * (1.35f * pop);
            float arrowGlow = near ? Mathf.Lerp(1f, 1.6f, pulse) : Mathf.Lerp(.95f, 1.15f, pulse);
            _block.Clear();
            _block.SetColor(BaseColor, new Color(arrowGlow, arrowGlow, arrowGlow, 1f));
            _arrowRenderer.SetPropertyBlock(_block);

            if (_beamRenderer != null)
            {
                _block.Clear();
                float beamAlpha = BeamTint.a * _reveal * (near ? Mathf.Lerp(.8f, 1.3f, pulse) : 1f);
                _block.SetColor(BeamColor, new Color(BeamTint.r, BeamTint.g, BeamTint.b, beamAlpha));
                _beamRenderer.SetPropertyBlock(_block);
            }
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
