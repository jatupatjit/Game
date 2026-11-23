using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Colliders on this object still collide but are left out when a player picks where to grip the crate
    /// (on the tall stack the hands hold the bottom box, not the boxes stacked on top).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CarryGripIgnore : MonoBehaviour { }
}
