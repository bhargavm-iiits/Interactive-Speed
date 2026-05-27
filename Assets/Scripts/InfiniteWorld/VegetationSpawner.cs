using UnityEngine;
using System.Collections.Generic;

namespace InfiniteWorld
{
    /// <summary>
    /// Spawns trees and grass details on terrain chunks using Poisson-disk sampling.
    /// Trees are excluded from road corridors. Grass is painted via detail maps.
    /// </summary>
    public class VegetationSpawner : MonoBehaviour
    {
        [Header("Tree Settings")]
        [Tooltip("Tree prefabs to use (assign in Inspector). If empty, procedural placeholders are used.")]
        public GameObject[] treePrefabs;

        [Tooltip("Density of trees per square metre in cluster zones.")]
        public float clusterTreeDensity = 0.003f;
        [Tooltip("Density of scattered trees per square metre in open areas.")]
        public float scatterTreeDensity = 0.0005f;
        [Tooltip("Minimum scale of tree instances.")]
        public float treeMinScale = 0.7f;
        [Tooltip("Maximum scale of tree instances.")]
        public float treeMaxScale = 1.4f;
        [Tooltip("Metres from road centre to exclude trees.")]
        public float roadExclusionRadius = 18f;

        [Header("Grass Detail")]
        [Tooltip("Grass texture for terrain detail layer. If null, a solid colour is used.")]
        public Texture2D grassDetailTexture;
        [Tooltip("Grass detail density (0–15).")]
        [Range(0, 15)]
        public int grassDensity = 8;

        [Header("Poisson Disk")]
        [Tooltip("Minimum spacing between trees (metres).")]
        public float minTreeSpacing = 8f;

        // Reference set externally
        private InfiniteRoadSystem _roadSystem;

        public void Initialize(InfiniteRoadSystem roadSystem)
        {
            _roadSystem = roadSystem;
        }

        /// <summary>
        /// Populates trees and grass on the given terrain for the specified chunk.
        /// Must be called on the main thread after terrain heightmap is set.
        /// </summary>
        public void SpawnVegetation(Terrain terrain, ChunkCoord coord)
        {
            TerrainData td = terrain.terrainData;
            Vector3 chunkOrigin = terrain.transform.position;
            float size = td.size.x;
            var rng = WorldSeed.GetRNG(coord);

            // ── Assign tree prototypes ────────────────────────────────────
            SetupTreePrototypes(td);

            // ── Assign grass detail prototype ─────────────────────────────
            SetupGrassDetail(td);

            // ── Poisson-disk tree placement (only if prototypes are defined) ──
            if (td.treePrototypes.Length > 0)
            {
                var positions = PoissonDisk(size, minTreeSpacing, rng);
                var trees = new List<TreeInstance>(positions.Count);

                foreach (Vector2 p in positions)
                {
                    Vector3 worldPos = chunkOrigin + new Vector3(p.x, 0f, p.y);

                    // Road exclusion
                    if (_roadSystem != null && _roadSystem.IsNearRoad(worldPos, roadExclusionRadius))
                        continue;

                    // Biome cluster probability
                    float clusterNoise = Mathf.PerlinNoise(
                        worldPos.x * 0.005f + WorldSeed.Seed * 0.1f,
                        worldPos.z * 0.005f + WorldSeed.Seed * 0.07f);
                    float spawnProb = clusterNoise > 0.55f ? clusterTreeDensity * 100f : scatterTreeDensity * 100f;
                    if ((float)rng.NextDouble() > spawnProb * 0.01f) continue;

                    float nx = p.x / size;
                    float nz = p.y / size;

                    TreeInstance ti = new TreeInstance
                    {
                        position = new Vector3(nx, 0f, nz),
                        prototypeIndex = rng.Next(0, td.treePrototypes.Length),
                        widthScale = treeMinScale + (float)rng.NextDouble() * (treeMaxScale - treeMinScale),
                        heightScale = treeMinScale + (float)rng.NextDouble() * (treeMaxScale - treeMinScale),
                        rotation = (float)(rng.NextDouble() * Mathf.PI * 2f),
                        color = Color.white,
                        lightmapColor = Color.white
                    };
                    trees.Add(ti);
                }

                td.treeInstances = trees.ToArray();
            }
            else
            {
                // No prototypes — ensure the array is cleared so Unity doesn't hold stale refs
                td.treeInstances = new TreeInstance[0];
            }

            // ── Grass detail map ──────────────────────────────────────────
            PaintGrassDetail(td, coord, rng, chunkOrigin, size);
        }

        // ── Tree Prototypes ───────────────────────────────────────────────────

        private void SetupTreePrototypes(TerrainData td)
        {
            if (td.treePrototypes.Length > 0) return; // already set

            if (treePrefabs != null && treePrefabs.Length > 0)
            {
                var protos = new TreePrototype[treePrefabs.Length];
                for (int i = 0; i < treePrefabs.Length; i++)
                    protos[i] = new TreePrototype { prefab = treePrefabs[i], bendFactor = 0.5f };
                td.treePrototypes = protos;
            }
            else
            {
                // Procedural placeholder: use a Unity Capsule as stand-in
                // (no external asset needed)
                td.treePrototypes = new TreePrototype[0];
            }
        }

        // ── Grass Details ─────────────────────────────────────────────────────

        private void SetupGrassDetail(TerrainData td)
        {
            if (td.detailPrototypes.Length > 0) return; // already set

            Texture2D tex = grassDetailTexture;
            if (tex == null)
            {
                tex = new Texture2D(4, 4);
                for (int i = 0; i < tex.width; i++)
                    for (int j = 0; j < tex.height; j++)
                        tex.SetPixel(i, j, new Color(0.3f, 0.55f, 0.15f, 1f));
                tex.Apply();
            }

            var dp = new DetailPrototype
            {
                prototypeTexture = tex,
                renderMode = DetailRenderMode.Grass,
                dryColor = new Color(0.72f, 0.67f, 0.3f),
                healthyColor = new Color(0.35f, 0.6f, 0.2f),
                minWidth = 0.5f,
                maxWidth = 1.0f,
                minHeight = 0.3f,
                maxHeight = 0.7f,
                noiseSpread = 0.4f,
                usePrototypeMesh = false
            };

            td.detailPrototypes = new DetailPrototype[] { dp };
        }

        private void PaintGrassDetail(TerrainData td, ChunkCoord coord, System.Random rng, Vector3 chunkOrigin, float size)
        {
            if (td.detailPrototypes.Length == 0) return;

            int res = td.detailResolution;
            int[,] map = new int[res, res];

            float invRes = size / res;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    Vector3 worldPos = chunkOrigin + new Vector3(x * invRes, 0f, z * invRes);

                    // Skip road area
                    if (_roadSystem != null && _roadSystem.IsNearRoad(worldPos, roadExclusionRadius * 0.6f))
                    {
                        map[z, x] = 0;
                        continue;
                    }

                    float grassNoise = Mathf.PerlinNoise(
                        worldPos.x * 0.03f + coord.X,
                        worldPos.z * 0.03f + coord.Z);
                    map[z, x] = grassNoise > 0.3f ? (int)(grassNoise * grassDensity) : 0;
                }
            }

            td.SetDetailLayer(0, 0, 0, map);
        }

        // ── Poisson Disk Sampling ─────────────────────────────────────────────

        private List<Vector2> PoissonDisk(float size, float minDist, System.Random rng)
        {
            var result = new List<Vector2>();
            var active = new List<Vector2>();
            var grid = new Dictionary<(int, int), Vector2>();
            float cellSize = minDist / Mathf.Sqrt(2f);
            int k = 20; // candidates per point

            Vector2 first = new Vector2(
                (float)rng.NextDouble() * size,
                (float)rng.NextDouble() * size);
            result.Add(first);
            active.Add(first);
            GridAdd(grid, first, cellSize);

            while (active.Count > 0)
            {
                int idx = rng.Next(0, active.Count);
                Vector2 origin = active[idx];
                bool found = false;

                for (int attempt = 0; attempt < k; attempt++)
                {
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float dist = minDist + (float)rng.NextDouble() * minDist;
                    Vector2 candidate = origin + new Vector2(
                        Mathf.Cos(angle) * dist,
                        Mathf.Sin(angle) * dist);

                    if (candidate.x < 0 || candidate.x >= size ||
                        candidate.y < 0 || candidate.y >= size)
                        continue;

                    if (!GridHasNeighbour(grid, candidate, minDist, cellSize))
                    {
                        result.Add(candidate);
                        active.Add(candidate);
                        GridAdd(grid, candidate, cellSize);
                        found = true;
                        break;
                    }
                }
                if (!found) active.RemoveAt(idx);
            }

            return result;
        }

        private void GridAdd(Dictionary<(int, int), Vector2> grid, Vector2 p, float cellSize)
        {
            var key = ((int)(p.x / cellSize), (int)(p.y / cellSize));
            grid[key] = p;
        }

        private bool GridHasNeighbour(Dictionary<(int, int), Vector2> grid, Vector2 p, float minDist, float cellSize)
        {
            int cx = (int)(p.x / cellSize), cz = (int)(p.y / cellSize);
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    var key = (cx + dx, cz + dz);
                    if (grid.TryGetValue(key, out Vector2 n))
                    {
                        if (Vector2.Distance(p, n) < minDist)
                            return true;
                    }
                }
            }
            return false;
        }
    }
}
