using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The path one bullet (or one pellet) took on the shooter's PC, sent to the others so they draw that path and judge
/// nothing again (Docs/Records/SyncAudit.md, item 1, rule 1). Each PC used to trace the shot again against its own
/// copies of the zombies and corpses, which stand a little elsewhere there, so the holes, sparks, tracer's end and blood
/// were not where the shooter saw them. Now each place it met comes as a BodyPoint (a point on a body's bone, or in the
/// world), with each stretch after the first (through a wall or a body, or off a surface) and the far side it came out
/// of. Every PC puts those points on its own copies and finds what is there with a ray a few centimetres long (Place),
/// for the effects that ask what was hit (its surface, the body a mark rides). It also carries the slot and gun the
/// shooter held, so a copy whose gun has not come out yet still fires this one.
/// </summary>
public struct ShotPath : INetworkSerializable
{
    // How far either side of a sent point a ray looks for what is there (m): a body's pose differs a little per PC.
    private const float Reach = .3f;
    private const int MostLegs = 16;

    [Flags]
    private enum Has : byte { None = 0, Hit = 1, Headshot = 2, Ricochet = 4, Exit = 8 }

    // One straight stretch: where it set off and which way (the first starts at Origin along Direction), what it met,
    // or how far it went when it met nothing.
    private struct Stretch
    {
        public Has Marks;
        public Vector3 From, Way;
        public float Length;
        public byte Share;
        public BodyPoint Hit, Exit;
    }

    public Vector3 Origin, Direction;
    public byte Pellet;
    public uint Seed;
    /// <summary>The shooter's slot and the gun in it (PlayerNetworkCombat.GunId), for a copy that holds another yet.</summary>
    public sbyte Slot;
    public ushort Gun;
    private Stretch first;
    private Stretch[] legs;

    /// <summary>The path this PC's own shot took (its trace and the stretches after the first).</summary>
    public static ShotPath Of(in WeaponShot shot, sbyte slot, ushort gun)
    {
        var path = new ShotPath
        {
            Origin = shot.Origin, Direction = shot.Direction, Pellet = (byte)Mathf.Clamp(shot.Pellet, 0, 255), Seed = shot.Seed,
            Slot = slot, Gun = gun,
        };
        path.first = StretchOf(shot.Trace, shot.Origin);
        if (shot.Later != null && shot.Later.Length > 0)
        {
            int count = Mathf.Min(shot.Later.Length, MostLegs);
            path.legs = new Stretch[count];
            for (int i = 0; i < count; i++)
            {
                var leg = shot.Later[i];
                var stretch = StretchOf(leg.Trace, leg.Onward.From);
                stretch.From = leg.Onward.From;
                stretch.Way = leg.Onward.Direction;
                stretch.Share = (byte)Mathf.Clamp(Mathf.RoundToInt(leg.Share * 255f), 0, 255);
                if (leg.Onward.Ricochet) stretch.Marks |= Has.Ricochet;
                if (leg.Onward.HasExit)
                {
                    stretch.Marks |= Has.Exit;
                    stretch.Exit = BodyPoint.Of(leg.Onward.Exit.collider, leg.Onward.Exit.point, leg.Onward.Exit.normal);
                }
                path.legs[i] = stretch;
            }
        }
        return path;
    }

    private static Stretch StretchOf(in AttackTraceResult trace, Vector3 from)
    {
        var stretch = new Stretch { Length = Vector3.Distance(from, trace.End) };
        if (trace.HasHit)
        {
            stretch.Marks |= Has.Hit;
            if (trace.IsHeadshot) stretch.Marks |= Has.Headshot;
            stretch.Hit = BodyPoint.Of(trace.Hit.collider, trace.Hit.point, trace.Hit.normal);
        }
        return stretch;
    }

    /// <summary>
    /// The path put on this PC's copies: the first stretch's trace, and the later ones. Nothing is hurt. Corpse: a body this
    /// path met that lies as a corpse here, for its blood (the shooter's PC bleeds it as its bullet strikes it).
    /// </summary>
    public AttackTraceResult Place(GameObject owner, int layers, out BulletLeg[] later)
    {
        later = null;
        var trace = Place(first, Origin, Direction, layers, owner);
        if (legs == null || legs.Length == 0) return trace;
        later = new BulletLeg[legs.Length];
        Collider through = trace.Hit.collider;
        for (int i = 0; i < legs.Length; i++)
        {
            var leg = legs[i];
            Vector3 way = leg.Way.sqrMagnitude > 1e-6f ? leg.Way.normalized : Direction;
            RaycastHit exit = default;
            bool hasExit = (leg.Marks & Has.Exit) != 0;
            // The far side was found looking back along the bullet: so is it here.
            if (hasExit) exit = Recover(leg.Exit, leg.From, -way, layers, owner);
            var onward = new BulletOnward(leg.From, way, 0f, (leg.Marks & Has.Ricochet) != 0, hasExit, exit);
            var stretch = Place(leg, leg.From, way, layers, owner);
            later[i] = new BulletLeg(through, onward, leg.Share / 255f, stretch);
            through = stretch.Hit.collider;
        }
        return trace;
    }

    /// <summary>Calls back for each place this path met a body lying as a corpse on this PC, with where and which way.</summary>
    public void ForEachCorpse(Action<ZombieCorpse, Vector3, Vector3> met)
    {
        Corpse(first, Direction, met);
        if (legs == null) return;
        foreach (var leg in legs) Corpse(leg, leg.Way, met);
    }

    private static void Corpse(in Stretch stretch, Vector3 way, Action<ZombieCorpse, Vector3, Vector3> met)
    {
        if ((stretch.Marks & Has.Hit) == 0 || !stretch.Hit.IsOnBody) return;
        if (!stretch.Hit.Resolve(out var point, out _, out var on) || on == null) return;
        var corpse = on.GetComponentInParent<ZombieCorpse>();
        if (corpse != null) met(corpse, point, way);
    }

    private static AttackTraceResult Place(in Stretch stretch, Vector3 from, Vector3 way, int layers, GameObject owner)
    {
        Vector3 end = from + way * stretch.Length;
        if ((stretch.Marks & Has.Hit) == 0) return new AttackTraceResult(false, default, end, DamageResult.Ignored);
        var hit = Recover(stretch.Hit, end, way, layers, owner);
        hit.distance = Vector3.Distance(from, hit.point);
        return new AttackTraceResult(true, hit, hit.point, DamageResult.Ignored, (stretch.Marks & Has.Headshot) != 0);
    }

    private static readonly RaycastHit[] near = new RaycastHit[16];

    // What is at a sent point here: the nearest thing a bullet would meet (AttackTrace.LetsThrough) on a short ray through
    // it, along the way the bullet came. The point and facing stay the sent ones put on this PC's copy; the thing found is
    // for what the effects ask of it (its surface, the body a mark rides). Nothing found: no thing, the point stays.
    private static RaycastHit Recover(in BodyPoint at, Vector3 fallback, Vector3 along, int layers, GameObject owner)
    {
        if (!at.Resolve(out var point, out var normal, out _)) { point = fallback; normal = -along; }
        var found = default(RaycastHit);
        float best = float.PositiveInfinity;
        int count = Physics.RaycastNonAlloc(point - along * Reach, along, near, Reach * 2f, layers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            if (AttackTrace.LetsThrough(near[i].collider, owner)) continue;
            float off = (near[i].point - point).sqrMagnitude;
            if (off < best) { best = off; found = near[i]; }
        }
        found.point = point;
        found.normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : -along;
        return found;
    }

    // ---- On the wire: about 25 bytes for a bullet that stops in what it hit -----------------------------------------

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Origin);
        serializer.SerializeValue(ref Direction);
        serializer.SerializeValue(ref Pellet);
        serializer.SerializeValue(ref Seed);
        serializer.SerializeValue(ref Slot);
        serializer.SerializeValue(ref Gun);
        SerializeStretch(serializer, ref first, false);
        byte count = (byte)(legs != null ? legs.Length : 0);
        serializer.SerializeValue(ref count);
        if (serializer.IsReader) legs = count > 0 ? new Stretch[Mathf.Min(count, MostLegs)] : null;
        for (int i = 0; i < count && legs != null && i < legs.Length; i++) SerializeStretch(serializer, ref legs[i], true);
    }

    private static void SerializeStretch<T>(BufferSerializer<T> serializer, ref Stretch stretch, bool leg) where T : IReaderWriter
    {
        byte marks = (byte)stretch.Marks;
        serializer.SerializeValue(ref marks);
        stretch.Marks = (Has)marks;
        ushort length = 0;
        if (serializer.IsWriter) length = Mathf.FloatToHalf(stretch.Length);
        serializer.SerializeValue(ref length);
        if (serializer.IsReader) stretch.Length = Mathf.HalfToFloat(length);
        if (leg)
        {
            serializer.SerializeValue(ref stretch.From);
            serializer.SerializeValue(ref stretch.Way);
            serializer.SerializeValue(ref stretch.Share);
            if ((stretch.Marks & Has.Exit) != 0) stretch.Exit.NetworkSerialize(serializer);
        }
        if ((stretch.Marks & Has.Hit) != 0) stretch.Hit.NetworkSerialize(serializer);
    }
}
