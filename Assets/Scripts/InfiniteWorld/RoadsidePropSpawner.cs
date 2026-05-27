using System.Collections.Generic;
using UnityEngine;

namespace InfiniteWorld
{
    /// <summary>
    /// Spawns roadside props (reflective poles, rocks, bushes) along the road spline.
    /// Uses an object pool to recycle props behind the player.
    /// </summary>
    public class RoadsidePropSpawner : MonoBehaviour
    {
        [Header("Reflective Poles")]
        [Tooltip("Spacing between roadside poles (metres).")]
        public float poleSpacing = 50f;
        [Tooltip("Lateral offset from road edge to pole.")]
        public float poleOffset = 1.2f;
        [Tooltip("Pole height (metres).")]
        public float poleHeight = 1.1f;

        [Header("Rocks & Bushes")]
        [Tooltip("Average spacing between rock/bush clusters (metres).")]
        public float clusterSpacing = 80f;
        [Tooltip("Maximum lateral offset of clusters from road edge.")]
        public float maxClusterOffset = 12f;
        [Tooltip("Items per cluster.")]
        [Range(1, 8)]
        public int clusterSize = 4;

        [Header("Generation")]
        [Tooltip("How far ahead of player to generate props (metres).")]
        public float generationAhead = 700f;
        [Tooltip("How far behind player before props are recycled (metres).")]
        public float recycleDistance = 300f;

        // ── Internal ─────────────────────────────────────────────────────────
        private InfiniteRoadSystem _roadSystem;
        private Transform _player;
        private TerrainChunkManager _chunkManager;

        // Prop pools
        private Queue<GameObject> _polePool = new Queue<GameObject>();
        private Queue<GameObject> _rockPool = new Queue<GameObject>();
        private Queue<GameObject> _bushPool = new Queue<GameObject>();

        // Active prop tracking
        private struct ActiveProp
        {
            public GameObject go;
            public float roadDistance;
        }
        private List<ActiveProp> _activeProps = new List<ActiveProp>(256);
        private float _lastGeneratedDistance = 0f;

        // Materials
        private Material _poleMaterial;
        private Material _rockMaterial;
        private Material _bushMaterial;

        public void Initialize(InfiniteRoadSystem roadSystem, Transform player, TerrainChunkManager chunkMgr)
        {
            _roadSystem = roadSystem;
            _player = player;
            _chunkManager = chunkMgr;
            CreateMaterials();
            PrewarmPools(30, 40, 40);
        }

        private void LateUpdate()
        {
            if (_roadSystem == null || _player == null) return;

            float playerProgress = _roadSystem.GetNearestProgress(_player.position);

            // Recycle props behind player
            RecycleBehind(playerProgress - recycleDistance);

            // Generate new props ahead
            if (_lastGeneratedDistance < playerProgress + generationAhead)
                GenerateFrom(_lastGeneratedDistance, playerProgress + generationAhead);
        }

        // ── Generation ────────────────────────────────────────────────────────

        private void GenerateFrom(float start, float end)
        {
            float roadHalf = 4.5f + 1.5f; // lane half + shoulder

            // Poles on both sides
            float nextPole = Mathf.Ceil(start / poleSpacing) * poleSpacing;
            while (nextPole < end)
            {
                SpawnPole(nextPole, -1f, roadHalf); // left
                SpawnPole(nextPole,  1f, roadHalf); // right
                nextPole += poleSpacing;
            }

            // Rocks & bushes
            var rng = new System.Random(WorldSeed.Seed ^ (int)(start * 100f));
            float nextCluster = Mathf.Ceil(start / clusterSpacing) * clusterSpacing;
            while (nextCluster < end)
            {
                float side = rng.NextDouble() > 0.5 ? 1f : -1f;
                float lateralOffset = roadHalf + 2f + (float)rng.NextDouble() * maxClusterOffset;
                bool isRock = rng.NextDouble() > 0.5;
                SpawnCluster(nextCluster, side, lateralOffset, isRock, rng);
                nextCluster += clusterSpacing * (0.7f + (float)rng.NextDouble() * 0.6f);
            }

            _lastGeneratedDistance = end;
        }

        private void SpawnPole(float roadDist, float side, float roadHalf)
        {
            Vector3 roadPos = _roadSystem.GetPositionAtDistance(roadDist);
            Vector3 tangent = _roadSystem.GetTangentAtDistance(roadDist);
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            Vector3 pos = roadPos + right * (side * (roadHalf + poleOffset));
            pos.y = GetTerrainHeight(pos) + poleHeight * 0.5f;

            GameObject go = GetFromPool(_polePool, CreatePole);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;
            go.SetActive(true);

            _activeProps.Add(new ActiveProp { go = go, roadDistance = roadDist });
        }

        private void SpawnCluster(float roadDist, float side, float lateralOffset, bool isRock, System.Random rng)
        {
            Vector3 roadPos = _roadSystem.GetPositionAtDistance(roadDist);
            Vector3 tangent = _roadSystem.GetTangentAtDistance(roadDist);
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;

            for (int i = 0; i < clusterSize; i++)
            {
                float fwdJitter = (float)(rng.NextDouble() - 0.5) * 12f;
                float latJitter = (float)(rng.NextDouble() - 0.5) * 4f;
                Vector3 pos = roadPos + tangent * fwdJitter + right * (side * lateralOffset + latJitter);
                pos.y = GetTerrainHeight(pos);

                float scale = 0.3f + (float)rng.NextDouble() * 0.7f;
                GameObject go = GetFromPool(isRock ? _rockPool : _bushPool,
                    isRock ? CreateRock : (System.Func<GameObject>)CreateBush);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                go.transform.localScale = Vector3.one * scale;
                go.SetActive(true);

                _activeProps.Add(new ActiveProp { go = go, roadDistance = roadDist });
            }
        }

        // ── Pool Management ───────────────────────────────────────────────────

        private void RecycleBehind(float behindArc)
        {
            for (int i = _activeProps.Count - 1; i >= 0; i--)
            {
                if (_activeProps[i].roadDistance < behindArc)
                {
                    GameObject go = _activeProps[i].go;
                    go.SetActive(false);
                    ReturnToPool(go);
                    _activeProps.RemoveAt(i);
                }
            }
        }

        private void ReturnToPool(GameObject go)
        {
            string n = go.name;
            if (n.StartsWith("Pole")) _polePool.Enqueue(go);
            else if (n.StartsWith("Rock")) _rockPool.Enqueue(go);
            else _bushPool.Enqueue(go);
        }

        private GameObject GetFromPool(Queue<GameObject> pool, System.Func<GameObject> factory)
        {
            return pool.Count > 0 ? pool.Dequeue() : factory();
        }

        private void PrewarmPools(int poles, int rocks, int bushes)
        {
            for (int i = 0; i < poles; i++) { var go = CreatePole(); go.SetActive(false); _polePool.Enqueue(go); }
            for (int i = 0; i < rocks; i++) { var go = CreateRock(); go.SetActive(false); _rockPool.Enqueue(go); }
            for (int i = 0; i < bushes; i++) { var go = CreateBush(); go.SetActive(false); _bushPool.Enqueue(go); }
        }

        // ── Prop Factories ────────────────────────────────────────────────────

        private GameObject CreatePole()
        {
            var go = new GameObject("Pole");
            go.transform.SetParent(transform, true);

            // Shaft
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            shaft.transform.SetParent(go.transform, false);
            shaft.transform.localScale = new Vector3(0.05f, poleHeight * 0.5f, 0.05f);
            shaft.transform.localPosition = new Vector3(0f, 0f, 0f);
            shaft.GetComponent<Renderer>().sharedMaterial = _poleMaterial;
            Destroy(shaft.GetComponent<Collider>());

            // Reflector cap
            var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.name = "Cap";
            cap.transform.SetParent(go.transform, false);
            cap.transform.localScale = new Vector3(0.08f, 0.06f, 0.04f);
            cap.transform.localPosition = new Vector3(0f, poleHeight * 0.5f, 0f);
            var capMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            capMat.color = new Color(0.9f, 0.15f, 0.1f);
            capMat.SetFloat("_Smoothness", 0.85f);
            capMat.enableInstancing = true;
            cap.GetComponent<Renderer>().sharedMaterial = capMat;
            Destroy(cap.GetComponent<Collider>());

            return go;
        }

        private GameObject CreateRock()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Rock";
            go.transform.SetParent(transform, true);
            go.transform.localScale = new Vector3(
                Random.Range(0.2f, 0.8f),
                Random.Range(0.1f, 0.45f),
                Random.Range(0.25f, 0.7f));
            go.GetComponent<Renderer>().sharedMaterial = _rockMaterial;
            Destroy(go.GetComponent<Collider>());
            return go;
        }

        private GameObject CreateBush()
        {
            var go = new GameObject("Bush");
            go.transform.SetParent(transform, true);

            // Two overlapping spheres for a shrub silhouette
            for (int i = 0; i < 2; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.transform.SetParent(go.transform, false);
                s.transform.localPosition = new Vector3(Random.Range(-0.15f, 0.15f), i * 0.12f, 0f);
                s.transform.localScale = new Vector3(
                    Random.Range(0.3f, 0.55f),
                    Random.Range(0.25f, 0.45f),
                    Random.Range(0.3f, 0.5f));
                s.GetComponent<Renderer>().sharedMaterial = _bushMaterial;
                Destroy(s.GetComponent<Collider>());
            }

            return go;
        }

        // ── Materials ─────────────────────────────────────────────────────────

        private void CreateMaterials()
        {
            _poleMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _poleMaterial.name = "Pole_White";
            _poleMaterial.color = new Color(0.92f, 0.92f, 0.9f);
            _poleMaterial.SetFloat("_Smoothness", 0.4f);
            _poleMaterial.enableInstancing = true;

            _rockMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _rockMaterial.name = "Rock_Gray";
            _rockMaterial.color = new Color(0.45f, 0.42f, 0.38f);
            _rockMaterial.SetFloat("_Smoothness", 0.05f);
            _rockMaterial.enableInstancing = true;

            _bushMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _bushMaterial.name = "Bush_Green";
            _bushMaterial.color = new Color(0.22f, 0.38f, 0.15f);
            _bushMaterial.SetFloat("_Smoothness", 0.1f);
            _bushMaterial.enableInstancing = true;
        }

        // ── Utility ───────────────────────────────────────────────────────────

        private float GetTerrainHeight(Vector3 pos)
        {
            if (_chunkManager != null) return _chunkManager.SampleHeight(pos);
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null) continue;
                var tp = t.transform.position;
                var td = t.terrainData.size;
                if (pos.x >= tp.x && pos.x <= tp.x + td.x &&
                    pos.z >= tp.z && pos.z <= tp.z + td.z)
                    return t.SampleHeight(pos);
            }
            return 0f;
        }
    }
}
