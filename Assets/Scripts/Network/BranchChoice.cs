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

        private readonly NetworkVariable<int> _choice = new(0);
        private int _shown = -1;
        private Bounds[] _mainBounds, _repairBounds;

        public Choice Current => IsSpawned ? (Choice)_choice.Value : Choice.None;

        public override void OnNetworkSpawn()
        {
            _mainBounds = Capture(_mainObjects);
            _repairBounds = Capture(_repairObjects);
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
                if (Over(_mainBounds, c)) _choice.Value = (int)Choice.Main;
                else if (Over(_repairBounds, c)) _choice.Value = (int)Choice.Repair;
            }
            if (_choice.Value != _shown) Apply(_choice.Value, false);
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
            if (_sign != null)
                _sign.text = choice == 1 ? "เลือกทางหลักแล้ว" : choice == 2 ? "เลือกทางไปร้านช่างลังแล้ว" : "ทางแยก\n<- ซ้าย: ทางหลัก (ง่าย)\nขวา: ร้านช่างลัง ซ่อมฟรี (ยาก) ->\nพาลังเข้าทางไหน อีกทางจะหายไป";
            if (choice == 0) return;
            var losers = choice == 1 ? _repairObjects : _mainObjects;
            foreach (var go in losers)
            {
                if (go == null || !go.activeSelf) continue;
                if (instant || _sinkSeconds <= 0f) go.SetActive(false);
                else StartCoroutine(Sink(go.transform));
            }
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
