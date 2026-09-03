using System;
using Transity.Combat;

namespace Transity.Missions
{
    public enum LedgerKind : byte
    {
        Kill = 0,
        Capture = 1,
        PlayerDeath = 2,
        Extracted = 3,
        Exposed = 4,
        Lost = 5,
        Completion = 6
    }

    /// <summary>
    /// One line of the expedition's record, replicated so the debrief reads the same on
    /// every screen. Deliberately never names the shooter on a player death: the cause
    /// says "gunfire" and the crew draws its own conclusions. Unmanaged for NetworkList.
    /// </summary>
    public struct LedgerEntry : IEquatable<LedgerEntry>
    {
        public LedgerKind Kind;

        /// <summary>Creature definition id for kills, captures and creature-caused deaths.</summary>
        public int SubjectId;

        /// <summary>The player it concerns: the tagger, the captor, the dead, the paid.</summary>
        public ulong ClientId;

        /// <summary>A second player where one is named publicly (an exposed murderer).</summary>
        public ulong ByClientId;

        public int Value;
        public DamageKind Cause;

        public bool Equals(LedgerEntry other) =>
            Kind == other.Kind && SubjectId == other.SubjectId && ClientId == other.ClientId &&
            ByClientId == other.ByClientId && Value == other.Value && Cause == other.Cause;

        public override bool Equals(object obj) => obj is LedgerEntry other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Kind, SubjectId, ClientId, ByClientId, Value, (int)Cause);
    }
}
