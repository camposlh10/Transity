using System.Collections.Generic;
using Transity.Creatures;
using Transity.Missions;
using UnityEditor;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>The jobs on the mission computer's board.</summary>
    public static class ContractBuilder
    {
        const string Folder = "Assets/_Game/Data/Contracts";

        public static ContractRegistry Build(Dictionary<string, CreatureDefinition> creatures)
        {
            GrayboxKit.EnsureFolder(Folder);

            creatures.TryGetValue("mossback", out var mossback);
            creatures.TryGetValue("stalker", out var stalker);
            creatures.TryGetValue("hound", out var hound);

            // Easiest first, on purpose. The board is read top down and MissionDirector
            // defaults to index 0, so whatever sits here is what a crew that just presses
            // depart will face. It is also the only contract the Collector never visits:
            // a betrayal offer lands very differently on people who have not yet worked
            // out how the hunt itself is meant to go.
            var contracts = new List<ContractDefinition>
            {
                Define("Contract_CullThePack", c =>
                {
                    c.id = "contract.hounds";
                    c.title = "Cull the Pack";
                    c.briefing = "Bramble hounds have been running the north ridge in numbers. Thin them out. They break when you make noise -- and they come back.";
                    c.tier = 1;
                    c.creature = hound; c.count = 5; c.spawnAsPack = true;
                    c.rewardMultiplier = 1f; c.completionBonus = 150;
                    c.betrayalChance = 0f; c.betrayalBonus = 600;
                }),
                Define("Contract_MossbackBounty", c =>
                {
                    c.id = "contract.mossback";
                    c.title = "Mossback Bounty";
                    c.briefing = "Something has been dragging cattle into the treeline west of the depot. Old plate scars on the fence posts. Bring it in, or bring proof.";
                    c.tier = 2;
                    c.creature = mossback; c.count = 1; c.spawnAsPack = false;
                    c.secondaryCreature = hound; c.secondaryCount = 3; c.secondaryAsPack = true;
                    c.rewardMultiplier = 1f; c.completionBonus = 250;
                    c.betrayalChance = 0.35f; c.betrayalBonus = 900;
                }),
                Define("Contract_SomethingInTheTrees", c =>
                {
                    c.id = "contract.stalker";
                    c.title = "Something in the Trees";
                    c.briefing = "Two survey teams came back short a man each. Neither saw what took him. Keep your lights up and your backs to something solid.";
                    c.tier = 2;
                    c.creature = stalker; c.count = 1; c.spawnAsPack = false;
                    c.rewardMultiplier = 1.1f; c.completionBonus = 200;
                    c.betrayalChance = 0.45f; c.betrayalBonus = 800;
                }),
                Define("Contract_TwinTerritories", c =>
                {
                    c.id = "contract.twin";
                    c.title = "Twin Territories";
                    c.briefing = "Two Mossbacks have split the valley between them, and something taller has been walking the line. Nobody has come back from this one with everything they took in.";
                    c.tier = 4;
                    c.creature = mossback; c.count = 2; c.spawnAsPack = false;
                    c.secondaryCreature = stalker; c.secondaryCount = 1; c.secondaryAsPack = false;
                    c.rewardMultiplier = 1.35f; c.completionBonus = 600;
                    c.betrayalChance = 0.5f; c.betrayalBonus = 1400;
                })
            };

            var path = $"{Folder}/ContractRegistry.asset";
            var registry = AssetDatabase.LoadAssetAtPath<ContractRegistry>(path);
            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<ContractRegistry>();
                AssetDatabase.CreateAsset(registry, path);
            }

            var so = new SerializedObject(registry);
            var list = so.FindProperty("contracts");
            list.arraySize = contracts.Count;
            for (var i = 0; i < contracts.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = contracts[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            return registry;
        }

        static ContractDefinition Define(string assetName, System.Action<ContractDefinition> configure)
        {
            var path = $"{Folder}/{assetName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<ContractDefinition>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<ContractDefinition>();
                AssetDatabase.CreateAsset(asset, path);
            }

            asset.secondaryCreature = null;
            asset.secondaryCount = 0;
            configure(asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }
    }
}
