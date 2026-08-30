namespace Transity.Core
{
    /// <summary>
    /// Single source of truth for scene names. Anything that loads a scene goes
    /// through here so a rename is one edit instead of a string hunt.
    /// </summary>
    public static class SceneCatalog
    {
        public const string Boot = "Boot";
        public const string MainMenu = "MainMenu";
        public const string TrainHub = "TrainHub";
        public const string Forest = "Forest";

        /// <summary>Scenes that must be present in Build Settings, in build order.</summary>
        public static readonly string[] All = { Boot, MainMenu, TrainHub, Forest };
    }
}
