using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gore's blood (ZombieGore): the burst when a part comes off (the impact pack's splashes plus ImpactVfx's spray), a stump
/// that pumps blood a few times, weaker each time, and a torn face of gut on every cut. Drops are one shared particle
/// system that dies on the world.
/// </summary>
internal sealed class GoreFx
{
    // A stump's beat (s) and how much each beat keeps of the last.
    private const float Beat = .27f, BeatDecay = .7f;

    private sealed class Spurt
    {
        public Transform root;
        public float radius, age, seconds, carry;
    }

    private readonly ZombieGore owner;
    private readonly ParticleSystem drops;
    private readonly List<Spurt> spurts = new List<Spurt>();
    private readonly Stack<Spurt> idleSpurts = new Stack<Spurt>();
    private ParticleSystem.EmitParams emit;
    private int nextGut;

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

    /// <summary>A burst head's guts flung along the shot.</summary>
    public void Burst(Vector3 at, Vector3 direction, float speed, int guts)
    {
        for (int i = 0; i < guts; i++)
        {
            var velocity = (direction + SharedRandom.InsideUnitSphere() * .55f).normalized * speed * SharedRandom.Range(.5f, 1.1f) + Vector3.up * SharedRandom.Range(.5f, 2.5f);
            // Which gut mesh: the event's draw inside one, so every PC throws the same; else this PC's own turn.
            owner.Gibs.Gut(at + SharedRandom.InsideUnitSphere() * .06f, velocity, SharedRandom.Range(.09f, .16f),
                SharedRandom.InScope ? SharedRandom.Range(0, 1 << 16) : nextGut++);
        }
    }

    /// <summary>A stump on the body: a torn face of gut at the joint and a few beats of blood.</summary>
    public void Stump(Transform root, BodyPart part)
    {
        if (root == null || root.parent == null) return;
        float radius = BodyParts.CutRadius(part);
        Vector3 outward = (root.position - root.parent.position).normalized;
        Cap(root.parent, root.position, outward, radius);
        // A stump that has stopped pumping is kept for the next (Tick), so a burst makes no new one.
        var spurt = idleSpurts.Count > 0 ? idleSpurts.Pop() : new Spurt();
        spurt.root = root;
        spurt.radius = radius;
        spurt.seconds = owner.SpurtSeconds * (part == BodyPart.Head ? 1f : .7f);
        spurt.age = spurt.carry = 0f;
        spurts.Add(spurt);
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
            Quaternion.LookRotation(outward) * Quaternion.Euler(0f, 0f, SharedRandom.Range(0f, 360f)));
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
        if (SharedRandom.Value() > strength * .8f) return;
        emit.position = at + SharedRandom.InsideUnitSphere() * .03f;
        emit.velocity = velocity * .3f + SharedRandom.InsideUnitSphere() * .4f;
        Drop(SharedRandom.Range(.012f, .024f), SharedRandom.Range(.4f, .8f));
    }

    public void Tick(float dt)
    {
        for (int i = spurts.Count - 1; i >= 0; i--)
        {
            var spurt = spurts[i];
            if (spurt.root == null || spurt.root.parent == null) { Done(i); continue; }
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
                    emit.position = at + SharedRandom.InsideUnitSphere() * spurt.radius * .6f;
                    emit.velocity = (outward + SharedRandom.InsideUnitSphere() * .35f).normalized * SharedRandom.Range(1.4f, 3.4f) * (.5f + .5f * push);
                    Drop(SharedRandom.Range(.012f, .028f), SharedRandom.Range(.45f, .9f));
                }
            }
            if (spurt.age >= spurt.seconds) Done(i);
        }
    }

    // A stump done pumping: out of the list and kept for the next.
    private void Done(int index)
    {
        var spurt = spurts[index];
        spurts.RemoveAt(index);
        spurt.root = null;
        idleSpurts.Push(spurt);
    }

    private void Spray(Vector3 at, Vector3 direction, int count, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            emit.position = at + SharedRandom.InsideUnitSphere() * .05f;
            emit.velocity = (direction + SharedRandom.InsideUnitSphere() * .7f).normalized * SharedRandom.Range(.4f, 1f) * speed + Vector3.up * SharedRandom.Range(0f, 1.5f);
            Drop(SharedRandom.Range(.012f, .03f), SharedRandom.Range(.4f, .9f));
        }
    }

    private void Drop(float size, float life)
    {
        var settings = GunFeelSettings.Instance;
        Color color = settings.bloodColor * SharedRandom.Range(.7f, 1.1f);
        color.a = 1f;
        emit.startColor = color;
        emit.startSize = size;
        emit.startLifetime = life;
        drops.Emit(emit, 1);
    }
}
