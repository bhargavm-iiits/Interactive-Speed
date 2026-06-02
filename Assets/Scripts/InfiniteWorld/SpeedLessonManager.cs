using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace InfiniteWorld
{
    /// <summary>
    /// Master GameManager that orchestrates the 5 interactive educational driving zones
    /// to teach the concept: Speed = Distance ÷ Time.
    /// 
    /// Features:
    ///   - Auto-initializes and binds to VRCar at runtime.
    ///   - Seated experience throughout: no scenes, menus, or teleport systems.
    ///   - Beautiful glowing world-space holographic UI plates.
    ///   - Raycast laser pointers for Quest 3 / mouse click interactions.
    ///   - Procedural glowing transparent ghost car side-by-side replay.
    ///   - Interactive speed trials with persistent side-by-side color trails.
    ///   - Giant forest lights and particle celebration on mastery success.
    /// </summary>
    public class SpeedLessonManager : MonoBehaviour
    {
        // ── Singleton instance ────────────────────────────────────────────────
        public static SpeedLessonManager Instance { get; private set; }

        // ── Lesson states ─────────────────────────────────────────────────────
        public enum LessonState
        {
            IntroSplash,
            Zone1_DiscoverSpeed_Intro,
            Zone1_DiscoverSpeed_Driving,
            Zone1_DiscoverSpeed_Review,
            Zone2_FasterOrSlower_Intro,
            Zone2_FasterOrSlower_Driving,
            Zone2_FasterOrSlower_Review,
            Zone3_SpeedTunnel,
            Zone4_ExperimentArea,
            Zone5_HeroMission_Quiz,
            Zone5_HeroMission_Driving,
            Zone5_HeroMission_Celebration,
            Completed
        }

        [Header("State Tracking")]
        public LessonState currentState = LessonState.IntroSplash;

        // Reference dependencies
        private StraightLineDriver _driver;
        private VRHologramRaycaster _raycaster;

        // Holographic visual parents
        private GameObject _hologramContainer;
        private List<GameObject> _zoneObjects = new List<GameObject>();

        // Zone 1 Recording (for Zone 2 Ghost Car)
        private List<float> _zRecord = new List<float>();
        private List<float> _timeRecord = new List<float>();
        private float _zone1Timer = 0f;
        private bool _isRecordingZ = false;
        private bool _isZone2Mission2 = false; // Tracks Mission 1 vs 2 in Zone 2

        // Zone 2 Ghost Car Replay
        private GameObject _ghostCar;
        private bool _isReplayingGhost = false;
        private float _ghostPlaytime = 0f;

        // Zone 4 Trails
        private struct TrialData
        {
            public float speedKmh;
            public float startZ;
            public float endZ;
            public Color color;
            public GameObject trailLineGo;
        }
        private List<TrialData> _trials = new List<TrialData>();
        private bool _isZone4TrialActive = false;
        private float _zone4TrialTimer = 0f;
        private int _selectedSpeedIndex = 0;
        private float[] _zone4Speeds = { 20f, 40f, 60f, 80f, 100f };
        private Color[] _zone4Colors = { Color.red, Color.yellow, Color.green, Color.cyan, Color.magenta };

        // Zone 5 Mission State
        private float _zone5Timer = 0f;
        private bool _isZone5Running = false;
        private float _zone5RequiredSpeedMs = 40f; // 800m / 20s = 40 m/s
        private float _zone5ChosenSpeedVal = 0f;
        private bool _markersSpawned = false;

        // Colors
        private static readonly Color NeonCyan = new Color(0f, 0.85f, 1f, 0.8f);
        private static readonly Color NeonOrange = new Color(1f, 0.45f, 0f, 0.8f);
        private static readonly Color NeonGreen = new Color(0.1f, 0.95f, 0.2f, 0.8f);
        private static readonly Color NeonRed = new Color(1f, 0.1f, 0.2f, 0.8f);

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

            // Remove/Disable duplicate AudioListeners to prevent Unity console warning flooding
            DisableDuplicateAudioListeners();
        }

        private void DisableDuplicateAudioListeners()
        {
            var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            if (listeners.Length > 1)
            {
                Debug.Log($"[SpeedLessonManager] Found {listeners.Length} AudioListeners. Retaining only the one on Main Camera and disabling others.");
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
                    Debug.Log($"[SpeedLessonManager] Disabled duplicate AudioListener on GameObject: {listener.gameObject.name}");
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

            // Spawn visual raycaster pointer for laser clicks
            _raycaster = gameObject.AddComponent<VRHologramRaycaster>();

            // Create container for spawned holographic elements
            _hologramContainer = new GameObject("Holograms_Root");

            // Auto-wire backend components for FastAPI multi-agent connection
            var backendGo = GameObject.Find("BackendManager");
            if (backendGo == null)
            {
                backendGo = new GameObject("BackendManager");
                Debug.Log("[SpeedLessonManager] Created new BackendManager GameObject.");
            }

            var connector = backendGo.GetComponent<VRBackendConnector>();
            if (connector == null)
            {
                connector = backendGo.AddComponent<VRBackendConnector>();
                Debug.Log("[SpeedLessonManager] Added missing VRBackendConnector to BackendManager.");
            }

            var uiController = backendGo.GetComponent<VRBackendUIController>();
            if (uiController == null)
            {
                uiController = backendGo.AddComponent<VRBackendUIController>();
                Debug.Log("[SpeedLessonManager] Added missing VRBackendUIController to BackendManager.");
            }

            // Kick off state machine
            StartCoroutine(RunIntroSequence());
        }

        // ── 0. INTRO SPLASH: "SPEED" RACE ANIMATION ────────────────────────────
        private IEnumerator RunIntroSequence()
        {
            currentState = LessonState.IntroSplash;
            _driver.Paused = true;
            _driver.automaticSpeedKmh = 0f; // Lock car at start

            // Wait a frame to ensure the driver's SetupCar has completed
            yield return null;

            // 1. Create the Slide board container in front of the camera
            var board = new GameObject("IntroDefinitionSlide");
            board.transform.SetParent(_driver.transform, false); // Parent to camera!
            
            // Centered exactly 1.25m in front of the camera
            board.transform.localPosition = new Vector3(0f, 0.05f, 1.25f);
            board.transform.localRotation = Quaternion.identity;

            Font builtinFont = GetSafeBuiltinFont();

            // Title Text (Increased scale for readability)
            var titleTextGo = new GameObject("TitleText");
            titleTextGo.transform.SetParent(board.transform, false);
            titleTextGo.transform.localPosition = new Vector3(0f, 0.52f, -0.015f);
            titleTextGo.transform.localScale = Vector3.one * 0.012f;
            var tmTitle = titleTextGo.AddComponent<TextMesh>();
            if (builtinFont != null)
            {
                tmTitle.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = NeonCyan;
                titleTextGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }
            tmTitle.text = "SPEED";
            tmTitle.fontSize = 72;
            tmTitle.fontStyle = FontStyle.Bold;
            tmTitle.anchor = TextAnchor.MiddleCenter;
            tmTitle.alignment = TextAlignment.Center;
            tmTitle.color = NeonCyan;

            // Subtitle Text ("HOW FAST AN OBJECT MOVES", increased scale)
            var subtitleTextGo = new GameObject("SubtitleText");
            subtitleTextGo.transform.SetParent(board.transform, false);
            subtitleTextGo.transform.localPosition = new Vector3(0f, 0.40f, -0.015f);
            subtitleTextGo.transform.localScale = Vector3.one * 0.006f;
            var tmSubtitle = subtitleTextGo.AddComponent<TextMesh>();
            if (builtinFont != null)
            {
                tmSubtitle.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = NeonCyan;
                subtitleTextGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }
            tmSubtitle.text = "HOW FAST AN OBJECT MOVES";
            tmSubtitle.fontSize = 54;
            tmSubtitle.fontStyle = FontStyle.BoldAndItalic;
            tmSubtitle.anchor = TextAnchor.MiddleCenter;
            tmSubtitle.alignment = TextAlignment.Center;
            tmSubtitle.color = NeonCyan;

            // Definition Text (increased scale)
            var defGo = new GameObject("DefinitionText");
            defGo.transform.SetParent(board.transform, false);
            defGo.transform.localPosition = new Vector3(0f, 0.20f, -0.015f);
            defGo.transform.localScale = Vector3.one * 0.0075f;
            var tmDef = defGo.AddComponent<TextMesh>();
            if (builtinFont != null)
            {
                tmDef.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = Color.white;
                defGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }
            tmDef.text = "SPEED IS THE DISTANCE TRAVELLED BY AN OBJECT\nIN A UNIT OF TIME.";
            tmDef.fontSize = 48;
            tmDef.fontStyle = FontStyle.Bold;
            tmDef.anchor = TextAnchor.MiddleCenter;
            tmDef.alignment = TextAlignment.Center;
            tmDef.color = Color.white;

            // Formula Text (increased scale)
            var formGo = new GameObject("FormulaText");
            formGo.transform.SetParent(board.transform, false);
            formGo.transform.localPosition = new Vector3(0f, -0.06f, -0.015f);
            formGo.transform.localScale = Vector3.one * 0.008f;
            var tmForm = formGo.AddComponent<TextMesh>();
            if (builtinFont != null)
            {
                tmForm.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = NeonOrange;
                formGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }
            tmForm.text = "            DISTANCE\nSPEED = ────────────\n              TIME";
            tmForm.fontSize = 48;
            tmForm.fontStyle = FontStyle.Bold;
            tmForm.anchor = TextAnchor.MiddleCenter;
            tmForm.alignment = TextAlignment.Center;
            tmForm.color = NeonOrange;

            // SI Unit Text (increased scale)
            var siUnitGo = new GameObject("SIUnitText");
            siUnitGo.transform.SetParent(board.transform, false);
            siUnitGo.transform.localPosition = new Vector3(0f, -0.28f, -0.015f);
            siUnitGo.transform.localScale = Vector3.one * 0.007f;
            var tmSIUnit = siUnitGo.AddComponent<TextMesh>();
            if (builtinFont != null)
            {
                tmSIUnit.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = NeonGreen;
                siUnitGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }
            tmSIUnit.text = "SI UNIT = METRE PER SECOND (m/s)";
            tmSIUnit.fontSize = 48;
            tmSIUnit.fontStyle = FontStyle.BoldAndItalic;
            tmSIUnit.anchor = TextAnchor.MiddleCenter;
            tmSIUnit.alignment = TextAlignment.Center;
            tmSIUnit.color = NeonGreen;

            // Interactive bottom alignment button (slightly larger for easier desktop raycast hovering)
            var startBtnGo = new GameObject("IntroStartBtn");
            startBtnGo.transform.SetParent(board.transform, false);
            startBtnGo.transform.localPosition = new Vector3(0f, -0.46f, -0.015f);
            startBtnGo.transform.localScale = Vector3.one;
            var startBtnComp = startBtnGo.AddComponent<HolographicButton>();
            startBtnComp.width = 1.3f;
            startBtnComp.height = 0.22f;
            startBtnComp.buttonText = "ALIGN TO ROAD";
            startBtnComp.textColor = Color.white;

            bool startPressed = false;

            startBtnComp.OnClick = () => {
                startPressed = true;
            };

            // Wait for Spacebar or Start button click
            while (!startPressed)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    try
                    {
                        if (kb.spaceKey.wasPressedThisFrame)
                        {
                            startPressed = true;
                        }
                    }
                    catch { }
                }

                yield return null;
            }

            // Quick shrink-out animation on dismiss
            float dismissElapsed = 0f;
            float dismissDuration = 0.25f;
            Vector3 startScale = board.transform.localScale;
            while (dismissElapsed < dismissDuration)
            {
                dismissElapsed += Time.deltaTime;
                float t = dismissElapsed / dismissDuration;
                board.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }

            Destroy(board);

            // Windshield align prompt
            SpawnInstructionBoard("ALIGNING TO ROAD...", NeonCyan);

            // Interpolate vehicle from grass to road start
            if (_driver != null && _driver.Car != null)
            {
                Vector3 startPos = _driver.Car.position;
                Quaternion startRot = _driver.Car.rotation;

                // Ensure WorldBuilder generated road coordinates
                while (_driver.worldBuilder == null || _driver.worldBuilder.GetRoadPosition(0f) == Vector3.zero)
                {
                    yield return null;
                }

                Vector3 targetPos = _driver.worldBuilder.GetRoadPosition(0f);
                Vector3 targetTangent = _driver.worldBuilder.GetRoadTangent(0f);
                Quaternion targetRot = Quaternion.LookRotation(targetTangent, Vector3.up);

                float alignElapsed = 0f;
                float alignDuration = 3.0f;
                while (alignElapsed < alignDuration)
                {
                    alignElapsed += Time.deltaTime;
                    float t = alignElapsed / alignDuration;
                    float smoothT = Mathf.SmoothStep(0f, 1f, t);

                    _driver.Car.position = Vector3.Lerp(startPos, targetPos, smoothT);
                    _driver.Car.rotation = Quaternion.Slerp(startRot, targetRot, smoothT);

                    yield return null;
                }

                _driver.Car.position = targetPos;
                _driver.Car.rotation = targetRot;
            }

            ClearWindshieldHUD();

            if (_driver != null)
            {
                _driver.SnapCarToRoadStart();
            }

            // Transition to Level 1
            StartZone1();
        }

        private void SetMaterialColor(Material mat, Color col)
        {
            if (mat == null) return;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
        }

        // ── 1. ZONE 1: DISCOVER SPEED ──────────────────────────────────────────
        private void StartZone1()
        {
            currentState = LessonState.Zone1_DiscoverSpeed_Intro;
            ClearHolograms();
            SetWeatherForLevel(1);

            // Spawn Holographic Entrance Gate at Z = 10m
            Vector3 gatePos = _driver.worldBuilder.GetRoadPosition(10f);
            Vector3 gateTangent = _driver.worldBuilder.GetRoadTangent(10f);
            SpawnHolographicGate("Zone1_Gate", gatePos, gateTangent, NeonCyan, "DISCOVER SPEED");

            // Instruct driver via centered interactive pop-up (Accept/Reject)
            SpawnInstructionBoard("Discover Speed\n\nDrive through the gate.\nYour goal: reach the 500m checkpoint in exactly 50 seconds!", NeonCyan,
                onAccept: () => {
                    Debug.Log("[SpeedLessonManager] Zone 1 challenge accepted!");
                },
                onReject: () => {
                    _driver.Z = 0f; // reset to beginning
                    StartZone1();
                }
            );
        }

        private void Update()
        {
            if (!_markersSpawned && _driver != null && _driver.worldBuilder != null)
            {
                _markersSpawned = true;
                SpawnZMarkers();
            }

            float playerZ = _driver.Z;

            // State-based triggers
            switch (currentState)
            {
                // ZONE 1 Driving Check
                case LessonState.Zone1_DiscoverSpeed_Intro:
                    if (playerZ >= 10f)
                    {
                        currentState = LessonState.Zone1_DiscoverSpeed_Driving;
                        _isRecordingZ = true;
                        _zRecord.Clear();
                        _timeRecord.Clear();
                        _zone1Timer = 0f;
                        
                        // Spawn Checkpoint at Z = 500m
                        Vector3 cpPos = _driver.worldBuilder.GetRoadPosition(500f);
                        Vector3 cpTangent = _driver.worldBuilder.GetRoadTangent(500f);
                        SpawnHolographicGate("Zone1_Checkpoint", cpPos, cpTangent, NeonOrange, "CHECKPOINT");
                    }
                    break;

                case LessonState.Zone1_DiscoverSpeed_Driving:
                    _zone1Timer += Time.deltaTime;
                    if (_isRecordingZ)
                    {
                        _zRecord.Add(playerZ);
                        _timeRecord.Add(_zone1Timer);
                    }

                    // Display dashboard info on the windshield HUD
                    SpawnInstructionBoard($"LEVEL 1 ACTIVE\n\nSpeed = {Mathf.RoundToInt(_driver.SpeedKmh)} km/h\nDistance Covered = {Mathf.Min(500f, playerZ):F0}m / 500m\nTime Remaining = {Mathf.Max(0f, 50f - _zone1Timer):F1}s", NeonOrange);

                    // Reached Z = 500m Checkpoint
                    if (playerZ >= 500f)
                    {
                        _isRecordingZ = false;
                        currentState = LessonState.Zone1_DiscoverSpeed_Review;
                        _driver.Paused = true; // Freeze Time/Vehicle

                        // Display Formula HUD
                        float avgSpeed = 500f / _zone1Timer; // 500m divided by elapsed time
                        ShowZone1FormulaHUD(avgSpeed, _zone1Timer);
                    }
                    break;

                // ZONE 2 Trigger
                case LessonState.Zone2_FasterOrSlower_Intro:
                    // Waiting for Accept button callback to transition state
                    break;

                case LessonState.Zone2_FasterOrSlower_Driving:
                    _zone1Timer += Time.deltaTime;
                    
                    if (_isRecordingZ)
                    {
                        _zRecord.Add(playerZ);
                        _timeRecord.Add(_zone1Timer);
                    }

                    // Display dashboard info on the windshield HUD
                    float maxTime = _isZone2Mission2 ? 35f : 70f;
                    float distCovered = Mathf.Min(700f, playerZ - 500f);
                    SpawnInstructionBoard($"LEVEL 2 ACTIVE ({( _isZone2Mission2 ? "MISSION 2" : "MISSION 1" )})\n\nSpeed = {Mathf.RoundToInt(_driver.SpeedKmh)} km/h\nDistance Covered = {distCovered:F0}m / 700m\nTime Remaining = {Mathf.Max(0f, maxTime - _zone1Timer):F1}s", NeonOrange);

                    if (playerZ >= 1200f)
                    {
                        _driver.Paused = true; // Freeze

                        if (!_isZone2Mission2)
                        {
                            // Mission 1 finished, trigger Mission 2 setup
                            _isRecordingZ = false;
                            currentState = LessonState.Zone2_FasterOrSlower_Intro; // Transition out of driving state immediately to stop HUD updates
                            ClearHolograms();
                            SpawnInstructionBoard("Mission 1 Complete!\n\nNow, let's try the same 700m, but in only 35 seconds!\nYour previous attempt will run as a Ghost Car.", NeonCyan,
                                onAccept: () => {
                                    _isZone2Mission2 = true;
                                    _driver.Z = 500f; // Reset Z back to 500m
                                    _driver.Paused = false;
                                    _zone1Timer = 0f;
                                    _isRecordingZ = false;
                                    SpawnProceduralGhostCar();
                                    currentState = LessonState.Zone2_FasterOrSlower_Driving; // Go directly to driving

                                    // Spawn Checkpoint at Z = 1200m
                                    Vector3 cpPos = _driver.worldBuilder.GetRoadPosition(1200f);
                                    Vector3 cpTangent = _driver.worldBuilder.GetRoadTangent(1200f);
                                    SpawnHolographicGate("Zone2_Checkpoint", cpPos, cpTangent, NeonOrange, "CHECKPOINT");
                                },
                                onReject: () => {
                                    _driver.Z = 500f;
                                    StartZone2();
                                }
                            );
                        }
                        else
                        {
                            // Mission 2 finished, show comparison
                            _isReplayingGhost = false;
                            if (_ghostCar != null) Destroy(_ghostCar);
                            currentState = LessonState.Zone2_FasterOrSlower_Review;
                            ShowZone2ComparisonHUD(_zone1Timer);
                        }
                    }
                    break;

                // ZONE 3 Trigger (Speed Tunnel)
                case LessonState.Zone3_SpeedTunnel:
                    UpdateZone3Gates(playerZ);
                    break;

                // ZONE 4 Trigger (Experiment Track)
                case LessonState.Zone4_ExperimentArea:
                    UpdateZone4Experiment();
                    break;

                // ZONE 5 Trigger (Hero Mission)
                case LessonState.Zone5_HeroMission_Driving:
                    UpdateZone5Mission(playerZ);
                    break;
            }

            // Animate Ghost Car if replaying
            if (_isReplayingGhost && _ghostCar != null && _zRecord.Count > 0)
            {
                _ghostPlaytime += Time.deltaTime;
                float currentPlaybackZ = GetGhostZAtTime(_ghostPlaytime);

                // Playback Z is exact since recording and playback run on the same Z range (500m to 1200m)
                float adjustedZ = currentPlaybackZ; 
                
                Vector3 roadPos = _driver.worldBuilder.GetRoadPosition(adjustedZ);
                float roadSurfaceY = _driver.worldBuilder.SampleTerrainAt(roadPos.x, adjustedZ);
                Vector3 tangent = _driver.worldBuilder.GetRoadTangent(adjustedZ);
                Vector3 perpendicular = new Vector3(-tangent.z, 0f, tangent.x).normalized;

                // Snapped to road surface instead of camera/vehicle eye level
                _ghostCar.transform.position = new Vector3(roadPos.x, roadSurfaceY, roadPos.z) + perpendicular * -2.5f + Vector3.up * 0.1f;
                if (tangent.sqrMagnitude > 0.01f)
                    _ghostCar.transform.rotation = Quaternion.LookRotation(tangent);

                // Stop replaying if we exceed record length
                if (_ghostPlaytime >= _timeRecord[_timeRecord.Count - 1])
                {
                    _ghostPlaytime = 0f; // Loop
                }
            }
        }

        private float GetGhostZAtTime(float time)
        {
            if (_timeRecord.Count == 0) return 500f;
            if (time <= _timeRecord[0]) return _zRecord[0];
            if (time >= _timeRecord[_timeRecord.Count - 1]) return _zRecord[_zRecord.Count - 1];

            // Linear interpolation
            for (int i = 0; i < _timeRecord.Count - 1; i++)
            {
                if (time >= _timeRecord[i] && time <= _timeRecord[i+1])
                {
                    float t = (time - _timeRecord[i]) / (_timeRecord[i+1] - _timeRecord[i]);
                    return Mathf.Lerp(_zRecord[i], _zRecord[i+1], t);
                }
            }
            return _zRecord[0];
        }

        private void ShowZone1FormulaHUD(float avgSpeed, float elapsed)
        {
            ClearHolograms();

            var board = SpawnStatsBoard("ZONE 1 REVIEW: SPEED FORMULA", NeonCyan);
            
            // Add Stats Texts (Spaced cleanly without overlapping)
            AddStatsLine(board, $"Distance Traveled = 500 m", 0.18f);
            AddStatsLine(board, $"Time Taken = {elapsed:F2} seconds", 0.06f);
            AddStatsLine(board, $"Average Speed = {avgSpeed:F2} m/s", -0.06f);
            
            // Add Formula diagram
            AddStatsLine(board, "Speed = Distance ÷ Time", -0.18f, FontStyle.Bold, NeonOrange);

            // Floating Continue button
            var btnGo = new GameObject("ContinueButton");
            btnGo.transform.SetParent(board.transform, false);
            btnGo.transform.localPosition = new Vector3(0f, -0.42f, -0.05f);
            var btn = btnGo.AddComponent<HolographicButton>();
            btn.width = 1.6f;
            btn.height = 0.45f;
            btn.buttonText = "Continue driving";
            btn.OnClick = () => {
                _driver.Paused = false; // Resume
                StartZone2();
            };
            
            _zoneObjects.Add(board);
        }

        // ── 2. ZONE 2: FASTER OR SLOWER ────────────────────────────────────────
        private void StartZone2()
        {
            currentState = LessonState.Zone2_FasterOrSlower_Intro;
            ClearHolograms();
            SetWeatherForLevel(2);

            // Spawn Entrance Gate at Z = 500m
            Vector3 gatePos = _driver.worldBuilder.GetRoadPosition(500f);
            Vector3 gateTangent = _driver.worldBuilder.GetRoadTangent(500f);
            SpawnHolographicGate("Zone2_Gate", gatePos, gateTangent, NeonCyan, "FASTER OR SLOWER");

            // Instruct driver via centered interactive pop-up (Accept/Reject)
            SpawnInstructionBoard("Level 2: Same Distance\n\nDrive the same 700m distance at different speeds.\nMission 1: Reach 1200m in 70 seconds.", NeonCyan,
                onAccept: () => {
                    _isZone2Mission2 = false;
                    _zRecord.Clear();
                    _timeRecord.Clear();
                    _zone1Timer = 0f;
                    _isRecordingZ = true;
                    currentState = LessonState.Zone2_FasterOrSlower_Driving;

                    // Spawn Checkpoint at Z = 1200m
                    Vector3 cpPos = _driver.worldBuilder.GetRoadPosition(1200f);
                    Vector3 cpTangent = _driver.worldBuilder.GetRoadTangent(1200f);
                    SpawnHolographicGate("Zone2_Checkpoint", cpPos, cpTangent, NeonOrange, "CHECKPOINT");
                },
                onReject: () => {
                    _driver.Z = 500f; // reset back to start of Zone 2
                    StartZone2();
                }
            );
        }

        private void ShowZone2ComparisonHUD(float elapsed)
        {
            ClearHolograms();

            // Instantiates procedural semi-transparent Cyber Ghost Car next to player
            SpawnProceduralGhostCar();

            var board = SpawnStatsBoard("ZONE 2 REVIEW: FASTER OR SLOWER", NeonCyan);

            float speedA = 10.0f; // Zone 2 Mission 1 target: 700m / 70s = 10 m/s
            float speedB = 700f / elapsed; // Zone 2 Mission 2 target: 700m / 35s = 20 m/s

            AddStatsLine(board, "Compare attempts side-by-side:", 0.25f, FontStyle.Bold, Color.white);
            AddStatsLine(board, $"Run A (Mission 1) : 700m in {_timeRecord[_timeRecord.Count - 1]:F2}s  ->  Speed: {speedA:F1} m/s", 0.12f, FontStyle.Normal, NeonCyan);
            AddStatsLine(board, $"Run B (Mission 2) : 700m in {elapsed:F2}s  ->  Speed: {speedB:F1} m/s", -0.01f, FontStyle.Normal, NeonOrange);
            
            AddStatsLine(board, "The distance stayed the same (700m).", -0.15f);
            AddStatsLine(board, "The time decreased.", -0.27f);
            AddStatsLine(board, "Therefore, SPEED increased!", -0.39f, FontStyle.Bold, NeonGreen);

            // Floating Continue button
            var btnGo = new GameObject("ContinueButton");
            btnGo.transform.SetParent(board.transform, false);
            btnGo.transform.localPosition = new Vector3(0f, -0.58f, -0.05f);
            var btn = btnGo.AddComponent<HolographicButton>();
            btn.width = 1.6f;
            btn.height = 0.45f;
            btn.buttonText = "Continue to Tunnel";
            btn.OnClick = () => {
                _isReplayingGhost = false;
                if (_ghostCar != null) Destroy(_ghostCar);
                _driver.Paused = false; // Resume
                StartZone3();
            };

            _zoneObjects.Add(board);
        }

        private void SpawnProceduralGhostCar()
        {
            if (_ghostCar != null) Destroy(_ghostCar);

            // Procedural blueprint outline of a cyber futuristic vehicle
            _ghostCar = new GameObject("Holographic_Ghost_Car");
            _ghostCar.transform.position = _driver.transform.position;

            // Cyan glowing chassis
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

            // Transparent cyber material using URP helper
            var cyMat = CreateTranslucentMaterial(new Color(0f, 0.9f, 0.9f, 0.28f));
            
            body.GetComponent<Renderer>().sharedMaterial = cyMat;
            cap.GetComponent<Renderer>().sharedMaterial = cyMat;

            // Spawn wheels
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

                var whMat = CreateSolidUnlitMaterial(new Color(0f, 0.9f, 0.9f, 0.6f));
                wh.GetComponent<Renderer>().sharedMaterial = whMat;
            }

            _isReplayingGhost = true;
            _ghostPlaytime = 0f;
        }

        // ── 3. ZONE 3: SPEED TUNNEL (Z = 600m to 1100m) ─────────────────────────
        private struct SpeedQuestion
        {
            public float triggerZ;
            public string text;
            public string[] answers;
            public int correctIdx;
            public bool answered;
            public bool userResponded;
        }
        private List<SpeedQuestion> _zone3Questions = new List<SpeedQuestion>();
        private GameObject _activeQuestionBoard;

        private void StartZone3()
        {
            currentState = LessonState.Zone3_SpeedTunnel;
            ClearHolograms();
            SetWeatherForLevel(3);

            // Set up Tunnel Questions (Spaced nicely along the road)
            _zone3Questions.Clear();
            _zone3Questions.Add(new SpeedQuestion {
                triggerZ = 1400f,
                text = "Passed through Speed Tunnel Gate A!\n\nDistance = 100 m\nTime = 10 s\n\nWhat is your Speed?",
                answers = new string[] { "5 m/s", "10 m/s", "20 m/s" },
                correctIdx = 1, // 10 m/s
                answered = false,
                userResponded = false
            });

            _zone3Questions.Add(new SpeedQuestion {
                triggerZ = 1600f,
                text = "Passed through Speed Tunnel Gate B!\n\nDistance = 200 m\nTime = 10 s\n\nWhat is your Speed?",
                answers = new string[] { "10 m/s", "20 m/s", "30 m/s" },
                correctIdx = 1, // 20 m/s
                answered = false,
                userResponded = false
            });

            _zone3Questions.Add(new SpeedQuestion {
                triggerZ = 1800f,
                text = "Passed through Speed Tunnel Gate C!\n\nDistance = 300 m\nTime = 15 s\n\nWhat is your Speed?",
                answers = new string[] { "15 m/s", "20 m/s", "25 m/s" },
                correctIdx = 1, // 20 m/s
                answered = false,
                userResponded = false
            });

            _zone3Questions.Add(new SpeedQuestion {
                triggerZ = 2000f,
                text = "Passed through Speed Tunnel Gate D!\n\nDistance = 150 m\nTime = 5 s\n\nWhat is your Speed?",
                answers = new string[] { "15 m/s", "25 m/s", "30 m/s" },
                correctIdx = 2, // 30 m/s
                answered = false,
                userResponded = false
            });

            _zone3Questions.Add(new SpeedQuestion {
                triggerZ = 2200f,
                text = "Passed through Speed Tunnel Gate E!\n\nDistance = 400 m\nTime = 10 s\n\nWhat is your Speed?",
                answers = new string[] { "20 m/s", "40 m/s", "60 m/s" },
                correctIdx = 1, // 40 m/s
                answered = false,
                userResponded = false
            });

            // Spawn entrance sign
            Vector3 gatePos = _driver.worldBuilder.GetRoadPosition(1210f);
            Vector3 gateTangent = _driver.worldBuilder.GetRoadTangent(1210f);
            SpawnHolographicGate("Zone3_Entrance", gatePos, gateTangent, NeonCyan, "SPEED TUNNEL");

            SpawnInstructionBoard("ZONE 3: SPEED TUNNEL\n\nSelect answer buttons using your Laser Pointer.\nDo not stop! Learning happens while driving.", NeonCyan);

            // Spawn gates
            foreach (var q in _zone3Questions)
            {
                Vector3 gPos = _driver.worldBuilder.GetRoadPosition(q.triggerZ);
                Vector3 gTan = _driver.worldBuilder.GetRoadTangent(q.triggerZ);
                SpawnHolographicGate($"Tunnel_Gate_{q.triggerZ}", gPos, gTan, NeonCyan, "SPEED GATE");
            }
        }

        private void UpdateZone3Gates(float playerZ)
        {
            // Check if player passed a gate trigger Z
            for (int i = 0; i < _zone3Questions.Count; i++)
            {
                var q = _zone3Questions[i];
                if (!q.answered && playerZ >= q.triggerZ)
                {
                    q.answered = true;
                    _zone3Questions[i] = q; // save state

                    // Show question board in front of car
                    ShowSpeedTunnelQuestion(q);
                }
            }

            // Move to Zone 4 after the final question has been responded to and the question board is dismissed
            if (_zone3Questions.Count > 0 && _zone3Questions[_zone3Questions.Count - 1].userResponded && _activeQuestionBoard == null)
            {
                StartZone4();
            }
        }

        private void ShowSpeedTunnelQuestion(SpeedQuestion q)
        {
            if (_activeQuestionBoard != null) Destroy(_activeQuestionBoard);

            // Spawn a board that mounts and moves relative to VRCar so player can interact while driving!
            _activeQuestionBoard = new GameObject("TunnelQuestionBoard");
            _activeQuestionBoard.transform.SetParent(_driver.transform, false);
            // Perfectly centered in front of the camera (windshield HUD position)
            _activeQuestionBoard.transform.localPosition = new Vector3(0f, 0.08f, 1.1f);
            _activeQuestionBoard.transform.localRotation = Quaternion.identity;

            // BACKGROUND PLATE REMOVED ENTIRELY as requested ("without any background")
            // BORDERS REMOVED ENTIRELY as requested ("without any background")

            // Text (Placed higher up at 0.45f and scaled down for clean raw layout with NO overlaps)
            var tmGo = new GameObject("QText");
            tmGo.transform.SetParent(_activeQuestionBoard.transform, false);
            tmGo.transform.localPosition = new Vector3(0f, 0.45f, -0.015f);
            tmGo.transform.localScale = Vector3.one * 0.008f; // Scaled up for VR readability
            
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
            tm.text = q.text.ToUpper(); // Uppercase white text!
            tm.fontSize = 48;
            tm.fontStyle = FontStyle.BoldAndItalic; // Slanted and bold!
            tm.anchor = TextAnchor.UpperCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white; // Pure white!

            // Build interactive answer buttons side-by-side (Placed at Y = -0.40f)
            for (int i = 0; i < q.answers.Length; i++)
            {
                int btnIdx = i;
                float xOffset = -0.55f + i * 0.55f;
                var btnGo = new GameObject($"AnsBtn_{i}");
                btnGo.transform.SetParent(_activeQuestionBoard.transform, false);
                btnGo.transform.localPosition = new Vector3(xOffset, -0.40f, -0.02f);
                btnGo.transform.localScale = Vector3.one * 0.32f; // scale down button

                var btn = btnGo.AddComponent<HolographicButton>();
                btn.width = 1.6f;
                btn.height = 0.5f;
                btn.buttonText = q.answers[i];
                btn.OnClick = () => {
                    HandleQuestionAnswer(btnIdx, q.correctIdx);
                };
            }
        }

        private void HandleQuestionAnswer(int selectedIdx, int correctIdx)
        {
            if (_activeQuestionBoard == null) return;

            // Mark the active question as responded
            for (int i = 0; i < _zone3Questions.Count; i++)
            {
                var q = _zone3Questions[i];
                if (q.answered && !q.userResponded)
                {
                    q.userResponded = true;
                    _zone3Questions[i] = q;
                    break;
                }
            }

            // Visual celebration in the cockpit
            var bgRenderer = _activeQuestionBoard.transform.Find("ButtonBG")?.GetComponent<Renderer>();
            var tm = _activeQuestionBoard.transform.Find("QText")?.GetComponent<TextMesh>();

            if (selectedIdx == correctIdx)
            {
                // Correct! Show uppercase feedback
                if (tm != null) tm.text = "CORRECT!\n\nEXCELLENT WORK DRIVER.\nKEEP HEADING DOWN THE HIGHWAY.";
                SpawnFlashFeedback(true);
            }
            else
            {
                // Wrong! Show uppercase feedback
                if (tm != null) tm.text = "INCORRECT!\n\nREMEMBER: SPEED = DISTANCE ÷ TIME.\nTRY THE NEXT GATE!";
                SpawnFlashFeedback(false);
            }

            // Remove answer buttons immediately
            foreach (Transform child in _activeQuestionBoard.transform)
            {
                if (child.name.StartsWith("AnsBtn_"))
                {
                    Destroy(child.gameObject);
                }
            }

            // Dismiss board after 2.5 seconds
            Destroy(_activeQuestionBoard, 2.5f);
        }

        private void SpawnFlashFeedback(bool correct)
        {
            // Spawn a visual screen splash light inside car
            var go = new GameObject("FlashFeedback");
            go.transform.SetParent(_driver.transform, false);
            go.transform.localPosition = new Vector3(0, 0.5f, 0.5f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = correct ? Color.green : Color.red;
            l.intensity = 3.5f;
            l.range = 5f;
            Destroy(go, 0.5f);
        }

        // ── 4. ZONE 4: SPEED EXPERIMENT AREA ──────────────────────────────────
        private void StartZone4()
        {
            currentState = LessonState.Zone4_ExperimentArea;
            ClearHolograms();
            SetWeatherForLevel(4);

            // Teleport car to experiment start lane (lateral offset Z = 2200m)
            _driver.Z = 2200f;
            _driver.Paused = true;
            _driver.automaticSpeedKmh = 0f;

            // Spawn Board containing speed trial selector buttons
            ShowExperimentSelectionHUD();
        }

        private void ShowExperimentSelectionHUD()
        {
            ClearHolograms();

            var board = SpawnStatsBoard("ZONE 4: SPEED EXPERIMENT AREA", NeonCyan);

            AddStatsLine(board, "Choose a Speed and see how far the car travels in 30s:", 0.22f, FontStyle.Bold, Color.white);

            // Spawns 5 speed buttons side-by-side
            for (int i = 0; i < _zone4Speeds.Length; i++)
            {
                int idx = i;
                float x = -0.66f + i * 0.33f;
                var btnGo = new GameObject($"SpeedBtn_{_zone4Speeds[i]}");
                btnGo.transform.SetParent(board.transform, false);
                btnGo.transform.localPosition = new Vector3(x, -0.08f, -0.03f);
                btnGo.transform.localScale = Vector3.one * 0.4f; // Uniform scale

                var btn = btnGo.AddComponent<HolographicButton>();
                btn.width = 0.75f;
                btn.height = 0.45f;
                btn.buttonText = $"{_zone4Speeds[i]} km/h";
                btn.OnClick = () => {
                    StartZone4Trial(idx);
                };
            }

            // Spawns continue button at bottom
            var btnGoCont = new GameObject("ContinueButton");
            btnGoCont.transform.SetParent(board.transform, false);
            btnGoCont.transform.localPosition = new Vector3(0f, -0.42f, -0.03f);
            var btnCont = btnGoCont.AddComponent<HolographicButton>();
            btnCont.width = 1.6f;
            btnCont.height = 0.45f;
            btnCont.buttonText = "Continue to Mission";
            btnCont.OnClick = () => {
                // Clear persistent trails
                foreach (var t in _trials)
                {
                    if (t.trailLineGo != null) Destroy(t.trailLineGo);
                }
                _trials.Clear();
                _driver.Paused = false;
                _driver.automaticSpeedKmh = null;
                StartZone5();
            };

            _zoneObjects.Add(board);
        }

        private void StartZone4Trial(int speedIndex)
        {
            _selectedSpeedIndex = speedIndex;
            float targetSpeedKmh = _zone4Speeds[speedIndex];

            // Reset player Z to experiment start line Z = 2200f
            _driver.Z = 2200f;
            _driver.Paused = false;
            _driver.automaticSpeedKmh = targetSpeedKmh;

            _zone4TrialTimer = 0f;
            _isZone4TrialActive = true;

            // Clear any hud
            ClearHolograms();

            // Spawn HUD overlay showing countdown
            SpawnInstructionBoard($"TRIAL ACTIVE: {targetSpeedKmh} km/h\n\nDriving for exactly 30 seconds...", _zone4Colors[speedIndex]);
        }

        private void UpdateZone4Experiment()
        {
            if (!_isZone4TrialActive) return;

            _zone4TrialTimer += Time.deltaTime;

            // Draw a temporary visual trail in real-time behind the player's car
            // using a simple LineRenderer drawn along the road
            DrawRealtimeTrail();

            if (_zone4TrialTimer >= 30.0f)
            {
                // Completed the 30-second trial!
                _isZone4TrialActive = false;
                _driver.Paused = true;
                _driver.automaticSpeedKmh = 0f;

                // Save persistent trial data
                SaveTrialData();

                // Re-open selection HUD
                ShowExperimentSelectionHUD();
            }
        }

        private void DrawRealtimeTrail()
        {
            // Redraws the line representing current trial progress
            // (We will save this cleanly upon completion)
        }

        private void SaveTrialData()
        {
            float targetSpeedKmh = _zone4Speeds[_selectedSpeedIndex];
            Color col = _zone4Colors[_selectedSpeedIndex];

            var data = new TrialData
            {
                speedKmh = targetSpeedKmh,
                startZ = 2200f,
                endZ = _driver.Z,
                color = col
            };

            // Spawn a visual glowing line mesh along the road representing this trial
            var lineGo = new GameObject($"TrailLine_{targetSpeedKmh}");
            lineGo.transform.SetParent(_hologramContainer.transform, false);

            var lr = lineGo.AddComponent<LineRenderer>();
            lr.startWidth = 0.4f;
            lr.endWidth = 0.4f;
            lr.sharedMaterial = CreateSolidUnlitMaterial(col);

            // Draw vertices along road snapping to terrain height
            int segments = 40;
            lr.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)(segments - 1);
                float currentZ = Mathf.Lerp(data.startZ, data.endZ, t);
                Vector3 roadPos = _driver.worldBuilder.GetRoadPosition(currentZ);
                
                // Offset sideways depending on which trial it is so they sit cleanly side-by-side!
                Vector3 tangent = _driver.worldBuilder.GetRoadTangent(currentZ);
                Vector3 perpendicular = new Vector3(-tangent.z, 0f, tangent.x).normalized;
                
                float lateralOffset = -3.0f + _selectedSpeedIndex * 1.5f;

                lr.SetPosition(i, roadPos + perpendicular * lateralOffset + Vector3.up * 0.15f);
            }

            data.trailLineGo = lineGo;
            _trials.Add(data);
        }

        // ── 5. ZONE 5: SPEED HERO MISSION (Z = 3200m to 4000m) ─────────────────
        private void StartZone5()
        {
            currentState = LessonState.Zone5_HeroMission_Quiz;
            ClearHolograms();
            SetWeatherForLevel(5);

            var board = SpawnStatsBoard("ZONE 5: SPEED HERO MISSION", NeonCyan);

            AddStatsLine(board, "EMERGENCY MISSION: Deliver package to medical base!", 0.25f, FontStyle.Bold, NeonRed);
            AddStatsLine(board, "Distance = 800 m", 0.08f);
            AddStatsLine(board, "Required Time = 20 seconds", -0.05f);
            AddStatsLine(board, "What Speed is required to deliver it in time?", -0.22f, FontStyle.Bold, Color.white);

            // Choice Buttons (Speed = Distance / Time -> 800 / 20 = 40 m/s = 144 km/h)
            float[] choiceSpeeds = { 20f, 40f, 60f }; // Correct is 40 m/s
            for (int i = 0; i < choiceSpeeds.Length; i++)
            {
                float spd = choiceSpeeds[i];
                float x = -0.5f + i * 0.5f;
                var btnGo = new GameObject($"ChoiceBtn_{spd}");
                btnGo.transform.SetParent(board.transform, false);
                btnGo.transform.localPosition = new Vector3(x, -0.45f, -0.03f);
                btnGo.transform.localScale = Vector3.one * 0.45f; // Uniform scale

                var btn = btnGo.AddComponent<HolographicButton>();
                btn.width = 0.9f;
                btn.height = 0.45f;
                btn.buttonText = $"{spd} m/s";
                btn.OnClick = () => {
                    SelectHeroSpeed(spd);
                };
            }

            _zoneObjects.Add(board);
        }

        private void SelectHeroSpeed(float speedVal)
        {
            _zone5ChosenSpeedVal = speedVal;
            ClearHolograms();

            // Set up driver for attempt
            _driver.Z = 3200f; // Mission start is Z = 3200m
            _driver.Paused = false;
            
            // Force selected speed automatically so player experiences their choice!
            _driver.automaticSpeedKmh = speedVal * 3.6f;

            _zone5Timer = 20.0f; // 20s countdown
            _isZone5Running = true;
            currentState = LessonState.Zone5_HeroMission_Driving;

            // Spawn visual Checkpoint at Z = 4000m (800m ahead)
            Vector3 cpPos = _driver.worldBuilder.GetRoadPosition(4000f);
            Vector3 cpTangent = _driver.worldBuilder.GetRoadTangent(4000f);
            SpawnHolographicGate("Zone5_MedicalBase", cpPos, cpTangent, NeonRed, "MEDICAL BASE");
        }

        private void UpdateZone5Mission(float playerZ)
        {
            if (!_isZone5Running) return;

            _zone5Timer -= Time.deltaTime;

            // Display floating emergency HUD in front of windshield
            SpawnInstructionBoard($"HERO MISSION ACTIVE\n\nSpeed = {_zone5ChosenSpeedVal} m/s\nTime Remaining = {Mathf.Max(0f, _zone5Timer):F2}s\nDistance to Base = {Mathf.Max(0f, 4000f - playerZ):F0}m", NeonRed);

            // Reached destination
            if (playerZ >= 4000f)
            {
                _isZone5Running = false;
                _driver.Paused = true;
                _driver.automaticSpeedKmh = 0f;

                if (_zone5Timer >= 0f)
                {
                    // Success!
                    StartCoroutine(TriggerSuccessCelebration());
                }
                else
                {
                    // Failed (Too slow!)
                    StartCoroutine(TriggerFailedSequence());
                }
            }
            // Ran out of time
            else if (_zone5Timer <= 0f)
            {
                _isZone5Running = false;
                _driver.Paused = true;
                _driver.automaticSpeedKmh = 0f;
                StartCoroutine(TriggerFailedSequence());
            }
        }

        private IEnumerator TriggerSuccessCelebration()
        {
            currentState = LessonState.Zone5_HeroMission_Celebration;
            ClearHolograms();
            SetWeatherForLevel(6); // Celebration weather

            // Spawn giant floating victory banner
            var board = SpawnStatsBoard("MISSION COMPLETE: HERO STATUS!", NeonGreen);
            AddStatsLine(board, "Awesome Drive! You reached the base in time.", 0.22f, FontStyle.Bold, Color.white);
            AddStatsLine(board, "Required Speed: " + _zone5RequiredSpeedMs + " m/s (800m / 20s)", 0.07f);
            AddStatsLine(board, $"Your Speed Choice: {_zone5ChosenSpeedVal} m/s", -0.08f, FontStyle.Normal, NeonGreen);
            AddStatsLine(board, "YOU HAVE MASTERED THE CONCEPT OF SPEED!", -0.23f, FontStyle.Bold, NeonCyan);

            // Light up forest! Spawn colorful lights blinking around car
            List<Light> spotlights = new List<Light>();
            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject($"DiscoLight_{i}");
                go.transform.position = _driver.transform.position + Random.insideUnitSphere * 12f + Vector3.up * 8f;
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = Random.ColorHSV(0f, 1f, 1f, 1f, 1f, 1f);
                l.intensity = 15f;
                l.range = 35f;
                l.spotAngle = 45f;
                spotlights.Add(l);
            }

            // Spawn celebration particles procedurally (rising glowing bubbles)
            List<GameObject> particles = new List<GameObject>();
            for (int i = 0; i < 40; i++)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                p.name = "VictoryParticle";
                p.transform.position = _driver.transform.position + Random.insideUnitSphere * 15f + Vector3.up * -1f;
                p.transform.localScale = Vector3.one * Random.Range(0.2f, 0.5f);
                Destroy(p.GetComponent<Collider>());
                
                Color particleColor = Random.ColorHSV(0f, 1f, 0.8f, 1f, 0.8f, 1f, 0.5f, 0.8f);
                p.GetComponent<Renderer>().sharedMaterial = CreateSolidUnlitMaterial(particleColor);
                
                particles.Add(p);
            }

            // Visual effects looping
            float el = 0f;
            while (el < 5.0f)
            {
                el += Time.deltaTime;
                // Rotate lights and float particles
                foreach (var sl in spotlights)
                {
                    sl.transform.Rotate(Vector3.up, Time.deltaTime * 60f);
                }
                foreach (var p in particles)
                {
                    p.transform.Translate(Vector3.up * Time.deltaTime * 3.5f, Space.World);
                }
                yield return null;
            }

            // Cleanup spotlights and particles
            foreach (var sl in spotlights) Destroy(sl.gameObject);
            foreach (var p in particles) Destroy(p);

            // Finish game! Let them drive normally
            AddStatsLine(board, "Press Continue to drive freely through the forest.", -0.48f, FontStyle.Normal, Color.gray);

            var btnGo = new GameObject("ContinueButton");
            btnGo.transform.SetParent(board.transform, false);
            btnGo.transform.localPosition = new Vector3(0f, -0.7f, -0.05f);
            var btn = btnGo.AddComponent<HolographicButton>();
            btn.width = 1.6f;
            btn.height = 0.5f;
            btn.buttonText = "Free Drive";
            btn.OnClick = () => {
                ClearHolograms();
                _driver.Paused = false;
                _driver.automaticSpeedKmh = null;
                currentState = LessonState.Completed;
            };

            _zoneObjects.Add(board);
        }

        private IEnumerator TriggerFailedSequence()
        {
            ClearHolograms();
            
            var board = SpawnStatsBoard("MISSION FAILED: OUT OF TIME!", NeonRed);

            AddStatsLine(board, "The package did not reach the base in time.", 0.22f, FontStyle.Normal, Color.white);
            AddStatsLine(board, "Formula check: Speed = Distance ÷ Time", 0.07f);
            AddStatsLine(board, "Distance = 800m  /  Time = 20s", -0.08f, FontStyle.Bold, NeonOrange);
            AddStatsLine(board, "Required Speed = 40 m/s (144 km/h)!", -0.23f, FontStyle.Bold, NeonGreen);
            AddStatsLine(board, $"Your Speed Choice was: {_zone5ChosenSpeedVal} m/s", -0.38f, FontStyle.Normal, NeonRed);

            // Spawns Retry button
            var btnGo = new GameObject("RetryBtn");
            btnGo.transform.SetParent(board.transform, false);
            btnGo.transform.localPosition = new Vector3(0f, -0.58f, -0.03f);
            var btn = btnGo.AddComponent<HolographicButton>();
            btn.width = 1.4f;
            btn.height = 0.45f;
            btn.buttonText = "Retry Mission";
            btn.OnClick = () => {
                StartZone5();
            };

            _zoneObjects.Add(board);
            yield return null;
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
                mat.SetFloat("_Surface", 1); // Transparent
                mat.SetFloat("_Blend", 0);   // Alpha
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
                mat.SetFloat("_Mode", 3); // Transparent
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

        private void SpawnHolographicGate(string name, Vector3 pos, Vector3 tangent, Color col, string label)
        {
            var gateRoot = new GameObject(name);
            
            // Snap gate position to road surface height exactly
            float roadY = pos.y - _driver.worldBuilder.cameraHeight + 0.08f;
            gateRoot.transform.position = new Vector3(pos.x, roadY, pos.z);

            if (tangent.sqrMagnitude > 0.01f)
                gateRoot.transform.rotation = Quaternion.LookRotation(tangent);

            gateRoot.transform.SetParent(_hologramContainer.transform);

            // Left Post (starts from Y=0, height=7m, made thin and elegant)
            var lPost = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lPost.transform.SetParent(gateRoot.transform, false);
            lPost.transform.localPosition = new Vector3(-6.2f, 3.5f, 0f);
            lPost.transform.localScale = new Vector3(0.15f, 3.5f, 0.15f);
            Destroy(lPost.GetComponent<Collider>());

            // Right Post
            var rPost = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rPost.transform.SetParent(gateRoot.transform, false);
            rPost.transform.localPosition = new Vector3(6.2f, 3.5f, 0f);
            rPost.transform.localScale = new Vector3(0.15f, 3.5f, 0.15f);
            Destroy(rPost.GetComponent<Collider>());

            // Cross Beam
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.transform.SetParent(gateRoot.transform, false);
            beam.transform.localPosition = new Vector3(0f, 7.0f, 0f);
            beam.transform.localScale = new Vector3(12.7f, 0.15f, 0.15f);
            Destroy(beam.GetComponent<Collider>());

            // Semi-transparent unlit neon material compatible with URP
            Color translucentCol = new Color(col.r, col.g, col.b, 0.5f);
            var mat = CreateTranslucentMaterial(translucentCol);
            lPost.GetComponent<Renderer>().sharedMaterial = mat;
            rPost.GetComponent<Renderer>().sharedMaterial = mat;
            beam.GetComponent<Renderer>().sharedMaterial = mat;

            // Sky beam removed completely to prevent blocking the road

            // Glowing Label Text in the middle of the arch (scaled nicely)
            var tmGo = new GameObject("LabelText");
            tmGo.transform.SetParent(gateRoot.transform, false);
            tmGo.transform.localPosition = new Vector3(0f, 7.5f, 0f); // just above the 7.0m crossbeam
            tmGo.transform.localScale = Vector3.one * 0.045f; // sleeker text scale

            var tm = tmGo.AddComponent<TextMesh>();
            Font builtinFont = GetSafeBuiltinFont();
            if (builtinFont != null)
            {
                tm.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = col;
                tmGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }
            tm.text = label;
            tm.fontSize = 80;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = col;

            _zoneObjects.Add(gateRoot);
        }

        private void SpawnInstructionBoard(string text, Color themeCol, System.Action onAccept = null, System.Action onReject = null)
        {
            bool hasButtons = (onAccept != null || onReject != null);
            
            // Check if we can reuse the existing HUD to avoid destroying and recreating every frame
            var existing = _driver.transform.Find("InstructionWindshieldHUD");
            if (existing != null && !hasButtons && existing.Find("AcceptBtn") == null && existing.Find("RejectBtn") == null)
            {
                var existingTm = existing.GetComponentInChildren<TextMesh>();
                if (existingTm != null)
                {
                    existingTm.text = text.ToUpper();
                    return;
                }
            }

            if (existing != null) Destroy(existing.gameObject);

            var hud = new GameObject("InstructionWindshieldHUD");
            hud.transform.SetParent(_driver.transform, false);
            
            // Push further away and scale down so it floats elegantly like a compact dashboard UI panel
            hud.transform.localPosition = new Vector3(0f, 0.08f, 1.1f);
            hud.transform.localRotation = Quaternion.identity;
            hud.transform.localScale = Vector3.one * 0.6f;

            // BACKGROUND PLATE REMOVED ENTIRELY as requested ("without any background")
            // BORDERS REMOVED ENTIRELY as requested ("without any background")

            // Label text (Scaled down to prevent bounds clipping)
            var tmGo = new GameObject("HUDText");
            tmGo.transform.SetParent(hud.transform, false);
            
            tmGo.transform.localPosition = new Vector3(0f, hasButtons ? 0.35f : 0.05f, -0.01f);
            tmGo.transform.localScale = Vector3.one * 0.009f; // Scaled up 3x for clear dashboard/windshield visibility
            
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
            tm.text = text.ToUpper(); // Uppercase white text!
            tm.fontSize = 48;
            tm.fontStyle = FontStyle.BoldAndItalic; // Slanted and bold!
            tm.anchor = hasButtons ? TextAnchor.UpperCenter : TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white; // Pure white!

            // Add Interactive Buttons if callbacks are supplied
            if (hasButtons)
            {
                // Pause vehicle while interacting with pop-up
                _driver.Paused = true;
                _driver.automaticSpeedKmh = 0f;

                // 1. Accept Button (Shifted left for breathing room)
                var acceptGo = new GameObject("AcceptBtn");
                acceptGo.transform.SetParent(hud.transform, false);
                acceptGo.transform.localPosition = new Vector3(-0.55f, -0.28f, -0.02f);
                acceptGo.transform.localScale = Vector3.one * 0.55f; // Uniform scale (increased for readability)

                var btnAcc = acceptGo.AddComponent<HolographicButton>();
                btnAcc.width = 1.6f;
                btnAcc.height = 0.7f; // Increased height to allow larger font fit
                btnAcc.buttonText = "Accept";
                btnAcc.OnClick = () => {
                    Destroy(hud);
                    _driver.Paused = false;
                    _driver.automaticSpeedKmh = null;
                    if (onAccept != null) onAccept.Invoke();
                };

                // 2. Reject Button (Shifted right for breathing room)
                var rejectGo = new GameObject("RejectBtn");
                rejectGo.transform.SetParent(hud.transform, false);
                rejectGo.transform.localPosition = new Vector3(0.55f, -0.28f, -0.02f);
                rejectGo.transform.localScale = Vector3.one * 0.55f; // Uniform scale (increased for readability)

                var btnRej = rejectGo.AddComponent<HolographicButton>();
                btnRej.width = 1.6f;
                btnRej.height = 0.7f; // Increased height to allow larger font fit
                btnRej.buttonText = "Reject";
                btnRej.OnClick = () => {
                    Destroy(hud);
                    _driver.Paused = false;
                    _driver.automaticSpeedKmh = null;
                    if (onReject != null) onReject.Invoke();
                };
            }
        }

        private GameObject SpawnStatsBoard(string title, Color themeCol)
        {
            var board = new GameObject("ReviewHoloBoard");
            board.transform.SetParent(_driver.transform, false); // Parent to camera!
            
            // Centered exactly 1.2m in front of the camera (moving and rotating with it)
            board.transform.localPosition = new Vector3(0f, 0.05f, 1.2f);
            board.transform.localRotation = Quaternion.identity;

            // BACKGROUND PLATE REMOVED ENTIRELY as requested ("without any background")
            // BORDERS REMOVED ENTIRELY as requested ("without any background")
            
            // Title Text (Scaled down for clean raw layout)
            var tmGo = new GameObject("TitleText");
            tmGo.transform.SetParent(board.transform, false);
            tmGo.transform.localPosition = new Vector3(0f, 0.58f, -0.02f);
            tmGo.transform.localScale = Vector3.one * 0.008f; // Increased for VR readability

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
            tm.text = title.ToUpper(); // Uppercase white text!
            tm.fontSize = 54;
            tm.fontStyle = FontStyle.BoldAndItalic; // Slanted and bold!
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white; // Pure white!

            return board;
        }

        private void AddStatsLine(GameObject board, string text, float yPos, FontStyle style = FontStyle.BoldAndItalic, Color? col = null)
        {
            var lineGo = new GameObject("Line_" + yPos);
            lineGo.transform.SetParent(board.transform, false);
            // Symmetrical text line layout with proper vertical positions
            lineGo.transform.localPosition = new Vector3(0f, yPos, -0.02f);
            lineGo.transform.localScale = Vector3.one * 0.007f; // Increased for legibility at 3 meters

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
            tm.text = text.ToUpper(); // Uppercase white text!
            tm.fontSize = 44;
            tm.fontStyle = style; // Respect custom style!
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = col ?? Color.white;
        }

        private void ClearWindshieldHUD()
        {
            if (_driver != null)
            {
                var old = _driver.transform.Find("InstructionWindshieldHUD");
                if (old != null) Destroy(old.gameObject);
            }
        }

        private void ClearHolograms()
        {
            ClearWindshieldHUD();
            foreach (var go in _zoneObjects)
            {
                if (go != null) Destroy(go);
            }
            _zoneObjects.Clear();

            // Clean up the ghost car whenever transitioning or restarting levels/zones
            _isReplayingGhost = false;
            if (_ghostCar != null)
            {
                Destroy(_ghostCar);
            }
        }

        private void SpawnZMarkers()
        {
            // Spawn floating Z markers every 100m along the road (100m to 4000m)
            for (float z = 100f; z <= 4000f; z += 100f)
            {
                // Skip if it's a major level checkpoint/gate to prevent overlapping
                if (z == 500f || z == 1200f || z == 2200f || z == 3200f || z == 4000f)
                    continue;

                Vector3 pos = _driver.worldBuilder.GetRoadPosition(z);
                Vector3 tangent = _driver.worldBuilder.GetRoadTangent(z);
                
                // Spawn a floating distance sign
                GameObject marker = new GameObject($"ZMarker_{z}m");
                float roadY = pos.y - _driver.worldBuilder.cameraHeight + 0.08f;
                marker.transform.position = new Vector3(pos.x, roadY + 4f, pos.z); // float 4m above road
                
                if (tangent.sqrMagnitude > 0.01f)
                    marker.transform.rotation = Quaternion.LookRotation(tangent);
                
                marker.transform.SetParent(_hologramContainer.transform);

                // Add simple floating text
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
                
                _zoneObjects.Add(marker);
            }
        }

        private void SetWeatherForLevel(int level)
        {
            // Set up ambient fog and directional light to create weather transitions
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            
            // Find main directional light
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
                case 1: // Morning (Discover Speed) - Made clear
                    RenderSettings.fogColor = new Color(0.7f, 0.85f, 0.95f);
                    RenderSettings.fogDensity = 0.002f;
                    if (mainLight != null)
                    {
                        mainLight.color = new Color(1f, 0.95f, 0.9f);
                        mainLight.intensity = 1.0f;
                    }
                    break;
                case 2: // Afternoon (Same Distance / Faster or Slower) - Made clear
                    RenderSettings.fogColor = new Color(0.9f, 0.95f, 1f);
                    RenderSettings.fogDensity = 0.001f;
                    if (mainLight != null)
                    {
                        mainLight.color = new Color(1f, 1f, 1f);
                        mainLight.intensity = 1.3f;
                    }
                    break;
                case 3: // Twilight / Speed Tunnel - Made clear
                    RenderSettings.fogColor = new Color(0.2f, 0.1f, 0.35f);
                    RenderSettings.fogDensity = 0.004f;
                    if (mainLight != null)
                    {
                        mainLight.color = new Color(0.6f, 0.5f, 0.8f);
                        mainLight.intensity = 0.5f;
                    }
                    break;
                case 4: // Sunset / Experiment Area - Made clear
                    RenderSettings.fogColor = new Color(0.8f, 0.35f, 0.2f);
                    RenderSettings.fogDensity = 0.003f;
                    if (mainLight != null)
                    {
                        mainLight.color = new Color(1.0f, 0.5f, 0.3f);
                        mainLight.intensity = 0.7f;
                    }
                    break;
                case 5: // Storm / Hero Mission - Made clear
                    RenderSettings.fogColor = new Color(0.15f, 0.18f, 0.22f);
                    RenderSettings.fogDensity = 0.005f;
                    if (mainLight != null)
                    {
                        mainLight.color = new Color(0.4f, 0.45f, 0.5f);
                        mainLight.intensity = 0.3f;
                    }
                    break;
                case 6: // Celebration / Success - Made clear
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
            ClearHolograms();
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
