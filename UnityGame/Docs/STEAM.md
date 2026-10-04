# SCREAMER - Steam Layer Guide

How to enable, test and ship the optional Steam layer. The layer is strictly conditional: **every Steam line of code sits behind `#if SCREAMER_STEAM`**, the SDK is **never committed to this repository**, and the project must always compile and run green with both absent. UnityTransport (direct IP) is the first-class, forever-supported path; Steam is a bonus on top of it.

## 1. Architecture Recap

- `INetworkBackend` (`Assets/Scripts/Net/INetworkBackend.cs`) is the seam: `Host()`, `Join(address)`, `Shutdown()`, `LobbyCode`, invite support. (Player-list changes are read from the synchronized `GameManager.Roster`, not from the seam.)
- `UtpBackend` implements it over UnityTransport. Always compiled. The CI/compile/test path.
- `SteamBackend` + `SteamIntegration` (lobbies, P2P, rich presence, achievements) exist **only** inside `#if SCREAMER_STEAM`.
- `BackendSelector.Active` picks the Steam backend when the define is present **and** `SteamAPI.Init()` succeeds at startup; any failure falls back to UTP silently. Players without Steam running still get direct-IP play.

What the layer adds when active:

- Steam lobbies: hosting creates a friends-only lobby whose numeric id becomes the room code, and the lobby screen gains an INVITE FRIENDS button that opens the Steam overlay's friends invite dialog. The menu's `TRACKING: MANUAL` field stays and accepts either a pasted Steam lobby id or a direct `ip:port`, so mixed setups keep working.
- Rich presence: `Burning noodles in SCREAMER`, `Being a chair in SCREAMER`, `Screaming (therapy) in SCREAMER`.
- Launch achievements: `THERAPY COMPLETE`, `IT WAS JUST A CHAIR`, `PACIFIST CHICKEN`, `FINAL GIRL`, `NATURE IS HEALING`.

## 2. Enabling the SDK

1. **Import Steamworks.NET.** Download the `.unitypackage` release from the Steamworks.NET project (use a release that supports Unity 2022.3) and import it via *Assets > Import Package > Custom Package*. Import into `Assets/` only - do **not** add anything to `Packages/manifest.json`; the manifest stays untouched by project rule.
2. **App ID for testing.** Steamworks.NET creates `steam_appid.txt` in the project root on first run; set it to your App ID, or `480` (Spacewar) before your own App ID exists. The Steam client must be running and logged in.
3. **Enable the define.** Two supported ways:
   - **For release builds (recommended):** just use **Screamer > Build Windows x64 (Steam)**. It toggles `SCREAMER_STEAM` via the Player scripting defines for the duration of the build and restores your project defines afterwards. Your working copy stays define-free and always compiles without the SDK.
   - **For in-editor Steam testing:** *Edit > Project Settings > Player > Other Settings > Scripting Define Symbols*, add `SCREAMER_STEAM` to the Standalone group. Remove it again before committing work; the repository norm is define-off.
4. **Verify.** Enter Play mode with the define on and Steam running: hosting produces a numeric Steam lobby id as the room code (instead of `ip:port`), the lobby screen shows the INVITE FRIENDS overlay button, and a second account joins by pasting that lobby id into the `TRACKING: MANUAL` field (or by accepting an overlay invite).

If `SteamAPI.Init()` fails (no client, no `steam_appid.txt`, wrong account), the game logs the fallback and continues on UnityTransport - this is expected behavior, not an error state.

## 3. Building the Steam Variant

**Screamer > Build Windows x64 (Steam)** runs the shipped-string audit, warns if no Steamworks types are loaded (a Steam build without the SDK cannot compile), builds to `Builds/WindowsSteam/SCREAMER.exe`, and restores your previous scripting defines whether the build succeeds or fails.

Checklist for the depot payload:

- `SCREAMER.exe`, `SCREAMER_Data/`, `UnityPlayer.dll`, the Mono folder - everything Unity put in `Builds/WindowsSteam/`.
- `steam_api64.dll` must sit next to the exe (Steamworks.NET's post-build step normally copies it; verify).
- **Do NOT ship `steam_appid.txt` inside the depot.** It is a local development override; shipping it breaks ownership checks.

## 4. Uploading a Depot With steamcmd

One-time Steamworks dashboard setup: create the App, one Windows depot (depot ID is usually `<appid> + 1`), and a default launch option pointing at `SCREAMER.exe`.

1. Download the Steamworks SDK zip (partner site) and unzip. The uploader lives in `sdk/tools/ContentBuilder/`.
2. Lay out the content and scripts:

```
sdk/tools/ContentBuilder/
  content/
    windows64/           <- copy the full contents of Builds/WindowsSteam/ here
  scripts/
    app_build_YOURAPPID.vdf
    depot_build_YOURDEPOTID.vdf
  output/                <- build logs and the upload cache land here
```

3. `scripts/depot_build_YOURDEPOTID.vdf`:

```
"DepotBuildConfig"
{
    "DepotID" "YOURDEPOTID"
    "ContentRoot" "../content/windows64/"
    "FileMapping"
    {
        "LocalPath" "*"
        "DepotPath" "."
        "recursive" "1"
    }
    "FileExclusion" "steam_appid.txt"
    "FileExclusion" "*.pdb"
}
```

4. `scripts/app_build_YOURAPPID.vdf`:

```
"AppBuild"
{
    "AppID" "YOURAPPID"
    "Desc" "SCREAMER build - <date, short changelist>"
    "BuildOutput" "../output/"
    "ContentRoot" "../content/"
    "SetLive" ""        // leave empty; set branches live from the dashboard
    "Depots"
    {
        "YOURDEPOTID" "depot_build_YOURDEPOTID.vdf"
    }
}
```

5. Upload (from `sdk/tools/ContentBuilder/builder/` on Windows):

```
steamcmd.exe +login YOUR_BUILD_ACCOUNT +run_app_build ../scripts/app_build_YOURAPPID.vdf +quit
```

Use a dedicated build account with the *Edit App Metadata* and *Publish App Changes To Steam* permissions and Steam Guard pre-authorized on the build machine.

6. **Set the build live:** Steamworks dashboard > your app > *SteamPipe > Builds*, pick the uploaded build, set it live on `default` (or a `beta` branch first - recommended). Install through the Steam client and run a full round before touching `default`.

## 5. Release Test Matrix (binding - run all three before every release)

| # | Configuration | How to produce it | Must pass |
|---|---|---|---|
| 1 | **Editor, no SDK** | Fresh checkout, no Steamworks import, no define | Project compiles with zero errors; Play mode hosts and joins over UnityTransport; a full round (lobby, morph, chores, finale, results, rematch) works against bots |
| 2 | **Build, no SDK** | **Screamer > Build Windows x64** | String audit passes; two instances host/join by IP; rematch loops without an app restart; no Steam DLLs in the output folder |
| 3 | **Build, with SDK** | Import SDK, **Screamer > Build Windows x64 (Steam)** | `BackendSelector` picks Steam with the client running; lobby-id paste joins and overlay INVITE FRIENDS joins work; rich presence and achievements fire; with the Steam client closed the same exe still falls back to direct IP |

Any matrix failure blocks the release. Rows 1 and 2 protect the promise that the Steam layer is optional; row 3 protects the paying customer.

## 6. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Compile errors mentioning `Steamworks` after enabling the define | The SDK is not imported (or was imported into a different folder). Import Steamworks.NET, or remove `SCREAMER_STEAM` from the Player defines. |
| `SteamAPI.Init()` returns false in the editor | Steam client not running, not logged in, or `steam_appid.txt` missing/wrong. The game falls back to UnityTransport by design. |
| `DllNotFoundException: steam_api64` in a build | `steam_api64.dll` is not next to the exe. Copy it from the Steamworks.NET plugins folder. |
| Overlay/invites do not appear | The overlay needs the game launched through Steam (or the client running with the app registered). Test invite flows from a Steam-launched install. |
| Players report ownership errors on the shipped build | `steam_appid.txt` leaked into the depot. Add the `FileExclusion` above, rebuild the depot. |
| The Steam build works but the plain build fails to compile | Steam code leaked outside `#if SCREAMER_STEAM`. Fix the guard; matrix row 2 exists to catch exactly this. |
