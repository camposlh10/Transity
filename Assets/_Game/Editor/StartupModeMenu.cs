using Transity.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// Flips the Boot scene between going through the main menu and dropping straight into
    /// the depot. Edits the scene in place so there is no second copy to keep in sync.
    /// </summary>
    public static class StartupModeMenu
    {
        const string BootScenePath = "Assets/_Game/Scenes/Boot.unity";

        [MenuItem("Tools/Transity/Startup Mode/Skip Menu (Offline Host)", priority = 60)]
        static void UseOfflineHost() => Apply(StartupMode.OfflineHost);

        [MenuItem("Tools/Transity/Startup Mode/Through Main Menu", priority = 61)]
        static void UseMainMenu() => Apply(StartupMode.MainMenu);

        /// <summary>
        /// Straight into a live hunt. The fastest loop for tuning creatures and weapons:
        /// press Play, walk to the supply cache, pick a fight.
        /// </summary>
        [MenuItem("Tools/Transity/Startup Mode/Forest Sandbox (Straight Into A Hunt)", priority = 62)]
        static void UseForestSandbox() => Apply(StartupMode.ForestSandbox);

        static void Apply(StartupMode mode)
        {
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            var openedScene = false;

            if (bootstrap == null)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return;
                }

                EditorSceneManager.OpenScene(BootScenePath);
                bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
                openedScene = true;
            }

            if (bootstrap == null)
            {
                Debug.LogError($"No GameBootstrap found. Build the scaffold first, or open {BootScenePath}.");
                return;
            }

            GrayboxKit.Wire(bootstrap, ("startupMode", (int)mode));
            EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);
            EditorSceneManager.SaveScene(bootstrap.gameObject.scene);

            Debug.Log($"<b>Transity</b>: startup mode set to {mode}." +
                      (openedScene ? " (Boot scene opened to apply it.)" : string.Empty));
        }
    }
}
