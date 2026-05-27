using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Optimization
{
    /// <summary>
    /// Rate-limits terrain chunk generation to spread work across frames,
    /// preventing frame stutter during high-speed driving.
    /// Prioritizes chunks in the player's direction of travel.
    /// </summary>
    public class ChunkLoadScheduler : MonoBehaviour
    {
        [Header("Scheduling")]
        [Tooltip("Maximum chunk operations per second.")]
        public int maxChunksPerSecond = 2;
        [Tooltip("Frame budget for terrain operations (milliseconds). Operations pause if exceeded.")]
        public float frameBudgetMs = 4f;

        [Header("Priority")]
        [Tooltip("Weight for chunks in front of player vs behind.")]
        public float forwardPriorityBias = 3f;

        // ── Internal ──────────────────────────────────────────────────────────
        private InfiniteWorld.TerrainChunkManager _chunkManager;
        private Transform _player;

        private float _lastGenTime;
        private float _genInterval;

        private void Awake()
        {
            _genInterval = 1f / Mathf.Max(1, maxChunksPerSecond);
        }

        private void Start()
        {
            _chunkManager = FindFirstObjectByType<InfiniteWorld.TerrainChunkManager>();
            if (_chunkManager != null) _player = _chunkManager.player;
        }

        private void Update()
        {
            // Monitor frame time — if we're over budget, flag it for the chunk manager
            float frameMs = Time.deltaTime * 1000f;
            bool overBudget = frameMs > frameBudgetMs + 2f;

            // Expose budget state so TerrainChunkManager can pause generation if needed
            IsBudgetExceeded = overBudget;
        }

        /// <summary>True when the current frame is consuming too much time.</summary>
        public static bool IsBudgetExceeded { get; private set; }

        /// <summary>Scores a chunk coordinate for load priority (higher = more urgent).</summary>
        public float GetChunkPriority(InfiniteWorld.ChunkCoord coord, float chunkSize)
        {
            if (_player == null) return 0f;

            Vector3 chunkCenter = coord.WorldOrigin(chunkSize) + new Vector3(chunkSize * 0.5f, 0f, chunkSize * 0.5f);
            Vector3 toChunk = chunkCenter - _player.position;
            float dist = toChunk.magnitude;
            float forwardDot = Vector3.Dot(toChunk.normalized, _player.forward);

            // Closer + ahead = higher priority
            float priority = 1f / (dist + 1f);
            priority *= 1f + Mathf.Max(0f, forwardDot) * forwardPriorityBias;

            return priority;
        }
    }
}
