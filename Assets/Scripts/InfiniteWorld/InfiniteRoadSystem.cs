using System.Collections.Generic;
using UnityEngine;

namespace InfiniteWorld
{
    /// <summary>
    /// Maintains an infinite Catmull-Rom spline of road control points.
    /// Extends ahead of the player deterministically from the world seed.
    /// </summary>
    public class InfiniteRoadSystem : MonoBehaviour
    {
        [Header("Road Spline Settings")]
        [Tooltip("Minimum distance between control points (metres).")]
        public float minSegmentLength = 80f;
        [Tooltip("Maximum distance between control points (metres).")]
        public float maxSegmentLength = 200f;
        [Tooltip("Maximum lateral deviation (metres) per new control point.")]
        public float maxLateralDeviation = 15f;
        [Tooltip("Elevation smoothing: control points snap this fraction toward terrain height.")]
        [Range(0f, 1f)]
        public float elevationSmoothFactor = 0.35f;
        [Tooltip("Road half-width including shoulder (metres) — used for terrain flattening query.")]
        public float roadHalfWidth = 7f;

        [Header("Generation Distance")]
        [Tooltip("Ensure this many metres of road exist ahead of the player at all times.")]
        public float lookaheadDistance = 1200f;
        [Tooltip("Remove control points this many metres behind the player.")]
        public float cleanupDistance = 600f;

        // ── Public Access ───────────────────────────────────────────────────
        /// <summary>All active road control points in world space.</summary>
        public List<Vector3> ControlPoints { get; private set; } = new List<Vector3>();

        /// <summary>Total arc length of the spline in the forward direction.</summary>
        public float TotalForwardLength { get; private set; }

        // ── Private ─────────────────────────────────────────────────────────
        private Transform _player;
        private float _playerProgress;       // distance along spline of player
        private float _generatedAhead;       // metres generated ahead
        private System.Random _rng;
        private Vector3 _lastDir = Vector3.forward;
        private int _nextSeedCounter = 0;

        // Cached arc-lengths for efficient t lookup
        private List<float> _arcLengths = new List<float>();
        private const int SubdivisionsPerSegment = 20;

        // ────────────────────────────────────────────────────────────────────
        private void Awake()
        {
            _rng = new System.Random(WorldSeed.Seed ^ unchecked((int)0xDEADBEEF));
        }

        private void Start()
        {
            // Seed the spline with a straight stretch so the player doesn't start in a curve
            BootstrapStraightSection(200f);
            RebuildArcLengths();
        }

        public void Initialize(Transform player)
        {
            _player = player;
        }

        private void Update()
        {
            if (_player == null) return;

            _playerProgress = GetNearestProgress(_player.position);
            float playerArc = _playerProgress;
            float generatedArc = GetTotalArcLength();

            // Extend road ahead when needed
            if (generatedArc - playerArc < lookaheadDistance)
            {
                ExtendRoad(lookaheadDistance - (generatedArc - playerArc) + 100f);
                RebuildArcLengths();
            }

            // Trim control points far behind player
            TrimBehind(playerArc - cleanupDistance);
        }

        // ── Spline Evaluation ────────────────────────────────────────────────

        /// <summary>Returns world position on the road at arc-distance <paramref name="distance"/> from the start.</summary>
        public Vector3 GetPositionAtDistance(float distance)
        {
            if (ControlPoints.Count < 4) return Vector3.zero;
            GetSegmentAndT(distance, out int seg, out float t);
            return CatmullRom(seg, t);
        }

        /// <summary>Returns forward tangent on the road at arc-distance <paramref name="distance"/>.</summary>
        public Vector3 GetTangentAtDistance(float distance)
        {
            if (ControlPoints.Count < 4) return Vector3.forward;
            GetSegmentAndT(distance, out int seg, out float t);
            return CatmullRomDerivative(seg, t).normalized;
        }

        /// <summary>Returns the nearest arc-distance from a world position.</summary>
        public float GetNearestProgress(Vector3 worldPos)
        {
            if (_arcLengths.Count < 2) return 0f;

            float bestDist = float.MaxValue;
            float bestArc = 0f;
            int count = ControlPoints.Count - 2;

            for (int seg = 1; seg < count - 1; seg++)
            {
                for (int sub = 0; sub < SubdivisionsPerSegment; sub++)
                {
                    float t = sub / (float)SubdivisionsPerSegment;
                    Vector3 p = CatmullRom(seg, t);
                    float d = Vector3.SqrMagnitude(worldPos - p);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        float baseArc = seg < _arcLengths.Count ? _arcLengths[seg] : 0f;
                        float nextArc = (seg + 1) < _arcLengths.Count ? _arcLengths[seg + 1] : baseArc;
                        bestArc = Mathf.Lerp(baseArc, nextArc, t);
                    }
                }
            }
            return bestArc;
        }

        /// <summary>Returns true if <paramref name="worldPos"/> is within <paramref name="radius"/> metres of the road.</summary>
        public bool IsNearRoad(Vector3 worldPos, float radius)
        {
            if (ControlPoints.Count < 4) return false;
            int count = ControlPoints.Count - 2;
            float radiusSq = radius * radius;

            for (int seg = 1; seg < count - 1; seg++)
            {
                Vector3 pStart = ControlPoints[seg];
                Vector3 pEnd = ControlPoints[seg + 1];

                // Fast bounding box filtering on Z and X axis to skip far segments
                float minSegZ = Mathf.Min(pStart.z, pEnd.z) - radius;
                float maxSegZ = Mathf.Max(pStart.z, pEnd.z) + radius;
                if (worldPos.z < minSegZ || worldPos.z > maxSegZ)
                    continue;

                float minSegX = Mathf.Min(pStart.x, pEnd.x) - radius;
                float maxSegX = Mathf.Max(pStart.x, pEnd.x) + radius;
                if (worldPos.x < minSegX || worldPos.x > maxSegX)
                    continue;

                for (int sub = 0; sub < 10; sub++)
                {
                    float t = sub / 10f;
                    Vector3 p = CatmullRom(seg, t);
                    float dx = worldPos.x - p.x;
                    float dz = worldPos.z - p.z;
                    if (dx * dx + dz * dz < radiusSq)
                        return true;
                }
            }
            return false;
        }

        // ── Internal Generation ──────────────────────────────────────────────

        private void BootstrapStraightSection(float length)
        {
            ControlPoints.Clear();
            // Ghost point behind
            ControlPoints.Add(new Vector3(0f, 0f, -maxSegmentLength));
            ControlPoints.Add(new Vector3(0f, 0f, 0f));
            float z = 0f;
            while (z < length)
            {
                float seg = Mathf.Min(maxSegmentLength, length - z);
                z += seg;
                ControlPoints.Add(new Vector3(0f, 0f, z));
            }
            // Forward ghost
            ControlPoints.Add(ControlPoints[ControlPoints.Count - 1] + Vector3.forward * maxSegmentLength);
        }

        private void ExtendRoad(float byDistance)
        {
            float generated = 0f;
            while (generated < byDistance)
            {
                Vector3 last = ControlPoints[ControlPoints.Count - 2];
                Vector3 secondLast = ControlPoints[ControlPoints.Count - 3];
                Vector3 dir = (last - secondLast).normalized;

                // Smooth random curve — limit turn angle
                float angle = (float)(_rng.NextDouble() * 2.0 - 1.0) * 22f;
                dir = Quaternion.Euler(0f, angle, 0f) * dir;

                float segLen = minSegmentLength + (float)_rng.NextDouble() * (maxSegmentLength - minSegmentLength);
                Vector3 newPoint = last + dir * segLen;

                // Sample terrain height (will be ~0 before terrain exists; terrain generator will flatten)
                newPoint.y = SampleTerrainHeight(newPoint);

                // Remove old ghost, insert new point, add new ghost
                ControlPoints.RemoveAt(ControlPoints.Count - 1);
                ControlPoints.Add(newPoint);
                ControlPoints.Add(newPoint + dir * maxSegmentLength); // new ghost

                generated += segLen;
                _nextSeedCounter++;
            }
        }

        private void TrimBehind(float behindArc)
        {
            if (_arcLengths.Count < 4) return;
            int removeCount = 0;
            for (int i = 0; i < _arcLengths.Count - 4; i++)
            {
                if (_arcLengths[i] < behindArc) removeCount++;
                else break;
            }
            if (removeCount > 0)
            {
                ControlPoints.RemoveRange(0, Mathf.Min(removeCount, ControlPoints.Count - 4));
                RebuildArcLengths();
            }
        }

        private float SampleTerrainHeight(Vector3 worldPos)
        {
            // Sample active Unity terrains
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (terrain == null) continue;
                Vector3 tp = terrain.transform.position;
                Vector3 td = terrain.terrainData.size;
                if (worldPos.x >= tp.x && worldPos.x <= tp.x + td.x &&
                    worldPos.z >= tp.z && worldPos.z <= tp.z + td.z)
                {
                    return terrain.SampleHeight(worldPos);
                }
            }
            return 0f;
        }

        // ── Arc Length Table ─────────────────────────────────────────────────

        private void RebuildArcLengths()
        {
            _arcLengths.Clear();
            if (ControlPoints.Count < 4) return;

            float total = 0f;
            _arcLengths.Add(0f);
            int segCount = ControlPoints.Count - 3;

            for (int seg = 1; seg < segCount; seg++)
            {
                Vector3 prev = CatmullRom(seg, 0f);
                for (int sub = 1; sub <= SubdivisionsPerSegment; sub++)
                {
                    float t = sub / (float)SubdivisionsPerSegment;
                    Vector3 curr = CatmullRom(seg, t);
                    total += Vector3.Distance(prev, curr);
                    prev = curr;
                }
                _arcLengths.Add(total);
            }
            TotalForwardLength = total;
        }

        private float GetTotalArcLength()
        {
            return _arcLengths.Count > 0 ? _arcLengths[_arcLengths.Count - 1] : 0f;
        }

        private void GetSegmentAndT(float distance, out int segment, out float t)
        {
            if (_arcLengths.Count < 2) { segment = 1; t = 0f; return; }

            distance = Mathf.Clamp(distance, 0f, _arcLengths[_arcLengths.Count - 1]);

            // Binary search
            int lo = 0, hi = _arcLengths.Count - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) / 2;
                if (_arcLengths[mid] <= distance) lo = mid;
                else hi = mid;
            }

            segment = lo + 1; // +1 because segment 0 is ghost
            segment = Mathf.Clamp(segment, 1, ControlPoints.Count - 3);

            float segStart = _arcLengths[lo];
            float segEnd   = lo + 1 < _arcLengths.Count ? _arcLengths[lo + 1] : segStart + 1f;
            t = (segEnd - segStart) > 0.001f ? (distance - segStart) / (segEnd - segStart) : 0f;
        }

        // ── Catmull-Rom Math ─────────────────────────────────────────────────

        private Vector3 CatmullRom(int seg, float t)
        {
            int i = Mathf.Clamp(seg, 1, ControlPoints.Count - 3);
            Vector3 p0 = ControlPoints[i - 1];
            Vector3 p1 = ControlPoints[i];
            Vector3 p2 = ControlPoints[i + 1];
            Vector3 p3 = ControlPoints[i + 2];

            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1)
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private Vector3 CatmullRomDerivative(int seg, float t)
        {
            int i = Mathf.Clamp(seg, 1, ControlPoints.Count - 3);
            Vector3 p0 = ControlPoints[i - 1];
            Vector3 p1 = ControlPoints[i];
            Vector3 p2 = ControlPoints[i + 1];
            Vector3 p3 = ControlPoints[i + 2];

            float t2 = t * t;
            return 0.5f * ((-p0 + p2)
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (2f * t)
                + (-p0 + 3f * p1 - 3f * p2 + p3) * (3f * t2));
        }

        // ── Gizmos ───────────────────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (ControlPoints == null || ControlPoints.Count < 4) return;
            Gizmos.color = Color.yellow;
            int segCount = ControlPoints.Count - 3;
            for (int seg = 1; seg < segCount; seg++)
            {
                Vector3 prev = CatmullRom(seg, 0f);
                for (int sub = 1; sub <= 10; sub++)
                {
                    Vector3 curr = CatmullRom(seg, sub / 10f);
                    Gizmos.DrawLine(prev, curr);
                    prev = curr;
                }
            }
        }
#endif
    }
}
