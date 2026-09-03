// Emits the C# equipment catalogue from the Blender export report, so the table in Unity
// and the meshes on disk cannot drift apart.
import fs from "fs";
import path from "path";

import { fileURLToPath } from "url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, "../..");

const report = JSON.parse(fs.readFileSync(path.join(HERE, "equipment_report.json"), "utf8"));
const OUT = path.join(REPO, "Assets/_Game/Editor/EquipmentCatalog.cs");

// Price, stash limit and whether the item is issued free. Not in the art manifests -- this
// is game balance, so it lives here where it can be tuned without touching the art drop.
const ECON = {
  basic_flashlight:         { price: 0,   stash: 20, issued: true },
  heavy_flashlight:         { price: 180, stash: 20 },
  glow_stick:               { price: 25,  stash: 99 },
  night_vision_goggles:     { price: 520, stash: 5 },
  thermal_monocular:        { price: 640, stash: 5 },
  uv_tracking_light:        { price: 210, stash: 20 },
  creature_bait_canister:   { price: 90,  stash: 30 },
  motion_sensor_alarm:      { price: 150, stash: 30 },
  scent_neutralizer_spray:  { price: 110, stash: 30 },
  medical_kit:              { price: 120, stash: 20 },
  adrenaline_injector:      { price: 95,  stash: 20 },
  field_respirator:         { price: 260, stash: 10 },
  trauma_kit:               { price: 300, stash: 10 },
  light_hunter_vest:        { price: 340, stash: 5 },
  heavy_hunter_vest:        { price: 580, stash: 5 },
  tranquilizer_pistol:      { price: 430, stash: 5 },
  field_repair_kit:         { price: 200, stash: 10 },
  hunter_body_camera:       { price: 275, stash: 10 },
  standard_hunter_backpack: { price: 0,   stash: 3,  issued: true },
  field_pistol:             { price: 250, stash: 5 },
  heavy_hunting_handgun:    { price: 610, stash: 5 },
  pump_shotgun:             { price: 480, stash: 5 },
  semi_automatic_shotgun:   { price: 720, stash: 5 },
  compact_carbine:          { price: 540, stash: 5 },
  big_game_rifle:           { price: 880, stash: 5 },
  hunter_crossbow:          { price: 495, stash: 5 },
  rescue_hatchet:           { price: 70,  stash: 10 },
};

// Manifest slot -> shop aisle. Mostly one-to-one; the two overrides put the alarm beside
// the other traps and the glow stick beside the other light sources, which is where a
// player looking for them would actually go.
const SLOT_CATEGORY = {
  flashlight: "Lighting",
  vision: "Optics",
  utility: "Gadget",
  medical: "Medical",
  armor: "Armor",
  sidearm: "Weapon",
  primary: "Weapon",
  backpack: "Gear",
  accessory: "Gear",
  tool: "Tool",
};
const CATEGORY_OVERRIDE = {
  motion_sensor_alarm: "Trap",
  glow_stick: "Lighting",
};

const SLOT_ENUM = {
  flashlight: "Flashlight", vision: "Vision", utility: "Utility", medical: "Medical",
  armor: "Armor", sidearm: "Sidearm", primary: "Primary", backpack: "Backpack",
  accessory: "Accessory", tool: "Tool",
};

function colliderKind(spec) {
  return /capsule/i.test(spec) ? "Capsule" : "Box";
}

function csharpString(s) {
  return '"' + String(s).replace(/\\/g, "\\\\").replace(/"/g, '\\"') + '"';
}

const rows = report.map(r => {
  const econ = ECON[r.asset_key];
  if (!econ) throw new Error("no economy entry for " + r.asset_key);
  const category = CATEGORY_OVERRIDE[r.asset_key] || SLOT_CATEGORY[r.slot];
  if (!category) throw new Error("no category for slot " + r.slot);

  return {
    model: "HD_" + r.pascal_name,
    id: "item.hd." + r.asset_key.replace(/_/g, ""),
    display: r.display_name,
    slot: SLOT_ENUM[r.slot],
    category,
    price: econ.price,
    stash: econ.stash,
    atRisk: !econ.issued,
    collider: colliderKind(r.collider),
    atlas: r.atlas,
    emissive: r.emissive_materials,
    tradeoff: r.tradeoff,
    tris: r.triangles,
    dims: r.measured_size,
  };
});

const lines = [];
lines.push(`using System.Collections.Generic;`);
lines.push(`using Transity.Inventory;`);
lines.push(``);
lines.push(`namespace Transity.EditorTools`);
lines.push(`{`);
lines.push(`    /// <summary>`);
lines.push(`    /// The Hunter Depot equipment collection, as it lands in the game.`);
lines.push(`    ///`);
lines.push(`    /// Generated from the art drop's manifests, so the shop list, the item assets and`);
lines.push(`    /// the meshes on disk all come from one table. Prices, stash limits and shop aisles`);
lines.push(`    /// are balance rather than art, so those are set here and survive a re-import.`);
lines.push(`    ///`);
lines.push(`    /// Colliders are deliberately one primitive per item even where the art manifest`);
lines.push(`    /// asks for several: these are pickups, and a single fitted box or capsule is what`);
lines.push(`    /// the interaction ray needs. Revisit if equipment ever becomes physics debris.`);
lines.push(`    /// </summary>`);
lines.push(`    public static class EquipmentCatalog`);
lines.push(`    {`);
lines.push(`        public sealed class Entry`);
lines.push(`        {`);
lines.push(`            public string Model;`);
lines.push(`            public string ItemId;`);
lines.push(`            public string DisplayName;`);
lines.push(`            public EquipmentSlot Slot;`);
lines.push(`            public ItemCategory Category;`);
lines.push(`            public int Price;`);
lines.push(`            public int StashLimit;`);
lines.push(`            public bool AtRisk;`);
lines.push(`            public ColliderKind Collider;`);
lines.push(`            public string Atlas;`);
lines.push(`            public string[] EmissiveMaterials;`);
lines.push(`            public string Tradeoff;`);
lines.push(`        }`);
lines.push(``);
lines.push(`        public enum ColliderKind { Box, Capsule }`);
lines.push(``);
lines.push(`        public static readonly IReadOnlyList<Entry> Entries = new[]`);
lines.push(`        {`);

for (const r of rows) {
  lines.push(`            // ${r.tris} tris, ${r.dims.map(v => v.toFixed(2)).join(" x ")} m -- ${r.tradeoff}`);
  lines.push(`            new Entry`);
  lines.push(`            {`);
  lines.push(`                Model = ${csharpString(r.model)},`);
  lines.push(`                ItemId = ${csharpString(r.id)},`);
  lines.push(`                DisplayName = ${csharpString(r.display)},`);
  lines.push(`                Slot = EquipmentSlot.${r.slot},`);
  lines.push(`                Category = ItemCategory.${r.category},`);
  lines.push(`                Price = ${r.price},`);
  lines.push(`                StashLimit = ${r.stash},`);
  lines.push(`                AtRisk = ${r.atRisk},`);
  lines.push(`                Collider = ColliderKind.${r.collider},`);
  lines.push(`                Atlas = ${csharpString(r.atlas)},`);
  lines.push(`                EmissiveMaterials = new[] { ${r.emissive.map(csharpString).join(", ") || ""} },`);
  lines.push(`                Tradeoff = ${csharpString(r.tradeoff)}`);
  lines.push(`            },`);
}

lines.push(`        };`);
lines.push(``);
lines.push(`        /// <summary>Looks an entry up by model name, e.g. "HD_BasicFlashlight".</summary>`);
lines.push(`        public static Entry Find(string model)`);
lines.push(`        {`);
lines.push(`            foreach (var entry in Entries)`);
lines.push(`            {`);
lines.push(`                if (entry.Model == model)`);
lines.push(`                {`);
lines.push(`                    return entry;`);
lines.push(`                }`);
lines.push(`            }`);
lines.push(``);
lines.push(`            return null;`);
lines.push(`        }`);
lines.push(``);
lines.push(`        /// <summary>Null for the graybox items, which are not part of this drop.</summary>`);
lines.push(`        public static Entry FindByItemId(string itemId)`);
lines.push(`        {`);
lines.push(`            foreach (var entry in Entries)`);
lines.push(`            {`);
lines.push(`                if (entry.ItemId == itemId)`);
lines.push(`                {`);
lines.push(`                    return entry;`);
lines.push(`                }`);
lines.push(`            }`);
lines.push(``);
lines.push(`            return null;`);
lines.push(`        }`);
lines.push(`    }`);
lines.push(`}`);

// Empty array initialisers need an explicit type, and C# rejects "new[] {  }".
let text = lines.join("\r\n") + "\r\n";
text = text.replace(/new\[\] \{  \}/g, "System.Array.Empty<string>()");

fs.writeFileSync(OUT, text);
console.log("wrote", OUT, "-", rows.length, "entries");
console.log("categories:", [...new Set(rows.map(r => r.category))].sort().join(", "));
console.log("slots:", [...new Set(rows.map(r => r.slot))].sort().join(", "));
console.log("capsule colliders:", rows.filter(r => r.collider === "Capsule").map(r => r.model).join(", "));
console.log("ids:", rows.length, "unique:", new Set(rows.map(r => r.id)).size);
