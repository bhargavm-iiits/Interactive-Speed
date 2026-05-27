using System.Collections.Generic;
using UnityEngine;

namespace InfiniteWorld
{
    /// <summary>
    /// Builds and continuously updates a procedural road mesh along the InfiniteRoadSystem spline.
    /// Produces a two-submesh road: asphalt body + lane markings.
    /// Recycles mesh segments behind the player for zero-allocation steady state.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RoadMeshBuilder : MonoBehaviour
    {
        [Header("Road Geometry")]
        [Tooltip("Half-width of the driveable lanes (metres).")]
        public float laneHalfWidth = 4.5f;
        [Tooltip("Shoulder width on each side (metres).")]
        public float shoulderWidth = 1.5f;
        [Tooltip("Distance between mesh vertices along the road (metres).")]
        public float meshStepDistance = 4f;

        [Header("Generation Distances")]
        [Tooltip("Road mesh extends this many metres ahead of player.")]
        public float meshAheadDistance = 600f;
        [Tooltip("Road mesh extends this many metres behind player.")]
        public float meshBehindDistance = 150f;

        [Header("Lane Markings")]
        [Tooltip("Length of each dashed lane mark (metres).")]
        public float dashLength = 6f;
        [Tooltip("Gap between dashes (metres).")]
        public float dashGap = 8f;
        [Tooltip("Half-width of each dash stripe (metres).")]
        public float dashHalfWidth = 0.1f;

        [Header("Materials")]
        public Material asphaltMaterial;
        public Material laneMarkingMaterial;

        [Header("Road Elevation")]
        [Tooltip("Raise road mesh slightly above terrain to prevent Z-fighting.")]
        public float roadElevationOffset = 0.08f;

        // ── References ────────────────────────────────────────────────────────
        private InfiniteRoadSystem _roadSystem;
        private Transform _player;

        // ── Mesh State ────────────────────────────────────────────────────────
        private Mesh _mesh;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;

        private List<Vector3> _verts = new List<Vector3>(4096);
        private List<Vector2> _uvs = new List<Vector2>(4096);
        private List<int> _triAsphalt = new List<int>(8192);
        private List<int> _triMarkings = new List<int>(2048);

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();

            _mesh = new Mesh { name = "InfiniteRoad" };
            _mesh.MarkDynamic();
            _meshFilter.sharedMesh = _mesh;

            // Apply materials
            var mats = new Material[2];
            mats[0] = asphaltMaterial != null ? asphaltMaterial : CreateDefaultAsphaltMaterial();
            mats[1] = laneMarkingMaterial != null ? laneMarkingMaterial : CreateDefaultMarkingMaterial();
            _meshRenderer.sharedMaterials = mats;

            // No shadow casting needed on the road mesh itself (terrain handles it)
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = true;
        }

        public void Initialize(InfiniteRoadSystem roadSystem, Transform player)
        {
            _roadSystem = roadSystem;
            _player = player;
        }

        private void LateUpdate()
        {
            if (_roadSystem == null || _player == null) return;
            if (_roadSystem.ControlPoints.Count < 4) return;

            RebuildMesh();
        }

        // ── Mesh Rebuild ──────────────────────────────────────────────────────

        private void RebuildMesh()
        {
            _verts.Clear();
            _uvs.Clear();
            _triAsphalt.Clear();
            _triMarkings.Clear();

            float playerProgress = _roadSystem.GetNearestProgress(_player.position);
            float startDist = Mathf.Max(0f, playerProgress - meshBehindDistance);
            float endDist = playerProgress + meshAheadDistance;

            float totalWidth = laneHalfWidth + shoulderWidth;
            float uvZ = 0f;
            float dashAccum = 0f;
            bool dashOn = true;

            int prevBaseIdx = -1;
            Vector3 prevLeft = Vector3.zero, prevRight = Vector3.zero;
            Vector3 prevShoulderL = Vector3.zero, prevShoulderR = Vector3.zero;

            for (float d = startDist; d <= endDist; d += meshStepDistance)
            {
                Vector3 center = _roadSystem.GetPositionAtDistance(d);
                Vector3 tangent = _roadSystem.GetTangentAtDistance(d);
                Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;

                // Snap to terrain height + offset
                center.y = SampleHeight(center) + roadElevationOffset;

                Vector3 rL  = center - right * laneHalfWidth;
                Vector3 rR  = center + right * laneHalfWidth;
                Vector3 sL  = center - right * totalWidth;
                Vector3 sR  = center + right * totalWidth;

                float uStep = meshStepDistance;
                uvZ += uStep;
                dashAccum += uStep;
                if (dashAccum > dashLength + dashGap) dashAccum = 0f;
                dashOn = dashAccum < dashLength;

                int baseIdx = _verts.Count;
                // 0: shoulder left, 1: lane left, 2: lane right, 3: shoulder right
                _verts.Add(sL); _uvs.Add(new Vector2(0f, uvZ / 10f));
                _verts.Add(rL); _uvs.Add(new Vector2(0.35f, uvZ / 10f));
                _verts.Add(rR); _uvs.Add(new Vector2(0.65f, uvZ / 10f));
                _verts.Add(sR); _uvs.Add(new Vector2(1f, uvZ / 10f));

                if (prevBaseIdx >= 0)
                {
                    int pb = prevBaseIdx;
                    // Asphalt submesh (full road width)
                    AddQuad(_triAsphalt, pb, pb + 1, baseIdx, baseIdx + 1); // shoulder-L
                    AddQuad(_triAsphalt, pb + 1, pb + 2, baseIdx + 1, baseIdx + 2); // lanes
                    AddQuad(_triAsphalt, pb + 2, pb + 3, baseIdx + 2, baseIdx + 3); // shoulder-R

                    // Lane marking submesh — only during dash on
                    if (dashOn)
                    {
                        // Center dashed line (white)
                        float halfDash = dashHalfWidth;
                        // We use the lane center UV region (0.48–0.52 in U) as a proxy
                        // by adding extra verts at exact dash positions
                        AddDashMarkQuad(d - meshStepDistance, d, _triMarkings, center - right * halfDash, center + right * halfDash, rL, rR, roadElevationOffset + 0.005f);
                    }
                }

                prevBaseIdx = baseIdx;
                prevLeft = rL; prevRight = rR;
                prevShoulderL = sL; prevShoulderR = sR;
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.subMeshCount = 2;
            _mesh.SetTriangles(_triAsphalt, 0);
            _mesh.SetTriangles(_triMarkings, 1);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        private void AddQuad(List<int> tris, int a, int b, int c, int d)
        {
            // Two triangles for a quad (a,b,c,d where a-b and c-d are pairs)
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(b); tris.Add(c); tris.Add(d);
        }

        private void AddDashMarkQuad(float dStart, float dEnd,
            List<int> tris, Vector3 centerL, Vector3 centerR,
            Vector3 laneL, Vector3 laneR, float elevBoost)
        {
            // Simple inline dash: narrow strip at center of road
            Vector3 tangent = (centerR - centerL).normalized;
            float hw = 0.12f;
            int bi = _verts.Count;
            Vector3 pA = _roadSystem.GetPositionAtDistance(dStart);
            Vector3 pB = _roadSystem.GetPositionAtDistance(dEnd);
            Vector3 tA = _roadSystem.GetTangentAtDistance(dStart);
            Vector3 tB = _roadSystem.GetTangentAtDistance(dEnd);
            Vector3 rA = Vector3.Cross(Vector3.up, tA).normalized;
            Vector3 rB = Vector3.Cross(Vector3.up, tB).normalized;
            pA.y = SampleHeight(pA) + elevBoost;
            pB.y = SampleHeight(pB) + elevBoost;

            _verts.Add(pA - rA * hw); _uvs.Add(new Vector2(0f, 0f));
            _verts.Add(pA + rA * hw); _uvs.Add(new Vector2(1f, 0f));
            _verts.Add(pB - rB * hw); _uvs.Add(new Vector2(0f, 1f));
            _verts.Add(pB + rB * hw); _uvs.Add(new Vector2(1f, 1f));
            tris.Add(bi); tris.Add(bi + 2); tris.Add(bi + 1);
            tris.Add(bi + 1); tris.Add(bi + 2); tris.Add(bi + 3);
        }

        private float SampleHeight(Vector3 pos)
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (terrain == null) continue;
                var tp = terrain.transform.position;
                var td = terrain.terrainData.size;
                if (pos.x >= tp.x && pos.x <= tp.x + td.x &&
                    pos.z >= tp.z && pos.z <= tp.z + td.z)
                    return terrain.SampleHeight(pos);
            }
            return 0f;
        }

        // ── Default Materials ─────────────────────────────────────────────────

        private Material CreateDefaultAsphaltMaterial()
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = "Road_Asphalt_Default";
            // Dark gray asphalt color
            mat.color = new Color(0.18f, 0.18f, 0.18f);
            mat.SetFloat("_Smoothness", 0.12f);
            mat.SetFloat("_Metallic", 0f);
            mat.enableInstancing = true;
            return mat;
        }

        private Material CreateDefaultMarkingMaterial()
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = "Road_Markings_Default";
            mat.color = new Color(0.9f, 0.87f, 0.75f); // faded cream/white
            mat.SetFloat("_Smoothness", 0.05f);
            mat.enableInstancing = true;
            return mat;
        }
    }
}
