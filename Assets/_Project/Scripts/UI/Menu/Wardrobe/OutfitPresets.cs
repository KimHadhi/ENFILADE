using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The wardrobe's presets: a whole look kept under a name, worn again in a click (the user, 2026-11-13: "옷장 내가 지금까지
/// 옷 입혀서 꾸민 커스텀 프리셋으로 만들어서 저장할 수 있게 해 줘"). One keeps the character, its colour set and the outfit as
/// OutfitLook writes it (every slot, the underwear and the accessories, and the body options "shape:…"), and a portrait of
/// the character dressed so, taken in the wardrobe's photo studio (OutfitStudio.StandDressed). They are this PC's: in
/// persistentDataPath/OutfitPresets, presets.json and a PNG for each.
/// Worn, a preset becomes the character's outfit in the settings, as a pick in the wardrobe does, so the body, the title
/// screen's character and the other PCs (PlayerNetworkPose sends the skin and its outfit) all follow. A pick of an item
/// the game no longer has, or has no more for that character (a mod taken out, an item renamed), only empties that slot
/// (the character's own piece shows); an item that moved or was cut up is found where it went (OutfitLook.Settled); a
/// colour set that is gone leaves the item in its first. The rest is worn as saved.
/// </summary>
public static class OutfitPresets
{
    /// <summary>The portraits' size (px): a card standing up, as the character cards are (2:3).</summary>
    public const int PortraitWidth = 192, PortraitHeight = 288;
    private const string Folder = "OutfitPresets", ListFile = "presets.json";
    // How far from above the portrait looks (°): a little, as the title screen's camera does.
    private const float PortraitPitch = 4f;
    // A name's most characters.
    public const int MostName = 24;

    [Serializable]
    private sealed class Stored
    {
        public List<OutfitPreset> presets = new List<OutfitPreset>();
    }

    private static List<OutfitPreset> presets;
    private static readonly Dictionary<string, Texture2D> portraits = new Dictionary<string, Texture2D>(StringComparer.Ordinal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        presets = null;
        foreach (var picture in portraits.Values)
            if (picture != null) UnityEngine.Object.Destroy(picture);
        portraits.Clear();
    }

    private static string Root => Path.Combine(Application.persistentDataPath, Folder);
    private static string PortraitPath(OutfitPreset preset) => Path.Combine(Root, preset.id + ".png");

    /// <summary>Every preset, the last saved first.</summary>
    public static IReadOnlyList<OutfitPreset> All
    {
        get
        {
            Load();
            return presets;
        }
    }

    // ---- Keeping ----

    /// <summary>
    /// Keeps the character's look as it is now (the settings' skin, colour set and saved outfit) under
    /// <paramref name="name"/> (empty: the character's name and the next number), and takes its portrait.
    /// </summary>
    public static OutfitPreset Add(string name, PlayerBodyProfile.Skin skin)
    {
        if (skin == null) return null;
        Load();
        var preset = new OutfitPreset { id = Guid.NewGuid().ToString("N").Substring(0, 12) };
        Fill(preset, skin);
        preset.name = Named(name, skin);
        presets.Insert(0, preset);
        Write();
        TakePortrait(preset);
        return preset;
    }

    /// <summary>The preset takes the character's look as it is now, and a new portrait.</summary>
    public static void Overwrite(OutfitPreset preset, PlayerBodyProfile.Skin skin)
    {
        if (preset == null || skin == null) return;
        Load();
        Fill(preset, skin);
        presets.Remove(preset);
        presets.Insert(0, preset);
        Write();
        TakePortrait(preset);
    }

    public static void Rename(OutfitPreset preset, string name)
    {
        if (preset == null) return;
        string kept = Clean(name);
        if (kept.Length == 0 || kept == preset.name) return;
        preset.name = kept;
        Write();
    }

    /// <summary>The preset goes, and its portrait.</summary>
    public static void Remove(OutfitPreset preset)
    {
        if (preset == null) return;
        Load();
        presets.Remove(preset);
        Write();
        Forget(preset);
        try
        {
            string path = PortraitPath(preset);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[OutfitPresets] " + problem.Message);
        }
    }

    // The settings' look of the character, as worn now.
    private static void Fill(OutfitPreset preset, PlayerBodyProfile.Skin skin)
    {
        preset.skin = skin.Id;
        var colour = skin.VariantOf(GameSettings.Current.characterVariant);
        preset.colour = colour != null ? colour.Id : "";
        preset.look = OutfitLook.Saved(skin.Id).ToString();
        preset.saved = DateTime.UtcNow.Ticks;
    }

    // The name as typed, kept to one line and MostName characters; empty: "마요 3".
    private static string Named(string typed, PlayerBodyProfile.Skin skin)
    {
        string name = Clean(typed);
        if (name.Length > 0) return name;
        int number = 1;
        foreach (var preset in presets) if (preset.skin == skin.Id) number++;
        return Texts.Format("wardrobe.presets.default", skin.DisplayName, number);
    }

    private static string Clean(string text)
    {
        string name = (text ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return name.Length > MostName ? name.Substring(0, MostName).TrimEnd() : name;
    }

    // ---- Wearing ----

    /// <summary>The preset's character, or null when the game no longer has it.</summary>
    public static PlayerBodyProfile.Skin SkinOf(OutfitPreset preset)
    {
        var profile = PlayerBodyProfile.Load();
        if (profile == null || preset == null) return null;
        foreach (var skin in profile.Skins)
            if (skin != null && skin.Model != null && string.Equals(skin.Id, preset.skin, StringComparison.Ordinal)) return skin;
        return null;
    }

    /// <summary>
    /// Wears the preset: its character and colour set chosen and its outfit saved for the character, as this game can wear
    /// it (<see cref="Wearable"/>). The caller applies and saves the settings, as any pick in the wardrobe does. False when
    /// its character is gone. <paramref name="emptied"/> counts the slots whose item the game no longer has.
    /// </summary>
    public static bool Wear(OutfitPreset preset, out int emptied)
    {
        emptied = 0;
        var skin = SkinOf(preset);
        if (skin == null) return false;
        var look = Wearable(skin, preset.look, out emptied);
        var settings = GameSettings.Current;
        settings.characterSkin = skin.Id;
        var colour = skin.VariantOf(preset.colour);
        settings.characterVariant = colour != null ? colour.Id : "";
        OutfitLook.Save(skin.Id, look);
        return true;
    }

    /// <summary>Whether the character wears just this preset now (as this game can wear it).</summary>
    public static bool IsWorn(OutfitPreset preset)
    {
        var skin = SkinOf(preset);
        if (skin == null) return false;
        var settings = GameSettings.Current;
        if (!string.Equals(settings.characterSkin, skin.Id, StringComparison.Ordinal)) return false;
        var colour = skin.VariantOf(preset.colour);
        var worn = skin.VariantOf(settings.characterVariant);
        if (colour != worn) return false;
        return Wearable(skin, preset.look, out _).ToString() == OutfitLook.Saved(skin.Id).ToString();
    }

    /// <summary>
    /// A look as this game can wear it on <paramref name="skin"/>: moved or cut-up items where they went, and each pick of
    /// an item the game no longer has, or has no more for this character, or of a slot that is gone, dropped (that slot
    /// shows the character's own piece; <paramref name="emptied"/> counts them). A colour set gone leaves the item in its
    /// first. Taking a piece off (-) and the body options stay as saved.
    /// </summary>
    public static OutfitLook Wearable(PlayerBodyProfile.Skin skin, string text, out int emptied)
    {
        emptied = 0;
        var look = OutfitLook.Parse(text).Settled();
        var wearable = new OutfitLook();
        foreach (string slot in look.Slots)
        {
            if (!look.TryGet(slot, out var pick)) continue;
            if (slot.StartsWith(BodyShapes.Prefix, StringComparison.Ordinal))
            {
                wearable.Set(slot, pick.Item, pick.Colour);
                continue;
            }
            if (OutfitCatalog.Slot(slot) == null)
            {
                emptied++;
                continue;
            }
            if (pick.IsOff)
            {
                wearable.Set(slot, pick.Item);
                continue;
            }
            var item = OutfitCatalog.Item(pick.Item);
            var entry = item != null && skin != null ? OutfitCatalog.Model(item.id, skin.Id) : null;
            if (entry == null || entry.Model == null)
            {
                emptied++;
                continue;
            }
            bool colourThere = false;
            foreach (var set in entry.Variants)
                if (set != null && string.Equals(set.Id, pick.Colour, StringComparison.Ordinal)) colourThere = true;
            wearable.Set(slot, item.id, colourThere ? pick.Colour : "");
        }
        return wearable;
    }

    // ---- Portraits ----

    /// <summary>The preset's portrait, or null when it has none (taken on this PC, or the file is gone).</summary>
    public static Texture Portrait(OutfitPreset preset)
    {
        if (preset == null) return null;
        if (portraits.TryGetValue(preset.id, out var known)) return known;
        Texture2D picture = null;
        try
        {
            string path = PortraitPath(preset);
            if (System.IO.File.Exists(path))
            {
                picture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Preset " + preset.name, wrapMode = TextureWrapMode.Clamp };
                if (!picture.LoadImage(System.IO.File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(picture);
                    picture = null;
                }
            }
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[OutfitPresets] " + problem.Message);
            picture = null;
        }
        portraits[preset.id] = picture;
        return picture;
    }

    // The character standing whole in the photo studio, dressed as the preset says, upright and from a little above,
    // lit and exposed as the wardrobe's pictures are.
    private static void TakePortrait(OutfitPreset preset)
    {
        var skin = SkinOf(preset);
        if (skin == null) return;
        Forget(preset);
        var studio = new OutfitStudio();
        try
        {
            if (!studio.StandDressed(skin, skin.VariantOf(preset.colour), Wearable(skin, preset.look, out _))) return;
            var upright = new OutfitFraming
            {
                zoom = 1f, roll = -OutfitPictureLook.AutoTilt, pitch = PortraitPitch - OutfitPictureLook.AutoPitch, part = -1
            };
            studio.Frame(upright, PortraitWidth / (float)PortraitHeight);
            var picture = studio.Capture(PortraitWidth, PortraitHeight);
            if (picture == null) return;
            try
            {
                Directory.CreateDirectory(Root);
                System.IO.File.WriteAllBytes(PortraitPath(preset), picture.EncodeToPNG());
            }
            catch (Exception problem)
            {
                Debug.LogWarning("[OutfitPresets] " + problem.Message);
            }
            picture.name = "Preset " + preset.name;
            portraits[preset.id] = picture;
        }
        finally
        {
            studio.Dispose();
        }
    }

    private static void Forget(OutfitPreset preset)
    {
        if (!portraits.TryGetValue(preset.id, out var picture)) return;
        if (picture != null) UnityEngine.Object.Destroy(picture);
        portraits.Remove(preset.id);
    }

    // ---- The file ----

    private static void Load()
    {
        if (presets != null) return;
        presets = new List<OutfitPreset>();
        try
        {
            string path = Path.Combine(Root, ListFile);
            if (!System.IO.File.Exists(path)) return;
            var stored = JsonUtility.FromJson<Stored>(System.IO.File.ReadAllText(path));
            if (stored?.presets == null) return;
            foreach (var preset in stored.presets)
                if (preset != null && !string.IsNullOrEmpty(preset.id)) presets.Add(preset);
            presets.Sort((a, b) => b.saved.CompareTo(a.saved));
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[OutfitPresets] " + ListFile + ": " + problem.Message);
        }
    }

    private static void Write()
    {
        try
        {
            Directory.CreateDirectory(Root);
            var stored = new Stored { presets = presets ?? new List<OutfitPreset>() };
            System.IO.File.WriteAllText(Path.Combine(Root, ListFile), JsonUtility.ToJson(stored, true));
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[OutfitPresets] " + ListFile + ": " + problem.Message);
        }
    }
}

/// <summary>A kept look (OutfitPresets): its name, its character and colour set, the outfit as OutfitLook writes it.</summary>
[Serializable]
public sealed class OutfitPreset
{
    public string id = "", name = "", skin = "", colour = "", look = "";
    /// <summary>When it was last kept (UTC ticks).</summary>
    public long saved;
}
