using System.Collections.Generic;
using CoopGame.CarrySystem;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Player
{
    /// <summary>
    /// Aimed-throw preview for the local player (<see cref="RagdollCharacter"/>, hold Q while carrying): flies the
    /// crate's launch velocity through the same gravity and linear damping the physics step uses, from the crate's
    /// centre of mass, sweeping a sphere the size of the crate so the path stops where the crate would first touch
    /// something. Draws the curve, arrows sliding along it, a ring where it lands (green on the ground, orange on a
    /// wall), a light beam rising from it (tall enough to show above the thrower's head from the normal camera) and
    /// the distance as text. While aiming, the carried crate is drawn as a faint ghost (<see cref="CargoGhostAlpha"/>)
    /// so it does not hide the path. Only on the thrower's screen; nothing is sent over the network.
    /// </summary>
    public sealed class ThrowPreview : MonoBehaviour
    {
        private const int MaxSteps = 180;          // 3.6 s at 50 Hz
        // Big, high-contrast marks (George 2026-11-05): every bright mark has a dark copy just behind it.
        private const float ArrowSpacing = 1.1f, ArrowSpeed = 2.6f, ArrowSize = .55f, RingRadius = 1.2f;
        private const float RippleTime = 1.1f;
        private static readonly Color Shade = new Color(0f, 0f, 0f, .55f);
        private const float BeamHeight = 5f, BeamWidth = .35f;
        /// <summary>How much of the carried crate still shows while aiming (George 2026-11-05: the crate fades more
        /// than the body, it sits in the middle of the screen).</summary>
        public const float CargoGhostAlpha = .2f;
        private static readonly Color Weak = new Color(1f, 1f, 1f, .85f), Strong = new Color(1f, .75f, .2f, .95f);
        private static readonly Color Ground = new Color(.35f, 1f, .45f, .9f), Wall = new Color(1f, .55f, .2f, .9f);

        private readonly Vector3[] _points = new Vector3[MaxSteps + 1];
        private readonly float[] _length = new float[MaxSteps + 1];
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private LineRenderer _line;
        private Material _lineMaterial, _arrowMaterial, _ringMaterial, _beamMaterial, _ghostMaterial, _shadeMaterial;
        private MaterialPropertyBlock _block;
        private Mesh _arrowMesh, _ringMesh, _beamMesh;
        private Text _distanceText;
        private CarryableObject _ghostOf;
        private readonly List<(Renderer renderer, bool wasEnabled)> _hiddenCargo = new();
        private readonly List<(MeshFilter mesh, Transform at)> _ghostParts = new();
        private int _count;
        private bool _visible, _landed;
        private Vector3 _landPoint, _landNormal;

        public bool Visible => _visible;
        public bool Landed => _landed;
        public Vector3 LandingPoint => _landPoint;

        public static ThrowPreview Create(Transform owner)
        {
            var go = new GameObject("ThrowPreview");
            go.transform.SetParent(owner, false);
            var preview = go.AddComponent<ThrowPreview>();
            preview.Build();
            return preview;
        }

        private void Build()
        {
            // A Resources material keeps the shader in standalone builds (Shader.Find alone may be stripped).
            var template = Resources.Load<Material>("ThrowPreview");
            Shader shader = template != null ? template.shader : Shader.Find("Custom/ThrowPreview");
            Material Make(float pull)
            {
                var m = template != null ? new Material(template) : new Material(shader);
                m.hideFlags = HideFlags.DontSave;
                m.SetFloat("_DepthPull", pull);
                // The carried crate sits right in front of the camera and would hide the path: draw over everything.
                m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                return m;
            }
            _lineMaterial = Make(.02f);
            _arrowMaterial = Make(.03f);
            _ringMaterial = Make(.05f);

            _line = gameObject.AddComponent<LineRenderer>();
            _line.sharedMaterial = _lineMaterial;
            _line.useWorldSpace = true;
            _line.alignment = LineAlignment.View;
            _line.numCapVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.widthCurve = new AnimationCurve(new Keyframe(0f, .05f), new Keyframe(1f, .12f));
            _line.enabled = false;

            _beamMaterial = Make(0f);
            // Dark copies draw first (lower queue), so the bright mark always sits on a dark edge.
            _shadeMaterial = Make(.04f);
            _shadeMaterial.renderQueue = _arrowMaterial.renderQueue - 1;
            _shadeMaterial.SetColor("_Color", Shade);
            _block = new MaterialPropertyBlock();
            _arrowMesh = BuildArrow();
            _ringMesh = BuildRing(RingRadius, RingRadius * .72f, 48);
            _beamMesh = BuildBeam();

            // The crate ghost: the see-through highlight shader, normal depth test, brighter at the edges.
            var xray = Resources.Load<Material>("HighlightXRay");
            if (xray != null)
            {
                _ghostMaterial = new Material(xray) { hideFlags = HideFlags.DontSave };
                _ghostMaterial.SetColor("_Color", new Color(1f, 1f, 1f, CargoGhostAlpha));
                _ghostMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                _ghostMaterial.SetFloat("_DepthPull", 0f);
            }

            // Distance text on its own overlay canvas, placed over the top of the beam each frame.
            var canvasObject = new GameObject("ThrowPreviewLabel", typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            _distanceText = UITheme.MakeText(canvasObject.transform, "Distance", "", 30, UITextRole.Primary, TextAnchor.MiddleCenter);
            _distanceText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.AddOutline(_distanceText, 3f);
            ((RectTransform)_distanceText.transform).sizeDelta = new Vector2(200f, 50f);
            _distanceText.enabled = false;
        }

        public void Hide()
        {
            _visible = false;
            if (_line != null) _line.enabled = false;
            if (_distanceText != null) _distanceText.enabled = false;
            ShowCargo();
        }

        /// <summary>Swaps the carried crate's own renderers for a faint ghost (drawn in LateUpdate).</summary>
        private void GhostCargo(CarryableObject cargo)
        {
            if (_ghostOf == cargo || _ghostMaterial == null) return;
            ShowCargo();
            _ghostOf = cargo;
            foreach (var renderer in cargo.GetComponentsInChildren<Renderer>())
            {
                if (renderer is ParticleSystemRenderer) continue;
                _hiddenCargo.Add((renderer, renderer.enabled));
                if (renderer.enabled && renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) &&
                    filter.sharedMesh != null && !renderer.name.StartsWith("XRay"))
                    _ghostParts.Add((filter, renderer.transform));
                renderer.enabled = false;
            }
        }

        /// <summary>Puts the crate's renderers back as they were.</summary>
        private void ShowCargo()
        {
            foreach (var (renderer, wasEnabled) in _hiddenCargo)
                if (renderer != null) renderer.enabled = wasEnabled;
            _hiddenCargo.Clear();
            _ghostParts.Clear();
            _ghostOf = null;
        }

        /// <summary>Recomputes and shows the path of <paramref name="cargo"/> launched at <paramref name="velocity"/>.</summary>
        public void Show(CarryableObject cargo, Vector3 velocity, float charge, int ignoreLayer)
        {
            if (cargo == null) { Hide(); return; }
            var body = cargo.GetComponent<Rigidbody>();
            Vector3 start = body != null ? body.worldCenterOfMass : cargo.transform.position;
            float damping = body != null ? body.linearDamping : 0f;
            float radius = CargoRadius(cargo);
            Simulate(cargo, start, velocity, damping, radius, ~(1 << ignoreLayer));

            Color tint = Color.Lerp(Weak, Strong, charge);
            _lineMaterial.SetColor("_Color", Color.white);
            _arrowMaterial.SetColor("_Color", tint);
            _ringMaterial.SetColor("_Color", _landNormal.y > .6f ? Ground : Wall);
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
                new[] { new GradientAlphaKey(.15f, 0f), new GradientAlphaKey(tint.a, .25f), new GradientAlphaKey(tint.a, 1f) });
            _line.colorGradient = fade;
            _line.positionCount = _count;
            _line.SetPositions(_points);
            _line.enabled = _count > 1;
            _visible = true;
            GhostCargo(cargo);
        }

        private static float CargoRadius(CarryableObject cargo)
        {
            bool any = false;
            Bounds b = default;
            foreach (var c in cargo.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (!any) return .15f;
            float smallest = Mathf.Min(b.extents.x, Mathf.Min(b.extents.y, b.extents.z));
            return Mathf.Clamp(smallest * .9f, .05f, .35f);
        }

        private void Simulate(CarryableObject cargo, Vector3 p, Vector3 v, float damping, float radius, int mask)
        {
            float dt = Time.fixedDeltaTime > 1e-4f ? Time.fixedDeltaTime : .02f;
            Vector3 g = Physics.gravity;
            _points[0] = p;
            _length[0] = 0f;
            _count = 1;
            _landed = false;
            _landNormal = Vector3.up;
            for (int i = 0; i < MaxSteps; i++)
            {
                // PhysX order: gravity into the velocity, damping, then the move.
                v += g * dt;
                v *= 1f / (1f + dt * damping);
                Vector3 next = p + v * dt;
                Vector3 step = next - p;
                float dist = step.magnitude;
                if (dist > 1e-5f && FirstHit(cargo, p, step / dist, dist, radius, mask, out RaycastHit hit))
                {
                    Vector3 centre = p + step / dist * hit.distance;
                    Add(centre);
                    _landed = true;
                    _landPoint = hit.point;
                    _landNormal = hit.normal;
                    return;
                }
                p = next;
                Add(p);
            }
            _landPoint = p;
        }

        private void Add(Vector3 point)
        {
            _points[_count] = point;
            _length[_count] = _length[_count - 1] + Vector3.Distance(_points[_count - 1], point);
            _count++;
        }

        private bool FirstHit(CarryableObject cargo, Vector3 from, Vector3 dir, float dist, float radius, int mask, out RaycastHit best)
        {
            best = default;
            int n = Physics.SphereCastNonAlloc(from, radius, dir, _hits, dist, mask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.distance <= 0f && h.point == Vector3.zero) continue; // started inside: not a landing
                if (h.collider.GetComponentInParent<CarryableObject>() == cargo) continue;
                if (h.distance >= nearest) continue;
                nearest = h.distance;
                best = h;
            }
            return nearest < float.MaxValue;
        }

        private void LateUpdate()
        {
            if (!_visible || _count < 2) return;
            Camera view = Camera.main;
            Vector3 eye = view != null ? view.transform.position : _points[0] + Vector3.up;
            int layer = gameObject.layer;

            // Arrows sliding forward along the path (skip the first bit, it sits inside the crate).
            float total = _length[_count - 1];
            float offset = Mathf.Repeat(Time.time * ArrowSpeed, ArrowSpacing);
            int seg = 1;
            Color arrowTint = _arrowMaterial.GetColor("_Color");
            for (float s = .6f + offset; s < total - .2f; s += ArrowSpacing)
            {
                while (seg < _count - 1 && _length[seg] < s) seg++;
                float span = Mathf.Max(1e-4f, _length[seg] - _length[seg - 1]);
                Vector3 at = Vector3.Lerp(_points[seg - 1], _points[seg], (s - _length[seg - 1]) / span);
                Vector3 tangent = (_points[seg] - _points[seg - 1]).normalized;
                if (tangent.sqrMagnitude < .5f) continue;
                Vector3 toEye = eye - at;
                var rotation = Quaternion.LookRotation(tangent, Vector3.Cross(tangent, Vector3.Cross(toEye, tangent)));
                // Pops in leaving the crate, swells along the way, fades as it reaches the ring.
                float grow = Mathf.SmoothStep(0f, 1f, (s - .6f) / ArrowSpacing);
                float fadeOut = Mathf.Clamp01((total - .2f - s) / ArrowSpacing);
                float size = ArrowSize * grow * Mathf.Lerp(.8f, 1.15f, s / Mathf.Max(1f, total));
                if (size < .02f) continue;
                Color c = arrowTint;
                c.a *= fadeOut;
                Graphics.DrawMesh(_arrowMesh, Matrix4x4.TRS(at, rotation, Vector3.one * size * 1.3f), _shadeMaterial, layer, null, 0, Tint(Shade, fadeOut));
                Graphics.DrawMesh(_arrowMesh, Matrix4x4.TRS(at, rotation, Vector3.one * size), _arrowMaterial, layer, null, 0, Tint(c, 1f));
            }

            // Landing ring, flat on what it hits, gently pulsing, with a light beam and the distance above it.
            if (_landed)
            {
                float pulse = 1f + .1f * Mathf.Sin(Time.time * 6f);
                var flat = Quaternion.FromToRotation(Vector3.up, _landNormal);
                Vector3 ground = _landPoint + _landNormal * .02f;
                Color ringTint = _ringMaterial.GetColor("_Color");
                Graphics.DrawMesh(_ringMesh, Matrix4x4.TRS(ground, flat, Vector3.one * pulse * 1.12f), _shadeMaterial, layer, null, 0, Tint(Shade, 1f));
                Graphics.DrawMesh(_ringMesh, Matrix4x4.TRS(ground, flat, Vector3.one * pulse), _ringMaterial, layer, null, 0, Tint(ringTint, 1f));
                for (int r = 0; r < 2; r++)
                {
                    float t = Mathf.Repeat(Time.time / RippleTime + r * .5f, 1f);
                    Graphics.DrawMesh(_ringMesh, Matrix4x4.TRS(ground, flat, Vector3.one * Mathf.Lerp(.3f, 1.6f, t)), _ringMaterial,
                        layer, null, 0, Tint(ringTint, (1f - t) * .8f));
                }
                float dot = .28f + .06f * Mathf.Abs(Mathf.Sin(Time.time * 4f));
                Graphics.DrawMesh(_ringMesh, Matrix4x4.TRS(ground, flat, Vector3.one * dot), _ringMaterial, layer, null, 0, Tint(ringTint, 1f));

                Vector3 toEyeFlat = eye - _landPoint;
                toEyeFlat.y = 0f;
                var facing = toEyeFlat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toEyeFlat) : Quaternion.identity;
                Color beam = _ringMaterial.GetColor("_Color");
                beam.a = .55f + .15f * Mathf.Sin(Time.time * 6f);
                _beamMaterial.SetColor("_Color", beam);
                Graphics.DrawMesh(_beamMesh, Matrix4x4.TRS(_landPoint, facing, new Vector3(BeamWidth, BeamHeight, 1f)), _beamMaterial, layer);

                Vector3 screen = view != null ? view.WorldToScreenPoint(_landPoint + Vector3.up * (BeamHeight * .55f)) : Vector3.back;
                bool showText = screen.z > 0f;
                _distanceText.enabled = showText;
                if (showText)
                {
                    float metres = Vector3.Distance(new Vector3(_points[0].x, 0f, _points[0].z), new Vector3(_landPoint.x, 0f, _landPoint.z));
                    _distanceText.text = metres.ToString("0.0") + " ม.";
                    _distanceText.fontSize = Mathf.RoundToInt(42f * Screen.height / 1080f);
                    _distanceText.color = Color.Lerp(Color.white, new Color(beam.r, beam.g, beam.b, 1f), .35f);
                    _distanceText.transform.position = screen;
                }
            }
            else if (_distanceText != null) _distanceText.enabled = false;

            // The carried crate as a faint ghost, so the path behind it shows.
            foreach (var (mesh, at) in _ghostParts)
                if (mesh != null && at != null)
                    for (int sub = 0; sub < mesh.sharedMesh.subMeshCount; sub++)
                        Graphics.DrawMesh(mesh.sharedMesh, at.localToWorldMatrix, _ghostMaterial, layer, null, sub);
        }

        private MaterialPropertyBlock Tint(Color color, float alpha)
        {
            color.a *= alpha;
            _block.SetColor("_Color", color);
            return _block;
        }

        /// <summary>A unit quad standing on its bottom edge (x -0.5..0.5, y 0..1), solid in the middle, fading up.</summary>
        private static Mesh BuildBeam()
        {
            var mesh = new Mesh { name = "ThrowBeam", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[]
            {
                new Vector3(-.5f, 0f, 0f), new Vector3(.5f, 0f, 0f), new Vector3(-.5f, 1f, 0f), new Vector3(.5f, 1f, 0f),
                new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f),
            };
            Color edge = new Color(1f, 1f, 1f, .2f), core = Color.white, top = new Color(1f, 1f, 1f, 0f);
            mesh.colors = new[] { edge, edge, top, top, core, top };
            mesh.triangles = new[] { 0, 4, 2, 4, 5, 2, 4, 1, 5, 1, 3, 5 };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A flat chevron in the XY plane pointing along +Z (seen face-on from +Y after the LookRotation).</summary>
        private static Mesh BuildArrow()
        {
            // Chevron: two wings meeting at the tip, in the plane spanned by +Z (forward) and +X.
            var v = new[]
            {
                new Vector3(0f, 0f, .5f), new Vector3(-.5f, 0f, -.3f), new Vector3(-.28f, 0f, -.3f),
                new Vector3(0f, 0f, .12f), new Vector3(.28f, 0f, -.3f), new Vector3(.5f, 0f, -.3f),
            };
            var mesh = new Mesh { name = "ThrowArrow", hideFlags = HideFlags.DontSave };
            mesh.vertices = v;
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildRing(float outer, float inner, int segments)
        {
            var vertices = new Vector3[segments * 2];
            var colors = new Color[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices[i * 2] = d * outer;
                vertices[i * 2 + 1] = d * inner;
                colors[i * 2] = colors[i * 2 + 1] = Color.white;
                int n = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2; triangles[t + 1] = n * 2; triangles[t + 2] = i * 2 + 1;
                triangles[t + 3] = i * 2 + 1; triangles[t + 4] = n * 2; triangles[t + 5] = n * 2 + 1;
            }
            var mesh = new Mesh { name = "ThrowRing", hideFlags = HideFlags.DontSave, vertices = vertices, colors = colors, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null) Destroy(_lineMaterial);
            if (_arrowMaterial != null) Destroy(_arrowMaterial);
            if (_ringMaterial != null) Destroy(_ringMaterial);
            if (_arrowMesh != null) Destroy(_arrowMesh);
            if (_ringMesh != null) Destroy(_ringMesh);
            if (_beamMaterial != null) Destroy(_beamMaterial);
            if (_beamMesh != null) Destroy(_beamMesh);
            if (_ghostMaterial != null) Destroy(_ghostMaterial);
            if (_shadeMaterial != null) Destroy(_shadeMaterial);
            ShowCargo();
        }
    }
}
