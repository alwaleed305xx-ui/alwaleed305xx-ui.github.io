# SCREAMER — Parallel Upgrade Plan (PLAN.md)

**Scope:** turn the current prototype into the game specified by `Docs/GDD.md`, built by 8 independent engineers working in parallel with no communication. This document is the only shared contract. The GDD is the binding design spec; this plan is the binding ownership and interface spec.

All paths below are relative to the repository root (`UnityGame/...`). No engineer may create, modify, or delete any file outside their own `ownedFiles` list. No file appears in two modules.

---

## 0. Global Rules (binding for every module)

1. **Unity 2022.3 LTS, C# 9, Netcode for GameObjects 1.12, uGUI only.** No TextMeshPro. No new packages in `Packages/manifest.json`. Built-in Render Pipeline. Only stable, well-documented Unity/NGO APIs.
2. **English only, everywhere.** Every owned file that currently contains Arabic comments, strings, or identifiers is rewritten into polished American-indie English (voice per GDD section 11). No Arabic characters may remain in any file this project ships. `ArabicText.cs` and all its call sites are deleted. The automated build check (module 8) fails any build containing non-Latin characters in shipped code.
3. **One class per file, file named after the class.** Enums and interfaces also get their own files (a small enum nested inside its owning class is acceptable only where this plan explicitly shows it nested, e.g. `GameManager.GameState`).
4. **All Steam code strictly behind `#if SCREAMER_STEAM`.** The project must compile and run green with the define absent and the Steam SDK absent. The seam is `INetworkBackend` (module 1).
5. **Editor-only code** lives in `Assets/Editor/` or behind `#if UNITY_EDITOR`. All editor menu items live under `Screamer/`.
6. **Never mention any AI model or AI assistance anywhere** — code, comments, docs, UI, store copy.
7. **Factory pattern for scene construction.** Every module that contributes scene objects exposes a *runtime-compilable* static factory (listed in its interfaces). The editor wizard (module 8) only orchestrates: it calls the factories in order, saves prefabs/materials/scene via `AssetDatabase`/`PrefabUtility`, and wires references. Factories must not reference `UnityEditor`.
8. **Materials:** factories that need materials take a `System.Func<string, UnityEngine.Color, UnityEngine.Material> mat` parameter (name + color -> material). The wizard passes an asset-backed implementation; `ScreamerPalette.MakeRuntimeMaterial` (module 6) is the play-mode fallback. Shader resolution always goes through `ScreamerPalette.LitShader()` (URP/Lit -> HDRP/Lit -> Standard fallback chain, kept from the current wizard).
9. **Actor IDs.** Humans are NGO client ids. Bots use synthetic ids starting at `GameManager.BotIdBase = 9000`. Every API that identifies a participant takes a `ulong actorId`.
10. **Colors** come exclusively from `ScreamerPalette` (module 6). Pure `#FFFFFF` and `#000000` are forbidden. `MonsterRed` is reserved for the Monster/danger only.
11. **Player-facing strings** come from `GameCopy` (module 4) wherever this plan or the GDD defines the line. Gameplay modules consume `GameCopy` constants instead of inlining prose.
12. **Compatibility:** evolve existing classes in place (same type names unless the GDD's rebrand mandate renames them). Contract breaks relative to current code are listed in section 10 and are the only permitted breaks.
13. **Stubs:** you may stub consumed types locally to compile and test, but stubs are never committed. Only files in your `ownedFiles` list are committed. If a signature you need is not in this document, you must solve the problem inside your own module.
14. **`// SECURITY:` comments** mark every client-trusting path (per GDD 8.2). Do not build server-side validation beyond what the GDD requires.

---

## 1. Module Map (8 modules)

| # | Key | Title | Owns (summary) |
|---|-----|-------|----------------|
| 1 | `round-flow` | Round Flow, Lobby & Network Core | GameManager state machine, rematch reset, roster/ready, RoundStats, NoiseSystem, bootstrap, backend seam + Steam runtime layer |
| 2 | `characters` | Survivor, Monster & Mimic Characters | PlayerController, ghost mode + Boo, MonsterController, skins, Mimic economy, furniture slap |
| 3 | `tasks` | Tasks & Escape Finale | TaskBase, the six tasks, TaskManager, EscapeDoor + padlocks + scream-powered finale |
| 4 | `ui` | UI/UX, Menus & Settings | All uGUI screens, HUDs, settings persistence, UI style kit, the full English copy catalog, superlatives |
| 5 | `bots` | AI Bots | Waypoint graph + A*, survivor bots, monster bot, personalities, bot fill |
| 6 | `world` | Map, Lighting & Atmosphere | Palette, binding map layout data, house builder, practicals/flicker, house tells, event lighting, screen FX overlay, anchors, house-in-a-mood |
| 7 | `feel-audio` | Game Feel, Juice, Killcam & Procedural Audio | ScreamerCam, GagFeedback, ParticleFactory, sound-as-light rings, morph FX, killcam v1, full procedural audio bank |
| 8 | `editor-build` | Editor Tooling, Build Pipeline & Docs | ScreamerSetupWizard (one-click build of everything), build menu + language audit, STEAM.md, README |

Integration hub: module 1's contracts are consumed by everyone; modules 2/3/6/7 feed module 8's wizard through their factories.

---

## 2. Module 1 — `round-flow`: Round Flow, Lobby & Network Core

**Mission.** The GDD's M0+M1 backbone plus the network seam: rewrite `GameManager` into the server-authoritative state machine `Lobby -> Countdown -> Lockdown -> Playing -> Finale -> Results -> Lobby` with the **rematch-without-restart reset** (despawn all pawns, clear caught/escaped, reset monster id, call `TaskManager.ServerResetAll()`, `EscapeDoor.ServerReset()`, `KillcamRecorder.ServerReset()`, `BotManager.ServerDespawnAllPawns()`, respawn lobby pawns — no scene reload). Owns role selection (random among non-bot clients if >= 2 humans, else any actor), lobby pawn spawning on join, countdown and lockdown timers via server time, monster swap at countdown zero (despawn survivor pawn, spawn monster prefab at same position), finale entry (map-wide mega-ping + monster speed bonus via consumed APIs), win conditions including the monster-disconnect coward clause and host-quit teardown, join-in-progress as spectator ghost, roster with ready states and rematch voting, server-side `RoundStats`, results broadcast, and the rewritten `NoiseSystem` with **server-side hearing-radius culling** (radius `8 + loudness * 52`) and the **silence-after-a-kill** rule (`NoiseType.DeathScream` is never relayed to the monster). Also owns the `INetworkBackend` seam: `UtpBackend` (always compiled, first-class) and `SteamBackend` + `SteamIntegration` (achievements, rich presence) entirely inside `#if SCREAMER_STEAM`, selected by `BackendSelector`. Renames `SayehBootstrap` to `ScreamerBootstrap`. All files rewritten in English.

**Owned files** (D = delete, R = rewrite existing, N = new):
- R `UnityGame/Assets/Scripts/Core/GameManager.cs`
- R `UnityGame/Assets/Scripts/Core/NoiseSystem.cs`
- R `UnityGame/Assets/Scripts/Core/ClientNetworkTransform.cs`
- D `UnityGame/Assets/Scripts/Core/SayehBootstrap.cs`
- N `UnityGame/Assets/Scripts/Core/ScreamerBootstrap.cs`
- N `UnityGame/Assets/Scripts/Core/NoiseType.cs`
- N `UnityGame/Assets/Scripts/Core/StatKind.cs`
- N `UnityGame/Assets/Scripts/Core/RoundStats.cs`
- N `UnityGame/Assets/Scripts/Core/PlayerRoundResult.cs`
- N `UnityGame/Assets/Scripts/Core/RosterEntry.cs`
- N `UnityGame/Assets/Scripts/Core/RoundFlowFactory.cs`
- N `UnityGame/Assets/Scripts/Net/INetworkBackend.cs`
- N `UnityGame/Assets/Scripts/Net/BackendSelector.cs`
- N `UnityGame/Assets/Scripts/Net/UtpBackend.cs`
- N `UnityGame/Assets/Scripts/Net/SteamBackend.cs`
- N `UnityGame/Assets/Scripts/Net/SteamIntegration.cs`

**Exposes:**

```csharp
public class GameManager : Unity.Netcode.NetworkBehaviour
{
    public static GameManager Instance { get; }
    public enum GameState : byte { Lobby, Countdown, Lockdown, Playing, Finale, Results }
    public NetworkVariable<GameState> State { get; }               // server-write
    public NetworkVariable<ulong> MonsterClientId { get; }         // ulong.MaxValue = none
    public NetworkVariable<bool> SurvivorsWon { get; }             // valid during Results
    public NetworkVariable<double> StateEndsAtServerTime { get; }  // 0 = no timer; drives countdown/lockdown/results UI
    public NetworkList<RosterEntry> Roster { get; }
    public bool IAmMonster { get; }
    public const ulong BotIdBase = 9000;
    public const int MinFillPlayers = 4;
    public static bool IsBotId(ulong actorId);
    public string ActorName(ulong actorId);                        // "Steam persona" / "Victim N" / "[BOT] Chad"
    public RoundStats Stats { get; }                               // server only

    // server API
    public void StartRound();                                      // host; Lobby -> Countdown (no min-human gate beyond 1)
    public void ServerActorCaught(ulong actorId);
    public void ServerActorEscaped(ulong actorId);
    public void ServerAddStat(ulong actorId, StatKind kind, float amount);
    public void ServerEnterFinale();                               // called by EscapeDoor when 6/6 unlock fires
    public void ServerDoorOpened();                                // called by EscapeDoor when finale meter fills
    public void ServerRegisterBot(ulong botId, string botName);    // roster row add
    public void ServerUnregisterBot(ulong botId);

    // client -> server
    [ServerRpc(RequireOwnership = false)] public void SetReadyServerRpc(bool ready, ServerRpcParams p = default);
    [ServerRpc(RequireOwnership = false)] public void VoteRematchServerRpc(ServerRpcParams p = default);
    [ServerRpc(RequireOwnership = false)] public void ReportStatServerRpc(StatKind kind, float amount, ServerRpcParams p = default); // SECURITY: client-trusted

    // events
    public static event System.Action<GameState> OnClientStateChanged;       // every client
    public static event System.Action<PlayerRoundResult[]> OnClientResults;  // fired on every client entering Results
    public event System.Action<ulong> OnServerActorCaught;
    public event System.Action<ulong> OnServerActorEscaped;
}

public enum NoiseType : byte { Footsteps, Scream, Music, Alarm, Chicken, Plumbing, Cooking, Slap, Boo, DeathScream }

public class NoiseSystem : Unity.Netcode.NetworkBehaviour
{
    public static NoiseSystem Instance { get; }
    public static float HearingRadius(float loudness);             // 8 + loudness * 52
    public void MakeNoise(ulong sourceActorId, Vector3 position, float loudness, NoiseType type, string label); // any client; SECURITY: client-trusted
    public void ServerMakeNoise(ulong sourceActorId, Vector3 position, float loudness, NoiseType type, string label); // bots, house events
    // monster client only; server-culled by hearing radius; DeathScream never delivered
    public static event System.Action<ulong, Vector3, float, NoiseType, string> OnMonsterHeardNoise;
    // every client (world rings, self-noise meter); sourceActorId == ulong.MaxValue for environment
    public static event System.Action<ulong, Vector3, float, NoiseType> OnNoiseVisible;
}

public enum StatKind : byte { NoiseEmitted, NoodleBurns, WrongNotes, ChickenChaseSeconds, FurnitureSlaps,
    BoosUsed, FinaleScreamContribution, TasksCompleted, DeathTime, KillLoudness }

public class RoundStats   // plain C# class, server-side
{
    public void Add(ulong actorId, StatKind kind, float amount = 1f);
    public float Get(ulong actorId, StatKind kind);
    public PlayerRoundResult[] BuildResults();
    public void Reset();
}

[System.Serializable]
public struct PlayerRoundResult : Unity.Netcode.INetworkSerializable
{
    public ulong actorId; public Unity.Collections.FixedString64Bytes name;
    public bool isBot; public bool wasMonster; public bool escaped;
    public float deathTime;            // < 0 = survived
    public float noiseEmitted, noodleBurns, wrongNotes, chickenChaseSeconds,
                 furnitureSlaps, boosUsed, finaleScreamContribution, tasksCompleted, killLoudness;
    public void NetworkSerialize<T>(Unity.Netcode.BufferSerializer<T> s) where T : Unity.Netcode.IReaderWriter;
}

[System.Serializable]
public struct RosterEntry : Unity.Netcode.INetworkSerializable, System.IEquatable<RosterEntry>
{
    public ulong actorId; public Unity.Collections.FixedString64Bytes name;
    public bool ready; public bool isBot; public int colorIndex; public float pingMs;
    public void NetworkSerialize<T>(Unity.Netcode.BufferSerializer<T> s) where T : Unity.Netcode.IReaderWriter;
    public bool Equals(RosterEntry other);
}

public class ScreamerBootstrap : UnityEngine.MonoBehaviour  // replaces SayehBootstrap, same prefab-registration job
{
    public UnityEngine.GameObject[] networkPrefabs;
}

public interface INetworkBackend
{
    bool Host(); bool Join(string address); void Shutdown();
    string LobbyCode { get; }            // Steam lobby id, or "ip:port" fallback
    bool SupportsInvites { get; }
    void OpenInviteOverlay();            // no-op on UTP
    // membership changes are read from GameManager.Roster, not from the seam
}

public static class BackendSelector
{
    public static INetworkBackend Active { get; }  // SteamBackend when SCREAMER_STEAM and SteamAPI.Init() ok, else UtpBackend
}

public static class RoundFlowFactory   // called by the wizard
{
    // creates GameManager(+NetworkObject+NoiseSystem) and NetworkManager(+UnityTransport+ScreamerBootstrap), wires prefabs and spawns
    public static void BuildNetworkAndManagers(UnityEngine.GameObject survivorPrefab, UnityEngine.GameObject monsterPrefab,
        UnityEngine.Vector3[] survivorSpawns, UnityEngine.Vector3 monsterSpawn);
}
```

**Consumes:** `MonsterController.ServerFreeze(float)` / `ServerApplySpeedBonus(float, float)` / `MonsterController.ActiveMonster` (module 2); `TaskManager.ServerResetAll()` (module 3); `EscapeDoor.Instance`, `EscapeDoor.ServerReset()` (module 3); `BotManager.Instance.ServerFillTo/ServerSpawnSurvivorBot/ServerSpawnMonsterBot/ServerDespawnAllPawns/TryGetBotName` (module 5); `KillcamRecorder.Instance.ServerNotifyKill/ServerBroadcastBestClip/ServerReset` (module 7); `HouseLayout.SurvivorSpawns/MonsterSpawn/CellarDoor` (module 6); `GameCopy` lines (module 4); `AudioDirector`/`Sfx` for the 3D death scream within 25 units (module 7).

Balance constants: lockdown 10 s, countdown 5 s, results auto-rematch 15 s, bots fill to 4 after 10 s in lobby (fill via `BotManager.ServerFillTo(MinFillPlayers)`), finale speed bonus +25% for 8 s. Death scream audible radius 25.

---

## 3. Module 2 — `characters`: Survivor, Monster & Mimic Characters

**Mission.** Rewrite `PlayerController` (walk 4.5 / sprint 7.5 / jump 6 / gravity -18, sprint noise loudness 0.3 every 2.5 s through the new `NoiseSystem` signature, interact raycast range 3 with context prompts via `GameUI.ShowPrompt`, furniture slap on [F] with range 2.0 / cooldown 0.8 s / loudness 0.45, ready-up pose in lobby, caught/escape flow, ghost conversion) and `MonsterController` (per-skin speeds 7.8/8.6/8.2, camera-forward raycast attack range 2.4 / cooldown 1.2 s against `IVictim`, server freeze, finale speed bonus, bot-drive hooks). Extract `GhostFly` into `GhostController` (plain MonoBehaviour: fly speed 8, Boo aiming on [B], look-raycast <= 20, one per round; the RPC lives on `PlayerController` because NGO forbids adding NetworkBehaviours at runtime). Implement the **Mimic economy** (`MimicDisguise`: [F] while unseen — server line-of-sight check, no living survivor within 25 — locks into a whitelisted prop proxy from `MimicPropCatalog`; +10% saturation tell; 2-frame twitch every 20-35 s; move/attack breaks disguise with 0.5 s unfold; 0.3 s attack windup out of disguise; disguise re-allowed after 3 s unseen). Slapping the Mimic triggers it at point blank. All Arabic rewritten to English; strings come from `GameCopy`.

**Owned files:**
- R `UnityGame/Assets/Scripts/Player/PlayerController.cs`
- N `UnityGame/Assets/Scripts/Player/GhostController.cs`
- N `UnityGame/Assets/Scripts/Player/IVictim.cs`
- N `UnityGame/Assets/Scripts/Player/SlappableFurniture.cs`
- N `UnityGame/Assets/Scripts/Player/CharacterFactory.cs`
- R `UnityGame/Assets/Scripts/Monster/MonsterController.cs`
- R `UnityGame/Assets/Scripts/Monster/MonsterSkinSelector.cs`
- N `UnityGame/Assets/Scripts/Monster/MimicDisguise.cs`
- N `UnityGame/Assets/Scripts/Monster/MimicPropCatalog.cs`

**Exposes:**

```csharp
public interface IVictim
{
    ulong ActorId { get; }
    bool IsCatchable { get; }            // alive, not escaped
    UnityEngine.Transform VictimTransform { get; }
    void RequestCaught();                // safe to call from the monster owner's client
}

public class PlayerController : Unity.Netcode.NetworkBehaviour, IVictim
{
    public static PlayerController Local { get; }                 // the local player's pawn, null if none
    public static readonly System.Collections.Generic.List<PlayerController> All;
    public bool IsGhost { get; }
    public bool InputLocked { get; set; }                          // tasks lock movement
    public NetworkVariable<int> ColorIndex { get; }                // index into ScreamerPalette.SurvivorColors
    public NetworkVariable<bool> ReadyPose { get; }                // lobby diegetic ready (raised hand, frozen)
    public NetworkVariable<bool> BooSpent { get; }
    public float walkSpeed, runSpeed, jumpForce, gravity;          // 4.5 / 7.5 / 6 / -18
    public float interactRange;                                    // 3
    [ServerRpc(RequireOwnership = false)] public void GetCaughtServerRpc();
    [ServerRpc] public void EscapeServerRpc();
    [ServerRpc] public void BooServerRpc(ulong targetActorId);     // ghost Boo: flash target + loudness 0.7 ping at target
    [ServerRpc(RequireOwnership = false)] public void SlapServerRpc(); // stat + innocent-slap noise
}

public class GhostController : UnityEngine.MonoBehaviour { public float speed; } // 8; fly + Boo aim; added after death

public class SlappableFurniture : UnityEngine.MonoBehaviour { }    // marker; placed by HouseFactory on furniture

public class MonsterController : Unity.Netcode.NetworkBehaviour
{
    public static MonsterController ActiveMonster { get; }         // on all clients; null when despawned
    public float moveSpeed;                                        // set per skin: 7.8 / 8.6 / 8.2
    public float attackRange, attackCooldown;                      // 2.4 / 1.2
    public bool IsFrozen { get; }
    public void ServerFreeze(float seconds);                       // server-time synced (kept from current code)
    public void ServerApplySpeedBonus(float multiplier, float seconds); // finale: 1.25f, 8f
    public int SkinIndex { get; }                                  // 0 zombie, 1 mutant, 2 mimic
    // bot drive (server only; disables local input reading)
    public bool BotDriven { get; set; }
    public void BotMove(UnityEngine.Vector3 worldDirection);
    public void BotTryAttack();
}

public class MonsterSkinSelector : Unity.Netcode.NetworkBehaviour
{
    public UnityEngine.GameObject[] skins;                         // children Skins/Skin_Zombie, Skin_Mutant, Skin_Mimic
    public int CurrentSkin { get; }                                // synced; -1 until chosen
}

public class MimicDisguise : Unity.Netcode.NetworkBehaviour
{
    public bool IsDisguised { get; }
    [ServerRpc] public void TryDisguiseServerRpc();                // validates LOS rule server-side
    public void ServerBreakDisguise();                             // move/attack/slapped
    public static event System.Action<bool> OnLocalDisguiseChanged; // monster client HUD prompt swap
}

public static class MimicPropCatalog
{
    public static readonly string[] PropNames;                     // "Couch","Armchair","FloorLamp","TVStand"
    public static UnityEngine.GameObject BuildPropProxy(int index, System.Func<string, UnityEngine.Color, UnityEngine.Material> mat);
}

public static class CharacterFactory   // called by the wizard; returns scene objects the wizard saves as prefabs
{
    public static UnityEngine.GameObject BuildSurvivorPawn(System.Func<string, UnityEngine.Color, UnityEngine.Material> mat); // capsule + googly eyes + cap + CameraHolder + CharacterController + NetworkObject + ClientNetworkTransform
    public static UnityEngine.GameObject BuildMonsterPawn(System.Func<string, UnityEngine.Color, UnityEngine.Material> mat);  // three skin children with staging (rim lights, flies halo, etc.)
}
```

**Consumes:** `GameManager` (state, `IAmMonster`, `ServerActorCaught/Escaped`, `ServerAddStat`, `ReportStatServerRpc`), `NoiseSystem.MakeNoise/ServerMakeNoise`, `NoiseType` (module 1); `TaskBase.TryStart(PlayerController)`, `EscapeDoor.TryEscape(PlayerController)` (module 3); `GameUI.ShowPrompt/ShowCenterCard/SetGhostHud/SetMonsterHud/PulseSelfNoise` and `GameCopy` (module 4); `ScreamerCam`, `GagFeedback`, `AudioDirector`, `Sfx` (module 7); `ScreamerPalette`, `ScreenFxOverlay.SetDesaturation` for ghost view (module 6); `ScreamerSettings` (mouse sensitivity, invert Y, FOV) (module 4).

---

## 4. Module 3 — `tasks`: Tasks & Escape Finale

**Mission.** Rewrite `TaskBase` (English, `NoiseType` + source actor on every ping, VU-meter hook `GameUI.NotifyTaskNoise()`, `[Q]` abandon, `ServerReset()` for rematch, honest `ServerBotWork` channel for bots) and split `FunnyTasks.cs` into six files per GDD 4.3: `TaskScream` (10 mashes of [Space], each a loudness-1.0 instant ping), `TaskKaraoke` (8 s, J/K/L, correct 3x, idle 0.5x, wrong note = loudness 0.9 ping + 0 that frame), `TaskDance` (7 s, arrows, 2.5x/0.2x, beat-cycled 16-tile pad), `TaskNoodles` (9 s, stir [E] every 2 s, miss = loudness 1.0 + visible rollback at -1x/s for 2 s), `TaskChicken` (4 s at chicken, flee speed 5 within radius 6, wander leash 11, squawk 0.8 every 1.2 s while fleeing), `TaskToilet` (8 s, calm rhythm [E], gap >= 0.5 s = 5x, rushed = loudness 1.0 geyser + 0). Rewrite `TaskManager` (per-task completion -> padlock break, 6/6 -> `EscapeDoor.ServerUnlock()`; reset API) and `EscapeDoor` into a NetworkBehaviour: six padlock props, diegetic counter, locked-door red light state handoff, **finale group scream** (all living survivors within 4.0 of the door mash a shared 3.0 s meter; door blows open; photo-finish slow-mo trigger owned by module 7 via events). Stats reporting (wrong notes, burns, chase seconds, finale contribution) via `GameManager.ReportStatServerRpc`/`ServerAddStat`.

**Owned files:**
- R `UnityGame/Assets/Scripts/Tasks/TaskBase.cs`
- R `UnityGame/Assets/Scripts/Tasks/TaskManager.cs`
- R `UnityGame/Assets/Scripts/Tasks/EscapeDoor.cs`
- D `UnityGame/Assets/Scripts/Tasks/FunnyTasks.cs`
- N `UnityGame/Assets/Scripts/Tasks/TaskScream.cs`
- N `UnityGame/Assets/Scripts/Tasks/TaskKaraoke.cs`
- N `UnityGame/Assets/Scripts/Tasks/TaskDance.cs`
- N `UnityGame/Assets/Scripts/Tasks/TaskNoodles.cs`
- N `UnityGame/Assets/Scripts/Tasks/TaskChicken.cs`
- N `UnityGame/Assets/Scripts/Tasks/TaskToilet.cs`
- N `UnityGame/Assets/Scripts/Tasks/TaskFactory.cs`

**Exposes:**

```csharp
public abstract class TaskBase : Unity.Netcode.NetworkBehaviour
{
    public string taskName;
    [UnityEngine.TextArea] public string flavorText;               // renamed from funnyDescription
    public float duration;                                         // seconds at 1x
    public float noiseInterval;
    [UnityEngine.Range(0f, 1f)] public float noiseLoudness;
    public NoiseType noiseType;
    public string noiseLabel;                                      // from GameCopy
    public bool IsDone { get; }
    public void TryStart(PlayerController player);                 // human interaction entry
    public void ServerReset();                                     // rematch: done = false, progress = 0
    public void ServerBotWork(ulong botId, float deltaTime, float chaosMultiplier); // advances server-side progress, emits honest noise/fails
    public static event System.Action<TaskBase> OnAnyTaskCompleted; // fires on every client when done flips true
    protected virtual float ProgressMultiplier();                  // 1
    protected virtual void OnTaskStart();
    protected virtual void OnTaskEnd();
}

public class TaskManager : UnityEngine.MonoBehaviour
{
    public static TaskManager Instance { get; }
    public TaskBase[] allTasks;
    public EscapeDoor escapeDoor;
    public int CompletedCount { get; }
    public int TotalCount { get; }
    public void ServerResetAll();                                  // rematch reset path
}

public class EscapeDoor : Unity.Netcode.NetworkBehaviour
{
    public static EscapeDoor Instance { get; }
    public NetworkVariable<int> PadlocksBroken { get; }            // 0..6
    public NetworkVariable<bool> Unlocked { get; }                 // 6/6 done; finale gather phase
    public NetworkVariable<float> FinaleMeter { get; }             // 0..1 shared scream meter
    public NetworkVariable<bool> Open { get; }                     // door blown open
    public void ServerBreakPadlock();
    public void ServerUnlock();                                    // also calls GameManager.ServerEnterFinale()
    public void ServerReset();                                     // relock, restore padlocks
    public void TryEscape(PlayerController player);                // [E] when Open
    [ServerRpc(RequireOwnership = false)] public void FinaleScreamServerRpc(ServerRpcParams p = default); // validates distance <= 4 and alive
    public static event System.Action<float> OnFinaleMeterChanged; // all clients
    public static event System.Action<ulong, float> OnClientEscape; // actorId, round time (photo-finish watchers)
}

public static class TaskFactory   // called by the wizard
{
    public static TaskBase[] BuildAllStations(UnityEngine.Transform parent, System.Func<string, UnityEngine.Color, UnityEngine.Material> mat); // six stations at HouseLayout positions, chicken with NetworkObject + NetworkTransform
    public static EscapeDoor BuildCellarDoor(UnityEngine.Transform parent, System.Func<string, UnityEngine.Color, UnityEngine.Material> mat); // angled double door + six padlock props + signs
}
```

**Consumes:** `NoiseSystem`, `NoiseType`, `GameManager` (stats, `ServerEnterFinale`, `ServerDoorOpened`, state), `PlayerController` (lock input, `IsGhost`, `All` for chicken flee), `GameUI` (`ShowTaskPanel/ShowTaskHint/UpdateTaskProgress/NotifyTaskNoise/HideTaskPanel`), `GameCopy` (task names, flavor, hints, noise labels), `GagFeedback`/`ScreamerCam`/`AudioDirector`/`Sfx` (per-event juice from GDD 12.1), `ScreamerPalette`, `HouseLayout` station positions (module 6), `Billboard` (module 4) for world labels.

---

## 5. Module 4 — `ui`: UI/UX, Menus & Settings

**Mission.** Everything uGUI, per GDD section 7, built in code through `ScreamerUIStyle` (cream plates, ink drop shadows, rust borders, squash-pop buttons, back-ease panel entrances, `Font.CreateDynamicFontFromOSFont("Arial", size)` with the optional `Assets/Screamer/Fonts/` OFL override, reference resolution 1920x1080). Screens: `MenuUI` (live den backdrop with camera dolly `MenuCameraDolly`, rattling title, PLAY / HOW TO DIE / SETTINGS / SNEAK AWAY, host/join/practice flows through `BackendSelector.Active`, sticky-note patch notes), `LobbyUI` (right-rail roster from `GameManager.Roster`, room code + copy, ready/start, ticker, countdown numbers), `GameUI` (survivor HUD: padlock icons, event feed typewriter, self-noise meter + crosshair bloom, task VU panel; monster HUD: red vignette via `ScreenFxOverlay`, screen-edge directional smear pings with 3 s ghost-echo reusing the existing `SignedAngle` math, shaped ping sprites by `NoiseType`, lockdown countdown, kill tallies, Mimic prompts), `SettingsUI` (AUDIO/VIDEO/CONTROLS/GAMEPLAY tabs per GDD 7.5), `PauseUI` (edge-darkening only, never pauses the server), `ResultsUI` (podium screen framing, killcam window fed by `KillcamPlayer`, three polaroid superlatives from `SuperlativeEngine`, per-player fate lines, rematch footer with 15 s ring). Owns `ScreamerSettings` (PlayerPrefs singleton, live-apply, `OnChanged`) and `GameCopy`, the single English copy catalog implementing GDD section 11 verbatim. Deletes `ArabicText.cs` and `ConnectUI.cs` (replaced by MenuUI flows); `GameUI.Set()` writes `t.text = s` directly.

**Owned files:**
- R `UnityGame/Assets/Scripts/UI/GameUI.cs`
- R `UnityGame/Assets/Scripts/UI/Billboard.cs`
- D `UnityGame/Assets/Scripts/UI/ArabicText.cs`
- D `UnityGame/Assets/Scripts/UI/ConnectUI.cs`
- N `UnityGame/Assets/Scripts/UI/MenuUI.cs`
- N `UnityGame/Assets/Scripts/UI/MenuCameraDolly.cs`
- N `UnityGame/Assets/Scripts/UI/LobbyUI.cs`
- N `UnityGame/Assets/Scripts/UI/SettingsUI.cs`
- N `UnityGame/Assets/Scripts/UI/PauseUI.cs`
- N `UnityGame/Assets/Scripts/UI/ResultsUI.cs`
- N `UnityGame/Assets/Scripts/UI/ScreamerUIStyle.cs`
- N `UnityGame/Assets/Scripts/UI/ScreamerSettings.cs`
- N `UnityGame/Assets/Scripts/UI/GameCopy.cs`
- N `UnityGame/Assets/Scripts/UI/SuperlativeEngine.cs`
- N `UnityGame/Assets/Scripts/UI/UiFactory.cs`

**Exposes:**

```csharp
public class GameUI : UnityEngine.MonoBehaviour
{
    public static GameUI Instance { get; }
    public void ShowRoleReveal(bool iAmMonster, int monsterSkinIndex);  // "IT'S YOU." card + skin intro
    public void ShowEvent(string line);                                  // feed, 4 s, typewriter 40 chars/s
    public void ShowTaskPanel(string title, string flavor);
    public void ShowTaskHint(string hint);
    public void UpdateTaskProgress(float normalized);
    public void NotifyTaskNoise();                                       // VU clips into MonsterRed 0.2 s
    public void HideTaskPanel(string closingLine);
    public void ShowPrompt(string prompt);                               // bottom context plate; null/"" hides
    public void PulseSelfNoise(float loudness);                          // self-noise meter + crosshair bloom
    public void ShowCenterCard(string text, UnityEngine.Color color, float seconds);
    public void SetMonsterHud(bool on);                                  // swap, not overlay
    public void SetGhostHud(bool on, bool booArmed);
    public void ShowNoisePing(UnityEngine.Vector3 worldPos, float loudness, NoiseType type, string label);
    public void ShowLockdownCountdown(float secondsLeft);                // < 0 hides
    public void AddKillTally();
    public void ResetRoundHud();                                         // rematch
}

public class ScreamerSettings : UnityEngine.MonoBehaviour
{
    public static ScreamerSettings Instance { get; }
    public float MouseSensitivity;   // 0.5..5, default 2.2
    public bool InvertY;
    public float Fov;                // 60..100, default 70
    public float ScreenShake;        // 0..2, default 1
    public float ScreenEffects;      // 0..1
    public int QualityTier;          // 0 LOW, 1 MEDIUM, 2 COZY
    public bool ColorblindPalette;
    public float Gamma;              // -0.5..0.5
    public float MasterVolume, SfxVolume, MusicVolume, AmbienceVolume, VoiceVolume; // 0..1
    public static event System.Action OnChanged;   // every system self-applies on this
    public void Save(); public void Load();
}

public static class GameCopy        // the complete GDD section 11 catalog; all shipped prose lives here
{
    public static string NoiseLabel(NoiseType type);                 // GDD 4.2 labels
    public static readonly string[] LobbyTips; public static readonly string[] StickyPatchNotes;
    public static string EventJoin(string n); public static string EventLeave(string n);
    public static string EventTaskDone(string n, string taskName);
    public static string EventCaught(string n); public static string EventEscaped(string n);
    public static string EventBoo(string ghost, string target);
    public static string KillcamCaption(byte contextId, string victimName);  // GDD 11.8
    // ...plus named constants for every fixed line in GDD sections 11.1-11.8 (headers, laws, prompts, confirmations)
}

public static class SuperlativeEngine
{
    public struct Award { public string title; public string caption; public ulong actorId; }
    public static Award[] Pick(PlayerRoundResult[] results);         // GDD 9.6 priority order, dedupe, max 3
}

public class Billboard : UnityEngine.MonoBehaviour { }               // unchanged behavior, English comments

public static class UiFactory        // called by the wizard
{
    public static void BuildAll();   // canvas + EventSystem + every screen + GameUI/MenuUI/LobbyUI/SettingsUI/PauseUI/ResultsUI wiring + ScreamerSettings
}
```

**Consumes:** `GameManager` (state/events/roster/results/`StartRound`/`SetReadyServerRpc`/`VoteRematchServerRpc`), `NoiseSystem.OnMonsterHeardNoise/OnNoiseVisible`, `BackendSelector.Active` (module 1); `EscapeDoor.PadlocksBroken/OnFinaleMeterChanged` (module 3); `ScreamerPalette`, `ScreenFxOverlay` (vignette, letterbox, grain toggles) (module 6); `KillcamPlayer.Play`, `KillcamRecorder.OnClientClipReady`, `AudioDirector.PlayUI`, `Sfx`, `GagFeedback` (module 7); `MimicDisguise.OnLocalDisguiseChanged` (module 2).

---

## 6. Module 5 — `bots`: AI Bots

**Mission.** GDD section 10. Hand-authored **waypoint graph** (A* over ~25 nodes; node/edge data read from `HouseLayout.WaypointNodes/WaypointEdges`), `BotManager` (server-driven roster of up to 7 bots, auto-fill to 4, host add/remove, late-join replacement, spawn survivor/monster pawns, rematch despawn), `BotPawn` (server-owned survivor pawn: CharacterController movement along paths, implements `IVictim` with `[ServerRpc(RequireOwnership=false)]` catch routing, emits real sprint noise), `SurvivorBotBrain` (state machine PickTask -> WalkToTask -> DoTask via `TaskBase.ServerBotWork` at human-ish cadence -> Flee on monster sighting <= 12 or loud ping <= 10 -> Rejoin; 15% couch-orbit panic scream; honest chaos rolls: noodles 5%, karaoke 15% wrong notes, toilet 10% rush — scaled by personality), `MonsterBotBrain` (practice mode: walk to latest ping >= 0.5, patrol after 12 s silence, attack on contact via `MonsterController.BotMove/BotTryAttack`, leaves the yard 10 s after entering, never disguises), and the four personalities (Chad panicThreshold 0.9, Brenda 0.2 + bathroom hide 8 s, Dale chaosRoll x3, Tiffany followBias 0.8). Bot deaths feed stats/killcam like humans.

**Owned files:**
- N `UnityGame/Assets/Scripts/Bots/BotManager.cs`
- N `UnityGame/Assets/Scripts/Bots/BotPawn.cs`
- N `UnityGame/Assets/Scripts/Bots/SurvivorBotBrain.cs`
- N `UnityGame/Assets/Scripts/Bots/MonsterBotBrain.cs`
- N `UnityGame/Assets/Scripts/Bots/BotPersonality.cs`
- N `UnityGame/Assets/Scripts/Bots/WaypointGraph.cs`
- N `UnityGame/Assets/Scripts/Bots/BotFactory.cs`

**Exposes:**

```csharp
public class BotManager : Unity.Netcode.NetworkBehaviour
{
    public static BotManager Instance { get; }
    public int BotCount { get; }
    public ulong ServerAddBot();                       // next personality; registers with GameManager; returns botId
    public void ServerRemoveBot(ulong botId);
    public void ServerFillTo(int minTotalPlayers);     // called by GameManager 10 s into lobby
    public void ServerSpawnSurvivorBot(ulong botId, UnityEngine.Vector3 position);
    public void ServerSpawnMonsterBot(ulong botId, UnityEngine.Vector3 position);  // practice mode
    public void ServerDespawnAllPawns();               // rematch reset
    public bool TryGetBotName(ulong botId, out string name);
}

public class BotPawn : Unity.Netcode.NetworkBehaviour, IVictim
{
    public static readonly System.Collections.Generic.List<BotPawn> All;
    public ulong BotId { get; }
    // IVictim implemented: ActorId == BotId, RequestCaught routes a RequireOwnership=false ServerRpc
}

public class WaypointGraph : UnityEngine.MonoBehaviour
{
    public static WaypointGraph Instance { get; }
    public void SetGraph(UnityEngine.Vector3[] nodes, int[][] edges);
    public int NearestNode(UnityEngine.Vector3 position);
    public System.Collections.Generic.List<UnityEngine.Vector3> FindPath(UnityEngine.Vector3 from, UnityEngine.Vector3 to);
}

public class BotPersonality : UnityEngine.ScriptableObject { }  // NOT used as asset; plain serializable class is fine instead
// (implementation detail: a plain [Serializable] class with name, panicThreshold, chaosRoll, followBias)

public static class BotFactory       // called by the wizard
{
    public static void Install();    // creates BotManager (+NetworkObject) and WaypointGraph, feeds it HouseLayout graph data
}
```

(If `BotPersonality` is implemented as a plain class, it still lives alone in its file.)

**Consumes:** `GameManager` (`BotIdBase`, `ServerRegisterBot/Unregister`, `ServerActorCaught`, `ServerAddStat`, state/events), `NoiseSystem.ServerMakeNoise` to emit bot noise (module 1). Bot *sensing* uses `NoiseSystem.OnNoiseVisible`, which module 1 guarantees also fires on the server/host process — that is the bots' ping-awareness channel (flee on loudness >= 0.8 landing <= 10 away); `OnMonsterHeardNoise` is monster-client-only and is never used by bots. Also consumes `TaskBase.ServerBotWork`, `TaskManager.Instance.allTasks` (module 3); `MonsterController.ActiveMonster/BotDriven/BotMove/BotTryAttack` (module 2); `HouseLayout` (rooms, waypoint data, couch/bathroom positions) (module 6); `GameCopy` bot names/leave lines (module 4). Bot pawns use plain server-authoritative `NetworkTransform`, not `ClientNetworkTransform`.

---

## 7. Module 6 — `world`: Map, Lighting & Atmosphere

**Mission.** Single source of truth for color (`ScreamerPalette`, GDD section 2 exactly) and geometry (`HouseLayout`: every floor/wall/fence segment, room bounds, doorway gaps, station/spawn/prop positions, light positions, waypoint nodes+edges — all binding coordinates from GDD sections 3 and 10). `HouseFactory` builds the Henderson House from primitives with art-directed warm materials, furniture (couch, armchairs, lamp, TV stand — tagged `SlappableFurniture`), the screaming closet, yard + fence, emissive window quads, the `MapAnchors` object (every named anchor from GDD section 13), all per-room practicals with behaviors (`LightFlicker` Perlin, `SwingingLight` garage pendulum, `TvStaticFlicker`), global lighting (flat teal ambient, exponential fog 0.012, moon, plum camera background, <= 2 real-time lights per room). Owns the information-channel systems: `HouseTells` (4 Hz monster-distance poll, 15% teal desaturation + fireplace dim within 15), `HouseLightsDirector` (task pulse, finale surge, morph brown-out, loud-noise flinch within 12), `ScreenFxOverlay` (code-built overlay canvas sort order 5000: procedural radial vignette, 64x64 scrolling grain, flash/tint layer, letterbox bars, desaturation tint; obeys `ScreamerSettings.ScreenEffects` and quality tier), and `HouseMoodDirector` (1-in-20 rounds; fake TV/alarm/door pings ~45 s apart via `ServerMakeNoise`; announced only by the lobby ticker line — cut first if slipping).

**Owned files:**
- N `UnityGame/Assets/Scripts/World/ScreamerPalette.cs`
- N `UnityGame/Assets/Scripts/World/HouseLayout.cs`
- N `UnityGame/Assets/Scripts/World/HouseFactory.cs`
- N `UnityGame/Assets/Scripts/World/MapAnchors.cs`
- N `UnityGame/Assets/Scripts/World/HouseTells.cs`
- N `UnityGame/Assets/Scripts/World/HouseLightsDirector.cs`
- N `UnityGame/Assets/Scripts/World/LightFlicker.cs`
- N `UnityGame/Assets/Scripts/World/SwingingLight.cs`
- N `UnityGame/Assets/Scripts/World/TvStaticFlicker.cs`
- N `UnityGame/Assets/Scripts/World/ScreenFxOverlay.cs`
- N `UnityGame/Assets/Scripts/World/HouseMoodDirector.cs`

**Exposes:**

```csharp
public static class ScreamerPalette
{
    public static readonly UnityEngine.Color LamplightAmber;  // #FFB84D
    public static readonly UnityEngine.Color ShagRust;        // #C4502E
    public static readonly UnityEngine.Color HauntedTeal;     // #1E6E6E
    public static readonly UnityEngine.Color MidnightPlum;    // #2B1B3D
    public static readonly UnityEngine.Color ScreamYellow;    // #FFE234
    public static readonly UnityEngine.Color MonsterRed;      // #FF2E4C
    public static readonly UnityEngine.Color GhostMint;       // #7DFFD4
    public static readonly UnityEngine.Color NoodleCream;     // #FFF3DC
    public static readonly UnityEngine.Color InkBlack;        // #141019
    public static readonly UnityEngine.Color[] SurvivorColors;            // 7, join order (GDD section 2)
    public static readonly UnityEngine.Color[] SurvivorColorsColorblind;  // deuteranopia-safe set
    public static UnityEngine.Color Survivor(int colorIndex);             // honors ScreamerSettings.ColorblindPalette
    public static UnityEngine.Shader LitShader();                         // URP/Lit -> HDRP/Lit -> Standard
    public static UnityEngine.Material MakeRuntimeMaterial(string name, UnityEngine.Color c);
}

public static class HouseLayout
{
    public struct Box { public string name; public UnityEngine.Vector3 center; public UnityEngine.Vector3 size; }
    public static readonly Box[] Floors; public static readonly Box[] Walls; public static readonly Box[] Fence;
    public static readonly UnityEngine.Vector3[] SurvivorSpawns;   // 8, around the den couch (GDD 3.4)
    public static readonly UnityEngine.Vector3 MonsterSpawn;       // garage (10, 0.1, -16)
    public static readonly UnityEngine.Vector3 ScreamStation, KaraokeStation, DancePad, NoodleStove,
        ToiletStation, ChickenStart, CellarDoor, Couch, Fireplace, Tv;
    public struct RoomDef { public string name; public UnityEngine.Rect boundsXZ; public UnityEngine.Vector3 center; }
    public static readonly RoomDef[] Rooms;
    public static readonly UnityEngine.Vector3[] WaypointNodes; public static readonly int[][] WaypointEdges;
}

public class MapAnchors : UnityEngine.MonoBehaviour
{
    public static MapAnchors Instance { get; }
    public UnityEngine.Transform Get(string anchorName);   // GDD section 13 names, e.g. "FireplaceAnchor"
}

public class HouseLightsDirector : UnityEngine.MonoBehaviour
{
    public static HouseLightsDirector Instance { get; }
    public void TaskCompletePulse();      // +25% amber, 0.6 s ease-out
    public void FinaleSurge();            // +20% for 1 s; door area floods GhostMint
    public void MorphBrownout();          // 20% for 0.4 s
    public void NoiseFlinch(UnityEngine.Vector3 position, float loudness);  // loudness >= 0.8, radius 12, -30% 0.2 s
    public void SetDoorFinaleState(bool finale);  // red lock light <-> mint flood
    public void ResetRoundLighting();     // rematch
}

public class ScreenFxOverlay : UnityEngine.MonoBehaviour
{
    public static ScreenFxOverlay Instance { get; }
    public void SetVignette(float alpha, UnityEngine.Color tint);  // 0.25 normal / 0.45 + MonsterRed on monster
    public void Flash(UnityEngine.Color color, float duration);
    public void SetLetterbox(bool on);
    public void SetGrain(bool on);
    public void SetDesaturation(float amount);                     // ghost view, 0..1
}

public static class HouseFactory     // called by the wizard
{
    public static void BuildAll(UnityEngine.Transform mapRoot, System.Func<string, UnityEngine.Color, UnityEngine.Material> mat);
    // geometry + furniture + anchors + lights + directors + ScreenFxOverlay canvas + HouseTells + HouseMoodDirector
}
```

**Consumes:** `MonsterController.ActiveMonster` (module 2) for tells; `NoiseSystem.OnNoiseVisible` + `ServerMakeNoise` and `GameManager` state events for pulses/brown-out/mood rounds (modules 1); `TaskBase.OnAnyTaskCompleted` (module 3); `SlappableFurniture` marker (module 2); `ScreamerSettings.OnChanged` (module 4). Subscribes rather than being called wherever an event exists; the explicit methods above remain for module 1's state transitions.

---

## 8. Module 7 — `feel-audio`: Game Feel, Juice, Killcam & Procedural Audio

**Mission.** GDD sections 12 and 13.2 plus the sound-as-light doctrine (9.3) and the public morph (9.1). `ScreamerCam` (rotational Perlin shake, FOV kicks, tilt, hitstop; obeys `ScreamerSettings.ScreenShake`), `GagFeedback` (popup + particles + SFX in one call), `ParticleFactory` (colored quad/sphere bursts, confetti, feathers, smoke column, no textures; counts scale with quality tier), `NoiseRingFx` (expanding in-world ring at every `OnNoiseVisible` event, color by `NoiseType`; ghosts see all), `MonsterMorphFx` (countdown-zero red smoke burst + 1.2 s silhouette scale-lerp, driven by `GameManager.OnClientStateChanged` + `MonsterClientId`), photo-finish slow-mo (client-side `Time.timeScale` lerp 0.5x for 1.2 s on escapes decided within 2 s, cosmetic only), and **killcam v1** (staged cinematic: `KillcamRecorder` server ring buffer, 10 Hz, 6 s of victim+monster pos/yaw keyed by victim `KillLoudness`; `KillcamPlayer` re-poses two proxy pawns in the real map, renders from the nearest of the wizard-placed cinematic anchors to a 640x360 `RenderTexture` with letterbox + timestamp). The full **procedural audio bank**: `AudioSynth` (waveform/envelope primitives), `ProceduralAudioBank` (every clip in GDD 13.2 via `AudioClip.Create`, mono 44.1 kHz, +/-10% pitch per play), `AudioDirector` (one clip table; 3D one-shots, UI one-shots, loops; buses Master/SFX/Music/Ambience/Voice live-applied from settings; ambience -18 dB under SFX), `AmbienceController` (den/yard loops, monster-proximity drone + heartbeat within dread radius 15, ghost reverb-style echo toggle).

**Owned files:**
- N `UnityGame/Assets/Scripts/Feel/ScreamerCam.cs`
- N `UnityGame/Assets/Scripts/Feel/GagFeedback.cs`
- N `UnityGame/Assets/Scripts/Feel/ParticleFactory.cs`
- N `UnityGame/Assets/Scripts/Feel/NoiseRingFx.cs`
- N `UnityGame/Assets/Scripts/Feel/MonsterMorphFx.cs`
- N `UnityGame/Assets/Scripts/Feel/PhotoFinishDirector.cs`
- N `UnityGame/Assets/Scripts/Feel/KillcamClip.cs`
- N `UnityGame/Assets/Scripts/Feel/KillcamRecorder.cs`
- N `UnityGame/Assets/Scripts/Feel/KillcamPlayer.cs`
- N `UnityGame/Assets/Scripts/Feel/FeelFactory.cs`
- N `UnityGame/Assets/Scripts/Audio/Sfx.cs`
- N `UnityGame/Assets/Scripts/Audio/AudioSynth.cs`
- N `UnityGame/Assets/Scripts/Audio/ProceduralAudioBank.cs`
- N `UnityGame/Assets/Scripts/Audio/AudioDirector.cs`
- N `UnityGame/Assets/Scripts/Audio/AmbienceController.cs`
- N `UnityGame/Assets/Scripts/Audio/AudioFactory.cs`

**Exposes:**

```csharp
public enum Sfx : byte { Scream, ChickenSquawk, FeedbackScreech, SmokeAlarm, KettleWhine, SlideWhistle,
    BassKick, Chime, Glorp, GeyserBurst, GlassBreak, UiThunk, StampThunk, DoorExplosion, PipeOrganSting,
    MonsterDrone, ShuffleThump, Heartbeat, DenAmbience, YardAmbience, Footstep, Breathing, PatPat, BooSting }

public class AudioDirector : UnityEngine.MonoBehaviour
{
    public static AudioDirector Instance { get; }
    public void Play(Sfx sfx, UnityEngine.Vector3 worldPos, float volume = 1f, float pitch = 1f);
    public void PlayUI(Sfx sfx, float volume = 1f);
    public UnityEngine.AudioSource StartLoop(Sfx sfx, UnityEngine.Vector3 worldPos, float volume = 1f);
    public void StopLoop(UnityEngine.AudioSource handle);
    public UnityEngine.AudioClip Clip(Sfx sfx);        // for AudioSources owned by other modules
}

public class ScreamerCam : UnityEngine.MonoBehaviour
{
    public static ScreamerCam Instance { get; }
    public void Shake(float amplitude, float duration);   // amplitude 0..1, scaled by settings
    public void FovKick(float targetFov, float duration); // sprint 70 -> 76
    public void Tilt(float degrees, float duration);
    public void Hitstop(float duration);                  // 0.15 s catch freeze
}

public class GagFeedback : UnityEngine.MonoBehaviour
{
    public static GagFeedback Instance { get; }
    public void Gag(string popupText, UnityEngine.Vector3 worldPos, Sfx sfx, UnityEngine.Color accent, float shake = 0f);
    public void Popup(string text, UnityEngine.Color color);   // screen-space gag text only
}

public static class ParticleFactory
{
    public static void Burst(UnityEngine.Vector3 pos, UnityEngine.Color color, int count, float speed = 3f, float life = 0.8f);
    public static void Confetti(UnityEngine.Vector3 pos, UnityEngine.Color[] colors, int count);
    public static void SmokeColumn(UnityEngine.Vector3 pos, float height, float seconds);  // map-wide burn shame
}

[System.Serializable]
public struct KillcamClip : Unity.Netcode.INetworkSerializable
{
    public ulong victimId; public byte captionContextId;   // feeds GameCopy.KillcamCaption
    public UnityEngine.Vector3[] victimPos; public float[] victimYaw;
    public UnityEngine.Vector3[] monsterPos; public float[] monsterYaw;   // 10 Hz, 6 s
    public void NetworkSerialize<T>(Unity.Netcode.BufferSerializer<T> s) where T : Unity.Netcode.IReaderWriter;
}

public class KillcamRecorder : Unity.Netcode.NetworkBehaviour
{
    public static KillcamRecorder Instance { get; }
    public void ServerNotifyKill(ulong victimId, float victimRecentLoudness, byte captionContextId);
    public void ServerBroadcastBestClip();                 // ClientRpc on Results entry
    public static event System.Action<KillcamClip> OnClientClipReady;
    public void ServerReset();
}

public class KillcamPlayer : UnityEngine.MonoBehaviour
{
    public static KillcamPlayer Instance { get; }
    public UnityEngine.RenderTexture Play(KillcamClip clip);   // 640x360 staged replay, letterboxed
    public void Stop();
}

public static class FeelFactory      // called by the wizard
{
    public static void Install(System.Func<string, UnityEngine.Color, UnityEngine.Material> mat);
    // ScreamerCam on Camera.main, GagFeedback, NoiseRingFx, MonsterMorphFx, PhotoFinishDirector,
    // KillcamRecorder(+NetworkObject), KillcamPlayer, 4 cinematic camera anchors per room
}
public static class AudioFactory { public static void Install(); }  // AudioDirector + bank generation + AmbienceController
```

**Consumes:** `NoiseSystem.OnNoiseVisible`, `GameManager` events (`OnClientStateChanged`, `MonsterClientId`, `OnServerActorCaught`, `OnClientResults`), `EscapeDoor.OnClientEscape/OnFinaleMeterChanged/Open` (photo-finish + door explosion), `MonsterController.ActiveMonster` (drone distance, morph target), `PlayerController.Local/All` + `BotPawn.All` (killcam sampling, self events), `ScreamerPalette`, `ScreamerSettings.OnChanged` (volumes, shake, effects, quality), `GameCopy.KillcamCaption`, `MapAnchors` (cinematic anchor placement validation).

---

## 9. Module 8 — `editor-build`: Editor Tooling, Build Pipeline & Docs

**Mission.** The one-click wizard and everything release. `ScreamerSetupWizard` (menu `Screamer/Build Everything`) replaces `SayehSetupWizard`: creates `Assets/Screamer/{Prefabs,Scenes,Materials,Fonts}`, a fresh scene, then orchestrates **in this order**: `HouseFactory.BuildAll` -> `CharacterFactory.BuildSurvivorPawn/BuildMonsterPawn` (saved via `PrefabUtility.SaveAsPrefabAsset` to `Assets/Screamer/Prefabs/`) -> `TaskFactory.BuildAllStations/BuildCellarDoor` -> `RoundFlowFactory.BuildNetworkAndManagers` (with `HouseLayout` spawns) -> `BotFactory.Install` -> `AudioFactory.Install` -> `FeelFactory.Install` -> `UiFactory.BuildAll` -> wire `TaskManager.allTasks/escapeDoor` -> save scene `Assets/Screamer/Scenes/Game.unity`, set build settings. The wizard's `mat` delegate creates/loads materials under `Assets/Screamer/Materials/` using `ScreamerPalette.LitShader()`. Also owns `ScreamerBuildPipeline` (menu items `Screamer/Build Windows x64` and `Screamer/Build Windows x64 (Steam)`, the latter toggling `SCREAMER_STEAM` via `PlayerSettings.SetScriptingDefineSymbolsForGroup` for the build) and `ScreamerStringAudit` (fails the build if any shipped `.cs` or scene string contains characters outside basic Latin + common punctuation; fails if an `ArabicText` type exists; warns on remaining `Debug.Log`). Rewrites `README.md` in English (quick start, controls, asset drop-in guide per the anchor contract) and writes `Docs/STEAM.md` (SDK import, define enablement, steamcmd depot upload walkthrough, the editor-no-SDK / build-no-SDK / build-with-SDK test matrix).

**Owned files:**
- D `UnityGame/Assets/Editor/SayehSetupWizard.cs`
- N `UnityGame/Assets/Editor/ScreamerSetupWizard.cs`
- N `UnityGame/Assets/Editor/ScreamerBuildPipeline.cs`
- N `UnityGame/Assets/Editor/ScreamerStringAudit.cs`
- R `UnityGame/README.md`
- N `UnityGame/Docs/STEAM.md`

**Exposes:**

```csharp
// All editor-only, inside Assets/Editor (no #if needed beyond SCREAMER_STEAM where relevant)
public static class ScreamerSetupWizard
{
    public const string Root = "Assets/Screamer";
    [UnityEditor.MenuItem("Screamer/Build Everything")] public static void BuildEverything();
}
public static class ScreamerBuildPipeline
{
    [UnityEditor.MenuItem("Screamer/Build Windows x64")] public static void BuildWindows();
    [UnityEditor.MenuItem("Screamer/Build Windows x64 (Steam)")] public static void BuildWindowsSteam();
}
public static class ScreamerStringAudit
{
    public static bool AuditProject(out string report);   // also run by both build menu items; build fails on violations
    [UnityEditor.MenuItem("Screamer/Audit Shipped Strings")] public static void AuditMenu();
}
```

**Consumes:** every factory listed in modules 1-7 (`HouseFactory.BuildAll`, `CharacterFactory.*`, `TaskFactory.*`, `RoundFlowFactory.BuildNetworkAndManagers`, `BotFactory.Install`, `AudioFactory.Install`, `FeelFactory.Install`, `UiFactory.BuildAll`), `HouseLayout` (spawns), `ScreamerPalette.LitShader/MakeRuntimeMaterial`, `TaskManager`/`EscapeDoor` for final wiring. It creates nothing gameplay-specific itself; if a factory is missing something, that factory's module owns the fix.

---

## 10. Explicit Contract Breaks (the only permitted ones)

| Old (current code) | New (binding) |
|---|---|
| `GameManager.GameState { Lobby, Playing, SurvivorsWin, MonsterWins }` | `{ Lobby, Countdown, Lockdown, Playing, Finale, Results }` + `SurvivorsWon` NetworkVariable |
| `GameManager.ServerPlayerCaught/ServerPlayerEscaped(ulong clientId)` | `ServerActorCaught/ServerActorEscaped(ulong actorId)` |
| 2-player minimum gate in `StartRound()` | removed; 1 human + bot fill |
| `NoiseSystem.MakeNoise(Vector3, float, string)` | `MakeNoise(ulong sourceActorId, Vector3, float, NoiseType, string)`; `NoiseClientRpc` broadcast replaced by server-culled targeted delivery |
| `GameUI` API surface (`ShowRoleReveal(bool)`, text counter, center arrow) | expanded per module 4; padlock icons replace "3/6"; edge smear replaces the arrow |
| `TaskBase.funnyDescription` | `flavorText`; all task defaults English via `GameCopy` |
| `EscapeDoor` plain MonoBehaviour, `Unlock()` client-local | NetworkBehaviour with `PadlocksBroken/Unlocked/FinaleMeter/Open` and finale RPC |
| `ArabicText.Fix(...)` call sites | deleted; direct assignment |
| `SayehBootstrap`, `SayehSetupWizard`, `Sayeh/` menu, `Assets/Sayeh` | `ScreamerBootstrap`, `ScreamerSetupWizard`, `Screamer/` menu, `Assets/Screamer` |
| `GhostFly` class nested in `PlayerController.cs` | `GhostController.cs` (one class per file) |
| `TaskChicken` leash 15 | 11 (GDD 4.3) |

Everything else evolves in place: `ServerFreeze` server-time sync, owner-authoritative `ClientNetworkTransform`, the attack raycast, `TaskBase` progress-multiplier pattern, skin selection sync, task-sound NetworkVariables.

---

## 11. Integration Notes

- **Merge order** (when the parallel branches land): 1 -> 2 -> 3 -> 6 -> 7 -> 4 -> 5 -> 8. Module 8 runs the wizard last and files integration bugs against the owning module's factory.
- **Engineering order inside each module** follows GDD section 14: the rematch reset path and English rewrite are each module's first commit on its own files.
- **Quality bar:** GTX 950-class laptop holds 60 fps on MEDIUM; every screen readable at a 720p30 re-encode; `MonsterRed` appears only when the Monster is relevant.
- **Cut order if slipping:** `HouseMoodDirector` first, then the ghost Boo. Never the finale.
- **Testing without peers:** every module must run in a scene containing only its own factory output plus stubbed singletons; stubs are never committed (rule 0.13).
