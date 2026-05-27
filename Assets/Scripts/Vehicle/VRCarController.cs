using UnityEngine;
using UnityEngine.InputSystem;

namespace Vehicle
{
    /// <summary>
    /// Simple, reliable VR car controller.
    /// Uses Transform-based movement + downward Raycast for ground following.
    /// NO WheelCollider physics — eliminates all bouncing/flying issues.
    ///
    /// Public API is identical to the old physics version so all other
    /// scripts (VRCockpitInput, VRSteeringWheel, VRDashboard, etc.) work unchanged.
    ///
    /// In Editor: WASD keys drive the car.
    /// On Quest 3:  Right trigger = throttle, Left trigger = brake.
    /// </summary>
    public class VRCarController : MonoBehaviour
    {
        // ── Movement ───────────────────────────────────────────────────────────
        [Header("Movement")]
        [Tooltip("Top speed in km/h.")]
        public float maxSpeedKmh     = 120f;
        [Tooltip("Acceleration in km/h per second.")]
        public float acceleration    = 22f;
        [Tooltip("Braking deceleration in km/h per second.")]
        public float brakeDecel      = 45f;
        [Tooltip("Natural roll-off when no throttle/brake.")]
        public float rollOffDecel    = 8f;
        [Tooltip("Max steer angle at low speed (degrees/sec of yaw).")]
        public float maxSteerYaw     = 42f;

        // ── Ground Following ───────────────────────────────────────────────────
        [Header("Ground Following")]
        [Tooltip("How high the car body sits above the ground surface.")]
        public float groundOffset    = 0.45f;
        [Tooltip("Raycast distance to search for ground below car.")]
        public float groundRayDist   = 8f;
        [Tooltip("How fast the car Y snaps to ground height.")]
        public float groundSnapSpeed = 12f;
        [Tooltip("How fast car aligns to terrain slope.")]
        public float slopeAlignSpeed = 5f;
        [Tooltip("Layers that count as driveable ground. -1 = all layers.")]
        public LayerMask groundLayers = -1;

        // ── Editor Keyboard ────────────────────────────────────────────────────
        [Header("Editor Keyboard Fallback (WASD)")]
        public bool enableKeyboardFallback = true;

        // ── Wheel Colliders (kept for backward-compat — not used for physics) ──
        [Header("Wheel Colliders (visual only — physics not used)")]
        public WheelCollider wheelFL;
        public WheelCollider wheelFR;
        public WheelCollider wheelRL;
        public WheelCollider wheelRR;

        [Header("Wheel Meshes (Visual)")]
        public Transform wheelMeshFL;
        public Transform wheelMeshFR;
        public Transform wheelMeshRL;
        public Transform wheelMeshRR;

        // ── Public Read-Only Properties (same API as before) ───────────────────
        public float SpeedKmh         => _speedKmh;
        public float ThrottleInput    => _throttle;
        public float BrakeInput       => _brake;
        public float SteerInput       => _steer;
        public float CurrentRPM       { get; internal set; } = 800f;
        public int   CurrentGear      { get; internal set; } = 1;
        public float TorqueMultiplier { get; internal set; } = 1f;
        public bool  IsStopped        => _speedKmh < 0.5f;

        // ── Private ────────────────────────────────────────────────────────────
        private float _speedKmh;
        private float _throttle;
        private float _steer;
        private float _brake;
        private bool  _parked = false;   // starts in drive mode — just press W

        private float _steerAngle;       // current yaw being applied
        private float _wheelRotation;    // visual wheel spin angle

        // Raycast results cache to avoid GC allocation at 72 FPS
        private readonly RaycastHit[] _raycastHits = new RaycastHit[8];

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Start()
        {
            // Snap to ground immediately on first frame
            SnapToGround();
        }

        private void Update()
        {
            // ── Keyboard fallback (Editor only) ───────────────────────────────
#if UNITY_EDITOR
            if (enableKeyboardFallback)
                ReadKeyboard();
#endif

            float dt = Time.deltaTime;

            // ── Speed computation ─────────────────────────────────────────────
            if (_parked)
            {
                _speedKmh = Mathf.MoveTowards(_speedKmh, 0f, brakeDecel * dt);
            }
            else
            {
                if (_throttle > 0.01f)
                    _speedKmh += _throttle * acceleration * TorqueMultiplier * dt;

                if (_brake > 0.01f)
                    _speedKmh -= _brake * brakeDecel * dt;
                else if (_throttle < 0.01f)
                    _speedKmh -= rollOffDecel * dt;
            }

            _speedKmh = Mathf.Clamp(_speedKmh, 0f, maxSpeedKmh);

            // ── Steering ──────────────────────────────────────────────────────
            // Speed-sensitive steering: full angle at 20 km/h, reduced at high speed
            float speedT         = Mathf.Clamp01(_speedKmh / 80f);
            float effectiveYaw   = Mathf.Lerp(maxSteerYaw, maxSteerYaw * 0.25f, speedT);

            // Only steer when moving
            float speedFactor    = Mathf.Clamp01(_speedKmh / 5f);
            float yawDelta       = _steer * effectiveYaw * speedFactor * dt;
            transform.Rotate(Vector3.up, yawDelta, Space.World);

            // ── Move forward ──────────────────────────────────────────────────
            float speedMs = _speedKmh / 3.6f;
            transform.Translate(Vector3.forward * speedMs * dt, Space.Self);

            // ── Follow ground ─────────────────────────────────────────────────
            FollowGround(dt);

            // ── Spin wheel meshes visually ────────────────────────────────────
            _wheelRotation += speedMs * dt * Mathf.Rad2Deg / 0.33f;
            SpinWheels(_wheelRotation);
        }

        // ── Ground Following ───────────────────────────────────────────────────

        private bool FindGround(Vector3 origin, float maxDistance, out RaycastHit closestHit)
        {
            closestHit = default;
            // Pre-allocated non-allocating raycast
            int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, _raycastHits, maxDistance, groundLayers, QueryTriggerInteraction.Ignore);
            
            float closestDist = float.MaxValue;
            bool found = false;
            
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _raycastHits[i];
                if (hit.transform == null) continue;
                
                // Ignore the car itself and any of its children to prevent flying/self-collision
                if (hit.transform.IsChildOf(transform)) continue;
                
                if (hit.distance < closestDist)
                {
                    closestDist = hit.distance;
                    closestHit = hit;
                    found = true;
                }
            }
            
            return found;
        }

        private void FollowGround(float dt)
        {
            // Cast a ray from above the car downward, ignoring the car's own colliders
            Vector3 rayOrigin = transform.position + Vector3.up * 3f;
            if (FindGround(rayOrigin, groundRayDist, out RaycastHit hit))
            {
                // Snap Y to ground height + offset
                float targetY  = hit.point.y + groundOffset;
                Vector3 pos    = transform.position;
                pos.y          = Mathf.Lerp(pos.y, targetY, dt * groundSnapSpeed);
                transform.position = pos;

                // Gently align car to terrain slope
                Quaternion slopeRot = Quaternion.FromToRotation(transform.up, hit.normal)
                                      * transform.rotation;
                transform.rotation = Quaternion.Lerp(transform.rotation, slopeRot,
                                                     dt * slopeAlignSpeed);
            }
        }

        /// <summary>Called once in Start() to instantly place car on ground.</summary>
        private void SnapToGround()
        {
            Vector3 rayOrigin = transform.position + Vector3.up * 5f;
            if (FindGround(rayOrigin, groundRayDist + 5f, out RaycastHit hit))
            {
                Vector3 pos = transform.position;
                pos.y = hit.point.y + groundOffset;
                transform.position = pos;
                Debug.Log($"[VRCarController] Snapped to ground at Y={pos.y:F2}");
            }
            else
            {
                Debug.LogWarning("[VRCarController] Could not find ground below car. " +
                                 "Make sure VRCar is above the terrain and that the Ground Layers mask is correct.");
            }
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Set throttle 0..1.</summary>
        public void SetThrottle(float value)
        {
            _throttle = Mathf.Clamp01(value);
            if (_throttle > 0.02f) _parked = false;
        }

        /// <summary>Set brake 0..1.</summary>
        public void SetBrake(float value) => _brake = Mathf.Clamp01(value);

        /// <summary>Set steer -1 (left) .. +1 (right).</summary>
        public void SetSteer(float value) => _steer = Mathf.Clamp(value, -1f, 1f);

        /// <summary>Engage or release park.</summary>
        public void SetParked(bool parked)
        {
            _parked = parked;
            if (parked) _throttle = 0f;
        }

        // ── Keyboard Fallback (Editor) ─────────────────────────────────────────
#if UNITY_EDITOR
        private void ReadKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            float throttle = (kb.wKey.isPressed || kb.upArrowKey.isPressed)    ? 1f : 0f;
            float brake    = (kb.sKey.isPressed || kb.downArrowKey.isPressed)   ? 1f : 0f;
            float steer    = 0f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer =  1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  steer = -1f;

            // Always call the API so releasing keys resets input to 0 correctly
            SetThrottle(throttle);
            SetBrake(brake);
            SetSteer(steer);
        }
#endif

        // ── Wheel Visual Spin ──────────────────────────────────────────────────

        private void SpinWheels(float rotAngle)
        {
            SpinWheel(wheelMeshFL, rotAngle, _steer * 25f);
            SpinWheel(wheelMeshFR, rotAngle, _steer * 25f);
            SpinWheel(wheelMeshRL, rotAngle, 0f);
            SpinWheel(wheelMeshRR, rotAngle, 0f);
        }

        private static void SpinWheel(Transform mesh, float spin, float steerAngle)
        {
            if (mesh == null) return;
            mesh.localRotation = Quaternion.Euler(spin, steerAngle, 0f);
        }

        // ── Debug ──────────────────────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Show ground ray
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position + Vector3.up * 3f,
                            transform.position + Vector3.down * groundRayDist);
            Gizmos.DrawWireSphere(transform.position, 0.1f);
        }
#endif
    }
}
