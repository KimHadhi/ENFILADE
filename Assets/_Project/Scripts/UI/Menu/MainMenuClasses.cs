using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The title screen's 클래스 page (stage 7, PlayerClass): the seven classes down the left as slanted plates, each at its
/// own lean and indent (KitStack keeps room between them), the one played dark with a pink edge and bar, those whose
/// abilities come later marked 준비 중 (they can still be played); its rank (ClassGrowth) at the right of each. At the
/// right, the class pointed at or played: its name, what it does, its starting guns, its own guns and their bonus, and
/// its ability in its numbers. A click plays that class from the next run. The records page's look.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuClasses : MonoBehaviour
{
    private const float LeftWidth = 600f, PlateHeight = 74f, PlateGap = 18f, Slant = 16f, RightX = 860f, RightWidth = 880f;
    private static readonly float[] Tilts = { 1.2f, -1f, .8f, -1.3f, 1f, -.8f, 1.3f }, Indents = { 0f, 24f, 8f, 30f, 14f, 4f, 26f };

    private RectTransform root, content;
    private TextMeshProUGUI detailTitle, detailRole, detailBody;
    private float edge;
    private string pointed;

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
    public void Open()
    {
        if (content != null) Destroy(content.gameObject);
        root.gameObject.SetActive(true);
        content = KitStyle.Stretch("Content", root);
        var reveal = content.gameObject.AddComponent<KitReveal>();
        reveal.Hide();
        pointed = null;
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
            Plate(catalog[i], new Vector2(edge + x, y), tilt, reveal, at);
            at += .05f;
            lastX = x;
            lastTilt = tilt;
        }
        BuildDetail(reveal, at);
        Point(PlayerClass.CurrentId);
        reveal.Open();
    }

    public void Close()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

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
        KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(28f, 6f), new Vector2(260f, 40f)), info.Title, 30f,
            TextAlignmentOptions.MidlineLeft, chosen ? KitStyle.Text : KitStyle.With(KitStyle.Text, .85f));
        var role = KitStyle.Flat(KitStyle.TopLeft("Role", plate, new Vector2(30f, 44f), new Vector2(LeftWidth - 170f, 22f)), info.Role, 14f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        role.fontStyle = FontStyles.Normal;
        role.overflowMode = CombatUIAssets.ClippedOverflow(role);
        // Its rank, and 준비 중 for one whose abilities come later.
        string side = Texts.Format("classes.rank", ClassGrowth.RankNow(info.Id));
        KitStyle.Flat(KitStyle.TopLeft("Rank", plate, new Vector2(LeftWidth - 150f, 8f), new Vector2(120f, 26f)), side, 15f,
            TextAlignmentOptions.MidlineRight, chosen ? KitStyle.Accent : KitStyle.Soft);
        if (!info.Ready)
        {
            var later = KitStyle.TopLeft("Later", plate, new Vector2(LeftWidth - 128f, 40f), new Vector2(96f, 24f));
            KitSkew.On(KitStyle.Fill(later, KitStyle.With(KitStyle.Ice, .12f)), 6f);
            KitStyle.Flat(later, Texts.Get("classes.later"), 13f, TextAlignmentOptions.Center, KitStyle.Soft);
        }
        else if (chosen)
        {
            var mark = KitStyle.TopLeft("Chosen", plate, new Vector2(LeftWidth - 128f, 40f), new Vector2(96f, 24f));
            KitSkew.On(KitStyle.Fill(mark, KitStyle.Accent), 6f);
            KitStyle.Flat(mark, Texts.Get("classes.chosen"), 13f, TextAlignmentOptions.Center, KitStyle.Ink);
        }
        var lid = KitStyle.Fill(KitStyle.Stretch("Flash", plate), KitStyle.Flash);
        KitSkew.On(lid, Slant);
        reveal.Card(plate, lid, at);
        var button = plate.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        string id = info.Id;
        button.onClick.AddListener(() => Pick(id));
        plate.gameObject.AddComponent<KitHover>();
        var trigger = plate.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => Point(id));
        trigger.triggers.Add(enter);
    }

    // Plays this class from the next run: the page is built again to show it.
    private void Pick(string id)
    {
        if (PlayerClass.Is(id)) return;
        ClassGrowth.Choose(id);
        KitSound.Play(KitSound.Cue.Buy, .5f);
        Open();
    }

    private void BuildDetail(KitReveal reveal, float at)
    {
        var panel = KitStyle.TopLeft("Detail", content, new Vector2(RightX, 160f), new Vector2(RightWidth, 560f));
        KitStyle.Cut(panel, KitStyle.With(KitStyle.Ice, .05f));
        detailTitle = KitStyle.Flat(KitStyle.TopLeft("Name", panel, new Vector2(34f, 22f), new Vector2(RightWidth - 68f, 60f)), "", 46f,
            TextAlignmentOptions.MidlineLeft);
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
    private void Point(string id)
    {
        if (id == pointed || detailTitle == null) return;
        pointed = id;
        var info = PlayerClass.Find(id);
        if (info == null) return;
        detailTitle.text = info.Title;
        detailRole.text = info.Role;
        string weapons = info.Weapons.Length > 0 ? string.Join(" · ", info.Weapons) : "-";
        string body = Texts.Format("classes.loadout", Name(info.Primary), Name(info.Secondary)) + "\n"
            + (info.GunBonus > 0f ? Texts.Format("classes.weapons", weapons, info.GunBonus.ToString("0")) + "\n" : "")
            + "\n<color=#" + ColorUtility.ToHtmlStringRGB(KitStyle.Accent) + ">" + Texts.Get("classes.ability") + "</color>\n" + Ability(info);
        if (!info.Ready) body += "\n\n<color=#" + ColorUtility.ToHtmlStringRGB(KitStyle.Soft) + ">" + Texts.Get("classes.later_note") + "</color>";
        detailBody.text = body;
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

    // What the class's ability does, with its numbers from Tables/ClassAbilities.
    private static string Ability(PlayerClass.Info info)
    {
        string key = "class." + info.Id + ".ability";
        switch (info.Id)
        {
            case PlayerClass.Assault:
                return Texts.Format(key, Whole("Assault", "stackMax", 8f), Whole("Assault", "damagePerStack", 3f), Whole("Assault", "speedPerStack", 1.5f),
                    Whole("Assault", "chargeSeconds", 7f), Whole("Assault", "chargeRadius", 15f), Whole("Assault", "chargeDamage", 25f),
                    Whole("Assault", "chargeSpeed", 25f), KeyBindings.Name(UnityEngine.InputSystem.InputSystem.actions != null
                        ? UnityEngine.InputSystem.InputSystem.actions.FindAction("Player/ClassAbility", false) : null));
            case PlayerClass.Point:
                return Texts.Format(key, Whole("Point", "reloadShare", 1f));
            default:
                return Texts.Has(key) ? Texts.Get(key) : "";
        }
    }

    private static string Whole(string row, string name, float fallback)
        => PlayerClass.Ability(row, name, fallback).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
