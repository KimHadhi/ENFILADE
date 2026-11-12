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
public sealed class KillFlairFx
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
        bool money = (pieces & KillFlairs.Row.Money) != 0 && what.Mine;
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
            if (strike != null && strike.gameObject.activeSelf) strike.rectTransform.localScale = new Vector3(Ease((t - .12f) / .13f), 1f, 1f);
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
                tag.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.9f, 1f, Ease(s));
                tag.alpha = s * show;
            }
            if (veil != null && veil.gameObject.activeSelf)
            {
                // The black lifts off the row from its right end in .18 s (a takedown out of the dark).
                veil.Cover(0f, t < 0f ? 1f : 1f - Ease(t / .18f));
                if (t > .2f) veil.gameObject.SetActive(false);
            }
            if (beam != null && beam.gameObject.activeSelf)
            {
                beam.rectTransform.localScale = new Vector3(Ease(t / .14f), 1f, 1f);
                SetAlpha(beam, t < 0f ? 0f : 1f - Mathf.Clamp01((t - .14f) / .18f));
            }
            if (markRing != null && markRing.gameObject.activeSelf)
            {
                float s = Mathf.Clamp01(t / .26f), pop = Mathf.Clamp01(t / .16f);
                markRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(.4f, 2.2f, Ease(s));
                SetAlpha(markRing, t < 0f ? 0f : 1f - s);
                mark.rectTransform.localScale = Vector3.one * (t < 0f ? 1f : pop < .7f ? Mathf.Lerp(2f, .9f, Ease(pop / .7f)) : Mathf.Lerp(.9f, 1f, (pop - .7f) / .3f));
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
                plate.Trace(Ease(t / .6f) * 1.0f, .35f, colour, 2.5f);
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

    // ---- the icon ----

    private bool UsesLayer(KillFlairs.Icon kind) => kind != KillFlairs.Icon.None && kind != KillFlairs.Icon.Echo && kind != KillFlairs.Icon.Rays;

    private void PlaceIcon(KillFlairs.Flair icon, Color colour)
    {
        var kind = icon.Icon;
        if (kind == KillFlairs.Icon.None || weapon == null || weapon.sprite == null) return;
        if (UsesLayer(kind) && Layer())
        {
            // The pieces round the icon (a spark, a ring, a cut, crossing lines) flash the shape in their colour too.
            float mode = kind == KillFlairs.Icon.Fire ? 4f : kind == KillFlairs.Icon.Engulf ? 1f : kind == KillFlairs.Icon.Gild ? 2f
                : kind == KillFlairs.Icon.Trail ? 3f : 0f;
            layer.sprite = weapon.sprite;
            layer.gameObject.SetActive(true);
            material.SetFloat(ModeId, mode);
            material.SetColor(ColourId, kind == KillFlairs.Icon.Flash ? Color.white : colour);
            material.SetFloat(SeedId, seed);
            material.SetFloat(LickId, kind == KillFlairs.Icon.Trail ? .16f : .14f);
            material.SetVector(SpanId, WeaponIcons.Span(play.Picture, out float front));
            material.SetFloat(FrontId, front);
            material.SetFloat(HeatId, 0f);
        }
        var box = weapon.rectTransform.sizeDelta;
        switch (kind)
        {
            case KillFlairs.Icon.Echo:
                for (int i = 0; i < echoes.Length; i++)
                {
                    if (echoes[i] == null)
                    {
                        echoes[i] = FeelUI.Rect("Flair Echo", weapon.rectTransform).gameObject.AddComponent<Image>();
                        echoes[i].raycastTarget = false;
                        echoes[i].preserveAspect = true;
                        Stretch(echoes[i].rectTransform, 0f);
                    }
                    echoes[i].sprite = weapon.sprite;
                    echoes[i].color = Clear(colour);
                    echoes[i].gameObject.SetActive(true);
                }
                break;
            case KillFlairs.Icon.Spark:
                if (spark == null) spark = Raw("Flair Spark", weapon.rectTransform, CombatUIAssets.CriticalBurst);
                spark.rectTransform.sizeDelta = Vector2.one * 34f;
                spark.color = Color.white;
                spark.gameObject.SetActive(true);
                break;
            case KillFlairs.Icon.Ring:
                if (ring == null) ring = SpriteImage("Flair Ring", weapon.rectTransform, CombatUIAssets.Ring);
                ring.rectTransform.sizeDelta = Vector2.one * 40f;
                ring.color = colour;
                ring.gameObject.SetActive(true);
                break;
            case KillFlairs.Icon.Slash:
                if (slash == null)
                {
                    slash = Raw("Flair Slash", weapon.rectTransform, CombatUIAssets.BladeSlash);
                    slash.rectTransform.pivot = new Vector2(0f, .5f);
                }
                slash.rectTransform.sizeDelta = new Vector2(box.x * 1.25f, 10f);
                slash.rectTransform.anchoredPosition = new Vector2(-box.x * .62f, 0f);
                slash.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -16f);
                slash.color = colour;
                slash.gameObject.SetActive(true);
                break;
            case KillFlairs.Icon.Rays:
                for (int i = 0; i < rays.Length; i++)
                {
                    if (rays[i] == null)
                    {
                        rays[i] = FeelUI.Rect("Flair Ray", weapon.rectTransform).gameObject.AddComponent<Image>();
                        rays[i].raycastTarget = false;
                        rays[i].rectTransform.pivot = new Vector2(0f, .5f);
                        rays[i].rectTransform.sizeDelta = new Vector2(12f, 2.5f);
                    }
                    rays[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f + (i - 2.5f) * 36f);
                    rays[i].color = colour;
                    rays[i].gameObject.SetActive(true);
                }
                break;
            case KillFlairs.Icon.Cross:
                for (int i = 0; i < crosses.Length; i++)
                {
                    if (crosses[i] == null)
                    {
                        crosses[i] = FeelUI.Rect("Flair Cross", weapon.rectTransform).gameObject.AddComponent<Image>();
                        crosses[i].raycastTarget = false;
                    }
                    crosses[i].rectTransform.sizeDelta = new Vector2(box.x * .72f, 2.5f);
                    crosses[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 24f : -24f);
                    crosses[i].color = colour;
                    crosses[i].gameObject.SetActive(true);
                }
                break;
        }
    }

    // Moves the icon's effect on; whether anything of it still shows.
    private bool TickIcon(KillFlairs.Flair icon, float t)
    {
        var kind = icon.Icon;
        float life = icon.Seconds;
        bool showing = t <= life + Fade;
        if (layer != null && layer.gameObject.activeSelf)
        {
            float heat, reach = 1f;
            switch (kind)
            {
                case KillFlairs.Icon.Fire: heat = Out(t, life); reach = Ease(t / FireReach); break;
                case KillFlairs.Icon.Engulf: heat = Out(t, life); reach = Ease(t / EngulfReach); break;
                case KillFlairs.Icon.Gild: heat = Out(t, life); reach = Ease(t / GildReach); break;
                case KillFlairs.Icon.Trail: heat = Mathf.Clamp01(t / .06f) * Out(t, life); break;
                case KillFlairs.Icon.Flash: heat = t < 0f ? 0f : 1f - Mathf.Clamp01(t / .2f); break;
                // A tint, and the flash under a spark, a ring, a cut or crossing lines: full at once, gone over its seconds.
                default: heat = t < 0f ? 0f : 1f - Mathf.Clamp01(t / Mathf.Min(life, .4f)); break;
            }
            material.SetFloat(HeatId, heat);
            material.SetFloat(ProgressId, reach);
            if (t > life + Fade) layer.gameObject.SetActive(false);
        }
        switch (kind)
        {
            case KillFlairs.Icon.Echo:
                for (int i = 0; i < echoes.Length; i++)
                {
                    if (echoes[i] == null || !echoes[i].gameObject.activeSelf) continue;
                    // Two quick draws: each slides in from the left onto the gun and is gone as it lands.
                    float s = Mathf.Clamp01((t - i * .09f) / .26f);
                    echoes[i].rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-22f - i * 12f, 0f, Ease(s / .7f)), 0f);
                    var c = echoes[i].color;
                    c.a = t < i * .09f ? 0f : s < .7f ? .9f * (s / .7f) : .9f * (1f - (s - .7f) / .3f);
                    echoes[i].color = c;
                    if (s >= 1f) echoes[i].gameObject.SetActive(false);
                }
                break;
            case KillFlairs.Icon.Spark:
                if (spark != null && spark.gameObject.activeSelf)
                {
                    float s = Mathf.Clamp01(t / .17f);
                    spark.rectTransform.localScale = Vector3.one * (s < .5f ? Mathf.Lerp(.2f, 1.4f, s / .5f) : Mathf.Lerp(1.4f, 1.7f, (s - .5f) / .5f));
                    spark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 40f * s);
                    SetAlpha(spark, t < 0f ? 0f : s < .5f ? 1f : 1f - (s - .5f) / .5f);
                    if (s >= 1f) spark.gameObject.SetActive(false);
                }
                break;
            case KillFlairs.Icon.Ring:
                if (ring != null && ring.gameObject.activeSelf)
                {
                    float s = Mathf.Clamp01(t / .28f);
                    ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(.3f, 2.3f, Ease(s));
                    SetAlpha(ring, t < 0f ? 0f : 1f - s);
                    if (s >= 1f) ring.gameObject.SetActive(false);
                }
                break;
            case KillFlairs.Icon.Slash:
                if (slash != null && slash.gameObject.activeSelf)
                {
                    slash.rectTransform.localScale = new Vector3(Ease(t / .13f), 1f, 1f);
                    SetAlpha(slash, t < 0f ? 0f : 1f - Mathf.Clamp01((t - .13f) / .13f));
                    if (t > .3f) slash.gameObject.SetActive(false);
                }
                break;
            case KillFlairs.Icon.Rays:
                for (int i = 0; i < rays.Length; i++)
                {
                    if (rays[i] == null || !rays[i].gameObject.activeSelf) continue;
                    float s = Mathf.Clamp01(t / .17f), angle = (90f + (i - 2.5f) * 36f) * Mathf.Deg2Rad;
                    rays[i].rectTransform.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Lerp(5f, 15f, Ease(s));
                    rays[i].rectTransform.localScale = new Vector3(Mathf.Lerp(.3f, 1f, Ease(s)), 1f, 1f);
                    SetAlpha(rays[i], t < 0f ? 0f : 1f - s);
                    if (s >= 1f) rays[i].gameObject.SetActive(false);
                }
                break;
            case KillFlairs.Icon.Cross:
                for (int i = 0; i < crosses.Length; i++)
                {
                    if (crosses[i] == null || !crosses[i].gameObject.activeSelf) continue;
                    float s = Mathf.Clamp01((t - i * .08f) / .18f);
                    crosses[i].rectTransform.localScale = new Vector3(Ease(s), 1f, 1f);
                    SetAlpha(crosses[i], t < i * .08f ? 0f : 1f - Mathf.Clamp01((t - i * .08f - .18f) / .14f));
                    if (t > i * .08f + .34f) crosses[i].gameObject.SetActive(false);
                }
                break;
        }
        return showing;
    }

    // ---- pieces ----

    private float ChipY => -KillFeedRow.Height * .5f - 12f;

    private bool Layer()
    {
        if (layer != null) return true;
        if (shader == null) shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null) return false;
        material = new Material(shader) { name = "Kill Flair (Runtime)", hideFlags = HideFlags.DontSave };
        layer = FeelUI.Rect("Flair", weapon.rectTransform).gameObject.AddComponent<Image>();
        Stretch(layer.rectTransform, 0f);
        layer.preserveAspect = true;
        layer.raycastTarget = false;
        layer.material = material;
        layer.gameObject.AddComponent<OwnedMaterial>().Own(material);
        return true;
    }

    private void PlaceStamp(string words, Color colour)
    {
        if (stamp == null)
        {
            stamp = FeelUI.Rect("Flair Stamp", row);
            stamp.anchorMin = stamp.anchorMax = new Vector2(1f, .5f);
            stampGroup = Group(stamp);
            stampEdge = Fill("Edge", stamp, 0f);
            stampFill = Fill("Fill", stamp, 2.5f);
            stampWords = FeelUI.ScoreLabel("Words", stamp, 15f, TextAlignmentOptions.Center);
            Stretch(stampWords.rectTransform, 0f);
        }
        stampWords.text = words;
        stampWords.color = colour;
        stampEdge.color = colour;
        stampFill.color = Night;
        float width = stampWords.GetPreferredValues(words).x + 18f, height = 26f;
        stamp.sizeDelta = new Vector2(width, height);
        // Over the row's end, a little above it, clear of the names under it; the feed makes the room (Above).
        stamp.anchoredPosition = new Vector2(-14f - width * .5f, KillFeedRow.Height * .5f + height * .5f - 6f);
        stamp.SetAsLastSibling();
        stamp.gameObject.SetActive(true);
        stampGroup.alpha = 0f;
    }

    private void PlaceSeal(string words, Color colour)
    {
        if (seal == null)
        {
            seal = FeelUI.Rect("Flair Seal", row);
            seal.anchorMin = seal.anchorMax = new Vector2(1f, .5f);
            sealGroup = Group(seal);
            sealFill = SpriteImage("Fill", seal, CombatUIAssets.Disc);
            Stretch(sealFill.rectTransform, 0f);
            sealRing = SpriteImage("Ring", seal, CombatUIAssets.Ring);
            Stretch(sealRing.rectTransform, 0f);
            sealWords = FeelUI.ScoreLabel("Words", seal, 11f, TextAlignmentOptions.Center);
            sealWords.lineSpacing = -18f;
            Stretch(sealWords.rectTransform, 4f);
            for (int i = 0; i < specks.Length; i++)
            {
                specks[i] = SpriteImage("Speck", seal, CombatUIAssets.Disc);
                specks[i].rectTransform.sizeDelta = Vector2.one * 4f;
            }
        }
        // Two lines, broken at the space ("계약 완료" → 계약 / 완료).
        sealWords.text = words.Replace(' ', '\n');
        sealWords.color = colour;
        sealRing.color = colour;
        sealFill.color = Night;
        for (int i = 0; i < specks.Length; i++)
        {
            specks[i].color = colour;
            float angle = Random.value * Mathf.PI * 2f, reach = 26f + Random.value * 16f;
            speckWay[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach;
        }
        seal.sizeDelta = Vector2.one * 46f;
        // On the row's end, over its end bar, standing a little out past it.
        seal.anchoredPosition = new Vector2(8f, 5f);
        seal.SetAsLastSibling();
        seal.gameObject.SetActive(true);
        sealGroup.alpha = 0f;
    }

    private void PlaceChip(bool money, string words, Color colour, bool besideSeal)
    {
        if (chip == null)
        {
            chip = FeelUI.Rect("Flair Chip", row);
            chip.anchorMin = chip.anchorMax = new Vector2(1f, .5f);
            chipGroup = Group(chip);
            chipPlate = Fill("Plate", chip, 0f);
            chipMoney = MoneyIcon.Make(chip, 16f);
            chipMoney.rectTransform.anchorMin = chipMoney.rectTransform.anchorMax = new Vector2(0f, .5f);
            chipWords = FeelUI.ScoreLabel("Words", chip, 13f, TextAlignmentOptions.MidlineLeft);
            chipWords.rectTransform.anchorMin = chipWords.rectTransform.anchorMax = new Vector2(0f, .5f);
            chipWords.rectTransform.pivot = new Vector2(0f, .5f);
        }
        chipMoney.gameObject.SetActive(money);
        chipWords.text = money ? paid.ToString() : words;
        chipWords.color = colour;
        chipPlate.color = Night;
        float text = chipWords.GetPreferredValues(money ? Mathf.Max(paid, 10).ToString() : words).x;
        float left = money ? 24f : 8f, width = left + text + 8f, height = 20f;
        chip.sizeDelta = new Vector2(width, height);
        chipMoney.rectTransform.anchoredPosition = new Vector2(13f, 0f);
        chipWords.rectTransform.anchoredPosition = new Vector2(left, 0f);
        chipWords.rectTransform.sizeDelta = new Vector2(text + 4f, height);
        // Under the row's end; beside a seal, left of it so the two never cover each other; the feed makes the room (Below).
        chip.anchoredPosition = new Vector2((besideSeal ? -40f : -6f) - width * .5f, ChipY);
        chip.SetAsLastSibling();
        chip.gameObject.SetActive(true);
        chipGroup.alpha = 0f;
    }

    private void PlaceStrike(Vector2 at, float wide, Color colour)
    {
        if (strike == null)
        {
            strike = FeelUI.Rect("Flair Strike", row).gameObject.AddComponent<Image>();
            strike.raycastTarget = false;
            var rect = strike.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, .5f);
            rect.pivot = new Vector2(0f, .5f);
        }
        strike.color = colour;
        strike.rectTransform.sizeDelta = new Vector2(wide + 2f, 2.5f);
        strike.rectTransform.anchoredPosition = new Vector2(at.x - wide * .5f - 1f, at.y);
        strike.rectTransform.localScale = new Vector3(0f, 1f, 1f);
        strike.rectTransform.SetAsLastSibling();
        strike.gameObject.SetActive(true);
    }

    private void PlaceFrame(Vector2 at, float wide, Color colour)
    {
        if (frame == null)
        {
            frame = FeelUI.Rect("Flair Frame", row);
            frame.anchorMin = frame.anchorMax = new Vector2(1f, .5f);
            frameGroup = Group(frame);
            for (int i = 0; i < corners.Length; i++)
            {
                corners[i] = FeelUI.Rect("Corner", frame).gameObject.AddComponent<Image>();
                corners[i].raycastTarget = false;
            }
        }
        float w = wide + 14f, h = KillFeedRow.Height - 6f, arm = 8f, line = 2.5f;
        frame.sizeDelta = new Vector2(w, h);
        frame.anchoredPosition = at;
        // The brackets lean as the plate's ends do (KillRowPlate: its top 9 px right of its bottom over the row's 32), so
        // every outline a flair draws keeps the row's own shape.
        float lean = PlateShear / KillFeedRow.Height, tilt = -Mathf.Atan(lean) * Mathf.Rad2Deg;
        for (int i = 0; i < 4; i++)
        {
            float sx = i % 2 == 0 ? -1f : 1f, sy = i < 2 ? 1f : -1f;
            float cx = sx * w * .5f + sy * lean * h * .5f, cy = sy * h * .5f;
            var across = corners[i * 2].rectTransform;
            across.sizeDelta = new Vector2(arm, line);
            across.anchoredPosition = new Vector2(cx - sx * arm * .5f, cy - sy * line * .5f);
            across.localRotation = Quaternion.identity;
            var down = corners[i * 2 + 1].rectTransform;
            down.sizeDelta = new Vector2(line, arm);
            down.anchoredPosition = new Vector2(cx - sx * line * .5f - sy * lean * arm * .5f, cy - sy * arm * .5f);
            down.localRotation = Quaternion.Euler(0f, 0f, tilt);
            corners[i * 2].color = corners[i * 2 + 1].color = colour;
        }
        frame.SetAsLastSibling();
        frame.gameObject.SetActive(true);
        frameGroup.alpha = 0f;
    }

    private void PlaceTick(Vector2 at, float wide, Color colour)
    {
        if (tick == null)
        {
            tick = FeelUI.Rect("Flair Tick", row);
            tick.anchorMin = tick.anchorMax = new Vector2(1f, .5f);
            tickGroup = Group(tick);
            tickShort = Stroke("Short", tick);
            tickLong = Stroke("Long", tick);
        }
        // A check across the one killed: a short stroke down, then a long one up to the right.
        float span = Mathf.Max(24f, wide * .7f);
        tick.anchoredPosition = at;
        tickShort.color = tickLong.color = colour;
        tickShort.rectTransform.sizeDelta = new Vector2(span * .32f, 3.5f);
        tickShort.rectTransform.anchoredPosition = new Vector2(-span * .42f, 2f);
        tickShort.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -38f);
        tickLong.rectTransform.sizeDelta = new Vector2(span * .78f, 3.5f);
        tickLong.rectTransform.anchoredPosition = new Vector2(-span * .2f, -5f);
        tickLong.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 30f);
        tick.SetAsLastSibling();
        tick.gameObject.SetActive(true);
        tickGroup.alpha = 0f;
    }

    private void PlaceTag(string words, Color colour)
    {
        if (tag == null)
        {
            tag = FeelUI.ScoreLabel("Flair Tag", weapon.rectTransform, 11f, TextAlignmentOptions.Center);
            var rect = tag.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.sizeDelta = new Vector2(90f, 14f);
            rect.anchoredPosition = new Vector2(0f, 6f);
            tag.fontStyle = FontStyles.Italic;
            tag.characterSpacing = 12f;
        }
        tag.text = words;
        tag.color = colour;
        tag.alpha = 0f;
        tag.gameObject.SetActive(true);
    }

    private void PlaceVeil()
    {
        if (veil == null)
        {
            veil = FeelUI.Rect("Flair Veil", row).gameObject.AddComponent<KillRowCover>();
            veil.raycastTarget = false;
            Stretch(veil.rectTransform, 0f);
            veil.color = new Color(0f, 0f, 0f, .94f);
        }
        veil.Cover(0f, 1f);
        veil.rectTransform.SetAsLastSibling();
        veil.gameObject.SetActive(true);
    }

    private void PlaceBeam(Color colour)
    {
        if (beam == null)
        {
            beam = FeelUI.Rect("Flair Beam", row).gameObject.AddComponent<Image>();
            beam.raycastTarget = false;
            var rect = beam.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, .5f);
            rect.pivot = new Vector2(0f, .5f);
        }
        // From the muzzle (where the drawing ends on the icon's canvas, WeaponIconSpans.csv) through the one killed and
        // 10 px past the row's end.
        var box = weapon.rectTransform;
        float muzzle = WeaponIcons.Span(play.Picture, out _).y;
        float from = box.anchoredPosition.x + box.sizeDelta.x * (muzzle - .5f);
        beam.rectTransform.anchoredPosition = new Vector2(from, 2f);
        beam.rectTransform.sizeDelta = new Vector2(10f - from, 2f);
        beam.rectTransform.localScale = new Vector3(0f, 1f, 1f);
        beam.color = Color.Lerp(Color.white, colour, .35f);
        beam.rectTransform.SetAsLastSibling();
        beam.gameObject.SetActive(true);
    }

    private void PlaceMarkRing(Color colour)
    {
        if (markRing == null)
        {
            markRing = SpriteImage("Flair Mark Ring", mark.rectTransform, CombatUIAssets.Ring);
            Stretch(markRing.rectTransform, -3f);
        }
        markRing.color = colour;
        markRing.gameObject.SetActive(true);
    }

    // ---- motion ----

    private static void Slammed(RectTransform piece, CanvasGroup group, float t, float show, float tilt)
    {
        float s = Mathf.Clamp01(t / Slam);
        // 2.3x and flat, down past its size, and back: a slam, not a fade.
        float scale = s < .7f ? Mathf.Lerp(2.3f, .92f, Ease(s / .7f)) : Mathf.Lerp(.92f, 1f, (s - .7f) / .3f);
        piece.localScale = Vector3.one * scale;
        piece.localRotation = Quaternion.Euler(0f, 0f, tilt);
        group.alpha = (t >= 0f ? Mathf.Clamp01(s / .4f) : 0f) * show;
    }

    private void Specks(float t)
    {
        for (int i = 0; i < specks.Length; i++)
        {
            var speck = specks[i];
            if (speck == null) continue;
            float s = Mathf.Clamp01(t / .26f);
            speck.rectTransform.anchoredPosition = speckWay[i] * Ease(s);
            var c = speck.color;
            c.a = t < 0f ? 0f : 1f - s;
            speck.color = c;
        }
    }

    // Full until the flair's seconds are up, then gone over Fade.
    private static float Out(float t, float life) => t < 0f ? 0f : 1f - Mathf.Clamp01((t - life) / Fade);
    private static float Ease(float s) { s = Mathf.Clamp01(s); return 1f - (1f - s) * (1f - s) * (1f - s); }
    private static float Back(float s) { s = Mathf.Clamp01(s); return 1f + 2.2f * Mathf.Pow(s - 1f, 3f) + 1.2f * Mathf.Pow(s - 1f, 2f); }
    private static Color Clear(Color c) { c.a = 0f; return c; }

    private void HideAll()
    {
        if (layer != null) layer.gameObject.SetActive(false);
        foreach (var piece in new Component[] { stamp, seal, chip, strike, frame, tick, tag, veil, beam, markRing, spark, ring, slash })
            if (piece != null) piece.gameObject.SetActive(false);
        foreach (var group in new[] { echoes, rays, crosses })
            foreach (var piece in group)
                if (piece != null) piece.gameObject.SetActive(false);
        if (mark != null) mark.rectTransform.localScale = Vector3.one;
    }

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        var c = graphic.color;
        c.a = alpha;
        graphic.color = c;
    }

    private static CanvasGroup Group(RectTransform piece)
    {
        var group = piece.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        return group;
    }

    private static Image Fill(string name, RectTransform parent, float inset)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        Stretch(image.rectTransform, inset);
        return image;
    }

    private static Image SpriteImage(string name, RectTransform parent, Sprite sprite)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    private static RawImage Raw(string name, RectTransform parent, Texture texture)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        return image;
    }

    private static Image Stroke(string name, RectTransform parent)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.rectTransform.pivot = new Vector2(0f, .5f);
        return image;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * inset; rect.offsetMax = -Vector2.one * inset;
    }

    /// <summary>Lets a row's flair material go with the row.</summary>
    private sealed class OwnedMaterial : MonoBehaviour
    {
        private Material owned;
        public void Own(Material material) => owned = material;
        private void OnDestroy() { if (owned != null) Destroy(owned); }
    }
}
