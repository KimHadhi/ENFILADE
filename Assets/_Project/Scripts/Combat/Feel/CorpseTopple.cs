using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Kill read for enemies that are destroyed on death: a visual copy of the body flashes white-hot, is shoved along the
/// killing blow and topples over its leading edge, slams down with a thud and dust, lies briefly, then sinks away.
/// It has no colliders and no gameplay; the real enemy is still destroyed as before (wave counts, Died event).
/// Replace with real death animation/ragdoll later by turning off GunFeel → Death Topple.
/// </summary>
[DisallowMultipleComponent]
public sealed class CorpseTopple : MonoBehaviour
{
    private static readonly List<Renderer> renderers = new List<Renderer>();
    private Vector3 pivot, direction, axis;
    private float age, slide, toppleSeconds, lingerSeconds, sinkSeconds, height;
    private bool landed;

    public static CorpseTopple Spawn(Component target, DamageHit hit, bool critical)
    {
        var settings = GunFeelSettings.Instance;
        if (!Application.isPlaying || target == null || !settings.deathTopple) return null;
        renderers.Clear();
        foreach (var renderer in target.GetComponentsInChildren<Renderer>())
            if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && renderer.enabled && renderer.gameObject.activeInHierarchy
                && renderer.GetComponentInParent<Canvas>() == null
                && (!(renderer is MeshRenderer) || (renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)))
                renderers.Add(renderer);
        if (renderers.Count == 0) return null;

        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        Vector3 direction = Vector3.ProjectOnPlane(hit.Direction, Vector3.up);
        if (direction.sqrMagnitude < .01f && hit.Source != null) direction = Vector3.ProjectOnPlane(target.transform.position - hit.Source.transform.position, Vector3.up);
        if (direction.sqrMagnitude < .01f) direction = -target.transform.forward;
        direction.Normalize();
        // Topple over the leading bottom edge, so the body lands on its side instead of sinking into the floor.
        float reach = Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.z) * bounds.extents.z;
        Vector3 pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) + direction * reach;

        var root = new GameObject(target.name + " (Corpse)");
        root.transform.SetPositionAndRotation(pivot, Quaternion.identity);
        var block = new MaterialPropertyBlock();
        var overlay = HitFlashRenderer.OverlayMaterial;
        var materials = new List<Material>();
        foreach (var renderer in renderers)
        {
            var part = new GameObject(renderer.name);
            part.transform.SetParent(root.transform, false);
            Mesh mesh;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                mesh = new Mesh { name = renderer.name + " (Baked)" };
                skinned.BakeMesh(mesh, true);
                part.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                part.AddComponent<CorpseMeshOwner>().mesh = mesh;
            }
            else
            {
                mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                part.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                part.transform.localScale = renderer.transform.lossyScale;
            }
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var copy = part.AddComponent<MeshRenderer>();
            renderer.GetSharedMaterials(materials);
            if (overlay != null) materials.RemoveAll(material => material == overlay);
            copy.SetSharedMaterials(materials);
            copy.shadowCastingMode = renderer.shadowCastingMode;
            // Keep runtime tints (e.g. the grey/yellow attack indicator) the body had when it died.
            renderer.GetPropertyBlock(block);
            copy.SetPropertyBlock(block);
        }

        var corpse = root.AddComponent<CorpseTopple>();
        corpse.pivot = pivot; corpse.direction = direction; corpse.axis = Vector3.Cross(Vector3.up, direction);
        corpse.height = bounds.size.y;
        corpse.toppleSeconds = settings.toppleSeconds * (critical ? .85f : 1f);
        corpse.lingerSeconds = settings.corpseLingerSeconds;
        corpse.sinkSeconds = settings.corpseSinkSeconds;
        // Do not slide through walls.
        float slide = settings.deathSlide * (critical ? 1.3f : 1f);
        if (Physics.Raycast(bounds.center, direction, out RaycastHit wall, slide + reach, ~0, QueryTriggerInteraction.Ignore)
            && !wall.collider.transform.IsChildOf(target.transform))
            slide = Mathf.Max(0f, wall.distance - reach - .05f);
        corpse.slide = slide;
        HitFlashRenderer.For(root.transform).KillFlash(hit.Point, critical ? .16f : .11f);
        return corpse;
    }

    private void Update()
    {
        age += Time.deltaTime;
        float topple = Mathf.Clamp01(age / Mathf.Max(.05f, toppleSeconds));
        // Gravity-like: slow start, fast finish, then a small rebound off the floor.
        float angle = 90f * topple * topple;
        if (topple >= 1f)
        {
            float after = age - toppleSeconds;
            angle -= 7f * Mathf.Sin(Mathf.Clamp01(after / .22f) * Mathf.PI) * Mathf.Exp(-after * 6f);
            if (!landed) Land();
        }
        float travel = slide * (1f - Mathf.Pow(1f - Mathf.Clamp01(age / (toppleSeconds * 1.2f)), 3f));
        float sinkStart = toppleSeconds + lingerSeconds;
        float sink = Mathf.Clamp01((age - sinkStart) / sinkSeconds);
        transform.SetPositionAndRotation(pivot + direction * travel - Vector3.up * (sink * sink * .6f),
            Quaternion.AngleAxis(angle, axis));
        transform.localScale = Vector3.one * (1f - sink * .25f);
        if (sink >= 1f) Destroy(gameObject);
    }

    private void Land()
    {
        landed = true;
        Vector3 impact = pivot + direction * (slide + height * .5f);
        ImpactVfx.Dust(impact, Mathf.Clamp(height * .6f, .5f, 1.6f));
        // Reuses the imported body-thud clips when present, otherwise the synthesized fall.
        FeelAudio.PlayAt(FeelSound.BodyFall, CombatFeedbackSettings.Instance?.bodyThudSounds, impact, .6f, Random.Range(.85f, 1f), 18f);
        var view = Camera.main;
        if (view != null && Vector3.Distance(view.transform.position, impact) < 7f)
            CameraViewPunch.For(view)?.Punch(Vector3.zero, 0f, .12f);
    }
}
