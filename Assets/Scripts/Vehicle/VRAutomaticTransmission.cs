using UnityEngine;

namespace Vehicle
{
    /// <summary>
    /// Simulates a 6-speed automatic transmission with only Drive mode.
    /// Writes CurrentRPM, CurrentGear, and TorqueMultiplier back to VRCarController.
    /// No clutch, no reverse, no manual shifting.
    /// </summary>
    public class VRAutomaticTransmission : MonoBehaviour
    {
        [Header("References")]
        public VRCarController car;

        [Header("Gear Ratios (6-speed)")]
        [Tooltip("Torque multiplier per gear. Higher = more torque but lower top speed per gear.")]
        public float[] gearRatios = { 3.6f, 2.1f, 1.45f, 1.08f, 0.85f, 0.72f };

        [Header("RPM Limits")]
        public float idleRPM     = 800f;
        public float shiftUpRPM  = 5500f;
        public float shiftDownRPM = 2000f;
        public float maxRPM      = 7000f;
        [Tooltip("RPM smoothing speed.")]
        public float rpmSmoothing = 8f;

        [Header("Shift Timing")]
        [Tooltip("Minimum seconds between gear changes.")]
        public float shiftCooldown = 0.8f;

        // ── Read-only ──────────────────────────────────────────────────────────
        public int   CurrentGear { get; private set; } = 1;
        public float CurrentRPM  { get; private set; }
        public float ShiftProgress { get; private set; }  // 0..1 for dashboard anim

        private float _shiftTimer = 0f;
        private float _targetRPM;

        private void Awake()
        {
            if (car == null) car = GetComponent<VRCarController>();
        }

        private void Update()
        {
            if (car == null) return;

            _shiftTimer -= Time.deltaTime;

            float speed    = car.SpeedKmh;
            float throttle = car.ThrottleInput;
            int   maxGear  = gearRatios.Length;

            // ── Compute target RPM from speed + gear ──────────────────────────
            // RPM ∝ speed × gear_ratio (simplified, ignores wheel radius)
            float speedFactor = speed / Mathf.Max(maxSpeedForGear(CurrentGear), 1f);
            _targetRPM = Mathf.Lerp(idleRPM, shiftUpRPM, speedFactor);

            // Throttle pushes RPM up toward rev limit
            _targetRPM = Mathf.Lerp(_targetRPM, maxRPM * 0.92f, throttle * 0.35f);
            _targetRPM = Mathf.Clamp(_targetRPM, idleRPM, maxRPM);

            CurrentRPM = Mathf.Lerp(CurrentRPM, _targetRPM, Time.deltaTime * rpmSmoothing);

            // ── Auto shift ────────────────────────────────────────────────────
            if (_shiftTimer <= 0f)
            {
                if (CurrentRPM >= shiftUpRPM && CurrentGear < maxGear && speed > 5f)
                {
                    CurrentGear++;
                    _shiftTimer = shiftCooldown;
                }
                else if (CurrentRPM <= shiftDownRPM && CurrentGear > 1)
                {
                    CurrentGear--;
                    _shiftTimer = shiftCooldown;
                }
            }

            ShiftProgress = Mathf.InverseLerp(shiftDownRPM, shiftUpRPM, CurrentRPM);

            // ── Write back to car ─────────────────────────────────────────────
            car.CurrentRPM      = CurrentRPM;
            car.CurrentGear     = CurrentGear;
            car.TorqueMultiplier = gearRatios[CurrentGear - 1];
        }

        private float maxSpeedForGear(int gear)
        {
            // Rough top speed estimate per gear in km/h
            // (full speed at top gear = car.maxSpeedKmh)
            float[] maxSpeeds = { 30f, 55f, 85f, 115f, 140f, 165f };
            int idx = Mathf.Clamp(gear - 1, 0, maxSpeeds.Length - 1);
            return maxSpeeds[idx];
        }
    }
}
