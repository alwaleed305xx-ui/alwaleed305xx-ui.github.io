using UnityEngine;

/// <summary>
/// Free-fly spectator control, added to the local player's pawn after death or
/// escape. Fly speed 8; Space climbs, Left Ctrl descends. Also aims the one-per-round
/// ghost Boo: look at a living victim within 20 units and press [B].
///
/// Plain MonoBehaviour by design -- NGO forbids adding NetworkBehaviours at runtime,
/// so the Boo ServerRpc lives on PlayerController and this class only aims and asks.
/// Only ever exists on the owning client.
/// </summary>
public class GhostController : MonoBehaviour
{
    [Header("Ghost movement (GDD 4.1)")]
    public float speed = 8f;

    [Header("Boo (GDD 9.7)")]
    public float booRange = 20f;

    const string PromptBoo = "[B] BOO";

    PlayerController player;
    Transform look;
    float pitch;
    bool promptShown;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        look = player != null && player.cameraHolder != null ? player.cameraHolder : transform;

        // Continue from the camera's current pitch so death does not snap the view.
        float x = look.localEulerAngles.x;
        pitch = x > 180f ? x - 360f : x;
    }

    void Update()
    {
        LookAround();
        Fly();
        AimBoo();
    }

    void LookAround()
    {
        float sens = Sensitivity();
        float mx = Input.GetAxis("Mouse X") * sens;
        float my = Input.GetAxis("Mouse Y") * sens;
        transform.Rotate(Vector3.up * mx);
        pitch = Mathf.Clamp(pitch + (InvertLook() ? my : -my), -80f, 80f);
        look.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }

    void Fly()
    {
        Vector3 planar = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        float up = (Input.GetKey(KeyCode.Space) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftControl) ? 1f : 0f);
        Vector3 move = transform.TransformDirection(planar) + Vector3.up * up;
        if (move.sqrMagnitude > 1f) move.Normalize();
        transform.position += move * speed * Time.deltaTime;
    }

    void AimBoo()
    {
        IVictim target = null;
        if (CanBoo() && Physics.Raycast(look.position, look.forward, out RaycastHit hit, booRange,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            IVictim v = hit.collider.GetComponentInParent<IVictim>();
            if (v != null && v.IsCatchable) target = v;
        }

        SetPrompt(target != null);

        if (target != null && Input.GetKeyDown(KeyCode.B))
            player.BooServerRpc(target.ActorId);
    }

    bool CanBoo()
    {
        if (player == null || !player.IsSpawned || player.BooSpent.Value) return false;
        if (GameManager.Instance == null) return true; // isolated module test scene
        GameManager.GameState s = GameManager.Instance.State.Value;
        return s == GameManager.GameState.Playing || s == GameManager.GameState.Finale;
    }

    void SetPrompt(bool show)
    {
        if (show == promptShown) return;
        promptShown = show;
        if (GameUI.Instance != null) GameUI.Instance.ShowPrompt(show ? PromptBoo : null);
    }

    void OnDestroy()
    {
        if (promptShown) SetPrompt(false);
    }

    static float Sensitivity() => ScreamerSettings.Instance != null ? ScreamerSettings.Instance.MouseSensitivity : 2.2f;
    static bool InvertLook() => ScreamerSettings.Instance != null && ScreamerSettings.Instance.InvertY;
}
