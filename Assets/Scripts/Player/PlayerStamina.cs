using System;
using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// PlayerStamina manages physical exertion, stamina drain, and regeneration.
    /// 
    /// Adheres to SOLID Single Responsibility Principle (SRP):
    /// - Responsible SOLELY for tracking stamina points, drain rates, and exhaustion states.
    /// - Decoupled from input handling and carrying mechanics (notified via public API).
    /// - Exposes events and normalized properties for HUD/UI consumption.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerStamina : MonoBehaviour
    {
        [Header("Stamina Capacity")]
        [Tooltip("Maximum stamina pool")]
        [SerializeField] private float _maxStamina = 100.0f;

        [Header("Exertion & Drain Rates (Points/Sec)")]
        [Tooltip("Stamina drain per second when carrying with both hands (Solo carry: 2.0f/s)")]
        [SerializeField] private float _twoHandDrainRate = 2.0f;

        [Tooltip("Stamina drain per second when carrying with one hand (3.5f/s)")]
        [SerializeField] private float _oneHandDrainRate = 3.5f;

        [Tooltip("Baseline object mass in kg where no extra weight penalty applies")]
        [SerializeField] private float _baseMass = 10.0f;

        [Tooltip("Extra drain penalty multiplier per kg above base mass")]
        [SerializeField] private float _massDrainMultiplier = 0.05f;

        [Header("Regeneration")]
        [Tooltip("Stamina recovery rate per second when resting")]
        [SerializeField] private float _recoveryRate = 32.0f;

        [Tooltip("Delay in seconds after exertion before stamina starts regenerating")]
        [SerializeField] private float _recoveryDelay = 0.6f;

        [Header("Exhaustion Mechanics")]
        [Tooltip("Minimum stamina percentage (0.0 to 1.0) required to recover from exhaustion")]
        [Range(0.1f, 0.5f)]
        [SerializeField] private float _exhaustionRecoveryPercent = 0.20f;

        [Header("Co-op Synergy (ยกช่วยกันกับเพื่อน)")]
        [Tooltip("Additional stamina reduction synergy multiplier when carrying with co-op friends")]
        [Range(0.5f, 1.0f)]
        [SerializeField] private float _coopSynergyMultiplier = 0.85f;

        // Current state
        private float _currentStamina;
        private bool _isExhausted = false;
        private float _lastExertionTime = -10f;
        private bool _wasUpdatedThisFrame = false;

        // Events
        public event Action<float, float> OnStaminaChanged;
        public event Action OnExhausted;
        public event Action OnRecoveredFromExhaustion;

        // Public properties
        public float MaxStamina => _maxStamina;
        public float CarryDrainMultiplier { get; set; } = 1f;
        public float CurrentStamina => _currentStamina;
        public float NormalizedStamina => Mathf.Clamp01(_currentStamina / _maxStamina);
        public bool IsExhausted => _isExhausted;
        public float CoopSynergyMultiplier => _coopSynergyMultiplier;

        private void Awake()
        {
            _currentStamina = _maxStamina;
        }

        private void Update()
        {
            // Explicit carry drain/recovery already accounts for this frame.
            if (!_wasUpdatedThisFrame)
            {
                if (Time.time >= _lastExertionTime + _recoveryDelay && _currentStamina < _maxStamina)
                {
                    float prev = _currentStamina;
                    _currentStamina = Mathf.Min(_maxStamina, _currentStamina + _recoveryRate * Time.deltaTime);

                    if (!Mathf.Approximately(prev, _currentStamina))
                    {
                        OnStaminaChanged?.Invoke(_currentStamina, _maxStamina);
                    }

                    // Check if recovered from exhaustion
                    if (_isExhausted && _currentStamina >= _maxStamina * _exhaustionRecoveryPercent)
                    {
                        _isExhausted = false;
                        OnRecoveredFromExhaustion?.Invoke();
                    }
                }
            }

            _wasUpdatedThisFrame = false;
        }

        /// <summary>
        /// Regenerates stamina points actively (e.g. during co-op carry or resting).
        /// </summary>
        public void RegenerateStamina(float deltaTime)
        {
            _wasUpdatedThisFrame = true;
            if (_currentStamina < _maxStamina)
            {
                float prev = _currentStamina;
                _currentStamina = Mathf.Min(_maxStamina, _currentStamina + _recoveryRate * deltaTime);

                if (!Mathf.Approximately(prev, _currentStamina))
                {
                    OnStaminaChanged?.Invoke(_currentStamina, _maxStamina);
                }

                if (_isExhausted && _currentStamina >= _maxStamina * _exhaustionRecoveryPercent)
                {
                    _isExhausted = false;
                    OnRecoveredFromExhaustion?.Invoke();
                }
            }
        }

        /// <summary>
        /// Applies continuous stamina drain while lifting or holding objects.
        /// - Solo carry (1 player): 2.0f/s (two-handed) or 3.5f/s (one-handed).
        /// - Co-op carry (2+ players): 0 stamina drain and actively regenerates stamina!
        /// </summary>
        /// <param name="isOneHanded">True if only one hand is holding the object</param>
        /// <param name="objectMass">Mass of the object in kg</param>
        /// <param name="carrierCount">Number of players currently carrying this object</param>
        public void DrainStaminaContinuous(bool isOneHanded, float objectMass, int carrierCount = 1)
        {
            _wasUpdatedThisFrame = true;
            // Co-op carry (2+ players): zero drain & active stamina regeneration
            if (carrierCount >= 2)
            {
                RegenerateStamina(Time.deltaTime);
                return;
            }

            _lastExertionTime = Time.time;

            float baseRate = isOneHanded ? _oneHandDrainRate : _twoHandDrainRate;

            float massExcess = Mathf.Max(0f, objectMass - _baseMass);
            float massScale = 1.0f + (massExcess * _massDrainMultiplier);

            float totalDrain = baseRate * massScale * Mathf.Clamp(CarryDrainMultiplier, .1f, 1f) * Time.deltaTime;

            ApplyDrain(totalDrain);
        }

        /// <summary>
        /// Consumes an immediate chunk of stamina (e.g. for throwing an object).
        /// </summary>
        public bool ConsumeStamina(float amount)
        {
            _lastExertionTime = Time.time;
            ApplyDrain(amount);
            return !_isExhausted;
        }

        private void ApplyDrain(float amount)
        {
            if (_currentStamina <= 0f) return;

            float prev = _currentStamina;
            _currentStamina = Mathf.Max(0f, _currentStamina - amount);

            if (!Mathf.Approximately(prev, _currentStamina))
            {
                OnStaminaChanged?.Invoke(_currentStamina, _maxStamina);
            }

            if (_currentStamina <= 0f && !_isExhausted)
            {
                _isExhausted = true;
                OnExhausted?.Invoke();
            }
        }

        /// <summary>
        /// Checks if the player has enough stamina to perform a specific action without triggering exhaustion.
        /// </summary>
        public bool CanPerformAction(float requiredStamina)
        {
            return !_isExhausted && _currentStamina >= requiredStamina;
        }

        /// <summary>
        /// Resets stamina to full capacity (e.g. on respawn).
        /// </summary>
        public void ResetStamina()
        {
            _currentStamina = _maxStamina;
            _isExhausted = false;
            OnStaminaChanged?.Invoke(_currentStamina, _maxStamina);
            OnRecoveredFromExhaustion?.Invoke();
        }
    }
}
