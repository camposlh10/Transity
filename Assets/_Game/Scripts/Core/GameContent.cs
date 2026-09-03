using Transity.Creatures;
using Transity.Inventory;
using Transity.Missions;
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
        [SerializeField] CreatureRegistry creatureRegistry;
        [SerializeField] ContractRegistry contractRegistry;

        public static ItemRegistry ItemRegistry => Exists ? Instance.itemRegistry : null;
        public static CreatureRegistry Creatures => Exists ? Instance.creatureRegistry : null;
        public static ContractRegistry Contracts => Exists ? Instance.contractRegistry : null;

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

            if (creatureRegistry == null)
            {
                GameLog.Warn("GameContent has no CreatureRegistry assigned; the forest will be empty.");
            }

            if (contractRegistry == null)
            {
                GameLog.Warn("GameContent has no ContractRegistry assigned; nothing to hunt.");
            }
        }
    }
}
