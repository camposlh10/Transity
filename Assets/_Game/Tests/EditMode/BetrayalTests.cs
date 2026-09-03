using System.Collections.Generic;
using NUnit.Framework;
using Transity.Missions;
using UnityEngine;

namespace Transity.Tests
{
    /// <summary>
    /// The Collector's offer only works if "without anyone knowing" is a rule players can
    /// reason about. If being seen is unpredictable, betrayal stops being a decision and
    /// becomes a gamble, and the crew has no reason to trust anyone ever again.
    ///
    /// So the witness rules are pinned here: close enough is always seen, behind you is
    /// not, a wall between you is not, and distance eventually saves you.
    /// </summary>
    public sealed class BetrayalTests
    {
        static WitnessCheck.Observer Watcher(Vector3 position, Vector3 forward,
            float fov = 120f, float maxDistance = 60f)
        {
            return new WitnessCheck.Observer
            {
                Position = position,
                Forward = forward,
                FovDegrees = fov,
                MaxDistance = maxDistance
            };
        }

        static bool ClearSight(Vector3 a, Vector3 b) => true;
        static bool BlockedSight(Vector3 a, Vector3 b) => false;

        [Test]
        public void NobodyAroundIsNeverWitnessed()
        {
            Assert.IsFalse(WitnessCheck.IsWitnessed(
                Vector3.zero, new List<WitnessCheck.Observer>(), ClearSight));
        }

        [Test]
        public void SomeoneLookingStraightAtYouIsAWitness()
        {
            var observers = new[] { Watcher(new Vector3(0f, 0f, -20f), Vector3.forward) };
            Assert.IsTrue(WitnessCheck.IsWitnessed(Vector3.zero, observers, ClearSight));
        }

        [Test]
        public void SomeoneFacingAwayIsNot()
        {
            var observers = new[] { Watcher(new Vector3(0f, 0f, -20f), Vector3.back) };
            Assert.IsFalse(WitnessCheck.IsWitnessed(Vector3.zero, observers, ClearSight));
        }

        [Test]
        public void AWallBetweenYouHidesIt()
        {
            var observers = new[] { Watcher(new Vector3(0f, 0f, -20f), Vector3.forward) };
            Assert.IsFalse(WitnessCheck.IsWitnessed(Vector3.zero, observers, BlockedSight));
        }

        [Test]
        public void FarEnoughAwayIsNotAWitness()
        {
            var observers = new[] { Watcher(new Vector3(0f, 0f, -200f), Vector3.forward) };
            Assert.IsFalse(WitnessCheck.IsWitnessed(Vector3.zero, observers, ClearSight));
        }

        [Test]
        public void PointBlankIsSeenEvenFacingAwayThroughAWall()
        {
            // Standing next to someone when they die is not something you get to miss.
            // Without this rule, two players could stand back to back and one could
            // execute the other with total deniability.
            var observers = new[]
            {
                Watcher(new Vector3(0f, 0f, -2f), Vector3.back, fov: 1f, maxDistance: 0.1f)
            };

            Assert.IsTrue(WitnessCheck.IsWitnessed(Vector3.zero, observers, BlockedSight));
        }

        [Test]
        public void PointBlankBoundaryIsInclusive()
        {
            var justInside = new[]
            {
                Watcher(new Vector3(0f, 0f, -WitnessCheck.PointBlankRadius + 0.01f),
                    Vector3.back, fov: 1f, maxDistance: 0.1f)
            };
            var justOutside = new[]
            {
                Watcher(new Vector3(0f, 0f, -WitnessCheck.PointBlankRadius - 0.01f),
                    Vector3.back, fov: 1f, maxDistance: 0.1f)
            };

            Assert.IsTrue(WitnessCheck.IsWitnessed(Vector3.zero, justInside, BlockedSight));
            Assert.IsFalse(WitnessCheck.IsWitnessed(Vector3.zero, justOutside, BlockedSight));
        }

        [Test]
        public void OneWitnessAmongManyIsEnough()
        {
            var observers = new[]
            {
                Watcher(new Vector3(30f, 0f, 0f), Vector3.forward),   // facing away
                Watcher(new Vector3(-30f, 0f, 0f), Vector3.forward),  // facing away
                Watcher(new Vector3(0f, 0f, -20f), Vector3.forward)   // facing you
            };

            Assert.IsTrue(WitnessCheck.IsWitnessed(Vector3.zero, observers, ClearSight));
        }

        [Test]
        public void HeightDoesNotSaveYou()
        {
            // Killing someone from the top of a rock is still killing them in the open.
            var observers = new[] { Watcher(new Vector3(0f, -8f, -20f), Vector3.forward) };
            Assert.IsTrue(WitnessCheck.IsWitnessed(Vector3.zero, observers, ClearSight));
        }

        [Test]
        public void ANarrowFieldOfViewMissesWhatIsOffToTheSide()
        {
            var observers = new[]
            {
                Watcher(new Vector3(-20f, 0f, -20f), Vector3.forward, fov: 30f)
            };

            Assert.IsFalse(WitnessCheck.IsWitnessed(Vector3.zero, observers, ClearSight));
        }

        [Test]
        public void MissingLineOfSightTestDefaultsToBeingSeen()
        {
            // Fail closed. If the server cannot work out whether anyone saw it, the safe
            // answer is that they did -- an undetected betrayal should never be the result
            // of a missing check.
            var observers = new[] { Watcher(new Vector3(0f, 0f, -20f), Vector3.forward) };
            Assert.IsTrue(WitnessCheck.IsWitnessed(Vector3.zero, observers, null));
        }
    }
}
