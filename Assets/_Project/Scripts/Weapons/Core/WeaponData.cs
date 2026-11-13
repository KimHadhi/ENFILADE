using UnityEngine;

/// <summary>
/// How the trigger fires. Fanning is a single-action revolver's (B on the R08): from the hip, held down, the palm slaps the
/// hammer and the cylinder empties fast and wide; down the sights it fires one aimed round a pull, as SemiAutomatic.
/// </summary>
public enum WeaponFireMode { SemiAutomatic, Automatic, Burst, Fanning }
public enum WeaponCategory { Ranged, Melee, Special }

/// <summary>
/// One weapon's numbers and presentation. Columns of Data/Resources/Tables/Weapons/Weapons.csv (row = this asset's name) overwrite
/// the matching fields at runtime; the asset keeps references (icon, sounds) and anything the table leaves out.
/// </summary>
[CreateAssetMenu(fileName = "Weapon", menuName = "ENFILADE/Combat/Weapon Data")]
public sealed class WeaponData : ScriptableObject
{
    public const string TableName = "Weapons";
    public void BindTable() => GameTables.Bind(TableName, name, this);

    [SerializeField] private string displayName = "Weapon";
    [SerializeField] private Sprite hudIcon;
    [Tooltip("Crosshair while this weapon is in hand. Empty: melee weapons use Feedback's Melee Crosshair, the rest the scene's own.")]
    [SerializeField] private CrosshairProfile crosshairProfile;
    [SerializeField] private WeaponCategory category = WeaponCategory.Ranged;
    [SerializeField] private WeaponFireMode fireMode = WeaponFireMode.Automatic;
    [SerializeField, Min(0.01f)] private float damage = 20f;
    [SerializeField, Min(1f)] private float roundsPerMinute = 600f;
    [SerializeField, Min(1)] private int magazineSize = 30;
    [SerializeField, Min(0.01f)] private float reloadDuration = 1.5f;
    [SerializeField, Min(0.1f)] private float range = 100f;
    [Tooltip("How far a shot is heard by zombies (m, Noise). A wall between halves it for them.")]
    [SerializeField, Min(0f)] private float noiseRadius = 40f;
    [Tooltip("With a suppressor on (GunAttachments), the share of Noise Radius a shot is heard at by zombies.")]
    [SerializeField, Range(0f, 1f)] private float suppressedNoise = .3f;
    [Tooltip("With a vertical grip on (GunAttachments), the share of the climb each shot keeps (Rainbow Six Siege's vertical grip).")]
    [SerializeField, Range(0f, 1f)] private float gripRecoil = .8f;
    [Tooltip("With an angled grip on, the share of Ads Transition Time the sight takes to come up (Siege's angled grip).")]
    [SerializeField, Range(.1f, 1f)] private float gripAimTime = .8f;
    [Tooltip("Pitch / yaw in degrees. Positive pitch kicks the view upward.")]
    [SerializeField] private Vector2 recoilPerShot = new Vector2(1.2f, 0.4f);
    [Header("Ammunition / fire mode")]
    [SerializeField, Min(0)] private int initialReserveAmmo = 90;
    [SerializeField, Min(.01f)] private float emptyReloadDuration = 2f;
    [SerializeField, Min(1)] private int burstCount = 3;
    [SerializeField, Min(0f)] private float burstPause = .22f;
    [Header("Accuracy (cone half-angle in degrees)")]
    [SerializeField, Min(0f)] private float hipSpread = 1.2f;
    [SerializeField, Min(0f)] private float adsSpread = .12f;
    [SerializeField, Min(0f)] private float movingSpread = .8f;
    [SerializeField, Min(0f)] private float airborneSpread = 1.8f;
    [SerializeField, Min(0f)] private float bloomPerShot = .18f;
    [SerializeField, Min(0f)] private float maxBloom = 2.5f;
    [SerializeField, Min(0f)] private float bloomRecovery = 2.2f;
    [SerializeField, Min(1f)] private float headshotMultiplier = 2f;
    [Tooltip("How far a bullet pushes through what it hits, in ArcCW's millimetres (BulletPenetration): Horde's M4A1 is 10, " +
        "its G17 4. 0 stops at the first hit.")]
    [SerializeField, Min(0f)] private float penetration;
    [Tooltip("Bodies its round goes on through whole by its own rule (BulletPierce), before the bodies' usual cost: the " +
        "Desperado's piercing round. Walls still take Penetration. 0: none, as every other gun.")]
    [SerializeField, Min(0)] private int pierceBodies;
    [Tooltip("The share of the damage its round keeps for each body it went through by Pierce Bodies.")]
    [SerializeField, Range(0f, 1f)] private float pierceKeep = 1f;
    [Tooltip("Crouched on the ground, the whole cone (hip or aimed, with moving, airborne and bloom) is multiplied by this, " +
        "as ArcCW's arccw_mult_crouchdisp does. 0.5 is TFA's CrouchAccuracyMultiplier; 1 turns it off.")]
    [SerializeField, Range(.05f, 1f)] private float crouchSpread = .5f;
    [Tooltip("Crouched on the ground, aim recoil is multiplied by this, as ArcCW's arccw_mult_crouchrecoil does. 0.65 is " +
        "TFA's CrouchRecoilMultiplier; 1 turns it off.")]
    [SerializeField, Range(.05f, 1f)] private float crouchRecoil = .65f;
    [Tooltip("An empty magazine starts its reload by itself this long after the gun could fire again (s), as ArcCW's " +
        "arccw_automaticreload does (0.2). Below 0 turns it off.")]
    [SerializeField] private float autoReloadDelay = .2f;
    [Header("Shotgun")]
    [Tooltip("Bullets one shot fires, each with the full Damage, spread round where the shot goes within Pellet Spread. " +
        "1 is a single bullet.")]
    [SerializeField, Min(1)] private int pellets = 1;
    [Tooltip("Half-angle (degrees) of the circle the pellets fill round the crosshair's middle, on top of the gun's own spread. " +
        "They fill it evenly every shot, and the crosshair's parts stand on it (WeaponController).")]
    [SerializeField, Min(0f)] private float pelletSpread;
    [Tooltip("Half-angle (degrees) of the pellets' circle the moment the sights are fully up, before any choke draws it in. " +
        "0: Pellet Spread.")]
    [SerializeField, Min(0f)] private float adsPelletSpread;
    [Tooltip("Apex Legends' Peacekeeper precision choke: held fully down the sights, the pellets' circle draws in to this " +
        "half-angle (degrees) over Choke Seconds, and a shot lets it out again (user, 2026-11-11). 0 with no Choke Seconds.")]
    [SerializeField, Min(0f)] private float chokePelletSpread;
    [Tooltip("Seconds fully down the sights for the choke to draw the pellets all the way in. 0: no choke.")]
    [SerializeField, Min(0f)] private float chokeSeconds;
    [Header("Fanning (a revolver's Fanning fire mode)")]
    [Tooltip("Rounds a minute while fanning from the hip (the trigger held, the palm on the hammer).")]
    [SerializeField, Min(1f)] private float fanRoundsPerMinute = 500f;
    [Tooltip("Degrees fanning adds to the cone: the gun bucks off line with every slap.")]
    [SerializeField, Min(0f)] private float fanSpread = 3.5f;
    [Tooltip("Loaded a shell at a time, as a pump shotgun: Reload Duration is the start (Empty Reload Duration the start " +
        "from empty, which also chambers a shell), then Shell Insert Duration for each shell, then Shell End Duration. " +
        "Firing stops it once the shell in hand is in.")]
    [SerializeField] private bool shellReload;
    [SerializeField, Min(.01f)] private float shellInsertDuration = .6f;
    [SerializeField, Min(0f)] private float shellEndDuration = .6f;
    [Tooltip("A shell reload's start from empty (Empty Reload Duration) puts a round in the chamber, so one shell less follows: " +
        "a pump shotgun's. Off where that start only opens the action, so every round goes in with a shell of its own: FPS " +
        "Animation Ultimate's Kar98k opens its bolt.")]
    [SerializeField] private bool emptyStartLoads = true;
    [Tooltip("A shell reload's sounds, each as its step starts: the start, the start from empty, every shell, the end. On a " +
        "magazine reload the two start ones, when set, are the gun's own recording of its whole reload (FPS Animation Ultimate's " +
        "L96X), played as a fresh reload starts in place of the magazine's and bolt's moments.")]
    [SerializeField] private AudioClip[] reloadStartSounds = new AudioClip[0], reloadStartEmptySounds = new AudioClip[0],
        shellInsertSounds = new AudioClip[0], reloadEndSounds = new AudioClip[0];
    [Tooltip("A magazine reload's own sounds at the moments its clip moves the parts (ReloadMoments): the magazine out and in, "
        + "the bolt or slide back and forward (a revolver: its spent rounds out, the new ones in, the crane open and shut). "
        + "Empty: the gun profile's (GunFeel, SoundCues). The Desperado wears the Fatebringer's.")]
    [SerializeField] private AudioClip[] magOutSounds = new AudioClip[0], magInSounds = new AudioClip[0],
        boltBackSounds = new AudioClip[0], boltForwardSounds = new AudioClip[0];
    [Tooltip("As the gun comes into the hands. Empty: the gun profile's (SoundCues GunDraw).")]
    [SerializeField] private AudioClip[] drawSounds = new AudioClip[0];
    [Tooltip("A gun worked by hand after every shot (FPSBodyAssets.Gun.sourceFire): one recording of the whole action, played as " +
        "it starts. Empty: the sounds KINEMATION's gun-part clip of the shot calls for, at their moments (the Kar98k's, the " +
        "L96X's, the KXG12's).")]
    [SerializeField] private AudioClip[] cycleSounds = new AudioClip[0];
    [Tooltip("Repeats while firing; an empty array uses Recoil Per Shot. Only when Recoil Vertical is 0.")]
    [SerializeField] private Vector2[] recoilPattern = new Vector2[0];
    [Tooltip("The fire modes B switches between, as 'Automatic;Burst;SemiAutomatic'. The gun starts on Fire Mode. Empty: Fire " +
        "Mode only.")]
    [SerializeField] private string fireModes = "";
    [Header("Draw and holster")]
    [Tooltip("Seconds from taking the gun out to firing it: its draw clip plays fitted to this, and it can't fire, aim or reload " +
        "until it is up. 0: ready at once (and no draw clip).")]
    [SerializeField, Min(0f)] private float drawDuration;
    [Tooltip("Seconds the gun takes to go away before the next one comes out (its holster clip fitted to it). 0: swapped at once.")]
    [SerializeField, Min(0f)] private float holsterDuration;
    [Header("Range falloff (Rainbow Six Siege)")]
    [Tooltip("Full body damage up to this far (m). A headshot kills at any range; only the body's damage falls off.")]
    [SerializeField, Min(0f)] private float falloffStart = 1000f;
    [Tooltip("From Falloff Start the body damage goes down evenly to Falloff Minimum of itself here (m), and stays there.")]
    [SerializeField, Min(0f)] private float falloffEnd = 1000f;
    [SerializeField, Range(.05f, 1f)] private float falloffMinimum = 1f;
    [Header("Recoil pattern (Rainbow Six Siege)")]
    [Tooltip("Degrees every shot lifts the view, kept there (Recoil Retention 1): the player pulls it back down, as in Rainbow Six " +
        "Siege. 0 uses Recoil Pattern or Recoil Per Shot instead.")]
    [SerializeField, Min(0f)] private float recoilVertical;
    [Tooltip("Most degrees a shot throws the view sideways once the spray has settled (Recoil Settle Shots). The throw follows a " +
        "wave of the gun's own, the same every spray, so it can be learnt, with a little chance on top.")]
    [SerializeField, Min(0f)] private float recoilHorizontal;
    [Tooltip("Which way the sideways throw leans: -1 left, 1 right, 0 both ways evenly.")]
    [SerializeField, Range(-1f, 1f)] private float recoilDrift;
    [Tooltip("The first shot's lift, times Recoil Vertical: Siege's guns jump on their first round.")]
    [SerializeField, Min(0f)] private float recoilFirstShot = 1f;
    [Tooltip("Shots until the sideways throw is full: the spray first climbs nearly straight (a quarter of the throw).")]
    [SerializeField, Min(1)] private int recoilSettleShots = 1;
    [Header("Recoil feel")]
    [Tooltip("Spring response in 1/seconds. 24 peaks about 42ms after firing; higher is snappier.")]
    [SerializeField, Range(8f, 50f)] private float recoilResponse = 24f;
    [Tooltip("Fraction retained as gradual aim climb. The rest springs back without smoothing mouse input.")]
    [SerializeField, Range(0f, 1f)] private float recoilRetention = .22f;
    [Header("Aim down sights")]
    [SerializeField, Range(20f, 100f)] private float adsFov = 40f;
    [SerializeField, Range(.1f, 1f)] private float adsMoveMultiplier = .6f;
    [SerializeField, Min(.01f)] private float adsTransitionTime = .16f;
    [Header("High-ready pose (level view space; Weapons.csv)")]
    [Tooltip("Seconds to halve the visual distance to high ready. Cover and ADS blocking remain immediate.")]
    [SerializeField, Range(.01f, .2f)] private float highReadyRaiseHalfLife = .07f;
    [Tooltip("Seconds to halve the visual distance back to the firing pose.")]
    [SerializeField, Range(.01f, .2f)] private float highReadyLowerHalfLife = .09f;
    [Header("Gun points for the HUD (weapon-root space, m)")]
    [Tooltip("Support-hand wrist under the handguard: the score popup hangs beside it.")]
    [SerializeField] private Vector3 leftGrip = new Vector3(.26f, -.285f, .7f);
    [Tooltip("Right side of the handguard's rail, where a laser sight mounts (weapon-root space, m). Zero: measured from " +
        "the gun, on its right surface at the bore's height a little ahead of the support hand. The score popup hangs just above it.")]
    [SerializeField] private Vector3 sideRail;
    [Header("Feel (presentation; aim recoil stays above)")]
    [Tooltip("Visual-only camera kick per shot, degrees: x = up, y = random left/right, z = random roll. Bullets still go to the crosshair.")]
    [SerializeField] private Vector3 viewPunch = new Vector3(.75f, .3f, .9f);
    [Tooltip("Momentary field-of-view widening per shot, degrees.")]
    [SerializeField, Range(0f, 4f)] private float viewPunchFov = .6f;
    [Tooltip("Camera shake added per shot (0..1). Builds up under sustained fire.")]
    [SerializeField, Range(0f, .5f)] private float shakeTrauma = .07f;
    [SerializeField, Range(0f, 3f)] private float muzzleFlashScale = 1f;
    [SerializeField] private bool ejectShells = true;
    [Tooltip("Synthesized shot used when Fire Sounds is empty.")]
    [SerializeField] private GunSoundProfile soundProfile = GunSoundProfile.Rifle;
    [SerializeField] private AudioClip[] fireSounds = new AudioClip[0];
    [Tooltip("The shot with a suppressor on (the Tactical Shooter Pack's own); empty plays Fire Sounds, quieter and higher.")]
    [SerializeField] private AudioClip[] suppressedFireSounds = new AudioClip[0];
    [SerializeField, Range(0f, 2f)] private float fireVolume = 1f;
    [Header("Feel → target reaction (gameplay)")]
    [Tooltip("Posture (stagger meter) damage per bullet. Headshots multiply it (GunFeel). Bullets never cancel an attack through posture alone.")]
    [SerializeField, Min(0f)] private float postureDamage = 4f;
    [Tooltip("Scales bullet push, slow and the body-damage interrupt threshold of the enemy hit reaction.")]
    [SerializeField, Range(0f, 3f)] private float flinchMultiplier = 1f;
    [Tooltip("Momentum (N·s) one bullet gives a PhysicsProp: 3 sends a 1 kg prop at 3 m/s and a 20 kg prop at 0.15 m/s.")]
    [SerializeField, Min(0f)] private float propImpulse = 3f;

    public string DisplayName => displayName;
    public Sprite HudIcon => hudIcon;
    public CrosshairProfile CrosshairProfile => crosshairProfile;
    public WeaponCategory Category => category;
    public WeaponFireMode FireMode => fireMode;
    public float Damage => Mathf.Max(0.01f, damage);
    public float ShotInterval => 60f / Mathf.Max(1f, roundsPerMinute);
    public int MagazineSize => Mathf.Max(1, magazineSize);
    public float ReloadDuration => Mathf.Max(0.01f, reloadDuration);
    public float Range => Mathf.Max(0.1f, range);
    public float NoiseRadius => Mathf.Max(0f, noiseRadius);
    public float SuppressedNoise => Mathf.Clamp01(suppressedNoise);
    public float GripRecoil => Mathf.Clamp01(gripRecoil);
    /// <summary>For the customize menu's stats: how far each shot climbs (degrees), before its random share.</summary>
    public float RecoilVertical => recoilVertical > 0f ? recoilVertical : recoilPerShot.x;
    public float GripAimTime => Mathf.Clamp(gripAimTime, .1f, 1f);
    public float RecoilResponse => Mathf.Clamp(recoilResponse, 8f, 50f);
    public float RecoilRetention => Mathf.Clamp01(recoilRetention);
    public int InitialReserveAmmo => Mathf.Max(0, initialReserveAmmo);
    public float EmptyReloadDuration => Mathf.Max(.01f, emptyReloadDuration);
    public int BurstCount => Mathf.Max(1, burstCount);
    public float BurstPause => Mathf.Max(0f, burstPause);
    public float HipSpread => Mathf.Max(0f, hipSpread);
    public float AdsSpread => Mathf.Max(0f, adsSpread);
    public float MovingSpread => Mathf.Max(0f, movingSpread);
    public float AirborneSpread => Mathf.Max(0f, airborneSpread);
    public float BloomPerShot => Mathf.Max(0f, bloomPerShot);
    public float MaxBloom => Mathf.Max(0f, maxBloom);
    public float BloomRecovery => Mathf.Max(0f, bloomRecovery);
    public float HeadshotMultiplier => Mathf.Max(1f, headshotMultiplier);
    public float Penetration => Mathf.Max(0f, penetration);
    /// <summary>Its round's own rule for bodies (the Desperado's piercing round); none for most guns.</summary>
    public BulletPierce BodyPierce => new BulletPierce(pierceBodies, pierceKeep);
    public float CrouchSpread => Mathf.Clamp(crouchSpread, .05f, 1f);
    public float CrouchRecoil => Mathf.Clamp(crouchRecoil, .05f, 1f);
    /// <summary>Seconds after the gun could fire again that an empty magazine reloads by itself; negative: never.</summary>
    public float AutoReloadDelay => autoReloadDelay;
    public float DrawDuration => Mathf.Max(0f, drawDuration);
    public float HolsterDuration => Mathf.Max(0f, holsterDuration);
    public int Pellets => Mathf.Max(1, pellets);
    public float PelletSpread => Mathf.Max(0f, pelletSpread);
    /// <summary>A shotgun whose pellets draw in while it is held down the sights (Choke Seconds).</summary>
    public bool HasChoke => chokeSeconds > 0f && Pellets > 1;
    /// <summary>Seconds between fanned rounds, and the degrees fanning adds to the cone.</summary>
    public float FanInterval => 60f / Mathf.Max(1f, fanRoundsPerMinute);
    public float FanSpread => Mathf.Max(0f, fanSpread);
    public float ChokeSeconds => Mathf.Max(0f, chokeSeconds);

    /// <summary>
    /// The pellets' circle (half-angle, degrees, on top of the gun's own spread) at <paramref name="aim"/> down the sights
    /// and <paramref name="choke"/> of the choke drawn in (both 0-1): wide at the hip to sweep what is close (user,
    /// 2026-11-11: "근거리에선 시원하게 한번에 여러 명을 다 쓸어 버리는"), tighter the moment the sights are up, and in to
    /// Choke Pellet Spread as the choke draws (Apex's Peacekeeper).
    /// </summary>
    public float PelletCone(float aim, float choke)
    {
        float aimed = adsPelletSpread > 0f ? adsPelletSpread : PelletSpread;
        if (HasChoke) aimed = Mathf.Lerp(aimed, Mathf.Max(0f, chokePelletSpread), Mathf.Clamp01(choke));
        return Mathf.Lerp(PelletSpread, aimed, Mathf.Clamp01(aim));
    }
    public bool ShellReload => shellReload;
    public float ShellInsertDuration => Mathf.Max(.01f, shellInsertDuration);
    public float ShellEndDuration => Mathf.Max(0f, shellEndDuration);
    public bool EmptyStartLoads => emptyStartLoads;
    public AudioClip[] ReloadStartSounds => reloadStartSounds;
    public AudioClip[] ReloadStartEmptySounds => reloadStartEmptySounds;
    public AudioClip[] ShellInsertSounds => shellInsertSounds;
    public AudioClip[] ReloadEndSounds => reloadEndSounds;
    public AudioClip[] MagOutSounds => magOutSounds;
    public AudioClip[] MagInSounds => magInSounds;
    public AudioClip[] BoltBackSounds => boltBackSounds;
    public AudioClip[] BoltForwardSounds => boltForwardSounds;
    public AudioClip[] DrawSounds => drawSounds;
    public AudioClip[] CycleSounds => cycleSounds;
    public float AdsFov => Mathf.Clamp(adsFov, 20f, 100f);
    public float AdsMoveMultiplier => Mathf.Clamp(adsMoveMultiplier, .1f, 1f);
    public float AdsTransitionTime => Mathf.Max(.01f, adsTransitionTime);
    public Vector3 LeftGrip => leftGrip;
    /// <summary>The handguard's right-side rail mount (weapon-root space), or zero when the gun should be measured.</summary>
    public Vector3 SideRail => sideRail;
    public float HighReadyRaiseHalfLife => Mathf.Clamp(highReadyRaiseHalfLife, .01f, .2f);
    public float HighReadyLowerHalfLife => Mathf.Clamp(highReadyLowerHalfLife, .01f, .2f);
    public Vector3 ViewPunch => viewPunch;
    public float ViewPunchFov => Mathf.Clamp(viewPunchFov, 0f, 4f);
    public float ShakeTrauma => Mathf.Clamp(shakeTrauma, 0f, .5f);
    public float MuzzleFlashScale => Mathf.Clamp(muzzleFlashScale, 0f, 3f);
    public bool EjectShells => ejectShells;
    public GunSoundProfile SoundProfile => soundProfile;
    public AudioClip[] FireSounds => fireSounds;
    public AudioClip[] SuppressedFireSounds => suppressedFireSounds;
    public float FireVolume => Mathf.Clamp(fireVolume, 0f, 2f);
    public float PostureDamage => Mathf.Max(0f, postureDamage);
    public float FlinchMultiplier => Mathf.Clamp(flinchMultiplier, 0f, 3f);
    public float PropImpulse => Mathf.Max(0f, propImpulse);
    public Vector2 RecoilAt(int index) => recoilPattern != null && recoilPattern.Length > 0
        ? recoilPattern[Mathf.Abs(index) % recoilPattern.Length] : recoilPerShot;

    /// <summary>
    /// The fire mode after <paramref name="current"/> in Fire Modes (B), round again from the last; <paramref name="current"/>
    /// itself when the gun has no other.
    /// </summary>
    public WeaponFireMode NextFireMode(WeaponFireMode current)
    {
        var modes = new System.Collections.Generic.List<WeaponFireMode>();
        foreach (string part in (fireModes ?? string.Empty).Split(';', ',', ' '))
            if (System.Enum.TryParse(part.Trim(), true, out WeaponFireMode mode) && !modes.Contains(mode)) modes.Add(mode);
        if (!modes.Contains(fireMode)) modes.Insert(0, fireMode);
        if (modes.Count <= 1) return current;
        int at = modes.IndexOf(current);
        return modes[(at + 1) % modes.Count];
    }

    /// <summary>Share of the body damage a bullet keeps at <paramref name="distance"/> m (Falloff Start, End, Minimum).</summary>
    public float FalloffAt(float distance)
    {
        float minimum = Mathf.Clamp(falloffMinimum, .05f, 1f);
        if (distance <= falloffStart || minimum >= 1f) return 1f;
        float span = Mathf.Max(.01f, falloffEnd - falloffStart);
        return Mathf.Lerp(1f, minimum, Mathf.Clamp01((distance - falloffStart) / span));
    }

    /// <summary>
    /// The view's kick (pitch up, yaw right; degrees) for the spray's shot number <paramref name="shot"/> (0 first), the
    /// Rainbow Six Siege way when Recoil Vertical is set: a steady lift, a bigger first one, and a sideways throw that grows
    /// over the settle shots along a wave of this gun's own. Otherwise RecoilAt.
    /// </summary>
    public Vector2 RecoilKick(int shot)
    {
        if (recoilVertical <= 0f) return RecoilAt(shot);
        shot = Mathf.Max(0, shot);
        float lift = recoilVertical * (shot == 0 ? recoilFirstShot : 1f);
        float settle = Mathf.Lerp(.25f, 1f, Mathf.Clamp01(shot / (float)Mathf.Max(1, recoilSettleShots)));
        float wave = Mathf.PerlinNoise(PatternSeed, shot * .45f) * 2f - 1f;
        float side = (Mathf.Clamp(wave + recoilDrift, -1f, 1f) + Random.Range(-.15f, .15f)) * recoilHorizontal * settle;
        return new Vector2(lift, side);
    }

    // Where along Perlin noise this gun's sideways wave runs: from its name, so it is the same in every session and build.
    private float PatternSeed
    {
        get
        {
            int hash = 17;
            foreach (char c in name) hash = unchecked(hash * 31 + c);
            return (hash & 1023) * .173f;
        }
    }
}
