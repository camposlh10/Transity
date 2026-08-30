# Transity

First-person, 1–4 player co-op creature hunting. A train is the hub and the extraction
point; expeditions run 20–30 minutes into the forest and back.

This repository currently holds the **week 1–5 foundation** of the 12-week vertical slice:
project setup, movement, a reusable interaction system, host/join by code, and synchronised
scene transitions between the train and the forest.

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
| `Shift` / `Ctrl` | Sprint, crouch |
| `Space` | Jump |
| `E` | Interact |
| `Q` / `Tab` | Previous / next inventory slot |
| `G` | Drop selected item |
| `Esc` | Release the cursor (essential with several editor instances) |

## Project layout

```
Assets/_Game/
├── Art/            Materials, models, textures        (blockout materials are generated)
├── Audio/
├── Data/           ScriptableObject definitions
│   ├── Items/      ItemDefinition assets + ItemRegistry
│   ├── Network/    NetworkPrefabsList
│   ├── Creatures/  (week 6–7)
│   ├── Contracts/  (week 10)
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
│   ├── Train/      DepartureLever
│   ├── UI/         MainMenuUI, HudUI
│   ├── Combat/     (week 8–9)
│   ├── Creatures/  (week 6–7)
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

> Item ids cross the wire as FNV-1a hashes of the id string, not as `string.GetHashCode`,
> which is not stable between processes. Renaming an `itemId` changes its network id and
> invalidates saved loadouts — `ItemIdentityTests` pins the hash so this cannot happen
> silently.

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
| 6–7 | First creature AI: roam, hear, stalk, attack, retreat, die | not started |
| 8–9 | Weapons, traps, tracking clues, health, downed state, dropped backpack | not started |
| 10 | Contract selection, creature proof, extraction, mission results | partial — extraction and phase machine exist, contracts do not |
| 11 | Shop, loadouts, money, saving, equipment-loss rules | not started |
| 12 | Visual pass, sound, balancing, optimisation, external playtest | not started |

The slice is finished when a group can create a lobby, buy and equip gear aboard the train,
select a bounty, enter the forest, track and kill the creature, recover proof, extract, get
paid or lose their gear, and start another contract without restarting the game.

## Deliberately not built yet

Driveable train, procedural terrain, public matchmaking, dedicated servers, host migration,
proximity voice chat, character customisation, multiple biomes, more than one creature,
crafting trees, consoles, cross-platform play.

Two items are *scoped out of this drop specifically* and belong to their planned weeks:

- **Dropping equipment on disconnect.** Doing it correctly needs the backpack entity from
  week 8–9; NGO despawns a disconnecting client's player object before a clean hook fires,
  so it needs a server-side inventory snapshot rather than a despawn callback.
- **Cinemachine.** The package is installed for later use (creature cameras, shake, cutscenes)
  but the first-person camera is a plain transform under the head pivot, which is simpler
  and has fewer moving parts while the controller is still changing.
