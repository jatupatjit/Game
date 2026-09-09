using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// NetworkPlayer coordinates player components (Input, Movement, Camera) with
    /// Unity Netcode for GameObjects (NGO).
    /// 
    /// Adheres to SOLID Principles:
    /// - Acts as an Orchestrator/Mediator for the player GameObject.
    /// - Delegates input reading to PlayerInputReader.
    /// - Delegates physics execution to PlayerMovement.
    /// - Delegates camera tracking to PlayerCameraController.
    /// - Clearly delineates Server vs Client vs Local Owner authority boundaries.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(PlayerInputReader))]
    [DisallowMultipleComponent]
    public class NetworkPlayer : NetworkBehaviour
    {
        [Header("Component References")]
        [SerializeField] private PlayerMovement _movement;
        [SerializeField] private PlayerInputReader _inputReader;
        [SerializeField] private PlayerCameraController _cameraController;
        [SerializeField] private CharacterController _characterController;
        [SerializeField] private CoopGame.CarrySystem.PlayerCarry _playerCarry;

        [Header("Visuals (Optional)")]
        [Tooltip("Renderer to tint with distinct player colors for easy multiplayer visual identification")]
        [SerializeField] private Renderer _playerRenderer;

        // Distinct colors for players 0, 1, 2, 3
        private static readonly Color[] PlayerColors = new Color[]
        {
            new Color(0.2f, 0.6f, 1.0f), // Player 1: Soft Sky Blue
            new Color(1.0f, 0.4f, 0.4f), // Player 2: Soft Coral Red
            new Color(0.3f, 0.85f, 0.4f), // Player 3: Soft Emerald Green
            new Color(1.0f, 0.8f, 0.2f)  // Player 4: Soft Amber Yellow
        };

        private void Awake()
        {
            if (_movement == null) _movement = GetComponent<PlayerMovement>();
            if (_inputReader == null) _inputReader = GetComponent<PlayerInputReader>();
            if (_cameraController == null) _cameraController = GetComponent<PlayerCameraController>();
            if (_characterController == null) _characterController = GetComponent<CharacterController>();
            if (_playerCarry == null) _playerCarry = GetComponent<CoopGame.CarrySystem.PlayerCarry>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // =========================================================================
            // MULTIPLAYER AUTHORITY & COMPONENT CONFIGURATION
            // =========================================================================
            if (IsOwner)
            {
                // [Local Owner Client Logic]
                // This instance represents the human sitting at THIS computer/screen.
                // We MUST activate the camera, input listeners, and local CharacterController.
                _inputReader.enabled = true;
                _inputReader.EnableInput();

                if (_cameraController != null)
                {
                    _cameraController.SetOwnershipState(true);
                }

                // If a default scene camera exists, disable it so it doesn't conflict with the player camera
                Camera[] sceneCameras = FindObjectsByType<Camera>();
                foreach (Camera cam in sceneCameras)
                {
                    if (cam.transform.root != transform && cam.gameObject.name.Contains("Main Camera"))
                    {
                        cam.gameObject.SetActive(false);
                    }
                }

                // Ensure player spawns flush on ground (y = 1.05m) and spaced out by ClientId
                Vector3 currentPos = transform.position;
                if (currentPos.sqrMagnitude < 0.1f || currentPos.y < 0.5f)
                {
                    Vector3 spawnPos = CalculateSpawnPosition(OwnerClientId);
                    if (_characterController != null) _characterController.enabled = false;
                    transform.position = spawnPos;
                    if (_characterController != null) _characterController.enabled = true;
                    if (_movement != null) _movement.ResetVelocity();
                }

                _characterController.enabled = true;
                _movement.enabled = true;

                Debug.Log($"[NetworkPlayer] Local Player initialized with Owner ClientId: {OwnerClientId} at {transform.position}");
            }
            else
            {
                // [Remote Proxy Client Logic]
                // This instance represents another player connected over the network.
                // We MUST disable local input and camera to prevent hijacking this screen/controls!
                _inputReader.DisableInput();
                _inputReader.enabled = false;

                if (_cameraController != null)
                {
                    _cameraController.SetOwnershipState(false);
                }

                // Important NGO gotcha: CharacterController can conflict with incoming
                // NetworkTransform position interpolation if left enabled on non-owners.
                _characterController.enabled = false;
                _movement.enabled = false;

                Debug.Log($"[NetworkPlayer] Remote Proxy Player spawned for ClientId: {OwnerClientId}");
            }

            // Apply distinct player identification color based on ClientId
            ApplyPlayerColor();
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            if (IsOwner)
            {
                _inputReader.DisableInput();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void Update()
        {
            // Only the owner processes local hardware inputs and drives local physics
            if (!IsOwner) return;

            // 1. Pass mouse/stick look delta to camera controller
            if (_cameraController != null)
            {
                _cameraController.UpdateLookInput(_inputReader.LookInput);
            }

            // 2. Drive movement using camera heading and input reader values
            Vector3 camForward = (_cameraController != null) ? _cameraController.HorizontalForward : transform.forward;
            Vector3 camRight = (_cameraController != null) ? _cameraController.HorizontalRight : transform.right;

            _movement.ProcessMovement(
                _inputReader.MoveInput,
                camForward,
                camRight,
                _inputReader.SprintHeld,
                _inputReader.JumpTriggered
            );
        }

        /// <summary>
        /// Sets a distinctive tint for this player's visual mesh based on OwnerClientId.
        /// </summary>
        private void ApplyPlayerColor()
        {
            if (_playerRenderer == null)
            {
                _playerRenderer = GetComponentInChildren<Renderer>();
            }

            if (_playerRenderer != null)
            {
                int colorIndex = (int)(OwnerClientId % (ulong)PlayerColors.Length);
                Color chosenColor = PlayerColors[colorIndex];

                // Create a material property block to tint without cloning materials
                MaterialPropertyBlock propBlock = new MaterialPropertyBlock();
                _playerRenderer.GetPropertyBlock(propBlock);
                propBlock.SetColor("_BaseColor", chosenColor); // URP default color property
                propBlock.SetColor("_Color", chosenColor);     // Standard fallback
                _playerRenderer.SetPropertyBlock(propBlock);
            }
        }

        /// <summary>
        /// Calculates an offset spawn position so players spawn on top of ground (y = 1.05m)
        /// and spaced out according to ClientId to prevent overlapping.
        /// </summary>
        private static Vector3 CalculateSpawnPosition(ulong clientId)
        {
            float y = 1.05f;
            switch (clientId % 4)
            {
                case 0: return new Vector3(0f, y, 0f);
                case 1: return new Vector3(2f, y, 0f);
                case 2: return new Vector3(-2f, y, 0f);
                case 3: return new Vector3(0f, y, 2f);
                default: return new Vector3(0f, y, 0f);
            }
        }
    }
}
