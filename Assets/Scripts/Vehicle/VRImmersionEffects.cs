using UnityEngine;

namespace Vehicle
{
    /// <summary>
    /// Additional immersion effects not in VRCockpitRig (which handles camera movement):
    ///  • Dashboard ambient point light color flicker
    ///  • Interior light subtle pulse with RPM
    ///  • Speed-glow on dashboard gauges (emission boost)
    /// </summary>
    public class VRImmersionEffects : MonoBehaviour
    {
        [Header("References")]
        public VRCarController car;

        [Header("Interior Lighting")]
        [Tooltip("The interior ambient point light.")]
        public Light interiorLight;
        [Tooltip("Base interior light intensity.")]
        public float baseLightIntensity = 0.4f;
        [Tooltip("How much RPM pulses the light (0 = none).")]
        public float rpmLightPulse = 0.06f;

        [Header("Dashboard Emission (optional)")]
        [Tooltip("Dashboard renderer to boost emission at high RPM.")]
        public Renderer dashboardRenderer;
        public int dashEmissionMaterialIndex = 0;
        public Color dashBaseEmission  = new Color(0.05f, 0.35f, 0.7f) * 0.5f;
        public Color dashPeakEmission  = new Color(0.05f, 0.35f, 0.7f) * 1.5f;

        [Header("Headlights")]
        public Light[] headlights;
        [Tooltip("Headlights are always on while car is on.")]
        public bool headlightsAlwaysOn = true;

        [Header("Brake Lights")]
        public Light[] brakeLights;
        public float brakeLightIntensity = 2.0f;

        private float _noiseOffset;

        private void Awake()
        {
            if (car == null) car = GetComponentInParent<VRCarController>();
            _noiseOffset = Random.Range(0f, 100f);

            if (headlightsAlwaysOn && headlights != null)
                foreach (var l in headlights)
                    if (l != null) l.enabled = true;
        }

        private void Update()
        {
            if (car == null) return;

            float rpmT   = Mathf.Clamp01(car.CurrentRPM / 7000f);
            float brakeT = car.BrakeInput;

            // ── Interior Light ────────────────────────────────────────────────
            if (interiorLight != null)
            {
                float flicker = Mathf.PerlinNoise(Time.time * 12f, _noiseOffset);
                interiorLight.intensity = baseLightIntensity
                    + rpmT * rpmLightPulse
                    + (flicker - 0.5f) * 0.015f;        // tiny ambient flicker
            }

            // ── Dashboard Glow ────────────────────────────────────────────────
            if (dashboardRenderer != null)
            {
                var mats = dashboardRenderer.materials;
                if (dashEmissionMaterialIndex < mats.Length)
                {
                    Color emission = Color.Lerp(dashBaseEmission, dashPeakEmission, rpmT);
                    mats[dashEmissionMaterialIndex].SetColor("_EmissionColor", emission);
                }
                dashboardRenderer.materials = mats;
            }

            // ── Brake Lights ──────────────────────────────────────────────────
            if (brakeLights != null)
            {
                foreach (var bl in brakeLights)
                {
                    if (bl == null) continue;
                    bl.enabled   = brakeT > 0.05f;
                    bl.intensity = brakeT * brakeLightIntensity;
                }
            }
        }
    }
}
