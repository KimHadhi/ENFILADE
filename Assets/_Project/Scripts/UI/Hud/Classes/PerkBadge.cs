using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// A held perk as a small slanted badge: a pink bar, its icon, its name and a pip per level (the levels it has in pink).
/// Lined up in a centred row for the card screen's tray and the Tab screen's "내 능력" (PerkCardScreen, ScoreboardHUD).
/// </summary>
public static class PerkBadge
{
    private const float PipWidth = 14f, PipHeight = 4f, PipGap = 4f, Slant = 8f;

    /// <summary>
    /// Fills <paramref name="row"/> (centred on its pivot) with a badge per held perk, <paramref name="gap"/> px apart;
    /// returns each badge by perk id. Clears whatever the row held.
    /// </summary>
    public static Dictionary<string, RectTransform> Row(RectTransform row, IEnumerable<(PlayerPerks.Perk perk, int level)> held, float height, float gap)
    {
        for (int i = row.childCount - 1; i >= 0; i--) Object.Destroy(row.GetChild(i).gameObject);
        var made = new Dictionary<string, RectTransform>();
        var badges = new List<RectTransform>();
        float total = 0f;
        foreach (var (perk, level) in held)
        {
            var badge = Make(row, perk, level, height);
            made[perk.Id] = badge;
            badges.Add(badge);
            total += badge.sizeDelta.x;
        }
        total += gap * Mathf.Max(0, badges.Count - 1);
        float x = -total * .5f;
        foreach (var badge in badges)
        {
            badge.anchoredPosition = new Vector2(x + badge.sizeDelta.x * .5f, 0f);
            x += badge.sizeDelta.x + gap;
        }
        return made;
    }

    /// <summary>One badge, centred on its pivot, as wide as it needs.</summary>
    public static RectTransform Make(Transform parent, PlayerPerks.Perk perk, int level, float height)
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

        var back = KitStyle.Fill(KitStyle.Stretch("Back", badge), KitStyle.With(KitStyle.Night, .86f));
        back.transform.SetAsFirstSibling();
        KitSkew.On(back, Slant);
        KitSkew.On(KitStyle.Bar("Bar", badge, Vector2.zero, new Vector2(4f, height), KitStyle.Accent), Slant);
        var icon = KitButtons.IconImage(badge, perk.Icon, iconSize, KitStyle.Text);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, .5f);
        icon.rectTransform.anchoredPosition = new Vector2(pad + 4f + iconSize * .5f, 0f);
        float textX = pad + 4f + iconSize + pad * .7f;
        var nameBox = (RectTransform)label.rectTransform.parent;
        nameBox.anchoredPosition = new Vector2(textX, -height * .1f);
        nameBox.sizeDelta = new Vector2(textWidth + 4f, height * .5f);
        for (int i = 0; i < pips; i++)
            KitStyle.Bar("Pip", badge, new Vector2(textX + i * (PipWidth + PipGap), height * .66f), new Vector2(PipWidth, PipHeight),
                i < level ? KitStyle.Accent : KitStyle.Faint);
        return badge;
    }
}
