namespace Transity.Core
{
    public enum StartupMode
    {
        /// <summary>Normal flow: Boot -> MainMenu -> host or join by code.</summary>
        MainMenu = 0,

        /// <summary>
        /// Skip the menu and start a local host immediately, straight into the train hub.
        /// Uses NetworkManager directly rather than the Sessions SDK, so it needs no Relay
        /// allocation, no sign-in and no linked Unity Cloud project -- it just runs.
        /// Other players cannot join this; it is for looking at the scene and testing solo.
        /// </summary>
        OfflineHost = 1,

        /// <summary>
        /// Straight into the forest, mid-expedition, on the first contract.
        ///
        /// Exists because testing the hunt through the front door means booting, walking
        /// the depot, picking a contract and departing before a single creature exists --
        /// and then arriving with an empty pack. This skips all of it: creatures are
        /// spawned by the director as usual, and a supply cache sits beside the landing
        /// zone so there is something to fight them with.
        /// </summary>
        ForestSandbox = 2
    }
}
