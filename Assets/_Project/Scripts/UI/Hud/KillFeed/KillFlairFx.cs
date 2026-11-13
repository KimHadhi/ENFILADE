using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays a kill's flair (KillFlairs) on its feed row (KillFeedRow). On the weapon's icon: a layer drawn with the icon's own
/// sprite by KillFlair.shader (the barrel catching fire from the muzzle, the whole gun engulfed, gold, a trail of flame, a
/// tint, a white-hot flash) and pieces round it (two afterimages, a burst of light, a shock ring, a red cut, strokes thrown
/// off, two crossing lines). On the row: a stamp or a seal over its end, a line through the one killed, brackets and a
/// check round them, the bounty paid (on the hunter's own PC only), a chip, a tag over the icon, the one killed in the
/// flair's colour, a veil lifting off it, a beam through it, the headshot skull popping, and in the plate's own slanted
/// shape (KillRowPlate) its edge lines, a sheen, a flash and a light running round its outline. Each piece is built the
/// first time a flair needs it and kept for the row's next kill. Short and loud, as the user wants hits: a piece lands in
/// a sixth of a second, holds for the row's seconds and goes in a third; the line through the one killed and its colour
/// stay as long as the row. While a flair plays the row asks the feed for its room over and under it (Above, Below), so
/// nothing of it covers the rows next to it. Real time, like the row.
/// </summary>
public sealed partial class KillFlairFx
{
    private const string ShaderPath = "Combat/Shaders/KillFlair";
    // KillRowPlate's Shear: how far the plate's top runs right of its bottom at each end (px).
    private const float PlateShear = 9f;
    private const float Slam = .15f, Fade = .3f, FireReach = .3f, EngulfReach = .38f, GildReach = .33f, KickSeconds = .13f, KickSize = 5f;
    private static readonly int ModeId = Shader.PropertyToID("_Mode"), ProgressId = Shader.PropertyToID("_Progress"),
        HeatId = Shader.PropertyToID("_Heat"), LickId = Shader.PropertyToID("_Lick"), SeedId = Shader.PropertyToID("_Seed"),
        ColourId = Shader.PropertyToID("_FlairColor"), SpanId = Shader.PropertyToID("_Span"), FrontId = Shader.PropertyToID("_Front");
    private static readonly Color Night = new Color(.035f, .035f, .045f, .88f);
    private static Shader shader;

    private readonly RectTransform row;
    private readonly KillRowPlate plate;
    private readonly Image weapon, accent;
    private readonly TMP_Text victim;
    private readonly RawImage mark;
    private readonly float seed = Random.value * 50f;

    private Image layer;
    private Material material;
    private RectTransform stamp, seal, chip, frame, tick;
    private CanvasGroup stampGroup, sealGroup, chipGroup, frameGroup, tickGroup;
    private Image stampEdge, stampFill, sealRing, sealFill, chipPlate, strike, tickShort, tickLong, ring, markRing, beam;
    private readonly Image[] corners = new Image[8], specks = new Image[6], echoes = new Image[2], rays = new Image[6], crosses = new Image[2];
    private readonly Vector2[] speckWay = new Vector2[6];
    private RawImage chipMoney, spark, slash;
    private TextMeshProUGUI stampWords, sealWords, chipWords, tag;
    private KillRowCover veil;

    private KillFlairs.Play play;
    private float age = 99f;
    private int paid;
    private bool tinted, traced;

    /// <summary>What the row reads back each frame: its edge lines, its sheen, its jolt, the colour on the one killed and
    /// on its end bar, and the room its flair takes over and under it (px before the row's own scale).</summary>
    public Color Edge { get; private set; }
    public float EdgeStrength { get; private set; }
    public bool Sheening { get; private set; }
    public bool SheenEnded { get; private set; }
    public float SheenAt { get; private set; } = -1f;
    public Color SheenTint { get; private set; }
    public Vector2 Kick { get; private set; }
    public Color NameColour { get; private set; }
    public float NameTint { get; private set; }
    public Color AccentColour { get; private set; }
    public float AccentTint { get; private set; }
    public float Above { get; private set; }
    public float Below { get; private set; }

    /// <summary>A row without its plate, mark or end bar handed over: what needs them doesn't play.</summary>
    public KillFlairFx(RectTransform row, Image weapon, TMP_Text victim) : this(row, null, weapon, victim, null, null) { }

    public KillFlairFx(RectTransform row, KillRowPlate plate, Image weapon, TMP_Text victim, RawImage mark, Image accent)
    {
        this.row = row;
        this.plate = plate;
        this.weapon = weapon;
        this.victim = victim;
        this.mark = mark;
        this.accent = accent;
    }

    /// <summary>A new kill on this row: what it plays from now (nothing for none). The row's layout is already set.</summary>
    public void Play(KillFlairs.Play what)
    {
        play = what;
        age = 0f;
        Edge = Color.clear; EdgeStrength = 0f; Sheening = SheenEnded = false; SheenAt = -1f; Kick = Vector2.zero; NameTint = 0f;
        AccentTint = 0f; Above = Below = 0f;
        HideAll();
        if (!what.Any) return;
        var icon = what.Icon;
        if (icon != null) PlaceIcon(icon, what.ColourOf(icon));
        var flair = what.Row;
        if (flair == null) return;
        var pieces = flair.Row;
        Color colour = what.ColourOf(flair);
        string words = flair.Text ?? "";
        Vector2 at = victim.rectTransform.anchoredPosition;
        float wide = victim.rectTransform.sizeDelta.x;
        if ((pieces & KillFlairs.Row.Stamp) != 0) PlaceStamp(words, colour);
        if ((pieces & KillFlairs.Row.Seal) != 0) PlaceSeal(words, colour);
        // The bounty's sum is the hunter's own business (the user, 2026-11-12: "현상금까진 걍 본인만 보면 될 것 같고"); a chip of
        // words is everyone's.
        // Only for a kill that claimed a bounty (a duel's foe carries its mark too): never a sum that wasn't paid.
        bool money = (pieces & KillFlairs.Row.Money) != 0 && what.Mine && (what.Marks & KillMarks.Bounty) != 0;
        if (money || (pieces & KillFlairs.Row.Chip) != 0)
        {
            bool elite = (what.Marks & KillMarks.Elite) != 0;
            paid = money ? Mathf.RoundToInt(PlayerClass.Ability("Schiesshund", elite ? "bountyElite" : "bountyMoney", elite ? 300f : 100f)) : 0;
            PlaceChip(money, words, colour, (pieces & KillFlairs.Row.Seal) != 0);
        }
        if ((pieces & KillFlairs.Row.Strike) != 0) PlaceStrike(at, wide, colour);
        if ((pieces & KillFlairs.Row.Frame) != 0) PlaceFrame(at, wide, colour);
        if ((pieces & KillFlairs.Row.Tick) != 0) PlaceTick(at, wide, colour);
        if ((pieces & KillFlairs.Row.Tag) != 0) PlaceTag(words, colour);
        if ((pieces & KillFlairs.Row.Veil) != 0) PlaceVeil();
        if ((pieces & KillFlairs.Row.Beam) != 0) PlaceBeam(colour);
        if ((pieces & KillFlairs.Row.Mark) != 0 && mark != null && mark.gameObject.activeSelf) PlaceMarkRing(colour);
        NameColour = colour;
        AccentColour = colour;
    }

    /// <summary>
    /// Makes every piece a flair can draw on this row, put away (KillFeedRow.Prewarm for CombatFeedHUD.Prewarm, behind the
    /// loading screen): the icon's layer and its material, the stamp, seal, chip with its banknote, strike, brackets,
    /// check, tag, veil, beam, the skull's ring, and the icon's afterimages, spark, ring, cut, strokes and crossing lines.
    /// Each is made once and kept, so the first special kill builds nothing mid-fight; nothing playing is touched.
    /// </summary>
    public void Prewarm()
    {
        Layer();
        MakeStamp(); MakeSeal(); MakeChip(); MakeStrike(); MakeFrame(); MakeTick(); MakeTag(); MakeVeil(); MakeBeam(); MakeMarkRing();
        MakeEchoes(); MakeSpark(); MakeRing(); MakeSlash(); MakeRays(); MakeCrosses();
    }

    /// <summary>Moves everything on by dt (real seconds).</summary>
    public void Tick(float dt)
    {
        SheenEnded = false;
        if (age > 50f) return;
        age += dt;
        bool any = false;
        var icon = play.Icon;
        if (icon != null && TickIcon(icon, age - icon.Delay)) any = true;
        var flair = play.Row;
        if (flair != null)
        {
            float t = age - flair.Delay, life = flair.Seconds, show = t < 0f ? 0f : Out(t, life);
            var pieces = flair.Row;
            if (t <= life + Fade) any = true;
            if (stamp != null && stamp.gameObject.activeSelf) Slammed(stamp, stampGroup, t, show, -8f);
            if (seal != null && seal.gameObject.activeSelf)
            {
                Slammed(seal, sealGroup, t, show, -14f);
                Specks(t - .12f);
            }
            if (chip != null && chip.gameObject.activeSelf)
            {
                float late = (pieces & KillFlairs.Row.Seal) != 0 ? .22f : .28f, s = Mathf.Clamp01((t - late) / .14f);
                chip.anchoredPosition = new Vector2(chip.anchoredPosition.x, ChipY + (1f - s) * 6f);
                chipGroup.alpha = s * show;
                if (paid > 0 && chipWords != null)
                {
                    // Under a seal the sum counts up; under a stamp it is there at once.
                    int shown = (pieces & KillFlairs.Row.Seal) != 0 ? Mathf.RoundToInt(paid * Mathf.Clamp01((t - late) / .32f)) : paid;
                    chipWords.text = shown.ToString();
                }
            }
            if (strike != null && strike.gameObject.activeSelf) strike.rectTransform.localScale = new Vector3(Ease.Out((t - .12f) / .13f), 1f, 1f);
            if (frame != null && frame.gameObject.activeSelf)
            {
                float s = Mathf.Clamp01(t / .13f);
                frame.localScale = Vector3.one * Mathf.LerpUnclamped(1.9f, 1f, Back(s));
                frameGroup.alpha = s * show;
            }
            if (tick != null && tick.gameObject.activeSelf)
            {
                float s = Mathf.Clamp01((t - .15f) / .15f);
                tickShort.rectTransform.localScale = new Vector3(Mathf.Clamp01(s * 2f), 1f, 1f);
                tickLong.rectTransform.localScale = new Vector3(Mathf.Clamp01(s * 2f - 1f), 1f, 1f);
                tickGroup.alpha = (t >= .15f ? 1f : 0f) * show;
            }
            if (tag != null && tag.gameObject.activeSelf)
            {
                float s = Mathf.Clamp01(t / .14f);
                tag.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.9f, 1f, Ease.Out(s));
                tag.alpha = s * show;
            }
            if (veil != null && veil.gameObject.activeSelf)
            {
                // The black lifts off the row from its right end in .18 s (a takedown out of the dark).
                veil.Cover(0f, t < 0f ? 1f : 1f - Ease.Out(t / .18f));
                if (t > .2f) veil.gameObject.SetActive(false);
            }
            if (beam != null && beam.gameObject.activeSelf)
            {
                beam.rectTransform.localScale = new Vector3(Ease.Out(t / .14f), 1f, 1f);
                SetAlpha(beam, t < 0f ? 0f : 1f - Mathf.Clamp01((t - .14f) / .18f));
            }
            if (markRing != null && markRing.gameObject.activeSelf)
            {
                float s = Mathf.Clamp01(t / .26f), pop = Mathf.Clamp01(t / .16f);
                markRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(.4f, 2.2f, Ease.Out(s));
                SetAlpha(markRing, t < 0f ? 0f : 1f - s);
                mark.rectTransform.localScale = Vector3.one * (t < 0f ? 1f : pop < .7f ? Mathf.Lerp(2f, .9f, Ease.Out(pop / .7f)) : Mathf.Lerp(.9f, 1f, (pop - .7f) / .3f));
            }
            NameTint = (pieces & KillFlairs.Row.Name) != 0 ? Mathf.Clamp01((t - .15f) / .12f) : 0f;
            AccentTint = (pieces & KillFlairs.Row.Accent) != 0 ? show : 0f;
            if ((pieces & KillFlairs.Row.Edge) != 0)
            {
                Edge = play.ColourOf(flair);
                EdgeStrength = t < 0f ? 0f : Mathf.Clamp01(t / (.15f * life)) * Mathf.Clamp01((life - t) / (.3f * life));
            }
            if ((pieces & KillFlairs.Row.Sheen) != 0)
            {
                bool was = Sheening;
                Sheening = t >= 0f && t <= .38f;
                SheenAt = Sheening ? 1.2f * (t / .38f) - .1f : -1f;
                Color tint = play.ColourOf(flair);
                tint.a = .75f;
                SheenTint = tint;
                SheenEnded = was && !Sheening;
            }
            if ((pieces & KillFlairs.Row.Flash) != 0 && plate != null)
            {
                // Washes the plate once, as the stamp or seal lands if there is one.
                float start = (pieces & (KillFlairs.Row.Stamp | KillFlairs.Row.Seal)) != 0 ? .1f : 0f;
                plate.Tint(play.ColourOf(flair), t < start ? 0f : .55f * (1f - Mathf.Clamp01((t - start) / .2f)));
                tinted = true;
            }
            if ((pieces & KillFlairs.Row.Trace) != 0 && plate != null)
            {
                // One lap round the plate's outline in .6 s, its slanted ends included, then the lit edge fades.
                Color colour = play.ColourOf(flair);
                colour.a = t < 0f ? 0f : 1f - Mathf.Clamp01((t - .6f) / .25f);
                plate.Trace(Ease.Out(t / .6f) * 1.0f, .35f, colour, 2.5f);
                traced = true;
            }
            Kick = (pieces & KillFlairs.Row.Kick) != 0 && t >= 0f && t <= KickSeconds
                ? new Vector2(-KickSize, 2f) * Mathf.Sin(Mathf.PI * t / KickSeconds) : Vector2.zero;
            // Room for what stands over the row (a stamp 20 px over its top with its tilt, a seal 12) and under it (a chip
            // 24), and the flair's own margin on top, while it plays.
            float over = 0f;
            if (stamp != null && stamp.gameObject.activeSelf) over = Mathf.Max(over, stamp.sizeDelta.y * show);
            if (seal != null && seal.gameObject.activeSelf) over = Mathf.Max(over, 12f * show);
            Above = over + flair.Above * show;
            Below = (chip != null && chip.gameObject.activeSelf ? 24f * show : 0f) + flair.Below * show;
        }
        if (!any) End();
    }

    /// <summary>The row goes away: nothing of its flair is left showing.</summary>
    public void Hide()
    {
        HideAll();
        End();
        Kick = Vector2.zero; Sheening = false; NameTint = 0f;
    }

    private void End()
    {
        age = 99f;
        Above = Below = 0f;
        EdgeStrength = 0f; AccentTint = 0f;
        if (tinted && plate != null) plate.Tint(Color.clear, 0f);
        if (traced && plate != null) plate.Trace(-1f, 0f, Color.clear, 0f);
        tinted = traced = false;
        if (mark != null) mark.rectTransform.localScale = Vector3.one;
    }

    // Each is made once, put away; its Place* sets it up and shows it. Prewarm makes them all ahead.
}
