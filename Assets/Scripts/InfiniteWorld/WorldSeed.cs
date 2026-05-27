using UnityEngine;

namespace InfiniteWorld
{
    /// <summary>
    /// Holds the global world seed and provides deterministic RNG per chunk.
    /// </summary>
    public static class WorldSeed
    {
        /// <summary>World seed. Change this in the Inspector via WorldSeedInitializer.</summary>
        public static int Seed { get; private set; } = 42;

        public static void SetSeed(int seed) => Seed = seed;

        /// <summary>Returns a deterministic System.Random for the given chunk coordinate.</summary>
        public static System.Random GetRNG(ChunkCoord coord)
        {
            // Cantor pairing with seed for a unique integer per (x,z,seed) triple
            int a = coord.X >= 0 ? 2 * coord.X : -2 * coord.X - 1;
            int b = coord.Z >= 0 ? 2 * coord.Z : -2 * coord.Z - 1;
            int hash = ((a + b) * (a + b + 1) / 2) + b;
            return new System.Random(hash ^ Seed);
        }

        /// <summary>Returns a deterministic Unity.Random.State for a given coord (for use with UnityEngine.Random).</summary>
        public static int GetIntSeed(ChunkCoord coord)
        {
            var rng = GetRNG(coord);
            return rng.Next();
        }

        /// <summary>Returns a float hash in [0,1) for a position — fast lookup for large-scale noise seeds.</summary>
        public static float HashFloat(int x, int z)
        {
            int n = x + z * 57 + Seed * 131;
            n = (n << 13) ^ n;
            return 1.0f - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0f;
        }
    }

    /// <summary>Integer XZ coordinate identifying a terrain chunk.</summary>
    [System.Serializable]
    public struct ChunkCoord : System.IEquatable<ChunkCoord>
    {
        public int X;
        public int Z;

        public ChunkCoord(int x, int z) { X = x; Z = z; }

        public bool Equals(ChunkCoord other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is ChunkCoord c && Equals(c);
        public override int GetHashCode() => X * 73856093 ^ Z * 19349663;
        public override string ToString() => $"Chunk({X},{Z})";

        public static ChunkCoord FromWorldPos(Vector3 worldPos, float chunkSize)
        {
            return new ChunkCoord(
                Mathf.FloorToInt(worldPos.x / chunkSize),
                Mathf.FloorToInt(worldPos.z / chunkSize)
            );
        }

        public Vector3 WorldOrigin(float chunkSize)
        {
            return new Vector3(X * chunkSize, 0f, Z * chunkSize);
        }
    }
}
