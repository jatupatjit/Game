using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// Marks a worn accessory (hat) under the player rig. <see cref="PlayerAppearance.ApplyColor"/> skips everything
    /// under it so the body colour never dyes accessories (George 2026-11-21).
    /// </summary>
    public sealed class CosmeticPart : MonoBehaviour { }
}
