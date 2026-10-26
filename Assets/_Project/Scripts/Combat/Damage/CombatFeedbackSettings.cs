using TMPro;
using UnityEngine;

/// <summary>Shared imported presentation assets, loaded for existing scenes without prefab migration.</summary>
public sealed class CombatFeedbackSettings : ScriptableObject
{
    public Texture2D staggerImage;
    public AudioClip staggerSound;
    [Range(0f, 1f), Tooltip("Volume of the stagger cue. 0 mutes it; 1 is full volume.")]
    public float staggerSoundVolume = .25f;
    [Min(40f), Tooltip("Stagger image width at 3 metres, in reference screen pixels before the opening pop. Closer grows up to 2x; farther shrinks with distance.")]
    public float staggerImageWidth = 260f;
    public TMP_FontAsset promptFont;
    [Header("Executable heartbeat")]
    [Range(0f, .4f), Tooltip("Stagger icon swell on each heartbeat (lub, then a smaller dub). 0 turns the pulse off.")]
    public float heartbeatScale = .28f;
    [Tooltip("Heartbeats per minute when the enemy becomes executable / just before posture fully recovers. Speeds up as the window closes.")]
    public Vector2 heartbeatBpm = new Vector2(72f, 150f);
    [Tooltip("Bloom and elliptical waves around the enemy HP bar while executable. Off-white so it never reads as red health. Alpha is the brightest heartbeat.")]
    public Color executableGlowColor = new Color(.93f, .91f, .9f, .85f);
    [Tooltip("Round glow behind the execution mark, pulsing with the heartbeat.")]
    public Color executableMarkGlowColor = new Color(1f, .12f, .2f, .85f);
    [Header("Executable palette (whole enemy bar re-dresses while executable)")]
    [Tooltip("Remaining health fill. Bright crimson pink: clearly not the normal red, without going dull.")]
    public Color executableHealthColor = new Color(.98f, .24f, .42f);
    [Tooltip("Already lost part of the health track.")]
    public Color executableHealthLostColor = new Color(.45f, .05f, .16f, .4f);
    [Tooltip("Dark soft plate behind the bar, so the re-dressed bar stands out on any background.")]
    public Color executablePlateColor = new Color(.06f, 0f, .03f, .8f);
    [Tooltip("< > brackets and the posture bar (counts down the window as it refills).")]
    public Color executableAccentColor = new Color(.95f, .93f, .92f);
    [Tooltip("Enemy name above the bar.")]
    public Color executableNameColor = new Color(1f, .5f, .6f);
    [Range(0f, .5f), Tooltip("Enemy HP bar scale kick when the enemy becomes executable.")]
    public float executableBarPunch = .22f;
    [Min(.05f), Tooltip("White flash + light sweep across the HP bar at the moment of becoming executable.")]
    public float executableFlashSeconds = .4f;
    [Min(0f), Tooltip("How far the elliptical waves radiate out from the HP bar on each heartbeat, bar units (bar is 120 wide).")]
    public float executableRippleReach = 36f;
    [Header("Shove Impacts")]
    public AudioClip[] shoveEnemySounds;
    public AudioClip shoveWorldSound;
    [Range(0f, 1f)] public float shoveEnemyVolume = .3f;
    [Range(0f, 1f)] public float shoveWorldVolume = .3f;
    [Header("Player Attack - Punch")]
    [Tooltip("One random clip on every punch, hit or miss.")]
    [InspectorName("Punch Swing Sounds")]
    public AudioClip[] fistSwingSounds;
    [Tooltip("One random clip when a punch damages at least one target. Blocked or parried punches stay silent here.")]
    [InspectorName("Punch Hit Sounds")]
    public AudioClip[] fistHitSounds;
    [InspectorName("Punch Wall Sounds")]
    public AudioClip[] fistWallSounds;
    [Range(0f, 1f)] public float fistSwingVolume = .3f, fistHitVolume = .3f;
    [Range(0f, 1f), InspectorName("Punch Wall Volume")] public float fistWallVolume = .3f;
    [Header("Hit and kill sounds")]
    [Tooltip("Headshot hit and kill layers. Effects Only and Off both mute them; there are no hit effects yet.")]
    public HitFeedbackLevel headshotFeedback = HitFeedbackLevel.SoundAndEffects;
    [Tooltip("Body hit, body kill and shove/melee kill layers.")]
    public HitFeedbackLevel bodyshotFeedback = HitFeedbackLevel.SoundAndEffects;
    [Tooltip("Player fall damage reuses Shove Enemy Sounds (same clips) with Tackle Sounds.")]
    public AudioClip[] impactBulletSounds, impactPierceSounds, fleshBreakSounds, fleshSplatSounds,
        bodyThudSounds, boneCrackSounds, tackleSounds;
    [InspectorName("Player Damage Sounds")]
    public AudioClip[] punchSounds;
    [Range(0f, 1f)] public float hitSoundVolume = .3f, playerHitVolume = .3f;
    [Min(1f), Tooltip("Hits not dealt by the player play at the hit point and fade out by this distance, metres.")]
    public float hitSoundDistance = 10f;
    [Header("Defense and enemy attacks")]
    [Tooltip("Ordinary blocks: Block clips only.")]
    public AudioClip[] blockSounds;
    [Tooltip("Ordinary parries, including combo finishers: Parry clips only. Hard/Hardest Parry are reserved for future explicitly stronger attacks.")]
    public AudioClip[] parrySounds;
    public AudioClip enemyAttackStartSound, perfectEvadeSound;
    [Range(0f, 1f)] public float blockVolume = .3f, parryVolume = .3f, enemyAttackVolume = .3f, perfectEvadeVolume = .3f;
    [Header("Perfect defense counter")]
    [Range(.01f, 1f)] public float perfectTimeScale = .08f;
    [Min(.1f)] public float counterWindowSeconds = 1f;
    [Min(1f)] public float counterDamage = 80f;
    [Min(0f)] public float counterPostureDamage = 60f;
    [Min(.1f)] public float counterReach = 4f;
    [Range(0f, 1f)] public float perfectVignetteStrength = .8f;

    [Header("Damage popups")]
    [Tooltip("Optional. Empty uses Prompt Font.")]
    public TMP_FontAsset damageFont;
    [Tooltip("Optional white icon shown left of a critical number. Empty uses the built-in burst.")]
    public Texture2D criticalIcon;
    [Tooltip("Optional white icon shown right of a death-blow number. Empty uses the built-in skull.")]
    public Texture2D killIcon;
    [Tooltip("Optional white icon shown left of a counter number. Empty uses the built-in return arrow.")]
    public Texture2D counterIcon;
    [Tooltip("Normal hit by the player.")]
    public Color normalDamageColor = Color.white;
    [Tooltip("Critical number and icon (gold).")]
    public Color criticalColor = new Color(1f, .84f, .28f);
    [Tooltip("Damage not dealt by the player: enemy friendly fire, falls, hazards. Reads as environmental.")]
    public Color environmentDamageColor = new Color(.62f, .62f, .64f);
    [Tooltip("Perfect-defense counter number.")]
    public Color counterColor = new Color(.43f, .84f, 1f);
    [Tooltip("Player's finishing-blow number fill (orange). Environment kills stay grey.")]
    public Color deathBlowColor = new Color(1f, .55f, .12f);
    [Tooltip("Finishing-blow drop shadow (deep crimson), offset down-right behind the orange number.")]
    public Color deathBlowShadowColor = new Color(.75f, .1f, .06f);
    [Tooltip("White sword cut drawn top-right to bottom-left behind a counter number.")]
    public Color counterSlashColor = new Color(1f, 1f, 1f, .9f);
    [Range(0f, 1f), Tooltip("How much of the number's colour the slash takes on (0 = pure white). Sky blue for counters, orange for counter kills.")]
    public float counterSlashTint = .6f;
    [Range(.1f, .8f), Tooltip("Slash thickness relative to the font size.")]
    public float counterSlashThickness = .32f;
    [Range(10f, 60f), Tooltip("Counter slash angle from horizontal, degrees.")]
    public float counterSlashAngle = 32f;
    [Tooltip("Thin line that sweeps out sideways behind a finishing-blow number.")]
    public Color deathBlowSlashColor = new Color(1f, .25f, .18f);
    [Tooltip("Blurred light behind a death-blow number. Kept small so the slash line reads first.")]
    public Color deathBlowGlowColor = new Color(1f, .08f, .05f, .9f);
    public Color killIconColor = new Color(1f, .95f, .92f);
    [Min(8f), Tooltip("Settled number size, 1920x1080 canvas units.")]
    public float damageFontSize = 34f;
    [Tooltip("Damage mapped to the smallest / largest opening pop.")]
    public Vector2 popDamageRange = new Vector2(5f, 120f);
    [Tooltip("Opening scale for small / large damage. The number shrinks from this to 1.")]
    public Vector2 popStartScale = new Vector2(1.6f, 3f);
    [Min(.01f)] public float popShrinkSeconds = .12f;
    [Min(.1f)] public float popupLifetime = .8f;
    [Min(.1f)] public float deathBlowLifetime = 1.25f;
    [Min(.01f)] public float popupFadeSeconds = .25f;
    [Header("Punch (critical, counter and finishing blow)")]
    [Min(0f), Tooltip("The number freezes at its largest size for this long before shrinking (a tiny hit-stop).")]
    public float punchHoldSeconds = .035f;
    [Min(.01f)] public float punchShrinkSeconds = .09f;
    [Range(0f, .3f), Tooltip("Small dip below the settled size after the shrink, then back.")]
    public float punchSettleDip = .07f;
    [Min(0f), Tooltip("Digits start white-hot and settle into their colour over this time.")]
    public float punchFlashSeconds = .07f;
    [Min(1f), Tooltip("Opening scale multiplier on top of the damage-based pop.")]
    public float punchStartBoost = 1.15f;
    [Min(1f), Tooltip("Settled size of critical and counter numbers relative to normal hits.")]
    public float punchSettledScale = 1.12f;
    [Min(1f), Tooltip("Settled size of finishing-blow numbers relative to normal hits. Clearly larger than criticals.")]
    public float deathBlowSettledScale = 1.5f;
    [Min(1f), Tooltip("Finishing-blow opening scale multiplier, used instead of Punch Start Boost.")]
    public float deathBlowStartBoost = 1.25f;
    [Min(0f), Tooltip("Finishing-blow freeze at the peak, used instead of Punch Hold Seconds.")]
    public float deathBlowHoldSeconds = .08f;
    [Min(0f), Tooltip("Finishing-blow number rattle right after impact, canvas units. Dies out over Death Blow Shake Seconds.")]
    public float deathBlowShake = 7f;
    [Min(.01f)] public float deathBlowShakeSeconds = .25f;
    [Min(.01f), Tooltip("Icons pop in (scale + small twist) a beat after the number.")]
    public float punchIconPopSeconds = .12f;

    [Tooltip("Random offset around the hit point: x = ± horizontal (biased outward, away from the centre), y = vertical reach (from -half to +full), canvas units. Finishing blows use half.")]
    public Vector2 popupJitter = new Vector2(90f, 48f);
    [Tooltip("Upward drift over the lifetime, canvas units. The popup still follows its target.")]
    public float popupRise = 22f;
    [Min(.1f), Tooltip("Numbers keep full size up to this distance and shrink beyond it.")]
    public float popupFullSizeDistance = 6f;
    [Range(.2f, 1f)] public float popupMinimumDistanceScale = .65f;

    [Header("Enemy health bar")]
    public Color enemyHealthColor = new Color(.93f, .13f, .11f);
    [Tooltip("Already lost health: almost invisible grey.")]
    public Color enemyHealthLostColor = new Color(.62f, .62f, .62f, .12f);

    [Header("Health loss (enemy and player bars)")]
    [Tooltip("Off: every trail is Small Loss Color (plain yellow). On: yellow -> orange by hit size.")]
    public bool colorTrailByLossSize = true;
    [Tooltip("Trail colour for a tiny hit.")]
    public Color smallLossColor = new Color(1f, .9f, .2f);
    [Tooltip("Trail colour when one hit removes Large Loss Fraction of maximum health (orange).")]
    public Color largeLossColor = new Color(1f, .57f, .12f);
    [Tooltip("Trail colour for a near-total hit (deep orange, deliberately not red so it never reads as the bar).")]
    public Color fullLossColor = new Color(1f, .4f, .1f);
    [Range(.05f, .95f), Tooltip("Single-hit fraction of maximum health that reads orange.")]
    public float largeLossFraction = .7f;
    [Tooltip("Lighter diagonal stripes over the trail, so it never reads as remaining health.")]
    public bool trailStripes = true;
    [Range(0f, 1f), Tooltip("How much lighter the alternate stripes are (0 = invisible, 1 = white).")]
    public float trailStripeLighten = .35f;
    [Min(.01f), Tooltip("Stripes slide toward the remaining health, units per second.")]
    public float trailStripeScroll = 14f;
    [Tooltip("Thin white line where remaining health ends while a trail is showing.")]
    public bool trailEdgeLine = true;
    [Range(.5f, 8f), Tooltip("Edge line thickness at rest, bar units (bar is 120 wide, 8 tall). Sits mostly on the lost side so it reads as the boundary.")]
    public float trailEdgeWidth = 3f;
    [Header("Health edge hit pulse (enemy bar)")]
    [Range(0f, 4f), Tooltip("On a hit the edge line snaps this much taller (1.6 = 2.6x), springs back with a small squash and settles. 0 turns the pulse off.")]
    public float trailEdgePulseScale = 1.6f;
    [Range(0f, 20f), Tooltip("On a hit the edge line also stretches this far to the right (into the lost part), bar units, then snaps back faster than the height.")]
    public float trailEdgeHitStretch = 6f;
    [Min(.05f), Tooltip("Time for the spring back to settle after the snap, seconds. Bigger = slower, floatier.")]
    public float trailEdgeSettleSeconds = .5f;
    [Tooltip("Edge line alpha: x = at rest, y = at the moment of the hit.")]
    public Vector2 trailEdgeAlpha = new Vector2(.75f, 1f);
    [Tooltip("Soft glow that blooms around the edge line on a hit and fades with the spring. Alpha is the peak; 0 turns it off.")]
    public Color trailEdgeGlowColor = new Color(1f, .97f, .94f, .8f);
    [Min(0f), Tooltip("The trail starts white and settles into its loss colour over this time.")]
    public float trailFlashSeconds = .12f;
    [Min(0f), Tooltip("Seconds the lost part stays full before draining.")]
    public float trailHoldSeconds = .15f;
    [Min(.01f), Tooltip("Base drain speed, fraction of maximum health per second.")]
    public float trailDrainPerSecond = .6f;
    [Min(0f), Tooltip("Extra drain speed per remaining fraction, so large chunks catch up. 70% drains in about 0.45 s.")]
    public float trailDrainCatchUp = 4f;
    [Min(0f), Tooltip("Enemy bar shake for a hit removing all health, bar units (bar is 120 wide). Scales with the share of health lost.")]
    public float enemyShakeMax = 14f;
    [Min(.05f), Tooltip("How long the enemy bar rattles after a hit.")]
    public float enemyShakeSeconds = .32f;
    [Min(1f), Tooltip("Enemy bar rattle speed, back-and-forth swings per second.")]
    public float enemyShakeFrequency = 18f;
    [Min(0f), Tooltip("How quickly the enemy rattle dies out. Higher = shorter, snappier.")]
    public float enemyShakeDamping = 12f;
    [Range(0f, .3f), Tooltip("Enemy bar briefly grows by this much on a heavy hit (half of maximum health or more).")]
    public float enemyShakeScalePunch = .07f;
    [Min(0f), Tooltip("Player HP bar shake for a hit removing all health, 1920x1080 canvas units.")]
    public float playerShakeMax = 16f;
    [Min(.05f)] public float shakeSeconds = .35f;

    /// <summary>Yellow → orange (at Large Loss Fraction) → red, by single-hit fraction of maximum health.</summary>
    public Color HealthLossColor(float fraction)
    {
        if (!colorTrailByLossSize) return smallLossColor;
        float large = Mathf.Clamp(largeLossFraction, .05f, .95f);
        fraction = Mathf.Clamp01(fraction);
        return fraction <= large
            ? Color.Lerp(smallLossColor, largeLossColor, fraction / large)
            : Color.Lerp(largeLossColor, fullLossColor, (fraction - large) / (1f - large));
    }

    private static CombatFeedbackSettings cached, defaults;
    public static CombatFeedbackSettings Instance => cached != null ? cached : cached = Resources.Load<CombatFeedbackSettings>("Combat/Feedback");
    /// <summary>Instance, or field defaults when the Resources asset is missing.</summary>
    public static CombatFeedbackSettings InstanceOrDefaults
    {
        get
        {
            var loaded = Instance;
            if (loaded != null) return loaded;
            if (defaults == null) { defaults = CreateInstance<CombatFeedbackSettings>(); defaults.hideFlags = HideFlags.HideAndDontSave; }
            return defaults;
        }
    }
}
