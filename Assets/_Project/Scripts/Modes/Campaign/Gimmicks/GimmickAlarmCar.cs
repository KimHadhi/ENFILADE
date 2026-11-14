using UnityEngine;

/// <summary>
/// A car with its alarm set, left in a jam (the map gimmick card "경보 차가 섞인 정체 구간", after Left 4 Dead's car
/// alarms): shot, struck, hit by a thrown stone or can, run into or caught in a blast, it wails a while and every calm
/// zombie that hears it walks over to it (a gathering lure, NoiseEvent.Gather), which is how a far one is set off on
/// purpose to draw them off the way. The shot that sets it off makes no noise of its own (ShotMuffle; the user's note:
/// "총 쏴서 그 주변 좀비들이 경계 상태 됐다고 잘 진행하는 스텔스가 망치는 건 싫어"). Its lamps pulse smoothly while it wails, never
/// a strobe. A run arms only some of the jam's alarm cars (armedShare of them, by the run's seed); an armed one has a small
/// red light pulsing slowly inside its windscreen, the tell to look for, and the rest are dead cars. Whoever's PC saw it
/// set off says so (the actor decides). States: 0 armed, 1 wailing, 2 spent.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class GimmickAlarmCar : GimmickSwitch, IDamageReceiver
{
    private const byte Armed = 0, Wailing = 1, Spent = 2;
    // A lamp's slow pulse (Hz), and how near (m) a thrown thing must land on it, or a sprinter come, to set it off.
    private const float Pulse = 1.2f, Touch = .6f, RunInto = 4.2f;

    [SerializeField, Min(1f), Tooltip("How long it wails (s).")]
    private float seconds = 20f;
    [SerializeField, Min(1f), Tooltip("How far its wail carries to a calm zombie in the open (m).")]
    private float radius = 45f;
    [SerializeField, Tooltip("Its hazard lamps, pulsing while it wails; may be empty.")]
    private Light[] lamps = new Light[0];
    [SerializeField, Tooltip("The armed tell: a small red light inside, pulsing slowly while it is armed and quiet; may be empty.")]
    private Light tell;
    [SerializeField, Range(0f, 1f), Tooltip("The share of runs it is armed in (the run's seed decides, the same on every PC).")]
    private float armedShare = 1f;
    [SerializeField, Tooltip("Its roll of the run's seed: cars with different salts are armed independently.")]
    private int salt;

    // Armed this run (an unarmed one is only a car). Until the seed is known, armed as built.
    private bool armed = true;

    private Collider body;
    private AudioSource wail;
    private float startedAt, nextNoise;
    private float[] full;
    private float tellFull;

    private void Awake()
    {
        body = GetComponent<Collider>();
        ShotMuffle.Add(body);
        if (tell != null) tellFull = tell.intensity;
        full = new float[lamps.Length];
        for (int i = 0; i < lamps.Length; i++)
            if (lamps[i] != null) { full[i] = lamps[i].intensity; lamps[i].enabled = false; }
    }

    private void OnEnable() => Noise.Made += Heard;

    private void OnDisable() => Noise.Made -= Heard;

    private void OnDestroy() => ShotMuffle.Remove(body);

    internal override bool Accepts(byte now, byte proposed) => proposed > now;

    // This run's seed: armed or a dead car. A dead one drowns no shot either.
    internal override void Seed(ushort seed)
    {
        armed = MapGimmicks.Mix(seed, (uint)(salt * 613 + 7)) < armedShare;
        if (!armed) ShotMuffle.Remove(body);
        if (tell != null && !armed) tell.enabled = false;
    }

    public DamageResult ReceiveHit(DamageHit hit)
    {
        // This PC's own player's shot or blow sets it off here; the others' set it off on their own PCs.
        var by = hit.Source != null ? hit.Source.GetComponentInParent<PlayerMovement>() : null;
        if (armed && State == Armed && by != null && by.IsLocal) Set(Wailing);
        return DamageResult.Ignored;
    }

    // A thrown stone or can landing on it (a lure noise right at it), on the PC that saw it land.
    private void Heard(NoiseEvent noise)
    {
        if (!armed || State != Armed || !noise.Lure || noise.Gather || body == null) return;
        if (body.bounds.SqrDistance(noise.Point) > Touch * Touch) return;
        var thrower = noise.Source != null ? noise.Source.GetComponentInParent<PlayerMovement>() : null;
        if (thrower == null || thrower.IsLocal) Set(Wailing);
    }

    /// <summary>A blast on this PC from its own player (a frag, a claymore): every armed car in reach goes off.</summary>
    internal static void Blast(Vector3 at, float radius)
    {
        var map = MapGimmicks.Active;
        if (map == null) return;
        foreach (var each in map.Switches)
            if (each is GimmickAlarmCar car && car.armed && car.State == Armed && car.body != null && car.body.bounds.SqrDistance(at) <= radius * radius)
                car.Set(Wailing);
    }

    internal override void Show(byte state, bool fresh)
    {
        if (tell != null && state != Armed) tell.enabled = false;
        if (state == Wailing)
        {
            startedAt = Time.time;
            nextNoise = 0f;
            if (wail == null) wail = GimmickSounds.Loop(gameObject, GimmickSounds.Cue.CarAlarm, 6f, radius + 15f);
            wail.Play();
            foreach (var lamp in lamps) if (lamp != null) lamp.enabled = true;
        }
        else if (state == Spent)
        {
            if (wail != null) wail.Stop();
            foreach (var lamp in lamps) if (lamp != null) lamp.enabled = false;
        }
    }

    internal override void Tick(float dt, bool host)
    {
        if (State == Armed)
        {
            if (!armed) return;
            // The tell inside: a slow, soft pulse (never a blink).
            if (tell != null) tell.intensity = tellFull * (.35f + .65f * (.5f + .5f * Mathf.Sin(Time.time * Mathf.PI)));
            // Run into at a sprint by this PC's own player.
            var mine = Mine;
            if (mine != null && body != null && body.bounds.SqrDistance(mine.transform.position + Vector3.up * .9f) < .09f
                && new Vector2(mine.ActualVelocity.x, mine.ActualVelocity.z).magnitude >= RunInto)
                Set(Wailing);
            return;
        }
        if (State != Wailing) return;
        float on = Time.time - startedAt;
        for (int i = 0; i < lamps.Length; i++)
            if (lamps[i] != null) lamps[i].intensity = full[i] * (.55f + .45f * Mathf.Sin(on * Mathf.PI * 2f * Pulse));
        if (on >= seconds)
        {
            if (wail != null) wail.Stop();
            foreach (var lamp in lamps) if (lamp != null) lamp.enabled = false;
            if (host) Set(Spent);
            return;
        }
        if (!host || Time.time < nextNoise) return;
        nextNoise = Time.time + 1.5f;
        Noise.Make(body.bounds.center + Vector3.up * .5f, radius, gameObject, true, true);
    }
}
