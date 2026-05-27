using System.Collections.Generic;
using UnityEngine;

namespace InfiniteWorld
{
    /// <summary>
    /// Object pool for Unity Terrain GameObjects.
    /// Recycles terrain instances as they scroll out of the active radius
    /// instead of Destroy/Instantiate, preventing GC spikes.
    /// </summary>
    public class TerrainChunkPool : MonoBehaviour
    {
        [Header("Terrain Template")]
        [Tooltip("Size of each terrain chunk in metres.")]
        public float chunkSize = 500f;
        [Tooltip("Terrain heightmap resolution (must be 2^n + 1).")]
        public int heightmapResolution = 513;
        [Tooltip("Terrain alphamap (splatmap) resolution.")]
        public int alphamapResolution = 512;
        [Tooltip("Terrain detail resolution.")]
        public int detailResolution = 512;
        [Tooltip("Maximum tree instances per chunk.")]
        public int maxTreesPerChunk = 200;

        [Header("Pool")]
        [Tooltip("Maximum number of active terrain instances (should equal view grid area).")]
        public int maxPoolSize = 25;

        // ── Internal ────────────────────────────────────────────────────────
        private Queue<Terrain> _availablePool = new Queue<Terrain>();
        private List<Terrain> _allTerrains = new List<Terrain>();

        private void Awake()
        {
            // Pre-warm pool
            for (int i = 0; i < maxPoolSize; i++)
            {
                Terrain t = CreateTerrain();
                t.gameObject.SetActive(false);
                _availablePool.Enqueue(t);
                _allTerrains.Add(t);
            }
        }

        /// <summary>
        /// Gets a terrain from the pool, positioned at the given chunk's world origin.
        /// Returns null if pool is exhausted.
        /// </summary>
        public Terrain GetTerrain(ChunkCoord coord)
        {
            Terrain terrain;
            if (_availablePool.Count > 0)
            {
                terrain = _availablePool.Dequeue();
            }
            else
            {
                Debug.LogWarning("[TerrainPool] Pool exhausted — consider increasing maxPoolSize.");
                return null;
            }

            Vector3 origin = coord.WorldOrigin(chunkSize);
            terrain.transform.position = origin;
            terrain.gameObject.name = $"Terrain_{coord.X}_{coord.Z}";
            terrain.gameObject.SetActive(true);

            // Reset tree and detail data
            ResetTerrainData(terrain.terrainData);

            return terrain;
        }

        /// <summary>Returns a terrain instance back to the pool.</summary>
        public void ReturnTerrain(Terrain terrain)
        {
            if (terrain == null) return;
            terrain.gameObject.SetActive(false);
            terrain.transform.position = new Vector3(0f, -9999f, 0f); // hide underground
            _availablePool.Enqueue(terrain);
        }

        // ── Factory ──────────────────────────────────────────────────────────

        private Terrain CreateTerrain()
        {
            GameObject go = new GameObject("Terrain_Pool");
            go.transform.SetParent(transform, false);

            TerrainData td = new TerrainData();
            td.heightmapResolution = heightmapResolution;
            td.alphamapResolution = alphamapResolution;
            td.SetDetailResolution(detailResolution, 16);
            td.size = new Vector3(chunkSize, 100f, chunkSize);

            // Mesh settings for quality
            td.baseMapResolution = 1024;

            var terrainCollider = go.AddComponent<TerrainCollider>();
            terrainCollider.terrainData = td;

            var terrain = go.AddComponent<Terrain>();
            terrain.terrainData = td;

            // Visual quality settings
            terrain.drawInstanced = true;            // GPU instancing
            terrain.heightmapPixelError = 5;         // LOD quality (lower = better)
            terrain.basemapDistance = 1000f;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            terrain.detailObjectDistance = 150f;
            terrain.treeDistance = 1200f;
            terrain.treeBillboardDistance = 800f;
            terrain.treeCrossFadeLength = 60f;
            terrain.treeMaximumFullLODCount = 50;

            return terrain;
        }

        private void ResetTerrainData(TerrainData td)
        {
            // Clear heights to flat
            float[,] heights = new float[td.heightmapResolution, td.heightmapResolution];
            td.SetHeights(0, 0, heights);

            // Clear tree instances
            td.treeInstances = new TreeInstance[0];
        }

        private void OnDestroy()
        {
            foreach (var t in _allTerrains)
            {
                if (t != null && t.terrainData != null)
                    Destroy(t.terrainData);
            }
        }
    }
}
