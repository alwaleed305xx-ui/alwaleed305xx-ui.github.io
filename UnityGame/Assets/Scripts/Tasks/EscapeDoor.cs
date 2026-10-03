using UnityEngine;

/// <summary>
/// باب الهروب — مقفول لين تخلص كل المهام، وبعدها أي ناجي يوصله ينجو.
/// حطه على موديل باب مع Collider (الفتح يصير محلياً عند كل لاعب
/// لأن TaskManager يتزامن عبر المهام نفسها).
/// </summary>
public class EscapeDoor : MonoBehaviour
{
    [Header("اختياري: جسم يختفي لما يفتح الباب (مثل ضلفة الباب)")]
    public GameObject closedVisual;
    public AudioSource openSound;

    public bool IsUnlocked { get; private set; }

    public void Unlock()
    {
        if (IsUnlocked) return;
        IsUnlocked = true;
        if (closedVisual != null) closedVisual.SetActive(false);
        if (openSound != null) openSound.Play();
    }

    public void TryEscape(PlayerController player)
    {
        if (!IsUnlocked)
        {
            if (GameUI.Instance != null)
                GameUI.Instance.ShowEvent("🔒 الباب مقفول! خلصوا المهام أول");
            return;
        }
        if (player.IsGhost || !player.IsOwner) return;

        player.EscapeServerRpc();
    }
}
