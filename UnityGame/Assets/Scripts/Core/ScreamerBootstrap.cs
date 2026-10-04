using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Registers the game's network prefabs (survivor and monster pawns) with
/// the NetworkManager before any connection starts, so nobody has to
/// maintain the Network Prefabs list by hand. Lives on the NetworkManager's
/// GameObject; RoundFlowFactory (and the editor wizard) wires it up.
/// </summary>
public class ScreamerBootstrap : MonoBehaviour
{
    [Header("Prefabs registered with the network layer on startup")]
    public GameObject[] networkPrefabs;

    bool registered;

    void Start()
    {
        RegisterPrefabs();
    }

    /// <summary>
    /// Idempotent registration; safe to call again if the NetworkManager
    /// appeared after this component's Start ran.
    /// </summary>
    public void RegisterPrefabs()
    {
        if (registered) return;
        if (NetworkManager.Singleton == null || networkPrefabs == null) return;

        foreach (GameObject prefab in networkPrefabs)
        {
            if (prefab == null) continue;
            NetworkManager.Singleton.AddNetworkPrefab(prefab);
        }
        registered = true;
    }
}
