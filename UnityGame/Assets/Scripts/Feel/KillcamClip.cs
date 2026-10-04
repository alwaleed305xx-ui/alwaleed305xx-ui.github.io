using Unity.Netcode;
using UnityEngine;

/// <summary>
/// One recorded kill, ready to restage (GDD 12.2): up to six seconds of victim
/// and monster positions and yaws at 10 Hz - about 120 floats, trivial on the
/// wire - plus the caption context id that <c>GameCopy.KillcamCaption</c>
/// turns into the joke. Recorded by <see cref="KillcamRecorder"/> on the
/// server, broadcast once at Results, replayed on proxy pawns by
/// <see cref="KillcamPlayer"/>.
///
/// The four arrays are index-aligned (sampled on the same server tick, oldest
/// first, ending at the moment of the catch) and always share one length.
/// </summary>
[System.Serializable]
public struct KillcamClip : INetworkSerializable
{
    public ulong victimId;
    public byte captionContextId;
    public Vector3[] victimPos;
    public float[] victimYaw;
    public Vector3[] monsterPos;
    public float[] monsterYaw;

    /// <summary>Samples in the clip; all four arrays share this length.</summary>
    public int SampleCount => victimPos != null ? victimPos.Length : 0;

    /// <summary>True when there is enough aligned data to stage a replay.</summary>
    public bool IsValid =>
        SampleCount >= 2 &&
        victimYaw != null && victimYaw.Length == SampleCount &&
        monsterPos != null && monsterPos.Length == SampleCount &&
        monsterYaw != null && monsterYaw.Length == SampleCount;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref victimId);
        serializer.SerializeValue(ref captionContextId);
        SerializeVectors(serializer, ref victimPos);
        SerializeFloats(serializer, ref victimYaw);
        SerializeVectors(serializer, ref monsterPos);
        SerializeFloats(serializer, ref monsterYaw);
    }

    // Length-prefixed element loops: boring, explicit, and guaranteed to work
    // the same way on both sides of every NGO 1.x transport.

    static void SerializeVectors<T>(BufferSerializer<T> serializer, ref Vector3[] array) where T : IReaderWriter
    {
        int length = array != null ? array.Length : 0;
        serializer.SerializeValue(ref length);

        if (serializer.IsReader) array = new Vector3[length];
        for (int i = 0; i < length; i++)
            serializer.SerializeValue(ref array[i]);
    }

    static void SerializeFloats<T>(BufferSerializer<T> serializer, ref float[] array) where T : IReaderWriter
    {
        int length = array != null ? array.Length : 0;
        serializer.SerializeValue(ref length);

        if (serializer.IsReader) array = new float[length];
        for (int i = 0; i < length; i++)
            serializer.SerializeValue(ref array[i]);
    }
}
