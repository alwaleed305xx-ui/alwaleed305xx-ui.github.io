using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The per-skin monster ability on [F] (Hide &amp; Shriek update, GDD 14.1).
/// One key, three monsters, three personalities:
/// - ZOMBIE — LUNGE: a 0.45 s forward burst (+9 speed) on an 8 s cooldown.
///   The lunge grunt is a REAL loudness 0.45 noise at the monster's position,
///   so nearby survivors see the ring — you trade stealth for the bite.
/// - MUTANT — ROAR: every living survivor within 16 units yelps
///   involuntarily — a real loudness 0.5 ping AT THEIR POSITION, relayed to
///   the monster through the normal hearing pipe. Wallhack by comedy, 25 s.
/// - MIMIC — [F] belongs to MimicDisguise; this component stays silent.
/// This component also owns the monster's closet interaction: [E] on a
/// HideSpot opens it (and flushes out whoever thought a closet was a plan).
///
/// Lives on the monster prefab next to MonsterController (CharacterFactory
/// wires it). The cooldown clock is server-written and replicated so the HUD
/// prompt and the server validation can never disagree.
/// </summary>
public class MonsterAbilities : NetworkBehaviour
{
    [Header("Zombie lunge (GDD 14.1)")]
    public float lungeCooldown = 8f;
    public float lungeSeconds = 0.45f;
    public float lungeExtraSpeed = 9f;
    [Range(0f, 1f)] public float lungeLoudness = 0.45f;

    [Header("Mutant roar (GDD 14.1)")]
    public float roarCooldown = 25f;
    public float roarRadius = 16f;
    [Range(0f, 1f)] public float roarYelpLoudness = 0.5f;

    [Header("Closet interaction")]
    public float interactRange = 3f;

    const float PromptPollInterval = 0.25f; // 4 Hz, same cadence as MimicDisguise

    // Server-written; replicated so the ready-prompt matches server truth.
    readonly NetworkVariable<double> abilityReadyServerTime = new NetworkVariable<double>(0);

    MonsterController monster;
    MonsterSkinSelector skinSelector;
    MimicDisguise mimic;
    CharacterController cc;

    float dashEndsLocalTime = -1f;
    float promptPollTimer;
    string shownPrompt;

    double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;
    bool AbilityReady => Now >= abilityReadyServerTime.Value;

    public override void OnNetworkSpawn()
    {
        monster = GetComponent<MonsterController>();
        skinSelector = GetComponent<MonsterSkinSelector>();
        mimic = GetComponent<MimicDisguise>();
        cc = GetComponent<CharacterController>();
    }

    public override void OnNetworkDespawn()
    {
        if (shownPrompt != null) SetPrompt(null);
    }

    void Update()
    {
        if (!IsSpawned) return;
        TickDash();

        if (!IsOwner || monster == null || monster.BotDriven) return;
        if (!IsHuntState()) { SetPrompt(null); return; }

        UpdatePromptAndInput();
    }

    // ------------------------- owner input / prompt -------------------------

    void UpdatePromptAndInput()
    {
        // The closet wins the prompt while one is in the crosshair; the
        // ability hint fills the quiet moments between hunts.
        HideSpot aimedSpot = AimedHideSpot();

        promptPollTimer -= Time.deltaTime;
        if (promptPollTimer <= 0f || aimedSpot != null)
        {
            promptPollTimer = PromptPollInterval;
            SetPrompt(ResolvePrompt(aimedSpot));
        }

        if (aimedSpot != null && Input.GetKeyDown(KeyCode.E))
        {
            aimedSpot.RequestInteract();
            return;
        }

        if (Input.GetKeyDown(KeyCode.F))
            TryUseAbility();
    }

    string ResolvePrompt(HideSpot aimedSpot)
    {
        if (aimedSpot != null) return aimedSpot.PromptFor(null);
        if (monster.IsFrozen || !AbilityReady) return null;
        if (mimic != null && mimic.IsBusy) return null;

        switch (SkinIndex())
        {
            case MonsterSkinSelector.SkinZombie: return GameCopy.PromptLunge;
            case MonsterSkinSelector.SkinMutant: return GameCopy.PromptRoar;
            default: return null; // the Mimic's [F] prompt belongs to MimicDisguise
        }
    }

    void TryUseAbility()
    {
        if (monster.IsFrozen || !AbilityReady) return;
        if (mimic != null && mimic.IsBusy) return;

        switch (SkinIndex())
        {
            case MonsterSkinSelector.SkinZombie:
                // The burst starts locally the same frame (owner-authoritative
                // movement); the server stamps the cooldown and makes the noise.
                dashEndsLocalTime = Time.time + lungeSeconds;
                LungeServerRpc();
                break;

            case MonsterSkinSelector.SkinMutant:
                RoarServerRpc();
                break;
        }
    }

    void TickDash()
    {
        if (dashEndsLocalTime < 0f || !IsOwner) return;
        if (Time.time >= dashEndsLocalTime) { dashEndsLocalTime = -1f; return; }
        if (monster == null || monster.IsFrozen || cc == null || !cc.enabled) return;

        // Additive forward push on top of MonsterController's own movement.
        cc.Move(transform.forward * lungeExtraSpeed * Time.deltaTime);
    }

    int SkinIndex() => skinSelector != null ? skinSelector.CurrentSkin : -1;

    HideSpot AimedHideSpot()
    {
        Transform eye = monster.cameraHolder != null ? monster.cameraHolder : transform;
        if (!Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, interactRange,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            return null;
        return hit.collider.GetComponentInParent<HideSpot>();
    }

    void SetPrompt(string prompt)
    {
        if (prompt == shownPrompt) return;
        shownPrompt = prompt;
        if (GameUI.Instance != null) GameUI.Instance.ShowPrompt(prompt);
    }

    // ------------------------- server validation -------------------------

    [ServerRpc]
    void LungeServerRpc()
    {
        if (!ServerValidateAbility(MonsterSkinSelector.SkinZombie)) return;
        abilityReadyServerTime.Value = Now + lungeCooldown;

        if (NoiseSystem.Instance != null)
            NoiseSystem.Instance.ServerMakeNoise(OwnerClientId, transform.position,
                lungeLoudness, NoiseType.Footsteps, GameCopy.NoiseLunge);

        LungeClientRpc(transform.position);
    }

    [ServerRpc]
    void RoarServerRpc()
    {
        if (!ServerValidateAbility(MonsterSkinSelector.SkinMutant)) return;
        abilityReadyServerTime.Value = Now + roarCooldown;

        // Everyone close enough yelps — a real ping at each victim's position,
        // pushed through the normal hearing pipe (culling included).
        var affected = new List<ulong>();
        foreach (IVictim victim in ServerCollectVictims())
        {
            Transform vt = victim.VictimTransform;
            if (vt == null || !victim.IsCatchable) continue;
            if ((vt.position - transform.position).sqrMagnitude > roarRadius * roarRadius) continue;

            affected.Add(victim.ActorId);
            if (NoiseSystem.Instance != null)
                NoiseSystem.Instance.ServerMakeNoise(victim.ActorId, vt.position,
                    roarYelpLoudness, NoiseType.Scream, GameCopy.NoiseYelp);
        }

        RoarClientRpc(transform.position, affected.ToArray());
    }

    bool ServerValidateAbility(int requiredSkin)
    {
        if (!IsHuntState()) return false;
        if (monster == null || monster.IsFrozen || monster.BotDriven) return false;
        if (mimic != null && mimic.IsBusy) return false;
        if (SkinIndex() != requiredSkin) return false;
        return AbilityReady;
    }

    static IEnumerable<IVictim> ServerCollectVictims()
    {
        // Humans first (cheap registry), then bot pawns — the MimicDisguise pattern.
        foreach (PlayerController pc in PlayerController.All)
            if (pc != null) yield return pc;
        foreach (NetworkBehaviour nb in FindObjectsOfType<NetworkBehaviour>())
            if (nb is IVictim v && !(nb is PlayerController)) yield return v;
    }

    // ------------------------- feel (every client) -------------------------

    [ClientRpc]
    void LungeClientRpc(Vector3 at)
    {
        if (AudioDirector.Instance != null)
            AudioDirector.Instance.Play(Sfx.ShuffleThump, at, 1f, 1.35f);

        if (IsOwner && !monster.BotDriven && ScreamerCam.Instance != null)
            ScreamerCam.Instance.Shake(0.12f, lungeSeconds);
    }

    [ClientRpc]
    void RoarClientRpc(Vector3 at, ulong[] affectedActorIds)
    {
        if (AudioDirector.Instance != null)
            AudioDirector.Instance.Play(Sfx.MonsterDrone, at, 1f, 1.6f);
        if (ScreamerCam.Instance != null)
            ScreamerCam.Instance.Shake(IsOwner ? 0.35f : 0.2f, 0.3f);

        // The yelp lands personally on everyone it exposed.
        PlayerController local = PlayerController.Local;
        if (local == null || local.IsGhost) return;
        foreach (ulong id in affectedActorIds)
        {
            if (id != local.OwnerClientId) continue;
            if (ScreenFxOverlay.Instance != null)
                ScreenFxOverlay.Instance.Flash(ScreamerPalette.MonsterRed, 0.25f);
            if (GagFeedback.Instance != null)
                GagFeedback.Instance.Popup(GameCopy.PopupYelped, ScreamerPalette.MonsterRed);
            if (GameUI.Instance != null)
                GameUI.Instance.PulseSelfNoise(roarYelpLoudness);
            break;
        }
    }

    static bool IsHuntState()
    {
        if (GameManager.Instance == null) return true; // isolated module test scene
        GameManager.GameState s = GameManager.Instance.State.Value;
        return s == GameManager.GameState.Playing || s == GameManager.GameState.Finale;
    }
}
