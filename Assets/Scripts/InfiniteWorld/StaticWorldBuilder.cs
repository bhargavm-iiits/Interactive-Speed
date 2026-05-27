using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace InfiniteWorld
{
    /// <summary>
    /// Builds a fixed 5 km countryside environment with:
    ///   - Gentle S-curve road
    ///   - Green rolling hills
    ///   - Procedural trees (no prefabs needed)
    ///   - Correct grass splatmap
    /// </summary>
    public class StaticWorldBuilder : MonoBehaviour
    {
        // ── World ─────────────────────────────────────────────────────────────
        [Header("World")]
        public float worldLength   = 5000f;
        public float worldWidth    = 1400f;
        public float maxHeight     = 18f;
        public int   heightmapRes  = 513;
        public int   alphamapRes   = 512;

        // ── Terrain Layers ────────────────────────────────────────────────────
        [Header("Terrain Layers")]
        public TerrainLayer layerGrass1;
        public TerrainLayer layerGrass2;
        public TerrainLayer layerGravel;
        public TerrainLayer layerAsphalt;
        public TerrainLayer layerSoil;
        public TerrainLayer layerScrubs;

        // ── Road ──────────────────────────────────────────────────────────────
        [Header("Road")]
        public float roadHalfWidth = 5.5f;
        public float flatHalfWidth = 12f;
        [Tooltip("Amplitude of the S-curve lateral offset (metres).")]
        public float curveAmplitude = 80f;
        public Material asphaltMaterial;
        public Material laneMarkingMaterial;

        // ── Forest ───────────────────────────────────────────────────
        [Header("Forest")]
        [Tooltip("Oak tree prefab from ALP_Assets/Big Oak Tree FREE/Prefabs.")]
        public GameObject oakTreePrefab;
        [Tooltip("Total number of forest clusters.")]
        public int   forestClusters  = 60;
        [Tooltip("Min trees per cluster.")]
        public int   clusterMin      = 20;
        [Tooltip("Max trees per cluster.")]
        public int   clusterMax      = 35;
        [Tooltip("Cluster spread radius in metres.")]
        public float clusterRadius   = 40f;
        [Tooltip("Min distance from road centre to start planting.")]
        public float treeMinDist     = 10f;
        [Tooltip("Max distance from road centre (forest depth).")]
        public float treeMaxDist     = 130f;
        [Tooltip("Min tree scale multiplier.")]
        public float treeScaleMin    = 0.7f;
        [Tooltip("Max tree scale multiplier.")]
        public float treeScaleMax    = 1.4f;

        // ── Camera ────────────────────────────────────────────────────────────
        [Header("Camera")]
        public float cameraHeight = 2.8f;
        public float cameraSpeed  = 28f;
        [Tooltip("Random seed — change for a different tree layout.")]
        public int   seed         = 42;

        // ── Internal state ────────────────────────────────────────────────────
        private Terrain     _terrain;
        private Vector3[]   _roadPath;
        private int         _pathSteps;
        private float       _pathStep = 5f;
        [SerializeField, HideInInspector]
        private bool        _built;      // true once world has been generated

        // Loading UI
        private string _msg      = "Initialising…";
        private float  _loadProgress = 0f;
        private bool   _loading  = true;



        // ── Road path query (used by camera driver) ───────────────────────────

        /// <summary>Returns the road centre position at world Z.</summary>
        public Vector3 GetRoadPosition(float wz)
        {
            if (_roadPath == null) return new Vector3(0, cameraHeight, wz);
            int i = Mathf.Clamp(Mathf.RoundToInt(wz / _pathStep), 0, _roadPath.Length - 1);
            return _roadPath[i];
        }

        /// <summary>Returns the forward tangent of the road at world Z.</summary>
        public Vector3 GetRoadTangent(float wz)
        {
            if (_roadPath == null || _roadPath.Length < 2) return Vector3.forward;
            int i  = Mathf.Clamp(Mathf.RoundToInt(wz / _pathStep), 0, _roadPath.Length - 2);
            return (_roadPath[i + 1] - _roadPath[i]).normalized;
        }

        // ── Road curve formula ────────────────────────────────────────────────

        private float RoadX(float z)
        {
            // Gentle S-curves: two overlapping sine waves
            return curveAmplitude * 0.6f * Mathf.Sin(z * 0.00125f)
                 + curveAmplitude * 0.4f * Mathf.Sin(z * 0.0031f + 1.4f);
        }

        // ── Build coroutine (runtime / Play mode) ────────────────────────────

        private void Start()
        {
            // If the wizard already built the world in Edit mode, skip.
            if (_built)
            {
                _loading = false;
                _terrain = GetComponentInChildren<Terrain>() ?? GameObject.Find("WorldTerrain")?.GetComponent<Terrain>();
                ComputeRoadPath();

                // Back-fill Y into road path now that terrain exists
                if (_terrain != null && _roadPath != null)
                {
                    for (int i = 0; i < _roadPath.Length; i++)
                    {
                        float wz = i * _pathStep;
                        float wx = _roadPath[i].x;
                        _roadPath[i].y = SampleTerrainAt(wx, wz) + cameraHeight;
                    }
                }
                return;
            }
            StartCoroutine(Build());
        }

        private IEnumerator Build()
        {
            yield return null;
            SetMsg("Planning road…",    0.00f); ComputeRoadPath(); yield return null;
            SetMsg("Creating terrain…", 0.05f); CreateTerrain();   yield return null;
            SetMsg("Sculpting hills…",  0.15f); ApplyHeightmap();  yield return null;
            SetMsg("Painting grass…",   0.38f); ApplySplatmap();   yield return null;
            SetMsg("Adding cover…",     0.55f); ApplyGrassDetail();yield return null;
            SetMsg("Building road…",    0.65f); BuildRoadMesh();   yield return null;
            SetMsg("Planting trees…",   0.75f); PlantTrees();      yield return null;
            SetMsg("Camera…",           0.95f); PositionCamera();
            _loading = false; _built = true;
            Debug.Log("[StaticWorldBuilder] 5 km world ready.");
        }

        // ── BuildNow: synchronous — called by the wizard in Edit mode ──────────

        /// <summary>
        /// Builds the entire world synchronously. Safe to call from Editor scripts.
        /// Result is visible in the Scene view immediately.
        /// </summary>
        public void BuildNow()
        {
            _loading = false;           // suppress loading overlay
            ComputeRoadPath();
            CreateTerrain();
            ApplyHeightmap();
            ApplySplatmap();
            ApplyGrassDetail();
            BuildRoadMesh();
            PlantTrees();
            PositionCamera();
            _built = true;
            Debug.Log("[StaticWorldBuilder] World built synchronously.");
        }

        // ── 0. Road path ──────────────────────────────────────────────────────

        private void ComputeRoadPath()
        {
            _pathSteps = Mathf.CeilToInt(worldLength / _pathStep) + 1;
            _roadPath  = new Vector3[_pathSteps];
            for (int i = 0; i < _pathSteps; i++)
            {
                float z = i * _pathStep;
                _roadPath[i] = new Vector3(RoadX(z), 0f, z); // Y set after terrain
            }
        }

        // ── 1. Terrain ────────────────────────────────────────────────────────

        private void CreateTerrain()
        {
            var td = new TerrainData
            {
                heightmapResolution = heightmapRes,
                alphamapResolution  = alphamapRes,
                baseMapResolution   = 1024,
                size = new Vector3(worldWidth, maxHeight + 8f, worldLength)
            };
            td.SetDetailResolution(512, 8);
            td.terrainLayers = BuildLayers();

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "WorldTerrain";
            // Centre terrain on X=0
            go.transform.position = new Vector3(-worldWidth * 0.5f, 0f, 0f);

            _terrain = go.GetComponent<Terrain>();
            _terrain.drawInstanced       = true;
            _terrain.heightmapPixelError = 5f;
            _terrain.basemapDistance     = 2000f;
            _terrain.detailObjectDistance = 100f;
        }

        private TerrainLayer[] BuildLayers()
        {
            var list = new List<TerrainLayer>();
            // Order: 0=Grass1  1=Grass2  2=Gravel  3=Asphalt  4=Soil  5=Scrubs
            list.Add(layerGrass1  ?? MakeLayer(new Color(0.30f, 0.58f, 0.18f), new Vector2(6, 6)));
            list.Add(layerGrass2  ?? MakeLayer(new Color(0.25f, 0.52f, 0.15f), new Vector2(7, 7)));
            list.Add(layerGravel  ?? MakeLayer(new Color(0.52f, 0.48f, 0.40f), new Vector2(4, 4)));
            list.Add(layerAsphalt ?? MakeLayer(new Color(0.15f, 0.15f, 0.15f), new Vector2(5, 5)));
            list.Add(layerSoil    ?? MakeLayer(new Color(0.46f, 0.35f, 0.22f), new Vector2(5, 5)));
            list.Add(layerScrubs  ?? MakeLayer(new Color(0.28f, 0.46f, 0.17f), new Vector2(8, 8)));
            return list.ToArray();
        }

        private TerrainLayer MakeLayer(Color c, Vector2 tile)
        {
            var tex = new Texture2D(8, 8);
            var cols = new Color[64];
            for (int i = 0; i < 64; i++) cols[i] = c;
            tex.SetPixels(cols); tex.Apply();
            return new TerrainLayer { diffuseTexture = tex, tileSize = tile };
        }

        // ── 2. Heightmap ──────────────────────────────────────────────────────

        private void ApplyHeightmap()
        {
            int res = heightmapRes;
            var h   = new float[res, res];

            for (int zi = 0; zi < res; zi++)
            {
                float zn = zi / (float)(res - 1);
                float wz = zn * worldLength;
                float rx = RoadX(wz); // road X at this Z

                for (int xi = 0; xi < res; xi++)
                {
                    float xn  = xi / (float)(res - 1);
                    float wx  = (xn - 0.5f) * worldWidth;
                    float dist = Mathf.Abs(wx - rx); // distance from road centre

                    // Gentle multi-octave hills — wider & softer than before
                    float raw  = Mathf.PerlinNoise(wx * 0.0008f + 10f, wz * 0.0008f + 20f) * 1.0f
                               + Mathf.PerlinNoise(wx * 0.0025f + 3f,  wz * 0.0025f + 7f)  * 0.30f
                               + Mathf.PerlinNoise(wx * 0.007f  + 6f,  wz * 0.007f  + 2f)  * 0.10f;
                    raw /= 1.40f;
                    raw  = Mathf.Pow(raw, 1.4f); // bias toward lower values (flatter plains)

                    // Wide smooth flat corridor around road (80 m flat, 140 m blend)
                    float flatBlend = Mathf.SmoothStep(0f, 1f, (dist - flatHalfWidth * 2f) / 140f);
                    h[zi, xi] = Mathf.Lerp(0.04f, raw, flatBlend);
                }
            }
            _terrain.terrainData.SetHeights(0, 0, h);

            // Back-fill Y into road path now that terrain exists
            for (int i = 0; i < _pathSteps; i++)
            {
                float wz = i * _pathStep;
                float wx = _roadPath[i].x;
                _roadPath[i].y = SampleTerrainAt(wx, wz) + cameraHeight;
            }
        }

        // ── 3. Splatmap ───────────────────────────────────────────────────────

        private void ApplySplatmap()
        {
            var td  = _terrain.terrainData;
            int res = alphamapRes;
            int n   = td.terrainLayers.Length;
            var map = new float[res, res, n];

            for (int zi = 0; zi < res; zi++)
            {
                float zn = zi / (float)(res - 1);
                float wz = zn * worldLength;
                float rx = RoadX(wz);

                for (int xi = 0; xi < res; xi++)
                {
                    float xn   = xi / (float)(res - 1);
                    float wx   = (xn - 0.5f) * worldWidth;
                    float dist = Mathf.Abs(wx - rx);

                    // ── Asphalt: sharp road band
                    float wAsph = Mathf.SmoothStep(roadHalfWidth + 0.8f, roadHalfWidth - 0.3f, dist);

                    // ── Gravel: narrow shoulder
                    float wGrav = Mathf.SmoothStep(flatHalfWidth + 2f, flatHalfWidth - 1f, dist)
                                  * (1f - wAsph);

                    // ── Grass dominates everything else (NO height-based soil)
                    float noiseG2 = Mathf.PerlinNoise(wx * 0.005f + 7f, wz * 0.005f + 3f);
                    float wG2 = noiseG2 * 0.45f * (1f - wAsph) * (1f - wGrav);
                    float wG1 = (1f - wAsph) * (1f - wGrav);  // base fill

                    float[] w = new float[Mathf.Max(n, 6)];
                    if (n > 0) w[0] = wG1;
                    if (n > 1) w[1] = wG2;
                    if (n > 2) w[2] = wGrav;
                    if (n > 3) w[3] = wAsph;
                    // layers 4 (Soil) and 5 (Scrubs) stay 0 — force pure green

                    float sum = 0f;
                    for (int k = 0; k < n; k++) sum += w[k];
                    if (sum < 0.0001f) sum = 1f;
                    for (int k = 0; k < n; k++) map[zi, xi, k] = w[k] / sum;
                }
            }
            td.SetAlphamaps(0, 0, map);
        }

        // ── 4. Grass detail ────────────────────────────────────────────────

        private void ApplyGrassDetail()
        {
            var td  = _terrain.terrainData;
            int res = td.detailResolution;

            // Bright lush green texture
            var tex = new Texture2D(8, 8);
            var cols = new Color[64];
            for (int i = 0; i < 64; i++)
                cols[i] = Color.Lerp(
                    new Color(0.22f, 0.55f, 0.12f),
                    new Color(0.30f, 0.68f, 0.18f),
                    (i % 3) / 2f);
            tex.SetPixels(cols); tex.Apply();

            td.detailPrototypes = new[]
            {
                new DetailPrototype
                {
                    prototypeTexture = tex,
                    renderMode       = DetailRenderMode.Grass,
                    healthyColor     = new Color(0.32f, 0.60f, 0.20f),
                    dryColor         = new Color(0.65f, 0.60f, 0.25f),
                    minWidth = 0.5f, maxWidth = 1.0f,
                    minHeight = 0.25f, maxHeight = 0.60f,
                    noiseSpread = 0.4f, usePrototypeMesh = false
                }
            };

            var map = new int[res, res];
            for (int zi = 0; zi < res; zi++)
            {
                float zn = zi / (float)(res - 1);
                float wz = zn * worldLength;
                float rx = RoadX(wz);
                for (int xi = 0; xi < res; xi++)
                {
                    float xn   = xi / (float)(res - 1);
                    float wx   = (xn - 0.5f) * worldWidth;
                    float dist = Mathf.Abs(wx - rx);
                    if (dist < flatHalfWidth + 5f) { map[zi, xi] = 0; continue; }
                    float n = Mathf.PerlinNoise(wx * 0.025f + 2f, wz * 0.025f + 1f);
                    map[zi, xi] = n > 0.38f ? Mathf.RoundToInt(n * 10f) : 0;
                }
            }
            td.SetDetailLayer(0, 0, 0, map);
        }

        // ── 5. Road mesh ──────────────────────────────────────────────────────

        private void BuildRoadMesh()
        {
            int steps = _pathSteps - 1;
            var verts = new List<Vector3>();
            var tris  = new List<int>();
            var uvs   = new List<Vector2>();
            var lV    = new List<Vector3>();
            var lT    = new List<int>();
            var lU    = new List<Vector2>();

            float uvAcc = 0f;

            for (int i = 0; i <= steps; i++)
            {
                float z   = i * _pathStep;
                float rx  = RoadX(z);
                float ry  = SampleTerrainAt(rx, z) + 0.03f;

                // Road tangent for proper perpendicular
                Vector3 fwd = i < steps
                    ? new Vector3(RoadX(z + _pathStep) - rx, 0, _pathStep).normalized
                    : Vector3.forward;
                Vector3 right = new Vector3(fwd.z, 0, -fwd.x); // perpendicular in XZ

                Vector3 l = new Vector3(rx, ry, z) - right * roadHalfWidth;
                Vector3 r = new Vector3(rx, ry, z) + right * roadHalfWidth;

                verts.Add(l); verts.Add(r);
                uvs.Add(new Vector2(0f, uvAcc)); uvs.Add(new Vector2(1f, uvAcc));
                uvAcc += _pathStep / 10f;

                if (i < steps)
                {
                    int b = i * 2;
                    tris.Add(b); tris.Add(b+2); tris.Add(b+1);
                    tris.Add(b+1); tris.Add(b+2); tris.Add(b+3);
                }
            }

            // Dashed centre line
            float dashOn = 8f, dashOff = 5f, dashW = 0.10f, d = 0f;
            bool on = true; int lb = 0;
            while (d < worldLength - dashOn)
            {
                if (on)
                {
                    float z0 = d, z1 = d + dashOn;
                    float x0 = RoadX(z0), x1 = RoadX(z1);
                    float y0 = SampleTerrainAt(x0, z0) + 0.045f;
                    float y1 = SampleTerrainAt(x1, z1) + 0.045f;
                    lV.Add(new Vector3(x0 - dashW, y0, z0)); lV.Add(new Vector3(x0 + dashW, y0, z0));
                    lV.Add(new Vector3(x1 - dashW, y1, z1)); lV.Add(new Vector3(x1 + dashW, y1, z1));
                    lU.Add(Vector2.zero); lU.Add(Vector2.right);
                    lU.Add(Vector2.up);   lU.Add(Vector2.one);
                    lT.Add(lb); lT.Add(lb+2); lT.Add(lb+1);
                    lT.Add(lb+1); lT.Add(lb+2); lT.Add(lb+3);
                    lb += 4;
                }
                d += on ? dashOn : dashOff; on = !on;
            }

            var root = new GameObject("RoadMesh");
            MakeSubMesh(root, "Asphalt", verts, tris, uvs, AsphaltMat());
            if (lV.Count > 0) MakeSubMesh(root, "Lines", lV, lT, lU, LineMat());
        }

        private void MakeSubMesh(GameObject parent, string name, List<Vector3> v, List<int> t,
                                  List<Vector2> u, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.SetUVs(0, u);
            mesh.RecalculateNormals();
            mf.sharedMesh = mesh;
            mr.sharedMaterial = mat;
        }

        private Material AsphaltMat()
        {
            if (asphaltMaterial != null) return asphaltMaterial;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = new Color(0.14f, 0.14f, 0.14f);
            m.SetFloat("_Smoothness", 0.1f);
            return m;
        }

        private Material LineMat()
        {
            if (laneMarkingMaterial != null) return laneMarkingMaterial;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.color = new Color(0.97f, 0.93f, 0.78f);
            return m;
        }

        // ── 6. Procedural Forest (Oak prefab) ──────────────────────────────

        private void PlantTrees()
        {
            if (oakTreePrefab == null)
            {
                Debug.LogWarning("[StaticWorldBuilder] oakTreePrefab is not assigned! " +
                    "Run the Setup Wizard to auto-assign it.");
                return;
            }

            var forestRoot = new GameObject("Forest");
            var rng = new System.Random(seed);
            int totalTrees = 0;

            for (int c = 0; c < forestClusters; c++)
            {
                // Cluster Z: spread evenly along the road
                float cz = 60f + (float)c / forestClusters * (worldLength - 120f);

                // Plant on BOTH sides for every cluster — max density
                for (int s = 0; s < 2; s++)
                {
                    float side  = (s == 0) ? 1f : -1f;
                    float cDist = treeMinDist + 8f
                                  + (float)rng.NextDouble() * (treeMaxDist - treeMinDist - 8f);
                    float cx    = RoadX(cz) + side * cDist;

                    int count = clusterMin + rng.Next(0, clusterMax - clusterMin + 1);

                    for (int t = 0; t < count; t++)
                    {
                        // sqrt distribution packs trees toward cluster centre
                        double ang  = rng.NextDouble() * System.Math.PI * 2.0;
                        double dist = System.Math.Sqrt(rng.NextDouble()) * clusterRadius;
                        float tx = cx + (float)(System.Math.Cos(ang) * dist);
                        float tz = cz + (float)(System.Math.Sin(ang) * dist);

                        // Exclude road corridor
                        if (Mathf.Abs(tx - RoadX(tz)) < treeMinDist) continue;

                        float ty  = SampleTerrainAt(tx, tz);
                        float scl = treeScaleMin
                                  + (float)rng.NextDouble() * (treeScaleMax - treeScaleMin);
                        float rot = (float)(rng.NextDouble() * 360.0);

                        // Instantiate oak prefab
                        var tree = (GameObject)UnityEngine.Object.Instantiate(
                            oakTreePrefab,
                            new Vector3(tx, ty, tz),
                            Quaternion.Euler(0f, rot, 0f),
                            forestRoot.transform);
                        tree.name     = "Oak";
                        tree.isStatic = true;
                        tree.transform.localScale = Vector3.one * scl;
                        totalTrees++;
                    }
                }
            }

            Debug.Log($"[StaticWorldBuilder] Oak forest: {totalTrees} trees planted.");
        }

        // ── 7. Camera ─────────────────────────────────────────────────────────

        private void PositionCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 start = GetRoadPosition(15f);
            cam.transform.position = start;
            cam.transform.rotation = Quaternion.LookRotation(GetRoadTangent(15f), Vector3.up);
            cam.farClipPlane  = 3000f;
            cam.nearClipPlane = 0.3f;

            // Remove old components
            var old1 = cam.GetComponent<Vehicle.AutoDriveCamera>();
            if (old1) { old1.enabled = false; Destroy(old1); }

            var drv = cam.GetComponent<StraightLineDriver>();
            if (drv == null) drv = cam.gameObject.AddComponent<StraightLineDriver>();
            drv.speed         = cameraSpeed;
            drv.endZ          = worldLength - 60f;
            drv.cameraHeight  = cameraHeight;
            drv.terrain       = _terrain;
            drv.worldBuilder  = this;
        }

        // ── Utility ──────────────────────────────────────────────────────────

        private float SampleTerrainAt(float wx, float wz)
        {
            if (_terrain == null) return 0f;
            var td  = _terrain.terrainData;
            var ori = _terrain.transform.position;
            float xn = Mathf.Clamp01((wx - ori.x) / td.size.x);
            float zn = Mathf.Clamp01((wz - ori.z) / td.size.z);
            return ori.y + td.GetInterpolatedHeight(xn, zn);
        }

        /// Destroys a Unity Object safely in both Edit and Play mode.
        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else                       Object.DestroyImmediate(obj);
        }

        private void SetMsg(string msg, float p) { _msg = msg; _loadProgress = p; }

        // ── Loading screen ────────────────────────────────────────────────────

        private GUIStyle _boxStyle;
        private bool _styleReady;

        private void OnGUI()
        {
            if (!_loading) return;
            if (!_styleReady)
            {
                _boxStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 18, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
                _styleReady = true;
            }
            float w = Screen.width, h = Screen.height;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Box(new Rect(w*0.5f-240f, h*0.5f-45f, 480f, 90f),
                $"🌿  Building countryside…\n{_msg}   {_loadProgress*100f:F0}%",
                _boxStyle);
            GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
            GUI.DrawTexture(new Rect(w*0.5f-240f, h*0.5f+55f, 480f, 10f), Texture2D.whiteTexture);
            GUI.color = new Color(0.25f, 0.80f, 0.35f, 0.9f);
            GUI.DrawTexture(new Rect(w*0.5f-240f, h*0.5f+55f, 480f*_loadProgress, 10f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
