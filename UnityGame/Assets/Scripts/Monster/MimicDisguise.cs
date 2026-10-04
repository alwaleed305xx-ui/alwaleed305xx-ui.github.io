using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The Mimic economy (GDD 9.4). While the Mimic skin is active and no living
/// survivor has line of sight within 25 units, the monster owner can press [F] to
/// lock into a whitelisted furniture proxy from MimicPropCatalog. While disguised
/// it is immobile, shows no skin (so no red rim), sits slightly TOO saturated
/// (+10% -- the tell), and twitches for 2 frames every 20-35 seconds. Moving or
/// attacking breaks the disguise with a 0.5 s unfold pop; attacking out of
/// disguise carries a 0.3 s windup (owned by MonsterController). Re-disguise is
/// allowed again after 3 seconds unseen. Slapping the "furniture" triggers the
/// Mimic instantly at point blank.
///
/// Lives on the monster prefab next to MonsterController and MonsterSkinSelector.
/// The unseen check is server-validated; the owner client only asks.
/// </summary>
public class MimicDisguise : NetworkBehaviour
{
    [Header("Disguise rules (GDD 9.4 / 4.4)")]
    public float unseenRadius = 25f;
    public float reDisguiseUnseenSeconds = 3f;
    public float unfoldSeconds = 0.5f;
    public float twitchMinInterval = 20f;
    public float twitchMaxInterval = 35f;
    [Range(0f, 0.5f)] public float saturationTell = 0.10f;

    const float VisibilityPollInterval = 0.25f; // 4 Hz server LOS poll
    const float VictimCacheInterval = 2f;

    // Server-written, replicated so every client dresses/undresses the same proxy.
    // propIndex and the unfold clock are declared BEFORE disguised on purpose:
    // NetworkVariable deltas apply in field-declaration order, so the handler
    // reacting to disguised flipping true always reads the already-updated prop
    // roll (the same pattern TaskBase uses for completedBy/done). Declared the
    // other way around, every remote client would dress the Mimic as the
    // PREVIOUS roll (prop 0 on the first disguise of a round).
    readonly NetworkVariable<int> propIndex = new NetworkVariable<int>(-1);
    readonly NetworkVariable<double> unfoldEndsServerTime = new NetworkVariable<double>(0);
    readonly NetworkVariable<bool> disguised = new NetworkVariable<bool>(false);

    MonsterController monster;
    MonsterSkinSelector skinSelector;
    GameObject proxy;
    Coroutine twitchRoutine;

    // Server-side visibility bookkeeping.
    double lastSeenServerTime;
    double lastBreakServerTime = -1.0;
    float visibilityPollTimer;

    // Shared victim cache (server validation + owner-side prompt estimate).
    readonly List<IVictim> victimCache = new List<IVictim>();
    float victimCacheTimer;

    // Owner-side prompt bookkeeping.
    bool promptShown;
    float promptPollTimer;
    bool promptEligible;

    /// <summary>True while locked into a furniture proxy (synced).</summary>
    public bool IsDisguised => disguised.Value;

    /// <summary>True during the 0.5 s unfold after a break (movement stays locked).</summary>
    public bool IsUnfolding => Now < unfoldEndsServerTime.Value;

    /// <summary>Disguised or unfolding -- MonsterController blocks movement while this is true.</summary>
    public bool IsBusy => IsDisguised || IsUnfolding;

    /// <summary>Fired on the monster owner's client when the disguise toggles (HUD prompt swap).</summary>
    public static event Action<bool> OnLocalDisguiseChanged;

    double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;

    bool IsMimicSkin => skinSelector != null && skinSelector.CurrentSkin == MonsterSkinSelector.SkinMimic;

    public override void OnNetworkSpawn()
    {
        monster = GetComponent<MonsterController>();
        skinSelector = GetComponent<MonsterSkinSelector>();

        disguised.OnValueChanged += OnDisguisedChanged;

        // Treat spawn time as "just seen": the live LOS check in the RPC still decides,
        // but the 3 s re-disguise clock can never dip into pre-spawn time.
        lastSeenServerTime = Now;

        // Late joiners dress an already-disguised Mimic immediately.
        if (disguised.Value) ApplyDisguiseVisuals(true, true);
    }

    public override void OnNetworkDespawn()
    {
        disguised.OnValueChanged -= OnDisguisedChanged;
        StopTwitch();
        if (proxy != null) { Destroy(proxy); proxy = null; }
        if (promptShown) SetPrompt(false);
    }

    void Update()
    {
        if (!IsSpawned) return;
        ServerTrackVisibility();
        OwnerHandleInput();
    }

    // ------------------------- owner input / prompt -------------------------

    void OwnerHandleInput()
    {
        if (!IsOwner || monster == null || monster.BotDriven) return; // bots never disguise
        if (!IsMimicSkin)
        {
            if (promptShown) SetPrompt(false);
            return;
        }

        // Cheap 4 Hz client-side eligibility estimate for the prompt; the server
        // re-validates everything on the actual request.
        promptPollTimer -= Time.deltaTime;
        if (promptPollTimer <= 0f)
        {
            promptPollTimer = VisibilityPollInterval;
            promptEligible = !disguised.Value && !IsUnfolding && !monster.IsFrozen
                             && IsHuntState() && !SeenByAnySurvivor();
        }
        if (disguised.Value || IsUnfolding) promptEligible = false;

        SetPrompt(promptEligible);

        if (promptEligible && Input.GetKeyDown(KeyCode.F))
            TryDisguiseServerRpc();
    }

    void SetPrompt(bool show)
    {
        if (show == promptShown) return;
        promptShown = show;
        if (GameUI.Instance != null)
            GameUI.Instance.ShowPrompt(show ? GameCopy.MimicPromptDisguise : null);
    }

    // ------------------------- server validation -------------------------

    /// <summary>
    /// Monster owner asks to disguise. The server owns the truth about being seen:
    /// no living survivor may have line of sight within 25 units, and after a break
    /// the Mimic must have stayed unseen for 3 full seconds.
    /// </summary>
    [ServerRpc]
    public void TryDisguiseServerRpc()
    {
        if (disguised.Value || IsUnfolding) return;
        if (!IsMimicSkin) return;
        if (monster == null || monster.IsFrozen || monster.BotDriven) return;
        if (!IsHuntState()) return;

        RefreshVictimCache();
        if (SeenByAnySurvivor()) return;

        double now = Now;
        bool hasBrokenBefore = lastBreakServerTime >= 0.0;
        if (hasBrokenBefore && now - Math.Max(lastBreakServerTime, lastSeenServerTime) < reDisguiseUnseenSeconds)
            return;

        propIndex.Value = UnityEngine.Random.Range(0, MimicPropCatalog.PropNames.Length);
        disguised.Value = true;
    }

    /// <summary>Server-side break: move, attack, or a survivor slap. Starts the 0.5 s unfold.</summary>
    public void ServerBreakDisguise()
    {
        if (!IsSpawned || !IsServer || !disguised.Value) return;
        double now = Now;
        lastBreakServerTime = now;
        lastSeenServerTime = now; // conservative: the unseen clock restarts at the reveal
        unfoldEndsServerTime.Value = now + unfoldSeconds;
        disguised.Value = false;
    }

    /// <summary>Owner-side break request (movement or attack input while disguised).</summary>
    public void RequestBreak()
    {
        if (!IsSpawned || !disguised.Value) return;
        if (IsServer) ServerBreakDisguise();
        else if (IsOwner) BreakDisguiseServerRpc();
    }

    [ServerRpc]
    void BreakDisguiseServerRpc() => ServerBreakDisguise();

    /// <summary>Called by the slapping survivor's client when its [F] raycast lands on the proxy.</summary>
    public void NotifySlapped()
    {
        if (!IsSpawned || !disguised.Value) return;
        SlappedServerRpc();
    }

    // SECURITY: client-trusted -- the slapping survivor's client decides it hit the prop
    // (private-lobby trust model, GDD 8.2). The server only confirms the disguise exists.
    [ServerRpc(RequireOwnership = false)]
    void SlappedServerRpc() => ServerBreakDisguise();

    // ------------------------- visibility -------------------------

    void ServerTrackVisibility()
    {
        if (!IsServer || disguised.Value || !IsMimicSkin) return;

        visibilityPollTimer -= Time.deltaTime;
        if (visibilityPollTimer > 0f) return;
        visibilityPollTimer = VisibilityPollInterval;

        if (SeenByAnySurvivor()) lastSeenServerTime = Now;
    }

    bool SeenByAnySurvivor()
    {
        RefreshVictimCacheIfStale();
        Vector3 target = transform.position + Vector3.up * 1.2f;
        foreach (IVictim victim in victimCache)
        {
            if (victim == null || !victim.IsCatchable) continue;
            Transform vt = victim.VictimTransform;
            if (vt == null) continue;
            if ((vt.position - transform.position).sqrMagnitude > unseenRadius * unseenRadius) continue;

            Vector3 eye = vt.position + Vector3.up * 1.6f;
            if (!LineBlocked(eye, target)) return true;
        }
        return false;
    }

    bool LineBlocked(Vector3 eye, Vector3 target)
    {
        // A hit on our own hierarchy means the ray reached the monster: visible.
        if (Physics.Linecast(eye, target, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.transform != null && hit.transform.root != transform.root;
        return false;
    }

    void RefreshVictimCacheIfStale()
    {
        victimCacheTimer -= Time.deltaTime;
        if (victimCacheTimer > 0f && victimCache.Count > 0) return;
        RefreshVictimCache();
    }

    void RefreshVictimCache()
    {
        victimCacheTimer = VictimCacheInterval;
        victimCache.Clear();

        // Humans first (cheap registry)...
        foreach (PlayerController pc in PlayerController.All)
            if (pc != null) victimCache.Add(pc);

        // ...then any other IVictim (bot pawns), without referencing their concrete type.
        foreach (NetworkBehaviour nb in FindObjectsOfType<NetworkBehaviour>())
        {
            if (nb is IVictim v && !(nb is PlayerController) && nb.transform.root != transform.root)
                victimCache.Add(v);
        }
    }

    static bool IsHuntState()
    {
        if (GameManager.Instance == null) return true; // isolated module test scene
        GameManager.GameState s = GameManager.Instance.State.Value;
        return s == GameManager.GameState.Playing || s == GameManager.GameState.Finale;
    }

    // ------------------------- visuals -------------------------

    void OnDisguisedChanged(bool previous, bool next)
    {
        ApplyDisguiseVisuals(next, false);
        if (IsOwner)
        {
            if (next && promptShown) SetPrompt(false);
            OnLocalDisguiseChanged?.Invoke(next);
        }
    }

    void ApplyDisguiseVisuals(bool on, bool instant)
    {
        if (on)
        {
            if (skinSelector != null) skinSelector.HideAllSkins();

            int index = Mathf.Clamp(propIndex.Value, 0, MimicPropCatalog.PropNames.Length - 1);
            proxy = MimicPropCatalog.BuildPropProxy(index, ScreamerPalette.MakeRuntimeMaterial);
            proxy.transform.SetParent(transform, false);
            proxy.transform.localPosition = Vector3.zero;
            proxy.transform.localRotation = Quaternion.identity;

            BoostSaturation(proxy); // the +10% "too clean" tell
            StopTwitch();
            twitchRoutine = StartCoroutine(TwitchLoop(proxy.transform));
        }
        else
        {
            StopTwitch();
            if (skinSelector != null) skinSelector.RefreshSkin();

            if (proxy != null)
            {
                if (instant || !gameObject.activeInHierarchy) Destroy(proxy);
                else StartCoroutine(UnfoldAndDestroy(proxy));
                proxy = null;
            }

            if (!instant && AudioDirector.Instance != null)
                AudioDirector.Instance.Play(Sfx.StampThunk, transform.position, 0.8f);
        }
    }

    void StopTwitch()
    {
        if (twitchRoutine != null) { StopCoroutine(twitchRoutine); twitchRoutine = null; }
    }

    void BoostSaturation(GameObject target)
    {
        foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
        {
            Material m = r.sharedMaterial; // runtime-created per proxy, safe to edit directly
            if (m == null) continue;
            Color.RGBToHSV(m.color, out float h, out float s, out float v);
            m.color = Color.HSVToRGB(h, Mathf.Min(1f, s * (1f + saturationTell)), v);
        }
    }

    /// <summary>A 2-frame twitch every 20-35 s -- blink and you miss the couch flinching.</summary>
    IEnumerator TwitchLoop(Transform prop)
    {
        while (prop != null)
        {
            yield return new WaitForSeconds(UnityEngine.Random.Range(twitchMinInterval, twitchMaxInterval));
            if (prop == null) yield break;

            Quaternion rest = prop.localRotation;
            Vector3 restPos = prop.localPosition;
            prop.localRotation = rest * Quaternion.Euler(0f, UnityEngine.Random.Range(-4f, 4f), 1.5f);
            prop.localPosition = restPos + new Vector3(0.02f, 0f, 0.02f);
            yield return null;
            yield return null;
            if (prop == null) yield break;
            prop.localRotation = rest;
            prop.localPosition = restPos;
        }
    }

    /// <summary>The 0.5 s unfold: the prop pops outward, then collapses as the skin returns.</summary>
    IEnumerator UnfoldAndDestroy(GameObject prop)
    {
        Vector3 baseScale = prop.transform.localScale;
        float t = 0f;
        while (t < unfoldSeconds && prop != null)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / unfoldSeconds);
            // Pop to 125% in the first 40%, then shrink to zero.
            float k = n < 0.4f ? Mathf.Lerp(1f, 1.25f, n / 0.4f) : Mathf.Lerp(1.25f, 0f, (n - 0.4f) / 0.6f);
            prop.transform.localScale = baseScale * k;
            yield return null;
        }
        if (prop != null) Destroy(prop);
    }
}
