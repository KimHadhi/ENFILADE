using UnityEngine;
using UnityEngine.Serialization;

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

    [Header("Muzzle light (MuzzleLight; the L4D2 addon Optimized Muzzle Flash 3605084814: 250 150 30, 12.7 m, 0.08 s)")]
    [Tooltip("The addon's 250 150 30 as Unity's colour picker reads it: the soft orange the user picked (2026-11-06). Read as " +
        "Source's linear colour it would be 253 202 96, a yellower light the user found duller.")]
    public Color muzzleLightColor = new Color(250f / 255f, 150f / 255f, 30f / 255f);
    [Min(0f), Tooltip("Brightness of one's own muzzle light sideways, at the gun's Muzzle Flash Scale 1. Straight down and up it " +
        "is dimmed by Muzzle Light Below.")]
    public float muzzleLightIntensity = 10f;
    [Range(.1f, 1f), Tooltip("Share of the brightness straight down and straight up (sideways is full), eased between by the " +
        "angle. The floor under the gun and the ceiling over it are the nearest surfaces, and with no tonemapping they burned to " +
        "one flat colour and looked like the light's source. 1 is an even point light. Read when the gun is spawned.")]
    public float muzzleLightBelow = .5f;
    [Min(.5f), Tooltip("How far it reaches (m): 500 units.")]
    public float muzzleLightRange = 12.7f;
    [Min(.01f), Tooltip("How long one shot's light lasts (s): the addon's 0.08. With Hold 0.6 it is full for 48 ms and fades " +
        "out by 80 ms, which follows MuzzleFlash's flame (50 ms bright, a tail to 68 ms). CS2's light, measured with the flame, " +
        "is 0.06 with Hold 0.5.")]
    public float muzzleLightSeconds = .08f;
    [Range(0f, 1f), Tooltip("The share of that time at full brightness; the rest falls away squared.")]
    public float muzzleLightHold = .6f;
    [Tooltip("One's own muzzle light casts shadows: walls and bodies stop it. Six shadow faces while it shines; off, it " +
        "lights through walls like the leak below at full strength.")]
    public bool muzzleLightShadows = true;
    [Range(0, 2), Tooltip("Size of those six shadow faces, a tier of the render pipeline asset: 0 low (256), 1 medium (512), " +
        "2 high (1024). Higher draws sharper shadows and fills more of the shadow atlas. Read when the gun is spawned.")]
    public int muzzleLightShadowTier = 1;
    [Range(0f, 1f), Tooltip("With shadows on, a second light this share as bright and without shadows: the glow that slips round " +
        "doors and under walls into the next room.")]
    public float muzzleLightLeak = .15f;
    [Range(0f, 1f), Tooltip("Someone else's gun lights this share as bright as one's own, without shadows.")]
    public float muzzleLightOthers = .35f;
    [Min(0f), Tooltip("How far ahead of the muzzle the light sits (m), so the gun itself doesn't hide it.")]
    public float muzzleLightAhead = .15f;

    [Header("Sounds (empty = synthesized)")]
    [Range(0f, 1f)] public float gunshotVolume = .55f;
    [Range(0f, .2f)] public float gunshotPitchJitter = .045f;
    public AudioClip[] dryFireSounds, lowAmmoSounds, lowAmmoClickSounds, lastRoundSounds, magOutSounds, magInSounds, boltSounds;
    [Range(0f, 1f)] public float mechanicalVolume = .45f;
    [Range(0f, 1f), Tooltip("The low-ammo warning plays on every shot that leaves less than this share of the magazine, louder " +
        "the emptier: TFA Base's LowAmmoSoundThreshold 0.33.")]
    public float lowAmmoFraction = .33f;
    [Min(0), Tooltip("Magazines this size or smaller get no low-ammo sounds (TFA's autodetection: 4); their last round still " +
        "clicks empty.")]
    public int lowAmmoSmallMagazine = 4;
    [Tooltip("Only used while the HitConfirm sound bank (Art/Audio/HitConfirm) is missing; empty = synthesized.")]
    public AudioClip[] killSounds, headshotKillSounds;
    [Range(0f, 1f)] public float killVolume = .6f;
    [Range(0f, .1f), Tooltip("Each kill inside the kill feed's streak window raises the kill confirm's pitch by this much.")]
    public float killStreakPitchStep = .025f;
    [Min(0), Tooltip("Kills after which the kill confirm stops climbing.")]
    public int killStreakSteps = 10;

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
    [Range(0f, 1f), Tooltip("A fresh marker starts this much bigger and settles over Marker Pop Seconds, so each hit of a burst reads on its own.")]
    public float markerPop = .3f;
    [Min(.01f)] public float markerPopSeconds = .07f;
    [Tooltip("Body and head markers sized by the share of the target's health the hit took: a graze at the small size, " +
        "Marker Full Damage or more at the large one. Kills and blocked hits keep their own size.")]
    public float markerSmallScale = .85f;
    public float markerLargeScale = 1.25f;
    [Range(.05f, 1f)] public float markerFullDamage = .5f;
    [Tooltip("Headshot ticks are this much thicker and longer than a body hit's, so the gold reads at a glance.")]
    public float headThickness = 1.5f, headLength = 1.15f;
    // A headshot's burst (star, ring, sparks, split flare) is in HitMarkers.csv, rows Head.* (HitMarkerHUD.HeadLook), for
    // both looks: the glint, bloom and lens flare that were here hid the head they marked (2026-11-12).
    [Min(1f), Tooltip("A headshot's ticks run their frames this much faster than a body hit's, so they snap in and out.")]
    public float headMarkerPace = 1.6f;
    [Range(0f, 1f), Tooltip("How much bigger a headshot's ticks start before popping back (a body hit uses Marker Pop).")]
    public float headPop = .55f;
    [Range(0f, 1f), Tooltip("Of the hits landed in one frame (a shotgun's pellets), at least this share must be headshots for the " +
        "headshot marker and sound, as in Overwatch. A single hit always counts.")]
    public float headshotHitShare = .3f;
    [Range(0f, 2f), Tooltip("A hit whose bullet went through or off something on the way (DamageHit.Pierced): each tick trails a fading " +
        "tail outward, this share of the tick's length by the time the marker goes (it starts at 60% of that).")]
    public float piercedTailLength = .75f;
    [Range(0f, 1f), Tooltip("Opacity of the tail's near half, as a share of the marker's own; the far half shows at 42% of it.")]
    public float piercedTailAlpha = .38f;
    [Tooltip("The posture break marker: a ring and four shards bursting out from the aim point.")]
    public Color breakColor = new Color(1f, .52f, .14f);
    [Min(.05f)] public float breakSeconds = .4f;

    [Header("Kill screen: which kills")]
    [Tooltip("Executions and perfect-defense counters get the special kill screen (plus hit-stop and a camera thump).")]
    public bool killPulseOnExecution = true;
    public bool killPulseOnCounter = true;
    [Tooltip("Every other player kill gets the ordinary kill screen (brightened, glowing, cool frame). Turn gun kills off if mowing down a crowd gets too bright.")]
    public bool killPulseOnGunKill = true, killPulseOnOtherKill = true;

    [Header("Kill screen: ordinary kills (brightened, glowing, cool frame)")]
    [Min(.03f), Tooltip("Seconds the kill screen stays at full strength (including the 0.03 s snap-in).")]
    [FormerlySerializedAs("gtaKillHoldSeconds")] public float ordinaryKillHoldSeconds = .5f;
    [Min(.01f), Tooltip("Seconds it then takes to fade away.")]
    [FormerlySerializedAs("gtaKillFadeSeconds")] public float ordinaryKillFadeSeconds = .25f;
    [Range(0f, 3f), Tooltip("Overexposure, in stops. The post stack runs without tonemapping, so white walls clip fast: keep this low.")]
    [FormerlySerializedAs("gtaKillExposure")] public float ordinaryKillExposure = .12f;
    [Range(0f, 10f), Tooltip("Glow around bright areas (shirts, windows, floor reflections).")]
    [FormerlySerializedAs("gtaKillBloom")] public float ordinaryKillBloom = .8f;
    [Range(0f, 2f), Tooltip("Brightness above which things glow. Lower = more of the screen blooms.")]
    [FormerlySerializedAs("gtaKillBloomThreshold")] public float ordinaryKillBloomThreshold = 1.1f;
    [FormerlySerializedAs("gtaKillBloomTint")] public Color ordinaryKillBloomTint = new Color(.72f, .84f, 1f);
    [Tooltip("Colour filter over the whole picture: the cool blue-white wash.")]
    [FormerlySerializedAs("gtaKillTint")] public Color ordinaryKillTint = new Color(.98f, .99f, 1f);
    [Range(-100f, 0f), Tooltip("White balance. Negative = cooler / bluer.")]
    [FormerlySerializedAs("gtaKillTemperature")] public float ordinaryKillTemperature = -6f;
    [Range(-100f, 0f), FormerlySerializedAs("gtaKillSaturation")] public float ordinaryKillSaturation = -3f;
    [Range(-100f, 100f), Tooltip("Slightly negative washes the picture out.")]
    [FormerlySerializedAs("gtaKillContrast")] public float ordinaryKillContrast = -3f;
    [Range(0f, 1f), Tooltip("Colour fringing toward the screen edges.")]
    [FormerlySerializedAs("gtaKillChromatic")] public float ordinaryKillChromatic = .1f;
    [Range(1f, 2f), Tooltip("Headshot kills multiply the bloom by this much.")]
    [FormerlySerializedAs("gtaHeadshotBoost")] public float ordinaryKillHeadshotBoost = 1.2f;

    [Header("Kill screen: ordinary kills at low health")]
    [Range(0f, 2f), Tooltip("At low health the colour stays drained (LowHealthGrade), because colour coming back reads as healing. " +
        "The kill shows through what the grade leaves alone instead. These are the values at full drain; a partly drained screen " +
        "gets that share. A white flash, in stops, that snaps back over Drained Kill Impact Seconds.")]
    public float drainedKillFlash = .45f;
    [Range(.05f, .4f)] public float drainedKillImpactSeconds = .18f;
    [Range(-1f, 0f), Tooltip("Lens squeeze on the kill, snapping back with the flash.")]
    public float drainedKillLens = -.16f;
    [Range(0f, 1f), Tooltip("Colour fringing, spiking with the flash.")]
    public float drainedKillChromatic = .4f;
    [Range(-100f, 100f), Tooltip("Contrast while it shows: a crisp, struck grey instead of a washed one.")]
    public float drainedKillContrast = 18f;
    [Range(1f, 3f), Tooltip("The kill's glow is multiplied by this much.")]
    public float drainedKillBloomScale = 1.6f;

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
    [Min(0), Tooltip("Gun kills whose bullet went through or off something first (관통 사살).")]
    public int piercedBonus = 25;
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
    public Color bloodColor = new Color(.72f, .03f, .03f, 1f);
    [Min(0)] public int bodyDroplets = 10, headDroplets = 18, killDroplets = 26;
    [Min(1f)] public float bloodDecalSeconds = 9f;
    [Min(1f), Tooltip("How long a bullet hole stays before it fades.")]
    public float bulletHoleSeconds = 20f;

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
