using UnityEngine;
using UnityEngine.XR;

namespace Vehicle
{
    /// <summary>
    /// Grabbable VR steering wheel.
    ///
    /// USAGE: Attach to the steering wheel GameObject.
    /// The player grabs it with either or both hands by squeezing the grip button.
    /// Wheel rotation drives VRCarController.SetSteer().
    ///
    /// Physics model:
    ///  – Each grabbed hand contributes an angle offset from wheel centre
    ///  – Primary hand (first grabbed) drives rotation directly
    ///  – Secondary hand stabilises (averaged with primary)
    ///  – Spring return to centre when released
    ///
    /// D-shape wheel: rotation is clamped to ±540° (1.5 turns lock-to-lock)
    /// </summary>
    public class VRSteeringWheel : MonoBehaviour
    {
        [Header("References")]
        public VRCarController car;
        [Tooltip("The visual steering wheel transform (this spins).")]
        public Transform wheelVisual;
        [Tooltip("XR Origin camera offset or car cockpit anchor.")]
        public Transform cockpitAnchor;

        [Header("Steering Settings")]
        [Tooltip("Total rotation lock-to-lock in degrees (540 = 1.5 turns each way).")]
        public float lockToLockDegrees = 540f;
        [Tooltip("Spring force returning wheel to centre (degrees/sec²).")]
        public float centeringSpeed = 180f;
        [Tooltip("Damping on self-centering (0=none, 1=critical).")]
        [Range(0f, 1f)]
        public float centeringDamping = 0.85f;
        [Tooltip("Grab detection sphere radius around wheel rim.")]
        public float grabRadius = 0.18f;

        [Header("Haptics")]
        public float steeringHapticAmplitude = 0.06f;

        // ── Private ────────────────────────────────────────────────────────────
        private InputDevice _rightCtrl;
        private InputDevice _leftCtrl;

        private bool _rightGrabbing;
        private bool _leftGrabbing;
        private bool _rightPrevGrip;
        private bool _leftPrevGrip;

        // Position of controller at grab start, and wheel angle at that moment
        private Vector3 _rightGrabLocalPos;
        private Vector3 _leftGrabLocalPos;
        private float _rightGrabWheelAngle;
        private float _leftGrabWheelAngle;

        // Current accumulated wheel rotation (degrees, positive = clockwise)
        private float _wheelAngle = 0f;
        private float _wheelVelocity = 0f;

        // Steer output -1..1
        private float _steerOutput = 0f;
        public float SteerValue => _steerOutput;

        private void Start()
        {
            RefreshDevices();
        }

        private void RefreshDevices()
        {
            if (!_rightCtrl.isValid) _rightCtrl = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!_leftCtrl.isValid)  _leftCtrl  = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        }

        private void Update()
        {
            RefreshDevices();

            HandleGrab(ref _rightCtrl, ref _rightGrabbing, ref _rightPrevGrip,
                       ref _rightGrabLocalPos, ref _rightGrabWheelAngle, XRNode.RightHand);
            HandleGrab(ref _leftCtrl, ref _leftGrabbing, ref _leftPrevGrip,
                       ref _leftGrabLocalPos, ref _leftGrabWheelAngle, XRNode.LeftHand);

            bool anyGrabbing = _rightGrabbing || _leftGrabbing;

            if (anyGrabbing)
            {
                float delta = CalculateWheelDelta();
                _wheelVelocity = delta / Time.deltaTime;
                _wheelAngle += delta;
                _wheelAngle  = Mathf.Clamp(_wheelAngle, -lockToLockDegrees * 0.5f, lockToLockDegrees * 0.5f);

                // Haptics when steering hard
                float hapticAmt = Mathf.Abs(delta / Time.deltaTime) / 360f * steeringHapticAmplitude;
                if (hapticAmt > 0.01f)
                {
                    SendHaptic(_rightCtrl, hapticAmt, Time.deltaTime);
                    SendHaptic(_leftCtrl,  hapticAmt, Time.deltaTime);
                }
            }
            else
            {
                // Spring return to centre
                float damping = 1f - Mathf.Clamp01(centeringDamping);
                float centering = -_wheelAngle * centeringSpeed * Time.deltaTime;
                _wheelVelocity = Mathf.Lerp(_wheelVelocity + centering, 0f, 1f - damping);
                _wheelAngle += _wheelVelocity * Time.deltaTime;

                if (Mathf.Abs(_wheelAngle) < 0.5f)
                {
                    _wheelAngle = 0f;
                    _wheelVelocity = 0f;
                }
            }

            // Steer output normalised -1..1
            _steerOutput = Mathf.Clamp(_wheelAngle / (lockToLockDegrees * 0.5f), -1f, 1f);

            // Apply steer to visual
            if (wheelVisual != null)
            {
                Vector3 euler = wheelVisual.localEulerAngles;
                euler.z = -_wheelAngle;
                wheelVisual.localEulerAngles = euler;
            }

            // Send to car
            if (car != null)
                car.SetSteer(_steerOutput);
        }

        // ── Grab Handling ──────────────────────────────────────────────────────

        private void HandleGrab(ref InputDevice device, ref bool isGrabbing,
                                ref bool prevGrip, ref Vector3 grabLocalPos,
                                ref float grabWheelAngle, XRNode node)
        {
            if (!device.isValid) return;

            device.TryGetFeatureValue(CommonUsages.gripButton, out bool currentGrip);

            // Check if controller is close enough to wheel rim
            bool nearWheel = IsNearWheelRim(node);

            if (currentGrip && !prevGrip && nearWheel)
            {
                // Grab start
                isGrabbing = true;
                grabLocalPos = GetControllerLocalPos(node);
                grabWheelAngle = _wheelAngle;
            }
            else if (!currentGrip && prevGrip && isGrabbing)
            {
                // Release
                isGrabbing = false;
            }

            prevGrip = currentGrip;
        }

        private float CalculateWheelDelta()
        {
            float totalDelta = 0f;
            int count = 0;

            if (_rightGrabbing)
            {
                float delta = ComputeAngleDelta(_rightGrabLocalPos, XRNode.RightHand, _rightGrabWheelAngle);
                totalDelta += delta;
                count++;
            }

            if (_leftGrabbing)
            {
                float delta = ComputeAngleDelta(_leftGrabLocalPos, XRNode.LeftHand, _leftGrabWheelAngle);
                totalDelta += delta;
                count++;
            }

            return count > 0 ? totalDelta / count : 0f;
        }

        /// <summary>
        /// Computes how much the wheel should rotate based on controller movement
        /// around the wheel axis (Z axis in local space).
        /// </summary>
        private float ComputeAngleDelta(Vector3 grabLocalPos, XRNode node, float grabAngle)
        {
            Vector3 currentLocalPos = GetControllerLocalPos(node);

            // Angle of grab position around wheel centre (projected to XY plane)
            float grabAngleRaw    = Mathf.Atan2(grabLocalPos.y,    grabLocalPos.x)    * Mathf.Rad2Deg;
            float currentAngleRaw = Mathf.Atan2(currentLocalPos.y, currentLocalPos.x) * Mathf.Rad2Deg;

            float rawDelta = Mathf.DeltaAngle(grabAngleRaw, currentAngleRaw);
            return grabAngle + rawDelta - _wheelAngle;
        }

        private Vector3 GetControllerLocalPos(XRNode node)
        {
            // Get controller world position, convert to wheel local space
            InputDevices.GetDeviceAtXRNode(node).TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 worldPos);
            Transform reference = cockpitAnchor != null ? cockpitAnchor : transform.parent;
            return reference != null ? reference.InverseTransformPoint(worldPos) : worldPos;
        }

        private bool IsNearWheelRim(XRNode node)
        {
            InputDevices.GetDeviceAtXRNode(node).TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 worldPos);
            float dist = Vector3.Distance(worldPos, transform.position);
            return dist < grabRadius;
        }

        private static void SendHaptic(InputDevice device, float amplitude, float duration)
        {
            if (!device.isValid) return;
            device.SendHapticImpulse(0, Mathf.Clamp01(amplitude), duration);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, grabRadius);
        }
#endif
    }
}
