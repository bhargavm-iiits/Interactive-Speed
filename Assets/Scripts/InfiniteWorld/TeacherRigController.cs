using UnityEngine;

namespace InfiniteWorld
{
    /// <summary>
    /// Procedural Rigging and Animation Controller for static teacher character OBJ model.
    /// Deforms mesh vertices dynamically to animate the mouth (speaking motion) and the arm (pointing/explaining motion).
    /// </summary>
    public class TeacherRigController : MonoBehaviour
    {
        private class MeshData
        {
            public MeshFilter filter;
            public Vector3[] originalVertices;
            public Vector3[] deformedVertices;
            public int[] mouthVertexIndices;
            public int[] rightArmVertexIndices;
        }

        private System.Collections.Generic.List<MeshData> _meshes = new System.Collections.Generic.List<MeshData>();
        private bool _initialized = false;
        private Vector3 _wristPivot = new Vector3(-11.0f, 0f, 0f);
        private float _mouthTargetX = 0f;
        private float _mouthTargetY = 0f;
        private float _mouthTargetZ = 0f;

        [Header("Animation Settings")]
        public bool isSpeaking = true;
        public bool isPointing = true;
        public AudioSource audioSource;

        private float _speakTimer = 0f;
        private float _mouthOpenAmount = 0f;
        private float _maxObservedRms = 0.05f;
        private float _armAnimTimer = 0f;

        private void Start()
        {
            InitializeRigging();
        }

        private void InitializeRigging()
        {
            var filters = GetComponentsInChildren<MeshFilter>();
            if (filters.Length == 0) return;

#if UNITY_EDITOR
            // Automatically enable Read/Write on the model importers if disabled
            bool needsReimport = false;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                string assetPath = UnityEditor.AssetDatabase.GetAssetPath(filter.sharedMesh);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    var importer = UnityEditor.AssetImporter.GetAtPath(assetPath) as UnityEditor.ModelImporter;
                    if (importer != null && !importer.isReadable)
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                        needsReimport = true;
                        Debug.Log($"[TeacherRigController] Automatically enabled Read/Write on model: {assetPath}");
                    }
                }
            }
            if (needsReimport)
            {
                filters = GetComponentsInChildren<MeshFilter>();
            }
#endif

            // Find global height (max Y) and bottom (min Y) of the character
            float maxY = -9999f;
            float minY = 9999f;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                Vector3[] verts = filter.sharedMesh.vertices;
                foreach (var v in verts)
                {
                    if (v.y > maxY) maxY = v.y;
                    if (v.y < minY) minY = v.y;
                }
            }

            float height = maxY - minY;
            if (height <= 0) return;

            // Find face orientation (+Z or -Z) and horizontal center
            // We examine the head region (top 15% of the body height)
            float maxZ = -9999f;
            float minZ = 9999f;
            float sumZ = 0f;
            float sumX = 0f;
            int countHeadVerts = 0;

            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                Vector3[] verts = filter.sharedMesh.vertices;
                foreach (var v in verts)
                {
                    if (v.y > minY + height * 0.85f)
                    {
                        if (v.z > maxZ) maxZ = v.z;
                        if (v.z < minZ) minZ = v.z;
                        sumZ += v.z;
                        sumX += v.x;
                        countHeadVerts++;
                    }
                }
            }

            float centerZ = countHeadVerts > 0 ? sumZ / countHeadVerts : 0f;
            bool faceIsPositiveZ = (maxZ - centerZ) > (centerZ - minZ);
            
            _mouthTargetX = 0f; // Reset to 0f as character origin is symmetrical, avoiding bushy hair bias
            _mouthTargetY = minY + height * 0.88f; // Mouth is at ~88% of character height
            _mouthTargetZ = faceIsPositiveZ ? maxZ : minZ;

            // Locate and group vertex indices for mouth and right pointing arm
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;

                // Instantiate instance mesh to allow write access to vertices
                Mesh mesh = filter.mesh;
                Vector3[] origVerts = mesh.vertices;
                
                var meshData = new MeshData();
                meshData.filter = filter;
                meshData.originalVertices = origVerts;
                meshData.deformedVertices = (Vector3[])origVerts.Clone();

                var mouthList = new System.Collections.Generic.List<int>();
                var armList = new System.Collections.Generic.List<int>();

                for (int i = 0; i < origVerts.Length; i++)
                {
                    Vector3 v = origVerts[i];

                    // 1. Precise Mouth/Jaw Vertices Selection:
                    // Must be centered in X (using dynamically calculated horizontal face center),
                    // at the correct mouth height range in Y, and at the front of the face in Z.
                    bool inX = Mathf.Abs(v.x - _mouthTargetX) < 1.8f; // wider selection to capture both corners of mouth fully
                    bool inY = v.y < _mouthTargetY + 0.5f && v.y > _mouthTargetY - 2.2f;
                    bool inZ = faceIsPositiveZ ? (v.z > _mouthTargetZ - 1.2f) : (v.z < _mouthTargetZ + 1.2f);

                    if (inX && inY && inZ)
                    {
                        mouthList.Add(i);
                    }

                    // 2. Pointing Right Hand Vertices Selection:
                    // Character's pointing hand is on the far left side in OBJ coordinate space (X < -11.0f).
                    // Neck and head region should not be affected, so height is limited.
                    if (v.x < -11.0f && v.y < minY + height * 0.82f && v.y > minY + height * 0.2f)
                    {
                        armList.Add(i);
                    }
                }

                meshData.mouthVertexIndices = mouthList.ToArray();
                meshData.rightArmVertexIndices = armList.ToArray();
                _meshes.Add(meshData);
            }

            // Find wrist pivot dynamically by averaging all vertices around the X boundary (-10.8f to -11.2f)
            Vector3 wristSum = Vector3.zero;
            int wristCount = 0;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                Vector3[] verts = filter.sharedMesh.vertices;
                foreach (var v in verts)
                {
                    if (v.x < -10.8f && v.x > -11.2f && v.y < minY + height * 0.82f && v.y > minY + height * 0.2f)
                    {
                        wristSum += v;
                        wristCount++;
                    }
                }
            }
            if (wristCount > 0)
            {
                _wristPivot = wristSum / wristCount;
                Debug.Log($"[TeacherRigController] Dynamically calculated wrist pivot: {_wristPivot}");
            }
            else
            {
                _wristPivot = new Vector3(-11.0f, minY + height * 0.55f, 0f);
                Debug.Log($"[TeacherRigController] Fallback wrist pivot: {_wristPivot}");
            }

            _initialized = true;
            Debug.Log($"[TeacherRigController] Procedural Rigging Initialized. Face Z+ = {faceIsPositiveZ}, Height = {height:F2}. Found meshes: {_meshes.Count}");
        }

        private void Update()
        {
            if (!_initialized || _meshes.Count == 0) return;

            // 1. Mouth Lipsync/Speech Animation
            if (audioSource != null && audioSource.isPlaying)
            {
                float[] samples = new float[64];
                audioSource.GetOutputData(samples, 0);
                float sum = 0f;
                for (int i = 0; i < samples.Length; i++)
                {
                    sum += samples[i] * samples[i];
                }
                float rms = Mathf.Sqrt(sum / samples.Length);
                
                // Track dynamic max volume to normalize mouth movements
                if (rms > _maxObservedRms)
                {
                    _maxObservedRms = rms;
                }
                _maxObservedRms = Mathf.Max(0.05f, _maxObservedRms - Time.deltaTime * 0.02f); // slow decay to adapt to other clips

                float targetOpen = 0f;
                if (rms > 0.005f) // Noise gate
                {
                    targetOpen = Mathf.Clamp01(rms / _maxObservedRms);
                }
                _mouthOpenAmount = Mathf.Lerp(_mouthOpenAmount, targetOpen, Time.deltaTime * 18f);
            }
            else if (isSpeaking)
            {
                _speakTimer += Time.deltaTime;
                float baseFreq = 14f; // slightly faster for more dynamic speaking
                float wave = Mathf.Sin(_speakTimer * baseFreq);
                // Open and close mouth with random talking variance
                _mouthOpenAmount = Mathf.Clamp01(wave * 0.5f + 0.5f) * (0.6f + Mathf.PingPong(_speakTimer * 3f, 0.4f));
                
                // Add conversational pauses
                if (Mathf.PingPong(_speakTimer * 0.4f, 1f) > 0.75f)
                {
                    _mouthOpenAmount = 0f;
                }
            }
            else
            {
                _mouthOpenAmount = Mathf.Lerp(_mouthOpenAmount, 0f, Time.deltaTime * 8f);
            }

            // 2. Arm Pointing/Explaining Animation
            float armAngle = 0f;
            if (isPointing)
            {
                _armAnimTimer += Time.deltaTime;
                // Gesturing/pointing motion (up and down by 14 degrees for clear, distinct pointing movement)
                armAngle = Mathf.Sin(_armAnimTimer * 2.2f) * 12f + Mathf.PingPong(_armAnimTimer * 4.0f, 6f) - 3f;
            }

            // Apply deformations
            foreach (var meshData in _meshes)
            {
                Vector3[] orig = meshData.originalVertices;
                Vector3[] def = meshData.deformedVertices;

                // Reset to original mesh coordinates
                System.Array.Copy(orig, def, orig.Length);

                float mouthTargetX = _mouthTargetX;
                float mouthTargetY = _mouthTargetY;

                // Deform mouth (jaw/lower lip vertices shift down)
                if (_mouthOpenAmount > 0.01f)
                {
                    // Clean jaw movement:
                    // 1. Only deform vertices below the mouth center (v.y < mouthTargetY) to keep upper lip/nose 100% static.
                    // 2. Weight is based on horizontal distance (X) from center to fade out at cheeks/corners.
                    // 3. Weight also fades out near the bottom of the chin (v.y near mouthTargetY - 2.0f) to blend smoothly with neck.
                    float maxMouthShiftY = -0.55f; // Adjusted speaking scale for a natural and clean jaw drop

                    foreach (int idx in meshData.mouthVertexIndices)
                    {
                        Vector3 v = orig[idx];

                        // Keep upper lip and nose static
                        if (v.y >= mouthTargetY) continue;

                        // Horizontal weight (fade out at cheeks/corners)
                        float weightX = 1f - (Mathf.Abs(v.x - mouthTargetX) / 1.8f);
                        weightX = Mathf.Clamp01(weightX);

                        // Vertical weight (fade out at lower neck/chin boundary to blend smoothly)
                        float weightY = (v.y - (mouthTargetY - 2.0f)) / 2.0f;
                        weightY = Mathf.Clamp01(weightY);

                        float weight = weightX * weightY;

                        def[idx].y += maxMouthShiftY * _mouthOpenAmount * weight;
                    }
                }

                // Deform hand (rotate hand vertices around wrist pivot with soft weight skinning)
                if (isPointing)
                {
                    foreach (int idx in meshData.rightArmVertexIndices)
                    {
                        Vector3 v = orig[idx];

                        // Soft skinning weight:
                        // Wrist connection at X = -11.0f gets 0% rotation.
                        // Hand/fingers at X = -12.5f gets 100% rotation.
                        float weight = (v.x - (-11.0f)) / (-12.5f - (-11.0f));
                        weight = Mathf.Clamp01(weight);

                        if (weight > 0.01f)
                        {
                            float angle = armAngle * 1.6f * weight;
                            float rZ = angle * Mathf.Deg2Rad;
                            float rY = angle * 0.5f * Mathf.Deg2Rad; // smaller yaw wave

                            // Translate relative to wrist pivot
                            float tx = v.x - _wristPivot.x;
                            float ty = v.y - _wristPivot.y;
                            float tz = v.z - _wristPivot.z;

                            // Apply rotation around Z (in XY plane, up/down wave)
                            float cosZ = Mathf.Cos(rZ);
                            float sinZ = Mathf.Sin(rZ);
                            float x1 = tx * cosZ - ty * sinZ;
                            float y1 = tx * sinZ + ty * cosZ;

                            // Apply rotation around Y (in XZ plane, forward/backward wave)
                            float cosY = Mathf.Cos(rY);
                            float sinY = Mathf.Sin(rY);
                            float x2 = x1 * cosY - tz * sinY;
                            float z2 = x1 * sinY + tz * cosY;

                            def[idx].x = x2 + _wristPivot.x;
                            def[idx].y = y1 + _wristPivot.y;
                            def[idx].z = z2 + _wristPivot.z;
                        }
                    }
                }

                // Write deformed vertices back to the mesh
                meshData.filter.mesh.vertices = def;
                meshData.filter.mesh.RecalculateNormals();
                meshData.filter.mesh.RecalculateBounds();
            }
        }
    }
}
