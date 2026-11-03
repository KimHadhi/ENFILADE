using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>Where a bullet goes on from a hit (BulletPenetration.Onward).</summary>
public readonly struct BulletOnward
{
    /// <summary>Where it sets off again, and which way.</summary>
    public readonly Vector3 From, Direction;
    /// <summary>Penetration it has left (mm).</summary>
    public readonly float Left;
    /// <summary>It glanced off instead of going through.</summary>
    public readonly bool Ricochet;
    /// <summary>The far side it came out of, for an exit hole; false when it glanced off or the far side wasn't found.</summary>
    public readonly bool HasExit;
    public readonly RaycastHit Exit;

    public BulletOnward(Vector3 from, Vector3 direction, float left, bool ricochet, bool hasExit, RaycastHit exit)
    {
        From = from; Direction = direction; Left = left; Ricochet = ricochet; HasExit = hasExit; Exit = exit;
    }
}

/// <summary>
/// What a bullet does after it hits something, ported from ArcCW's DoPenetration and GetRicochetChance (the weapon base
/// Horde's guns use). A glancing hit may ricochet: the flatter the angle and the more penetration the bullet has left, the
/// likelier, up to 45 %; it bounces off like light off a mirror, a few degrees astray, keeping a random share of its
/// penetration. Otherwise it pushes on through what it hit 4 units (10 cm) at a time, each step costing 4 × the material's
/// toughness (Surfaces.csv's penetration column) of its penetration (mm), 5 % more than the step before, and drifting off
/// line a little. What it has left when it comes out is the share of the gun's damage it still carries. Bodies count as
/// flesh, and anything that isn't level geometry (bodies, props, things that take damage) is half again as tough, as
/// ArcCW treats entities. Each shot seeds its own randomness, so every PC draws the same path for it.
/// </summary>
public static class BulletPenetration
{
    // ArcCW counts in GMod units, an inch each (ArcCW.HUToM), and goes through solid 4 of them at a time.
    private const float Unit = .0254f, StepUnits = 4f, Step = StepUnits * Unit;
    // ArcCW.PenTable: a material not in it counts 1, and bodies are MAT_FLESH.
    private const float Unlisted = 1f;
    private const string Table = "Surfaces", Column = "penetration", FleshRow = "Flesh";
    // How far a single ricochet can turn astray (ArcCW's AngleRand() * 15 / 360) at the flattest angle.
    private const float StrayPitch = 90f * 15f / 360f, StrayYaw = 180f * 15f / 360f;
    // Unity's queries miss a shape they start inside, so the bullet sets off just clear of the surface it leaves.
    private const float Clear = .001f;
    // ArcCW's loop has no other end; this only stops a bullet that would wander on for ever.
    private const int MostSteps = 256;

    private static readonly Dictionary<string, float> toughness = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        toughness.Clear();
        GameTables.Reloaded -= toughness.Clear;
        GameTables.Reloaded += toughness.Clear;
    }

    /// <summary>The randomness a shot from here, this way, draws on: the same on every PC that knows the shot.</summary>
    public static System.Random RandomFor(Vector3 origin, Vector3 direction)
    {
        unchecked
        {
            int seed = 17;
            seed = seed * 31 + origin.x.GetHashCode();
            seed = seed * 31 + origin.y.GetHashCode();
            seed = seed * 31 + origin.z.GetHashCode();
            seed = seed * 31 + direction.x.GetHashCode();
            seed = seed * 31 + direction.y.GetHashCode();
            seed = seed * 31 + direction.z.GetHashCode();
            return new System.Random(seed);
        }
    }

    /// <summary>
    /// Whether the bullet that made `hit`, fired from `start` along `direction` with `left` penetration, goes on, and
    /// from where. False: it stopped there.
    /// </summary>
    public static bool Onward(in RaycastHit hit, Vector3 start, Vector3 direction, float left, System.Random random, int layers,
        out BulletOnward onward)
    {
        onward = default;
        if (left <= 0f || hit.collider == null) return false;
        Vector3 dir = direction.normalized;
        float tough = Toughness(hit.collider);
        float mult = tough * (IsLevel(hit.collider) ? 1f : 1.5f);
        mult *= Range(random, .9f, 1.1f) * Range(random, .9f, 1.1f);

        // GetRicochetChance: 0 head on, min(penetration × toughness × 2, 45) along the surface, against a roll of 0–100.
        float degree = Vector3.Dot(hit.normal, (start - hit.point).normalized);
        float chance = Mathf.Clamp(Mathf.LerpUnclamped(Mathf.Min(left * tough * 2f, 45f), 0f, degree), 0f, 100f);
        if (chance > random.Next(0, 101))
        {
            if (degree == 0f || degree == 1f) return false;
            Vector3 bounce = 2f * degree * hit.normal + dir;
            float stray = 1f - degree;
            Vector3 turned = Quaternion.LookRotation(bounce).eulerAngles;
            turned.x += Range(random, -StrayPitch, StrayPitch) * stray;
            turned.y += Range(random, -StrayYaw, StrayYaw) * stray;
            Range(random, -180f, 180f); // ArcCW's roll, which leaves the line alone
            bounce = Quaternion.Euler(turned.x, turned.y, 0f) * Vector3.forward;
            left *= Range(random, .25f, .95f);
            if (left <= 0f) return false;
            onward = new BulletOnward(hit.point + hit.normal * Clear, bounce, left, true, false, default);
            return true;
        }

        // Through: ArcCW tests each step by tracing 4 units on from the place before the last step, so it always pays at
        // least twice and comes out 4–8 units past the far side. The far side is measured once along the line it went in.
        float thickness = Thickness(hit, dir, Reach(left, mult), layers, out bool hasExit, out RaycastHit exit);
        Vector3 entry = hit.point, end = entry, tested = entry, line = dir;
        float factor = 1f;
        for (int i = 0; i < MostSteps && left > 0f && Inside(tested, entry, dir, thickness); i++)
        {
            left -= StepUnits * mult * factor;
            factor *= 1.05f;
            tested = end;
            end += line * Step;
            line += new Vector3(Range(random, -1f, 1f), Range(random, -1f, 1f), Range(random, -1f, 1f)) * (.025f * mult);
        }
        if (left <= 0f || line.sqrMagnitude < 1e-8f) return false;
        // ArcCW's next bullet starts at the last step's end; one here starting inside whatever stands right behind the
        // far side would pass through it unseen, so it starts at the far side itself.
        Vector3 from = hasExit ? exit.point + dir * Clear : end;
        onward = new BulletOnward(from, line.normalized, left, false, hasExit, exit);
        return true;
    }

    // The point the last step began at is still in the solid: the hit surface itself, or short of the far side.
    private static bool Inside(Vector3 tested, Vector3 entry, Vector3 dir, float thickness)
    {
        float along = Vector3.Dot(tested - entry, dir);
        return along <= Clear || along < thickness;
    }

    // As far as `left` could carry the bullet through this material, with the step past it.
    private static float Reach(float left, float mult)
    {
        float cost = 0f, factor = 1f;
        int steps = 0;
        while (cost < left && steps < MostSteps) { cost += StepUnits * mult * factor; factor *= 1.05f; steps++; }
        return (steps + 1) * Step;
    }

    /// <summary>
    /// How deep the solid goes along `dir` from the hit: through the shape hit and on through any more of the same thing
    /// that starts within a step of where it ends (ArcCW counts the world as one thing). Infinity when the far side lies
    /// beyond `reach`, or isn't there (an open surface).
    /// </summary>
    private static float Thickness(in RaycastHit hit, Vector3 dir, float reach, int layers, out bool hasExit, out RaycastHit exit)
    {
        hasExit = false;
        exit = default;
        Collider shape = hit.collider;
        Vector3 from = hit.point;
        object thing = ThingOf(shape);
        float depth = float.PositiveInfinity;
        for (int guard = 0; guard < 8; guard++)
        {
            float rest = reach - Vector3.Dot(from - hit.point, dir);
            // Back along the line from beyond: the first face met is the far side.
            if (rest <= 0f || !shape.Raycast(new Ray(from + dir * rest, -dir), out RaycastHit back, rest)) return float.PositiveInfinity;
            hasExit = true;
            exit = back;
            depth = Vector3.Dot(back.point - hit.point, dir);
            if (!Physics.Raycast(back.point + dir * Clear, dir, out RaycastHit next, Step, layers, QueryTriggerInteraction.Ignore)
                || !Equals(ThingOf(next.collider), thing)) break;
            shape = next.collider;
            from = next.point;
        }
        return depth;
    }

    // Level geometry is one thing, as the world is to ArcCW; anything else is the thing that takes the damage, or the body.
    private static object ThingOf(Collider shape)
    {
        if (IsLevel(shape)) return null;
        var receiver = shape.GetComponentInParent<IDamageReceiver>();
        return receiver != null ? receiver : (object)shape.attachedRigidbody;
    }

    private static bool IsLevel(Collider shape) => shape.attachedRigidbody == null && shape.GetComponentInParent<IDamageReceiver>() == null;

    private static bool IsBody(Collider shape)
    {
        var receiver = shape.GetComponentInParent<IDamageReceiver>();
        return receiver is EnemyHealth || receiver is PlayerDamageReceiver || receiver is RagdollCorpse;
    }

    /// <summary>The material's toughness (ArcCW.PenTable): Surfaces.csv's penetration column, from the surface or its parents.</summary>
    public static float Toughness(Collider shape)
    {
        string surface = IsBody(shape) ? FleshRow : Surfaces.Of(shape);
        if (toughness.TryGetValue(surface, out float known)) return known;
        float value = Unlisted;
        foreach (string id in Surfaces.Fallbacks(surface))
            if (GameTables.TryGetRow(Table, id, out var row) && row.TryGetValue(Column, out string cell)
                && float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                value = Mathf.Max(.001f, parsed);
                break;
            }
        toughness[surface] = value;
        return value;
    }

    private static float Range(System.Random random, float from, float to) => from + (to - from) * (float)random.NextDouble();
}
