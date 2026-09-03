# Equipment pipeline

Rebuilds the Hunter Depot equipment in `Assets/_Game/Art/Equipment` from the source drop.

The drop (`HunterDepot_V1_Equipment_Collection.zip`) contains no meshes. Each of the 27
items is a Blender *generator script* that builds its geometry when run, plus an albedo
atlas and a manifest. These scripts run the whole set headlessly and land the result in
Unity.

## Order

Paths below assume the zip is extracted to `<drop>`, so `<drop>/packs/HD_*_BlenderPack`
exists.

```bash
blender -b --factory-startup --python Tools/blender/export_equipment.py -- \
  "<drop>/packs" "Assets/_Game/Art/Equipment" "Tools/blender/equipment_report.json"
```

```bash
node Tools/blender/enrich_equipment_report.mjs
node Tools/blender/generate_equipment_catalog.mjs
```

Then in Unity: `Tools > Transity > Build Vertical Slice Scaffold`, or
`Tools > Transity > Rebuild (Keep My Depot Edits)` once the depot has hand edits worth
keeping. Either one reimports the equipment, builds materials, and regenerates the item
definitions and world prefabs.

Check the result with `Tools > Transity > Render Equipment Contact Sheet`.

## Known faults in the V1 drop

Both are worked around, not patched into the vendor files, so a corrected drop can replace
this one without unpicking anything.

**JSON booleans in Python source.** 20 of the 27 generators contain `"emissive": true`
inside their `SPEC` dict, which is a `NameError` the moment the module executes.
`export_equipment.py` binds `true`/`false` in the exec namespace.

**Three truncated atlases.** `CompactCarbine`, `TraumaKit` and `LightHunterVest` end
mid-`IDAT` with no `IEND` chunk — inside the zip, so this is not a download artifact. No
image loader will open them, and Unity fails the import outright.
`repair_truncated_png.mjs` inflates the scanlines that survived, repeats the last good row
over the missing tail and re-encodes a valid PNG:

| atlas | rows recovered |
|---|---|
| `HD_TraumaKit_Atlas_Albedo.png` | 1800 / 2048 (88%) |
| `HD_LightHunterVest_Atlas_Albedo.png` | 1561 / 2048 (76%) |
| `HD_CompactCarbine_Atlas_Albedo.png` | 1404 / 2048 (69%) |

Damage is at the bottom of each image, which is the lower row of atlas cells — so the
affected materials are the later ones in each manifest, and they show a vertical smear
rather than a hole. Replace these three files if a clean drop arrives; nothing else needs
to change.

## What the report is for

`equipment_report.json` is what Blender measured on the way out — triangle counts, real
bounding sizes, collider specs, material tables and which materials are emissive. The C#
catalogue is generated from it, so the table in Unity cannot drift from the meshes on disk.
Prices, stash limits, shop aisles and equipment slots are balance rather than art, and live
in `generate_equipment_catalog.mjs`.
