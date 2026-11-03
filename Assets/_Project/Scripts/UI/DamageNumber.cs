using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One damage number: a thin bar on the side facing its target, the number, and one mark after it (a burst for a
/// critical hit, a return arrow for a counter, a skull for the finishing blow). It is laid out from its near edge, so it
/// pops and grows away from the target instead of back over it. Add() folds another hit in: the digits count up, the
/// number pops by the hit's size, and on a running total a critical or counter colours it only for a moment.
/// DamagePopupManager decides where it stands and when hits fold together. Colours, font and motion come from
/// Resources/Combat/Feedback (Damage popups), the rest from Hud.csv's Damage row (DamageNumberLayout).
/// </summary>
public sealed class DamageNumber
{
    private enum Mark { None, Critical, Counter, Kill }

    private const float FadeIn = .06f, SlideSeconds = .14f, MarkFade = .18f, LiftSpeed = 18f;
    private static readonly Color DarkOutline = new Color(.16f, .04f, 0f, .95f);

    private readonly RectTransform root, content;
    private readonly CanvasGroup group;
    private readonly Image bar, glow;
    private readonly RawImage mark, slash, cut;
    private readonly TextMeshProUGUI label, shade;

    private float total, shown, rollFrom, rollAge, shotAmount;
    private float slideAge, popAge, popPeak, hold, leaveAge = -1f, markAge, cutAge, killAge, lift, liftTarget, width;
    private bool environmental, fadeMarks, punch, counterCut;
    private Mark markKind;
    // What the parts were last laid out for; any change lays them out again.
    private int laidValue = -1, laidSide;
    private Mark laidMark;
    private bool laidKilled, laidCut;

    /// <summary>The finishing blow landed in this number; it takes no more hits.</summary>
    public bool Killed { get; private set; }
    public float SinceHit { get; private set; }
    public bool Leaving => leaveAge >= 0f;

    public DamageNumber(Transform parent, CombatFeedbackSettings settings)
    {
        root = Rect("Damage Number", parent);
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        content = Rect("Content", root);

        glow = Rect("Kill Glow", content).gameObject.AddComponent<Image>();
        glow.sprite = CombatUIAssets.SoftGlow;
        glow.type = Image.Type.Sliced;
        glow.raycastTarget = false;
        slash = Rect("Kill Slash", content).gameObject.AddComponent<RawImage>();
        slash.texture = CombatUIAssets.SlashLine;
        slash.raycastTarget = false;
        cut = Rect("Counter Cut", content).gameObject.AddComponent<RawImage>();
        cut.texture = CombatUIAssets.BladeSlash;
        cut.raycastTarget = false;
        cut.rectTransform.pivot = new Vector2(1f, .5f); // grows from its top-right end toward the bottom-left
        bar = Rect("Bar", content).gameObject.AddComponent<Image>();
        bar.raycastTarget = false;

        var font = CombatUIAssets.ResolveFont(settings.damageFont);
        shade = Label("Damage Shadow", content, font); // behind the number, finishing blows only
        label = Label("Damage", content, font);
        var outline = CombatUIAssets.Outlined(label.font, DarkOutline, .24f);
        if (outline != null) { label.fontSharedMaterial = outline; shade.fontSharedMaterial = outline; }
        mark = Rect("Mark", content).gameObject.AddComponent<RawImage>();
        mark.raycastTarget = false;
        root.gameObject.SetActive(false);
    }

    /// <summary>Starts the number with its first hit. fadeMarks: a running total, whose criticals tint it only briefly.</summary>
    public void Begin(float amount, bool environmental, bool critical, bool counter, bool kill, bool fadeMarks,
        DamageNumberLayout layout, CombatFeedbackSettings settings)
    {
        this.environmental = environmental;
        this.fadeMarks = fadeMarks;
        total = shown = rollFrom = amount;
        rollAge = float.PositiveInfinity;
        slideAge = 0f; leaveAge = -1f; lift = liftTarget = 0f;
        Killed = counterCut = false;
        markKind = Mark.None;
        laidValue = -1; laidSide = 0;
        group.alpha = 0f;
        root.gameObject.SetActive(true);
        root.SetAsLastSibling();
        Hit(amount, critical, counter, kill, true, layout, settings);
    }

    /// <summary>Folds another hit into the number: the total counts up to include it.</summary>
    public void Add(float amount, bool critical, bool counter, bool kill, DamageNumberLayout layout, CombatFeedbackSettings settings)
    {
        rollFrom = shown;
        total += amount;
        rollAge = 0f;
        Hit(amount, critical, counter, kill, false, layout, settings);
    }

    /// <summary>Starts leaving now (rise and fade) even though its time is not up.</summary>
    public void Leave()
    {
        if (leaveAge < 0f) leaveAge = 0f;
    }

    public void Release() => root.gameObject.SetActive(false);
    public void Hide() => group.alpha = 0f;
    public void SetLift(float target) => liftTarget = target;
    public bool Finished(CombatFeedbackSettings settings) => leaveAge >= Mathf.Max(.01f, settings.popupFadeSeconds);
    /// <summary>Settled height (canvas px at full size), for stacking a target's numbers.</summary>
    public float Height(CombatFeedbackSettings settings) => FontSize(settings) * 1.1f * Settled(settings);
    /// <summary>Settled width (canvas px at full size), for keeping the numbers on screen.</summary>
    public float Width(CombatFeedbackSettings settings) => width * Settled(settings);

    public void Tick(float dt, DamageNumberLayout layout, CombatFeedbackSettings settings)
    {
        SinceHit += dt; slideAge += dt; popAge += dt; rollAge += dt; markAge += dt; cutAge += dt; killAge += dt;
        if (leaveAge >= 0f) leaveAge += dt;
        else if (SinceHit >= hold) leaveAge = 0f;
        lift = Mathf.Lerp(lift, liftTarget, 1f - Mathf.Exp(-LiftSpeed * dt));
        // On a running total a critical or counter tints it for markHold, then lets go.
        if (fadeMarks && (markKind == Mark.Critical || markKind == Mark.Counter) && markAge >= layout.markHold + MarkFade)
            markKind = Mark.None;
        shown = layout.roll > 0f && rollAge < layout.roll
            ? Mathf.Lerp(rollFrom, total, 1f - Mathf.Pow(1f - rollAge / layout.roll, 3f))
            : total;
    }

    /// <summary>Puts the number's near edge on spot (the target's side; side +1 grows right, -1 left), raised by its place in the column.</summary>
    public void Draw(Vector2 spot, int side, float far, DamageNumberLayout layout, CombatFeedbackSettings settings)
    {
        if (side == 0) side = 1;
        int value = Mathf.Max(1, Mathf.RoundToInt(shown));
        if (side != laidSide && laidSide != 0) slideAge = 0f; // moved to the body's other side: slide out again there
        if (side != laidSide || value != laidValue || markKind != laidMark || Killed != laidKilled || counterCut != laidCut)
            Lay(value, side, layout, settings);

        float size = FontSize(settings), settled = Settled(settings);
        bool finisher = Killed && !environmental;
        float scale = Pop(settings) * settled * far;
        content.localScale = Vector3.one * scale;

        Color normal = environmental ? settings.environmentDamageColor
            : finisher ? settings.deathBlowColor : settings.normalDamageColor;
        Color tint = normal;
        if (!Killed && (markKind == Mark.Critical || markKind == Mark.Counter))
        {
            Color marked = markKind == Mark.Critical ? settings.criticalColor : settings.counterColor;
            tint = Color.Lerp(marked, normal, fadeMarks ? Mathf.Clamp01((markAge - layout.markHold) / MarkFade) : 0f);
        }
        // Critical, counter and finishing blow start white-hot.
        if (punch && settings.punchFlashSeconds > 0f) tint = Color.Lerp(tint, Color.white, Mathf.Clamp01(1f - popAge / settings.punchFlashSeconds));
        label.color = tint;
        Color barTint = tint; barTint.a *= .9f;
        bar.color = barTint;

        if (markKind != Mark.None)
        {
            Color markTint = markKind == Mark.Kill ? settings.killIconColor : tint;
            if (fadeMarks && markKind != Mark.Kill) markTint.a *= 1f - Mathf.Clamp01((markAge - layout.markHold) / MarkFade);
            mark.color = markTint;
            // The mark pops in with a small twist a beat after the number.
            float iconT = 1f - Mathf.Pow(1f - Mathf.Clamp01(markAge / Mathf.Max(.01f, settings.punchIconPopSeconds)), 3f);
            mark.rectTransform.localScale = Vector3.one * (1.6f - .6f * iconT);
            mark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, side * (1f - iconT) * 25f);
        }
        if (counterCut)
            cut.rectTransform.localScale = new Vector3(1f - Mathf.Pow(1f - Mathf.Clamp01(cutAge / .1f), 3f), 1f, 1f);

        Vector2 shake = Vector2.zero;
        if (finisher)
        {
            // The light flares once on impact and then holds; the slash sweeps out; the number rattles and settles.
            float flare = 1f - Mathf.Clamp01(killAge / .2f);
            Color light = settings.deathBlowGlowColor;
            light.a *= .5f + .4f * flare;
            glow.color = light;
            glow.rectTransform.localScale = Vector3.one * (1f + flare * .25f);
            slash.rectTransform.localScale = new Vector3(1f - Mathf.Pow(1f - Mathf.Clamp01(killAge / .12f), 3f), 1f, 1f);
            if (killAge < settings.deathBlowShakeSeconds)
            {
                float decay = 1f - killAge / settings.deathBlowShakeSeconds;
                shake = new Vector2(Mathf.Sin(killAge * 97f), Mathf.Cos(killAge * 71f)) * (settings.deathBlowShake * decay * decay);
            }
        }

        // It slides out from the body as it appears, and rises as it leaves.
        float slid = 1f - Mathf.Pow(1f - Mathf.Clamp01(slideAge / SlideSeconds), 3f);
        float fade = Mathf.Max(.01f, settings.popupFadeSeconds);
        float leave = leaveAge >= 0f ? Mathf.Clamp01(leaveAge / fade) : 0f;
        float rise = settings.popupRise * (1f - (1f - leave) * (1f - leave));
        Vector2 at = spot + new Vector2(-side * layout.slide * (1f - slid) * far, lift + rise) + shake;
        root.anchoredPosition = layout.ClearOfCrosshair(at, side, size * .5f * scale);
        group.alpha = Mathf.Clamp01(slideAge / FadeIn) * (1f - leave);
    }

    private void Hit(float amount, bool critical, bool counter, bool kill, bool first, DamageNumberLayout layout, CombatFeedbackSettings settings)
    {
        // Hits of one shot (a shotgun's pellets) pop as one: by their sum, and as hard as the hardest of them.
        bool sameShot = !first && SinceHit <= layout.sameShot;
        shotAmount = sameShot ? shotAmount + amount : amount;
        SinceHit = 0f;
        popAge = 0f;
        Vector2 range = settings.popDamageRange;
        float weight = Mathf.InverseLerp(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y), shotAmount);
        weight = 1f - (1f - weight) * (1f - weight); // early damage steps read clearly, huge hits saturate
        float peak = Mathf.Max(1f, Mathf.Lerp(settings.popStartScale.x, settings.popStartScale.y, weight));
        bool hard = !environmental && (critical || counter || kill);
        popPeak = sameShot ? Mathf.Max(popPeak, peak) : peak;
        punch = sameShot ? punch || hard : hard;

        // One mark at a time: the finishing blow's skull is final, otherwise the latest counter or critical.
        if (kill)
        {
            Killed = true;
            killAge = 0f;
            markKind = Mark.Kill;
            markAge = 0f;
        }
        else if (markKind != Mark.Kill && !environmental && counter)
        {
            markKind = Mark.Counter;
            markAge = 0f;
            counterCut = true;
            cutAge = 0f;
        }
        else if (markKind != Mark.Kill && !environmental && critical && !(sameShot && markKind == Mark.Counter))
        {
            markKind = Mark.Critical;
            markAge = 0f;
        }
        hold = Killed ? settings.deathBlowLifetime : settings.popupLifetime;
    }

    // Big for an instant, then a fast ease back. Critical, counter and finishing blow punch instead: freeze at the peak,
    // snap back, dip slightly and settle.
    private float Pop(CombatFeedbackSettings settings)
    {
        if (!punch)
        {
            float shrink = Mathf.Clamp01(popAge / Mathf.Max(.01f, settings.popShrinkSeconds));
            return Mathf.Lerp(popPeak, 1f, 1f - Mathf.Pow(1f - shrink, 3f));
        }
        float peak = popPeak * (Killed ? settings.deathBlowStartBoost : settings.punchStartBoost);
        float freeze = Killed ? settings.deathBlowHoldSeconds : settings.punchHoldSeconds;
        if (popAge < freeze) return peak;
        float snap = Mathf.Max(.01f, settings.punchShrinkSeconds);
        float t = Mathf.Clamp01((popAge - freeze) / snap);
        float pop = Mathf.Lerp(peak, 1f, 1f - Mathf.Pow(1f - t, 3f));
        if (t >= 1f) pop -= settings.punchSettleDip * Mathf.Sin(Mathf.Clamp01((popAge - freeze - snap) / .12f) * Mathf.PI);
        return pop;
    }

    // A finishing blow settles clearly larger; when each hit shows its own, criticals and counters settle a little larger.
    private float Settled(CombatFeedbackSettings settings)
    {
        if (Killed && !environmental) return settings.deathBlowSettledScale;
        return !fadeMarks && (markKind == Mark.Critical || markKind == Mark.Counter) ? settings.punchSettledScale : 1f;
    }

    private static float FontSize(CombatFeedbackSettings settings) => Mathf.Max(8f, settings.damageFontSize);

    // Lays the parts out from the near edge outward (u), mirrored for numbers left of their target: bar, number, mark.
    private void Lay(int value, int side, DamageNumberLayout layout, CombatFeedbackSettings settings)
    {
        laidValue = value; laidSide = side; laidMark = markKind; laidKilled = Killed; laidCut = counterCut;
        float size = FontSize(settings);
        string text = value.ToString();
        bool finisher = Killed && !environmental;
        label.fontSize = size;
        label.text = text;
        Vector2 measured = label.GetPreferredValues(text);

        float u = 0f;
        bar.gameObject.SetActive(layout.bar > 0f);
        if (layout.bar > 0f)
        {
            Place(bar.rectTransform, side, layout.bar * .5f, layout.bar, size * .9f);
            u = layout.bar + size * .22f;
        }
        float centre = u + measured.x * .5f;
        Place(label.rectTransform, side, centre, measured.x + 6f, size * 1.3f);
        u += measured.x;

        // Finishing blow: orange digits over a crimson copy offset down-right, a small glow and a slash line behind.
        shade.gameObject.SetActive(finisher);
        glow.gameObject.SetActive(finisher);
        slash.gameObject.SetActive(finisher);
        if (finisher)
        {
            shade.fontSize = size;
            shade.text = text;
            shade.color = settings.deathBlowShadowColor;
            Place(shade.rectTransform, side, centre, measured.x + 6f, size * 1.3f);
            shade.rectTransform.anchoredPosition += new Vector2(size * .06f, -size * .06f);
            Place(glow.rectTransform, side, centre, measured.x + size * 1.3f, size * 1.6f);
            Place(slash.rectTransform, side, centre, measured.x + size * 1.8f, size * .12f);
            slash.rectTransform.anchoredPosition += new Vector2(0f, -size * .08f);
            slash.color = settings.deathBlowSlashColor;
        }

        mark.gameObject.SetActive(markKind != Mark.None);
        if (markKind != Mark.None)
        {
            float icon = size * .9f, gap = size * .14f;
            mark.texture = markKind == Mark.Kill ? (settings.killIcon != null ? settings.killIcon : CombatUIAssets.Skull)
                : markKind == Mark.Counter ? (settings.counterIcon != null ? settings.counterIcon : CombatUIAssets.CounterArrow)
                : settings.criticalIcon != null ? settings.criticalIcon : CombatUIAssets.CriticalBurst;
            Place(mark.rectTransform, side, u + gap + icon * .5f, icon, icon);
            u += gap + icon;
        }
        width = u;

        // Counter: a sword cut behind the number from top-right to bottom-left, light but carrying the number's colour.
        cut.gameObject.SetActive(counterCut);
        if (counterCut)
        {
            Color.RGBToHSV(finisher ? settings.deathBlowColor : settings.counterColor, out float hue, out float saturation, out float brightness);
            // Pale colours are enriched first, otherwise mixing them into white still reads as plain white.
            Color rich = Color.HSVToRGB(hue, Mathf.Max(saturation, .6f), Mathf.Max(brightness, .95f));
            Color tint = Color.Lerp(settings.counterSlashColor, rich, settings.counterSlashTint);
            tint.a = settings.counterSlashColor.a;
            cut.color = tint;
            float length = measured.x + size * 1.6f, angle = settings.counterSlashAngle * Mathf.Deg2Rad;
            var rect = cut.rectTransform;
            rect.sizeDelta = new Vector2(length, size * settings.counterSlashThickness);
            rect.anchoredPosition = new Vector2(side * centre, 0f) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (length * .5f);
            rect.localRotation = Quaternion.Euler(0f, 0f, settings.counterSlashAngle);
        }
    }

    private static void Place(RectTransform rect, int side, float u, float width, float height)
    {
        rect.anchoredPosition = new Vector2(side * u, 0f);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity; // a reused mark may keep a previous twist
    }

    private static TextMeshProUGUI Label(string name, Transform parent, TMP_FontAsset font)
    {
        var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = Vector2.zero;
        return rect;
    }
}
