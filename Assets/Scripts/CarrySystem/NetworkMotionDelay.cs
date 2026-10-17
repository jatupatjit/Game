using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Estimates how old a replicated pose is on this peer: transit from its authority (one way)
    /// plus the NetworkTransform interpolation delay. Co-op carry compares hands, bodies and cargo
    /// that live on different peers; without this the remote side always looks one delay behind
    /// and the grip springs/reach limits fight each other while walking.
    /// Transit should come from an in-game measurement (PlayerCarry pings the server): transports
    /// may not report RTT at all (Facepunch/Steam returns 0), so the transport is only a fallback.
    /// </summary>
    public static class NetworkMotionDelay
    {
        /// <summary>Two 30 Hz ticks + 0.1 s lerp smoothing + 0.15 s one-way transit.</summary>
        public const float MaxDelay = .35f;

        /// <summary>Largest one-way transit that is compensated (~300 ms RTT). Beyond it, carry partners
        /// compensating each other's late data become unstable and drop the cargo; leaving the excess
        /// uncompensated only slows the team down instead.</summary>
        public const float MaxCompensatedTransit = .15f;

        public static float CompensatedTransit(float oneWayTransit) => Mathf.Clamp(oneWayTransit, 0f, MaxCompensatedTransit);

        /// <param name="oneWayTransit">Measured one-way transit from the pose's authority, in seconds.</param>
        public static float Estimate(NetworkManager manager, NetworkTransform networkTransform, float oneWayTransit)
        {
            if (manager == null || !manager.IsListening) return 0f;
            float delay = 0f;
            if (networkTransform != null && networkTransform.Interpolate)
            {
                bool remoteOwnerAuthority = !manager.IsServer && !networkTransform.IsServerAuthoritative();
                // Mirrors NetworkTransform's render time: legacy lerp renders a fixed 1 (2) ticks back,
                // the other interpolators use the measured tick latency.
                int ticks = networkTransform.PositionInterpolationType == NetworkTransform.InterpolationTypes.LegacyLerp
                    ? 1 + NetworkTransform.InterpolationBufferTickOffset
                    : Mathf.Max(1, manager.NetworkTimeSystem.TickLatency + NetworkTransform.InterpolationBufferTickOffset);
                if (remoteOwnerAuthority) ticks++;
                delay += ticks / (float)Mathf.Max(1, (int)manager.NetworkConfig.TickRate);
                // Lerp smoothing trails a moving target by roughly its time constant.
                if (networkTransform.PositionLerpSmoothing) delay += networkTransform.PositionMaxInterpolationTime;
            }
            delay += CompensatedTransit(oneWayTransit);
            return Mathf.Clamp(delay, 0f, MaxDelay);
        }

        /// <summary>Fallback one-way transit from the transport's RTT; 0 when the transport does not measure it.</summary>
        public static float HalfRoundTrip(NetworkManager manager, ulong authorityClientId)
        {
            if (manager == null || !manager.IsListening || authorityClientId == manager.LocalClientId) return 0f;
            var transport = manager.NetworkConfig.NetworkTransport;
            if (transport == null) return 0f;
            ulong peer = manager.IsServer ? authorityClientId : NetworkManager.ServerClientId;
            if (manager.IsServer && !manager.ConnectedClients.ContainsKey(peer)) return 0f;
            return Mathf.Min(transport.GetCurrentRtt(peer) * .0005f, MaxDelay); // milliseconds round trip -> seconds one way
        }
    }
}
