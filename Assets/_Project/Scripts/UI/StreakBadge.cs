using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>How the kill feed reads, from Tables/KillFeed.</summary>
[Serializable]
public sealed class KillFeedLook
{
    [Min(1)] public int flameFrom = 3, mostNumeral = 25;
    [Range(0f, 1f)] public float ownPlateStrength = .55f;
    [Min(0f), Tooltip("Seconds added to the score's multi-kill window to keep a streak going.")]
    public float streakGrace = 2.5f;
    [Min(1f), Tooltip("How far the fire runs left of the row at flameFrom and on the last numeral, px. The longest is under 190, the image's reach.")]
    public float flameShortest = 74f, flameLongest = 180f;
    [Range(.1f, 1f), Tooltip("Below 1, the fire lengthens fastest early on, where most streaks end.")]
    public float flameGrowth = .7f;
    [Tooltip("The fire's stages, evenly spaced from flameFrom; the last of them leads into blueColor on the last numeral.")]
    public Color[] flameColors =
    {
        new Color(1f, .35f, .12f), new Color(1f, .48f, .08f), new Color(1f, .72f, .11f),
        new Color(1f, .85f, .48f), new Color(1f, .95f, .82f), new Color(.56f, .84f, 1f)
    };
    public Color blueColor = new Color(.31f, .61f, 1f);
    [Tooltip("How far the newest row turns toward the player beyond the other rows (°).")]
    public float leadYaw = 2.5f;
    [Tooltip("The newest row's tilt within the screen (°), to the other side on each kill.")]
    public float leadRoll = .7f;
    [Tooltip("How far the newest row stands off the others toward the player (px).")]
    public float leadLift = 8f;
    [Tooltip("Extra share of the lean and lift for a headshot, a burning streak or a grave notice.")]
    public float leadEmphasis = .6f;
    [Tooltip("How much bigger the newest row stands (share).")]
    public float leadScale = .25f;
    [Tooltip("Extra room under the newest row, so it stands apart from the list (px).")]
    public float heroGap = 6f;
    [Tooltip("The newest row's card: light, so it reads first against the dark and gold rows below it.")]
    public Color heroPlate = new Color(1f, .965f, .918f, .94f);
    [Tooltip("The text on that card.")]
    public Color heroInk = new Color(.09f, .07f, .055f);
    [Tooltip("For your own kills, how much of the gold the card's left end keeps (0-1).")]
    public float heroOwnWash = .45f;
    [Tooltip("How much wider the newest row's end bar is (share of its width).")]
    public float leadAccent = 1f;
    [Tooltip("Opacity each older row loses per step below the newest.")]
    public float olderFade = .13f;
    [Tooltip("The least opacity an older row keeps from that.")]
    public float olderFloor = .55f;
}

/// <summary>
/// A kill streak at the left of a feed row: a Roman numeral in blades of fire streaming left off the row's end, longer with
/// the count and heating all the way, from red through gold and white to blue. Past the last numeral there's nothing left to count: a skull with one
/// eye burning blue, its outline traced in the same cold light.
/// </summary>
public sealed class StreakBadge
{
    private const string FlameShader = "Combat/Shaders/FeedFlame";
    private const float RowHeight = 32f, SkullSize = 30f, EyeGlowSize = 14f;
    // FeedFlame's trail image, and the point on it that sits on the middle of the row's left end. They match the shader.
    private static readonly Vector2 TrailSize = new Vector2(340f, 96f), TrailAnchor = new Vector2(196f, 48f);
    // The eye on the right of the screen, in the skull texture's -1 to 1 space.
    private static readonly Vector2 Eye = new Vector2(.29f, .1f);
    private static readonly Color Bone = new Color(.86f, .94f, 1f);
    private static readonly int TrailId = Shader.PropertyToID("_Trail");
    private static Material oneFlame, trailOfFire, numeralShade;

    private readonly RectTransform root;
    private readonly KillRowPlate plate;
    private readonly RawImage flame, neon, skull, eyeGlow, eyeFlame;
    private readonly TextMeshProUGUI numeral;
    private readonly float seed = UnityEngine.Random.value * 40f;
    private Color neonColor;
    private bool burning;

    public StreakBadge(Transform row)
    {
        root = FeelUI.Rect("Streak", row);
        plate = row.GetComponent<KillRowPlate>();
        root.anchorMin = root.anchorMax = new Vector2(0f, .5f);
        root.pivot = new Vector2(1f, .5f);
        root.anchoredPosition = new Vector2(-6f, 0f);

        // On the row's plate and under its text, so the fire takes hold of the end and the names stay readable.
        flame = Flame("Flame", row, true);
        var trail = flame.rectTransform;
        trail.SetAsFirstSibling();
        trail.anchorMin = trail.anchorMax = new Vector2(0f, .5f);
        trail.pivot = new Vector2(TrailAnchor.x / TrailSize.x, TrailAnchor.y / TrailSize.y);
        trail.sizeDelta = TrailSize;
        trail.anchoredPosition = Vector2.zero;
        neon = Image("Neon", CombatUIAssets.SkullNeon, SkullSize * CombatUIAssets.SkullNeonScale);
        skull = Image("Skull", CombatUIAssets.Skull, SkullSize);
        skull.color = Bone;
        Vector2 eye = Eye * SkullSize * .5f;
        eyeGlow = Image("Eye Glow", CombatUIAssets.SoftGlow.texture, EyeGlowSize);
        eyeGlow.rectTransform.anchoredPosition = eye;
        eyeFlame = Flame("Eye Flame", root, false);
        eyeFlame.rectTransform.pivot = new Vector2(.5f, 0f);
        eyeFlame.rectTransform.sizeDelta = new Vector2(20f, 40f);
        eyeFlame.rectTransform.anchoredPosition = eye - new Vector2(0f, 5f);
        numeral = FeelUI.ScoreLabel("Numeral", root, 22f, TextAlignmentOptions.Center);
        numeral.fontStyle = FontStyles.Italic;
        // Bold's weight comes from the material; its spacing is kept here.
        if (numeral.font != null) numeral.characterSpacing = numeral.font.boldSpacing;
        numeral.fontSharedMaterial = NumeralMaterial(numeral.fontSharedMaterial);
        numeral.color = Color.white;
    }

    private RawImage Image(string name, Texture texture, float size)
    {
        var image = FeelUI.Rect(name, root).gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        image.rectTransform.sizeDelta = new Vector2(size, size);
        return image;
    }

    private RawImage Flame(string name, Transform parent, bool trail)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<RawImage>();
        image.material = FlameMaterial(trail);
        image.raycastTarget = false;
        image.uvRect = new Rect(Mathf.Floor(seed), 0f, 1f, 1f);
        return image;
    }

    private static Material FlameMaterial(bool trail)
    {
        var material = trail ? trailOfFire : oneFlame;
        if (material != null) return material;
        var shader = Resources.Load<Shader>(FlameShader);
        if (shader == null) return null;
        material = new Material(shader) { name = "Feed Flame (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        material.SetFloat(TrailId, trail ? 1f : 0f);
        if (trail) trailOfFire = material; else oneFlame = material;
        return material;
    }

    /// <summary>
    /// The score labels' shadow, tighter, on a face as heavy as bold. Faux bold and the wide shadow stretched each glyph's box
    /// past the room it has in the font atlas, so the box's edge picked up the strokes packed beside it: specks around the
    /// numeral and a dark line over it, plain on the fire. A dilated face is padded for, where bold is not.
    /// </summary>
    private static Material NumeralMaterial(Material shadowed)
    {
        if (numeralShade != null) return numeralShade;
        if (shadowed == null || !shadowed.HasProperty(ShaderUtilities.ID_FaceDilate)) return shadowed;
        numeralShade = new Material(shadowed) { name = shadowed.name + " Numeral", hideFlags = HideFlags.HideAndDontSave };
        float bold = (numeralShade.GetFloat(ShaderUtilities.ID_WeightBold) - numeralShade.GetFloat(ShaderUtilities.ID_WeightNormal)) / 4f;
        numeralShade.SetFloat(ShaderUtilities.ID_FaceDilate, bold);
        numeralShade.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, .75f));
        numeralShade.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, .2f);
        numeralShade.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -.26f);
        numeralShade.SetFloat(ShaderUtilities.ID_UnderlayDilate, .05f);
        numeralShade.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .35f);
        return numeralShade;
    }

    /// <summary>streak 0 shows nothing.</summary>
    public void Show(int streak, KillFeedLook look)
    {
        root.gameObject.SetActive(streak > 0);
        flame.gameObject.SetActive(false);
        if (plate != null) plate.Burn(0f);
        burning = streak > look.mostNumeral;
        if (streak <= 0) return;
        foreach (var part in new Graphic[] { neon, skull, eyeGlow, eyeFlame }) part.gameObject.SetActive(burning);
        numeral.gameObject.SetActive(!burning);
        if (burning) { ShowSkull(look); return; }

        numeral.text = Roman(streak);
        float width = numeral.GetPreferredValues(numeral.text).x + 12f;
        root.sizeDelta = new Vector2(width, RowHeight);
        numeral.rectTransform.sizeDelta = new Vector2(width, RowHeight);
        bool lit = streak >= look.flameFrom;
        flame.gameObject.SetActive(lit);
        if (!lit) return;
        float climb = Mathf.InverseLerp(look.flameFrom, look.mostNumeral, streak);
        flame.color = FireColor(climb, look);
        // How long it runs travels in the uv: the row's fade is on the vertex alpha, and the fire must fade with it.
        float length = Mathf.Lerp(look.flameShortest, look.flameLongest, Mathf.Pow(climb, look.flameGrowth));
        flame.uvRect = new Rect(Mathf.Floor(seed), Mathf.Round(length), 1f, 1f);
        // How far FeedFlame's burn cools along the row: its 60 + full * 0.35.
        if (plate != null) plate.Burn(60f + Mathf.Round(length) * .35f);
    }

    private static Color FireColor(float climb, KillFeedLook look)
    {
        Color[] stages = look.flameColors;
        float place = climb * stages.Length;
        int stage = Mathf.Min(Mathf.FloorToInt(place), stages.Length - 1);
        return Color.Lerp(stages[stage], stage + 1 < stages.Length ? stages[stage + 1] : look.blueColor, place - stage);
    }

    private void ShowSkull(KillFeedLook look)
    {
        root.sizeDelta = new Vector2(SkullSize + 8f, RowHeight);
        neonColor = Color.Lerp(look.blueColor, Color.white, .2f);
        Color fire = look.blueColor;
        fire.a = 1f;
        eyeFlame.color = fire;
        Tick();
    }

    /// <summary>The cold light flickers with the flame that throws it.</summary>
    public void Tick()
    {
        if (!burning) return;
        float flicker = .78f + .22f * Mathf.PerlinNoise(Time.unscaledTime * 3.4f, seed);
        neon.color = new Color(neonColor.r, neonColor.g, neonColor.b, flicker);
        eyeGlow.color = new Color(.7f, .9f, 1f, flicker * .85f);
    }

    private static readonly string[] Ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
    private static string Roman(int number) => new string('X', number / 10) + Ones[number % 10];
}
