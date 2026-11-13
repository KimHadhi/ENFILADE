using TMPro;
using UnityEngine;
using UnityEngine.UI;

// MainMenuWardrobe: The kit's pieces as this page uses them. One part of the class; the rest is in MainMenuWardrobe.cs and the files beside it.
public sealed partial class MainMenuWardrobe
{
    // A page's big title, and the line under it, which shows only once the title's pink has drained.
    private void Title(RectTransform parent, float x, string text, float tilt, string under, Vector2 underAt, float underWidth)
    {
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", parent, new Vector2(x, 26f), new Vector2(parent == content ? LeftWidth : RightWidth, 80f)),
            text, 64f, TextAlignmentOptions.MidlineLeft);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, tilt);
        float at = growAt;
        Grow(title.rectTransform, 0f);
        var line = Note(parent, underAt, underWidth, under);
        if (reveal == null) return;
        // Shown at once, the line lay under the title's tilted pink while it opened (the user, 2026-11-12: "분홍색
        // 네모칸이랑 저 아래 글자랑 겹쳐서 … 딜레이 줘서"): it grows in from its left after, with no pink of its own.
        var clear = KitStyle.Fill(KitStyle.Stretch("Flash", line.rectTransform), Color.clear);
        reveal.Card(line.rectTransform, clear, at + KitReveal.GrowSeconds + KitReveal.FlashHold + KitReveal.DrainSeconds);
    }

    private static TextMeshProUGUI Note(RectTransform parent, Vector2 at, float width, string text, float size = 16f)
    {
        var note = KitStyle.Flat(KitStyle.TopLeft("Note", parent, at, new Vector2(width, size + 6f)), text, size, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        note.fontStyle = FontStyles.Normal;
        note.overflowMode = FontRoles.ClippedOverflow(note);
        return note;
    }

    // A small slanted chip; the chosen one solid pink. Without an action it only shows.
    private RectTransform Chip(RectTransform parent, Vector2 at, float width, string text, bool on, System.Action picked)
    {
        var chip = KitStyle.TopLeft("Chip " + text, parent, at, new Vector2(width, ChipHeight));
        var back = KitStyle.Fill(chip, on ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .09f));
        KitSkew.On(back, ChipSlant);
        back.raycastTarget = picked != null;
        var label = KitStyle.Flat(KitStyle.Stretch("Text", chip), text, 14f, TextAlignmentOptions.Center, on ? KitStyle.Ink : KitStyle.Text);
        label.overflowMode = FontRoles.ClippedOverflow(label);
        Grow(chip, ChipSlant);
        if (picked == null) return chip;
        var button = chip.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(() => picked());
        chip.gameObject.AddComponent<KitHover>();
        return chip;
    }

    // The pink 2 px edge of the worn piece, leaning as its plate does.
    private static void Rim(RectTransform plate, float slant)
    {
        var rim = KitStyle.Stretch("Edge", plate);
        rim.sizeDelta = new Vector2(2f, 2f);
        rim.SetAsFirstSibling();
        KitSkew.On(KitStyle.Fill(rim, KitStyle.With(KitStyle.Accent, .75f)), slant);
    }

    // Opening, each piece grows in under a pink lid a moment after the one before.
    private void Grow(RectTransform piece, float slant)
    {
        if (reveal == null) return;
        var lid = KitStyle.Fill(KitStyle.Stretch("Flash", piece), KitStyle.Flash);
        if (slant != 0f) KitSkew.On(lid, slant);
        reveal.Card(piece, lid, growAt);
        growAt += .015f;
    }
}
