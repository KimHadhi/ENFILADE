using UnityEngine;

// MainMenuWardrobe: Trying on: the choice under the pointer, after a short rest. One part of the class; the rest is in MainMenuWardrobe.cs and the files beside it.
public sealed partial class MainMenuWardrobe
{
    // The choice under the pointer: the look it would make, its slot, whether it takes the slot's pieces off, and what
    // the badge says a click does.
    private void Want(OutfitLook look, string slot, bool removes, string line)
    {
        wanted = look;
        wantedSlot = slot;
        wantedRemoves = removes;
        wantedLine = line;
        wantedText = look != null ? look + "|" + slot + "|" + removes : null;
        wantedAt = Time.unscaledTime + TryAfter;
    }

    private void Update()
    {
        if (preview != null && preview.CanTry && wantedText != triedText && Time.unscaledTime >= wantedAt)
        {
            triedText = wantedText;
            preview.Try(wanted, wantedSlot, wantedRemoves);
        }
        TickPresets();
        ShowBadge();
    }

    // The badge is up while a try is on the character, saying what a click would do, gently beating.
    private void ShowBadge()
    {
        if (badge == null) return;
        bool trying = triedText != null;
        if (badge.gameObject.activeSelf != trying) badge.gameObject.SetActive(trying);
        if (!trying) return;
        if (shownLine != wantedLine)
        {
            shownLine = wantedLine;
            badgeLine.text = wantedLine ?? "";
        }
        badgeGroup.alpha = .82f + .18f * Mathf.Sin(Time.unscaledTime * 5f);
    }

    // Back to the saved outfit with nothing marked: leaving a slot's list, the page, or the character.
    private void StopTrying()
    {
        wanted = null;
        wantedText = triedText = wantedSlot = wantedLine = null;
        wantedRemoves = false;
        if (preview == null) return;
        preview.Try(null, null, false);
        preview.Highlight(null);
    }

    private void Save(PlayerBodyProfile.Skin skin, OutfitLook look)
    {
        OutfitLook.Save(skin.Id, look);
        Commit();
    }

    // Worn and kept at once, as the menu's other choices are: the preview and the body follow GameSettings.Changed.
    private void Commit()
    {
        GameSettings.Apply();
        GameSettings.Save();
        KitSound.Play(KitSound.Cue.Buy, .4f);
        Build();
    }

    // " · 검정" after an item worn in a colour other than its first.
    private static string ColourNote(PlayerBodyProfile.Skin skin, OutfitItem item, string colour)
    {
        var entry = OutfitCatalog.Model(item.id, skin.Id);
        if (entry == null || entry.Variants.Count < 2) return "";
        var set = entry.VariantOf(colour);
        return set != null && set != entry.Variants[0]
            ? "<color=#" + ColorUtility.ToHtmlStringRGBA(KitStyle.Soft) + ">  ·  " + set.DisplayName + "</color>" : "";
    }
}
