# Environment art spec — Depot Lobby

The master layout reference is the isometric roof-off concept: scale mannequin in frame,
floor grid visible. Treat it as **composition reference only**. The room is built as a
modular kit, never as one generated mesh.

## Dimensions

| Element | Size |
| --- | --- |
| Main lobby | 24 × 18 m |
| Interior height | 7 m |
| Wall thickness | 0.20–0.30 m |
| Train opening | 5 × 5 m |
| Central table | 4.2 × 1.8 m |
| Central rug | 7 × 5 m |
| Mission station | 5.5 × 3 m |
| Trophy gallery | 5 × 4 m |
| Loadout station | 5 × 3 m |
| Wardrobe | 4 × 3 m |
| Tavern counter | 5 × 1.2 m |
| Main circulation paths | minimum 1.8 m |
| Normal doorways | minimum 1.2 m |

Construction grid is **1 m**; props snap to **0.25 m**. Player reference height is **1.8 m**.

### Bay layout as built

The north wall carries the platform arch. The arch module is **6 m wide with a 5 m clear
opening**, spanning X = 2 → 8. That is deliberate: 24 m − 6 m leaves runs of 14 m and 4 m,
both of which divide cleanly into the 4 m / 2 m wall kit. A 5 m module would have left an
odd 19 m of wall, which no combination of 4 m and 2 m pieces can fill.

Columns sit on X = −12, −8, −4, 0, 2, 8, 12 and Z = −9, −5, −1, 3, 7, 9.

## Modular kit

Author each as a separate Blender asset.

**Architecture** — 2 m and 4 m wall modules · 4 m structural bays · steel columns and
horizontal beams · wooden wall inserts · 2 × 2 m floor sections · roof trusses ·
train-platform arch · stairs and railings · fireplace structure.

**Functional prefabs** — mission board and computer desk · trophy cabinets · loadout
workbench and weapon racks · wardrobe, lockers and mirror · central table and chairs ·
tavern counter · train-platform equipment · lighting fixtures.

> Furniture is never joined to the building. Every gameplay station stays its own Unity
> prefab, dropped onto a `StationAnchor`.

## Geometry targets

| Asset | Triangles |
| --- | --- |
| Simple architectural module | 200–1,500 |
| Chair, locker or cabinet | 1,500–4,000 |
| Large table or workbench | 3,000–8,000 |
| Small prop | 300–2,000 |
| Hero trophy skull | 4,000–12,000 |
| Visible train exterior | 40,000–80,000 |
| Complete visible lobby | ~250,000–450,000 |

Silhouette beats density. Small bevels on furniture and structural edges do more for the
stylised lighting than extra polygons.

## Textures

| Set | Resolution |
| --- | --- |
| Architecture trim sheet | 2048² |
| Shared furniture atlas | 2048² |
| Train texture set | 2048² |
| Hero trophy textures | 1024² |
| Ordinary props | 512² or 1024² |

Texel density ~256 px/m, up to 512 px/m on hero objects. Target **8–12 shared materials**
for the whole room. Softly painted base colours, gentle AO, simplified roughness. Normal
maps only on structural surfaces and important objects.

The blockout already uses a 10-material palette on that budget: `ENV_Floor`, `ENV_WallWood`,
`ENV_Steel`, `ENV_Trim`, `PRP_Wood`, `PRP_Leather`, `PRP_Fabric`, `PRP_Metal`, `PRP_Bone`,
`FX_Fire`.

## Blender → Unity

- One Blender unit = one metre.
- Apply rotation and scale before export.
- Bottom-centre origins for furniture; door pivots on the hinge.
- Export FBX with **−Z Forward, Y Up**. Import at scale 1.
- Generate secondary UVs for baked lighting.
- Collision objects suffixed `_COL`; LODs suffixed `_LOD0` / `_LOD1` / `_LOD2`.

### Naming

```
ENV_Depot_Wall_4m_A_LOD0
ENV_Depot_Wall_4m_A_LOD1
ENV_Depot_Wall_4m_A_COL
PRP_MissionDesk_A
PRP_TrophyCabinet_A
PRP_HunterChair_A
```

The generated blockout already follows `ENV_` / `PRP_` prefixes, so finished art can replace
placeholder objects by name.

## Unity rendering targets

- URP, 1080p at 60 FPS on a lower-midrange PC.
- Mostly baked lighting. **2–4 real-time shadow-casting lights** — currently the sun and the
  fireplace; the four interior lamps are set to `Baked` with shadows off.
- One main exterior directional light.
- Light probes over the playable area; reflection probes for the main room and the platform.
- Simple Box Colliders wherever possible. Small decorative objects have no collision.
- ~250–350 draw calls before characters and effects. All architecture is flagged
  batching-static, contribute-GI, occluder and occludee.

## AI 3D generation

**Do not** feed the whole lobby image to Meshy or similar expecting a finished environment.
It produces fused furniture and architecture, wrong scale, broken rear geometry, unusable
collision, excessive topology, poor modularity and difficult UVs.

Use image-to-3D **only for isolated assets**: creature skulls, lanterns, chairs, backpacks,
decorative trophies, coffee equipment, small hunting props.

Room, walls, beams, floor, workstations and pathways are built by hand — in Blender, in
ProBuilder, or (as now) generated procedurally from `DepotBlockout.cs`, which has the
advantage of snapping exactly to the 1 m grid and being re-runnable.

## Replacing the blockout

`Tools > Transity > Build Vertical Slice Scaffold` regenerates the depot from code and
**overwrites the TrainHub scene wholesale**. Once real art starts landing, stop running it
against that scene and edit by hand — or swap the meshes inside the station prefabs in
`Assets/_Game/Prefabs/Stations/`, which the generator writes once and the scene only
references.
