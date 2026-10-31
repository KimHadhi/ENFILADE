using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The sounds of the world's ladders, ziplines and balance beams, after Beatrun's: taking hold, each rung, the slide
/// down a ladder with its scrape, the ride's hum, letting go, and each note and step on a beam. The climb off a ladder's top is a
/// mantle and sounds in PlayerMovementAudio. What each cue plays is tuned in Tables/SoundCues. Numbers: Player.csv row
/// FixtureAudio.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerFixtureAudio : MonoBehaviour
{
    [SerializeField, Range(0f, 1f), Tooltip("Loudness of the scrape sliding down a ladder.")]
    private float ladderSlideVolume = .8f;
    [SerializeField, Range(0f, 1f), Tooltip("Loudness of a zipline's hum at ziplineLoudSpeed.")]
    private float ziplineVolume = .3f;
    [SerializeField, Min(.1f), Tooltip("Ride speed where the hum is loudest (m/s).")]
    private float ziplineLoudSpeed = 12f;
    [SerializeField, Min(.01f), Tooltip("How long the scrape and the hum take to fade in or out (s).")]
    private float fadeTime = .1f;

    private const string MovementBank = "Movement", ZiplineLoop = "Zipline/Loop";
    private static readonly string[] LadderSlide = { "Ladder/Slide", "Ladder/SlideHands" };

    private PlayerMovement player;
    private CharacterSounds sounds;
    private SoundLoop[] slideLoops;
    private SoundLoop rideLoop;
    private float rideLevel;
    private bool sliding;

    private void Awake() => player = GetComponent<PlayerMovement>();

    private void OnEnable()
    {
        if (player == null) return;
        player.StateChanged += OnStateChanged;
        player.LadderStepped += OnLadderStepped;
        player.BalanceBeat += OnBalanceBeat;
        player.BalanceStep += OnBalanceStep;
    }

    private void OnDisable()
    {
        if (player != null)
        {
            player.StateChanged -= OnStateChanged;
            player.LadderStepped -= OnLadderStepped;
            player.BalanceBeat -= OnBalanceBeat;
            player.BalanceStep -= OnBalanceStep;
        }
        rideLoop?.Stop();
        if (slideLoops != null) foreach (var loop in slideLoops) loop?.Stop();
        sliding = false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || player == null || !player.IsInitialized) return;
        var fixtures = player.Abilities.Fixtures;
        KeepSliding(player.CurrentState == MovementState.Ladder && fixtures.LadderSliding);
        if (rideLoop != null)
        {
            bool riding = player.CurrentState == MovementState.Zipline;
            rideLoop.FadeTo(riding ? rideLevel * Mathf.Clamp01(fixtures.ZiplineSpeed / ziplineLoudSpeed) : 0f, fadeTime);
            rideLoop.Tick(dt);
        }
        if (slideLoops != null) foreach (var loop in slideLoops) loop?.Tick(dt);
    }

    private void OnStateChanged(MovementState from, MovementState to)
    {
        var fixtures = player.Abilities.Fixtures;
        if (from == MovementState.Ladder && to != MovementState.Mantle) Cue("LadderLetGo", null, true);
        if (from == MovementState.Zipline) Cue("ZiplineEnd", null, true);
        switch (to)
        {
            case MovementState.Ladder: Cue(fixtures.LadderCaught ? "LadderCatch" : "LadderEnter", null, true); break;
            case MovementState.Zipline:
                Cue("ZiplineStart", null, true);
                StartRide();
                break;
            case MovementState.BalanceFall: Cue("BalanceFall", Surfaces.Of(fixtures.Beam != null ? fixtures.Beam.Surface : null)); break;
        }
    }

    private void OnLadderStepped(bool leftHand) => Cue("LadderStep", null, true);

    private void OnBalanceBeat(BalanceJudgement judgement)
    {
        string cue = judgement == BalanceJudgement.Perfect ? "BalancePerfect" : judgement == BalanceJudgement.Good ? "BalanceGood" : "BalanceStumble";
        Cue(cue, BeamSurface());
    }

    // Both modes report actual contacts; walking uses this cue, running keeps its ordinary footstep cue.
    private void OnBalanceStep()
    {
        if (player.CurrentState == MovementState.Balance) Cue("BalanceStep", BeamSurface());
    }

    private string BeamSurface()
    {
        var beam = player.Abilities.Fixtures.Beam;
        return Surfaces.Of(beam != null ? beam.Surface : null);
    }

    private void StartRide()
    {
        var voices = Sounds();
        var bank = voices != null ? voices.Bank(MovementBank) : null;
        if (bank == null) return;
        rideLoop ??= voices.CreateLoop("Zipline hum");
        rideLevel = bank.SettingsOf(ZiplineLoop).Volume * ziplineVolume;
        rideLoop.Play(bank.Pick(ZiplineLoop), 1f);
    }

    private void KeepSliding(bool now)
    {
        if (now == sliding) return;
        sliding = now;
        var voices = Sounds();
        var bank = voices != null ? voices.Bank(MovementBank) : null;
        if (bank == null) return;
        slideLoops ??= new SoundLoop[LadderSlide.Length];
        for (int i = 0; i < LadderSlide.Length; i++)
        {
            slideLoops[i] ??= voices.CreateLoop(LadderSlide[i] + " scrape");
            if (now) slideLoops[i].Play(bank.Pick(LadderSlide[i]), 1f);
            slideLoops[i].FadeTo(now ? bank.SettingsOf(LadderSlide[i]).Volume * ladderSlideVolume : 0f, fadeTime);
        }
    }

    // At the feet, or at the hands for what they hold.
    private void Cue(string cue, string surface, bool atHands = false)
    {
        Vector3 point = transform.position + (atHands ? Vector3.up * 1.6f : Vector3.zero);
        Sounds()?.Play(cue, point, surface);
    }

    private CharacterSounds Sounds() => sounds != null ? sounds : sounds = GetComponent<CharacterSounds>();
}
