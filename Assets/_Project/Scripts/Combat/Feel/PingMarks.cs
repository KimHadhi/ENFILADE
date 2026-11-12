using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Pinged enemies (PlayerPing, stage 3 of the classes; the user's Back 4 Blood ping): F on an enemy marks it for the
/// whole team for a few seconds, on every PC (the pinger's at once, the others as PlayerNetworkCombat tells them): its
/// outline in the ping's colour where it can be seen (HitFlashRenderer.SetPing), a flat throbbing silhouette where
/// something stands in front of it (Hidden/ENFILADE/RevealThroughWalls on copies of its meshes, as HoldReveal does), and
/// a diamond over its head with how far it is. A plain ping is the HUD's pink; a Schießhund's is its teal bounty mark,
/// which its class hunts (ClassKit) and lasts longer. Each player has one of each at a time: a new one moves it. A mark
/// ends with its time or its enemy's life. Made on first need and lives with the scene. Numbers: Tables/ClassAbilities,
/// Ping.* and Schiesshund.markSeconds. Any foe takes a mark (ICombatFoe): a zombie, or a target on the hub's range, whose
/// mark ends as it tips over.
/// </summary>
[DisallowMultipleComponent]
public sealed class PingMarks : MonoBehaviour
{
    private const string ShaderPath = "Combat/Shaders/RevealThroughWalls", CopyName = "Ping Reveal";
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    // The diamond's size, how far over the head it stands (m), how much of the screen's edge it keeps off (px at 1080p),
    // and how long a new mark takes to land (s).
    private const float DiamondSize = 22f, OverHead = .45f, EdgeMargin = 40f, LandSeconds = .18f;

    /// <summary>A plain ping's colour: the HUD's pink.</summary>
    public static Color PingColor => KitStyle.Accent;
    /// <summary>A Schießhund's bounty mark: teal (the user's).</summary>
    public static readonly Color BountyColor = new Color(.12f, .92f, .82f, 1f);

    private sealed class Mark
    {
        // A zombie or a range target, and the same as the foe it is.
        public Component Enemy;
        public ICombatFoe Foe;
        public PlayerMovement By;
        public bool Bounty;
        public float Until, Since;
        public readonly List<GameObject> Copies = new List<GameObject>();
        public RectTransform Marker;
        public DiamondGraphic Diamond;
        public TextMeshProUGUI Distance;
    }

    private static PingMarks instance;
    private readonly List<Mark> marks = new List<Mark>();
    private readonly List<Renderer> found = new List<Renderer>();
    private Material pingMaterial, bountyMaterial;
    private RectTransform canvas;
    private float beat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>
    /// Marks this foe (a zombie, or a range target in the hub) as pinged by <paramref name="by"/>: a bounty if they play
    /// a Schießhund (PlayerPing decides on their own PC and says so to the others). Their earlier mark of the same kind
    /// moves here.
    /// </summary>
    public static void Ping(Component enemy, PlayerMovement by, bool bounty)
    {
        if (!Application.isPlaying || !CombatFoe.IsAlive(enemy)) return;
        if (instance == null) instance = new GameObject("Ping Marks").AddComponent<PingMarks>();
        instance.Add(enemy, (ICombatFoe)enemy, by, bounty);
    }

    /// <summary>Whether this enemy carries <paramref name="hunter"/>'s bounty mark now (a Schießhund's hunt: ClassKit, AttackTrace).</summary>
    public static bool IsBountyOf(Component enemy, PlayerMovement hunter)
    {
        if (instance == null || enemy == null || hunter == null) return false;
        foreach (var mark in instance.marks)
            if (mark.Bounty && mark.By == hunter && mark.Enemy != null && mark.Enemy.gameObject == enemy.gameObject && Time.time < mark.Until)
                return true;
        return false;
    }

    /// <summary>
    /// Whether anyone's mark (a ping or a bounty) is on this foe now: its outline is the mark's, so a Schießhund's bounty
    /// dye (DyeMarks), which outlines through the same channel, leaves it alone.
    /// </summary>
    public static bool IsMarked(Component enemy)
    {
        if (instance == null || enemy == null) return false;
        foreach (var mark in instance.marks)
            if (mark.Enemy != null && mark.Enemy.gameObject == enemy.gameObject && Time.time < mark.Until) return true;
        return false;
    }

    /// <summary>The foe carrying <paramref name="hunter"/>'s bounty mark now, or null (the HUD's).</summary>
    public static Component BountyOf(PlayerMovement hunter)
    {
        if (instance == null || hunter == null) return null;
        foreach (var mark in instance.marks)
            if (mark.Bounty && mark.By == hunter && mark.Enemy != null && mark.Foe.IsAlive && Time.time < mark.Until) return mark.Enemy;
        return null;
    }

    private void Awake()
    {
        var shader = Resources.Load<Shader>(ShaderPath);
        if (shader != null)
        {
            // Ahead of the teammates' auras (Transparent+50), as HoldReveal is, so the stencil there doesn't cut it.
            pingMaterial = new Material(shader) { name = "Ping Reveal (Runtime)", hideFlags = HideFlags.HideAndDontSave, renderQueue = 3049 };
            bountyMaterial = new Material(shader) { name = "Bounty Reveal (Runtime)", hideFlags = HideFlags.HideAndDontSave, renderQueue = 3049 };
        }
        var host = new GameObject("Ping markers", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        host.transform.SetParent(transform, false);
        var overlay = host.GetComponent<Canvas>();
        overlay.renderMode = RenderMode.ScreenSpaceOverlay;
        overlay.sortingOrder = 72;
        var scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        canvas = (RectTransform)host.transform;
    }

    private void OnDestroy()
    {
        foreach (var mark in marks) Clear(mark);
        marks.Clear();
        if (pingMaterial != null) Destroy(pingMaterial);
        if (bountyMaterial != null) Destroy(bountyMaterial);
        if (instance == this) instance = null;
    }

    private void Add(Component enemy, ICombatFoe foe, PlayerMovement by, bool bounty)
    {
        // Their earlier mark of this kind goes; so does anyone's mark already on this enemy, which this one takes over.
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            var old = marks[i];
            if ((old.By == by && old.Bounty == bounty) || old.Enemy == enemy) { Clear(old); marks.RemoveAt(i); }
        }
        float seconds = bounty ? PlayerClass.Ability("Schiesshund", "markSeconds", 15f) : PlayerClass.Ability("Ping", "seconds", 8f);
        var mark = new Mark
        {
            Enemy = enemy, Foe = foe, By = by, Bounty = bounty, Until = Time.time + Mathf.Max(.5f, seconds), Since = Time.unscaledTime
        };
        Reveal(mark);
        HitFlashRenderer.For(enemy)?.SetPing(1f, bounty ? BountyColor : PingColor);
        mark.Marker = KitStyle.TopLeft(bounty ? "Bounty mark" : "Ping mark", canvas, Vector2.zero, new Vector2(140f, 60f));
        mark.Marker.anchorMin = mark.Marker.anchorMax = Vector2.zero;
        mark.Marker.pivot = new Vector2(.5f, 0f);
        mark.Diamond = HudShapes.Make<DiamondGraphic>("Diamond", mark.Marker, bounty ? BountyColor : PingColor);
        var diamond = mark.Diamond.rectTransform;
        diamond.anchorMin = diamond.anchorMax = new Vector2(.5f, 0f);
        diamond.sizeDelta = Vector2.one * DiamondSize;
        diamond.anchoredPosition = new Vector2(0f, DiamondSize * .5f);
        mark.Distance = KitStyle.Label(KitStyle.TopLeft("Distance", mark.Marker, Vector2.zero, new Vector2(140f, 22f)), "", 14f,
            TextAlignmentOptions.Center, KitStyle.Ice);
        var label = mark.Distance.rectTransform.parent as RectTransform;
        label.anchorMin = label.anchorMax = new Vector2(.5f, 0f);
        label.pivot = new Vector2(.5f, 0f);
        label.anchoredPosition = new Vector2(0f, DiamondSize + 4f);
        marks.Add(mark);
    }

    private void Update()
    {
        beat += Time.unscaledDeltaTime * 2f;
        float pulse = 1f - Mathf.Repeat(beat, 1f);
        Tint(pingMaterial, PingColor, pulse);
        Tint(bountyMaterial, BountyColor, pulse);
        var view = Camera.main;
        var local = PlayerRoster.Local;
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            var mark = marks[i];
            if (mark.Enemy == null || !mark.Foe.IsAlive || Time.time >= mark.Until) { Clear(mark); marks.RemoveAt(i); continue; }
            Place(mark, view, local);
        }
    }

    private static void Tint(Material material, Color color, float pulse)
    {
        if (material == null) return;
        color.a = .62f * Mathf.Lerp(.55f, 1f, pulse * pulse);
        material.SetColor(ColorId, color);
    }

    // The diamond over its head, landing from large as the mark is made, kept on screen at its edge; how far off it is.
    private void Place(Mark mark, Camera view, PlayerMovement local)
    {
        if (mark.Marker == null) return;
        // Over its head: a zombie's capsule's top, a range target's head part (CombatFoe.Top).
        Vector3 head = CombatFoe.Top(mark.Enemy) + Vector3.up * OverHead;
        Vector3 screen = view != null ? view.WorldToScreenPoint(head) : Vector3.zero;
        bool shown = view != null && screen.z > 0f;
        mark.Marker.gameObject.SetActive(shown);
        if (!shown) return;
        float scale = canvas.rect.width / Mathf.Max(1f, Screen.width);
        Vector2 at = new Vector2(screen.x * scale, screen.y * scale);
        at.x = Mathf.Clamp(at.x, EdgeMargin, canvas.rect.width - EdgeMargin);
        at.y = Mathf.Clamp(at.y, EdgeMargin, canvas.rect.height - EdgeMargin - 40f);
        mark.Marker.anchoredPosition = at;
        float landed = Mathf.Clamp01((Time.unscaledTime - mark.Since) / LandSeconds);
        mark.Marker.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, KitReveal.EaseOut(landed));
        // A bounty's diamond turns slowly, the hound's eye on it.
        mark.Diamond.rectTransform.localRotation = mark.Bounty ? Quaternion.Euler(0f, 0f, Mathf.Sin(Time.unscaledTime * 3f) * 12f) : Quaternion.identity;
        float metres = local != null ? Vector3.Distance(local.transform.position, mark.Enemy.transform.position) : 0f;
        string text = Mathf.RoundToInt(metres) + "m";
        if (mark.Distance.text != text) mark.Distance.text = text;
    }

    // A copy of each of its meshes in the mark's reveal material, riding the same bones (HoldReveal's way).
    private void Reveal(Mark mark)
    {
        var material = mark.Bounty ? bountyMaterial : pingMaterial;
        if (material == null) return;
        mark.Enemy.GetComponentsInChildren(found);
        foreach (var source in found)
        {
            if (!source.enabled || !source.gameObject.activeInHierarchy || ZombieBody.IsEye(source)) continue;
            // Never a copy of a copy: a wave's reveal, a holder's, or this.
            if (source.name == CopyName || source.name == "Reveal" || source.name == "Hold Reveal") continue;
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
            mark.Copies.Add(copy.gameObject);
        }
        found.Clear();
    }

    private static void Clear(Mark mark)
    {
        foreach (var copy in mark.Copies) if (copy != null) Destroy(copy);
        mark.Copies.Clear();
        if (mark.Enemy != null && mark.Enemy.TryGetComponent(out HitFlashRenderer flash)) flash.SetPing(0f, Color.clear);
        if (mark.Marker != null) Destroy(mark.Marker.gameObject);
    }
}
