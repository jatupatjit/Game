using System.Collections;
using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Route fork: the first time the CARGO is over any object of one branch, that branch is chosen for the run and
    /// every object of the other branch sinks away and is hidden on every peer (players still standing there fall and
    /// respawn as usual). The server decides once; late joiners see the result straight away.
    /// Branch objects must not be (or contain) NetworkObjects.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BranchChoice : NetworkBehaviour
    {
        public enum Choice { None = 0, Main = 1, Repair = 2 }

        [SerializeField] private FragileCargo _cargo;
        [Tooltip("Easy branch (left at the fork).")]
        [SerializeField] private GameObject[] _mainObjects = System.Array.Empty<GameObject>();
        [Tooltip("Hard branch with the repair shop (right at the fork).")]
        [SerializeField] private GameObject[] _repairObjects = System.Array.Empty<GameObject>();
        [Tooltip("Cargo counts as on a branch object when its centre is inside that object's bounds grown by this much (m).")]
        [SerializeField, Min(0f)] private float _margin = .5f;
        [SerializeField, Min(0f)] private float _heightAbove = 3f;
        [SerializeField, Min(0f)] private float _sinkDistance = 45f;
        [SerializeField, Min(.1f)] private float _sinkSeconds = 2.5f;
        [SerializeField] private TextMesh _sign;
        [Header("Pressure plates (optional)")]
        [Tooltip("A player standing here picks the easy route; the repair route sinks.")]
        [SerializeField] private Transform _mainPlate;
        [Tooltip("A player standing here picks the repair route; the easy route sinks.")]
        [SerializeField] private Transform _repairPlate;
        [SerializeField, Min(.3f)] private float _plateRadius = 1.3f;
        [SerializeField, Min(0f)] private float _plateHoldSeconds = 1f;
        [Tooltip("When plates are set, also let the crate pick by entering a branch.")]
        [SerializeField] private bool _cargoAlsoChooses = true;
        [Tooltip("George 2026-11-27: standing on a plate DESTROYS that plate's route (the other route stays). " +
                 "Off = the old rule: the plate picks its route and the other one sinks.")]
        [SerializeField] private bool _plateDestroysOwnRoute = true;
        [Tooltip("Also sink everything standing on a lost route's islets (trees, fences, stones, slabs, decor) that is " +
                 "not listed in the arrays above. Off = only the listed objects sink, as before.")]
        [SerializeField] private bool _sinkObjectsOnTop = true;
        private float _mainHeld, _repairHeld;
        private System.Collections.Generic.List<GameObject> _mainRiders, _repairRiders;

        private readonly NetworkVariable<int> _choice = new(0);
        private int _shown = -1;
        private Bounds[] _mainBounds, _repairBounds;

        public Choice Current => IsSpawned ? (Choice)_choice.Value : Choice.None;

        public override void OnNetworkSpawn()
        {
            _mainBounds = Capture(_mainObjects);
            _repairBounds = Capture(_repairObjects);
            if (_sinkObjectsOnTop)
            {
                _mainRiders = FindRiders(_mainObjects, _mainBounds, _repairObjects, _repairBounds);
                _repairRiders = FindRiders(_repairObjects, _repairBounds, _mainObjects, _mainBounds);
            }
            _shown = -1;
            Apply(_choice.Value, true);
        }

        private Bounds[] Capture(GameObject[] objects)
        {
            var result = new Bounds[objects.Length];
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] == null) continue;
                bool any = false;
                Bounds b = new(objects[i].transform.position, Vector3.zero);
                foreach (var c in objects[i].GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger) continue;
                    if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
                }
                b.max += Vector3.up * _heightAbove;
                b.Expand(new Vector3(_margin * 2f, 0f, _margin * 2f));
                result[i] = b;
            }
            return result;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned) return;
            if (IsServer && _choice.Value == 0 && _cargo != null && !_cargo.IsDestroyed)
            {
                Vector3 c = _cargo.ApproximateBoundsCenter;
                bool byCargo = _cargoAlsoChooses || (_mainPlate == null && _repairPlate == null);
                if (byCargo && Over(_mainBounds, c)) _choice.Value = (int)Choice.Main;
                else if (byCargo && Over(_repairBounds, c)) _choice.Value = (int)Choice.Repair;
                else
                {
                    _mainHeld = PlayerOn(_mainPlate) ? _mainHeld + Time.fixedDeltaTime : 0f;
                    _repairHeld = PlayerOn(_repairPlate) ? _repairHeld + Time.fixedDeltaTime : 0f;
                    // Destroy-own-route: the main plate sinks the main route, so the repair route is what is left.
                    Choice onMain = _plateDestroysOwnRoute ? Choice.Repair : Choice.Main;
                    Choice onRepair = _plateDestroysOwnRoute ? Choice.Main : Choice.Repair;
                    if (_mainPlate != null && _mainHeld >= _plateHoldSeconds) _choice.Value = (int)onMain;
                    else if (_repairPlate != null && _repairHeld >= _plateHoldSeconds) _choice.Value = (int)onRepair;
                }
            }
            if (_choice.Value != _shown) Apply(_choice.Value, false);
        }

        private bool PlayerOn(Transform plate)
        {
            if (plate == null) return false;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                var player = client.PlayerObject;
                if (player == null) continue;
                Vector3 d = player.transform.position - plate.position;
                if (d.y > -.5f && d.y < 2.5f && new Vector2(d.x, d.z).sqrMagnitude <= _plateRadius * _plateRadius) return true;
            }
            return false;
        }

        private static bool Over(Bounds[] list, Vector3 p)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Length; i++)
                if (list[i].size != Vector3.zero && list[i].Contains(p)) return true;
            return false;
        }

        private void Apply(int choice, bool instant)
        {
            _shown = choice;
            // The old sign explains the old rule (and levels carry no text signs): hidden with the new rule.
            if (_sign != null && _plateDestroysOwnRoute) _sign.gameObject.SetActive(false);
            else if (_sign != null)
                _sign.text = choice == 1 ? "เลือกทางหลักแล้ว" : choice == 2 ? "เลือกทางไปร้านช่างลังแล้ว" : "ทางแยก\n<- ซ้าย: ทางหลัก (ง่าย)\nขวา: ร้านช่างลัง ซ่อมฟรี (ยาก) ->\nพาลังเข้าทางไหน อีกทางจะหายไป";
            if (choice != 0)
            {
                // The used-up plates sink with the branch they closed.
                if (_mainPlate != null && choice == 2) _mainPlate.gameObject.SetActive(false);
                if (_repairPlate != null && choice == 1) _repairPlate.gameObject.SetActive(false);
            }
            if (choice == 0) return;
            var losers = choice == 1 ? _repairObjects : _mainObjects;
            foreach (var go in losers) SinkOrHide(go, instant);
            var riders = choice == 1 ? _repairRiders : _mainRiders;
            if (riders != null) foreach (var go in riders) SinkOrHide(go, instant);
        }

        private void SinkOrHide(GameObject go, bool instant)
        {
            if (go == null || !go.activeSelf) return;
            if (instant || _sinkSeconds <= 0f) go.SetActive(false);
            else StartCoroutine(Sink(go.transform));
        }

        /// <summary>
        /// Everything standing on a branch that is not listed in the arrays: decor, trip stones, slabs, fences, trees,
        /// bridges between two islets of that branch. Each candidate is taken as a group (its highest parent no wider
        /// than 30 m, so a bridge or a stone cluster goes whole). A group sinks only when at least one of its pieces
        /// stands on this branch's ground (ray down) and none stands on other ground (the other branch, the fork islet,
        /// the forest...), so shared pieces stay. Networked objects, the fork and its plates are never touched.
        /// </summary>
        private System.Collections.Generic.List<GameObject> FindRiders(GameObject[] own, Bounds[] ownBounds, GameObject[] other, Bounds[] otherBounds)
        {
            var result = new System.Collections.Generic.List<GameObject>();
            if (ownBounds == null) return result;
            var ownSet = new System.Collections.Generic.HashSet<Transform>();
            var listed = new System.Collections.Generic.HashSet<Transform>();
            foreach (var go in own) if (go != null) { ownSet.Add(go.transform); listed.Add(go.transform); }
            foreach (var go in other) if (go != null) listed.Add(go.transform);

            bool Skip(Transform t)
            {
                for (var u = t; u != null; u = u.parent)
                {
                    if (listed.Contains(u) || u == transform || u == _mainPlate || u == _repairPlate) return true;
                    // Networked things that move (players, crates, NetworkTransform props) are never touched; a static
                    // networked prop (the repair shop) sinks on every peer like decor.
                    var no = u.GetComponent<NetworkObject>();
                    if (no != null && (no.GetComponentInChildren<Unity.Netcode.Components.NetworkTransform>() != null ||
                        no.GetComponentInChildren<Rigidbody>() != null || no.IsPlayerObject)) return true;
                }
                return false;
            }
            var otherSet = new System.Collections.Generic.HashSet<Transform>();
            foreach (var go in other) if (go != null) otherSet.Add(go.transform);
            bool UnderOther(Transform t)
            {
                for (var u = t; u != null; u = u.parent) if (otherSet.Contains(u)) return true;
                return false;
            }
            bool UnderOwn(Transform t)
            {
                for (var u = t; u != null; u = u.parent) if (ownSet.Contains(u)) return true;
                return false;
            }

            // Candidate groups: renderers whose middle is over this branch.
            var groups = new System.Collections.Generic.HashSet<Transform>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled || r.gameObject.scene != gameObject.scene || Skip(r.transform)) continue;
                if (!Over(ownBounds, r.bounds.center)) continue;
                Transform top = r.transform;
                for (var p = top.parent; p != null && p.parent != null; p = p.parent)
                {
                    if (Skip(p)) break;
                    Bounds pb = RenderBounds(p);
                    if (pb.size.x > 30f || pb.size.z > 30f) break;
                    top = p;
                }
                groups.Add(top);
            }

            bool InGroups(Transform t, Transform self)
            {
                for (var u = t; u != null; u = u.parent) if (u != self && groups.Contains(u)) return true;
                return false;
            }

            var hits = new RaycastHit[16];
            foreach (var group in groups)
            {
                int onOwn = 0;
                bool onOther = false, onCommon = false, onRider = false;
                foreach (var r in group.GetComponentsInChildren<Renderer>())
                {
                    Bounds b = r.bounds;
                    int n = Physics.RaycastNonAlloc(new Vector3(b.center.x, b.max.y + .3f, b.center.z), Vector3.down, hits,
                        b.size.y + 6f, ~0, QueryTriggerInteraction.Ignore);
                    System.Array.Sort(hits, 0, n, HitDistance);
                    for (int i = 0; i < n; i++)
                    {
                        Transform h = hits[i].collider.transform;
                        if (h.IsChildOf(group)) continue; // its own pieces
                        if (UnderOwn(h)) onOwn++;
                        else if (UnderOther(h)) onOther = true;
                        else if (InGroups(h, group)) onRider = true; // resting on a piece that sinks with this route
                        else if (IsGround(hits[i].collider)) onCommon = true; // the fork islet, the forest...
                        else continue; // on another small prop: look further down
                        break;
                    }
                    if (onOther) break;
                }
                if (onOther) continue; // also stands on the route that stays
                Bounds gb = RenderBounds(group);
                bool spans = gb.size.x > 8f || gb.size.z > 8f; // a bridge from shared ground onto this route goes with it
                bool floating = onOwn == 0 && !onCommon && (onRider || Over(ownBounds, gb.center)) && !Over(otherBounds, gb.center);
                if ((onOwn > 0 && (!onCommon || spans)) || floating) result.Add(group.gameObject);
            }
            // Drop groups inside other picked groups.
            var picked = new System.Collections.Generic.HashSet<Transform>();
            foreach (var go in result) picked.Add(go.transform);
            result.RemoveAll(go =>
            {
                for (var u = go.transform.parent; u != null; u = u.parent) if (picked.Contains(u)) return true;
                return false;
            });
            return result;
        }

        private static readonly System.Collections.Generic.IComparer<RaycastHit> HitDistance =
            System.Collections.Generic.Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        /// <summary>Terrain-like: an islet mesh or a large solid piece (not a small prop resting on the ground).</summary>
        private static bool IsGround(Collider c)
        {
            if (c is MeshCollider) return true;
            Vector3 s = c.bounds.size;
            return s.x > 6f || s.z > 6f;
        }


        private static Bounds RenderBounds(Transform root)
        {
            bool any = false;
            Bounds b = new(root.position, Vector3.zero);
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // The object's middle is over the branch and it is no wider than the branch piece it stands on.
        private static bool InAny(Bounds[] list, Bounds b)
        {
            if (list == null) return false;
            foreach (var area in list)
                if (area.size != Vector3.zero && area.Contains(b.center) &&
                    b.size.x <= area.size.x + 1f && b.size.z <= area.size.z + 1f && b.min.y >= area.min.y - 1f) return true;
            return false;
        }

        private static bool Touches(Bounds[] list, Bounds b)
        {
            if (list == null) return false;
            foreach (var area in list) if (area.size != Vector3.zero && area.Contains(b.center)) return true;
            return false;
        }

        private IEnumerator Sink(Transform root)
        {
            Vector3 start = root.position;
            float delay = Random.Range(0f, .6f), t = 0f;
            yield return new WaitForSeconds(delay);
            while (t < _sinkSeconds && root != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / _sinkSeconds);
                root.position = start + Vector3.down * (_sinkDistance * k * k) + Random.insideUnitSphere * (.08f * (1f - k));
                yield return null;
            }
            if (root != null) root.gameObject.SetActive(false);
        }
    }
}
