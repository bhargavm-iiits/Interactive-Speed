using System.Collections.Generic;
using UnityEngine;

namespace InfiniteWorld
{
    /// <summary>
    /// Spawns and manages flying birds in the sky from the Zacxophone assets folder.
    /// Self-bootstraps at runtime to avoid manual scene assembly.
    /// </summary>
    public class BirdSpawner : MonoBehaviour
    {
        [Header("Spawning Ranges")]
        public float spawnAheadMin = 60f;
        public float spawnAheadMax = 150f;
        public float flyHeightMin = 8f;
        public float flyHeightMax = 18f;
        public float lateralOffsetMax = 12f;
        
        [Header("Flight Speed")]
        public float minSpeed = 9f;   // in m/s
        public float maxSpeed = 17f;  // in m/s
        
        [Header("Flock Count")]
        public int maxActiveFlocks = 4;
        public float recycleBehindDistance = 150f;

        private StraightLineDriver _driver;
        private List<ActiveFlock> _activeFlocks = new List<ActiveFlock>();
        private GameObject _birdPrefab;

        private struct ActiveFlock
        {
            public GameObject go;
            public Vector3 direction;
            public float speed;
            public float spawnDistanceZ;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            var driver = FindFirstObjectByType<StraightLineDriver>();
            if (driver != null)
            {
                var go = new GameObject("BirdSpawner_Auto");
                go.AddComponent<BirdSpawner>();
                Debug.Log("[BirdSpawner] Successfully auto-bootstrapped bird spawner in the scene.");
            }
        }

        private void Start()
        {
            _driver = FindFirstObjectByType<StraightLineDriver>();
            _birdPrefab = LoadBirdPrefab();
            
            if (_birdPrefab == null)
            {
                Debug.LogWarning("[BirdSpawner] Zacxophone bird prefab could not be loaded. Spawning is disabled.");
            }
        }

        private void Update()
        {
            if (_driver == null || _birdPrefab == null) return;

            float dt = Time.deltaTime;
            float playerZ = _driver.Z;

            // 1. Move flocks and recycle those that fly too far behind the player
            for (int i = _activeFlocks.Count - 1; i >= 0; i--)
            {
                var flock = _activeFlocks[i];
                if (flock.go == null)
                {
                    _activeFlocks.RemoveAt(i);
                    continue;
                }

                // Fly forward
                flock.go.transform.position += flock.direction * flock.speed * dt;

                // Recycle check (Z distance)
                float relZ = flock.go.transform.position.z - playerZ;
                if (relZ < -recycleBehindDistance)
                {
                    Destroy(flock.go);
                    _activeFlocks.RemoveAt(i);
                }
            }

            // 2. Maintain active flock population
            if (_activeFlocks.Count < maxActiveFlocks)
            {
                SpawnFlock(playerZ);
            }
        }

        private void SpawnFlock(float playerZ)
        {
            if (_birdPrefab == null || _driver == null || _driver.worldBuilder == null) return;

            // Choose spawning distance ahead along the road
            float spawnZ = playerZ + Random.Range(spawnAheadMin, spawnAheadMax);
            
            Vector3 roadPos = _driver.worldBuilder.GetRoadPosition(spawnZ);
            Vector3 tangent = _driver.worldBuilder.GetRoadTangent(spawnZ);
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;

            // Compute random flying height and lateral offset
            float height = Random.Range(flyHeightMin, flyHeightMax);
            float latOffset = Random.Range(-lateralOffsetMax, lateralOffsetMax);
            Vector3 spawnPos = roadPos + Vector3.up * height + right * latOffset;

            // Angle heading - fly crossing the road or along it, randomizing direction along tangent (forward vs backward)
            float directionSign = (Random.value > 0.5f) ? 1f : -1f;
            float angle = Random.Range(-15f, 15f);
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * (tangent * directionSign);
            dir.y = Random.Range(-0.06f, 0.06f); // slight vertical climb/dive variation
            dir.Normalize();

            // Instantiate bird group
            GameObject flockGo = Instantiate(_birdPrefab, spawnPos, Quaternion.LookRotation(dir));
            flockGo.transform.localScale = Vector3.one * 5.0f; // Scale up to make clearly visible in the sky corridor

            // Enable and randomize animator wing-flap animation speeds
            var anims = flockGo.GetComponentsInChildren<Animator>();
            foreach (var a in anims)
            {
                if (a != null)
                {
                    a.enabled = true;
                    a.speed = Random.Range(0.85f, 1.25f);
                }
            }

            // Store active flock data
            var flock = new ActiveFlock
            {
                go = flockGo,
                direction = dir,
                speed = Random.Range(minSpeed, maxSpeed),
                spawnDistanceZ = spawnZ
            };
            
            _activeFlocks.Add(flock);
        }

        private GameObject LoadBirdPrefab()
        {
#if UNITY_EDITOR
            string[] paths = {
                "Assets/Zacxophone/Birds/URP/Prefabs/05BirdsPrefab.prefab",
                "Assets/Zacxophone/Birds/URP/Prefabs/10BirdsPrefab.prefab",
                "Assets/Zacxophone/Birds/URP/Prefabs/03BirdsPrefab.prefab",
                "Assets/Zacxophone/Birds/URP/Prefabs/15BirdsPrefab.prefab",
                "Assets/Zacxophone/Birds/URP/Prefabs/01BirdPrefab.prefab"
            };

            foreach (var path in paths)
            {
                var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null)
                {
                    Debug.Log($"[BirdSpawner] Successfully loaded prefab at path: {path}");
                    return go;
                }
            }
#endif
            return null;
        }

        private void OnDestroy()
        {
            foreach (var flock in _activeFlocks)
            {
                if (flock.go != null) Destroy(flock.go);
            }
            _activeFlocks.Clear();
        }
    }
}
