using UnityEngine;

namespace Vehicle
{
    /// <summary>
    /// Cinematic HUD overlay showing speed and basic telemetry.
    /// Rendered using OnGUI — no Canvas dependency required.
    /// </summary>
    public class SpeedHUD : MonoBehaviour
    {
        [Header("Target")]
        public CarController car;

        [Header("Style")]
        public Color textColor = new Color(0.95f, 0.95f, 0.92f, 0.92f);
        public Color shadowColor = new Color(0f, 0f, 0f, 0.5f);

        private GUIStyle _speedStyle;
        private GUIStyle _labelStyle;
        private bool _stylesCreated;
        private float _displaySpeed;

        private void Update()
        {
            if (car == null) car = FindFirstObjectByType<CarController>();
            _displaySpeed = Mathf.Lerp(_displaySpeed,
                car != null ? car.SpeedKmh : 0f,
                Time.deltaTime * 8f);
        }

        private void OnGUI()
        {
            if (!_stylesCreated) CreateStyles();

            float sw = Screen.width, sh = Screen.height;

            // Speed — bottom right
            string speedStr = $"{_displaySpeed:F0}";
            string unitStr = "km/h";

            // Shadow
            GUI.color = shadowColor;
            GUI.Label(new Rect(sw - 178, sh - 98, 200, 80), speedStr, _speedStyle);
            GUI.Label(new Rect(sw - 172, sh - 44, 200, 30), unitStr, _labelStyle);

            // Main text
            GUI.color = textColor;
            GUI.Label(new Rect(sw - 180, sh - 100, 200, 80), speedStr, _speedStyle);
            GUI.Label(new Rect(sw - 174, sh - 46, 200, 30), unitStr, _labelStyle);

            // Controls hint (fades after 8 seconds)
            if (Time.timeSinceLevelLoad < 8f)
            {
                float alpha = Mathf.Clamp01(1f - (Time.timeSinceLevelLoad - 5f) / 3f);
                GUI.color = new Color(1f, 1f, 1f, alpha * 0.75f);
                GUI.Label(new Rect(sw * 0.5f - 150f, sh - 60f, 300f, 50f),
                    "WASD / Arrows → Drive  |  SPACE → Handbrake",
                    _labelStyle);
            }

            GUI.color = Color.white;
        }

        private void CreateStyles()
        {
            _speedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 56,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleRight,
            };

            _stylesCreated = true;
        }
    }
}
