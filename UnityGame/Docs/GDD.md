# SCREAMER — Game Design Document (Final, Steam Release)

**Version 1.0 — binding for implementation. Zero open design decisions.**
**Engine:** Unity 2022.3 LTS (2022.3.45f1) · C# 9 · Netcode for GameObjects 1.12 · uGUI only (no TextMeshPro) · Built-in Render Pipeline (no new packages)
**Platform:** Windows x64, Steam (conditional layer) with full offline/LAN fallback

---

## 0. The One-Liner

> **SCREAMER is a 4-8 player horror comedy where the tutorial for staying alive is "please stop screaming" — and nobody, NOBODY, stops screaming.**

One random player is the **MONSTER**. Everyone else is a **SURVIVOR** completing six of the loudest chores ever designed inside a haunted house that narcs on them. Every task makes noise. The monster hears everything. The game never kills you — *you* call the monster over, by karaoke-ing off key, burning instant noodles, or chasing a chicken with the lung capacity of an air-raid siren. Every death is the victim's fault, which is why every death is funny, which is why every death is a clip.

**Identity thesis: "A SITCOM SET THAT BITES."** The world is warm, cozy, and lovingly lit like a 70s family den — which makes the Monster reveal land harder and keeps the comedy register dominant. Environment = warm and saturated. Danger = one screaming accent color that exists nowhere else in the game.

**Grafted strengths from the rejected directions** (adopted as binding design, detailed in their sections below):

| Grafted idea | Origin | Where it lands here |
|---|---|---|
| Silence after a kill — the death scream produces NO monster ping | retro-vhs | §9.8 |
| Staged killcam cinematic as plan-of-record (no risky frame capture) | retro-vhs | §12.2 |
| Bot personality parameters (one tunable per bot archetype) | retro-vhs | §10 |
| Task progress bar doubles as a "recording level" noise meter | retro-vhs | §7.3 |
| The house is a snitch for everyone — lighting as a second info channel | cinematic-haunt | §6.4 |
| Art-directed primitives + named anchors placeholder contract | cinematic-haunt | §13 |
| Rematch-without-restart refactor scheduled FIRST | cinematic-haunt (judge) | §14 |

---

## 1. Product Pillars (in priority order)

1. **Self-incrimination comedy.** Every system routes danger through the player's own noise. No random deaths, ever.
2. **Readable at 480p.** Streams and clips must read at thumbnail size. No fog soup, no pitch-black screens, Monster Red reserved for threat only.
3. **Every round ends with a shareable artifact.** Killcam replay + superlative awards + photo-finish slow-mo are the marketing department.
4. **A full round with zero friends.** Bots fill every lobby; the solo Steam reviewer at 2 AM gets a real, funny round.
5. **One-click buildable.** The entire game — map, prefabs, all six UI screens, audio bank — is built from code by the editor wizard. The project runs before any art asset is imported.

**Language mandate:** ALL player-facing text, code comments, identifiers, docs, and store copy are in sharp American-indie English. `ArabicText.cs` is deleted along with its call sites. A build-pipeline check (§14.4) fails any build containing a non-ASCII-Latin string in shipped code. No Arabic characters anywhere in the shipped product, repo, or store page.

**Rebrand mandate (step zero of implementation):**
- `SayehSetupWizard` → `ScreamerSetupWizard` (file + class), editor menu `Sayeh/...` → `Screamer/Build Everything`
- `SayehBootstrap` → `ScreamerBootstrap`
- `Assets/Sayeh/` → `Assets/Screamer/`
- `Assets/Scripts/UI/ArabicText.cs` deleted; `GameUI.Set()` writes `t.text = s` directly.
- README.md rewritten in English.

---

## 2. Color System — `ScreamerPalette` (static class, single source of truth)

All code-built materials, lights, and UI reference these constants. **Pure white (#FFFFFF) and pure black (#000000) are forbidden everywhere.**

| Constant | Hex | Role (exhaustive — nothing else may use it) |
|---|---|---|
| `LamplightAmber` | `#FFB84D` | Hero light color: den practicals, task-available glow, cozy interior key lights, task-complete house pulse |
| `ShagRust` | `#C4502E` | Floors, furniture accents, secondary UI accents, hover states on destructive-ish buttons |
| `HauntedTeal` | `#1E6E6E` | Ambient light tint, ALL shadows (never gray), yard moonlight, dread desaturation target |
| `MidnightPlum` | `#2B1B3D` | Deepest darks: skybox horizon, hallway, corridor ends, letterbox bars |
| `ScreamYellow` | `#FFE234` | Survivor identity: progress fills, gag popup text, READY pulses, title wordmark |
| `MonsterRed` | `#FF2E4C` | **EXCLUSIVELY the Monster**: its vision vignette, noise pings, kill feed lines, monster UI labels, smoke-morph burst. Nothing else. Red in peripheral vision ALWAYS means death. |
| `GhostMint` | `#7DFFD4` | Spectator ghosts (40% alpha additive), escape-door-unlocked state, ghost HUD tint |
| `NoodleCream` | `#FFF3DC` | All UI panels and text plates; default body text color |
| `InkBlack` | `#141019` | UI text on cream plates, drop shadows (never #000) |

**Survivor player colors** (assigned in join order, used for name tags, rim lights, dance notes, karaoke particles — colorblind-safe at distance):
`#FF8A3D` orange, `#4DA6FF` blue, `#FFD23F` gold, `#B86BFF` violet, `#3DE1AD` jade, `#FF6BB5` pink, `#9BCB3C` lime (bots take the tail of the list).

**Per-frame grading rule:** ~70% warm environment (amber/rust/cream), ~20% teal/plum shadow, ~10% maximum of yellow/red/mint punch. Red appears only when the Monster is relevant.

---

## 3. The Map — "The Henderson House"

One continuous sitcom set. Floor at y=0 (floor slab top). Walls 4 units high, 0.5 thick (use 1.0-thick cubes where the current wizard does; coordinates below are binding). All built by `ScreamerSetupWizard.BuildMap()` from primitives, with a `MapAnchors` object exposing one named empty Transform per future asset (§13).

### 3.1 Footprint

- **House interior:** X ∈ [-20, +20], Z ∈ [-20, +12] (40 × 32)
- **Backyard:** X ∈ [-14, +14], Z ∈ [+12, +28] (28 × 16), enclosed by a 2.2-high fence
- Floor slabs: `Floor_House` center (0, -0.25, -4) scale (41, 0.5, 33); `Floor_Yard` center (0, -0.25, 20) scale (29, 0.5, 17), yard floor material darker (ShagRust × 0.4).

### 3.2 Rooms

| Room | Bounds (X, Z) | Purpose | Key light |
|---|---|---|---|
| **THE DEN** (hub + lobby) | X [-12, +12], Z [-4, +12] | Couch, fireplace, TV; Karaoke + Dance Floor tasks; survivor spawns | Fireplace amber flicker + TV static blue |
| **THE KITCHEN** | X [-20, -12], Z [-4, +12] | Instant Noodles task | NoodleCream over-stove practical |
| **THE BATHROOM** | X [+12, +20], Z [+2, +12] | Haunted Toilet task | Sickly green-teal fluorescent, rhythm-linked flicker |
| **THE UTILITY NOOK** | X [+12, +20], Z [-4, +2] | Decor/nav pocket; Mimic ambush spot | None — doorway spill only |
| **THE HALLWAY** | X [-20, +20], Z [-8, -4] | Full-width dark spine; the "risk tax" between zones | None — doorway spill only; darkest interior |
| **THE BEDROOM** | X [-20, -4], Z [-20, -8] | Contains THE SCREAMING CLOSET (Scream Therapy) | One weak bedside amber lamp |
| **THE GARAGE** | X [-4, +20], Z [-20, -8] | Monster spawn/lair; junk clutter | One bare swinging bulb (amber × 0.5) |
| **THE BACKYARD** | X [-14, +14], Z [+12, +28] | Catch the Chicken; ESCAPE CELLAR DOOR | Teal moonlight + warm window glow from house |

### 3.3 Wall segments (binding; gap = no cube; all doorway gaps 2.5 wide, full height)

Outer walls (house): south (0, 2, -20.5) scale (41, 4, 1); west (-20.5, 2, -4) scale (1, 4, 34); east (+20.5, 2, -4) scale (1, 4, 34); north split by the back door gap at X=0 (gap spans X [-1.25, +1.25]): `Wall_N_L` (-10.875, 2, 12.5) scale (19.25, 4, 1), `Wall_N_R` (+10.875, 2, 12.5) scale (19.25, 4, 1).

Interior walls:
- **Kitchen|Den** at X=-12, Z [-4, 12]: gap at Z=4 (spans Z [2.75, 5.25]) → `W_KD_S` (-12, 2, -0.625) scale (1, 4, 6.75), `W_KD_N` (-12, 2, 8.625) scale (1, 4, 6.75)
- **Bathroom|Den** at X=+12, Z [+2, +12]: gap at Z=7 (spans Z [5.75, 8.25]) → `W_BD_S` (12, 2, 3.875) scale (1, 4, 3.75), `W_BD_N` (12, 2, 10.125) scale (1, 4, 3.75)
- **Utility|Den** at X=+12, Z [-4, +2]: gap at Z=-1 → `W_UD_S` (12, 2, -3.125) scale (1, 4, 1.75), `W_UD_N` (12, 2, 1.125) scale (1, 4, 1.75)
- **Bathroom|Utility** at Z=+2, X [+12, +20]: solid `W_BU` (16, 2, 2) scale (8, 4, 1) — bathroom reachable only from the den (claustrophobia by design)
- **Hallway|Den+Kitchen** at Z=-4, X [-20, +20]: gaps at X=0 (den door, spans X [-1.25, 1.25]) and X=-16 (kitchen service door, spans X [-17.25, -14.75]) → `W_H1` (-18.625, 2, -4) scale (2.75, 4, 1), `W_H2` (-8.0, 2, -4) scale (13.5, 4, 1), `W_H3` (+10.625, 2, -4) scale (18.75, 4, 1)
- **Hallway|Bedroom+Garage** at Z=-8, X [-20, +20]: gaps at X=-12 (bedroom door, spans X [-13.25, -10.75]) and X=+8 (garage door, spans X [6.75, 9.25]) → `W_H4` (-16.625, 2, -8) scale (6.75, 4, 1), `W_H5` (-2.0, 2, -8) scale (17.5, 4, 1), `W_H6` (+14.625, 2, -8) scale (10.75, 4, 1)
- **Bedroom|Garage** at X=-4, Z [-20, -8]: solid `W_BG` (-4, 2, -14) scale (1, 4, 12)
- **Screaming Closet** (inside bedroom): 3-sided box, opening faces east. Walls: (-18, 2, -11.5) scale (4, 4, 0.5); (-18, 2, -16.5) scale (4, 4, 0.5); (-19.75, 2, -14) scale (0.5, 4, 5.5). Door sign on the opening (§11, "THE SCREAMING CLOSET").
- **Yard fence** (2.2 high, 0.4 thick): west (-14.2, 1.1, 20) scale (0.4, 2.2, 17); east (+14.2, 1.1, 20) scale (0.4, 2.2, 17); north split by cellar door gap (3.5 wide, spans X [-1.75, +1.75]) at X=0: `Fence_N_L` (-7.875, 1.1, 28.2) scale (12.25, 2.2, 0.4), `Fence_N_R` (+7.875, 1.1, 28.2) scale (12.25, 2.2, 0.4)

### 3.4 Task stations, spawns, escape door (exact positions)

| Object | Position | Notes |
|---|---|---|
| Scream Therapy station | (-18.0, 0.5, -14.0) | Inside the Screaming Closet; trigger cube 1×1×1 |
| Karaoke machine | (-9.0, 0.5, +9.0) | Den NW corner; toy karaoke box, magenta practical above (§6.2) |
| Dance Floor pad | (+7.0, 0.05, +7.5) | Den NE; 4×4 flat pad of 16 one-unit tiles (code-tinted, beat-cycled) |
| Noodle stove | (-16.0, 0.9, +8.0) | Kitchen north wall; pot = scaled sphere on cube stove |
| Haunted Toilet | (+16.5, 0.5, +9.5) | Bathroom back wall |
| Chicken (start) | (0, 0.4, +20.0) | Backyard; leash center = start, max wander radius 11 |
| Escape cellar door | (0, 0.6, +27.5) | Double door, 3.5 wide, angled 40° like storm-cellar doors; SIX oversized padlock props across it at X = -1.25, -0.75, -0.25, +0.25, +0.75, +1.25, y=1.0 |
| Survivor spawns ×8 | (-2, 0.1, 2), (2, 0.1, 2), (-4, 0.1, 5), (4, 0.1, 5), (0, 0.1, 7), (-2, 0.1, 9), (2, 0.1, 9), (0, 0.1, 4) | Around the den couch — the lobby IS the den (§8.2) |
| Monster spawn | (+10.0, 0.1, -16.0) | Garage center — two doorways from anywhere survivors are |
| Couch | (0, 0.4, 2.5) facing +Z | scale (4, 0.8, 1.4); lobby seating + Mimic disguise prop |
| Fireplace | (0, 0.8, 11.4) | In north den wall face; emissive amber quad + point light |
| TV | (-6, 1.0, 11.4) | Emissive blue-gray quad, flicker script |

Monster-to-nearest-survivor-spawn path length ≥ 26 units; with the 10 s lockdown (§5) survivors always get a real head start.

---

## 4. Balance Numbers (binding; all live as public fields with these defaults)

### 4.1 Movement

| Parameter | Value |
|---|---|
| Survivor walk speed | 4.5 |
| Survivor sprint speed | 7.5 |
| Survivor jump force | 6.0; gravity -18 |
| Monster move speed (Zombie skin) | 7.8 |
| Monster move speed (Mutant skin) | 8.6 |
| Monster move speed (Mimic skin) | 8.2 |
| Monster finale speed bonus (§9.9) | +25% for 8 s |
| Ghost fly speed | 8.0 |
| Mouse sensitivity default | 2.2 (settings range 0.5–5.0) |
| Monster lockdown at round start | 10 s (server-time synced, existing `ServerFreeze`) |
| Monster attack range / cooldown | 2.4 / 1.2 s |
| Monster attack is a camera-forward raycast | unchanged from current `MonsterController.Attack()` |

### 4.2 Noise → hearing model

Noise events carry `loudness` ∈ [0,1]. **Hearing radius = 8 + loudness × 52 units** (max 60 covers the whole map). The server culls pings outside the radius before relaying to the monster client — change `NoiseClientRpc` to a targeted send: compute distance monster↔noise on the server; only deliver if within radius. Ping display time = `Lerp(1.5, 5.0, loudness)` (keep existing). Monster-side ping ring size = `Lerp(40, 160, loudness)` px.

| Noise source | Loudness | Label shown to monster (§11.6) |
|---|---|---|
| Sprint footsteps (every 2.5 s while sprinting) | 0.30 | `RUNNING. SOMEWHERE.` |
| Walking, crouching, standing | 0.00 (silent) | — |
| Scream Therapy, each scream | 1.00 | `HYSTERICAL SCREAMING` |
| Karaoke ambient (every 2.0 s during task) | 0.60 | `SOMEONE'S SINGING. BADLY.` |
| Karaoke wrong note (instant) | 0.90 | `THAT WAS NOT A NOTE.` |
| Dance Floor ambient (every 1.0 s) | 0.70 | `UNLICENSED DISCO` |
| Noodles ambient (every 3.0 s) | 0.30 | `SOMETHING'S COOKING` |
| Noodles BURN (instant) | 1.00 | `SMOKE ALARM. BLESS THEM.` |
| Chicken squawk (every 1.2 s while fleeing) | 0.80 | `THE CHICKEN. OBVIOUSLY.` |
| Toilet ambient (every 2.5 s) | 0.40 | `SUSPICIOUS PLUMBING` |
| Toilet geyser (instant) | 1.00 | `THE PIPES HAVE OPINIONS.` |
| Furniture slap ([F], §9.4) | 0.45 | `SOMEONE'S HITTING THE FURNITURE` |
| Ghost Boo (§9.7) | 0.70 | `A SCREAM? A FAKE? WHO KNOWS.` |
| Finale group scream (§9.9) | 1.00 map-wide | `THEY'RE AT THE DOOR. GO. NOW.` |

### 4.3 Tasks (durations = seconds of progress at 1× multiplier; keep existing multiplier logic)

| Task | Duration | Interaction | Fail condition | Fail penalty |
|---|---|---|---|---|
| SCREAM THERAPY | progress = screams, 10 needed | Mash [Space] | none | none — the screams ARE the cost |
| KARAOKE NIGHT | 8.0 | Press shown key (J/K/L), correct = 3× progress, idle = 0.5× | Wrong key | loudness-0.9 ping, progress multiplier 0 that frame |
| DANCE FLOOR | 7.0 | Mash arrow keys, dancing = 2.5×, idle = 0.2× | none | — |
| INSTANT NOODLES | 9.0 | Press [E] to stir, within every 2.0 s window | Missed stir window | loudness-1.0 ping + **progress rolls back at −1×/s for 2 s** (visible rewind) |
| CATCH THE CHICKEN | 4.0 (at the chicken) | Chase; [E] within 2.0 of chicken | none | chicken flees at 5.0 speed within 6.0 radius, wander-leash 11 from start |
| HAUNTED TOILET | 8.0 | [E] in calm rhythm; gap ≥ 0.5 s = 5× progress | [E] pressed < 0.5 s after previous | loudness-1.0 geyser, multiplier 0 |
| (all) abandon | — | [Q] cancels, releases player | — | — |

### 4.4 Round / meta

| Parameter | Value |
|---|---|
| Players | 2–8 humans; bots auto-fill to **4 minimum** after 10 s in lobby |
| Role split | 1 monster (random among non-bot clients if ≥2 humans; else any) |
| Countdown | 5 s full-screen numbers |
| Tasks to unlock door | 6 (= all stations) |
| Finale group scream | all living survivors within 4.0 of cellar door, combined mash fills a 3.0 s meter |
| Monster dread radius (drone + house tells) | 15 |
| Mimic re-disguise | unseen by any survivor for 3 s; disguise breaks on move/attack |
| Furniture slap range | 2.0; slap cooldown 0.8 s |
| Ghost Boo | 1 per ghost per round, range: any living player, targeted via look-raycast ≤ 20 |
| Results auto-rematch timer | 15 s (visible ring) |
| Interact range | 3.0 (existing raycast) |

---

## 5. Round Flow (the Steam loop — no app restarts, ever)

`GameState` (NetworkVariable, server-authoritative) becomes: `Lobby → Countdown → Lockdown → Playing → Finale → Results → Lobby`.

1. **LOBBY** — Players spawn as survivor-model pawns in the den (walkable, no tasks active). Ready-up via [R] or the READY button; readied characters raise a hand and freeze in a goofy pose (diegetic ready state). Bots fill to 4 after 10 s. Host gets START ROUND when all humans ready.
2. **COUNTDOWN (5 s)** — Full-screen numbers (§9.1); on 0, den lights slam off for 0.4 s, the chosen player's pawn bursts into MonsterRed smoke and morphs into the monster prefab **in full public view** (the role reveal is a public event). Privately, the monster saw "IT'S YOU." 1 s earlier.
3. **LOCKDOWN (10 s)** — Monster teleported to the garage, frozen (existing `ServerFreeze`), countdown "RELEASED IN 0:07" on its HUD. Survivors scatter.
4. **PLAYING** — 6 tasks → each completion shatters one padlock (diegetic counter) → all 6 done triggers FINALE state.
5. **FINALE** — Cellar door floods GhostMint; all living survivors must gather at the door and mash-scream together for 3 s ("scream-powered hydraulics"); monster gets a map-wide mega-ping + 25% speed for 8 s. Door blows open; survivors sprint out. Photo-finish within 2 s margin triggers 1.2 s of 0.5× slow-mo on all screens (server toggles, clients lerp `Time.timeScale` — cosmetic only, server logic unaffected).
6. **RESULTS** — Winners podium + killcam + superlatives (§8.6). REMATCH (majority vote or 15 s timer) returns everyone to LOBBY.
7. **REMATCH RESET (the mandatory refactor, scheduled first — §14)** — Server: despawn all player NetworkObjects, clear `caught`/`escaped`, reset `MonsterClientId` to `ulong.MaxValue`, reset every `TaskBase.done`, re-lock the door, restore padlocks and all lighting states, clear round stats, set `State = Lobby`, respawn lobby pawns. No scene reload, no app restart.

**Win conditions** (existing logic kept): monster wins when every survivor is caught; survivors win when ≥1 escapes and all are resolved. **Monster disconnect** = survivors win ("the coward clause", existing `Update()` check, copy in §11.5). Dead players → flying ghosts (existing), now with the Boo (§9.7).

---

## 6. Lighting — warm rooms, teal shadows, red = death

### 6.1 Globals (evolve existing `SetupLighting()`)

- Render pipeline: **Built-in** (manifest untouched). The wizard's `LitShader()` fallback chain stays, so the project survives a future URP migration without code changes.
- Ambient: Flat, `HauntedTeal × 0.35` → RGB (0.041, 0.151, 0.151).
- Fog: Exponential, density **0.012** interior-tuned, color `MidnightPlum × 0.6`. (Legibility beats atmosphere — streams must read at 480p.)
- Directional "moon": color `#7E8FC4`, intensity 0.35, rotation (55, -30, 0), soft shadows, shadow distance 50.
- Skybox: solid-color camera background `MidnightPlum` (no skybox asset).
- Per-pixel light budget: ≤ 2 real-time point/spot lights per room + moon.

### 6.2 Per-room practicals (all built by the wizard; flicker = scripted Perlin intensity)

| Room | Light | Color / intensity / range | Behavior |
|---|---|---|---|
| Den | Fireplace point @ (0, 1.2, 10.8) | `LamplightAmber`, 1.4, range 12 | Perlin flicker 0.8–1.2×; **dims 35% when monster within 15 of the den center** (house tell) |
| Den | TV area light (emissive quad only) | blue-gray `#AFC4D8` | 2-frame random static flicker; silhouettes anyone crossing it |
| Den | Karaoke practical @ (-9, 3.2, 9) | magenta `#C44FD0`, 0.9, range 6 — **the only palette exception, diegetic toy-neon** | Strobes `MonsterRed` 0.15 s on each wrong note |
| Kitchen | Over-stove point @ (-16, 3.4, 8) | `NoodleCream`, 1.1, range 7 | Steady; strobes `MonsterRed` at 2 Hz for 3 s on noodle burn |
| Bathroom | Fluorescent point @ (16, 3.6, 9) | green-teal `#9FD8B8`, 1.0, range 6 | Flicker frequency scales with toilet-rhythm degradation (diegetic performance feedback) |
| Bedroom | Bedside lamp @ (-10, 1.2, -14) | `LamplightAmber × 0.6`, 0.7, range 6 | Steady, weak |
| Garage | Bare bulb @ (8, 3.6, -14) | `LamplightAmber × 0.5`, 0.8, range 8 | Scripted pendulum swing ±15°, period 3 s (moving shadows for free dread) |
| Hallway | **No lights** | — | Doorway spill only — crossing darkness between amber doorframes is the game's visual thesis (and the capsule art) |
| Yard | Moon (global) + 4 emissive "window glow" quads on the house's north face | windows `LamplightAmber`, emissive only | Cozy = safety, visible from danger |
| Escape door | Point @ (0, 1.6, 27) | `MonsterRed`, 0.8, range 5 while locked → swaps to `GhostMint` on finale | Power-surge double-flicker on unlock |

### 6.3 Event lighting (global, scripted)

- **Monster morph (countdown 0):** all practicals brown-out to 20% for 0.4 s.
- **Each task completion:** house-wide amber pulse, +25% intensity for 0.6 s with ease-out (hope made visible) + one padlock shatters.
- **Final (6th) task:** all lights surge 20% for 1 s; cellar door floods GhostMint.
- **Loud noise (loudness ≥ 0.8):** every practical within 12 of the source flinches (-30% for 0.2 s) — a distant scream is also a lighting event you feel.

### 6.4 The house is a snitch — lighting as the second information channel (graft)

When the monster is within **15 units** of a survivor: that room's practical desaturates 15% toward teal and (den only) the fireplace dims. No UI, no text — players learn the house's tells, streamers narrate them, reviewers call it smart. Implemented as one `HouseTells` component polling monster distance per room anchor at 4 Hz, lerping light color/intensity.

### 6.5 Post stack (safe, package-free; URP note)

No post-processing package. The look is achieved with:
1. **Palette discipline + lights** (the grade is the lighting).
2. **A single code-built full-screen overlay canvas** (`ScreenFxOverlay`, sort order 5000, built by the wizard): vignette sprite (procedural radial-gradient `Texture2D`, alpha 0.25 normal / 0.45 + MonsterRed tint on monster screens), film-grain `RawImage` (64×64 procedural noise texture, UV-scrolled, alpha 0.08), flash/tint layer for event flashes, letterbox bars for the killcam. Settings slider "Screen Effects 0–100%" scales the whole stack; Low quality disables grain.
3. **If URP is ever added later** (explicitly out of scope for v1): the wizard's `LitShader()` already resolves URP/Lit first, the three quality tiers (§8.5) map to three URP assets, and `ScreenFxOverlay` keeps working unchanged. No other system may depend on render pipeline.

---

## 7. uGUI Screen Specs (every screen, element by element)

**Global UI language** (`ScreamerUIStyle` static factory the wizard uses; evolve the existing `Panel/Txt/Btn` helpers):
- Panels: `NoodleCream` Image, alpha 0.96, plus a hard 4 px `InkBlack` drop-shadow Image offset (+4, -4) behind it (two stacked Images — no shadow components). Corners square (no 9-slice dependency); a 2 px `ShagRust` border via a slightly larger backing Image.
- Font: `Arial.ttf` via `Font.CreateDynamicFontFromOSFont("Arial", size)` everywhere, bold for headers. Headers ALL CAPS via code. (If a single OFL display font .ttf is later dropped into `Assets/Screamer/Fonts/`, `ScreamerUIStyle.UiFont()` prefers it — one function, zero other changes.)
- Text: `InkBlack` on cream plates; `NoodleCream` on dark backdrops; `ScreamYellow` for gags; `MonsterRed` only for monster/danger.
- Buttons: cream plate, label flips to `ShagRust` on hover with a 0.1 s squash (scale y 0.92) and 1.06× pop + click-thunk SFX on press. All panels animate in over 0.15 s with back-ease overshoot (scale 0.9→1.02→1.0, coroutine).
- Reference resolution 1920×1080, `CanvasScaler.ScaleWithScreenSize`, match 0.5.

### 7.1 MAIN MENU (`MenuUI`, scene: same Game scene, camera on a menu dolly)

- Backdrop: live 3D den at dusk; camera dollies on a 20 s loop between (−8, 2.2, −1) and (6, 2.0, 3), looking at the fireplace. The couch-disguised Mimic prop **twitches for 2 frames every 25–40 s** (menu easter egg, first clip bait).
- Title: "SCREAMER" — `ScreamYellow`, bold, 140 pt, anchored (0.5, 0.78); the whole title `RectTransform` rattles ±3 px for 0.3 s every ~8 s, synced to a muffled off-screen scream SFX.
- Left-anchored vertical stack (anchor 0.18, 0.45; buttons 340×64, spacing 14):
  1. **PLAY** → sub-panel: HOST GAME / JOIN GAME (Steam friends-list button when `SCREAMER_STEAM`; IP + port fields labeled "TRACKING: MANUAL" in fallback) / PRACTICE WITH BOTS (offline host, bots to 4, bot monster allowed)
  2. **HOW TO DIE** — one-page rules screen: six rows, each = task icon placeholder (colored square) + one-liner (§11.2), plus the three laws at the bottom
  3. **SETTINGS** (§7.5)
  4. **SNEAK AWAY** (quit; confirm dialog "Leave? The chicken will remember this." / YES, FLEE / NO, STAY)
- Bottom-left: `v{version} — made by people who scream at their own game` (18 pt, cream, alpha 0.6).
- Bottom-right: rotating yellow sticky-note (160×120, tilt −4°) with fake patch notes, cycling every 12 s (§11.1).

### 7.2 LOBBY (diegetic — the den IS the lobby; overlay only)

- Players walk around the den as survivor pawns; floating name + player-color tag overhead (existing `Billboard.cs`).
- Right rail (anchor right, 380 wide): roster card — one row per slot: color chip (24×24), name ("Steam persona" or "Victim 3"), ping ms, READY check in `ScreamYellow`. Bot rows: "[BOT] Chad" etc. + a host-only ✕ to remove; host-only "+ ADD BOT" row at the bottom.
- Bottom-center: room code in giant letters (fallback: IP), one-click COPY button; INVITE FRIENDS (Steam overlay; hidden in fallback).
- Bottom-center-above: **READY UP** (becomes host's **START ROUND**, pulsing `ScreamYellow`, when all humans ready).
- Top ticker (VHS-yellow on `InkBlack` strip, 28 px tall): cycling tips (§11.1).
- Countdown: full-screen numbers 5→1, 320 pt, each lands with a 6 px camera thump and dust-puff particle; lights slam to dark on 0 (§5).

### 7.3 HUD — SURVIVOR

- Top-center: **six padlock icons** (procedural: rounded-square Image + shackle arc from two rotated Images, 40×48 each, spacing 8) mirroring the door; each completion shatters one (icon flips to broken sprite state: shackle rotated open + 0.3 s shake + glass-break SFX). Replaces the "3/6" text counter.
- Top edge, under padlocks: event feed, one line, 4 s, typewriter-in at 40 chars/s (§11.5 lines).
- Bottom-center: context prompt plate, visible in range only: `[E] SCREAM THERAPY` etc.
- Bottom-left: **self-noise meter** — horizontal bar 220×18 that fills toward `MonsterRed` with your emitted noise (decays over 1.5 s) + a mouth icon (circle Image, `localScale.y` opens with level). A thin ring around the crosshair blooms outward once per noise event **you** caused — you always know you just snitched on yourself.
- Task mode (docked bottom-third plate, 720×190): task name (36 pt bold), one-line flavor (§11.3), **progress bar styled as a recording-level VU meter** — `ScreamYellow` fill that spikes and clips into `MonsterRed` for 0.2 s every time the task emits a noise ping (graft: teaches the noise system diegetically); rolls visibly backward on noodle burn; hint line in yellow; `[Q] walk away from this` in 16 pt.
- Crosshair: 4 px cream dot.

### 7.4 HUD — MONSTER (swapped, not overlaid)

- `MonsterRed` vignette (overlay §6.5, 0.45) + faint heartbeat pulse (vignette alpha ±0.05 at 1.2 Hz).
- **Noise pings:** replace the center-screen arrow with a screen-edge directional smear: a red soft glow segment on the compass-accurate screen edge toward the noise (reuse existing `SignedAngle` math; render as a rotated arc Image on an edge ring), ping ring size by loudness (§4.2), plus the noise label in `MonsterRed` caps near it, with a 3 s fading ghost-echo so the monster reads noise *history*.
- Lockdown: center countdown `RELEASED IN 0:07` + lockdown copy (§11.4).
- Kill counter: tally marks (Image strokes), top-right.
- Mimic only: bottom-center `[F] BECOME FURNITURE` prompt when eligible; while disguised: `YOU ARE A COUCH. LIVE THE COUCH.` 16 pt, alpha 0.5.

### 7.5 SETTINGS (tabbed cream panel 900×620; tabs: AUDIO / VIDEO / CONTROLS / GAMEPLAY)

- AUDIO: Master / SFX / Music / Ambience sliders (0–100); "Scream Volume" gag slider that genuinely maps to voice-SFX gain, caption "(you will be heard anyway)".
- VIDEO: resolution dropdown (`Screen.resolutions`), fullscreen mode dropdown, VSync toggle, quality: LOW / MEDIUM / **COZY** (§8.5), FOV slider 60–100 (default 70), Screen Effects 0–100% (§6.5), gamma slider −0.5…+0.5 with the classic "barely visible monster logo" calibration image (procedural: dark sprite at 3% contrast).
- CONTROLS: mouse sensitivity 0.5–5.0, invert Y toggle, read-only key reference list (rebinding is explicitly CUT from v1 — last-20% trap).
- GAMEPLAY: screen shake 0–200% (default 100; the 200% tick label reads "YES"), colorblind palette toggle (swaps survivor colors for a deuteranopia-safe set: `#E69F00 #56B4E9 #F0E442 #CC79A7 #009E73 #D55E00 #999999`).
- Persisted via `PlayerPrefs` through one `ScreamerSettings` singleton, applied live, reachable from menu and pause.

### 7.6 PAUSE ([Esc] in round — never pauses the server)

Edge-darkening only (vignette to 0.5), stack: RESUME / SETTINGS / **ABANDON FRIENDS** (leave to menu; label becomes **FLEE LIKE A COWARD** when you are the monster — matching the forfeit logic). Under the stack, 20 pt: `THE MONSTER DOESN'T PAUSE.`

### 7.7 RESULTS

- Winners' pawns on a code-built podium in the den (3 cubes); losers visible behind (survivors sulk-idle, or the monster slow-claps via two rotating arm cubes).
- Headline, 96 pt: `SURVIVORS ESCAPED` (mint) or `EVERYBODY DIED :)` (red), stamped in at a 4° angle with a thunk + 0.2 s shake.
- **Killcam window** (left, 640×360, §12.2) plays the round's loudest kill with letterbox + caption.
- **Superlatives** (right): three polaroid cards (280×330, tilts −6°/+3°/−2°) dealt with stamp-thunk SFX, 0.4 s apart (§9.6, copy §11.7).
- Per-player line items under the headline: name — fate (`Escaped`, `Eaten at 4:12`, `Died to a toilet`) — noise total.
- Footer: **REMATCH** (shows `x/8 want a sequel`) / BACK TO LOBBY / SNEAK AWAY. 15 s auto-rematch ring around the REMATCH button.

---

## 8. Supporting Systems

### 8.1 Networking backend seam

`INetworkBackend` interface: `Host()`, `Join(string address)`, `Shutdown()`, `string LobbyCode { get; }` (membership changes are read from the synchronized `GameManager.Roster`, not from the seam). Implementations: `UtpBackend` (UnityTransport — always compiled, the CI/compile/test path, first-class forever) and `SteamBackend` (Steam Lobbies + P2P) **entirely inside `#if SCREAMER_STEAM`**. A `BackendSelector` picks Steam when the define is present and `SteamAPI.Init()` succeeds, else UTP. The project must compile and run green with the Steam SDK absent. Enablement guide + steamcmd depot upload walkthrough: `Docs/STEAM.md` (see §14.3; build menu item `Screamer/Build Windows x64 (Steam)`).

### 8.2 Security scope (explicit decision)

Ship **friends/invite-first**. Client-trusting paths (`MakeNoiseServerRpc RequireOwnership=false`, client task input) are acceptable for private lobbies; a `// SECURITY:` comment marks each one for the post-launch public-matchmaking pass. Do not spend v1 budget on server validation beyond: server culls noise pings by hearing radius (§4.2), server owns catch/escape/task-done state (already true).

### 8.3 Round stats (feeds superlatives + results + killcam choice)

Server-side `RoundStats` dictionary per client: `noiseEmitted` (sum of loudness), `noodleBurns`, `wrongNotes`, `chickenChaseSeconds`, `furnitureSlaps`, `boosUsed`, `finaleScreamContribution`, `tasksCompleted`, `deathTime`, `killLoudness` (loudness of the victim's last 5 s — picks the killcam star). Synced to clients once at Results via a ClientRpc with a serializable struct array.

### 8.4 Steam extras (inside the define)

Rich presence strings: `Burning noodles in SCREAMER`, `Being a chair in SCREAMER` (Mimic-disguised), `Screaming (therapy) in SCREAMER`. Achievements at launch (jokes as beats): `THERAPY COMPLETE` scream 100 times; `IT WAS JUST A CHAIR` slap 50 innocent furniture pieces; `PACIFIST CHICKEN` win without catching the chicken yourself; `FINAL GIRL` escape as sole survivor 3 times; `NATURE IS HEALING` win because the monster quit. 4-pack SKU from day one.

### 8.5 Quality tiers

LOW: no grain, no particles beyond task-critical, half particle counts, hard shadows, pixel light count 2. MEDIUM: grain on, full particles, soft shadows. COZY (high): everything + light-flinch effects at full rate. A GTX 950-class laptop must hold 60 fps on MEDIUM.

### 8.6 Loose-end rules

- Host migration: none (v1). Host leaves → session ends to menu with `THE HOST UNPLUGGED THE HOUSE.`
- Join-in-progress: joins land in spectator-ghost mode until next round, event line `{name} is watching. Judging, mostly.`
- Late lobby join: fine, replaces a bot if one exists (bot leaves with event line `{bot} had to go. Nobody asked where.`).
- Min players to start: 1 human (bots fill); existing 2-client check is removed in favor of bot-fill.

---

## 9. Signature Features (the identity, in build order of importance)

### 9.1 Public monster morph (round opener)

On countdown 0: brown-out, MonsterRed smoke burst (40 red/plum quads), the chosen pawn silhouette-morphs (1.2 s scale-lerp swap) into the monster prefab in front of everyone. Monster screen already showed `IT'S YOU.` card 1 s earlier (§11.4). Everyone screams; round starts hot.

### 9.2 Three monsters = three horror movies (skins exist; add staging)

- **ZOMBIE (Shambler)** — slight desaturation aura (teal rim), flies-dot particle halo, audio tell: shuffle-THUMP loop, tiny zombie-arms idle sway. Slow-ish (7.8) but relentless.
- **MUTANT (Bruiser)** — loudest footsteps in the game (audible through walls at ≤ 18), heat-shimmer sleeve (scrolling alpha quad), amber crack decal (flattened dark quad) where it lands after sprint-stops. Fastest (8.6).
- **MIMIC (Liar)** — the marquee: see §9.4. Speed 8.2.
Monster-side intro card on role reveal (1.5 s): skin close-up framing + tagline (§11.4).

### 9.3 Sound-as-light doctrine

Every audible event spawns a visible expanding shockwave ring in-world at the source (torus-ish: a flat ring sprite quad scaling 0.5→loudness×8 over 0.6 s, color by type: `ScreamYellow` screams/music, `NoodleCream` cooking, feather-white chicken, `GhostMint` boo). Survivors see their own sins; ghosts see everything. Monster pings are **shaped** on its HUD: jagged ring = scream, note ring = music, double ring = alarm, feather ring = chicken (4 procedural sprites) — the monster reads WHAT it hears, not just where.

### 9.4 THE MIMIC ECONOMY (headline feature)

Mimic monster can press [F] while unseen (server checks: no living survivor has line-of-sight within 25) to lock into a furniture disguise: its model swaps to a **whitelisted prop proxy** (v1 placeholder set: Couch, Armchair, FloorLamp, TVStand — hand-checked colliders; when the Vintage Living Room pack lands, whitelist its hand-verified props only, never "any mesh"). Disguised: immobile, no red rim, slightly TOO saturated (+10% sat tint — the tell), 2-frame twitch every 20–35 s. Moving or attacking breaks disguise with a 0.5 s unfold animation (scale pop) — attack out of disguise has a 0.3 s windup, so point-blank reveals are survivable by sprinting.
**Counter-play IS the comedy:** survivors can **slap any furniture ([F])** — a dorky pat-pat with sound. Slapping real furniture = loudness 0.45 ping (you snitched on yourself, and `furnitureSlaps++` toward the FURNITURE ABUSER award). Slapping the Mimic triggers it instantly at point blank. Mimic rounds get **no "monster is loose" lockdown-end announcement** — survivors must notice the silence (graft of Mimic Roulette). A chair that wasn't there last round IS the monster; rematch map-knowledge becomes a skill.

### 9.5 SCREAM-POWERED FINALE (the engineered every-round clip)

§5 step 5. The door sign reads `SCREAM-POWERED HYDRAULICS. YES, REALLY.` The design guarantees the 15-second clip: group screaming + door explosion + monster rounding the corner at +25% speed + auto slow-mo photo-finish. After any finale decided within 2 s, a toast: `CLIP THAT. YOU KNOW YOU WANT TO.` (nudging Steam Game Recording / ShadowPlay users).

### 9.6 POST-ROUND SUPERLATIVES (yearbook polaroids)

Three awards computed from `RoundStats` (§8.3), priority order with dedupe (one award per player, fill from the top): LOUDEST HUMAN (max noiseEmitted) · NOODLE ARSONIST (max burns ≥ 1) · THAT WAS A J (max wrongNotes ≥ 2) · CHICKEN'S NEMESIS (max chase time ≥ 20 s) · FURNITURE ABUSER (max slaps ≥ 3) · CLUTCH MOUTH (max finale contribution) · FINAL GIRL/GUY (last living survivor) · USELESS (min task % — always available as filler) · THE VEGETARIAN (monster with 0 kills). Copy in §11.7.

### 9.7 GHOST BOO BUTTON

Each ghost gets ONE Boo per round: aim at a living player ≤ 20 away, press [B]: target gets a 0.2 s ghost-face flash (procedural sprite: two eye circles + wavy mouth arc on mint) + sting SFX, **and a real loudness-0.7 noise ping fires at the target's position**. Dead friends become chaos agents betraying the living for laughs. Ghost HUD shows `BOO: ARMED` / `BOO: SPENT`. Ghosts also see all noise pings (backseat commentators).

### 9.8 SILENCE AFTER A KILL (graft — one if-statement of pure dread)

When the monster catches someone, nearby survivors (≤ 25) hear the real 3D death scream — but the monster gets **NO noise ping for it**. The loudest sound in the house, and the hunter's screen stays quiet. Survivors learn: silence after a scream means it's eating.

### 9.9 THE HOUSE OCCASIONALLY JOINS IN (variance layer, cut-first candidate)

1-in-20 rounds modifier, announced only by the lobby ticker line `Tonight the house is in a mood.`: the TV randomly blares (fake 0.6 ping at the TV), smoke alarm chirps low-battery (fake 0.4 ping, kitchen), a door slams (fake 0.5 ping, hallway) — each ~45 s apart. The monster must learn to distrust the house; noise bluffing enters the meta.

---

## 10. Bots — "a doomed film crew" (review insurance, and funny on purpose)

Bots are server-driven survivor pawns (plus one bot-Monster mode for PRACTICE WITH BOTS). Movement: **waypoint graph, not NavMesh** — the wizard bakes a hand-authored node graph (one node per doorway + 2–4 per room at the coordinates in §3; A* over ~25 nodes) because NavMesh over future furniture clutter is the top predicted bug class. Bots are **fun-dumb**: they panic, they make noise, they die hilariously — which is cheaper AND funnier than competent.

**Survivor-bot state machine:** `PickTask → WalkToTask → DoTask (keypress simulation at human-ish cadence) → Flee (if monster seen ≤ 12 or a loudness ≥ 0.8 ping lands ≤ 10 away) → Rejoin`. While fleeing: sprint (emitting real sprint noise) to the farthest adjacent room node, 15% chance to just orbit the couch screaming (one 1.0 scream ping). Task execution: 5% per-stir chaos roll to miss the noodle window; karaoke 15% wrong-note rate; toilet 10% rush rate — bots feed the comedy systems honestly.

**One personality parameter each** (graft):

| Bot | Name tag | Parameter | Effect |
|---|---|---|---|
| The Rusher | [BOT] Chad | panicThreshold 0.9 | Barely flees; sprints everywhere (constant noise) |
| The Coward | [BOT] Brenda | panicThreshold 0.2 | Flees from distant pings; hides 8 s in the bathroom |
| The Liability | [BOT] Dale | chaosRoll ×3 | Triple fail rates on noodles/karaoke/toilet |
| The Shadow | [BOT] Tiffany | followBias 0.8 | Shadows the nearest human player, uselessly |

**Monster-bot** (practice mode only): walks to the latest ping ≥ 0.5; if none for 12 s, patrols room nodes; attacks on raycast contact; never camps the door (hard rule: leaves the yard 10 s after entering). Never uses Mimic disguise (humans only).

Bots fill to 4 after 10 s in lobby; host can add/remove to 8. Bot deaths feed the killcam and awards so solo play still produces the full results screen.

---

## 11. Game Copy (complete, binding English — the voice is deadpan, self-aware, slightly mean)

### 11.1 Menu / lobby ambience

- Sticky-note fake patch notes (rotate): `v1.3: chicken 4% angrier` · `v1.2: toilet grudge persistence fixed (it still remembers)` · `v1.1: monster now legally allowed in the kitchen` · `v1.0: removed the second monster. there was never a second monster.`
- Lobby ticker tips (rotate): `Tip: the monster hears sprinting.` · `Tip: it also hears screaming.` · `Tip: you will do both.` · `Tip: that chair was not there last round.` · `Tip: the fireplace dims when something is close. The fireplace is a coward too.` · `Tip: dead friends get one BOO. Choose your friends carefully.` · `Tonight the house is in a mood.` (modifier rounds only)
- Quit confirm: `Leave? The chicken will remember this.` → `YES, FLEE` / `NO, STAY`

### 11.2 HOW TO DIE (rules screen)

Header: `HOW TO DIE (A BEGINNER'S GUIDE)`
- `SCREAM THERAPY — Mash [SPACE] to let it all out. Fun fact: the monster also hears your therapy.`
- `KARAOKE NIGHT — Hit the right key. Miss, and the feedback screech files a noise complaint with the monster.`
- `DANCE FLOOR — Mash the arrows. The bass is excellent. The bass is also a homing beacon.`
- `INSTANT NOODLES — Stir with [E] or the smoke alarm tells everyone where you live.`
- `CATCH THE CHICKEN — It's in the yard. It's furious. It screams like you do.`
- `HAUNTED TOILET — Plunge calmly with [E]. Rush it and the pipes go full geyser.`
Footer, the three laws: `1. Everything you do is loud. 2. The monster hears everything. 3. See laws 1 and 2.`

### 11.3 Role reveal & task flavor (in-round)

- Survivor card: `YOU'RE A SURVIVOR — Do 6 chores. Quietly. (You won't.)`
- Monster card: `YOU ARE THE MONSTER — Eat your friends. They'd do the same to you.` (shown 1 s pre-morph as `IT'S YOU.`)
- Task panel flavor lines (one per task, under the name):
  - Scream Therapy: `Let it all out. All of it. Everyone's listening.`
  - Karaoke: `J, K, L. Three notes. How hard can it be. (Hard.)`
  - Dance Floor: `Nobody's watching. Something is listening.`
  - Noodles: `You had ONE noodle job.`
  - Chicken: `It ran this way. It's still screaming about it.`
  - Toilet: `Calm hands. Calm heart. Calm plunger.`
- Task cancel ([Q]): `Chickened out. The actual chicken is judging you.`
- Task complete: `DONE. Somehow.`

### 11.4 Monster-side copy

- Lockdown: `HEAD START — THEY RUN, YOU WAIT.` + countdown `RELEASED IN 0:07`
- Release: `GO EAT.`
- Skin intro cards: ZOMBIE — `Slow. Relentless. Smells incredible.` · MUTANT — `Fast. Angry. Skipped therapy.` · MIMIC — `That chair was not a chair.`
- Mimic disguise prompt: `[F] BECOME FURNITURE` / while disguised: `YOU ARE A COUCH. LIVE THE COUCH.`
- Finale mega-ping: `THEY'RE AT THE DOOR. GO. NOW.`
- Noise labels: see §4.2 table.

### 11.5 Event feed lines (`{n}` = player name)

- Join/leave: `{n} walked in. Bold.` · `{n} left. Smart.` · `{bot} had to go. Nobody asked where.` · `{n} is watching. Judging, mostly.`
- Task done: `{n} finished SCREAM THERAPY. SO MUCH PROGRESS.` · `{n} nailed KARAOKE. The neighbors called anyway.` · `{n} survived the DANCE FLOOR.` · `{n} cooked noodles without arson. Growth.` · `{n} CAUGHT THE CHICKEN. The chicken disagrees.` · `{n} tamed the toilet. Respect.`
- Fails: `{n} BURNED THE NOODLES. Classic {n}.` · `{n} hit a note that doesn't exist.` · `{n} angered the pipes.` · `{n} slapped an innocent chair.`
- Catch: `The monster ate {n}.` · victim's own banner: `YOU'RE DEAD. GREAT NEWS: NO MORE CHORES.`
- Escape: `{n} escaped! {n} would like everyone to know that.` · sole escape: `{n} left everyone behind. Final girl behavior.`
- Boo: `{ghost} booed {n}. From beyond the grave. Petty.`
- Endings: survivors win: `SURVIVORS ESCAPED` + sub `The monster is doing breathing exercises.` · monster wins: `EVERYBODY DIED :)` + sub `The chores remain unfinished. Typical.` · monster quit: `The monster rage-quit. Nature is healing.` · host quit: `THE HOST UNPLUGGED THE HOUSE.`
- Door locked: `Locked. Six padlocks. Subtle.` · Door finale sign: `SCREAM-POWERED HYDRAULICS. YES, REALLY.` · Finale prompt: `EVERYBODY SCREAM INTO THE DOOR` · Clip toast: `CLIP THAT. YOU KNOW YOU WANT TO.`

### 11.6 Pause / settings strings

`THE MONSTER DOESN'T PAUSE.` · `ABANDON FRIENDS` / `FLEE LIKE A COWARD` (monster) · Scream Volume caption `(you will be heard anyway)` · screen shake max label `YES` · gamma caption `Slide until you can barely see the little guy. He can always see you.`

### 11.7 Superlative award cards (title + caption)

- `LOUDEST HUMAN` — `A lighthouse, but for monsters.`
- `NOODLE ARSONIST` — `Three alarms. One pot.`
- `THAT WAS A J` — `The note heard around the house.`
- `CHICKEN'S NEMESIS` — `43 seconds. One bird. No dignity.`
- `FURNITURE ABUSER` — `The ottoman did nothing wrong.`
- `CLUTCH MOUTH` — `Carried the door scream. Hero.`
- `FINAL GIRL` / `FINAL GUY` — `Saw everyone die. Kept doing chores.`
- `USELESS` — `Contributed vibes.`
- `THE VEGETARIAN` — `A monster of principle. Zero meals.`

### 11.8 Killcam captions (pick by context)

`{n} DISCOVERED THE OTTOMAN.` (Mimic kill) · `{n} DIED DOING WHAT THEY LOVED: SCREAMING.` (killed during Scream Therapy) · `{n} NEVER SAW IT. EVERYONE ELSE DID.` (default) · `{n} OUTRAN NOTHING.` (caught while sprinting) · `{n} VS. PLUMBING: PLUMBING WINS.` (caught in bathroom)

---

## 12. Game Feel & The Killcam (every event has a camera, a sound, and a joke)

All feel routes through two wizard-installed systems: `ScreamerCam` (rotational Perlin shake, FOV kicks, tilt; obeys the screen-shake setting) and `GagFeedback` (popup text + particle burst + procedural SFX in one call). Particles come from a runtime `ParticleFactory` (colored quads/spheres, no textures).

### 12.1 Per-event spec

| Event | Camera | Visual | Audio (§13.2 bank) | Popup |
|---|---|---|---|---|
| Footstep (walk) | — | — | soft noise thud, wood-creak resonance indoors | — |
| Sprint | FOV 70→76 over 0.3 s; bob ×1.6 | `MIC HOT` amber chip bottom-left | louder thuds + breathing loop | — |
| Own noise emitted | — | crosshair ring bloom + mouth meter spike | — | — |
| Scream (each mash) | 2 px 1-frame punch | `AAAH ×7` counter, random tilt −15…15° | pitched scream (combo raises pitch) | — |
| Scream complete | 0.3 shake, 0.2 s | confetti burst (20 quads, survivor colors) | final biggest scream | `THERAPIST FIRED` |
| Karaoke correct | — | note particle in player color | clean sine chime (per-note pitch step) | — |
| Karaoke wrong | roll ±3°, 0.2 s | karaoke box rattles 0.1 s; practical strobes red | ring-mod feedback screech | `THAT WAS A J.` |
| Dance arrow | 1° bounce on beat | floor tile flash under player; 15° capsule lean | bass kick | — |
| Dance 8-streak | — | disco sweep + confetti | riser | `UNSTOPPABLE.` |
| Noodle stir | — | steam quads; pot light warms | bubbling | — |
| Noodle near-burn (last 0.6 s of window) | — | gray steam + lid rattle | rising kettle whine | — |
| Noodle burn | 0.4 shake | amber strobe, black smoke column **visible map-wide above the roof** (public shaming geometry) | klaxon + sad slide-whistle as bar rewinds | `PROGRESS: GONE. DIGNITY: ALSO GONE.` |
| Chicken squawk | — | feather quads + ROT-free white sound ring | pitched-up scream 0.8–1.3× | — |
| Chicken whiff | 10° tilt, 0.3 s stumble lock | — | whiff whoosh | `MISSED.` |
| Chicken catch | 0.2 s freeze-frame | feathers everywhere; chicken held overhead; **keeps squawking while carried** | triumphant cluck | `CHICKEN ACQUIRED. THE CHICKEN DISAGREES.` |
| Toilet good plunge | subtle nod | small mint ghost-wisp leaves the bowl | deep glorp | — |
| Toilet geyser | 0.5 shake | 4 m water column, droplet overlay 1.5 s wiped by a wiper animation | burst + rain patter | `THE PIPES HAVE OPINIONS.` |
| Furniture slap | tiny push | dust puff | pat-pat | `IT'S JUST A CHAIR.` (if innocent) |
| Monster catch | victim: whip-snap to monster face, 0.15 s hitstop, 1 frame white | victim ragdoll-style launch (impulse 600, 20° up — slapstick, zero gore), screen cracks (procedural crack sprite) then falls away to mint | bite thunk + victim scream (3D, §9.8 silence rule) | kill feed line |
| Become ghost | float drift inertia | world desaturates 30% (overlay tint), mint edges | reverb added to all audio (echo filter) | `YOU'RE DEAD. GREAT NEWS: NO MORE CHORES.` |
| Task complete (global) | — | house amber pulse + padlock shatter | glass break + one rising chord note (stacks per task toward a major chord) | event line |
| 6th task | — | light surge, door floods mint | pipe-organ sting, triumphant-but-wrong | `FINAL REEL` slate |
| Finale scream | per-mash 1 px punches | door strains, dust falls | layered screams + hydraulic groan | meter: `SCREAM-POWERED HYDRAULICS` |
| Door blows open | 0.6 shake all | door panels fly, white-out 0.3 s | explosion + chord resolve | — |
| Escape | — | sprint into light; name slate `SLAM: {NAME}` | door slam | — |
| Photo-finish | 0.5× slow-mo 1.2 s | — | audio pitch drop | `CLIP THAT.` toast |
| Monster ≤ 15 of you | — | house tells (§6.4) + VHS-free dread: vignette +0.1 | low drone fade-in, no text | — |

All screen transitions are hard cuts with a 2 px vertical shake — the UI is possessed, not elegant. Never crossfade.

### 12.2 KILLCAM — plan of record (graft: honest fallback ladder, v1 ships)

**v1 (ships):** a **staged cinematic replay**, not frame capture. The server records a lightweight ring buffer of the loudest-kill moment: positions + yaw of victim and monster for the 6 s before the catch (10 Hz samples = 120 floats — trivial). At Results, the client re-poses two proxy pawns in the actual map, renders them live from a wall-mounted camera (picked from 4 pre-placed cinematic anchors per room, nearest with line of sight), letterboxed with timestamp `REPLAY — 11:4X PM`, caption from §11.8, played in the Results window via a second camera rendering to a `RenderTexture` (256 MB-safe at 640×360). Every round ends by handing the lobby a ready-to-film artifact.
**v2 (post-launch):** PNG film-strip export of 6 frames. **v3 (maybe never):** MP4. Ship v1, promise nothing.

---

## 13. Placeholder Contract & Asset Attachment Points

The game is fully playable, art-directed, before any import. **Art-directed primitives** (graft): walls tinted warm cream-rust (not gray), survivor capsules get huge googly eyes (two white spheres + black pupils, code-built — honest, self-aware placeholder comedy), monsters keep final silhouette scale + final accent/rim light so the asset drop inherits a finished mood.

**Named anchors** — `MapAnchors` GameObject with child empties the wizard creates; asset packs drop onto them without touching triggers/spawns:
`FireplaceAnchor, CouchAnchor, ArmchairAnchor×2, TVAnchor, KaraokeAnchor, DancePadAnchor, StoveAnchor, ToiletAnchor, BedAnchor, GarageJunkAnchor×3, YardShedAnchor, YardFenceDressingAnchor×4, CellarDoorAnchor` — positions per §3.4.
**Monster skins:** `MonsterSkinSelector` children stay `Skins/Skin_Zombie`, `Skins/Skin_Mutant`, `Skins/Skin_Mimic` — drop the real models (FREE Zombie Male AAB, Morbid Creatures Mutant, Mimic prototype) as children, keep the code-built rim lights.
**Mimic prop whitelist:** `MimicPropCatalog` ScriptableObject-free static list (v1: the 4 primitive props); real Vintage Living Room props are added one by one after collider/pivot hand-check.
**Yard dressing:** lowpoly medieval + Wasteland LITE pieces are **silhouettes beyond the fence only** — never lit, never visited, never sharing a sightline with interior assets.
**Audio:** every SFX routes through `AudioDirector` (one clip table) — real assets replace the procedural bank in one place.
**Font:** drop any OFL .ttf in `Assets/Screamer/Fonts/` and `ScreamerUIStyle` picks it up.

### 13.2 Procedural audio bank (generated at boot via `AudioClip.Create`; all mono 44.1 kHz; ±10% random pitch per play; ambience −18 dB under SFX)

| Clip | Synthesis (waveform / freq / envelope) | Duration |
|---|---|---|
| Scream | sawtooth sweep 300→900 Hz + vibrato 8 Hz depth 30 Hz; ADSR 0.02/0.1/0.7/0.3 | 0.6 s |
| Chicken squawk | same sweep ×1.6 pitch, duration ×0.5, 2 repeats 60 ms apart | 0.35 s |
| Feedback screech | ring-mod: saw 700 Hz × sine 55 Hz, sweep up 400 Hz; hard attack | 0.8 s |
| Smoke-alarm klaxon | square 50% duty, alternating 950/750 Hz every 0.25 s | 2.0 s loop |
| Kettle whine | sine rising 800→1600 Hz, slow attack | 1.5 s |
| Slide-whistle (rewind) | sine falling 1200→300 Hz | 0.8 s |
| Bass kick (dance) | sine 110→45 Hz over 0.12 s, click transient | 0.15 s |
| Chime (karaoke good) | sine at note freq (523/659/784 Hz for J/K/L), 0.4 s decay | 0.5 s |
| Glorp (toilet) | sine 180→60 Hz + noise burst tail | 0.4 s |
| Geyser burst | white noise, band 400–3k, 0.05 attack 1.0 decay | 1.2 s |
| Glass break (padlock) | noise burst band 2–6 kHz, 3 staggered grains | 0.4 s |
| UI thunk | sine 140 Hz, 0.08 s + 20 ms noise click | 0.1 s |
| Stamp thunk | sine 90 Hz, 0.15 s, heavy attack | 0.2 s |
| Door explosion | brown noise burst + 60 Hz sine layer, 1.4 s decay | 1.6 s |
| Pipe-organ sting | detuned saw triad (220/277/330 Hz −8 cents apart) | 2.0 s |
| Monster drone | two sines 55 + 57 Hz (beating), slow 2 s attack | 4 s loop |
| Shuffle-THUMP (zombie) | noise scrape 0.3 s + 70 Hz thud | 0.8 s loop |
| Heartbeat | double sine-60 Hz thumps 0.4 s apart | 1.2 s loop |
| Den ambience | brown noise LP 400 Hz + vinyl-crackle impulses (random 20 ms ticks) | 8 s loop |
| Yard ambience | brown noise LP 250 Hz + wind amplitude LFO 0.1 Hz + owl (sine 420 Hz, twice, every ~20 s) | 10 s loop |
| Footstep | enveloped noise burst 0.07 s, LP 900 Hz (indoor adds 120 Hz resonance) | 0.08 s |
| Breathing (sprint) | filtered noise swells at 0.8 Hz | 2.5 s loop |
| Pat-pat (slap) | two 0.05 s LP-noise taps 120 ms apart | 0.25 s |
| Boo sting | saw 220→880 Hz, 0.15 s, hard attack | 0.3 s |

---

## 14. Engineering Order (binding schedule logic; from the judges)

1. **M0 — Rematch refactor + English rebrand.** The GameManager Results→Lobby reset path (§5.7) first — it invalidates everything built on top if done late. In the same pass: full rebrand + string rewrite + delete ArabicText.cs (every file is being touched anyway).
2. **M1 — Round flow + screens.** Lobby/countdown/public morph/lockdown/finale/results states; all uGUI screens from the wizard; settings persistence.
3. **M2 — Feel + audio bank + sound-as-light.** ScreamerCam, GagFeedback, ParticleFactory, AudioDirector, padlocks, house tells, silence-after-kill.
4. **M3 — Bots.** Waypoint graph + survivor bots + monster bot.
5. **M4 — Signature features.** Mimic economy → finale scream → killcam v1 → superlatives → Boo. **Cut order if slipping: House-in-a-mood (§9.9) first, then Boo — never the finale (it's the trailer).**
6. **M5 — Steam layer + build pipeline.** `INetworkBackend`, `SCREAMER_STEAM`, `Screamer/Build Windows x64 (Steam)` menu item, `Docs/STEAM.md` with the steamcmd depot walkthrough. Test matrix every release: editor-no-SDK / build-no-SDK / build-with-SDK.
7. **M6 — Polish gate.** Playtest rule (falsifiable): **at least one genuine flinch per round, or the hallway gets darker.** If testers mute the game, the audio failed. Verify every screen at a 720p30 re-encode.

**14.4 Build checks (automated, in the build menu item):** fail the build if any `.cs` or scene string contains characters outside basic Latin + common punctuation; fail if `ArabicText` type exists; warn if any `Debug.Log` remains.

---

## 15. Steam Positioning (store-facing, for reference)

- **Capsule art brief:** the hallway thesis shot — four googly-eyed survivors mid-panic in a doorway of warm amber light, the Mutant's silhouette in MidnightPlum darkness behind them, one survivor still holding the screaming chicken; SCREAMER in rattling ScreamYellow.
- **Tags:** Online Co-Op, Horror, Comedy, Party Game, Multiplayer, Dark Humor. Comp row: Lethal Company, Content Warning, Among Us, Phasmophobia.
- **Trailer (30 s, all in-engine):** 3 cozy seconds of noodle-stirring → smoke alarm → smash-cut montage (karaoke screech ping, chair-slapping, toilet geyser, a couch standing up) → scream-powered door finale in slow-mo → stamped `EVERYBODY DIED :)` report → title card `SCREAMER. Shhh.`
- **Price:** $9.99, 4-pack $29.99. Early Access framing honest: core loop done; content cadence = new tasks (one `TaskBase` subclass each) and Mimic furniture (each new asset pack).
- **Launch:** creator-seeded — keys to 50 mid-size co-op streamers; bots guarantee the solo reviewer a full round, a kill, a killcam, and an award.

*End of document. If something isn't specified here, the answer is: warmer, louder, and the monster hears it.*

---

## 14. Hide & Shriek Update (post-ship content drop 1)

Three systems that deepen the noise meta without touching the core loop.
All numbers live as public fields with these defaults.

### 14.1 Per-skin monster abilities — one key, three personalities ([F])

| Skin | Ability | Numbers | The joke |
|---|---|---|---|
| ZOMBIE | **LUNGE** — forward burst | +9 speed for 0.45 s, 8 s cooldown, REAL loudness-0.45 ping at the monster | It trades stealth for the bite; survivors see the ring coming |
| MUTANT | **ROAR** — everyone yelps | radius 16, 25 s cooldown; every living survivor in range emits a REAL loudness-0.5 ping at THEIR position | Wallhack by comedy: the roar makes you snitch on yourself |
| MIMIC | (unchanged) | [F] remains BECOME FURNITURE | The couch needs no buffs |

The cooldown clock is a replicated server-time stamp (`MonsterAbilities`), so
the `[F] LUNGE` / `[F] ROAR` ready-prompt and server validation always agree.
Bot monsters never use abilities (same rule as the Mimic disguise).

### 14.2 Hideable closets (`HideSpot`, built by `HideSpotFactory`)

Two closets: the bedroom screaming-closet alcove finally gets doors, plus a
freestanding garage wardrobe. Honest stealth: the doors are real geometry, so
hiding works by blocking line of sight — no invisibility.

- Closed closet shows ONE uniform prompt (`[E] THE CLOSET`) whether empty or
  occupied — the slap-prompt no-free-detector rule.
- Survivor inside: `[E] LEAVE. BRAVELY.` Opening an occupied closet from
  outside (monster or "friend") flushes the occupant with a REAL loudness-0.6
  yelp, a PEEKABOO popup, and an event-feed line.
- Every door touch is a loudness-0.3 ping labeled `A CLOSET DOOR. SUSPICIOUS.`
  A closet is a bet, not a bunker.
- Server-authoritative occupancy with a watchdog (caught/disconnected occupant
  frees the closet) and a rematch reset hook on the game-state change.

### 14.3 The rubber chicken decoy ([G], one per survivor per round)

Throw a rubber chicken (12-unit lob, walls catch it honestly). After landing
it squeaks 3 times, 1.1 s apart — each squeak is an ENVIRONMENTAL loudness-0.75
chicken ping wearing the real chicken's label (`THE CHICKEN. OBVIOUSLY.`), so
the monster cannot tell the liar from the task chicken, and nobody farms
LOUDEST HUMAN with a toy. `DecoySpent` is a replicated per-round flag; the
visual bird (`DecoyChicken`) is local-only cosmetics on every client.

## 15. Whispering Woods (forest map) + Auto-Install

- **Forest map**: Screamer > Build Everything (Forest Map). Every HouseLayout
  blocking rect becomes a treeline with an IDENTICAL box collider, so tasks,
  spawns, closets, waypoints and hearing geometry carry over untouched.
  Clearing dressing (ferns/rocks/mushrooms, deterministic seed), campfire at
  the hearth position, lantern posts marking each chore (same event-lighting
  roles, driven by HouseLightsDirector unchanged), slappable stumps where the
  furniture sat, log fences, backdrop cabins past the north treeline carrying
  MedievalBuildingAnchor1-3.
- **Auto-Install My Assets** (Screamer menu): after importing the owned packs
  via Package Manager > My Assets, one click finds models by name and wires
  them: Zombie/Mutant/Mimic models into the Monster prefab skin roots (scaled
  to pawn height, grounded, placeholders disabled not deleted) and furniture/
  buildings onto the open scene's MapAnchors (footprint-clamped, grounded,
  faced toward the map center). Misses are reported and stay on placeholders;
  reruns skip anything already installed.
