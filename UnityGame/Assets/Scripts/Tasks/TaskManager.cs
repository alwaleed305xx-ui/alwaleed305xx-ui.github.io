using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Counts finished chores and drives the diegetic door counter: every task
/// completion shatters one padlock on the cellar door, and the sixth unlocks
/// it (which flips the round into the Finale via EscapeDoor.ServerUnlock).
///
/// Lives on a plain scene GameObject; TaskFactory output is wired into
/// <see cref="allTasks"/> and <see cref="escapeDoor"/> by the setup wizard.
/// Completion state itself is synchronized per task, so the counts here are
/// correct on every client without extra traffic.
/// </summary>
public class TaskManager : MonoBehaviour
{
    public static TaskManager Instance { get; private set; }

    [Header("Every task station in the house (wired by the setup wizard)")]
    public TaskBase[] allTasks = new TaskBase[0];

    [Header("The cellar door the chores unlock")]
    public EscapeDoor escapeDoor;

    /// <summary>Finished chores this round (derived from synchronized task state).</summary>
    public int CompletedCount
    {
        get
        {
            int count = 0;
            if (allTasks != null)
                for (int i = 0; i < allTasks.Length; i++)
                    if (allTasks[i] != null && allTasks[i].IsDone)
                        count++;
            return count;
        }
    }

    public int TotalCount => allTasks != null ? allTasks.Length : 0;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable() => TaskBase.OnAnyTaskCompleted += HandleTaskCompleted;
    void OnDisable() => TaskBase.OnAnyTaskCompleted -= HandleTaskCompleted;

    static bool IsServerProcess =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    void HandleTaskCompleted(TaskBase task)
    {
        if (task == null) return;

        // Every client: credit the chore-doer on the event feed.
        if (GameUI.Instance != null && GameManager.Instance != null)
        {
            string doer = GameManager.Instance.ActorName(task.CompletedByActorId);
            GameUI.Instance.ShowEvent(GameCopy.EventTaskDone(doer, task.taskName));
        }

        // Server: one padlock per chore; the sixth unlocks the door.
        if (IsServerProcess && escapeDoor != null)
        {
            escapeDoor.ServerBreakPadlock();
            if (TotalCount > 0 && CompletedCount >= TotalCount)
                escapeDoor.ServerUnlock();
        }
    }

    /// <summary>
    /// Rematch reset path (called by GameManager.ServerResetToLobby). Resets
    /// every task; the door resets itself via EscapeDoor.ServerReset().
    /// </summary>
    public void ServerResetAll()
    {
        if (!IsServerProcess || allTasks == null) return;
        for (int i = 0; i < allTasks.Length; i++)
            if (allTasks[i] != null)
                allTasks[i].ServerReset();
    }

    /// <summary>
    /// Server: silences any station loop left running by a client that just
    /// disconnected mid-chore (called by GameManager's disconnect handler).
    /// </summary>
    public void ServerClearSoundForClient(ulong clientId)
    {
        if (!IsServerProcess || allTasks == null) return;
        for (int i = 0; i < allTasks.Length; i++)
            if (allTasks[i] != null)
                allTasks[i].ServerClearSoundForClient(clientId);
    }
}
