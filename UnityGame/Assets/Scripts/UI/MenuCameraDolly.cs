using UnityEngine;

/// <summary>
/// The main menu's living backdrop: dollies the camera through the den at
/// dusk on a 20-second ping-pong loop between two fixed marks, always gazing
/// at the fireplace (GDD 7.1). Enabled by MenuUI while the menu is on screen
/// and disabled the moment a session starts, when the player pawn's own
/// camera takes over.
/// </summary>
public class MenuCameraDolly : MonoBehaviour
{
    public static MenuCameraDolly Instance { get; private set; }

    [Header("Dolly marks (GDD 7.1)")]
    public Vector3 pointA = new Vector3(-8f, 2.2f, -1f);
    public Vector3 pointB = new Vector3(6f, 2f, 3f);

    [Tooltip("What the camera keeps looking at - the fireplace face.")]
    public Vector3 lookTarget = new Vector3(0f, 1.4f, 11.4f);

    [Tooltip("Seconds for one full there-and-back loop.")]
    public float loopSeconds = 20f;

    float timer;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        // Jump straight onto the rail so enabling never snaps mid-frame.
        Apply(0f);
    }

    void Update()
    {
        timer += Time.deltaTime;
        Apply(timer);
    }

    void Apply(float time)
    {
        if (loopSeconds <= 0.01f) return;
        float phase = Mathf.PingPong(time / (loopSeconds * 0.5f), 1f);
        float eased = Mathf.SmoothStep(0f, 1f, phase);
        transform.position = Vector3.Lerp(pointA, pointB, eased);
        transform.rotation = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);
    }
}
