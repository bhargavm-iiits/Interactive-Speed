using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

namespace InfiniteWorld
{
    /// <summary>
    /// Procedural Rigging and Animation Controller for teacher character.
    /// Supports both static OBJ models (using CPU vertex deformation) and skinned FBX biped models (using Playables API and PBR texture merging).
    /// </summary>
    public class TeacherRigController : MonoBehaviour
    {
        private class MeshData
        {
            public MeshFilter filter;
            public SkinnedMeshRenderer skinnedRenderer;
            public Mesh mesh;
            public Vector3[] originalVertices;
            public Vector3[] deformedVertices;
            public int[] mouthVertexIndices;
            public int[] rightArmVertexIndices;
        }

        private System.Collections.Generic.List<MeshData> _meshes = new System.Collections.Generic.List<MeshData>();
        private bool _initialized = false;
        private Vector3 _wristPivot = new Vector3(-11.0f, 0f, 0f);
        private Vector3 _defaultHandPos = new Vector3(-12.5f, 0f, 0f);
        private Quaternion _currentHandRotation = Quaternion.identity;

        // Skinned character fields
        private bool _isSkinned = false;
        private PlayableGraph _playableGraph;
        private Playable _clipPlayable;
        private float _animSpeed = 0f;
        private float _scaleFactor = 1f;
        private bool _faceIsPositiveZ = true;
        private int _mouthBlendShapeIndex = -1;
        private SkinnedMeshRenderer _skinnedRenderer;
        private GameObject _upperTeethGo;
        private GameObject _lowerTeethGo;
        private Vector3 _defaultLowerTeethLocalPos;
        private static Texture2D _cachedCombinedTexture;

        private float _mouthTargetX = 0f;
        private float _mouthTargetY = 0f;
        private float _mouthTargetZ = 0f;

        [Header("Animation Settings")]
        public bool isSpeaking = true;
        public bool isPointing = true;
        public AudioSource audioSource;
        public Transform chalkTransform;
        public Transform blackboardTransform;
        public AnimationClip animationClip;

        private float _speakTimer = 0f;
        private float _mouthOpenAmount = 0f;
        private float _maxObservedRms = 0.05f;

        private void Start()
        {
            InitializeRigging();
        }

        private void InitializeRigging()
        {
            var skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>();
            if (skinnedRenderers.Length > 0)
            {
                _isSkinned = true;
                _skinnedRenderer = skinnedRenderers[0];
                if (_skinnedRenderer.sharedMesh != null)
                {
                    Mesh mesh = _skinnedRenderer.sharedMesh;
                    _mouthBlendShapeIndex = mesh.GetBlendShapeIndex("Mouth_Open");
                    Debug.Log($"[TeacherRigController] Found Mouth_Open blendshape at index: {_mouthBlendShapeIndex}. Total blendshapes: {mesh.blendShapeCount}");
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        string bsName = mesh.GetBlendShapeName(i);
                        int frameCount = mesh.GetBlendShapeFrameCount(i);
                        float maxDelta = 0f;
                        int deformedVerts = 0;
                        if (frameCount > 0)
                        {
                            Vector3[] deltaVerts = new Vector3[mesh.vertexCount];
                            mesh.GetBlendShapeFrameVertices(i, 0, deltaVerts, null, null);
                            for (int v = 0; v < deltaVerts.Length; v++)
                            {
                                float mag = deltaVerts[v].magnitude;
                                if (mag > 0.0001f)
                                {
                                    deformedVerts++;
                                    if (mag > maxDelta) maxDelta = mag;
                                }
                            }
                        }
                        Debug.Log($"[TeacherRigController] Blendshape [{i}]: '{bsName}', frames={frameCount}, deformedVerts={deformedVerts}/{mesh.vertexCount}, maxDelta={maxDelta:F6}");
                    }
                }
                InitializeSkinnedAnimation();
            }

            var filters = GetComponentsInChildren<MeshFilter>();

            _meshes.Clear();

#if UNITY_EDITOR
            // Automatically enable Read/Write on the model importers if disabled
            foreach (var filter in filters)
            {
                if (filter.sharedMesh != null) EnableReadWriteForMesh(filter.sharedMesh);
            }
            foreach (var smr in skinnedRenderers)
            {
                if (smr.sharedMesh != null) EnableReadWriteForMesh(smr.sharedMesh);
            }
#endif

            // Find global height (max Y) and bottom (min Y) of the character
            float maxY = -9999f;
            float minY = 9999f;
            float minX = 9999f;
            float maxX = -9999f;
            float minZ = 9999f;
            float maxZ = -9999f;

            System.Action<Mesh> updateBounds = (Mesh m) =>
            {
                if (m == null) return;
                Vector3[] verts = m.vertices;
                foreach (var v in verts)
                {
                    if (v.y > maxY) maxY = v.y;
                    if (v.y < minY) minY = v.y;
                    if (v.x > maxX) maxX = v.x;
                    if (v.x < minX) minX = v.x;
                    if (v.z > maxZ) maxZ = v.z;
                    if (v.z < minZ) minZ = v.z;
                }
            };

            foreach (var filter in filters)
            {
                if (filter.sharedMesh != null) updateBounds(filter.sharedMesh);
            }
            foreach (var smr in skinnedRenderers)
            {
                if (smr.sharedMesh != null) updateBounds(smr.sharedMesh);
            }

            float height = maxY - minY;
            Debug.Log($"[TeacherRigController] Vertex Bounds in Unity: X=[{minX:F4}, {maxX:F4}], Y=[{minY:F4}, {maxY:F4}], Z=[{minZ:F4}, {maxZ:F4}]");
            if (height <= 0) return;

            _scaleFactor = height / 20.0f; // Scale relative to standard 20-unit static model

            // Find face orientation and frontmost coordinate using a narrow center column (to ignore side/bushy hair)
            float faceMaxZ = -9999f;
            float faceMinZ = 9999f;
            float sumFaceZ = 0f;
            int countFaceVerts = 0;

            System.Action<Mesh> processFaceStrip = (Mesh m) =>
            {
                if (m == null) return;
                Vector3[] verts = m.vertices;
                foreach (var v in verts)
                {
                    // Center strip in X, and vertically only where the face is (80% to 93% of character height)
                    if (Mathf.Abs(v.x) < 0.15f * _scaleFactor && v.y > minY + height * 0.80f && v.y < minY + height * 0.93f)
                    {
                        if (v.z > faceMaxZ) faceMaxZ = v.z;
                        if (v.z < faceMinZ) faceMinZ = v.z;
                        sumFaceZ += v.z;
                        countFaceVerts++;
                    }
                }
            };

            foreach (var filter in filters)
            {
                if (filter.sharedMesh != null) processFaceStrip(filter.sharedMesh);
            }
            foreach (var smr in skinnedRenderers)
            {
                if (smr.sharedMesh != null) processFaceStrip(smr.sharedMesh);
            }

            float centerFaceZ = countFaceVerts > 0 ? sumFaceZ / countFaceVerts : 0f;
            bool faceIsPositiveZ = (faceMaxZ - centerFaceZ) > (centerFaceZ - faceMinZ);
            
            _mouthTargetX = 0f; // Symmetrical center
            _mouthTargetY = minY + height * 0.875f; // Center of mouth
            _mouthTargetZ = faceIsPositiveZ ? faceMaxZ : faceMinZ;
            _faceIsPositiveZ = faceIsPositiveZ;

            // Tight bounds optimized for clean mouth/jaw movement on biped models (keeps neck/shoulders static)
            float searchLimitX = 0.8f * _scaleFactor;
            float searchLimitYUp = 0.2f * _scaleFactor;
            float searchLimitYDown = 0.8f * _scaleFactor;
            float searchLimitZ = 0.5f * _scaleFactor;

            // Locate and group vertex indices for mouth and right pointing arm
            System.Action<Mesh, MeshFilter, SkinnedMeshRenderer> addMeshData = (Mesh sharedMesh, MeshFilter filter, SkinnedMeshRenderer smr) =>
            {
                if (sharedMesh == null) return;

                Mesh meshInstance;
                if (filter != null)
                {
                    meshInstance = filter.mesh;
                }
                else
                {
                    meshInstance = Instantiate(sharedMesh);
                    smr.sharedMesh = meshInstance;
                }

                Vector3[] origVerts = meshInstance.vertices;
                
                var meshData = new MeshData();
                meshData.filter = filter;
                meshData.skinnedRenderer = smr;
                meshData.mesh = meshInstance;
                meshData.originalVertices = origVerts;
                meshData.deformedVertices = (Vector3[])origVerts.Clone();

                var mouthList = new System.Collections.Generic.List<int>();
                var armList = new System.Collections.Generic.List<int>();

                for (int i = 0; i < origVerts.Length; i++)
                {
                    Vector3 v = origVerts[i];

                    bool inX = Mathf.Abs(v.x - _mouthTargetX) < searchLimitX;
                    bool inY = v.y < _mouthTargetY + searchLimitYUp && v.y > _mouthTargetY - searchLimitYDown;
                    bool inZ = faceIsPositiveZ ? (v.z > _mouthTargetZ - searchLimitZ) : (v.z < _mouthTargetZ + searchLimitZ);

                    if (inX && inY && inZ)
                    {
                        mouthList.Add(i);
                    }

                    if (v.x < -11.0f && v.y < minY + height * 0.82f && v.y > minY + height * 0.2f)
                    {
                        armList.Add(i);
                    }
                }

                meshData.mouthVertexIndices = mouthList.ToArray();
                meshData.rightArmVertexIndices = armList.ToArray();
                _meshes.Add(meshData);

                string name = filter != null ? filter.name : smr.name;
                Debug.Log($"[TeacherRigController] Mesh {name}: Mouth Verts count={mouthList.Count}, Arm Verts count={armList.Count}");
            };

            foreach (var filter in filters)
            {
                if (filter.sharedMesh != null) addMeshData(filter.sharedMesh, filter, null);
            }
            foreach (var smr in skinnedRenderers)
            {
                if (smr.sharedMesh != null) addMeshData(smr.sharedMesh, null, smr);
            }

            // Find wrist pivot dynamically by averaging all vertices around the X boundary (-10.8f to -11.2f)
            Vector3 wristSum = Vector3.zero;
            int wristCount = 0;
            System.Action<Mesh> calculateWrist = (Mesh m) =>
            {
                if (m == null) return;
                Vector3[] verts = m.vertices;
                foreach (var v in verts)
                {
                    if (v.x < -10.8f && v.x > -11.2f && v.y < minY + height * 0.82f && v.y > minY + height * 0.2f)
                    {
                        wristSum += v;
                        wristCount++;
                    }
                }
            };
            foreach (var filter in filters)
            {
                if (filter.sharedMesh != null) calculateWrist(filter.sharedMesh);
            }
            foreach (var smr in skinnedRenderers)
            {
                if (smr.sharedMesh != null) calculateWrist(smr.sharedMesh);
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

            // Find default hand position (vertex in the hand with the minimum X coordinate)
            float minArmX = 9999f;
            Vector3 handPosDefault = Vector3.zero;
            System.Action<Mesh> calculateHandDefault = (Mesh m) =>
            {
                if (m == null) return;
                Vector3[] verts = m.vertices;
                foreach (var v in verts)
                {
                    if (v.x < -11.0f && v.y < minY + height * 0.82f && v.y > minY + height * 0.2f)
                    {
                        if (v.x < minArmX)
                        {
                            minArmX = v.x;
                            handPosDefault = v;
                        }
                    }
                }
            };
            foreach (var filter in filters)
            {
                if (filter.sharedMesh != null) calculateHandDefault(filter.sharedMesh);
            }
            foreach (var smr in skinnedRenderers)
            {
                if (smr.sharedMesh != null) calculateHandDefault(smr.sharedMesh);
            }

            if (minArmX < 9999f)
            {
                _defaultHandPos = handPosDefault;
                Debug.Log($"[TeacherRigController] Dynamically calculated default hand position: {_defaultHandPos}");
            }
            else
            {
                _defaultHandPos = new Vector3(-12.5f, minY + height * 0.55f, 0f);
                Debug.Log($"[TeacherRigController] Fallback default hand position: {_defaultHandPos}");
            }

            _currentHandRotation = Quaternion.identity;
            _initialized = true;
            CreateProceduralTeeth(faceIsPositiveZ);
            Debug.Log($"[TeacherRigController] Procedural Rigging Initialized. Face Z+ = {faceIsPositiveZ}, Height = {height:F2}. Found meshes: {_meshes.Count}");
        }

#if UNITY_EDITOR
        private static void EnableReadWriteForMesh(Mesh mesh)
        {
            if (mesh == null) return;
            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(mesh);
            if (!string.IsNullOrEmpty(assetPath))
            {
                var importer = UnityEditor.AssetImporter.GetAtPath(assetPath) as UnityEditor.ModelImporter;
                if (importer != null && !importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    Debug.Log($"[TeacherRigController] Automatically enabled Read/Write on model: {assetPath}");
                }
            }
        }
#endif

        private void InitializeSkinnedAnimation()
        {
#if UNITY_EDITOR
            ConfigureTeacherHDAssets(false);
#endif
#if UNITY_EDITOR
            if (animationClip == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Meshy_AI_Open_Armed_Dapper_in__biped/Meshy_AI_Open_Armed_Dapper_in__biped_Animation_Talk_with_Right_Hand_Open_withSkin.fbx");
                if (assets != null)
                {
                    foreach (var asset in assets)
                    {
                        if (asset is AnimationClip clip && !clip.name.Contains("__preview__"))
                        {
                            animationClip = clip;
                            break;
                        }
                    }
                }
            }
            // Enable Read/Write and Combine Metallic/Roughness textures for stunning HD rendering
            var renderers = GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var r in renderers)
            {
                Material mat = r.sharedMaterial;
                if (mat != null)
                {
                    if (_cachedCombinedTexture == null)
                    {
                        Texture2D metallicTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Meshy_AI_Open_Armed_Dapper_in__biped/Meshy_AI_Open_Armed_Dapper_in__biped_texture_0_metallic.png");
                        Texture2D roughnessTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Meshy_AI_Open_Armed_Dapper_in__biped/Meshy_AI_Open_Armed_Dapper_in__biped_texture_0_roughness.png");
                        
                        if (metallicTex != null && roughnessTex != null)
                        {
                            EnableReadWrite(metallicTex);
                            EnableReadWrite(roughnessTex);
                            
                            _cachedCombinedTexture = CombineMetallicSmoothness(metallicTex, roughnessTex);
                        }
                    }

                    if (_cachedCombinedTexture != null)
                    {
                        // Create instance material to keep project asset unmodified
                        Material instancedMat = Instantiate(mat);
                        instancedMat.SetTexture("_MetallicGlossMap", _cachedCombinedTexture);
                        instancedMat.EnableKeyword("_METALLICGLOSSMAP");
                        instancedMat.SetFloat("_Metallic", 1.0f);
                        instancedMat.SetFloat("_Smoothness", 1.0f); // Map's Alpha channel dictates exact PBR smoothness
                        
                        r.sharedMaterial = instancedMat;
                    }
                }
            }
#endif

            if (animationClip != null)
            {
                var animator = GetComponent<Animator>();
                if (animator == null) animator = gameObject.AddComponent<Animator>();
                
                _playableGraph = PlayableGraph.Create();
                var output = AnimationPlayableOutput.Create(_playableGraph, "Animation", animator);
                
                var clipPlayable = AnimationClipPlayable.Create(_playableGraph, animationClip);
                clipPlayable.SetApplyFootIK(false);
                clipPlayable.SetSpeed(0f);
                _clipPlayable = clipPlayable;
                
                output.SetSourcePlayable(clipPlayable);
                animationClip.wrapMode = WrapMode.Loop;
                _playableGraph.Play();
            }
            _initialized = true;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Tools/Configure Teacher HD Assets")]
        public static void ConfigureTeacherHDAssetsMenu()
        {
            ConfigureTeacherHDAssets(true);
        }

        public static void ConfigureTeacherHDAssetsBatch()
        {
            ConfigureTeacherHDAssets(false);
        }

        public static void ConfigureTeacherHDAssets(bool showDialog = false)
        {
            // 1. Configure the 4 textures
            string[] texturePaths = new string[]
            {
                "Assets/Meshy_AI_Open_Armed_Dapper_in__biped/Meshy_AI_Open_Armed_Dapper_in__biped_texture_0.png",
                "Assets/Meshy_AI_Open_Armed_Dapper_in__biped/Meshy_AI_Open_Armed_Dapper_in__biped_texture_0_normal.png",
                "Assets/Meshy_AI_Open_Armed_Dapper_in__biped/Meshy_AI_Open_Armed_Dapper_in__biped_texture_0_metallic.png",
                "Assets/Meshy_AI_Open_Armed_Dapper_in__biped/Meshy_AI_Open_Armed_Dapper_in__biped_texture_0_roughness.png"
            };

            foreach (var path in texturePaths)
            {
                var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
                if (importer != null)
                {
                    bool changed = false;
                    if (!importer.isReadable)
                    {
                        importer.isReadable = true;
                        changed = true;
                    }
                    
                    int targetSize = 16384; // 16K for ultra detail
                    if (path.Contains("metallic") || path.Contains("roughness"))
                    {
                        targetSize = 4096; // 4K for metallic/roughness PBR reflections
                    }
                    
                    // Force default platform settings to 16K Uncompressed
                    var defaultSettings = importer.GetDefaultPlatformTextureSettings();
                    if (defaultSettings.maxTextureSize != targetSize || defaultSettings.textureCompression != UnityEditor.TextureImporterCompression.Uncompressed)
                    {
                        defaultSettings.maxTextureSize = targetSize;
                        defaultSettings.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
                        importer.SetPlatformTextureSettings(defaultSettings);
                        changed = true;
                    }

                    // Force Standalone platform settings to 16K Uncompressed
                    var standaloneSettings = importer.GetPlatformTextureSettings("Standalone");
                    if (!standaloneSettings.overridden || standaloneSettings.maxTextureSize != targetSize || standaloneSettings.textureCompression != UnityEditor.TextureImporterCompression.Uncompressed)
                    {
                        standaloneSettings.overridden = true;
                        standaloneSettings.maxTextureSize = targetSize;
                        standaloneSettings.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
                        importer.SetPlatformTextureSettings(standaloneSettings);
                        changed = true;
                    }

                    if (importer.maxTextureSize != targetSize || importer.textureCompression != UnityEditor.TextureImporterCompression.Uncompressed)
                    {
                        importer.maxTextureSize = targetSize;
                        importer.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
                        changed = true;
                    }
                    
                    if (changed)
                    {
                        importer.SaveAndReimport();
                        Debug.Log($"[TeacherRigController] Configured HD texture at: {path} (Target Size: {targetSize}, Uncompressed)");
                    }
                }
            }

            // 2. Configure the animation clip to loop
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Meshy_AI_Open_Armed_Dapper_in__biped/Meshy_AI_Open_Armed_Dapper_in__biped_Animation_Talk_with_Right_Hand_Open_withSkin.fbx");
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    if (asset is AnimationClip clip && !clip.name.Contains("__preview__"))
                    {
                        var settings = UnityEditor.AnimationUtility.GetAnimationClipSettings(clip);
                        if (!settings.loopTime)
                        {
                            settings.loopTime = true;
                            UnityEditor.AnimationUtility.SetAnimationClipSettings(clip, settings);
                            UnityEditor.EditorUtility.SetDirty(clip);
                            UnityEditor.AssetDatabase.SaveAssets();
                            Debug.Log("[TeacherRigController] Configured loop settings for animation clip.");
                        }
                        break;
                    }
                }
            }
            // 3. Configure FBX Model Importer and force re-import
            string fbxPath = "Assets/Meshy_AI_Open_Armed_Dapper_in__biped/Meshy_AI_Open_Armed_Dapper_in__biped_Animation_Talk_with_Right_Hand_Open_withSkin.fbx";
            var modelImporter = UnityEditor.AssetImporter.GetAtPath(fbxPath) as UnityEditor.ModelImporter;
            if (modelImporter != null)
            {
                bool modelImporterChanged = false;
                if (!modelImporter.importBlendShapes)
                {
                    modelImporter.importBlendShapes = true;
                    modelImporterChanged = true;
                }
                
                if (modelImporterChanged)
                {
                    modelImporter.SaveAndReimport();
                    Debug.Log("[TeacherRigController] Enabled importBlendShapes on the teacher FBX model importer.");
                }
                else
                {
                    // Force re-import to load the new shape key deforms from disk
                    UnityEditor.AssetDatabase.ImportAsset(fbxPath, UnityEditor.ImportAssetOptions.ForceUpdate);
                    Debug.Log("[TeacherRigController] Force re-imported teacher FBX model asset to load new shape keys.");
                }
            }

            if (showDialog)
            {
                UnityEditor.EditorUtility.DisplayDialog("Success", "Teacher HD textures (16K Uncompressed) and animation clip loop settings configured successfully!", "OK");
            }
        }

        private void EnableReadWrite(Texture2D tex)
        {
            if (tex == null) return;
            string path = UnityEditor.AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path)) return;
            var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
        }

        private Texture2D CombineMetallicSmoothness(Texture2D metallicTex, Texture2D roughnessTex)
        {
            int width = metallicTex.width;
            int height = metallicTex.height;
            Texture2D combined = new Texture2D(width, height, TextureFormat.RGBA32, true);
            Color32[] metallicPixels = metallicTex.GetPixels32();
            Color32[] roughnessPixels = roughnessTex.GetPixels32();
            Color32[] combinedPixels = new Color32[metallicPixels.Length];
            
            for (int i = 0; i < metallicPixels.Length; i++)
            {
                byte m = metallicPixels[i].r;
                byte r = roughnessPixels[i].r;
                byte smoothness = (byte)(255 - r);
                combinedPixels[i] = new Color32(m, m, m, smoothness);
            }
            
            combined.SetPixels32(combinedPixels);
            combined.Apply();
            return combined;
        }
#endif

        private void OnDestroy()
        {
            if (_playableGraph.IsValid())
            {
                _playableGraph.Destroy();
            }
        }

        private void Update()
        {
            if (!_initialized) return;

            // Gesture/animate only if speaking, or if chalk is writing (actively teaching)
            bool isActivelyTeaching = isSpeaking || (audioSource != null && audioSource.isPlaying) || (chalkTransform != null && chalkTransform.gameObject.activeInHierarchy);

            if (_isSkinned)
            {
                // Smoothly slerp animation speed based on whether they are actively teaching
                float targetSpeed = isActivelyTeaching ? 1.0f : 0.0f;
                _animSpeed = Mathf.MoveTowards(_animSpeed, targetSpeed, Time.deltaTime * 3.0f);
                
                if (_clipPlayable.IsValid())
                {
                    _clipPlayable.SetSpeed(_animSpeed);
                    
                    // Fallback manual loop check if the clip is not set to loop natively
                    if (animationClip != null && !animationClip.isLooping && _clipPlayable.GetTime() >= animationClip.length)
                    {
                        _clipPlayable.SetTime(0.0);
                    }
                }
            }

            // 1. Mouth Lipsync/Speech Animation
            float targetOpen = 0f;
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
                
                if (rms > 0.005f) // Noise gate
                {
                    targetOpen = Mathf.Clamp01(rms / _maxObservedRms);
                }
                else
                {
                    // Fallback to procedural chattering if RMS is 0 (e.g. read-locked compressed audio)
                    _speakTimer += Time.deltaTime;
                    float baseFreq = 8.0f;
                    float wave = Mathf.Sin(_speakTimer * baseFreq);
                    float envelope = Mathf.PerlinNoise(_speakTimer * 0.5f, 0f);
                    targetOpen = Mathf.Clamp01(wave * 0.5f + 0.5f) * (0.35f + envelope * 0.65f);
                }
            }
            else if (isSpeaking)
            {
                _speakTimer += Time.deltaTime;
                float baseFreq = 8.0f; // Slower, smoother wave for natural opening and closing
                float wave = Mathf.Sin(_speakTimer * baseFreq);
                // Smooth Perlin noise envelope for conversational volume variations
                float envelope = Mathf.PerlinNoise(_speakTimer * 0.5f, 0f);
                targetOpen = Mathf.Clamp01(wave * 0.5f + 0.5f) * (0.35f + envelope * 0.65f);
            }

            // Always smoothly lerp to targetOpen to guarantee clean mouth transitions
            _mouthOpenAmount = Mathf.Lerp(_mouthOpenAmount, targetOpen, Time.deltaTime * 12f);

#if UNITY_EDITOR
            if (Time.frameCount % 60 == 0)
            {
                float testRms = 0f;
                if (audioSource != null && audioSource.isPlaying)
                {
                    float[] samples = new float[64];
                    audioSource.GetOutputData(samples, 0);
                    float sum = 0f;
                    for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
                    testRms = Mathf.Sqrt(sum / samples.Length);
                }
                Debug.Log($"[TeacherRigController] MouthOpen={_mouthOpenAmount:F3}, RMS={testRms:F5}, isSpeaking={isSpeaking}, audioPlaying={(audioSource != null && audioSource.isPlaying)}, blendshapeIdx={_mouthBlendShapeIndex}");
            }
#endif



            // 2. Hand Pointing/Explaining Animation Target & Rotation
            Vector3 targetWorld = Vector3.zero;
            bool hasTarget = false;

            if (isPointing && isActivelyTeaching)
            {
                if (chalkTransform != null && chalkTransform.gameObject.activeInHierarchy)
                {
                    targetWorld = chalkTransform.position;
                    hasTarget = true;
                }
                else if (blackboardTransform != null)
                {
                    // Natural idle drifting across the blackboard using local right/up
                    float time = Time.time;
                    Vector3 drift = blackboardTransform.right * Mathf.Sin(time * 1.2f) * 1.1f + 
                                    blackboardTransform.up * (Mathf.Cos(time * 0.7f) * 0.35f + 0.15f);
                    targetWorld = blackboardTransform.position + drift;
                    hasTarget = true;
                }
            }

            if (isPointing && hasTarget)
            {
                // Convert world target to teacher local space
                Vector3 targetLocal = transform.InverseTransformPoint(targetWorld);
                
                Vector3 defaultHandVec = _defaultHandPos - _wristPivot;
                Vector3 targetHandVec = targetLocal - _wristPivot;
                
                if (targetHandVec.sqrMagnitude > 0.001f)
                {
                    targetHandVec = targetHandVec.normalized * defaultHandVec.magnitude;
                    Quaternion targetRot = Quaternion.FromToRotation(defaultHandVec, targetHandVec);
                    
                    // Clamp max wrist bending angle to 45 degrees for a natural look
                    float angle = Quaternion.Angle(Quaternion.identity, targetRot);
                    if (angle > 45f)
                    {
                        targetRot = Quaternion.Slerp(Quaternion.identity, targetRot, 45f / angle);
                    }
                    
                    // Smoothly slerp current rotation to target rotation to avoid snapping
                    _currentHandRotation = Quaternion.Slerp(_currentHandRotation, targetRot, Time.deltaTime * 6f);
                }
            }
            else
            {
                // Smoothly return to default posture (perfectly static pointing pose)
                _currentHandRotation = Quaternion.Slerp(_currentHandRotation, Quaternion.identity, Time.deltaTime * 4f);
            }

            // Apply deformations (only for non-skinned static meshes)
            if (!_isSkinned)
            {
                foreach (var meshData in _meshes)
                {
                    Vector3[] orig = meshData.originalVertices;
                    Vector3[] def = meshData.deformedVertices;

                    // Reset to original mesh coordinates
                    System.Array.Copy(orig, def, orig.Length);

                    float mouthTargetX = _mouthTargetX;
                    float mouthTargetY = _mouthTargetY;

                    // Deform mouth (jaw/lower lip vertices shift down and Z-depth pushes back to create 3D mouth cavity)
                    if (_mouthOpenAmount > 0.01f)
                    {
                        // Clean jaw movement:
                        // 1. Only deform vertices below the mouth center (v.y < mouthTargetY) to keep upper lip/nose 100% static.
                        // 2. Weight is based on horizontal distance (X) from center to fade out at cheeks/corners.
                        // 3. Weight also fades out near the bottom of the chin (v.y near mouthTargetY - 2.0f) to blend smoothly with neck.
                        float maxMouthShiftY = -0.55f * _scaleFactor; // Adjusted speaking scale for a natural and clean jaw drop

                        foreach (int idx in meshData.mouthVertexIndices)
                        {
                            Vector3 v = orig[idx];

                            // Keep upper lip and nose static
                            if (v.y >= mouthTargetY) continue;

                            // Horizontal weight (fade out at cheeks/corners)
                            float weightX = 1f - (Mathf.Abs(v.x - mouthTargetX) / (1.8f * _scaleFactor));
                            weightX = Mathf.Clamp01(weightX);

                            // Vertical weight (fade out at lower neck/chin boundary to blend smoothly)
                            float weightY = (v.y - (mouthTargetY - (2.0f * _scaleFactor))) / (2.0f * _scaleFactor);
                            weightY = Mathf.Clamp01(weightY);

                            float weight = weightX * weightY;

                            def[idx].y += maxMouthShiftY * _mouthOpenAmount * weight;

                            // Push center of mouth inward to create a 3D mouth cavity/hole
                            float yDistFromCenter = Mathf.Abs(v.y - mouthTargetY);
                            float zPushWeight = 1f - (yDistFromCenter / (0.8f * _scaleFactor));
                            zPushWeight = Mathf.Clamp01(zPushWeight) * weight; // fade out towards cheeks and upper/lower bounds

                            if (zPushWeight > 0.01f)
                            {
                                float pushAmount = 0.45f * _scaleFactor * _mouthOpenAmount * zPushWeight;
                                if (_faceIsPositiveZ)
                                {
                                    def[idx].z -= pushAmount;
                                }
                                else
                                {
                                    def[idx].z += pushAmount;
                                }
                            }
                        }
                    }

                    // Deform hand (rotate hand vertices around wrist pivot with soft weight skinning)
                    // Only applied for non-skinned static OBJ meshes (skinned models use bone-based right arm animation)
                    if (isPointing && !_isSkinned)
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
                                Quaternion vertexRot = Quaternion.Slerp(Quaternion.identity, _currentHandRotation, weight);

                                Vector3 localV = v - _wristPivot;
                                Vector3 rotatedV = vertexRot * localV;
                                def[idx] = rotatedV + _wristPivot;
                            }
                        }
                    }

                    // Write deformed vertices back to the mesh
                    meshData.mesh.vertices = def;
                    meshData.mesh.RecalculateNormals();
                    meshData.mesh.RecalculateBounds();
                }
            }
        }

        private void LateUpdate()
        {
            if (!_initialized) return;

            if (_isSkinned && _skinnedRenderer != null && _mouthBlendShapeIndex != -1)
            {
                float targetWeight = _mouthOpenAmount * 100f;
                _skinnedRenderer.SetBlendShapeWeight(_mouthBlendShapeIndex, targetWeight);
#if UNITY_EDITOR
                if (Time.frameCount % 60 == 0)
                {
                    float actualWeight = _skinnedRenderer.GetBlendShapeWeight(_mouthBlendShapeIndex);
                    Debug.Log($"[TeacherRigController] LateUpdate: SetWeight={targetWeight:F2}, ActualWeight={actualWeight:F2}, index={_mouthBlendShapeIndex}");
                }
#endif
            }

            if (_lowerTeethGo != null)
            {
                float teethShiftY = -0.35f * _scaleFactor * _mouthOpenAmount;
                _lowerTeethGo.transform.localPosition = _defaultLowerTeethLocalPos + new Vector3(0f, teethShiftY, 0f);
            }
        }

        private void CreateProceduralTeeth(bool faceIsPositiveZ)
        {
            _faceIsPositiveZ = faceIsPositiveZ;

            // Destroy existing ones first (if any) to prevent duplicates
            if (_upperTeethGo != null) DestroyImmediate(_upperTeethGo);
            if (_lowerTeethGo != null) DestroyImmediate(_lowerTeethGo);

            Transform headBone = FindHeadBone(this.transform);
            Transform parentTransform = headBone != null ? headBone : this.transform;

            // Clean up any existing teeth container under parentTransform
            Transform existingContainer = parentTransform.Find("ProceduralTeethContainer");
            if (existingContainer != null)
            {
                DestroyImmediate(existingContainer.gameObject);
            }

            // Create container
            var teethContainer = new GameObject("ProceduralTeethContainer");
            
            // Generate textures
            Texture2D upperTex = GenerateTeethTexture(true);
            Texture2D lowerTex = GenerateTeethTexture(false);

            // Materials using URP Lit Shader
            Material upperMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Transparent"));
            upperMat.mainTexture = upperTex;
            upperMat.SetFloat("_AlphaClip", 1f);
            upperMat.SetFloat("_Cutoff", 0.05f);
            upperMat.EnableKeyword("_ALPHATEST_ON");
            upperMat.renderQueue = 2450;

            Material lowerMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Transparent"));
            lowerMat.mainTexture = lowerTex;
            lowerMat.SetFloat("_AlphaClip", 1f);
            lowerMat.SetFloat("_Cutoff", 0.05f);
            lowerMat.EnableKeyword("_ALPHATEST_ON");
            lowerMat.renderQueue = 2450;

            // Create Upper Teeth Quad
            _upperTeethGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _upperTeethGo.name = "UpperTeeth";
            DestroyImmediate(_upperTeethGo.GetComponent<Collider>());
            _upperTeethGo.GetComponent<Renderer>().sharedMaterial = upperMat;
            _upperTeethGo.transform.SetParent(teethContainer.transform, false);

            // Create Lower Teeth Quad
            _lowerTeethGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _lowerTeethGo.name = "LowerTeeth";
            DestroyImmediate(_lowerTeethGo.GetComponent<Collider>());
            _lowerTeethGo.GetComponent<Renderer>().sharedMaterial = lowerMat;
            _lowerTeethGo.transform.SetParent(teethContainer.transform, false);

            // Scale teeth based on character size
            float teethWidth = 0.45f * _scaleFactor;
            float teethHeight = 0.10f * _scaleFactor;
            _upperTeethGo.transform.localScale = new Vector3(teethWidth, teethHeight, 1f);
            _lowerTeethGo.transform.localScale = new Vector3(teethWidth, teethHeight, 1f);

            // Align quads
            float zOffset = faceIsPositiveZ ? -0.15f * _scaleFactor : 0.15f * _scaleFactor;
            float rotY = faceIsPositiveZ ? 180f : 0f;

            teethContainer.transform.SetParent(parentTransform, false);
            
            // Set position relative to Head bone or local root
            Vector3 localOffset = parentTransform.InverseTransformPoint(transform.TransformPoint(new Vector3(_mouthTargetX, _mouthTargetY, _mouthTargetZ + zOffset)));
            teethContainer.transform.localPosition = localOffset;
            teethContainer.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);

            // Positions relative to container
            _upperTeethGo.transform.localPosition = new Vector3(0f, -0.01f * _scaleFactor, 0f);
            
            _defaultLowerTeethLocalPos = new Vector3(0f, -0.07f * _scaleFactor, 0f);
            _lowerTeethGo.transform.localPosition = _defaultLowerTeethLocalPos;
        }

        private Texture2D GenerateTeethTexture(bool isUpper)
        {
            int width = 128;
            int height = 64;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            
            Color gumColor = new Color(0.72f, 0.28f, 0.32f, 1f);
            Color toothColor = new Color(0.96f, 0.96f, 0.94f, 1f);
            Color transparent = new Color(0f, 0f, 0f, 0f);
            Color gapColor = new Color(0.12f, 0.05f, 0.08f, 1f);

            for (int y = 0; y < height; y++)
            {
                float yn = (float)y / (height - 1);
                for (int x = 0; x < width; x++)
                {
                    float xn = (float)x / (width - 1);

                    bool isGum = isUpper ? (yn > 0.48f) : (yn < 0.52f);
                    float edgeDist = isUpper ? yn : (1f - yn);

                    if (isGum)
                    {
                        tex.SetPixel(x, y, gumColor);
                    }
                    else if (edgeDist < 0.08f)
                    {
                        float toothPhase = xn * 10f;
                        float toothFrac = toothPhase - Mathf.Floor(toothPhase);
                        float cornerDist = Mathf.Min(toothFrac, 1f - toothFrac);
                        
                        if (cornerDist < 0.12f && edgeDist < (0.08f - cornerDist))
                        {
                            tex.SetPixel(x, y, transparent);
                        }
                        else
                        {
                            tex.SetPixel(x, y, toothColor);
                        }
                    }
                    else
                    {
                        float toothPhase = xn * 10f;
                        float toothFrac = toothPhase - Mathf.Floor(toothPhase);
                        
                        if (toothFrac < 0.06f || x == 0 || x == width - 1)
                        {
                            tex.SetPixel(x, y, gapColor);
                        }
                        else
                        {
                            float edgeShade = Mathf.SmoothStep(0f, 1f, toothFrac / 0.15f) * Mathf.SmoothStep(0f, 1f, (1f - toothFrac) / 0.15f);
                            Color finalColor = Color.Lerp(gapColor, toothColor, 0.25f + edgeShade * 0.75f);
                            tex.SetPixel(x, y, finalColor);
                        }
                    }
                }
            }
            tex.Apply();
            return tex;
        }

        private Transform FindHeadBone(Transform current)
        {
            if (current == null) return null;
            if (current.name.ToLowerInvariant().Contains("head") && !current.name.ToLowerInvariant().Contains("top"))
            {
                return current;
            }
            for (int i = 0; i < current.childCount; i++)
            {
                Transform found = FindHeadBone(current.GetChild(i));
                if (found != null) return found;
            }
            return null;
        }
    }
}
