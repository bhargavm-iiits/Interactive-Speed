using UnityEngine;
using InfiniteWorld;

namespace Vehicle
{
    /// <summary>
    /// Moves the Main Camera automatically forward along the road spline —
    /// no car needed. The camera IS the player reference for terrain streaming.
    /// 
    /// Controls (optional manual override):
    ///   Mouse look  — look around
    ///   W/S         — speed up / slow down
    ///   Space       — toggle auto-drive
    /// </summary>
    public class AutoDriveCamera : MonoBehaviour
    {
        [Header("Auto Drive")]
        [Tooltip("Cruise speed in metres per second.")]
        public float cruiseSpeed = 30f;
        [Tooltip("How smoothly the camera aligns to the road tangent.")]
        public float alignSmoothing = 3f;
        [Tooltip("Height above the road surface.")]
        public float rideHeight = 2.8f;
        [Tooltip("Start auto-driving on Play.")]
        public bool autoDriveOnStart = true;

        [Header("Mouse Look")]
        [Tooltip("Enable free mouse look while holding right mouse button.")]
        public bool enableMouseLook = true;
        public float mouseSensitivity = 2f;

        [Header("Road Reference")]
        [Tooltip("Auto-found if left empty.")]
        public InfiniteRoadSystem roadSystem;

        // ── State ──────────────────────────────────────────────────────────────
        private float _distanceAlongRoad = 10f;
        private bool _isAutoDriving;
        private float _yaw, _pitch;
        private bool _mouseHeld;

        // ── Speed HUD ──────────────────────────────────────────────────────────
        public float SpeedKmh => cruiseSpeed * 3.6f;

        private void Start()
        {
            if (roadSystem == null)
                roadSystem = FindFirstObjectByType<InfiniteRoadSystem>();

            _isAutoDriving = autoDriveOnStart;

            // Initialise yaw/pitch from current camera rotation
            _yaw   = transform.eulerAngles.y;
            _pitch = transform.eulerAngles.x;

            Cursor.lockState = CursorLockMode.Confined;
        }

        private void Update()
        {
            HandleSpeedInput();
            HandleAutoToggle();

            if (_isAutoDriving && roadSystem != null && roadSystem.ControlPoints.Count >= 4)
                AutoDrive();
            else
                FreeFly();

            HandleMouseLook();
        }

        // ── Auto Drive ─────────────────────────────────────────────────────────

        private void AutoDrive()
        {
            _distanceAlongRoad += cruiseSpeed * Time.deltaTime;

            Vector3 roadPos    = roadSystem.GetPositionAtDistance(_distanceAlongRoad);
            Vector3 roadTangent = roadSystem.GetTangentAtDistance(_distanceAlongRoad);

            // Terrain height at road position
            float terrainY = SampleTerrain(roadPos);
            roadPos.y = Mathf.Max(roadPos.y, terrainY) + rideHeight;

            // Smooth position follow
            transform.position = Vector3.Lerp(transform.position, roadPos, Time.deltaTime * 12f);

            // Smooth look-ahead
            if (roadTangent.sqrMagnitude > 0.01f && !_mouseHeld)
            {
                Quaternion targetRot = Quaternion.LookRotation(roadTangent, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * alignSmoothing);

                _yaw   = transform.eulerAngles.y;
                _pitch = transform.eulerAngles.x;
            }
        }

        // ── Free Fly (when auto-drive is off or road isn't ready) ─────────────

        private void FreeFly()
        {
            float speed = cruiseSpeed;
            Vector3 move = Vector3.zero;

            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed)   move += transform.forward;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  move -= transform.forward;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  move -= transform.right;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move += transform.right;
                if (kb.eKey.isPressed) move += transform.up;
                if (kb.qKey.isPressed) move -= transform.up;
            }

            transform.position += move.normalized * speed * Time.deltaTime;
        }

        // ── Mouse Look ─────────────────────────────────────────────────────────

        private void HandleMouseLook()
        {
            if (!enableMouseLook) return;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return;

            _mouseHeld = mouse.rightButton.isPressed;
            if (!_mouseHeld) return;

            _yaw   += mouse.delta.x.ReadValue() * mouseSensitivity;
            _pitch -= mouse.delta.y.ReadValue() * mouseSensitivity;
            _pitch  = Mathf.Clamp(_pitch, -80f, 80f);

            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        // ── Input Helpers ──────────────────────────────────────────────────────

        private void HandleSpeedInput()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;

            if (kb.wKey.isPressed && _isAutoDriving)
                cruiseSpeed = Mathf.Min(cruiseSpeed + 5f * Time.deltaTime, 120f);
            if (kb.sKey.isPressed && _isAutoDriving)
                cruiseSpeed = Mathf.Max(cruiseSpeed - 5f * Time.deltaTime, 5f);
        }

        private void HandleAutoToggle()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.spaceKey.wasPressedThisFrame)
                _isAutoDriving = !_isAutoDriving;
        }

        // ── Utility ────────────────────────────────────────────────────────────

        private float SampleTerrain(Vector3 pos)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null) continue;
                var tp = t.transform.position;
                var td = t.terrainData.size;
                if (pos.x >= tp.x && pos.x <= tp.x + td.x &&
                    pos.z >= tp.z && pos.z <= tp.z + td.z)
                    return t.SampleHeight(pos);
            }
            return 0f;
        }

        // ── HUD ────────────────────────────────────────────────────────────────

        private GUIStyle _hudStyle;
        private bool _styleInit;

        private void OnGUI()
        {
            if (!_styleInit)
            {
                _hudStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.95f, 0.95f, 0.9f, 0.9f) }
                };
                _styleInit = true;
            }

            float w = Screen.width, h = Screen.height;
            GUI.Label(new Rect(16, h - 90, 400, 80),
                $"Speed: {SpeedKmh:F0} km/h   |   Auto-drive: {(_isAutoDriving ? "ON" : "OFF")}\n" +
                $"Road dist: {_distanceAlongRoad:F0} m\n" +
                $"[Space] toggle drive  [RMB] look  [W/S] speed",
                _hudStyle);
        }
    }
}
