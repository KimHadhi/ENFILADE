using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A downed teammate glows through walls, so their friends can find them, and the glow tells how long they have: it
/// turns from gold to red and its heartbeat quickens as they bleed out. It softens up close, where it would only get in
/// the way. A copy of each skinned mesh carries it, because an extra material on the real mesh only covers its last
/// submesh. They're made with the body and wait switched off. Numbers: Tables/DownedAura.
/// </summary>
[DisallowMultipleComponent]
public sealed class DownedAura : MonoBehaviour
{
    public const string TableName = "DownedAura", TableRow = "Default";
    private const string ShaderPath = "Combat/Shaders/TeammateAura";
    private static readonly int ColorId = Shader.PropertyToID("_AuraColor"), RimId = Shader.PropertyToID("_Rim"), WidthId = Shader.PropertyToID("_OutlineWidth");

    [SerializeField] private Color calmColor = new Color(1f, .76f, .28f), urgentColor = new Color(1f, .12f, .18f);
    [SerializeField, Min(1f), Tooltip("Heartbeats a minute just after going down, and at the very end.")]
    private float calmBeats = 60f, urgentBeats = 170f;
    [SerializeField, Min(0f), Tooltip("Colour multiplier: above 1 it blooms.")]
    private float brightness = 1.6f;
    [SerializeField, Min(0f)] private float rimStrength = .5f, outlineWidth = 3f;
    [SerializeField, Min(.1f), Tooltip("Closer than this it starts to fade (m).")]
    private float nearDistance = 3f;

    private readonly List<SkinnedMeshRenderer> glows = new List<SkinnedMeshRenderer>();
    private PlayerMovement player;
    private PlayerBody body;
    private Animator builtFor;
    private Material material;
    private float startHealth = 1f, beat;
    private bool shown;

    /// <summary>How far they have bled out: 0 just down, 1 at the end.</summary>
    public float Urgency { get; private set; }
    public Color Color => Color.Lerp(calmColor, urgentColor, Urgency);

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        body = GetComponent<PlayerBody>();
    }

    private void LateUpdate()
    {
        if (!player.IsInitialized) return;
        Build();
        bool downed = player.IsDowned;
        if (downed != shown)
        {
            shown = downed;
            if (downed) { startHealth = Mathf.Max(1f, player.DamageReceiver.Health); beat = 0f; }
            foreach (var glow in glows) if (glow != null) glow.enabled = downed;
        }
        if (!downed || material == null) return;
        Urgency = 1f - Mathf.Clamp01(player.DamageReceiver.Health / startHealth);
        beat += Time.unscaledDeltaTime * Mathf.Lerp(calmBeats, urgentBeats, Urgency) / 60f;
        float pulse = 1f - Mathf.Repeat(beat, 1f);
        float strength = Mathf.Lerp(.55f, 1f, pulse * pulse * pulse);
        var mine = PlayerRoster.Local;
        float near = mine != null ? Mathf.Clamp01(Vector3.Distance(mine.transform.position, transform.position) / nearDistance) : 1f;
        material.SetColor(ColorId, Color * brightness * strength);
        material.SetFloat(RimId, rimStrength * near);
        material.SetFloat(WidthId, outlineWidth * Mathf.Lerp(.4f, 1f, near));
    }

    // Parented to the player, not the model: the bones drive them, and the ragdoll copies the model.
    private void Build()
    {
        var animator = body != null ? body.BodyAnimator : null;
        if (animator == null || animator == builtFor) return;
        builtFor = animator;
        foreach (var glow in glows) if (glow != null) Destroy(glow.gameObject);
        glows.Clear();
        shown = false;
        if (material == null)
        {
            var shader = Resources.Load<Shader>(ShaderPath);
            if (shader == null) return;
            material = new Material(shader) { name = "Teammate Aura (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        }
        foreach (var source in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (source.sharedMesh == null) continue;
            var glow = new GameObject("Aura", typeof(SkinnedMeshRenderer)).GetComponent<SkinnedMeshRenderer>();
            glow.transform.SetParent(transform, false);
            glow.sharedMesh = source.sharedMesh;
            glow.bones = source.bones;
            glow.rootBone = source.rootBone;
            glow.updateWhenOffscreen = true;
            glow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glow.receiveShadows = false;
            var slots = new Material[source.sharedMesh.subMeshCount];
            for (int i = 0; i < slots.Length; i++) slots[i] = material;
            glow.sharedMaterials = slots;
            glow.enabled = false;
            glows.Add(glow);
        }
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
}
