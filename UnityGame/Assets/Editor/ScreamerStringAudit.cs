using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The shipped-string audit (GDD 1 language mandate, GDD 14.4 build checks).
///
/// SCREAMER ships in sharp American-indie English and nothing else. This
/// audit scans every SHIPPED text asset - runtime C# scripts and scene files
/// under Assets/ - and:
///
///   FAILS on any character outside basic Latin + common punctuation
///          (printable ASCII plus tab/newline), anywhere in a shipped file;
///   FAILS on any surviving reference to the deleted ArabicText type;
///   WARNS on Debug.Log calls that remain in shipped code.
///
/// Editor-only code (any folder named "Editor") never ships, so it is exempt.
/// Both build menu items run this audit first and refuse to build on FAIL.
/// </summary>
public static class ScreamerStringAudit
{
    const string ForbiddenType = "ArabicText";
    const int MaxEntriesPerFile = 5;

    /// <summary>
    /// Audits every shipped script and scene under Assets/. Returns true when
    /// the project is clean enough to ship (warnings allowed, failures not);
    /// <paramref name="report"/> always carries the full human-readable result.
    /// </summary>
    public static bool AuditProject(out string report)
    {
        var failures = new List<string>();
        var warnings = new List<string>();
        int scannedFiles = 0;

        foreach (string path in ShippedScriptPaths())
        {
            scannedFiles++;
            AuditScript(path, failures, warnings);
        }

        foreach (string path in ScenePaths())
        {
            scannedFiles++;
            AuditScene(path, failures, warnings);
        }

        var builder = new StringBuilder();
        builder.Append("SCREAMER shipped-string audit: ");
        builder.Append(failures.Count == 0 ? "PASS" : "FAIL");
        builder.Append(" (").Append(scannedFiles).Append(" files, ")
               .Append(failures.Count).Append(" failures, ")
               .Append(warnings.Count).Append(" warnings)");

        if (failures.Count > 0)
        {
            builder.Append("\n\nFAIL - these block every build:");
            foreach (string failure in failures)
                builder.Append("\n  ").Append(failure);
        }

        if (warnings.Count > 0)
        {
            builder.Append("\n\nWARN - allowed, but clean them up before release:");
            foreach (string warning in warnings)
                builder.Append("\n  ").Append(warning);
        }

        report = builder.ToString();
        return failures.Count == 0;
    }

    [MenuItem("Screamer/Audit Shipped Strings")]
    public static void AuditMenu()
    {
        bool passed = AuditProject(out string report);

        if (passed) Debug.Log(report);
        else Debug.LogError(report);

        EditorUtility.DisplayDialog("Shipped-string audit",
            passed
                ? "PASS - every shipped string is basic Latin and no ArabicText reference survives.\n\nThe full report is in the Console."
                : "FAIL - non-Latin characters or forbidden types remain in shipped files.\n\nThe full report is in the Console; builds are blocked until it passes.",
            "OK");
    }

    // ------------------------- File enumeration -------------------------

    /// <summary>Runtime scripts only: everything under Assets/ except Editor folders.</summary>
    static IEnumerable<string> ShippedScriptPaths()
    {
        foreach (string path in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories))
            if (!IsEditorPath(path))
                yield return Normalize(path);
    }

    static IEnumerable<string> ScenePaths()
    {
        foreach (string path in Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories))
            yield return Normalize(path);
    }

    static bool IsEditorPath(string path)
    {
        foreach (string segment in Normalize(path).Split('/'))
            if (segment == "Editor")
                return true;
        return false;
    }

    static string Normalize(string path) => path.Replace('\\', '/');

    // ------------------------- Per-file audits -------------------------

    static void AuditScript(string path, List<string> failures, List<string> warnings)
    {
        string[] lines = ReadLinesSafe(path, failures);
        if (lines == null) return;

        int nonLatinReported = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int lineNumber = i + 1;

            // 1. Basic-Latin discipline over the whole file: comments snitch
            // on the project just as loudly as string literals do.
            int column = FirstDisallowedColumn(line);
            if (column >= 0)
            {
                nonLatinReported++;
                if (nonLatinReported <= MaxEntriesPerFile)
                    failures.Add(path + ":" + lineNumber + ":" + (column + 1) +
                        " non-Latin character " + Describe(line[column]));
            }

            // 2. The deleted ArabicText type must not be referenced anywhere.
            if (line.Contains(ForbiddenType))
                failures.Add(path + ":" + lineNumber + " references forbidden type '" + ForbiddenType + "'");

            // 3. Debug.Log left in shipped code: warn (covers Log/LogWarning/LogError/LogFormat...).
            if (line.Contains("Debug.Log"))
                warnings.Add(path + ":" + lineNumber + " shipped Debug.Log call");
        }

        if (nonLatinReported > MaxEntriesPerFile)
            failures.Add(path + " ... and " + (nonLatinReported - MaxEntriesPerFile) + " more non-Latin characters");
    }

    static void AuditScene(string path, List<string> failures, List<string> warnings)
    {
        string[] lines = ReadLinesSafe(path, failures);
        if (lines == null) return;

        // Scenes must be text-serialized YAML to be auditable; a binary scene
        // cannot be inspected line by line, so flag it instead of guessing.
        if (lines.Length == 0 || !lines[0].StartsWith("%YAML"))
        {
            warnings.Add(path + " is not text-serialized; switch Asset Serialization to Force Text so scenes can be audited");
            return;
        }

        int nonLatinReported = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            int column = FirstDisallowedColumn(lines[i]);
            if (column < 0) continue;

            nonLatinReported++;
            if (nonLatinReported <= MaxEntriesPerFile)
                failures.Add(path + ":" + (i + 1) + ":" + (column + 1) +
                    " non-Latin character " + Describe(lines[i][column]) + " in scene data");
        }

        if (nonLatinReported > MaxEntriesPerFile)
            failures.Add(path + " ... and " + (nonLatinReported - MaxEntriesPerFile) + " more non-Latin characters");
    }

    // ------------------------- Character policy -------------------------

    /// <summary>
    /// Index of the first character outside the shipping alphabet (printable
    /// ASCII 0x20-0x7E plus tab), or -1 when the line is clean. Line endings
    /// are already stripped by the reader.
    /// </summary>
    static int FirstDisallowedColumn(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            bool allowed = c == '\t' || (c >= ' ' && c <= '~');
            if (!allowed) return i;
        }
        return -1;
    }

    static string Describe(char c)
    {
        return "U+" + ((int)c).ToString("X4");
    }

    static string[] ReadLinesSafe(string path, List<string> failures)
    {
        try
        {
            return File.ReadAllLines(path);
        }
        catch (IOException e)
        {
            failures.Add(path + " could not be read for auditing (" + e.GetType().Name + ")");
            return null;
        }
        catch (System.UnauthorizedAccessException)
        {
            failures.Add(path + " could not be read for auditing (access denied)");
            return null;
        }
    }
}
