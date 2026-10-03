using UnityEngine;

/// <summary>
/// يعدّ المهام المنجزة — إذا خلصت كلها يفتح باب الهروب.
/// حطه على GameObject فاضي في مشهد Game واربط فيه كل المهام والباب.
/// </summary>
public class TaskManager : MonoBehaviour
{
    public static TaskManager Instance;

    [Header("اربط هنا كل المهام الموجودة بالخريطة")]
    public TaskBase[] allTasks;

    [Header("باب الهروب — يفتح إذا خلصت كل المهام")]
    public EscapeDoor escapeDoor;

    public int CompletedCount { get; private set; }
    public int TotalCount => allTasks.Length;

    void Awake() => Instance = this;

    public void OnTaskCompleted(string taskName)
    {
        CompletedCount++;

        if (GameUI.Instance != null)
        {
            GameUI.Instance.ShowEvent("✅ انخلصت مهمة: " + taskName +
                " (" + CompletedCount + "/" + TotalCount + ")");
            GameUI.Instance.UpdateTaskCounter(CompletedCount, TotalCount);
        }

        if (CompletedCount >= TotalCount && escapeDoor != null)
        {
            escapeDoor.Unlock();
            if (GameUI.Instance != null)
                GameUI.Instance.ShowEvent("🚪 باب الهروب انفتح!! اركضوووا!");
        }
    }
}
