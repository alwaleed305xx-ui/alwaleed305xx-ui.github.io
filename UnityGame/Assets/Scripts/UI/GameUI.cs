using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// واجهة اللعبة كاملة: كشف الدور، شريط الأحداث، لوحة المهمة،
/// عداد المهام، وسهم الضجة حق الوحش.
///
/// طريقة التركيب: Canvas واحد بالمشهد وحط عليه هذا السكربت،
/// واربط العناصر من الـ Inspector (الأسماء توضح كل شي).
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
    public RectTransform noiseArrow;   // صورة سهم بنص الشاشة
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
        if (eventText != null) eventText.text = "";
    }

    // ───── كشف الدور ─────

    public void ShowRoleReveal(bool iAmMonster)
    {
        if (rolePanel == null) return;
        rolePanel.SetActive(true);
        roleText.text = iAmMonster
            ? "أنت الوحش!! 🧟\nكُل عيال... بس أول خلهم ينتشرون"
            : "أنت من العيال! 🏃\nخلصوا المهام واهربوا... وحاول ما تصايح 🤫";
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
        eventText.text = msg;
        yield return new WaitForSeconds(4f);
        eventText.text = "";
    }

    // ───── لوحة المهمة ─────

    public void ShowTaskPanel(string name, string desc)
    {
        if (taskPanel == null) return;
        taskPanel.SetActive(true);
        taskNameText.text = name;
        taskDescText.text = desc + "\n\n[Q] للهروب من المهمة";
        taskHintText.text = "";
        taskProgressBar.value = 0;
    }

    public void ShowTaskHint(string hint)
    {
        if (taskHintText != null) taskHintText.text = hint;
    }

    public void UpdateTaskProgress(float t)
    {
        if (taskProgressBar != null) taskProgressBar.value = Mathf.Clamp01(t);
    }

    public void HideTaskPanel(string closingMsg)
    {
        if (taskPanel != null) taskPanel.SetActive(false);
        ShowEvent(closingMsg);
    }

    public void UpdateTaskCounter(int donecount, int total)
    {
        if (taskCounterText != null)
            taskCounterText.text = "المهام: " + donecount + "/" + total;
    }

    // ───── سهم الضجة (للوحش) ─────

    public void ShowNoisePing(Vector3 worldPos, float loudness, string label)
    {
        lastNoisePos = worldPos;
        // الضجة الأقوى تظل على الشاشة أطول
        noiseShowUntil = Time.time + Mathf.Lerp(1.5f, 5f, loudness);
        if (noiseLabelText != null) noiseLabelText.text = label;
        if (noiseArrow != null) noiseArrow.gameObject.SetActive(true);
    }

    void Update()
    {
        if (noiseArrow == null || !noiseArrow.gameObject.activeSelf) return;
        if (Time.time > noiseShowUntil)
        {
            noiseArrow.gameObject.SetActive(false);
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
        gameOverText.text = (survivorsWon ? "🎉 فوز العيال!\n" : "🧟 فوز الوحش!\n") + msg;
        Cursor.lockState = CursorLockMode.None;
    }
}
