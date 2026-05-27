using UnityEngine;
using UnityEngine.XR;

namespace Vehicle
{
    /// <summary>
    /// Reads Meta Quest 3 controller inputs and feeds them to VRCarController.
    ///
    ///  Right Trigger  →  Throttle (analog 0..1)
    ///  Left  Trigger  →  Brake    (analog 0..1)
    ///  Right Grip     →  Stop / Park (hold to engage park when slow)
    ///
    /// Uses UnityEngine.XR.InputDevices — works with any OpenXR backend
    /// including Meta Quest 3.
    /// </summary>
    public class VRCockpitInput : MonoBehaviour
    {
        [Header("References")]
        public VRCarController car;
        public VRImmersionEffects immersionFX;

        [Header("Haptics")]
        [Tooltip("Haptic amplitude during engine idle pulse.")]
        public float idleHapticAmplitude = 0.04f;
        [Tooltip("Haptic amplitude at maximum speed.")]
        public float speedHapticMax = 0.15f;
        [Tooltip("Haptic amplitude when braking hard.")]
        public float brakeHapticAmplitude = 0.12f;

        [Header("Pedal References (visual animation)")]
        public Transform acceleratorPedalTransform;
        public Transform brakePedalTransform;
        [Tooltip("Max rotation of pedal when fully pressed (degrees).")]
        public float pedalMaxRotation = 12f;

        // ── Private ────────────────────────────────────────────────────────────
        private InputDevice _rightController;
        private InputDevice _leftController;

        private float _throttle;
        private float _brake;
        private float _hapticTimer;
        private const float IdleHapticInterval = 0.12f;  // ~8 Hz engine pulse

        private void Start()
        {
            // Try to get devices immediately; retry in Update if not found
            RefreshDevices();
        }

        private void RefreshDevices()
        {
            if (!_rightController.isValid)
                _rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!_leftController.isValid)
                _leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        }

        private void Update()
        {
            if (!_rightController.isValid || !_leftController.isValid)
                RefreshDevices();

            ReadTriggers();
            AnimatePedals();
            SendHaptics();

            if (car == null) return;
            car.SetThrottle(_throttle);
            car.SetBrake(_brake);

            // Park: hold right grip when stopped
            if (_rightController.isValid)
            {
                _rightController.TryGetFeatureValue(CommonUsages.gripButton, out bool grip);
                if (grip && car.IsStopped)
                    car.SetParked(true);
                else if (_throttle > 0.05f)
                    car.SetParked(false);
            }
        }

        private void ReadTriggers()
        {
            // Right trigger → throttle
            if (_rightController.isValid)
                _rightController.TryGetFeatureValue(CommonUsages.trigger, out _throttle);
            else
                _throttle = 0f;

            // Left trigger → brake
            if (_leftController.isValid)
                _leftController.TryGetFeatureValue(CommonUsages.trigger, out _brake);
            else
                _brake = 0f;
        }

        private void AnimatePedals()
        {
            // Rotate pedal pivot on X axis to simulate physical depression
            if (acceleratorPedalTransform != null)
            {
                float targetAngle = -_throttle * pedalMaxRotation;
                Vector3 euler = acceleratorPedalTransform.localEulerAngles;
                euler.x = Mathf.LerpAngle(euler.x, targetAngle, Time.deltaTime * 20f);
                acceleratorPedalTransform.localEulerAngles = euler;
            }

            if (brakePedalTransform != null)
            {
                float targetAngle = -_brake * pedalMaxRotation;
                Vector3 euler = brakePedalTransform.localEulerAngles;
                euler.x = Mathf.LerpAngle(euler.x, targetAngle, Time.deltaTime * 20f);
                brakePedalTransform.localEulerAngles = euler;
            }
        }

        private void SendHaptics()
        {
            if (car == null) return;
            float speed = car.SpeedKmh;
            float rpmFactor = Mathf.Clamp01(car.CurrentRPM / 7000f);

            // ── Right hand (throttle side) ─────────────────────────────────────
            _hapticTimer -= Time.deltaTime;
            if (_hapticTimer <= 0f)
            {
                // Engine idle pulse — always present
                float engineAmplitude = idleHapticAmplitude + rpmFactor * 0.08f;
                SendHaptic(_rightController, engineAmplitude, 0.05f);
                _hapticTimer = IdleHapticInterval * (1f - rpmFactor * 0.5f);
            }

            // Speed rumble on right hand
            if (speed > 80f)
            {
                float rumble = Mathf.InverseLerp(80f, 160f, speed) * speedHapticMax;
                SendHaptic(_rightController, rumble, Time.deltaTime);
            }

            // ── Left hand (brake side) ─────────────────────────────────────────
            if (_brake > 0.3f)
            {
                float brakeRumble = (_brake - 0.3f) / 0.7f * brakeHapticAmplitude;
                SendHaptic(_leftController, brakeRumble, Time.deltaTime);
            }
        }

        private static void SendHaptic(InputDevice device, float amplitude, float duration)
        {
            if (!device.isValid) return;
            device.SendHapticImpulse(0, Mathf.Clamp01(amplitude), duration);
        }

        // ── Public accessors for other systems ─────────────────────────────────
        public float ThrottleValue => _throttle;
        public float BrakeValue    => _brake;
    }
}
