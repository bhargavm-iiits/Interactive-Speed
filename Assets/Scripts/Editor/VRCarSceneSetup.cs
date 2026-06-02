using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InfiniteWorld
{
    /// <summary>
    /// One-click tool to create the VRCar GameObject in the scene.
    /// Run via: Tools → Speed → Create VRCar In Scene
    ///
    /// Creates a visible car body (box primitives) at the road start position
    /// with all required components so StraightLineDriver can find it.
    /// </summary>
    public static class VRCarSceneSetup
    {
#if UNITY_EDITOR
        [MenuItem("Tools/Speed/Create VRCar In Scene")]
        public static void CreateVRCar()
        {
            // Check if one already exists
            var existing = GameObject.Find("VRCar");
            if (existing != null)
            {
                EditorUtility.DisplayDialog("VRCar Setup",
                    "VRCar already exists in the scene!", "OK");
                Selection.activeGameObject = existing;
                SceneView.FrameLastActiveSceneView();
                return;
            }

            // ── Create VRCar root ──────────────────────────────────────────────
            var carGO = new GameObject("VRCar");
            carGO.transform.position = new Vector3(0f, 1f, 20f); // near road start

            // ── Add required components ────────────────────────────────────────
            carGO.AddComponent<Vehicle.VRCarController>();
            carGO.AddComponent<Vehicle.VRCockpitBuilder>();

            // ── Build visible car body shell ───────────────────────────────────
            BuildCarShell(carGO.transform);

            // ── Register undo and select ───────────────────────────────────────
            Undo.RegisterCreatedObjectUndo(carGO, "Create VRCar");
            Selection.activeGameObject = carGO;

            // Frame it in scene view
            SceneView.lastActiveSceneView?.FrameSelected();

            Debug.Log("[VRCarSceneSetup] VRCar created at position (0, 1, 20). " +
                      "Move it to your road start, then press Play.");

            EditorUtility.DisplayDialog("VRCar Created!",
                "VRCar has been created in the scene.\n\n" +
                "1. It is now selected in the Hierarchy.\n" +
                "2. Press F in Scene view to frame it.\n" +
                "3. Press Play — StraightLineDriver will place it on the road automatically.",
                "Got it!");
        }

        private static void BuildCarShell(Transform carRoot)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(0.1f, 0.15f, 0.25f); // dark navy

            // Car body — main slab
            CreateBox(carRoot, "CarBody",
                new Vector3(0f, 0.5f, 0f),
                new Vector3(1.8f, 0.5f, 4.2f), mat);

            // Roof
            CreateBox(carRoot, "CarRoof",
                new Vector3(0f, 1.1f, -0.3f),
                new Vector3(1.6f, 0.45f, 2.4f), mat);

            // Hood
            var hoodMat = new Material(mat);
            hoodMat.color = new Color(0.08f, 0.12f, 0.22f);
            var hood = CreateBox(carRoot, "CarHood",
                new Vector3(0f, 0.8f, 1.6f),
                new Vector3(1.7f, 0.08f, 1.2f), hoodMat);
            hood.localRotation = Quaternion.Euler(-10f, 0f, 0f);

            // Windshield (tinted glass)
            var glassMat = new Material(mat);
            glassMat.color = new Color(0.3f, 0.5f, 0.7f, 0.4f);
            SetTransparent(glassMat);
            var ws = CreateBox(carRoot, "Windshield",
                new Vector3(0f, 1.05f, 0.85f),
                new Vector3(1.55f, 0.6f, 0.05f), glassMat);
            ws.localRotation = Quaternion.Euler(-25f, 0f, 0f);

            // Rear window
            var rw = CreateBox(carRoot, "RearWindow",
                new Vector3(0f, 1.0f, -1.45f),
                new Vector3(1.4f, 0.5f, 0.05f), glassMat);
            rw.localRotation = Quaternion.Euler(25f, 0f, 0f);

            // Wheels
            var wheelMat = new Material(mat);
            wheelMat.color = new Color(0.08f, 0.08f, 0.08f);
            CreateWheel(carRoot, "WheelFL", new Vector3(-0.92f, 0.35f,  1.3f), wheelMat);
            CreateWheel(carRoot, "WheelFR", new Vector3( 0.92f, 0.35f,  1.3f), wheelMat);
            CreateWheel(carRoot, "WheelRL", new Vector3(-0.92f, 0.35f, -1.3f), wheelMat);
            CreateWheel(carRoot, "WheelRR", new Vector3( 0.92f, 0.35f, -1.3f), wheelMat);

            // Headlights
            var lightMat = new Material(mat);
            lightMat.color = new Color(1f, 0.98f, 0.85f);
            CreateBox(carRoot, "HeadlightL",
                new Vector3(-0.55f, 0.65f, 2.12f),
                new Vector3(0.35f, 0.15f, 0.05f), lightMat);
            CreateBox(carRoot, "HeadlightR",
                new Vector3( 0.55f, 0.65f, 2.12f),
                new Vector3(0.35f, 0.15f, 0.05f), lightMat);

            // Tail lights
            var tailMat = new Material(mat);
            tailMat.color = new Color(0.9f, 0.1f, 0.1f);
            CreateBox(carRoot, "TailLightL",
                new Vector3(-0.6f, 0.65f, -2.12f),
                new Vector3(0.4f, 0.14f, 0.05f), tailMat);
            CreateBox(carRoot, "TailLightR",
                new Vector3( 0.6f, 0.65f, -2.12f),
                new Vector3(0.4f, 0.14f, 0.05f), tailMat);
        }

        private static Transform CreateBox(Transform parent, string name,
                                           Vector3 localPos, Vector3 size,
                                           Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            // Remove collider from decorative parts
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go.transform;
        }

        private static void CreateWheel(Transform parent, string name,
                                        Vector3 localPos, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = new Vector3(0.7f, 0.18f, 0.7f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        private static void SetTransparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);       // Transparent
            mat.SetFloat("_Blend", 0f);          // Alpha blend
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
        }
#endif
    }
}
