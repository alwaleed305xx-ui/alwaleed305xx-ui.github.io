using Unity.Netcode;
using UnityEngine;

/// <summary>
/// الأساس لكل المهام المضحكة. كل مهمة:
/// - لها اسم ووصف مضحك يطلع للاعب
/// - تاخذ وقت لإنجازها (اللاعب واقف مكانه = فريسة سهلة 😈)
/// - تسوي "ضجة" بفترات — والوحش يسمعها!
///
/// حط المهمة على جسم بالخريطة (مع Collider + NetworkObject) وورّث منها لمهمة جديدة.
/// </summary>
public abstract class TaskBase : NetworkBehaviour
{
    [Header("معلومات المهمة")]
    public string taskName = "مهمة";
    [TextArea] public string funnyDescription = "";
    public float duration = 6f;          // كم ثانية تاخذ المهمة
    public float noiseInterval = 1.5f;   // كل كم ثانية تطلع ضجة
    [Range(0f, 1f)] public float noiseLoudness = 0.5f;
    public string noiseLabel = "ضجة غريبة 🤔";

    [Header("صوت المهمة (اختياري) — يسمعه اللي قريب بالخريطة")]
    public AudioSource taskSound;

    // متزامنة للجميع
    NetworkVariable<bool> done = new NetworkVariable<bool>(false);
    NetworkVariable<bool> soundOn = new NetworkVariable<bool>(false);

    public bool IsDone => done.Value;

    protected PlayerController currentPlayer;
    float progress;
    float noiseTimer;
    bool running;

    public override void OnNetworkSpawn()
    {
        done.OnValueChanged += (_, isDone) =>
        {
            if (isDone && TaskManager.Instance != null)
                TaskManager.Instance.OnTaskCompleted(taskName);
        };
        soundOn.OnValueChanged += (_, on) =>
        {
            if (taskSound == null) return;
            if (on) taskSound.Play(); else taskSound.Stop();
        };
    }

    public void TryStart(PlayerController player)
    {
        if (IsDone || running) return;
        currentPlayer = player;
        running = true;
        progress = 0;
        player.InputLocked = true; // واقف يسوي المهمة — الله يعينه

        if (GameUI.Instance != null)
            GameUI.Instance.ShowTaskPanel(taskName, funnyDescription);

        OnTaskStart();
        SetSoundServerRpc(true);
    }

    void Update()
    {
        if (!running) return;

        // تقدر تلغي بزر Q وتهرب إذا شفت الوحش جاي 😂
        if (Input.GetKeyDown(KeyCode.Q)) { Cancel(); return; }

        progress += Time.deltaTime * ProgressMultiplier();
        progress = Mathf.Max(0, progress);
        noiseTimer += Time.deltaTime;

        if (noiseTimer >= noiseInterval)
        {
            noiseTimer = 0;
            if (NoiseSystem.Instance != null)
                NoiseSystem.Instance.MakeNoise(transform.position, noiseLoudness, noiseLabel);
        }

        if (GameUI.Instance != null)
            GameUI.Instance.UpdateTaskProgress(progress / duration);

        if (progress >= duration) Complete();
    }

    /// <summary>المهام التفاعلية (مثل الكاريوكي) تغير سرعة التقدم حسب أداء اللاعب.</summary>
    protected virtual float ProgressMultiplier() => 1f;
    protected virtual void OnTaskStart() { }
    protected virtual void OnTaskEnd() { }

    void Cancel()
    {
        running = false;
        ReleasePlayer();
        SetSoundServerRpc(false);
        if (GameUI.Instance != null)
            GameUI.Instance.HideTaskPanel("جبنت وهربت 🐔");
        OnTaskEnd();
    }

    void Complete()
    {
        running = false;
        ReleasePlayer();
        CompleteServerRpc();
        if (GameUI.Instance != null)
            GameUI.Instance.HideTaskPanel("خلصتها! ✅");
        OnTaskEnd();
    }

    void ReleasePlayer()
    {
        if (currentPlayer != null) currentPlayer.InputLocked = false;
        currentPlayer = null;
    }

    [ServerRpc(RequireOwnership = false)]
    void CompleteServerRpc()
    {
        done.Value = true;
        soundOn.Value = false;
    }

    [ServerRpc(RequireOwnership = false)]
    void SetSoundServerRpc(bool on)
    {
        if (!done.Value) soundOn.Value = on;
    }
}
