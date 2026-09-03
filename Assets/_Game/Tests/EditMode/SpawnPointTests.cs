using NUnit.Framework;
using Transity.Player;
using UnityEngine;

namespace Transity.Tests
{
    /// <summary>
    /// Spawn placement decides where four players land after every scene load, so the
    /// cycling and fallback behaviour is worth locking down.
    /// </summary>
    public sealed class SpawnPointTests
    {
        readonly System.Collections.Generic.List<GameObject> m_Created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in m_Created)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            m_Created.Clear();
        }

        [Test]
        public void TryGetPose_FallsBackWhenNoPointsExist()
        {
            Assert.IsFalse(PlayerSpawnPoint.TryGetPose(SpawnContext.Expedition, 0, out var position, out _));
            Assert.AreEqual(Vector3.up, position, "Fallback must be above the floor, not at the origin.");
        }

        [Test]
        public void TryGetPose_ReturnsDistinctPointsForDistinctSlots()
        {
            CreatePoint("TrainSpawn_0", SpawnContext.Train, new Vector3(1f, 0f, 0f));
            CreatePoint("TrainSpawn_1", SpawnContext.Train, new Vector3(2f, 0f, 0f));

            Assert.IsTrue(PlayerSpawnPoint.TryGetPose(SpawnContext.Train, 0, out var first, out _));
            Assert.IsTrue(PlayerSpawnPoint.TryGetPose(SpawnContext.Train, 1, out var second, out _));
            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void TryGetPose_CyclesWhenThereAreMorePlayersThanPoints()
        {
            CreatePoint("TrainSpawn_0", SpawnContext.Train, new Vector3(1f, 0f, 0f));
            CreatePoint("TrainSpawn_1", SpawnContext.Train, new Vector3(2f, 0f, 0f));

            Assert.IsTrue(PlayerSpawnPoint.TryGetPose(SpawnContext.Train, 0, out var first, out _));
            Assert.IsTrue(PlayerSpawnPoint.TryGetPose(SpawnContext.Train, 2, out var third, out _));
            Assert.AreEqual(first, third, "Slot 2 of 2 points should wrap back to the first point.");
        }

        [Test]
        public void TryGetPose_IgnoresOtherContexts()
        {
            CreatePoint("ExpeditionSpawn_0", SpawnContext.Expedition, new Vector3(5f, 0f, 0f));

            Assert.IsFalse(PlayerSpawnPoint.TryGetPose(SpawnContext.Train, 0, out _, out _));
            Assert.IsTrue(PlayerSpawnPoint.TryGetPose(SpawnContext.Expedition, 0, out var position, out _));
            Assert.AreEqual(new Vector3(5f, 0f, 0f), position);
        }

        void CreatePoint(string pointName, SpawnContext context, Vector3 position)
        {
            var go = new GameObject(pointName);
            go.transform.position = position;

            // PlayerSpawnPoint is [ExecuteAlways], so OnEnable runs on AddComponent in
            // edit mode too and the marker registers exactly as it would at runtime.
            var point = go.AddComponent<PlayerSpawnPoint>();
            var so = new UnityEditor.SerializedObject(point);
            so.FindProperty("context").enumValueIndex = (int)context;
            so.ApplyModifiedPropertiesWithoutUndo();

            m_Created.Add(go);
        }
    }
}
