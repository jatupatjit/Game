using System.Collections.Generic;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// The boxes stacked on a tall-stack crate (George 2026-11-12: the upper boxes should really fall off). The carried
    /// crate keeps its single collider (players grip the bottom box); when a box falls, every screen hides it and drops
    /// a loose physics copy in its place. The copy is local to each machine (looks, no gameplay), lies on the ground for
    /// a while and then shrinks away. Used by <see cref="CargoVariant"/> and <see cref="CoopGame.Network.CargoDifficulty"/>.
    /// </summary>
    public static class CrateStackPieces
    {
        /// <summary>Every screen: how many of <paramref name="pieces"/> (bottom first) are shown, and drop the ones that
        /// just fell as loose boxes (when <paramref name="animate"/>; a late joiner just hides them).</summary>
        public static void Show(IList<Transform> pieces, int fallen, Vector3 crateVelocity, Vector3 push, Collider[] crateColliders, bool animate)
        {
            if (pieces == null) return;
            fallen = Mathf.Clamp(fallen, 0, pieces.Count);
            // Top pieces fall first.
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                var piece = pieces[i];
                if (piece == null) continue;
                bool shouldFall = i >= pieces.Count - fallen;
                if (!shouldFall)
                {
                    if (!piece.gameObject.activeSelf) { piece.gameObject.SetActive(true); piece.localRotation = Quaternion.identity; }
                    continue;
                }
                if (!piece.gameObject.activeSelf) continue;
                if (animate) Drop(piece, crateVelocity, push, crateColliders);
                piece.gameObject.SetActive(false);
            }
        }

        private static void Drop(Transform piece, Vector3 crateVelocity, Vector3 push, Collider[] crateColliders)
        {
            var copy = Object.Instantiate(piece.gameObject, piece.position, piece.rotation);
            copy.name = piece.name + "_Fallen";
            copy.transform.localScale = piece.lossyScale;
            foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            var box = copy.GetComponent<BoxCollider>(); // stack pieces are cube meshes
            if (box == null) box = copy.AddComponent<BoxCollider>();
            box.enabled = true;
            var ignore = copy.GetComponent<CarryGripIgnore>();
            if (ignore != null) Object.Destroy(ignore);
            var body = copy.AddComponent<Rigidbody>();
            body.mass = 1.5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Vector3 side = Vector3.ProjectOnPlane(push, Vector3.up);
            if (side.sqrMagnitude < .01f) { Vector2 r = Random.insideUnitCircle.normalized; side = new Vector3(r.x, 0f, r.y); }
            body.linearVelocity = crateVelocity + side.normalized * Random.Range(1.6f, 2.4f) + Vector3.up * Random.Range(.8f, 1.4f);
            body.angularVelocity = Vector3.Cross(Vector3.up, side.normalized) * Random.Range(3f, 6f) + Random.insideUnitSphere;
            // The copy starts resting on the crate: never collide with the crate (it would bump or damage it).
            if (crateColliders != null)
                foreach (var c in crateColliders) if (c != null) Physics.IgnoreCollision(box, c);
            copy.AddComponent<FallenStackPiece>();
            CoopGame.Network.GameplayFeedback.Play(CoopGame.Network.GameplayFeedback.Cue.Impact, piece.position, .5f);
        }
    }

    /// <summary>A loose box that fell off a tall stack: lies for a while, then shrinks away.</summary>
    public sealed class FallenStackPiece : MonoBehaviour
    {
        private const float Lifetime = 15f, ShrinkSeconds = 1f;
        private float _born;
        private Vector3 _scale;

        private void Start() { _born = Time.time; _scale = transform.localScale; }

        private void Update()
        {
            float age = Time.time - _born;
            if (age < Lifetime) return;
            float k = 1f - (age - Lifetime) / ShrinkSeconds;
            if (k <= 0f || transform.position.y < -60f) { Destroy(gameObject); return; }
            transform.localScale = _scale * k;
        }
    }
}
