using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// Import rules for the depot kit in Art/Models and its texture sheets in Art/Textures.
    ///
    /// The kit ships UV'd meshes plus eight 2K albedo atlases. Materials are rebuilt here as
    /// shared URP assets rather than letting the FBX importer generate one copy per file, so
    /// the whole room draws from a single small set. Which atlas a material uses is derived
    /// from its name prefix, so a new HD_TEX_FURN_* material picks up the furniture atlas
    /// with no code change.
    /// </summary>
    public sealed class KitImportSettings : AssetPostprocessor
    {
        const string KitFolder = "Assets/_Game/Art/Models";
        const string TextureFolder = "Assets/_Game/Art/Textures";
        const string MaterialFolder = "Assets/_Game/Art/Materials";

        /// <summary>Material name prefix to atlas. Longest match wins.</summary>
        static readonly (string Prefix, string Texture)[] AtlasByPrefix =
        {
            ("HD_TEX_ARCH_", "HD_ArchitectureTrim_Albedo"),
            ("HD_TEX_FURN_", "HD_FurnitureAtlas_Albedo"),
            ("HD_TEX_GEAR_", "HD_GearAtlas_Albedo"),
            ("HD_TEX_TROPHY_", "HD_TrophyAtlas_Albedo"),
            ("HD_TEX_TRAIN_", "HD_TrainTrim_Albedo"),
            ("HD_TEX_CONCRETE", "HD_Concrete_Albedo"),
            ("HD_TEX_STONE", "HD_Stone_Albedo"),
            ("HD_TEX_RUG", "HD_Rug_Albedo")
        };

        /// <summary>
        /// Surface response by keyword. The atlases carry colour only, so metallic and
        /// smoothness are inferred rather than authored -- a flat 0/0.5 for everything would
        /// make steel and canvas read identically under the depot's baked lighting.
        /// </summary>
        static readonly (string Keyword, float Metallic, float Smoothness)[] SurfaceByKeyword =
        {
            ("GLASS", 0.05f, 0.90f),
            ("RUBBER", 0f, 0.18f),
            ("BRASS", 0.85f, 0.60f),
            ("PAPER", 0f, 0.08f),
            ("RIVETED", 0.70f, 0.42f),
            ("UNDERFRAME", 0.70f, 0.38f),
            ("WEAPON_METAL", 0.75f, 0.52f),
            ("STEEL_LIGHT", 0.55f, 0.50f),
            ("STEEL", 0.72f, 0.45f),
            ("TRAIN_ROOF", 0.25f, 0.45f),
            ("TRAIN_BLUE", 0.20f, 0.52f),
            ("TRAIN_RED", 0.20f, 0.52f),
            ("CONCRETE", 0f, 0.10f),
            ("STONE", 0f, 0.12f),
            ("RUG", 0f, 0.10f),
            ("CANVAS", 0f, 0.14f),
            ("FABRIC", 0f, 0.16f),
            ("LEATHER", 0f, 0.28f),
            ("OLIVE", 0f, 0.22f),
            ("HONEY", 0f, 0.26f),
            ("WALNUT", 0f, 0.24f),
            ("CREAM", 0f, 0.24f),
            ("BLACK", 0.15f, 0.30f),
            ("IVORY", 0f, 0.26f),
            ("BONE", 0f, 0.24f),
            ("HORN", 0f, 0.30f)
        };

        /// <summary>
        /// Materials with no texture: the two left over from batch one, plus all of batch
        /// two. The non-zero emission values are the ones that should read as light sources
        /// -- bulbs, frosted glass and screens. Set an entry to 0 to make it inert.
        /// </summary>
        static readonly Dictionary<string, (Color Base, float Emission)> FlatMaterials = new()
        {
            ["HD_MAT_CREAM"] = (new Color(0.78f, 0.62f, 0.34f), 0f),
            // An unlit fire reads as orange plastic, so it gets emission too.
            ["HD_MAT_FIRE"] = (new Color(1f, 0.22f, 0.025f), 3.5f),

            ["HD2_MAT_BLACK_RUBBER"] = (new Color(0.01f, 0.01f, 0.02f), 0f),
            ["HD2_MAT_BRASS"] = (new Color(0.48f, 0.25f, 0.05f), 0f),
            ["HD2_MAT_MAP_PAPER"] = (new Color(0.70f, 0.56f, 0.34f), 0f),
            ["HD2_MAT_MAP_PAPER_LIGHT"] = (new Color(0.84f, 0.70f, 0.45f), 0f),
            ["HD2_MAT_MAP_WATER"] = (new Color(0.08f, 0.25f, 0.30f), 0f),
            ["HD2_MAT_OLIVE_CANVAS"] = (new Color(0.17f, 0.22f, 0.09f), 0f),
            ["HD2_MAT_OLIVE_DARK"] = (new Color(0.06f, 0.09f, 0.04f), 0f),
            ["HD2_MAT_OLIVE_PAINTED"] = (new Color(0.13f, 0.19f, 0.09f), 0f),
            ["HD2_MAT_PAPER_TAPE"] = (new Color(0.62f, 0.43f, 0.20f), 0f),
            ["HD2_MAT_ROUTE_RUST_RED"] = (new Color(0.42f, 0.05f, 0.03f), 0f),
            ["HD2_MAT_SCREEN_GLASS"] = (new Color(0.02f, 0.05f, 0.06f), 0f),
            ["HD2_MAT_SCREEN_GLOW"] = (new Color(0.15f, 0.36f, 0.31f), 2.6f),
            ["HD2_MAT_SCREEN_WARM"] = (new Color(0.65f, 0.38f, 0.08f), 2.2f),
            ["HD2_MAT_SMOKY_GLASS"] = (new Color(0.08f, 0.17f, 0.22f), 0f),
            ["HD2_MAT_STEEL_CHARCOAL"] = (new Color(0.04f, 0.04f, 0.05f), 0f),
            ["HD2_MAT_STEEL_LIGHT"] = (new Color(0.13f, 0.15f, 0.16f), 0f),
            ["HD2_MAT_TAN_LEATHER"] = (new Color(0.34f, 0.11f, 0.04f), 0f),
            // The bulb itself, so a fixture reads as on even before its light reaches you.
            ["HD2_MAT_WARM_BULB"] = (new Color(0.95f, 0.55f, 0.15f), 6f),
            ["HD2_MAT_WARM_FROSTED_GLASS"] = (new Color(0.76f, 0.48f, 0.18f), 3f),
            ["HD2_MAT_WOOD_HONEY"] = (new Color(0.52f, 0.22f, 0.05f), 0f),
            ["HD2_MAT_WOOD_WALNUT"] = (new Color(0.25f, 0.09f, 0.03f), 0f)
        };

        bool IsKitModel => assetPath.Replace("\\", "/").StartsWith(KitFolder);
        bool IsKitTexture => assetPath.Replace("\\", "/").StartsWith(TextureFolder);

        // ------------------------------------------------------------------- textures

        void OnPreprocessTexture()
        {
            if (!IsKitTexture || assetImporter is not TextureImporter importer)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            // Albedo is colour data; the decal sheet is the only one carrying alpha.
            importer.sRGBTexture = true;
            importer.alphaSource = assetPath.Contains("Decals")
                ? TextureImporterAlphaSource.FromInput
                : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = assetPath.Contains("Decals");
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
        }

        // --------------------------------------------------------------------- models

        void OnPreprocessModel()
        {
            if (!IsKitModel || assetImporter is not ModelImporter importer)
            {
                return;
            }

            importer.globalScale = 1f;
            importer.useFileScale = true;

            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.animationType = ModelImporterAnimationType.None;

            // Box colliders are added when a piece is placed; a mesh collider per kit part
            // would be wasted physics.
            importer.addCollider = false;

            // The kit now ships its own UVs, so keep them and generate only the secondary
            // set that lightmapping needs.
            importer.generateSecondaryUV = true;
            importer.secondaryUVMarginMethod = ModelImporterSecondaryUVMarginMethod.Calculate;

            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.optimizeMeshVertices = true;
            importer.isReadable = false;

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }

        /// <summary>
        /// Look-up only. Creating assets during a model import is unreliable, so the palette
        /// is materialised up front by <see cref="EnsurePaletteMaterials"/>.
        /// </summary>
        Material OnAssignMaterialModel(Material material, Renderer renderer)
        {
            return IsKitModel
                ? AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{material.name}.mat")
                : null;
        }

        // ------------------------------------------------------------------ materials

        /// <summary>
        /// Builds a shared URP material for every material name found across the kit FBXs.
        /// Safe to re-run; existing materials are updated in place rather than duplicated.
        /// </summary>
        public static void EnsurePaletteMaterials()
        {
            EnsureFolder(MaterialFolder);

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("URP Lit shader not found; kit materials cannot be built.");
                return;
            }

            foreach (var materialName in CollectMaterialNames())
            {
                BuildMaterial(materialName, shader);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>Reads material names straight out of the imported FBXs.</summary>
        static IEnumerable<string> CollectMaterialNames()
        {
            var names = new SortedSet<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { KitFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is Material material)
                    {
                        names.Add(material.name);
                    }
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material != null)
                        {
                            names.Add(material.name);
                        }
                    }
                }
            }

            return names;
        }

        static void BuildMaterial(string materialName, Shader shader)
        {
            var path = $"{MaterialFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var isNew = material == null;

            if (isNew)
            {
                material = new Material(shader) { name = materialName };
            }

            if (FlatMaterials.TryGetValue(materialName, out var flat))
            {
                material.SetTexture("_BaseMap", null);
                material.SetColor("_BaseColor", flat.Base);
                var (flatMetallic, flatSmoothness) = SurfaceFor(materialName);
                material.SetFloat("_Metallic", flatMetallic);
                material.SetFloat("_Smoothness", flatSmoothness);

                if (flat.Emission > 0f)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", flat.Base * flat.Emission);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }
            else
            {
                var texture = FindAtlas(materialName);
                if (texture != null)
                {
                    material.SetTexture("_BaseMap", texture);
                    // White, so the atlas shows its authored colour rather than being tinted.
                    material.SetColor("_BaseColor", Color.white);
                }

                var (metallic, smoothness) = SurfaceFor(materialName);
                material.SetFloat("_Metallic", metallic);
                material.SetFloat("_Smoothness", smoothness);
            }

            if (isNew)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }
        }

        static Texture FindAtlas(string materialName)
        {
            var best = string.Empty;
            var textureName = string.Empty;

            foreach (var (prefix, texture) in AtlasByPrefix)
            {
                if (materialName.StartsWith(prefix) && prefix.Length > best.Length)
                {
                    best = prefix;
                    textureName = texture;
                }
            }

            return string.IsNullOrEmpty(textureName)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture>($"{TextureFolder}/{textureName}.png");
        }

        static (float Metallic, float Smoothness) SurfaceFor(string materialName)
        {
            foreach (var (keyword, metallic, smoothness) in SurfaceByKeyword)
            {
                if (materialName.Contains(keyword))
                {
                    return (metallic, smoothness);
                }
            }

            return (0f, 0.3f);
        }

        static void ImportAllModels()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { KitFolder }))
            {
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid),
                    ImportAssetOptions.ForceUpdate);
            }
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        [MenuItem("Tools/Transity/Reimport Depot Kit", priority = 40)]
        public static void ReimportKit()
        {
            // Textures first so materials have something to point at.
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }))
            {
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid),
                    ImportAssetOptions.ForceUpdate);
            }

            // Two model passes on purpose. The material names live inside the FBXs, so the
            // first pass is what makes them discoverable; the palette is then built, and the
            // second pass rebinds every slot to the shared assets.
            ImportAllModels();
            EnsurePaletteMaterials();
            ImportAllModels();

            Debug.Log("<b>Transity</b>: depot kit reimported with textures.");
        }
    }
}
