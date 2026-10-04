using UnityEngine;

/// <summary>
/// The rubber chicken decoy's local show (Hide &amp; Shriek update, GDD 14.3).
/// Purely cosmetic and built on every client from a ClientRpc: a doomed
/// yellow bird arcs through the air, lands, and "squeaks" three times. The
/// REAL noise pings are scheduled by the server (PlayerController's throw
/// RPC) as environmental chicken noise with the real chicken's label, so the
/// monster cannot tell this liar from the actual task chicken.
///
/// The visual squeak timing mirrors the server's ping schedule by sharing
/// the constants below; a few network milliseconds of drift is invisible.
/// </summary>
public class DecoyChicken : MonoBehaviour
{
    /// <summary>Seconds in the air before the first squeak.</summary>
    public const float FlightSeconds = 0.8f;
    public const int SqueakCount = 3;
    public const float SqueakInterval = 1.1f;
    /// <summary>What the throw costs the house in silence (server uses this).</summary>
    public const float SqueakLoudness = 0.75f;

    const float ArcHeight = 2.2f;
    const float LingerAfterLastSqueak = 1.5f;
    const float ShrinkSeconds = 0.4f;

    Vector3 from;
    Vector3 to;
    float age;
    int squeaksPlayed;
    Vector3 baseScale;

    /// <summary>Builds the bird on this client and sends it flying.</summary>
    public static void Spawn(Vector3 from, Vector3 to)
    {
        GameObject root = new GameObject("DecoyChicken");
        root.transform.position = from;

        BuildBird(root.transform);

        DecoyChicken decoy = root.AddComponent<DecoyChicken>();
        decoy.from = from;
        decoy.to = to;
        decoy.baseScale = Vector3.one;
    }

    static void BuildBird(Transform root)
    {
        Prim(PrimitiveType.Capsule, "Body", root, new Vector3(0f, 0f, 0f),
            new Vector3(0.3f, 0.26f, 0.3f), ScreamerPalette.ScreamYellow);
        Prim(PrimitiveType.Sphere, "Head", root, new Vector3(0f, 0.3f, 0.12f),
            Vector3.one * 0.2f, ScreamerPalette.ScreamYellow);
        Prim(PrimitiveType.Cube, "Beak", root, new Vector3(0f, 0.29f, 0.25f),
            new Vector3(0.08f, 0.06f, 0.12f), ScreamerPalette.LamplightAmber);
        Prim(PrimitiveType.Cube, "Comb", root, new Vector3(0f, 0.42f, 0.08f),
            new Vector3(0.05f, 0.1f, 0.14f), ScreamerPalette.MonsterRed);
    }

    static void Prim(PrimitiveType type, string name, Transform parent,
        Vector3 localPos, Vector3 localScale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>()); // a lie needs no physics
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial =
            ScreamerPalette.MakeRuntimeMaterial("DecoyChicken_" + name, color);
    }

    void Update()
    {
        age += Time.deltaTime;

        if (age < FlightSeconds)
        {
            // A cartoon parabola with a panicked spin.
            float n = age / FlightSeconds;
            Vector3 pos = Vector3.Lerp(from, to, n);
            pos.y += Mathf.Sin(n * Mathf.PI) * ArcHeight;
            transform.position = pos;
            transform.Rotate(Vector3.right, 540f * Time.deltaTime, Space.Self);
            return;
        }

        transform.position = to;
        transform.rotation = Quaternion.identity;

        // Squeaks: a squish and a squawk, matching the server's ping schedule.
        float sinceLanding = age - FlightSeconds;
        if (squeaksPlayed < SqueakCount && sinceLanding >= squeaksPlayed * SqueakInterval)
        {
            squeaksPlayed++;
            if (AudioDirector.Instance != null)
                AudioDirector.Instance.Play(Sfx.ChickenSquawk, to, 1f, 1.2f);
        }

        // Squash-and-stretch around each squeak moment.
        float sinceSqueak = sinceLanding - (squeaksPlayed - 1) * SqueakInterval;
        float squish = squeaksPlayed > 0 && sinceSqueak < 0.25f
            ? 1f - Mathf.Sin(sinceSqueak / 0.25f * Mathf.PI) * 0.35f
            : 1f;
        transform.localScale = new Vector3(baseScale.x / squish, baseScale.y * squish, baseScale.z / squish);

        // Done lying; shrink out with whatever dignity remains.
        float doneAt = FlightSeconds + (SqueakCount - 1) * SqueakInterval + LingerAfterLastSqueak;
        if (age > doneAt)
        {
            float k = 1f - Mathf.Clamp01((age - doneAt) / ShrinkSeconds);
            transform.localScale = baseScale * k;
            if (k <= 0f) Destroy(gameObject);
        }
    }
}
