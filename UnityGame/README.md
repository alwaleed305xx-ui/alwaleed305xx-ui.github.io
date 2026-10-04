# SCREAMER

A 4-8 player online horror comedy where the tutorial for staying alive is "please stop screaming" - and nobody, NOBODY, stops screaming.

One random player is the **MONSTER**. Everyone else is a **SURVIVOR** completing six of the loudest chores ever designed inside a haunted house that narcs on them. Every task makes noise. The monster hears everything. Every death is the victim's fault, which is why every death is funny, which is why every death is a clip.

- **Engine:** Unity 2022.3 LTS (2022.3.45f1)
- **Networking:** Netcode for GameObjects 1.12 (UnityTransport by default, Steam lobbies behind an optional define)
- **UI:** uGUI only. **Rendering:** Built-in Render Pipeline. No extra packages.
- **Platform:** Windows x64, with full offline/LAN play and bot-filled solo rounds.

---

## Quick Start

1. **Open the project:** Unity Hub > *Add project from disk* > select this `UnityGame` folder. Packages restore automatically.
2. **Build the game:** top menu **Screamer > Build Everything**. One click assembles the entire game from code - the Henderson House, both pawn prefabs, all six task stations, the escape cellar door, networking, bots, the procedural audio bank, the feel/killcam systems and every UI screen - then saves `Assets/Screamer/Scenes/Game.unity` and registers it in the build settings.
3. **Play:** press Play > **PLAY** > **HOST GAME** > walk around the den, press **[R]** to ready up > **START ROUND**. Bots fill the lobby to 4 players after 10 seconds, so a solo round works immediately. **PRACTICE WITH BOTS** starts an offline round (a bot monster is allowed).
4. **Multiplayer test:** **Screamer > Build Windows x64**, run the built exe as a second client and **JOIN GAME** at `127.0.0.1`. For friends over the internet, forward port 7777 or use any virtual LAN tool - or ship the Steam build (see below).

Everything you see is an art-directed placeholder (warm-tinted primitives with googly eyes). The game is fully playable before any art asset is imported; the drop-in guide below explains how to replace placeholders without touching a single script.

---

## How a Round Works

| Role | What you do |
|---|---|
| **Survivor** | Finish 6 chores. Each completion shatters one padlock on the cellar door. 6/6 opens the finale: gather at the door and scream it open together, then sprint out. |
| **Monster** | You hear everything. Noise pings point you at your dinner. Eat everyone before the chores are done. |
| **Ghost** | Caught players fly around, see every noise ping, and get ONE Boo to betray the living with. |

The three laws: **1. Everything you do is loud. 2. The monster hears everything. 3. See laws 1 and 2.**

### The six chores

1. **SCREAM THERAPY** - mash [Space] to let it all out. The monster also hears your therapy.
2. **KARAOKE NIGHT** - hit the right key (J/K/L). A wrong note files a noise complaint with the monster.
3. **DANCE FLOOR** - mash the arrow keys. The bass is excellent. The bass is also a homing beacon.
4. **INSTANT NOODLES** - stir with [E] every couple of seconds or the smoke alarm tells everyone where you live (and your progress rolls backward).
5. **CATCH THE CHICKEN** - it is in the yard, it is furious, and it screams like you do.
6. **HAUNTED TOILET** - plunge calmly with [E]. Rush it and the pipes go full geyser.

---

## Controls

| Input | Action |
|---|---|
| WASD | Move |
| Mouse | Look |
| Shift | Sprint (sprinting makes noise) |
| Space | Jump / scream during Scream Therapy |
| E | Interact: start tasks, stir, plunge, escape through the open door |
| Q | Abandon the current task |
| F | Slap furniture (survivor) / become furniture (Mimic monster) |
| J / K / L | Karaoke notes |
| Arrow keys | Dance |
| B | Boo (ghost, once per round) |
| R | Ready up in the lobby |
| Left mouse | Monster attack |
| Esc | Pause menu (the monster doesn't pause) |

Mouse sensitivity, invert Y, FOV, volumes, screen shake, quality tier and the colorblind palette live in **SETTINGS** (main menu and pause), persisted between sessions.

---

## Editor Menu Reference

All project tooling lives under the **Screamer** menu:

| Menu item | What it does |
|---|---|
| **Build Everything** | One-click scene assembly (see Quick Start). Safe to re-run; materials and prefabs under `Assets/Screamer/` are reused. |
| **Build Windows x64** | Runs the shipped-string audit, then builds to `Builds/Windows/SCREAMER.exe` with the Steam define stripped. |
| **Build Windows x64 (Steam)** | Runs the audit, toggles `SCREAMER_STEAM` on for the build only, builds to `Builds/WindowsSteam/SCREAMER.exe`, then restores your project defines. Requires the Steamworks SDK (see `Docs/STEAM.md`). |
| **Audit Shipped Strings** | The release gate, runnable on demand: FAILS on any non-Latin character in shipped scripts or scenes and on references to retired legacy text helpers; WARNS on `Debug.Log` calls left in shipped code. Both build items refuse to build while the audit fails. |

---

## Steam

The Steam layer (lobbies, P2P transport, friend invites, rich presence, achievements) is strictly conditional: every line sits behind `#if SCREAMER_STEAM`, and the project compiles and runs green without the define or the SDK. At runtime `BackendSelector` picks the Steam backend only when the define is present *and* `SteamAPI.Init()` succeeds; otherwise the game falls back to direct IP over UnityTransport.

Full walkthrough - SDK import, define enablement, the steamcmd depot upload, and the release test matrix - in [`Docs/STEAM.md`](Docs/STEAM.md).

---

## Replacing Placeholders With Real Assets

The placeholder contract: art drops onto **named anchors** and **named skin slots**; triggers, spawns and scripts never move.

### Map anchors

The scene contains a `MapAnchors` object with one named empty Transform per future asset. Parent your meshes under the matching anchor and delete or hide the primitive stand-in nearby:

```
FireplaceAnchor, CouchAnchor, ArmchairAnchor1, ArmchairAnchor2, TVAnchor,
KaraokeAnchor, DancePadAnchor, StoveAnchor, ToiletAnchor, BedAnchor,
GarageJunkAnchor1-3, YardShedAnchor, YardFenceDressingAnchor1-4, CellarDoorAnchor
```

Recommended interior pack: **Vintage Living Room 3D Game Pack** (the haunted-sitcom look). Exterior silhouette packs such as **lowpoly medieval buildings** or **The Wasteland LITE** belong *beyond the fence only* - never lit, never visited.

### Monster skins

Open `Assets/Screamer/Prefabs/Monster.prefab`. Under `Skins` you will find three children - keep these exact names and drop the real models inside them, keeping the code-built rim lights:

| Skin slot | Drop-in model |
|---|---|
| `Skins/Skin_Zombie` | FREE Zombie Male AAB |
| `Skins/Skin_Mutant` | Morbid Creatures: Mutant |
| `Skins/Skin_Mimic` | Mimic prototype |

`MonsterSkinSelector` picks one at random each round; no wiring changes needed as long as the three children stay in place.

### Mimic furniture whitelist

The Mimic can only disguise as props listed in `MimicPropCatalog` (v1: Couch, Armchair, FloorLamp, TVStand). When real furniture lands, add props to the catalog **one by one after hand-checking colliders and pivots** - never "any mesh".

### Audio and fonts

- Every sound routes through `AudioDirector`'s single clip table; replace the procedural bank with real clips in that one place.
- Fonts have two drop-in paths, both handled by `ScreamerUIStyle.UiFont()`:
  - **Editor sessions:** drop any OFL-licensed display font `.ttf` into `Assets/Screamer/Fonts/` and the whole UI picks it up automatically (this path uses `AssetDatabase` and works in the editor only).
  - **Built players:** the editor-only scan does not ship, so the build falls back to `Resources.Load<Font>("ScreamerFont")`. To ship a custom font, place it in any `Resources` folder named exactly `ScreamerFont` (e.g. `Assets/Screamer/Resources/ScreamerFont.ttf`). Without it, builds use OS Arial.

### Survivor model

Replace the capsule (keep the googly eyes as long as you can bear to) inside `Assets/Screamer/Prefabs/Survivor.prefab`; keep the `CameraHolder` child where it is.

---

## Tuning

Balance lives in public Inspector fields with binding defaults from the design doc (`Docs/GDD.md`, section 4): movement speeds (survivor walk 4.5 / sprint 7.5, monster 7.8-8.6 by skin), the 10 s monster lockdown, task durations and loudness values (`noiseLoudness` 0-1 maps to a hearing radius of `8 + loudness * 52` units), and the 15 s results auto-rematch timer.

## Project Layout

```
Assets/Scripts/Core/     Round flow, roster, stats, noise relay, bootstrap
Assets/Scripts/Net/      INetworkBackend seam: UnityTransport + optional Steam
Assets/Scripts/Player/   Survivor, ghost + Boo, character factory
Assets/Scripts/Monster/  Monster, skins, the Mimic economy
Assets/Scripts/Tasks/    TaskBase, the six chores, escape door + finale
Assets/Scripts/Bots/     Waypoint graph, survivor/monster bots, personalities
Assets/Scripts/World/    Palette, house layout + factory, lighting, screen FX
Assets/Scripts/Feel/     Camera juice, particles, morph FX, killcam
Assets/Scripts/Audio/    Procedural audio bank + director
Assets/Scripts/UI/       Every uGUI screen, settings, the copy catalog
Assets/Editor/           Setup wizard, build pipeline, string audit
Docs/                    GDD.md (design), PLAN.md (architecture), STEAM.md
```

New chores are one `TaskBase` subclass each - see any task in `Assets/Scripts/Tasks/` for the pattern.

Have fun. Try to stay quiet. You won't.
