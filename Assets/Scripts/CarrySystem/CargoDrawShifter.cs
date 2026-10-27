using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Client only, on the local player while they carry: draws the held cargo where the server has it now instead
    /// of where its late replica is, so the carrier's own body does not look sunk into it while walking.
    ///
    /// The cargo transform is moved at the very start of LateUpdate, before any arm, body or outline code reads it,
    /// so everything drawn on the crate (both carriers' hands, a team-mate's body, the outline) uses the same pose.
    /// It is put back after rendering, and again before physics if no camera rendered, so the Rigidbody, the
    /// network replica and the movement limit never see the shift.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public class CargoDrawShifter : MonoBehaviour
    {
        private PlayerCarry _carry;
        private Transform _shifted;
        private Vector3 _shift;

        /// <summary>True while a held cargo is drawn shifted this frame (its transform already includes the shift).</summary>
        public static bool Applied { get; private set; }

        private void Awake() => _carry = GetComponent<PlayerCarry>();

        private void OnEnable() => RenderPipelineManager.endContextRendering += OnEndRendering;

        private void OnDisable()
        {
            RenderPipelineManager.endContextRendering -= OnEndRendering;
            Restore();
        }

        private void FixedUpdate() => Restore();

        private void LateUpdate()
        {
            Restore();
            if (_carry == null || _carry.CurrentCarryable == null) return;
            Vector3 shift = _carry.ClientCargoLagOffset();
            if (shift.sqrMagnitude < 1e-6f) return;
            _shifted = _carry.CurrentCarryable.transform;
            _shift = shift;
            _shifted.position += shift;
            Applied = true;
        }

        private void OnEndRendering(ScriptableRenderContext context, List<Camera> cameras) => Restore();

        private void Restore()
        {
            if (_shifted != null) _shifted.position -= _shift;
            _shifted = null;
            Applied = false;
        }
    }
}
