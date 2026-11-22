using TMPro;
using UnityEngine;
using W = SettingsWidgets;

/// <summary>
/// The little pictures the HUD settings put on a choice (HudPageRows.Pictures): the bottom or the side of a screen drawn in
/// plain boxes, the thing the choice moves lit white, so which is which shows without a word. Each draws into a box
/// <see cref="Wide"/> by <see cref="Tall"/>.
/// </summary>
public static class HudPictures
{
    public const float Wide = 128f, Tall = 76f;
    private static readonly Color Faint = new Color(.36f, .36f, .37f), Dim = new Color(.2f, .2f, .21f);

    private static void Bit(RectTransform box, float x, float y, float w, float h, Color colour, float radius = 2f)
        => HudMenu.Box(KitStyle.TopLeft("Bit", box, new Vector2(x, y), new Vector2(w, h)), colour, false, radius);

    private static void Word(RectTransform box, string text, float x, float y, float size, Color colour)
    {
        var label = W.Label(KitStyle.TopLeft("Word", box, new Vector2(x, y), new Vector2(60f, size + 4f)), text, size, colour);
        KitStyle.Stretch(label.rectTransform);
        label.fontStyle = FontStyles.Bold;
    }

    // The player's corner: the magazine, the photo, the bars.
    private static void Corner(RectTransform box, float lift, float drop, Color photo)
    {
        Bit(box, 14f, 16f - lift, 5f, 30f, photo);
        Bit(box, 22f, 16f - lift, 30f, 30f, photo);
        Bit(box, 14f, 50f, 62f, 5f, photo);
        Bit(box, 14f, 57f + drop, 62f, 5f, photo);
    }

    public static void SpacingDefault(RectTransform box) => Corner(box, 0f, 0f, HudMenu.Lit);

    public static void SpacingWide(RectTransform box) => Corner(box, 6f, 4f, HudMenu.Lit);

    public static void ClassCentre(RectTransform box)
    {
        Corner(box, 0f, 0f, Dim);
        Bit(box, 52f, 62f, 8f, 8f, HudMenu.Lit);
        Bit(box, 62f, 65f, 34f, 4f, HudMenu.Lit);
    }

    public static void ClassPortrait(RectTransform box)
    {
        Corner(box, 0f, 0f, Dim);
        Bit(box, 56f, 37f, 8f, 8f, HudMenu.Lit);
        Bit(box, 66f, 40f, 34f, 4f, HudMenu.Lit);
    }

    public static void SlotPlates(RectTransform box)
    {
        Bit(box, 96f, 10f, 24f, 11f, Faint);
        Bit(box, 88f, 24f, 32f, 14f, HudMenu.Lit);
        Bit(box, 96f, 41f, 24f, 11f, Faint);
        Bit(box, 96f, 55f, 24f, 11f, Faint);
    }

    public static void SlotCards(RectTransform box)
    {
        Bit(box, 22f, 40f, 18f, 14f, Faint);
        Bit(box, 43f, 34f, 38f, 20f, HudMenu.Lit);
        Bit(box, 84f, 40f, 18f, 14f, Faint);
        Bit(box, 105f, 40f, 18f, 14f, Faint);
        Bit(box, 78f, 60f, 44f, 10f, Dim);
    }

    private static void Foe(RectTransform box)
    {
        Bit(box, 30f, 14f, 16f, 16f, Dim, 8f);
        Bit(box, 22f, 32f, 32f, 44f, Dim, 8f);
    }

    public static void NumbersOff(RectTransform box) => Foe(box);

    public static void NumbersTotal(RectTransform box)
    {
        Foe(box);
        Word(box, "102", 62f, 24f, 24f, HudMenu.Lit);
    }

    public static void NumbersEach(RectTransform box)
    {
        Foe(box);
        Word(box, "34", 60f, 44f, 15f, HudMenu.Muted);
        Word(box, "34", 72f, 27f, 17f, HudMenu.Text);
        Word(box, "34", 86f, 8f, 19f, HudMenu.Lit);
    }

    public static void AlertCard(RectTransform box)
    {
        Bit(box, 36f, 12f, 56f, 30f, HudMenu.Lit, 3f);
        Bit(box, 44f, 22f, 40f, 4f, HudMenu.Ink, 1f);
        Bit(box, 50f, 30f, 28f, 3f, HudMenu.Ink, 1f);
    }

    public static void AlertRibbon(RectTransform box)
    {
        Bit(box, 0f, 18f, Wide, 14f, Faint, 0f);
        Bit(box, 38f, 23f, 52f, 4f, HudMenu.Lit, 1f);
    }
}
