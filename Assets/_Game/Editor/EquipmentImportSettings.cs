using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// Import rules for the Hunter Depot equipment in Art/Equipment.
    ///
    /// Every asset ships one albedo atlas, and the generator baked each part's atlas cell
    /// straight into its UVs. That means one Unity material per asset covers all of its
    /// parts -- the several Blender materials per model exist only to pick the cell, and
    /// carry no information the mesh does not already have.
    ///
    /// The exception is the emissive parts (lenses, indicator lamps, glow sticks). Those
    /// need emission on, and emission is a material property, so each asset that has any
    /// gets a second material. Both sample the same atlas, so the glow lands on the right
    /// pixels without a separate emission texture.
    /// </summary>
    public sealed class EquipmentImportSettings : AssetPostprocessor
    {
        public const string EquipmentFolder = "Assets/_Game/Art/Equipment";
        public const string TextureFolder = EquipmentFolder + "/Textures";
        const string MaterialFolder = "Assets/_Game/Art/Materials/Equipment";

        static string Normalize(string path) => path.Replace("\\", "/");

        bool IsEquipment => Normalize(assetPath).StartsWith(EquipmentFolder);
        bool IsEquipmentTexture => Normalize(assetPath).StartsWith(TextureFolder);

        // ------------------------------------------------------------------- textures

        void OnPreprocessTexture()
        {
            if (!IsEquipmentTexture || assetImporter is not TextureImporter importer)
            {
                return;
            }

            // Albedo atlases: colour data, so sRGB stays on.
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;

            // Held at arm's length or seen across a room -- 1024 is plenty, and it keeps the
            // 27-asset set from costing more VRAM than the whole depot.
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Compressed;
        }

        // --------------------------------------------------------------------- models

        void OnPreprocessModel()
        {
            if (!IsEquipment || assetImporter is not ModelImporter importer)
            {
                return;
            }

            // Exported at 1 Blender unit = 1 metre with the transform applied, so Unity must
            // not apply a second conversion.
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = false;

            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importBlendShapes = false;

            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.weldVertices = true;
            importer.meshCompression = ModelImporterMeshCompression.Medium;

            // Nothing reads this geometry back at runtime, and leaving it off halves the
            // memory each mesh costs.
            importer.isReadable = false;

            // Realtime lighting throughout the depot, so no lightmap UVs are needed.
            importer.generateSecondaryUV = false;

            // Materials are resolved by OnAssignMaterialModel below, which hands back the
            // shared EQ_ material for the asset. (materialLocation is obsolete in Unity 6;
            // remapping through the postprocessor is the supported route.)
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        }

        /// <summary>
        /// Routes each Blender material to the asset's shared material, or to its emissive
        /// companion when the art manifest marked that material as a light source.
        /// </summary>
        Material OnAssignMaterialModel(Material material, Renderer renderer)
        {
            if (!IsEquipment)
            {
                return null;
            }

            var model = Path.GetFileNameWithoutExtension(assetPath);
            var entry = EquipmentCatalog.Find(model);

            if (entry == null)
            {
                return null;
            }

            var emissive = System.Array.IndexOf(entry.EmissiveMaterials, material.name) >= 0;
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(model, emissive));

            // Deliberately only a lookup: creating an asset from inside an import callback
            // can re-enter the importer. ReimportEquipment builds the materials first for
            // exactly this reason, so on the supported path this always finds one. If it
            // does not, Unity extracts its own and the next reimport corrects it.
            return existing;
        }

        // ------------------------------------------------------------------ materials

        static string MaterialPath(string model, bool emissive) =>
            $"{MaterialFolder}/EQ_{model[3..]}{(emissive ? "_Emissive" : string.Empty)}.mat";

        static Material BuildMaterial(EquipmentCatalog.Entry entry, bool emissive)
        {
            GrayboxKit.EnsureFolder(MaterialFolder);

            var path = MaterialPath(entry.Model, emissive);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            var atlas = entry.Atlas == null
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{entry.Atlas}");

            if (atlas != null)
            {
                material.SetTexture("_BaseMap", atlas);
            }

            material.SetColor("_BaseColor", Color.white);

            // Gear is worn field kit, not showroom stock: mostly rough, no metal flakes.
            material.SetFloat("_Smoothness", emissive ? 0.55f : 0.28f);
            material.SetFloat("_Metallic", 0f);

            if (emissive)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

                if (atlas != null)
                {
                    material.SetTexture("_EmissionMap", atlas);
                }

                // Modest: these are indicator lamps and lenses, not floodlights. Above 1 so
                // they still bloom a little in the dark depot.
                material.SetColor("_EmissionColor", Color.white * 1.6f);
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Builds every equipment material up front. The postprocessor covers the normal
        /// import path; this exists so a rebuild can guarantee they are all present without
        /// having to reimport 27 models first.
        /// </summary>
        public static void EnsureEquipmentMaterials()
        {
            GrayboxKit.EnsureFolder(MaterialFolder);

            foreach (var entry in EquipmentCatalog.Entries)
            {
                BuildMaterial(entry, false);

                if (entry.EmissiveMaterials.Length > 0)
                {
                    BuildMaterial(entry, true);
                }
            }

            AssetDatabase.SaveAssets();
        }

        public static GameObject LoadModel(string model) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{EquipmentFolder}/{model}.fbx");

        [MenuItem("Tools/Transity/Reimport Equipment", priority = 45)]
        public static void ReimportEquipment()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }))
                {
                    AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid),
                        ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // Materials before models, so the postprocessor has something to hand back
            // instead of letting Unity author its own.
            EnsureEquipmentMaterials();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var entry in EquipmentCatalog.Entries)
                {
                    var path = $"{EquipmentFolder}/{entry.Model}.fbx";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                    {
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    }
                    else
                    {
                        Debug.LogWarning($"Equipment model missing: {path}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"<b>Transity</b>: reimported {EquipmentCatalog.Entries.Count} equipment models.");
        }

        /// <summary>Reports models named in the catalogue that are not on disk.</summary>
        public static List<string> MissingModels()
        {
            var missing = new List<string>();

            foreach (var entry in EquipmentCatalog.Entries)
            {
                if (LoadModel(entry.Model) == null)
                {
                    missing.Add(entry.Model);
                }
            }

            return missing;
        }
    }
}
