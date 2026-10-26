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
    public AudioClip[] killSounds, headshotKillSounds;
    [Range(0f, 1f)] public float killVolume = .6f;

    [Header("Hit markers (1920x1080 canvas units)")]
    public Color bodyHitColor = Color.white;
    public Color headHitColor = new Color(1f, .82f, .2f);
    public Color killColor = new Color(1f, .16f, .12f);
    public Color blockedColor = new Color(.62f, .66f, .72f);
    [Min(1f)] public float markerLength = 16.5f;
    [Min(.5f)] public float markerThickness = 2.9f;
    [Min(0f), Tooltip("Distance from the aim point to the inner end of each tick while the marker is at full size.")]
    public float markerGap = 24f;
    [Min(0f), Tooltip("Inner-end distance once the marker has shrunk (Back 4 Blood style: the ticks slide in, their length stays).")]
    public float markerShrunkGap = 15.5f;
    [Tooltip("Frames are counted at this rate, so the marker times the same at any real framerate.")]
    [Min(1f)] public float markerFrameRate = 60f;
    [Min(0)] public int markerHoldFrames = 7;
    [Min(0)] public int markerShrinkFrames = 9;
    [Min(0), Tooltip("Frames shown faded at the shrunk size right before the marker disappears.")]
    public int markerFadeFrames = 1;
    [Range(0f, 1f)] public float markerFadeAlpha = .5f;

    [Header("Kill screen: which kills")]
    [Tooltip("Executions and perfect-defense counters get the special kill screen (plus hit-stop and a camera thump).")]
    public bool killPulseOnExecution = true;
    public bool killPulseOnCounter = true;
    [Tooltip("Every other player kill gets the GTA V-style kill screen. Turn gun kills off if mowing down a crowd gets too bright.")]
    public bool killPulseOnGunKill = true, killPulseOnOtherKill = true;

    [Header("Kill screen: GTA V style (ordinary kills)")]
    [Min(.03f), Tooltip("Seconds the kill screen stays at full strength (including the 0.03 s snap-in).")]
    public float gtaKillHoldSeconds = .5f;
    [Min(.01f), Tooltip("Seconds it then takes to fade away.")]
    public float gtaKillFadeSeconds = .25f;
    [Range(0f, 3f), Tooltip("Overexposure, in stops. The post stack runs without tonemapping, so white walls clip fast: keep this low.")]
    public float gtaKillExposure = .12f;
    [Range(0f, 10f), Tooltip("Glow around bright areas (shirts, windows, floor reflections).")]
    public float gtaKillBloom = .8f;
    [Range(0f, 2f), Tooltip("Brightness above which things glow. Lower = more of the screen blooms.")]
    public float gtaKillBloomThreshold = 1.1f;
    public Color gtaKillBloomTint = new Color(.72f, .84f, 1f);
    [Tooltip("Colour filter over the whole picture: the cool blue-white wash.")]
    public Color gtaKillTint = new Color(.98f, .99f, 1f);
    [Range(-100f, 0f), Tooltip("White balance. Negative = cooler / bluer.")]
    public float gtaKillTemperature = -6f;
    [Range(-100f, 0f)] public float gtaKillSaturation = -3f;
    [Range(-100f, 100f), Tooltip("Slightly negative washes the picture out like the reference.")]
    public float gtaKillContrast = -3f;
    [Range(0f, 1f), Tooltip("Colour fringing toward the screen edges.")]
    public float gtaKillChromatic = .1f;
    [Range(1f, 2f), Tooltip("Headshot kills multiply the bloom by this much.")]
    public float gtaHeadshotBoost = 1.2f;

    [Header("Kill screen: special (execution / counter)")]
    [Range(-100f, 0f)] public float killSaturation = -60f;
    [Range(0f, 2f), Tooltip("Brightness pop. Bright scenes white out quickly; 0.25 reads clearly without blinding.")]
    public float killExposure = .25f;
    [Range(0f, 1f)] public float killVignette = .34f;
    [Tooltip("Vignette colour per kind: executions bleed crimson, counters go cold steel.")]
    public Color executionVignetteColor = new Color(.32f, .02f, .02f), counterVignetteColor = new Color(.03f, .1f, .24f);
    [Range(0f, 1f)] public float killChromatic = .45f;
    [Range(-1f, 0f)] public float killLensDistortion = -.14f;
    [Range(0f, 3f), Tooltip("Soft glow on the brightest highlights so the frame flares on impact.")]
    public float killBloom = 1.2f;
    [Range(0f, .3f), Tooltip("Seconds the impact (lens squeeze and colour fringing) takes to snap back. The grade itself lingers longer.")]
    public float killImpactSeconds = .14f;
    [Range(0f, .3f), Tooltip("Seconds the special kill screen holds at full strength before its decay starts.")]
    public float killPulseHoldSeconds = .1f;
    [Range(1f, 2f), Tooltip("Executions and headshot counters multiply the pulse by this much.")]
    public float headshotKillBoost = 1.35f;
    [Min(.05f), Tooltip("Decay time of the special kill screen after its hold.")]
    public float killPulseSeconds = .5f;
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
