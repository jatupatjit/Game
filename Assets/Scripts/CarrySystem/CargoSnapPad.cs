using System.Collections.Generic;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Optional explicit "safe cargo pad": pressing F with the crate near it glides the crate onto the snap point,
    /// aligns its yaw and locks it there, with no HP loss. Hay pads, checkpoint pads, repair stations and the capstan
    /// lift are discovered automatically by <see cref="CarryableObject"/>; attach this to any other safe spot.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CargoSnapPad : MonoBehaviour
    {
        [Tooltip("Optional centre/yaw of the slot. Defaults to this transform.")]
        [SerializeField] private Transform _snapPoint;
        [Tooltip("Horizontal distance (m) from the slot centre within which F snaps the crate onto the pad.")]
        [SerializeField, Min(0.5f)] private float _snapRadius = 3.2f;
        [SerializeField] private bool _alignRotation = true;
        [SerializeField] private bool _lockOnLand = true;

        public static readonly List<CargoSnapPad> Active = new();

        /// <summary>
        /// The game should make the crate lose HP, so the safe-pad snap + lock is a switch and OFF
        /// for now. Off: F never glides/locks the crate onto a pad, and the capstan lift, checkpoint pads, repair
        /// stations and snap pads cost HP like any other floor (5 HP to set down, normal bump/scrape damage).
        /// Hay bales and cargo carts stay soft as before. Set to true to bring the whole safe-pad behaviour back.
        /// Read on the host (placement and damage run on the server).
        /// </summary>
        public static bool SnapLockEnabled = false;

        public Transform SnapPoint => _snapPoint != null ? _snapPoint : transform;
        public float SnapRadius => _snapRadius;
        public bool AlignRotation => _alignRotation;
        public bool LockOnLand => _lockOnLand;

        private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        private void OnDisable() => Active.Remove(this);
    }
}
