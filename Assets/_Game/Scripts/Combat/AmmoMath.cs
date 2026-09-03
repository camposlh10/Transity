using UnityEngine;

namespace Transity.Combat
{
    /// <summary>
    /// The arithmetic of a reload, kept pure so it can be tested without a scene.
    /// </summary>
    public static class AmmoMath
    {
        /// <summary>
        /// Moves rounds from a reserve into a magazine. Returns how many moved; the
        /// magazine and reserve are updated in place.
        /// </summary>
        public static int Reload(ref int magazine, int magazineSize, ref int reserve)
        {
            var wanted = Mathf.Max(0, magazineSize - magazine);
            var moved = Mathf.Min(wanted, Mathf.Max(0, reserve));
            magazine += moved;
            reserve -= moved;
            return moved;
        }

        /// <summary>
        /// Spread as a direction: a random point in a cone of the given half-angle around
        /// <paramref name="forward"/>. Seeded so the server can reproduce a client's shot.
        /// </summary>
        public static Vector3 Scatter(Vector3 forward, float halfAngleDegrees, System.Random random)
        {
            if (halfAngleDegrees <= 0f)
            {
                return forward;
            }

            var yaw = (float)(random.NextDouble() * 2.0 - 1.0) * halfAngleDegrees;
            var pitch = (float)(random.NextDouble() * 2.0 - 1.0) * halfAngleDegrees;
            var rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(pitch, yaw, 0f);
            return rotation * Vector3.forward;
        }
    }
}
