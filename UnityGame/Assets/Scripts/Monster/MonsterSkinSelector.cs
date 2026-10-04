using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Picks one of the three monster skins on the server and syncs the choice to every
/// client, late joiners included. The skin roots live under a "Skins" child:
/// [0] Skin_Zombie, [1] Skin_Mutant, [2] Skin_Mimic. CharacterFactory builds and
/// wires them; real models later drop in as children of those roots and inherit the
/// code-built rim lights.
/// </summary>
public class MonsterSkinSelector : NetworkBehaviour
{
    public const int SkinZombie = 0;
    public const int SkinMutant = 1;
    public const int SkinMimic = 2;

    [Header("Skin roots, index-aligned: 0 Zombie, 1 Mutant, 2 Mimic")]
    public GameObject[] skins;

    // Server picks once; the value replicates to everyone, including late joiners.
    readonly NetworkVariable<int> skinIndex = new NetworkVariable<int>(-1);

    /// <summary>Synced skin index; -1 until the server has chosen.</summary>
    public int CurrentSkin => skinIndex.Value;

    /// <summary>Raised on every client whenever a skin becomes active (passes the new index).</summary>
    public event Action<int> OnSkinApplied;

    public override void OnNetworkSpawn()
    {
        if (IsServer && skinIndex.Value < 0 && skins != null && skins.Length > 0)
            skinIndex.Value = UnityEngine.Random.Range(0, skins.Length);

        skinIndex.OnValueChanged += OnSkinIndexChanged;
        if (skinIndex.Value >= 0) Apply(skinIndex.Value);
    }

    public override void OnNetworkDespawn()
    {
        skinIndex.OnValueChanged -= OnSkinIndexChanged;
    }

    void OnSkinIndexChanged(int previous, int next) => Apply(next);

    /// <summary>Re-activates the current skin (used when the Mimic drops a disguise).</summary>
    public void RefreshSkin()
    {
        if (skinIndex.Value >= 0) Apply(skinIndex.Value);
    }

    /// <summary>Hides every skin root (used while the Mimic wears a prop proxy -- no red rim, no silhouette).</summary>
    public void HideAllSkins()
    {
        if (skins == null) return;
        foreach (GameObject skin in skins)
            if (skin != null) skin.SetActive(false);
    }

    void Apply(int index)
    {
        if (skins == null) return;
        for (int i = 0; i < skins.Length; i++)
            if (skins[i] != null) skins[i].SetActive(i == index);
        OnSkinApplied?.Invoke(index);
    }
}
