using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates every clip in the game at boot, straight from the GDD 13.2
/// synthesis table: one method per clip, mono 44.1 kHz, built with
/// <see cref="AudioSynth"/> primitives and handed to <see cref="AudioDirector"/>
/// as the single clip table. Real recorded assets later replace this bank by
/// swapping the director's table - nothing else in the project ever knows the
/// sounds were ever fake.
///
/// Module-internal: only AudioDirector calls <see cref="Build"/>.
/// </summary>
static class ProceduralAudioBank
{
    /// <summary>Synthesizes the complete 24-clip bank. Takes a few dozen milliseconds, once.</summary>
    public static Dictionary<Sfx, AudioClip> Build()
    {
        return new Dictionary<Sfx, AudioClip>
        {
            { Sfx.Scream, BuildScream() },
            { Sfx.ChickenSquawk, BuildChickenSquawk() },
            { Sfx.FeedbackScreech, BuildFeedbackScreech() },
            { Sfx.SmokeAlarm, BuildSmokeAlarm() },
            { Sfx.KettleWhine, BuildKettleWhine() },
            { Sfx.SlideWhistle, BuildSlideWhistle() },
            { Sfx.BassKick, BuildBassKick() },
            { Sfx.Chime, BuildChime() },
            { Sfx.Glorp, BuildGlorp() },
            { Sfx.GeyserBurst, BuildGeyserBurst() },
            { Sfx.GlassBreak, BuildGlassBreak() },
            { Sfx.UiThunk, BuildUiThunk() },
            { Sfx.StampThunk, BuildStampThunk() },
            { Sfx.DoorExplosion, BuildDoorExplosion() },
            { Sfx.PipeOrganSting, BuildPipeOrganSting() },
            { Sfx.MonsterDrone, BuildMonsterDrone() },
            { Sfx.ShuffleThump, BuildShuffleThump() },
            { Sfx.Heartbeat, BuildHeartbeat() },
            { Sfx.DenAmbience, BuildDenAmbience() },
            { Sfx.YardAmbience, BuildYardAmbience() },
            { Sfx.Footstep, BuildFootstep() },
            { Sfx.Breathing, BuildBreathing() },
            { Sfx.PatPat, BuildPatPat() },
            { Sfx.BooSting, BuildBooSting() }
        };
    }

    /// <summary>Clips that are meant to play on looping sources.</summary>
    public static bool IsLoop(Sfx sfx)
    {
        switch (sfx)
        {
            case Sfx.SmokeAlarm:
            case Sfx.MonsterDrone:
            case Sfx.ShuffleThump:
            case Sfx.Heartbeat:
            case Sfx.DenAmbience:
            case Sfx.YardAmbience:
            case Sfx.Breathing:
                return true;
            default:
                return false;
        }
    }

    // ------------------------- Voices -------------------------

    /// <summary>Sawtooth sweep 300-900 Hz, 8 Hz vibrato at 30 Hz depth, ADSR 0.02/0.1/0.7/0.3. 0.6 s.</summary>
    static AudioClip BuildScream()
    {
        float[] buffer = AudioSynth.NewBuffer(0.6f);
        AudioSynth.AddSawSweep(buffer, 0f, 0.6f, 300f, 900f, 1f, 8f, 30f);
        AudioSynth.LowPass(buffer, 5500f); // take the raw saw buzz off the top
        AudioSynth.ApplyAdsr(buffer, 0.02f, 0.1f, 0.7f, 0.3f);
        AudioSynth.Normalize(buffer);
        return AudioSynth.ToClip("Sfx_Scream", buffer);
    }

    /// <summary>The scream again, x1.6 pitch and half length, twice, 60 ms apart. 0.35 s of outrage.</summary>
    static AudioClip BuildChickenSquawk()
    {
        const float squawkSeconds = 0.145f;
        float[] squawk = AudioSynth.NewBuffer(squawkSeconds);
        AudioSynth.AddSawSweep(squawk, 0f, squawkSeconds, 480f, 1440f, 1f, 13f, 48f);
        AudioSynth.LowPass(squawk, 7000f);
        AudioSynth.ApplyAdsr(squawk, 0.01f, 0.04f, 0.7f, 0.06f);

        float[] buffer = AudioSynth.NewBuffer(0.35f);
        AudioSynth.Stamp(buffer, squawk, 0f);
        AudioSynth.Stamp(buffer, squawk, squawkSeconds + 0.06f, 0.85f);
        AudioSynth.Normalize(buffer);
        return AudioSynth.ToClip("Sfx_ChickenSquawk", buffer);
    }

    /// <summary>Ring-mod: saw 700 Hz x sine 55 Hz, carrier sweeping up 400 Hz, hard attack. 0.8 s.</summary>
    static AudioClip BuildFeedbackScreech()
    {
        float[] buffer = AudioSynth.NewBuffer(0.8f);
        AudioSynth.AddSawSweep(buffer, 0f, 0.8f, 700f, 1100f, 1f);
        AudioSynth.RingModulate(buffer, 55f);
        AudioSynth.LowPass(buffer, 6500f);
        AudioSynth.ApplyAdsr(buffer, 0.005f, 0.1f, 0.8f, 0.25f);
        AudioSynth.Normalize(buffer);
        return AudioSynth.ToClip("Sfx_FeedbackScreech", buffer);
    }

    // ------------------------- Kitchen & plumbing -------------------------

    /// <summary>Square wave alternating 950/750 Hz every 0.25 s. 2 s loop; also works as a one-shot.</summary>
    static AudioClip BuildSmokeAlarm()
    {
        float[] buffer = AudioSynth.NewBuffer(2f);
        for (int segment = 0; segment < 8; segment++)
        {
            float start = segment * 0.25f;
            float freq = segment % 2 == 0 ? 950f : 750f;
            float[] blip = AudioSynth.NewBuffer(0.25f);
            AudioSynth.AddSquare(blip, 0f, 0.25f, freq, 0.6f);
            AudioSynth.FadeEdges(blip, 0.004f, 0.004f); // no clicks between blips
            AudioSynth.Stamp(buffer, blip, start);
        }
        AudioSynth.LowPass(buffer, 7000f);
        AudioSynth.Normalize(buffer, 0.7f);
        return AudioSynth.ToClip("Sfx_SmokeAlarm", buffer);
    }

    /// <summary>Sine rising 800-1600 Hz with a slow attack - the pot is thinking about betrayal. 1.5 s.</summary>
    static AudioClip BuildKettleWhine()
    {
        float[] buffer = AudioSynth.NewBuffer(1.5f);
        AudioSynth.AddSineSweep(buffer, 0f, 1.5f, 800f, 1600f, 1f, 6f, 8f);
        AudioSynth.ApplyAdsr(buffer, 0.9f, 0.2f, 0.9f, 0.1f);
        AudioSynth.Normalize(buffer, 0.65f);
        return AudioSynth.ToClip("Sfx_KettleWhine", buffer);
    }

    /// <summary>Sine falling 1200-300 Hz: the progress bar's sad rewind. 0.8 s.</summary>
    static AudioClip BuildSlideWhistle()
    {
        float[] buffer = AudioSynth.NewBuffer(0.8f);
        AudioSynth.AddSineSweep(buffer, 0f, 0.8f, 1200f, 300f, 1f);
        AudioSynth.FadeEdges(buffer, 0.02f, 0.1f);
        AudioSynth.Normalize(buffer);
        return AudioSynth.ToClip("Sfx_SlideWhistle", buffer);
    }

    /// <summary>Sine 180-60 Hz plus a noise-burst tail. The toilet approves. 0.4 s.</summary>
    static AudioClip BuildGlorp()
    {
        float[] buffer = AudioSynth.NewBuffer(0.4f);
        AudioSynth.AddSineSweep(buffer, 0f, 0.3f, 180f, 60f, 1f);
        float[] tail = AudioSynth.NewBuffer(0.15f);
        AudioSynth.AddWhiteNoise(tail, 0f, 0.15f, 0.5f, 101);
        AudioSynth.LowPass(tail, 1200f);
        AudioSynth.FadeEdges(tail, 0.01f, 0.1f);
        AudioSynth.Stamp(buffer, tail, 0.22f, 0.6f);
        AudioSynth.FadeEdges(buffer, 0.005f, 0.05f);
        AudioSynth.Normalize(buffer);
        return AudioSynth.ToClip("Sfx_Glorp", buffer);
    }

    /// <summary>White noise banded 400-3000 Hz, 0.05 attack, 1.0 decay. The pipes' opinion. 1.2 s.</summary>
    static AudioClip BuildGeyserBurst()
    {
        float[] buffer = AudioSynth.NewBuffer(1.2f);
        AudioSynth.AddWhiteNoise(buffer, 0f, 1.2f, 1f, 202);
        AudioSynth.BandPass(buffer, 400f, 3000f);
        AudioSynth.ApplyAdsr(buffer, 0.05f, 1f, 0.05f, 0.15f);
        AudioSynth.Normalize(buffer);
        return AudioSynth.ToClip("Sfx_GeyserBurst", buffer);
    }

    // ------------------------- Impacts & UI -------------------------

    /// <summary>Three staggered noise grains banded 2-6 kHz: one padlock, gone. 0.4 s.</summary>
    static AudioClip BuildGlassBreak()
    {
        float[] buffer = AudioSynth.NewBuffer(0.4f);
        float[] gains = { 1f, 0.7f, 0.5f };
        float[] starts = { 0f, 0.07f, 0.16f };
        for (int g = 0; g < 3; g++)
        {
            float[] grain = AudioSynth.NewBuffer(0.1f);
            AudioSynth.AddWhiteNoise(grain, 0f, 0.1f, 1f, 303 + g);
            AudioSynth.FadeEdges(grain, 0.002f, 0.08f);
            AudioSynth.Stamp(buffer, grain, starts[g], gains[g]);
        }
        AudioSynth.BandPass(buffer, 2000f, 6000f);
        AudioSynth.Normalize(buffer);
        return AudioSynth.ToClip("Sfx_GlassBreak", buffer);
    }

    /// <summary>Sine 140 Hz for 0.08 s plus a 20 ms noise click. Every button, everywhere. 0.1 s.</summary>
    static AudioClip BuildUiThunk()
    {
        float[] buffer = AudioSynth.NewBuffer(0.1f);
        AudioSynth.AddTone(buffer, 0f, 0.08f, 140f, 1f);
        AudioSynth.AddWhiteNoise(buffer, 0f, 0.02f, 0.3f, 404);
        AudioSynth.FadeEdges(buffer, 0.002f, 0.03f);
        AudioSynth.Normalize(buffer, 0.6f);
        return AudioSynth.ToClip("Sfx_UiThunk", buffer);
    }

    /// <summary>Sine 90 Hz, heavy attack: headlines and polaroids landing. 0.2 s.</summary>
    static AudioClip BuildStampThunk()
    {
        float[] buffer = AudioSynth.NewBuffer(0.2f);
        AudioSynth.AddTone(buffer, 0f, 0.15f, 90f, 1f);
        AudioSynth.AddWhiteNoise(buffer, 0f, 0.015f, 0.4f, 505);
        AudioSynth.ApplyAdsr(buffer, 0.002f, 0.1f, 0.3f, 0.06f);
        AudioSynth.Normalize(buffer);
        return AudioSynth.ToClip("Sfx_StampThunk", buffer);
    }

    /// <summary>Brown noise burst with a 60 Hz sine layer, 1.4 s decay. Scream-powered hydraulics. 1.6 s.</summary>
    static AudioClip BuildDoorExplosion()
    {
        float[] buffer = AudioSynth.NewBuffer(1.6f);
        AudioSynth.AddBrownNoise(buffer, 0f, 1.6f, 1f, 606);
        AudioSynth.AddSineSweep(buffer, 0f, 1.2f, 60f, 40f, 0.7f);
        AudioSynth.LowPass(buffer, 3000f);
        AudioSynth.ApplyAdsr(buffer, 0.005f, 1.4f, 0.03f, 0.15f);
        AudioSynth.Normalize(buffer, 0.85f);
        return AudioSynth.ToClip("Sfx_DoorExplosion", buffer);
    }

    /// <summary>Detuned saw triad 220/277/330 Hz, each voice doubled 8 cents apart. Triumphant-but-wrong. 2 s.</summary>
    static AudioClip BuildPipeOrganSting()
    {
        float[] buffer = AudioSynth.NewBuffer(2f);
        float[] triad = { 220f, 277f, 330f };
        const float detune = 1.00463f; // +8 cents
        foreach (float freq in triad)
        {
            AudioSynth.AddSawSweep(buffer, 0f, 2f, freq / detune, freq / detune, 0.22f);
            AudioSynth.AddSawSweep(buffer, 0f, 2f, freq * detune, freq * detune, 0.22f);
        }
        AudioSynth.LowPass(buffer, 2400f);
        AudioSynth.ApplyAdsr(buffer, 0.1f, 0.3f, 0.8f, 0.6f);
        AudioSynth.Normalize(buffer, 0.7f);
        return AudioSynth.ToClip("Sfx_PipeOrganSting", buffer);
    }

    /// <summary>Sine at the karaoke base note (523 Hz / C5) with a soft octave, 0.4 s decay. J. The pitch
    /// parameter on AudioDirector.Play turns it into K and L. 0.5 s.</summary>
    static AudioClip BuildChime()
    {
        float[] buffer = AudioSynth.NewBuffer(0.5f);
        AudioSynth.AddTone(buffer, 0f, 0.5f, 523f, 1f);
        AudioSynth.AddTone(buffer, 0f, 0.5f, 1046f, 0.25f);
        AudioSynth.ApplyAdsr(buffer, 0.005f, 0.45f, 0f, 0.05f);
        AudioSynth.Normalize(buffer, 0.6f);
        return AudioSynth.ToClip("Sfx_Chime", buffer);
    }

    /// <summary>Sine drop 110-45 Hz over 0.12 s plus a click transient. The dance floor's homing beacon. 0.15 s.</summary>
    static AudioClip BuildBassKick()
    {
        float[] buffer = AudioSynth.NewBuffer(0.15f);
        AudioSynth.AddSineSweep(buffer, 0f, 0.12f, 110f, 45f, 1f);
        AudioSynth.AddWhiteNoise(buffer, 0f, 0.008f, 0.5f, 707);
        AudioSynth.FadeEdges(buffer, 0.001f, 0.04f);
        AudioSynth.Normalize(buffer);
        return AudioSynth.ToClip("Sfx_BassKick", buffer);
    }

    // ------------------------- Monster & dread loops -------------------------

    /// <summary>
    /// Two sines at 55 and 57 Hz beating against each other. Both frequencies
    /// complete whole cycles in 4 s, so the loop is seamless by construction;
    /// the spec's slow 2 s swell is performed live by AmbienceController's
    /// proximity fade instead of being baked into the loop.
    /// </summary>
    static AudioClip BuildMonsterDrone()
    {
        float[] buffer = AudioSynth.NewBuffer(4f);
        AudioSynth.AddTone(buffer, 0f, 4f, 55f, 0.5f);
        AudioSynth.AddTone(buffer, 0f, 4f, 57f, 0.5f);
        AudioSynth.Normalize(buffer, 0.6f);
        return AudioSynth.ToClip("Sfx_MonsterDrone", buffer);
    }

    /// <summary>Noise scrape then a 70 Hz thud: shuffle... THUMP. 0.8 s loop; edges silent.</summary>
    static AudioClip BuildShuffleThump()
    {
        float[] buffer = AudioSynth.NewBuffer(0.8f);

        float[] scrape = AudioSynth.NewBuffer(0.3f);
        AudioSynth.AddWhiteNoise(scrape, 0f, 0.3f, 1f, 808);
        AudioSynth.BandPass(scrape, 200f, 1200f);
        AudioSynth.FadeEdges(scrape, 0.05f, 0.1f);
        AudioSynth.Stamp(buffer, scrape, 0.05f, 0.4f);

        float[] thud = AudioSynth.NewBuffer(0.14f);
        AudioSynth.AddSineSweep(thud, 0f, 0.12f, 70f, 55f, 1f);
        AudioSynth.FadeEdges(thud, 0.002f, 0.06f);
        AudioSynth.Stamp(buffer, thud, 0.45f, 0.9f);

        AudioSynth.Normalize(buffer, 0.7f);
        return AudioSynth.ToClip("Sfx_ShuffleThump", buffer);
    }

    /// <summary>Double 60 Hz thumps 0.4 s apart, then silence. 1.2 s loop; edges silent.</summary>
    static AudioClip BuildHeartbeat()
    {
        float[] thump = AudioSynth.NewBuffer(0.12f);
        AudioSynth.AddSineSweep(thump, 0f, 0.1f, 62f, 50f, 1f);
        AudioSynth.FadeEdges(thump, 0.004f, 0.05f);

        float[] buffer = AudioSynth.NewBuffer(1.2f);
        AudioSynth.Stamp(buffer, thump, 0f, 0.95f);
        AudioSynth.Stamp(buffer, thump, 0.4f, 0.7f);
        AudioSynth.LowPass(buffer, 220f);
        AudioSynth.Normalize(buffer, 0.8f);
        return AudioSynth.ToClip("Sfx_Heartbeat", buffer);
    }

    // ------------------------- Room tone -------------------------

    /// <summary>Brown noise under 400 Hz plus vinyl-crackle ticks. The den pretending to be safe. 8 s loop.</summary>
    static AudioClip BuildDenAmbience()
    {
        // Generated a crossfade longer than the target, then trimmed seamless.
        float[] buffer = AudioSynth.NewBuffer(8.25f);
        AudioSynth.AddBrownNoise(buffer, 0f, 8.25f, 0.8f, 909);
        AudioSynth.LowPass(buffer, 400f);
        AddCrackle(buffer, 24, 0.12f, 910);
        buffer = AudioSynth.MakeSeamlessLoop(buffer, 0.25f);
        AudioSynth.Normalize(buffer, 0.5f);
        return AudioSynth.ToClip("Sfx_DenAmbience", buffer);
    }

    /// <summary>Brown noise under 250 Hz, wind LFO at 0.1 Hz, one distant two-hoot owl. 10 s loop.</summary>
    static AudioClip BuildYardAmbience()
    {
        float[] buffer = AudioSynth.NewBuffer(10.3f);
        AudioSynth.AddBrownNoise(buffer, 0f, 10.3f, 0.8f, 111);
        AudioSynth.LowPass(buffer, 250f);
        AudioSynth.ApplyAmplitudeLfo(buffer, 0.1f, 0.45f);

        float[] hoot = AudioSynth.NewBuffer(0.3f);
        AudioSynth.AddTone(hoot, 0f, 0.3f, 420f, 1f);
        AudioSynth.ApplyAdsr(hoot, 0.04f, 0.2f, 0.2f, 0.06f);
        AudioSynth.Stamp(buffer, hoot, 4.2f, 0.12f);
        AudioSynth.Stamp(buffer, hoot, 4.75f, 0.1f);

        buffer = AudioSynth.MakeSeamlessLoop(buffer, 0.3f);
        AudioSynth.Normalize(buffer, 0.5f);
        return AudioSynth.ToClip("Sfx_YardAmbience", buffer);
    }

    // ------------------------- Body -------------------------

    /// <summary>70 ms noise burst under 900 Hz with a 120 Hz wood resonance. 0.08 s.</summary>
    static AudioClip BuildFootstep()
    {
        float[] buffer = AudioSynth.NewBuffer(0.08f);
        AudioSynth.AddWhiteNoise(buffer, 0f, 0.07f, 1f, 212);
        AudioSynth.LowPass(buffer, 900f);
        AudioSynth.AddTone(buffer, 0f, 0.05f, 120f, 0.35f); // indoor wood-creak body
        AudioSynth.FadeEdges(buffer, 0.003f, 0.03f);
        AudioSynth.Normalize(buffer, 0.55f);
        return AudioSynth.ToClip("Sfx_Footstep", buffer);
    }

    /// <summary>Filtered noise swelling at 0.8 Hz - two full breaths in 2.5 s, seamless. Loop.</summary>
    static AudioClip BuildBreathing()
    {
        float[] buffer = AudioSynth.NewBuffer(2.5f);
        AudioSynth.AddWhiteNoise(buffer, 0f, 2.5f, 1f, 313);
        AudioSynth.LowPass(buffer, 700f);
        AudioSynth.ApplyAmplitudeLfo(buffer, 0.8f, 0.06f);
        AudioSynth.Normalize(buffer, 0.4f);
        return AudioSynth.ToClip("Sfx_Breathing", buffer);
    }

    /// <summary>Two soft low-passed taps 120 ms apart. The dorky furniture pat-pat. 0.25 s.</summary>
    static AudioClip BuildPatPat()
    {
        float[] tap = AudioSynth.NewBuffer(0.05f);
        AudioSynth.AddWhiteNoise(tap, 0f, 0.05f, 1f, 414);
        AudioSynth.LowPass(tap, 1500f);
        AudioSynth.AddTone(tap, 0f, 0.04f, 200f, 0.4f);
        AudioSynth.FadeEdges(tap, 0.002f, 0.02f);

        float[] buffer = AudioSynth.NewBuffer(0.25f);
        AudioSynth.Stamp(buffer, tap, 0f, 0.8f);
        AudioSynth.Stamp(buffer, tap, 0.12f, 0.65f);
        AudioSynth.Normalize(buffer, 0.55f);
        return AudioSynth.ToClip("Sfx_PatPat", buffer);
    }

    /// <summary>Saw rip 220-880 Hz over 0.15 s, hard attack. Beyond-the-grave pettiness. 0.3 s.</summary>
    static AudioClip BuildBooSting()
    {
        float[] buffer = AudioSynth.NewBuffer(0.3f);
        AudioSynth.AddSawSweep(buffer, 0f, 0.15f, 220f, 880f, 1f);
        AudioSynth.LowPass(buffer, 4000f);
        AudioSynth.ApplyAdsr(buffer, 0.003f, 0.1f, 0.5f, 0.12f);
        AudioSynth.Normalize(buffer);
        return AudioSynth.ToClip("Sfx_BooSting", buffer);
    }

    // ------------------------- Helpers -------------------------

    /// <summary>Scatters short high-passed ticks across the buffer (vinyl crackle).</summary>
    static void AddCrackle(float[] buffer, int tickCount, float amplitude, int seed)
    {
        var random = new System.Random(seed);
        for (int i = 0; i < tickCount; i++)
        {
            float at = (float)random.NextDouble() * (buffer.Length / (float)AudioSynth.SampleRate - 0.05f);
            float[] tick = AudioSynth.NewBuffer(0.02f);
            AudioSynth.AddWhiteNoise(tick, 0f, 0.02f, 1f, seed + i + 1);
            AudioSynth.HighPass(tick, 1800f);
            AudioSynth.FadeEdges(tick, 0.001f, 0.012f);
            AudioSynth.Stamp(buffer, tick, at, amplitude * (0.5f + (float)random.NextDouble() * 0.5f));
        }
    }
}
