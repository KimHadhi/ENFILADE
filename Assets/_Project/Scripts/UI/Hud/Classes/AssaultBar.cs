using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The objective bar's look of the fight's phase (CampaignRun.TryPhase), PAYDAY 2's cycle. Under the bar runs the phase
/// row (AssaultBar.Row.cs): the phase in brackets with its countdown, the song as a meter of segments with its assaults
/// in pink, and the song playing, title and artist, beside a small equaliser (user, 2026-11-14: "어썰트 바에는 현재 무슨
/// 노래가 플레이 중인지도 알려 줬으면 해"). As the assault breaks in, on the song's highlight, the strip becomes an alarm
/// panel: dark, edged in pink with a soft glow, hazard stripes drifting along its right end, ASSAULT IN PROGRESS over the
/// song's name, a line along its foot shrinking as the assault runs out, and white brackets at its corners; the ! tab
/// turns into a beacon whose light slowly goes round.
/// The second draft round's pick (2026-11-14: "경보 패널은 B인데 뭔가 아까에 비해 Too much"): the draft's scanlines and
/// glitch entrance are gone. Then, built that calm, it read too plain ("습격 임박이랑 습격 진입이랑 너무 슴슴해졌는데"),
/// so the moments carry it in motion (AssaultBar.Entrance.cs): in the anticipation a striped charge fills along the
/// strip's top, the beacon turns faster and pings out rings, and the countdown pops every second; as the assault breaks
/// in, one soft light, a white slash drawn across, the giant ASSAULT sweeping past behind the bar (the user's note on the
/// draft), a halftone burst out of the beacon, a short shake, the panel slamming open past its width, the stripes
/// rushing and slowing, the title landing letter by letter. And nothing blinks: the anticipation breathes, a smooth
/// swell no faster than 1.6 times a second that only dims to half (the draft's on/off blink nearly gave the user a
/// panic attack).
/// It sits on the bar itself (MissionBarLook adds it), so it leans and follows with the HUD. Real time.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class AssaultBar : MonoBehaviour, IHudPreview
{
    // The ! tab as MissionBarLook lays it (Hud.csv Mission badgeSize), the gap after it, how far the panel reaches over
    // and under the strip, its slant, its edge, the glow round it (px).
    private const float TabWidth = 50f, TabHeight = 54f, PanelGap = 4f, PanelOver = 8f, Slant = 8f, Edge = 1.5f, GlowReach = 14f;
    // The brackets at the panel's corners: arm length, thickness, how far out (px), how far they slide in from.
    private const float BracketArm = 10f, BracketThick = 2f, BracketOut = 5f, BracketSlide = 12f;
    // The hazard stripes: their share of the panel's right end and drift (px/s); the light that passes once.
    private const float StripeShare = .46f, StripeSpeed = 12f, SweepWidth = 46f;
    // Timing (s): the panel opens, its words settle, the light passes at and for, it closes; the beacon fades and turns
    // once in this long (assault, anticipation).
    private const float GrowSeconds = .35f, SettleSeconds = .5f, SweepAt = .2f, SweepSeconds = .55f, CloseSeconds = .4f,
        BeaconFade = .3f, TurnAssault = 2f, TurnAnticipation = 4f;
    // The anticipation's breath: as slow as this as it begins and as fast as this at the end (times a second), over its
    // last this many seconds, dimming only to this.
    private const float PulseSlow = .8f, PulseFast = 1.6f, PulseSpan = 12f, PulseDim = .55f;
    // The panel's words: the title's size and spacing, the song line's size.
    private const float TitleSize = 15f, TitleSpacing = 22f, SongSize = 10.5f;
    private static readonly Color PanelColor = new Color(.075f, .05f, .07f, .95f), SongColor = new Color(1f, .53f, .64f);
    // What ENFILADE → HUD → 겹침 검사 shows while it holds the bar in its assault: a 4:00 song at 1:50, assaults 1:39-2:25.
    private const float PreviewElapsed = 110f, PreviewLength = 240f;
    private static readonly float[] PreviewAssaults = { 99f, 145f, 175f, 236f };

    private RectTransform root, panel, plate, edge, glow, stripes, stripeFade, sweep, remain, beacon, beam;
    private readonly RectTransform[] brackets = new RectTransform[4];
    private RawImage stripeImage;
    private CanvasGroup panelGroup, beaconGroup, bracketGroup;
    private TextMeshProUGUI title, song;
    private MissionBarLook look;
    private PhaseTrack last;
    private CombatPhase shown = CombatPhase.Quiet;
    private float age = -1f, ending = -1f, beaconShow, pulseClock, pulse = 1f;
    private bool on, previewing;

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
        bool assault = phase == CombatPhase.Assault;
        if (assault && !on) Open();
        else if (!assault && on) Close();
        if (on) age += dt;
        else if (ending >= 0f) ending += dt;
        bool closing = !on && ending >= 0f && ending < CloseSeconds;
        rowShow = Mathf.MoveTowards(rowShow, wanted ? 1f : 0f, dt / (wanted ? RowFade : CloseSeconds));
        root.gameObject.SetActive(rowShow > 0f || on || closing || beaconShow > 0f);
        if (!root.gameObject.activeSelf) return;
        Breathe(phase, dt);
        float width = Mathf.Max(TabWidth + 60f, ((RectTransform)transform).rect.width);
        PosePanel(width, closing, dt);
        PoseBeacon(phase, dt);
        PoseBuildUp(phase, width, dt);
        PoseEntrance(width);
        PoseRow(width, dt);
    }

    // The anticipation breathes: a smooth swell, slow at first and faster as the assault nears, never a hard blink.
    private void Breathe(CombatPhase phase, float dt)
    {
        if (phase != CombatPhase.Anticipation) { pulse = 1f; pulseClock = 0f; return; }
        float nearness = Mathf.Clamp01((float.IsInfinity(last.Until) ? PulseSpan : last.Until) / PulseSpan);
        pulseClock = Mathf.Repeat(pulseClock + dt * Mathf.Lerp(PulseFast, PulseSlow, nearness), 1f);
        pulse = Mathf.Lerp(PulseDim, 1f, .5f + .5f * Mathf.Cos(pulseClock * Mathf.PI * 2f));
    }

    private void Open()
    {
        on = true;
        age = 0f;
        ending = -1f;
        root.SetAsLastSibling();
        title.text = Texts.Get("hud.assault.title");
    }

    private void Close()
    {
        on = false;
        ending = 0f;
    }

    private void PosePanel(float width, bool closing, float dt)
    {
        panel.gameObject.SetActive(on || closing);
        if (!panel.gameObject.activeSelf) return;
        float full = Mathf.Max(20f, width - TabWidth - PanelGap);
        // It slams open a little past its width and settles back.
        float open = on ? KitMotion.BackOut(age / GrowSeconds) : 1f - Ease.Out(ending / CloseSeconds);
        float shownWidth = full * open;
        float height = ((RectTransform)transform).rect.height + PanelOver * 2f;
        panel.anchoredPosition = new Vector2(TabWidth + PanelGap, -PanelOver);
        panel.sizeDelta = new Vector2(shownWidth, height);
        panelGroup.alpha = on ? Mathf.Clamp01(age / (GrowSeconds * .6f)) : open;
        edge.sizeDelta = new Vector2(shownWidth + Edge * 2f, height + Edge * 2f);
        glow.sizeDelta = new Vector2(shownWidth + GlowReach * 2f, height + GlowReach * 2f);
        // The stripes drift along the right end, faded into the panel at their left.
        float stripeWidth = full * StripeShare;
        stripes.sizeDelta = new Vector2(stripeWidth, 0f);
        stripeFade.sizeDelta = new Vector2(stripeWidth * .7f, 0f);
        var uv = stripeImage.uvRect;
        uv.width = stripeWidth / 32f;
        uv.height = height / 32f;
        // The stripes rush in and slow to their drift.
        float speed = on ? Mathf.Lerp(RushSpeed, StripeSpeed, Ease.Out(age / RushSeconds)) : StripeSpeed;
        uv.x = Mathf.Repeat(uv.x + dt * speed / 32f, 1f);
        stripeImage.uvRect = uv;
        // One soft light passes as it opens.
        float sweepT = (age - SweepAt) / SweepSeconds;
        sweep.gameObject.SetActive(on && sweepT > 0f && sweepT < 1f);
        sweep.anchoredPosition = new Vector2(Mathf.Lerp(-SweepWidth, full + SweepWidth, Ease.InOut(sweepT)), 0f);
        // The title lands letter by letter, then the song under it fades in.
        if (on && age < TitleAt + KitMotion.LettersSeconds(title.text.Length, TitleStep, TitleLand) + .05f)
            KitMotion.LandLetters(title, age - TitleAt, TitleStep, TitleLand, TitleFrom);
        song.alpha = on ? Ease.Out(Mathf.Clamp01((age - .45f) / SettleSeconds)) : 1f;
        song.text = PanelSong();
        // The line along the foot is the assault's time left.
        float left = last.Phase == CombatPhase.Assault && !float.IsInfinity(last.Until) ? Mathf.Clamp01(last.Until / AssaultSpan()) : 0f;
        remain.sizeDelta = new Vector2(full * left, 2f);
        // The brackets slide in to the corners as it opens, and go with it.
        bracketGroup.alpha = on ? Ease.Out(Mathf.Clamp01((age - .1f) / .3f)) : open;
        float slide = on ? (1f - Ease.Out(Mathf.Clamp01((age - .1f) / .3f))) * BracketSlide : 0f;
        float x0 = TabWidth + PanelGap - BracketOut - slide, x1 = TabWidth + PanelGap + shownWidth + BracketOut + slide;
        float y0 = -PanelOver - BracketOut, y1 = -PanelOver + height + BracketOut;
        brackets[0].anchoredPosition = new Vector2(x0, y1);
        brackets[1].anchoredPosition = new Vector2(x1, y1);
        brackets[2].anchoredPosition = new Vector2(x0, y0);
        brackets[3].anchoredPosition = new Vector2(x1, y0);
    }

    // How long the assault under way lasts, for the line along the foot: the song's window round now, or the hold's whole.
    private float AssaultSpan()
    {
        var spans = last.Assaults;
        if (spans != null)
            for (int i = 0; i + 1 < spans.Length; i += 2)
                if (last.Elapsed >= spans[i] && last.Elapsed < spans[i + 1]) return Mathf.Max(1f, spans[i + 1] - spans[i]);
        return Mathf.Max(1f, last.Length);
    }

    private string PanelSong()
    {
        string band = Texts.Get("hud.assault.band");
        if (!string.IsNullOrEmpty(last.Title)) return Texts.Format("hud.assault.song", band, last.Title, last.Artist);
        return last.Siren ? Texts.Format("hud.assault.siren", band) : band;
    }

    // The ! tab becomes a beacon in the anticipation (breathing with it) and the assault; its light turns slowly.
    private void PoseBeacon(CombatPhase phase, float dt)
    {
        bool wanted = phase == CombatPhase.Assault || phase == CombatPhase.Anticipation;
        beaconShow = Mathf.MoveTowards(beaconShow, wanted ? 1f : 0f, dt / BeaconFade);
        beacon.gameObject.SetActive(beaconShow > 0f);
        if (!beacon.gameObject.activeSelf) return;
        beaconGroup.alpha = beaconShow * (phase == CombatPhase.Anticipation ? pulse : 1f);
        // In the anticipation it turns faster as the assault nears.
        float turn = phase == CombatPhase.Assault ? TurnAssault : Mathf.Lerp(TurnNear, TurnAnticipation, Nearness);
        beam.localRotation = Quaternion.Euler(0f, 0f, beam.localEulerAngles.z - 360f * dt / turn);
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

    private void Build()
    {
        TryGetComponent(out look);
        var bar = (RectTransform)transform;
        root = FeelUI.Rect("Assault", bar);
        root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
        root.sizeDelta = Vector2.zero;

        // The alarm panel over the strip: a glow, a pink edge, the dark plate that clips what is on it.
        panel = Corner("Panel", root);
        panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
        glow = Centred("Glow", panel);
        var glowImage = glow.gameObject.AddComponent<Image>();
        glowImage.sprite = CombatUIAssets.SoftGlow;
        glowImage.type = Image.Type.Sliced;
        glowImage.color = KitStyle.With(KitStyle.Accent, .22f);
        glowImage.raycastTarget = false;
        edge = Centred("Edge", panel);
        KitSkew.On(Fill(edge, KitStyle.With(KitStyle.Accent, .75f)), Slant);
        plate = FeelUI.Rect("Plate", panel);
        KitStyle.Stretch(plate);
        var plateImage = Fill(plate, PanelColor);
        KitSkew.On(plateImage, Slant);
        plate.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        stripes = FeelUI.Rect("Stripes", plate);
        stripes.anchorMin = new Vector2(1f, 0f);
        stripes.anchorMax = Vector2.one;
        stripes.pivot = new Vector2(1f, .5f);
        stripeImage = KitStyle.Raw(stripes, KitShapes.Stripes, KitStyle.With(KitStyle.Accent, .7f));
        stripeFade = FeelUI.Rect("Stripe Fade", stripes);
        stripeFade.anchorMin = Vector2.zero;
        stripeFade.anchorMax = Vector2.up;
        stripeFade.pivot = new Vector2(0f, .5f);
        KitStyle.Raw(stripeFade, KitShapes.Fade, PanelColor);
        var lit = FeelUI.Rect("Lit Edge", plate);
        lit.anchorMin = new Vector2(0f, 1f);
        lit.anchorMax = Vector2.one;
        lit.pivot = new Vector2(.5f, 1f);
        lit.sizeDelta = new Vector2(0f, 1f);
        Fill(lit, KitStyle.With(KitStyle.Accent, .45f));
        sweep = FeelUI.Rect("Sweep", plate);
        sweep.anchorMin = Vector2.zero;
        sweep.anchorMax = Vector2.up;
        sweep.pivot = new Vector2(.5f, .5f);
        sweep.sizeDelta = new Vector2(SweepWidth, 0f);
        KitSkew.On(Fill(sweep, KitStyle.With(Color.white, .18f)), 14f);
        remain = FeelUI.Rect("Time Left", plate);
        remain.anchorMin = remain.anchorMax = remain.pivot = Vector2.zero;
        Fill(remain, KitStyle.Accent);
        title = Words("Title", plate, TitleSize, Color.white, 6f);
        title.fontStyle |= FontStyles.Bold;
        title.characterSpacing = TitleSpacing;
        song = Words("Song", plate, SongSize, SongColor, -10f);
        song.fontStyle |= FontStyles.UpperCase;
        song.characterSpacing = 6f;

        // The white brackets at its corners, outside the clip.
        var bracketRoot = FeelUI.Rect("Brackets", root);
        bracketRoot.anchorMin = bracketRoot.anchorMax = bracketRoot.pivot = Vector2.zero;
        bracketGroup = bracketRoot.gameObject.AddComponent<CanvasGroup>();
        for (int i = 0; i < 4; i++) brackets[i] = Bracket(bracketRoot, i);

        // The beacon over the ! tab.
        beacon = Corner("Beacon", root);
        beacon.sizeDelta = new Vector2(TabWidth, TabHeight);
        beaconGroup = beacon.gameObject.AddComponent<CanvasGroup>();
        beacon.gameObject.AddComponent<RectMask2D>();
        Fill(beacon, KitStyle.Ink);
        Frame(beacon, KitStyle.With(KitStyle.Accent, .7f), 1f);
        beam = FeelUI.Rect("Beam", beacon);
        beam.anchorMin = beam.anchorMax = new Vector2(.5f, .5f);
        beam.pivot = new Vector2(.5f, 0f);
        beam.sizeDelta = new Vector2(46f, 110f);
        KitStyle.Raw(beam, KitShapes.Beam, KitStyle.With(KitStyle.Accent, .55f));
        var core = Centred("Core", beacon);
        core.sizeDelta = new Vector2(34f, 34f);
        KitStyle.Raw(core, KitShapes.Glow, KitStyle.With(KitStyle.Accent, .6f));
        var diamond = Centred("Diamond", beacon);
        diamond.sizeDelta = new Vector2(18f, 18f);
        diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Frame(diamond, Color.white, 2f);
        var mark = Centred("Mark", beacon);
        mark.sizeDelta = new Vector2(20f, 20f);
        var markText = KitStyle.Flat(mark, "!", 13f, TextAlignmentOptions.Center, Color.white);
        markText.fontStyle |= FontStyles.Bold;

        BuildRow();
        BuildEntrance();
        panel.gameObject.SetActive(false);
        beacon.gameObject.SetActive(false);
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

    private static Image Fill(RectTransform rect, Color color)
    {
        var image = KitStyle.Fill(rect, color);
        image.raycastTarget = false;
        return image;
    }

    // Four thin lines along a rect's edges, inside it.
    private static void Frame(RectTransform rect, Color color, float thick)
    {
        for (int side = 0; side < 4; side++)
        {
            var line = FeelUI.Rect("Frame", rect);
            bool across = side < 2;
            line.anchorMin = side == 0 ? Vector2.zero : side == 1 ? Vector2.up : side == 2 ? Vector2.zero : Vector2.right;
            line.anchorMax = side == 0 ? Vector2.right : side == 1 ? Vector2.one : side == 2 ? Vector2.up : Vector2.one;
            line.pivot = side == 0 ? new Vector2(.5f, 0f) : side == 1 ? new Vector2(.5f, 1f) : side == 2 ? new Vector2(0f, .5f) : new Vector2(1f, .5f);
            line.sizeDelta = across ? new Vector2(0f, thick) : new Vector2(thick, 0f);
            Fill(line, color);
        }
    }

    // One corner's bracket: two arms meeting at its corner (0 top left, 1 top right, 2 bottom left, 3 bottom right).
    private static RectTransform Bracket(RectTransform parent, int corner)
    {
        var at = FeelUI.Rect("Bracket", parent);
        at.anchorMin = at.anchorMax = Vector2.zero;
        at.pivot = Vector2.zero;
        bool right = corner == 1 || corner == 3, top = corner < 2;
        var across = FeelUI.Rect("Across", at);
        across.anchorMin = across.anchorMax = Vector2.zero;
        across.pivot = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
        across.sizeDelta = new Vector2(BracketArm, BracketThick);
        Fill(across, KitStyle.With(Color.white, .9f));
        var down = FeelUI.Rect("Down", at);
        down.anchorMin = down.anchorMax = Vector2.zero;
        down.pivot = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
        down.sizeDelta = new Vector2(BracketThick, BracketArm);
        Fill(down, KitStyle.With(Color.white, .9f));
        return at;
    }

    // A line of words on the plate, from its left edge, its middle this far above the plate's middle (px).
    private static TextMeshProUGUI Words(string label, RectTransform parent, float size, Color color, float rise)
    {
        var rect = FeelUI.Rect(label, parent);
        rect.anchorMin = new Vector2(0f, .5f);
        rect.anchorMax = new Vector2(1f, .5f);
        rect.pivot = new Vector2(0f, .5f);
        rect.offsetMin = new Vector2(14f, rise - size * .7f);
        rect.offsetMax = new Vector2(-12f, rise + size * .7f);
        var text = KitStyle.Flat(rect, "", size, TextAlignmentOptions.MidlineLeft, color);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }
}
