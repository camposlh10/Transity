using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// Removes generated content that ended up loose at the scene root.
    ///
    /// Earlier versions of the builder parented some objects to nothing -- loose pickups and
    /// a couple of lights. Because a rebuild only replaces its known roots, those strays
    /// survived every run and quietly accumulated duplicates. The builder no longer creates
    /// them, so this is a one-time sweep for scenes that already have them.
    ///
    /// Deliberately conservative: it only touches root-level objects whose names match the
    /// generator's own prefixes, never the generated roots themselves, and never anything
    /// under "_Handmade".
    /// </summary>
    public static class DepotCleanup
    {
        static readonly string[] GeneratedPrefixes =
        {
            "ENV_", "PRP_", "FX_", "HD_", "HD2_", "Lamp_", "Spot_", "Anchor_", "WorldItem_"
        };

        [MenuItem("Tools/Transity/Clean Orphaned Depot Objects", priority = 42)]
        public static void CleanTrainHub()
        {
            const string path = "Assets/_Game/Scenes/TrainHub.unity";

            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning($"{path} does not exist yet.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var protectedRoots = new HashSet<string>(DepotBlockout.GeneratedRoots)
            {
                DepotBlockout.HandmadeRoot
            };

            var doomed = new List<GameObject>();

            foreach (var root in scene.GetRootGameObjects())
            {
                if (protectedRoots.Contains(root.name))
                {
                    continue;
                }

                if (GeneratedPrefixes.Any(prefix => root.name.StartsWith(prefix)))
                {
                    doomed.Add(root);
                }
            }

            if (doomed.Count == 0)
            {
                Debug.Log("<b>Transity</b>: no orphaned depot objects found.");
                return;
            }

            foreach (var go in doomed)
            {
                Debug.Log($"<b>Transity</b>: removing orphaned root '{go.name}'.");
                Object.DestroyImmediate(go);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"<b>Transity</b>: removed {doomed.Count} orphaned object(s).");
        }
    }
}
