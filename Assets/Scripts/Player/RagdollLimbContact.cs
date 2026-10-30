using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>Forwards a torso/leg limb's collisions to its <see cref="RagdollCharacter"/>.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class RagdollLimbContact : MonoBehaviour
    {
        public RagdollCharacter Owner;
        private Rigidbody _body;

        private void Awake() => _body = GetComponent<Rigidbody>();

        private void OnCollisionEnter(Collision collision)
        {
            if (Owner != null) Owner.OnLimbBump(_body, collision);
        }
    }
}
