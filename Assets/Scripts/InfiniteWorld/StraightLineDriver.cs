using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace InfiniteWorld
{
    /// <summary>
    /// Drives the VRCar along the road path with a full first-person cockpit experience.
    ///
    /// Features:
    ///  - Snaps car to terrain (no flying)
    ///  - Builds cockpit interior automatically (steering wheel, pedals, dashboard)
    ///  - Steering wheel rotates with A/D or Left/Right arrow keys
    ///  - Accelerator pedal depresses with W
    ///  - Brake pedal depresses with S
    ///  - R key = reverse gear
    ///  - Camera sits at driver eye level inside cockpit
    ///  - Tree collision: stop → reverse → resume
    /// </summary>
    public class StraightLineDriver : MonoBehaviour
    {
        // ── Hidden world references (wired by StaticWorldBuilder) ────────────
        [HideInInspector] public StaticWorldBuilder worldBuilder;
        [HideInInspector] public Terrain            terrain;
        [HideInInspector] public float              speed        = 28f;
        [HideInInspector] public float              endZ         = 4950f;
        [HideInInspector] public float              cameraHeight = 2.8f;

        // ── Driver settings ───────────────────────────────────────────────────
        [Header("Driver Seat (local pos inside VRCar)")]
        // Calculated from actual scene geometry:
        //   Seat_Driver in cockpitRoot : (-0.35, -0.5, -0.1)
        //   cockpitRoot in VRCar       : (0, 0.3, 0.1)
        //   Headrest in Seat_Driver    : (0, 0.8, -0.27)
        //   => eye in VRCar space      : (-0.35, 0.60, -0.15)
        public Vector3 driverEyeLocalPos = new Vector3(-0.35f, 0.60f, -0.15f);

        [Header("Ground Snap")]
        public float groundOffset      = 0.28f;
        public float groundRayDistance = 12f;
        public LayerMask groundLayers  = -1;

        [Header("Steering")]
        public float maxSteeringAngle  = 220f;   // degrees each way on wheel
        public float steerSpeed        = 5f;     // wheel return / follow speed
        public float carYawRate        = 38f;    // degrees/sec yaw at full steer

        [Header("Speed")]
        public float acceleration      = 18f;    // km/h per second
        public float brakeDecel        = 50f;
        public float rollOff           = 8f;
        public float maxSpeedKmh       = 100f;
        public float reverseMaxKmh     = 30f;

        [Header("Mouse Look")]
        public float mouseSensitivity  = 2.5f;

        [Header("Tree Collision")]
        public float treeDetectionRadius = 2.5f;
        public float treeDetectionOffset = 3.0f;
        public float treePauseDuration   = 0.5f;
        public float treeReverseSpeed    = 7f;
        public float treeReverseDuration = 1.8f;

        // ── Public Accessors for Speed Lesson ─────────────────────────────
        public float SpeedKmh { get => _speedKmh; set => _speedKmh = value; }
        public float Z { get => _z; set => _z = value; }
        public bool Paused { get => _paused; set => _paused = value; }
        public float? automaticSpeedKmh;
        public Transform Car => _car;

        public float Throttle => _throttle;
        public float BrakeInput => _brakeInput;
        public bool IsReverse => _reverse;

        // ── Private state ─────────────────────────────────────────────────────
        private Transform _car;
        private Transform _xrRoot;

        // Cockpit interior refs (found after VRCockpitBuilder runs)
        private Transform _steeringWheelPivot;
        private Transform _accelPedalPivot;
        private Transform _brakePedalPivot;

        // Input
        private float _speedKmh;
        private float _steerInput;   // -1 = left, +1 = right
        private float _throttle;     // 0..1
        private float _brakeInput;   // 0..1
        private bool  _reverse;
        private bool  _paused;

        // XR Controllers
        private UnityEngine.XR.InputDevice _leftHandDevice;
        private UnityEngine.XR.InputDevice _rightHandDevice;

        // Steering wheel visual angle (smooth)
        private float _wheelAngle;

        // Road position tracker
        private float _z;

        // Mouse look
        private float _yaw, _pitch;
        private bool  _rmbHeld;

        // Tree hit state
        private enum HitState { None, Pausing, Reversing }
        private HitState _hitState    = HitState.None;
        private float    _hitTimer    = 0f;
        private float    _hitCooldown = 0f;

        // Raycast buffers
        private readonly Collider[]   _overlapBuf  = new Collider[16];
        private readonly RaycastHit[] _groundHits  = new RaycastHit[8];

        // HUD
        private GUIStyle _hudStyle;
        private bool     _hudReady;

        // Custom HUD Styles (Forza Horizon style)
        private GUIStyle _progressStyle;
        private GUIStyle _progressLabelStyle;
        private GUIStyle _timeStyle;
        private GUIStyle _speedStyle;
        private GUIStyle _speedLabelStyle;
        private GUIStyle _gearStyle;
        private GUIStyle _posStyle;
        private GUIStyle _posLabelStyle;
        private Texture2D _gearBgTex;
        private Texture2D _speedometerFaceTex;
        private Texture2D _circleBadgeTex;
        private Texture2D _hudPlateTex;
        private Texture2D _pedalBgTex;
        private Texture2D _pedalFillBrakeTex;
        private Texture2D _pedalFillAccelTex;
        private Rect _pauseButtonRect = new Rect(25, 25, 45, 45);
        private bool _stylesInitialized;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            // Force driver eye position to match absolute local camera position from editor coordinates:
            // Camera Offset (local pos: -15.8, 0.5587897, 0.2500006) * Main Camera (local pos: 31.8, 2.66, 4.51)
            // => Absolute local pos relative to VRCar: (0.22587f, 1.89f, 4.69f)
            driverEyeLocalPos = new Vector3(0.22587f, 1.89f, 4.69f);

            // Clean up WheelColliders early to prevent "WheelCollider requires an attached Rigidbody to function" error
            var wheelColliders = FindObjectsByType<WheelCollider>(FindObjectsSortMode.None);
            foreach (var wc in wheelColliders)
            {
                Destroy(wc);
            }
        }

        private void Start()
        {
            _z   = 0f;
            _yaw = transform.eulerAngles.y;
            Cursor.lockState = CursorLockMode.Confined;

            SetupCar();
            EnsureTreeColliders();
        }

        public void SnapCarToRoadStart()
        {
            PlaceCarAtRoadStart();
        }

        // ── Car setup ─────────────────────────────────────────────────────────

        private void SetupCar()
        {
            // 1. Find or create VRCar
            FindOrCreateCar();
            if (_car == null) return;

            // 2. Remove any external car body (user wants cockpit-only view)
            CleanupExternalBody();

            // 3. Ensure scale is correct
            _car.localScale = Vector3.one;

            // 4. Disable VRCarController (we drive from here)
            var vrc = _car.GetComponent<Vehicle.VRCarController>();
            if (vrc != null) vrc.enabled = false;

            // 4b. Disable VRSteeringWheel script to prevent it from overwriting visual steering wheel rotation in keyboard mode
            var vsw = _car.GetComponentInChildren<Vehicle.VRSteeringWheel>(true);
            if (vsw != null) vsw.enabled = false;

            // 5. Ensure VRCockpitBuilder runs to create interior
            EnsureCockpit();

            // 6. Find the camera root (XR Origin)
            SitInsideCar();

            // 7. Snap car to road start (commented out to support grass start sequence)
            // PlaceCarAtRoadStart();

            // 8. Find interior controls
            FindInteriorControls();
        }

        private void FindOrCreateCar()
        {
            var builder = FindFirstObjectByType<Vehicle.VRCockpitBuilder>();
            if (builder != null) { _car = builder.transform; return; }

            var go = GameObject.Find("VRCar");
            if (go != null) { _car = go.transform; return; }

            // No VRCar found — create a minimal empty one (no body, cockpit only)
            _car = new GameObject("VRCar").transform;
            Debug.Log("[StraightLineDriver] Created empty VRCar (cockpit-only mode).");
        }

        /// <summary>
        /// Destroys any external car body parts that may have been built by
        /// BuildCarShell() in a previous session. Leaves Cockpit_Root intact.
        /// </summary>
        private void CleanupExternalBody()
        {
            // Names of parts created by the old BuildCarShell() method
            var bodyPartNames = new System.Collections.Generic.HashSet<string>
            {
                "Body", "Roof", "Hood", "Trunk",
                "Bumper_F", "Bumper_R",
                "Windshield", "RearGlass", "Glass_L", "Glass_R",
                "WheelFL", "WheelFR", "WheelRL", "WheelRR",
                "HeadL", "HeadR", "TailL", "TailR"
            };

            int removed = 0;
            // Check direct children of VRCar
            foreach (Transform child in _car)
            {
                if (bodyPartNames.Contains(child.name))
                {
                    Destroy(child.gameObject);
                    removed++;
                }
            }

            if (removed > 0)
                Debug.Log($"[StraightLineDriver] Removed {removed} external body parts. Cockpit-only mode active.");
        }

        private void EnsureCockpit()
        {
            if (_car == null) return;
            // AddComponent triggers Awake immediately — cockpit is built synchronously
            if (_car.GetComponent<Vehicle.VRCockpitBuilder>() == null)
                _car.gameObject.AddComponent<Vehicle.VRCockpitBuilder>();
        }

        private void SitInsideCar()
        {
            // Walk up from Main Camera, but STOP at the direct child of VRCar
            // so _xrRoot = XR Origin (not VRCar itself)
            _xrRoot = transform;
            while (_xrRoot.parent != null && _xrRoot.parent != _car)
                _xrRoot = _xrRoot.parent;

            // Neutralize intermediate offsets to ensure camera matches _xrRoot exactly in Start
            Transform curr = transform;
            while (curr != null && curr != _xrRoot)
            {
                curr.localPosition = Vector3.zero;
                curr.localRotation = Quaternion.identity;
                curr = curr.parent;
            }

            Debug.Log($"[StraightLineDriver] Camera root = '{_xrRoot.name}' " +
                      $"(parent: '{_xrRoot.parent?.name ?? "none"}'). Neutralized intermediate offsets.");
        }

        /// <summary>
        /// EVERY frame: force XR Origin to sit exactly at driver eye position
        /// inside the car. Works whether XR Origin is parented inside car or not.
        /// </summary>
        private void LateUpdate()
        {
            if (_car == null || _xrRoot == null) return;

            // Force intermediate offsets to zero every frame to override tracking template defaults
            Transform curr = transform;
            while (curr != null && curr != _xrRoot)
            {
                curr.localPosition = Vector3.zero;
                curr.localRotation = Quaternion.identity;
                curr = curr.parent;
            }

            if (_xrRoot.parent == _car)
            {
                // XR Origin is a direct child of VRCar — use local position (fast, no rounding)
                _xrRoot.localPosition = driverEyeLocalPos;
                _xrRoot.localRotation = Quaternion.identity;
            }
            else
            {
                // XR Origin is a scene root — set world position to match car
                _xrRoot.position = _car.TransformPoint(driverEyeLocalPos);
                _xrRoot.rotation = Quaternion.Euler(0f, _car.eulerAngles.y, 0f);
            }
        }

        private void PlaceCarAtRoadStart()
        {
            if (worldBuilder == null) return;
            Vector3 startPos = worldBuilder.GetRoadPosition(_z);
            Vector3 tan      = worldBuilder.GetRoadTangent(_z);
            _car.position    = startPos;
            if (tan.sqrMagnitude > 0.01f)
                _car.rotation = Quaternion.LookRotation(tan, Vector3.up);

            // Initial ground snap
            SnapToGround(instant: true);
        }

        private void FindInteriorControls()
        {
            if (_car == null) return;
            _steeringWheelPivot = FindDeep(_car, "SteeringWheel_Pivot");
            _accelPedalPivot    = FindDeep(_car, "AccelPedal_Pivot");
            _brakePedalPivot    = FindDeep(_car, "BrakePedal_Pivot");

            if (_steeringWheelPivot) Debug.Log("[StraightLineDriver] Steering wheel found.");
            if (_accelPedalPivot)    Debug.Log("[StraightLineDriver] Accelerator pedal found.");
            if (_brakePedalPivot)    Debug.Log("[StraightLineDriver] Brake pedal found.");
        }

        // ── Main Update ───────────────────────────────────────────────────────

        private void Update()
        {
            ReadInput();
            UpdateHitState();

            if (!_paused && _hitState == HitState.None)
                DriveUpdate();

            SnapToGround(instant: false);
            UpdateSteeringWheel();
            UpdatePedals();
            MouseLook();
        }

        private void ReadInput()
        {
            if (_hitState != HitState.None) return; // no input during tree hit

            // Refresh XR Controller devices
            if (!_leftHandDevice.isValid) _leftHandDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (!_rightHandDevice.isValid) _rightHandDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);

            float kbThrottle = 0f;
            float kbBrake = 0f;
            float kbSteer = 0f;

            var kb = Keyboard.current;
            if (kb != null)
            {
                // Throttle
                kbThrottle = (kb.wKey.isPressed || kb.upArrowKey.isPressed) ? 1f : 0f;

                // Brake
                kbBrake = (kb.sKey.isPressed || kb.downArrowKey.isPressed) ? 1f : 0f;

                // Steer
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  kbSteer -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) kbSteer += 1f;

                // Reverse toggle
                if (kb.rKey.wasPressedThisFrame)
                    _reverse = !_reverse;

                // Speed adjustment keyboard fallbacks (2 = 5 m/s, 3 = 10 m/s, 4 = 12 m/s, 5 = 15 m/s, 6 = 17 m/s, 7 = 20 m/s, 8 = 22 m/s, 9 = 25 m/s, 0 = 30 m/s)
                if (kb.digit2Key.wasPressedThisFrame) automaticSpeedKmh = 18f;
                if (kb.digit3Key.wasPressedThisFrame) automaticSpeedKmh = 36f;
                if (kb.digit4Key.wasPressedThisFrame) automaticSpeedKmh = 43.2f;
                if (kb.digit5Key.wasPressedThisFrame) automaticSpeedKmh = 54f;
                if (kb.digit6Key.wasPressedThisFrame) automaticSpeedKmh = 61.2f;
                if (kb.digit7Key.wasPressedThisFrame) automaticSpeedKmh = 72f;
                if (kb.digit8Key.wasPressedThisFrame) automaticSpeedKmh = 79.2f;
                if (kb.digit9Key.wasPressedThisFrame) automaticSpeedKmh = 90f;
                if (kb.digit0Key.wasPressedThisFrame) automaticSpeedKmh = 108f;

                // Pause (ignored during intro splash screen to allow Spacebar to align vehicle)
                if (kb.spaceKey.wasPressedThisFrame)
                {
                    if (SpeedLessonManager.Instance == null || SpeedLessonManager.Instance.currentState != SpeedLessonManager.LessonState.IntroSplash)
                    {
                        _paused = !_paused;
                    }
                }
            }

            // VR Input
            float vrThrottle = 0f;
            float vrBrake = 0f;
            float vrSteer = 0f;

            if (_rightHandDevice.isValid)
            {
                _rightHandDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out vrThrottle);
                Vector2 stick;
                if (_rightHandDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out stick))
                {
                    if (Mathf.Abs(stick.x) > 0.05f) vrSteer = stick.x;
                }
            }
            if (_leftHandDevice.isValid)
            {
                _leftHandDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out vrBrake);
                Vector2 stick;
                if (_leftHandDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out stick))
                {
                    if (Mathf.Abs(stick.x) > 0.05f) vrSteer = stick.x;
                }
            }

            // Legacy Input System Fallback
            float legacyThrottle = 0f;
            float legacyBrake = 0f;
            float legacySteer = 0f;

            try
            {
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) legacyThrottle = 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) legacyBrake = 1f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) legacySteer -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) legacySteer += 1f;

                // Support standard Unity Virtual Input Axes as an ultimate fallback (immune to input action intercepts)
                float vert = Input.GetAxisRaw("Vertical");
                if (vert > 0.1f) legacyThrottle = Mathf.Max(legacyThrottle, vert);
                else if (vert < -0.1f) legacyBrake = Mathf.Max(legacyBrake, -vert);

                float horiz = Input.GetAxisRaw("Horizontal");
                if (Mathf.Abs(horiz) > 0.1f) legacySteer = horiz;

                if (Input.GetKeyDown(KeyCode.R))
                    _reverse = !_reverse;

                if (Input.GetKeyDown(KeyCode.Alpha2)) automaticSpeedKmh = 18f;
                if (Input.GetKeyDown(KeyCode.Alpha3)) automaticSpeedKmh = 36f;
                if (Input.GetKeyDown(KeyCode.Alpha4)) automaticSpeedKmh = 43.2f;
                if (Input.GetKeyDown(KeyCode.Alpha5)) automaticSpeedKmh = 54f;
                if (Input.GetKeyDown(KeyCode.Alpha6)) automaticSpeedKmh = 61.2f;
                if (Input.GetKeyDown(KeyCode.Alpha7)) automaticSpeedKmh = 72f;
                if (Input.GetKeyDown(KeyCode.Alpha8)) automaticSpeedKmh = 79.2f;
                if (Input.GetKeyDown(KeyCode.Alpha9)) automaticSpeedKmh = 90f;
                if (Input.GetKeyDown(KeyCode.Alpha0)) automaticSpeedKmh = 108f;

                if (Input.GetKeyDown(KeyCode.Space))
                {
                    if (SpeedLessonManager.Instance == null || SpeedLessonManager.Instance.currentState != SpeedLessonManager.LessonState.IntroSplash)
                    {
                        _paused = !_paused;
                    }
                }
            }
            catch { }

            _throttle = Mathf.Max(kbThrottle, vrThrottle, legacyThrottle);
            _brakeInput = Mathf.Max(kbBrake, vrBrake, legacyBrake);
            
            float combinedSteer = Mathf.Abs(kbSteer) > 0.01f ? kbSteer : vrSteer;
            _steerInput = Mathf.Abs(combinedSteer) > 0.01f ? combinedSteer : legacySteer;
        }

        // ── Driving ───────────────────────────────────────────────────────────

        private void DriveUpdate()
        {
            float dt = Time.deltaTime;

            // ── Speed update ──────────────────────────────────────────────────
            float maxKmh = _reverse ? reverseMaxKmh : maxSpeedKmh;

            if (automaticSpeedKmh.HasValue && (_throttle > 0.05f || _brakeInput > 0.05f))
            {
                automaticSpeedKmh = null;
            }

            if (automaticSpeedKmh.HasValue)
            {
                _speedKmh = Mathf.MoveTowards(_speedKmh, automaticSpeedKmh.Value, acceleration * 2f * dt);
            }
            else
            {
                if (_throttle > 0.01f)
                {
                    if (_speedKmh < 1f)
                    {
                        _speedKmh = 30f;
                    }
                    _speedKmh += _throttle * acceleration * dt;
                }
                else if (_brakeInput > 0.01f)
                    _speedKmh -= _brakeInput * brakeDecel * dt;
                else
                    _speedKmh -= rollOff * dt;
            }

            _speedKmh = Mathf.Clamp(_speedKmh, 0f, maxKmh);

            float speedMs = (_speedKmh / 3.6f) * (_reverse ? -1f : 1f);

            // ── Road tracker advance ──────────────────────────────────────────
            _z += speedMs * dt;
            _z  = Mathf.Clamp(_z, 0f, endZ);

            // ── Move car ─────────────────────────────────────────────────────
            if (_car == null) return;

            if (worldBuilder != null)
            {
                Vector3 roadPos = worldBuilder.GetRoadPosition(_z);
                Vector3 roadTan = worldBuilder.GetRoadTangent(_z);

                if (_reverse) roadTan = -roadTan;

                // Apply lateral steering offset
                float yawDelta     = _steerInput * carYawRate * dt;
                _car.Rotate(Vector3.up, yawDelta, Space.World);

                _car.position = Vector3.Lerp(_car.position, roadPos, dt * 8f);

                if (!_rmbHeld && _speedKmh > 0.5f)
                {
                    Quaternion target = Quaternion.LookRotation(roadTan, Vector3.up);
                    _car.rotation = Quaternion.Slerp(_car.rotation, target, dt * 4f);
                    _yaw = _car.eulerAngles.y;
                }
            }
        }

        // ── Ground Snap ───────────────────────────────────────────────────────

        private void SnapToGround(bool instant)
        {
            if (_car == null) return;

            Vector3 origin   = _car.position + Vector3.up * 3f;
            int     hitCount = Physics.RaycastNonAlloc(origin, Vector3.down,
                                                       _groundHits, groundRayDistance + 3f,
                                                       groundLayers,
                                                       QueryTriggerInteraction.Ignore);

            float  closestDist  = float.MaxValue;
            bool   found        = false;
            Vector3 groundPoint = Vector3.zero;

            for (int i = 0; i < hitCount; i++)
            {
                var h = _groundHits[i];
                if (h.transform == null) continue;
                if (h.transform.IsChildOf(_car)) continue;

                if (h.distance < closestDist)
                {
                    closestDist  = h.distance;
                    groundPoint  = h.point;
                    found        = true;
                }
            }

            if (!found) return;

            float targetY  = groundPoint.y + groundOffset;
            Vector3 pos    = _car.position;

            pos.y = instant
                ? targetY
                : Mathf.Lerp(pos.y, targetY, Time.deltaTime * 14f);

            _car.position = pos;
        }

        // ── Cockpit control animation ─────────────────────────────────────────

        private void UpdateSteeringWheel()
        {
            if (_steeringWheelPivot == null) return;

            // Smooth steering wheel angle
            float targetAngle = _steerInput * maxSteeringAngle;
            _wheelAngle = Mathf.Lerp(_wheelAngle, targetAngle, Time.deltaTime * steerSpeed);

            // Apply base rotation from VRCockpitBuilder (-90f, 0f, -180f) and steer rotation around the local Y axis
            _steeringWheelPivot.localRotation =
                Quaternion.Euler(-90f, 0f, -180f) * Quaternion.Euler(0f, -_wheelAngle, 0f);
        }

        private void UpdatePedals()
        {
            // Accelerator: pivot rotates forward when pressed
            if (_accelPedalPivot != null)
            {
                float target = _throttle * -22f;
                _accelPedalPivot.localRotation = Quaternion.Lerp(
                    _accelPedalPivot.localRotation,
                    Quaternion.Euler(target, 0f, 0f),
                    Time.deltaTime * 12f);
            }

            // Brake: similar
            if (_brakePedalPivot != null)
            {
                float target = _brakeInput * -18f;
                _brakePedalPivot.localRotation = Quaternion.Lerp(
                    _brakePedalPivot.localRotation,
                    Quaternion.Euler(target, 0f, 0f),
                    Time.deltaTime * 12f);
            }
        }

        // ── Tree Hit State Machine ────────────────────────────────────────────

        private void UpdateHitState()
        {
            if (_hitCooldown > 0f) { _hitCooldown -= Time.deltaTime; return; }
            if (_hitState == HitState.None) { CheckTreeAhead(); return; }

            _hitTimer -= Time.deltaTime;

            if (_hitState == HitState.Pausing)
            {
                _speedKmh = 0f;
                if (_hitTimer <= 0f)
                {
                    _hitState = HitState.Reversing;
                    _hitTimer = treeReverseDuration;
                }
            }
            else if (_hitState == HitState.Reversing)
            {
                _z -= treeReverseSpeed * Time.deltaTime;
                _z  = Mathf.Max(_z, 0f);

                if (_car != null && worldBuilder != null)
                {
                    Vector3 rp = worldBuilder.GetRoadPosition(_z);
                    _car.position = Vector3.Lerp(_car.position, rp, Time.deltaTime * 10f);
                }

                if (_hitTimer <= 0f)
                {
                    _speedKmh    = 0f;
                    _hitState    = HitState.None;
                    _hitCooldown = 2f;
                    _reverse     = false;
                }
            }
        }

        private void CheckTreeAhead()
        {
            if (_car == null) return;
            if (_speedKmh < 0.5f) return;

            Vector3 probe = _car.position + _car.forward * treeDetectionOffset + Vector3.up * 0.9f;
            int count = Physics.OverlapSphereNonAlloc(probe, treeDetectionRadius,
                                                      _overlapBuf, ~0,
                                                      QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var col = _overlapBuf[i];
                if (col == null) continue;
                if (col.transform.IsChildOf(_car)) continue;
                if (col.transform.root.name != "Oak") continue;

                _hitState = HitState.Pausing;
                _hitTimer = treePauseDuration;
                _speedKmh = 0f;
                StartCoroutine(ShakeCamera());
                return;
            }
        }

        private IEnumerator ShakeCamera()
        {
            Transform t = _xrRoot != null ? _xrRoot : transform;
            Vector3   o = t.localPosition;
            float     e = 0f;
            while (e < 0.3f)
            {
                e += Time.deltaTime;
                t.localPosition = o + Random.insideUnitSphere * 0.08f * (1f - e / 0.3f);
                yield return null;
            }
            t.localPosition = o;
        }

        // ── Mouse Look ────────────────────────────────────────────────────────

        private void MouseLook()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            _rmbHeld = mouse.rightButton.isPressed;
            if (!_rmbHeld) return;

            _yaw   += mouse.delta.x.ReadValue() * mouseSensitivity;
            _pitch -= mouse.delta.y.ReadValue() * mouseSensitivity;
            _pitch  = Mathf.Clamp(_pitch, -60f, 60f);

            if (_car != null)
                _car.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        // ── Car shell builder ─────────────────────────────────────────────────

        private GameObject BuildCarShell()
        {
            var root    = new GameObject("VRCar");
            var bodyMat = Mat(new Color(0.08f, 0.12f, 0.22f));
            var glass   = TransMat(new Color(0.4f, 0.65f, 0.9f, 0.28f));
            var wheel   = Mat(new Color(0.06f, 0.06f, 0.06f));
            var chrome  = Mat(new Color(0.82f, 0.82f, 0.85f));
            var head    = Mat(new Color(1f, 0.97f, 0.82f));
            var tail    = Mat(new Color(0.9f, 0.1f, 0.1f));

            Bx(root, bodyMat, new Vector3(0,     0.30f,  0),    new Vector3(1.85f, 0.55f, 4.4f));
            Bx(root, bodyMat, new Vector3(0,     0.88f, -0.25f), new Vector3(1.55f, 0.42f, 2.5f));
            Bx(root, bodyMat, new Vector3(0,     0.62f,  1.7f), new Vector3(1.75f, 0.07f, 1.1f), Quaternion.Euler(-10, 0, 0));
            Bx(root, bodyMat, new Vector3(0,     0.60f, -1.8f), new Vector3(1.75f, 0.07f, 0.8f), Quaternion.Euler(10, 0, 0));
            Bx(root, chrome,  new Vector3(0,     0.18f,  2.18f), new Vector3(1.75f, 0.22f, 0.08f));
            Bx(root, chrome,  new Vector3(0,     0.18f, -2.18f), new Vector3(1.75f, 0.22f, 0.08f));
            Bx(root, glass,   new Vector3(0,     0.82f,  0.95f), new Vector3(1.5f,  0.60f, 0.04f), Quaternion.Euler(-22, 0, 0));
            Bx(root, glass,   new Vector3(0,     0.80f, -1.45f), new Vector3(1.45f, 0.50f, 0.04f), Quaternion.Euler(22, 0, 0));
            Bx(root, glass,   new Vector3(-0.90f, 0.82f, -0.25f), new Vector3(0.04f, 0.44f, 1.6f));
            Bx(root, glass,   new Vector3( 0.90f, 0.82f, -0.25f), new Vector3(0.04f, 0.44f, 1.6f));
            Wh(root, wheel,   new Vector3(-0.96f, 0.34f,  1.35f));
            Wh(root, wheel,   new Vector3( 0.96f, 0.34f,  1.35f));
            Wh(root, wheel,   new Vector3(-0.96f, 0.34f, -1.35f));
            Wh(root, wheel,   new Vector3( 0.96f, 0.34f, -1.35f));
            Bx(root, head,    new Vector3(-0.58f, 0.52f,  2.21f), new Vector3(0.38f, 0.14f, 0.04f));
            Bx(root, head,    new Vector3( 0.58f, 0.52f,  2.21f), new Vector3(0.38f, 0.14f, 0.04f));
            Bx(root, tail,    new Vector3(-0.62f, 0.52f, -2.21f), new Vector3(0.42f, 0.14f, 0.04f));
            Bx(root, tail,    new Vector3( 0.62f, 0.52f, -2.21f), new Vector3(0.42f, 0.14f, 0.04f));
            return root;
        }

        private static Transform Bx(GameObject p, Material m, Vector3 pos, Vector3 scl,
                                     Quaternion? rot = null)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.transform.SetParent(p.transform, false);
            g.transform.localPosition = pos;
            g.transform.localScale    = scl;
            g.transform.localRotation = rot ?? Quaternion.identity;
            g.GetComponent<Renderer>().material = m;
            Object.Destroy(g.GetComponent<Collider>());
            return g.transform;
        }

        private static void Wh(GameObject p, Material m, Vector3 pos)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            g.transform.SetParent(p.transform, false);
            g.transform.localPosition = pos;
            g.transform.localScale    = new Vector3(0.68f, 0.17f, 0.68f);
            g.transform.localRotation = Quaternion.Euler(0, 0, 90);
            g.GetComponent<Renderer>().material = m;
            Object.Destroy(g.GetComponent<Collider>());
        }

        private static Material Mat(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            m.color = c;
            return m;
        }

        private static Material TransMat(Color c)
        {
            var m = Mat(c);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = 3000;
            return m;
        }

        // ── Tree collider helper ──────────────────────────────────────────────

        private void EnsureTreeColliders()
        {
            int n = 0;
            foreach (var go in GameObject.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (go.transform.parent == null || go.name != "Oak") continue;
                if (go.GetComponentInChildren<Collider>() != null) continue;
                var cap       = go.AddComponent<CapsuleCollider>();
                cap.direction = 1; cap.radius = 0.7f;
                cap.height    = 22f;
                cap.center    = new Vector3(0, 11f, 0);
                n++;
            }
            if (n > 0) Debug.Log($"[StraightLineDriver] Added colliders to {n} trees.");
        }

        // ── Utility ───────────────────────────────────────────────────────────

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        // ── HUD ───────────────────────────────────────────────────────────────

        private Texture2D CreateColorTexture(Color col)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, col);
            tex.Apply();
            return tex;
        }

        private Texture2D CreateSpeedometerFaceTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float outerRadius = size * 0.46f;
            float innerRadius = size * 0.38f;
            float ringRadius = size * 0.25f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    Color col = Color.clear;

                    // Center ring (purple/blue gradient)
                    if (dist >= ringRadius - 1.5f && dist <= ringRadius + 1.5f)
                    {
                        float angleRad = Mathf.Atan2(dy, dx);
                        float t = (angleRad + Mathf.PI) / (2f * Mathf.PI);
                        col = Color.Lerp(new Color(0.2f, 0.4f, 1f, 0.9f), new Color(0.8f, 0.2f, 1f, 0.9f), t);
                    }
                    // Speed dial ticks
                    else if (dist >= innerRadius && dist <= outerRadius)
                    {
                        float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                        if (angle < 0) angle += 360f;

                        // Dial arc starting at 45 to 315 deg (270 degrees sweep)
                        if (angle >= 45f && angle <= 315f)
                        {
                            float modulo = angle % 4.5f;
                            if (modulo < 0.8f || modulo > 3.7f)
                            {
                                col = new Color(0.15f, 0.55f, 1f, 0.9f); // Neon blue-cyan ticks
                            }
                        }
                    }
                    // Outer border arc
                    else if (dist >= outerRadius + 2f && dist <= outerRadius + 5f)
                    {
                        float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                        if (angle < 0) angle += 360f;

                        if (angle >= 40f && angle <= 320f)
                        {
                            col = new Color(0.5f, 0.5f, 0.5f, 0.75f); // Gray gauge frame
                        }
                    }

                    tex.SetPixel(x, y, col);
                }
            }
            tex.Apply();
            return tex;
        }

        private Texture2D CreateCircleTexture(int size, Color col)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float radius = size * 0.46f;
            float border = size * 0.40f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    Color pixelCol = Color.clear;
                    if (dist <= border)
                    {
                        pixelCol = col;
                    }
                    else if (dist <= radius)
                    {
                        pixelCol = new Color(col.r * 0.8f, col.g * 0.8f, col.b * 0.8f, 1f);
                    }

                    tex.SetPixel(x, y, pixelCol);
                }
            }
            tex.Apply();
            return tex;
        }

        private void DrawHUDPlate(Rect rect, string value, string icon)
        {
            // Draw background plate
            GUI.DrawTexture(rect, _hudPlateTex);

            // Draw circular badge on the right
            float badgeSize = rect.height - 6f;
            Rect badgeRect = new Rect(rect.xMax - badgeSize - 3f, rect.y + 3f, badgeSize, badgeSize);
            GUI.DrawTexture(badgeRect, _circleBadgeTex);

            // Draw icon inside badge
            var iconStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.black }
            };
            GUI.Label(badgeRect, icon, iconStyle);

            // Draw value text on the left
            var valStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.BoldAndItalic,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(1f, 0.78f, 0.05f, 1f) }
            };
            Rect textRect = new Rect(rect.x + 10f, rect.y, rect.width - badgeSize - 20f, rect.height);
            GUI.Label(textRect, value, valStyle);
        }

        private void InitializeHUDStyles()
        {
            if (_stylesInitialized) return;

            _progressStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 44,
                fontStyle = FontStyle.BoldAndItalic,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };

            _progressLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.BoldAndItalic,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f, 0.7f) }
            };

            _timeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.BoldAndItalic,
                normal = { textColor = new Color(1f, 1f, 1f, 0.85f) }
            };

            _posStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 44,
                fontStyle = FontStyle.BoldAndItalic,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = Color.white }
            };

            _posLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.BoldAndItalic,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f, 0.7f) }
            };

            _speedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 68,
                fontStyle = FontStyle.BoldAndItalic,
                alignment = TextAnchor.MiddleRight,
                clipping = TextClipping.Overflow,
                normal = { textColor = Color.white }
            };

            _speedLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.BoldAndItalic,
                alignment = TextAnchor.MiddleRight,
                richText = true,
                clipping = TextClipping.Overflow,
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f, 0.8f) }
            };

            _gearBgTex = CreateColorTexture(new Color(0.04f, 0.16f, 0.05f, 0.9f));
            _gearStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 28,
                fontStyle = FontStyle.BoldAndItalic,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(1, 1, 1, 1),
                normal = {
                    textColor = new Color(0.2f, 1.0f, 0.4f, 1.0f),
                    background = _gearBgTex
                }
            };

            if (_speedometerFaceTex == null)
                _speedometerFaceTex = CreateSpeedometerFaceTexture(240);

            if (_circleBadgeTex == null)
                _circleBadgeTex = CreateCircleTexture(32, new Color(0.9f, 0.9f, 0.9f, 0.95f));

            if (_hudPlateTex == null)
                _hudPlateTex = CreateColorTexture(new Color(0.05f, 0.05f, 0.05f, 0.65f));

            if (_pedalBgTex == null)
                _pedalBgTex = CreateColorTexture(new Color(0.12f, 0.12f, 0.12f, 0.8f));

            if (_pedalFillBrakeTex == null)
                _pedalFillBrakeTex = CreateColorTexture(new Color(0.85f, 0.15f, 0.15f, 0.9f));

            if (_pedalFillAccelTex == null)
                _pedalFillAccelTex = CreateColorTexture(new Color(0.15f, 0.85f, 0.15f, 0.9f));

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitializeHUDStyles();

            bool showTelemetry = true;
            bool showTopRightStats = true;

            if (SpeedLessonManager.Instance != null)
            {
                var state = SpeedLessonManager.Instance.currentState;
                showTelemetry = (state == SpeedLessonManager.LessonState.MissionActive || state == SpeedLessonManager.LessonState.Completed);
                showTopRightStats = (state != SpeedLessonManager.LessonState.IntroSplash);
            }

            // Calculate progress percent
            float pct = Mathf.Clamp01(_z / endZ) * 100f;

            // Format Timer: minutes:seconds
            float elapsed = Time.timeSinceLevelLoad;
            int mins = Mathf.FloorToInt(elapsed / 60f);
            int secs = Mathf.FloorToInt(elapsed % 60f);
            string timeStr = string.Format("{0:00}:{1:00}", mins, secs);

            // Determine gear and state
            string gearStr = "1";
            if (_reverse) gearStr = "R";
            else if (_speedKmh < 0.5f) gearStr = "N";
            else
            {
                int gearNum = Mathf.Clamp(Mathf.FloorToInt(_speedKmh / 22f) + 1, 1, 6);
                gearStr = gearNum.ToString();
            }

            string impactState = _hitState == HitState.Pausing ? "  IMPACT!"
                               : _hitState == HitState.Reversing ? "  REVERSING"
                               : _paused ? "  PAUSED"
                               : "";

            // 1. Pause Button (Top-Left)
            if (showTelemetry)
            {
                GUI.Box(_pauseButtonRect, "", _gearStyle);
                var pauseIconStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
                GUI.Label(_pauseButtonRect, "||", pauseIconStyle);

                if (Event.current.type == EventType.MouseDown && _pauseButtonRect.Contains(Event.current.mousePosition))
                {
                    if (SpeedLessonManager.Instance == null || SpeedLessonManager.Instance.currentState != SpeedLessonManager.LessonState.IntroSplash)
                    {
                        _paused = !_paused;
                    }
                    Event.current.Use();
                }
            }

            float speedMsVal = _speedKmh / 3.6f;

            // 2. Stats Display Plates (Top-Right)
            if (showTopRightStats)
            {
                int scoreVal = 0;
                if (SpeedLessonManager.Instance != null)
                {
                    scoreVal = SpeedLessonManager.Instance.TotalScore;
                }

                float startY = 25f;
                float panelWidth = 190f;
                float panelHeight = 35f;
                float spacing = 8f;
                float startX = Screen.width - panelWidth - 25f;

                // Row 1: Score
                Rect scoreRect = new Rect(startX, startY, panelWidth, panelHeight);
                DrawHUDPlate(scoreRect, $"{scoreVal} PTS", "🏆");

                // Row 2: Distance
                Rect distRect = new Rect(startX, startY + panelHeight + spacing, panelWidth, panelHeight);
                DrawHUDPlate(distRect, $"{Mathf.RoundToInt(_z)} M ({pct:F0}%)", "🏁");

                // Row 3: Time
                Rect timeRect = new Rect(startX, startY + (panelHeight + spacing) * 2f, panelWidth, panelHeight);
                DrawHUDPlate(timeRect, timeStr, "⏱️");

                // Row 4: Speed
                Rect speedRect = new Rect(startX, startY + (panelHeight + spacing) * 3f, panelWidth, panelHeight);
                DrawHUDPlate(speedRect, $"{speedMsVal:F1} M/S", "⚡");
            }

            if (showTelemetry)
            {
                // 3. Neon Circular Dial Speedometer (Bottom-Center)
                Rect faceRect = new Rect((Screen.width - 240f) / 2f, Screen.height - 265f, 240f, 240f);
                GUI.DrawTexture(faceRect, _speedometerFaceTex);

                // Rotating Needle
                Vector2 speedoCenter = faceRect.center;
                float maxSpeedMs = 120f / 3.6f; // Max speed range 120 km/h (33.33 m/s)
                float tSpeed = Mathf.Clamp01(speedMsVal / maxSpeedMs);
                float needleAngle = Mathf.Lerp(-135f, 135f, tSpeed);

                // Draw dial number labels (0 to 120 at 45 degree intervals)
                var dialLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.7f, 0.9f, 1f, 0.85f) }
                };

                float[] dialValues = { 0, 20, 40, 60, 80, 100, 120 };
                float labelRadius = 96f; // Positioned perfectly inside ticks

                for (int i = 0; i < dialValues.Length; i++)
                {
                    float angle = -135f + (i * 45f);
                    float angleRad = (angle - 90f) * Mathf.Deg2Rad;
                    float lx = speedoCenter.x + labelRadius * Mathf.Cos(angleRad);
                    float ly = speedoCenter.y + labelRadius * Mathf.Sin(angleRad);
                    GUI.Label(new Rect(lx - 20f, ly - 10f, 40f, 20f), dialValues[i].ToString(), dialLabelStyle);
                }

                Matrix4x4 origMatrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(needleAngle, speedoCenter);
                
                // Draw a thin neon pink vertical line from the center upwards
                Rect needleRect = new Rect(speedoCenter.x - 2.5f, speedoCenter.y - 100f, 5f, 100f);
                Color origColor = GUI.color;
                GUI.color = new Color(1f, 0.2f, 0.6f, 0.95f); // Neon pink
                GUI.DrawTexture(needleRect, Texture2D.whiteTexture);
                GUI.color = origColor;
                
                GUI.matrix = origMatrix;

                // Draw gear text in the center of the ring
                var centerGearStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 32,
                    fontStyle = FontStyle.BoldAndItalic,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.2f, 1.0f, 0.4f, 1.0f) }
                };
                GUI.Label(new Rect(speedoCenter.x - 30f, speedoCenter.y - 20f, 60f, 40f), gearStr, centerGearStyle);

                // Draw digital speed readout at the bottom center of the circular dial
                var neonDigitalStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 24,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    richText = true
                };
                string neonSpeedText = $"<color=#FFFF33>{Mathf.RoundToInt(speedMsVal)}</color> <color=#FF3366>m/s</color>";
                GUI.Label(new Rect(faceRect.x, faceRect.yMax - 40f, faceRect.width, 35f), neonSpeedText, neonDigitalStyle);

                // 4. Pedals Overlay (Bottom-Right corner)
                float pedalAreaWidth = 100f;
                float pedalAreaHeight = 90f;
                float pedalStartX = Screen.width - 145f;
                float pedalStartY = Screen.height - 110f;

                // Brake pedal (Left)
                float brakeWidth = 32f;
                float brakeHeight = 60f;
                Rect brakeBgRect = new Rect(pedalStartX, pedalStartY + (pedalAreaHeight - brakeHeight), brakeWidth, brakeHeight);
                GUI.DrawTexture(brakeBgRect, _pedalBgTex);

                float brakeFillHeight = brakeHeight * _brakeInput;
                Rect brakeFillRect = new Rect(brakeBgRect.x, brakeBgRect.yMax - brakeFillHeight, brakeBgRect.width, brakeFillHeight);
                if (brakeFillHeight > 1f)
                {
                    GUI.DrawTexture(brakeFillRect, _pedalFillBrakeTex);
                }

                var pedalLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
                };
                GUI.Label(new Rect(brakeBgRect.x, brakeBgRect.yMax + 2f, brakeBgRect.width, 18f), "BRAKE", pedalLabelStyle);

                // Accelerator pedal (Right)
                float accelWidth = 24f;
                float accelHeight = 78f;
                Rect accelBgRect = new Rect(pedalStartX + brakeWidth + 18f, pedalStartY + (pedalAreaHeight - accelHeight), accelWidth, accelHeight);
                GUI.DrawTexture(accelBgRect, _pedalBgTex);

                float accelFillHeight = accelHeight * _throttle;
                Rect accelFillRect = new Rect(accelBgRect.x, accelBgRect.yMax - accelFillHeight, accelBgRect.width, accelFillHeight);
                if (accelFillHeight > 1f)
                {
                    GUI.DrawTexture(accelFillRect, _pedalFillAccelTex);
                }

                GUI.Label(new Rect(accelBgRect.x - 5f, accelBgRect.yMax + 2f, accelBgRect.width + 10f, 18f), "GAS", pedalLabelStyle);

                // 5. Driving controls helper
                var helperStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Normal,
                    normal = { textColor = new Color(0.7f, 0.7f, 0.7f, 0.6f) }
                };
                GUI.Label(new Rect(25, Screen.height - 35, 600, 25), "[W/S] DRIVE  [A/D] STEER  [R] REVERSE  [SPACE] PAUSE  [LMB] RAYCAST POINTER", helperStyle);
            }
        }

        private void OnDestroy()
        {
            if (_gearBgTex != null) Destroy(_gearBgTex);
            if (_speedometerFaceTex != null) Destroy(_speedometerFaceTex);
            if (_circleBadgeTex != null) Destroy(_circleBadgeTex);
            if (_hudPlateTex != null) Destroy(_hudPlateTex);
            if (_pedalBgTex != null) Destroy(_pedalBgTex);
            if (_pedalFillBrakeTex != null) Destroy(_pedalFillBrakeTex);
            if (_pedalFillAccelTex != null) Destroy(_pedalFillAccelTex);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_car == null) return;
            Vector3 probe = _car.position + _car.forward * treeDetectionOffset + Vector3.up;
            Gizmos.color = _hitState != HitState.None ? Color.red : Color.yellow;
            Gizmos.DrawWireSphere(probe, treeDetectionRadius);

            // Ground ray
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(_car.position + Vector3.up * 3f,
                            _car.position + Vector3.down * groundRayDistance);
        }
#endif
    }
}
