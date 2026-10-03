/// <summary>
/// نقطة مركزية لعرض النصوص العربية.
///
/// ملاحظة: Unity UI العادي يعرض العربية بحروف مقطّعة ومعكوسة.
/// الحل: ثبّت أسيت مجاني مثل "Arabic Support" أو "RTL TextMeshPro"
/// من الأسيت ستور، وبعدها عدّل دالة Fix تحت بسطر واحد، مثال:
///     return ArabicSupport.ArabicFixer.Fix(s);
/// وكل نصوص اللعبة بتنصلح مرة وحدة لأنها كلها تمر من هنا.
/// </summary>
public static class ArabicText
{
    public static string Fix(string s)
    {
        return s; // ← بدّل هذا السطر بعد تثبيت أداة تصحيح العربية
    }
}
