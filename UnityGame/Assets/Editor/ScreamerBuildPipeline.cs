using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Release builds for SCREAMER (menu: Screamer/Build Windows x64 and
/// Screamer/Build Windows x64 (Steam)).
///
/// Both menu items run the shipped-string audit first and refuse to build a
/// non-compliant product (GDD 14.4). The Steam variant toggles the
/// SCREAMER_STEAM scripting define for the duration of the build via
/// PlayerSettings.SetScriptingDefineSymbolsForGroup and restores the
/// project's previous defines afterwards, so the working copy never drifts.
/// The plain variant strips the define for its build the same way, keeping
/// the UnityTransport path the first-class, always-green configuration.
///
/// Outputs:
///   Builds/Windows/SCREAMER.exe        (UnityTransport only)
///   Builds/WindowsSteam/SCREAMER.exe   (Steam lobbies + P2P when the SDK is present)
///
/// The Steam SDK itself is never committed to this repository; see
/// Docs/STEAM.md for the import, testing and depot-upload walkthrough.
/// </summary>
public static class ScreamerBuildPipeline
{
    const string SteamDefine = "SCREAMER_STEAM";
    const string OutputRoot = "Builds";
    const string ExecutableName = "SCREAMER.exe";
    const string WizardScenePath = ScreamerSetupWizard.Root + "/Scenes/Game.unity";

    [MenuItem("Screamer/Build Windows x64")]
    public static void BuildWindows()
    {
        Build(steam: false);
    }

    [MenuItem("Screamer/Build Windows x64 (Steam)")]
    public static void BuildWindowsSteam()
    {
        Build(steam: true);
    }

    // ------------------------- Core build -------------------------

    static void Build(bool steam)
    {
        // 1. Audit. A failed audit is a failed build - no exceptions.
        bool auditPassed = ScreamerStringAudit.AuditProject(out string auditReport);
        if (auditPassed)
        {
            Debug.Log(auditReport);
        }
        else
        {
            Debug.LogError(auditReport);
            EditorUtility.DisplayDialog("SCREAMER build blocked",
                "The shipped-string audit failed, so the build was not started.\n\n" +
                "See the Console for the full report, fix every FAIL entry, then build again.",
                "OK");
            return;
        }

        // 2. Scenes. The wizard owns the scene list; building before running it
        // is a user error worth a clear message, not a cryptic Unity failure.
        string[] scenes = EnabledScenePaths();
        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog("SCREAMER build blocked",
                "No scenes are enabled in the build settings.\n\n" +
                "Run Screamer > Build Everything first - it creates " + WizardScenePath +
                " and registers it for the build.",
                "OK");
            return;
        }

        // 3. Steam preflight. With the define on but no Steamworks SDK in the
        // project, the Steam layer cannot compile; say so before burning a
        // build attempt. The check is advisory - a custom wrapper may resolve
        // the API differently, so the user may proceed deliberately.
        if (steam && !SteamSdkPresent())
        {
            bool proceed = EditorUtility.DisplayDialog("Steamworks SDK not detected",
                "No Steamworks.SteamAPI type is loaded in this project, so a build with " +
                SteamDefine + " enabled will most likely fail to compile.\n\n" +
                "Import the Steamworks.NET package first (see Docs/STEAM.md), or build anyway " +
                "if you know your Steam wrapper resolves differently.",
                "Build Anyway", "Cancel");
            if (!proceed) return;
        }

        // 4. Defines: toggle for this build only, restore no matter what.
        BuildTargetGroup group = BuildTargetGroup.Standalone;
        string originalDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
        string buildDefines = steam
            ? AddDefine(originalDefines, SteamDefine)
            : RemoveDefine(originalDefines, SteamDefine);

        string exePath = Path.Combine(OutputRoot, steam ? "WindowsSteam" : "Windows", ExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(exePath));

        BuildReport report;
        try
        {
            if (buildDefines != originalDefines)
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, buildDefines);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            report = BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            if (buildDefines != originalDefines)
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, originalDefines);
        }

        // 5. Verdict.
        BuildSummary summary = report.summary;
        string label = steam ? "Windows x64 (Steam)" : "Windows x64";

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log("SCREAMER build succeeded: " + label + " -> " + exePath +
                " (" + (summary.totalSize / (1024 * 1024)) + " MB, " +
                summary.totalTime.TotalSeconds.ToString("F0") + " s)");

            bool reveal = EditorUtility.DisplayDialog("SCREAMER build succeeded",
                label + " is ready:\n\n" + exePath +
                (steam ? "\n\nRemember: do not ship steam_appid.txt inside the depot (Docs/STEAM.md)." : ""),
                "Show In Folder", "Close");
            if (reveal) EditorUtility.RevealInFinder(exePath);
        }
        else
        {
            Debug.LogError("SCREAMER build failed: " + label + " (result: " + summary.result +
                ", errors: " + summary.totalErrors + "). See the Console and Editor log.");
            EditorUtility.DisplayDialog("SCREAMER build failed",
                label + " did not build (result: " + summary.result + ").\n\n" +
                "Check the Console for compile or packaging errors." +
                (steam ? "\nA Steam build additionally needs the Steamworks SDK imported (Docs/STEAM.md)." : ""),
                "OK");
        }
    }

    // ------------------------- Helpers -------------------------

    static string[] EnabledScenePaths()
    {
        var paths = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                paths.Add(scene.path);

        // Graceful fallback: the wizard scene exists but the list was cleared.
        if (paths.Count == 0 && File.Exists(WizardScenePath))
        {
            Debug.Log("ScreamerBuildPipeline: build settings were empty; falling back to " + WizardScenePath + ".");
            paths.Add(WizardScenePath);
        }
        return paths.ToArray();
    }

    /// <summary>True when a Steamworks wrapper (Steamworks.SteamAPI) is loaded in the editor domain.</summary>
    static bool SteamSdkPresent()
    {
        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (assembly.GetType("Steamworks.SteamAPI", false) != null)
                    return true;
            }
            catch
            {
                // A reflection-hostile assembly is not the SDK; keep looking.
            }
        }
        return false;
    }

    static string AddDefine(string defines, string define)
    {
        var parts = SplitDefines(defines);
        if (!parts.Contains(define)) parts.Add(define);
        return string.Join(";", parts);
    }

    static string RemoveDefine(string defines, string define)
    {
        var parts = SplitDefines(defines);
        parts.Remove(define);
        return string.Join(";", parts);
    }

    static List<string> SplitDefines(string defines)
    {
        var parts = new List<string>();
        foreach (string raw in (defines ?? string.Empty).Split(';'))
        {
            string trimmed = raw.Trim();
            if (trimmed.Length > 0 && !parts.Contains(trimmed))
                parts.Add(trimmed);
        }
        return parts;
    }
}
