namespace Transity.Inventory
{
    /// <summary>
    /// Shop and loadout grouping. Purely presentational for now, but it is what the market
    /// screen sorts by, so new gear lands in the right aisle without UI changes.
    /// </summary>
    public enum ItemCategory
    {
        Tool = 0,
        Medical = 1,
        Ammunition = 2,
        Trap = 3,
        Gadget = 4,
        Weapon = 5,

        // Appended for the Hunter Depot collection. Append only -- these are serialized as
        // ints in the item assets, so renumbering silently re-files existing gear.
        Lighting = 6,
        Optics = 7,
        Armor = 8,
        Gear = 9
    }
}
