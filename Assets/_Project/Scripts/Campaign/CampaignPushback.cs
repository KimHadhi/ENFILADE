using UnityEngine;

/// <summary>
/// Chapter 4's device (the user's memo 15, the airport): an airliner left across the runway, pushed off it by its tug so
/// the last transport can take off. The builder puts the airliner's boxes under this object (GreyboxBuilder.Pushback)
/// and the step's spot on it beside the tug. The step is a manned hold (CampaignSteps.manned): its clock runs only while
/// someone stands at the tug, so the plane rolls back only then and stops where it is when they step away; nothing is
/// taken back for leaving (a fight elsewhere costs time, never progress).
/// Every PC moves its own from the run it follows (CampaignRun.ProgressOf): while the hold runs it rolls toward the
/// hold's share of `offset`, never faster than CatchUp times the hold's own pace, so a guest's whole-second reports read
/// as one steady roll. Anything else is where it should be at once: before the hold, after it (also for anyone joining
/// late), and back at the start after a wipe. While it rolls, the tug growls and its reversing alarm beeps, a sound made
/// here since the project has no vehicle sounds. The airliner stays out of the walk-mesh bake: its body, wings and
/// engines hang high enough to walk under, and its gear and the tug carve the walk mesh as they go
/// (GreyboxBuilder.Carve).
/// </summary>
[DisallowMultipleComponent]
public sealed class CampaignPushback : MonoBehaviour
{
    private const int SampleRate = 44100;
    // How much faster than the hold's own pace it may catch up with the run.
    private const float CatchUp = 1.25f;
    // How long it may stand still before the engine stops (s): a guest's roll pauses briefly between reports.
    private const float EngineLinger = .4f;
    private static AudioClip engineClip;

    [SerializeField, Tooltip("The manned hold that moves it (a CampaignSteps id).")]
    private string step;
    [SerializeField, Tooltip("How far it goes from where it stands, by the time the step is done (m, in its parent's space).")]
    private Vector3 offset = new Vector3(0f, 0f, 40f);
    [SerializeField, Tooltip("Where the tug's engine sounds from (the builder's tug box).")]
    private Transform tug;

    private Vector3 start;
    private float shown, movedAt = float.NegativeInfinity;
    private AudioSource engine;

    private void Awake() => start = transform.localPosition;

    private void Update()
    {
        float target = CampaignRun.ProgressOf(step), before = shown;
        if (CampaignRun.IsRunning(step) && target >= shown && CampaignRun.TryAssault(out _, out float length) && length > 0f)
            shown = Mathf.MoveTowards(shown, target, CatchUp / length * Time.deltaTime);
        else shown = target;
        transform.localPosition = start + offset * shown;
        if (shown > before) movedAt = Time.time;
        Growl(Time.time - movedAt < EngineLinger);
    }

    private void Growl(bool on)
    {
        if (!on)
        {
            if (engine != null && engine.isPlaying) engine.Stop();
            return;
        }
        if (engine == null)
        {
            engine = (tug != null ? tug.gameObject : gameObject).AddComponent<AudioSource>();
            engine.clip = EngineClip();
            engine.loop = true;
            engine.playOnAwake = false;
            engine.spatialBlend = 1f;
            engine.minDistance = 6f;
            engine.maxDistance = 80f;
            GameAudio.Bind(engine, AudioChannel.Combat);
        }
        if (!engine.isPlaying) engine.Play();
    }

    // Two seconds that loop: a diesel's low growl (a 32 Hz note and its overtones, throbbing with the firing at 16 Hz,
    // over a little rough noise) and the reversing alarm, a 1.1 kHz beep each second. Every tone fits whole cycles into
    // the two seconds, so the loop has no seam.
    private static AudioClip EngineClip()
    {
        if (engineClip != null) return engineClip;
        const float Seconds = 2f, Growl = 32f, Firing = 16f, Beep = 1100f, BeepLength = .35f, Edge = .01f;
        const float GrowlLevel = .3f, NoiseLevel = .06f, BeepLevel = .16f;
        int total = Mathf.RoundToInt(Seconds * SampleRate);
        var samples = new float[total];
        var random = new System.Random(4);
        float rough = 0f;
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)SampleRate, phase = 2f * Mathf.PI * Growl * t;
            float throb = .55f + .45f * Mathf.Pow(.5f + .5f * Mathf.Sin(2f * Mathf.PI * Firing * t), 3f);
            float growl = Mathf.Sin(phase) + .6f * Mathf.Sin(2f * phase) + .35f * Mathf.Sin(3f * phase) + .2f * Mathf.Sin(5f * phase);
            // Noise through a one-pole low-pass: a rumble, not a hiss.
            rough += .08f * ((float)random.NextDouble() * 2f - 1f - rough);
            float inBeep = t % 1f;
            float beep = inBeep < BeepLength
                ? Mathf.Sin(2f * Mathf.PI * Beep * t) * Mathf.Min(1f, inBeep / Edge, (BeepLength - inBeep) / Edge) : 0f;
            samples[i] = (growl * .45f * throb) * GrowlLevel + rough * 8f * NoiseLevel * throb + beep * BeepLevel;
        }
        engineClip = AudioClip.Create("Pushback Tug", total, 1, SampleRate, false);
        engineClip.SetData(samples, 0);
        return engineClip;
    }
}
