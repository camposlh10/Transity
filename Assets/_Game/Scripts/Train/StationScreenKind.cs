namespace Transity.Train
{
    /// <summary>Which screen a station terminal opens.</summary>
    public enum StationScreenKind
    {
        /// <summary>Vendor stall: buy gear into the crew stash.</summary>
        Market = 0,

        /// <summary>Wardrobe: move gear between the stash and what you carry.</summary>
        Loadout = 1,

        /// <summary>Mission computer: crew overview and departure.</summary>
        MissionTerminal = 2
    }
}
