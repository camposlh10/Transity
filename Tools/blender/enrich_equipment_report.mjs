// Merges per-pack material data into the Blender export report, so the Unity table knows
// which Blender material on each model needs an emissive variant.
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const HERE = path.dirname(fileURLToPath(import.meta.url));

// Where the drop was extracted. Override with: node enrich_equipment_report.mjs <packs dir>
const PACKS = process.argv[2] || path.join(HERE, "drop/packs");
const REPORT = path.join(HERE, "equipment_report.json");

if (!fs.existsSync(PACKS)) {
  console.error(`No pack directory at ${PACKS}.\n` +
    `Pass the extracted drop's packs/ directory as the first argument.`);
  process.exit(1);
}

const report = JSON.parse(fs.readFileSync(REPORT, "utf8"));

for (const rec of report) {
  const p = rec.pascal_name;
  const manifest = JSON.parse(
    fs.readFileSync(path.join(PACKS, `HD_${p}_BlenderPack`, `HD_${p}_Manifest.json`), "utf8"));

  rec.material_table = manifest.materials.map(m => ({
    blender_material: m.blender_material,
    key: m.material_key,
    emissive: !!m.emissive,
    tags: m.tags,
  }));
  rec.emissive_materials = manifest.materials.filter(m => m.emissive).map(m => m.blender_material);
  rec.atlas_group = manifest.atlas_group;
  rec.secondary_tags = manifest.asset.secondary_tags;
}

fs.writeFileSync(REPORT, JSON.stringify(report, null, 2));

console.log("assets:", report.length);
console.log("with emissive:", report.filter(r => r.emissive_materials.length).length);
console.log("atlas groups:", [...new Set(report.map(r => r.atlas_group))].join(", "));
console.log("slots:", [...new Set(report.map(r => r.slot))].join(", "));
console.log("collider kinds:", [...new Set(report.map(r => r.collider))].join(" | "));
console.log("\nemissive material names (sample):");
for (const r of report.slice(0, 4)) {
  console.log(" ", r.pascal_name, "->", r.emissive_materials.join(",") || "(none)");
}
