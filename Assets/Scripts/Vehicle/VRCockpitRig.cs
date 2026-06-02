using UnityEngine;
using UnityEngine.XR;

namespace Vehicle
{
    /// <summary>
    /// Positions the XR Origin rigidly inside the cockpit (driver seat anchor).
    /// The XR Origin becomes a child of the car — head-tracking still works
    /// locally, giving the correct "seated inside a moving car" effect.
    ///
    /// Camera shake is applied to the camera offset child, NOT the XR Origin root,
    /// to avoid disturbing the tracking origin.
    /// </summary>
    public class VRCockpitRig : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The XR Origin (or XR Rig) GameObject. If null, auto-searched.")]
        public GameObject xrOrigin;
        [Tooltip("The car root transform that moves.")]
        public Transform carRoot;
        public VRCarController car;

        [Header("Driver Seat Offset")]
        [Tooltip("Position of driver eye level relative to car root (local space).")]
        public Vector3 driverSeatOffset = new Vector3(-0.42f, 0.75f, 0.25f);

        [Header("Camera FX")]
        [Tooltip("Backward sway offset during hard acceleration.")]
        public float accelerationSwayAmount = 0.04f;
        [Tooltip("Forward nod offset during hard braking.")]
        public float brakeNodAmount = 0.05f;
        [Tooltip("How fast camera sways respond.")]
        public float swaySmoothing = 5f;

        [Header("Idle Vibration")]
        public float idleVibeAmplitude = 0.0015f;
        [Tooltip("Noise frequency for idle vibration.")]
        public float idleVibeFrequency = 14f;

        [Header("Speed Shake")]
        [Tooltip("Speed (km/h) at which max shake is reached.")]
        public float maxShakeSpeed = 160f;
        public float maxShakeAmplitude = 0.004f;

        // ── Private ────────────────────────────────────────────────────────────
        private Transform _cameraOffsetTransform;
        private Vector3 _baseCameraOffset;
        private float _noiseOffsetX;
        private float _noiseOffsetY;
        private float _noiseOffsetZ;

        private float _prevSpeed;
        private Vector3 _swayOffset;

        private void Awake()
        {
            // Force seat offset directly behind the steering wheel coordinates (3, 1.1, 8) in absolute local camera position:
            driverSeatOffset = new Vector3(0.22587f, 1.89f, 4.69f);

            _noiseOffsetX = Random.Range(0f, 100f);
            _noiseOffsetY = Random.Range(0f, 100f);
            _noiseOffsetZ = Random.Range(0f, 100f);

            if (car == null) car = GetComponentInParent<VRCarController>();

            FindAndConfigureXROrigin();
        }

        private void FindAndConfigureXROrigin()
        {
            if (xrOrigin == null)
            {
                // Try to find by type name (works whether XRI is installed or not)
                var all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (var mb in all)
                {
                    if (mb.GetType().Name == "XROrigin" || mb.GetType().Name == "XRRig")
                    {
                        xrOrigin = mb.gameObject;
                        break;
                    }
                }
            }

            if (xrOrigin == null)
            {
                Debug.LogWarning("[VRCockpitRig] XR Origin not found. Create an XR Origin in the scene.");
                return;
            }

            // Parent the XR Origin to the car so it moves with it
            if (carRoot != null)
            {
                xrOrigin.transform.SetParent(carRoot, false);
                xrOrigin.transform.localPosition = driverSeatOffset;
                xrOrigin.transform.localRotation = Quaternion.identity;
            }

            // Find the CameraOffset child (standard XRI hierarchy)
            _cameraOffsetTransform = xrOrigin.transform.Find("Camera Offset");
            if (_cameraOffsetTransform == null)
                _cameraOffsetTransform = xrOrigin.transform; // fallback

            _baseCameraOffset = _cameraOffsetTransform.localPosition;
        }

        private void LateUpdate()
        {
            if (_cameraOffsetTransform == null || car == null) return;

            float speed = car.SpeedKmh;
            float throttle = car.ThrottleInput;
            float brake    = car.BrakeInput;

            // ── Acceleration sway ──────────────────────────────────────────────
            float accelDelta = (speed - _prevSpeed) / Time.deltaTime;
            _prevSpeed = speed;

            Vector3 targetSway = Vector3.zero;
            targetSway += Vector3.back  * Mathf.Clamp(accelDelta * 0.001f,  0f, accelerationSwayAmount);
            targetSway += Vector3.forward * brake * brakeNodAmount;

            _swayOffset = Vector3.Lerp(_swayOffset, targetSway, Time.deltaTime * swaySmoothing);

            // ── Idle engine vibration ──────────────────────────────────────────
            float t = Time.time * idleVibeFrequency;
            float vx = (Mathf.PerlinNoise(t, _noiseOffsetX) - 0.5f) * 2f * idleVibeAmplitude;
            float vy = (Mathf.PerlinNoise(t + 0.33f, _noiseOffsetY) - 0.5f) * 2f * idleVibeAmplitude;
            float vz = (Mathf.PerlinNoise(t + 0.66f, _noiseOffsetZ) - 0.5f) * 2f * idleVibeAmplitude;

            // ── High-speed road shake ──────────────────────────────────────────
            float speedShakeFactor = Mathf.InverseLerp(60f, maxShakeSpeed, speed);
            float st = Time.time * 28f;
            float sx = (Mathf.PerlinNoise(st,        _noiseOffsetX + 50f) - 0.5f) * 2f;
            float sy = (Mathf.PerlinNoise(st + 0.5f, _noiseOffsetY + 50f) - 0.5f) * 2f;
            Vector3 speedShake = new Vector3(sx, sy, 0f) * maxShakeAmplitude * speedShakeFactor;

            Vector3 combinedOffset = _baseCameraOffset + _swayOffset
                + new Vector3(vx, vy, vz)
                + speedShake;

            _cameraOffsetTransform.localPosition = combinedOffset;
        }
    }
}
