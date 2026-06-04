using UnityEngine;
using UnityEditor;
using System.IO;
using InfiniteWorld;

namespace InfiniteWorldEditor
{
    public static class DwarfUmbrellaPrefabBuilder
    {
        private const string PREFAB_PATH = "Assets/realistic-bushtree-dwarf-umbrella/DwarfUmbrellaPrefab.prefab";

        public static void BuildPrefab()
        {
            string fbxPath = "Assets/realistic-bushtree-dwarf-umbrella/source/Dwarf Umbrella.fbx";
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null)
            {
                Debug.LogWarning($"[DwarfUmbrellaPrefabBuilder] Could not find FBX at {fbxPath}");
                return;
            }

            Debug.Log("[DwarfUmbrellaPrefabBuilder] Generating Dwarf Umbrella prefab and materials...");

            // Create materials folder
            string matDir = "Assets/realistic-bushtree-dwarf-umbrella/materials";
            string fullMatDir = GetFullPath(matDir);
            if (!Directory.Exists(fullMatDir))
            {
                Directory.CreateDirectory(fullMatDir);
            }

            // Combine albedo and opacity textures to support alpha clipping
            string pbul4_AlbedoAlpha = "Assets/realistic-bushtree-dwarf-umbrella/textures/pbul4_Combined.png";
            CombineAlbedoAndOpacity(
                "Assets/realistic-bushtree-dwarf-umbrella/textures/pbul4_4K_Albedo.jpg",
                "Assets/realistic-bushtree-dwarf-umbrella/textures/pbul4_4K_Opacity.jpg",
                pbul4_AlbedoAlpha
            );

            string sefbdevh_AlbedoAlpha = "Assets/realistic-bushtree-dwarf-umbrella/textures/sefbdevh_Combined.png";
            CombineAlbedoAndOpacity(
                "Assets/realistic-bushtree-dwarf-umbrella/textures/sefbdevh_4K_Albedo.jpg",
                "Assets/realistic-bushtree-dwarf-umbrella/textures/sefbdevh_4K_Opacity.jpg",
                sefbdevh_AlbedoAlpha
            );

            // Create URP Lit materials
            Material leafMat1 = CreateURPLitMaterial(
                pbul4_AlbedoAlpha,
                "Assets/realistic-bushtree-dwarf-umbrella/textures/pbul4_4K_Normal.jpg",
                "Assets/realistic-bushtree-dwarf-umbrella/textures/pbul4_4K_Roughness.jpg",
                matDir + "/LeafMat_pbul4.mat",
                isTransparent: true
            );

            Material leafMat2 = CreateURPLitMaterial(
                sefbdevh_AlbedoAlpha,
                "Assets/realistic-bushtree-dwarf-umbrella/textures/sefbdevh_4K_Normal.jpg",
                "Assets/realistic-bushtree-dwarf-umbrella/textures/sefbdevh_4K_Roughness.jpg",
                matDir + "/LeafMat_sefbdevh.mat",
                isTransparent: true
            );

            Material barkMat = CreateURPLitMaterial(
                "Assets/realistic-bushtree-dwarf-umbrella/textures/xg0wceojw_4K_Albedo.jpg",
                "Assets/realistic-bushtree-dwarf-umbrella/textures/xg0wceojw_4K_Normal.jpg",
                "Assets/realistic-bushtree-dwarf-umbrella/textures/xg0wceojw_4K_Roughness.jpg",
                matDir + "/BarkMat_xg0wceojw.mat",
                isTransparent: false
            );

            // Instantiate FBX, assign materials, save prefab
            GameObject instance = PrefabUtility.InstantiatePrefab(fbx) as GameObject;
            if (instance != null)
            {
                var r = instance.GetComponentInChildren<Renderer>(true);
                if (r != null)
                {
                    int slotCount = r.sharedMaterials.Length;
                    Material[] mats = new Material[slotCount];
                    if (slotCount == 1)
                    {
                        mats[0] = leafMat2;
                    }
                    else
                    {
                        for (int i = 0; i < slotCount; i++)
                        {
                            if (i == 0) mats[i] = barkMat;
                            else if (i == 1) mats[i] = leafMat1;
                            else mats[i] = leafMat2;
                        }
                    }
                    r.sharedMaterials = mats;
                }

                PrefabUtility.SaveAsPrefabAsset(instance, PREFAB_PATH);
                Object.DestroyImmediate(instance);
                Debug.Log($"[DwarfUmbrellaPrefabBuilder] Successfully created bush prefab at {PREFAB_PATH}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void CombineAlbedoAndOpacity(string albedoPath, string opacityPath, string outputPath)
        {
            if (File.Exists(GetFullPath(outputPath))) return; // Already combined

            MakeTextureReadable(albedoPath);
            MakeTextureReadable(opacityPath);

            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            Texture2D opacity = AssetDatabase.LoadAssetAtPath<Texture2D>(opacityPath);

            if (albedo == null || opacity == null)
            {
                Debug.LogWarning($"[DwarfUmbrellaPrefabBuilder] Albedo or Opacity map not found: {albedoPath}, {opacityPath}");
                return;
            }

            int w = albedo.width;
            int h = albedo.height;

            Texture2D combined = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color[] cAlbedo = albedo.GetPixels();
            Color[] cOpacity = opacity.GetPixels();
            Color[] cCombined = new Color[cAlbedo.Length];

            for (int i = 0; i < cAlbedo.Length; i++)
            {
                cCombined[i] = new Color(cAlbedo[i].r, cAlbedo[i].g, cAlbedo[i].b, cOpacity[i].r);
            }

            combined.SetPixels(cCombined);
            combined.Apply();

            byte[] pngBytes = combined.EncodeToPNG();
            File.WriteAllBytes(GetFullPath(outputPath), pngBytes);
            AssetDatabase.ImportAsset(outputPath);
        }

        private static void MakeTextureReadable(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
        }

        private static Material CreateURPLitMaterial(string albedoAlphaPath, string normalPath, string roughnessPath, string matPath, bool isTransparent)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }

            if (albedoAlphaPath != null)
            {
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoAlphaPath);
                mat.SetTexture("_BaseMap", tex);
            }

            if (normalPath != null)
            {
                Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }

            mat.SetFloat("_Smoothness", 0.15f);

            if (isTransparent)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.45f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                mat.SetFloat("_Cull", 0f);
                mat.doubleSidedGI = true;
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static string GetFullPath(string assetPath)
        {
            if (assetPath.StartsWith("Assets/"))
            {
                return Path.Combine(Application.dataPath, assetPath.Substring(7));
            }
            return assetPath;
        }
    }
}
