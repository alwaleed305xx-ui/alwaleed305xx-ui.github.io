using UnityEngine;

/// <summary>
/// Runtime-compilable factory for the audio layer; the editor wizard calls
/// <see cref="Install"/> once while assembling the scene (never referencing
/// UnityEditor), and a play-mode call works for isolated module tests.
///
/// Creates one "AudioDirector" object carrying <see cref="AudioDirector"/>
/// (which synthesizes the whole procedural bank in its Awake, at play time)
/// and <see cref="AmbienceController"/> (room tone, dread layer, body foley).
/// Nothing non-serializable is created at edit time, so the saved scene stays
/// clean.
/// </summary>
public static class AudioFactory
{
    public static void Install()
    {
        if (Object.FindObjectOfType<AudioDirector>() != null)
            return; // already installed in this scene

        var go = new GameObject("AudioDirector");
        go.AddComponent<AudioDirector>();
        go.AddComponent<AmbienceController>();
    }
}
