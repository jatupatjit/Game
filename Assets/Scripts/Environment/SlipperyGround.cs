using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Marks ground as slippery: a player standing on any collider under this object gets weak foot grip
    /// (slides, overshoots, hard to stop with a crate). Also gives the colliders a low-friction physics material so
    /// crates slide too.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlipperyGround : MonoBehaviour
    {
        [Tooltip("Foot grip on this ground (1 = normal, lower = more slippery).")]
        [SerializeField, Range(.05f, 1f)] private float _traction = .25f;
        [SerializeField] private bool _slipperyForCrates = true;

        public float Traction => _traction;

        private void Awake()
        {
            if (!_slipperyForCrates) return;
            var ice = new PhysicsMaterial("SlipperyGround") { dynamicFriction = .04f, staticFriction = .06f, frictionCombine = PhysicsMaterialCombine.Minimum };
            foreach (var c in GetComponentsInChildren<Collider>()) if (!c.isTrigger) c.sharedMaterial = ice;
        }

        public static float TractionAt(Collider ground)
        {
            var s = ground != null ? ground.GetComponentInParent<SlipperyGround>() : null;
            return s != null ? s.Traction : 1f;
        }
    }
}
