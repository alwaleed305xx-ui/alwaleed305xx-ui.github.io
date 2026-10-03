using Unity.Netcode;
using UnityEngine;

/// <summary>
/// نظام الضجة — روح اللعبة كلها 😂
/// أي مهمة أو حركة تسوي صوت تنادي MakeNoise،
/// والوحش يطلع له سهم على الشاشة يدله على مصدر الصوت!
/// حطه على نفس GameObject حق GameManager.
/// </summary>
public class NoiseSystem : NetworkBehaviour
{
    public static NoiseSystem Instance;

    void Awake() => Instance = this;

    /// <summary>
    /// سوّ ضجة يسمعها الوحش!
    /// loudness من 0 لـ 1 — الصياح = 1 (يوصل لآخر الخريطة)
    /// </summary>
    public void MakeNoise(Vector3 position, float loudness, string funnyLabel)
    {
        MakeNoiseServerRpc(position, loudness, funnyLabel);
    }

    [ServerRpc(RequireOwnership = false)]
    void MakeNoiseServerRpc(Vector3 position, float loudness, string funnyLabel)
    {
        NoiseClientRpc(position, loudness, funnyLabel);
    }

    [ClientRpc]
    void NoiseClientRpc(Vector3 position, float loudness, string funnyLabel)
    {
        // الوحش فقط هو اللي "يسمع" الضجة على شاشته
        if (GameManager.Instance != null && GameManager.Instance.IAmMonster)
        {
            if (GameUI.Instance != null)
                GameUI.Instance.ShowNoisePing(position, loudness, funnyLabel);
        }
    }
}
