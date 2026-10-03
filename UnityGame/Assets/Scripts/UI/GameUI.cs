using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// واجهة اللعبة كاملة: كشف الدور، شريط الأحداث، لوحة المهمة،
/// عداد المهام، وسهم الضجة حق الوحش.
/// أداة التجهيز (قائمة Sayeh بالأعلى) تبنيها وتربطها تلقائياً.
/// </summary>
public class GameUI : MonoBehaviour
{
    public static GameUI Instance;

    [Header("كشف الدور (لوحة تطلع ببداية الجولة)")]
    public GameObject rolePanel;
    public Text roleText;

    [Header("شريط الأحداث (نص أعلى الشاشة)")]
    public Text eventText;

    [Header("لوحة المهمة")]
    public GameObject taskPanel;
    public Text taskNameText;
    public Text taskDescText;
    public Text taskHintText;
    public Slider taskProgressBar;

    [Header("عداد المهام (مثال: 3/6)")]
    public Text taskCounterText;

    [Header("سهم الضجة — يطلع للوحش فقط")]
    public RectTransform noiseArrow;
    public Text noiseLabelText;

    [Header("نهاية الجولة")]
    public GameObject gameOverPanel;
    public Text gameOverText;

    Coroutine eventRoutine;
    Vector3 lastNoisePos;
    float noiseShowUntil;

    void Awake()
    {
        Instance = this;
        if (rolePanel != null) rolePanel.SetActive(false);
        if (taskPanel != null) taskPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (noiseArrow != null) noiseArrow.gameObject.SetActive(false);
        Set(eventText, "");
    }

    // كل النصوص تمر من مصحح العربية (شوف ArabicText.cs)
    static void Set(Text t, string s)
    {
        if (t != null) t.text = ArabicText.Fix(s);
    }

    // ───── كشف الدور ─────

    public void ShowRoleReveal(bool iAmMonster)
    {
        if (rolePanel == null) return;
        rolePanel.SetActive(true);
        Set(roleText, iAmMonster
            ? "أنت الوحش!! 🧟\nكُل عيال... بس أول خلهم ينتشرون"
            : "أنت من العيال! 🏃\nخلصوا المهام واهربوا... وحاول ما تصايح 🤫");
        StartCoroutine(HideAfter(rolePanel, 4f));
    }

    IEnumerator HideAfter(GameObject go, float t)
    {
        yield return new WaitForSeconds(t);
        go.SetActive(false);
    }

    // ───── شريط الأحداث ─────

    public void ShowEvent(string msg)
    {
        if (eventText == null) return;
        if (eventRoutine != null) StopCoroutine(eventRoutine);
        eventRoutine = StartCoroutine(EventRoutine(msg));
    }

    IEnumerator EventRoutine(string msg)
    {
        Set(eventText, msg);
        yield return new WaitForSeconds(4f);
        Set(eventText, "");
    }

    // ───── لوحة المهمة ─────

    public void ShowTaskPanel(string taskTitle, string desc)
    {
        if (taskPanel == null) return;
        taskPanel.SetActive(true);
        Set(taskNameText, taskTitle);
        Set(taskDescText, desc + "\n\n[Q] للهروب من المهمة");
        Set(taskHintText, "");
        if (taskProgressBar != null) taskProgressBar.value = 0;
    }

    public void ShowTaskHint(string hint) => Set(taskHintText, hint);

    public void UpdateTaskProgress(float t)
    {
        if (taskProgressBar != null) taskProgressBar.value = Mathf.Clamp01(t);
    }

    public void HideTaskPanel(string closingMsg)
    {
        if (taskPanel != null) taskPanel.SetActive(false);
        ShowEvent(closingMsg);
    }

    public void UpdateTaskCounter(int doneCount, int total)
        => Set(taskCounterText, "المهام: " + doneCount + "/" + total);

    // ───── سهم الضجة (للوحش) ─────

    public void ShowNoisePing(Vector3 worldPos, float loudness, string label)
    {
        lastNoisePos = worldPos;
        // الضجة الأقوى تظل على الشاشة أطول
        noiseShowUntil = Time.time + Mathf.Lerp(1.5f, 5f, loudness);
        Set(noiseLabelText, label);
        if (noiseArrow != null) noiseArrow.gameObject.SetActive(true);
    }

    void Update()
    {
        if (noiseArrow == null || !noiseArrow.gameObject.activeSelf) return;
        if (Time.time > noiseShowUntil)
        {
            noiseArrow.gameObject.SetActive(false);
            if (noiseLabelText != null) Set(noiseLabelText, "");
            return;
        }

        // نلف السهم باتجاه مصدر الصوت بالنسبة لاتجاه نظر الوحش
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 toNoise = lastNoisePos - cam.transform.position;
        toNoise.y = 0;
        Vector3 forward = cam.transform.forward;
        forward.y = 0;
        float angle = Vector3.SignedAngle(forward, toNoise, Vector3.up);
        noiseArrow.localEulerAngles = new Vector3(0, 0, -angle);
    }

    // ───── نهاية الجولة ─────

    public void ShowGameOver(bool survivorsWon, string msg)
    {
        if (gameOverPanel == null) return;
        gameOverPanel.SetActive(true);
        Set(gameOverText, (survivorsWon ? "🎉 فوز العيال!\n" : "🧟 فوز الوحش!\n") + msg);
        Cursor.lockState = CursorLockMode.None;
    }
}
