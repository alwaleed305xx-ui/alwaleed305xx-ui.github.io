using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ⭐ هنا تربط وحوشك الثلاثة! ⭐
///
/// طريقة التركيب على بريفاب "Monster":
/// 1. اسحب داخل البريفاب 3 أبناء — موديل من كل أسيت:
///    [0] الزومبي   ← من FREE Zombie Male AAB
///    [1] المسخ     ← من Morbid Creatures: Mutant
///    [2] الميميك   ← من Mimic prototype
/// 2. اربط الثلاثة في مصفوفة skins تحت
/// 3. كل جولة الوحش يطلع بشكل عشوائي — ويتزامن مع كل اللاعبين تلقائياً
/// </summary>
public class MonsterSkinSelector : NetworkBehaviour
{
    [Header("اسحب هنا موديلات الوحوش الثلاثة")]
    [Tooltip("[0] الزومبي، [1] المسخ، [2] الميميك")]
    public GameObject[] skins;

    // السيرفر يختار والقيمة تتزامن للجميع حتى اللي يدخل متأخر
    NetworkVariable<int> skinIndex = new NetworkVariable<int>(-1);

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            skinIndex.Value = Random.Range(0, Mathf.Max(1, skins.Length));

        skinIndex.OnValueChanged += (_, newVal) => ApplySkin(newVal);
        if (skinIndex.Value >= 0) ApplySkin(skinIndex.Value);
    }

    void ApplySkin(int index)
    {
        for (int i = 0; i < skins.Length; i++)
            if (skins[i] != null)
                skins[i].SetActive(i == index);
    }
}
