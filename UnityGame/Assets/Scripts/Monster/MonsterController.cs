using Unity.Netcode;
using UnityEngine;

/// <summary>
/// تحكم الوحش: أسرع شوي من العيال، يمسكهم بكبسة زر،
/// ويشوف "أسهم الضجة" على شاشته إذا أحد سوى صوت.
///
/// طريقة التركيب على بريفاب "Monster":
/// - CharacterController + NetworkObject + NetworkTransform (Owner Authoritative)
/// - هذا السكربت + MonsterSkinSelector
/// - حط الموديلات الثلاثة (الزومبي، المسخ، الميميك) كأبناء
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class MonsterController : NetworkBehaviour
{
    [Header("الحركة — الوحش أسرع بس مو وايد (عشان العدل)")]
    public float moveSpeed = 8.2f;
    public float mouseSensitivity = 2.2f;
    public float gravity = -18f;

    [Header("الهجوم")]
    public float attackRange = 2.4f;
    public float attackCooldown = 1.2f;
    public Transform cameraHolder;

    [Header("صوت زئير اختياري عند الهجوم")]
    public AudioSource roarSound;

    // السيرفر يحدد متى ينفك الحبس وتتزامن للجميع
    NetworkVariable<double> frozenUntilServerTime = new NetworkVariable<double>(0);

    CharacterController cc;
    float verticalVelocity;
    float cameraPitch;
    float cooldownTimer;
    bool freezeMsgShown;

    bool IsFrozen => NetworkManager.ServerTime.Time < frozenUntilServerTime.Value;

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

    /// <summary>السيرفر يحبس الوحش ببداية الجولة عشان العيال تنتشر.</summary>
    public void ServerFreeze(float seconds)
    {
        if (IsServer || NetworkManager.Singleton.IsServer)
            frozenUntilServerTime.Value = NetworkManager.Singleton.ServerTime.Time + seconds;
    }

    void Update()
    {
        if (!IsOwner) return;
        if (GameManager.Instance == null ||
            GameManager.Instance.State.Value != GameManager.GameState.Playing) return;

        if (IsFrozen && !freezeMsgShown && GameUI.Instance != null)
        {
            freezeMsgShown = true;
            GameUI.Instance.ShowEvent("استنى شوي... خل الفرايس تنتشر 😈");
        }

        Look();
        if (!IsFrozen) Move();

        cooldownTimer -= Time.deltaTime;
        if (Input.GetMouseButtonDown(0) && cooldownTimer <= 0f && !IsFrozen)
            Attack();
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
        Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
        Vector3 move = transform.TransformDirection(input.normalized) * moveSpeed;

        if (cc.isGrounded) verticalVelocity = -1f;
        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity;
        cc.Move(move * Time.deltaTime);
    }

    void Attack()
    {
        cooldownTimer = attackCooldown;
        if (roarSound != null) roarSound.Play();

        // شعاع من الكاميرا — إذا قدامنا ناجي نمسكه
        Ray ray = new Ray(cameraHolder.position, cameraHolder.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, attackRange))
        {
            PlayerController victim = hit.collider.GetComponentInParent<PlayerController>();
            if (victim != null && !victim.IsGhost)
            {
                victim.GetCaughtServerRpc();
                if (GameUI.Instance != null)
                    GameUI.Instance.ShowEvent("مسكته! 😈 ههههه");
            }
        }
    }
}
