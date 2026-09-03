using UnityEngine;

namespace Transity.Combat
{
    /// <summary>
    /// What kind of harm this was. Drives the ledger's cause-of-death line, which is the
    /// only thing a crew has to go on when one of them dies to gunfire nobody saw.
    /// </summary>
    public enum DamageKind : byte
    {
        Ballistic = 0,
        Melee = 1,
        Bite = 2,
        Sedative = 3,
        Bleed = 4,
        Trap = 5,
        Fall = 6,
        Environment = 7
    }

    /// <summary>
    /// One hit. Built on the server by whatever dealt the damage and handed to
    /// <see cref="Health"/>, which decides what it means for the thing that was hit.
    /// </summary>
    public struct DamageInfo
    {
        public float Amount;
        public DamageKind Kind;
        public Vector3 Point;
        public Vector3 Direction;

        /// <summary>Client that caused it, or <see cref="NoInstigator"/> for creatures and the world.</summary>
        public ulong InstigatorClientId;

        /// <summary>Set when the hit landed on a weak point, for the hit marker.</summary>
        public bool WeakPoint;

        /// <summary>Sedation applied alongside (or instead of) health damage.</summary>
        public float Sedation;

        /// <summary>Whether the hit opens a wound that bleeds until treated.</summary>
        public bool CausesBleeding;

        /// <summary>Stable hash of the creature definition that dealt it, or 0.</summary>
        public int SourceId;

        public const ulong NoInstigator = ulong.MaxValue;

        public bool HasInstigator => InstigatorClientId != NoInstigator;

        public static DamageInfo FromCreature(float amount, DamageKind kind, Vector3 point, Vector3 direction,
            bool bleeding, int sourceId)
        {
            return new DamageInfo
            {
                Amount = amount,
                Kind = kind,
                Point = point,
                Direction = direction,
                InstigatorClientId = NoInstigator,
                CausesBleeding = bleeding,
                SourceId = sourceId
            };
        }
    }
}
