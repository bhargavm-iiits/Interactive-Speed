using UnityEngine;

namespace Vehicle
{
    /// <summary>
    /// Cinematic spring-arm follow camera for the player car.
    /// Follows with lag, leans into turns, and narrows FOV at high speed.
    /// </summary>
    public class FollowCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;
        [Tooltip("Offset from target in target's local space.")]
        public Vector3 offset = new Vector3(0f, 2.2f, -6.5f);

        [Header("Spring Arm")]
        [Tooltip("Position follow speed (higher = snappier).")]
        public float positionDamping = 6f;
        [Tooltip("Rotation follow speed.")]
        public float rotationDamping = 5f;
        [Tooltip("Look-ahead distance along target forward.")]
        public float lookAheadDistance = 8f;

        [Header("FOV")]
        [Tooltip("FOV at rest.")]
        public float baseFOV = 65f;
        [Tooltip("FOV at maximum speed.")]
        public float maxSpeedFOV = 80f;
        [Tooltip("Speed (km/h) at which max FOV is reached.")]
        public float maxSpeedRef = 200f;
        [Tooltip("FOV change smoothing.")]
        public float fovDamping = 3f;

        [Header("Camera Shake")]
        [Tooltip("Shake amplitude at max speed.")]
        public float maxShakeAmplitude = 0.04f;
        [Tooltip("Shake frequency.")]
        public float shakeFrequency = 25f;

        // ── Private ───────────────────────────────────────────────────────────
        private Camera _camera;
        private CarController _car;
        private float _currentFOV;
        private Vector3 _smoothVelocity;
        private float _shakeTime;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (_camera == null) _camera = GetComponentInChildren<Camera>();

            if (_camera != null)
                _currentFOV = _camera.fieldOfView = baseFOV;
        }

        private void Start()
        {
            if (target != null)
                _car = target.GetComponent<CarController>();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            // Desired position in world space
            Vector3 desiredPos = target.TransformPoint(offset);
            Vector3 lookAt = target.position + target.forward * lookAheadDistance;

            // Spring-arm position with velocity damping
            transform.position = Vector3.SmoothDamp(
                transform.position, desiredPos,
                ref _smoothVelocity, 1f / positionDamping);

            // Smooth rotation toward look-at
            Vector3 lookDir = (lookAt - transform.position).normalized;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, targetRot,
                    Time.deltaTime * rotationDamping);
            }

            // Dynamic FOV
            if (_camera != null)
            {
                float speedKmh = _car != null ? _car.SpeedKmh : 0f;
                float t = Mathf.Clamp01(speedKmh / maxSpeedRef);
                float targetFOV = Mathf.Lerp(baseFOV, maxSpeedFOV, t);
                _currentFOV = Mathf.Lerp(_currentFOV, targetFOV, Time.deltaTime * fovDamping);
                _camera.fieldOfView = _currentFOV;

                // Subtle camera shake at high speed
                _shakeTime += Time.deltaTime * shakeFrequency;
                float shakeMag = maxShakeAmplitude * (speedKmh / maxSpeedRef);
                Vector3 shake = new Vector3(
                    (Mathf.PerlinNoise(_shakeTime, 0f) - 0.5f) * shakeMag,
                    (Mathf.PerlinNoise(0f, _shakeTime) - 0.5f) * shakeMag,
                    0f);
                transform.localPosition += shake;
            }
        }

        /// <summary>Assigns target at runtime.</summary>
        public void SetTarget(Transform t)
        {
            target = t;
            _car = t != null ? t.GetComponent<CarController>() : null;
        }
    }
}
