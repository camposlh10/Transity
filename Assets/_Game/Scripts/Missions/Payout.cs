using UnityEngine;

namespace Transity.Missions
{
    /// <summary>The money arithmetic, pure so it can be pinned by tests.</summary>
    public static class Payout
    {
        /// <summary>
        /// One survivor's cut of the crew total. Dead hunters get nothing -- their gear is
        /// already gone -- and a body camera is worth a quarter more on top.
        /// </summary>
        public static int Share(int crewTotal, int survivors, float bountyMultiplier)
        {
            if (survivors <= 0 || crewTotal <= 0)
            {
                return 0;
            }

            return Mathf.RoundToInt(crewTotal / (float)survivors * Mathf.Max(0f, bountyMultiplier));
        }

        public static int Bounty(int baseBounty, float contractMultiplier) =>
            Mathf.RoundToInt(baseBounty * Mathf.Max(0f, contractMultiplier));
    }
}
