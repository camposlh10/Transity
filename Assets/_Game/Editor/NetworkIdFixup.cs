using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Transity.EditorTools
{
    /// <summary>
    /// Repairs GlobalObjectIdHash on NetworkObjects created by script.
    ///
    /// NetworkObject computes that hash in its own OnValidate, from
    /// GlobalObjectId.GetGlobalObjectIdSlow. That call only returns a real identifier once
    /// the object is persisted -- saved into a scene, or existing as a prefab asset. A
    /// NetworkObject added by an editor script and saved in the same pass therefore ends up
    /// with hash 0 (in-scene) or a shared junk hash (prefabs), and NGO silently refuses to
    /// spawn it. In-scene objects then throw "can only be created from spawned
    /// NetworkObjects" the moment anything references them.
    ///
    /// The fix is ordering: persist first, then re-run the validation that generates the
    /// hash, then persist again. OnValidate is internal, so it is invoked by reflection.
    /// </summary>
    public static class NetworkIdFixup
    {
        static readonly MethodInfo OnValidateMethod = typeof(NetworkObject)
            .GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo HashField = typeof(NetworkObject)
            .GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic);

        static void Regenerate(NetworkObject networkObject)
        {
            if (networkObject == null || OnValidateMethod == null)
            {
                return;
            }

            OnValidateMethod.Invoke(networkObject, null);
            EditorUtility.SetDirty(networkObject);
        }

        public static uint GetHash(NetworkObject networkObject)
        {
            return HashField != null && networkObject != null
                ? (uint)HashField.GetValue(networkObject)
                : 0u;
        }

        /// <summary>
        /// Re-validates every NetworkObject in a saved scene. Call after the first save,
        /// then save the scene again.
        /// </summary>
        public static int RefreshScene(Scene scene)
        {
            var count = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var networkObject in root.GetComponentsInChildren<NetworkObject>(true))
                {
                    Regenerate(networkObject);
                    count++;
                }
            }

            return count;
        }

        /// <summary>Re-validates a prefab asset that already exists on disk.</summary>
        public static void RefreshPrefab(GameObject prefabAsset)
        {
            if (prefabAsset == null)
            {
                return;
            }

            foreach (var networkObject in prefabAsset.GetComponentsInChildren<NetworkObject>(true))
            {
                Regenerate(networkObject);
            }

            EditorUtility.SetDirty(prefabAsset);
        }

        [MenuItem("Tools/Transity/Validate Network Ids", priority = 41)]
        public static void Validate()
        {
            var problems = 0;
            var seen = new System.Collections.Generic.Dictionary<uint, string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game/Prefabs" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                {
                    continue;
                }

                foreach (var networkObject in asset.GetComponentsInChildren<NetworkObject>(true))
                {
                    var hash = GetHash(networkObject);

                    if (hash == 0)
                    {
                        Debug.LogError($"NetworkObject on '{path}' has GlobalObjectIdHash 0; it will never spawn.");
                        problems++;
                    }
                    else if (seen.TryGetValue(hash, out var other))
                    {
                        Debug.LogError($"GlobalObjectIdHash collision {hash}: '{path}' and '{other}'.");
                        problems++;
                    }
                    else
                    {
                        seen[hash] = path;
                    }
                }
            }

            foreach (var scenePath in new[]
                     {
                         "Assets/_Game/Scenes/TrainHub.unity",
                         "Assets/_Game/Scenes/Forest.unity",
                         "Assets/_Game/Scenes/Boot.unity"
                     })
            {
                var text = System.IO.File.Exists(scenePath) ? System.IO.File.ReadAllText(scenePath) : null;
                if (text == null)
                {
                    continue;
                }

                // Anchored to the line start on purpose: an unanchored match also hits
                // InScenePlacedSourceGlobalObjectIdHash, which is legitimately 0 for in-scene
                // objects that did not come from a network prefab.
                var zeros = System.Text.RegularExpressions.Regex
                    .Matches(text, @"^\s*GlobalObjectIdHash: 0\s*$",
                        System.Text.RegularExpressions.RegexOptions.Multiline).Count;

                if (zeros > 0)
                {
                    Debug.LogError($"{scenePath} has {zeros} in-scene NetworkObject(s) with hash 0; " +
                                   "they will never spawn. Rebuild the scaffold.");
                    problems += zeros;
                }
            }

            if (problems == 0)
            {
                Debug.Log("<b>Transity</b>: network ids are valid.");
            }
        }
    }
}
