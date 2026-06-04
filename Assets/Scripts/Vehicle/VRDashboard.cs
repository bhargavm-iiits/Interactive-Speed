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

        private InfiniteWorld.StraightLineDriver _driver;
        private bool _hasDriver = false;

        private void Awake()
        {
            if (car == null)          car          = GetComponentInParent<VRCarController>();
            if (transmission == null) transmission = GetComponentInParent<VRAutomaticTransmission>();
            
            _driver = GetComponentInParent<InfiniteWorld.StraightLineDriver>();
            if (_driver == null)
            {
                _driver = FindFirstObjectByType<InfiniteWorld.StraightLineDriver>();
            }
            _hasDriver = (_driver != null);

            speedGaugeMax = 30f; // Force max dial range to 30 m/s programmatically
        }

        private void Start()
        {
            speedGaugeMax = 30f; // Force max dial range to 30 m/s programmatically
        }

        private void Update()
        {
            float speedMs = 0f;
            float rpm = 800f;
            int gear = 1;

            if (_hasDriver)
            {
                float speedKmh = _driver.SpeedKmh;
                speedMs = speedKmh / 3.6f;

                // Synthesize gear
                if (_driver.IsReverse)
                {
                    gear = 1;
                }
                else if (speedKmh < 0.5f)
                {
                    gear = 1;
                }
                else
                {
                    gear = Mathf.Clamp(Mathf.FloorToInt(speedKmh / 22f) + 1, 1, 6);
                }

                // Synthesize RPM
                float minSpeedForGear = (gear - 1) * 22f;
                float maxSpeedForGear = gear * 22f;
                float speedInGear = speedKmh - minSpeedForGear;
                float gearSpeedRange = maxSpeedForGear - minSpeedForGear;
                
                float speedPercentInGear = Mathf.Clamp01(speedInGear / gearSpeedRange);
                float minRpm = (gear == 1) ? 800f : 2000f;
                rpm = Mathf.Lerp(minRpm, 5800f, speedPercentInGear);

                if (_driver.Throttle > 0.01f)
                {
                    rpm = Mathf.Lerp(rpm, 6500f, _driver.Throttle * 0.25f);
                }
            }
            else if (car != null)
            {
                speedMs = car.SpeedKmh / 3.6f;
                rpm = car.CurrentRPM;
                gear = car.CurrentGear;
            }
            else
            {
                return;
            }

            // Smooth display values
            _displaySpeed = Mathf.Lerp(_displaySpeed, speedMs, Time.deltaTime * 8f);
            _displayRPM   = Mathf.Lerp(_displayRPM,   rpm,     Time.deltaTime * 6f);

            UpdateSpeedometer(_displaySpeed);
            UpdateRPMMeter(_displayRPM);
            UpdateTexts(gear);
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

        private void UpdateTexts(int gear)
        {
            // Digital speed readout
            if (speedText != null)
                speedText.text = $"{_displaySpeed:F0}";

            // Gear display
            if (gearText != null)
            {
                if (_hasDriver)
                {
                    if (_driver.IsReverse)
                        gearText.text = "R";
                    else if (_driver.SpeedKmh < 0.5f)
                        gearText.text = "N";
                    else
                        gearText.text = gear.ToString();
                }
                else if (car != null)
                {
                    gearText.text = gear.ToString();
                }
                gearText.color = gearHighlightColor;
            }

            // Mode indicator
            if (modeText != null)
            {
                if (_hasDriver && _driver.IsReverse)
                    modeText.text = "R";
                else if (_hasDriver && _driver.SpeedKmh < 0.5f)
                    modeText.text = "N";
                else
                    modeText.text = "D";
                modeText.color = gearHighlightColor;
            }
        }
    }
}
