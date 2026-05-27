using UnityEngine;

namespace InfiniteWorld
{
    /// <summary>
    /// Generates heightmap and splatmap data for a terrain chunk using multi-octave Perlin noise.
    /// Flattens terrain under the road spline and paints terrain layers by slope/height rules.
    /// </summary>
    public class ProceduralTerrainGenerator : MonoBehaviour
    {
        [Header("Terrain Shape")]
        [Tooltip("Base noise frequency — smaller = wider hills.")]
        public float baseFrequency = 0.0015f;
        [Tooltip("Number of Perlin octaves.")]
        [Range(1, 8)]
        public int octaves = 4;
        [Tooltip("Amplitude decay per octave.")]
        [Range(0.1f, 0.9f)]
        public float persistence = 0.45f;
        [Tooltip("Frequency multiplier per octave.")]
        [Range(1f, 4f)]
        public float lacunarity = 2.0f;
        [Tooltip("Maximum terrain height in metres.")]
        public float maxHeight = 40f;
        [Tooltip("Offset to make terrain predominantly flat in the center.")]
        [Range(0f, 1f)]
        public float flatBias = 0.35f;

        [Header("Road Flattening")]
        [Tooltip("Half-width of the flat road corridor (metres).")]
        public float roadFlattenHalfWidth = 8f;
        [Tooltip("Blend distance around road corridor.")]
        public float roadBlendWidth = 14f;

        [Header("Terrain Layers — Drag from MicroVerse-Extras/Terrain Textures/Layers/")]
        public TerrainLayer layerGrass1;
        public TerrainLayer layerGrass2;
        public TerrainLayer layerGravel;
        public TerrainLayer layerAsphalt;
        public TerrainLayer layerSoil;
        public TerrainLayer layerScrubs;

        // Reference to road system for flatten queries
        private InfiniteRoadSystem _roadSystem;

        public void Initialize(InfiniteRoadSystem roadSystem)
        {
            _roadSystem = roadSystem;
        }

        /// <summary>
        /// Fills <paramref name="terrain"/>'s heightmap and splatmap for the given chunk.
        /// Call this from a background thread (heightmap generation) then finish on main thread.
        /// </summary>
        public void GenerateChunk(Terrain terrain, ChunkCoord coord, float chunkSize)
        {
            TerrainData td = terrain.terrainData;

            int hmRes = td.heightmapResolution;
            int alphaRes = td.alphamapResolution;

            // ── Assign terrain layers ─────────────────────────────────────
            AssignLayers(td);

            // ── Generate heights ──────────────────────────────────────────
            float[,] heights = new float[hmRes, hmRes];
            Vector3 chunkOrigin = coord.WorldOrigin(chunkSize);

            float offsetX = WorldSeed.Seed * 1000f;
            float offsetZ = WorldSeed.Seed * 1337f;

            for (int z = 0; z < hmRes; z++)
            {
                for (int x = 0; x < hmRes; x++)
                {
                    float worldX = chunkOrigin.x + (x / (float)(hmRes - 1)) * chunkSize;
                    float worldZ = chunkOrigin.z + (z / (float)(hmRes - 1)) * chunkSize;

                    float h = SampleNoise(worldX + offsetX, worldZ + offsetZ);

                    // Road corridor flattening
                    if (_roadSystem != null)
                    {
                        float blend = GetRoadFlattenWeight(new Vector3(worldX, 0, worldZ));
                        h = Mathf.Lerp(h, 0.05f, blend); // flatten toward near-sea-level
                    }

                    heights[z, x] = h;
                }
            }

            td.SetHeights(0, 0, heights);

            // ── Generate splatmap ─────────────────────────────────────────
            if (td.terrainLayers.Length > 0)
            {
                int layerCount = td.terrainLayers.Length;
                float[,,] alphas = new float[alphaRes, alphaRes, layerCount];

                for (int z = 0; z < alphaRes; z++)
                {
                    for (int x = 0; x < alphaRes; x++)
                    {
                        float nx = x / (float)(alphaRes - 1);
                        float nz = z / (float)(alphaRes - 1);

                        float worldX = chunkOrigin.x + nx * chunkSize;
                        float worldZ = chunkOrigin.z + nz * chunkSize;

                        // Sample height and slope at this point
                        float normalizedH = td.GetHeight(
                            Mathf.RoundToInt(nx * (hmRes - 1)),
                            Mathf.RoundToInt(nz * (hmRes - 1))) / maxHeight;

                        float slope = td.GetSteepness(nx, nz) / 90f; // 0–1

                        float roadBlend = _roadSystem != null
                            ? GetRoadFlattenWeight(new Vector3(worldX, 0, worldZ))
                            : 0f;

                        // Paint layers
                        float[] weights = ComputeWeights(normalizedH, slope, roadBlend, layerCount);
                        for (int l = 0; l < layerCount; l++)
                            alphas[z, x, l] = weights[l];
                    }
                }

                td.SetAlphamaps(0, 0, alphas);
            }
        }

        // ── Noise ────────────────────────────────────────────────────────────

        private float SampleNoise(float wx, float wz)
        {
            float amplitude = 1f, frequency = baseFrequency, total = 0f, maxVal = 0f;
            for (int o = 0; o < octaves; o++)
            {
                total += Mathf.PerlinNoise(wx * frequency, wz * frequency) * amplitude;
                maxVal += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            float n = total / maxVal; // 0–1
            // Apply flat bias: push values toward 0.5 from above
            n = Mathf.Lerp(n, Mathf.Max(n - flatBias, 0f), 0.5f);
            return Mathf.Clamp01(n);
        }

        // ── Road Flatten ──────────────────────────────────────────────────────

        private float GetRoadFlattenWeight(Vector3 worldPos)
        {
            if (_roadSystem == null || _roadSystem.ControlPoints.Count < 4) return 0f;

            // Find nearest road segment distance (simple brute force for now)
            float nearest = float.MaxValue;
            var pts = _roadSystem.ControlPoints;
            int count = pts.Count;

            for (int i = 1; i < count - 1; i++)
            {
                float d = DistanceToSegmentXZ(worldPos, pts[i], pts[i + 1]);
                if (d < nearest) nearest = d;
            }

            if (nearest <= roadFlattenHalfWidth) return 1f;
            if (nearest >= roadFlattenHalfWidth + roadBlendWidth) return 0f;
            return 1f - (nearest - roadFlattenHalfWidth) / roadBlendWidth;
        }

        private float DistanceToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            // Project onto XZ plane
            Vector2 p2 = new Vector2(p.x, p.z);
            Vector2 a2 = new Vector2(a.x, a.z);
            Vector2 b2 = new Vector2(b.x, b.z);
            Vector2 ab = b2 - a2;
            float t = Mathf.Clamp01(Vector2.Dot(p2 - a2, ab) / (ab.sqrMagnitude + 0.0001f));
            Vector2 closest = a2 + t * ab;
            return Vector2.Distance(p2, closest);
        }

        // ── Layer Weights ─────────────────────────────────────────────────────

        private float[] ComputeWeights(float normalizedHeight, float slope, float roadBlend, int layerCount)
        {
            float[] w = new float[layerCount];

            // Layer indices match order in AssignLayers:
            // 0: Grass1, 1: Grass2, 2: Gravel, 3: Asphalt, 4: Soil, 5: Scrubs

            if (layerCount >= 4 && roadBlend > 0.05f)
            {
                // Road: asphalt
                w[3] = roadBlend;
                w[2] = (1f - roadBlend) * Mathf.Clamp01(slope * 3f); // gravel shoulder
                w[0] = 1f - w[3] - w[2];
            }
            else
            {
                float grassBlend = Mathf.Clamp01(1f - slope * 2f - normalizedHeight * 0.5f);
                float gravelBlend = Mathf.Clamp01(slope * 2f) * (1f - roadBlend);
                float soilBlend = normalizedHeight > 0.7f ? Mathf.Clamp01((normalizedHeight - 0.7f) * 3f) : 0f;
                float scrubBlend = normalizedHeight > 0.5f && slope < 0.3f
                    ? Mathf.Clamp01((normalizedHeight - 0.5f) * 2f)
                    : 0f;

                w[0] = grassBlend * 0.6f;
                if (layerCount > 1) w[1] = grassBlend * 0.4f;
                if (layerCount > 2) w[2] = gravelBlend;
                if (layerCount > 4) w[4] = soilBlend;
                if (layerCount > 5) w[5] = scrubBlend;
            }

            // Normalize
            float sum = 0f;
            for (int i = 0; i < layerCount; i++) sum += w[i];
            if (sum > 0.001f)
                for (int i = 0; i < layerCount; i++) w[i] /= sum;
            else
                w[0] = 1f;

            return w;
        }

        // ── Layer Assignment ──────────────────────────────────────────────────

        private void AssignLayers(TerrainData td)
        {
            var layers = new System.Collections.Generic.List<TerrainLayer>();

            if (layerGrass1 != null) layers.Add(layerGrass1);
            if (layerGrass2 != null) layers.Add(layerGrass2);
            if (layerGravel != null) layers.Add(layerGravel);
            if (layerAsphalt != null) layers.Add(layerAsphalt);
            if (layerSoil != null) layers.Add(layerSoil);
            if (layerScrubs != null) layers.Add(layerScrubs);

            if (layers.Count == 0)
            {
                // Fallback: create a plain green layer
                var fallback = new TerrainLayer();
                fallback.diffuseTexture = Texture2D.whiteTexture;
                fallback.tileSize = new Vector2(10f, 10f);
                layers.Add(fallback);
            }

            td.terrainLayers = layers.ToArray();
        }
    }
}
