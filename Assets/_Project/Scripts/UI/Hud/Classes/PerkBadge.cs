using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// A held perk as a small slanted badge: a bar, its icon, its name and a pip per level (the levels it has lit). The bar
/// and the lit pips wear the colour of the deck the card belongs to, its class's or everyone's (ClassLook.CardColour,
/// Tables/Classes/ClassColours.csv), so a row of badges reads class by class at a glance (the user, 2026-11-18:
/// "클래스별 고유 색상을 부여해 카드를 분류·정렬"); they were all the HUD's pink. Lined up in a centred row for the card
/// screen's tray and the Tab screen's "내 능력" (PerkCardScreen, ScoreboardHUD). Where the screen has a pointer a badge
/// can say what its card does when pointed at (hover: PerkTooltip).
/// </summary>
public static class PerkBadge
{
    private const float PipWidth = 14f, PipHeight = 4f, PipGap = 4f, Slant = 8f;

    /// <summary>
    /// Fills <paramref name="row"/> (centred on its pivot) with a badge per held perk, <paramref name="gap"/> px apart;
    /// returns each badge by perk id. Clears whatever the row held. With <paramref name="hover"/> each badge takes the
    /// pointer and shows its card's lines while pointed at. Badges that would run past <paramref name="most"/> px (0 = any
    /// width) go onto a further line under the first, each line centred, so a full deck stays on the screen: a class's
    /// ten cards and everyone's seven are some 2500 px side by side.
    /// </summary>
    public static Dictionary<string, RectTransform> Row(RectTransform row, IEnumerable<(PlayerPerks.Perk perk, int level)> held, float height, float gap,
        bool hover = false, float most = 0f)
    {
        for (int i = row.childCount - 1; i >= 0; i--) Object.Destroy(row.GetChild(i).gameObject);
        var made = new Dictionary<string, RectTransform>();
        var badges = new List<RectTransform>();
        foreach (var (perk, level) in held)
        {
            var badge = Make(row, perk, level, height, hover);
            made[perk.Id] = badge;
            badges.Add(badge);
        }
        // Line by line: as many badges as fit the room, each line centred on the row and the next under it.
        int from = 0;
        float down = 0f;
        while (from < badges.Count)
        {
            int to = from;
            float width = 0f;
            while (to < badges.Count)
            {
                float wider = width + (to > from ? gap : 0f) + badges[to].sizeDelta.x;
                if (most > 0f && to > from && wider > most) break;
                width = wider;
                to++;
            }
            float x = -width * .5f;
            for (int i = from; i < to; i++)
            {
                badges[i].anchoredPosition = new Vector2(x + badges[i].sizeDelta.x * .5f, -down);
                x += badges[i].sizeDelta.x + gap;
            }
            down += height + gap;
            from = to;
        }
        return made;
    }

    /// <summary>
    /// One badge, centred on its pivot, as wide as it needs. With <paramref name="hover"/> its plate takes the pointer,
    /// where it is drawn (the slant included: KitSkew), and shows the card's lines meanwhile.
    /// </summary>
    public static RectTransform Make(Transform parent, PlayerPerks.Perk perk, int level, float height, bool hover = false)
    {
        var badge = FeelUI.Rect("Perk " + perk.Id, parent);
        badge.gameObject.AddComponent<CanvasGroup>();
        float iconSize = height * .62f, pad = height * .22f;
        var label = KitStyle.Flat(KitStyle.TopLeft("Name", badge, Vector2.zero, new Vector2(10f, height * .6f)), perk.Name, height * .3f,
            TextAlignmentOptions.MidlineLeft);
        float nameWidth = label.GetPreferredValues(perk.Name).x;
        int pips = Mathf.Max(1, perk.Levels);
        float pipsWidth = pips * PipWidth + (pips - 1) * PipGap;
        float textWidth = Mathf.Max(nameWidth, pipsWidth);
        float width = pad + 4f + iconSize + pad * .7f + textWidth + pad * 1.4f;
        badge.sizeDelta = new Vector2(width, height);

        // The colour of the deck the card belongs to: its class's, or everyone's.
        Color deck = ClassLook.CardColour(perk.Class);
        var back = KitStyle.Fill(KitStyle.Stretch("Back", badge), KitStyle.With(KitStyle.Night, .86f));
        back.transform.SetAsFirstSibling();
        KitSkew.On(back, Slant);
        KitSkew.On(KitStyle.Bar("Bar", badge, Vector2.zero, new Vector2(4f, height), deck), Slant);
        if (hover)
        {
            // The plate itself is what takes the pointer, as drawn (its KitSkew filters the square it leans out of).
            back.raycastTarget = true;
            var shows = badge.gameObject.AddComponent<PerkTooltip.Hover>();
            shows.Perk = perk;
            shows.Level = level;
        }
        var icon = KitButtons.IconImage(badge, perk.Icon, iconSize, KitStyle.Text);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, .5f);
        icon.rectTransform.anchoredPosition = new Vector2(pad + 4f + iconSize * .5f, 0f);
        float textX = pad + 4f + iconSize + pad * .7f;
        var nameBox = (RectTransform)label.rectTransform.parent;
        nameBox.anchoredPosition = new Vector2(textX, -height * .1f);
        nameBox.sizeDelta = new Vector2(textWidth + 4f, height * .5f);
        for (int i = 0; i < pips; i++)
            KitStyle.Bar("Pip", badge, new Vector2(textX + i * (PipWidth + PipGap), height * .66f), new Vector2(PipWidth, PipHeight),
                i < level ? deck : KitStyle.Faint);
        return badge;
    }
}
