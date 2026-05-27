using UnityEngine;
using UnityEditor;

namespace InfiniteWorldEditor
{
    /// <summary>
    /// One-click fixer for the Big Oak Tree FREE materials.
    /// The asset ships with TVE (The Vegetation Engine) shaders which are
    /// not installed, causing magenta/pink materials in URP projects.
    /// This tool remaps every material to URP/Lit with correct textures.
    /// </summary>
    public static class OakMaterialFixer
    {
        private const string ROOT = "Assets/ALP_Assets/Big Oak Tree FREE/Models/Materials/";

        // [MenuItem("Tools/Fix Oak Tree Materials (URP)")]
        public static void FixAll()
        {
            int fixed_ = 0;

            fixed_ += FixBranches();
            fixed_ += FixTrunk();
            fixed_ += FixGround();
            fixed_ += FixBillboard();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Oak Material Fixer",
                $"Done! Fixed {fixed_} material(s).\n\n" +
                "Run  Window → Infinite World → Setup Wizard → BUILD 5 KM WORLD  to rebuild.",
                "OK");

            Debug.Log($"[OakMaterialFixer] Fixed {fixed_} materials to URP/Lit.");
        }

        // ── Branches / Leaves ─────────────────────────────────────────────────
        private static int FixBranches()
        {
            var mat = Load("Branches001.mat");
            if (mat == null) return 0;

            // Cache existing textures before shader swap
            var albedo  = mat.GetTexture("_MainAlbedoTex") ?? mat.GetTexture("_MainTex");
            var normal  = mat.GetTexture("_MainNormalTex") ?? mat.GetTexture("_BumpMap");
            var mask    = mat.GetTexture("_MainMaskTex");

            ApplyURPLit(mat);

            // Leaf shader needs alpha clipping for transparency
            mat.SetFloat("_AlphaClip",  1f);
            mat.SetFloat("_Cutoff",     0.45f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;

            // Double-sided leaves
            mat.SetFloat("_Cull", 0f);   // CullMode.Off
            mat.doubleSidedGI = true;

            SetTex(mat, "_BaseMap",  albedo);
            SetTex(mat, "_BumpMap",  normal);

            // Tint slightly green for lush look
            mat.SetColor("_BaseColor", new Color(0.85f, 1.0f, 0.75f, 1f));
            mat.SetFloat("_Smoothness", 0.15f);

            EditorUtility.SetDirty(mat);
            Debug.Log("[OakMaterialFixer] Branches001 → URP/Lit (AlphaClip, double-sided)");
            return 1;
        }

        // ── Bark / Trunk ──────────────────────────────────────────────────────
        private static int FixTrunk()
        {
            var mat = Load("Trunk01.mat");
            if (mat == null) return 0;

            var albedo = mat.GetTexture("_MainAlbedoTex") ?? mat.GetTexture("_MainTex");
            var normal = mat.GetTexture("_MainNormalTex") ?? mat.GetTexture("_BumpMap");

            ApplyURPLit(mat);

            SetTex(mat, "_BaseMap", albedo);
            SetTex(mat, "_BumpMap", normal);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.1f);

            EditorUtility.SetDirty(mat);
            Debug.Log("[OakMaterialFixer] Trunk01 → URP/Lit");
            return 1;
        }

        // ── Ground ────────────────────────────────────────────────────────────
        private static int FixGround()
        {
            var mat = Load("Ground.mat");
            if (mat == null) return 0;

            var albedo = mat.GetTexture("_MainAlbedoTex") ?? mat.GetTexture("_MainTex");
            var normal = mat.GetTexture("_MainNormalTex") ?? mat.GetTexture("_BumpMap");

            ApplyURPLit(mat);

            SetTex(mat, "_BaseMap", albedo);
            SetTex(mat, "_BumpMap", normal);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.05f);

            EditorUtility.SetDirty(mat);
            Debug.Log("[OakMaterialFixer] Ground → URP/Lit");
            return 1;
        }

        // ── Billboard ─────────────────────────────────────────────────────────
        private static int FixBillboard()
        {
            var mat = Load("BillboardBigOak01.mat");
            if (mat == null) return 0;

            var albedo = mat.GetTexture("_MainAlbedoTex") ?? mat.GetTexture("_MainTex");

            // Use Unlit for billboard — avoids lighting artefacts on flat card
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            mat.shader = shader;

            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff",    0.45f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;

            SetTex(mat, "_BaseMap", albedo);
            mat.SetColor("_BaseColor", Color.white);

            EditorUtility.SetDirty(mat);
            Debug.Log("[OakMaterialFixer] BillboardBigOak01 → URP/Unlit (AlphaClip)");
            return 1;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void ApplyURPLit(Material mat)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[OakMaterialFixer] Cannot find 'Universal Render Pipeline/Lit'. " +
                    "Make sure URP is installed.");
                return;
            }
            mat.shader = shader;

            // Reset render state to opaque defaults
            mat.SetFloat("_Surface",    0f);   // Opaque
            mat.SetFloat("_Blend",      0f);   // Alpha
            mat.SetFloat("_AlphaClip",  0f);
            mat.SetFloat("_Cull",       2f);   // Back-face cull (default)
            mat.renderQueue = -1;              // default
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        private static void SetTex(Material mat, string prop, Texture tex)
        {
            if (tex != null && mat.HasProperty(prop))
                mat.SetTexture(prop, tex);
        }

        private static Material Load(string filename)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ROOT + filename);
            if (mat == null)
                Debug.LogWarning($"[OakMaterialFixer] Could not load: {ROOT + filename}");
            return mat;
        }
    }
}
