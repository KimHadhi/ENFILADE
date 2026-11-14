using UnityEngine;

/// <summary>
/// A big lure (the map gimmick card "큰 미끼", after PAYDAY 3's speakers and Guts &amp; Blackpowder's music box): a TV, a
/// radio or a public address that [F] switches on. For its seconds it blares from its emitters (itself, or speakers far
/// off: an airport's announcement), and every calm zombie that hears it walks over and stays round it a while (a
/// gathering lure, NoiseEvent.Gather), turning their backs on the way past. Bunched there they can be blown up together,
/// or left behind. One go each: it is spent after. States: 0 ready, 1 on, 2 spent.
/// </summary>
public sealed class GimmickLure : GimmickSwitch
{
    private const byte Ready = 0, On = 1, Spent = 2;

    [SerializeField, Min(1f), Tooltip("How long it blares (s).")]
    private float seconds = 24f;
    [SerializeField, Min(1f), Tooltip("How far it carries to a calm zombie in the open (m; half through a wall, as any sound).")]
    private float radius = 36f;
    [SerializeField, Min(.2f), Tooltip("It is heard again this often while it blares (s): those that come in range later go too.")]
    private float every = 1.5f;
    [SerializeField, Tooltip("Where it sounds from; empty: from itself.")]
    private Transform[] emitters = new Transform[0];
    [SerializeField, Tooltip("Its sound.")]
    private GimmickSounds.Cue cue = GimmickSounds.Cue.Static;
    [SerializeField, Tooltip("Texts id of the [F] prompt (\"[F] TV 켜기\").")]
    private string prompt = "gimmick.lure.tv";
    [SerializeField, Tooltip("A light that glows while it blares (a TV's screen); may be empty.")]
    private Light glow;

    private AudioSource[] loops;
    private float startedAt, nextNoise, glowFull;

    internal override string Offer(PlayerMovement mine) => State == Ready ? Texts.Get(prompt) : null;

    internal override int Use(PlayerMovement mine) => State == Ready ? On : -1;

    internal override bool Accepts(byte now, byte proposed) => proposed > now;

    internal override void Show(byte state, bool fresh)
    {
        if (state == On)
        {
            startedAt = Time.time;
            nextNoise = 0f;
            Sound(true);
            if (glow != null) { glowFull = glowFull > 0f ? glowFull : glow.intensity; glow.enabled = true; }
        }
        else if (state == Spent)
        {
            Sound(false);
            if (glow != null) glow.enabled = false;
        }
    }

    internal override void Tick(float dt, bool host)
    {
        if (State != On) return;
        float on = Time.time - startedAt;
        // A screen's light breathes gently, never a flicker (the no-strobe rule).
        if (glow != null && glowFull > 0f) glow.intensity = glowFull * (.8f + .2f * Mathf.Sin(on * Mathf.PI * 1.2f));
        if (on >= seconds)
        {
            // Every PC goes quiet on its own clock; the host's word that it is spent follows.
            Sound(false);
            if (glow != null) glow.enabled = false;
            if (host) Set(Spent);
            return;
        }
        if (!host || Time.time < nextNoise) return;
        nextNoise = Time.time + every;
        if (emitters == null || emitters.Length == 0) Noise.Make(transform.position + Vector3.up * .5f, radius, gameObject, true, true);
        else foreach (var emitter in emitters) if (emitter != null) Noise.Make(emitter.position, radius, gameObject, true, true);
    }

    private void Sound(bool on)
    {
        if (loops == null)
        {
            var hosts = emitters != null && emitters.Length > 0 ? emitters : new[] { transform };
            loops = new AudioSource[hosts.Length];
            for (int i = 0; i < hosts.Length; i++)
                if (hosts[i] != null) loops[i] = GimmickSounds.Loop(hosts[i].gameObject, cue, 3f, radius);
        }
        foreach (var loop in loops)
        {
            if (loop == null) continue;
            if (on && !loop.isPlaying) loop.Play();
            else if (!on && loop.isPlaying) loop.Stop();
        }
    }
}
