using NUnit.Framework;
using Transity.Combat;
using Transity.Creatures;
using Transity.Missions;
using UnityEngine;

namespace Transity.Tests
{
    /// <summary>
    /// The arithmetic behind reloading, being noticed and getting paid.
    ///
    /// These three are pure on purpose. Perception in particular decides whether a creature
    /// is fair -- a hearing curve that reaches too far, or a sight cone with a hole in it,
    /// reads to a player as the AI cheating, and neither is visible by watching one chase.
    /// </summary>
    public sealed class CombatMathTests
    {
        // ------------------------------------------------------------------- reloading

        [Test]
        public void Reload_FillsMagazineFromReserve()
        {
            var magazine = 2;
            var reserve = 30;

            var moved = AmmoMath.Reload(ref magazine, 8, ref reserve);

            Assert.AreEqual(6, moved);
            Assert.AreEqual(8, magazine);
            Assert.AreEqual(24, reserve);
        }

        [Test]
        public void Reload_TakesOnlyWhatTheReserveHas()
        {
            var magazine = 0;
            var reserve = 3;

            var moved = AmmoMath.Reload(ref magazine, 30, ref reserve);

            Assert.AreEqual(3, moved);
            Assert.AreEqual(3, magazine);
            Assert.AreEqual(0, reserve);
        }

        [Test]
        public void Reload_OnAFullMagazineIsANoOp()
        {
            var magazine = 8;
            var reserve = 20;

            Assert.AreEqual(0, AmmoMath.Reload(ref magazine, 8, ref reserve));
            Assert.AreEqual(8, magazine);
            Assert.AreEqual(20, reserve);
        }

        [Test]
        public void Reload_NeverInventsRoundsFromAnEmptyReserve()
        {
            var magazine = 1;
            var reserve = 0;

            Assert.AreEqual(0, AmmoMath.Reload(ref magazine, 8, ref reserve));
            Assert.AreEqual(1, magazine);
            Assert.AreEqual(0, reserve);
        }

        [Test]
        public void Scatter_IsDeterministicForAGivenSeed()
        {
            // The server re-rolls a client's shot to validate it, so the same seed has to
            // give the same cone -- otherwise every shotgun blast disagrees across the wire.
            var a = AmmoMath.Scatter(Vector3.forward, 6f, new System.Random(1234));
            var b = AmmoMath.Scatter(Vector3.forward, 6f, new System.Random(1234));

            Assert.AreEqual(a.x, b.x, 1e-6f);
            Assert.AreEqual(a.y, b.y, 1e-6f);
            Assert.AreEqual(a.z, b.z, 1e-6f);
        }

        [Test]
        public void Scatter_StaysInsideItsCone()
        {
            var random = new System.Random(7);

            for (var i = 0; i < 200; i++)
            {
                var direction = AmmoMath.Scatter(Vector3.forward, 5f, random);

                // Yaw and pitch are each up to the half-angle, so the worst case is the
                // diagonal of the two rather than the half-angle itself.
                Assert.LessOrEqual(Vector3.Angle(Vector3.forward, direction), 5f * 1.5f,
                    "Spread escaped its cone; a rifle would sometimes shoot sideways.");
            }
        }

        [Test]
        public void Scatter_WithNoSpreadReturnsTheAimExactly()
        {
            var aim = new Vector3(0.3f, 0.1f, 0.9f).normalized;
            Assert.AreEqual(aim, AmmoMath.Scatter(aim, 0f, new System.Random(1)));
        }

        // ------------------------------------------------------------------ perception

        [Test]
        public void Sight_IsBlindWithoutLineOfSight()
        {
            Assert.AreEqual(0f, Perception.SightScore(5f, 30f, 0f, 130f, lineOfSight: false, visibility: 1f));
        }

        [Test]
        public void Sight_IsBlindOutsideTheCone()
        {
            // Directly behind: 180 degrees off a 130 degree cone.
            Assert.AreEqual(0f, Perception.SightScore(5f, 30f, 180f, 130f, true, 1f));
        }

        [Test]
        public void Sight_IsBlindBeyondRange()
        {
            Assert.AreEqual(0f, Perception.SightScore(40f, 30f, 0f, 130f, true, 1f));
        }

        [Test]
        public void Sight_FallsOffWithDistance()
        {
            var near = Perception.SightScore(10f, 30f, 0f, 130f, true, 1f);
            var far = Perception.SightScore(25f, 30f, 0f, 130f, true, 1f);

            Assert.Greater(near, far, "Standing further away has to be safer, or hiding is pointless.");
        }

        [Test]
        public void Sight_IsWorseAtTheEdgeOfTheEye()
        {
            var centre = Perception.SightScore(15f, 30f, 0f, 130f, true, 1f);
            var rim = Perception.SightScore(15f, 30f, 62f, 130f, true, 1f);

            Assert.Greater(centre, rim, "Peripheral vision should be worse than looking straight at you.");
        }

        [Test]
        public void Sight_LowVisibilityShortensTheEffectiveRange()
        {
            // Crouching in the dark at 20 m: inside the raw 30 m range, outside the
            // range that low visibility leaves.
            Assert.AreEqual(0f, Perception.SightScore(20f, 30f, 0f, 130f, true, visibility: 0.5f));
            Assert.Greater(Perception.SightScore(20f, 30f, 0f, 130f, true, visibility: 1f), 0f);
        }

        [Test]
        public void Sight_CloseUpIsObviousEvenInTheDark()
        {
            // A torch-off player pressed against a creature is still seen.
            Assert.Greater(Perception.SightScore(1.5f, 30f, 0f, 130f, true, 0.4f), 0f);
        }

        [Test]
        public void Sight_IsNeverNegativeAndNeverUnbounded()
        {
            foreach (var distance in new[] { 0f, 1f, 8f, 29f })
            {
                foreach (var visibility in new[] { 0.3f, 1f, 1.6f })
                {
                    var score = Perception.SightScore(distance, 30f, 10f, 130f, true, visibility);
                    Assert.GreaterOrEqual(score, 0f);
                    Assert.LessOrEqual(score, 2f);
                }
            }
        }

        [Test]
        public void Hearing_IsLoudestAtTheSourceAndSilentAtTheEdge()
        {
            Assert.AreEqual(1f, Perception.HearingScore(0f, 20f, 1f), 1e-5f);
            Assert.AreEqual(0f, Perception.HearingScore(20f, 20f, 1f));
            Assert.AreEqual(0f, Perception.HearingScore(21f, 20f, 1f));
        }

        [Test]
        public void Hearing_ScalesWithTheCreaturesEar()
        {
            // A sharp-eared creature hears a noise a deaf one misses entirely.
            Assert.Greater(Perception.HearingScore(25f, 20f, 2f), 0f);
            Assert.AreEqual(0f, Perception.HearingScore(25f, 20f, 1f));
        }

        [Test]
        public void Cone_AcceptsAheadAndRejectsBehind()
        {
            Assert.IsTrue(Perception.IsInsideCone(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 5f), 90f));
            Assert.IsFalse(Perception.IsInsideCone(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, -5f), 90f));
        }

        [Test]
        public void Cone_IgnoresHeight()
        {
            // A creature below a player on a rock is still "in front of" them.
            Assert.IsTrue(Perception.IsInsideCone(
                Vector3.zero, Vector3.forward, new Vector3(0f, -9f, 5f), 60f));
        }

        [Test]
        public void Awareness_TakesTheStatedTimeToNoticeAClearTarget()
        {
            // A score of 1 over a 2 second notice time should be exactly half per second.
            Assert.AreEqual(0.5f, Perception.AwarenessGain(1f, 2f), 1e-5f);
        }

        [Test]
        public void Awareness_SaturatesSoCertaintyCannotSpike()
        {
            var clear = Perception.AwarenessGain(1.5f, 2f);
            var overwhelming = Perception.AwarenessGain(8f, 2f);

            Assert.AreEqual(clear, overwhelming, 1e-5f,
                "An enormous sight score must not let a creature notice instantly.");
        }

        [Test]
        public void Awareness_DoesNotGrowWithoutSight()
        {
            Assert.AreEqual(0f, Perception.AwarenessGain(0f, 2f));
        }

        // ---------------------------------------------------------------------- payout

        [Test]
        public void Share_SplitsEvenlyBetweenSurvivors()
        {
            Assert.AreEqual(250, Payout.Share(1000, 4, 1f));
        }

        [Test]
        public void Share_IsLargerWhenFewerComeHome()
        {
            // The uncomfortable arithmetic the whole betrayal mechanic rests on.
            Assert.Greater(Payout.Share(1000, 2, 1f), Payout.Share(1000, 4, 1f));
        }

        [Test]
        public void Share_AppliesTheBodyCameraBonus()
        {
            Assert.AreEqual(Mathf.RoundToInt(250 * 1.25f), Payout.Share(1000, 4, 1.25f));
        }

        [Test]
        public void Share_IsNothingWhenNobodySurvives()
        {
            Assert.AreEqual(0, Payout.Share(1000, 0, 1f));
        }

        [Test]
        public void Share_IsNothingOnAnEmptyBounty()
        {
            Assert.AreEqual(0, Payout.Share(0, 3, 1f));
        }

        [Test]
        public void Share_TreatsANegativeMultiplierAsZeroRatherThanADebt()
        {
            Assert.AreEqual(0, Payout.Share(1000, 4, -2f));
        }

        [Test]
        public void Bounty_ScalesWithTheContract()
        {
            Assert.AreEqual(600, Payout.Bounty(400, 1.5f));
            Assert.AreEqual(0, Payout.Bounty(400, -1f));
        }
    }
}
