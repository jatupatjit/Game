using CoopGame.CarrySystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.Network
{
    /// <summary>
    /// Shows new players where the crate goes, without any signboard text: a glowing gold ring on the delivery
    /// spot with a bobbing gold arrow above it, and a smaller arrow over the crate while nobody is carrying it.
    /// When the crate is carried within <see cref="NearDistance"/> of the spot the ring and arrow pulse faster and
    /// brighter. Everything hides once the crate is delivered. Local visuals only (same on every peer).
    /// Added automatically to every level that has a <see cref="LevelMission"/>.
    /// </summary>
    public sealed class DeliveryBeacon : MonoBehaviour
    {
        private const float NearDistance = 15f;
        private const int RingSegments = 48;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color Gold = new(1f, .82f, .25f, 1f);

        private LevelMission _mission;
        private Transform _spotArrow, _crateArrow;
        private LineRenderer _ring, _innerRing;
        private Renderer[] _spotArrowParts, _crateArrowParts;
        private Vector3 _spot;
        private float _radius;
        private MaterialPropertyBlock _block;

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
            var material = Resources.Load<Material>("BeaconGold");
            var zone = _mission.Delivery.GetComponent<Collider>();
            Bounds b = zone != null ? zone.bounds : new Bounds(_mission.Delivery.transform.position, Vector3.one * 4f);
            // The visible surface inside the delivery volume (dais top, cart bed...).
            _spot = new Vector3(b.center.x, b.min.y, b.center.z);
            if (Physics.Raycast(new Vector3(b.center.x, b.max.y + 1f, b.center.z), Vector3.down, out RaycastHit hit,
                    b.size.y + 4f, ~0, QueryTriggerInteraction.Ignore))
                _spot = hit.point;
            _radius = Mathf.Clamp(Mathf.Min(b.extents.x, b.extents.z) * .8f, 1.2f, 4f);

            var ringMaterial = Resources.Load<Material>("BeaconRing") ?? material; // vertex-coloured, so the ring can pulse
            _ring = Ring("Ring", _radius, .16f, ringMaterial);
            _innerRing = Ring("InnerRing", _radius * .62f, .08f, ringMaterial);
            _spotArrow = Arrow("SpotArrow", 2.4f, material, out _spotArrowParts);
            _crateArrow = Arrow("CrateArrow", .8f, material, out _crateArrowParts);
        }

        private LineRenderer Ring(string name, float radius, float width, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = _spot + Vector3.up * .06f;
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = RingSegments;
            line.widthMultiplier = width;
            line.sharedMaterial = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.TransformZ;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < RingSegments; i++)
            {
                // Lying flat: points were laid out in XZ, the rotation turns the line's facing up.
                float a = i * Mathf.PI * 2f / RingSegments;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
            return line;
        }

        /// <summary>Downward arrow: a short shaft and a head made of two tilted bars (a chevron), no collider.</summary>
        private Transform Arrow(string name, float size, Material material, out Renderer[] parts)
        {
            var root = new GameObject(name).transform;
            root.SetParent(transform, false);
            var list = new System.Collections.Generic.List<Renderer>();
            Part(root, new Vector3(0f, .55f, 0f) * size, new Vector3(.22f, .7f, .22f) * size, Quaternion.identity, material, list);
            Part(root, new Vector3(-.17f, .05f, 0f) * size, new Vector3(.2f, .6f, .2f) * size, Quaternion.Euler(0f, 0f, 45f), material, list);
            Part(root, new Vector3(.17f, .05f, 0f) * size, new Vector3(.2f, .6f, .2f) * size, Quaternion.Euler(0f, 0f, -45f), material, list);
            parts = list.ToArray();
            return root;
        }

        private static void Part(Transform parent, Vector3 position, Vector3 scale, Quaternion rotation, Material material,
            System.Collections.Generic.List<Renderer> list)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.transform.localRotation = rotation;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            list.Add(r);
        }

        private void LateUpdate()
        {
            if (_mission == null || _ring == null) return;
            var cargo = _mission.Cargo;
            bool delivered = _mission.Delivery == null || _mission.Delivery.IsDelivered || (cargo != null && cargo.IsSecured);
            bool alive = cargo != null && !cargo.IsDestroyed;
            bool show = !delivered;
            if (_ring.gameObject.activeSelf != show)
            {
                _ring.gameObject.SetActive(show);
                _innerRing.gameObject.SetActive(show);
                _spotArrow.gameObject.SetActive(show);
            }
            bool crateArrow = show && alive && !cargo.IsCarried;
            if (_crateArrow.gameObject.activeSelf != crateArrow) _crateArrow.gameObject.SetActive(crateArrow);
            if (!show) return;

            float t = Time.time;
            bool near = alive && cargo.IsCarried &&
                        (Flat(cargo.transform.position - _spot)).sqrMagnitude < NearDistance * NearDistance;
            float speed = near ? 7f : 2.4f;
            float pulse = .5f + .5f * Mathf.Sin(t * speed);
            float glow = near ? Mathf.Lerp(.85f, 1.9f, pulse) : Mathf.Lerp(.75f, 1.15f, pulse);
            Color c = new Color(Gold.r * glow, Gold.g * glow, Gold.b * glow, 1f);

            _ring.startColor = _ring.endColor = c;
            _innerRing.startColor = _innerRing.endColor = c;
            _ring.widthMultiplier = (near ? .24f : .16f) * Mathf.Lerp(.9f, 1.15f, pulse);
            float ringScale = near ? Mathf.Lerp(1f, 1.08f, pulse) : 1f;
            _ring.transform.localScale = Vector3.one * ringScale;
            _innerRing.transform.localScale = Vector3.one * Mathf.Lerp(.85f, 1.1f, 1f - pulse);
            SetColor(_spotArrowParts, c);

            var cam = Camera.main;
            float bob = Mathf.Sin(t * (near ? 6f : 3f)) * (near ? .35f : .25f);
            _spotArrow.position = _spot + Vector3.up * (3.6f + bob);
            if (cam != null) _spotArrow.rotation = Quaternion.LookRotation(Flat(_spotArrow.position - cam.transform.position) + Vector3.forward * 1e-4f);

            if (crateArrow)
            {
                Bounds cb = CrateBounds(cargo);
                _crateArrow.position = new Vector3(cb.center.x, cb.max.y + .9f + Mathf.Sin(t * 3.4f) * .18f, cb.center.z);
                if (cam != null) _crateArrow.rotation = Quaternion.LookRotation(Flat(_crateArrow.position - cam.transform.position) + Vector3.forward * 1e-4f);
                SetColor(_crateArrowParts, new Color(Gold.r * Mathf.Lerp(.8f, 1.2f, pulse), Gold.g * Mathf.Lerp(.8f, 1.2f, pulse), Gold.b, 1f));
            }
        }

        private static Bounds CrateBounds(FragileCargo cargo)
        {
            var renderers = cargo.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(cargo.transform.position, Vector3.one);
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) if (!(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
            return b;
        }

        private void SetColor(Renderer[] parts, Color c)
        {
            _block.SetColor(BaseColor, c);
            foreach (var r in parts) if (r != null) r.SetPropertyBlock(_block);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
