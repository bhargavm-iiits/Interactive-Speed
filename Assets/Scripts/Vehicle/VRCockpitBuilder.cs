using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Vehicle
{
    /// <summary>
    /// Procedural VR Cockpit Builder
    /// ══════════════════════════════════════════════════════════
    ///
    /// Builds the ENTIRE car interior at runtime from Unity primitives.
    /// No external models required. Hierarchy created programmatically.
    ///
    /// Interior design based on modern EV/performance car reference:
    ///  – D-shaped flat-bottom steering wheel (carbon fibre style)
    ///  – Minimalist dashboard with dual gauges + gear display
    ///  – Driver seat with headrest
    ///  – Windshield + A-pillars
    ///  – Door panels (left + right)
    ///  – Rear-view mirror
    ///  – Side mirrors (left + right)
    ///  – Pedal box (accelerator + brake)
    ///  – Interior ambient lighting
    ///  – World-space Canvas dashboard UI
    ///
    /// After building, all component references on VRCarController and
    /// supporting scripts are auto-wired.
    ///
    /// ══════════════════════════════════════════════════════════
    /// ATTACH THIS TO THE VR CAR ROOT GAMEOBJECT and hit Play.
    /// ══════════════════════════════════════════════════════════
    /// </summary>
    public class VRCockpitBuilder : MonoBehaviour
    {
        // ── Wiring targets ─────────────────────────────────────────────────────
        [Header("Auto-Wire Targets")]
        public VRCarController    carController;
        public VRCockpitRig       cockpitRig;
        public VRCockpitInput     cockpitInput;
        public VRSteeringWheel    steeringWheelScript;
        public VRDashboard        dashboard;
        public VRImmersionEffects immersionEffects;
        public VRAudioManager     audioManager;

        [Header("Build Settings")]
        [Tooltip("Scale factor for the entire cockpit. 1 = real-world scale.")]
        public float scale = 1f;
        [Tooltip("Position of the cockpit root relative to car body.")]
        public Vector3 cockpitOffset = new Vector3(0f, 0.3f, 0.1f);
        [Tooltip("Additional local rotation offset applied to the real steering wheel model so it faces the driver correctly.")]
        public Vector3 realWheelRotationOffset = Vector3.zero;
        [Tooltip("Target visual diameter of the real steering wheel in meters.")]
        public float realWheelDiameter = 0.36f;

        // ── Palette ────────────────────────────────────────────────────────────
        private static readonly Color ColBodyPanel      = new Color(0.12f, 0.12f, 0.14f);  // near black
        private static readonly Color ColCarbonFibre    = new Color(0.18f, 0.18f, 0.18f);  // dark grey
        private static readonly Color ColLeather        = new Color(0.10f, 0.08f, 0.08f);  // very dark brown-black
        private static readonly Color ColChrome         = new Color(0.82f, 0.82f, 0.85f);  // silver
        private static readonly Color ColGlass          = new Color(0.55f, 0.75f, 0.85f, 0.25f);  // tinted glass
        private static readonly Color ColDashGlow       = new Color(0.08f, 0.45f, 0.9f);   // blue glow
        private static readonly Color ColSeat           = new Color(0.08f, 0.08f, 0.12f);  // dark blue-black
        private static readonly Color ColGaugeBack      = new Color(0.05f, 0.05f, 0.08f);  // near-black gauge face
        private static readonly Color ColGaugeText      = new Color(0.9f,  0.9f,  0.9f);   // white text
        private static readonly Color ColNeedle         = new Color(1.0f,  0.35f, 0.0f);   // orange needle

        // ── Built references (exposed for runtime access) ──────────────────────
        [HideInInspector] public Transform cockpitRoot;
        [HideInInspector] public Transform steeringWheelTransform;
        [HideInInspector] public Transform acceleratorPedal;
        [HideInInspector] public Transform brakePedal;
        [HideInInspector] public Transform speedNeedle;
        [HideInInspector] public Transform rpmNeedle;
        [HideInInspector] public Light     interiorLight;

        // Materials for runtime updates
        private Material _dashboardMat;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            BuildCockpit();
            WireComponents();

            // Ensure StraightLineDriver is enabled so that camera sitting alignment and driving logic execute
            var driver = FindFirstObjectByType<InfiniteWorld.StraightLineDriver>();
            if (driver != null)
            {
                driver.enabled = true;
                Debug.Log("[VRCockpitBuilder] Automatically enabled StraightLineDriver script.");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // MAIN BUILD ENTRY
        // ══════════════════════════════════════════════════════════════════════

        private void BuildCockpit()
        {
            // We only build the steering wheel as requested (Cockpit_Root and other panels deleted)
            cockpitRoot = transform;

            // BuildCarBody();
            // BuildWindshield();
            // BuildDashboard();
            // BuildSteeringColumn();
            BuildSteeringWheel();
            // BuildPedalBox();
            // BuildDriverSeat();
            // BuildDoorPanels();
            // BuildMirrors();
            // BuildDashboardUI();
            // BuildInteriorLighting();
        }

        // ══════════════════════════════════════════════════════════════════════
        // CAR BODY SHELL (interior visible surfaces)
        // ══════════════════════════════════════════════════════════════════════

        private void BuildCarBody()
        {
            // Floor
            CreateBox("Floor", cockpitRoot,
                pos: new Vector3(0f, -0.55f, 0.2f),
                size: new Vector3(1.6f, 0.04f, 3.0f),
                col: ColBodyPanel);

            // Roof
            CreateBox("Roof", cockpitRoot,
                pos: new Vector3(0f, 1.0f, 0.1f),
                size: new Vector3(1.6f, 0.05f, 2.2f),
                col: ColBodyPanel);

            // Left wall (driver side - we use left-hand drive)
            CreateBox("Wall_Left", cockpitRoot,
                pos: new Vector3(-0.78f, 0.2f, 0.1f),
                size: new Vector3(0.04f, 1.5f, 2.8f),
                col: ColBodyPanel);

            // Right wall (passenger side)
            CreateBox("Wall_Right", cockpitRoot,
                pos: new Vector3(0.78f, 0.2f, 0.1f),
                size: new Vector3(0.04f, 1.5f, 2.8f),
                col: ColBodyPanel);

            // Firewall (front wall between engine bay and cabin)
            CreateBox("Firewall", cockpitRoot,
                pos: new Vector3(0f, 0.15f, 0.8f),
                size: new Vector3(1.6f, 1.4f, 0.04f),
                col: ColBodyPanel);

            // Rear shelf
            CreateBox("RearShelf", cockpitRoot,
                pos: new Vector3(0f, 0.3f, -1.3f),
                size: new Vector3(1.5f, 0.05f, 0.4f),
                col: ColBodyPanel);
        }

        // ══════════════════════════════════════════════════════════════════════
        // WINDSHIELD
        // ══════════════════════════════════════════════════════════════════════

        private void BuildWindshield()
        {
            // A-pillars
            var aL = CreateBox("APillar_Left", cockpitRoot,
                pos: new Vector3(-0.62f, 0.5f, 0.75f),
                size: new Vector3(0.06f, 0.8f, 0.06f),
                col: ColBodyPanel);
            aL.localRotation = Quaternion.Euler(25f, 0f, 0f);

            var aR = CreateBox("APillar_Right", cockpitRoot,
                pos: new Vector3(0.62f, 0.5f, 0.75f),
                size: new Vector3(0.06f, 0.8f, 0.06f),
                col: ColBodyPanel);
            aR.localRotation = Quaternion.Euler(25f, 0f, 0f);

            // Windshield glass (quad)
            var ws = CreateBox("Windshield", cockpitRoot,
                pos: new Vector3(0f, 0.6f, 0.8f),
                size: new Vector3(1.15f, 0.7f, 0.015f),
                col: ColGlass);
            ws.localRotation = Quaternion.Euler(22f, 0f, 0f);

            // Make glass transparent
            SetTransparent(ws.GetComponent<Renderer>(), ColGlass);
        }

        // ══════════════════════════════════════════════════════════════════════
        // DASHBOARD
        // ══════════════════════════════════════════════════════════════════════

        private void BuildDashboard()
        {
            // Main dash body — curved look via angled boxes
            var dashRoot = new GameObject("Dashboard_Root");
            dashRoot.transform.SetParent(cockpitRoot, false);
            dashRoot.transform.localPosition = new Vector3(0f, 0.1f, 0.65f);

            // Lower dash slab
            var lower = CreateBox("Dash_Lower", dashRoot.transform,
                pos: new Vector3(0f, -0.12f, 0f),
                size: new Vector3(1.5f, 0.18f, 0.38f),
                col: ColBodyPanel);
            _dashboardMat = lower.GetComponent<Renderer>().material;

            // Upper dash shelf
            var upper = CreateBox("Dash_Upper", dashRoot.transform,
                pos: new Vector3(0f, 0.06f, -0.06f),
                size: new Vector3(1.5f, 0.06f, 0.22f),
                col: ColCarbonFibre);
            upper.localRotation = Quaternion.Euler(-12f, 0f, 0f);

            // Chrome trim strip across dash
            CreateBox("Dash_ChromeTrim", dashRoot.transform,
                pos: new Vector3(0f, -0.02f, 0.04f),
                size: new Vector3(1.5f, 0.012f, 0.012f),
                col: ColChrome);

            // Center console
            CreateBox("CenterConsole", cockpitRoot,
                pos: new Vector3(0f, -0.3f, 0.2f),
                size: new Vector3(0.22f, 0.35f, 1.1f),
                col: ColCarbonFibre);

            // Gauge pods (two round circles for speedo + RPM)
            BuildGaugePod("Speedo_Pod", dashRoot.transform, new Vector3(-0.47f, 0f, 0.02f));
            BuildGaugePod("RPM_Pod",    dashRoot.transform, new Vector3(-0.23f, 0f, 0.02f));
        }

        private void BuildGaugePod(string name, Transform parent, Vector3 localPos)
        {
            // Gauge bezel
            var bezel = CreateCylinder(name + "_Bezel", parent,
                pos: localPos,
                radius: 0.095f, height: 0.015f,
                col: ColChrome);

            // Gauge face
            var face = CreateCylinder(name + "_Face", parent,
                pos: localPos + new Vector3(0f, 0.01f, 0f),
                radius: 0.085f, height: 0.005f,
                col: ColGaugeBack);

            // Position needles
            if (name.StartsWith("Speedo"))
            {
                speedNeedle = CreateNeedle(name + "_Needle", parent, localPos + new Vector3(0f, 0.018f, 0f));
            }
            else
            {
                rpmNeedle = CreateNeedle(name + "_Needle", parent, localPos + new Vector3(0f, 0.018f, 0f));
            }
        }

        private Transform CreateNeedle(string name, Transform parent, Vector3 localPos)
        {
            var needleGO  = new GameObject(name);
            needleGO.transform.SetParent(parent, false);
            needleGO.transform.localPosition = localPos;
            // Needle mesh: thin long box pointing forward (+Z)
            var segment = CreateBox("Segment", needleGO.transform,
                pos: new Vector3(0f, 0.002f, 0.035f),
                size: new Vector3(0.007f, 0.003f, 0.07f),
                col: ColNeedle);
            // Pivot is at origin (rear of needle)
            return needleGO.transform;
        }

        // ══════════════════════════════════════════════════════════════════════
        // STEERING COLUMN + WHEEL
        // ══════════════════════════════════════════════════════════════════════

        private void BuildSteeringColumn()
        {
            // Column tube
            var col = CreateCylinder("SteeringColumn", cockpitRoot,
                pos: new Vector3(-0.35f, -0.08f, 0.5f),
                radius: 0.022f, height: 0.35f,
                col: ColBodyPanel);
            col.localRotation = Quaternion.Euler(35f, 0f, 0f);

            // Column shroud
            var shroud = CreateBox("ColumnShroud", cockpitRoot,
                pos: new Vector3(-0.35f, -0.02f, 0.52f),
                size: new Vector3(0.09f, 0.075f, 0.22f),
                col: ColBodyPanel);
            shroud.localRotation = Quaternion.Euler(35f, 0f, 0f);
        }

        private void BuildSteeringWheel()
        {
            // Delete any duplicate steering wheel objects pre-existing in the VRCar hierarchy in the editor
            // to avoid duplicates in Play Mode
            var toDestroy = new System.Collections.Generic.List<GameObject>();
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("Meshy_AI_Three_Spoke_Sport_Ste") || child.name.Contains("StearingWheel"))
                {
                    toDestroy.Add(child.gameObject);
                }
            }
            foreach (var go in toDestroy)
            {
                DestroyImmediate(go);
            }

            // Wheel pivot — this is what the VRSteeringWheel script rotates
            var pivotGO = new GameObject("SteeringWheel_Pivot");
            pivotGO.transform.SetParent(transform, false);
            pivotGO.transform.localPosition = new Vector3(3f, 1.1f, 8f);
            pivotGO.transform.localRotation = Quaternion.Euler(-90f, 0f, -180f);
            steeringWheelTransform = pivotGO.transform;

            bool assetLoaded = false;

#if UNITY_EDITOR
            // Try loading high-quality steering wheel asset
            GameObject fbxModel = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ALP_Assets/StearingWheel/Meshy_AI_Three_Spoke_Sport_Ste_0602143311_texture.fbx");
            if (fbxModel != null)
            {
                // Create a container under pivot
                GameObject container = new GameObject("SteeringWheel_VisualContainer");
                container.transform.SetParent(pivotGO.transform, false);
                container.transform.localPosition = Vector3.zero;
                container.transform.localRotation = Quaternion.identity;

                // Instantiate FBX under container
                GameObject fbxInstance = Instantiate(fbxModel, container.transform);
                fbxInstance.transform.localPosition = Vector3.zero;
                fbxInstance.transform.localRotation = Quaternion.identity;
                fbxInstance.transform.localScale = new Vector3(50f, 50f, 50f); // Lock to your verified inspector scale!

                // Disable and destroy any Animator component that would lock local rotations
                var animator = fbxInstance.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.enabled = false;
                    DestroyImmediate(animator);
                }

                // Check for renderers and apply materials
                Renderer[] renderers = fbxInstance.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Material wheelMat = null;
#if UNITY_EDITOR
                    wheelMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/ALP_Assets/StearingWheel/Black.mat");
#endif
                    if (wheelMat == null)
                    {
                        wheelMat = CreateRealSteeringWheelMaterial();
                    }

                    foreach (var r in renderers)
                    {
                        r.sharedMaterial = wheelMat;
                    }
                }
                assetLoaded = true;
                Debug.Log("[VRCockpitBuilder] Successfully loaded real steering wheel asset at locked scale (50) and facing the driver!");
            }
#endif

            if (!assetLoaded)
            {
                // Fallback to original procedural D-Shape Rim
                float rimRadius    = 0.175f;
                int   rimSegments  = 14;          // segments for top ~280° arc
                float arcStartDeg  = -140f;       // start (bottom-left of arc)
                float arcEndDeg    =  140f;       // end (bottom-right)
                float arcRange     = arcEndDeg - arcStartDeg;

                var rimRoot = new GameObject("Rim");
                rimRoot.transform.SetParent(pivotGO.transform, false);

                for (int i = 0; i < rimSegments; i++)
                {
                    float a0 = arcStartDeg + (arcRange / rimSegments) * i;
                    float a1 = arcStartDeg + (arcRange / rimSegments) * (i + 1);
                    float aMid = (a0 + a1) * 0.5f * Mathf.Deg2Rad;

                    Vector3 segPos = new Vector3(Mathf.Sin(aMid) * rimRadius, Mathf.Cos(aMid) * rimRadius, 0f);
                    float segAngle = -(a0 + a1) * 0.5f;

                    var seg = CreateCylinder($"RimSeg_{i}", rimRoot.transform,
                        pos: segPos,
                        radius: 0.016f,
                        height: rimRadius * 2f * Mathf.Sin(arcRange * Mathf.Deg2Rad / rimSegments / 2f) * 2.1f,
                        col: i % 2 == 0 ? ColCarbonFibre : ColLeather);
                    seg.localRotation = Quaternion.Euler(0f, 0f, segAngle + 90f);
                }

                // ── Flat Bottom Bar ────────────────────────────────────────────────
                float bottomY = Mathf.Cos(arcEndDeg * Mathf.Deg2Rad) * rimRadius;
                float bottomX = Mathf.Sin(arcEndDeg * Mathf.Deg2Rad) * rimRadius;

                CreateCylinder("RimBottom", pivotGO.transform,
                    pos: new Vector3(0f, bottomY, 0f),
                    radius: 0.016f,
                    height: bottomX * 2f,
                    col: ColLeather).localRotation = Quaternion.Euler(0f, 0f, 90f);

                // ── Spokes ────────────────────────────────────────────────────────
                BuildSpoke("Spoke_Left",  pivotGO.transform, -0.09f, rimRadius);
                BuildSpoke("Spoke_Right", pivotGO.transform,  0.09f, rimRadius);

                // ── Hub / Airbag ───────────────────────────────────────────────────
                CreateBox("Hub", pivotGO.transform,
                    pos: new Vector3(0f, 0f, 0f),
                    size: new Vector3(0.14f, 0.09f, 0.025f),
                    col: ColLeather);

                CreateBox("HubAccent", pivotGO.transform,
                    pos: new Vector3(0f, 0f, -0.013f),
                    size: new Vector3(0.10f, 0.05f, 0.005f),
                    col: ColChrome);

                BuildControlCluster("Cluster_Left",  pivotGO.transform, new Vector3(-0.085f, 0.01f, -0.01f));
                BuildControlCluster("Cluster_Right", pivotGO.transform, new Vector3( 0.085f, 0.01f, -0.01f));

                CreateBox("BottomTrim", pivotGO.transform,
                    pos: new Vector3(0f, bottomY - 0.005f, 0f),
                    size: new Vector3(0.08f, 0.01f, 0.018f),
                    col: ColChrome);
                
                Debug.Log("[VRCockpitBuilder] Custom steering wheel asset not found or not in editor; fell back to procedural wheel.");
            }
        }

        private void BuildSpoke(string name, Transform parent, float xOffset, float rimRadius)
        {
            var spoke = CreateBox(name, parent,
                pos: new Vector3(xOffset, rimRadius * 0.35f, 0f),
                size: new Vector3(0.028f, rimRadius * 0.65f, 0.018f),
                col: ColCarbonFibre);
            spoke.localRotation = Quaternion.Euler(0f, 0f, xOffset < 0 ? -15f : 15f);
        }

        private void BuildControlCluster(string name, Transform parent, Vector3 localPos)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;

            // Button plate
            CreateBox("Plate", root.transform,
                pos: Vector3.zero,
                size: new Vector3(0.06f, 0.04f, 0.008f),
                col: ColBodyPanel);

            // Two small chrome buttons
            CreateCylinder("Btn1", root.transform,
                pos: new Vector3(-0.015f, 0.005f, -0.005f),
                radius: 0.006f, height: 0.006f,
                col: ColChrome).localRotation = Quaternion.Euler(90f, 0f, 0f);

            CreateCylinder("Btn2", root.transform,
                pos: new Vector3(0.015f, 0.005f, -0.005f),
                radius: 0.006f, height: 0.006f,
                col: ColChrome).localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // ══════════════════════════════════════════════════════════════════════
        // PEDAL BOX
        // ══════════════════════════════════════════════════════════════════════

        private void BuildPedalBox()
        {
            // Pedal floor plate
            CreateBox("PedalFloor", cockpitRoot,
                pos: new Vector3(-0.35f, -0.52f, 0.58f),
                size: new Vector3(0.35f, 0.02f, 0.32f),
                col: ColBodyPanel);

            // Brake pedal
            brakePedal = BuildPedal("BrakePedal", new Vector3(-0.26f, -0.42f, 0.62f));
            // Accelerator pedal
            acceleratorPedal = BuildPedal("AccelPedal", new Vector3(-0.40f, -0.43f, 0.58f));
        }

        private Transform BuildPedal(string name, Vector3 localPos)
        {
            // Pivot point (hinge at top of pedal)
            var pivot = new GameObject(name + "_Pivot");
            pivot.transform.SetParent(cockpitRoot, false);
            pivot.transform.localPosition = localPos;

            // Pedal face
            var face = CreateBox(name + "_Face", pivot.transform,
                pos: new Vector3(0f, -0.065f, 0.02f),
                size: new Vector3(0.08f, 0.13f, 0.012f),
                col: ColChrome);
            face.localRotation = Quaternion.Euler(-15f, 0f, 0f);

            // Pedal rubber grip lines
            for (int i = 0; i < 4; i++)
            {
                CreateBox($"Grip_{i}", pivot.transform,
                    pos: new Vector3(0f, -0.04f - i * 0.022f, 0.027f),
                    size: new Vector3(0.07f, 0.005f, 0.004f),
                    col: ColBodyPanel);
            }

            return pivot.transform;
        }

        // ══════════════════════════════════════════════════════════════════════
        // DRIVER SEAT
        // ══════════════════════════════════════════════════════════════════════

        private void BuildDriverSeat()
        {
            var seatRoot = new GameObject("Seat_Driver");
            seatRoot.transform.SetParent(cockpitRoot, false);
            seatRoot.transform.localPosition = new Vector3(-0.35f, -0.5f, -0.1f);

            // Seat base
            CreateBox("SeatBase", seatRoot.transform,
                pos: new Vector3(0f, 0f, 0f),
                size: new Vector3(0.52f, 0.1f, 0.52f),
                col: ColSeat);

            // Seat back
            var back = CreateBox("SeatBack", seatRoot.transform,
                pos: new Vector3(0f, 0.4f, -0.24f),
                size: new Vector3(0.52f, 0.7f, 0.1f),
                col: ColSeat);
            back.localRotation = Quaternion.Euler(-8f, 0f, 0f);

            // Headrest
            CreateBox("Headrest", seatRoot.transform,
                pos: new Vector3(0f, 0.8f, -0.27f),
                size: new Vector3(0.28f, 0.22f, 0.12f),
                col: ColSeat);

            // Side bolsters
            CreateBox("Bolster_Left", seatRoot.transform,
                pos: new Vector3(-0.25f, 0.12f, 0f),
                size: new Vector3(0.05f, 0.22f, 0.5f),
                col: ColSeat);
            CreateBox("Bolster_Right", seatRoot.transform,
                pos: new Vector3( 0.25f, 0.12f, 0f),
                size: new Vector3(0.05f, 0.22f, 0.5f),
                col: ColSeat);

            // Seat stitching accent (thin chrome strip)
            CreateBox("SeatAccent", seatRoot.transform,
                pos: new Vector3(0f, 0.38f, -0.2f),
                size: new Vector3(0.45f, 0.005f, 0.005f),
                col: ColChrome);
        }

        // ══════════════════════════════════════════════════════════════════════
        // DOOR PANELS
        // ══════════════════════════════════════════════════════════════════════

        private void BuildDoorPanels()
        {
            BuildDoorPanel("DoorPanel_Left",  -0.74f,  true);
            BuildDoorPanel("DoorPanel_Right",  0.74f, false);
        }

        private void BuildDoorPanel(string name, float xPos, bool isDriver)
        {
            var root = new GameObject(name);
            root.transform.SetParent(cockpitRoot, false);

            // Main panel
            CreateBox("Panel", root.transform,
                pos: new Vector3(xPos, 0.1f, 0f),
                size: new Vector3(0.04f, 0.85f, 1.1f),
                col: ColBodyPanel);

            // Armrest
            CreateBox("Armrest", root.transform,
                pos: new Vector3(xPos + (isDriver ? 0.04f : -0.04f), -0.05f, 0.05f),
                size: new Vector3(0.1f, 0.055f, 0.4f),
                col: ColLeather);

            // Door handle (chrome)
            CreateBox("Handle", root.transform,
                pos: new Vector3(xPos + (isDriver ? 0.06f : -0.06f), 0.0f, 0.32f),
                size: new Vector3(0.12f, 0.025f, 0.018f),
                col: ColChrome);

            // Speaker grille
            BuildSpeakerGrille(root.transform, xPos, isDriver);

            // Chrome trim strip
            CreateBox("DoorTrim", root.transform,
                pos: new Vector3(xPos + (isDriver ? 0.03f : -0.03f), 0.32f, 0f),
                size: new Vector3(0.005f, 0.01f, 1.0f),
                col: ColChrome);
        }

        private void BuildSpeakerGrille(Transform parent, float xPos, bool isDriver)
        {
            float side = isDriver ? 0.042f : -0.042f;
            // Small speaker dots (5 in a row)
            for (int i = 0; i < 5; i++)
            {
                CreateCylinder($"Speaker_{i}", parent,
                    pos: new Vector3(xPos + side, -0.22f, -0.28f + i * 0.045f),
                    radius: 0.008f, height: 0.008f,
                    col: ColBodyPanel).localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // MIRRORS
        // ══════════════════════════════════════════════════════════════════════

        private void BuildMirrors()
        {
            // Rear-view mirror (inside, top of windshield)
            BuildRearviewMirror();
            // Side mirrors (outside body — visible through glass)
            BuildSideMirror("Mirror_Left",  new Vector3(-0.72f, 0.42f, 0.72f), false);
            BuildSideMirror("Mirror_Right", new Vector3( 0.72f, 0.42f, 0.72f), true);
        }

        private void BuildRearviewMirror()
        {
            var mirrorRoot = new GameObject("RearviewMirror");
            mirrorRoot.transform.SetParent(cockpitRoot, false);
            mirrorRoot.transform.localPosition = new Vector3(0f, 0.92f, 0.6f);

            // Mount bracket
            CreateBox("Bracket", mirrorRoot.transform,
                pos: new Vector3(0f, 0f, 0f),
                size: new Vector3(0.02f, 0.07f, 0.02f),
                col: ColBodyPanel);

            // Mirror face (wide, thin box — acts as mirror surface)
            var face = CreateBox("MirrorFace", mirrorRoot.transform,
                pos: new Vector3(0f, -0.06f, 0f),
                size: new Vector3(0.28f, 0.07f, 0.01f),
                col: ColChrome);
            face.localRotation = Quaternion.Euler(10f, 0f, 0f);

            // Add VRMirrorCamera for reflections
            var mc = mirrorRoot.AddComponent<VRMirrorCamera>();
            mc.mirrorRenderer = face.GetComponent<Renderer>();
            mc.renderSize     = 128;
            mc.renderEveryNFrames = 4;
        }

        private void BuildSideMirror(string name, Vector3 localPos, bool isRight)
        {
            var root = new GameObject(name);
            root.transform.SetParent(cockpitRoot, false);
            root.transform.localPosition = localPos;

            // Mirror housing
            CreateBox("Housing", root.transform,
                pos: Vector3.zero,
                size: new Vector3(0.13f, 0.075f, 0.04f),
                col: ColBodyPanel);

            // Mirror surface
            var face = CreateBox("Face", root.transform,
                pos: new Vector3(isRight ? -0.06f : 0.06f, 0f, 0.005f),
                size: new Vector3(0.008f, 0.065f, 0.09f),
                col: ColChrome);
            face.localRotation = Quaternion.Euler(0f, isRight ? -12f : 12f, 0f);

            // Add VRMirrorCamera
            var mc = root.AddComponent<VRMirrorCamera>();
            mc.mirrorRenderer = face.GetComponent<Renderer>();
            mc.renderSize     = 64;
            mc.renderEveryNFrames = 6;
        }

        // ══════════════════════════════════════════════════════════════════════
        // DASHBOARD UI (World-Space Canvas)
        // ══════════════════════════════════════════════════════════════════════

        private void BuildDashboardUI()
        {
            var canvasGO = new GameObject("Dashboard_Canvas");
            canvasGO.transform.SetParent(cockpitRoot, false);
            canvasGO.transform.localPosition = new Vector3(0f, 0.16f, 0.67f);
            canvasGO.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);
            canvasGO.transform.localScale    = Vector3.one * 0.0012f;  // world-space scale

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rt = canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(900f, 200f);

            // Speed digital display (left)
            var speedGO = new GameObject("SpeedText");
            speedGO.transform.SetParent(canvasGO.transform, false);
            var speedText = speedGO.AddComponent<Text>();
            speedText.text      = "0";
            speedText.fontSize  = 72;
            speedText.color     = ColDashGlow;
            speedText.fontStyle = FontStyle.Bold;
            speedText.alignment = TextAnchor.MiddleCenter;
            speedText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var speedRT = speedGO.GetComponent<RectTransform>();
            speedRT.anchoredPosition = new Vector2(-280f, 30f);
            speedRT.sizeDelta        = new Vector2(200f, 100f);

            // km/h label
            var unitGO = new GameObject("UnitText");
            unitGO.transform.SetParent(canvasGO.transform, false);
            var unitText = unitGO.AddComponent<Text>();
            unitText.text      = "m/s";
            unitText.fontSize  = 22;
            unitText.color     = new Color(0.6f, 0.6f, 0.6f);
            unitText.alignment = TextAnchor.MiddleCenter;
            unitText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var unitRT = unitGO.GetComponent<RectTransform>();
            unitRT.anchoredPosition = new Vector2(-280f, -22f);
            unitRT.sizeDelta        = new Vector2(120f, 40f);

            // Gear display (center)
            var gearGO = new GameObject("GearText");
            gearGO.transform.SetParent(canvasGO.transform, false);
            var gearText = gearGO.AddComponent<Text>();
            gearText.text      = "D";
            gearText.fontSize  = 60;
            gearText.color     = new Color(0.2f, 1f, 0.4f);
            gearText.fontStyle = FontStyle.Bold;
            gearText.alignment = TextAnchor.MiddleCenter;
            gearText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var gearRT = gearGO.GetComponent<RectTransform>();
            gearRT.anchoredPosition = new Vector2(0f, 20f);
            gearRT.sizeDelta        = new Vector2(100f, 100f);

            // Mode label (D)
            var modeGO = new GameObject("ModeText");
            modeGO.transform.SetParent(canvasGO.transform, false);
            var modeText = modeGO.AddComponent<Text>();
            modeText.text      = "DRIVE";
            modeText.fontSize  = 18;
            modeText.color     = new Color(0.3f, 0.8f, 0.3f);
            modeText.alignment = TextAnchor.MiddleCenter;
            modeText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var modeRT = modeGO.GetComponent<RectTransform>();
            modeRT.anchoredPosition = new Vector2(0f, -28f);
            modeRT.sizeDelta        = new Vector2(120f, 40f);

            // Wire to VRDashboard if present
            if (dashboard != null)
            {
                dashboard.speedometerNeedle = speedNeedle;
                dashboard.rpmNeedle        = rpmNeedle;
                dashboard.speedText         = speedText;
                dashboard.gearText          = gearText;
                dashboard.modeText          = modeText;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // INTERIOR LIGHTING
        // ══════════════════════════════════════════════════════════════════════

        private void BuildInteriorLighting()
        {
            // Main ambient point light (warm white)
            var lightGO = new GameObject("InteriorLight");
            lightGO.transform.SetParent(cockpitRoot, false);
            lightGO.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            interiorLight = lightGO.AddComponent<Light>();
            interiorLight.type      = LightType.Point;
            interiorLight.color     = new Color(1.0f, 0.92f, 0.8f);
            interiorLight.intensity = 0.45f;
            interiorLight.range     = 2.5f;
            interiorLight.shadows   = LightShadows.None;  // no shadows inside for perf

            // Dashboard glow light (blue)
            var dashLightGO = new GameObject("DashLight");
            dashLightGO.transform.SetParent(cockpitRoot, false);
            dashLightGO.transform.localPosition = new Vector3(0f, 0.15f, 0.65f);
            var dashLight = dashLightGO.AddComponent<Light>();
            dashLight.type      = LightType.Point;
            dashLight.color     = ColDashGlow;
            dashLight.intensity = 0.3f;
            dashLight.range     = 0.8f;
            dashLight.shadows   = LightShadows.None;

            // Wire to immersion effects
            if (immersionEffects != null)
                immersionEffects.interiorLight = interiorLight;
        }

        // ══════════════════════════════════════════════════════════════════════
        // COMPONENT WIRING
        // ══════════════════════════════════════════════════════════════════════

        private void WireComponents()
        {
            if (carController == null)
                carController = GetComponent<VRCarController>();

            // Dashboard
            if (dashboard != null && carController != null)
            {
                dashboard.car          = carController;
                dashboard.transmission = GetComponent<VRAutomaticTransmission>();
                dashboard.speedometerNeedle = speedNeedle;
                dashboard.rpmNeedle         = rpmNeedle;
            }

            // Cockpit Input — pedal references
            if (cockpitInput != null)
            {
                cockpitInput.car                   = carController;
                cockpitInput.acceleratorPedalTransform = acceleratorPedal;
                cockpitInput.brakePedalTransform       = brakePedal;
                cockpitInput.immersionFX               = immersionEffects;
            }

            // Steering Wheel
            if (steeringWheelScript != null)
            {
                steeringWheelScript.car          = carController;
                steeringWheelScript.wheelVisual  = steeringWheelTransform;
                steeringWheelScript.cockpitAnchor = cockpitRoot;
            }

            // Cockpit Rig
            if (cockpitRig != null)
                cockpitRig.car = carController;

            // Immersion effects
            if (immersionEffects != null)
            {
                immersionEffects.car           = carController;
                immersionEffects.interiorLight = interiorLight;
                immersionEffects.dashboardRenderer = _dashboardMat != null
                    ? cockpitRoot.Find("Dashboard_Root/Dash_Lower")?.GetComponent<Renderer>()
                    : null;
            }

            // Audio manager
            if (audioManager != null)
                audioManager.car = carController;
        }

        // ══════════════════════════════════════════════════════════════════════
        // PRIMITIVE HELPERS
        // ══════════════════════════════════════════════════════════════════════

        private Transform CreateBox(string name, Transform parent,
                                    Vector3 pos, Vector3 size, Color col)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale    = size;
            ApplyMaterial(go.GetComponent<Renderer>(), col);
            // Remove collider from decorative parts
            DestroyImmediate(go.GetComponent<Collider>());
            return go.transform;
        }

        private Transform CreateCylinder(string name, Transform parent,
                                         Vector3 pos, float radius, float height, Color col)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale    = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            ApplyMaterial(go.GetComponent<Renderer>(), col);
            DestroyImmediate(go.GetComponent<Collider>());
            return go.transform;
        }

        private static void ApplyMaterial(Renderer r, Color col)
        {
            if (r == null) return;
            // Use URP/Lit shader — compatible with both Mobile and PC URP
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ??
                                   Shader.Find("Standard"));
            mat.color = col;

            // Metallic/smoothness for shiny parts
            if (col == ColChrome)
            {
                mat.SetFloat("_Metallic",   0.95f);
                mat.SetFloat("_Smoothness", 0.88f);
            }
            else if (col == ColCarbonFibre)
            {
                mat.SetFloat("_Metallic",   0.3f);
                mat.SetFloat("_Smoothness", 0.55f);
            }
            else if (col == ColLeather)
            {
                mat.SetFloat("_Metallic",   0.0f);
                mat.SetFloat("_Smoothness", 0.25f);
            }
            else if (col == ColDashGlow)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", col * 0.5f);
                mat.SetFloat("_Metallic",   0.1f);
                mat.SetFloat("_Smoothness", 0.7f);
            }
            else
            {
                mat.SetFloat("_Metallic",   0.05f);
                mat.SetFloat("_Smoothness", 0.4f);
            }
            r.material = mat;
        }

        private static void SetTransparent(Renderer r, Color col)
        {
            if (r == null) return;
            var mat = r.material;
            // URP transparent surface
            mat.SetFloat("_Surface", 1f);         // 0=Opaque, 1=Transparent
            mat.SetFloat("_Blend",   0f);         // Alpha blend
            mat.renderQueue = 3000;
            mat.color = col;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

#if UNITY_EDITOR
        private static Bounds GetLocalBounds(Transform targetLocalSpace, Renderer renderer)
        {
            Bounds worldBounds = renderer.bounds;
            Vector3 localMin = targetLocalSpace.InverseTransformPoint(worldBounds.min);
            Vector3 localMax = targetLocalSpace.InverseTransformPoint(worldBounds.max);
            
            Bounds localBounds = new Bounds(targetLocalSpace.InverseTransformPoint(worldBounds.center), Vector3.zero);
            localBounds.Encapsulate(localMin);
            localBounds.Encapsulate(localMax);
            return localBounds;
        }

        private static Material CreateRealSteeringWheelMaterial()
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");
            Material mat = new Material(litShader);

            Texture2D baseTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ALP_Assets/StearingWheel/Meshy_AI_Three_Spoke_Sport_Ste_0602143311_texture.png");
            Texture2D normalTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ALP_Assets/StearingWheel/Meshy_AI_Three_Spoke_Sport_Ste_0602143311_texture_normal.png");
            Texture2D metallicTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ALP_Assets/StearingWheel/Meshy_AI_Three_Spoke_Sport_Ste_0602143311_texture_metallic.png");
            Texture2D roughnessTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ALP_Assets/StearingWheel/Meshy_AI_Three_Spoke_Sport_Ste_0602143311_texture_roughness.png");
            Texture2D emissionTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ALP_Assets/StearingWheel/Meshy_AI_Three_Spoke_Sport_Ste_0602143311_texture_emission.png");

            if (baseTex != null)
            {
                mat.SetTexture("_BaseMap", baseTex);
                mat.SetTexture("_MainTex", baseTex);
            }
            if (normalTex != null)
            {
                mat.SetTexture("_BumpMap", normalTex);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (metallicTex != null)
            {
                mat.SetTexture("_MetallicGlossMap", metallicTex);
                mat.EnableKeyword("_METALLICGLOSSMAP");
                mat.SetFloat("_Metallic", 1.0f);
            }
            if (emissionTex != null)
            {
                mat.SetTexture("_EmissionMap", emissionTex);
                mat.SetColor("_EmissionColor", Color.white);
                mat.EnableKeyword("_EMISSION");
            }
            mat.SetFloat("_Smoothness", 0.5f);

            return mat;
        }
#endif
    }
}
