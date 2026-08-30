namespace Transity.Missions
{
    /// <summary>
    /// The expedition loop. Every phase change is a server decision replicated to clients;
    /// nothing in the game advances this by itself on a client.
    /// </summary>
    public enum MissionPhase : byte
    {
        /// <summary>Aboard the train: shopping, loadout, contract selection. Late joins allowed here.</summary>
        Preparing = 0,

        /// <summary>Contract accepted, loading the expedition scene.</summary>
        Deploying = 1,

        /// <summary>In the forest. The run is live and equipment is at risk.</summary>
        Expedition = 2,

        /// <summary>Extraction triggered, returning to the train.</summary>
        Extracting = 3,

        /// <summary>Results screen: payout or loss, then back to Preparing.</summary>
        Debrief = 4
    }
}
