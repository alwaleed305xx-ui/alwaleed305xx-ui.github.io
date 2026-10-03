using Unity.Netcode;
using UnityEngine;

/// <summary>
/// يسجّل بريفابات الشبكة (Survivor و Monster) تلقائياً قبل أي اتصال —
/// عشان ما تحتاج تعدل قائمة Network Prefabs يدوياً.
/// ينحط على نفس GameObject حق NetworkManager (أداة التجهيز تسويها عنك).
/// </summary>
public class SayehBootstrap : MonoBehaviour
{
    [Header("بريفابات تتسجل بالشبكة تلقائياً")]
    public GameObject[] networkPrefabs;

    void Start()
    {
        if (NetworkManager.Singleton == null) return;
        foreach (GameObject p in networkPrefabs)
            if (p != null)
                NetworkManager.Singleton.AddNetworkPrefab(p);
    }
}
