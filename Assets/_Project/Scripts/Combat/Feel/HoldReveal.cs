using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A zombie holding a player (ZombieGrip: a shambler's grab, one kneeling to eat, a runner's pin) stands out on every PC
/// while it holds: the holder outline where it can be seen (HitFlashRenderer.SetHolder) and a flat, throbbing silhouette
/// where something stands in front of it (Hidden/ENFILADE/RevealThroughWalls on copies of its meshes, as EnemyReveal shows
/// a wave's last enemies). The user's playtest of 2026-11-11: a shambler below a step grabbed them and the very floor they
/// stood on hid it, and a friend who wants to help has to see at once both the grabbed teammate (DownedAura) and what
/// holds them. A grip signs up here as it starts holding (Watch) and drops off once it lets go; one of these is made on
/// first need and lives with the scene. Numbers: Tables/HoldReveal.csv.
/// </summary>
[DisallowMultipleComponent]
public sealed class HoldReveal : MonoBehaviour
{
    public const string TableName = "HoldReveal", TableRow = "Default";
    private const string ShaderPath = "Combat/Shaders/RevealThroughWalls", CopyName = "Hold Reveal";
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static HoldReveal instance;

    [SerializeField, Tooltip("The holder's silhouette through walls; alpha is how solid it is at the top of a beat.")]
    private Color holderColor = new Color(1f, .16f, .3f, .7f);
    [SerializeField, Min(0f), Tooltip("Beats a minute: the silhouette throbs from dimShare up to full and back.")]
    private float beats = 120f;
    [SerializeField, Range(0f, 1f), Tooltip("How solid the silhouette stays between beats, as a share of holderColor's alpha.")]
    private float dimShare = .55f;

    private readonly List<ZombieGrip> watched = new List<ZombieGrip>();
    private readonly Dictionary<ZombieGrip, List<GameObject>> shown = new Dictionary<ZombieGrip, List<GameObject>>();
    private readonly List<ZombieGrip> done = new List<ZombieGrip>();
    private readonly List<Renderer> found = new List<Renderer>();
    private Material material;
    private float beat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>This grip has started holding someone, on this PC's knowledge (ZombieGrip): show it while it holds.</summary>
    internal static void Watch(ZombieGrip grip)
    {
        if (grip == null || !Application.isPlaying) return;
        if (instance == null)
        {
            instance = new GameObject("Hold Reveal").AddComponent<HoldReveal>();
            GameTables.Bind(TableName, TableRow, instance);
        }
        if (!instance.watched.Contains(grip)) instance.watched.Add(grip);
    }

    private void Awake()
    {
        var shader = Resources.Load<Shader>(ShaderPath);
        if (shader != null) material = new Material(shader) { name = "Hold Reveal (Runtime)", hideFlags = HideFlags.HideAndDontSave };
    }

    private void OnDestroy()
    {
        foreach (var pair in shown) Hide(pair.Key, pair.Value);
        shown.Clear();
        if (material != null) Destroy(material);
        if (instance == this) instance = null;
    }

    private void Update()
    {
        if (material != null)
        {
            beat += Time.unscaledDeltaTime * beats / 60f;
            float pulse = 1f - Mathf.Repeat(beat, 1f);
            var color = holderColor;
            color.a *= Mathf.Lerp(dimShare, 1f, pulse * pulse);
            material.SetColor(ColorId, color);
        }
        done.Clear();
        foreach (var grip in watched)
        {
            bool holding = Holds(grip);
            bool has = grip != null && shown.ContainsKey(grip);
            if (holding && !has) shown.Add(grip, Show(grip));
            else if (!holding && has)
            {
                Hide(grip, shown[grip]);
                shown.Remove(grip);
            }
            if (!holding) done.Add(grip);
        }
        foreach (var grip in done) watched.Remove(grip);
    }

    // Holding someone, alive, and still wearing the body it held them with (a death hands the body to its corpse).
    private static bool Holds(ZombieGrip grip)
    {
        if (grip == null || !grip.isActiveAndEnabled || !grip.IsHolding) return false;
        return grip.TryGetComponent(out EnemyHealth health) && health.IsAlive;
    }

    // A copy of each of its meshes in the reveal material, riding the same bones, and the holder outline on the body.
    private List<GameObject> Show(ZombieGrip grip)
    {
        var copies = new List<GameObject>();
        if (grip.TryGetComponent(out EnemyHealth health)) HitFlashRenderer.For(health)?.SetHolder(1f);
        if (material == null) return copies;
        grip.GetComponentsInChildren(found);
        foreach (var source in found)
        {
            if (!source.enabled || !source.gameObject.activeInHierarchy || ZombieBody.IsEye(source)) continue;
            // Never a copy of a copy: a wave's reveal (EnemyReveal), or this.
            if (source.name == CopyName || source.name == "Reveal") continue;
            Renderer copy;
            Mesh mesh;
            if (source is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                mesh = skinned.sharedMesh;
                var glow = new GameObject(CopyName, typeof(SkinnedMeshRenderer)).GetComponent<SkinnedMeshRenderer>();
                glow.transform.SetParent(skinned.transform, false);
                glow.sharedMesh = mesh;
                glow.bones = skinned.bones;
                glow.rootBone = skinned.rootBone;
                glow.updateWhenOffscreen = true;
                copy = glow;
            }
            else if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                mesh = filter.sharedMesh;
                var holder = new GameObject(CopyName, typeof(MeshFilter), typeof(MeshRenderer));
                holder.transform.SetParent(source.transform, false);
                holder.GetComponent<MeshFilter>().sharedMesh = mesh;
                copy = holder.GetComponent<MeshRenderer>();
            }
            else continue;
            copy.gameObject.layer = source.gameObject.layer;
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
            var slots = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < slots.Length; i++) slots[i] = material;
            copy.sharedMaterials = slots;
            copies.Add(copy.gameObject);
        }
        found.Clear();
        return copies;
    }

    private static void Hide(ZombieGrip grip, List<GameObject> copies)
    {
        foreach (var copy in copies) if (copy != null) Destroy(copy);
        copies.Clear();
        if (grip != null && grip.TryGetComponent(out HitFlashRenderer flash)) flash.SetHolder(0f);
    }
}
