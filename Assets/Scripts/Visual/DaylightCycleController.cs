using UnityEngine;
using UnityEngine.Rendering;

namespace Visual
{
    /// <summary>
    /// Controls the day-night cycle by rotating the directional light and
    /// adjusting ambient light, fog color, and sun intensity throughout the day.
    /// Starts at a golden-hour time for cinematic effect.
    /// </summary>
    public class DaylightCycleController : MonoBehaviour
    {
        [Header("Sun")]
        public Light sunLight;
        [Tooltip("Duration of a full 24-hour day in real seconds. 0 = frozen.")]
        public float dayDurationSeconds = 0f; // 0 = frozen at start time
        [Tooltip("Starting time of day (0–24). 7.5 = sunrise, 15 = afternoon.")]
        [Range(0f, 24f)]
        public float startTimeOfDay = 14.5f;

        [Header("Sun Color Gradient")]
        [Tooltip("Sun color over the full day (X=0 midnight, X=0.5 noon, X=1 midnight again).")]
        public Gradient sunColorGradient;

        [Header("Sun Intensity")]
        [Tooltip("Sun intensity at noon.")]
        public float noonIntensity = 1.5f;
        [Tooltip("Sun intensity at dawn/dusk.")]
        public float horizonIntensity = 0.6f;

        [Header("Ambient")]
        [Tooltip("Sky ambient color gradient over the day.")]
        public Gradient skyAmbientGradient;
        [Tooltip("Ground ambient color gradient over the day.")]
        public Gradient groundAmbientGradient;

        [Header("Fog")]
        [Tooltip("Fog color gradient over the day.")]
        public Gradient fogColorGradient;
        [Tooltip("Fog density at noon.")]
        public float noonFogDensity = 0.002f;
        [Tooltip("Fog density at dusk/dawn.")]
        public float horizonFogDensity = 0.005f;

        // ── Private ───────────────────────────────────────────────────────────
        private float _timeOfDay;

        private void Awake()
        {
            _timeOfDay = startTimeOfDay;
            InitDefaultGradients();
        }

        private void Update()
        {
            if (dayDurationSeconds > 0f)
                _timeOfDay += (Time.deltaTime / dayDurationSeconds) * 24f;

            _timeOfDay %= 24f;
            ApplyTimeOfDay(_timeOfDay);
        }

        private void ApplyTimeOfDay(float hour)
        {
            float t = hour / 24f; // 0–1 normalised

            // ── Sun rotation ──────────────────────────────────────────────
            // Sun rises at t=0.25 (6am), sets at t=0.75 (18pm)
            if (sunLight != null)
            {
                float sunAngle = (t - 0.25f) * 360f; // -90 at midnight, +270 at next midnight
                sunLight.transform.rotation = Quaternion.Euler(sunAngle, -30f, 0f);

                // Intensity peaks at noon (t=0.5)
                float noonT = Mathf.Sin(Mathf.Clamp01((t - 0.2f) / 0.6f) * Mathf.PI);
                sunLight.intensity = Mathf.Lerp(horizonIntensity, noonIntensity, noonT);
                sunLight.color = sunColorGradient != null
                    ? sunColorGradient.Evaluate(t)
                    : new Color(1f, 0.95f * noonT + 0.6f * (1f - noonT), 0.7f * noonT);

                sunLight.enabled = (hour > 5f && hour < 22f);
            }

            // ── Ambient light ─────────────────────────────────────────────
            if (skyAmbientGradient != null)
                RenderSettings.ambientSkyColor = skyAmbientGradient.Evaluate(t);
            if (groundAmbientGradient != null)
                RenderSettings.ambientGroundColor = groundAmbientGradient.Evaluate(t);

            // ── Fog ───────────────────────────────────────────────────────
            float noonFactor = Mathf.Sin(Mathf.Clamp01((t - 0.2f) / 0.6f) * Mathf.PI);
            RenderSettings.fogDensity = Mathf.Lerp(horizonFogDensity, noonFogDensity, noonFactor);
            if (fogColorGradient != null)
                RenderSettings.fogColor = fogColorGradient.Evaluate(t);
        }

        // ── Default gradient setup ────────────────────────────────────────────

        private void InitDefaultGradients()
        {
            if (sunColorGradient == null || sunColorGradient.colorKeys.Length == 0)
            {
                sunColorGradient = new Gradient();
                sunColorGradient.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(new Color(0.05f, 0.05f, 0.25f), 0.0f),   // midnight
                        new GradientColorKey(new Color(1.0f, 0.45f, 0.15f), 0.25f),   // sunrise
                        new GradientColorKey(new Color(1.0f, 0.98f, 0.88f), 0.5f),    // noon
                        new GradientColorKey(new Color(1.0f, 0.55f, 0.2f),  0.75f),   // sunset
                        new GradientColorKey(new Color(0.05f, 0.05f, 0.25f), 1.0f),   // midnight
                    },
                    new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
                );
            }

            if (fogColorGradient == null || fogColorGradient.colorKeys.Length == 0)
            {
                fogColorGradient = new Gradient();
                fogColorGradient.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(new Color(0.02f, 0.02f, 0.08f), 0.0f),   // night
                        new GradientColorKey(new Color(0.7f, 0.55f, 0.4f),   0.25f),  // dawn haze
                        new GradientColorKey(new Color(0.65f, 0.72f, 0.78f), 0.5f),   // noon haze
                        new GradientColorKey(new Color(0.7f, 0.5f, 0.35f),   0.75f),  // dusk
                        new GradientColorKey(new Color(0.02f, 0.02f, 0.08f), 1.0f),
                    },
                    new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
                );
            }

            if (skyAmbientGradient == null || skyAmbientGradient.colorKeys.Length == 0)
            {
                skyAmbientGradient = new Gradient();
                skyAmbientGradient.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(new Color(0.02f, 0.02f, 0.08f), 0f),
                        new GradientColorKey(new Color(0.55f, 0.62f, 0.7f), 0.5f),
                        new GradientColorKey(new Color(0.02f, 0.02f, 0.08f), 1f),
                    },
                    new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
                );
            }

            if (groundAmbientGradient == null || groundAmbientGradient.colorKeys.Length == 0)
            {
                groundAmbientGradient = new Gradient();
                groundAmbientGradient.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(new Color(0.01f, 0.01f, 0.02f), 0f),
                        new GradientColorKey(new Color(0.12f, 0.15f, 0.08f), 0.5f),
                        new GradientColorKey(new Color(0.01f, 0.01f, 0.02f), 1f),
                    },
                    new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
                );
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Sets the current time of day (0–24).</summary>
        public void SetTimeOfDay(float hour) => _timeOfDay = hour % 24f;
        public float TimeOfDay => _timeOfDay;
    }
}
