using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// Import rules for playable characters in Art/Characters.
    ///
    /// Rigs come in as Humanoid so retargeted clips (Mixamo and friends) drop straight on
    /// without hand-mapping every bone. The source skeleton uses Hip/Spine01/L_Thigh naming,
    /// which Unity's auto-mapper handles; check the Rig tab if a limb looks wrong.
    /// </summary>
    public sealed class CharacterImportSettings : AssetPostprocessor
    {
        const string CharacterFolder = "Assets/_Game/Art/Characters";
        const string TextureFolder = "Assets/_Game/Art/Characters/Textures";
        const string MaterialFolder = "Assets/_Game/Art/Materials";

        /// <summary>
        /// Model name, the texture stem it ships with, whether it has a skeleton, and which
        /// half of the Human Basic Motions library it animates from.
        ///
        /// All three are rigged: the two that arrived as bare meshes were bound to a refitted
        /// copy of the first one's skeleton, so they share bone names and retarget alike.
        /// ClipSet only chooses the style of the motion -- both sets retarget onto any
        /// Humanoid rig, so swap a value if a character carries itself wrongly.
        /// </summary>
        public static readonly (string Model, string TextureStem, bool Rigged, string ClipSet)[] Characters =
        {
            ("CH_AdventurerGirl", "adventurer_girl_3d_model", true, "Female"),
            ("CH_AdventurerScout", "adventurer_character_3d_model", true, "Male"),
            ("CH_Mercenary", "stylized_mercenary_3d_model", true, "Male")
        };

        const string AnimationFolder = "Assets/_Game/Animation";

        static (string Model, string TextureStem, bool Rigged, string ClipSet) EntryFor(string path)
        {
            var stem = System.IO.Path.GetFileNameWithoutExtension(path);

            foreach (var entry in Characters)
            {
                if (stem.Equals(entry.Model, System.StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return default;
        }

        bool IsCharacter => assetPath.Replace("\\", "/").StartsWith(CharacterFolder);
        bool IsCharacterTexture => assetPath.Replace("\\", "/").StartsWith(TextureFolder);

        void OnPreprocessTexture()
        {
            if (!IsCharacterTexture || assetImporter is not TextureImporter importer)
            {
                return;
            }

            var lower = assetPath.ToLowerInvariant();

            if (lower.Contains("normal"))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
            }
            else if (lower.Contains("metallicsmoothness"))
            {
                // Packed map: metallic in R, smoothness in A. Must stay linear, and the
                // alpha has to survive import or everything reads fully rough.
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
            }
            else if (lower.Contains("basecolor"))
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
            }
            else
            {
                // The loose metallic/roughness JPEGs are superseded by the packed map; keep
                // them importable but small so they cost nothing.
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.maxTextureSize = 512;
                return;
            }

            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
        }

        void OnPreprocessModel()
        {
            if (!IsCharacter || assetImporter is not ModelImporter importer)
            {
                return;
            }

            importer.globalScale = 1f;
            importer.useFileScale = true;

            // Scaled to 1.8 m on export, so no rescaling here.
            var entry = EntryFor(assetPath);

            if (entry.Rigged)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;

                // Name the take and make it cycle. An idle that does not loop plays once and
                // then freezes, which reads as the character having died standing up.
                var clips = importer.defaultClipAnimations;
                if (clips is { Length: > 0 })
                {
                    foreach (var clip in clips)
                    {
                        clip.name = "Idle";
                        clip.loopTime = true;
                        clip.wrapMode = WrapMode.Loop;
                        clip.lockRootRotation = true;
                        clip.lockRootHeightY = true;
                        clip.keepOriginalPositionY = true;
                    }

                    importer.clipAnimations = clips;
                }
            }
            else
            {
                // No skeleton: a static body that can be worn but never animated.
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
            }

            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.addCollider = false;

            // A skinned character is lit dynamically; it never contributes to a lightmap, so
            // generating a second UV set would be wasted import time and memory.
            importer.generateSecondaryUV = false;

            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.optimizeMeshVertices = true;
            importer.isReadable = false;

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }

        Material OnAssignMaterialModel(Material material, Renderer renderer)
        {
            if (!IsCharacter)
            {
                return null;
            }

            var entry = EntryFor(assetPath);
            return string.IsNullOrEmpty(entry.Model) ? null : EnsureCharacterMaterial(entry);
        }

        /// <summary>Builds every character material.</summary>
        public static void EnsureAllCharacterMaterials()
        {
            foreach (var entry in Characters)
            {
                EnsureCharacterMaterial(entry);
            }
        }

        /// <summary>Builds the shared URP material for one character.</summary>
        public static Material EnsureCharacterMaterial(
            (string Model, string TextureStem, bool Rigged, string ClipSet) entry)
        {
            var path = $"{MaterialFolder}/{entry.Model}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                return existing;
            }

            var material = existing != null ? existing : new Material(shader) { name = entry.Model };

            var baseMap = Load(entry.TextureStem + "_basecolor");
            var normal = Load(entry.TextureStem + "_normal");
            var packed = Load(entry.Model + "_MetallicSmoothness");

            if (baseMap != null)
            {
                material.SetTexture("_BaseMap", baseMap);
            }

            material.SetColor("_BaseColor", Color.white);

            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
                material.SetFloat("_BumpScale", 1f);
            }

            if (packed != null)
            {
                material.SetTexture("_MetallicGlossMap", packed);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.SetFloat("_Metallic", 1f);
                material.SetFloat("_Smoothness", 1f);
                // 0 = metallic alpha, which is where the export packed smoothness.
                material.SetFloat("_SmoothnessTextureChannel", 0f);
            }
            else
            {
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Smoothness", 0.3f);
            }

            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        static Texture Load(string fileStem)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{fileStem} t:Texture", new[] { TextureFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path)
                    .Equals(fileStem, System.StringComparison.OrdinalIgnoreCase))
                {
                    return AssetDatabase.LoadAssetAtPath<Texture>(path);
                }
            }

            return null;
        }

        /// <summary>
        /// Builds a one-state Animator Controller per character, holding its Idle clip.
        ///
        /// One controller each rather than a shared one because the clips live inside their
        /// own FBX files. When there is a real move set this becomes a blend tree and the
        /// controllers can merge.
        /// </summary>
        public static void EnsureAnimatorControllers()
        {
            GrayboxKit.EnsureFolder(AnimationFolder);

            foreach (var entry in Characters)
            {
                if (!entry.Rigged)
                {
                    continue;
                }

                // Preferred: the full locomotion graph built from the animation library.
                // The single-idle controller below is only the fallback for a clone that
                // does not have the package imported.
                if (LocomotionControllerBuilder.PackagePresent)
                {
                    var missing = LocomotionControllerBuilder.MissingClips(entry.ClipSet);
                    if (missing.Count > 0)
                    {
                        Debug.LogWarning($"{entry.Model}: {missing.Count} locomotion clip(s) missing " +
                                         $"({string.Join(", ", missing)}). Building anyway.");
                    }

                    if (LocomotionControllerBuilder.Build(entry.Model, entry.ClipSet) != null)
                    {
                        continue;
                    }
                }

                var modelPath = $"{CharacterFolder}/{entry.Model}.fbx";
                var clip = FindIdleClip(modelPath);

                if (clip == null)
                {
                    Debug.LogWarning($"No Idle clip inside {entry.Model}.fbx; skipping its controller.");
                    continue;
                }

                var controllerPath = $"{AnimationFolder}/{entry.Model}_Controller.controller";
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);

                if (controller == null)
                {
                    controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                }

                var layer = controller.layers[0];
                var machine = layer.stateMachine;

                // Rebuild the single state rather than appending another one each run.
                foreach (var existing in machine.states)
                {
                    machine.RemoveState(existing.state);
                }

                var state = machine.AddState("Idle");
                state.motion = clip;
                machine.defaultState = state;

                EditorUtility.SetDirty(controller);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>The clip is a sub-asset of the model, so it has to be dug out.</summary>
        public static AnimationClip FindIdleClip(string modelPath)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                // Skip the __preview__ clip Unity generates alongside the real one.
                if (asset is AnimationClip clip && !clip.name.StartsWith("__"))
                {
                    return clip;
                }
            }

            return null;
        }

        public static RuntimeAnimatorController ControllerFor(string modelName)
        {
            // The locomotion graph when the animation library is present, the single-idle
            // fallback when it is not.
            var locomotion = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                $"{AnimationFolder}/{modelName}_Locomotion.controller");

            return locomotion != null
                ? locomotion
                : AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    $"{AnimationFolder}/{modelName}_Controller.controller");
        }

        [MenuItem("Tools/Transity/Reimport Characters", priority = 44)]
        public static void ReimportCharacters()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { TextureFolder }))
            {
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid),
                    ImportAssetOptions.ForceUpdate);
            }

            EnsureAllCharacterMaterials();
            AssetDatabase.SaveAssets();

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { CharacterFolder }))
            {
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid),
                    ImportAssetOptions.ForceUpdate);
            }

            // Controllers last: the clips only exist once the models have imported.
            EnsureAnimatorControllers();

            Debug.Log("<b>Transity</b>: characters reimported with idle animations.");
        }
    }
}
