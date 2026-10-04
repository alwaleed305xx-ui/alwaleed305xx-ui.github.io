using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Runtime-compilable factory for the networking backbone. The editor wizard
/// calls this once while assembling the scene; it must never reference
/// UnityEditor.
///
/// Creates two root objects:
///  - "NetworkManager": NetworkManager + UnityTransport + ScreamerBootstrap
///    (which registers the pawn prefabs before any connection).
///  - "GameManager": GameManager + NoiseSystem + NetworkObject, wired with
///    prefabs and spawn points. As a scene-placed NetworkObject it spawns
///    automatically when the session starts.
/// </summary>
public static class RoundFlowFactory
{
    public const ushort DefaultPort = 7777;

    public static void BuildNetworkAndManagers(GameObject survivorPrefab, GameObject monsterPrefab,
        Vector3[] survivorSpawns, Vector3 monsterSpawn)
    {
        BuildNetworkManager(survivorPrefab, monsterPrefab);
        BuildGameManager(survivorPrefab, monsterPrefab, survivorSpawns, monsterSpawn);
    }

    static void BuildNetworkManager(GameObject survivorPrefab, GameObject monsterPrefab)
    {
        var go = new GameObject("NetworkManager");

        var networkManager = go.AddComponent<NetworkManager>();
        var transport = go.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", DefaultPort, "0.0.0.0");

        // EnableSceneManagement stays at its default (true): integrated scene
        // management is what synchronizes the in-scene defined NetworkObjects
        // (GameManager, task stations, door, bots, killcam) to joining clients.
        // Disabling it makes clients destroy those objects on connect, breaking
        // JOIN GAME. The single Game scene is in Build Settings on both ends,
        // so clients reuse the already-loaded scene; rematch still resets in
        // place with no scene reload.
        networkManager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            ConnectionApproval = false
        };

        var bootstrap = go.AddComponent<ScreamerBootstrap>();
        bootstrap.networkPrefabs = new[] { survivorPrefab, monsterPrefab };
    }

    static void BuildGameManager(GameObject survivorPrefab, GameObject monsterPrefab,
        Vector3[] survivorSpawns, Vector3 monsterSpawn)
    {
        var go = new GameObject("GameManager");
        go.AddComponent<NetworkObject>();

        var gameManager = go.AddComponent<GameManager>();
        gameManager.survivorPrefab = survivorPrefab;
        gameManager.monsterPrefab = monsterPrefab;
        gameManager.survivorSpawnPoints = survivorSpawns ?? new Vector3[0];
        gameManager.monsterSpawnPoint = monsterSpawn;

        go.AddComponent<NoiseSystem>();
    }
}
