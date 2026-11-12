using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Every class's own knife for executions and stealth kills (the user, 2026-11-13: "일단 칼은 클래스별로 각자 전용 칼을 쓰게
/// 하고 싶고"): Tables/ClassKnives, one row per class id (Tables/Classes), and a default row for a class without one (a
/// modder's). A row names the knife's model (a prefab under Resources: Resources/ClassProps/Knives holds the seven made from
/// workshop addons by Tools/Blender/addon_weapon_to_fbx.py), its stroke (a KnifeStroke table), where the fist holds it and
/// which ways its blade and edge leave the fist, its size, and the sound cues of its draw, stab and sheath. The hand's axes
/// are RepairArms': x the side away from the thumb, y out of the palm, z the fingers' way, positions in hand lengths from
/// the wrist. The model's own ways come from its markers (Tip, Edge, Pommel, as the converter places them), so a model
/// brought in any frame sits the same; without them its blade is taken along its -Z and its edge along -Y (the converter's
/// frame as Unity imports it, a half turn about Y).
/// The same knife is the melee slot's in place of the fists (MeleeKnife; the user, 2026-11-13: "클래스 별로 기본 근접 슬롯에
/// 칼 들게 해 줘"): the row also names it (a Texts id) and its picture (WeaponIcons), how the fist holds it to fight (a
/// forward or a reverse grip, and their seat), its swing clips (KnifeSwing tables, the Blender knife workspace's) and what
/// it deals in place of the fists' numbers, with its head and back factors and its extra reach.
/// </summary>
public static class ClassKnives
{
    public const string Table = "ClassKnives", DefaultRow = "default";

    /// <summary>One class's knife, as its row of Tables/ClassKnives says.</summary>
    public sealed class Knife
    {
        public string Id = DefaultRow;
        /// <summary>The model's prefab under Resources (no extension).</summary>
        public string Model = "ClassProps/Knives/PrimalKnife";
        /// <summary>The stroke's table (KnifeStroke).</summary>
        public string Motion = KnifeStroke.DefaultTable;
        /// <summary>The handle's middle in the fist, in hand lengths from the wrist (x away from the thumb, y out of the palm, z the fingers' way).</summary>
        public Vector3 Held = new Vector3(0f, .3f, .55f);
        /// <summary>
        /// Which way the handle leads to the blade, and the edge's facing, in the same hand axes: a reverse grip, the blade out
        /// of the little finger's side and leaning the fingers' way, so a stab from above goes in ahead of the fist.
        /// </summary>
        public Vector3 Blade = new Vector3(.93f, 0f, .36f), Edge = Vector3.forward;
        public float Scale = 1f;
        public string DrawSound = "Knife/Draw", StabSound = "Knife/Stab", SheathSound = "Knife/Sheath";

        // ---- In the melee slot, in place of the fists (MeleeKnife; the user, 2026-11-13: "클래스 별로 기본 근접 슬롯에 칼
        // 들게 해 줘") ----

        /// <summary>The knife's name (a Texts id, or the words themselves for a modder's row) and its picture (WeaponIcons id).</summary>
        public string Name = "knife.default.name", Icon = "PrimalKnife";
        /// <summary>Held in the fist for fighting: a forward grip (the blade out of the thumb's side) or a reverse one.</summary>
        public bool MeleeReverse;
        /// <summary>That grip's handle middle and blade and edge ways, in the hand axes above.</summary>
        public Vector3 MeleeHeld = new Vector3(0f, .3f, .55f), MeleeBlade = ForwardBlade, MeleeEdge = Vector3.forward;
        /// <summary>The swing clips (KnifeSwing tables): drawn from the hip, the guard held, the light slash and its backhand, the heavy stab.</summary>
        public string DrawClip = "KnifeMeleeDraw", GuardClip = "KnifeMeleeGuard", SlashClip = "KnifeMeleeSlash", BackslashClip = "KnifeMeleeBackslash",
            StabClip = "KnifeMeleeStab";
        /// <summary>
        /// What it deals in place of the fists' 10-28 (Player.csv Fists.damage): a light slash and a full-charge stab, with the
        /// posture they break; on the head and in the back these times more (generous, as the user's guns are); and how much
        /// further it reaches than a fist (m).
        /// </summary>
        public float Slash = 45f, Stab = 110f, SlashPosture = 18f, StabPosture = 40f, HeadFactor = 2f, BackFactor = 2f, Reach;
        public string SwingSound = "Knife/Swing", HitSound = "Knife/Hit", HeavyHitSound = "Knife/HeavyHit", WallSound = "Knife/Wall";
    }

    /// <summary>A forward grip's blade: out of the thumb's side, leaning the fingers' way, so a fist held thumb up points it ahead.</summary>
    public static readonly Vector3 ForwardBlade = new Vector3(-.8f, 0f, .6f);
    /// <summary>A reverse grip's blade: out of the little finger's side (the execution's grip).</summary>
    public static readonly Vector3 ReverseBlade = new Vector3(.93f, 0f, .36f);

    private static readonly Dictionary<string, Knife> knives = new Dictionary<string, Knife>(System.StringComparer.OrdinalIgnoreCase);
    private static bool listening;

    /// <summary>The knife of a class (its id in Tables/Classes), else the default row's, else the built-in one.</summary>
    public static Knife Of(string classId)
    {
        if (!listening)
        {
            listening = true;
            GameTables.Reloaded += knives.Clear;
        }
        string id = string.IsNullOrWhiteSpace(classId) ? DefaultRow : classId.Trim();
        if (knives.TryGetValue(id, out var known)) return known;
        var knife = Read(id) ?? Read(DefaultRow) ?? new Knife();
        knives[id] = knife;
        return knife;
    }

    /// <summary>
    /// The class a player plays, on any PC: their tally carries it to every PC (PlayerTally.ShowClass); this PC's own
    /// player falls back to the class picked here.
    /// </summary>
    public static string ClassOf(PlayerMovement who)
    {
        if (who == null) return "";
        if (who.TryGetComponent(out PlayerTally tally) && !string.IsNullOrEmpty(tally.ClassId)) return tally.ClassId;
        return who.IsLocal ? PlayerClass.CurrentId : "";
    }

    /// <summary>A new copy of the knife's model with nothing that collides or simulates, or null when its prefab is missing.</summary>
    public static GameObject Make(Knife knife)
    {
        var prefab = knife != null && !string.IsNullOrEmpty(knife.Model) ? Resources.Load<GameObject>(knife.Model) : null;
        if (prefab == null)
        {
            if (knife != null) Debug.LogWarning($"ClassKnives: no prefab at Resources/{knife.Model} for the {knife.Id} knife.");
            return null;
        }
        var made = Object.Instantiate(prefab);
        made.name = prefab.name;
        foreach (var body in made.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(body);
        foreach (var collider in made.GetComponentsInChildren<Collider>(true)) Object.Destroy(collider);
        return made;
    }

    private static Knife Read(string id)
    {
        if (!GameTables.TryGetRow(Table, id, out var row)) return null;
        var knife = new Knife { Id = id };
        knife.Model = Text(row, "model", knife.Model);
        knife.Motion = Text(row, "motion", knife.Motion);
        knife.Held = new Vector3(Number(row, "heldSide", knife.Held.x), Number(row, "heldPalm", knife.Held.y), Number(row, "heldAlong", knife.Held.z));
        knife.Blade = Way(row, "blade", knife.Blade);
        knife.Edge = Way(row, "edge", knife.Edge);
        knife.Scale = Mathf.Max(.01f, Number(row, "scale", 1f));
        knife.DrawSound = Text(row, "drawSound", knife.DrawSound);
        knife.StabSound = Text(row, "stabSound", knife.StabSound);
        knife.SheathSound = Text(row, "sheathSound", knife.SheathSound);
        // In the melee slot: the grip picks the blade's default way and the stab clip; any column may override it.
        knife.Name = Text(row, "name", knife.Name);
        string file = knife.Model.Substring(knife.Model.LastIndexOf('/') + 1);
        knife.Icon = Text(row, "icon", file.Length > 0 ? file : knife.Icon);
        knife.MeleeReverse = string.Equals(Text(row, "meleeGrip", "forward"), "reverse", System.StringComparison.OrdinalIgnoreCase);
        knife.MeleeHeld = new Vector3(Number(row, "meleeHeldSide", knife.Held.x), Number(row, "meleeHeldPalm", knife.Held.y),
            Number(row, "meleeHeldAlong", knife.Held.z));
        knife.MeleeBlade = Way(row, "meleeBlade", knife.MeleeReverse ? ReverseBlade : ForwardBlade);
        knife.MeleeEdge = Way(row, "meleeEdge", Vector3.forward);
        knife.DrawClip = Text(row, "drawClip", knife.DrawClip);
        knife.GuardClip = Text(row, "guardClip", knife.GuardClip);
        knife.SlashClip = Text(row, "slashClip", knife.SlashClip);
        knife.BackslashClip = Text(row, "backslashClip", knife.BackslashClip);
        knife.StabClip = Text(row, "stabClip", knife.MeleeReverse ? "KnifeMeleeStabReverse" : knife.StabClip);
        knife.Slash = Mathf.Max(0f, Number(row, "slash", knife.Slash));
        knife.Stab = Mathf.Max(0f, Number(row, "stab", knife.Stab));
        knife.SlashPosture = Mathf.Max(0f, Number(row, "slashPosture", knife.SlashPosture));
        knife.StabPosture = Mathf.Max(0f, Number(row, "stabPosture", knife.StabPosture));
        knife.HeadFactor = Mathf.Max(0f, Number(row, "headFactor", knife.HeadFactor));
        knife.BackFactor = Mathf.Max(0f, Number(row, "backFactor", knife.BackFactor));
        knife.Reach = Number(row, "reach", knife.Reach);
        knife.SwingSound = Text(row, "swingSound", knife.SwingSound);
        knife.HitSound = Text(row, "hitSound", knife.HitSound);
        knife.HeavyHitSound = Text(row, "heavyHitSound", knife.HeavyHitSound);
        knife.WallSound = Text(row, "wallSound", knife.WallSound);
        return knife;
    }

    private static Vector3 Way(IReadOnlyDictionary<string, string> row, string name, Vector3 fallback)
    {
        var way = new Vector3(Number(row, name + "X", fallback.x), Number(row, name + "Y", fallback.y), Number(row, name + "Z", fallback.z));
        return way.sqrMagnitude > 1e-8f ? way.normalized : fallback;
    }

    private static string Text(IReadOnlyDictionary<string, string> row, string key, string fallback) => TableCells.Text(row, key, fallback);

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback) => TableCells.Number(row, key, fallback);
}
