using System;
using System.Collections.Generic;
using UnityEngine;

namespace Transity.Missions
{
    /// <summary>
    /// Whether anyone saw it. Pure geometry with the line-of-sight test injected, so it
    /// runs in a unit test as readily as on the server.
    ///
    /// A witness is anyone who has the killer in view -- inside their field of view, in
    /// range, and with nothing between them -- or who is standing close enough that no
    /// amount of looking the other way would be believed.
    /// </summary>
    public static class WitnessCheck
    {
        public struct Observer
        {
            public Vector3 Position;
            public Vector3 Forward;
            public float FovDegrees;
            public float MaxDistance;
        }

        public const float PointBlankRadius = 6f;

        public static bool IsWitnessed(Vector3 killer, IEnumerable<Observer> observers,
            Func<Vector3, Vector3, bool> hasLineOfSight)
        {
            foreach (var observer in observers)
            {
                var to = killer - observer.Position;
                var distance = to.magnitude;

                if (distance <= PointBlankRadius)
                {
                    return true;
                }

                if (distance > observer.MaxDistance)
                {
                    continue;
                }

                var flat = new Vector3(to.x, 0f, to.z);
                var forward = new Vector3(observer.Forward.x, 0f, observer.Forward.z);
                if (flat.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                if (Vector3.Angle(forward, flat) > observer.FovDegrees * 0.5f)
                {
                    continue;
                }

                if (hasLineOfSight == null || hasLineOfSight(observer.Position, killer))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
