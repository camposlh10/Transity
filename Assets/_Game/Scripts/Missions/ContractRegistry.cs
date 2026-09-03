using System.Collections.Generic;
using UnityEngine;

namespace Transity.Missions
{
    /// <summary>What the mission computer lists, in order.</summary>
    [CreateAssetMenu(menuName = "Transity/Contract Registry", fileName = "ContractRegistry")]
    public sealed class ContractRegistry : ScriptableObject
    {
        [SerializeField] List<ContractDefinition> contracts = new();

        public IReadOnlyList<ContractDefinition> Contracts => contracts;
        public int Count => contracts.Count;

        public ContractDefinition Get(int index) =>
            index >= 0 && index < contracts.Count ? contracts[index] : null;

        public int IndexOf(ContractDefinition contract) => contracts.IndexOf(contract);
    }
}
