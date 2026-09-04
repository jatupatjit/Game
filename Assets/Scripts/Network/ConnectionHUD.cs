using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// ConnectionHUD provides a simple, immediate-mode GUI (IMGUI) overlay for starting and testing
    /// multiplayer sessions (Host, Server, Client).
    /// 
    /// Why this design:
    /// - Zero dependency on complex Canvas UI prefabs during initial development and testing.
    /// - Enables instant testing both inside the Unity Editor and in standalone build instances.
    /// - Clearly separates network session startup logic from game mechanics.
    /// </summary>
    [DisallowMultipleComponent]
    public class ConnectionHUD : MonoBehaviour
    {
        [Header("UI Settings")]
        [Tooltip("Position offset from top-left screen corner")]
        [SerializeField] private Vector2 _guiOffset = new Vector2(15f, 15f);

        [Tooltip("Show or hide the connection HUD overlay")]
        [SerializeField] private bool _showHUD = true;

        private void OnGUI()
        {
            if (!_showHUD) return;

            // Cache reference to the active NetworkManager singleton
            NetworkManager networkManager = NetworkManager.Singleton;

            if (networkManager == null)
            {
                GUILayout.BeginArea(new Rect(_guiOffset.x, _guiOffset.y, 250f, 60f), GUI.skin.box);
                GUILayout.Label("NetworkManager not found in scene!");
                GUILayout.EndArea();
                return;
            }

            // Create a styled box container for the connection controls
            GUILayout.BeginArea(new Rect(_guiOffset.x, _guiOffset.y, 260f, 180f), GUI.skin.box);
            GUILayout.Label("<b>Co-op Physics Game - Network</b>");

            // State 1: Neither Server nor Client is running -> show connection options
            if (!networkManager.IsClient && !networkManager.IsServer)
            {
                // Host Mode: Runs both Server authority AND local Player Client on this machine.
                // In our co-op game, the Host maintains physical authority over the carried object.
                if (GUILayout.Button("Start Host (Server + Player)", GUILayout.Height(30)))
                {
                    networkManager.StartHost();
                }

                // Client Mode: Joins an existing Host/Server as a remote client.
                if (GUILayout.Button("Start Client (Join Host)", GUILayout.Height(30)))
                {
                    networkManager.StartClient();
                }

                // Dedicated Server Mode: Headless or non-player server (useful for tests).
                if (GUILayout.Button("Start Dedicated Server", GUILayout.Height(25)))
                {
                    networkManager.StartServer();
                }
            }
            // State 2: Session is currently active -> show status and disconnect option
            else
            {
                string status = "";
                if (networkManager.IsHost)
                {
                    status = $"<b>Mode:</b> Host\n<b>Local Client ID:</b> {networkManager.LocalClientId}\n<b>Connected Players:</b> {networkManager.ConnectedClientsIds.Count}";
                }
                else if (networkManager.IsServer)
                {
                    status = $"<b>Mode:</b> Dedicated Server\n<b>Connected Players:</b> {networkManager.ConnectedClientsIds.Count}";
                }
                else if (networkManager.IsClient)
                {
                    status = $"<b>Mode:</b> Client\n<b>Local Client ID:</b> {networkManager.LocalClientId}";
                }

                GUILayout.Label(status);

                // Disconnect button to cleanly shut down network loop
                if (GUILayout.Button("Disconnect", GUILayout.Height(28)))
                {
                    networkManager.Shutdown();
                }
            }

            GUILayout.EndArea();
        }
    }
}
