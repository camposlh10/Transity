# Transity

First-person, 1–4 player co-op creature hunting. A train is the hub and the extraction
point; expeditions run 20–30 minutes into the forest and back.

This repository holds **weeks 1–10 of the 12-week vertical slice**: project setup, movement,
interaction, host/join by code, synchronised scene transitions, 27 pieces of equipment,
three creatures with a shared behaviour tree, weapons and traps, contracts, extraction and a
debrief that pays out. Saving and the audio/visual polish pass are what remain.

---

## Requirements

| Area | Choice |
| --- | --- |
| Editor | Unity `6000.5.10f1` (the version this project is pinned to) |
| Rendering | Universal Render Pipeline `17.5.0` |
| Networking | Netcode for GameObjects `2.13.2` |
| Online sessions | Multiplayer Services SDK `2.3.1` (Lobby + Relay via Sessions) |
| Input | Input System `1.20.0` |
| Navigation | AI Navigation `2.0.14` |
| Camera | Cinemachine `3.1.7` |
| Testing | Test Framework, Multiplayer Play Mode `2.0.2`, Multiplayer Tools `2.2.11` |
| Version control | Git + Git LFS |

> The production plan calls for Unity 6.3 LTS. This project was created on `6000.5.10f1`
> (Unity 6.5), which is **not** the 6.3 LTS line. Downgrading a project is not supported by
> Unity, so switching would mean recreating the project on 6.3 LTS and re-importing these
> assets. Decide before the art pipeline starts producing content, not after.

## First run

1. Open the project. Unity resolves the new packages on first import — this takes a few
   minutes and the console will be noisy until it finishes.
2. **Link a Unity Cloud project**: `Project Settings > Services`. Relay and anonymous
   authentication will not work without it. Until then the game still boots and the main
   menu reports `Online unavailable` rather than hanging.
3. Run **`Tools > Transity > Build Vertical Slice Scaffold`**. This generates the item
   data, the networked prefabs, the four scenes with blockout geometry, and the build
   settings list.
4. Open `Assets/_Game/Scenes/Boot.unity` and press Play.
5. For a second player, use **Multiplayer Play Mode** (`Window > Multiplayer > Multiplayer
   Play Mode`) and enable one or more virtual players.

### Controls

| Input | Action |
| --- | --- |
| `WASD` / mouse | Move, look |
| `Shift` / `C` | Sprint, crouch |
| `Space` | Jump |
| `E` | Interact — stations, pickups, tagging a kill, containing a sedated creature |
| `LMB` | Use the held item — fire, heal, deploy, throw |
| `RMB` | Aim (narrows spread, zooms, steadies) |
| `R` | Reload |
| `1`–`4` / scroll | Select inventory slot |
| `F` | Torch on/off |
| `G` | Drop selected item |
| `Tab` | Hold for the crew scoreboard |
| `Alt`+`5` | Third-person inspection view (see below) |
| `Esc` | Release the cursor (essential with several editor instances) |

First person is full-body: look down and you see your own torso, legs and arms, with the
held item in the character's right hand. The head bone is collapsed for the owner, since
the camera sits where the skull would be. An upper-body override layer holds the arms in a
grip pose while the legs keep running, so a sprinting hunter does not wave their rifle
about.

`Alt`+`5` pulls the camera behind the character and gives the head back, for judging
animation and equipment from outside. It is a view, not a way to play: aim still comes
from the head, so the crosshair no longer sits where the shot goes.

## Project layout

```
Assets/_Game/
├── Art/            Materials, models, textures        (blockout materials are generated)
├── Audio/
├── Data/           ScriptableObject definitions
│   ├── Items/      ItemDefinition assets + ItemRegistry
│   ├── Network/    NetworkPrefabsList
│   ├── Creatures/  CreatureDefinition assets + CreatureRegistry
│   ├── Contracts/  ContractDefinition assets + ContractRegistry
│   ├── Behaviours/ Weapon / consumable / deployable / toggle item behaviours
│   └── Upgrades/   (week 11)
├── Editor/         Transity.Editor — scaffold generator, blockout helpers
├── Input/          InputSystem_Actions.inputactions
├── Prefabs/        Player, Interactables, Network
├── Scenes/         Boot, MainMenu, TrainHub, Forest
├── Scripts/        Transity.Runtime
│   ├── Core/       Bootstrap, scene catalog, persistence helpers, logging
│   ├── Networking/ SessionManager, ServerBootstrap
│   ├── Player/     Movement, look, input, spawning, feedback
│   ├── Interaction/IInteractable, Interactor, NetworkInteractable
│   ├── Inventory/  ItemDefinition, ItemRegistry, InventoryComponent, WorldItem
│   ├── Missions/   MissionDirector, MissionPhase, ExtractionPoint
│   ├── Train/      DepartureLever, StationTerminal
│   ├── UI/         MainMenuUI, HudUI, StationScreenUI, CollectorApparition
│   ├── Combat/     Health, Hitbox, Sedation, NoiseBus, traps and deployables
│   ├── Creatures/  CreatureBrain, Perception, CreatureBody, ForestDirector
│   ├── Audio/      Procedural one-shots, tension music
│   └── Saving/     (week 11)
└── Tests/EditMode/ Transity.Tests.EditMode
```

Three assemblies: `Transity.Runtime`, `Transity.Editor`, `Transity.Tests.EditMode`. Keeping
them separate keeps iteration compiles small and stops editor-only code leaking into builds.

## Authority model

The host is the server. It decides everything that can be cheated:

| Decision | Owner |
| --- | --- |
| Movement and look | Owning client (`NetworkTransform`, `AuthorityMode = Owner`) |
| Interaction outcome | **Server** — re-resolves the target and re-checks range and line of sight |
| Inventory contents | **Server** — `NetworkList<int>` of item ids, clients read only |
| Item spawning / despawning | **Server** |
| Mission phase and scene loads | **Server** (`MissionDirector`) |
| Selected hotbar slot | Owning client (a view concern only) |

Movement is owner-authoritative on purpose: for co-op PvE the cost of rollback machinery
buys nothing, and a lying client can only misreport its own position — not damage, loot or
payouts, which never leave the server.

`Interactor` shows a prompt from a local raycast, but that raycast is only a *request*. The
server independently resolves the `NetworkBehaviourReference`, checks distance from the
player root (not the camera, which a client controls) and casts for occluders before calling
`OnServerInteract`.

## Adding content

**A new interactable** — derive from `NetworkInteractable`, override `OnServerInteract`, and
put a collider on it. Nothing else needs to change; `Interactor` finds it through
`GetComponentInParent<IInteractable>`.

**A new item** — create an `ItemDefinition` asset (`Create > Transity > Item Definition`),
add it to `ItemRegistry`, and give it a world prefab carrying `NetworkObject` + `WorldItem`.
Register that prefab in `Data/Network/TransityNetworkPrefabs`.

In practice you rarely do that by hand: the 27 Hunter Depot items are generated from
`Editor/EquipmentCatalog.cs`, and a scaffold rebuild creates their definitions, materials,
world prefabs and network registration in one pass. Add a row to that table instead — price,
stash limit, shop aisle and slot all live there, next to the model name.

> Item ids cross the wire as FNV-1a hashes of the id string, not as `string.GetHashCode`,
> which is not stable between processes. Renaming an `itemId` changes its network id and
> invalidates saved loadouts — `ItemIdentityTests` pins the hash so this cannot happen
> silently.

**What an item does** when you click with it is an `ItemBehaviour` asset — weapon,
consumable, deployable, toggle or passive. `Editor/EquipmentTuning.cs` holds the numbers
for all 27; edit that table and run `Tools > Transity > Rebuild Gameplay Content`.

**A new creature** — add a row to `Editor/CreatureBuilder.cs`. One row produces the
definition asset, the graybox body with its hitboxes and weak point, the NavMeshAgent, the
network prefab and the registry entry. `CreatureBrain` is shared; a creature that needs
genuinely new behaviour is a new state in the brain, not a new script.

**A new contract** — add a row to `Editor/ContractBuilder.cs`: which creature, how many,
what it pays, and whether the Collector may make an offer during it.

`GameplayContentTests` then checks the result — that every creature can path, has a weak
point worth aiming at, cannot one-shot a full-health hunter and pays more alive than dead;
that every contract names creatures that exist; that every usable item actually does
something. These are the failures that produce no error at all, just a game that feels wrong.

## Creatures

Three mid-size creatures share one brain (`CreatureBrain`) and differ only by a
`CreatureDefinition` asset. The states are Idle, Roam, Investigate, Stalk, Chase, Attack,
Flee, Recover, Sedated and Dead, and the whole point of the design is that a player can
read which one they are in without a health bar:

| Creature | Temperament | Health | Run | Bite | Weak point | Kill / capture |
| --- | --- | --- | --- | --- | --- | --- |
| Mossback | Territorial | 520 | 6.8 | 38 | moss plate | 500 / 1100 |
| Stilt Stalker | Hunter | 260 | 8.0 | 26 | throat sac | 450 / 1000 |
| Bramble Hound (x3) | Pack | 120 | 9.0 | 16 | flank | 150 / 300 |

All three outrun a sprint (6.4 m/s), and sprinting drains stamina, so a footrace is never
the escape. Breaking line of sight is: awareness decays when they cannot see you and
interest runs out after 12–22 seconds. That pairing is deliberate and is pinned by tests —
a creature that is slower than a sprint is harmless in the open, and one that never forgets
makes a chase unlosable.

The rules that make them feel fair rather than scripted:

- **They lose you.** Awareness climbs while they can see you and decays when they cannot.
  Breaking line of sight and staying quiet works, and it works *visibly*.
- **They telegraph.** Every attack has a windup long enough to react to, and the body
  animates it. No creature can kill a full-health hunter in one hit.
- **Escape is terrain, not speed.** They are all faster than you; sprinting buys the
  seconds to get something solid between you and them, and stamina limits how many.
- **They are afraid too.** Below a health fraction they break off, heal, and come back —
  they do not fight to the death because nothing alive does.
- **They react to what you did, not where you are.** Shots, sprinting, glow sticks, bait
  and alarms all feed one `NoiseBus`; the brain investigates positions, not players.

Movement is smoothed on top of the NavMeshAgent (`CreatureBody`) so turns bank, legs cycle
with actual speed, and the head tracks its target — an agent driving a transform directly
is what makes AI look robotic.

`Tools > Transity > Rebuild Gameplay Content` regenerates creatures, contracts, deployables
and item tuning without touching a scene. That is the balance loop.

## The Collector

One hunter may be offered a private deal mid-expedition: kill a named crewmate without
being seen and keep a bonus. It is deliberately awkward to act on.

- Only offered on contracts with a `betrayalChance`, and never on the tier 1 opener.
- The offer is a `SendTo.SpecifiedInParams` RPC — nobody else's client is ever told.
- Payment requires the kill to be **unwitnessed**: `WitnessCheck` fails closed, treats
  point-blank as always seen, and respects walls and facing. Pinned by `BetrayalTests`.
- Friendly fire is scaled down but never zero, so betrayal takes commitment and the
  victim gets time to notice what is happening.
- The debrief tells the whole crew that *someone* was approached — never who, and never
  whether they took it.

## Networking rules for this phase

- Networking is in from the prototype, not bolted on later.
- Late joining is allowed **only** aboard the train.
- If the host quits, the expedition is cancelled and everyone returns to the menu.
- No host migration, no dedicated servers, no public matchmaking yet.
- Test every feature with at least one host **and** one client before calling it done.

## Art pipeline rules

- One Unity unit is one metre.
- Apply transforms before exporting from Blender.
- Modular environment pieces on a consistent grid.
- Few, reusable materials; atlases or trim sheets for environment kits.
- Collision meshes end in `_COL`; LODs end in `_LOD0` / `_LOD1` / `_LOD2`.
- Temporary models until the loop is fun. Creature silhouette, animation and sound come
  before texture detail.
- Baked environmental lighting, few real-time shadow casters, fog, LODs, pooled effects.
- The warm train interior should read as a different world from the cold forest — the
  blockout scenes already set that contrast in ambient and fog.

### Equipment (Hunter Depot collection)

The 27 gear models in `Art/Equipment` were not shipped as meshes. The drop is a set of
Blender *generator scripts*, one per item, which build the geometry when run. They are
rebuilt with `Tools/blender/export_equipment.py` (Blender 5.2, headless) rather than edited
as models, so a corrected drop can be re-run from scratch. See `Tools/blender/README.md`
for the full order.

Two things about that drop are worth knowing before re-running it:

- 20 of the 27 generators contain JSON booleans (`"emissive": true`) in Python source and
  raise `NameError` on import. The exporter binds `true`/`false` in the exec namespace
  instead of patching vendor files, so the packs run unmodified.
- Three atlases arrived truncated inside the zip (`CompactCarbine`, `TraumaKit`,
  `LightHunterVest`) — no `IEND` chunk, so no loader will open them. They were repaired by
  inflating the surviving scanlines and repeating the last good row; 69–88% of each is
  genuine. Replace them if a clean drop ever arrives.

Each asset ships one albedo atlas with each part's cell baked into its UVs, so one Unity
material covers a whole item. Items with lenses or indicator lamps get a second, emissive
material sampling the same atlas. `Tools > Transity > Render Equipment Contact Sheet` draws
all 27 into one image — an atlas pipeline fails silently, and that sheet is the cheapest way
to see that a drop landed.

## Git

Git LFS tracks source art, audio, video, fonts and binaries (see `.gitattributes`). Unity
YAML stays as text so diffs are reviewable. To use Unity's merge tool for scenes and prefabs,
register the smart merge driver once per machine:

```bash
git config merge.unityyamlmerge.driver '"C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p %O %B %A %A'
```

Meta files are visible and serialization is forced to text (already set in
`ProjectSettings/EditorSettings.asset`).

## The 12-week vertical slice

| Weeks | Deliverable | State |
| --- | --- | --- |
| 1 | Setup, Git, packages, folders, blockout train and forest | **done** |
| 2–3 | Movement, interaction, equipment pickup/drop, inventory | **done** |
| 4–5 | Host/join by code, 4-player spawning, synced interactions and scene loading | **done** |
| 6–7 | First creature AI: roam, hear, stalk, attack, retreat, die | **done** — three creatures on one brain |
| 8–9 | Weapons, traps, tracking clues, health, death | **done** — death is final, no downed state (chosen) |
| 10 | Contract selection, creature proof, extraction, mission results | **done** — four contracts, tagging, capture, debrief |
| 11 | Shop, loadouts, money, saving, equipment-loss rules | partial — wallets, payouts and loss rules exist; saving does not |
| 12 | Visual pass, sound, balancing, optimisation, external playtest | not started |

The slice is finished when a group can create a lobby, buy and equip gear aboard the train,
select a bounty, enter the forest, track and kill the creature, recover proof, extract, get
paid or lose their gear, and start another contract without restarting the game.

## Deliberately not built yet

Driveable train, procedural terrain, public matchmaking, dedicated servers, host migration,
proximity voice chat, multiple biomes, crafting trees, consoles, cross-platform play.

Chosen against, rather than merely unbuilt:

- **No downed-and-revive state.** Death ends your expedition and you spectate the crew.
  It makes the first mistake matter, which is the whole tension of the hunt — but it does
  strand a player who dies early, so watch this one in playtests.
- **Creature models are graybox.** `CreatureBody` builds them from primitives and animates
  them procedurally (gait, banking, head tracking, lunge). Real meshes drop onto the same
  rig later; the behaviour is what needed to exist first.
- **Audio is procedural.** `ProceduralAudio` generates growls, shots and heartbeats as
  waveforms at runtime. It is placeholder, but it means the game has real audio cues to
  design against instead of silence.

Two items are *scoped out of this drop specifically* and belong to their planned weeks:

- **Dropping equipment on disconnect.** Doing it correctly needs the backpack entity from
  week 8–9; NGO despawns a disconnecting client's player object before a clean hook fires,
  so it needs a server-side inventory snapshot rather than a despawn callback.
- **Cinemachine.** The package is installed for later use (creature cameras, shake, cutscenes)
  but the first-person camera is a plain transform under the head pivot, which is simpler
  and has fewer moving parts while the controller is still changing.
