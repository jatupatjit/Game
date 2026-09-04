using UnityEngine;

namespace CoopGame.CarrySystem
{
    /// <summary>
    /// Contract for any physical object in the game that can be picked up and transported
    /// cooperatively by 1 to 4 players.
    /// 
    /// Adheres to SOLID Interface Segregation Principle (ISP):
    /// - Keeps the contract focused purely on carrying queries, attachment, detachment, and input streaming.
    /// </summary>
    public interface ICarryable
    {
        /// <summary>
        /// Whether the object is currently in a state to be grabbed.
        /// </summary>
        bool CanBeCarried { get; }

        /// <summary>
        /// Number of players currently holding this object.
        /// </summary>
        int CurrentCarrierCount { get; }

        /// <summary>
        /// Maximum number of players that can carry this object simultaneously (typically 2 to 4).
        /// </summary>
        int MaxCarriers { get; }

        /// <summary>
        /// Attempts to attach a player to the nearest available carry socket.
        /// Called exclusively on the Server.
        /// </summary>
        /// <param name="clientId">Network ClientId of the grabbing player</param>
        /// <param name="playerWorldPos">Current world position of the player</param>
        /// <param name="socketIndex">Index of the assigned socket</param>
        /// <returns>True if successfully attached, false if full or invalid</returns>
        bool TryAttachCarrier(ulong clientId, Vector3 playerWorldPos, out int socketIndex);

        /// <summary>
        /// Detaches a player from their assigned socket.
        /// Called exclusively on the Server.
        /// </summary>
        void DetachCarrier(ulong clientId);

        /// <summary>
        /// Updates the directional movement vector and facing heading supplied by a specific carrier.
        /// Called on the Server to aggregate multi-player cooperative inputs.
        /// </summary>
        /// <param name="clientId">Network ClientId of the carrier</param>
        /// <param name="worldMoveDirection">WASD movement vector in world space (for translation/strafing)</param>
        /// <param name="forwardHeading">Camera/look heading in world space (for orientation/turning)</param>
        /// <param name="holdHeight">Dynamic height above ground determined by camera vertical pitch</param>
        void UpdateCarrierInput(ulong clientId, Vector3 worldMoveDirection, Vector3 forwardHeading, float holdHeight);

        /// <summary>
        /// Updates carrier input including independent left and right hand active states.
        /// </summary>
        void UpdateCarrierInput(ulong clientId, Vector3 worldMoveDirection, Vector3 forwardHeading, float holdHeight, bool leftHandActive, bool rightHandActive);

        /// <summary>
        /// Retrieves the world Transform of a given socket index.
        /// </summary>
        Transform GetSocketTransform(int socketIndex);
    }
}
