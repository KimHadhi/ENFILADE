using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Gore's blood (ZombieGore): the burst when a part comes off (the impact pack's splashes plus ImpactVfx's spray), a stump
/// that pumps blood a few times, weaker each time, a torn face of gut on every cut, and the RVFX pack's projected blood
/// decals: a big splat on the wall behind a burst head, splats where pieces land, and a pool that spreads under a stump.
/// Drops are one shared particle system that dies on the world; decals come from a pool that recycles the oldest.
/// </summary>
internal sealed class GoreFx
{
    // A stump's beat (s) and how much each beat keeps of the last.
    private const float Beat = .27f, BeatDecay = .7f;
    private const float PoolDelay = .6f, PoolGrow = 2.5f, SplatFade = 2f;
    private static readonly int SolidMask = ~0;

    private sealed class Spurt
    {
        public Transform root;
        public float radius, age, seconds, carry;
        public bool pooled;
    }

    private sealed class Decal
    {
        public DecalProjector projector;
        public float age, life, grow, from, to;
    }

    private readonly ZombieGore owner;
    private readonly ParticleSystem drops;
    private readonly List<Spurt> spurts = new List<Spurt>();
    private readonly List<Decal> decals = new List<Decal>();
    private readonly RaycastHit[] hits = new RaycastHit[8];
    private ParticleSystem.EmitParams emit;
    private int nextDecal, nextGut;

    public GoreFx(ZombieGore owner, Transform parent)
    {
        this.owner = owner;
        drops = FeelAssets.CreateEmitter("Gore Drops", parent, FeelAssets.Particle(FeelAssets.SoftDot, false), 1500, 1f,
            ParticleSystemRenderMode.Stretch);
        var stretch = drops.GetComponent<ParticleSystemRenderer>();
        stretch.velocityScale = .05f; stretch.lengthScale = 1.2f;
        var size = drops.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, .5f));
        // Drops end where they meet the world instead of falling through the floor.
        var collision = drops.collision; collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World; collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.quality = ParticleSystemCollisionQuality.Medium; collision.lifetimeLoss = 1f; collision.bounce = 0f;
        collision.collidesWith = LayerMask.GetMask("Default", "World");
        drops.Play();
    }

    /// <summary>A part came off here: a splash along the shot and the spray ImpactVfx throws for a kill.</summary>
    public void Wound(Vector3 at, Vector3 direction, Component target, bool big)
    {
        ImpactPool.Play(big ? "BloodKill" : "BloodHead", at - direction * .05f, -direction, big ? 1.4f : 1f);
        ImpactPool.Play("BloodExit", at + direction * .05f, direction, big ? 1.2f : .9f);
        ImpactVfx.Blood(at, direction, true, true, true, target);
        ImpactVfx.ExitWound(at + direction * .05f, direction, target);
        Spray(at, direction, big ? 90 : 45, big ? 6.5f : 4.5f);
    }

    /// <summary>A burst head's guts flung along the shot, and its blood on the wall behind if there is one near.</summary>
    public void Burst(Vector3 at, Vector3 direction, Transform body, float speed, int guts)
    {
        for (int i = 0; i < guts; i++)
        {
            var velocity = (direction + Random.insideUnitSphere * .55f).normalized * speed * Random.Range(.5f, 1.1f) + Vector3.up * Random.Range(.5f, 2.5f);
            owner.Gibs.Gut(at + Random.insideUnitSphere * .06f, velocity, Random.Range(.09f, .16f), nextGut++);
        }
        if (Cast(at, direction, 4.5f, body, out var hit)) Splat(hit.point, hit.normal, owner.WallSplat, Random.Range(0, int.MaxValue));
    }

    /// <summary>A stump on the body: a torn face of gut at the joint, a few beats of blood and a pool spreading under it.</summary>
    public void Stump(Transform root, BodyPart part)
    {
        if (root == null || root.parent == null) return;
        float radius = BodyParts.CutRadius(part);
        Vector3 outward = (root.position - root.parent.position).normalized;
        Cap(root.parent, root.position, outward, radius);
        spurts.Add(new Spurt { root = root, radius = radius, seconds = owner.SpurtSeconds * (part == BodyPart.Head ? 1f : .7f) });
    }

    /// <summary>A torn face of gut on a cut, under `parent`, facing `outward`.</summary>
    public void Cap(Transform parent, Vector3 at, Vector3 outward, float radius)
    {
        var library = GoreLibrary.Instance;
        if (parent == null || library == null || !library.Gut(nextGut++, out var mesh, out var material)) return;
        var cap = new GameObject("Gore Stump");
        cap.layer = parent.gameObject.layer;
        cap.transform.SetParent(parent, true);
        if (outward.sqrMagnitude < 1e-4f) outward = parent.up;
        // Flattened along the cut, so it reads as a face rather than a lump.
        cap.transform.SetPositionAndRotation(at + outward * radius * .15f,
            Quaternion.LookRotation(outward) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
        float scale = radius * 2.6f / Mathf.Max(.01f, mesh.bounds.size.magnitude * .6f);
        Vector3 lossy = parent.lossyScale;
        cap.transform.localScale = new Vector3(scale / Mathf.Max(1e-4f, Mathf.Abs(lossy.x)), scale / Mathf.Max(1e-4f, Mathf.Abs(lossy.y)),
            scale * .55f / Mathf.Max(1e-4f, Mathf.Abs(lossy.z)));
        cap.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = cap.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>A trail drop off a flying piece; `strength` 0..1 thins it as it goes.</summary>
    public void Drip(Vector3 at, Vector3 velocity, float strength)
    {
        if (Random.value > strength * .8f) return;
        emit.position = at + Random.insideUnitSphere * .03f;
        emit.velocity = velocity * .3f + Random.insideUnitSphere * .4f;
        Drop(Random.Range(.012f, .024f), Random.Range(.4f, .8f));
    }

    public void Tick(float dt)
    {
        for (int i = spurts.Count - 1; i >= 0; i--)
        {
            var spurt = spurts[i];
            if (spurt.root == null || spurt.root.parent == null) { spurts.RemoveAt(i); continue; }
            spurt.age += dt;
            Vector3 at = spurt.root.position, outward = (spurt.root.position - spurt.root.parent.position).normalized;
            if (spurt.age < spurt.seconds)
            {
                // Beats that fade: each pumps hard and drops off, weaker than the last.
                float phase = spurt.age / Beat, beat = Mathf.Floor(phase), within = phase - beat;
                float push = Mathf.Pow(BeatDecay, beat) * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(within * 1.4f)), 2f)
                    * (1f - spurt.age / spurt.seconds);
                spurt.carry += owner.SpurtRate * push * dt;
                while (spurt.carry >= 1f)
                {
                    spurt.carry -= 1f;
                    emit.position = at + Random.insideUnitSphere * spurt.radius * .6f;
                    emit.velocity = (outward + Random.insideUnitSphere * .35f).normalized * Random.Range(1.4f, 3.4f) * (.5f + .5f * push);
                    Drop(Random.Range(.012f, .028f), Random.Range(.45f, .9f));
                }
            }
            if (!spurt.pooled && spurt.age >= PoolDelay)
            {
                spurt.pooled = true;
                if (Cast(at + Vector3.up * .3f, Vector3.down, 3f, spurt.root.root, out var ground))
                    Pool(ground.point, ground.normal);
            }
            if (spurt.pooled && spurt.age >= spurt.seconds) spurts.RemoveAt(i);
        }

        for (int i = 0; i < decals.Count; i++)
        {
            var decal = decals[i];
            if (decal.projector == null || !decal.projector.enabled) continue;
            decal.age += dt;
            if (decal.grow > 0f)
            {
                float s = Mathf.Lerp(decal.from, decal.to, 1f - Mathf.Pow(1f - Mathf.Clamp01(decal.age / decal.grow), 2f));
                decal.projector.size = new Vector3(s, s, decal.projector.size.z);
            }
            float left = decal.life - decal.age;
            decal.projector.fadeFactor = Mathf.Clamp01(left / SplatFade);
            if (left <= 0f) decal.projector.enabled = false;
        }
    }

    /// <summary>
    /// A splat of blood projected onto the surface at `point` (its `normal`), `size` metres across. A random pick is any
    /// number: GoreLibrary.Splat wraps it over however many splat materials the library holds (RVFX's and Cry of Fear's).
    /// </summary>
    public void Splat(Vector3 point, Vector3 normal, float size, int pick = -1)
    {
        var decal = Take(pick < 0 ? Random.Range(0, int.MaxValue) : pick);
        if (decal == null) return;
        Place(decal, point, normal, size, size, 0f);
    }

    // A pool that spreads under a stump.
    private void Pool(Vector3 point, Vector3 normal)
    {
        var decal = Take(Random.Range(0, int.MaxValue));
        if (decal == null) return;
        Place(decal, point, normal, owner.PoolSize * .15f, owner.PoolSize, PoolGrow);
    }

    private void Place(Decal decal, Vector3 point, Vector3 normal, float from, float to, float grow)
    {
        if (normal.sqrMagnitude < 1e-4f) normal = Vector3.up;
        const float depth = .5f;
        var projector = decal.projector;
        projector.transform.SetPositionAndRotation(point + normal * depth * .4f,
            Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
        projector.size = new Vector3(from, from, depth);
        projector.pivot = new Vector3(0f, 0f, depth * .5f);
        projector.fadeFactor = 1f;
        projector.enabled = true;
        decal.age = 0f; decal.life = owner.SplatSeconds; decal.grow = grow; decal.from = from; decal.to = to;
    }

    // The next decal of the pool: a new one while there are fewer than the most allowed, else the oldest.
    private Decal Take(int pick)
    {
        var library = GoreLibrary.Instance;
        var material = library != null ? library.Splat(pick) : null;
        if (material == null) return null;
        Decal decal;
        if (decals.Count < Mathf.Max(1, owner.MostSplats))
        {
            var host = new GameObject("Gore Splat");
            host.transform.SetParent(owner.transform, false);
            decal = new Decal { projector = host.AddComponent<DecalProjector>() };
            decals.Add(decal);
        }
        else
        {
            decal = decals[nextDecal % decals.Count];
            nextDecal++;
        }
        decal.projector.material = material;
        return decal;
    }

    private void Spray(Vector3 at, Vector3 direction, int count, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            emit.position = at + Random.insideUnitSphere * .05f;
            emit.velocity = (direction + Random.insideUnitSphere * .7f).normalized * Random.Range(.4f, 1f) * speed + Vector3.up * Random.Range(0f, 1.5f);
            Drop(Random.Range(.012f, .03f), Random.Range(.4f, .9f));
        }
    }

    private void Drop(float size, float life)
    {
        var settings = GunFeelSettings.Instance;
        Color color = settings.bloodColor * Random.Range(.7f, 1.1f);
        color.a = 1f;
        emit.startColor = color;
        emit.startSize = size;
        emit.startLifetime = life;
        drops.Emit(emit, 1);
    }

    // The nearest solid scenery along a ray, past the body itself, players and enemies.
    private bool Cast(Vector3 from, Vector3 direction, float distance, Transform ignore, out RaycastHit chosen)
    {
        chosen = default;
        int count = Physics.RaycastNonAlloc(from, direction, hits, distance, SolidMask, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.distance >= best || (ignore != null && hit.collider.transform.IsChildOf(ignore))) continue;
            var rigid = hit.collider.attachedRigidbody;
            if (hit.collider.GetComponentInParent<IDamageReceiver>() != null || (rigid != null && !rigid.isKinematic)) continue;
            best = hit.distance; chosen = hit; found = true;
        }
        return found;
    }
}
