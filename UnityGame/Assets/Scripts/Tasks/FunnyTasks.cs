using UnityEngine;

/* ═══════════════════════════════════════════════════════════
   المهام المضحكة الست 😂 — كل وحدة سكربت جاهز
   تحطها على أي جسم بالخريطة (مع Collider) وتضبط القيم من الـ Inspector
   ═══════════════════════════════════════════════════════════ */

/// <summary>
/// 😱 مهمة الصياح — أغبى وأحلى مهمة باللعبة!
/// لازم "تصايح" (تضغط Space بسرعة) 10 مرات... بس كل صيحة
/// الوحش يسمعها من آخر الخريطة. حظ موفق يا بطل.
/// </summary>
public class TaskScream : TaskBase
{
    [Header("إعدادات الصياح")]
    public int screamsNeeded = 10;
    int screams;

    void Reset()
    {
        taskName = "صايح بأعلى صوتك";
        funnyDescription = "اضغط [Space] بسرعة عشان تصايح!\nملاحظة صغيرة: الوحش يسمع كل صيحة 🙂";
        duration = 10f;
        noiseLoudness = 1f; // أعلى ضجة باللعبة — الوحش جااااي
        noiseLabel = "صياح هيستيري!! 😱";
    }

    protected override void OnTaskStart() => screams = 0;

    protected override float ProgressMultiplier()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            screams++;
            // كل صيحة = ضجة فورية إضافية
            if (NoiseSystem.Instance != null)
                NoiseSystem.Instance.MakeNoise(transform.position, 1f, "صيحة رقم " + screams + " 😱");
        }
        // التقدم مربوط بعدد الصيحات مو بالوقت
        return screams >= screamsNeeded ? 100f : 0f;
    }
}

/// <summary>
/// 🎤 الكاريوكي — غنّ على المايك! اضغط الحرف اللي يطلع على الشاشة
/// بالوقت الصح. كل غلطة = نشاز... والنشاز ضجته أقوى 😂
/// </summary>
public class TaskKaraoke : TaskBase
{
    KeyCode[] keys = { KeyCode.J, KeyCode.K, KeyCode.L };
    KeyCode currentKey;
    float keyTimer;

    void Reset()
    {
        taskName = "حفلة كاريوكي 🎤";
        funnyDescription = "اضغط الحرف اللي يطلع لك (J / K / L)\nغلطت؟ نشاز! والوحش يكره النشاز... أو يحبه 🤔";
        duration = 8f;
        noiseInterval = 2f;
        noiseLoudness = 0.6f;
        noiseLabel = "أحد يغني... بفشل 🎤";
    }

    protected override void OnTaskStart() => PickKey();

    void PickKey()
    {
        currentKey = keys[Random.Range(0, keys.Length)];
        keyTimer = 0;
        if (GameUI.Instance != null)
            GameUI.Instance.ShowTaskHint("اضغط: [" + currentKey + "]");
    }

    protected override float ProgressMultiplier()
    {
        keyTimer += Time.deltaTime;
        if (Input.GetKeyDown(currentKey)) { PickKey(); return 3f; } // نغمة صح = تقدم سريع
        foreach (KeyCode k in keys)
            if (k != currentKey && Input.GetKeyDown(k))
            {
                // نشاز! ضجة قوية فورية
                if (NoiseSystem.Instance != null)
                    NoiseSystem.Instance.MakeNoise(transform.position, 0.9f, "نشاز فظييييع 🎸💥");
                PickKey();
                return 0f;
            }
        return 0.5f; // واقف ساكت = تقدم بطيء
    }
}

/// <summary>
/// 🕺 منصة الرقص — اوقف على المنصة وارقص (اضغط الأسهم)!
/// الموسيقى شغالة بأعلى صوت طبعاً... الوحش بعد يحب يرقص 💃
/// </summary>
public class TaskDance : TaskBase
{
    void Reset()
    {
        taskName = "ارقص على المنصة 🕺";
        funnyDescription = "اضغط الأسهم ← ↑ → ↓ عشوائياً وارقص!\nالموسيقى صاخبة... والوحش دي جي محترف";
        duration = 7f;
        noiseInterval = 1f;
        noiseLoudness = 0.7f;
        noiseLabel = "موسيقى صاخبة ورقص 🕺🎵";
    }

    protected override float ProgressMultiplier()
    {
        bool dancing = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow)
                    || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow);
        return dancing ? 2.5f : 0.2f; // وقف عن الرقص = ما تخلص أبد
    }
}

/// <summary>
/// 🍜 اطبخ النودلز — استنى النودلز تستوي... بس لا تخليها تحترق!
/// إذا احترقت ينطلق جهاز إنذار الدخان 🚨 وفضحتنا
/// </summary>
public class TaskNoodles : TaskBase
{
    [Header("نافذة التقليب")]
    public float stirEvery = 2f; // لازم يقلب (يضغط E) كل كم ثانية
    float sinceStir;

    void Reset()
    {
        taskName = "اطبخ النودلز 🍜";
        funnyDescription = "قلّب بزر [E] كل شوي!\nنسيت تقلب = تحترق = إنذار الدخان = الوحش عرف مكانك. عوافي";
        duration = 9f;
        noiseInterval = 3f;
        noiseLoudness = 0.3f;
        noiseLabel = "ريحة طبخ... نودلز؟ 🍜";
    }

    protected override void OnTaskStart() => sinceStir = 0;

    protected override float ProgressMultiplier()
    {
        sinceStir += Time.deltaTime;
        if (Input.GetKeyDown(KeyCode.E)) sinceStir = 0;

        if (sinceStir > stirEvery)
        {
            sinceStir = 0;
            // احترقت!! إنذار دخان = أقوى ضجة
            if (NoiseSystem.Instance != null)
                NoiseSystem.Instance.MakeNoise(transform.position, 1f, "🚨 إنذار دخان!! أحد حرق النودلز");
            if (GameUI.Instance != null)
                GameUI.Instance.ShowTaskHint("احترقت!! 🔥 قلّب يا رجال!");
            return -1f; // يرجع التقدم لورا كمان 😂
        }
        return 1f;
    }
}

/// <summary>
/// 🐔 امسك الدجاجة — فيه دجاجة هاربة بالخريطة، الحقها!
/// (هذي المهمة الوحيدة اللي تخليك تتحرك — بس الدجاجة تصارخ وهي تهرب)
/// حطها على جسم الدجاجة، والدجاجة تهرب منك تلقائياً.
/// </summary>
public class TaskChicken : TaskBase
{
    [Header("هروب الدجاجة")]
    public float fleeSpeed = 5f;
    public float fleeRange = 6f; // تهرب إذا قربت منها
    Vector3 startPos;

    void Reset()
    {
        taskName = "امسك الدجاجة 🐔";
        funnyDescription = "فيه دجاجة مسعورة! الحقها واضغط [E] جنبها\nمشكلة بسيطة: صراخها يسمعه اللي في المريخ";
        duration = 4f; // قصيرة — بس الصعوبة بالمطاردة نفسها
        noiseInterval = 1.2f;
        noiseLoudness = 0.8f;
        noiseLabel = "دجاجة تصارخ 🐔💨";
    }

    void Awake() => startPos = transform.position;

    void LateUpdate()
    {
        // السيرفر فقط يحرك الدجاجة (حط NetworkTransform على جسمها عشان تتزامن)
        if (IsDone || !IsServer) return;
        // الدجاجة تهرب من أقرب لاعب — وتصارخ
        PlayerController[] players = FindObjectsOfType<PlayerController>();
        foreach (PlayerController p in players)
        {
            if (p.IsGhost) continue;
            float dist = Vector3.Distance(transform.position, p.transform.position);
            if (dist < fleeRange)
            {
                Vector3 away = (transform.position - p.transform.position).normalized;
                away.y = 0;
                Vector3 target = transform.position + away * fleeSpeed * Time.deltaTime;
                // ما تبتعد وايد عن منطقتها
                if (Vector3.Distance(target, startPos) < 15f)
                    transform.position = target;
                break;
            }
        }
    }
}

/// <summary>
/// 🚽 دورة المياه المسكونة — السيفون خربان ويطلق أصوات مرعبة.
/// صلّحه... كل محاولة فاشلة = صوت "خرخرة" يفضحك
/// </summary>
public class TaskToilet : TaskBase
{
    void Reset()
    {
        taskName = "صلّح دورة المياه المسكونة 🚽";
        funnyDescription = "اضغط [E] بإيقاع ثابت (مو بسرعة!)\nضغطت بسرعة = السيفون ينفجر بصوت يسمعه الحي كله";
        duration = 8f;
        noiseInterval = 2.5f;
        noiseLoudness = 0.4f;
        noiseLabel = "خرخرة سباكة مريبة 🚽";
    }

    float lastPress = -99f;

    protected override float ProgressMultiplier()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            float gap = Time.time - lastPress;
            lastPress = Time.time;
            if (gap < 0.5f)
            {
                // استعجلت! انفجار سيفون
                if (NoiseSystem.Instance != null)
                    NoiseSystem.Instance.MakeNoise(transform.position, 1f, "💦 انفجار سيفون مدوّي!!");
                if (GameUI.Instance != null)
                    GameUI.Instance.ShowTaskHint("على كيفك!! 💦 بإيقاع هادئ");
                return 0f;
            }
            return 5f; // ضغطة بإيقاع صح = تقدم ممتاز
        }
        return 0.3f;
    }
}
