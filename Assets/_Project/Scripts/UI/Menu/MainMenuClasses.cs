using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The title screen's 클래스 page (stage 7, PlayerClass): the classes down the left as slanted plates, each at its own lean
/// and indent (KitStack keeps room between them), in a list that scrolls once they outgrow the page (KitScroll), the one
/// played dark with a pink edge and bar, those whose abilities come later marked 준비 중 (they can still be played). Each
/// plate ends in its rank insignia (ClassGrowth, the LAYERLAB Space Rank picture) with its prestige as pink diamonds under
/// it, and a thin line along its foot shows the way to the next insignia. At the right, always the class played (the
/// user, 2026-11-11: "오른쪽에는 내가 선택된 거 설명이 계속"): its name, its variants to pick, its growth (insignia, rank,
/// prestige and the experience to the next rank), what it does, its guns and its ability with the table's numbers.
/// A click plays that class from the next run.
/// Rebuilt 2026-11-13 after the user found the prestige diamonds over the class's line, no experience to be seen, and a
/// page that would break with more classes ("클래스 메뉴가 옛날에 만든 거라 지금이랑 좀 호환이 안 되는 게 많지 … 좀 더 유동적
/// 으로 코드를 짜는 습관이 필요하다"): nothing on the right stands at a fixed spot; each block is measured and the next goes
/// under it, inside a list of its own that scrolls when a long ability runs past the page's foot. A class's variants
/// (Classes.csv base: GMod Horde's subclasses, "시순트는 변형으로 처형 집행인 같은 게") have no plates of their own; they
/// stand as chips under the name, the one played lit. The ability's words come from Texts.csv with the numbers named in
/// them ({Assault.stackMax}, AbilityText), so a new class or variant needs table rows only.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuClasses : MonoBehaviour
{
    private const float LeftWidth = 600f, PlateHeight = 78f, PlateGap = 18f, Slant = 16f, RightX = 860f, RightWidth = 880f,
        IconSize = 48f, TextX = 86f, DetailIcon = 120f, PlateInsignia = 50f, DetailInsignia = 84f, ChipHeight = 26f, ChipGap = 8f,
        BlockGap = 16f, PanelPad = 34f;
    private static readonly float[] Tilts = { 1.2f, -1f, .8f, -1.3f, 1f, -.8f, 1.3f }, Indents = { 0f, 24f, 8f, 30f, 14f, 4f, 26f };
    // A number named in an ability's words: {Row.name}, or {Row.name:less} for (1 - it) × 100; {key} is the class key.
    private static readonly Regex Hole = new Regex(@"\{([A-Za-z][A-Za-z0-9]*)\.([A-Za-z0-9_]+)(:less)?\}", RegexOptions.Compiled);

    private RectTransform root, content;
    // The plates and the right side's blocks scroll once they outgrow the page (KitScroll; a class more, or a short 21:9
    // screen) and keep their place across a pick; the plates run from under the heading to this far above the page's foot.
    private KitScroll plates, detail;
    private float platesOffset;
    private const float ListTop = 150f, PageFoot = 16f, ListRoom = 40f, DetailTop = 160f;
    private float edge;

    public bool IsOpen => root != null && root.gameObject.activeSelf;

    public static MainMenuClasses Make(RectTransform canvas, float edge, float top, float bottom)
    {
        var root = FeelUI.Rect("Classes", canvas);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = new Vector2(0f, bottom);
        root.offsetMax = new Vector2(0f, -top);
        KitStyle.Fill(root, KitStyle.With(KitStyle.Night, .9f)).raycastTarget = true;
        var page = root.gameObject.AddComponent<MainMenuClasses>();
        page.root = root;
        page.edge = edge;
        root.gameObject.SetActive(false);
        return page;
    }

    /// <summary>Builds the page from the table and the pick as they are now, and grows it in.</summary>
    public void Open() => Build(null);

    public void Close()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

    // The page from the table and the pick as they are now. Opening, all of it grows in. After a pick (picked), it is
    // built again at once and only the plate picked and the right side grow in again, so clicking through the classes
    // to read them doesn't replay the whole page.
    private void Build(string picked)
    {
        if (plates != null) platesOffset = plates.Offset;
        if (content != null) Destroy(content.gameObject);
        root.gameObject.SetActive(true);
        content = KitStyle.Stretch("Content", root);
        var reveal = content.gameObject.AddComponent<KitReveal>();
        reveal.Hide();
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", content, new Vector2(edge, 26f), new Vector2(LeftWidth, 80f)), Texts.Get("classes.title"), 64f,
            TextAlignmentOptions.MidlineLeft);
        title.rectTransform.parent.localRotation = Quaternion.Euler(0f, 0f, 3f);
        KitStyle.Flat(KitStyle.TopLeft("Where", content, new Vector2(edge + 4f, 108f), new Vector2(LeftWidth + 200f, 22f)), Texts.Get("classes.where"), 16f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        plates = KitScroll.Make("Plates", content, Vector2.zero, new Vector2(0f, 1f), new Vector2(edge, PageFoot),
            new Vector2(edge + LeftWidth + ListRoom, -ListTop));
        float y = 10f, lastX = 0f, lastTilt = 0f, at = .05f;
        int count = 0;
        RectTransform played = null;
        string pickedFamily = picked != null ? PlayerClass.KitOf(picked) : null;
        foreach (var info in PlayerClass.Catalog)
        {
            // A variant stands under its class's plate, as a chip on the right (Detail).
            if (info.IsVariant) continue;
            float tilt = Tilts[count % Tilts.Length], x = Indents[count % Indents.Length];
            if (count > 0) y = KitStack.Below(lastX, y, LeftWidth, PlateHeight, lastTilt, x, LeftWidth, tilt, PlateGap);
            bool grows = picked == null || info.Id == pickedFamily;
            var plate = Plate(plates.Rows, info, new Vector2(x, y), tilt, grows ? reveal : null, picked == null ? at : 0f);
            if (PlayerClass.Is(info.Id)) played = plate;
            at += .05f;
            lastX = x;
            lastTilt = tilt;
            count++;
        }
        plates.Fit(count > 0 ? y + PlateHeight : 0f);
        plates.Offset = platesOffset;
        plates.Show(played);
        BuildDetail(reveal, picked == null ? at : .05f);
        // Built again after a pick, the page stays shown: no fade of the whole page.
        reveal.Open(picked == null);
    }

    // One class's plate; with a reveal it grows in under a pink lid at its time, without one it is simply there.
    private RectTransform Plate(RectTransform rows, PlayerClass.Info info, Vector2 where, float tilt, KitReveal reveal, float at)
    {
        bool chosen = PlayerClass.Is(info.Id);
        var plate = KitStyle.TopLeft("Class " + info.Id, rows, where, new Vector2(LeftWidth, PlateHeight));
        plate.localRotation = Quaternion.Euler(0f, 0f, tilt);
        var back = KitStyle.Fill(KitStyle.Stretch("Plate", plate), chosen ? KitStyle.With(KitStyle.Night, .95f) : KitStyle.With(KitStyle.Ice, .07f));
        KitSkew.On(back, Slant);
        back.raycastTarget = true;
        if (chosen)
        {
            var rim = KitStyle.Stretch("Edge", plate);
            rim.sizeDelta = new Vector2(2f, 2f);
            rim.SetAsFirstSibling();
            KitSkew.On(KitStyle.Fill(rim, KitStyle.With(KitStyle.Accent, .75f)), Slant);
        }
        KitSkew.On(KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(4f, PlateHeight), chosen ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .35f)), Slant);
        // Its white icon at the left (Resources/UI/Icons/Class_<id>, drawn by Tools/Icons/class_icons.py).
        var icon = KitButtons.IconImage(plate, Icon(info.Id), IconSize, chosen ? KitStyle.Ice : KitStyle.With(KitStyle.Ice, .72f));
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, .5f);
        icon.rectTransform.anchoredPosition = new Vector2(24f + IconSize * .5f, 2f);

        // Its rank insignia at the right end, the prestige under it inside the plate, at a lean of its own.
        var badge = RankBadge(plate, info.Id, PlateInsignia, chosen ? 1f : .8f, out var badgeSize);
        badge.anchorMin = badge.anchorMax = new Vector2(1f, .5f);
        badge.anchoredPosition = new Vector2(-28f - badgeSize.x * .5f, 0f);
        badge.localRotation = Quaternion.Euler(0f, 0f, -6f);
        float right = LeftWidth - 28f - badgeSize.x - 14f;
        // 준비 중, 선택됨, or the variant played, left of it, as wide as its words.
        var played = chosen ? PlayerClass.Current : null;
        string mark = !info.Ready ? Texts.Get("classes.later") : played == null ? null
            : played.IsVariant ? played.Title : Texts.Get("classes.chosen");
        if (mark != null)
        {
            var chip = Chip(plate, mark, info.Ready, null);
            chip.anchoredPosition = new Vector2(right - chip.sizeDelta.x, -(PlateHeight - ChipHeight) * .5f);
            right -= chip.sizeDelta.x + 12f;
        }
        KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(TextX, 6f), new Vector2(Mathf.Max(120f, right - TextX), 40f)), info.Title, 30f,
            TextAlignmentOptions.MidlineLeft, chosen ? KitStyle.Text : KitStyle.With(KitStyle.Text, .85f)).overflowMode = TextOverflowModes.Ellipsis;
        var role = KitStyle.Flat(KitStyle.TopLeft("Role", plate, new Vector2(TextX + 2f, 44f), new Vector2(Mathf.Max(120f, right - TextX - 2f), 20f)), info.Role, 14f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        role.fontStyle = FontStyles.Normal;
        role.overflowMode = FontRoles.ClippedOverflow(role);
        // The way to the next insignia, along the plate's foot (RankProgress: what is stored).
        var view = RankProgress.Of(info.Id);
        float length = Mathf.Max(60f, LeftWidth - 28f - badgeSize.x - 14f - TextX);
        KitStyle.Bar("Growth", plate, new Vector2(TextX + 2f, PlateHeight - 9f), new Vector2(length, 3f), KitStyle.With(KitStyle.Ice, .12f));
        KitStyle.Bar("Growth Fill", plate, new Vector2(TextX + 2f, PlateHeight - 9f), new Vector2(Mathf.Max(2f, length * view.Share), 3f),
            chosen ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .55f));
        if (reveal != null)
        {
            var lid = KitStyle.Fill(KitStyle.Stretch("Flash", plate), KitStyle.Flash);
            KitSkew.On(lid, Slant);
            reveal.Card(plate, lid, at);
        }
        var button = plate.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        string id = info.Id;
        // A class's plate plays the class; a variant of it already played stays (its chips on the right switch them).
        button.onClick.AddListener(() => { if (!PlayerClass.Is(id)) Pick(id); });
        plate.gameObject.AddComponent<KitHover>();
        return plate;
    }

    // Plays this class from the next run: the page is built again to show it, the right side telling of it.
    private void Pick(string id)
    {
        if (PlayerClass.Plays(id)) return;
        ClassGrowth.Choose(id);
        KitSound.Play(KitSound.Cue.Buy, .5f);
        Build(id);
    }

    // ---- The right side: the class played, block under block ----

    private void BuildDetail(KitReveal reveal, float at)
    {
        var info = PlayerClass.Current;
        // Where it was laid out (x 860 of 1920), moved left on a narrower screen so its right edge stays, and as tall as
        // the page leaves it.
        float width = root.rect.width > 0f ? root.rect.width : 1920f, height = root.rect.height > 0f ? root.rect.height : 972f;
        float x = Mathf.Max(edge + LeftWidth + ListRoom, Mathf.Min(RightX, width - edge - RightWidth));
        float panelWidth = Mathf.Max(420f, Mathf.Min(RightWidth, width - edge - x));
        var panel = KitStyle.TopLeft("Detail", content, new Vector2(x, DetailTop), new Vector2(panelWidth, Mathf.Max(320f, height - DetailTop - PageFoot)));
        KitStyle.Cut(panel, KitStyle.With(KitStyle.Ice, .05f));
        reveal.Card(panel, KitButtons.Lid(panel), at);
        if (info == null) return;
        // The class's icon large at the top right, leaning a little, over the blocks.
        var icon = KitButtons.IconImage(panel, Icon(info.Id), DetailIcon, KitStyle.With(KitStyle.Ice, .9f));
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(1f, 1f);
        icon.rectTransform.anchoredPosition = new Vector2(-36f - DetailIcon * .5f, -24f - DetailIcon * .5f);
        icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -4f);

        detail = KitScroll.Make("Detail Rows", panel, Vector2.zero, Vector2.one, new Vector2(PanelPad, 18f), new Vector2(-PanelPad, -18f));
        var rows = detail.Rows;
        float inner = panelWidth - 2f * PanelPad, beside = inner - DetailIcon - 16f, y = 4f;
        y += Words(rows, y, beside, info.Title, 46f, KitStyle.Text, FontStyles.Bold) + 2f;
        if (info.IsVariant)
            y += Words(rows, y, beside, Texts.Format("classes.variant.of", ClassGrowth.TitleOf(info.Base)), 16f, KitStyle.Soft) + 4f;
        y = Variants(rows, info, y, beside);
        y = Mathf.Max(y, DetailIcon - 10f) + BlockGap;
        y += Growth(rows, info.Id, y, inner) + BlockGap;
        y += Words(rows, y, inner, info.Role, 18f, KitStyle.Soft) + BlockGap;
        string weapons = info.Weapons.Length > 0 ? string.Join(" · ", System.Array.ConvertAll(info.Weapons, Name)) : "-";
        string kit = Texts.Format("classes.loadout", Name(info.Primary), Name(info.Secondary))
            + (info.GunBonus > 0f ? "\n" + Texts.Format("classes.weapons", weapons, info.GunBonus.ToString("0", CultureInfo.InvariantCulture)) : "");
        y += Words(rows, y, inner, kit, 18f, KitStyle.Text) + BlockGap;
        string ability = "<color=#" + ColorUtility.ToHtmlStringRGB(KitStyle.Accent) + ">" + Texts.Get("classes.ability") + "</color>\n" + AbilityText(info);
        y += Words(rows, y, inner, ability, 18f, KitStyle.Text, FontStyles.Normal, 18f);
        if (!info.Ready) y += BlockGap + Words(rows, y + BlockGap, inner, Texts.Get("classes.later_note"), 16f, KitStyle.Soft);
        detail.Fit(y + 8f);
    }

    // The class and its variants as chips under the name (Classes.csv base), the one played lit; a press plays it. None
    // for a class that has no variants. Returns the y under them.
    private float Variants(RectTransform rows, PlayerClass.Info info, float y, float width)
    {
        var family = PlayerClass.FamilyOf(info.Id);
        if (family.Count < 2) return y;
        y += 6f;
        Words(rows, y, width, Texts.Get("classes.variants"), 14f, KitStyle.Soft);
        y += 22f;
        float x = 0f;
        foreach (var member in family)
        {
            string id = member.Id;
            bool on = PlayerClass.Plays(id);
            string label = member.Ready ? member.Title : member.Title + "  ·  " + Texts.Get("classes.later");
            var chip = Chip(rows, label, on, on ? null : (System.Action)(() => Pick(id)));
            if (x > 0f && x + chip.sizeDelta.x > width)
            {
                x = 0f;
                y += ChipHeight + ChipGap;
            }
            chip.anchoredPosition = new Vector2(x, -y);
            x += chip.sizeDelta.x + ChipGap;
        }
        return y + ChipHeight + 4f;
    }

    // The class's growth: its insignia with its prestige, the rank and prestige in words, and a bar with the experience
    // gathered toward the next rank (RankProgress, what is stored; the user, 2026-11-13: "지금 클래스 경험치가 몇인지 안
    // 보이는 문제"). Returns its height.
    private static float Growth(RectTransform rows, string id, float y, float width)
    {
        var badge = RankBadge(rows, id, DetailInsignia, 1f, out var badgeSize);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(0f, 1f);
        badge.anchoredPosition = new Vector2(6f, -y);
        badge.localRotation = Quaternion.Euler(0f, 0f, -6f);
        var view = RankProgress.Of(id);
        float left = badgeSize.x + 28f, room = Mathf.Max(160f, width - left), at = y + 4f;
        at += Words(rows, at, room, Texts.Format("classes.rank", view.Rank, ClassGrowth.RankCount), 24f, KitStyle.Text, FontStyles.Bold, 0f, left) + 2f;
        if (view.Prestige > 0) at += Words(rows, at, room, Texts.Format("classes.prestige", view.Prestige), 15f, KitStyle.Accent, FontStyles.Normal, 0f, left) + 2f;
        at += 6f;
        KitStyle.Bar("Growth", rows, new Vector2(left, at), new Vector2(room, 6f), KitStyle.With(KitStyle.Ice, .12f));
        KitStyle.Bar("Growth Fill", rows, new Vector2(left, at), new Vector2(Mathf.Max(3f, room * view.Share), 6f), KitStyle.Accent);
        at += 12f;
        string words = view.Top
            ? Texts.Format("classes.xp.top", Whole(view.To - view.Reach))
            : Texts.Format("classes.xp", Whole(view.Reach - view.From), Whole(view.To - view.From), Whole(view.ToNext));
        at += Words(rows, at, room, words, 15f, KitStyle.Soft, FontStyles.Normal, 0f, left);
        return Mathf.Max(badgeSize.y, at - y);
    }

    private static string Whole(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    // Words at y in the rows, wrapping in width, as tall as they come out; returns that height.
    private static float Words(RectTransform rows, float y, float width, string text, float size, Color colour, FontStyles style = FontStyles.Normal,
        float lineSpacing = 0f, float x = 0f)
    {
        var label = KitStyle.Flat(KitStyle.TopLeft("Words", rows, new Vector2(x, y), new Vector2(width, size * 1.4f)), text ?? "", size,
            TextAlignmentOptions.TopLeft, colour);
        label.fontStyle = style;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.lineSpacing = lineSpacing;
        float height = Mathf.Ceil(label.GetPreferredValues(label.text, width, 0f).y) + 2f;
        label.rectTransform.sizeDelta = new Vector2(width, height);
        return height;
    }

    // A small slanted chip as wide as its words; the lit one solid pink. Without an action it only shows. Top-left anchored:
    // place it by anchoredPosition (x, -y).
    private static RectTransform Chip(RectTransform parent, string text, bool on, System.Action picked)
    {
        var chip = KitStyle.TopLeft("Chip", parent, Vector2.zero, new Vector2(96f, ChipHeight));
        var back = KitStyle.Fill(chip, on ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .12f));
        KitSkew.On(back, 6f);
        var label = KitStyle.Flat(KitStyle.Stretch("Text", chip), text, 13f, TextAlignmentOptions.Center, on ? KitStyle.Ink : KitStyle.Soft);
        chip.sizeDelta = new Vector2(Mathf.Clamp(Mathf.Ceil(label.GetPreferredValues(text, 400f, ChipHeight).x) + 28f, 72f, 260f), ChipHeight);
        if (picked == null) return chip;
        back.raycastTarget = true;
        var button = chip.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(() => picked());
        chip.gameObject.AddComponent<KitHover>();
        return chip;
    }

    // A class's rank insignia (ClassGrowth: its rank's LAYERLAB Space Rank picture) with its prestige as small pink
    // diamonds under it, all inside the holder it returns: its size covers both, so whatever goes beside or under it can
    // be placed clear of it (the old one hung its diamonds below its square, over the line under it). Anchor and place it.
    private static RectTransform RankBadge(RectTransform parent, string id, float size, float alpha, out Vector2 badgeSize)
    {
        int pips = Mathf.Min(ClassGrowth.PrestigeNow(id), ClassGrowth.MostPips);
        float pip = Mathf.Max(6f, size * .11f), pipRow = pips > 0 ? pip * 1.8f : 0f;
        badgeSize = new Vector2(size, size + pipRow);
        var holder = FeelUI.Rect("Rank", parent);
        holder.sizeDelta = badgeSize;
        var picture = new GameObject("Insignia", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        picture.transform.SetParent(holder, false);
        picture.raycastTarget = false;
        picture.texture = ClassGrowth.Insignia(ClassGrowth.RankNow(id));
        picture.color = new Color(1f, 1f, 1f, picture.texture != null ? alpha : 0f);
        picture.rectTransform.sizeDelta = new Vector2(size, size);
        picture.rectTransform.anchoredPosition = new Vector2(0f, pipRow * .5f);
        for (int i = 0; i < pips; i++)
        {
            var mark = FeelUI.Rect("Prestige", holder);
            KitStyle.Fill(mark, KitStyle.Accent).raycastTarget = false;
            mark.sizeDelta = new Vector2(pip, pip);
            mark.anchoredPosition = new Vector2((i - (pips - 1) * .5f) * pip * 1.8f, -badgeSize.y * .5f + pipRow * .5f);
            mark.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }
        return holder;
    }

    /// <summary>
    /// A class's icon's name in Resources/UI/Icons: its own (Class_&lt;id&gt;), else its base class's for a variant that
    /// has none of its own yet.
    /// </summary>
    public static string Icon(string id)
    {
        string own = "Class_" + id;
        return KitButtons.Icon(own) != null ? own : "Class_" + PlayerClass.KitOf(id);
    }

    // A gun's name as the HUD has it.
    private static string Name(string weapon)
    {
        if (string.IsNullOrEmpty(weapon)) return "-";
        var assets = FPSBodyAssets.Instance;
        if (assets != null)
            foreach (var entry in assets.guns)
                if (entry.weapon != null && string.Equals(entry.weapon.name, weapon, System.StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrEmpty(entry.weapon.DisplayName) ? weapon : entry.weapon.DisplayName;
        return weapon;
    }

    /// <summary>
    /// What a class's ability does, its numbers filled in: Texts.csv class.&lt;id&gt;.ability (a variant without one of its
    /// own takes its base's) names each number where it stands, {Row.name} for Tables/ClassAbilities' Row.name as
    /// PlayerClass.Ability reads it (a variant's own row first), {Row.name:less} for (1 - it) × 100, and {key} for the class
    /// key as bound. A name the table lacks shows as ?. Any class or variant added to the tables describes itself so.
    /// </summary>
    public static string AbilityText(PlayerClass.Info info)
    {
        if (info == null) return "";
        string key = "class." + info.Id + ".ability";
        if (!Texts.Has(key) && info.IsVariant) key = "class." + info.Base + ".ability";
        if (!Texts.Has(key)) return "";
        string text = Texts.Get(key).Replace("{key}", ClassKey());
        return Hole.Replace(text, match =>
        {
            float value = PlayerClass.Ability(match.Groups[1].Value, match.Groups[2].Value, float.NaN);
            if (float.IsNaN(value)) return "?";
            if (match.Groups[3].Success) value = (1f - value) * 100f;
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        });
    }

    // The class key as the player has it bound (the wheel click unless changed in Settings → 조작).
    private static string ClassKey() => KeyBindings.Name(UnityEngine.InputSystem.InputSystem.actions != null
        ? UnityEngine.InputSystem.InputSystem.actions.FindAction("Player/ClassAbility", false) : null);
}
