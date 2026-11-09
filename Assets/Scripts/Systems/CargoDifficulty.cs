using System.Collections.Generic;
using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.Network
{
    /// <summary>
    /// Turns the level crate into one of the five crate variants for the room difficulty (<see cref="GameRoom"/>):
    /// 1 chicken (jitters while carried), 2 tall stack (hard starts/stops cost HP), 3 glass (+60% damage),
    /// 4 heavy iron (needs two carriers), 5 explosive (a hard hit blows up and knocks nearby players away).
    /// Added at runtime on every machine to the crate of the scene's <see cref="LevelMission"/>; gameplay runs on the
    /// host, the look (extra pieces, tint, sway) on every screen. A crate that already is a variant prefab is left alone.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CargoDifficulty : MonoBehaviour
    {
        private FragileCargo _cargo;
        private Rigidbody _body;
        private int _applied = -1;
        private bool _originalHeavy;
        private Transform _addOn;
        private readonly List<Transform> _pieces = new();
        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private Vector3 _lastVelocity, _lastPosition, _seenVelocity;
        private float _wobble, _nextJitter, _nextBlast;
        private bool _inExtraDamage;
        private float _top = .5f;

        // ── Attach to the level crate on scene load ──
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            if (scene.name != "Lobby") GameRoom.ServerNewLevel(); // host draws this level's crate
            foreach (var mission in Object.FindObjectsByType<LevelMission>())
            {
                var cargo = mission.Cargo;
                if (cargo == null || cargo.GetComponent<CargoVariant>() != null || cargo.GetComponent<CargoDifficulty>() != null) continue;
                cargo.gameObject.AddComponent<CargoDifficulty>();
            }
        }

        private void Awake()
        {
            _cargo = GetComponent<FragileCargo>();
            _body = GetComponent<Rigidbody>();
            _originalHeavy = _cargo != null && _cargo.RequiresTwoCarriers;
            _block = new MaterialPropertyBlock();
            _lastPosition = transform.position;
            FragileCargo.OnCargoDamagedGlobal += OnDamaged;
        }

        private void OnDestroy() => FragileCargo.OnCargoDamagedGlobal -= OnDamaged;

        private static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer;

        private void Apply(int kind)
        {
            _applied = kind;
            if (_addOn != null) { DestroyImmediate(_addOn.gameObject); _addOn = null; }
            _pieces.Clear();
            _cargo.RequiresTwoCarriers = _originalHeavy || kind == 4;
            _renderers = GetComponentsInChildren<Renderer>(true);
            // Top of the crate in local space, from its drawn bounds.
            Bounds b = default; bool any = false;
            foreach (var r in _renderers) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            _top = any ? transform.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z)).y : .5f;
            float size = any ? Mathf.Max(.3f, Mathf.Min(b.size.x, b.size.z) / Mathf.Max(.01f, transform.lossyScale.x)) : 1f;
            Tint(kind == 3 ? new Color(.7f, .92f, 1f) : kind == 4 ? new Color(.55f, .58f, .65f) : kind == 5 ? new Color(1f, .45f, .4f) : Color.white);
            if (kind == 0) return;
            _addOn = new GameObject("DifficultyAddOn").transform;
            _addOn.SetParent(transform, false);
            _addOn.localPosition = new Vector3(0f, _top, 0f);
            float s = size;
            switch (kind)
            {
                case 1: // chicken sitting on the lid
                    var chicken = Part(_addOn, PrimitiveType.Sphere, new Vector3(0f, .16f * s, 0f), new Vector3(.32f, .26f, .4f) * s, Color.white);
                    _pieces.Add(chicken);
                    Part(chicken, PrimitiveType.Sphere, new Vector3(0f, .55f, .45f), new Vector3(.55f, .6f, .5f), Color.white);
                    Part(chicken, PrimitiveType.Cube, new Vector3(0f, .55f, .78f), new Vector3(.18f, .12f, .25f), new Color(1f, .6f, .1f));
                    Part(chicken, PrimitiveType.Cube, new Vector3(0f, .9f, .45f), new Vector3(.1f, .25f, .3f), new Color(.9f, .15f, .15f));
                    break;
                case 2: // two boxes stacked on top
                    var a = Part(_addOn, PrimitiveType.Cube, new Vector3(0f, .16f * s, 0f), new Vector3(.6f, .32f, .6f) * s, new Color(.8f, .6f, .38f));
                    var c = Part(a, PrimitiveType.Cube, new Vector3(0f, 1f, 0f), new Vector3(.75f, 1f, .75f), new Color(.72f, .52f, .32f));
                    _pieces.Add(a); _pieces.Add(c);
                    break;
                case 3: // glass sign
                    Part(_addOn, PrimitiveType.Cube, new Vector3(0f, .02f, 0f), new Vector3(.5f, .04f, .5f) * s, new Color(.6f, .9f, 1f));
                    break;
                case 4: // iron bands
                    Part(_addOn, PrimitiveType.Cube, new Vector3(0f, .01f, 0f), new Vector3(1.04f, .05f, .14f) * s, new Color(.25f, .26f, .3f));
                    Part(_addOn, PrimitiveType.Cube, new Vector3(0f, .01f, 0f), new Vector3(.14f, .05f, 1.04f) * s, new Color(.25f, .26f, .3f));
                    break;
                case 5: // fuse with a blinking spark
                    Part(_addOn, PrimitiveType.Cylinder, new Vector3(0f, .1f * s, 0f), new Vector3(.06f, .1f, .06f) * s, new Color(.15f, .12f, .1f));
                    _pieces.Add(Part(_addOn, PrimitiveType.Sphere, new Vector3(0f, .22f * s, 0f), Vector3.one * .1f * s, new Color(1f, .8f, .2f)));
                    break;
            }
        }

        private void Tint(Color color)
        {
            if (_renderers == null) return;
            foreach (var r in _renderers)
            {
                if (r == null || (_addOn != null && r.transform.IsChildOf(_addOn))) continue;
                r.GetPropertyBlock(_block);
                if (color == Color.white) _block.Clear();
                else { _block.SetColor("_BaseColor", color); _block.SetColor("_Color", color); }
                r.SetPropertyBlock(_block);
            }
        }

        private static readonly Dictionary<PrimitiveType, Mesh> Meshes = new();
        private static Material _litTemplate;

        private static Transform Part(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
        {
            if (!Meshes.TryGetValue(type, out var mesh))
            {
                var temp = GameObject.CreatePrimitive(type);
                mesh = temp.GetComponent<MeshFilter>().sharedMesh;
                DestroyImmediate(temp);
                Meshes[type] = mesh;
            }
            var go = new GameObject(type.ToString(), typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (_litTemplate == null) _litTemplate = Resources.Load<Material>("CosmeticLit");
            var renderer = go.GetComponent<MeshRenderer>();
            if (_litTemplate != null)
            {
                var m = new Material(_litTemplate);
                m.SetColor("_BaseColor", color);
                renderer.sharedMaterial = m;
            }
            return go.transform;
        }

        private void FixedUpdate()
        {
            if (_cargo == null) return;
            int kind = GameRoom.CrateKind;
            if (kind != _applied) Apply(kind);
            if (!IsServer || _cargo.IsSecured || _cargo.IsDestroyed) return;
            Vector3 velocity = _body != null ? _body.linearVelocity : Vector3.zero;
            Vector3 change = Vector3.ProjectOnPlane(velocity - _lastVelocity, Vector3.up);
            _lastVelocity = velocity;
            if (!_cargo.IsCarried) { _wobble = 0f; return; }
            if (kind == 2)
            {
                // George 2026-11-09: about half as harsh as the variant prefab (limit 12 -> 18, drain 4 -> 6, 6 -> 4 HP).
                _wobble = Mathf.Max(0f, _wobble - 6f * Time.fixedDeltaTime) + change.magnitude;
                if (_wobble > 18f) { _wobble = 0f; _cargo.ApplyDamageServer(4, "Tall stack jerked"); }
            }
            if (kind == 1 && _body != null && Time.time >= _nextJitter)
            {
                _nextJitter = Time.time + .45f * Random.Range(.6f, 1.4f);
                Vector2 kick = Random.insideUnitCircle.normalized * 1.6f;
                _body.linearVelocity += new Vector3(kick.x, Random.Range(.5f, 1.2f), kick.y);
                _body.angularVelocity += Random.insideUnitSphere * 2f;
                GameRoom.ServerEffect(GameRoom.EffectCluck, transform.position);
            }
        }

        private void Update()
        {
            if (_pieces.Count == 0 || Time.deltaTime <= 0f) return;
            Vector3 velocity = (transform.position - _lastPosition) / Time.deltaTime;
            _lastPosition = transform.position;
            Vector3 accel = (velocity - _seenVelocity) / Time.deltaTime;
            _seenVelocity = Vector3.Lerp(_seenVelocity, velocity, 1f - Mathf.Exp(-Time.deltaTime * 8f));
            Vector3 local = transform.InverseTransformDirection(Vector3.ProjectOnPlane(accel, Vector3.up)) * .02f;
            if (_applied == 5)
            {
                // blinking fuse spark
                _pieces[0].localScale = Vector3.one * (.06f + .05f * Mathf.Abs(Mathf.Sin(Time.time * 8f)));
                return;
            }
            for (int i = 0; i < _pieces.Count; i++)
            {
                float weight = (i + 1f) / _pieces.Count;
                Quaternion target = _applied == 1
                    ? Quaternion.Euler(12f * Mathf.Sin(Time.time * 9f + i), 20f * Mathf.Sin(Time.time * 3.7f), 0f)
                    : Quaternion.Euler(Mathf.Clamp(local.z, -1f, 1f) * 9f * weight, 0f, -Mathf.Clamp(local.x, -1f, 1f) * 9f * weight);
                _pieces[i].localRotation = Quaternion.Slerp(_pieces[i].localRotation, target, 1f - Mathf.Exp(-Time.deltaTime * 10f));
            }
        }

        // Host: glass takes extra damage, explosive blows up on a hard hit.
        private void OnDamaged(FragileCargo cargo, int damage)
        {
            if (cargo != _cargo || !IsServer || _inExtraDamage || damage <= 0) return;
            if (_applied == 3)
            {
                // The crate has a short damage cooldown after a hit, so the extra comes straight off the HP
                // (never down to 0 by itself: the next real hit breaks it and runs the normal break/restart).
                int extra = Mathf.Max(1, Mathf.RoundToInt(damage * .6f));
                int hp = _cargo.CurrentHP.Value;
                if (hp > 1) _cargo.CurrentHP.Value = Mathf.Max(1, hp - extra);
            }
            else if (_applied == 5 && damage >= 12 && Time.time >= _nextBlast)
            {
                _nextBlast = Time.time + 3f;
                GameRoom.ServerEffect(GameRoom.EffectBlast, transform.position);
                GameRoom.ServerFeed("ลังระเบิด!", new Color(1f, .5f, .2f));
            }
        }
    }
}
