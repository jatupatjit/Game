using CoopGame.Network;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>Local footsteps from actual replicated displacement. Does not drive the player rig.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFootstepAudio : NetworkBehaviour
    {
        [SerializeField, Min(.1f)] private float _strideDistance = .95f;
        [SerializeField, Min(.1f)] private float _minimumInterval = .32f;
        private PlayerMovement _movement;
        private global::Wallclimb _climb;
        private CharacterController _controller;
        private Vector3 _previous;
        private float _distance;
        private float _nextStep;

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            _climb = GetComponent<global::Wallclimb>();
            _controller = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            _previous = transform.position;
            _distance = 0f;
            _nextStep = 0f;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned) return;
            Vector3 delta = transform.position - _previous;
            _previous = transform.position;
            // Respawns and scene teleports must never produce a burst of steps.
            if (delta.sqrMagnitude > 4f || (_climb != null && _climb.IsClimbing) ||
                (IsOwner && (PauseMenu.IsPaused || ExpeditionHUD.BlocksGameplayInput || CarrySystem.MissionFailUI.IsVisible)))
            {
                _distance = 0f;
                return;
            }
            delta.y = 0f;
            float traveled = delta.magnitude;
            if (traveled < .002f)
            {
                _distance = 0f;
                return;
            }
            bool grounded = IsOwner && _movement != null ? _movement.IsGrounded : IsProxyGrounded();
            if (!grounded) { _distance = 0f; return; }
            _distance += traveled;
            if (_distance < _strideDistance || Time.unscaledTime < _nextStep) return;
            _distance = 0f;
            _nextStep = Time.unscaledTime + _minimumInterval;
            GameplayFeedback.Play(GameplayFeedback.Cue.Step, transform.position);
        }

        private bool IsProxyGrounded()
        {
            if (_controller == null) return false;
            float height = _controller.height * Mathf.Abs(transform.lossyScale.y);
            Vector3 foot = transform.TransformPoint(_controller.center) - Vector3.up * (height * .5f - .15f);
            return Physics.Raycast(foot, Vector3.down, .35f, ~0, QueryTriggerInteraction.Ignore);
        }
    }
}
