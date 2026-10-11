using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    // Server-only queries use spawned player objects, independently of transport.
    internal static class SessionPlayers
    {
        public static int Count(NetworkManager manager)
        {
            if (manager == null || !manager.IsServer) return 0;
            int count = 0;
            var clients = manager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
                if (Valid(clients[i].PlayerObject)) count++;
            return count;
        }

        private static bool Valid(NetworkObject player) => player != null && player.IsSpawned &&
            player.TryGetComponent<NetworkPlayer>(out _);

        public static bool Pads(NetworkManager manager, BoxCollider left, BoxCollider right,
            int required, out bool occupiedLeft, out bool occupiedRight)
        {
            ulong firstLeft = ulong.MaxValue, firstRight = ulong.MaxValue;
            bool extraLeft = false, extraRight = false;
            var clients = manager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                var player = clients[i].PlayerObject;
                if (!Valid(player) || !player.TryGetComponent<CharacterController>(out var controller)) continue;
                Vector3 feet = player.transform.TransformPoint(controller.center - Vector3.up * controller.height * .5f);
                ulong id = clients[i].ClientId;
                if (Contains(left, feet))
                {
                    if (firstLeft == ulong.MaxValue) firstLeft = id;
                    else if (firstLeft != id) extraLeft = true;
                }
                if (Contains(right, feet))
                {
                    if (firstRight == ulong.MaxValue) firstRight = id;
                    else if (firstRight != id) extraRight = true;
                }
            }
            occupiedLeft = firstLeft != ulong.MaxValue;
            occupiedRight = required > 1 && firstRight != ulong.MaxValue;
            return required == 1 ? occupiedLeft : occupiedLeft && occupiedRight &&
                (firstLeft != firstRight || extraLeft || extraRight);
        }

        private static bool Contains(BoxCollider pad, Vector3 feet)
        {
            if (pad == null || !pad.gameObject.activeInHierarchy) return false;
            Vector3 point = pad.transform.InverseTransformPoint(feet) - pad.center;
            Vector3 half = pad.size * .5f;
            return Mathf.Abs(point.x) <= half.x && Mathf.Abs(point.z) <= half.z && Mathf.Abs(point.y) <= half.y;
        }
    }
}
