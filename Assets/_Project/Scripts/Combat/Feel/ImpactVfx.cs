using UnityEngine;

/// <summary>
/// Procedural impact matter: blood spray (mostly out of the exit side, some back toward the shooter), a blood mist puff,
/// blood splats on the nearest floor/wall behind the target, and dust puffs for bodies hitting the ground.
/// Headshots and kills spray harder. Pooled particle systems and decals; created on first use during Play.
/// </summary>
[DisallowMultipleComponent]
public sealed class ImpactVfx : MonoBehaviour
{
    private const int DecalCount = 32;
    private static ImpactVfx instance;
    private ParticleSystem droplets, mist, dust;
    private readonly Transform[] decals = new Transform[DecalCount];
    private readonly MeshRenderer[] decalRenderers = new MeshRenderer[DecalCount];
    private readonly float[] decalAge = new float[DecalCount];
    private readonly Color[] decalColor = new Color[DecalCount];
    private readonly RaycastHit[] hits = new RaycastHit[8];
    private MaterialPropertyBlock block;
    private int nextDecal;

    private static ImpactVfx Get()
    {
        if (instance == null) instance = new GameObject("Impact VFX").AddComponent<ImpactVfx>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        block = new MaterialPropertyBlock();

        droplets = FeelAssets.CreateEmitter("Blood Droplets", transform, FeelAssets.Particle(FeelAssets.SoftDot, false), 600, 1.3f,
            ParticleSystemRenderMode.Stretch);
        var stretch = droplets.GetComponent<ParticleSystemRenderer>();
        stretch.velocityScale = .045f; stretch.lengthScale = 1.1f;
        var dropletSize = droplets.sizeOverLifetime; dropletSize.enabled = true;
        dropletSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, .45f));

        mist = FeelAssets.CreateEmitter("Blood Mist", transform, FeelAssets.Particle(FeelAssets.SoftDot, false), 120, .05f);
        var mistSize = mist.sizeOverLifetime; mistSize.enabled = true;
        mistSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .5f, 1f, 1.6f));
        var mistColor = mist.colorOverLifetime; mistColor.enabled = true;
        mistColor.color = FeelAssets.Fade(Color.white, new Color(1f, 1f, 1f, 0f));
        var mistDrag = mist.limitVelocityOverLifetime; mistDrag.enabled = true; mistDrag.drag = 6f; mistDrag.multiplyDragByParticleSize = false;

        dust = FeelAssets.CreateEmitter("Body Dust", transform, FeelAssets.Particle(FeelAssets.SoftDot, false), 120, -.02f);
        var dustSize = dust.sizeOverLifetime; dustSize.enabled = true;
        dustSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .6f, 1f, 2.2f));
        var dustColor = dust.colorOverLifetime; dustColor.enabled = true;
        dustColor.color = FeelAssets.Fade(Color.white, new Color(1f, 1f, 1f, 0f));
        var dustDrag = dust.limitVelocityOverLifetime; dustDrag.enabled = true; dustDrag.drag = 4f; dustDrag.multiplyDragByParticleSize = false;
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    /// <summary>direction is the attack direction (into the target). bullet = directional exit spray; otherwise a rounder burst.</summary>
    public static void Blood(Vector3 point, Vector3 direction, bool critical, bool kill, bool bullet, Component target)
    {
        if (!Application.isPlaying || !GunFeelSettings.Instance.proceduralBlood || point == Vector3.zero) return;
        Get().Spray(point, direction, critical, kill, bullet, target);
    }

    public static void Dust(Vector3 point, float scale)
    {
        if (!Application.isPlaying) return;
        Get().Puff(point, scale);
    }

    private void Spray(Vector3 point, Vector3 direction, bool critical, bool kill, bool bullet, Component target)
    {
        var settings = GunFeelSettings.Instance;
        if (direction.sqrMagnitude < .0001f) direction = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
        direction.Normalize();
        int count = kill ? settings.killDroplets : critical ? settings.headDroplets : settings.bodyDroplets;
        if (!bullet) count = Mathf.RoundToInt(count * .6f);
        float force = (critical ? 1.35f : 1f) * (kill ? 1.25f : 1f);
        var emit = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            // About two thirds leave through the exit side; the rest splash back toward the shooter.
            bool exit = !bullet || Random.value < .68f;
            Vector3 axis = exit ? direction : -direction;
            Vector3 velocity = Cone(axis, bullet ? (exit ? 28f : 40f) : 75f) * Random.Range(1.2f, 4.6f) * force + Vector3.up * Random.Range(0f, 1.3f);
            emit.position = point + Random.insideUnitSphere * .03f;
            emit.velocity = velocity;
            emit.startSize = Random.Range(.012f, .034f) * (critical ? 1.3f : 1f);
            emit.startLifetime = Random.Range(.35f, .75f);
            emit.startColor = Shade(settings.bloodColor, Random.Range(.75f, 1.15f));
            droplets.Emit(emit, 1);
        }
        int puffs = kill ? 5 : critical ? 4 : 2;
        for (int i = 0; i < puffs; i++)
        {
            emit.position = point + Random.insideUnitSphere * .04f;
            emit.velocity = (direction * Random.Range(.2f, 1.2f) + Random.insideUnitSphere * .5f) * force;
            emit.startSize = Random.Range(.14f, .26f) * (critical ? 1.35f : 1f);
            emit.startLifetime = Random.Range(.22f, .42f);
            Color color = Shade(settings.bloodColor, 1.1f); color.a = .55f;
            emit.startColor = color;
            emit.rotation = Random.Range(0f, 360f);
            mist.Emit(emit, 1);
        }
        // Splats: behind the target along the shot, and on the floor below the wound.
        Transform ignore = target != null ? target.transform : null;
        if (bullet) Splat(point + direction * .05f, direction, 3.5f, ignore, critical || kill ? .55f : .38f);
        Splat(point, Vector3.down, 2.6f, ignore, kill ? .7f : critical ? .5f : .34f);
    }

    private void Splat(Vector3 origin, Vector3 direction, float distance, Transform ignore, float size)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity; RaycastHit chosen = default; bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.distance >= best || (ignore != null && hit.collider.transform.IsChildOf(ignore))) continue;
            // Only static scenery takes blood: not other enemies, targets or the player.
            var body = hit.collider.attachedRigidbody;
            if (hit.collider.GetComponentInParent<IDamageReceiver>() != null || (body != null && !body.isKinematic)) continue;
            best = hit.distance; chosen = hit; found = true;
        }
        if (!found) return;
        int slot = nextDecal; nextDecal = (nextDecal + 1) % DecalCount;
        if (decals[slot] == null)
        {
            var host = new GameObject("Blood Splat");
            host.transform.SetParent(transform, false);
            host.AddComponent<MeshFilter>().sharedMesh = FeelAssets.Quad;
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = FeelAssets.Particle(FeelAssets.BloodSplat, false);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            decals[slot] = host.transform; decalRenderers[slot] = renderer;
        }
        // Tiny per-slot lift keeps overlapping splats from z-fighting.
        float lift = .006f + slot * .0004f;
        decals[slot].SetPositionAndRotation(chosen.point + chosen.normal * lift,
            Quaternion.LookRotation(-chosen.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
        decals[slot].localScale = Vector3.one * size * Random.Range(.8f, 1.2f);
        decalAge[slot] = 0f;
        decalColor[slot] = Shade(GunFeelSettings.Instance.bloodColor, Random.Range(.6f, .85f));
        decals[slot].gameObject.SetActive(true);
        Tint(slot, 1f);
    }

    private void Puff(Vector3 point, float scale)
    {
        var emit = new ParticleSystem.EmitParams();
        int count = Mathf.RoundToInt(6 * Mathf.Clamp(scale, .3f, 2f));
        for (int i = 0; i < count; i++)
        {
            Vector2 ring = Random.insideUnitCircle.normalized;
            emit.position = point + new Vector3(ring.x, .05f, ring.y) * .25f * scale;
            emit.velocity = new Vector3(ring.x, .25f, ring.y) * Random.Range(.8f, 1.8f) * scale;
            emit.startSize = Random.Range(.25f, .45f) * scale;
            emit.startLifetime = Random.Range(.6f, 1f);
            emit.startColor = new Color(.62f, .58f, .52f, .35f);
            emit.rotation = Random.Range(0f, 360f);
            dust.Emit(emit, 1);
        }
    }

    private void Update()
    {
        float seconds = GunFeelSettings.Instance.bloodDecalSeconds;
        for (int i = 0; i < DecalCount; i++)
        {
            if (decals[i] == null || !decals[i].gameObject.activeSelf) continue;
            decalAge[i] += Time.deltaTime;
            float fade = 1f - Mathf.Clamp01((decalAge[i] - seconds) / 1.2f);
            if (fade <= 0f) { decals[i].gameObject.SetActive(false); continue; }
            if (decalAge[i] > seconds) Tint(i, fade);
        }
    }

    private void Tint(int slot, float alpha)
    {
        Color color = decalColor[slot]; color.a *= alpha;
        block.SetColor("_Color", color);
        decalRenderers[slot].SetPropertyBlock(block);
    }

    private static Vector3 Cone(Vector3 axis, float degrees)
    {
        Vector3 random = Random.onUnitSphere;
        return Vector3.Slerp(axis, random, Random.Range(0f, degrees / 180f)).normalized;
    }

    private static Color Shade(Color color, float value)
        => new Color(Mathf.Clamp01(color.r * value), Mathf.Clamp01(color.g * value), Mathf.Clamp01(color.b * value), color.a);
}
