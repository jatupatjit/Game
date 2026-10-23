using System;
using System.Threading.Tasks;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

namespace CoopGame.Network
{
    /// <summary>
    /// Unity Relay helper: gives a host a short join code (used as the room code) and lets a client connect with it,
    /// without Steam, port forwarding or IP addresses. Only configures the UnityTransport; callers start host/client.
    /// </summary>
    public static class RelayConnection
    {
        private static async Task EnsureSignedInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        /// <summary>Creates an allocation, points the transport at it and returns the join code.</summary>
        public static async Task<string> PrepareHostAsync(UnityTransport transport, int maxMembers)
        {
            await EnsureSignedInAsync();
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(Math.Max(1, maxMembers - 1));
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            transport.SetRelayServerData(allocation.RelayServer.IpV4, (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData);
            return joinCode;
        }

        /// <summary>Resolves a join code and points the transport at the host's allocation.</summary>
        public static async Task PrepareClientAsync(UnityTransport transport, string joinCode)
        {
            await EnsureSignedInAsync();
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            transport.SetRelayServerData(allocation.RelayServer.IpV4, (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData, allocation.HostConnectionData);
        }
    }
}
