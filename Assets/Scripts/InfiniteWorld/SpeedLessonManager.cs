using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace InfiniteWorld
{
    /// <summary>
    /// Master GameManager that orchestrates the 5 interactive educational driving levels
    /// to teach the concept: Speed = Distance ÷ Time.
    /// </summary>
    public class SpeedLessonManager : MonoBehaviour
    {
        // ── Singleton instance ────────────────────────────────────────────────
        public static SpeedLessonManager Instance { get; private set; }

        // ── Lesson states ─────────────────────────────────────────────────────
        public enum LessonState
        {
            Classroom,
            IntroSplash,
            LevelSelection,
            Prediction,
            MissionActive,
            LevelCompletion,
            FinalResults,
            Completed
        }

        // ── Level definition structures ───────────────────────────────────────
        public struct LevelConfig
        {
            public float[] distances;
            public float[] times;
        }

        private readonly LevelConfig[] _levels = new LevelConfig[]
        {
            new LevelConfig { distances = new float[] { 300f, 400f, 500f }, times = new float[] { 20f, 25f, 30f } }, // Level 1
            new LevelConfig { distances = new float[] { 600f, 700f, 800f }, times = new float[] { 35f, 40f, 45f } }, // Level 2
            new LevelConfig { distances = new float[] { 900f, 1000f, 1100f }, times = new float[] { 50f, 55f, 60f } }, // Level 3
            new LevelConfig { distances = new float[] { 1200f, 1300f, 1400f }, times = new float[] { 65f, 70f, 75f } }, // Level 4
            new LevelConfig { distances = new float[] { 1500f, 1700f, 1900f }, times = new float[] { 80f, 90f, 100f } } // Level 5 (Emergency Delivery)
        };

        [Header("State Tracking")]
        public LessonState currentState = LessonState.Classroom;
        public int currentLevelIndex = 0; // 0 to 4

        // Colors
        private static readonly Color NeonCyan = new Color(0f, 0.85f, 1f, 0.8f);
        private static readonly Color NeonOrange = new Color(1f, 0.45f, 0f, 0.8f);
        private static readonly Color NeonGreen = new Color(0.1f, 0.95f, 0.2f, 0.8f);
        private static readonly Color NeonRed = new Color(1f, 0.1f, 0.2f, 0.8f);

        // Selection states
        private float _selectedDistance = 0f;
        private float _selectedTime = 0f;
        private float _requiredSpeedMs = 0f;
        private float _predictedSpeedMs = 0f;
        private bool _predictionCorrect = false;

        // Active driving variables
        private float _levelStartRawZ = 0f;
        private float _levelStartDistanceOffset = 0f;
        private float _missionTimer = 0f;
        private bool _isMissionRunning = false;
        private float _distanceCovered = 0f;

        // Scoring
        private int _totalScore = 0;
        public int TotalScore => _totalScore;
        private int _knowledgePoints = 0;
        private int _missionPoints = 0;
        private int _precisionPoints = 0;

        // Stats tracking
        private float _highestSpeedMaintained = 0f;
        private float _bestMissionTimeRelative = 9999f;
        private int _totalPrecisionRewardsCount = 0;
        private int _correctPredictionsCount = 0;
        private int _totalPredictionsCount = 0;
        private float _totalSpeedDriven = 0f;
        private int _speedReadingsCount = 0;

        // Precision timers
        private float _precisionZoneTimer = 0f;
        private float _deviationZoneTimer = 0f;
        private string _precisionStatusText = "STABILIZING SPEED...";

        // Status text overlay
        private string _temporaryStatusText = "";
        private float _temporaryStatusTimer = 0f;
        private Color _temporaryStatusColor = Color.white;

        // Ghost car variables
        public struct GhostFrame
        {
            public float time;
            public Vector3 position;
            public Quaternion rotation;
            public float speedMs;
        }

        private List<GhostFrame> _currentAttemptRecording = new List<GhostFrame>();
        private List<GhostFrame> _previousAttemptRecording = new List<GhostFrame>();
        private bool _isRetry = false;
        private GameObject _ghostCar;
        private bool _isReplayingGhost = false;

        // References
        private StraightLineDriver _driver;
        private VRHologramRaycaster _raycaster;
        private GameObject _hologramContainer;
        private readonly List<GameObject> _hudObjects = new List<GameObject>();
        private bool _markersSpawned = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            var driver = FindFirstObjectByType<StraightLineDriver>();
            if (driver == null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    driver = cam.gameObject.AddComponent<StraightLineDriver>();
                    driver.worldBuilder = FindFirstObjectByType<StaticWorldBuilder>();
                    Debug.Log("[SpeedLessonManager] Dynamically attached missing StraightLineDriver component to Main Camera.");
                }
            }

            if (driver != null)
            {
                driver.enabled = true;
                var gm = driver.gameObject.AddComponent<SpeedLessonManager>();
                Debug.Log("[SpeedLessonManager] Successfully auto-spawned and attached to player car.");
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            // Remove duplicate AudioListeners
            DisableDuplicateAudioListeners();
        }

        private void DisableDuplicateAudioListeners()
        {
            var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            if (listeners.Length > 1)
            {
                bool keptOne = false;
                foreach (var listener in listeners)
                {
                    if (!keptOne && (listener.gameObject.name == "Main Camera" || listener.gameObject.CompareTag("MainCamera")))
                    {
                        keptOne = true;
                        listener.enabled = true;
                        continue;
                    }
                    if (!keptOne)
                    {
                        keptOne = true;
                        listener.enabled = true;
                        continue;
                    }
                    listener.enabled = false;
                }
            }
        }

        private void Start()
        {
            _driver = GetComponent<StraightLineDriver>();
            if (_driver == null)
            {
                Debug.LogError("[SpeedLessonManager] StraightLineDriver not found on this GameObject!");
                return;
            }

            _raycaster = gameObject.AddComponent<VRHologramRaycaster>();
            _hologramContainer = new GameObject("Holograms_Root");

            // Kick off Classroom
            StartCoroutine(RunClassroomSequence());
        }

        private void Update()
        {
            if (!_markersSpawned && _driver != null && _driver.worldBuilder != null)
            {
                _markersSpawned = true;
                SpawnZMarkers();
            }

            // Update temporary status overlays
            if (_temporaryStatusTimer > 0f)
            {
                _temporaryStatusTimer -= Time.deltaTime;
                if (_temporaryStatusTimer <= 0f)
                {
                    _temporaryStatusText = "";
                }
            }

            if (_isMissionRunning)
            {
                float dt = Time.deltaTime;
                _missionTimer -= dt;

                // Wrap Z coordinate if it exceeds 4500m to keep road infinite and continuous
                if (_driver.Z > 4500f)
                {
                    float wrapOffset = 4000f;
                    _driver.Z -= wrapOffset;
                    _levelStartDistanceOffset += wrapOffset;
                    _driver.SnapCarToRoadStart();
                }

                _distanceCovered = (_driver.Z + _levelStartDistanceOffset) - _levelStartRawZ;

                // Track averages and stats
                float currentSpeedMs = _driver.SpeedKmh / 3.6f;
                _totalSpeedDriven += currentSpeedMs;
                _speedReadingsCount++;
                if (currentSpeedMs > _highestSpeedMaintained)
                {
                    _highestSpeedMaintained = currentSpeedMs;
                }

                // Record current attempt frame
                _currentAttemptRecording.Add(new GhostFrame
                {
                    time = _selectedTime - _missionTimer,
                    position = _driver.Car.position,
                    rotation = _driver.Car.rotation,
                    speedMs = currentSpeedMs
                });

                // Update Ghost Car playback
                if (_isReplayingGhost && _previousAttemptRecording.Count > 0 && _ghostCar != null)
                {
                    UpdateGhostCar(_selectedTime - _missionTimer);
                }

                // Precision Speed Management System
                float speedDiff = Mathf.Abs(currentSpeedMs - _requiredSpeedMs);

                // Tolerance Zone (+/- 1.5 m/s)
                if (speedDiff <= 1.5f)
                {
                    _precisionZoneTimer += dt;
                    _deviationZoneTimer = 0f;
                    _precisionStatusText = $"IN TOLERANCE ZONE: {_precisionZoneTimer:F1}s / 5.0s";

                    if (_precisionZoneTimer >= 5.0f)
                    {
                        _precisionZoneTimer = 0f;
                        _precisionPoints += 10;
                        _totalScore += 10;
                        _totalPrecisionRewardsCount++;
                        StartCoroutine(ShowTemporaryStatusText("PERFECT SPEED CONTROL (+10)", NeonGreen));
                    }
                }
                // Deviation Zone (4.0 m/s above or below)
                else if (speedDiff >= 4.0f)
                {
                    _deviationZoneTimer += dt;
                    _precisionZoneTimer = 0f;
                    _precisionStatusText = $"SPEED DEVIATION TIMER: {_deviationZoneTimer:F1}s / 5.0s";

                    if (_deviationZoneTimer >= 5.0f)
                    {
                        _deviationZoneTimer = 0f;
                        _precisionPoints -= 5;
                        _totalScore -= 5;
                        StartCoroutine(ShowTemporaryStatusText("SPEED DEVIATION PENALTY (-5)", NeonRed));
                    }
                }
                else
                {
                    _precisionZoneTimer = 0f;
                    _deviationZoneTimer = 0f;
                    _precisionStatusText = "STABILIZING SPEED...";
                }

                // Update windshield HUD
                UpdateActiveHUD();

                // Success condition
                if (_distanceCovered >= _selectedDistance)
                {
                    CompleteLevel(success: true);
                }
                // Failure condition
                else if (_missionTimer <= 0f)
                {
                    CompleteLevel(success: false);
                }
            }
        }

        private IEnumerator ShowTemporaryStatusText(string text, Color color)
        {
            _temporaryStatusText = text.ToUpper();
            _temporaryStatusColor = color;
            _temporaryStatusTimer = 2.5f;
            yield return null;
        }

        private void UpdateGhostCar(float elapsed)
        {
            if (_previousAttemptRecording == null || _previousAttemptRecording.Count == 0 || _ghostCar == null)
                return;

            int index = 0;
            while (index < _previousAttemptRecording.Count - 1 && _previousAttemptRecording[index + 1].time < elapsed)
            {
                index++;
            }

            if (index >= _previousAttemptRecording.Count - 1)
            {
                var lastFrame = _previousAttemptRecording[_previousAttemptRecording.Count - 1];
                // Smoothly snap to final recorded position
                Vector3 rotRight = lastFrame.rotation * Vector3.right;
                _ghostCar.transform.position = Vector3.Lerp(_ghostCar.transform.position, lastFrame.position + rotRight * -2.5f, Time.deltaTime * 5f);
                _ghostCar.transform.rotation = Quaternion.Slerp(_ghostCar.transform.rotation, lastFrame.rotation, Time.deltaTime * 5f);
            }
            else
            {
                var f0 = _previousAttemptRecording[index];
                var f1 = _previousAttemptRecording[index + 1];
                float t = (elapsed - f0.time) / (f1.time - f0.time);

                Vector3 pos = Vector3.Lerp(f0.position, f1.position, t);
                Quaternion rot = Quaternion.Slerp(f0.rotation, f1.rotation, t);

                // Offset laterally to prevent overlapping with player car
                Vector3 offset = rot * Vector3.right * -2.5f;
                _ghostCar.transform.position = pos + offset;
                _ghostCar.transform.rotation = rot;
            }
        }

        private GameObject CreateRoomPart(GameObject parent, string name, Vector3 pos, Vector3 size, Color col)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;

            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = col;
            if (name == "Floor")
            {
                mat.SetFloat("_Smoothness", 0.2f);
            }
            mr.sharedMaterial = mat;
            return go;
        }

        private IEnumerator RunClassroomSequence()
        {
            currentState = LessonState.Classroom;
            _driver.Paused = true;
            _driver.automaticSpeedKmh = 0f;

            // Wait a frame to ensure all Start() methods (like VRCockpitBuilder) have run and instantiated the steering wheel
            yield return null;

            // Find and hide steering wheel during classroom phase
            Transform wheelPivot = null;
            if (_driver != null)
            {
                if (_driver.Car != null)
                {
                    wheelPivot = _driver.Car.Find("SteeringWheel_Pivot");
                }
                if (wheelPivot == null)
                {
                    wheelPivot = _driver.transform.Find("SteeringWheel_Pivot");
                }
                if (wheelPivot == null)
                {
                    var allTransforms = _driver.gameObject.GetComponentsInChildren<Transform>(true);
                    foreach (var t in allTransforms)
                    {
                        if (t.name == "SteeringWheel_Pivot")
                        {
                            wheelPivot = t;
                            break;
                        }
                    }
                }

                if (wheelPivot != null)
                {
                    wheelPivot.gameObject.SetActive(false);
                    Debug.Log("[SpeedLessonManager] Steering wheel hidden during classroom phase.");
                }
            }

            var classroomContainer = new GameObject("ClassroomContainer");
            classroomContainer.transform.position = new Vector3(0f, -200f, 0f);

            Color wallCol = new Color(0.92f, 0.90f, 0.84f); // Warm beige
            Color floorCol = new Color(0.24f, 0.18f, 0.12f); // Dark wood
            Color ceilingCol = new Color(0.95f, 0.95f, 0.95f); // Soft white

            // Floor (Y = 0f local)
            CreateRoomPart(classroomContainer, "Floor", new Vector3(0f, 0f, 0f), new Vector3(12f, 0.2f, 10f), floorCol);
            // Ceiling (Y = 5f local)
            CreateRoomPart(classroomContainer, "Ceiling", new Vector3(0f, 5f, 0f), new Vector3(12f, 0.2f, 10f), ceilingCol);
            // Front wall (Z = 5f local)
            CreateRoomPart(classroomContainer, "FrontWall", new Vector3(0f, 2.5f, 5f), new Vector3(12f, 5f, 0.2f), wallCol);
            // Back wall (Z = -5f local)
            CreateRoomPart(classroomContainer, "BackWall", new Vector3(0f, 2.5f, -5f), new Vector3(12f, 5f, 0.2f), wallCol);
            // Left wall (X = -6f local)
            CreateRoomPart(classroomContainer, "LeftWall", new Vector3(-6f, 2.5f, 0f), new Vector3(0.2f, 5f, 10f), wallCol);
            // Right wall (X = 6f local)
            CreateRoomPart(classroomContainer, "RightWall", new Vector3(6f, 2.5f, 0f), new Vector3(0.2f, 5f, 10f), wallCol);

            // Light
            GameObject lightGo = new GameObject("ClassroomLight");
            lightGo.transform.SetParent(classroomContainer.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 4.5f, 0f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 25f;
            light.intensity = 3.5f;
            light.color = new Color(1f, 0.96f, 0.88f);

            // Instantiate blackboard FBX or fallback
            GameObject blackboardGo = null;
#if UNITY_EDITOR
            GameObject blackboardPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/uploads_files_1953209_Blackboard/blackboard.fbx");
            if (blackboardPrefab != null)
            {
                blackboardGo = Instantiate(blackboardPrefab, classroomContainer.transform);
                blackboardGo.name = "BlackboardFBX";
                blackboardGo.transform.localPosition = new Vector3(0f, 1.8f, 4.2f);
                blackboardGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                blackboardGo.transform.localScale = Vector3.one * 0.8f; // Scaled down to prevent blocking full screen

                Material blackboardMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                Texture2D albedo = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/uploads_files_1953209_Blackboard/blackboard_low_Material.003_AlbedoTransparency.png");
                Texture2D metallic = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/uploads_files_1953209_Blackboard/blackboard_low_Material.003_MetallicSmoothness.png");
                Texture2D normal = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/uploads_files_1953209_Blackboard/blackboard_low_Material.003_Normal.png");

                if (albedo != null) blackboardMat.mainTexture = albedo;
                if (metallic != null)
                {
                    blackboardMat.SetTexture("_MetallicGlossMap", metallic);
                    blackboardMat.EnableKeyword("_METALLICGLOSSMAP");
                    blackboardMat.SetFloat("_Metallic", 1.0f);
                }
                if (normal != null)
                {
                    blackboardMat.SetTexture("_BumpMap", normal);
                    blackboardMat.EnableKeyword("_NORMALMAP");
                }

                foreach (var r in blackboardGo.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterial = blackboardMat;
                    if (r.GetComponent<Collider>() == null)
                    {
                        r.gameObject.AddComponent<BoxCollider>();
                    }
                }
            }
#endif

            if (blackboardGo == null)
            {
                blackboardGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blackboardGo.name = "FallbackBlackboard";
                blackboardGo.transform.SetParent(classroomContainer.transform, false);
                blackboardGo.transform.localPosition = new Vector3(0f, 1.8f, 4.2f);
                blackboardGo.transform.localScale = new Vector3(4.5f, 2.2f, 0.1f);
                
                var mr = blackboardGo.GetComponent<MeshRenderer>();
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = new Color(0.12f, 0.28f, 0.16f);
                mr.sharedMaterial = mat;

                var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frame.name = "Frame";
                frame.transform.SetParent(blackboardGo.transform, false);
                frame.transform.localPosition = Vector3.zero;
                frame.transform.localScale = new Vector3(1.02f, 1.04f, 1.2f);
                
                var frameMr = frame.GetComponent<MeshRenderer>();
                var frameMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                frameMat.color = new Color(0.38f, 0.22f, 0.12f);
                frameMr.sharedMaterial = frameMat;
                Destroy(frame.GetComponent<Collider>());
            }

            // Instantiate teacher character next to the blackboard (scaled appropriately for inches-to-meters)
            GameObject teacherGo = null;
#if UNITY_EDITOR
            GameObject teacherPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/man/source/man/man.obj");
            if (teacherPrefab != null)
            {
                teacherGo = Instantiate(teacherPrefab, classroomContainer.transform);
                teacherGo.name = "Teacher";
                teacherGo.transform.localPosition = new Vector3(-1.8f, 0.0f, 3.8f);
                teacherGo.transform.localRotation = Quaternion.Euler(0f, 150f, 0f); // Face slightly towards the student
                teacherGo.transform.localScale = Vector3.one * 0.0254f; // Convert inches to meters for 1.8m height

                Material teacherMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                Texture2D teacherTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/man/source/man/textures/00208_Quint009_Diffuse.JPG");
                if (teacherTex == null)
                {
                    teacherTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/man/textures/00208_Quint009_Diffuse.jpeg");
                }

                if (teacherTex != null)
                {
                    teacherMat.mainTexture = teacherTex;
                }
                else
                {
                    teacherMat.color = new Color(0.8f, 0.7f, 0.6f);
                }
                teacherMat.SetFloat("_Smoothness", 0.05f);

                foreach (var r in teacherGo.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterial = teacherMat;
                }
                Debug.Log("[SpeedLessonManager] Instantiated teacher character model next to the blackboard.");
            }
#endif

            // Blackboard text
            var boardTextGo = new GameObject("BlackboardText");
            boardTextGo.transform.SetParent(classroomContainer.transform, false);
            
            if (blackboardGo.name == "BlackboardFBX")
            {
                boardTextGo.transform.SetParent(blackboardGo.transform, false);
                boardTextGo.transform.localPosition = new Vector3(-1.6f, 0.7f, -0.05f);
                boardTextGo.transform.localScale = Vector3.one * 0.009f;
            }
            else
            {
                boardTextGo.transform.localPosition = new Vector3(-1.9f, 2.6f, 4.1f);
                boardTextGo.transform.localScale = Vector3.one * 0.007f;
            }

            var tm = boardTextGo.AddComponent<TextMesh>();
            Font builtinFont = GetSafeBuiltinFont();
            if (builtinFont != null)
            {
                tm.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = Color.white;
                boardTextGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }

            tm.fontSize = 72;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.UpperLeft;
            tm.alignment = TextAlignment.Left;
            tm.color = Color.white;

            // Instantiate chalk piece
            GameObject chalk = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            chalk.name = "ChalkPiece";
            chalk.transform.SetParent(boardTextGo.transform.parent, false);
            if (blackboardGo.name == "BlackboardFBX")
            {
                chalk.transform.localScale = new Vector3(0.02f, 0.06f, 0.02f);
            }
            else
            {
                chalk.transform.localScale = new Vector3(0.015f, 0.05f, 0.015f);
            }
            chalk.transform.localRotation = Quaternion.Euler(60f, 0f, 0f);
            var chalkMr = chalk.GetComponent<MeshRenderer>();
            var chalkMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard"));
            chalkMat.color = Color.white;
            chalkMr.sharedMaterial = chalkMat;
            Destroy(chalk.GetComponent<Collider>());
            chalk.SetActive(false);

            // DEMO GAME button
            var demoBtnGo = new GameObject("DemoGameBtn");
            demoBtnGo.transform.SetParent(classroomContainer.transform, false);
            demoBtnGo.transform.localPosition = new Vector3(0f, 0.9f, 2.2f);
            demoBtnGo.transform.localScale = Vector3.one * 1.5f;

            var btn = demoBtnGo.AddComponent<HolographicButton>();
            btn.width = 1.8f;
            btn.height = 0.35f;
            btn.buttonText = "DEMO GAME";
            btn.textColor = NeonOrange;

            // Teleport vehicle (align with Y = -200f floor)
            if (_driver != null)
            {
                _driver.Z = 0f;
                if (_driver.Car != null)
                {
                    _driver.Car.position = new Vector3(0f, -200f + _driver.groundOffset, -3.5f);
                    _driver.Car.rotation = Quaternion.identity;
                }
                else
                {
                    _driver.transform.position = new Vector3(0f, -200f + _driver.groundOffset, -3.5f);
                    _driver.transform.rotation = Quaternion.identity;
                }
            }

            // Hide DEMO GAME button initially while writing
            demoBtnGo.SetActive(false);

            // Speed definition text content
            string fullText = 
                "SPEED\n\n" +
                "Definition:\n" +
                "Speed is the distance travelled by\n" +
                "an object per unit time.\n\n" +
                "SI Unit:\n" +
                "metre per second (m/s)\n\n" +
                "Formula:\n" +
                "Speed = Distance \u00f7 Time\n\n" +
                "Real-Life Example:\n" +
                "A car moving on a highway at 60 km/h\n" +
                "is an example of speed.\n\n" +
                "Key Point:\n" +
                "Speed tells us how fast or slow\n" +
                "an object is moving.";

            // Run writing animation
            yield return StartCoroutine(AnimateChalkWriting(tm, chalk, fullText, blackboardGo));

            // Reveal DEMO GAME button
            demoBtnGo.SetActive(true);

            bool demoClicked = false;
            btn.OnClick = () => { demoClicked = true; };

            while (!demoClicked)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    try { if (kb.spaceKey.wasPressedThisFrame) demoClicked = true; } catch { }
                }
                yield return null;
            }

            // Restore steering wheel when transitioning to demo game
            if (wheelPivot != null)
            {
                wheelPivot.gameObject.SetActive(true);
                Debug.Log("[SpeedLessonManager] Steering wheel popped up/restored for demo game.");
            }

            Destroy(classroomContainer);

            while (_driver.worldBuilder == null || _driver.worldBuilder.GetRoadPosition(0f) == Vector3.zero)
            {
                yield return null;
            }

            _driver.Z = 0f;
            _driver.SnapCarToRoadStart();

            StartCoroutine(RunIntroSequence());
        }

        private IEnumerator AnimateChalkWriting(TextMesh tm, GameObject chalk, string fullText, GameObject blackboardGo)
        {
            tm.text = "";
            chalk.SetActive(true);

            float charWidth = 0.015f;
            float lineHeight = 0.08f;

            if (blackboardGo.name == "BlackboardFBX")
            {
                charWidth = 0.018f;
                lineHeight = 0.085f;
            }
            else
            {
                charWidth = 0.015f;
                lineHeight = 0.08f;
            }

            string[] lines = fullText.Split('\n');
            string currentText = "";

            Vector3 startPos = tm.transform.localPosition;

            for (int l = 0; l < lines.Length; l++)
            {
                string line = lines[l];
                
                if (l > 0)
                {
                    currentText += "\n";
                    tm.text = currentText;
                }

                float currentY = startPos.y - (l * lineHeight);

                for (int c = 0; c < line.Length; c++)
                {
                    currentText += line[c];
                    tm.text = currentText;

                    float currentX = startPos.x + (c * charWidth);
                    
                    // Position chalk slightly offset from character
                    chalk.transform.localPosition = new Vector3(currentX + 0.03f, currentY - 0.03f, startPos.z - 0.01f);

                    yield return new WaitForSeconds(Random.Range(0.015f, 0.035f));
                }

                chalk.SetActive(false);
                yield return new WaitForSeconds(0.4f);
                chalk.SetActive(true);
            }

            chalk.SetActive(false);
        }

        // ── 0. INTRO SPLASH ───────────────────────────────────────────────────
        private IEnumerator RunIntroSequence()
        {
            currentState = LessonState.IntroSplash;
            _driver.Paused = true;
            _driver.automaticSpeedKmh = 0f;

            // Wait for the road path and world builder to be initialized
            while (_driver.worldBuilder == null || _driver.worldBuilder.GetRoadPosition(0f) == Vector3.zero)
            {
                yield return null;
            }

            // Immediately snap the vehicle to the road start on play
            _driver.Z = 0f;
            _driver.SnapCarToRoadStart();

            yield return null;

            // ── 0a. PRE-INTRO SPLASH: "SPEED" ──────────────────────────────────────
            var speedSplash = new GameObject("SpeedSplashBoard");
            speedSplash.transform.SetParent(Camera.main != null ? Camera.main.transform : _driver.transform, false);
            speedSplash.transform.localPosition = new Vector3(0f, 0.05f, 1.20f);
            speedSplash.transform.localRotation = Quaternion.identity;
            speedSplash.transform.localScale = Vector3.zero;

            var titleGo = new GameObject("SpeedTitle");
            titleGo.transform.SetParent(speedSplash.transform, false);
            titleGo.transform.localPosition = new Vector3(0f, 0.1f, -0.02f);
            titleGo.transform.localScale = Vector3.one * 0.008f;

            var titleTm = titleGo.AddComponent<TextMesh>();
            Font builtinFont = GetSafeBuiltinFont();
            if (builtinFont != null)
            {
                titleTm.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = Color.white;
                titleGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }
            titleTm.text = "SPEED";
            titleTm.fontSize = 96;
            titleTm.fontStyle = FontStyle.BoldAndItalic;
            titleTm.anchor = TextAnchor.MiddleCenter;
            titleTm.alignment = TextAlignment.Center;
            titleTm.color = NeonOrange;

            var subGo = new GameObject("SpeedSub");
            subGo.transform.SetParent(speedSplash.transform, false);
            subGo.transform.localPosition = new Vector3(0f, -0.15f, -0.02f);
            subGo.transform.localScale = Vector3.one * 0.007f;

            var subTm = subGo.AddComponent<TextMesh>();
            if (builtinFont != null)
            {
                subTm.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = Color.white;
                subGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }
            subTm.text = "PHYSICAL SPEED SIMULATION";
            subTm.fontSize = 28;
            subTm.fontStyle = FontStyle.Bold;
            subTm.anchor = TextAnchor.MiddleCenter;
            subTm.alignment = TextAlignment.Center;
            subTm.color = NeonCyan;

            // Animation loop: Scale in smoothly
            float animDuration = 0.6f;
            float elapsed = 0f;
            while (elapsed < animDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / animDuration;
                float scale = Mathf.Sin(t * Mathf.PI * 0.5f) * 1.3f;
                speedSplash.transform.localScale = Vector3.one * scale;
                yield return null;
            }
            speedSplash.transform.localScale = Vector3.one * 1.3f;

            // Pulsing effect while waiting (1.8 seconds or spacebar press)
            float waitDuration = 1.8f;
            float waitElapsed = 0f;
            bool skipPressed = false;
            while (waitElapsed < waitDuration && !skipPressed)
            {
                waitElapsed += Time.deltaTime;
                float pulse = 1.3f + Mathf.Sin(Time.time * 6f) * 0.03f;
                speedSplash.transform.localScale = Vector3.one * pulse;

                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    try { if (kb.spaceKey.wasPressedThisFrame) skipPressed = true; } catch { }
                }
                yield return null;
            }

            // Scale out smoothly
            elapsed = 0f;
            float fadeDuration = 0.25f;
            Vector3 finalScale = speedSplash.transform.localScale;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / fadeDuration;
                speedSplash.transform.localScale = Vector3.Lerp(finalScale, Vector3.zero, t);
                yield return null;
            }

            Destroy(speedSplash);

            var board = SpawnStatsBoard("SPEED MASTER VR", NeonCyan);
            var line1 = AddStatsLine(board, "A DRIVING ADVENTURE IN PHYSICAL SPEED", 0.35f, FontStyle.BoldAndItalic, NeonCyan);
            var line2 = AddStatsLine(board, "1. SELECT DISTANCE AND TIME FOR EACH MISSION", 0.18f, FontStyle.Normal, Color.white);
            var line3 = AddStatsLine(board, "2. PREDICT THE REQUIRED TARGET SPEED", 0.05f, FontStyle.Normal, Color.white);
            var line4 = AddStatsLine(board, "3. DRIVE AND ADJUST VEHICLE SPEED IN INCREMENTS", -0.08f, FontStyle.Normal, Color.white);
            var line5 = AddStatsLine(board, "4. MAINTAIN SPEED ACCURATELY TO EARN PRECISION REWARDS", -0.21f, FontStyle.Normal, Color.white);

            var startBtnGo = new GameObject("IntroStartBtn");
            startBtnGo.transform.SetParent(board.transform, false);
            startBtnGo.transform.localPosition = new Vector3(0f, -0.45f, -0.015f);
            startBtnGo.transform.localScale = Vector3.one;

            var btn = startBtnGo.AddComponent<HolographicButton>();
            btn.width = 1.6f;
            btn.height = 0.28f;
            btn.buttonText = "START ADVENTURE";
            btn.textColor = Color.white;

            bool startPressed = false;
            btn.OnClick = () => { startPressed = true; };

            // Find title text GameObject and build the list of items to slide in
            Transform titleTrans = board.transform.Find("TitleText");
            var slideItems = new List<GameObject>();
            if (titleTrans != null) slideItems.Add(titleTrans.gameObject);
            if (line1 != null) slideItems.Add(line1);
            if (line2 != null) slideItems.Add(line2);
            if (line3 != null) slideItems.Add(line3);
            if (line4 != null) slideItems.Add(line4);
            if (line5 != null) slideItems.Add(line5);
            slideItems.Add(startBtnGo);

            // Start the slide in animation
            StartCoroutine(AnimateSlideIn(slideItems, 0.12f));

            while (!startPressed)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    try { if (kb.spaceKey.wasPressedThisFrame) startPressed = true; } catch { }
                }
                yield return null;
            }

            // Dismiss board
            float dismissElapsed = 0f;
            float dismissDuration = 0.2f;
            Vector3 startScale = board.transform.localScale;
            while (dismissElapsed < dismissDuration)
            {
                dismissElapsed += Time.deltaTime;
                float t = dismissElapsed / dismissDuration;
                board.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
                yield return null;
            }

            Destroy(board);

            // Align to road start
            SpawnHUDOverlay("ALIGNING TO forest ROAD...", NeonCyan);
            if (_driver != null && _driver.Car != null)
            {
                while (_driver.worldBuilder == null || _driver.worldBuilder.GetRoadPosition(0f) == Vector3.zero)
                {
                    yield return null;
                }

                Vector3 targetPos = _driver.worldBuilder.GetRoadPosition(0f);
                Vector3 targetTangent = _driver.worldBuilder.GetRoadTangent(0f);
                Quaternion targetRot = Quaternion.LookRotation(targetTangent, Vector3.up);

                _driver.Car.position = targetPos;
                _driver.Car.rotation = targetRot;
                _driver.Z = 0f;
            }

            ClearHUD();
            StartLevelSelection();
        }

        private IEnumerator AnimateSlideIn(List<GameObject> items, float staggerDelay)
        {
            int count = items.Count;
            float slideDuration = 0.65f;
            float startX = 3.5f;

            // Set initial position of all items to be far right
            foreach (var item in items)
            {
                if (item != null)
                {
                    Vector3 pos = item.transform.localPosition;
                    pos.x = startX;
                    item.transform.localPosition = pos;
                }
            }

            // Staggered trigger of slide animations
            for (int i = 0; i < count; i++)
            {
                if (items[i] != null)
                {
                    StartCoroutine(SlideItemCoroutine(items[i], slideDuration));
                }
                yield return new WaitForSeconds(staggerDelay);
            }
        }

        private IEnumerator SlideItemCoroutine(GameObject item, float duration)
        {
            if (item == null) yield break;

            float elapsed = 0f;
            Vector3 localPos = item.transform.localPosition;
            float startX = localPos.x;
            float targetX = 0f;

            while (elapsed < duration)
            {
                if (item == null) yield break;
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                
                // Smooth ease-out cubic curve (starts fast, slows down at center)
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                
                Vector3 pos = item.transform.localPosition;
                pos.x = Mathf.Lerp(startX, targetX, ease);
                item.transform.localPosition = pos;
                yield return null;
            }

            if (item != null)
            {
                Vector3 pos = item.transform.localPosition;
                pos.x = targetX;
                item.transform.localPosition = pos;
            }
        }

        // ── 1. LEVEL SELECTION ───────────────────────────────────────────────
        private void StartLevelSelection()
        {
            currentState = LessonState.LevelSelection;
            ClearHUD();
            _driver.Paused = true;
            _driver.automaticSpeedKmh = 0f;
            SetWeatherForLevel(currentLevelIndex + 1);

            // Reset selection values
            _selectedDistance = 0f;
            _selectedTime = 0f;

            // Spawn Selection Board
            string header = $"LEVEL {currentLevelIndex + 1} CONFIGURATION";
            if (currentLevelIndex == 4)
            {
                header = "LEVEL 5 (EMERGENCY DELIVERY) CONFIG";
            }

            var board = SpawnStatsBoard(header, NeonCyan);

            AddStatsLine(board, "SELECT TARGET DISTANCE", 0.38f, FontStyle.Bold, Color.white);
            AddStatsLine(board, "SELECT TARGET TIME", 0.08f, FontStyle.Bold, Color.white);

            var config = _levels[currentLevelIndex];

            // Distance buttons (Left column layout)
            HolographicButton[] distBtns = new HolographicButton[3];
            for (int i = 0; i < 3; i++)
            {
                float distVal = config.distances[i];
                var btnGo = new GameObject($"DistBtn_{distVal}");
                btnGo.transform.SetParent(board.transform, false);
                btnGo.transform.localPosition = new Vector3(-0.55f + i * 0.55f, 0.23f, -0.02f);
                btnGo.transform.localScale = Vector3.one * 0.35f;

                var btn = btnGo.AddComponent<HolographicButton>();
                btn.width = 1.4f;
                btn.height = 0.55f;
                btn.buttonText = $"{distVal}m";
                btn.textColor = Color.white;
                distBtns[i] = btn;

                int idx = i;
                btn.OnClick = () =>
                {
                    _selectedDistance = distVal;
                    for (int k = 0; k < 3; k++) distBtns[k].IsSelected = (k == idx);
                    CheckSelectionAndShowPredictButton(board);
                };
            }

            // Time buttons (Right column layout)
            HolographicButton[] timeBtns = new HolographicButton[3];
            for (int i = 0; i < 3; i++)
            {
                float timeVal = config.times[i];
                var btnGo = new GameObject($"TimeBtn_{timeVal}");
                btnGo.transform.SetParent(board.transform, false);
                btnGo.transform.localPosition = new Vector3(-0.55f + i * 0.55f, -0.07f, -0.02f);
                btnGo.transform.localScale = Vector3.one * 0.35f;

                var btn = btnGo.AddComponent<HolographicButton>();
                btn.width = 1.4f;
                btn.height = 0.55f;
                btn.buttonText = $"{timeVal}s";
                btn.textColor = Color.white;
                timeBtns[i] = btn;

                int idx = i;
                btn.OnClick = () =>
                {
                    _selectedTime = timeVal;
                    for (int k = 0; k < 3; k++) timeBtns[k].IsSelected = (k == idx);
                    CheckSelectionAndShowPredictButton(board);
                };
            }

            _hudObjects.Add(board);
        }

        private void CheckSelectionAndShowPredictButton(GameObject board)
        {
            if (_selectedDistance > 0f && _selectedTime > 0f)
            {
                // Create predict button if not already created
                var oldBtn = board.transform.Find("PredictBtn");
                if (oldBtn != null) return;

                var predictBtnGo = new GameObject("PredictBtn");
                predictBtnGo.transform.SetParent(board.transform, false);
                predictBtnGo.transform.localPosition = new Vector3(0f, -0.38f, -0.02f);
                predictBtnGo.transform.localScale = Vector3.one;

                var btn = predictBtnGo.AddComponent<HolographicButton>();
                btn.width = 1.8f;
                btn.height = 0.28f;
                btn.buttonText = "PREDICT REQUIRED SPEED";
                btn.textColor = NeonOrange;
                btn.OnClick = () =>
                {
                    StartPrediction();
                };
            }
        }

        // ── 2. PRE-MISSION PREDICTION ─────────────────────────────────────────
        private void StartPrediction()
        {
            currentState = LessonState.Prediction;
            ClearHUD();

            // Automatically calculate Required Speed = Distance / Time (m/s)
            _requiredSpeedMs = _selectedDistance / _selectedTime;

            var board = SpawnStatsBoard("REQUIRED SPEED PREDICTION", NeonCyan);

            AddStatsLine(board, $"DISTANCE = {_selectedDistance}m", 0.38f, FontStyle.Normal, Color.white);
            AddStatsLine(board, $"TIME = {_selectedTime}s", 0.25f, FontStyle.Normal, Color.white);
            AddStatsLine(board, "WHAT SPEED IS REQUIRED?", 0.08f, FontStyle.Bold, NeonCyan);

            // Generate options
            List<float> speedChoices = GeneratePredictionOptions(_requiredSpeedMs);

            HolographicButton[] choiceBtns = new HolographicButton[4];
            for (int i = 0; i < 4; i++)
            {
                float speedVal = speedChoices[i];
                var btnGo = new GameObject($"ChoiceBtn_{speedVal}");
                btnGo.transform.SetParent(board.transform, false);
                btnGo.transform.localPosition = new Vector3(-0.66f + i * 0.44f, -0.12f, -0.02f);
                btnGo.transform.localScale = Vector3.one * 0.28f;

                var btn = btnGo.AddComponent<HolographicButton>();
                btn.width = 1.5f;
                btn.height = 0.65f;
                btn.buttonText = $"{speedVal} m/s";
                btn.textColor = Color.white;
                choiceBtns[i] = btn;

                int idx = i;
                btn.OnClick = () =>
                {
                    // Clicked choice
                    _predictedSpeedMs = speedVal;
                    _predictionCorrect = (Mathf.Abs(_predictedSpeedMs - _requiredSpeedMs) < 1.0f);

                    if (_predictionCorrect)
                    {
                        _knowledgePoints += 10;
                        _totalScore += 10;
                        _correctPredictionsCount++;
                        StartCoroutine(ShowTemporaryStatusText("CORRECT PREDICTION (+10 KP)", NeonGreen));
                    }
                    else
                    {
                        StartCoroutine(ShowTemporaryStatusText("PREDICTION REGISTERED", NeonOrange));
                    }
                    _totalPredictionsCount++;

                    // Destroy selection buttons
                    for (int k = 0; k < 4; k++)
                    {
                        if (choiceBtns[k] != null) Destroy(choiceBtns[k].gameObject);
                    }

                    // Add "START MISSION" button
                    var startBtnGo = new GameObject("StartMissionBtn");
                    startBtnGo.transform.SetParent(board.transform, false);
                    startBtnGo.transform.localPosition = new Vector3(0f, -0.42f, -0.02f);
                    startBtnGo.transform.localScale = Vector3.one;

                    var startBtn = startBtnGo.AddComponent<HolographicButton>();
                    startBtn.width = 1.6f;
                    startBtn.height = 0.28f;
                    if (_predictionCorrect)
                    {
                        startBtn.buttonText = "START MISSION (CORRECT!)";
                        startBtn.textColor = NeonGreen;
                    }
                    else
                    {
                        startBtn.buttonText = "START MISSION (INCORRECT!)";
                        startBtn.textColor = NeonRed;
                    }

                    startBtn.OnClick = () =>
                    {
                        StartMission();
                    };
                };
            }

            _hudObjects.Add(board);
        }

        private List<float> GeneratePredictionOptions(float correctSpeed)
        {
            List<float> options = new List<float>();
            float roundedCorrect = Mathf.Round(correctSpeed);
            options.Add(roundedCorrect);

            // Add distractors in m/s
            float[] offsets = { -5f, 5f, 10f, -2f, 2f };
            foreach (float offset in offsets)
            {
                float val = Mathf.Round(correctSpeed + offset);
                val = Mathf.Clamp(val, 5f, 30f);
                if (!options.Contains(val))
                {
                    options.Add(val);
                }
                if (options.Count >= 4) break;
            }

            // Pad with standard m/s increments
            float[] padSpeeds = { 5f, 10f, 15f, 20f, 25f, 30f };
            foreach (float pad in padSpeeds)
            {
                if (options.Count >= 4) break;
                if (!options.Contains(pad))
                {
                    options.Add(pad);
                }
            }

            options.Sort();
            return options;
        }

        // ── 3. ACTIVE MISSION ──────────────────────────────────────────────────
        private void StartMission()
        {
            currentState = LessonState.MissionActive;
            ClearHUD();

            // Set up driver positioning and starting speed
            _levelStartRawZ = _driver.Z;
            _levelStartDistanceOffset = 0f;
            _missionTimer = _selectedTime;
            _distanceCovered = 0f;
            _isMissionRunning = true;

            _precisionZoneTimer = 0f;
            _deviationZoneTimer = 0f;
            _precisionStatusText = "STABILIZING SPEED...";

            // Spawn Ghost Car if retrying
            if (_isRetry)
            {
                SpawnProceduralGhostCar();
                _isReplayingGhost = true;
            }

            _driver.Paused = false;
            _driver.automaticSpeedKmh = 30f; // 30 km/h starting speed (8.33 m/s)
            _driver.SpeedKmh = 30f;

            _currentAttemptRecording.Clear();

            // Spawn active HUD layout
            SpawnActiveHUD();
        }

        private TextMesh _hudStatsText;
        private HolographicButton[] _speedAdjBtns;

        private void SpawnActiveHUD()
        {
            var hud = new GameObject("WindshieldActiveHUD");
            var cam = Camera.main;
            if (cam == null) cam = FindFirstObjectByType<Camera>();
            hud.transform.SetParent(cam != null ? cam.transform : _driver.transform, false);
            hud.transform.localPosition = new Vector3(0f, 0.22f, 1.0f); // Positioned higher up on the windshield at 1.0m depth
            hud.transform.localRotation = Quaternion.identity;
            hud.transform.localScale = Vector3.one * 0.7f;

            // Stats Text
            var tmGo = new GameObject("StatsText");
            tmGo.transform.SetParent(hud.transform, false);
            tmGo.transform.localPosition = new Vector3(0f, 0.35f, -0.01f);
            tmGo.transform.localScale = Vector3.one * 0.008f;

            _hudStatsText = tmGo.AddComponent<TextMesh>();
            Font builtinFont = GetSafeBuiltinFont();
            if (builtinFont != null)
            {
                _hudStatsText.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = Color.white;
                tmGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }
            _hudStatsText.fontSize = 52;
            _hudStatsText.fontStyle = FontStyle.BoldAndItalic;
            _hudStatsText.anchor = TextAnchor.UpperCenter;
            _hudStatsText.alignment = TextAlignment.Center;
            _hudStatsText.color = Color.white;

            // Buttons removed as requested

            _hudObjects.Add(hud);
        }

        private void UpdateSpeedAdjustmentButtonSelections(float targetSpeedKmh)
        {
            if (_speedAdjBtns == null) return;
            float[] speedValues = { 5f, 10f, 12f, 15f, 17f, 20f, 22f, 25f, 30f };
            float targetSpeedMs = targetSpeedKmh / 3.6f;
            for (int i = 0; i < 9; i++)
            {
                if (_speedAdjBtns[i] != null)
                {
                    _speedAdjBtns[i].IsSelected = (Mathf.Abs(speedValues[i] - targetSpeedMs) < 0.5f);
                }
            }
        }

        private void UpdateActiveHUD()
        {
            if (_hudStatsText == null) return;

            // Keyboard shortcut checks during update to keep button highlights in sync
            if (_driver.automaticSpeedKmh.HasValue)
            {
                UpdateSpeedAdjustmentButtonSelections(_driver.automaticSpeedKmh.Value);
            }

            string levelName = currentLevelIndex == 4 ? "5 (EMERGENCY)" : (currentLevelIndex + 1).ToString();

            string statusLine = _precisionStatusText;
            if (!string.IsNullOrEmpty(_temporaryStatusText))
            {
                statusLine = _temporaryStatusText;
            }

            _hudStatsText.text = 
                $"LEVEL: {levelName}   |   SCORE: {_totalScore}\n" +
                $"SPEED: {Mathf.RoundToInt(_driver.SpeedKmh / 3.6f)} m/s   |   REQUIRED: {Mathf.RoundToInt(_requiredSpeedMs)} m/s\n" +
                $"DISTANCE: {Mathf.RoundToInt(_distanceCovered)}m / {Mathf.RoundToInt(_selectedDistance)}m\n" +
                $"TIME REMAINING: {Mathf.Max(0f, _missionTimer):F1}s\n" +
                $"PREDICTION: {(_predictionCorrect ? "CORRECT (+10)" : "INCORRECT")}\n\n" +
                $"{statusLine}";

            if (!string.IsNullOrEmpty(_temporaryStatusText))
            {
                _hudStatsText.color = _temporaryStatusColor;
            }
            else
            {
                _hudStatsText.color = Color.white;
            }
        }

        // ── 4. MISSION COMPLETION / FAIL ───────────────────────────────────────
        private void CompleteLevel(bool success)
        {
            _isMissionRunning = false;
            _driver.Paused = true;
            _driver.automaticSpeedKmh = 0f;
            _isReplayingGhost = false;

            if (_ghostCar != null) Destroy(_ghostCar);

            ClearHUD();

            if (success)
            {
                _missionPoints += 10;
                _totalScore += 10;

                // Time bonus
                float r = Mathf.Max(0f, _missionTimer);
                int timeBonus = Mathf.RoundToInt(r) * 2;
                _totalScore += timeBonus;

                // Calculate average speed
                float avgSpeed = 0f;
                if (_speedReadingsCount > 0)
                {
                    avgSpeed = _totalSpeedDriven / _speedReadingsCount;
                }

                // Update best mission time relative
                float relativeTime = (_selectedTime - _missionTimer) / _selectedTime;
                if (relativeTime < _bestMissionTimeRelative)
                {
                    _bestMissionTimeRelative = relativeTime;
                }

                var board = SpawnStatsBoard("MISSION SUCCESS!", NeonGreen);
                AddStatsLine(board, $"REQUIRED SPEED = {Mathf.RoundToInt(_requiredSpeedMs)} m/s", 0.38f, FontStyle.Normal, Color.white);
                AddStatsLine(board, $"AVERAGE SPEED = {Mathf.RoundToInt(avgSpeed)} m/s", 0.25f, FontStyle.Normal, Color.white);
                AddStatsLine(board, $"MISSION TIME = {(_selectedTime - _missionTimer):F1}s / {_selectedTime}s", 0.12f, FontStyle.Normal, Color.white);
                AddStatsLine(board, $"TIME BONUS = +{timeBonus}", -0.01f, FontStyle.Bold, NeonOrange);

                if (_predictionCorrect)
                {
                    AddStatsLine(board, "CORRECT PREDICTION (+10 KNOWLEDGE POINTS)!", -0.14f, FontStyle.Bold, NeonGreen);
                }
                else
                {
                    AddStatsLine(board, $"PREDICTION WAS INCORRECT. CORRECT SPEED: {Mathf.RoundToInt(_requiredSpeedMs)} m/s", -0.14f, FontStyle.Bold, NeonRed);
                }

                var btnGo = new GameObject("ContBtn");
                btnGo.transform.SetParent(board.transform, false);
                btnGo.transform.localPosition = new Vector3(0f, -0.42f, -0.02f);
                btnGo.transform.localScale = Vector3.one;

                var btn = btnGo.AddComponent<HolographicButton>();
                btn.width = 1.6f;
                btn.height = 0.28f;

                if (currentLevelIndex < 4)
                {
                    btn.buttonText = "CONTINUE TO NEXT LEVEL";
                    btn.textColor = Color.white;
                    btn.OnClick = () =>
                    {
                        currentLevelIndex++;
                        _isRetry = false;
                        _previousAttemptRecording.Clear();
                        StartLevelSelection();
                    };
                }
                else
                {
                    btn.buttonText = "VIEW FINAL RESULTS";
                    btn.textColor = NeonGreen;
                    btn.OnClick = () =>
                    {
                        ShowFinalResults();
                    };
                }

                _hudObjects.Add(board);
            }
            else
            {
                // Penalty
                _totalScore -= 10;

                // Save recording for ghost car
                _previousAttemptRecording = new List<GhostFrame>(_currentAttemptRecording);
                _isRetry = true;

                var board = SpawnStatsBoard("MISSION FAILED", NeonRed);
                AddStatsLine(board, "YOU DID NOT REACH THE TARGET DISTANCE IN TIME.", 0.25f, FontStyle.Bold, NeonRed);
                AddStatsLine(board, $"REQUIRED SPEED WAS: {Mathf.RoundToInt(_requiredSpeedMs)} m/s", 0.08f, FontStyle.Normal, Color.white);
                AddStatsLine(board, $"PENALTY: -10 POINTS", -0.08f, FontStyle.Bold, NeonRed);

                var btnGo = new GameObject("RetryBtn");
                btnGo.transform.SetParent(board.transform, false);
                btnGo.transform.localPosition = new Vector3(0f, -0.42f, -0.02f);
                btnGo.transform.localScale = Vector3.one;

                var btn = btnGo.AddComponent<HolographicButton>();
                btn.width = 1.6f;
                btn.height = 0.28f;
                btn.buttonText = "RETRY MISSION (WITH GHOST CAR)";
                btn.textColor = NeonOrange;
                btn.OnClick = () =>
                {
                    StartLevelSelection();
                };

                _hudObjects.Add(board);
            }
        }

        // ── 5. FINAL RESULTS / CERTIFICATE ─────────────────────────────────────
        private void ShowFinalResults()
        {
            currentState = LessonState.FinalResults;
            ClearHUD();
            SetWeatherForLevel(6); // Celebration weather

            float avgSpeedOverall = 0f;
            if (_speedReadingsCount > 0)
            {
                avgSpeedOverall = _totalSpeedDriven / _speedReadingsCount;
            }

            float predAccuracy = 0f;
            if (_totalPredictionsCount > 0)
            {
                predAccuracy = ((float)_correctPredictionsCount / _totalPredictionsCount) * 100f;
            }

            var board = SpawnStatsBoard("SPEED MASTER CERTIFICATE", NeonGreen);

            AddStatsLine(board, "CONGRATULATIONS DRIVER!", 0.38f, FontStyle.Bold, NeonGreen);
            AddStatsLine(board, $"FINAL SCORE: {_totalScore}", 0.25f, FontStyle.Bold, Color.white);
            AddStatsLine(board, $"HIGHEST SPEED MAINTAINED: {Mathf.RoundToInt(_highestSpeedMaintained)} m/s", 0.14f, FontStyle.Normal, Color.white);
            AddStatsLine(board, $"BEST MISSION TIME: {_bestMissionTimeRelative * 100f:F0}% OF TARGET", 0.03f, FontStyle.Normal, Color.white);
            AddStatsLine(board, $"TOTAL PRECISION REWARDS: {_totalPrecisionRewardsCount}", -0.08f, FontStyle.Normal, Color.white);
            AddStatsLine(board, $"PREDICTION ACCURACY: {predAccuracy:F0}%", -0.19f, FontStyle.Normal, Color.white);
            AddStatsLine(board, $"AVERAGE SPEED: {Mathf.RoundToInt(avgSpeedOverall)} m/s", -0.30f, FontStyle.Normal, Color.white);

            var btnGo = new GameObject("FreeDriveBtn");
            btnGo.transform.SetParent(board.transform, false);
            btnGo.transform.localPosition = new Vector3(0f, -0.48f, -0.02f);
            btnGo.transform.localScale = Vector3.one;

            var btn = btnGo.AddComponent<HolographicButton>();
            btn.width = 1.6f;
            btn.height = 0.28f;
            btn.buttonText = "FREE DRIVE HIGHWAY";
            btn.textColor = NeonCyan;
            btn.OnClick = () =>
            {
                ClearHUD();
                _driver.Paused = false;
                _driver.automaticSpeedKmh = null;
                currentState = LessonState.Completed;
            };

            // Spawn celebration spotlights
            StartCoroutine(CelebrationCelebrationEffects());

            _hudObjects.Add(board);
        }

        private IEnumerator CelebrationCelebrationEffects()
        {
            List<Light> spotlights = new List<Light>();
            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject($"VictoryLight_{i}");
                go.transform.position = _driver.transform.position + Random.insideUnitSphere * 12f + Vector3.up * 8f;
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = Random.ColorHSV(0f, 1f, 1f, 1f, 1f, 1f);
                l.intensity = 15f;
                l.range = 35f;
                l.spotAngle = 45f;
                spotlights.Add(l);
            }

            List<GameObject> particles = new List<GameObject>();
            for (int i = 0; i < 40; i++)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                p.name = "VictoryBubble";
                p.transform.position = _driver.transform.position + Random.insideUnitSphere * 15f + Vector3.up * -1f;
                p.transform.localScale = Vector3.one * Random.Range(0.2f, 0.5f);
                Destroy(p.GetComponent<Collider>());
                
                Color col = Random.ColorHSV(0f, 1f, 0.8f, 1f, 0.8f, 1f, 0.5f, 0.8f);
                p.GetComponent<Renderer>().sharedMaterial = CreateSolidUnlitMaterial(col);
                
                particles.Add(p);
            }

            float timer = 0f;
            while (currentState == LessonState.FinalResults && timer < 10.0f)
            {
                timer += Time.deltaTime;
                foreach (var l in spotlights)
                {
                    if (l != null) l.transform.Rotate(Vector3.up, Time.deltaTime * 50f);
                }
                foreach (var p in particles)
                {
                    if (p != null) p.transform.Translate(Vector3.up * Time.deltaTime * 2.5f, Space.World);
                }
                yield return null;
            }

            foreach (var l in spotlights) if (l != null) Destroy(l.gameObject);
            foreach (var p in particles) if (p != null) Destroy(p);
        }

        // ── GHOST CAR HELPER ──────────────────────────────────────────────────
        private void SpawnProceduralGhostCar()
        {
            if (_ghostCar != null) Destroy(_ghostCar);

            _ghostCar = new GameObject("GhostCar");
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(_ghostCar.transform, false);
            body.transform.localScale = new Vector3(1.8f, 0.4f, 4.0f);
            Destroy(body.GetComponent<Collider>());

            var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cap.transform.SetParent(_ghostCar.transform, false);
            cap.transform.localPosition = new Vector3(0f, 0.4f, -0.3f);
            cap.transform.localScale = new Vector3(1.5f, 0.4f, 2.0f);
            cap.transform.localRotation = Quaternion.Euler(0, 0, 90);
            Destroy(cap.GetComponent<Collider>());

            var mat = CreateTranslucentMaterial(new Color(0f, 0.85f, 1f, 0.28f));
            body.GetComponent<Renderer>().sharedMaterial = mat;
            cap.GetComponent<Renderer>().sharedMaterial = mat;

            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0) ? -0.95f : 0.95f;
                float z = (i < 2) ? 1.2f : -1.2f;
                var wh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wh.transform.SetParent(_ghostCar.transform, false);
                wh.transform.localPosition = new Vector3(x, -0.1f, z);
                wh.transform.localScale = new Vector3(0.7f, 0.15f, 0.7f);
                wh.transform.localRotation = Quaternion.Euler(0, 0, 90);
                Destroy(wh.GetComponent<Collider>());

                var whMat = CreateSolidUnlitMaterial(new Color(0f, 0.85f, 1f, 0.6f));
                wh.GetComponent<Renderer>().sharedMaterial = whMat;
            }
        }

        // ── HOLOGRAPHIC PRIMITIVE BUILDERS ────────────────────────────────────
        private Material CreateSolidUnlitMaterial(Color col)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
            return mat;
        }

        private Material CreateTranslucentMaterial(Color col)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.SetColor("_BaseColor", col);
                mat.SetFloat("_Surface", 1);
                mat.SetFloat("_Blend", 0);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3000;
                return mat;
            }
            else
            {
                var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Unlit/Color"));
                mat.color = col;
                mat.SetFloat("_Mode", 3);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
                return mat;
            }
        }

        private GameObject SpawnStatsBoard(string title, Color themeCol)
        {
            var board = new GameObject("ReviewHoloBoard");
            board.transform.SetParent(Camera.main != null ? Camera.main.transform : _driver.transform, false);
            board.transform.localPosition = new Vector3(0f, 0.05f, 1.3f);
            board.transform.localRotation = Quaternion.identity;
            board.transform.localScale = Vector3.one * 1.1f;
            
            var tmGo = new GameObject("TitleText");
            tmGo.transform.SetParent(board.transform, false);
            tmGo.transform.localPosition = new Vector3(0f, 0.58f, -0.02f);
            tmGo.transform.localScale = Vector3.one * 0.008f;

            var tm = tmGo.AddComponent<TextMesh>();
            Font builtinFont = GetSafeBuiltinFont();
            if (builtinFont != null)
            {
                tm.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = Color.white;
                tmGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }
            tm.text = title.ToUpper();
            tm.fontSize = 54;
            tm.fontStyle = FontStyle.BoldAndItalic;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;

            return board;
        }

        private GameObject AddStatsLine(GameObject board, string text, float yPos, FontStyle style = FontStyle.BoldAndItalic, Color? col = null)
        {
            var lineGo = new GameObject("Line_" + yPos);
            lineGo.transform.SetParent(board.transform, false);
            lineGo.transform.localPosition = new Vector3(0f, yPos, -0.02f);
            lineGo.transform.localScale = Vector3.one * 0.007f;

            var tm = lineGo.AddComponent<TextMesh>();
            Font builtinFont = GetSafeBuiltinFont();
            if (builtinFont != null)
            {
                tm.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = col ?? Color.white;
                lineGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }
            tm.text = text.ToUpper();
            tm.fontSize = 44;
            tm.fontStyle = style;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = col ?? Color.white;

            return lineGo;
        }

        private void SpawnHUDOverlay(string text, Color col)
        {
            ClearHUD();
            var board = SpawnStatsBoard(text, col);
            _hudObjects.Add(board);
        }

        private void ClearHUD()
        {
            foreach (var go in _hudObjects)
            {
                if (go != null) Destroy(go);
            }
            _hudObjects.Clear();
            _hudStatsText = null;
            _speedAdjBtns = null;
        }

        private void SpawnZMarkers()
        {
            // Spawn floating Z markers every 100m (100m to 4500m)
            for (float z = 100f; z <= 4500f; z += 100f)
            {
                Vector3 pos = _driver.worldBuilder.GetRoadPosition(z);
                Vector3 tangent = _driver.worldBuilder.GetRoadTangent(z);
                
                GameObject marker = new GameObject($"ZMarker_{z}m");
                float roadY = pos.y - _driver.worldBuilder.cameraHeight + 0.08f;
                marker.transform.position = new Vector3(pos.x, roadY + 4f, pos.z);
                
                if (tangent.sqrMagnitude > 0.01f)
                    marker.transform.rotation = Quaternion.LookRotation(tangent);
                
                marker.transform.SetParent(_hologramContainer.transform);

                var tmGo = new GameObject("Text");
                tmGo.transform.SetParent(marker.transform, false);
                tmGo.transform.localPosition = Vector3.zero;
                tmGo.transform.localScale = Vector3.one * 0.04f;

                var tm = tmGo.AddComponent<TextMesh>();
                Font builtinFont = GetSafeBuiltinFont();
                if (builtinFont != null)
                {
                    tm.font = builtinFont;
                    var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                    txtMat.mainTexture = builtinFont.material.mainTexture;
                    txtMat.color = NeonCyan;
                    tmGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
                }
                tm.text = $"{z} M";
                tm.fontSize = 64;
                tm.fontStyle = FontStyle.Bold;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = NeonCyan;
            }
        }

        private void SetWeatherForLevel(int level)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            
            Light mainLight = null;
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional)
                {
                    mainLight = l;
                    break;
                }
            }

            switch (level)
            {
                case 1: // Morning (Clear)
                    RenderSettings.fogColor = new Color(0.7f, 0.85f, 0.95f);
                    RenderSettings.fogDensity = 0.002f;
                    if (mainLight != null)
                    {
                        mainLight.color = new Color(1f, 0.95f, 0.9f);
                        mainLight.intensity = 1.0f;
                    }
                    break;
                case 2: // Afternoon
                    RenderSettings.fogColor = new Color(0.9f, 0.95f, 1f);
                    RenderSettings.fogDensity = 0.001f;
                    if (mainLight != null)
                    {
                        mainLight.color = new Color(1f, 1f, 1f);
                        mainLight.intensity = 1.3f;
                    }
                    break;
                case 3: // Twilight
                    RenderSettings.fogColor = new Color(0.2f, 0.1f, 0.35f);
                    RenderSettings.fogDensity = 0.004f;
                    if (mainLight != null)
                    {
                        mainLight.color = new Color(0.6f, 0.5f, 0.8f);
                        mainLight.intensity = 0.5f;
                    }
                    break;
                case 4: // Sunset
                    RenderSettings.fogColor = new Color(0.8f, 0.35f, 0.2f);
                    RenderSettings.fogDensity = 0.003f;
                    if (mainLight != null)
                    {
                        mainLight.color = new Color(1.0f, 0.5f, 0.3f);
                        mainLight.intensity = 0.7f;
                    }
                    break;
                case 5: // Storm (Emergency Delivery)
                    RenderSettings.fogColor = new Color(0.15f, 0.18f, 0.22f);
                    RenderSettings.fogDensity = 0.005f;
                    if (mainLight != null)
                    {
                        mainLight.color = new Color(0.4f, 0.45f, 0.5f);
                        mainLight.intensity = 0.3f;
                    }
                    break;
                case 6: // Celebration
                    RenderSettings.fogColor = new Color(0.1f, 0.3f, 0.2f);
                    RenderSettings.fogDensity = 0.001f;
                    if (mainLight != null)
                    {
                        mainLight.color = new Color(0.5f, 1.0f, 0.6f);
                        mainLight.intensity = 1.2f;
                    }
                    break;
            }
        }

        private void OnDestroy()
        {
            ClearHUD();
            if (_hologramContainer != null) Destroy(_hologramContainer);
            if (_ghostCar != null) Destroy(_ghostCar);
        }

        private static Font GetSafeBuiltinFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
            return f;
        }
    }
}
