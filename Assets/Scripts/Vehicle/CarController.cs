using UnityEngine;
using UnityEngine.InputSystem;

namespace Vehicle
{
    /// <summary>
    /// Rigidbody-based car controller using four WheelColliders.
    /// No external assets required — full physics with realistic steering.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour
    {
        [Header("Wheel Colliders")]
        public WheelCollider wheelFL;
        public WheelCollider wheelFR;
        public WheelCollider wheelRL;
        public WheelCollider wheelRR;

        [Header("Wheel Transforms (Visual)")]
        public Transform wheelMeshFL;
        public Transform wheelMeshFR;
        public Transform wheelMeshRL;
        public Transform wheelMeshRR;

        [Header("Engine")]
        [Tooltip("Peak motor torque (Nm).")]
        public float motorTorque = 2500f;
        [Tooltip("Top speed (km/h).")]
        public float maxSpeedKmh = 200f;
        [Tooltip("Brake torque (Nm).")]
        public float brakeTorque = 4000f;
        [Tooltip("Handbrake torque (Nm).")]
        public float handbrakeTorque = 8000f;

        [Header("Steering")]
        [Tooltip("Maximum steering angle (degrees).")]
        public float maxSteerAngle = 32f;
        [Tooltip("Steering angle reduction at high speed.")]
        public AnimationCurve steerCurve = AnimationCurve.Linear(0f, 1f, 200f, 0.25f);

        [Header("Stability")]
        [Tooltip("Anti-roll bar stiffness.")]
        public float antiRollStiffness = 8000f;
        [Tooltip("Centre-of-mass Y offset (lower = more stable).")]
        public float centreOfMassY = -0.4f;

        [Header("Audio")]
        public AudioSource engineAudio;
        [Tooltip("Engine idle pitch.")]
        public float idlePitch = 0.5f;
        [Tooltip("Engine max pitch.")]
        public float maxPitch = 2.2f;

        // ── Properties ────────────────────────────────────────────────────────
        public float SpeedKmh => _rb.linearVelocity.magnitude * 3.6f;
        public float ThrottleInput => _throttle;
        public float SteerInput => _steer;

        // ── Private ───────────────────────────────────────────────────────────
        private Rigidbody _rb;
        private float _throttle, _steer, _brake;
        private bool _handbrake;
        private float _currentMotorTorque;

        // Input System actions (read raw)
        private InputAction _accelAction, _brakeAction, _steerAction, _handbrakeAction, _reverseAction;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.mass = 1400f;
            _rb.linearDamping = 0.02f;
            _rb.angularDamping = 0.15f;
            _rb.centerOfMass = new Vector3(0f, centreOfMassY, 0f);
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            SetupWheelFriction();
            SetupInputActions();
        }

        private void OnEnable()
        {
            _accelAction?.Enable();
            _brakeAction?.Enable();
            _steerAction?.Enable();
            _handbrakeAction?.Enable();
            _reverseAction?.Enable();
        }

        private void OnDisable()
        {
            _accelAction?.Disable();
            _brakeAction?.Disable();
            _steerAction?.Disable();
            _handbrakeAction?.Disable();
            _reverseAction?.Disable();
        }

        private void Update()
        {
            ReadInput();
            UpdateWheelMeshes();
            UpdateEngineAudio();
        }

        private void FixedUpdate()
        {
            float speedKmh = SpeedKmh;
            bool movingForward = Vector3.Dot(_rb.linearVelocity, transform.forward) > 0f;

            // Speed limiter
            float torqueMultiplier = speedKmh < maxSpeedKmh ? 1f : 0f;

            // Motor
            _currentMotorTorque = _throttle * motorTorque * torqueMultiplier;
            wheelRL.motorTorque = _currentMotorTorque;
            wheelRR.motorTorque = _currentMotorTorque;

            // Steering (Ackermann approximation via speed curve)
            float steerFactor = steerCurve.Evaluate(speedKmh);
            float angle = _steer * maxSteerAngle * steerFactor;
            wheelFL.steerAngle = angle;
            wheelFR.steerAngle = angle;

            // Braking
            float brakeForce = _brake * brakeTorque;
            wheelFL.brakeTorque = brakeForce;
            wheelFR.brakeTorque = brakeForce;
            wheelRL.brakeTorque = _handbrake ? handbrakeTorque : brakeForce;
            wheelRR.brakeTorque = _handbrake ? handbrakeTorque : brakeForce;

            // Anti-roll bars
            ApplyAntiRoll(wheelFL, wheelFR);
            ApplyAntiRoll(wheelRL, wheelRR);

            // Downforce (improves cornering)
            _rb.AddForce(-transform.up * _rb.linearVelocity.sqrMagnitude * 0.8f);
        }

        // ── Input ─────────────────────────────────────────────────────────────

        private void SetupInputActions()
        {
            _accelAction = new InputAction("Accelerate", InputActionType.Value,
                "<Keyboard>/w", expectedControlType: "Axis");
            _accelAction.AddBinding("<Gamepad>/rightTrigger");
            _accelAction.AddBinding("<Keyboard>/upArrow");

            _brakeAction = new InputAction("Brake", InputActionType.Value,
                "<Keyboard>/s", expectedControlType: "Axis");
            _brakeAction.AddBinding("<Gamepad>/leftTrigger");
            _brakeAction.AddBinding("<Keyboard>/downArrow");

            _steerAction = new InputAction("Steer", InputActionType.Value,
                expectedControlType: "Axis");
            _steerAction.AddBinding("<Keyboard>/a").WithProcessor("scale(factor=-1)");
            _steerAction.AddBinding("<Keyboard>/d");
            _steerAction.AddBinding("<Keyboard>/leftArrow").WithProcessor("scale(factor=-1)");
            _steerAction.AddBinding("<Keyboard>/rightArrow");
            _steerAction.AddBinding("<Gamepad>/leftStick/x");

            _handbrakeAction = new InputAction("Handbrake", InputActionType.Button,
                "<Keyboard>/space");
            _handbrakeAction.AddBinding("<Gamepad>/buttonSouth");

            _reverseAction = new InputAction("Reverse", InputActionType.Button,
                "<Keyboard>/r");
        }

        private void ReadInput()
        {
            _throttle = _accelAction?.ReadValue<float>() ?? 0f;
            _brake = _brakeAction?.ReadValue<float>() ?? 0f;
            _steer = _steerAction?.ReadValue<float>() ?? 0f;
            _steer = Mathf.Clamp(_steer, -1f, 1f);
            _handbrake = _handbrakeAction?.IsPressed() ?? false;
        }

        // ── Wheel Helpers ─────────────────────────────────────────────────────

        private void UpdateWheelMeshes()
        {
            SyncWheelMesh(wheelFL, wheelMeshFL);
            SyncWheelMesh(wheelFR, wheelMeshFR);
            SyncWheelMesh(wheelRL, wheelMeshRL);
            SyncWheelMesh(wheelRR, wheelMeshRR);
        }

        private void SyncWheelMesh(WheelCollider col, Transform mesh)
        {
            if (col == null || mesh == null) return;
            col.GetWorldPose(out Vector3 pos, out Quaternion rot);
            mesh.position = pos;
            mesh.rotation = rot;
        }

        private void SetupWheelFriction()
        {
            var fwdFriction = new WheelFrictionCurve
            {
                extremumSlip = 0.4f, extremumValue = 1f,
                asymptoteSlip = 0.8f, asymptoteValue = 0.75f,
                stiffness = 1.0f
            };
            var sideFriction = new WheelFrictionCurve
            {
                extremumSlip = 0.2f, extremumValue = 1f,
                asymptoteSlip = 0.5f, asymptoteValue = 0.75f,
                stiffness = 1.0f
            };

            foreach (var w in new[] { wheelFL, wheelFR, wheelRL, wheelRR })
            {
                if (w == null) continue;
                w.forwardFriction = fwdFriction;
                w.sidewaysFriction = sideFriction;
                w.suspensionDistance = 0.22f;
                var spring = w.suspensionSpring;
                spring.spring = 28000f;
                spring.damper = 2800f;
                spring.targetPosition = 0.35f;
                w.suspensionSpring = spring;
            }
        }

        private void ApplyAntiRoll(WheelCollider left, WheelCollider right)
        {
            if (left == null || right == null) return;

            left.GetGroundHit(out WheelHit hitL);
            right.GetGroundHit(out WheelHit hitR);

            float travelL = left.isGrounded
                ? (-left.transform.InverseTransformPoint(hitL.point).y - left.radius) / left.suspensionDistance
                : 1f;
            float travelR = right.isGrounded
                ? (-right.transform.InverseTransformPoint(hitR.point).y - right.radius) / right.suspensionDistance
                : 1f;

            float antiRollForce = (travelL - travelR) * antiRollStiffness;
            if (left.isGrounded) _rb.AddForceAtPosition(left.transform.up * -antiRollForce, left.transform.position);
            if (right.isGrounded) _rb.AddForceAtPosition(right.transform.up * antiRollForce, right.transform.position);
        }

        // ── Audio ─────────────────────────────────────────────────────────────

        private void UpdateEngineAudio()
        {
            if (engineAudio == null) return;
            float t = Mathf.Clamp01(SpeedKmh / maxSpeedKmh);
            engineAudio.pitch = Mathf.Lerp(idlePitch, maxPitch, t);
            engineAudio.volume = 0.3f + 0.7f * Mathf.Max(_throttle, t * 0.3f);
        }

        // ── Speed HUD Helper ──────────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 200, 30),
                $"<b><size=18>{SpeedKmh:F0} km/h</size></b>");
        }
#endif
    }
}
