using TMPro;
using UnityEngine;

/// <summary>Shared imported presentation assets, loaded for existing scenes without prefab migration.</summary>
public sealed class CombatFeedbackSettings : ScriptableObject
{
    public AudioClip staggerSound;
    [Range(0f, 1f), Tooltip("Volume of the stagger cue. 0 mutes it; 1 is full volume.")]
    public float staggerSoundVolume = .25f;
    /// <summary>
    /// The prompt font: the Prompt text role (Fonts.csv), no longer a field of this asset, so every reader of this name
    /// follows the role table.
    /// </summary>
    public TMP_FontAsset promptFont => FontRoles.Font(FontRoles.Prompt);
    [Header("Crosshair")]
    [Tooltip("Guns without a Crosshair Profile of their own. Its line sprite is the '기본 그림' line shape in the settings.")]
    public CrosshairProfile gunCrosshair;
    [Tooltip("Fists and melee weapons without a Crosshair Profile of their own.")]
    public CrosshairProfile meleeCrosshair;
    [Header("Executable body (the enemy itself while executable)")]
    [Tooltip("Faint tint over the whole body. Alpha is its strength.")]
    public Color executableBodyTint = new Color(1f, .06f, .1f, .3f);
    [ColorUsage(false, true), Tooltip("Outline round the body where it is seen (EnemyOutlines; its width is Tables/Hud/Outlines', one for every outline). Above 1 it catches the bloom.")]
    public Color executableOutlineColor = new Color(1.6f, .07f, .12f, 1f);
    [Min(.01f), Tooltip("Tint and outline coming in and going out (real seconds).")]
    public float executableBodyFade = .12f;
    [Header("Holder (a zombie that has this PC's player in its grip)")]
    [ColorUsage(false, true), Tooltip("Outline round a zombie holding or pinning a player, seen or not (HoldReveal, EnemyOutlines; its width is Tables/Hud/Outlines'). Above 1 it catches the bloom.")]
    public Color holderOutlineColor = new Color(2f, 1.05f, .1f, 1f);
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
    [Tooltip("Charged punches, counters and the guard break's blow.")]
    [InspectorName("Punch Heavy Hit Sounds")]
    public AudioClip[] fistHeavyHitSounds;
    [InspectorName("Punch Wall Sounds")]
    public AudioClip[] fistWallSounds;
    [Tooltip("Taking hold of an enemy: the guard break closing in, the start of an execution.")]
    public AudioClip[] grabSounds;
    [Range(0f, 1f)] public float fistSwingVolume = .3f, fistHitVolume = .3f, fistHeavyHitVolume = .48f, grabVolume = .34f;
    [Range(0f, 1f), InspectorName("Punch Wall Volume")] public float fistWallVolume = .3f;
    [Header("Hit and kill sounds")]
    [Tooltip("Headshot hit and kill layers. Effects Only and Off both mute them; there are no hit effects yet.")]
    public HitFeedbackLevel headshotFeedback = HitFeedbackLevel.SoundAndEffects;
    [Tooltip("Body hit, body kill and shove/melee kill layers.")]
    public HitFeedbackLevel bodyshotFeedback = HitFeedbackLevel.SoundAndEffects;
    [Tooltip("Tackle Sounds also play for player fall damage.")]
    public AudioClip[] impactBulletSounds, impactPierceSounds, fleshBreakSounds, fleshSplatSounds,
        bodyThudSounds, boneCrackSounds, tackleSounds;
    [Tooltip("A kill by shove, fist or execution.")]
    public AudioClip[] closeKillSounds;
    [Tooltip("An enemy hitting the ground, at the enemy.")]
    public AudioClip[] knockdownSounds;
    [Range(0f, 1f)] public float closeKillVolume = .5f, knockdownVolume = .42f;
    [InspectorName("Player Damage Sounds")]
    public AudioClip[] punchSounds;
    [Range(0f, 1f)] public float hitSoundVolume = .3f, playerHitVolume = .3f;
    [Min(1f), Tooltip("Hits not dealt by the player play at the hit point and fade out by this distance, metres.")]
    public float hitSoundDistance = 10f;
    [Range(0f, .15f), Tooltip("Hits vary their pitch by up to this share, so repeats never sound machine-made.")]
    public float hitPitchVariance = .035f;
    [Header("Defense and enemy attacks")]
    [Tooltip("Ordinary blocks: Block clips only.")]
    public AudioClip[] blockSounds;
    [Tooltip("Fist parries. The metal Parry clips wait for the katana.")]
    public AudioClip[] parrySounds;
    [Tooltip("A parry that breaks the attacker's posture.")]
    public AudioClip[] parryBreakSounds;
    [Tooltip("Particle prefab played at the contact point of every parry, any pack's. Empty plays none.")]
    public GameObject parryHitEffect;
    [Tooltip("Played with the hit effect on every parry: the orange stars that fly off it.")]
    public GameObject parrySparkleEffect;
    [Tooltip("Added when a parry breaks posture: the execution icon's look, a red slash over black ink.")]
    public GameObject parryBreakInkEffect, parryBreakSlashEffect;
    public AudioClip enemyAttackStartSound, perfectEvadeSound;
    [Range(0f, 1f)] public float blockVolume = .3f, parryVolume = .42f, parryBreakVolume = .5f, enemyAttackVolume = .3f, perfectEvadeVolume = .3f;
    [Header("Perfect defense counter")]
    [Range(.01f, 1f)] public float perfectTimeScale = .08f;
    [Min(.1f)] public float counterWindowSeconds = 1f;
    [Min(1f)] public float counterDamage = 80f;
    [Min(0f)] public float counterPostureDamage = 60f;
    [Range(0f, 1f)] public float perfectVignetteStrength = .8f;

    /// <summary>The damage-number font: the Damage text role (Fonts.csv), no longer a field of this asset.</summary>
    public TMP_FontAsset damageFont => FontRoles.Font(FontRoles.Damage);

    [Header("Damage popups")]
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
    public Color killIconColor = new Color(1f, .95f, .92f);
    [Min(8f), Tooltip("Settled number size, 1920x1080 canvas units.")]
    public float damageFontSize = 28f;
    [Tooltip("Damage mapped to the smallest / largest pop.")]
    public Vector2 popDamageRange = new Vector2(5f, 120f);
    [Tooltip("Pop on each hit for small / large damage. The number grows away from its target to this and eases back to 1.")]
    public Vector2 popStartScale = new Vector2(1.15f, 1.45f);
    [Min(.01f)] public float popShrinkSeconds = .12f;
    [Min(.1f), Tooltip("Seconds a number stays after its last hit before it leaves (rises and fades).")]
    public float popupLifetime = .8f;
    [Min(.1f), Tooltip("The same for a number that took the finishing blow.")]
    public float deathBlowLifetime = 1.25f;
    [Min(.01f), Tooltip("Seconds a leaving number takes to rise and fade.")]
    public float popupFadeSeconds = .25f;
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
    [Min(.01f), Tooltip("Icons pop in (scale) a beat after the number.")]
    public float punchIconPopSeconds = .12f;

    [Tooltip("How far a leaving number rises, canvas units. Where numbers stand is Hud.csv's Damage row.")]
    public float popupRise = 22f;
    [Min(.1f), Tooltip("Numbers keep full size up to this distance and shrink beyond it.")]
    public float popupFullSizeDistance = 6f;
    [Range(.2f, 1f)] public float popupMinimumDistanceScale = .65f;


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
