using Transity.Inventory;
using UnityEngine;

namespace Transity.Core
{
    /// <summary>
    /// Holds the content registries that runtime systems need to reach without a scene
    /// reference. Lives on the Boot scene's persistent object.
    /// </summary>
    public sealed class GameContent : PersistentSingleton<GameContent>
    {
        [SerializeField] ItemRegistry itemRegistry;

        public static ItemRegistry ItemRegistry => Exists ? Instance.itemRegistry : null;

        protected override void Awake()
        {
            base.Awake();

            if (Instance != this)
            {
                return;
            }

            if (itemRegistry == null)
            {
                GameLog.Warn("GameContent has no ItemRegistry assigned; dropping items will fail.");
            }
            else
            {
                itemRegistry.Rebuild();
            }
        }
    }
}
