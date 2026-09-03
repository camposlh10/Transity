using UnityEngine;

namespace Transity.Creatures
{
    /// <summary>
    /// The arithmetic of noticing. Pure functions so the numbers can be tested without a
    /// creature, and so the brain reads as decisions rather than trigonometry.
    /// </summary>
    public static class Perception
    {
        /// <summary>
        /// 0..1+ how well a target can be seen. Distance falls off linearly inside range,
        /// the field of view is a hard edge with a soft rim, and visibility factors
        /// (lights, crouching, glow sticks) multiply on top.
        /// </summary>
        public static float SightScore(float distance, float sightRange, float angleDegrees, float fovDegrees,
            bool lineOfSight, float visibility)
        {
            if (!lineOfSight || sightRange <= 0f || distance > sightRange * visibility)
            {
                return 0f;
            }

            var halfFov = fovDegrees * 0.5f;
            if (angleDegrees > halfFov)
            {
                return 0f;
            }

            // Sharpest in the middle of the view; the rim of the eye is worse.
            var rim = angleDegrees > halfFov * 0.7f
                ? 1f - (angleDegrees - halfFov * 0.7f) / (halfFov * 0.3f) * 0.6f
                : 1f;

            var proximity = 1f - Mathf.Clamp01(distance / (sightRange * visibility));

            // Very close is very obvious, whatever the light.
            var closeBonus = distance < 4f ? 1.5f : 1f;

            return Mathf.Clamp(proximity * rim * closeBonus * visibility, 0f, 2f);
        }

        /// <summary>0..1 how loud a noise arrives, given the creature's hearing.</summary>
        public static float HearingScore(float distance, float noiseRadius, float hearing)
        {
            var reach = noiseRadius * hearing;
            if (reach <= 0f || distance >= reach)
            {
                return 0f;
            }

            return 1f - distance / reach;
        }

        /// <summary>Whether an observer at a position, facing a way, has a target inside a cone.</summary>
        public static bool IsInsideCone(Vector3 observerPosition, Vector3 observerForward, Vector3 target,
            float coneDegrees)
        {
            var to = target - observerPosition;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            var forward = observerForward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            return Vector3.Angle(forward.normalized, to.normalized) <= coneDegrees * 0.5f;
        }

        /// <summary>
        /// Awareness gain per second from a sight score. Saturates so a creature that can
        /// already see you clearly does not become infinitely certain.
        /// </summary>
        public static float AwarenessGain(float sightScore, float secondsToNotice)
        {
            if (sightScore <= 0f || secondsToNotice <= 0f)
            {
                return 0f;
            }

            return Mathf.Min(sightScore, 1.5f) / secondsToNotice;
        }
    }
}
