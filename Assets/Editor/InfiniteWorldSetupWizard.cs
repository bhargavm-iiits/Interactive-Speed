using UnityEngine;
using UnityEditor;
using InfiniteWorld;

namespace InfiniteWorldEditor
{
    /// <summary>
    /// Setup Wizard — Static 5 km World Edition.
    /// Window → Infinite World → Setup Wizard
    /// </summary>
    public class InfiniteWorldSetupWizard : EditorWindow
    {
        private const string LAYERS_PATH = "Assets/MicroVerse-Extras/Terrain Textures/Layers";

        private int   _seed        = 42;
        private float _speed       = 28f;
        private float _worldLength = 5000f;
        private float _maxHeight   = 18f;

        private TerrainLayer _grass1, _grass2, _gravel, _asphalt, _soil, _scrubs;
        private bool _layersFound;
        private Vector2 _scroll;

        [MenuItem("Window/Infinite World/Setup Wizard")]
        public static void Open()
        {
            var w = GetWindow<InfiniteWorldSetupWizard>(false, "Infinite World Setup", true);
            w.minSize = new Vector2(420f, 480f);
            w.AutoDetect();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("🌿  5 km Countryside Environment", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Builds a 5 km road through rolling terrain at scene start.\nNo streaming — everything loads in ~2 seconds.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(10);

            // World settings
            Sep("World Settings");
            _worldLength = EditorGUILayout.FloatField("World Length (m)", _worldLength);
            _maxHeight   = EditorGUILayout.FloatField("Max Hill Height (m)", _maxHeight);
            _speed       = EditorGUILayout.Slider("Camera Speed (m/s)", _speed, 5f, 100f);
            EditorGUILayout.LabelField($"  ≈ {_speed * 3.6f:F0} km/h", EditorStyles.miniLabel);

            // Terrain layers
            EditorGUILayout.Space(8);
            Sep("Terrain Layers  (MicroVerse-Extras)");
            EditorGUILayout.HelpBox(
                _layersFound ? "✓ All layers detected automatically."
                             : "⚠ Assign layers manually or click Auto-Detect.",
                _layersFound ? MessageType.Info : MessageType.Warning);

            _grass1  = Obj("Grass 1",  _grass1);
            _grass2  = Obj("Grass 2",  _grass2);
            _gravel  = Obj("Gravel",   _gravel);
            _asphalt = Obj("Asphalt",  _asphalt);
            _soil    = Obj("Soil",     _soil);
            _scrubs  = Obj("Scrubs",   _scrubs);

            if (GUILayout.Button("Auto-Detect Layers")) AutoDetect();

            EditorGUILayout.Space(14);
            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.3f);
            if (GUILayout.Button("▶  BUILD 5 KM WORLD", GUILayout.Height(46)))
                Build();
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndScrollView();
        }

        private void Build()
        {
            // Cannot modify scene during play mode
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Stop Play Mode First",
                    "Please press the Play button to STOP play mode before building the world.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Build 5 km World",
                "Clears ALL existing StaticWorldBuilders and rebuilds the world in the Scene view.\n\nYou will see the terrain, road and forest appear immediately.\n\nProceed?",
                "Build!", "Cancel"))
                return;

            Undo.SetCurrentGroupName("Build 5km World");
            int group = Undo.GetCurrentGroup();

            // ── Destroy ALL StaticWorldBuilder GameObjects (fix duplicates) ──────
            foreach (var swb in Object.FindObjectsByType<StaticWorldBuilder>(
                         FindObjectsSortMode.None))
            {
                Undo.DestroyObjectImmediate(swb.gameObject);
            }

            // ── Remove other old world objects ──────────────────────────────
            foreach (var n in new[]{ "InfiniteWorldManager","InfiniteRoadSystem",
                "RoadsidePropSpawner","OptimizationManager","AtmosphericFog",
                "DaylightCycle","WorldTerrain","RoadMesh","Forest","Trees","PlayerCar" })
                DestroyIfExists(n);

            // ── Strip all missing-script components from every GameObject ───
            RemoveMissingScripts();

            // ── Fix the source .mat files on disk permanently first ─────────
            FixSourceMaterials();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ── Camera setup ───────────────────────────────────────────────────
            if (Camera.main != null)
            {
                Camera.main.transform.position = new Vector3(0f, 5f, 10f);
                Camera.main.transform.rotation = Quaternion.Euler(4f, 0f, 0f);
                Camera.main.farClipPlane  = 3000f;
                Camera.main.nearClipPlane = 0.3f;
                Camera.main.fieldOfView   = 72f;

                // Remove stale components
                RemoveComponent<Vehicle.AutoDriveCamera>(Camera.main.gameObject);
                RemoveComponent<StraightLineDriver>(Camera.main.gameObject);
                RemoveComponent<Vehicle.FollowCamera>(Camera.main.gameObject);
            }

            // ── Lighting ───────────────────────────────────────────────────────
            var sun = Object.FindFirstObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
                sun.color      = new Color(1f, 0.92f, 0.77f);
                sun.intensity  = 1.4f;
                sun.shadows    = LightShadows.Soft;
                sun.shadowStrength = 0.72f;
            }

            // ── Fog ────────────────────────────────────────────────────────────
            RenderSettings.fog        = true;
            RenderSettings.fogMode    = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0012f;
            RenderSettings.fogColor   = new Color(0.60f, 0.68f, 0.74f);

            // ── StaticWorldBuilder ─────────────────────────────────────────────
            var builderGO = new GameObject("StaticWorldBuilder");
            Undo.RegisterCreatedObjectUndo(builderGO, "Create StaticWorldBuilder");
            var builder = Undo.AddComponent<StaticWorldBuilder>(builderGO);

            builder.worldLength   = _worldLength;
            builder.maxHeight     = _maxHeight;
            builder.cameraSpeed   = _speed;
            builder.seed          = _seed;
            builder.layerGrass1   = _grass1;
            builder.layerGrass2   = _grass2;
            builder.layerGravel   = _gravel;
            builder.layerAsphalt  = _asphalt;
            builder.layerSoil     = _soil;
            builder.layerScrubs   = _scrubs;

            // ── Setup persistent Road Materials so they survive Play Mode ──────
            if (!AssetDatabase.IsValidFolder("Assets/InfiniteWorld"))
                AssetDatabase.CreateFolder("Assets", "InfiniteWorld");
            if (!AssetDatabase.IsValidFolder("Assets/InfiniteWorld/Materials"))
                AssetDatabase.CreateFolder("Assets/InfiniteWorld", "Materials");

            string asphaltPath = "Assets/InfiniteWorld/Materials/RoadAsphalt.mat";
            string linePath = "Assets/InfiniteWorld/Materials/RoadLine.mat";

            var asphaltMat = AssetDatabase.LoadAssetAtPath<Material>(asphaltPath);
            if (asphaltMat == null)
            {
                asphaltMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                asphaltMat.color = new Color(0.14f, 0.14f, 0.14f);
                asphaltMat.SetFloat("_Smoothness", 0.1f);
                AssetDatabase.CreateAsset(asphaltMat, asphaltPath);
            }

            var lineMat = AssetDatabase.LoadAssetAtPath<Material>(linePath);
            if (lineMat == null)
            {
                lineMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                lineMat.color = new Color(0.97f, 0.93f, 0.78f);
                AssetDatabase.CreateAsset(lineMat, linePath);
            }

            builder.asphaltMaterial = asphaltMat;
            builder.laneMarkingMaterial = lineMat;

            // Auto-assign Oak tree prefab
            builder.oakTreePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ALP_Assets/Big Oak Tree FREE/Prefabs/OakBigTree01_pr.prefab");
            if (builder.oakTreePrefab == null)
                Debug.LogWarning("[InfiniteWorld] Could not find OakBigTree01_pr.prefab " +
                    "at Assets/ALP_Assets/Big Oak Tree FREE/Prefabs/");
            else
                Debug.Log("[InfiniteWorld] Oak tree prefab assigned successfully.");

            // ── Build immediately in Edit mode — world appears in Scene view now ──
            EditorUtility.DisplayProgressBar("Building 5 km World",
                "Generating terrain, road and forest…", 0.1f);
            try   { builder.BuildNow(); }
            finally { EditorUtility.ClearProgressBar(); }

            // ── Save TerrainData as a persistent asset so it survives Play Mode ──
            var terrainGo = GameObject.Find("WorldTerrain");
            if (terrainGo != null)
            {
                var terrain = terrainGo.GetComponent<Terrain>();
                if (terrain != null && terrain.terrainData != null)
                {
                    string tdPath = "Assets/InfiniteWorld/TerrainData.asset";
                    AssetDatabase.DeleteAsset(tdPath);
                    AssetDatabase.CreateAsset(terrain.terrainData, tdPath);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[InfiniteWorld] TerrainData saved permanently to disk.");
                }
            }

            // ── Force URP materials on every oak tree (bypasses broken TVE shader) ──
            EditorUtility.DisplayProgressBar("Building 5 km World",
                "Applying green/brown tree materials…", 0.92f);
            try   { OverrideOakMaterials(); }
            finally { EditorUtility.ClearProgressBar(); }

            // Mark scene dirty so Unity knows it changed (edit mode only)
            if (!Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager
                    .MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager
                    .GetActiveScene());
            }
            // ── Wind ───────────────────────────────────────────────────────────
            if (Object.FindFirstObjectByType<WindZone>() == null)
            {
                var wGO = new GameObject("WindZone");
                Undo.RegisterCreatedObjectUndo(wGO, "WindZone");
                var wind = wGO.AddComponent<WindZone>();
                wind.mode = WindZoneMode.Directional;
                wind.windMain = 0.3f;
                wind.windTurbulence = 0.12f;
            }

            Undo.CollapseUndoOperations(group);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            EditorUtility.DisplayDialog("Done! 🌿",
                "Scene ready!\n\n" +
                "► Press PLAY — a loading screen will appear for ~2 seconds, then the 5 km countryside road environment will be fully visible.\n\n" +
                "[Space]  pause\n[W/S]    speed\n[RMB]    look around",
                "Play!");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void AutoDetect()
        {
            _grass1  = Load("Grass 01");
            _grass2  = Load("Grass 02");
            _gravel  = Load("Gravel 01");
            _asphalt = Load("Asphalt 01");
            _soil    = Load("Soil 01");
            _scrubs  = Load("Scrubs 01");
            _layersFound = _grass1 != null && _asphalt != null;
            Repaint();
        }

        private TerrainLayer Load(string n)
            => AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{LAYERS_PATH}/{n}.terrainlayer");

        private static TerrainLayer Obj(string label, TerrainLayer val)
            => (TerrainLayer)EditorGUILayout.ObjectField(label, val, typeof(TerrainLayer), false);

        private static void Sep(string t)
        {
            EditorGUILayout.LabelField(t, EditorStyles.boldLabel);
            var r = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            EditorGUILayout.Space(2);
        }

        private static void DestroyIfExists(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) Undo.DestroyObjectImmediate(go);
        }

        private static void RemoveComponent<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c != null) Undo.DestroyObjectImmediate(c);
        }

        // ── Missing-script remover ─────────────────────────────────────────────

        [MenuItem("Tools/Remove Missing Scripts From Scene")]
        public static void RemoveMissingScriptsMenu()
        {
            int removed = RemoveMissingScripts();
            EditorUtility.DisplayDialog("Missing Scripts Removed",
                $"Removed {removed} missing script reference(s) from the scene.", "OK");
        }

        /// <summary>
        /// Walks every root GameObject (and all children) and strips any
        /// component whose script reference is null (the 'Missing Script' case).
        /// Returns the total number of missing components removed.
        /// </summary>
        private static int RemoveMissingScripts()
        {
            int total = 0;
            // Include inactive objects so nothing is missed
            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in allObjects)
            {
                // Only process scene objects, skip assets/prefabs
                if (!go.scene.isLoaded) continue;

                var so = new SerializedObject(go);
                var components = so.FindProperty("m_Component");
                int removed = 0;

                for (int i = components.arraySize - 1; i >= 0; i--)
                {
                    var elem = components.GetArrayElementAtIndex(i);
                    var comp = elem.FindPropertyRelative("component").objectReferenceValue;
                    if (comp == null)
                    {
                        components.DeleteArrayElementAtIndex(i);
                        removed++;
                    }
                }

                if (removed > 0)
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    total += removed;
                    Debug.Log($"[MissingScripts] Removed {removed} missing component(s) " +
                              $"from '{go.name}'");
                }
            }
            return total;
        }
        // \u2500\u2500 Oak material override \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        [MenuItem("Tools/Fix Oak Tree Materials (URP)")]
        public static void FixOakMaterialsMenu()
        {
            // Fix the source .mat files too (for future prefab instantiations)
            FixSourceMaterials();
            // Then override any already-instantiated trees in the scene
            int n = OverrideOakMaterials();
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Oak Materials Fixed",
                $"Applied URP green/brown materials to {n} renderer(s) in the scene.\n\n" +
                "Rebuild the world if needed: Window → Infinite World → Setup Wizard", "OK");
        }

        /// <summary>
        /// Creates two fresh URP materials (leaf = green, bark = brown) and
        /// stamps them onto every Renderer inside the "Forest" GameObject.
        /// Returns the number of renderers updated.
        /// </summary>
        private static int OverrideOakMaterials()
        {
            const string MAT = "Assets/ALP_Assets/Big Oak Tree FREE/Models/Materials/";

            var leafMat = AssetDatabase.LoadAssetAtPath<Material>(MAT + "Branches001.mat");
            var barkMat = AssetDatabase.LoadAssetAtPath<Material>(MAT + "Trunk01.mat");
            var billboardMat = AssetDatabase.LoadAssetAtPath<Material>(MAT + "BillboardBigOak01.mat");

            if (leafMat == null || barkMat == null)
            {
                Debug.LogError("[OakFix] Cannot load oak tree materials from disk path: " + MAT);
                return 0;
            }

            // ── Apply to every renderer in Forest ─────────────────────────────
            var forest = GameObject.Find("Forest");
            if (forest == null)
            {
                Debug.LogWarning("[OakFix] No 'Forest' GameObject found in scene. " +
                    "Run the Setup Wizard to build the world first.");
                return 0;
            }

            int count = 0;
            foreach (var r in forest.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;

                // Identify by material name or renderer name keywords
                string mname = r.sharedMaterial != null
                    ? r.sharedMaterial.name.ToLowerInvariant() : "";
                string rname = r.gameObject.name.ToLowerInvariant();

                bool isBillboard = mname.Contains("billboard") || rname.Contains("billboard");
                bool isLeaf = mname.Contains("branch") || mname.Contains("leaf")
                           || rname.Contains("branch") || rname.Contains("leaf");

                if (isBillboard)
                {
                    r.sharedMaterial = billboardMat;
                }
                else
                {
                    r.sharedMaterial = isLeaf ? leafMat : barkMat;
                }
                count++;
            }

            Debug.Log($"[OakFix] Overrode {count} renderer(s) with persistent on-disk materials.");
            return count;
        }

        /// <summary>
        /// Also patches the source .mat files on disk so future Instantiate()
        /// calls pick up correct shaders instead of pink TVE ones.
        /// </summary>
        private static void FixSourceMaterials()
        {
            const string MAT  = "Assets/ALP_Assets/Big Oak Tree FREE/Models/Materials/";
            const string TEX  = "Assets/ALP_Assets/Big Oak Tree FREE/Models/Textures/";
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) return;

            // Branches (leaves)
            PatchMat(MAT + "Branches001.mat", urpLit,
                AssetDatabase.LoadAssetAtPath<Texture2D>(TEX + "BranchesOak001.tif"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(TEX + "BranchesOak001_N.png"),
                new Color(0.25f, 0.55f, 0.12f), isLeaf: true);

            // Trunk
            PatchMat(MAT + "Trunk01.mat", urpLit,
                AssetDatabase.LoadAssetAtPath<Texture2D>(TEX + "barkOak001.png"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(TEX + "barkOak001_N.png"),
                new Color(0.38f, 0.24f, 0.12f), isLeaf: false);

            // Billboard
            PatchMat(MAT + "BillboardBigOak01.mat", urpLit,
                AssetDatabase.LoadAssetAtPath<Texture2D>(TEX + "BigOakBillboard01.tif"),
                null,
                new Color(0.25f, 0.55f, 0.12f), isLeaf: true);

            // Ground (just brown dirt)
            PatchMat(MAT + "Ground.mat", urpLit, null, null,
                new Color(0.38f, 0.28f, 0.18f), isLeaf: false);
        }

        private static void PatchMat(string path, Shader shader, Texture2D albedo,
                                     Texture2D normal, Color color, bool isLeaf)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { Debug.LogWarning($"[OakFix] Not found: {path}"); return; }
            mat.shader = shader;
            if (albedo != null) mat.SetTexture("_BaseMap", albedo);
            if (normal != null) mat.SetTexture("_BumpMap",  normal);
            mat.SetColor("_BaseColor",  color);
            mat.SetFloat("_Smoothness", isLeaf ? 0.08f : 0.05f);
            mat.SetFloat("_Metallic",   0f);
            if (isLeaf)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff",    0.35f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull",      0f);
                mat.renderQueue = 2450;
            }
            else
            {
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_AlphaClip", 0f);
                mat.SetFloat("_Cull",      2f);
                mat.renderQueue = -1;
            }
            EditorUtility.SetDirty(mat);
        }
    }
}
