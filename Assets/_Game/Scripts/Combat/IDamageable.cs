using UnityEngine;

namespace Transity.Combat
{
    /// <summary>
    /// Anything a hit can land on. Weapons and traps find this through the collider they
    /// hit and never care whether it was a player, a creature or a barrel.
    /// </summary>
    public interface IDamageable
    {
        Transform Transform { get; }
        bool IsAlive { get; }

        /// <summary>Server only. Clients raise requests; this is where the number is applied.</summary>
        void ServerApplyDamage(in DamageInfo info);
    }
}
