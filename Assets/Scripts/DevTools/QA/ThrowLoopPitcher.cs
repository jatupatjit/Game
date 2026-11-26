using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.DevTools
{
    /// <summary>
    /// Catch practice (test scene <c>Scenes/Test/ThrowLoop</c>): a box-built pitcher throws the crate at the nearest
    /// player again and again, through the same server throw as a player (<see cref="CarryableObject.ThrowObject"/>),
    /// so in-flight state, catching with F held, throw damage and the catch snap all behave as in a level.
    /// Each throw refills the crate to full HP first; the screen shows what the last throw cost.
    /// Host / offline only drives the throws; clients just see the crate fly.
    /// </summary>
    public sealed class ThrowLoopPitcher : MonoBehaviour
    {
        [SerializeField] private FragileCargo _cargo;
        [Tooltip("Where the crate is held before each throw (the pitcher's hands).")]
        [SerializeField] private Transform _hands;
        [Tooltip("Seconds between throws once the crate is free again (landed, or set down after a catch).")]
        [SerializeField, Min(.5f)] private float _interval = 3f;
        [Tooltip("Flight time of each throw (s). Lower = flatter, faster throw.")]
        [SerializeField, Range(.4f, 1.6f)] private float _flightTime = .9f;
        [Tooltip("Aim this far in front of the player's chest (m), so the crate meets their hands.")]
        [SerializeField] private float _leadInFront = .6f;
        [Tooltip("Random aim spread (m) around the target, to practise moving to the crate.")]
        [SerializeField, Min(0f)] private float _spread = .4f;
        [SerializeField] private bool _showResult = true;

        private float _nextThrowAt;
        private bool _flying, _wasCaught;
        private int _hpBefore, _throws, _catches;
        private string _last = "รอโยนครั้งแรก...";

        private void Start()
        {
            if (_cargo == null) _cargo = FindAnyObjectByType<FragileCargo>();
            _nextThrowAt = Time.time + 4f; // give the player time to spawn and stand up
        }

        private static bool Authority => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening ||
                                         NetworkManager.Singleton.IsServer;

        private void FixedUpdate()
        {
            if (_cargo == null || !Authority) return;
            var carry = (CarryableObject)_cargo;

            if (_flying)
            {
                if (carry.IsCarried) _wasCaught = true;
                if (!carry.InFlight || carry.IsCarried)
                {
                    _flying = false;
                    int lost = _hpBefore - _cargo.CurrentHP.Value;
                    if (_wasCaught) _catches++;
                    _last = (_wasCaught ? "รับได้" : "ตกพื้น / ไม่ได้รับ") + (lost > 0 ? $"  HP -{lost}" : "  HP ไม่ลด");
                    _nextThrowAt = Time.time + _interval;
                }
                return;
            }

            // A caught crate stays with the player until they set it down (F) or throw it; then the loop resumes.
            if (carry.IsCarried) { _nextThrowAt = Time.time + _interval; return; }
            if (Time.time < _nextThrowAt) return;
            Transform target = NearestPlayer();
            if (target == null) { _nextThrowAt = Time.time + 1f; return; }
            Throw(target);
        }

        private void Throw(Transform player)
        {
            var body = _cargo.GetComponent<Rigidbody>();
            Vector3 start = _hands != null ? _hands.position : transform.position + Vector3.up * 1.6f;
            body.isKinematic = false;
            body.position = start;
            body.rotation = Quaternion.LookRotation(Flat(player.position - start) + Vector3.forward * 1e-4f);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            _cargo.transform.SetPositionAndRotation(start, body.rotation);

            RefillHp();
            _hpBefore = _cargo.CurrentHP.Value;

            Vector3 toward = Flat(start - player.position).normalized;
            Vector2 jitter = Random.insideUnitCircle * _spread;
            Vector3 target = player.position + Vector3.up * 1.2f + toward * _leadInFront + new Vector3(jitter.x, 0f, jitter.y);
            float t = _flightTime;
            // Ballistic launch that reaches the target in t seconds (the crate's drag is small over one throw).
            Vector3 velocity = (target - start) / t - .5f * Physics.gravity * t;
            ((CarryableObject)_cargo).ThrowObject(ulong.MaxValue, velocity, Random.insideUnitSphere * 2f);
            _flying = true;
            _wasCaught = false;
            _throws++;
        }

        private void RefillHp()
        {
            if (_cargo.CurrentHP.Value < _cargo.MaxHP) _cargo.HealServer(_cargo.MaxHP);
        }

        private Transform NearestPlayer()
        {
            Transform best = null;
            float bestSq = float.MaxValue;
            foreach (var p in FindObjectsByType<CoopGame.Player.RagdollCharacter>())
            {
                if (p == null || !p.isActiveAndEnabled) continue;
                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = p.transform; }
            }
            return best;
        }

        private void OnGUI()
        {
            if (!_showResult || !Authority) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
            GUI.Box(new Rect(20, Screen.height - 150, 520, 70),
                $"ฝึกรับลัง: กด F ค้างรอรับ   รับได้ {_catches}/{_throws}\nครั้งล่าสุด: {_last}", style);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
