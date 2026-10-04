/// <summary>
/// The single English copy catalog for SCREAMER. Every player-facing line the
/// game ships lives here, implementing GDD section 11 verbatim (plus the menu
/// and HUD chrome strings the screens need). Gameplay modules consume these
/// constants instead of inlining prose, which keeps the voice consistent -
/// deadpan, self-aware, slightly mean - and lets the build-time string audit
/// police one file's tone instead of forty.
/// </summary>
public static class GameCopy
{
    // ------------------------- Noise labels (GDD 4.2) -------------------------

    public const string NoiseFootsteps = "RUNNING. SOMEWHERE.";
    public const string NoiseScream = "HYSTERICAL SCREAMING";
    public const string NoiseKaraokeAmbient = "SOMEONE'S SINGING. BADLY.";
    public const string NoiseKaraokeWrongNote = "THAT WAS NOT A NOTE.";
    public const string NoiseDance = "UNLICENSED DISCO";
    public const string NoiseCooking = "SOMETHING'S COOKING";
    public const string NoiseNoodleBurn = "SMOKE ALARM. BLESS THEM.";
    public const string NoiseChicken = "THE CHICKEN. OBVIOUSLY.";
    public const string NoisePlumbing = "SUSPICIOUS PLUMBING";
    public const string NoiseToiletGeyser = "THE PIPES HAVE OPINIONS.";
    public const string NoiseSlap = "SOMEONE'S HITTING THE FURNITURE";
    public const string NoiseBoo = "A SCREAM? A FAKE? WHO KNOWS.";
    public const string NoiseFinale = "THEY'RE AT THE DOOR. GO. NOW.";

    /// <summary>
    /// Default label per noise category. Specific events (wrong notes, burns,
    /// geysers) pass their own sharper constant from this file instead.
    /// </summary>
    public static string NoiseLabel(NoiseType type)
    {
        switch (type)
        {
            case NoiseType.Footsteps: return NoiseFootsteps;
            case NoiseType.Scream: return NoiseScream;
            case NoiseType.Music: return NoiseKaraokeAmbient;
            case NoiseType.Alarm: return NoiseNoodleBurn;
            case NoiseType.Chicken: return NoiseChicken;
            case NoiseType.Plumbing: return NoisePlumbing;
            case NoiseType.Cooking: return NoiseCooking;
            case NoiseType.Slap: return NoiseSlap;
            case NoiseType.Boo: return NoiseBoo;
            case NoiseType.DeathScream: return ""; // never shown; never relayed
            default: return "";
        }
    }

    // ------------------------- Menu / lobby ambience (GDD 11.1) -------------------------

    public static readonly string[] StickyPatchNotes =
    {
        "v1.3: chicken 4% angrier",
        "v1.2: toilet grudge persistence fixed (it still remembers)",
        "v1.1: monster now legally allowed in the kitchen",
        "v1.0: removed the second monster. there was never a second monster."
    };

    public static readonly string[] LobbyTips =
    {
        "Tip: the monster hears sprinting.",
        "Tip: it also hears screaming.",
        "Tip: you will do both.",
        "Tip: that chair was not there last round.",
        "Tip: the fireplace dims when something is close. The fireplace is a coward too.",
        "Tip: dead friends get one BOO. Choose your friends carefully."
    };

    /// <summary>Shown in the ticker ONLY on house-in-a-mood modifier rounds.</summary>
    public const string TipHouseMood = "Tonight the house is in a mood.";

    public const string QuitConfirm = "Leave? The chicken will remember this.";
    public const string QuitYes = "YES, FLEE";
    public const string QuitNo = "NO, STAY";

    // ------------------------- HOW TO DIE (GDD 11.2) -------------------------

    public const string HowToDieHeader = "HOW TO DIE (A BEGINNER'S GUIDE)";

    public static readonly string[] HowToDieLines =
    {
        "SCREAM THERAPY - Mash [SPACE] to let it all out. Fun fact: the monster also hears your therapy.",
        "KARAOKE NIGHT - Hit the right key. Miss, and the feedback screech files a noise complaint with the monster.",
        "DANCE FLOOR - Mash the arrows. The bass is excellent. The bass is also a homing beacon.",
        "INSTANT NOODLES - Stir with [E] or the smoke alarm tells everyone where you live.",
        "CATCH THE CHICKEN - It's in the yard. It's furious. It screams like you do.",
        "HAUNTED TOILET - Plunge calmly with [E]. Rush it and the pipes go full geyser."
    };

    public const string HowToDieLaws = "1. Everything you do is loud.  2. The monster hears everything.  3. See laws 1 and 2.";

    // ------------------------- Role reveal & task flavor (GDD 11.3) -------------------------

    public const string RoleItsYou = "IT'S YOU.";
    public const string RoleSurvivorCard = "YOU'RE A SURVIVOR\nDo 6 chores. Quietly. (You won't.)";
    public const string RoleMonsterCard = "YOU ARE THE MONSTER\nEat your friends. They'd do the same to you.";

    public const string FlavorScreamTherapy = "Let it all out. All of it. Everyone's listening.";
    public const string FlavorKaraoke = "J, K, L. Three notes. How hard can it be. (Hard.)";
    public const string FlavorDance = "Nobody's watching. Something is listening.";
    public const string FlavorNoodles = "You had ONE noodle job.";
    public const string FlavorChicken = "It ran this way. It's still screaming about it.";
    public const string FlavorToilet = "Calm hands. Calm heart. Calm plunger.";

    public const string TaskCancelled = "Chickened out. The actual chicken is judging you.";
    public const string TaskComplete = "DONE. Somehow.";
    public const string TaskWalkAway = "[Q] walk away from this";

    // ------------------------- Monster-side copy (GDD 11.4) -------------------------

    public const string LockdownHeader = "HEAD START - THEY RUN, YOU WAIT.";
    public const string MonsterRelease = "GO EAT.";

    public const string SkinIntroZombie = "Slow. Relentless. Smells incredible.";
    public const string SkinIntroMutant = "Fast. Angry. Skipped therapy.";
    public const string SkinIntroMimic = "That chair was not a chair.";

    public const string MimicPromptDisguise = "[F] BECOME FURNITURE";
    public const string MimicWhileDisguised = "YOU ARE A COUCH. LIVE THE COUCH.";

    /// <summary>Skin intro tagline by MonsterSkinSelector index (0 zombie, 1 mutant, 2 mimic).</summary>
    public static string SkinIntro(int skinIndex)
    {
        switch (skinIndex)
        {
            case 0: return SkinIntroZombie;
            case 1: return SkinIntroMutant;
            case 2: return SkinIntroMimic;
            default: return "";
        }
    }

    /// <summary>"RELEASED IN 0:07" - the lockdown HUD countdown line.</summary>
    public static string LockdownReleasedIn(float secondsLeft)
    {
        if (secondsLeft < 0f) secondsLeft = 0f;
        int total = (int)System.Math.Ceiling(secondsLeft);
        return "RELEASED IN " + (total / 60) + ":" + (total % 60).ToString("00");
    }

    // ------------------------- Event feed lines (GDD 11.5) -------------------------

    public static string EventJoin(string n) => n + " walked in. Bold.";
    public static string EventLeave(string n) => n + " left. Smart.";
    public static string EventBotLeft(string n) => n + " had to go. Nobody asked where.";
    public static string EventSpectating(string n) => n + " is watching. Judging, mostly.";

    public static string EventTaskDone(string n, string taskName)
    {
        string key = (taskName ?? "").ToUpperInvariant();
        if (key.Contains("SCREAM")) return n + " finished SCREAM THERAPY. SO MUCH PROGRESS.";
        if (key.Contains("KARAOKE")) return n + " nailed KARAOKE. The neighbors called anyway.";
        if (key.Contains("DANCE")) return n + " survived the DANCE FLOOR.";
        if (key.Contains("NOODLE")) return n + " cooked noodles without arson. Growth.";
        if (key.Contains("CHICKEN")) return n + " CAUGHT THE CHICKEN. The chicken disagrees.";
        if (key.Contains("TOILET")) return n + " tamed the toilet. Respect.";
        return n + " finished " + key + ". SO MUCH PROGRESS.";
    }

    public static string EventNoodleBurn(string n) => n + " BURNED THE NOODLES. Classic " + n + ".";
    public static string EventWrongNote(string n) => n + " hit a note that doesn't exist.";
    public static string EventAngeredPipes(string n) => n + " angered the pipes.";
    public static string EventInnocentSlap(string n) => n + " slapped an innocent chair.";

    public static string EventCaught(string n) => "The monster ate " + n + ".";
    public const string BannerCaught = "YOU'RE DEAD. GREAT NEWS: NO MORE CHORES.";

    public static string EventEscaped(string n) => n + " escaped! " + n + " would like everyone to know that.";
    public static string EventSoleEscape(string n) => n + " left everyone behind. Final girl behavior.";

    public static string EventBoo(string ghost, string target) =>
        ghost + " booed " + target + ". From beyond the grave. Petty.";

    // ------------------------- Endings (GDD 11.5) -------------------------

    public const string HeadlineSurvivorsWin = "SURVIVORS ESCAPED";
    public const string SubSurvivorsWin = "The monster is doing breathing exercises.";
    public const string HeadlineMonsterWins = "EVERYBODY DIED :)";
    public const string SubMonsterWins = "The chores remain unfinished. Typical.";
    public const string MonsterQuit = "The monster rage-quit. Nature is healing.";
    public const string HostQuit = "THE HOST UNPLUGGED THE HOUSE.";

    // ------------------------- Door / finale (GDD 11.5) -------------------------

    public const string DoorLocked = "Locked. Six padlocks. Subtle.";
    public const string DoorFinaleSign = "SCREAM-POWERED HYDRAULICS. YES, REALLY.";
    public const string FinalePrompt = "EVERYBODY SCREAM INTO THE DOOR";
    public const string ClipToast = "CLIP THAT. YOU KNOW YOU WANT TO.";

    // ------------------------- Pause / settings strings (GDD 11.6) -------------------------

    public const string PauseFooter = "THE MONSTER DOESN'T PAUSE.";
    public const string PauseResume = "RESUME";
    public const string PauseSettings = "SETTINGS";
    public const string PauseAbandon = "ABANDON FRIENDS";
    public const string PauseAbandonMonster = "FLEE LIKE A COWARD";
    public const string ScreamVolumeCaption = "(you will be heard anyway)";
    public const string ShakeMaxLabel = "YES";
    public const string GammaCaption = "Slide until you can barely see the little guy. He can always see you.";

    // ------------------------- Superlative award cards (GDD 11.7) -------------------------

    public const string AwardLoudestHuman = "LOUDEST HUMAN";
    public const string AwardLoudestHumanCaption = "A lighthouse, but for monsters.";
    public const string AwardNoodleArsonist = "NOODLE ARSONIST";
    public const string AwardNoodleArsonistCaption = "Three alarms. One pot.";
    public const string AwardThatWasAJ = "THAT WAS A J";
    public const string AwardThatWasAJCaption = "The note heard around the house.";
    public const string AwardChickensNemesis = "CHICKEN'S NEMESIS";
    public const string AwardChickensNemesisCaption = "43 seconds. One bird. No dignity.";
    public const string AwardFurnitureAbuser = "FURNITURE ABUSER";
    public const string AwardFurnitureAbuserCaption = "The ottoman did nothing wrong.";
    public const string AwardClutchMouth = "CLUTCH MOUTH";
    public const string AwardClutchMouthCaption = "Carried the door scream. Hero.";
    public const string AwardFinalGirl = "FINAL GIRL";
    public const string AwardFinalGirlCaption = "Saw everyone die. Kept doing chores.";
    public const string AwardUseless = "USELESS";
    public const string AwardUselessCaption = "Contributed vibes.";
    public const string AwardVegetarian = "THE VEGETARIAN";
    public const string AwardVegetarianCaption = "A monster of principle. Zero meals.";

    // ------------------------- Killcam captions (GDD 11.8) -------------------------

    /// <summary>Caption context ids carried on KillcamClip.captionContextId.</summary>
    public const byte KillcamContextDefault = 0;
    public const byte KillcamContextMimic = 1;
    public const byte KillcamContextScreamTherapy = 2;
    public const byte KillcamContextSprinting = 3;
    public const byte KillcamContextBathroom = 4;

    public static string KillcamCaption(byte contextId, string victimName)
    {
        string n = (victimName ?? "").ToUpperInvariant();
        switch (contextId)
        {
            case KillcamContextMimic: return n + " DISCOVERED THE OTTOMAN.";
            case KillcamContextScreamTherapy: return n + " DIED DOING WHAT THEY LOVED: SCREAMING.";
            case KillcamContextSprinting: return n + " OUTRAN NOTHING.";
            case KillcamContextBathroom: return n + " VS. PLUMBING: PLUMBING WINS.";
            default: return n + " NEVER SAW IT. EVERYONE ELSE DID.";
        }
    }

    // ------------------------- Menu chrome -------------------------

    public const string TitleWordmark = "SCREAMER";
    public const string MenuPlay = "PLAY";
    public const string MenuHowToDie = "HOW TO DIE";
    public const string MenuSettings = "SETTINGS";
    public const string MenuQuit = "SNEAK AWAY";
    public const string MenuHost = "HOST GAME";
    public const string MenuJoin = "JOIN GAME";
    public const string MenuPractice = "PRACTICE WITH BOTS";
    public const string MenuBack = "BACK";
    public const string JoinFieldLabel = "TRACKING: MANUAL";
    public const string JoinFieldPlaceholder = "127.0.0.1:7777";
    public const string HostFailed = "HOSTING FAILED. THE HOUSE SAID NO.";
    public const string JoinFailed = "COULD NOT FIND THE HOUSE. CHECK THE ADDRESS.";
    public const string Joining = "TRACKING THE HOUSE...";

    public static string VersionFooter(string version) =>
        "v" + version + " - made by people who scream at their own game";

    // ------------------------- Lobby chrome -------------------------

    public const string ReadyUp = "READY UP";
    public const string Unready = "UNREADY";
    public const string StartRoundLabel = "START ROUND";
    public const string AddBot = "+ ADD BOT";
    public const string RemoveBot = "X";
    public const string CopyCode = "COPY";
    public const string CodeCopied = "COPIED";
    public const string InviteFriends = "INVITE FRIENDS";
    public const string RoomCodeHeader = "ROOM CODE";
    public const string ReadyCheck = "READY";

    // ------------------------- HUD chrome -------------------------

    public const string GhostBooArmed = "BOO: ARMED";
    public const string GhostBooSpent = "BOO: SPENT";
    public const string SelfNoiseLabel = "MIC";

    // ------------------------- Results chrome -------------------------

    public const string RematchLabel = "REMATCH";
    public const string BackToLobby = "BACK TO LOBBY";
    public const string ShowResults = "SHOW RESULTS";
    public const string WaitingForOthers = "WAITING FOR THE OTHERS...";
    public const string NoKillcam = "NO KILLS. A SUSPICIOUSLY QUIET NIGHT.";
    public const string KillcamHeader = "TONIGHT'S LOUDEST MISTAKE";
    public const string FateEscaped = "Escaped";
    public const string FateSurvived = "Survived. Allegedly.";
    public const string FateMonster = "Was the monster";

    public static string RematchCount(int votes, int total) => votes + "/" + total + " want a sequel";

    public static string FateEaten(float deathTime)
    {
        if (deathTime < 0f) deathTime = 0f;
        int total = (int)deathTime;
        return "Eaten at " + (total / 60) + ":" + (total % 60).ToString("00");
    }

    // ------------------------- Settings chrome -------------------------

    public const string TabAudio = "AUDIO";
    public const string TabVideo = "VIDEO";
    public const string TabControls = "CONTROLS";
    public const string TabGameplay = "GAMEPLAY";

    public const string SettingMaster = "MASTER";
    public const string SettingSfx = "SFX";
    public const string SettingMusic = "MUSIC";
    public const string SettingAmbience = "AMBIENCE";
    public const string SettingScreamVolume = "SCREAM VOLUME";
    public const string SettingResolution = "RESOLUTION";
    public const string SettingFullscreen = "FULLSCREEN MODE";
    public const string SettingVsync = "VSYNC";
    public const string SettingQuality = "QUALITY";
    public const string QualityLow = "LOW";
    public const string QualityMedium = "MEDIUM";
    public const string QualityCozy = "COZY";
    public const string SettingFov = "FIELD OF VIEW";
    public const string SettingScreenEffects = "SCREEN EFFECTS";
    public const string SettingGamma = "GAMMA";
    public const string SettingSensitivity = "MOUSE SENSITIVITY";
    public const string SettingInvertY = "INVERT Y";
    public const string SettingShake = "SCREEN SHAKE";
    public const string SettingColorblind = "COLORBLIND PALETTE";

    public const string KeyReference =
        "[W A S D]  move\n" +
        "[SHIFT]  sprint (the monster hears it)\n" +
        "[SPACE]  jump / scream therapy\n" +
        "[E]  interact / stir / plunge\n" +
        "[F]  slap furniture (or become it)\n" +
        "[Q]  abandon task\n" +
        "[R]  ready up\n" +
        "[B]  boo (ghosts only, one charge)\n" +
        "[ESC]  pause\n\n" +
        "Rebinding shipped in v1. It did not. We are sorry.";
}
