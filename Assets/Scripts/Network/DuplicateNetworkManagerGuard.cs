using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// The Lobby scene authors its own NetworkManager. When the host brings everyone back to the Lobby during a
    /// session (<see cref="ExpeditionHUD.ReturnToLobbyTogether"/>), that object loads again beside the running one;
    /// the running session keeps its manager and the new copy is removed. A fresh start (nothing listening) is untouched.
    /// </summary>
    public static class DuplicateNetworkManagerGuard
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] // after NGO resets its statics (SubsystemRegistration)
        private static void Hook()
        {
            NetworkManager.OnInstantiated -= OnInstantiated;
            NetworkManager.OnInstantiated += OnInstantiated;
        }

        private static void OnInstantiated(NetworkManager created)
        {
            var running = NetworkManager.Singleton;
            if (running == null || running == created || !running.IsListening) return;
            Debug.Log("[DuplicateNetworkManagerGuard] Session already running; removing the scene's extra NetworkManager.");
            Object.Destroy(created.gameObject);
        }
    }
}
