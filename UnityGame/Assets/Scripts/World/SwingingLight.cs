using UnityEngine;

/// <summary>
/// The garage's bare bulb: a scripted pendulum swing (plus or minus
/// <see cref="swingDegrees"/> around the chosen local axis, sinusoidal with
/// period <see cref="periodSeconds"/>). Hang the Light and the bulb mesh as
/// children below this pivot and the moving shadows come for free (GDD 6.2).
/// </summary>
public class SwingingLight : MonoBehaviour
{
    [Tooltip("Peak deflection from rest, in degrees.")]
    public float swingDegrees = 15f;

    [Tooltip("Seconds per full back-and-forth swing.")]
    public float periodSeconds = 3f;

    [Tooltip("Local axis the pivot rotates around. Default tilts the hanging cord sideways.")]
    public Vector3 swingAxis = Vector3.forward;

    [Tooltip("Random phase offset so multiple swinging lights never sync up.")]
    public bool randomizePhase = true;

    Quaternion restRotation;
    float phase;

    void Awake()
    {
        restRotation = transform.localRotation;
        if (randomizePhase) phase = Random.Range(0f, Mathf.PI * 2f);
    }

    void Update()
    {
        if (periodSeconds <= 0.01f) return;

        float angle = swingDegrees * Mathf.Sin(Time.time * (Mathf.PI * 2f) / periodSeconds + phase);
        transform.localRotation = restRotation * Quaternion.AngleAxis(angle, swingAxis);
    }
}
