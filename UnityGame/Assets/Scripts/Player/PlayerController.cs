using Unity.Netcode;
using UnityEngine;

/// <summary>
/// تحكم الولد الهارب (الناجي): مشي، ركض، قفز، وتفاعل مع المهام بزر E.
///
/// طريقة التركيب على بريفاب "Survivor":
/// - CharacterController
/// - NetworkObject
/// - NetworkTransform (فعّل خيار Owner Authoritative / Client Authority)
/// - هذا السكربت
/// - ابن فاضي اسمه CameraHolder على مستوى الراس واربطه هنا
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("الحركة")]
    public float walkSpeed = 4.5f;
    public float runSpeed = 7.5f;
    public float jumpForce = 6f;
    public float gravity = -18f;

    [Header("الكاميرا")]
    public Transform cameraHolder;
    public float mouseSensitivity = 2.2f;

    [Header("الركض يسوي ضجة!")]
    public float runNoiseInterval = 2.5f;
    public float runNoiseLoudness = 0.3f;

    [Header("التفاعل")]
    public float interactRange = 3f;

    CharacterController cc;
    float verticalVelocity;
    float cameraPitch;
    float runNoiseTimer;

    public bool IsGhost { get; private set; }
    public bool InputLocked { get; set; } // المهام تقفل الحركة مؤقتاً

    public override void OnNetworkSpawn()
    {
        cc = GetComponent<CharacterController>();

        if (IsOwner)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.transform.SetParent(cameraHolder, false);
                cam.transform.localPosition = Vector3.zero;
                cam.transform.localRotation = Quaternion.identity;
            }
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

    void Update()
    {
        if (!IsOwner || IsGhost) return;
        if (GameManager.Instance == null ||
            GameManager.Instance.State.Value != GameManager.GameState.Playing) return;

        Look();
        if (!InputLocked)
        {
            Move();
            TryInteract();
        }
    }

    void Look()
    {
        float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity;
        transform.Rotate(Vector3.up * mx);
        cameraPitch = Mathf.Clamp(cameraPitch - my, -80f, 80f);
        cameraHolder.localEulerAngles = new Vector3(cameraPitch, 0, 0);
    }

    void Move()
    {
        bool running = Input.GetKey(KeyCode.LeftShift);
        float speed = running ? runSpeed : walkSpeed;

        Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
        Vector3 move = transform.TransformDirection(input.normalized) * speed;

        if (cc.isGrounded)
        {
            verticalVelocity = -1f;
            if (Input.GetButtonDown("Jump")) verticalVelocity = jumpForce;
        }
        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity;

        cc.Move(move * Time.deltaTime);

        // الركض مو ببلاش — الوحش يسمع خطواتك 👀
        if (running && input.sqrMagnitude > 0.1f && cc.isGrounded)
        {
            runNoiseTimer += Time.deltaTime;
            if (runNoiseTimer >= runNoiseInterval)
            {
                runNoiseTimer = 0;
                if (NoiseSystem.Instance != null)
                    NoiseSystem.Instance.MakeNoise(transform.position, runNoiseLoudness, "خطوات ركض 🏃");
            }
        }
        else runNoiseTimer = 0;
    }

    void TryInteract()
    {
        if (!Input.GetKeyDown(KeyCode.E)) return;

        Ray ray = new Ray(cameraHolder.position, cameraHolder.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange))
        {
            TaskBase task = hit.collider.GetComponentInParent<TaskBase>();
            if (task != null) { task.TryStart(this); return; }

            EscapeDoor door = hit.collider.GetComponentInParent<EscapeDoor>();
            if (door != null) door.TryEscape(this);
        }
    }

    // ───── الإمساك (الوحش يناديها) ─────

    [ServerRpc(RequireOwnership = false)]
    public void GetCaughtServerRpc()
    {
        if (IsGhost) return;
        GameManager.Instance.ServerPlayerCaught(OwnerClientId);
        BecomeGhostClientRpc(true);
    }

    // ───── الهروب (صاحب الشخصية يناديها من EscapeDoor) ─────

    [ServerRpc]
    public void EscapeServerRpc()
    {
        if (IsGhost) return;
        GameManager.Instance.ServerPlayerEscaped(OwnerClientId);
        BecomeGhostClientRpc(false);
    }

    [ClientRpc]
    void BecomeGhostClientRpc(bool wasCaught)
    {
        IsGhost = true;

        if (IsOwner)
        {
            if (GameUI.Instance != null)
            {
                GameUI.Instance.ShowEvent(wasCaught
                    ? "انمسكت! 👻 صرت شبح — اتفرج على ربعك وهم يعانون"
                    : "نجوت! 🎉 اتفرج على الباقين من فوق");
            }
            cc.enabled = false;
            gameObject.AddComponent<GhostFly>();
        }

        // الشبح يختفي عن الباقين (صاحبه يشوف مكانه عشان يطير براحته)
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            r.enabled = false;
        foreach (Collider col in GetComponentsInChildren<Collider>())
            col.enabled = false;
    }
}

/// <summary>طيران حر بسيط لوضع الشبح — Space فوق و Ctrl تحت.</summary>
public class GhostFly : MonoBehaviour
{
    public float speed = 8f;
    void Update()
    {
        Vector3 dir = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
        float up = (Input.GetKey(KeyCode.Space) ? 1 : 0) - (Input.GetKey(KeyCode.LeftControl) ? 1 : 0);
        Vector3 move = transform.TransformDirection(dir) + Vector3.up * up;
        transform.position += move.normalized * speed * Time.deltaTime;
    }
}
