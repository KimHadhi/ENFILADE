using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// dev-text: file (log lines and console answers for whoever makes an outfit mod; nothing here is shown to players)

/// <summary>
/// Clothes players bring themselves: an outfit made in Unity for one character's body (what BOOTH sells for an avatar:
/// a jacket, a skirt and shoes on a copy of that avatar's bones) or a rigid accessory for anyone, packed by the
/// exporter (ModAvatarExporter) into a mod's Avatars folder as a bundle and a file saying what it is (ModAvatarFile).
/// <code>
/// Mods/
///   MaidDress/
///     Avatars/
///       maid-dress.mayo.bundle          the outfit's prefab, its meshes and textures
///       maid-dress.mayo.avatar.json     its items by slot, its materials and PhysBones, as its maker had them
///     Tables/…                          optional: rows of the game's tables for it, which win over what is worked out here
/// </code>
/// The user asked for it once characters could be brought (2026-11-14: "좀 더 유저에게 자유를 주고 유동적이고 개방적인
/// 방향이네. 좋아. 이런 거 좋아요"): a character brought whole could only be worn whole, and the wardrobe is where
/// players make a character their own. An outfit's model is offered beside the game's own (OutfitCatalog.Model, after
/// the OutfitLibrary), and each thing its file lists to wear (ModAvatarFile.Item) is a row laid under Outfits.csv, so
/// its jacket goes in the outer slot, its skirt in the bottom slot and its shoes in the shoes slot, each to be mixed
/// with any other clothes and fitted to them as the game's own are (OutfitFit). Nothing is loaded until a piece of it
/// is put on or photographed for its tile: then the bundle is opened, its materials are made as near to its maker's as
/// the game can draw (ModMaterials), and its PhysBones become rows of Cloth.csv the way the editor's import makes them
/// for the game's own outfits (VrcWearer), so its skirt sways as tuned. It goes on by the rule every outfit goes on by
/// (OutfitDresser: its bones joined to the character's of the same names), which is why it fits only the character it
/// was made for; the same outfit made for two characters is two files with one id, and its items are offered to both.
/// A PC that hasn't the mod shows that slot as the character came, as for any item it doesn't know.
/// </summary>
public static class ModOutfits
{
    /// <summary>One outfit model a mod offers, for one character or for anyone.</summary>
    public sealed class Outfit
    {
        public string Id, Skin, Name, Mod;
        internal string Path;
        internal ModAvatarFile File;
        internal ModAvatarFile.Outfit Data;
        internal OutfitLibrary.Entry Entry;
        internal GameObject Loaded;
        internal bool Tried;

        /// <summary>Made for no one in particular (ModAvatarFile.Anyone): a rigid accessory.</summary>
        public bool ForAnyone => string.Equals(Skin, ModAvatarFile.Anyone, StringComparison.Ordinal);
    }

    private const string ItemsTable = "Outfits", SwayTable = "Cloth";

    // Every outfit model the loaded mods offer, in load order; a later mod's takes the place of an earlier one's of the
    // same id made for the same character.
    private static List<Outfit> outfits;
    // By "model@skin", and by the mod that brought them.
    private static readonly Dictionary<string, Outfit> byKey = new Dictionary<string, Outfit>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<Outfit>> byMod = new Dictionary<string, List<Outfit>>(StringComparer.OrdinalIgnoreCase);
    // Each item's meshes, by "model@item": the same outfit made for two characters gives an item once, with the mesh
    // names of both (a piece takes whichever of them the model it is worn from has).
    private static readonly Dictionary<string, List<string>> meshesOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    // The game's own items, each with its model: a mod's row of the same id would change the game's.
    private static Dictionary<string, string> gameItems;
    private static readonly HashSet<string> told = new HashSet<string>();
    private static int readFrom = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        outfits = null;
        byKey.Clear();
        byMod.Clear();
        meshesOf.Clear();
        gameItems = null;
        told.Clear();
        readFrom = -1;
    }

    /// <summary>
    /// The mods' model of this id made for this character, else the one made for anyone; null when there is neither.
    /// Its model is loaded only when asked for (OutfitLibrary.Entry.Model).
    /// </summary>
    internal static OutfitLibrary.Entry Find(string modelId, string skinId)
    {
        Ensure();
        if (outfits.Count == 0 || string.IsNullOrEmpty(modelId)) return null;
        if (!string.IsNullOrEmpty(skinId) && byKey.TryGetValue(Key(modelId, skinId), out var made)) return made.Entry;
        return byKey.TryGetValue(Key(modelId, ModAvatarFile.Anyone), out var anyone) ? anyone.Entry : null;
    }

    private static string Key(string model, string second) => model + OutfitLook.Separator + second;

    // Reads the outfits out of the mods' files (ModAvatars.Files), again whenever the mods' folders were read again.
    private static void Ensure()
    {
        var files = ModAvatars.Files;
        if (outfits != null && readFrom == ModAvatars.Generation) return;
        readFrom = ModAvatars.Generation;
        outfits = new List<Outfit>();
        byKey.Clear();
        byMod.Clear();
        meshesOf.Clear();
        gameItems = null;
        foreach (var read in files)
            foreach (var data in read.File.outfits ?? Array.Empty<ModAvatarFile.Outfit>())
            {
                if (data == null || string.IsNullOrEmpty(data.id) || string.IsNullOrEmpty(data.prefab)) continue;
                string skin = data.skin ?? "";
                bool anyone = skin == ModAvatarFile.Anyone;
                if (!ModAvatars.ValidId(data.id) || (!anyone && !ModAvatars.ValidId(skin)))
                {
                    Tell($"{read.Mod.Name}: 옷 '{data.id}'(캐릭터 '{skin}')의 id에는 영문 소문자·숫자·-·_만 쓸 수 있습니다(누구나 입는 것은 캐릭터 자리에 *). 건너뜁니다.");
                    continue;
                }
                var outfit = new Outfit
                {
                    Id = data.id, Skin = skin, Name = string.IsNullOrWhiteSpace(data.name) ? data.id : data.name.Trim(), Mod = read.Mod.Name,
                    Path = read.Path, File = read.File, Data = data
                };
                outfit.Entry = new OutfitLibrary.Entry(data.id, anyone ? OutfitLibrary.AnySkin : skin, () => ModelOf(outfit), Stood(data), Holds(data, read.Mod.Name));
                string key = Key(data.id, skin);
                if (byKey.TryGetValue(key, out var earlier))
                {
                    outfits.Remove(earlier);
                    if (byMod.TryGetValue(earlier.Mod, out var theirs)) theirs.Remove(earlier);
                }
                byKey[key] = outfit;
                outfits.Add(outfit);
                if (!byMod.TryGetValue(outfit.Mod, out var own)) byMod[outfit.Mod] = own = new List<Outfit>();
                own.Add(outfit);
            }
        foreach (var outfit in outfits)
            foreach (var item in outfit.Data.items ?? Array.Empty<ModAvatarFile.Item>())
            {
                if (item == null || string.IsNullOrEmpty(item.id)) continue;
                string key = Key(outfit.Id, item.id);
                if (!meshesOf.TryGetValue(key, out var meshes)) meshesOf[key] = meshes = new List<string>();
                foreach (string mesh in item.meshes ?? Array.Empty<string>())
                    if (!string.IsNullOrWhiteSpace(mesh) && !meshes.Contains(mesh.Trim())) meshes.Add(mesh.Trim());
            }
        if (outfits.Count == 0) return;
        var names = new List<string>();
        foreach (var outfit in outfits) names.Add(outfit.Id + OutfitLook.Separator + outfit.Skin);
        Debug.Log("[Mods] 모드 옷 " + outfits.Count + "벌: " + string.Join(", ", names));
    }

    // Where the outfit's root stood against its avatar's, as its file says; left unsaid (no scale), at the same spot.
    private static OutfitLibrary.Standing Stood(ModAvatarFile.Outfit data)
    {
        if (data.scale == Vector3.zero) return OutfitLibrary.Standing.Same;
        var turned = data.rotation;
        bool none = turned.x == 0f && turned.y == 0f && turned.z == 0f && turned.w == 0f;
        return new OutfitLibrary.Standing(data.position, none ? Quaternion.identity : turned.normalized, data.scale);
    }

    // Its parts that follow one bone of the body (ModAvatarFile.Hold), each with the bone by Unity's own name for it.
    private static OutfitLibrary.Hold[] Holds(ModAvatarFile.Outfit data, string mod)
    {
        var holds = new List<OutfitLibrary.Hold>();
        foreach (var hold in data.holds ?? Array.Empty<ModAvatarFile.Hold>())
        {
            if (hold == null) continue;
            // No humanoid bone named (Modular Avatar's LastBone): the path under runs from the avatar's root.
            bool fromRoot = string.IsNullOrEmpty(hold.bone) || string.Equals(hold.bone, nameof(HumanBodyBones.LastBone), StringComparison.OrdinalIgnoreCase);
            var bone = HumanBodyBones.LastBone;
            if (fromRoot ? string.IsNullOrEmpty(hold.under)
                : !Enum.TryParse(hold.bone, true, out bone) || bone == HumanBodyBones.LastBone || !Enum.IsDefined(typeof(HumanBodyBones), bone))
            {
                Tell($"{mod}: 옷 '{data.id}'의 '{hold.path}'가 따라갈 뼈 '{hold.bone}'는 휴머노이드 뼈 이름(Head, Chest, LeftHand …)이 아닙니다. 옷의 뼈대를 따라가게 둡니다.");
                continue;
            }
            string mode = hold.mode ?? "";
            bool world = mode.IndexOf("KeepWorldPose", StringComparison.OrdinalIgnoreCase) >= 0;
            holds.Add(new OutfitLibrary.Hold(hold.path, bone, hold.under, world || mode.IndexOf("KeepPosition", StringComparison.OrdinalIgnoreCase) >= 0,
                world || mode.IndexOf("KeepRotation", StringComparison.OrdinalIgnoreCase) >= 0));
        }
        return holds.ToArray();
    }

    // ---- Its model, from the mod's bundle ----

    private static GameObject ModelOf(Outfit outfit)
    {
        if (outfit.Loaded != null || outfit.Tried) return outfit.Loaded;
        outfit.Tried = true;
        try
        {
            var bundle = ModAvatars.Open(outfit.Path, outfit.File.bundle, outfit.Mod);
            if (bundle == null) return null;
            var prefab = bundle.LoadAsset<GameObject>(outfit.Data.prefab);
            if (prefab == null)
            {
                Tell($"{outfit.Mod}: 번들 {outfit.File.bundle}에 '{outfit.Data.prefab}'가 없습니다. 다시 내보내 주세요.");
                return null;
            }
            ModMaterials.Apply(prefab, outfit.File, bundle, outfit.Id);
            LaySway(outfit, prefab);
            outfit.Loaded = prefab;
        }
        catch (Exception error)
        {
            Tell($"{outfit.Mod}: 옷 '{outfit.Id}'를 불러오다 멈췄습니다({error.GetType().Name}: {error.Message}).");
        }
        return outfit.Loaded;
    }

    // Its PhysBones as rows of Cloth.csv, as the editor's import writes them for the game's own outfits: under
    // "model@skin" with the bones it shares with its character left to her own rows, or, made for anyone, under its own
    // id with every chain its own.
    private static void LaySway(Outfit outfit, GameObject prefab)
    {
        if (outfit.Data.sway == null || outfit.Data.sway.Length == 0) return;
        List<Dictionary<string, string>> rows;
        if (outfit.ForAnyone)
        {
            var bones = ModAvatars.Bones(prefab, outfit.Data.sway);
            if (bones.Count == 0) return;
            rows = VrcWearer.AnyoneRows(bones, prefab, outfit.Id);
        }
        else
        {
            // Its colliders on her legs or chest are found on her own model: the body part each stands on is hers to say.
            var character = ModelOfSkin(outfit.Skin);
            if (character == null) return;
            var bones = ModAvatars.Bones(prefab, outfit.Data.sway, character);
            if (bones.Count == 0) return;
            rows = ModAvatars.WearerOf(character).OutfitRows(bones, prefab, outfit.Id, outfit.Skin);
        }
        if (rows.Count > 0) GameTables.Lay(SwayTable, "모드 옷 " + outfit.Id + OutfitLook.Separator + outfit.Skin + "의 흔들림", ModAvatars.Csv(rows));
    }

    // The model of the character with exactly this id (the game's own or a mod's); null when no such character is here.
    private static GameObject ModelOfSkin(string skinId)
    {
        var profile = PlayerBodyProfile.Load();
        if (profile == null) return null;
        foreach (var skin in profile.Skins)
            if (skin != null && string.Equals(skin.Id, skinId, StringComparison.Ordinal)) return skin.Model;
        return null;
    }

    // ---- Rows worked out from the files alone, laid under each mod's own tables ----

    /// <summary>
    /// The rows a mod's outfits give Outfits.csv by what their files say (one for each thing to wear: its slot, its
    /// name, its model and the meshes of it that it shows), before the mod's own file of that table, which wins.
    /// </summary>
    internal static IEnumerable<KeyValuePair<string, string>> TableTexts(GameMods.Mod mod, string table)
    {
        if (!string.Equals(table, ItemsTable, StringComparison.OrdinalIgnoreCase)) yield break;
        Ensure();
        if (!byMod.TryGetValue(mod.Name, out var own) || own.Count == 0) yield break;
        var rows = new List<Dictionary<string, string>>();
        var given = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var outfit in own)
            foreach (var item in outfit.Data.items ?? Array.Empty<ModAvatarFile.Item>())
            {
                if (item == null || string.IsNullOrEmpty(item.id) || given.Contains(item.id)) continue;
                if (!ModAvatars.ValidId(item.id))
                {
                    Tell($"{mod.Name}: 옷 id '{item.id}'에는 영문 소문자·숫자·-·_만 쓸 수 있습니다. 건너뜁니다.");
                    continue;
                }
                if (!ModAvatars.SlotKnown(item.slot))
                {
                    Tell($"{mod.Name}: 옷 '{item.id}'의 칸 '{item.slot}'은 이 게임에 없는 칸입니다(콘솔 /mods ids가 칸 id를 보여 줍니다). 건너뜁니다.");
                    continue;
                }
                // The game's own item of this id: of this same model it is this very thing and serves as it is (the
                // game's outfit, brought for another character); of another model, this row would change the game's.
                if (GameItems().TryGetValue(item.id, out string model))
                {
                    if (!string.Equals(model, outfit.Id, StringComparison.OrdinalIgnoreCase))
                        Tell($"{mod.Name}: 옷 id '{item.id}'는 게임에 이미 있는 다른 옷입니다. 옷 모드의 id를 바꿔 다시 내보내 주세요. 건너뜁니다.");
                    continue;
                }
                given.Add(item.id);
                rows.Add(new Dictionary<string, string>
                {
                    ["id"] = item.id, ["slot"] = item.slot, ["also"] = item.also ?? "", ["hides"] = item.hides ?? "",
                    ["name"] = string.IsNullOrWhiteSpace(item.name) ? outfit.Name : item.name.Trim(), ["model"] = outfit.Id,
                    ["parts"] = ModAvatars.Names(meshesOf.TryGetValue(Key(outfit.Id, item.id), out var meshes) ? meshes.ToArray() : item.meshes),
                    ["sheer"] = item.sheer ? "true" : ""
                });
            }
        if (rows.Count > 0) yield return new KeyValuePair<string, string>(mod.Name + "의 옷", ModAvatars.Csv(rows));
    }

    // The ids of the game's own items, each to its model, from the game's own text of the table (asked while that very
    // table is being read, so not through GameTables' rows).
    private static Dictionary<string, string> GameItems()
    {
        if (gameItems != null) return gameItems;
        gameItems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string text = GameTables.SourceText(ItemsTable);
        var table = text != null ? CsvTable.Parse(text) : null;
        if (table == null) return gameItems;
        foreach (string id in table.RowIds)
        {
            if (!table.TryGetRow(id, out var row)) continue;
            string model = row.Text("model", "");
            gameItems[id] = model.Length > 0 ? model : id;
        }
        return gameItems;
    }

    /// <summary>The console's /mods: the outfits the mods offer, each with who it is for and whether it is loaded.</summary>
    internal static string Summary()
    {
        Ensure();
        if (outfits.Count == 0) return "";
        var text = new StringBuilder("모드 옷 " + outfits.Count + "벌:");
        foreach (var outfit in outfits)
            text.Append("\n· ").Append(outfit.Id).Append(" (").Append(outfit.Name).Append(", ").Append(outfit.ForAnyone ? "누구나" : outfit.Skin + "용")
                .Append(", 옷 ").Append(outfit.Data.items?.Length ?? 0).Append("개, ").Append(outfit.Mod).Append(")")
                .Append(outfit.Loaded != null ? " — 불러옴" : outfit.Tried ? " — 불러오지 못함(콘솔 경고 참고)" : "");
        return text.ToString();
    }

    private static void Tell(string message)
    {
        if (told.Add(message)) Debug.LogWarning("[Mods] " + message);
    }
}
