using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The title screen's 클래스 page (stage 7, PlayerClass): the seven classes down the left as slanted plates, each at its
/// own lean and indent (KitStack keeps room between them), the one played dark with a pink edge and bar, those whose
/// abilities come later marked 준비 중 (they can still be played); its rank insignia (ClassGrowth, the LAYERLAB Space Rank
/// picture) at the right of each, prestige as small pink diamonds under it, never a number (the user, 2026-11-11:
/// "클래스별로 랭크 이미지 좀 보이게"; growth reads as the picture alone). At the right, always the class played: its name
/// with its insignia beside it, what it does, its starting guns, its own guns and their bonus, and its ability in its
/// numbers. Pointing at another plate only lights it (the user, 2026-11-11: "오른쪽에는 내가 선택된 거
/// 설명이 계속"; it used to follow the pointer and stay on the last plate passed over). A click plays that class from
/// the next run and the right side turns to it. The records page's look.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuClasses : MonoBehaviour
{
    private const float LeftWidth = 600f, PlateHeight = 74f, PlateGap = 18f, Slant = 16f, RightX = 860f, RightWidth = 880f,
        IconSize = 48f, TextX = 86f, DetailIcon = 132f, PlateInsignia = 66f, DetailInsignia = 92f;
    private static readonly float[] Tilts = { 1.2f, -1f, .8f, -1.3f, 1f, -.8f, 1.3f }, Indents = { 0f, 24f, 8f, 30f, 14f, 4f, 26f };

    private RectTransform root, content;
    private TextMeshProUGUI detailTitle, detailRole, detailBody;
    private RawImage detailIcon;
    private RectTransform detailPanel, detailRank;
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

    // The page from the table and the pick as they are now. Opening, all of it grows in. After a pick (picked), it is
    // built again at once and only the plate picked and the right side grow in again, so clicking through the classes
    // to read them doesn't replay the whole page.
    private void Build(string picked)
    {
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
        float y = 160f, lastX = 0f, lastTilt = 0f, at = .05f;
        var catalog = PlayerClass.Catalog;
        for (int i = 0; i < catalog.Count; i++)
        {
            float tilt = Tilts[i % Tilts.Length], x = Indents[i % Indents.Length];
            if (i > 0) y = KitStack.Below(lastX, y, LeftWidth, PlateHeight, lastTilt, x, LeftWidth, tilt, PlateGap);
            bool grows = picked == null || catalog[i].Id == picked;
            Plate(catalog[i], new Vector2(edge + x, y), tilt, grows ? reveal : null, picked == null ? at : 0f);
            at += .05f;
            lastX = x;
            lastTilt = tilt;
        }
        BuildDetail(reveal, picked == null ? at : .05f);
        Show(PlayerClass.CurrentId);
        // Built again after a pick, the page stays shown: no fade of the whole page.
        reveal.Open(picked == null);
    }

    public void Close()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

    // One class's plate; with a reveal it grows in under a pink lid at its time, without one it is simply there.
    private void Plate(PlayerClass.Info info, Vector2 where, float tilt, KitReveal reveal, float at)
    {
        bool chosen = PlayerClass.Is(info.Id);
        var plate = KitStyle.TopLeft("Class " + info.Id, content, where, new Vector2(LeftWidth, PlateHeight));
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
        icon.rectTransform.anchoredPosition = new Vector2(24f + IconSize * .5f, 0f);
        KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(TextX, 6f), new Vector2(260f, 40f)), info.Title, 30f,
            TextAlignmentOptions.MidlineLeft, chosen ? KitStyle.Text : KitStyle.With(KitStyle.Text, .85f));
        var role = KitStyle.Flat(KitStyle.TopLeft("Role", plate, new Vector2(TextX + 2f, 44f), new Vector2(LeftWidth - TextX - 240f, 22f)), info.Role, 14f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        role.fontStyle = FontStyles.Normal;
        role.overflowMode = CombatUIAssets.ClippedOverflow(role);
        // Its rank insignia at the right end, at a lean of its own; 준비 중 or 선택됨 left of it.
        var rank = Rank(plate, info.Id, PlateInsignia, chosen ? 1f : .8f);
        rank.anchorMin = rank.anchorMax = new Vector2(1f, .5f);
        rank.anchoredPosition = new Vector2(-34f - PlateInsignia * .5f, 0f);
        rank.localRotation = Quaternion.Euler(0f, 0f, -6f);
        const float ChipX = LeftWidth - 216f, ChipY = (PlateHeight - 24f) * .5f;
        if (!info.Ready)
        {
            var later = KitStyle.TopLeft("Later", plate, new Vector2(ChipX, ChipY), new Vector2(96f, 24f));
            KitSkew.On(KitStyle.Fill(later, KitStyle.With(KitStyle.Ice, .12f)), 6f);
            KitStyle.Flat(later, Texts.Get("classes.later"), 13f, TextAlignmentOptions.Center, KitStyle.Soft);
        }
        else if (chosen)
        {
            var mark = KitStyle.TopLeft("Chosen", plate, new Vector2(ChipX, ChipY), new Vector2(96f, 24f));
            KitSkew.On(KitStyle.Fill(mark, KitStyle.Accent), 6f);
            KitStyle.Flat(mark, Texts.Get("classes.chosen"), 13f, TextAlignmentOptions.Center, KitStyle.Ink);
        }
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
        button.onClick.AddListener(() => Pick(id));
        plate.gameObject.AddComponent<KitHover>();
    }

    // Plays this class from the next run: the page is built again to show it, the right side telling of it.
    private void Pick(string id)
    {
        if (PlayerClass.Is(id)) return;
        ClassGrowth.Choose(id);
        KitSound.Play(KitSound.Cue.Buy, .5f);
        Build(id);
    }

    private void BuildDetail(KitReveal reveal, float at)
    {
        var panel = KitStyle.TopLeft("Detail", content, new Vector2(RightX, 160f), new Vector2(RightWidth, 560f));
        KitStyle.Cut(panel, KitStyle.With(KitStyle.Ice, .05f));
        detailPanel = panel;
        detailRank = null;
        detailTitle = KitStyle.Flat(KitStyle.TopLeft("Name", panel, new Vector2(34f, 22f), new Vector2(RightWidth - 68f, 60f)), "", 46f,
            TextAlignmentOptions.MidlineLeft);
        // The class's icon large at the top right, leaning a little.
        detailIcon = KitButtons.IconImage(panel, Icon(PlayerClass.CurrentId), DetailIcon, KitStyle.With(KitStyle.Ice, .9f));
        detailIcon.rectTransform.anchorMin = detailIcon.rectTransform.anchorMax = new Vector2(1f, 1f);
        detailIcon.rectTransform.anchoredPosition = new Vector2(-40f - DetailIcon * .5f, -28f - DetailIcon * .5f);
        detailIcon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -4f);
        detailRole = KitStyle.Flat(KitStyle.TopLeft("Role", panel, new Vector2(36f, 86f), new Vector2(RightWidth - 72f, 56f)), "", 18f,
            TextAlignmentOptions.TopLeft, KitStyle.Soft);
        detailRole.fontStyle = FontStyles.Normal;
        detailRole.textWrappingMode = TextWrappingModes.Normal;
        detailBody = KitStyle.Flat(KitStyle.TopLeft("Body", panel, new Vector2(36f, 160f), new Vector2(RightWidth - 72f, 380f)), "", 18f,
            TextAlignmentOptions.TopLeft, KitStyle.Text);
        detailBody.fontStyle = FontStyles.Normal;
        detailBody.textWrappingMode = TextWrappingModes.Normal;
        detailBody.lineSpacing = 18f;
        reveal.Card(panel, KitButtons.Lid(panel), at);
    }

    // The right side tells of this class.
    private void Show(string id)
    {
        if (detailTitle == null) return;
        var info = PlayerClass.Find(id);
        if (info == null) return;
        detailTitle.text = info.Title;
        // Its insignia right after its name, like a name badge, short of the icon at the top right.
        if (detailRank != null) Destroy(detailRank.gameObject);
        detailRank = Rank(detailPanel, id, DetailInsignia, 1f);
        detailRank.anchorMin = detailRank.anchorMax = new Vector2(0f, 1f);
        float after = 34f + detailTitle.GetPreferredValues(info.Title).x + 26f + DetailInsignia * .5f;
        detailRank.anchoredPosition = new Vector2(Mathf.Min(after, RightWidth - 60f - DetailIcon - DetailInsignia * .5f), -22f - 30f);
        detailRank.localRotation = Quaternion.Euler(0f, 0f, -6f);
        detailIcon.texture = KitButtons.Icon(Icon(id));
        detailIcon.enabled = detailIcon.texture != null;
        detailRole.text = info.Role;
        string weapons = info.Weapons.Length > 0 ? string.Join(" · ", info.Weapons) : "-";
        string body = Texts.Format("classes.loadout", Name(info.Primary), Name(info.Secondary)) + "\n"
            + (info.GunBonus > 0f ? Texts.Format("classes.weapons", weapons, info.GunBonus.ToString("0")) + "\n" : "")
            + "\n<color=#" + ColorUtility.ToHtmlStringRGB(KitStyle.Accent) + ">" + Texts.Get("classes.ability") + "</color>\n" + Ability(info);
        if (!info.Ready) body += "\n\n<color=#" + ColorUtility.ToHtmlStringRGB(KitStyle.Soft) + ">" + Texts.Get("classes.later_note") + "</color>";
        detailBody.text = body;
    }

    // A class's rank insignia (ClassGrowth: its rank's LAYERLAB Space Rank picture), its prestige as small pink diamonds
    // under it. Anchor and place the holder it returns.
    private static RectTransform Rank(RectTransform parent, string id, float size, float alpha)
    {
        var holder = FeelUI.Rect("Rank", parent);
        holder.sizeDelta = new Vector2(size, size);
        var picture = new GameObject("Insignia", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        picture.transform.SetParent(holder, false);
        picture.raycastTarget = false;
        picture.texture = ClassGrowth.Insignia(ClassGrowth.RankNow(id));
        picture.color = new Color(1f, 1f, 1f, picture.texture != null ? alpha : 0f);
        picture.rectTransform.sizeDelta = new Vector2(size, size);
        int pips = Mathf.Min(ClassGrowth.PrestigeNow(id), ClassGrowth.MostPips);
        float pip = Mathf.Max(6f, size * .11f);
        for (int i = 0; i < pips; i++)
        {
            var mark = FeelUI.Rect("Prestige", holder);
            KitStyle.Fill(mark, KitStyle.Accent).raycastTarget = false;
            mark.sizeDelta = new Vector2(pip, pip);
            mark.anchoredPosition = new Vector2((i - (pips - 1) * .5f) * pip * 1.8f, -size * .5f - pip * .3f);
            mark.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }
        return holder;
    }

    /// <summary>A class's icon's name in Resources/UI/Icons.</summary>
    public static string Icon(string id) => "Class_" + id;

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

    // What the class's ability does, with its numbers from Tables/ClassAbilities.
    private static string Ability(PlayerClass.Info info)
    {
        string key = "class." + info.Id + ".ability";
        switch (info.Id)
        {
            case PlayerClass.Assault:
                return Texts.Format(key, Whole("Assault", "stackMax", 8f), Whole("Assault", "damagePerStack", 3f), Whole("Assault", "speedPerStack", 1.5f),
                    Whole("Assault", "chargeSeconds", 7f), Whole("Assault", "chargeRadius", 15f), Whole("Assault", "chargeDamage", 25f),
                    Whole("Assault", "chargeSpeed", 25f), ClassKey());
            case PlayerClass.Point:
                return Texts.Format(key, Whole("Point", "reloadShare", 1f));
            case PlayerClass.Ghost:
                return Texts.Format(key, Whole("Ghost", "hunterMax", 5f), Whole("Ghost", "hunterDamage", 7f), Whole("Ghost", "hunterBodyLoss", 1f),
                    Whole("Ghost", "deadeye", 100f), Whole("Ghost", "stumbleBody", .8f), Whole("Ghost", "stumbleHead", 1.6f));
            case PlayerClass.Knight:
                float less = (1f - Mathf.Clamp01(PlayerClass.Ability("Knight", "taken", .75f))) * 100f;
                return Texts.Format(key, less.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                    Whole("Knight", "parryWindow", 50f), Whole("Knight", "parryPosture", 100f), Whole("Knight", "counter", 50f),
                    Whole("Knight", "fists", 50f), Whole("Knight", "tauntRadius", 12f), Whole("Knight", "tauntSeconds", 6f),
                    Whole("Knight", "tauntCooldown", 25f), ClassKey(), Whole("Knight", "breakFree", 6f));
            case PlayerClass.Schiesshund:
                return Texts.Format(key, Whole("Schiesshund", "markSeconds", 15f), Whole("Schiesshund", "markDamage", 25f),
                    Whole("Schiesshund", "bountyMoney", 100f), Whole("Schiesshund", "bountyElite", 300f), Whole("Schiesshund", "returnDamage", 6f),
                    Whole("Schiesshund", "returnSeconds", 8f), Whole("Schiesshund", "roundPoints", 2f), Whole("Schiesshund", "elitePoints", 2f),
                    Whole("Schiesshund", "desperadoDamage", 100f));
            case PlayerClass.Support:
                return Texts.Format(key, Whole("Support", "supplyMax", 100f), Whole("Support", "supplyRegen", 1.5f),
                    Whole("Support", "resupplyReach", 3.5f), Whole("Support", "resupplyCost", 25f), Whole("Support", "resupplyMagazines", 2f),
                    Whole("Support", "resupplyOverfill", 25f), ClassKey(), Whole("Support", "barricadeCost", 50f),
                    Whole("Support", "barricadeHealth", 400f), Whole("Support", "barricadeMax", 2f));
            case PlayerClass.Detente:
                return Texts.Format(key, Whole("Detente", "syringeMax", 100f), Whole("Detente", "injectHeal", 40f), Whole("Detente", "injectCost", 25f),
                    ClassKey(), Whole("Detente", "treatSeconds", 6f), Whole("Detente", "treatHeal", 50f), Whole("Detente", "goldenSpeed", 8f),
                    Whole("Detente", "goldenSeconds", 8f), Whole("Detente", "resurrectSeconds", 5f), Whole("Detente", "resurrectHealth", 50f));
            default:
                return Texts.Has(key) ? Texts.Get(key) : "";
        }
    }

    private static string Whole(string row, string name, float fallback)
        => PlayerClass.Ability(row, name, fallback).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    // The class key as the player has it bound (the wheel click unless changed in Settings → 조작).
    private static string ClassKey() => KeyBindings.Name(UnityEngine.InputSystem.InputSystem.actions != null
        ? UnityEngine.InputSystem.InputSystem.actions.FindAction("Player/ClassAbility", false) : null);
}
