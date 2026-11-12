using System.Collections.Generic;
using System.Globalization;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A gun's own finger shapes (2026-11-11, the user's gun-hand fixes: g-seven grip shapes, g-fingers thumbs). HandGrip holds
/// every gun with the same shapes (Trigger for the shooting hand; Cradle, or Cup under a pistol, for the support hand on the
/// gun's own body), which fit some guns' grips and not others. A gun may carry its own bend angles for either hand instead.
/// They are HandGrip's own terms: each finger's base, middle and tip joint bent toward the palm about the hand's own hinge
/// from the open hand, the thumb's from its rest, and how far the thumb first turns under the palm. So a row holds the same
/// on any humanoid hand, a mod's character included, and KeepHuman still keeps every joint within a hand's reach.
/// Rows: Tables/GunGrips.csv, id "&lt;WeaponData name&gt;/R" (shooting hand) or "/L" (support hand). The Grip Tuner (Unity
/// menu ENFILADE → 총 → Grip Tuner) changes them live in Play mode while you look at the hands, and saves them.
/// </summary>
public static class GunGrips
{
    public const string TableName = "GunGrips";
    /// <summary>The fingers in HandGrip's order, and the column names' stems.</summary>
    public static readonly string[] Fingers = { "index", "middle", "ring", "little", "thumb" };
    /// <summary>The joints in each finger's order: base (knuckle), middle, tip.</summary>
    public static readonly string[] Joints = { "Base", "Mid", "Tip" };

    /// <summary>One hand's shape for one gun.</summary>
    public sealed class Grip
    {
        /// <summary>Degrees per finger (index, middle, ring, little, thumb): base, middle and tip joint toward the palm.</summary>
        public readonly Vector3[] Bends = new Vector3[5];
        /// <summary>Degrees the thumb turns under the palm (about the line of the fingers) before it bends; 0 leaves it as the body holds it.</summary>
        public float ThumbUnder;
        /// <summary>Changes on every edit, so a hand works the pose out again (HandGrip caches it).</summary>
        public int Version;

        public Grip Copy()
        {
            var copy = new Grip { ThumbUnder = ThumbUnder, Version = Version };
            System.Array.Copy(Bends, copy.Bends, Bends.Length);
            return copy;
        }
    }

    // [0] support hand, [1] shooting hand, per gun name. The tuner's edits win over the table's rows this session.
    private static readonly Dictionary<string, Grip[]> live = new Dictionary<string, Grip[]>();
    private static Dictionary<string, Grip[]> table;
    private static int versions;
    private static bool listening;

    /// <summary>The shape a gun's hand holds it with, if the gun has its own (live from the tuner, else the table's).</summary>
    public static bool TryGet(string gun, bool left, out Grip grip)
    {
        grip = null;
        if (string.IsNullOrEmpty(gun)) return false;
        int side = left ? 0 : 1;
        if (live.TryGetValue(gun, out var edited) && edited[side] != null) { grip = edited[side]; return true; }
        var rows = Table();
        if (rows.TryGetValue(gun, out var saved) && saved[side] != null) { grip = saved[side]; return true; }
        return false;
    }

    /// <summary>The tuner's edit for one hand, worn at once (not saved until the tuner saves the table).</summary>
    public static void SetLive(string gun, bool left, Grip grip)
    {
        if (string.IsNullOrEmpty(gun) || grip == null) return;
        if (!live.TryGetValue(gun, out var hands)) live[gun] = hands = new Grip[2];
        grip.Version = ++versions;
        hands[left ? 0 : 1] = grip;
    }

    /// <summary>Drops the tuner's edits for a gun, so the table's rows (or the shared shapes) show again.</summary>
    public static void ClearLive(string gun)
    {
        if (gun != null) live.Remove(gun);
    }

    /// <summary>
    /// The shared shape's angles a hand starts from when its gun has none: the shooting hand's Trigger, the support hand's
    /// Cradle (Cup under a pistol).
    /// </summary>
    public static Grip Default(bool left, bool pistol)
    {
        var grip = new Grip();
        var angles = HandGrip.AnglesOf(left ? pistol ? HandGrip.Shape.Cup : HandGrip.Shape.Cradle : HandGrip.Shape.Trigger);
        for (int i = 0; i < grip.Bends.Length && i < angles.Length; i++) grip.Bends[i] = angles[i];
        return grip;
    }

    /// <summary>
    /// The gun the local player holds now (Play mode), for the tuner: its WeaponData name and whether it is a pistol.
    /// In a room every player's body holds a gun; the one this PC drives is the owner's.
    /// </summary>
    public static bool Holding(out string gun, out bool pistol)
    {
        gun = null;
        pistol = false;
        foreach (var hands in Object.FindObjectsByType<PlayerBodyHands>(FindObjectsSortMode.None))
        {
            var weapons = hands.GetComponent<WeaponController>();
            var body = FPSBody.Holding(weapons);
            if (body == null || weapons.EquippedData == null) continue;
            var network = hands.GetComponentInParent<NetworkObject>();
            if (network != null && network.IsSpawned && !network.IsOwner) continue;
            gun = weapons.EquippedData.name;
            pistol = body.HeldPistol;
            return true;
        }
        return false;
    }

    /// <summary>The table's column for a finger's joint ("indexBase" … "thumbTip").</summary>
    public static string Column(int finger, int joint) => Fingers[finger] + Joints[joint];

    private static Dictionary<string, Grip[]> Table()
    {
        if (table != null) return table;
        if (!listening)
        {
            listening = true;
            GameTables.Reloaded += () => table = null;
        }
        table = new Dictionary<string, Grip[]>();
        foreach (string id in GameTables.RowIds(TableName))
        {
            int bar = id.LastIndexOf('/');
            if (bar <= 0 || !GameTables.TryGetRow(TableName, id, out var row)) continue;
            string gun = id.Substring(0, bar), hand = id.Substring(bar + 1).Trim().ToUpperInvariant();
            if (hand != "L" && hand != "R") continue;
            bool left = hand == "L";
            // A cell left empty keeps the shared shape's angle there.
            var grip = Default(left, false);
            for (int f = 0; f < Fingers.Length; f++)
                for (int j = 0; j < Joints.Length; j++)
                    if (Number(row, Column(f, j), out float value)) grip.Bends[f][j] = value;
            if (Number(row, "thumbUnder", out float under)) grip.ThumbUnder = under;
            grip.Version = ++versions;
            if (!table.TryGetValue(gun, out var hands)) table[gun] = hands = new Grip[2];
            hands[left ? 0 : 1] = grip;
        }
        return table;
    }

    private static bool Number(IReadOnlyDictionary<string, string> row, string column, out float value)
    {
        value = 0f;
        return row.TryGetValue(column, out string text) && !string.IsNullOrWhiteSpace(text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
