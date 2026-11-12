using UnityEngine;

/// <summary>
/// A chapter's set-piece blast (moment-vfx's map of the Realistic Gunslinger pack, approved by the user on 2026-11-13: "ㅇㅇ
/// 제안대로 써"; the map part placed by maps-perf). The pack's Big Explosion Sequence goes off here a moment after its
/// campaign step starts, toned by its row of Tables/PackFx (PackFx: the pack's screen-filling glows, flares, flash and
/// warp left out, its last two bursts too, smaller near a camera), with the frag's bang played lower and heard from far,
/// the view shaken a little the nearer, and what it names left charred. Every PC plays its own from the run it follows
/// (CampaignRun.IsRunning, as CampaignGate opens), so everyone sees it at once; a wipe that sends the run back plays it
/// again when the step comes round. It only shows: nothing is hurt. The builder puts it where nobody stands when its step
/// starts (GreyboxBuilder.Blast): chapter 2's filling station, 58 m behind the toll hold; chapter 4's fuel truck, 60 m
/// from the tug.
/// </summary>
[DisallowMultipleComponent]
public sealed class CampaignBlast : MonoBehaviour
{
    [SerializeField, Tooltip("The step whose start sets it off (a CampaignSteps id).")]
    private string step;
    [SerializeField, Tooltip("Its row of Tables/PackFx: the effect and its look, its bang, its shake, its char.")]
    private string use = "setPieceBlast";
    [SerializeField, Tooltip("How long after the step starts it goes off (s).")]
    private float delay = 1.5f;
    [SerializeField, Tooltip("What is left charred when it goes off (the layout's boxes there).")]
    private Renderer[] scorched = System.Array.Empty<Renderer>();

    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), TopColor = Shader.PropertyToID("_TopColor");
    private bool wasRunning;
    private float goAt = -1f;

    private void Update()
    {
        bool running = CampaignRun.IsRunning(step);
        if (running && !wasRunning) goAt = Time.time + Mathf.Max(0f, delay);
        wasRunning = running;
        if (goAt < 0f || Time.time < goAt) return;
        goAt = -1f;
        Go();
    }

    private void Go()
    {
        Vector3 at = transform.position;
        PackFx.Play(use, at, transform.rotation);
        string sound = PackFx.Text(use, "sound");
        if (sound.Length > 0)
            Throwables.Play(sound, at + Vector3.up, PackFx.Number(use, "loud", 1f), PackFx.Number(use, "hearFull", 30f),
                PackFx.Number(use, "hearFar", 320f), PackFx.Number(use, "pitch", .7f));
        Shake(at);
        Scorch();
    }

    // The view kicks a little, the more the nearer: the row's shake at the blast, none past its shakeReach (m).
    private void Shake(Vector3 at)
    {
        var mine = PlayerRoster.Local;
        if (mine == null) return;
        float reach = Mathf.Max(1f, PackFx.Number(use, "shakeReach", 90f));
        float share = 1f - Mathf.Clamp01(Vector3.Distance(mine.transform.position, at) / reach);
        if (share <= 0f) return;
        share *= share * Mathf.Max(0f, PackFx.Number(use, "shake", .6f));
        var effects = mine.GetComponentInChildren<PlayerCameraEffects>();
        if (effects != null) effects.ApplyCameraImpulse(new Vector3(-8f * share, 0f, (Random.value < .5f ? -4f : 4f) * share), 4f * share);
        var eye = Camera.main;
        if (eye != null) CameraViewPunch.For(eye)?.Punch(new Vector3(-1.5f * share, 0f, 0f), 0f, .5f * share);
    }

    // What it names, charred: the colour it is drawn with (a map surface's top colour too) turned to the row's burnt.
    private void Scorch()
    {
        if (scorched == null || scorched.Length == 0) return;
        if (!ColorUtility.TryParseHtmlString(PackFx.Text(use, "burnt"), out Color burnt)) burnt = new Color(.16f, .14f, .13f);
        var block = new MaterialPropertyBlock();
        foreach (var part in scorched)
        {
            if (part == null) continue;
            part.GetPropertyBlock(block);
            block.SetColor(BaseColor, burnt);
            block.SetColor(TopColor, burnt);
            part.SetPropertyBlock(block);
        }
    }
}
