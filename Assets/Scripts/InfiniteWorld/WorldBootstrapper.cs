using UnityEngine;
using Vehicle;
using Visual;

namespace InfiniteWorld
{
    /// <summary>
    /// Runtime bootstrapper — wires up all system cross-references on Start.
    /// Uses the Main Camera as the player for terrain streaming (no car needed).
    /// </summary>
    public class WorldBootstrapper : MonoBehaviour
    {
        [Header("Player Override (drag VRCar here for VR mode)")]
        [Tooltip("Assign VRCar here. If empty, falls back to Camera.main.")]
        public Transform playerOverride;

        [Header("Auto-find if not assigned")]
        public TerrainChunkManager chunkManager;
        public InfiniteRoadSystem roadSystem;
        public RoadMeshBuilder roadMeshBuilder;
        public RoadsidePropSpawner propSpawner;
        public DaylightCycleController daylightController;
        public AtmosphericFogController fogController;
        public AutoDriveCamera autoDriveCamera;

        [Header("Seed Initializer")]
        public int worldSeed = 42;

        private void Awake()
        {
            WorldSeed.SetSeed(worldSeed);

            if (chunkManager == null)    chunkManager    = FindFirstObjectByType<TerrainChunkManager>();
            if (roadSystem == null)      roadSystem      = FindFirstObjectByType<InfiniteRoadSystem>();
            if (roadMeshBuilder == null) roadMeshBuilder = FindFirstObjectByType<RoadMeshBuilder>();
            if (propSpawner == null)     propSpawner     = FindFirstObjectByType<RoadsidePropSpawner>();
            if (daylightController == null) daylightController = FindFirstObjectByType<DaylightCycleController>();
            if (fogController == null)   fogController   = FindFirstObjectByType<AtmosphericFogController>();
            if (autoDriveCamera == null) autoDriveCamera = FindFirstObjectByType<AutoDriveCamera>();
        }

        private void Start()
        {
            // Player = Main Camera (no car)
            // Use VRCar if assigned, otherwise fall back to Camera.main
            Transform playerTransform = playerOverride != null
                ? playerOverride
                : (Camera.main != null ? Camera.main.transform : transform);

            // Wire road system
            if (roadSystem != null)
                roadSystem.Initialize(playerTransform);

            // Wire road mesh builder
            if (roadMeshBuilder != null && roadSystem != null)
                roadMeshBuilder.Initialize(roadSystem, playerTransform);

            // Wire prop spawner
            if (propSpawner != null && roadSystem != null)
                propSpawner.Initialize(roadSystem, playerTransform, chunkManager);

            // Wire fog controller
            if (fogController != null)
                fogController.player = playerTransform;

            // Wire chunk manager
            if (chunkManager != null && chunkManager.player == null)
                chunkManager.player = playerTransform;

            // Wire auto-drive camera road reference
            if (autoDriveCamera != null && roadSystem != null)
                autoDriveCamera.roadSystem = roadSystem;

            // Place camera at road start position, slightly elevated
            if (Camera.main != null && roadSystem != null && roadSystem.ControlPoints.Count >= 4)
            {
                Vector3 startPos = roadSystem.GetPositionAtDistance(10f);
                Vector3 startTangent = roadSystem.GetTangentAtDistance(10f);

                // Sample terrain height
                float terrainY = 0f;
                foreach (var t in Terrain.activeTerrains)
                {
                    if (t == null) continue;
                    terrainY = Mathf.Max(terrainY, t.SampleHeight(startPos));
                }
                startPos.y = terrainY + 2.8f;

                Camera.main.transform.position = startPos;
                if (startTangent.sqrMagnitude > 0.01f)
                    Camera.main.transform.rotation = Quaternion.LookRotation(startTangent, Vector3.up);
            }

            Debug.Log("[WorldBootstrapper] Infinite world ready. Camera drives the terrain streaming.");
        }
    }
}
