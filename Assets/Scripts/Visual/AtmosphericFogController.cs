using UnityEngine;
using Vehicle;

namespace Visual
{
    /// <summary>
    /// Dynamically adjusts URP exponential fog density based on camera speed,
    /// creating a speed-haze effect and enhancing depth at distance.
    /// Works with AutoDriveCamera (no car needed).
    /// </summary>
    public class AtmosphericFogController : MonoBehaviour
    {
        [Header("Target")]
        public Transform player;

        [Header("Fog Density")]
        public float baseDensity = 0.0015f;
        public float speedFogBonus = 0.002f;
        public float speedReference = 80f;
        public float densitySmoothing = 1.5f;

        private AutoDriveCamera _driveCamera;
        private float _currentDensity;
        private float _densityVelocity;
        private DaylightCycleController _daylight;

        private void Start()
        {
            if (player != null)
                _driveCamera = player.GetComponent<AutoDriveCamera>();
            if (_driveCamera == null)
                _driveCamera = FindFirstObjectByType<AutoDriveCamera>();

            _daylight = FindFirstObjectByType<DaylightCycleController>();
            _currentDensity = baseDensity;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = baseDensity;
        }

        private void Update()
        {
            float speedKmh = _driveCamera != null ? _driveCamera.SpeedKmh : 0f;
            float speedT = Mathf.Clamp01(speedKmh / speedReference);

            float dayDensity = _daylight != null ? RenderSettings.fogDensity : baseDensity;
            float targetDensity = dayDensity + speedFogBonus * speedT;

            _currentDensity = Mathf.SmoothDamp(_currentDensity, targetDensity,
                ref _densityVelocity, densitySmoothing);
            RenderSettings.fogDensity = _currentDensity;
        }
    }
}
