using System.Collections.Generic;
using CoopGame.CarrySystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.Network
{
    /// <summary>
    /// Shows the team where the crate goes next, without any signboard text (George's pick 1A, 2026-11-26).
    /// Goal = the next checkpoint that is not saved yet, then the delivery spot.
    /// - Compass arrow: a flat 3D gold dart floating over the crate (just above the "ITEM" HP label) that always
    ///   points at the goal; brighter and pulsing while the crate is carried.
    /// - While carried: a glowing gold box frame on the goal's drop spot (cart bed / pad, turned with its
    ///   CargoPoint) and a 3D gold arrow pointing down into it. Within <see cref="NearDistance"/> both pulse faster.
    /// Hidden once delivered. Local visuals only (same on every peer). Added automatically to every level that
    /// has a <see cref="LevelMission"/>.
    /// </summary>
    public sealed class DeliveryBeacon : MonoBehaviour
    {
        private const float NearDistance = 15f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color Gold = new(1f, .80f, .22f, 1f);
        private static readonly Color Cream = new(1f, .97f, .78f, 1f);
        private static readonly Color Amber = new(.78f, .40f, .06f, 1f);

        private LevelMission _mission;
        private Transform _compass, _dropArrow, _frame;
        private Renderer _compassRenderer, _dropArrowRenderer, _frameRenderer;
        private MaterialPropertyBlock _block;

        private Object _targetKey;
        private Vector3 _spot;
        private float _dropArrowHeight, _reveal;
        private Vector3 _deliverySpot;
        private Vector2 _deliverySize;
        private Quaternion _deliveryRotation;

        /// <summary>Where the beacon currently points (next checkpoint or the delivery spot). For tests.</summary>
        public Vector3 TargetSpot => _spot;
        public bool TargetIsCheckpoint => _targetKey is CheckpointPad;
        public float Reveal => _reveal;
        public Transform Compass => _compass;

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
            var delivery = _mission.Delivery.transform;
            var zone = _mission.Delivery.GetComponent<Collider>();
            Bounds b = zone != null ? zone.bounds : new Bounds(delivery.position, Vector3.one * 4f);
            // The visible surface inside the delivery volume (dais top, cart bed...).
            _deliverySpot = SurfaceBelow(new Vector3(b.center.x, b.max.y + 1f, b.center.z), b.size.y + 4f, new Vector3(b.center.x, b.min.y, b.center.z));
            float side = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * .7f, 1.8f, 5f);
            _deliverySize = new Vector2(side, side);
            _deliveryRotation = Quaternion.Euler(0f, delivery.eulerAngles.y, 0f);

            // Vertex-coloured unlit material (URP Particles Unlit), so the gold keeps its colours in shade.
            var material = Resources.Load<Material>("BeaconRing") ?? Resources.Load<Material>("BeaconGold");
            // The crate's yellow x-ray copy (Transparent+90, ZTest Greater) would paint over a depth-writing arrow
            // in front of it, so the compass is drawn after it without writing depth.
            var compassMaterial = new Material(material) { name = "BeaconCompass", renderQueue = 3200 };
            compassMaterial.SetFloat("_ZWrite", 0f);
            _compass = MakeRenderer("CrateCompass", BuildArrowMesh(CompassShape, new Vector2(0f, .05f), .16f), compassMaterial, out _compassRenderer);
            _compass.GetChild(0).localRotation = Quaternion.Euler(90f, 0f, 0f); // dart lies flat, tip along +Z
            _dropArrow = MakeRenderer("DropArrow", BuildArrowMesh(DownShape, new Vector2(0f, .62f), .3f), material, out _dropArrowRenderer);
            _frame = MakeRenderer("DropFrame", null, material, out _frameRenderer);
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
            Vector2 size;
            Quaternion rotation;
            if (next != null)
            {
                Transform point = next.CargoPoint;
                _spot = SurfaceBelow(point.position + Vector3.up * 2f, 5f, point.position);
                size = new Vector2(1.8f, 1.4f);
                rotation = Quaternion.Euler(0f, point.eulerAngles.y, 0f);
                _dropArrowHeight = 2.3f;
            }
            else
            {
                _spot = _deliverySpot;
                size = _deliverySize;
                rotation = _deliveryRotation;
                _dropArrowHeight = 2.8f;
            }
            _frame.GetComponentInChildren<MeshFilter>().sharedMesh = BuildFrameMesh(size.x, .35f, size.y, .07f);
            _frame.SetPositionAndRotation(_spot + Vector3.up * .03f, rotation);
        }

        private Transform MakeRenderer(string name, Mesh mesh, Material material, out Renderer renderer)
        {
            var root = new GameObject(name).transform;
            root.SetParent(transform, false);
            var go = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            renderer = r;
            root.gameObject.SetActive(false);
            return root;
        }

        // Down-pointing arrow (counter-clockwise, tip at the origin, 1.3 tall).
        private static readonly Vector2[] DownShape =
        {
            new(0f, 0f), new(.6f, .66f), new(.24f, .66f), new(.24f, 1.3f), new(-.24f, 1.3f), new(-.24f, .66f), new(-.6f, .66f),
        };

        // Slender compass dart pointing +Y, centred on the origin (laid flat it points along +Z).
        private static readonly Vector2[] CompassShape =
        {
            new(0f, .7f), new(-.42f, .02f), new(-.15f, .02f), new(-.15f, -.62f), new(.15f, -.62f), new(.15f, .02f), new(.42f, .02f),
        };

        /// <summary>Extruded 7-point arrow: gold faces, a smaller cream inlay on both faces, dark amber side walls,
        /// all in vertex colours. Points 0,1,6 = head, 2..5 = shaft.</summary>
        private static Mesh BuildArrowMesh(Vector2[] shape, Vector2 inlayCentre, float depth)
        {
            const float inlayLift = .006f;
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

            void Face(Vector2[] pts, float z, Vector3 normal, Color color)
            {
                Vector3 P(int i) => new Vector3(pts[i].x, pts[i].y, z);
                Tri(P(0), P(1), P(6), normal, color);
                Tri(P(2), P(3), P(4), normal, color);
                Tri(P(2), P(4), P(5), normal, color);
            }

            var inlay = new Vector2[shape.Length];
            for (int i = 0; i < inlay.Length; i++) inlay[i] = inlayCentre + (shape[i] - inlayCentre) * .56f;

            float half = depth * .5f;
            Face(shape, -half, Vector3.back, Gold);
            Face(shape, half, Vector3.forward, Gold);
            Face(inlay, -half - inlayLift, Vector3.back, Cream);
            Face(inlay, half + inlayLift, Vector3.forward, Cream);
            // Side walls (both outlines are counter-clockwise, so (dy, -dx) points outward).
            for (int i = 0; i < shape.Length; i++)
            {
                Vector2 a = shape[i], b = shape[(i + 1) % shape.Length];
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
            return mesh;
        }

        /// <summary>Open box made of 12 square bars (width x height x depth, bottom at y 0), gold vertex colour.</summary>
        private static Mesh BuildFrameMesh(float width, float height, float depth, float bar)
        {
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            float x = width * .5f, z = depth * .5f;

            void Bar(Vector3 from, Vector3 to)
            {
                Vector3 c = (from + to) * .5f;
                Vector3 size = new Vector3(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y), Mathf.Abs(to.z - from.z));
                size = Vector3.Max(size + Vector3.one * bar, Vector3.one * bar);
                Vector3 h = size * .5f;
                // 6 faces, 4 verts each, outward winding.
                Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var f in n)
                {
                    Vector3 u = f.x != 0f ? Vector3.up : Vector3.right;
                    Vector3 v = Vector3.Cross(f, u);
                    Vector3 fc = c + Vector3.Scale(f, h);
                    Vector3 uu = Vector3.Scale(u, h), vv = Vector3.Scale(v, h);
                    int s = verts.Count;
                    verts.Add(fc - uu - vv); verts.Add(fc + uu - vv); verts.Add(fc + uu + vv); verts.Add(fc - uu + vv);
                    for (int k = 0; k < 4; k++) colors.Add(Gold);
                    // Unity front faces are clockwise seen from outside.
                    bool flip = Vector3.Dot(Vector3.Cross(verts[s + 1] - verts[s], verts[s + 2] - verts[s]), f) < 0f;
                    if (flip) { tris.Add(s); tris.Add(s + 2); tris.Add(s + 1); tris.Add(s); tris.Add(s + 3); tris.Add(s + 2); }
                    else { tris.Add(s); tris.Add(s + 1); tris.Add(s + 2); tris.Add(s); tris.Add(s + 2); tris.Add(s + 3); }
                }
            }

            for (int level = 0; level < 2; level++)
            {
                float y = level * height;
                Bar(new Vector3(-x, y, -z), new Vector3(x, y, -z));
                Bar(new Vector3(-x, y, z), new Vector3(x, y, z));
                Bar(new Vector3(-x, y, -z), new Vector3(-x, y, z));
                Bar(new Vector3(x, y, -z), new Vector3(x, y, z));
            }
            Bar(new Vector3(-x, 0f, -z), new Vector3(-x, height, -z));
            Bar(new Vector3(x, 0f, -z), new Vector3(x, height, -z));
            Bar(new Vector3(-x, 0f, z), new Vector3(-x, height, z));
            Bar(new Vector3(x, 0f, z), new Vector3(x, height, z));

            var mesh = new Mesh { name = "BeaconFrame" };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void LateUpdate()
        {
            if (_mission == null || _compass == null) return;
            var cargo = _mission.Cargo;
            bool delivered = _mission.Delivery == null || _mission.Delivery.IsDelivered || (cargo != null && cargo.IsSecured);
            bool alive = cargo != null && !cargo.IsDestroyed;
            bool show = !delivered && alive;
            if (show) ResolveTarget();

            bool carried = show && cargo.IsCarried;
            _reveal = Mathf.MoveTowards(_reveal, carried ? 1f : 0f, Time.deltaTime * (carried ? 3.5f : 2.5f));
            bool lit = show && _reveal > .001f;
            if (_dropArrow.gameObject.activeSelf != lit) _dropArrow.gameObject.SetActive(lit);
            if (_frame.gameObject.activeSelf != lit) _frame.gameObject.SetActive(lit);

            Bounds cb = show ? CrateBounds(cargo) : default;
            Vector3 toGoal = show ? Flat(_spot - cb.center) : Vector3.zero;
            bool compass = show && toGoal.sqrMagnitude > 1.5f * 1.5f; // no direction to show once it sits on the goal
            if (_compass.gameObject.activeSelf != compass) _compass.gameObject.SetActive(compass);
            if (!show) return;

            float t = Time.time;
            bool near = carried && toGoal.sqrMagnitude < NearDistance * NearDistance;
            float pulse = .5f + .5f * Mathf.Sin(t * (near ? 7f : 2.6f));

            if (compass)
            {
                float bob = Mathf.Sin(t * 3.5f) * .12f;
                _compass.SetPositionAndRotation(new Vector3(cb.center.x, cb.max.y + .8f + bob, cb.center.z), Quaternion.LookRotation(toGoal));
                _compass.localScale = Vector3.one * (carried ? Mathf.Lerp(.85f, .95f, pulse) : .8f);
                float glow = carried ? (near ? Mathf.Lerp(.95f, 1.3f, pulse) : Mathf.Lerp(.95f, 1.15f, pulse)) : 1f;
                Tint(_compassRenderer, glow);
            }
            if (!lit) return;

            // Drop frame + down arrow: pop in with a small overshoot, pulse, faster and brighter near the goal.
            float pop = _reveal < 1f ? EaseOutBack(_reveal) : 1f;
            float frameGlow = near ? Mathf.Lerp(.75f, 1.3f, pulse) : Mathf.Lerp(.8f, 1.05f, pulse);
            _frame.localScale = Vector3.one * (pop * (near ? Mathf.Lerp(1f, 1.05f, pulse) : 1f));
            Tint(_frameRenderer, frameGlow);

            float drop = Mathf.Sin(t * (near ? 6f : 3f)) * (near ? .3f : .2f);
            _dropArrow.SetPositionAndRotation(_spot + Vector3.up * (_dropArrowHeight + drop), Quaternion.Euler(0f, t * (near ? 140f : 70f), 0f));
            _dropArrow.localScale = Vector3.one * (1.1f * pop);
            Tint(_dropArrowRenderer, near ? Mathf.Lerp(.9f, 1.3f, pulse) : Mathf.Lerp(.95f, 1.1f, pulse));
        }

        private void Tint(Renderer r, float glow)
        {
            _block.Clear();
            _block.SetColor(BaseColor, new Color(glow, glow, glow, 1f));
            r.SetPropertyBlock(_block);
        }

        private static Bounds CrateBounds(FragileCargo cargo)
        {
            var renderers = cargo.GetComponentsInChildren<Renderer>();
            Bounds b = default;
            bool any = false;
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? b : new Bounds(cargo.transform.position, Vector3.one);
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
