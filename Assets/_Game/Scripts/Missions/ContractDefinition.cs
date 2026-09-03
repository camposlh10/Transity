using Transity.Creatures;
using UnityEngine;

namespace Transity.Missions
{
    public enum ContractKind : byte
    {
        /// <summary>On foot: find, then kill or capture.</summary>
        Bounty = 0,

        /// <summary>Reserved: the train parks in the forest and has to be held. Not built yet.</summary>
        TrainDefence = 1
    }

    /// <summary>
    /// One job on the mission computer. Says what is out there and what it pays; the
    /// creature definitions carry the per-head bounties so a contract is mostly a cast list.
    /// </summary>
    [CreateAssetMenu(menuName = "Transity/Contract", fileName = "Contract_New")]
    public sealed class ContractDefinition : ScriptableObject
    {
        [Header("Listing")]
        public string id = "contract.new";
        public string title = "Untitled contract";
        [TextArea(3, 6)] public string briefing;
        public ContractKind kind = ContractKind.Bounty;
        [Range(1, 5)] public int tier = 1;

        [Header("Cast")]
        public CreatureDefinition creature;
        [Range(1, 8)] public int count = 1;
        public bool spawnAsPack;
        public CreatureDefinition secondaryCreature;
        [Range(0, 8)] public int secondaryCount;
        public bool secondaryAsPack = true;

        [Header("Pay")]
        [Tooltip("Multiplies every creature bounty on this contract.")]
        public float rewardMultiplier = 1f;
        [Tooltip("Paid on top when every creature is dealt with before extraction.")]
        public int completionBonus = 200;

        [Header("The Collector")]
        [Tooltip("Chance that one hunter receives a private offer during this contract.")]
        [Range(0f, 1f)] public float betrayalChance = 0.35f;
        public int betrayalBonus = 900;

        public int StableId => Inventory.ItemDefinition.StableHash(id);

        public string Objective
        {
            get
            {
                var main = creature != null ? $"{count}x {creature.displayName}" : "nothing";
                var extra = secondaryCreature != null && secondaryCount > 0
                    ? $" and {secondaryCount}x {secondaryCreature.displayName}"
                    : string.Empty;
                return $"Kill or capture {main}{extra}, then board the train.";
            }
        }
    }
}
