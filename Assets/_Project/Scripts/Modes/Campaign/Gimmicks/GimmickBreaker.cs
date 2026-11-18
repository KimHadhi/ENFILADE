using UnityEngine;

/// <summary>
/// A breaker box (the map gimmick cards "차단기와 불빛" and "셔터는 지하 차단기로"): it powers a circuit, the lamps on it and
/// what runs on it (MapGimmicks.Powered: a lift, a roller shutter). Down, its rooms are dark, so zombies make players out
/// later and from less far (StealthLight) and flashlights are wanted; up, the lights come on and those inside see far. Its
/// lamps are realtime only (the builder makes them so), as the map's baked light would keep their glow after they went
/// out. Worked with [F], or thrown by a campaign step instead (step: the step's own button is the [F], and the breaker
/// comes up as that step is done, the chapter 1 basement's starting the shutter's hold). States count the throws: odd is
/// the other way from how it was built.
/// A generator is one of these (the user, 2026-11-18: "옥상 엘리베이터 작동용 발전기 배치", chapter 1's lift): the same
/// source of a circuit's power, with its own words for [F] and an engine that is heard while it runs.
/// </summary>
public sealed class GimmickBreaker : GimmickSwitch
{
    // How far a running engine is heard from, full and gone (m).
    private const float EngineNear = 3f, EngineFar = 40f;

    [SerializeField, Tooltip("The circuit it powers (MapGimmicks.Powered), shared by the lamps and devices on it.")]
    private string circuit = "";
    [SerializeField, Tooltip("Texts ids of what [F] offers while it is down and while it is up: a breaker's by default, a generator says its own.")]
    private string offerUp = "gimmick.breaker.up", offerDown = "gimmick.breaker.down";
    [SerializeField, Tooltip("A generator: its engine is heard from it for as long as it is up (GimmickSounds.Cue.Generator).")]
    private bool engine;
    [SerializeField, Tooltip("Its lamps, lit while it is up.")]
    private Light[] lamps = new Light[0];
    [SerializeField, Tooltip("Things that glow while it is up (lamp shades, a lift's panel); may be empty.")]
    private Renderer[] glows = new Renderer[0];
    [SerializeField, Tooltip("Up as the map is built.")]
    private bool startsOn;
    [SerializeField, Tooltip("A campaign step that throws it instead of [F]: it comes up once that step is done.")]
    private string step = "";

    private bool shownOn;
    private AudioSource running;

    internal string Circuit => circuit;

    /// <summary>Up now, on this PC.</summary>
    internal bool On => !string.IsNullOrEmpty(step) ? StepDone || (State & 1) == 1 : ((State & 1) == 1) != startsOn;

    private bool StepDone => CampaignRun.ProgressOf(step) >= 1f;

    private void Start() => ShowLamps(On, false);

    internal override string Offer(PlayerMovement mine)
        => !string.IsNullOrEmpty(step) ? null : Texts.Get(On ? offerDown : offerUp);

    internal override int Use(PlayerMovement mine) => string.IsNullOrEmpty(step) ? (byte)(State + 1) : -1;

    // Two throws at once count once: the second asks from a state that is gone.
    internal override bool Accepts(byte now, byte proposed) => proposed == (byte)(now + 1);

    internal override void Show(byte state, bool fresh) => ShowLamps(On, fresh);

    internal override void Tick(float dt, bool host)
    {
        // A step's breaker follows its step on every PC: no state of its own goes round.
        if (!string.IsNullOrEmpty(step) && On != shownOn) ShowLamps(On, true);
    }

    private void ShowLamps(bool on, bool fresh)
    {
        bool changed = on != shownOn;
        shownOn = on;
        foreach (var lamp in lamps) if (lamp != null) lamp.enabled = on;
        foreach (var glow in glows) if (glow != null) glow.enabled = on;
        if (fresh && changed) GimmickSounds.PlayAt(GimmickSounds.Cue.Clunk, transform.position, 1f, 3f, 30f);
        if (!engine) return;
        // The engine is the state heard: running on every PC while it is up, a late joiner's too.
        if (running == null) running = GimmickSounds.Loop(gameObject, GimmickSounds.Cue.Generator, EngineNear, EngineFar);
        if (on && !running.isPlaying) running.Play();
        else if (!on && running.isPlaying) running.Stop();
    }
}
