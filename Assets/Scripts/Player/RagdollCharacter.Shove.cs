using CoopGame.CarrySystem;
using CoopGame.Network;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// Shove (rule A2, <see cref="MischiefSettings.Shove"/>): R next to a team-mate, hands free, knocks them over.
    /// A shoved carrier lets go of the crate (Knockdown drops it), so the crate's next hit is blamed on the shover.
    /// Costs stamina and has a cooldown so it cannot be spammed. The owner picks the target, the server checks the
    /// distance, tells the target's owner to fall and posts a feed line.
    /// </summary>
    public sealed partial class RagdollCharacter
    {
        private float _nextShoveAt;

        /// <summary>Owner (Update): R pressed with free hands next to a team-mate.</summary>
        private void UpdateShove(bool canAct)
        {
            var rules = MischiefSettings.Current;
            if (!rules.Shove || _input == null || !_input.ShovePressed) return;
            if (!canAct || _carry != null || Time.time < _nextShoveAt || !IsGrounded) return;
            if (_stamina != null && _stamina.CurrentStamina < rules.ShoveStaminaCost) return;
            RagdollCharacter target = FindShoveTarget(rules.ShoveRange);
            if (target == null) return;
            _nextShoveAt = Time.time + rules.ShoveCooldown;
            if (_stamina != null) _stamina.ConsumeStamina(rules.ShoveStaminaCost);
            Vector3 direction = Flat(target.HipsPosition - HipsPosition);
            direction = direction.sqrMagnitude > 1e-4f ? direction.normalized : Flat(transform.forward).normalized;
            // A small lunge of our own so the shove reads on screen.
            if (_physicsRoot != null) _bodies[Chest].AddForce(direction * 25f, ForceMode.Impulse);
            if (!IsSpawned) { ServerShove(target, direction); return; }
            if (IsServer) ServerShove(target, direction);
            else ShoveRpc(target.NetworkObject, direction);
        }

        private RagdollCharacter FindShoveTarget(float range)
        {
            Vector3 me = HipsPosition;
            float yaw = _camera != null ? _camera.Yaw : transform.eulerAngles.y;
            Vector3 look = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            RagdollCharacter best = null;
            float bestScore = float.MaxValue;
            foreach (var other in s_spawned)
            {
                if (other == null || other == this || !other.isActiveAndEnabled) continue;
                Vector3 to = other.HipsPosition - me;
                if (Mathf.Abs(to.y) > 1.5f) continue;
                Vector3 flat = Flat(to);
                float distance = flat.magnitude;
                if (distance > range) continue;
                // Prefer the team-mate in front of the camera; one right behind still counts when nobody else is near.
                float facing = distance > .01f ? Vector3.Dot(flat / distance, look) : 1f;
                float score = distance - facing * .8f;
                if (score < bestScore) { bestScore = score; best = other; }
            }
            return best;
        }

        [Rpc(SendTo.Server)]
        private void ShoveRpc(NetworkObjectReference targetRef, Vector3 direction, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId) return;
            if (!targetRef.TryGet(out NetworkObject o) || !o.TryGetComponent(out RagdollCharacter target)) return;
            ServerShove(target, direction);
        }

        private void ServerShove(RagdollCharacter target, Vector3 direction)
        {
            var rules = MischiefSettings.Current;
            if (!rules.Shove || target == null || target == this) return;
            // Positions on the server lag a little behind the owners': allow some slack over the owner's range.
            if ((Flat(target.HipsPosition - HipsPosition)).magnitude > rules.ShoveRange + 1.2f) return;
            direction = Flat(direction);
            direction = direction.sqrMagnitude > 1e-4f ? direction.normalized : Vector3.forward;
            Vector3 impulse = (direction + Vector3.up * .25f) * rules.ShoveImpulse;
            bool targetCarrying = target._serverCarry != null;
            if (targetCarrying) LevelResults.ServerBlameNext(3f, IsSpawned ? OwnerClientId : 0UL);
            if (IsSpawned)
            {
                target.ShovedRpc(impulse);
                CueRpc(GameplayFeedback.Cue.BodyThud, target.HipsPosition);
                GameRoom.ServerFeed(GameRoom.NameOfClient(OwnerClientId) + " ผลัก " + GameRoom.NameOfClient(target.OwnerClientId) +
                    (targetCarrying ? " ลังหลุดมือ!" : "!"), new Color(1f, .6f, .3f));
            }
            else target.Knockdown(impulse);
        }

        /// <summary>Owner of the shoved player: fall over (and let go of the crate).</summary>
        [Rpc(SendTo.Owner)]
        private void ShovedRpc(Vector3 impulse) => Knockdown(impulse);
    }
}
