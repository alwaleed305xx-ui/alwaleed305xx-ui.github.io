using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The results screen (GDD 7.7): the stamped headline, the killcam window
/// playing the round's loudest kill, three polaroid superlatives dealt with
/// stamp-thunks, per-player fate lines, and the REMATCH footer with its 15 s
/// auto-rematch ring. The 3D podium behind it belongs to the world; this
/// overlay frames it, left and right, and leaves the center open.
/// </summary>
public class ResultsUI : MonoBehaviour
{
    public static ResultsUI Instance { get; private set; }

    [Header("Roots")]
    public GameObject resultsRoot;

    [Header("Headline")]
    public RectTransform headlineRect;
    public Text headlineText;
    public Text subText;

    [Header("Killcam")]
    public RawImage killcamImage;
    public Text killcamHeaderText;
    public Text killcamCaptionText;
    public Text killcamEmptyText;

    [Header("Superlatives")]
    public RectTransform polaroidContainer;

    [Header("Player lines")]
    public RectTransform linesContainer;

    [Header("Footer")]
    public Button rematchButton;
    public Text rematchLabel;
    public Text rematchCountText;
    public Image rematchRing;
    public Button backToLobbyButton;
    public Button sneakAwayButton;

    [Header("Reopen affordance (visible while hidden during Results)")]
    public Button reopenButton;

    PlayerRoundResult[] results;
    KillcamClip pendingClip;
    bool hasClip;
    bool voted;
    Coroutine dealRoutine;

    void Awake()
    {
        Instance = this;
        InitVisuals();
    }

    void InitVisuals()
    {
        ScreamerUIStyle.ApplyFonts(gameObject);
        if (rematchRing != null)
        {
            rematchRing.sprite = ScreamerUIStyle.RingSprite(128, 0.86f, 0.1f);
            rematchRing.type = Image.Type.Filled;
            rematchRing.fillMethod = Image.FillMethod.Radial360;
            rematchRing.fillOrigin = (int)Image.Origin360.Top;
            rematchRing.fillClockwise = false;
        }
        if (resultsRoot != null && results == null) resultsRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        GameManager.OnClientResults += HandleResults;
        GameManager.OnClientStateChanged += HandleStateChanged;
        KillcamRecorder.OnClientClipReady += HandleClipReady;
    }

    void OnDisable()
    {
        GameManager.OnClientResults -= HandleResults;
        GameManager.OnClientStateChanged -= HandleStateChanged;
        KillcamRecorder.OnClientClipReady -= HandleClipReady;
    }

    void Start()
    {
        InitVisuals();
        if (rematchButton != null) rematchButton.onClick.AddListener(VoteRematch);
        if (backToLobbyButton != null) backToLobbyButton.onClick.AddListener(Hide);
        if (sneakAwayButton != null) sneakAwayButton.onClick.AddListener(SneakAway);
        if (reopenButton != null) reopenButton.onClick.AddListener(Show);

        WireFeel(rematchButton);
        WireFeel(backToLobbyButton);
        WireFeel(sneakAwayButton);
        WireFeel(reopenButton);
    }

    static void WireFeel(Button button)
    {
        if (button == null) return;
        RectTransform scaleTarget = button.transform.parent as RectTransform;
        ScreamerUIStyle.WireButtonFeel(button, scaleTarget != null ? scaleTarget : (RectTransform)button.transform,
            button.GetComponentInChildren<Text>());
    }

    void Update()
    {
        // The reopen chip only exists while Results is actually live.
        if (reopenButton != null && reopenButton.transform.parent.gameObject.activeSelf && !IsResultsLive())
            SetReopenVisible(false);

        if (resultsRoot == null || !resultsRoot.activeSelf) return;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        UpdateRematchRing();
        UpdateRematchCount();
    }

    static bool IsResultsLive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening &&
               GameManager.Instance != null &&
               GameManager.Instance.State.Value == GameManager.GameState.Results;
    }

    void SetReopenVisible(bool visible)
    {
        if (reopenButton != null && reopenButton.transform.parent.gameObject.activeSelf != visible)
            reopenButton.transform.parent.gameObject.SetActive(visible);
    }

    // ------------------------- Event handlers -------------------------

    void HandleClipReady(KillcamClip clip)
    {
        pendingClip = clip;
        hasClip = true;
    }

    void HandleResults(PlayerRoundResult[] roundResults)
    {
        results = roundResults;
        Show();
    }

    void HandleStateChanged(GameManager.GameState state)
    {
        if (state != GameManager.GameState.Results)
        {
            Hide();
            hasClip = false; // the next round brings its own clip (or none)
        }
    }

    // ------------------------- Show / hide -------------------------

    void Show()
    {
        if (resultsRoot == null || results == null) return;

        resultsRoot.SetActive(true);
        voted = false;
        if (rematchButton != null) rematchButton.interactable = true;
        if (rematchLabel != null) rematchLabel.text = GameCopy.RematchLabel;

        ApplyHeadline();
        BuildPlayerLines();
        StartKillcam();
        SetReopenVisible(false);

        if (dealRoutine != null) StopCoroutine(dealRoutine);
        dealRoutine = StartCoroutine(DealPolaroids());
    }

    void Hide()
    {
        if (resultsRoot == null || !resultsRoot.activeSelf) return;

        resultsRoot.SetActive(false);

        // BACK TO LOBBY only hides the overlay (the server decides when the
        // lobby actually returns), so while Results is still running a small
        // reopen chip stays available - hiding must never cost the rematch vote.
        SetReopenVisible(results != null && IsResultsLive());
        KillcamPlayer.Instance?.Stop();
        if (killcamImage != null) killcamImage.texture = null;
        // hasClip survives a manual hide so SHOW RESULTS can replay the clip;
        // HandleStateChanged clears it when Results actually ends.

        if (dealRoutine != null)
        {
            StopCoroutine(dealRoutine);
            dealRoutine = null;
        }
        ClearChildren(polaroidContainer);
        ClearChildren(linesContainer);
    }

    static void ClearChildren(RectTransform container)
    {
        if (container == null) return;
        for (int i = container.childCount - 1; i >= 0; i--)
            Destroy(container.GetChild(i).gameObject);
    }

    void ApplyHeadline()
    {
        // The outcome mirrors ride inside the Results RPC itself. The
        // SurvivorsWon/MonsterForfeited NetworkVariables land a tick LATER
        // than the RPC on remote clients, so reading them here would stamp
        // last round's headline (every survivor win would read as a loss).
        GameManager gm = GameManager.Instance;
        bool survivorsWon = gm != null && gm.ClientSurvivorsWon;
        bool forfeited = gm != null && gm.ClientMonsterForfeited;

        if (headlineText != null)
        {
            headlineText.text = survivorsWon ? GameCopy.HeadlineSurvivorsWin : GameCopy.HeadlineMonsterWins;
            headlineText.color = survivorsWon ? ScreamerPalette.GhostMint : ScreamerPalette.MonsterRed;
        }
        if (subText != null)
        {
            subText.text = forfeited ? GameCopy.MonsterQuit
                : survivorsWon ? GameCopy.SubSurvivorsWin : GameCopy.SubMonsterWins;
        }

        if (headlineRect != null) StartCoroutine(StampIn(headlineRect));
        AudioDirector.Instance?.PlayUI(Sfx.StampThunk);
        ScreamerCam.Instance?.Shake(0.2f, 0.2f);
    }

    static IEnumerator StampIn(RectTransform rt)
    {
        float t = 0f;
        const float duration = 0.2f;
        while (t < duration && rt != null)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            rt.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, k * k);
            yield return null;
        }
        if (rt != null) rt.localScale = Vector3.one;
    }

    // ------------------------- Killcam -------------------------

    void StartKillcam()
    {
        bool playing = false;

        if (hasClip && KillcamPlayer.Instance != null && killcamImage != null)
        {
            RenderTexture rt = KillcamPlayer.Instance.Play(pendingClip);
            if (rt != null)
            {
                killcamImage.texture = rt;
                playing = true;
                if (killcamCaptionText != null)
                    killcamCaptionText.text = GameCopy.KillcamCaption(pendingClip.captionContextId, VictimName(pendingClip.victimId));
            }
        }

        if (killcamImage != null) killcamImage.enabled = playing;
        if (killcamCaptionText != null) killcamCaptionText.gameObject.SetActive(playing);
        if (killcamEmptyText != null)
        {
            killcamEmptyText.gameObject.SetActive(!playing);
            killcamEmptyText.text = GameCopy.NoKillcam;
        }
    }

    string VictimName(ulong victimId)
    {
        if (results != null)
            foreach (PlayerRoundResult r in results)
                if (r.actorId == victimId)
                    return r.name.ToString();
        return GameManager.Instance != null ? GameManager.Instance.ActorName(victimId) : "";
    }

    // ------------------------- Player lines -------------------------

    void BuildPlayerLines()
    {
        ClearChildren(linesContainer);
        if (linesContainer == null || results == null) return;

        for (int i = 0; i < results.Length; i++)
        {
            PlayerRoundResult r = results[i];
            string fate = r.wasMonster ? GameCopy.FateMonster
                : r.escaped ? GameCopy.FateEscaped
                : r.deathTime >= 0f ? GameCopy.FateEaten(r.deathTime)
                : GameCopy.FateSurvived;

            string line = r.name.ToString() + "  -  " + fate + "  -  noise " + r.noiseEmitted.ToString("0");

            Text text = ScreamerUIStyle.Txt(linesContainer, "Line" + i, line, 20,
                ScreamerPalette.NoodleCream, TextAnchor.MiddleCenter);
            if (r.wasMonster) text.color = ScreamerPalette.MonsterRed;
            else if (r.escaped) text.color = ScreamerPalette.GhostMint;
            ScreamerUIStyle.Place(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -i * 30f), new Vector2(680f, 28f));
        }
    }

    // ------------------------- Polaroids -------------------------

    IEnumerator DealPolaroids()
    {
        ClearChildren(polaroidContainer);
        if (polaroidContainer == null || results == null) yield break;

        SuperlativeEngine.Award[] awards = SuperlativeEngine.Pick(results);
        float[] tilts = { -6f, 3f, -2f };

        for (int i = 0; i < awards.Length && i < 3; i++)
        {
            yield return new WaitForSecondsRealtime(0.4f);
            if (polaroidContainer == null || resultsRoot == null || !resultsRoot.activeSelf) yield break;

            RectTransform card = BuildPolaroid(awards[i], i, tilts[i % tilts.Length]);
            AudioDirector.Instance?.PlayUI(Sfx.StampThunk, 0.85f);
            StartCoroutine(StampIn(card));
        }
        dealRoutine = null;
    }

    RectTransform BuildPolaroid(SuperlativeEngine.Award award, int index, float tilt)
    {
        RectTransform card = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(polaroidContainer, "Polaroid" + index),
            new Vector2(0.5f, 1f), new Vector2((index - 1) * 30f, -index * 190f), new Vector2(280f, 330f));
        card.localEulerAngles = new Vector3(0f, 0f, tilt);

        Image shadow = ScreamerUIStyle.Img(card, "Shadow", ScreamerUIStyle.WithAlpha(ScreamerPalette.InkBlack, 0.9f));
        ScreamerUIStyle.Stretch(shadow.rectTransform);
        shadow.rectTransform.offsetMin = new Vector2(4f, -4f);
        shadow.rectTransform.offsetMax = new Vector2(4f, -4f);

        Image paper = ScreamerUIStyle.Img(card, "Paper", ScreamerPalette.NoodleCream);
        ScreamerUIStyle.Stretch(paper.rectTransform);

        // The "photo": an ink-dark pane with the winner's color and initial.
        Image photo = ScreamerUIStyle.Img(paper.transform, "Photo", ScreamerUIStyle.WithAlpha(ScreamerPalette.MidnightPlum, 0.95f));
        ScreamerUIStyle.Place(photo.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(248f, 190f));

        string winnerName = WinnerName(award.actorId, out int colorIndex);
        Color accent = ScreamerPalette.Survivor(colorIndex);

        Image face = ScreamerUIStyle.Img(photo.transform, "Face", accent);
        ScreamerUIStyle.Place(face.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(84f, 84f));
        face.sprite = ScreamerUIStyle.Disc();

        string initial = string.IsNullOrEmpty(winnerName) ? "?" : winnerName.Substring(0, 1).ToUpperInvariant();
        Text initialText = ScreamerUIStyle.Header(photo.transform, "Initial", initial, 48,
            ScreamerPalette.InkBlack, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Place(initialText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(84f, 84f));

        Text nameText = ScreamerUIStyle.Txt(photo.transform, "Winner", winnerName, 18,
            ScreamerPalette.NoodleCream, TextAnchor.LowerCenter);
        ScreamerUIStyle.Place(nameText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(230f, 24f));

        Text title = ScreamerUIStyle.Header(paper.transform, "Title", award.title, 24,
            ScreamerPalette.InkBlack, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Place(title.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 76f), new Vector2(260f, 32f));

        Text caption = ScreamerUIStyle.Txt(paper.transform, "Caption", award.caption, 16,
            ScreamerUIStyle.WithAlpha(ScreamerPalette.InkBlack, 0.75f), TextAnchor.UpperCenter);
        ScreamerUIStyle.Place(caption.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(260f, 50f));

        ScreamerUIStyle.ApplyFonts(card.gameObject);
        return card;
    }

    string WinnerName(ulong actorId, out int colorIndex)
    {
        colorIndex = (int)(actorId % 7);

        GameManager gm = GameManager.Instance;
        if (gm != null && gm.Roster != null)
        {
            for (int i = 0; i < gm.Roster.Count; i++)
            {
                if (gm.Roster[i].actorId != actorId) continue;
                colorIndex = gm.Roster[i].colorIndex;
                break;
            }
        }

        if (results != null)
            foreach (PlayerRoundResult r in results)
                if (r.actorId == actorId)
                    return r.name.ToString();
        return gm != null ? gm.ActorName(actorId) : "";
    }

    // ------------------------- Footer -------------------------

    void VoteRematch()
    {
        if (voted || GameManager.Instance == null) return;
        voted = true;
        GameManager.Instance.VoteRematchServerRpc();
        if (rematchButton != null) rematchButton.interactable = false;
        if (rematchLabel != null) rematchLabel.text = GameCopy.WaitingForOthers;
    }

    void SneakAway()
    {
        Hide();
        SetReopenVisible(false); // leaving the session, nothing to reopen
        MenuUI.NotifyIntentionalDisconnect();
        BackendSelector.Active.Shutdown();
    }

    void UpdateRematchRing()
    {
        if (rematchRing == null || GameManager.Instance == null || NetworkManager.Singleton == null) return;

        double endsAt = GameManager.Instance.StateEndsAtServerTime.Value;
        float total = Mathf.Max(1f, GameManager.Instance.resultsSeconds);
        if (endsAt <= 0)
        {
            rematchRing.fillAmount = 0f;
            return;
        }
        float left = (float)(endsAt - NetworkManager.Singleton.ServerTime.Time);
        rematchRing.fillAmount = Mathf.Clamp01(left / total);
    }

    void UpdateRematchCount()
    {
        if (rematchCountText == null || GameManager.Instance == null) return;

        // The server mirrors rematch votes into the roster ready flags.
        int votes = 0, total = 0;
        var roster = GameManager.Instance.Roster;
        if (roster != null)
        {
            for (int i = 0; i < roster.Count; i++)
            {
                total++;
                if (roster[i].ready) votes++;
            }
        }
        rematchCountText.text = GameCopy.RematchCount(votes, Mathf.Max(total, 1));
    }

    // ------------------------- Construction (called by UiFactory) -------------------------

    public static ResultsUI Build(Transform canvasRoot)
    {
        RectTransform root = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(canvasRoot, "ResultsUI"));
        ResultsUI ui = root.gameObject.AddComponent<ResultsUI>();

        RectTransform results = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(root, "ResultsRoot"));
        ui.resultsRoot = results.gameObject;

        // ---------- Headline ----------
        ui.headlineText = ScreamerUIStyle.Header(results, "Headline", "", 96,
            ScreamerPalette.GhostMint, TextAnchor.MiddleCenter);
        ui.headlineRect = ui.headlineText.rectTransform;
        ScreamerUIStyle.Place(ui.headlineRect, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1500f, 120f));
        ui.headlineRect.localEulerAngles = new Vector3(0f, 0f, 4f);

        ui.subText = ScreamerUIStyle.Txt(results, "Sub", "", 24,
            ScreamerPalette.NoodleCream, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Place(ui.subText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1200f, 34f));

        // ---------- Killcam window (left) ----------
        RectTransform killcamPlate = ScreamerUIStyle.Plate(results, "KillcamWindow",
            new Vector2(0f, 0.5f), new Vector2(48f, 40f), new Vector2(656f, 428f));

        ui.killcamHeaderText = ScreamerUIStyle.Header(killcamPlate, "Header", GameCopy.KillcamHeader, 18,
            ScreamerPalette.InkBlack, TextAnchor.MiddleLeft);
        ScreamerUIStyle.Place(ui.killcamHeaderText.rectTransform, new Vector2(0f, 1f), new Vector2(10f, -4f), new Vector2(500f, 24f));
        ui.killcamHeaderText.rectTransform.pivot = new Vector2(0f, 1f);

        Image screenBg = ScreamerUIStyle.Img(killcamPlate, "Screen", ScreamerUIStyle.WithAlpha(ScreamerPalette.MidnightPlum, 0.98f));
        ScreamerUIStyle.Place(screenBg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -32f), new Vector2(640f, 360f));

        var rawGo = new GameObject("KillcamView", typeof(RectTransform));
        var rawRt = (RectTransform)rawGo.transform;
        rawRt.SetParent(screenBg.transform, false);
        ScreamerUIStyle.Stretch(rawRt);
        ui.killcamImage = rawGo.AddComponent<RawImage>();
        ui.killcamImage.raycastTarget = false;

        ui.killcamEmptyText = ScreamerUIStyle.Txt(screenBg.transform, "Empty", GameCopy.NoKillcam, 20,
            ScreamerUIStyle.WithAlpha(ScreamerPalette.NoodleCream, 0.7f), TextAnchor.MiddleCenter);
        ScreamerUIStyle.Stretch(ui.killcamEmptyText.rectTransform);

        ui.killcamCaptionText = ScreamerUIStyle.Header(killcamPlate, "Caption", "", 17,
            ScreamerPalette.InkBlack, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Place(ui.killcamCaptionText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(630f, 26f));

        // ---------- Superlatives (right) ----------
        ui.polaroidContainer = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(results, "Polaroids"),
            new Vector2(1f, 1f), new Vector2(-190f, -230f), Vector2.zero);

        // ---------- Player fate lines (center) ----------
        ui.linesContainer = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(results, "PlayerLines"),
            new Vector2(0.5f, 1f), new Vector2(0f, -224f), new Vector2(680f, 260f));

        // ---------- Footer ----------
        ui.rematchButton = ScreamerUIStyle.Btn(results, "Rematch", GameCopy.RematchLabel,
            new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(300f, 64f));
        ui.rematchLabel = ui.rematchButton.GetComponentInChildren<Text>();

        Image ring = ScreamerUIStyle.Img((RectTransform)ui.rematchButton.transform.parent, "AutoRing", ScreamerPalette.ScreamYellow);
        ScreamerUIStyle.Place(ring.rectTransform, new Vector2(0f, 0.5f), new Vector2(-46f, 0f), new Vector2(64f, 64f));
        ui.rematchRing = ring;

        ui.rematchCountText = ScreamerUIStyle.Txt(results, "RematchCount", "", 18,
            ScreamerPalette.NoodleCream, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Place(ui.rematchCountText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 132f), new Vector2(400f, 26f));

        ui.backToLobbyButton = ScreamerUIStyle.Btn(results, "BackToLobby", GameCopy.BackToLobby,
            new Vector2(0.5f, 0f), new Vector2(-330f, 60f), new Vector2(280f, 56f));
        ui.sneakAwayButton = ScreamerUIStyle.Btn(results, "SneakAway", GameCopy.MenuQuit,
            new Vector2(0.5f, 0f), new Vector2(330f, 60f), new Vector2(280f, 56f));

        // ---------- Reopen chip (sibling of resultsRoot so hiding it survives) ----------
        ui.reopenButton = ScreamerUIStyle.Btn(root, "ReopenResults", GameCopy.ShowResults,
            new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(220f, 40f));
        ui.reopenButton.transform.parent.gameObject.SetActive(false);

        return ui;
    }
}
