using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays a kill's flair (KillFlairs) on its feed row (KillFeedRow): the icon's effect on a layer over the weapon's icon,
/// drawn with the icon's own sprite by KillFlair.shader (fire from the muzzle back, gold, a trail of flame, a tint), and the
/// row's pieces round the names (a stamp, a seal, a line through the one killed, brackets and a check, the bounty paid, a
/// chip, a tag over the icon, the one killed in the flair's colour, the plate's edges and sheen in it, a jolt). Each piece
/// is built the first time a flair needs it and kept for the row's next kill. Short and loud, as the user wants hits: a
/// piece slams in within a sixth of a second, holds for its row's seconds and goes in a third; the line through the one
/// killed and its colour stay as long as the row. Real time, like the row.
/// </summary>
public sealed class KillFlairFx
{
    private const string ShaderPath = "Combat/Shaders/KillFlair";
    private const float Slam = .15f, Fade = .3f, FireReach = .38f, GildReach = .33f, KickSeconds = .13f, KickSize = 5f;
    private static readonly int ModeId = Shader.PropertyToID("_Mode"), ProgressId = Shader.PropertyToID("_Progress"),
        HeatId = Shader.PropertyToID("_Heat"), LickId = Shader.PropertyToID("_Lick"), SeedId = Shader.PropertyToID("_Seed"),
        ColourId = Shader.PropertyToID("_FlairColor");
    private static readonly Color Night = new Color(.035f, .035f, .045f, .88f);
    private static Shader shader;

    private readonly RectTransform row;
    private readonly Image weapon;
    private readonly TMP_Text victim;
    private readonly float seed = Random.value * 50f;

    private Image layer;
    private Material material;
    private RectTransform stamp, seal, chip, frame, tick;
    private Image stampEdge, stampFill, sealRing, sealFill, chipPlate, strike, tickShort, tickLong;
    private readonly Image[] corners = new Image[8];
    private readonly Image[] specks = new Image[6];
    private readonly Vector2[] speckWay = new Vector2[6];
    private RawImage chipMoney;
    private TextMeshProUGUI stampWords, sealWords, chipWords, tag;

    private KillFlairs.Play play;
    private float age = 99f;
    private int paid;

    /// <summary>What the row reads back each frame: its edge lines, its sheen, its jolt, the colour on the one killed.</summary>
    public Color Edge { get; private set; }
    public float EdgeStrength { get; private set; }
    public bool Sheening { get; private set; }
    public bool SheenEnded { get; private set; }
    public float SheenAt { get; private set; } = -1f;
    public Color SheenTint { get; private set; }
    public Vector2 Kick { get; private set; }
    public Color NameColour { get; private set; }
    public float NameTint { get; private set; }
    /// <summary>
    /// The room the flair's pieces take over the row (a stamp, a seal) and under it (the bounty paid, a chip) now, px before
    /// the row's own scale: the feed stands the rows round it that much apart, so nothing of it covers another row.
    /// </summary>
    public float Above { get; private set; }
    public float Below { get; private set; }

    public KillFlairFx(RectTransform row, Image weapon, TMP_Text victim)
    {
        this.row = row;
        this.weapon = weapon;
        this.victim = victim;
    }

    /// <summary>A new kill on this row: what it plays from now (nothing for none). The row's layout is already set.</summary>
    public void Play(KillFlairs.Play what)
    {
        play = what;
        age = 0f;
        Edge = Color.clear; EdgeStrength = 0f; Sheening = SheenEnded = false; SheenAt = -1f; Kick = Vector2.zero; NameTint = 0f;
        Above = Below = 0f;
        HideAll();
        if (!what.Any) return;
        var icon = what.Icon;
        if (icon != null && icon.Icon != KillFlairs.Icon.None && weapon != null && weapon.sprite != null && Layer())
        {
            layer.sprite = weapon.sprite;
            layer.gameObject.SetActive(true);
            material.SetFloat(ModeId, icon.Icon == KillFlairs.Icon.Fire ? 1f : icon.Icon == KillFlairs.Icon.Gild ? 2f : icon.Icon == KillFlairs.Icon.Trail ? 3f : 0f);
            material.SetColor(ColourId, icon.Colour);
            material.SetFloat(SeedId, seed);
            material.SetFloat(LickId, icon.Icon == KillFlairs.Icon.Trail ? .16f : .14f);
            material.SetFloat(HeatId, 0f);
        }
        var flair = what.Row;
        if (flair == null) return;
        var pieces = flair.Row;
        string words = flair.Text ?? "";
        Vector2 at = victim.rectTransform.anchoredPosition;
        float wide = victim.rectTransform.sizeDelta.x;
        if ((pieces & KillFlairs.Row.Stamp) != 0) PlaceStamp(words, flair.Colour);
        if ((pieces & KillFlairs.Row.Seal) != 0) PlaceSeal(words, flair.Colour);
        if ((pieces & (KillFlairs.Row.Money | KillFlairs.Row.Chip)) != 0)
        {
            // What a Schießhund's bounty pays, as GunFeelDirector pays it (ClassAbilities Schiesshund.bountyMoney / bountyElite).
            bool elite = (what.Marks & KillMarks.Elite) != 0;
            paid = (pieces & KillFlairs.Row.Money) != 0
                ? Mathf.RoundToInt(PlayerClass.Ability("Schiesshund", elite ? "bountyElite" : "bountyMoney", elite ? 300f : 100f)) : 0;
            PlaceChip((pieces & KillFlairs.Row.Money) != 0, words, flair.Colour, (pieces & KillFlairs.Row.Seal) != 0);
        }
        if ((pieces & KillFlairs.Row.Strike) != 0) PlaceStrike(at, wide, flair.Colour);
        if ((pieces & KillFlairs.Row.Frame) != 0) PlaceFrame(at, wide, flair.Colour);
        if ((pieces & KillFlairs.Row.Tick) != 0) PlaceTick(at, wide, flair.Colour);
        if ((pieces & KillFlairs.Row.Tag) != 0) PlaceTag(words, flair.Colour);
        NameColour = flair.Colour;
    }

    /// <summary>Moves everything on by dt (real seconds).</summary>
    public void Tick(float dt)
    {
        SheenEnded = false;
        if (age > 50f) return;
        age += dt;
        bool any = false;
        var icon = play.Icon;
        if (icon != null && layer != null && layer.gameObject.activeSelf)
        {
            float t = age - icon.Delay, life = icon.Seconds;
            float heat = t < 0f ? 0f : icon.Icon == KillFlairs.Icon.Tint ? 1f - Mathf.Clamp01(t / life)
                : icon.Icon == KillFlairs.Icon.Trail ? Mathf.Clamp01(t / .06f) * Out(t, life) : Out(t, life);
            float reach = icon.Icon == KillFlairs.Icon.Fire ? Ease(t / FireReach) : icon.Icon == KillFlairs.Icon.Gild ? Ease(t / GildReach) : 1f;
            material.SetFloat(HeatId, heat);
            material.SetFloat(ProgressId, reach);
            if (t > life + Fade) layer.gameObject.SetActive(false);
            else any = true;
        }
        var flair = play.Row;
        if (flair != null)
        {
            float t = age - flair.Delay, life = flair.Seconds, show = t < 0f ? 0f : Out(t, life);
            var pieces = flair.Row;
            if (t <= life + Fade) any = true;
            if (stamp != null && stamp.gameObject.activeSelf) Slammed(stamp, t, show, -8f);
            if (seal != null && seal.gameObject.activeSelf)
            {
                Slammed(seal, t, show, -14f);
                Specks(t - .12f);
            }
            if (chip != null && chip.gameObject.activeSelf)
            {
                float late = (pieces & KillFlairs.Row.Seal) != 0 ? .22f : .28f, s = Mathf.Clamp01((t - late) / .14f);
                chip.anchoredPosition = new Vector2(chip.anchoredPosition.x, ChipY + (1f - s) * 6f);
                SetAlpha(chip, s * show);
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
                SetAlpha(frame, s * show);
            }
            if (tick != null && tick.gameObject.activeSelf)
            {
                float s = Mathf.Clamp01((t - .15f) / .15f);
                tickShort.rectTransform.localScale = new Vector3(Mathf.Clamp01(s * 2f), 1f, 1f);
                tickLong.rectTransform.localScale = new Vector3(Mathf.Clamp01(s * 2f - 1f), 1f, 1f);
                SetAlpha(tick, (t >= .15f ? 1f : 0f) * show);
            }
            if (tag != null && tag.gameObject.activeSelf)
            {
                float s = Mathf.Clamp01(t / .14f);
                tag.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.9f, 1f, Ease(s));
                tag.alpha = s * show;
            }
            NameTint = (pieces & KillFlairs.Row.Name) != 0 ? Mathf.Clamp01((t - .15f) / .12f) : 0f;
            if ((pieces & KillFlairs.Row.Edge) != 0)
            {
                Edge = flair.Colour;
                EdgeStrength = t < 0f ? 0f : Mathf.Clamp01(t / (.15f * life)) * Mathf.Clamp01((life - t) / (.3f * life));
            }
            if ((pieces & KillFlairs.Row.Sheen) != 0)
            {
                bool was = Sheening;
                Sheening = t >= 0f && t <= .38f;
                SheenAt = Sheening ? 1.2f * (t / .38f) - .1f : -1f;
                Color tint = flair.Colour;
                tint.a = .75f;
                SheenTint = tint;
                SheenEnded = was && !Sheening;
            }
            Kick = (pieces & KillFlairs.Row.Kick) != 0 && t >= 0f && t <= KickSeconds
                ? new Vector2(-KickSize, 2f) * Mathf.Sin(Mathf.PI * t / KickSeconds) : Vector2.zero;
            // A stamp stands 20 px over the row's top (its tilt a few more), a seal 12; a chip 24 under its bottom.
            float over = 0f;
            if (stamp != null && stamp.gameObject.activeSelf) over = Mathf.Max(over, (stamp.sizeDelta.y - 6f + 6f) * show);
            if (seal != null && seal.gameObject.activeSelf) over = Mathf.Max(over, 12f * show);
            Above = over;
            Below = chip != null && chip.gameObject.activeSelf ? 24f * show : 0f;
        }
        if (!any) { age = 99f; Above = Below = 0f; }
    }

    /// <summary>The row goes away: nothing of its flair is left showing.</summary>
    public void Hide()
    {
        HideAll();
        age = 99f;
        EdgeStrength = 0f; NameTint = 0f; Kick = Vector2.zero; Sheening = false;
        Above = Below = 0f;
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
        var rect = layer.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
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
        // Over the row's end, a little above it, clear of the names under it.
        stamp.anchoredPosition = new Vector2(-14f - width * .5f, KillFeedRow.Height * .5f + height * .5f - 6f);
        stamp.SetAsLastSibling();
        stamp.gameObject.SetActive(true);
    }

    private void PlaceSeal(string words, Color colour)
    {
        if (seal == null)
        {
            seal = FeelUI.Rect("Flair Seal", row);
            seal.anchorMin = seal.anchorMax = new Vector2(1f, .5f);
            sealFill = FeelUI.Rect("Fill", seal).gameObject.AddComponent<Image>();
            sealFill.sprite = CombatUIAssets.Disc;
            sealFill.raycastTarget = false;
            Stretch(sealFill.rectTransform, 0f);
            sealRing = FeelUI.Rect("Ring", seal).gameObject.AddComponent<Image>();
            sealRing.sprite = CombatUIAssets.Ring;
            sealRing.raycastTarget = false;
            Stretch(sealRing.rectTransform, 0f);
            sealWords = FeelUI.ScoreLabel("Words", seal, 11f, TextAlignmentOptions.Center);
            sealWords.lineSpacing = -18f;
            Stretch(sealWords.rectTransform, 4f);
            for (int i = 0; i < specks.Length; i++)
            {
                specks[i] = FeelUI.Rect("Speck", seal).gameObject.AddComponent<Image>();
                specks[i].sprite = CombatUIAssets.Disc;
                specks[i].raycastTarget = false;
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
    }

    private void PlaceChip(bool money, string words, Color colour, bool besideSeal)
    {
        if (chip == null)
        {
            chip = FeelUI.Rect("Flair Chip", row);
            chip.anchorMin = chip.anchorMax = new Vector2(1f, .5f);
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
        // Under the row's end; beside a seal, left of it so the two never cover each other.
        chip.anchoredPosition = new Vector2((besideSeal ? -40f : -6f) - width * .5f, ChipY);
        chip.SetAsLastSibling();
        chip.gameObject.SetActive(true);
        SetAlpha(chip, 0f);
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
            for (int i = 0; i < corners.Length; i++)
            {
                corners[i] = FeelUI.Rect("Corner", frame).gameObject.AddComponent<Image>();
                corners[i].raycastTarget = false;
            }
        }
        float w = wide + 14f, h = KillFeedRow.Height - 6f, arm = 8f, line = 2.5f;
        frame.sizeDelta = new Vector2(w, h);
        frame.anchoredPosition = at;
        for (int i = 0; i < 4; i++)
        {
            float sx = i % 2 == 0 ? -1f : 1f, sy = i < 2 ? 1f : -1f;
            var across = corners[i * 2].rectTransform;
            across.sizeDelta = new Vector2(arm, line);
            across.anchoredPosition = new Vector2(sx * (w * .5f - arm * .5f), sy * (h * .5f - line * .5f));
            var down = corners[i * 2 + 1].rectTransform;
            down.sizeDelta = new Vector2(line, arm);
            down.anchoredPosition = new Vector2(sx * (w * .5f - line * .5f), sy * (h * .5f - arm * .5f));
            corners[i * 2].color = corners[i * 2 + 1].color = colour;
        }
        frame.SetAsLastSibling();
        frame.gameObject.SetActive(true);
        SetAlpha(frame, 0f);
    }

    private void PlaceTick(Vector2 at, float wide, Color colour)
    {
        if (tick == null)
        {
            tick = FeelUI.Rect("Flair Tick", row);
            tick.anchorMin = tick.anchorMax = new Vector2(1f, .5f);
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
        SetAlpha(tick, 0f);
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

    // ---- motion ----

    private void Slammed(RectTransform piece, float t, float show, float tilt)
    {
        float s = Mathf.Clamp01(t / Slam);
        // 2.3x and flat, down past its size, and back: a slam, not a fade.
        float scale = s < .7f ? Mathf.Lerp(2.3f, .92f, Ease(s / .7f)) : Mathf.Lerp(.92f, 1f, (s - .7f) / .3f);
        piece.localScale = Vector3.one * scale;
        piece.localRotation = Quaternion.Euler(0f, 0f, tilt);
        SetAlpha(piece, (t >= 0f ? Mathf.Clamp01(s / .4f) : 0f) * show);
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

    private void HideAll()
    {
        if (layer != null) layer.gameObject.SetActive(false);
        foreach (var piece in new Component[] { stamp, seal, chip, strike, frame, tick, tag })
            if (piece != null) piece.gameObject.SetActive(false);
    }

    private static void SetAlpha(RectTransform piece, float alpha)
    {
        var group = piece.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = piece.gameObject.AddComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;
        }
        group.alpha = alpha;
    }

    private static Image Fill(string name, RectTransform parent, float inset)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        Stretch(image.rectTransform, inset);
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
