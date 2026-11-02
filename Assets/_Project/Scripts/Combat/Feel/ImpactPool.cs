using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Pack effects played by name, one name per row of Resources/Tables/ImpactEffects.csv. Every copy is built while the map
/// loads and waits awake out of sight, so a fight never creates one: a preset hands out its copies in turn, which makes
/// the next one the oldest, and a busy fight takes that one over. A graph's first draw stalls for about half a second,
/// so each copy also plays once as the map comes up, far down the view: in frame, but too far away to see.
/// </summary>
[DisallowMultipleComponent]
public sealed class ImpactPool : MonoBehaviour
{
    public const string TableName = "ImpactEffects";
    private const string LibraryPath = "Combat/ImpactLibrary";
    private static readonly Vector3 OutOfSight = Vector3.down * 5000f;
    private static readonly int LightnessId = Shader.PropertyToID("Liquid Lightness"), UseDecalsId = Shader.PropertyToID("Use Decals");
    private static ImpactPool instance;

    private sealed class Preset { public readonly List<VisualEffect> Copies = new List<VisualEffect>(); public int Next; }
    private sealed class Row { public List<Preset> Presets; public float Scale, Lightness, HoleSize; public Material Hole; public int Last = -1; }

    private readonly Dictionary<string, Preset> presets = new Dictionary<string, Preset>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Row> rows = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
    private ImpactLibrary library;
    private Transform holder;

    /// <summary>Builds every effect. Call while a map loads; later calls do nothing.</summary>
    public static void Prepare()
    {
        if (Application.isPlaying && instance == null)
            instance = FeelAssets.CreateRuntimeRoot("Impact Pool").AddComponent<ImpactPool>();
    }

    /// <summary>Plays the row's effect at point, with its burst leaving along up. False when the row has none to play.</summary>
    public static bool Play(string id, Vector3 point, Vector3 up)
    {
        if (!Application.isPlaying) return false;
        Prepare();
        return instance.Spawn(id, point, up);
    }

    /// <summary>A bullet's impact: the row of the surface hit, or of its nearest parent surface that has one, with its hole.</summary>
    public static void PlaySurface(Collider surface, Vector3 point, Vector3 normal)
    {
        if (!Application.isPlaying) return;
        Prepare();
        foreach (string id in Surfaces.Fallbacks(Surfaces.Of(surface)))
        {
            if (!instance.Spawn(id, point, normal)) continue;
            var row = instance.rows[id];
            if (row.Hole != null) BulletHoles.Place(row.Hole, row.HoleSize, point, normal);
            return;
        }
    }

    private void Awake()
    {
        library = Resources.Load<ImpactLibrary>(LibraryPath);
        if (library == null) Debug.LogWarning($"[ImpactPool] Resources/{LibraryPath} 에셋이 없습니다.");
        var unborn = new GameObject("Unborn");
        unborn.SetActive(false);
        holder = unborn.transform;
        holder.SetParent(transform, false);
        Load();
        GameTables.Reloaded += Load;
        // Reads the surface table now rather than on the first shot.
        Surfaces.Fallbacks(Surfaces.Default);
    }

    private IEnumerator Start()
    {
        // The player's camera exists from the next frame on.
        yield return null;
        var view = Camera.main;
        if (view == null) yield break;
        Vector3 spot = view.transform.position + view.transform.forward * view.farClipPlane * .8f;
        foreach (var preset in presets.Values)
            foreach (var effect in preset.Copies)
            {
                effect.transform.position = spot;
                effect.Play();
            }
        // A decal builds its shader on its first draw too; this close, this small, it can't be seen.
        foreach (var row in rows.Values)
            BulletHoles.Place(row.Hole, .001f, view.transform.position + view.transform.forward * 2f, -view.transform.forward);
        // The same for the materials of the muzzle flash, smoke, sparks, brass and blood.
        var stand = new GameObject("First Draws");
        stand.transform.SetParent(transform, false);
        foreach (var material in FeelAssets.CombatMaterials())
        {
            var quad = new GameObject("Quad");
            quad.transform.SetParent(stand.transform, false);
            quad.transform.position = spot;
            quad.AddComponent<MeshFilter>().sharedMesh = FeelAssets.Quad;
            quad.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
        // One frame to simulate and one to draw.
        yield return null;
        yield return null;
        Destroy(stand);
        foreach (var preset in presets.Values)
            foreach (var effect in preset.Copies)
                effect.transform.position = OutOfSight;
    }

    private void OnDestroy()
    {
        GameTables.Reloaded -= Load;
        if (instance == this) instance = null;
    }

    private void Load()
    {
        rows.Clear();
        if (library == null) return;
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var cells)) continue;
            int copies = int.TryParse(Cell(cells, "copies"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ? count : 1;
            float scale = float.TryParse(Cell(cells, "scale"), NumberStyles.Float, CultureInfo.InvariantCulture, out float size) ? size : 1f;
            float.TryParse(Cell(cells, "lightness"), NumberStyles.Float, CultureInfo.InvariantCulture, out float lightness);
            float.TryParse(Cell(cells, "holeSize"), NumberStyles.Float, CultureInfo.InvariantCulture, out float holeSize);
            var row = new Row { Presets = new List<Preset>(), Scale = scale, Lightness = lightness, HoleSize = holeSize };
            string hole = Cell(cells, "hole").Trim();
            if (hole.Length > 0)
            {
                row.Hole = library.FindHole(hole);
                if (row.Hole == null) Debug.LogWarning($"[ImpactPool] {TableName}.csv의 구멍 '{hole}'이(가) ImpactLibrary에 없습니다.");
            }
            foreach (string name in Cell(cells, "presets").Split(';'))
            {
                var preset = name.Trim().Length > 0 ? Ensure(name.Trim(), Mathf.Max(1, copies)) : null;
                if (preset != null) row.Presets.Add(preset);
            }
            rows[id] = row;
        }
    }

    private static string Cell(IReadOnlyDictionary<string, string> cells, string column)
        => cells.TryGetValue(column, out string value) ? value : string.Empty;

    private Preset Ensure(string name, int copies)
    {
        var prefab = library.Find(name);
        if (prefab == null)
        {
            Debug.LogWarning($"[ImpactPool] {TableName}.csv의 '{name}'이(가) ImpactLibrary에 없습니다.");
            return null;
        }
        if (!presets.TryGetValue(name, out var preset)) presets[name] = preset = new Preset();
        while (preset.Copies.Count < copies)
        {
            var effect = Instantiate(prefab, OutOfSight, Quaternion.identity, holder).GetComponent<VisualEffect>();
            // Changed under the inactive holder, or the copy would play once as it wakes.
            effect.initialEventName = string.Empty;
            // A pack decal would follow this copy to its next hit, so BulletHoles keeps the holes. The pack's light burst
            // builds an object on every play, which is what this pool exists to avoid.
            if (effect.HasBool(UseDecalsId)) effect.SetBool(UseDecalsId, false);
            if (effect.TryGetComponent<DAVFX.VFXLightBurstCompanion>(out var burst)) burst.enabled = false;
            effect.transform.SetParent(transform, true);
            preset.Copies.Add(effect);
        }
        return preset;
    }

    private bool Spawn(string id, Vector3 point, Vector3 up)
    {
        if (!rows.TryGetValue(id, out var row) || row.Presets.Count == 0) return false;
        int pick = UnityEngine.Random.Range(0, row.Presets.Count);
        if (pick == row.Last) pick = (pick + 1) % row.Presets.Count;
        row.Last = pick;
        var preset = row.Presets[pick];
        var effect = preset.Copies[preset.Next];
        preset.Next = (preset.Next + 1) % preset.Copies.Count;
        // The pack's own tester stands its effects on the surface like this: local Y out along the normal.
        effect.transform.SetPositionAndRotation(point, Quaternion.FromToRotation(Vector3.up, up));
        effect.transform.localScale = Vector3.one * row.Scale;
        if (row.Lightness > 0f && effect.HasFloat(LightnessId)) effect.SetFloat(LightnessId, row.Lightness);
        effect.Play();
        return true;
    }
}
