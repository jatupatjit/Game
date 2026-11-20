using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Marks a long cargo (long crate, cart) that players grip by its two short ends (local +Z / -Z) instead of by the
    /// nearest long side. RagdollCharacter.ComputeGrip picks the end nearest the player and, when someone already
    /// holds that end, the other one, so two carriers always end up front and back.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CarryGripEnds : MonoBehaviour { }
}
