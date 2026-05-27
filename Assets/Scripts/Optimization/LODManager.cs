using UnityEngine;

namespace Optimization
{
    /// <summary>
    /// Applies global LOD, GPU instancing, and terrain visual settings
    /// optimized for large-scale infinite-world driving performance.
    /// </summary>
    public class LODManager : MonoBehaviour
    {
        [Header("LOD Settings")]
        [Tooltip("LOD bias — higher extends high-LOD distance. 2+ recommended for open worlds.")]
        public float lodBias = 2.5f;
        [Tooltip("Maximum LOD level (0 = full quality).")]
        public int maximumLODLevel = 0;

        [Header("Terrain")]
        [Tooltip("Terrain pixel error — lower is higher quality but more GPU cost.")]
        public float terrainPixelError = 4f;
        [Tooltip("Distance to draw full tree detail (metres).")]
        public float treeDrawDistance = 1500f;
        [Tooltip("Distance to draw terrain details like grass (metres).")]
        public float detailDrawDistance = 150f;
        [Tooltip("Max trees rendered at full LOD simultaneously.")]
        public int maxFullLODTrees = 50;

        [Header("Shadow")]
        [Tooltip("Shadow distance (metres).")]
        public float shadowDistance = 200f;

        [Header("Camera")]
        [Tooltip("Camera far clip plane (metres).")]
        public float farClipPlane = 3000f;

        private void Awake()
        {
            // ── LOD ───────────────────────────────────────────────────────────
            QualitySettings.lodBias = lodBias;
            QualitySettings.maximumLODLevel = maximumLODLevel;

            // ── Shadows ───────────────────────────────────────────────────────
            QualitySettings.shadowDistance = shadowDistance;

            // ── Camera far clip ───────────────────────────────────────────────
            if (Camera.main != null)
                Camera.main.farClipPlane = farClipPlane;

            // ── Terrain global settings ───────────────────────────────────────
            ApplyTerrainSettings();
        }

        private void OnEnable()
        {
            // Re-apply when terrains are activated at runtime
            InvokeRepeating(nameof(ApplyTerrainSettings), 1f, 3f);
        }

        private void OnDisable()
        {
            CancelInvoke(nameof(ApplyTerrainSettings));
        }

        private void ApplyTerrainSettings()
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (terrain == null) continue;

                terrain.heightmapPixelError = terrainPixelError;
                terrain.treeDistance = treeDrawDistance;
                terrain.detailObjectDistance = detailDrawDistance;
                terrain.treeMaximumFullLODCount = maxFullLODTrees;
                terrain.drawInstanced = true;       // GPU instancing
                terrain.basemapDistance = 1500f;
                terrain.treeBillboardDistance = treeDrawDistance * 0.65f;
                terrain.treeCrossFadeLength = 50f;
            }
        }
    }
}
