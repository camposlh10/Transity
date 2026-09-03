namespace Transity.Creatures
{
    /// <summary>
    /// What a creature is doing. Decided on the server, replicated so every client can
    /// animate and voice it, and so the HUD can decide how frightened to be.
    /// </summary>
    public enum CreatureState : byte
    {
        Idle = 0,
        Roam = 1,
        Investigate = 2,
        Stalk = 3,
        Chase = 4,
        Attack = 5,
        Flee = 6,
        Recover = 7,
        Rooted = 8,
        Sedated = 9,
        Dead = 10
    }

    /// <summary>How a creature reads a crew.</summary>
    public enum Temperament : byte
    {
        /// <summary>Guards a patch. Slow to notice, will not be shaken off inside it, gives up outside it.</summary>
        Territorial = 0,

        /// <summary>Follows at a distance and only closes when nobody is looking.</summary>
        Hunter = 1,

        /// <summary>Bold in numbers, cowardly alone. Flanks.</summary>
        Pack = 2
    }

    public enum BodyShape : byte
    {
        Quadruped = 0,
        Hound = 1,
        Stilt = 2
    }
}
