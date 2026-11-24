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
        private float _mainHeld, _repairHeld;

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
                bool byCargo = _cargoAlsoChooses || (_mainPlate == null && _repairPlate == null);
                if (byCargo && Over(_mainBounds, c)) _choice.Value = (int)Choice.Main;
                else if (byCargo && Over(_repairBounds, c)) _choice.Value = (int)Choice.Repair;
                else
                {
                    _mainHeld = PlayerOn(_mainPlate) ? _mainHeld + Time.fixedDeltaTime : 0f;
                    _repairHeld = PlayerOn(_repairPlate) ? _repairHeld + Time.fixedDeltaTime : 0f;
                    if (_mainPlate != null && _mainHeld >= _plateHoldSeconds) _choice.Value = (int)Choice.Main;
                    else if (_repairPlate != null && _repairHeld >= _plateHoldSeconds) _choice.Value = (int)Choice.Repair;
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
            if (_sign != null)
                _sign.text = choice == 1 ? "เลือกทางหลักแล้ว" : choice == 2 ? "เลือกทางไปร้านช่างลังแล้ว" : "ทางแยก\n<- ซ้าย: ทางหลัก (ง่าย)\nขวา: ร้านช่างลัง ซ่อมฟรี (ยาก) ->\nพาลังเข้าทางไหน อีกทางจะหายไป";
            if (choice != 0)
            {
                // The used-up plates sink with the branch they closed.
                if (_mainPlate != null && choice == 2) _mainPlate.gameObject.SetActive(false);
                if (_repairPlate != null && choice == 1) _repairPlate.gameObject.SetActive(false);
            }
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
