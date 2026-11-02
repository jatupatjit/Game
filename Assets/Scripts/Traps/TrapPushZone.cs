using System.Collections.Generic;
using CoopGame.CarrySystem;
using CoopGame.Player;
using UnityEngine;

namespace CoopGame.Traps
{
    /// <summary>
    /// A box that keeps pushing everything inside it one way (wind fans, conveyor belts). It never stops.
    /// Players are pushed on their own machine; crates are pushed on the server.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class TrapPushZone : MonoBehaviour
    {
        [Tooltip("Push direction in this object's space")]
        [SerializeField] private Vector3 _direction = Vector3.forward;
        [Tooltip("Push on a ragdoll player (m/s²)")]
        [SerializeField, Min(0f)] private float _playerAcceleration = 16f;
        [Tooltip("Push on the animated player (m/s of drift)")]
        [SerializeField, Min(0f)] private float _controllerDrift = 2.5f;
        [Tooltip("Push on crates (m/s²)")]
        [SerializeField, Min(0f)] private float _cargoAcceleration = 10f;

        private BoxCollider _box;
        private static readonly Collider[] s_hits = new Collider[64];
        private static readonly HashSet<Rigidbody> s_bodies = new();

        private void Awake()
        {
            _box = GetComponent<BoxCollider>();
            _box.isTrigger = true;
        }

        private void FixedUpdate()
        {
            Vector3 scale = transform.lossyScale;
            Vector3 half = Vector3.Scale(_box.size * .5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            int count = Physics.OverlapBoxNonAlloc(transform.TransformPoint(_box.center), half, s_hits, transform.rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Vector3 dir = transform.TransformDirection(_direction).normalized;
            var local = TrapClock.LocalPlayer;
            var ragdoll = local != null ? local.GetComponent<RagdollCharacter>() : null;
            bool fullRagdoll = ragdoll != null && ragdoll.isActiveAndEnabled && ragdoll.FullRagdoll;
            bool controllerPushed = false;
            bool authority = TrapClock.IsAuthority;
            s_bodies.Clear();

            for (int i = 0; i < count; i++)
            {
                Collider other = s_hits[i];
                if (other == null) continue;
                Rigidbody body = other.attachedRigidbody;
                if (local != null && fullRagdoll && ragdoll.OwnsCollider(other))
                {
                    if (body != null && !body.isKinematic && s_bodies.Add(body)) body.AddForce(dir * _playerAcceleration, ForceMode.Acceleration);
                    continue;
                }
                if (local != null && !fullRagdoll && !controllerPushed && other.transform.IsChildOf(local.transform))
                {
                    var controller = local.GetComponent<CharacterController>();
                    if (controller != null && controller.enabled) controller.Move(dir * (_controllerDrift * Time.fixedDeltaTime));
                    controllerPushed = true;
                    continue;
                }
                if (authority && body != null && !body.isKinematic && body.GetComponent<FragileCargo>() != null && s_bodies.Add(body))
                    body.AddForce(dir * _cargoAcceleration, ForceMode.Acceleration);
            }
            System.Array.Clear(s_hits, 0, count);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(.3f, .7f, 1f, .25f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(.3f, .7f, 1f, .9f);
            Gizmos.DrawRay(box.center, _direction.normalized * Mathf.Max(1f, box.size.z * .5f));
        }
#endif
    }
}
