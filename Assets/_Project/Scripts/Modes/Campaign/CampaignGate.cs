using UnityEngine;

/// <summary>
/// Stage 5C: something a campaign step opens, like the alley's shutter in chapter 1. Shut before its step, it rises
/// through the later part of a hold (CampaignRun.ProgressOf: the hold's share of its time, from openFrom on) and is open
/// once the step is done; a wipe that sends
/// the run back shuts it again. While its step runs an alarm wails from it, a two-tone siren made here, since the
/// project has no alarm sound. It blocks players with its collider and enemies with a carving NavMeshObstacle, so no
/// horde comes through until it opens; the walk mesh was baked without it (NavMeshModifier, GreyboxBuilder.Gate). Every PC
/// moves its own from the run it follows; a guest's eases between the host's whole-second reports.
/// </summary>
[DisallowMultipleComponent]
public sealed class CampaignGate : MonoBehaviour
{
    // How fast the shown opening may change, as a share of the whole a second.
    private const float CatchUp = .5f;
    private const int SampleRate = 44100;
    private static AudioClip siren;

    [SerializeField, Tooltip("The step that opens it (a CampaignSteps id).")]
    private string step;
    [SerializeField, Tooltip("How it moves from shut to open (m, in its parent's space).")]
    private Vector3 openOffset = new Vector3(0f, 4.4f, 0f);
    [SerializeField, Range(0f, 1f), Tooltip("Share of the hold that passes before it starts to move, so nobody slides under it early.")]
    private float openFrom = .5f;
    [SerializeField, Tooltip("An alarm wails from it while its step runs.")]
    private bool alarm = true;

    private Vector3 shut;
    private float shown;
    private AudioSource sound;

    private void Awake() => shut = transform.localPosition;

    private void Update()
    {
        float progress = CampaignRun.ProgressOf(step);
        float target = progress >= 1f ? 1f : openFrom >= 1f ? 0f : Mathf.Clamp01((progress - openFrom) / (1f - openFrom));
        shown = Mathf.MoveTowards(shown, target, CatchUp * Time.deltaTime);
        transform.localPosition = shut + openOffset * shown;
        Ring(alarm && CampaignRun.IsRunning(step));
    }

    private void Ring(bool on)
    {
        if (!on)
        {
            if (sound != null && sound.isPlaying) sound.Stop();
            return;
        }
        if (sound == null)
        {
            sound = gameObject.AddComponent<AudioSource>();
            sound.clip = Siren();
            sound.loop = true;
            sound.playOnAwake = false;
            sound.spatialBlend = 1f;
            sound.minDistance = 8f;
            sound.maxDistance = 90f;
            GameAudio.Bind(sound, AudioChannel.Combat);
        }
        if (!sound.isPlaying) sound.Play();
    }

    // Two tones taking turns, a car alarm's wail: 0.35 s each, a soft edge on each so the switch doesn't click.
    private static AudioClip Siren()
    {
        if (siren != null) return siren;
        const float Tone = .35f, High = 960f, Low = 760f, Level = .32f, Edge = .01f;
        int perTone = Mathf.RoundToInt(Tone * SampleRate), total = perTone * 2;
        var samples = new float[total];
        float phase = 0f;
        for (int i = 0; i < total; i++)
        {
            float pitch = i < perTone ? High : Low, inTone = (i % perTone) / (float)SampleRate;
            phase += 2f * Mathf.PI * pitch / SampleRate;
            float wave = Mathf.Sin(phase) + .3f * Mathf.Sin(3f * phase) + .15f * Mathf.Sin(5f * phase);
            float fade = Mathf.Min(1f, inTone / Edge, (Tone - inTone) / Edge);
            samples[i] = wave * Level * fade;
        }
        siren = AudioClip.Create("Campaign Siren", total, 1, SampleRate, false);
        siren.SetData(samples, 0);
        return siren;
    }
}
