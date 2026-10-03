using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// قلب اللعبة — مبني على Unity Netcode for GameObjects
/// (يشتغل مع أسيت Multiplayer Session اللي عندك).
///
/// طريقة التركيب:
/// 1. GameObject فاضي بالمشهد اسمه "GameManager"
/// 2. أضف عليه: GameManager + NoiseSystem + NetworkObject
/// 3. اربط بريفابات Survivor و Monster ونقاط الظهور
/// 4. المضيف (Host) يضغط زر "ابدأ الجولة" المربوط بـ StartRound()
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance;

    [Header("البريفابات (لازم تكون مسجلة في NetworkManager → Network Prefabs)")]
    public GameObject survivorPrefab; // الولد الهارب
    public GameObject monsterPrefab;  // الوحش (فيه الأشكال الثلاثة)

    [Header("نقاط الظهور")]
    public Transform[] survivorSpawns;
    public Transform monsterSpawn;

    [Header("إعدادات الجولة")]
    public float monsterFreezeTime = 10f; // الوحش محبوس بالبداية عشان العيال تنتشر

    public enum GameState : byte { Lobby, Playing, SurvivorsWin, MonsterWins }

    // متغيرات متزامنة — السيرفر يكتبها والكل يقراها
    public NetworkVariable<GameState> State = new NetworkVariable<GameState>(GameState.Lobby);
    public NetworkVariable<ulong> MonsterClientId = new NetworkVariable<ulong>(ulong.MaxValue);

    public bool IAmMonster =>
        NetworkManager.Singleton != null &&
        NetworkManager.Singleton.LocalClientId == MonsterClientId.Value;

    // تتبع سيرفر فقط
    readonly HashSet<ulong> caught = new HashSet<ulong>();
    readonly HashSet<ulong> escaped = new HashSet<ulong>();
    int survivorsTotal;

    void Awake() => Instance = this;

    public override void OnNetworkSpawn()
    {
        MonsterClientId.OnValueChanged += (_, newVal) =>
        {
            if (GameUI.Instance != null && newVal != ulong.MaxValue)
                GameUI.Instance.ShowRoleReveal(IAmMonster);
        };
    }

    /// <summary>
    /// اربط هذي بزر "ابدأ الجولة" — تشتغل عند المضيف فقط.
    /// </summary>
    public void StartRound()
    {
        if (!IsServer) return;
        if (State.Value != GameState.Lobby) return;

        var clients = NetworkManager.Singleton.ConnectedClientsIds;
        if (clients.Count < 2)
        {
            if (GameUI.Instance != null)
                GameUI.Instance.ShowEvent("تحتاجون لاعبين على الأقل! وحش بدون فرايس؟ 😅");
            return;
        }

        // نختار الوحش المحظوظ 😈
        ulong monsterId = clients[Random.Range(0, clients.Count)];
        MonsterClientId.Value = monsterId;
        survivorsTotal = clients.Count - 1;
        caught.Clear();
        escaped.Clear();

        // السيرفر ينزّل شخصية كل لاعب حسب دوره
        int spawnIndex = 0;
        foreach (ulong clientId in clients)
        {
            bool isMonster = clientId == monsterId;
            Vector3 pos;
            if (isMonster)
                pos = monsterSpawn != null ? monsterSpawn.position : Vector3.zero;
            else
            {
                Transform s = survivorSpawns.Length > 0
                    ? survivorSpawns[spawnIndex++ % survivorSpawns.Length] : null;
                pos = s != null ? s.position : Vector3.zero;
            }

            GameObject prefab = isMonster ? monsterPrefab : survivorPrefab;
            GameObject obj = Instantiate(prefab, pos, Quaternion.identity);
            obj.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);

            if (isMonster)
                obj.GetComponent<MonsterController>().ServerFreeze(monsterFreezeTime);
        }

        State.Value = GameState.Playing;
    }

    // ───── الإمساك والهروب (سيرفر فقط) ─────

    public void ServerPlayerCaught(ulong clientId)
    {
        if (!IsServer || !caught.Add(clientId)) return;
        AnnounceClientRpc("😱 الوحش مسك " + PlayerName(clientId) + "!");

        if (caught.Count >= survivorsTotal)
        {
            State.Value = GameState.MonsterWins;
            GameOverClientRpc(false, "الوحش مسك الجميع! 🧟 كان الله بعونكم");
        }
    }

    public void ServerPlayerEscaped(ulong clientId)
    {
        if (!IsServer || !escaped.Add(clientId)) return;
        AnnounceClientRpc("🏃 " + PlayerName(clientId) + " هرب ونجا!");

        if (escaped.Count + caught.Count >= survivorsTotal && escaped.Count > 0)
        {
            State.Value = GameState.SurvivorsWin;
            GameOverClientRpc(true, "العيال نجوا! 🎉 والوحش قاعد يعيط");
        }
    }

    [ClientRpc]
    void AnnounceClientRpc(string msg)
    {
        if (GameUI.Instance != null) GameUI.Instance.ShowEvent(msg);
    }

    [ClientRpc]
    void GameOverClientRpc(bool survivorsWon, string msg)
    {
        if (GameUI.Instance != null) GameUI.Instance.ShowGameOver(survivorsWon, msg);
    }

    string PlayerName(ulong clientId) => "لاعب " + clientId;

    public override void OnNetworkDespawn()
    {
        // إذا الوحش فصل من اللعبة — العيال يفوزون
    }

    void Update()
    {
        if (!IsServer || State.Value != GameState.Playing) return;
        // الوحش انسحب؟ 🐔
        if (!NetworkManager.Singleton.ConnectedClientsIds.Contains(MonsterClientId.Value))
        {
            State.Value = GameState.SurvivorsWin;
            GameOverClientRpc(true, "الوحش انسحب! 🐔 يستاهل");
        }
    }
}
