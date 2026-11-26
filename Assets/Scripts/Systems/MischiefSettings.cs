using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// On/off switches for the "players troll each other" rules (George 2026-11-27, teacher's feedback):
    /// A) tug-of-war jolt, shove and the blame pop-ups; B) harder crate physics on the Hard difficulty only.
    /// Every rule keeps the old code path when switched off. Edit the asset <c>Resources/MischiefSettings</c> in the
    /// Inspector (all machines run the same build, the host decides the gameplay). No asset = the defaults below.
    /// </summary>
    [CreateAssetMenu(menuName = "Expedition/Mischief Settings", fileName = "MischiefSettings")]
    public sealed class MischiefSettings : ScriptableObject
    {
        [Header("A1 Tug-of-war (2+ carriers pulling opposite ways)")]
        [Tooltip("Two carriers walking opposite ways for a while yank the crate out of everyone's hands.")]
        public bool TugOfWar = true;
        [Min(.2f)] public float TugSeconds = 1.2f;
        [Tooltip("Two carriers count as pulling apart when their walking directions point this much against each other (dot product).")]
        [Range(-1f, 0f)] public float TugOppositeDot = -.3f;
        [Min(0)] public int TugDamage = 10;

        [Header("A2 Shove (R near a team-mate)")]
        public bool Shove = true;
        [Min(.5f)] public float ShoveRange = 1.8f;
        [Min(0f)] public float ShoveCooldown = 3.5f;
        [Min(0f)] public float ShoveStaminaCost = 15f;
        [Tooltip("Push given to the shoved player (their ragdoll goes down and lets go of any crate).")]
        [Min(0f)] public float ShoveImpulse = 260f;

        [Header("A3 Blame (who hurt the crate)")]
        [Tooltip("Big banner with the culprit's face, a red -HP mark over their head for a few seconds and their total in the team list.")]
        public bool BlamePopups = true;
        [Min(1f)] public float BlameMarkSeconds = 6f;

        [Header("B Harder crate physics (room difficulty Hard only)")]
        public bool HardCratePhysics = true;
        [Min(0f)] public float HardDamageCooldown = .75f;
        [Min(0f)] public float HardMinImpactVelocity = 1.8f;
        [Min(0)] public int HardPlaceDamage = 8;
        [Tooltip("One of two carriers lets go: the crate tips down on that side and hits the ground.")]
        [Min(0)] public int HardSuddenDropDamage = 10;

        private static MischiefSettings _current;

        /// <summary>The settings asset (Resources/MischiefSettings), or the defaults when there is none.</summary>
        public static MischiefSettings Current
        {
            get
            {
                if (_current != null) return _current;
                _current = Resources.Load<MischiefSettings>("MischiefSettings");
                if (_current == null) { _current = CreateInstance<MischiefSettings>(); _current.hideFlags = HideFlags.DontSave; }
                return _current;
            }
        }

        /// <summary>Rule B is on and the room plays Hard.</summary>
        public static bool HardPhysicsActive => Current.HardCratePhysics && GameRoom.Difficulty >= 2;
    }
}
