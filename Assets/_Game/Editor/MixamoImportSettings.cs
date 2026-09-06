using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// Import rules for Mixamo animation clips in Art/Animations/Mixamo.
    ///
    /// Mixamo files arrive as animation only, on a 65-bone <c>mixamorig:</c> skeleton, with
    /// the take named "mixamo.com" in every single file. Left alone they import as Generic,
    /// keep that useless name, and do not retarget onto anything. Three things fix that:
    /// Humanoid rig so the clip retargets, a clip name taken from the file, and the root
    /// motion baked flat.
    ///
    /// Baking the root is not optional here. Movement comes from the CharacterController,
    /// so any translation left on the clip fights it -- and "In Place" downloads still
    /// leave a few centimetres of hip sway that would slowly walk a standing player away
    /// from where the server thinks they are.
    /// </summary>
    public sealed class MixamoImportSettings : AssetPostprocessor
    {
        public const string Folder = "Assets/_Game/Art/Animations/Mixamo";

        /// <summary>
        /// The file whose skeleton defines the avatar every other clip copies. They are all
        /// the same rig, so any would do; naming one keeps Unity from building six
        /// near-identical avatars and lets a mismatch show up as an error rather than as
        /// two clips that subtly disagree.
        /// </summary>
        const string AvatarSource = "Idle";

        static string Normalize(string path) => path.Replace("\\", "/");

        bool IsMixamo => Normalize(assetPath).StartsWith(Folder);

        void OnPreprocessModel()
        {
            if (!IsMixamo || assetImporter is not ModelImporter importer)
            {
                return;
            }

            var stem = Path.GetFileNameWithoutExtension(assetPath);

            importer.animationType = ModelImporterAnimationType.Human;
            importer.importAnimation = true;

            // Animation only: the mesh, materials and skeleton geometry are all dead weight
            // here, because these clips drive characters that already exist in the project.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importBlendShapes = false;
            importer.optimizeGameObjects = false;

            if (stem == AvatarSource)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
            else
            {
                var source = LoadAvatar();
                if (source != null)
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    importer.sourceAvatar = source;
                }
                else
                {
                    // First import, before Idle exists. Each file carries the whole
                    // skeleton, so this is a valid fallback; ReimportAll fixes the order.
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                }
            }

            var clips = importer.defaultClipAnimations;
            if (clips is not { Length: > 0 })
            {
                return;
            }

            foreach (var clip in clips)
            {
                // Every Mixamo take is called "mixamo.com", so without this the project
                // ends up with six clips of the same name and no way to tell them apart.
                clip.name = stem;

                // All six are cycles, the idle included.
                clip.loopTime = true;
                clip.loopPose = true;
                clip.wrapMode = WrapMode.Loop;

                // Bake the root flat. lock* pins the root to the body pose; keepOriginal*
                // measures that pose from the clip's own authored orientation rather than
                // from the first frame, which is what stops a strafe from drifting.
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }

            importer.clipAnimations = clips;
        }

        static Avatar LoadAvatar()
        {
            var path = $"{Folder}/{AvatarSource}.fbx";

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Avatar avatar)
                {
                    return avatar;
                }
            }

            return null;
        }

        /// <summary>
        /// The clip inside a Mixamo file, by the name this importer gave it.
        /// </summary>
        public static AnimationClip LoadClip(string stem)
        {
            var path = $"{Folder}/{stem}.fbx";

            if (!File.Exists(path))
            {
                return null;
            }

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__"))
                {
                    return clip;
                }
            }

            return null;
        }

        public static List<string> AvailableClips()
        {
            var found = new List<string>();

            if (!AssetDatabase.IsValidFolder(Folder))
            {
                return found;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Folder }))
            {
                found.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));
            }

            found.Sort();
            return found;
        }

        /// <summary>
        /// Says which library the controllers will be built from and, when it is still the
        /// fallback, exactly which Mixamo downloads are outstanding.
        /// </summary>
        [MenuItem("Tools/Transity/Animation Status", priority = 49)]
        public static void ReportStatus()
        {
            var mixamo = new MixamoClipSet();
            var have = AvailableClips();

            if (mixamo.IsComplete)
            {
                Debug.Log($"<b>Transity</b>: Mixamo set complete ({have.Count} clips). " +
                          "Controllers build from Mixamo.");
                return;
            }

            Debug.Log($"<b>Transity</b>: {have.Count} Mixamo clip(s) present: {string.Join(", ", have)}\n" +
                      $"Still needed: {mixamo.DescribeMissing()}\n" +
                      (KevinIglesiasClipSet.PackagePresent
                          ? "Using the Kevin Iglesias package until the set is complete."
                          : "No fallback library; characters will not animate."));
        }

        /// <summary>
        /// Reimports the avatar source first, then everything else, so the copied avatars
        /// have something to copy from.
        /// </summary>
        [MenuItem("Tools/Transity/Reimport Mixamo Animations", priority = 48)]
        public static void ReimportAll()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                Debug.LogWarning($"No Mixamo folder at {Folder}.");
                return;
            }

            var source = $"{Folder}/{AvatarSource}.fbx";
            if (File.Exists(source))
            {
                AssetDatabase.ImportAsset(source, ImportAssetOptions.ForceUpdate);
            }
            else
            {
                Debug.LogWarning($"No {AvatarSource}.fbx; each clip will build its own avatar.");
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path != source)
                    {
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();

            var clips = AvailableClips();
            Debug.Log($"<b>Transity</b>: reimported {clips.Count} Mixamo clip(s): {string.Join(", ", clips)}");
        }
    }
}
