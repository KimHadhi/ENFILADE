using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Dual pistols (porting bundle 8, "쌍권총"): a gun held twice, one in each hand. Each row of Tables/Akimbo.csv is one
/// such pair. Its id is the pair's own WeaponData name and its row in Weapons.csv, which has its numbers: TFA Base's akimbo
/// rule (workshop 2840031720, common/akimbo.lua) is one magazine for both guns at twice the single gun's rate, the two
/// firing in turn, and its M9 pair (3020848099) has no sights. Its `single` is the gun it is two of, one of the KINEMATION
/// guns the body holds (FPSBodyAssets). The pair's WeaponData is made here at runtime as a copy of that gun (its sounds,
/// icon, crosshair and the rest), so it needs no asset of its own and every PC of a build makes the same one. The body
/// shows it as that gun in the right hand and a copy of it in the left (FPSBody.Akimbo); WeaponController fires them in
/// turn, and the left one alone on AltFire (ArcCW MW2's left trigger, 2135529088).
/// The rest of a row is how the pair is shown (Look). Another pistol's model and shot sounds go in by name: a folder under
/// Art/Models/Weapons (AkimboModels) and one under Art/Audio/Guns whose Shot folder holds the shots.
/// </summary>
public static class AkimboGuns
{
    public const string TableName = "Akimbo";
    // The Guns sound bank (Art/Audio/Guns) and its shot folder's name in it.
    private const string GunBank = "Guns", ShotFolder = "Shot";

    /// <summary>How a pair is shown, from its Akimbo.csv row (read again when the table is saved).</summary>
    [Serializable]
    public sealed class Look
    {
        [Tooltip("The gun the pair is two of (a WeaponData of Resources/Combat/FPSBody's guns). Read once, when the pair is made.")]
        public string single = "";
        [Tooltip("How far each gun is held out to its own side from where the single gun is held in both hands (m).")]
        [Min(0f)] public float spread = .11f;
        [Tooltip("How long the left hand takes to take its gun up or to put it away (s).")]
        [Min(.01f)] public float reach = .2f;
        [Tooltip("Half-life (s) of the left gun taking the right one's sway and walk: both swing the same way.")]
        [Min(0f)] public float follow = .06f;
        [Tooltip("Half-life (s) of the left gun settling where the right one rests, seen in a mirror down the middle of the view.")]
        [Min(0f)] public float settle = .3f;
        [Tooltip("After a shot of the right gun, the left one takes none of its motion for this long (s): the right gun's kick stays its own.")]
        [Min(0f)] public float steady = .12f;
        [Tooltip("The left gun's kick on its own shot: back (m) and up (degrees), and how fast it settles (1/s).")]
        [Min(0f)] public float kickBack = .03f;
        [Min(0f)] public float kickUp = 7f;
        [Min(.1f)] public float kickReturn = 16f;
        [Tooltip("How far a shot sends a drawn copy's slide back (m) and how fast it comes forward (1/s). Its slide is the part " +
            "named Slider or Slide; a gun without one shows none.")]
        [Min(0f)] public float slideTravel = .025f;
        [Min(.1f)] public float slideReturn = 30f;
        [Tooltip("Another model for both guns: the name of a folder under Art/Models/Weapons (AkimboModels). Empty: the single gun's own.")]
        public string model = "";
        [Tooltip("That model in the gun's frame, the sight line forward and the grip at its origin: its offset (m), turn (degrees) and size.")]
        public Vector3 modelOffset;
        public Vector3 modelTurn;
        [Min(.001f)] public float modelScale = 1f;
        [Tooltip("Shot sounds for both guns: the name of a folder under Art/Audio/Guns whose Shot folder holds them. Empty: the single gun's.")]
        public string sounds = "";
    }

    private sealed class Pair
    {
        public WeaponData Dual, SingleGun;
        public Look Look;
        public string Sounds;
    }

    private static readonly List<Pair> pairs = new List<Pair>();
    private static readonly Dictionary<WeaponData, Pair> byDual = new Dictionary<WeaponData, Pair>();
    private static bool loaded, listening;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        pairs.Clear();
        byDual.Clear();
        loaded = false;
    }

    /// <summary>This gun is a dual pistol, one of the pairs made here.</summary>
    public static bool Is(WeaponData gun) => gun != null && byDual.ContainsKey(gun);

    /// <summary>The gun a dual pistol is two of; null for any other gun.</summary>
    public static WeaponData SingleOf(WeaponData gun) => gun != null && byDual.TryGetValue(gun, out var pair) ? pair.SingleGun : null;

    /// <summary>How a dual pistol is shown (its Akimbo.csv row); null for any other gun.</summary>
    public static Look LookOf(WeaponData gun) => gun != null && byDual.TryGetValue(gun, out var pair) ? pair.Look : null;

    /// <summary>The dual pistol made of this gun, or null. A gun is made into one pair at most.</summary>
    public static WeaponData DualOf(WeaponData single)
    {
        if (single == null) return null;
        Load();
        foreach (var pair in pairs) if (pair.SingleGun == single) return pair.Dual;
        return null;
    }

    /// <summary>The dual pistol of this name (its Akimbo.csv id), or null.</summary>
    public static WeaponData Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        Load();
        foreach (var pair in pairs)
            if (string.Equals(pair.Dual.name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return pair.Dual;
        return null;
    }

    /// <summary>Every dual pistol, in the table's order (the shop and /gun list them with the other guns).</summary>
    public static IEnumerable<WeaponData> All
    {
        get
        {
            Load();
            foreach (var pair in pairs) yield return pair.Dual;
        }
    }

    private static void Load()
    {
        if (loaded || !Application.isPlaying) return;
        loaded = true;
        if (!listening)
        {
            listening = true;
            GameTables.Reloaded += Resound;
        }
        var assets = FPSBodyAssets.Instance;
        if (assets == null) return;
        foreach (string id in GameTables.RowIds(TableName))
        {
            string name = id.Trim();
            if (name.Length == 0 || !GameTables.TryGetRow(TableName, name, out var row)) continue;
            string singleName = row.TryGetValue("single", out string text) ? text.Trim() : "";
            var single = Gun(assets, singleName);
            if (single == null || single.Category != WeaponCategory.Ranged)
            {
                Debug.LogWarning($"[Akimbo] {TableName}.csv [{name}]: '{singleName}' is none of the guns the body holds (Resources/Combat/FPSBody).");
                continue;
            }
            if (Find(name) != null || DualOfLoaded(single) != null) continue;
            // A copy of the single gun under the pair's own name: Weapons.csv's row of that name gives it its numbers.
            var dual = Object.Instantiate(single);
            dual.name = name;
            dual.hideFlags = HideFlags.DontSave;
            dual.BindTable();
            var look = new Look();
            GameTables.BindNested(TableName, name, dual, look);
            var pair = new Pair { Dual = dual, SingleGun = single, Look = look };
            UseSounds(pair);
            pairs.Add(pair);
            byDual[dual] = pair;
        }
    }

    private static WeaponData DualOfLoaded(WeaponData single)
    {
        foreach (var pair in pairs) if (pair.SingleGun == single) return pair.Dual;
        return null;
    }

    private static WeaponData Gun(FPSBodyAssets assets, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var entry in assets.guns)
            if (entry.weapon != null && string.Equals(entry.weapon.name, name, StringComparison.OrdinalIgnoreCase)) return entry.weapon;
        return null;
    }

    // A saved table may name other shot sounds.
    private static void Resound()
    {
        foreach (var pair in pairs) UseSounds(pair);
    }

    // The pair's shots: the Guns bank's <sounds>/Shot clips (Art/Audio/Guns/<sounds>/Shot) when the row names a folder
    // that has them, else the single gun's own (its Fire Sounds, or the bank's Pistol/Shot when it has none). WeaponData
    // keeps its clips private and serialized; this pair is its only runtime-made copy, so they are set through reflection.
    private static void UseSounds(Pair pair)
    {
        string folder = pair.Look.sounds != null ? pair.Look.sounds.Trim() : "";
        if (folder == pair.Sounds) return;
        pair.Sounds = folder;
        AudioClip[] clips = pair.SingleGun.FireSounds;
        if (folder.Length > 0)
        {
            var bank = SoundBank.Load(GunBank);
            if (bank != null && bank.TryGet(folder + "/" + ShotFolder, out var found) && found != null && found.Length > 0) clips = found;
            else Debug.LogWarning($"[Akimbo] {TableName}.csv [{pair.Dual.name}]: no shots in Art/Audio/Guns/{folder}/{ShotFolder} yet; the {pair.SingleGun.name}'s are used.");
        }
        FireSoundsField?.SetValue(pair.Dual, clips != null ? (AudioClip[])clips.Clone() : new AudioClip[0]);
    }

    private static readonly FieldInfo FireSoundsField =
        typeof(WeaponData).GetField("fireSounds", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
}
