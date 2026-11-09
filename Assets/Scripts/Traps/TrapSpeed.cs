using UnityEngine;

namespace CoopGame.Traps
{
    /// <summary>
    /// One speed knob for a whole trap. Every <see cref="TrapMotor"/> and <see cref="TrapRockStream"/> on this object
    /// or its children runs at this speed (1 = normal, 2 = twice as fast, 0.5 = half). Faster parts also hit harder,
    /// because the hit force comes from how fast the part moves. Keep the same value on Host and Clients (it is saved in
    /// the prefab/scene), so everyone sees the same motion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrapSpeed : MonoBehaviour
    {
        [Tooltip("Trap speed: 1 = normal, 2 = twice as fast, 0.5 = half speed")]
        [SerializeField, Range(.1f, 5f)] private float _speed = 1f;

        public float Value
        {
            get => _speed;
            set => _speed = Mathf.Clamp(value, .1f, 5f);
        }
    }
}
