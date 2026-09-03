using System;
using UnityEngine;

namespace Transity.Combat
{
    public enum NoiseKind : byte
    {
        Footstep = 0,
        Sprint = 1,
        Gunshot = 2,
        Impact = 3,
        Alarm = 4,
        Voice = 5,
        Bait = 6
    }

    public struct NoiseEvent
    {
        public Vector3 Position;

        /// <summary>Metres. A creature inside this radius hears it, scaled by its hearing.</summary>
        public float Radius;

        public NoiseKind Kind;

        /// <summary>Who made it, for creatures to remember a face; NoInstigator for the world.</summary>
        public ulong SourceClientId;

        public float Time;
    }

    /// <summary>
    /// Server-side event bus for sounds that matter to creatures. Deliberately not
    /// networked: creatures only think on the server, so the noises only need to exist
    /// there. Clients hear the audible version through the audio system instead.
    /// </summary>
    public static class NoiseBus
    {
        public static event Action<NoiseEvent> Emitted;

        public static void Emit(Vector3 position, float radius, NoiseKind kind, ulong sourceClientId)
        {
            Emitted?.Invoke(new NoiseEvent
            {
                Position = position,
                Radius = radius,
                Kind = kind,
                SourceClientId = sourceClientId,
                Time = UnityEngine.Time.time
            });
        }
    }
}
