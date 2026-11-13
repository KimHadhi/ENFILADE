using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Pack effects played by name, one name per row of Data/Resources/Tables/Weapons/ImpactEffects.csv. Every copy is built while the map
/// loads and waits awake out of sight, so a fight never creates one: a preset hands out its copies in turn, which makes
/// the next one the oldest, and a busy fight takes that one over. A graph's first draw stalls for about half a second,
/// so each copy also plays once as the map comes up, far down the view: in frame, but too far away to see.
/// </summary>
[DisallowMultipleComponent]
public sealed class ImpactPool : MonoBehaviour
{
    public const string TableName = "ImpactEffects";
    private const string LibraryPath = "Combat/ImpactLibrary", NoHole = "none";
    private static readonly Vector3 OutOfSight = Vector3.down * 5000f;
    private static readonly int LightnessId = Shader.PropertyToID("Liquid Lightness"), UseDecalsId = Shader.PropertyToID("Use Decals");
    private static ImpactPool instance;

    private sealed class Preset { public readonly List<VisualEffect> Copies = new List<VisualEffect>(); public int Next; }
    private sealed class Row
    {
        public List<Preset> Presets, ExitPresets;
        public float Scale, Lightness, HoleSize, HoleVary, HoleStretch = 1f, ExitScale = 1f;
        // How long its holes stay before fading (s); 0 is GunFeel's bulletHoleSeconds.
        public float HoleSeconds;
        // Hivemind's bullet holes: three to six per surface, one picked a hit, never the last one twice running.
        public Material[] Holes = new Material[0];
        // The table says `none`: nothing is left on this surface, and its parents aren't asked (water).
        public bool NoHoles;
        public int Last = -1, LastExit = -1, LastHole = -1;
        public Material PickHole()
        {
            if (Holes.Length == 0) return null;
            int pick = UnityEngine.Random.Range(0, Holes.Length);
            if (pick == LastHole && Holes.Length > 1) pick = (pick + 1) % Holes.Length;
            LastHole = pick;
            return Holes[pick];
        }
    }

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

    /// <summary>
    /// A bullet's impact: the effect of the surface hit, or of its nearest parent surface that has one, and the hole of the
    /// nearest that has holes. The two are looked up apart, so a surface can have its own holes (a row with only `hole`)
    /// and still borrow its parent's burst, or leave none at all (`none`: water, which would otherwise get concrete's).
    /// `incoming` is the way the bullet was going: a hit at a slant leaves a long hole along it (BulletHoles), as far as
    /// the row's holeStretch lets it.
    /// </summary>
    public static void PlaySurface(Collider surface, Vector3 point, Vector3 normal, Vector3 incoming = default)
    {
        if (!Application.isPlaying) return;
        Prepare();
        var chain = Surfaces.Fallbacks(Surfaces.Of(surface));
        foreach (string id in chain)
            if (instance.Spawn(id, point, normal)) break;
        var holes = instance.HoleRow(chain);
        if (holes != null)
            BulletHoles.Place(holes.PickHole(), holes.HoleSize, point, normal, incoming, holes.HoleVary, holes.HoleStretch, holes.HoleSeconds);
    }

    private Row HoleRow(IReadOnlyList<string> chain)
    {
        foreach (string id in chain)
        {
            if (!rows.TryGetValue(id, out var row)) continue;
            if (row.NoHoles) return null;
            if (row.Holes.Length > 0) return row;
        }
        return null;
    }

    /// <summary>
    /// The far side of something a bullet went through: the surface's row again, its exitScale bigger and thrown out
    /// halfway between the face and the bullet's way on, with the row's exitPresets (debris) on top and a hole as much
    /// bigger, the way a wall spalls where a round leaves it; the hole is drawn out along the way it left, as one going
    /// in is.
    /// </summary>
    public static void PlayExit(Collider surface, Vector3 point, Vector3 normal, Vector3 direction)
    {
        if (!Application.isPlaying) return;
        Prepare();
        Vector3 up = Vector3.Slerp(normal.normalized, direction.normalized, .5f);
        var chain = Surfaces.Fallbacks(Surfaces.Of(surface));
        float exitScale = 1f;
        foreach (string id in chain)
        {
            if (!instance.rows.TryGetValue(id, out var row) || row.Presets.Count == 0) continue;
            instance.Spawn(row, row.Presets, ref row.Last, point, up, row.ExitScale);
            if (row.ExitPresets.Count > 0) instance.Spawn(row, row.ExitPresets, ref row.LastExit, point, up, row.ExitScale);
            exitScale = row.ExitScale;
            break;
        }
        var holes = instance.HoleRow(chain);
        if (holes != null)
            BulletHoles.Place(holes.PickHole(), holes.HoleSize * exitScale, point, normal, direction, holes.HoleVary, holes.HoleStretch,
                holes.HoleSeconds);
    }

    /// <summary>Plays the row's effect at point leaving along `up`, `scale` times its usual size. False when the row has none.</summary>
    public static bool Play(string id, Vector3 point, Vector3 up, float scale)
    {
        if (!Application.isPlaying) return false;
        Prepare();
        if (!instance.rows.TryGetValue(id, out var row) || row.Presets.Count == 0) return false;
        instance.Spawn(row, row.Presets, ref row.Last, point, up, scale);
        return true;
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
            foreach (var hole in row.Holes)
                BulletHoles.Place(hole, .001f, view.transform.position + view.transform.forward * 2f, -view.transform.forward);
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
            int copies = cells.Whole("copies", 1);
            float scale = cells.Number("scale", 1f), lightness = cells.Number("lightness", 0f);
            float holeSize = cells.Number("holeSize", 0f), holeVary = cells.Number("holeVary", 0f);
            float holeStretch = cells.Number("holeStretch", 1f), exitScale = cells.Number("exitScale", 1f);
            float holeSeconds = cells.Number("holeSeconds", 0f);
            var row = new Row { Presets = new List<Preset>(), ExitPresets = new List<Preset>(), Scale = scale, Lightness = lightness,
                HoleSize = holeSize, HoleVary = Mathf.Max(0f, holeVary), HoleStretch = Mathf.Max(1f, holeStretch),
                ExitScale = Mathf.Max(.1f, exitScale), HoleSeconds = Mathf.Max(0f, holeSeconds) };
            var holes = new List<Material>();
            row.NoHoles = string.Equals(Cell(cells, "hole").Trim(), NoHole, StringComparison.OrdinalIgnoreCase);
            foreach (string name in Cell(cells, "hole").Split(';'))
            {
                string hole = name.Trim();
                if (hole.Length == 0 || row.NoHoles) continue;
                var material = library.FindHole(hole);
                if (material != null) holes.Add(material);
                else Debug.LogWarning($"[ImpactPool] {TableName}.csv의 구멍 '{hole}'이(가) ImpactLibrary에 없습니다.");
            }
            row.Holes = holes.ToArray();
            foreach (string name in Cell(cells, "presets").Split(';'))
            {
                var preset = name.Trim().Length > 0 ? Ensure(name.Trim(), Mathf.Max(1, copies)) : null;
                if (preset != null) row.Presets.Add(preset);
            }
            foreach (string name in Cell(cells, "exitPresets").Split(';'))
            {
                var preset = name.Trim().Length > 0 ? Ensure(name.Trim(), Mathf.Max(1, copies)) : null;
                if (preset != null) row.ExitPresets.Add(preset);
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
        Spawn(row, row.Presets, ref row.Last, point, up, 1f);
        return true;
    }

    // One of the presets, never the one played last when there is a choice, at the row's scale times `scale`.
    private void Spawn(Row row, List<Preset> presets, ref int last, Vector3 point, Vector3 up, float scale)
    {
        int pick = UnityEngine.Random.Range(0, presets.Count);
        if (pick == last) pick = (pick + 1) % presets.Count;
        last = pick;
        var preset = presets[pick];
        var effect = preset.Copies[preset.Next];
        preset.Next = (preset.Next + 1) % preset.Copies.Count;
        // The pack's own tester stands its effects on the surface like this: local Y out along the normal.
        effect.transform.SetPositionAndRotation(point, Quaternion.FromToRotation(Vector3.up, up));
        effect.transform.localScale = Vector3.one * (row.Scale * scale);
        if (row.Lightness > 0f && effect.HasFloat(LightnessId)) effect.SetFloat(LightnessId, row.Lightness);
        effect.Play();
    }
}
