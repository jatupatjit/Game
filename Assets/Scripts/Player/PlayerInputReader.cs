using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame.Player
{
    /// <summary>
    /// PlayerInputReader is responsible SOLELY for reading, caching, and exposing user input
    /// using Unity's New Input System.
    /// 
    /// Features:
    /// - Two-handed grab controls like Human Fall Flat:
    ///   * Left Hand: Left Mouse Button / Gamepad Left Trigger
    ///   * Right Hand: Right Mouse Button / Gamepad Right Trigger
    ///   * Dual Grab: 'E' key / Gamepad West Button
    /// - Continuous polling of button hold states for interactive physical lifting.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInputReader : MonoBehaviour
    {
        [Header("Input Action Asset Reference")]
        [Tooltip("Assign InputSystem_Actions asset. Fallback actions are generated automatically.")]
        [SerializeField] private InputActionAsset _inputAsset;

        // Cached input action map and individual action references
        private InputActionMap _playerActionMap;
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _jumpAction;
        private InputAction _sprintAction;
        private InputAction _interactAction;
        private InputAction _grabLeftAction;
        private InputAction _grabRightAction;

        // Exposed properties for movement & camera controllers
        public Vector2 MoveInput { get; private set; }
        public Vector2 LookInput { get; private set; }
        public bool SprintHeld { get; private set; }

        // Two-handed grab states (Hold-to-grab like Human Fall Flat)
        public bool GrabLeftHeld { get; private set; }
        public bool GrabRightHeld { get; private set; }
        public bool InteractHeld { get; private set; }

        /// <summary>
        /// True if ANY grab input is currently pressed (Left Hand, Right Hand, or 'E').
        /// </summary>
        public bool IsGrabbing => GrabLeftHeld || GrabRightHeld || InteractHeld;

        // Single-frame triggers
        public bool JumpTriggered { get; private set; }
        public bool InteractTriggered { get; private set; }

        // Events
        public event Action OnJumpPerformed;
        public event Action OnInteractPerformed;
        public event Action OnGrabStarted;
        public event Action OnGrabEnded;

        private bool _wasGrabbing = false;

        private void Awake()
        {
            InitializeInputActions();
        }

        private void OnEnable()
        {
            EnableInput();
        }

        private void OnDisable()
        {
            DisableInput();
        }

        private void Update()
        {
            if (_moveAction != null)
            {
                MoveInput = _moveAction.ReadValue<Vector2>();
            }

            if (_lookAction != null)
            {
                LookInput = _lookAction.ReadValue<Vector2>();
            }

            if (_sprintAction != null)
            {
                SprintHeld = _sprintAction.IsPressed();
            }

            if (_grabLeftAction != null)
            {
                GrabLeftHeld = _grabLeftAction.IsPressed();
            }

            if (_grabRightAction != null)
            {
                GrabRightHeld = _grabRightAction.IsPressed();
            }

            if (_interactAction != null)
            {
                InteractHeld = _interactAction.IsPressed();
            }

            // Detect transition between Grabbing and Released
            bool currentlyGrabbing = IsGrabbing;
            if (currentlyGrabbing && !_wasGrabbing)
            {
                OnGrabStarted?.Invoke();
            }
            else if (!currentlyGrabbing && _wasGrabbing)
            {
                OnGrabEnded?.Invoke();
            }
            _wasGrabbing = currentlyGrabbing;
        }

        private void LateUpdate()
        {
            JumpTriggered = false;
            InteractTriggered = false;
        }

        private void InitializeInputActions()
        {
            if (_inputAsset != null)
            {
                _playerActionMap = _inputAsset.FindActionMap("Player");

                if (_playerActionMap != null)
                {
                    _moveAction = _playerActionMap.FindAction("Move");
                    _lookAction = _playerActionMap.FindAction("Look");
                    _jumpAction = _playerActionMap.FindAction("Jump");
                    _sprintAction = _playerActionMap.FindAction("Sprint");
                    _interactAction = _playerActionMap.FindAction("Interact");
                    _grabLeftAction = _playerActionMap.FindAction("Attack"); // Often bound to Left Mouse
                }
            }

            // Fallback actions
            if (_moveAction == null)
            {
                _moveAction = new InputAction("FallbackMove", InputActionType.Value);
                _moveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w")
                    .With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a")
                    .With("Right", "<Keyboard>/d")
                    .With("Up", "<Gamepad>/leftStick/up")
                    .With("Down", "<Gamepad>/leftStick/down")
                    .With("Left", "<Gamepad>/leftStick/left")
                    .With("Right", "<Gamepad>/leftStick/right");
            }

            if (_lookAction == null)
            {
                _lookAction = new InputAction("FallbackLook", InputActionType.Value);
                _lookAction.AddBinding("<Mouse>/delta");
                _lookAction.AddBinding("<Gamepad>/rightStick");
            }

            if (_jumpAction == null)
            {
                _jumpAction = new InputAction("FallbackJump", InputActionType.Button);
                _jumpAction.AddBinding("<Keyboard>/space");
                _jumpAction.AddBinding("<Gamepad>/buttonSouth");
            }

            if (_sprintAction == null)
            {
                _sprintAction = new InputAction("FallbackSprint", InputActionType.Button);
                _sprintAction.AddBinding("<Keyboard>/leftShift");
                _sprintAction.AddBinding("<Gamepad>/leftStickPress");
            }

            if (_interactAction == null)
            {
                _interactAction = new InputAction("FallbackInteract", InputActionType.Button);
                _interactAction.AddBinding("<Keyboard>/e");
                _interactAction.AddBinding("<Gamepad>/buttonWest");
            }

            // Left Hand Grab: Left Mouse Button or Gamepad Left Trigger
            if (_grabLeftAction == null)
            {
                _grabLeftAction = new InputAction("FallbackGrabLeft", InputActionType.Button);
                _grabLeftAction.AddBinding("<Mouse>/leftButton");
                _grabLeftAction.AddBinding("<Gamepad>/leftTrigger");
            }

            // Right Hand Grab: Right Mouse Button or Gamepad Right Trigger
            if (_grabRightAction == null)
            {
                _grabRightAction = new InputAction("FallbackGrabRight", InputActionType.Button);
                _grabRightAction.AddBinding("<Mouse>/rightButton");
                _grabRightAction.AddBinding("<Gamepad>/rightTrigger");
            }

            _jumpAction.performed += OnJumpTriggered;
            _interactAction.performed += OnInteractTriggered;
        }

        private void OnJumpTriggered(InputAction.CallbackContext context)
        {
            JumpTriggered = true;
            OnJumpPerformed?.Invoke();
        }

        private void OnInteractTriggered(InputAction.CallbackContext context)
        {
            InteractTriggered = true;
            OnInteractPerformed?.Invoke();
        }

        public void EnableInput()
        {
            _playerActionMap?.Enable();
            _moveAction?.Enable();
            _lookAction?.Enable();
            _jumpAction?.Enable();
            _sprintAction?.Enable();
            _interactAction?.Enable();
            _grabLeftAction?.Enable();
            _grabRightAction?.Enable();
        }

        public void DisableInput()
        {
            _playerActionMap?.Disable();
            _moveAction?.Disable();
            _lookAction?.Disable();
            _jumpAction?.Disable();
            _sprintAction?.Disable();
            _interactAction?.Disable();
            _grabLeftAction?.Disable();
            _grabRightAction?.Disable();

            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;
            SprintHeld = false;
            GrabLeftHeld = false;
            GrabRightHeld = false;
            InteractHeld = false;
            JumpTriggered = false;
            InteractTriggered = false;
        }

        private void OnDestroy()
        {
            if (_jumpAction != null) _jumpAction.performed -= OnJumpTriggered;
            if (_interactAction != null) _interactAction.performed -= OnInteractTriggered;
        }
    }
}
