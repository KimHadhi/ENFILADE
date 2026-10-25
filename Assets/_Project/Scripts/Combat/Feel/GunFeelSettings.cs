using UnityEngine;

/// <summary>
/// Global tuning for shot feel, hit feedback and enemy hit reactions. Per-weapon kick and sound live in WeaponData (Feel).
/// Loaded from Resources/Combat/GunFeel; field defaults apply when the asset is missing. Every clip slot is optional:
/// an empty slot uses the built-in synthesized sound (FeelAudio).
/// </summary>
public sealed class GunFeelSettings : ScriptableObject
{
    [Header("Master switches")]
    public bool viewPunch = true;
    public bool muzzleEffects = true;
    public bool shellCasings = true;
    public bool gunSounds = true;
    public bool hitMarkers = true;
    public bool hitFlash = true;
    public bool proceduralBlood = true;
    public bool deathTopple = true;
    public bool killScreenEffect = true;
    public bool scoreFeed = true;
    public bool killFeed = true;
    [Tooltip("Gameplay: bullets push, slow and can interrupt enemies.")]
    public bool enemyFlinch = true;

    [Header("Camera (visual only; bullets still follow the crosshair)")]
    [Range(0f, 1f), Tooltip("View punch multiplier while fully aimed down sights.")]
    public float adsViewPunchScale = .55f;
    [Range(10f, 60f), Tooltip("View punch spring response, 1/seconds. A kick peaks at 1/response (32 = about 31 ms) and settles shortly after.")]
    public float viewPunchResponse = 32f;
    [Range(0f, 4f), Tooltip("Maximum rotational shake at full trauma, degrees.")]
    public float shakeMaxAngle = 1.4f;
    [Range(.5f, 6f), Tooltip("Trauma lost per second.")]
    public float shakeRecovery = 2.6f;
    [Range(5f, 60f)] public float shakeFrequency = 24f;

    [Header("Muzzle")]
    public Color muzzleFlashColor = new Color(1f, .78f, .42f);
    [Min(0f)] public float muzzleLightIntensity = 5f;
    [Min(.5f)] public float muzzleLightRange = 7f;
    [Min(.01f)] public float muzzleFlashSeconds = .045f;
    [Range(0f, 1f)] public float muzzleSmokeAlpha = .16f;

    [Header("Sounds (empty = synthesized)")]
    [Range(0f, 1f)] public float gunshotVolume = .55f;
    [Range(0f, .2f)] public float gunshotPitchJitter = .045f;
    public AudioClip[] dryFireSounds, lowAmmoSounds, magOutSounds, magInSounds, boltSounds;
    [Range(0f, 1f)] public float mechanicalVolume = .45f;
    [Range(0f, 1f), Tooltip("Magazine fraction at which the low-ammo tick starts. It rises in pitch and volume toward empty.")]
    public float lowAmmoFraction = .3f;
    [Tooltip("Hit confirm tick for body and head hits alike; headshots already layer their own flesh/pierce impact (CombatHitAudio).")]
    public AudioClip[] hitMarkerSounds;
    public AudioClip[] killSounds, headshotKillSounds;
    [Range(0f, 1f)] public float hitMarkerVolume = .35f, killVolume = .6f;

    [Header("Hit markers (1920x1080 canvas units)")]
    public Color bodyHitColor = Color.white;
    public Color headHitColor = new Color(1f, .82f, .2f);
    public Color killColor = new Color(1f, .16f, .12f);
    public Color blockedColor = new Color(.62f, .66f, .72f);
    [Min(1f)] public float markerLength = 11f;
    [Min(.5f)] public float markerThickness = 2.6f;
    [Min(0f)] public float markerGap = 9f;
    [Min(.05f)] public float hitMarkerSeconds = .2f;
    [Min(.05f)] public float killMarkerSeconds = .5f;

    [Header("Kill screen effect (GTA-style pulse)")]
    [Tooltip("Kills that get the screen pulse, hit-stop and camera thump. Off for ordinary gun kills so mowing down a crowd stays readable; " +
        "rare, deliberate kills (execution, perfect-defense counter) keep the full impact.")]
    public bool killPulseOnExecution = true;
    public bool killPulseOnCounter = true, killPulseOnGunKill, killPulseOnOtherKill;
    [Range(-100f, 0f)] public float killSaturation = -60f;
    [Range(0f, 2f), Tooltip("Brightness pop. Bright scenes white out quickly; 0.25 reads clearly without blinding.")]
    public float killExposure = .25f;
    [Range(0f, 1f)] public float killVignette = .34f;
    [Range(0f, 1f)] public float killChromatic = .45f;
    [Range(-1f, 0f)] public float killLensDistortion = -.14f;
    [Range(1f, 2f), Tooltip("Executions and headshot kills multiply the pulse by this much.")]
    public float headshotKillBoost = 1.35f;
    [Min(.05f)] public float killPulseSeconds = .5f;
    [Range(.05f, 1f)] public float killHitStopScale = .18f;
    [Range(0f, .2f), Tooltip("Real-time hit-stop on a pulsed kill. 0 disables it.")]
    public float killHitStopSeconds = .035f;
    [Range(0f, .2f), Tooltip("Hit-stop for executions and headshot kills.")]
    public float headshotKillHitStopSeconds = .06f;

    [Header("Score (kills only, Call of Duty style)")]
    [Tooltip("Score, event-list and kill-feed font. Empty uses the combat prompt font. Default: Pretendard ExtraBold.")]
    public TMPro.TMP_FontAsset scoreFont;
    [Tooltip("The XP suffix, multi-kill and special-kill labels.")]
    public Color scoreColor = new Color(1f, .83f, .26f);
    [Tooltip("Kill score position relative to the crosshair, 1920x1080 canvas units (y up). The number ends here; events list to its right. " +
        "Lower right keeps it apart from damage numbers, which rise above the hit point.")]
    public Vector2 scoreOffset = new Vector2(130f, -72f);
    [Min(1f), Tooltip("Gun kills from at least this far add a long-range bonus (원거리 사격).")]
    public float longRangeDistance = 25f;
    [Min(0)] public int longRangeBonus = 25;
    [Min(0)] public int killScore = 100;
    [Min(0)] public int headshotBonus = 50;
    [Min(0)] public int multiKillBonus = 50;
    [Min(0)] public int executionBonus = 75;
    [Min(0)] public int counterBonus = 75;
    [Min(.1f)] public float multiKillWindow = 1.6f;
    [Min(.5f)] public float scoreStreakSeconds = 2.4f;

    [Header("Enemy hit flash")]
    public Color hitTintColor = new Color(1f, .08f, .04f, .55f);
    public Color hitGlowColor = new Color(1f, .45f, .18f);
    public Color hitRimColor = new Color(1f, .22f, .1f);
    [Min(.02f)] public float hitTintSeconds = .22f;
    [Min(.02f)] public float hitGlowSeconds = .3f;
    [Min(.05f)] public float hitGlowRadius = .32f;

    [Header("Blood")]
    public Color bloodColor = new Color(.52f, .02f, .02f, 1f);
    [Min(0)] public int bodyDroplets = 14, headDroplets = 26, killDroplets = 36;
    [Min(1f)] public float bloodDecalSeconds = 9f;

    [Header("Enemy hit reaction (gameplay)")]
    [Min(0f)] public float flinchPushBody = .12f;
    [Min(0f)] public float flinchPushHead = .2f;
    [Min(.01f)] public float flinchPushSeconds = .09f;
    [Range(0f, 1f), Tooltip("Enemy move speed right after a bullet hit; recovers over Flinch Slow Seconds.")]
    public float flinchSlow = .3f;
    [Min(.05f)] public float flinchSlowSeconds = .4f;
    [Min(0f), Tooltip("A headshot cancels the attack wind-up and stuns this long.")]
    public float headshotInterruptStun = .35f;
    [Range(.05f, 1f), Tooltip("Body damage within the window, as a fraction of maximum health, that interrupts.")]
    public float bodyInterruptFraction = .3f;
    [Min(.1f)] public float bodyInterruptWindow = .7f;
    [Min(0f)] public float bodyInterruptStun = .3f;
    [Min(0f), Tooltip("No new interrupt for this long after one, so sustained fire cannot stun-lock.")]
    public float interruptCooldown = 1f;
    [Min(1f)] public float headshotPostureMultiplier = 1.5f;

    [Header("Death topple")]
    [Min(.1f)] public float toppleSeconds = .5f;
    [Min(0f)] public float deathSlide = .45f;
    [Min(0f)] public float corpseLingerSeconds = .9f;
    [Min(.1f)] public float corpseSinkSeconds = .55f;

    private static GunFeelSettings cached, defaults;
    public static GunFeelSettings Instance
    {
        get
        {
            if (cached != null) return cached;
            cached = Resources.Load<GunFeelSettings>("Combat/GunFeel");
            if (cached != null) return cached;
            if (defaults == null) { defaults = CreateInstance<GunFeelSettings>(); defaults.hideFlags = HideFlags.HideAndDontSave; }
            return defaults;
        }
    }
}
