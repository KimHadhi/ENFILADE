using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The sounds of the player's actions, after Beatrun's: jumps, landings by how hard they hit, rolls, vaults, mantles,
/// ledge catches and climbs, wall climbs and kicks, bars, slides with their scrape, dives, dodges, prone, crouch and
/// lean. Listens to PlayerMovement's events and fires cues of Tables/SoundCues on the surface involved (the ground,
/// the obstacle, the ledge, the wall); what each cue plays is tuned there.
/// Footsteps are PlayerFootsteps', breathing PlayerBreathing's. Numbers: Player.csv row MovementAudio.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerMovementAudio : MonoBehaviour
{
    [Header("Landings")]
    [SerializeField, Min(0f), Tooltip("Gentler touchdowns make no landing sound; the footsteps cover them (m/s).")]
    private float landMinSpeed = 1.5f;
    [SerializeField, Min(0f), Tooltip("Touchdowns below this fall speed are soft (LandSoft), faster ones a full Land (m/s).")]
    private float landSoftSpeed = 4f;
    [SerializeField, Min(0f), Tooltip("Falls faster than this land hard (LandHard) (m/s). 12 m/s is about a 3 m drop.")]
    private float landHardSpeed = 12f;
    [SerializeField, Min(0f), Tooltip("Falls faster than this land heavily (LandHeavy) (m/s).")]
    private float landHeavySpeed = 16f;
    [SerializeField, Min(0f), Tooltip("Touching down this soon after a vault or a mantle sounds as its end (VaultLand, MantleLand) (s).")]
    private float traversalLandWindow = 1f;

    [Header("Parkour")]
    [SerializeField, Min(0f), Tooltip("Catching a ledge while falling faster than this grunts (LedgeCatchHard) (m/s).")]
    private float ledgeCatchHardSpeed = 4.75f;
    [SerializeField, Min(.05f), Tooltip("Time between hand sounds while shimmying along a ledge (s).")]
    private float shimmyInterval = .4f;
    [SerializeField, Min(.05f), Tooltip("Time between footfalls while running up a wall (s).")]
    private float wallStepInterval = .3f;
    [SerializeField, Min(0f), Tooltip("Swinging through the bottom of a bar faster than this whooshes (rad/s).")]
    private float barSwingSpeed = 1f;

    [Header("Slide scrape")]
    [SerializeField, Range(0f, 1f), Tooltip("Loudness of the slide's scrape at slideLoudSpeed.")]
    private float slideLoopVolume = .4f;
    [SerializeField, Min(.1f), Tooltip("Slide speed where the scrape is loudest (m/s).")]
    private float slideLoudSpeed = 8.4f;
    [SerializeField, Min(.01f), Tooltip("How long the scrape takes to fade in or out (s).")]
    private float slideFadeTime = .15f;

    [Header("Lean")]
    [SerializeField, Range(.1f, 1f), Tooltip("Share of the full Q/E lean where the clothes rustle.")]
    private float leanSoundShare = .5f;

    private const string MovementBank = "Movement", SlideLoopSound = "SlideLoop";
    // Below this the player is shimmying no longer (m/s); how often a slide checks for new ground (s).
    private const float ShimmyMinSpeed = .2f, SlideSurfaceCheck = .25f;

    private PlayerMovement player;
    private CharacterSounds sounds;
    private SoundLoop slideLoop;
    private string slideSurface, recentTraversal;
    private Vector3 lastPosition;
    private float traversalEndedAt, fallSpeed, repeatTimer, slideSurfaceTimer, slideLevel, lastBarAngle;
    private int leanSide;
    private bool wasGrounded;

    private void Awake() => player = GetComponent<PlayerMovement>();

    private void OnEnable()
    {
        if (player == null) return;
        player.StateChanged += OnStateChanged;
        player.Landed += OnLanded;
        player.Jumped += OnJumped;
        lastPosition = transform.position;
    }

    private void OnDisable()
    {
        if (player != null)
        {
            player.StateChanged -= OnStateChanged;
            player.Landed -= OnLanded;
            player.Jumped -= OnJumped;
        }
        slideLoop?.Stop();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || player == null || !player.IsInitialized) return;
        var traversal = player.Abilities.Traversal;
        float moved = Vector3.Distance(transform.position, lastPosition) / dt;
        switch (player.CurrentState)
        {
            case MovementState.LedgeHang:
                // Pulling in to the hang moves the player too; only sidling along the ledge afterwards is shimmying.
                bool shimmying = moved > ShimmyMinSpeed && player.CurrentStateDuration > player.AbilitySettings.ledgeCatchDuration;
                Repeat(shimmying, shimmyInterval, "Shimmy", Surfaces.Of(traversal.Ledge.Surface), dt);
                break;
            case MovementState.WallClimb:
                Repeat(true, wallStepInterval, "WallStep", Surfaces.Of(traversal.Wall.collider), dt);
                break;
            case MovementState.BarSwing:
                // A whoosh each time the swing passes under the bar.
                if (Mathf.Sign(traversal.Angle) != Mathf.Sign(lastBarAngle) && Mathf.Abs(traversal.AngularSpeed) > barSwingSpeed)
                    Cue("BarSwing", null, true);
                lastBarAngle = traversal.Angle;
                break;
        }
        if (player.IsSliding) KeepSliding(dt);
        slideLoop?.Tick(dt);
        Lean();
        fallSpeed = Mathf.Max(0f, -player.VerticalVelocity);
        wasGrounded = player.IsGrounded;
        lastPosition = transform.position;
    }

    private void OnStateChanged(MovementState from, MovementState to)
    {
        var traversal = player.Abilities.Traversal;
        bool wasSliding = IsSlide(from), sliding = IsSlide(to);
        if (sliding && !wasSliding) StartSlide(to);
        else if (wasSliding && !sliding)
        {
            Cue("SlideEnd", Ground());
            slideLoop?.FadeTo(0f, slideFadeTime);
        }
        if (from == MovementState.Vault || from == MovementState.Mantle)
        {
            recentTraversal = from == MovementState.Vault ? "VaultLand" : "MantleLand";
            traversalEndedAt = Time.time;
        }
        if (from == MovementState.LedgeHang && to != MovementState.Mantle && to != MovementState.LedgeJump)
            Cue("LedgeDrop", Surfaces.Of(traversal.Ledge.Surface), true);
        repeatTimer = 0f;
        switch (to)
        {
            case MovementState.Standing:
                if (from == MovementState.Crouching && wasGrounded) Cue("Stand", Ground());
                else if (from == MovementState.Proning) Cue("ProneUp", Ground());
                break;
            case MovementState.Crouching:
                if (from == MovementState.Standing && wasGrounded) Cue("Crouch", Ground());
                else if (from == MovementState.Proning) Cue("ProneUp", Ground());
                break;
            case MovementState.Proning:
                if (from == MovementState.Standing || from == MovementState.Crouching) Cue("ProneDown", Ground());
                break;
            case MovementState.Diving:
            case MovementState.SupineDiving: Cue("Dive", Ground()); break;
            case MovementState.StepDodge: Cue("Dodge", Ground()); break;
            case MovementState.LandingRoll: Cue("Roll", Ground()); break;
            case MovementState.Tackle: Cue("Tackle", Ground()); break;
            case MovementState.Vault: Cue("Vault", Surfaces.Of(traversal.Obstacle.Surface), true); break;
            case MovementState.Mantle:
                Cue(from == MovementState.LedgeHang ? "LedgeClimb" : "Mantle", Surfaces.Of(traversal.Obstacle.Surface), true);
                break;
            case MovementState.LedgeHang:
                Cue(fallSpeed > ledgeCatchHardSpeed ? "LedgeCatchHard" : "LedgeCatch", Surfaces.Of(traversal.Ledge.Surface), true);
                break;
            case MovementState.LedgeJump: Cue("LedgeJump", Surfaces.Of(traversal.Ledge.Surface), true); break;
            case MovementState.WallClimb: Cue("WallClimb", Surfaces.Of(traversal.Wall.collider)); break;
            case MovementState.WallKick: Cue("WallKick", Surfaces.Of(traversal.Wall.collider)); break;
            case MovementState.BarGrab: Cue("BarGrab", null, true); break;
            case MovementState.BarSwing: lastBarAngle = traversal.Angle; break;
            // Jump releases a lache; crouch, a lost grip or a blocked swing just lets go.
            case MovementState.LacheRelease: Cue(player.Input.JumpPressed ? "BarRelease" : "BarLetGo", null, true); break;
        }
    }

    private void OnLanded(float impact)
    {
        // A landing roll has its own sound (Roll).
        if (player.CurrentState == MovementState.LandingRoll) return;
        string cue;
        if (impact >= landHeavySpeed) cue = "LandHeavy";
        else if (impact >= landHardSpeed) cue = "LandHard";
        else if (recentTraversal != null && Time.time - traversalEndedAt <= traversalLandWindow) cue = recentTraversal;
        else if (impact >= landSoftSpeed) cue = "Land";
        else if (impact >= landMinSpeed) cue = "LandSoft";
        else return;
        recentTraversal = null;
        Cue(cue, Ground());
    }

    private void OnJumped() => Cue("Jump", Ground());

    private void StartSlide(MovementState state)
    {
        slideSurface = Ground();
        slideSurfaceTimer = SlideSurfaceCheck;
        Cue(state == MovementState.Sliding ? "SlideStart" : "BellySlideStart", slideSurface);
        PlaySlideLoop();
    }

    private void KeepSliding(float dt)
    {
        slideSurfaceTimer -= dt;
        if (slideSurfaceTimer <= 0f)
        {
            slideSurfaceTimer = SlideSurfaceCheck;
            string ground = Ground();
            if (ground != slideSurface)
            {
                slideSurface = ground;
                PlaySlideLoop();
            }
        }
        float speed = new Vector2(player.ActualVelocity.x, player.ActualVelocity.z).magnitude;
        slideLoop?.FadeTo(slideLevel * Mathf.Clamp01(speed / slideLoudSpeed), slideFadeTime);
    }

    private void PlaySlideLoop()
    {
        var voices = Sounds();
        var bank = voices != null ? voices.Bank(MovementBank) : null;
        string key = bank != null ? bank.Find(SlideLoopSound, slideSurface, out _) : null;
        if (key == null) return;
        slideLoop ??= voices.CreateLoop("Slide scrape");
        var settings = bank.SettingsOf(key);
        slideLevel = settings.Volume * slideLoopVolume;
        slideLoop.Play(bank.Pick(key), settings.RandomPitch());
    }

    private void Lean()
    {
        float full = player.AbilitySettings.leanOffset;
        float share = full > 0f ? player.LeanOffset / full : 0f;
        if (Mathf.Abs(share) < leanSoundShare * .5f) leanSide = 0;
        else if (Mathf.Abs(share) >= leanSoundShare && (int)Mathf.Sign(share) != leanSide)
        {
            leanSide = (int)Mathf.Sign(share);
            Cue("Lean", null, true);
        }
    }

    // A cue every interval while active, the first one interval after it starts.
    private void Repeat(bool active, float interval, string cue, string surface, float dt)
    {
        if (!active)
        {
            repeatTimer = 0f;
            return;
        }
        repeatTimer += dt;
        if (repeatTimer < interval) return;
        repeatTimer -= interval;
        Cue(cue, surface, true);
    }

    // At the feet, or at the hands for grips and the upper body.
    private void Cue(string cue, string surface, bool atHands = false)
    {
        Vector3 point = transform.position + (atHands ? Vector3.up * 1.2f : Vector3.zero);
        Sounds()?.Play(cue, point, surface);
    }

    // What the player stands on, else the default surface.
    private string Ground()
    {
        Vector3 origin = transform.position + Vector3.up * .3f;
        return Physics.Raycast(origin, Vector3.down, out RaycastHit hit, .8f, player.groundLayers, QueryTriggerInteraction.Ignore)
            ? Surfaces.Of(hit) : Surfaces.Default;
    }

    private static bool IsSlide(MovementState state) => state == MovementState.Sliding
        || state == MovementState.HeadFirstSliding || state == MovementState.SupineSliding;

    private CharacterSounds Sounds() => sounds != null ? sounds : sounds = GetComponent<CharacterSounds>();
}
