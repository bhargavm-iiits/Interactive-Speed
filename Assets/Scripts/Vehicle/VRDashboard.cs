using UnityEngine;
using UnityEngine.UI;

namespace Vehicle
{
    /// <summary>
    /// Drives the dashboard World-Space Canvas:
    ///  – Speedometer needle rotation
    ///  – RPM needle rotation
    ///  – Gear text (1–6 within Drive)
    ///  – Speed digital readout
    ///
    /// All values read from VRCarController / VRAutomaticTransmission every frame.
    /// </summary>
    public class VRDashboard : MonoBehaviour
    {
        [Header("References")]
        public VRCarController car;
        public VRAutomaticTransmission transmission;

        [Header("Speedometer")]
        [Tooltip("The needle Transform that rotates around its local Z axis.")]
        public Transform speedometerNeedle;
        [Tooltip("Angle (degrees) when speed = 0 km/h.")]
        public float speedNeedleMinAngle = -135f;
        [Tooltip("Angle (degrees) when speed = maxSpeedKmh.")]
        public float speedNeedleMaxAngle = 135f;
        [Tooltip("Max speed displayed on gauge (km/h).")]
        public float speedGaugeMax = 200f;

        [Header("RPM Meter")]
        public Transform rpmNeedle;
        public float rpmNeedleMinAngle = -135f;
        public float rpmNeedleMaxAngle = 135f;
        [Tooltip("Max RPM displayed on gauge.")]
        public float rpmGaugeMax = 7000f;

        [Header("Text Elements (optional — assign Text components)")]
        public Text gearText;        // shows current gear number
        public Text speedText;       // digital km/h readout
        public Text modeText;        // always "D" (Drive)

        [Header("Gear Text Colors")]
        public Color gearHighlightColor = new Color(0.2f, 1f, 0.4f);
        public Color gearNormalColor    = new Color(0.7f, 0.7f, 0.7f);

        // ── Smoothing ──────────────────────────────────────────────────────────
        private float _displaySpeed;
        private float _displayRPM;

        private void Awake()
        {
            if (car == null)          car          = GetComponentInParent<VRCarController>();
            if (transmission == null) transmission = GetComponentInParent<VRAutomaticTransmission>();
        }

        private void Update()
        {
            if (car == null) return;

            float speed = car.SpeedKmh;
            float rpm   = car.CurrentRPM;

            // Smooth display values
            _displaySpeed = Mathf.Lerp(_displaySpeed, speed, Time.deltaTime * 8f);
            _displayRPM   = Mathf.Lerp(_displayRPM,   rpm,   Time.deltaTime * 6f);

            UpdateSpeedometer(_displaySpeed);
            UpdateRPMMeter(_displayRPM);
            UpdateTexts();
        }

        private void UpdateSpeedometer(float speed)
        {
            if (speedometerNeedle == null) return;
            float t = Mathf.Clamp01(speed / speedGaugeMax);
            float angle = Mathf.Lerp(speedNeedleMinAngle, speedNeedleMaxAngle, t);
            speedometerNeedle.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void UpdateRPMMeter(float rpm)
        {
            if (rpmNeedle == null) return;
            float t = Mathf.Clamp01(rpm / rpmGaugeMax);
            float angle = Mathf.Lerp(rpmNeedleMinAngle, rpmNeedleMaxAngle, t);
            rpmNeedle.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void UpdateTexts()
        {
            // Digital speed readout
            if (speedText != null)
                speedText.text = $"{_displaySpeed:F0}";

            // Gear display — always in Drive; show gear 1–6
            if (gearText != null && transmission != null)
            {
                gearText.text  = car.CurrentGear.ToString();
                gearText.color = gearHighlightColor;
            }

            // Mode indicator always shows D
            if (modeText != null)
            {
                modeText.text  = "D";
                modeText.color = gearHighlightColor;
            }
        }
    }
}
