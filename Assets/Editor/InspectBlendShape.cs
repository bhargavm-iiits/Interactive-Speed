using UnityEngine;
using UnityEditor;

namespace InfiniteWorld
{
    public class InspectBlendShape : EditorWindow
    {
        [MenuItem("Tools/Inspect Teacher Blendshape")]
        public static void Inspect()
        {
            string fbxPath = "Assets/Meshy_AI_Open_Armed_Dapper_in__biped/Meshy_AI_Open_Armed_Dapper_in__biped_Animation_Talk_with_Right_Hand_Open_withSkin.fbx";
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null)
            {
                Debug.LogError($"[InspectBlendShape] Could not find FBX at: {fbxPath}");
                return;
            }

            var smr = fbx.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null)
            {
                Debug.LogError("[InspectBlendShape] No SkinnedMeshRenderer found on FBX.");
                return;
            }

            Mesh mesh = smr.sharedMesh;
            if (mesh == null)
            {
                Debug.LogError("[InspectBlendShape] SkinnedMeshRenderer has no sharedMesh.");
                return;
            }

            int count = mesh.blendShapeCount;
            Debug.Log($"[InspectBlendShape] Mesh name: {mesh.name}, BlendShape count: {count}");

            for (int i = 0; i < count; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                int frameCount = mesh.GetBlendShapeFrameCount(i);
                Debug.Log($"  BlendShape index {i}: '{name}', Frame count: {frameCount}");

                if (frameCount > 0)
                {
                    float weight = mesh.GetBlendShapeFrameWeight(i, 0);
                    Vector3[] deltaVertices = new Vector3[mesh.vertexCount];
                    Vector3[] deltaNormals = new Vector3[mesh.vertexCount];
                    Vector3[] deltaTangents = new Vector3[mesh.vertexCount];

                    mesh.GetBlendShapeFrameVertices(i, 0, deltaVertices, deltaNormals, deltaTangents);

                    float maxVertexDelta = 0f;
                    int deformedCount = 0;
                    for (int v = 0; v < deltaVertices.Length; v++)
                    {
                        float mag = deltaVertices[v].magnitude;
                        if (mag > 0.0001f)
                        {
                            deformedCount++;
                            if (mag > maxVertexDelta) maxVertexDelta = mag;
                        }
                    }

                    Debug.Log($"    Frame 0 (weight={weight}): Deformed vertices={deformedCount}/{mesh.vertexCount}, Max delta magnitude={maxVertexDelta:F6}");
                }
            }
        }
    }
}
