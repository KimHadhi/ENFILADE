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
    }

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
        return knife;
    }

    private static Vector3 Way(IReadOnlyDictionary<string, string> row, string name, Vector3 fallback)
    {
        var way = new Vector3(Number(row, name + "X", fallback.x), Number(row, name + "Y", fallback.y), Number(row, name + "Z", fallback.z));
        return way.sqrMagnitude > 1e-8f ? way.normalized : fallback;
    }

    private static string Text(IReadOnlyDictionary<string, string> row, string key, string fallback)
        => row.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float read)
            ? read : fallback;
}
