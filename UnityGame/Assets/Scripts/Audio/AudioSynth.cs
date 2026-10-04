using UnityEngine;

/// <summary>
/// Waveform, noise, envelope and filter primitives for the procedural audio
/// bank (GDD 13.2). Everything works on plain mono float buffers at 44.1 kHz;
/// <see cref="ProceduralAudioBank"/> composes these into finished AudioClips.
///
/// Module-internal: nothing outside the audio bank should synthesize samples.
/// All oscillators are phase-accumulated so frequency sweeps and vibrato stay
/// smooth instead of stepping, and all randomness is seeded so the bank sounds
/// identical on every machine - the game's voice is not a dice roll.
/// </summary>
static class AudioSynth
{
    public const int SampleRate = 44100;

    // Private oscillator selectors (kept as constants - no public enum needed).
    const int WaveSine = 0;
    const int WaveSaw = 1;
    const int WaveSquare = 2;

    // ------------------------- Buffers -------------------------

    /// <summary>A silent mono buffer covering <paramref name="seconds"/>.</summary>
    public static float[] NewBuffer(float seconds)
    {
        return new float[Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate))];
    }

    public static int SampleIndex(float seconds)
    {
        return Mathf.Clamp(Mathf.RoundToInt(seconds * SampleRate), 0, int.MaxValue);
    }

    /// <summary>Adds <paramref name="source"/> into <paramref name="target"/> starting at <paramref name="startSeconds"/>.</summary>
    public static void Stamp(float[] target, float[] source, float startSeconds, float gain = 1f)
    {
        int offset = SampleIndex(startSeconds);
        int count = Mathf.Min(source.Length, target.Length - offset);
        for (int i = 0; i < count; i++)
            target[offset + i] += source[i] * gain;
    }

    // ------------------------- Oscillators -------------------------

    /// <summary>Sine sweep from <paramref name="fromHz"/> to <paramref name="toHz"/> with optional vibrato.</summary>
    public static void AddSineSweep(float[] buffer, float startSeconds, float durationSeconds,
        float fromHz, float toHz, float amplitude, float vibratoHz = 0f, float vibratoDepthHz = 0f)
    {
        AddOscillator(buffer, WaveSine, startSeconds, durationSeconds, fromHz, toHz, amplitude, vibratoHz, vibratoDepthHz);
    }

    /// <summary>Sawtooth sweep (the bank's screams and stings).</summary>
    public static void AddSawSweep(float[] buffer, float startSeconds, float durationSeconds,
        float fromHz, float toHz, float amplitude, float vibratoHz = 0f, float vibratoDepthHz = 0f)
    {
        AddOscillator(buffer, WaveSaw, startSeconds, durationSeconds, fromHz, toHz, amplitude, vibratoHz, vibratoDepthHz);
    }

    /// <summary>Square wave at 50% duty (the smoke alarm's awful voice).</summary>
    public static void AddSquare(float[] buffer, float startSeconds, float durationSeconds,
        float freqHz, float amplitude)
    {
        AddOscillator(buffer, WaveSquare, startSeconds, durationSeconds, freqHz, freqHz, amplitude, 0f, 0f);
    }

    /// <summary>Steady sine tone - a sweep that goes nowhere.</summary>
    public static void AddTone(float[] buffer, float startSeconds, float durationSeconds,
        float freqHz, float amplitude)
    {
        AddOscillator(buffer, WaveSine, startSeconds, durationSeconds, freqHz, freqHz, amplitude, 0f, 0f);
    }

    static void AddOscillator(float[] buffer, int wave, float startSeconds, float durationSeconds,
        float fromHz, float toHz, float amplitude, float vibratoHz, float vibratoDepthHz)
    {
        int start = SampleIndex(startSeconds);
        int count = Mathf.Min(SampleIndex(durationSeconds), buffer.Length - start);
        if (count <= 0) return;

        double phase = 0.0;
        double vibratoPhase = 0.0;
        double dt = 1.0 / SampleRate;

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / count;
            double freq = Mathf.Lerp(fromHz, toHz, t);
            if (vibratoDepthHz > 0f)
            {
                freq += System.Math.Sin(vibratoPhase) * vibratoDepthHz;
                vibratoPhase += 2.0 * System.Math.PI * vibratoHz * dt;
            }

            phase += freq * dt;
            double cycle = phase - System.Math.Floor(phase); // 0..1 within the wave period

            float sample;
            switch (wave)
            {
                case WaveSaw: sample = (float)(cycle * 2.0 - 1.0); break;
                case WaveSquare: sample = cycle < 0.5 ? 1f : -1f; break;
                default: sample = (float)System.Math.Sin(cycle * 2.0 * System.Math.PI); break;
            }
            buffer[start + i] += sample * amplitude;
        }
    }

    /// <summary>Multiplies the whole buffer by a sine carrier (feedback-screech ring modulation).</summary>
    public static void RingModulate(float[] buffer, float freqHz)
    {
        double step = 2.0 * System.Math.PI * freqHz / SampleRate;
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= (float)System.Math.Sin(i * step);
    }

    // ------------------------- Noise -------------------------

    /// <summary>Seeded white noise burst.</summary>
    public static void AddWhiteNoise(float[] buffer, float startSeconds, float durationSeconds,
        float amplitude, int seed)
    {
        int start = SampleIndex(startSeconds);
        int count = Mathf.Min(SampleIndex(durationSeconds), buffer.Length - start);
        var random = new System.Random(seed);
        for (int i = 0; i < count; i++)
            buffer[start + i] += ((float)random.NextDouble() * 2f - 1f) * amplitude;
    }

    /// <summary>
    /// Seeded brown noise (leaky-integrated white): the warm rumble under the
    /// ambiences and the door explosion.
    /// </summary>
    public static void AddBrownNoise(float[] buffer, float startSeconds, float durationSeconds,
        float amplitude, int seed)
    {
        int start = SampleIndex(startSeconds);
        int count = Mathf.Min(SampleIndex(durationSeconds), buffer.Length - start);
        var random = new System.Random(seed);

        float value = 0f;
        for (int i = 0; i < count; i++)
        {
            float white = (float)random.NextDouble() * 2f - 1f;
            value = (value + white * 0.08f) * 0.985f; // integrate with leak
            buffer[start + i] += Mathf.Clamp(value * 3.2f, -1f, 1f) * amplitude;
        }
    }

    // ------------------------- Filters (in place, whole buffer) -------------------------

    /// <summary>One-pole low-pass.</summary>
    public static void LowPass(float[] buffer, float cutoffHz)
    {
        float rc = 1f / (2f * Mathf.PI * Mathf.Max(1f, cutoffHz));
        float dt = 1f / SampleRate;
        float alpha = dt / (rc + dt);

        float y = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            y += alpha * (buffer[i] - y);
            buffer[i] = y;
        }
    }

    /// <summary>One-pole high-pass.</summary>
    public static void HighPass(float[] buffer, float cutoffHz)
    {
        float rc = 1f / (2f * Mathf.PI * Mathf.Max(1f, cutoffHz));
        float dt = 1f / SampleRate;
        float alpha = rc / (rc + dt);

        float y = 0f;
        float previous = buffer.Length > 0 ? buffer[0] : 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float x = buffer[i];
            y = alpha * (y + x - previous);
            previous = x;
            buffer[i] = y;
        }
    }

    /// <summary>Band-pass as a high-pass into a low-pass - crude and perfect for geysers.</summary>
    public static void BandPass(float[] buffer, float lowHz, float highHz)
    {
        HighPass(buffer, lowHz);
        LowPass(buffer, highHz);
    }

    // ------------------------- Envelopes -------------------------

    /// <summary>
    /// Classic ADSR over the whole buffer: linear attack and decay, flat
    /// sustain, linear release occupying the final <paramref name="release"/> seconds.
    /// </summary>
    public static void ApplyAdsr(float[] buffer, float attack, float decay, float sustainLevel, float release)
    {
        int attackEnd = SampleIndex(attack);
        int decayEnd = attackEnd + SampleIndex(decay);
        int releaseStart = Mathf.Max(decayEnd, buffer.Length - SampleIndex(release));

        for (int i = 0; i < buffer.Length; i++)
        {
            float gain;
            if (i < attackEnd)
                gain = attackEnd > 0 ? (float)i / attackEnd : 1f;
            else if (i < decayEnd)
                gain = Mathf.Lerp(1f, sustainLevel, decayEnd > attackEnd ? (float)(i - attackEnd) / (decayEnd - attackEnd) : 1f);
            else if (i < releaseStart)
                gain = sustainLevel;
            else
                gain = sustainLevel * (buffer.Length > releaseStart
                    ? 1f - (float)(i - releaseStart) / (buffer.Length - releaseStart) : 0f);

            buffer[i] *= gain;
        }
    }

    /// <summary>Short linear fades at both ends - the universal click killer.</summary>
    public static void FadeEdges(float[] buffer, float fadeInSeconds, float fadeOutSeconds)
    {
        int fadeIn = Mathf.Min(SampleIndex(fadeInSeconds), buffer.Length);
        for (int i = 0; i < fadeIn; i++)
            buffer[i] *= (float)i / fadeIn;

        int fadeOut = Mathf.Min(SampleIndex(fadeOutSeconds), buffer.Length);
        for (int i = 0; i < fadeOut; i++)
            buffer[buffer.Length - 1 - i] *= (float)i / fadeOut;
    }

    /// <summary>
    /// Multiplies the buffer by a raised-cosine amplitude LFO: gain swings
    /// between <paramref name="floor"/> and 1 at <paramref name="lfoHz"/>,
    /// starting at the floor so whole-cycle loops stay seamless.
    /// </summary>
    public static void ApplyAmplitudeLfo(float[] buffer, float lfoHz, float floor)
    {
        double step = 2.0 * System.Math.PI * lfoHz / SampleRate;
        for (int i = 0; i < buffer.Length; i++)
        {
            float swing = (float)(0.5 - 0.5 * System.Math.Cos(i * step)); // 0 -> 1 -> 0 per cycle
            buffer[i] *= Mathf.Lerp(floor, 1f, swing);
        }
    }

    // ------------------------- Utilities -------------------------

    /// <summary>Scales the buffer so its peak sits at <paramref name="peak"/>.</summary>
    public static void Normalize(float[] buffer, float peak = 0.75f)
    {
        float max = 0f;
        for (int i = 0; i < buffer.Length; i++)
            max = Mathf.Max(max, Mathf.Abs(buffer[i]));
        if (max < 0.0001f) return;

        float gain = peak / max;
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= gain;
    }

    /// <summary>
    /// Returns a loop-safe copy: the final <paramref name="crossfadeSeconds"/>
    /// are blended onto the head and trimmed off the tail, so the loop seam
    /// lands on consecutive source samples. Use for noise-based loops; purely
    /// periodic content loops cleanly without it.
    /// </summary>
    public static float[] MakeSeamlessLoop(float[] buffer, float crossfadeSeconds)
    {
        int fade = Mathf.Min(SampleIndex(crossfadeSeconds), buffer.Length / 2);
        int newLength = buffer.Length - fade;
        var result = new float[newLength];

        for (int i = 0; i < newLength; i++)
            result[i] = buffer[i];
        for (int i = 0; i < fade; i++)
        {
            float w = (float)i / fade;
            result[i] = buffer[i] * w + buffer[newLength + i] * (1f - w);
        }
        return result;
    }

    /// <summary>Bakes a finished buffer into a mono 44.1 kHz AudioClip.</summary>
    public static AudioClip ToClip(string name, float[] samples)
    {
        AudioClip clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
