using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace InfiniteWorld
{
    /// <summary>
    /// Central manager for infinite terrain chunk streaming.
    /// Maintains a grid of active terrain chunks around the player,
    /// loading new ones and recycling old ones as the player moves.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class TerrainChunkManager : MonoBehaviour
    {
        [Header("References")]
        public Transform player;
        public TerrainChunkPool chunkPool;
        public ProceduralTerrainGenerator terrainGenerator;
        public VegetationSpawner vegetationSpawner;
        public InfiniteRoadSystem roadSystem;

        [Header("World Settings")]
        [Tooltip("Chunk size matches pool setting (metres).")]
        public float chunkSize = 500f;
        [Tooltip("Number of chunks visible in each direction from player.")]
        [Range(1, 4)]
        public int viewRadiusChunks = 2;

        [Header("Generation")]
        [Tooltip("Maximum chunks generated per frame (spread work over time).")]
        public int maxChunksPerFrame = 1;

        // ── State ────────────────────────────────────────────────────────────
        private Dictionary<ChunkCoord, Terrain> _activeChunks = new Dictionary<ChunkCoord, Terrain>();
        private HashSet<ChunkCoord> _generatingChunks = new HashSet<ChunkCoord>();
        private Queue<ChunkCoord> _pendingQueue = new Queue<ChunkCoord>();
        private ChunkCoord _lastPlayerChunk;
        private bool _initialized;

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Start()
        {
            if (player == null)
            {
                var cam = Camera.main;
                if (cam != null) player = cam.transform;
            }

            if (chunkPool == null) chunkPool = GetComponent<TerrainChunkPool>();
            if (terrainGenerator == null) terrainGenerator = GetComponent<ProceduralTerrainGenerator>();
            if (vegetationSpawner == null) vegetationSpawner = GetComponent<VegetationSpawner>();
            if (roadSystem == null) roadSystem = GetComponent<InfiniteRoadSystem>();

            // Wire road system into sub-systems
            if (roadSystem != null)
            {
                if (player != null) roadSystem.Initialize(player);
                terrainGenerator?.Initialize(roadSystem);
                vegetationSpawner?.Initialize(roadSystem);
            }

            _lastPlayerChunk = ChunkCoord.FromWorldPos(player ? player.position : Vector3.zero, chunkSize);
            _initialized = true;

            // Initial terrain load
            RefreshVisibleChunks();
            StartCoroutine(ProcessChunkQueue());
        }

        private void Update()
        {
            if (!_initialized || player == null) return;

            ChunkCoord currentChunk = ChunkCoord.FromWorldPos(player.position, chunkSize);
            if (currentChunk.Equals(_lastPlayerChunk)) return;

            _lastPlayerChunk = currentChunk;
            RefreshVisibleChunks();
        }

        // ── Chunk Visibility ─────────────────────────────────────────────────

        private void RefreshVisibleChunks()
        {
            ChunkCoord playerChunk = ChunkCoord.FromWorldPos(
                player != null ? player.position : Vector3.zero, chunkSize);

            var desiredChunks = new HashSet<ChunkCoord>();

            for (int dx = -viewRadiusChunks; dx <= viewRadiusChunks; dx++)
            {
                for (int dz = -viewRadiusChunks; dz <= viewRadiusChunks; dz++)
                {
                    desiredChunks.Add(new ChunkCoord(playerChunk.X + dx, playerChunk.Z + dz));
                }
            }

            // Unload chunks no longer needed
            var toRemove = new List<ChunkCoord>();
            foreach (var kv in _activeChunks)
            {
                if (!desiredChunks.Contains(kv.Key))
                    toRemove.Add(kv.Key);
            }
            foreach (var coord in toRemove)
                UnloadChunk(coord);

            // Queue new chunks
            foreach (var coord in desiredChunks)
            {
                if (!_activeChunks.ContainsKey(coord) && !_generatingChunks.Contains(coord))
                {
                    _pendingQueue.Enqueue(coord);
                    _generatingChunks.Add(coord);
                }
            }

            // Stitch terrain neighbors for seamless edges
            StitchNeighbors();
        }

        private IEnumerator ProcessChunkQueue()
        {
            while (true)
            {
                int processed = 0;
                while (_pendingQueue.Count > 0 && processed < maxChunksPerFrame)
                {
                    ChunkCoord coord = _pendingQueue.Dequeue();
                    if (!_activeChunks.ContainsKey(coord))
                    {
                        yield return LoadChunk(coord);
                        processed++;
                    }
                    else
                    {
                        _generatingChunks.Remove(coord);
                    }
                }
                yield return null;
            }
        }

        private IEnumerator LoadChunk(ChunkCoord coord)
        {
            // Get terrain from pool
            Terrain terrain = chunkPool?.GetTerrain(coord);
            if (terrain == null)
            {
                _generatingChunks.Remove(coord);
                yield break;
            }

            _activeChunks[coord] = terrain;

            // Generate heightmap + splatmap (spread over two frames)
            terrainGenerator?.GenerateChunk(terrain, coord, chunkSize);
            yield return null;

            // Spawn vegetation
            vegetationSpawner?.SpawnVegetation(terrain, coord);
            yield return null;

            // Re-stitch with neighbors
            StitchNeighbors();

            _generatingChunks.Remove(coord);
        }

        private void UnloadChunk(ChunkCoord coord)
        {
            if (_activeChunks.TryGetValue(coord, out Terrain terrain))
            {
                chunkPool?.ReturnTerrain(terrain);
                _activeChunks.Remove(coord);
            }
        }

        // ── Terrain Stitching ────────────────────────────────────────────────

        private void StitchNeighbors()
        {
            foreach (var kv in _activeChunks)
            {
                ChunkCoord coord = kv.Key;
                Terrain t = kv.Value;

                t.SetNeighbors(
                    GetTerrain(new ChunkCoord(coord.X - 1, coord.Z)),
                    GetTerrain(new ChunkCoord(coord.X, coord.Z + 1)),
                    GetTerrain(new ChunkCoord(coord.X + 1, coord.Z)),
                    GetTerrain(new ChunkCoord(coord.X, coord.Z - 1))
                );
            }
        }

        private Terrain GetTerrain(ChunkCoord coord)
        {
            _activeChunks.TryGetValue(coord, out Terrain t);
            return t;
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>Returns the terrain chunk at a world position, or null if not loaded.</summary>
        public Terrain GetTerrainAt(Vector3 worldPos)
        {
            var coord = ChunkCoord.FromWorldPos(worldPos, chunkSize);
            _activeChunks.TryGetValue(coord, out Terrain t);
            return t;
        }

        /// <summary>Samples the terrain height at a world XZ position.</summary>
        public float SampleHeight(Vector3 worldPos)
        {
            Terrain t = GetTerrainAt(worldPos);
            return t != null ? t.SampleHeight(worldPos) : 0f;
        }

        // ── Debug Gizmos ─────────────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || player == null) return;
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.15f);
            foreach (var kv in _activeChunks)
            {
                Vector3 center = kv.Key.WorldOrigin(chunkSize) + new Vector3(chunkSize * 0.5f, 0f, chunkSize * 0.5f);
                Gizmos.DrawCube(center, new Vector3(chunkSize, 1f, chunkSize));
            }
        }
#endif
    }
}
