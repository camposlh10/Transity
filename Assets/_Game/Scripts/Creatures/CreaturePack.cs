using System.Collections.Generic;
using UnityEngine;

namespace Transity.Creatures
{
    /// <summary>
    /// Bookkeeping for creatures that hunt together. Each member is handed a slot around
    /// the target so a pack spreads out instead of queueing behind the one in front --
    /// which is what makes three hounds feel like a pack and not three copies.
    /// Server only.
    /// </summary>
    public static class CreaturePack
    {
        static readonly Dictionary<int, List<CreatureBrain>> Packs = new();

        public static void Join(int packId, CreatureBrain member)
        {
            if (packId <= 0)
            {
                return;
            }

            if (!Packs.TryGetValue(packId, out var members))
            {
                members = new List<CreatureBrain>();
                Packs[packId] = members;
            }

            if (!members.Contains(member))
            {
                members.Add(member);
            }
        }

        public static void Leave(int packId, CreatureBrain member)
        {
            if (packId > 0 && Packs.TryGetValue(packId, out var members))
            {
                members.Remove(member);
                if (members.Count == 0)
                {
                    Packs.Remove(packId);
                }
            }
        }

        /// <summary>Living members within the cohesion radius of this one, including itself.</summary>
        public static int Together(int packId, CreatureBrain member, float cohesionRadius)
        {
            if (packId <= 0 || !Packs.TryGetValue(packId, out var members))
            {
                return 1;
            }

            var count = 0;
            foreach (var other in members)
            {
                if (other == null || other.State is CreatureState.Dead or CreatureState.Sedated)
                {
                    continue;
                }

                if ((other.transform.position - member.transform.position).sqrMagnitude <= cohesionRadius * cohesionRadius)
                {
                    count++;
                }
            }

            return Mathf.Max(1, count);
        }

        /// <summary>
        /// Where this member should stand relative to a target: evenly spaced around it,
        /// biased to the side the member is already on so they do not cross paths.
        /// </summary>
        public static Vector3 FlankPoint(int packId, CreatureBrain member, Vector3 target, float radius)
        {
            if (packId <= 0 || !Packs.TryGetValue(packId, out var members) || members.Count <= 1)
            {
                return target;
            }

            var index = Mathf.Max(0, members.IndexOf(member));
            var count = members.Count;

            var toMember = member.transform.position - target;
            toMember.y = 0f;
            var baseAngle = toMember.sqrMagnitude > 0.01f ? Mathf.Atan2(toMember.x, toMember.z) : 0f;

            // Spread the pack over 200 degrees centred where they are approaching from.
            var spread = 200f * Mathf.Deg2Rad;
            var angle = baseAngle + (index / (float)(count - 1) - 0.5f) * spread;

            return target + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
        }

        public static void Clear() => Packs.Clear();
    }
}
