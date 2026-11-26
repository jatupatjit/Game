using System.Collections.Generic;
using CoopGame.Network;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Tug-of-war jolt (rule A1, <see cref="MischiefSettings.TugOfWar"/>). Two carriers walking opposite ways (one
    /// presses W, the other S, or they pull apart) for <see cref="MischiefSettings.TugSeconds"/>: the crate shakes
    /// harder and harder, then is yanked out of everyone's hands, flips up and comes down (-10 HP, blamed on both).
    /// Server only; switched off it never runs and the hold pose is unchanged.
    /// </summary>
    public partial class CarryableObject
    {
        private float _tugSince = -1f;
        private readonly List<CarrierInfo> _tugCarriers = new();

        /// <summary>0..1 while two carriers pull apart (the crate shakes), 0 otherwise.</summary>
        public float TugAmount { get; private set; }

        /// <summary>Server, held step: track carriers pulling apart. True when the crate was just yanked free.</summary>
        private bool UpdateTugOfWar()
        {
            var rules = MischiefSettings.Current;
            if (!rules.TugOfWar || _activeCarriers.Count < 2) return ResetTug();

            _tugCarriers.Clear();
            foreach (var carrier in _activeCarriers.Values)
                if (carrier.PlayerTransform != null && (carrier.LeftHandActive || carrier.RightHandActive) &&
                    Time.fixedTime - carrier.InputReceivedAt <= InputStaleSeconds)
                    _tugCarriers.Add(carrier);

            bool apart = false;
            for (int i = 0; i < _tugCarriers.Count && !apart; i++)
                for (int j = i + 1; j < _tugCarriers.Count && !apart; j++)
                {
                    Vector3 a = _tugCarriers[i].InputDirection, b = _tugCarriers[j].InputDirection;
                    a.y = b.y = 0f;
                    if (a.sqrMagnitude < .25f || b.sqrMagnitude < .25f) continue;
                    apart = Vector3.Dot(a.normalized, b.normalized) <= rules.TugOppositeDot;
                }
            if (!apart) return ResetTug();

            if (_tugSince < 0f) _tugSince = Time.fixedTime;
            TugAmount = Mathf.Clamp01((Time.fixedTime - _tugSince) / rules.TugSeconds);
            if (TugAmount < 1f) return false;

            TugJolt(rules);
            return true;
        }

        private bool ResetTug()
        {
            _tugSince = -1f;
            TugAmount = 0f;
            return false;
        }

        /// <summary>Server, end of the held step: rattle the crate while the tug builds up.</summary>
        private void ShakeForTug()
        {
            if (TugAmount <= .15f || _rigidbody == null) return;
            float strength = TugAmount * TugAmount;
            _rigidbody.angularVelocity += Random.insideUnitSphere * (9f * strength);
            _rigidbody.linearVelocity += Random.insideUnitSphere * (1.2f * strength);
        }

        private void TugJolt(MischiefSettings rules)
        {
            var ids = new List<ulong>(_activeCarriers.Keys);
            string names = "";
            foreach (ulong id in ids) names += (names.Length > 0 ? " กับ " : "") + GameRoom.NameOfClient(id);
            ResetTug();
            DetachAllCarriers();
            if (_rigidbody != null)
            {
                Vector3 side = Random.insideUnitSphere; side.y = 0f;
                _rigidbody.useGravity = true;
                _rigidbody.constraints = RigidbodyConstraints.None;
                _rigidbody.linearVelocity = Vector3.up * 3.2f + side.normalized * 1.5f;
                _rigidbody.angularVelocity = Random.onUnitSphere * 7f;
            }
            LevelResults.ServerBlameNext(2f, ids.ToArray());
            OnTugJolt(rules.TugDamage);
            GameRoom.ServerFeed(names + " แย่งกันดึง! ลังหลุดมือ", new Color(1f, .55f, .3f));
            if (IsSpawned) TugJoltFxRpc(transform.position); else PlayTugFx(transform.position);
        }

        [Unity.Netcode.Rpc(Unity.Netcode.SendTo.Everyone)]
        private void TugJoltFxRpc(Vector3 at) => PlayTugFx(at);

        private static void PlayTugFx(Vector3 at)
        {
            GameplayFeedback.Play(GameplayFeedback.Cue.Impact, at, 1f);
            GameplayFeedback.Burst(at, new Color(.75f, .57f, .32f), 26, 4f, .3f);
            CameraShake.Add(at, .7f);
        }

        /// <summary>Server: the tug yanked the crate free (FragileCargo takes the damage).</summary>
        protected virtual void OnTugJolt(int damage) { }
    }
}
