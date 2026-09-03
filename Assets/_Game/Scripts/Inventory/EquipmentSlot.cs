namespace Transity.Inventory
{
    /// <summary>
    /// Where a piece of equipment sits on the hunter.
    ///
    /// Distinct from <see cref="ItemCategory"/>, which is only the shop aisle: two items can
    /// share an aisle and compete for the same slot (both vests), or share a slot and sit in
    /// different aisles (the glow stick and the flashlight). Loadout rules key off this.
    /// </summary>
    public enum EquipmentSlot
    {
        /// <summary>Carried freely; no slot contention. Ammunition and the legacy graybox items.</summary>
        None = 0,
        Flashlight = 1,
        Vision = 2,
        Utility = 3,
        Medical = 4,
        Armor = 5,
        Sidearm = 6,
        Primary = 7,
        Backpack = 8,
        Accessory = 9,
        Tool = 10
    }
}
