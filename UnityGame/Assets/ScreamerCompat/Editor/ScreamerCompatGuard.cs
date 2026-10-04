using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

/// <summary>
/// Keeps the project compiling no matter which Asset Store packs are imported.
///
/// Lives in its own assembly with no references so it survives when every
/// other assembly fails. Two jobs:
///
/// 1. Version rules. The Unity Gaming Services building blocks (Player
///    Account, Multiplayer Session, Matchmaker, Leaderboards, Achievements)
///    use Unity 6 UI Toolkit APIs (Tab, UxmlAttribute). On an older editor
///    the Assets/Blocks folder is parked as Assets/Blocks~ (Unity ignores
///    folders ending in ~) and com.unity.services.multiplayer, which they
///    pull in and which does not build against Netcode 1.x, is dropped from
///    the manifest. On Unity 6 the folder is restored and the package is
///    added back, so the F1 widget panel comes alive again.
///
/// 2. Compiler feedback. Any script under an imported pack that still fails
///    is parked as .cs~ and any package that fails is removed from the
///    manifest, then the project refreshes. Everything is logged, nothing is
///    deleted: rename a parked file back to undo.
///
/// The same rules exist as Tools/FixMyAssets.ps1 (run by FIX_MY_ASSETS.bat)
/// for the case where the editor cannot load any new script at all.
/// </summary>
[InitializeOnLoad]
public static class ScreamerCompatGuard
{
    const string BlocksPath = "Assets/Blocks";
    const string ParkedBlocksPath = "Assets/Blocks~";
    const string ManifestPath = "Packages/manifest.json";
    const string MultiplayerServicesPackage = "com.unity.services.multiplayer";
    const string PassKey = "Screamer.Compat.Passes";
    const int MaxPasses = 4;

    static readonly string[] ProtectedRoots =
    {
        "Assets/Scripts", "Assets/Editor", "Assets/Screamer", "Assets/ScreamerCompat"
    };

    static readonly string[] ProtectedPackages =
    {
        "com.unity.netcode.gameobjects", "com.unity.transport", "com.unity.ugui",
        "com.unity.collections", "com.unity.burst", "com.unity.mathematics",
        "com.unity.modules."
    };

    static readonly HashSet<string> failingFiles = new HashSet<string>();

    static ScreamerCompatGuard()
    {
        CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
        CompilationPipeline.compilationFinished += OnCompilationFinished;
        EditorApplication.delayCall += () => ApplyVersionRules(verbose: false);
    }

    [MenuItem("Screamer/Fix Compile Errors From Asset Packs")]
    public static void FixNow()
    {
        SessionState.SetInt(PassKey, 0);
        bool changed = ApplyVersionRules(verbose: true);
        changed |= ParkFailures(CollectErrorsFromEditorLog(), verbose: true);
        if (changed) AssetDatabase.Refresh();
        else EditorUtility.DisplayDialog("Fix Compile Errors",
            "Nothing to fix: no failing pack scripts or packages were found.", "OK");
    }

    // ------------------------- Version rules -------------------------

    static bool ApplyVersionRules(bool verbose)
    {
        bool changed = false;
#if UNITY_6000_0_OR_NEWER
        if (Directory.Exists(ParkedBlocksPath) && !Directory.Exists(BlocksPath))
        {
            Directory.Move(ParkedBlocksPath, BlocksPath);
            Debug.Log("SCREAMER compat: Unity 6 detected, restored " + BlocksPath + " (UGS building blocks).");
            changed = true;
        }
        if (!ManifestHas(MultiplayerServicesPackage) && Directory.Exists(BlocksPath))
        {
            UnityEditor.PackageManager.Client.Add(MultiplayerServicesPackage);
            Debug.Log("SCREAMER compat: re-adding " + MultiplayerServicesPackage + ".");
        }
#else
        if (Directory.Exists(BlocksPath))
        {
            ParkFolder(BlocksPath, ParkedBlocksPath);
            Debug.LogWarning("SCREAMER compat: the UGS building blocks need Unity 6, so " + BlocksPath +
                             " was parked as " + ParkedBlocksPath + ". Open the project in Unity 6 and they come back by themselves.");
            changed = true;
        }
        if (RemoveFromManifest(MultiplayerServicesPackage))
        {
            Debug.LogWarning("SCREAMER compat: removed " + MultiplayerServicesPackage +
                             " (only the Unity 6 building blocks use it; it does not build on this editor).");
            changed = true;
        }
#endif
        if (changed) AssetDatabase.Refresh();
        else if (verbose) Debug.Log("SCREAMER compat: version rules already satisfied.");
        return changed;
    }

    // ------------------------- Compiler feedback -------------------------

    static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
    {
        foreach (CompilerMessage message in messages)
        {
            if (message.type != CompilerMessageType.Error) continue;
            if (string.IsNullOrEmpty(message.file)) continue;
            failingFiles.Add(message.file.Replace('\\', '/'));
        }
    }

    static void OnCompilationFinished(object context)
    {
        if (failingFiles.Count == 0) return;
        var files = new List<string>(failingFiles);
        failingFiles.Clear();

        int passes = SessionState.GetInt(PassKey, 0);
        if (passes >= MaxPasses)
        {
            Debug.LogWarning("SCREAMER compat: gave up after " + MaxPasses + " passes; use Screamer > Fix Compile Errors From Asset Packs.");
            return;
        }

        EditorApplication.delayCall += () =>
        {
            if (ParkFailures(files, verbose: false))
            {
                SessionState.SetInt(PassKey, passes + 1);
                AssetDatabase.Refresh();
            }
        };
    }

    /// <summary>Parks failing pack scripts and removes failing packages. Returns true when anything changed.</summary>
    static bool ParkFailures(IEnumerable<string> files, bool verbose)
    {
        bool changed = false;
        var packages = new HashSet<string>();

        foreach (string raw in files)
        {
            string file = raw.Replace('\\', '/');
            int cache = file.IndexOf("PackageCache/", StringComparison.OrdinalIgnoreCase);
            if (cache >= 0)
            {
                string rest = file.Substring(cache + "PackageCache/".Length);
                int at = rest.IndexOf('@');
                if (at > 0) packages.Add(rest.Substring(0, at));
                continue;
            }

            int assets = file.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            if (assets < 0) continue;
            file = file.Substring(assets);
            if (IsProtected(file)) continue;
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(file)) continue;

            File.Move(file, file + "~");
            if (File.Exists(file + ".meta")) File.Delete(file + ".meta");
            Debug.LogWarning("SCREAMER compat: parked failing pack script " + file + " as " + file + "~ (rename it back to undo).");
            changed = true;
        }

        foreach (string package in packages)
        {
            if (IsProtectedPackage(package)) continue;
            if (!RemoveFromManifest(package)) continue;
            Debug.LogWarning("SCREAMER compat: removed package " + package + " because it does not build on this editor.");
            changed = true;
        }

        if (verbose && !changed) Debug.Log("SCREAMER compat: no pack scripts or packages needed parking.");
        return changed;
    }

    /// <summary>Error lines from the current Editor.log, for the manual menu item.</summary>
    static List<string> CollectErrorsFromEditorLog()
    {
        var result = new List<string>();
        string logPath = Application.platform == RuntimePlatform.WindowsEditor
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Logs", "Unity", "Editor.log");
        if (!File.Exists(logPath)) return result;

        string[] lines;
        try
        {
            using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
                lines = reader.ReadToEnd().Split('\n');
        }
        catch (Exception) { return result; }

        var pattern = new Regex(@"^(.+?\.cs)\(\d+,\d+\): error CS", RegexOptions.IgnoreCase);
        int start = Mathf.Max(0, lines.Length - 2500);
        for (int i = start; i < lines.Length; i++)
        {
            Match match = pattern.Match(lines[i].Trim());
            if (match.Success) result.Add(match.Groups[1].Value);
        }
        return result;
    }

    // ------------------------- Helpers -------------------------

    static void ParkFolder(string from, string to)
    {
        if (Directory.Exists(to))
        {
            foreach (string dir in Directory.GetDirectories(from))
            {
                string target = Path.Combine(to, Path.GetFileName(dir));
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(dir, target);
            }
            foreach (string file in Directory.GetFiles(from))
            {
                string target = Path.Combine(to, Path.GetFileName(file));
                if (File.Exists(target)) File.Delete(target);
                File.Move(file, target);
            }
            Directory.Delete(from, true);
        }
        else
        {
            Directory.Move(from, to);
        }
        if (File.Exists(from + ".meta")) File.Delete(from + ".meta");
    }

    static bool ManifestHas(string package)
    {
        return File.Exists(ManifestPath) && File.ReadAllText(ManifestPath).Contains("\"" + package + "\"");
    }

    static bool RemoveFromManifest(string package)
    {
        if (!File.Exists(ManifestPath)) return false;
        string json = File.ReadAllText(ManifestPath);
        var entry = new Regex("\\s*\"" + Regex.Escape(package) + "\"\\s*:\\s*\"[^\"]*\"\\s*,?");
        if (!entry.IsMatch(json)) return false;

        json = entry.Replace(json, "");
        json = Regex.Replace(json, ",(\\s*})", "$1"); // no trailing comma on the last entry
        File.WriteAllText(ManifestPath, json);
        return true;
    }

    static bool IsProtected(string assetPath)
    {
        foreach (string root in ProtectedRoots)
            if (assetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static bool IsProtectedPackage(string package)
    {
        foreach (string protectedPackage in ProtectedPackages)
            if (package.StartsWith(protectedPackage, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
