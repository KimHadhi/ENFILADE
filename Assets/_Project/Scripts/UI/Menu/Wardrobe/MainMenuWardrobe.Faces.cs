using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// MainMenuWardrobe: the face page, the face the character holds while it is looked at. One part of the class; the rest is in MainMenuWardrobe.cs and the files beside it.
public sealed partial class MainMenuWardrobe
{
    // The face page (CharacterFace.ShowFace) stands where a slot's list would, under this name in openSlot. The user
    // (2026-11-13) found the title menu's character pulling random faces on its own and asked to pick the face in the
    // wardrobe after a preview instead ("차라리 내가 옷장에서 표정 미리보기로 설정할 수 있는 거였으면").
    private const string FacePage = "#face";

    private readonly struct FaceChoice
    {
        public readonly string Face, Name, Note, Group;

        public FaceChoice(string face, string name, string note, string group)
        {
            Face = face;
            Name = name;
            Note = note;
            Group = group;
        }
    }

    // The face page's plate in the left wing, saying which face is held now.
    private void FacePlate(PlayerBodyProfile.Skin skin, Vector2 at)
    {
        bool open = openSlot == FacePage;
        var plate = KitStyle.TopLeft("Face", content, at, new Vector2(LeftWidth, BodyPlateHeight));
        var back = KitStyle.Fill(KitStyle.Stretch("Back", plate), open ? KitStyle.With(KitStyle.Night, .95f) : KitStyle.With(KitStyle.Ice, .07f));
        KitSkew.On(back, Slant);
        back.raycastTarget = true;
        if (open) Rim(plate, Slant);
        KitSkew.On(KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(4f, BodyPlateHeight), KitStyle.Accent), Slant);
        KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(22f, 4f), new Vector2(260f, 32f)), Texts.Get("wardrobe.face.open"), 24f,
            TextAlignmentOptions.MidlineLeft);
        Note(plate, new Vector2(24f, 36f), LeftWidth - 90f, Texts.Format("wardrobe.face.open.note", FaceName(skin.Id, CharacterFace.SavedShowFace(skin.Id))), 14f);
        KitStyle.Flat(KitStyle.TopLeft("Arrow", plate, new Vector2(LeftWidth - 52f, 0f), new Vector2(32f, BodyPlateHeight)), "›", 34f,
            TextAlignmentOptions.Center, KitStyle.Accent);
        Grow(plate, Slant);
        if (open) return;
        var button = plate.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(OpenFaces);
        plate.gameObject.AddComponent<KitHover>();
    }

    private void OpenFaces()
    {
        openSlot = FacePage;
        StopTrying();
        KitSound.Play(KitSound.Cue.Ping, .35f);
        Build();
    }

    // Every face the character can hold, as plates: the cute face, the old way left to chance, the game's poses its keys
    // make, then its creator's own expressions under a heading. Resting on one holds it at once (only the face changes,
    // so this needs no wait); a press keeps it for the character.
    private void BuildFaces(RectTransform right, PlayerBodyProfile.Skin skin)
    {
        if (preview != null) preview.Focus("head");
        Chip(right, new Vector2(0f, 26f), 150f, "‹  " + Texts.Get("wardrobe.back"), false, ShowSlots);
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", right, new Vector2(0f, 66f), new Vector2(RightWidth, 62f)), Texts.Get("wardrobe.face.title"), 50f,
            TextAlignmentOptions.MidlineLeft);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);
        Grow(title.rectTransform, 0f);
        Note(right, new Vector2(4f, 128f), RightWidth, Texts.Get("wardrobe.face.where"));

        string worn = CharacterFace.SavedShowFace(skin.Id);
        var list = ScrollView("Faces", 162f);
        float y = 4f;
        int row = 0;
        string group = null;
        foreach (var choice in FaceChoices(skin.Id))
        {
            if (choice.Group != group)
            {
                group = choice.Group;
                if (group != null)
                {
                    Note(list, new Vector2(4f, y + 4f), RightWidth, Texts.Get(group), 14f);
                    y += 30f;
                }
            }
            string face = choice.Face;
            bool on = face == worn;
            var plate = Plate(list, new Vector2(row % 2 == 0 ? 0f : 10f, y), choice.Name, choice.Note, on, (row % 2 == 0 ? 1f : -1f) * .6f,
                on ? null : (System.Action)(() => PickFace(skin, face)), null);
            if (!on && preview != null) WardrobeHover.On(plate.gameObject, () => preview.TryFace(face), () => preview.TryFace(null));
            y += PlateHeight + PlateGap;
            row++;
        }
        Scroll(list, y);
    }

    private void PickFace(PlayerBodyProfile.Skin skin, string face)
    {
        CharacterFace.SaveShowFace(skin.Id, face);
        if (preview != null) preview.TryFace(null);
        Commit();
    }

    // The faces offered for a character, in order: the cute face and chance first, then the game's poses (Faces.csv rows
    // that pose now and then: pool), then the creator's expressions (FacePresets.csv, named as the creator named them).
    // Group is the heading's Texts id, null for the first two.
    private static List<FaceChoice> FaceChoices(string skinId)
    {
        var choices = new List<FaceChoice>
        {
            new FaceChoice("", Texts.Get("face.show.cute"), Texts.Get("face.show.cute.note"), null),
            new FaceChoice(CharacterFace.Random, Texts.Get("face.show.random"), Texts.Get("face.show.random.note"), null),
        };
        foreach (var mood in FaceTables.Moods)
        {
            if (mood.Pool <= 0f || (FaceTables.Own(skinId, mood.Id) == null && FaceTables.Fallback(mood.Id) == null)) continue;
            string key = "face.show." + mood.Id;
            choices.Add(new FaceChoice(mood.Id, Texts.Has(key) ? Texts.Get(key) : mood.Id, "", "wardrobe.face.game"));
        }
        foreach (string preset in FaceTables.Faces(skinId))
            choices.Add(new FaceChoice(CharacterFace.PresetMark + preset, preset.Replace('_', ' '), "", "wardrobe.face.creator"));
        return choices;
    }

    // The name a face goes by on its plate, for the left wing's line; the cute face's for an unknown one.
    private static string FaceName(string skinId, string face)
    {
        foreach (var choice in FaceChoices(skinId))
            if (choice.Face == face) return choice.Name;
        return Texts.Get("face.show.cute");
    }
}
