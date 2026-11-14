using UnityEngine;

/// <summary>
/// The objective bar's look of the fight's phase (CampaignRun.TryPhase), PAYDAY 2's cycle, in the look the user picked on
/// the third draft (2026-11-14): the sticker cut-in, with what stood out too much taken off ("A가 디자인도 너무 튀는 거
/// 빼면 좀 더 나은 듯"). Over the ! tab sits the phase's emblem (AssaultBar.Emblem.cs), the siren's place made anew ("사이렌
/// 부분은 아예 싹 다 개편해 줘"): a sticker that fills like a gauge and never spins, blue with a moon in the rest, filling
/// pink in the anticipation as it counts the seconds, a warning triangle in the assault. Under the bar runs the phase row
/// (AssaultBar.Row.cs): the phase on a small slanted plate, the rest's in blue ("정비 단계는 너무 기본이랑 비슷하니까
/// 아쉬움. 파란색이나 휴식 시간이란 걸 보여 줬으면"), the song's time, and a line of the song with its assaults in pink. As
/// the assault breaks in, on the song's highlight, a pink sticker band (AssaultBar.Band.cs) bounces in over the strip with
/// 습격 중, an ASSAULT stamp and the song running along its white foot (so the row has no song line of its own: "아래에
/// 노래 제목을 안 넣어도 될 듯"); it wipes away as the assault ends, and the rest's blue band comes in for a moment. The
/// entrance's one-off moments, the giant ASSAULT sweeping past behind, the white slash, one soft light and a halftone burst
/// out of the emblem, are in AssaultBar.Entrance.cs. Nothing blinks: the anticipation breathes, no faster than 1.6 times a
/// second and only down to half (a draft's hard blink nearly gave the user a panic attack).
/// It sits on the bar itself (MissionBarLook adds it), so it leans and follows with the HUD. Real time.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class AssaultBar : MonoBehaviour, IHudPreview
{
    // The ! tab as MissionBarLook lays it (Hud.csv Mission badgeSize, px).
    private const float TabWidth = 50f, TabHeight = 54f;
    // The anticipation's breath: as slow as this as it begins and as fast as this at the end (times a second), over its
    // last this many seconds, dimming only to this.
    private const float PulseSlow = .8f, PulseFast = 1.6f, PulseSpan = 12f, PulseDim = .55f;
    // What ENFILADE → HUD → 겹침 검사 shows while it holds the bar in its assault: a 4:00 song at 1:50, assaults 1:39-2:25.
    private const float PreviewElapsed = 110f, PreviewLength = 240f;
    private static readonly float[] PreviewAssaults = { 99f, 145f, 175f, 236f };

    private RectTransform root;
    private MissionBarLook look;
    private PhaseTrack last;
    private CombatPhase shown = CombatPhase.Quiet;
    private float pulseClock, pulse = 1f;
    private bool previewing;

    /// <summary>
    /// How far under the bar's foot the phase row reaches now (px), from the moment it starts to show until it has faded
    /// away; 0 while the bar is plain. Whatever stands under the bar keeps clear of this.
    /// </summary>
    public float Below => root != null && root.gameObject.activeSelf && rowShow > 0f ? RowBottom : 0f;

    /// <summary>ENFILADE → HUD → 겹침 검사: holds the bar in an assault (a 4:00 song at 1:50) while it measures.</summary>
    public void Preview(bool show) => previewing = show;

    private void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        bool wanted = CampaignRun.TryPhase(out var now);
        if (previewing && !wanted) { wanted = true; now = PreviewTrack(); }
        if (wanted && root == null) Build();
        if (root == null) return;
        var phase = wanted ? now.Phase : CombatPhase.Quiet;
        if (wanted) last = now;
        if (phase != shown) Turn(phase);
        StepBand(phase, dt);
        rowShow = Mathf.MoveTowards(rowShow, wanted ? 1f : 0f, dt / RowFade);
        emblemShow = Mathf.MoveTowards(emblemShow, wanted ? 1f : 0f, dt / EmblemFade);
        root.gameObject.SetActive(rowShow > 0f || emblemShow > 0f || BandShowing);
        if (!root.gameObject.activeSelf) return;
        Breathe(phase, dt);
        float width = Mathf.Max(TabWidth + 60f, ((RectTransform)transform).rect.width);
        PoseBand(width, dt);
        PoseEmblem(dt);
        PoseEntrance(width);
        PoseRow(width, dt);
    }

    // A new phase: the band opens or wipes away (the rest's follows an assault's), the emblem turns, the row's plate pops.
    private void Turn(CombatPhase phase)
    {
        var from = shown;
        shown = phase;
        TurnBand(from, phase);
        TurnEmblem(from, phase);
        TurnRow(phase);
    }

    // The anticipation breathes: a smooth swell, slow at first and faster as the assault nears, never a hard blink.
    private void Breathe(CombatPhase phase, float dt)
    {
        if (phase != CombatPhase.Anticipation) { pulse = 1f; pulseClock = 0f; return; }
        float nearness = Mathf.Clamp01((float.IsInfinity(last.Until) ? PulseSpan : last.Until) / PulseSpan);
        pulseClock = Mathf.Repeat(pulseClock + dt * Mathf.Lerp(PulseFast, PulseSlow, nearness), 1f);
        pulse = Mathf.Lerp(PulseDim, 1f, .5f + .5f * Mathf.Cos(pulseClock * Mathf.PI * 2f));
    }

    // How long the assault under way lasts: the song's window round now, or the hold's whole.
    private float AssaultSpan()
    {
        var spans = last.Assaults;
        if (spans != null)
            for (int i = 0; i + 1 < spans.Length; i += 2)
                if (last.Elapsed >= spans[i] && last.Elapsed < spans[i + 1]) return Mathf.Max(1f, spans[i + 1] - spans[i]);
        return Mathf.Max(1f, last.Length);
    }

    private PhaseTrack PreviewTrack()
    {
        var track = new PhaseTrack
        {
            Phase = CombatPhase.Assault, Elapsed = PreviewElapsed, Length = PreviewLength, Until = PreviewAssaults[1] - PreviewElapsed,
            Assaults = PreviewAssaults, Title = "", Artist = ""
        };
        var songs = CampaignMusic.All;
        if (songs.Count > 0)
        {
            var first = CampaignMusic.Find(songs[0]);
            track.Title = Texts.Cell(first.Title);
            track.Artist = Texts.Cell(first.Artist);
        }
        return track;
    }

    // Back to front: the giant word (BuildEntrance puts it first), the band, the emblem over the tab, the row, the
    // entrance's burst, light and slash.
    private void Build()
    {
        TryGetComponent(out look);
        root = FeelUI.Rect("Assault", (RectTransform)transform);
        root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
        root.sizeDelta = Vector2.zero;
        BuildBand();
        BuildEmblem();
        BuildRow();
        BuildEntrance();
        root.gameObject.SetActive(false);
    }

    // A rect hung from its parent's bottom-left corner.
    private static RectTransform Corner(string label, RectTransform parent)
    {
        var rect = FeelUI.Rect(label, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        return rect;
    }

    // A rect centred on its parent, sized by code.
    private static RectTransform Centred(string label, RectTransform parent)
    {
        var rect = FeelUI.Rect(label, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        return rect;
    }

    // A rect filling its parent, pushed out (or in, negative) by this much on each side.
    private static RectTransform Around(string label, RectTransform parent, float across, float up)
    {
        var rect = FeelUI.Rect(label, parent);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-across, -up);
        rect.offsetMax = new Vector2(across, up);
        return rect;
    }
}
