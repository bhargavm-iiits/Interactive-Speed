using UnityEngine;
using UnityEngine.InputSystem;

namespace InfiniteWorld
{
    /// <summary>
    /// Drives the camera along the StaticWorldBuilder's curved road path.
    /// Follows the S-curves naturally. No car needed.
    /// 
    /// Controls:
    ///   W / UpArrow    — speed up
    ///   S / DownArrow  — slow down
    ///   Space          — pause / resume
    ///   RMB + drag     — free look
    /// </summary>
    public class StraightLineDriver : MonoBehaviour
    {
        [HideInInspector] public StaticWorldBuilder worldBuilder;
        [HideInInspector] public Terrain terrain;
        [HideInInspector] public float speed       = 28f;
        [HideInInspector] public float endZ        = 4950f;
        [HideInInspector] public float cameraHeight = 2.8f;

        public float mouseSensitivity = 2.5f;

        private float _z;
        private bool  _paused;
        private float _yaw, _pitch;
        private bool  _rmbHeld;

        private GUIStyle _hud;
        private bool _hudReady;

        private void Start()
        {
            // Start at the beginning of the road
            _z     = 15f;
            _yaw   = transform.eulerAngles.y;
            _pitch = transform.eulerAngles.x;
            Cursor.lockState = CursorLockMode.Confined;
        }

        private void Update()
        {
            HandleKeys();
            if (!_paused) Drive();
            MouseLook();
        }

        private void Drive()
        {
            _z += speed * Time.deltaTime;
            _z  = Mathf.Clamp(_z, 0f, endZ);

            // Follow curved road path from builder
            if (worldBuilder != null)
            {
                Vector3 roadPos = worldBuilder.GetRoadPosition(_z);
                Vector3 roadTan = worldBuilder.GetRoadTangent(_z);

                // Smooth position follow
                transform.position = Vector3.Lerp(transform.position, roadPos, Time.deltaTime * 10f);

                // Auto look-ahead along curve (unless player is free-looking)
                if (!_rmbHeld)
                {
                    Quaternion target = Quaternion.LookRotation(roadTan, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * 4f);
                    _yaw   = transform.eulerAngles.y;
                    _pitch = transform.eulerAngles.x;
                }
            }
            else
            {
                // Fallback: straight drive with terrain hug
                Vector3 p = transform.position;
                p.z += speed * Time.deltaTime;
                if (terrain != null)
                {
                    float h = terrain.SampleHeight(new Vector3(p.x, 0, p.z));
                    p.y = Mathf.Lerp(p.y, h + cameraHeight, Time.deltaTime * 8f);
                }
                transform.position = p;
            }
        }

        private void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.wKey.isPressed || kb.upArrowKey.isPressed)
                speed = Mathf.Min(speed + 10f * Time.deltaTime, 120f);
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed)
                speed = Mathf.Max(speed - 10f * Time.deltaTime, 0f);
            if (kb.spaceKey.wasPressedThisFrame)
                _paused = !_paused;
        }

        private void MouseLook()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            _rmbHeld = mouse.rightButton.isPressed;
            if (!_rmbHeld) return;

            _yaw   += mouse.delta.x.ReadValue() * mouseSensitivity;
            _pitch -= mouse.delta.y.ReadValue() * mouseSensitivity;
            _pitch  = Mathf.Clamp(_pitch, -75f, 75f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void OnGUI()
        {
            if (!_hudReady)
            {
                _hud = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 15, fontStyle = FontStyle.Bold,
                    normal    = { textColor = new Color(1f, 1f, 0.88f, 0.88f) }
                };
                _hudReady = true;
            }
            float kmh  = speed * 3.6f;
            float pct  = Mathf.Clamp01(_z / endZ) * 100f;
            GUI.Label(new Rect(16, Screen.height - 80, 520, 72),
                $"Speed: {kmh:F0} km/h   {(_paused ? "| PAUSED" : "")}\n" +
                $"Progress: {pct:F1}%  ({_z/1000f:F2} km / {endZ/1000f:F1} km)\n" +
                "[W/S] speed   [Space] pause   [RMB] look",
                _hud);

            float bw = Screen.width * 0.28f;
            float by = Screen.height - 18f;
            GUI.color = new Color(0,0,0,0.45f);
            GUI.DrawTexture(new Rect(16, by, bw, 5f), Texture2D.whiteTexture);
            GUI.color = new Color(0.28f, 0.88f, 0.38f, 0.9f);
            GUI.DrawTexture(new Rect(16, by, bw * (pct / 100f), 5f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
