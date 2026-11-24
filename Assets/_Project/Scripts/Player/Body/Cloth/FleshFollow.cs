using System.Collections.Generic;
using MagicaCloth2;
using UnityEngine;

/// <summary>
/// The shapes cloth meets on the body that stand for flesh swinging on its springs (Cloth.csv's springs: a bust, a
/// bottom, a belly, a thigh) swing with it. Magica Cloth puts a spring's bones back where they rest before it reads any
/// collider, so a shape stood where its flesh was drawn while the flesh swung centimetres off it: the hair laid on
/// Mayo's bust went into it at every bounce (the user, 2026-11-21: "머리카락이 내 몸에 충돌이 안 일어나서 내 몸을
/// 뚫고"), and Ichigo's thighs, a third of their skin on a spring that swings it up to some 3 cm, went through her skirt
/// round a thigh rod that stayed on the bone ("내 허벅지가 물리 때문에 덜렁거려도 치마는 허벅지 영향을 안 받으니 그대로
/// 뚫리는데?"). The shapes moved here are a bust's ball and the twins PlayerBodyClothColliders makes beside the shapes
/// that stay for the skin that doesn't swing (DressedBody.Swing, Twin).
/// A shape rides its body bone and adds the swing of its flesh: for each spring bone that swings it, its share of the
/// flesh times how far that bone carries the flesh's point off where the body bone carries it. With the springs at rest
/// nothing moves, and a body bone moving (a knee bending) moves nothing here either: only the flesh swinging does. A
/// ball moves its middle; a rod moves each end by the flesh there and turns to run between them. The swing is read once
/// Magica has written the frame's bones, in the space of the body bone the shape hangs from, and just before the next
/// simulation the shape is put where the flesh will be: carried on by its last frame's step. Flesh springs a few times a
/// second, so one frame's straight step lands within a few percent of the swing, where standing still would leave the
/// shape a whole frame behind it. A copy made smaller for one cloth part (PlayerBodyClothColliders.Fit) hangs from its
/// shape and moves with it.
/// It also keeps the play's record, Logs/cloth.txt ("천 기록 봐 줘"): each shape as made, how far it moved off where it
/// was drawn, and how much deeper than drawn a bone of a cloth part that meets it got inside it.
/// </summary>
internal sealed class FleshFollow
{
    internal const string RecordName = "cloth";
    private const string RecordHeading =
        "BODY: the shapes that swing with the flesh, as made (size, the spring bones that swing them and their shares) | SWING: since the last line, "
        + "how far the shape moved off its drawn place, and the deepest a bone of a cloth part that meets it got inside it beyond how deep it was "
        + "drawn (0 = never deeper; while shapes stood still, the flesh went through what lay on it by up to the distance it moved)";

    // A frame's step is carried on at most this many times when the next frame is longer (a hitch): further out, the
    // step is no longer a straight line along the swing.
    private const float MostAhead = 2f;
    // Seconds between a shape's record lines while it moves, and the least move worth a line (m).
    private const float RecordEvery = 5f, RecordFrom = .001f;

    // One end of a shape (a ball has one): the spring bones that swing it, each one's share of its flesh and the point
    // whose swing moves it, in that bone's own space; and in the space of the body bone the shape hangs from, that point
    // as drawn, where the end was drawn, where the flesh had it after the last simulation and after the one before.
    private sealed class End
    {
        public Transform[] Bones;
        public float[] Shares;
        public Vector3[] Points;
        public Vector3 Mark, Rest, Now, Before;
    }

    private sealed class Followed
    {
        public Transform Shape, Owner;
        // A rod's far end; null for a ball.
        public End Start, Finish;
        // A rod's turn and its line from start to end as drawn, in the owner's space.
        public Quaternion RestTurn;
        public Vector3 RestLine;
        public float Step;
        public bool Read;
        // The record, since its last line: the farthest an end moved off its drawn place (m), the deepest a watching
        // bone got inside the shape beyond how deep it was drawn (m) and which bone; the watching bones, each with the
        // collider of this shape it meets (the shape or its smaller copy), and how deep each was drawn.
        public float Moved, Deeper;
        public string DeeperBone;
        public readonly List<(Transform Bone, ColliderComponent Met)> Watchers = new List<(Transform, ColliderComponent)>();
        public float[] Drawn;
    }

    private readonly List<Followed> shapes = new List<Followed>();
    private readonly Dictionary<Transform, Followed> byShape = new Dictionary<Transform, Followed>();
    private string body = "";
    private float recordAt;

    /// <summary>A new body: the shapes of the one before are gone with it.</summary>
    internal void Clear(string bodyName)
    {
        WriteRecord(true);
        shapes.Clear();
        byShape.Clear();
        body = bodyName ?? "";
    }

    /// <summary>Whether this shape swings with its flesh: a copy made smaller of it hangs from it to swing too.</summary>
    internal bool Follows(ColliderComponent shape) => shape != null && byShape.ContainsKey(shape.transform);

    /// <summary>
    /// A shape to move with its flesh: the collider (its object a child of the body bone it rides, as
    /// PlayerBodyClothColliders makes them) and the spring bones that swing its start (a ball's middle) and a rod's far
    /// end, each with its share of the flesh there (DressedBody.Swing, Twin, LumpSwing). Null bones: that end doesn't
    /// swing. A ball moves as the flesh at <paramref name="swungAt"/> swings (world; a twin inside the hips' ball touches
    /// it where the flesh is), or at its middle.
    /// </summary>
    internal void Add(ColliderComponent shape, Transform[] startBones, float[] startShares, Transform[] endBones, float[] endShares,
        Vector3? swungAt = null)
    {
        if (shape == null || byShape.ContainsKey(shape.transform)) return;
        var owner = shape.transform.parent;
        // The shape's object is moved to put its start where the flesh has it, which is its start only with no offset.
        if (owner == null || shape.center != Vector3.zero) return;
        Vector3 start = shape.transform.position;
        var followed = new Followed
        {
            Shape = shape.transform, Owner = owner, RestTurn = shape.transform.localRotation,
            Start = MakeEnd(owner, start, swungAt ?? start, startBones, startShares)
        };
        if (shape is MagicaCapsuleCollider)
        {
            Vector3 end = start + Line(shape);
            followed.Finish = MakeEnd(owner, end, end, endBones, endShares);
            followed.RestLine = followed.Finish.Rest - followed.Start.Rest;
        }
        if (followed.Start.Bones.Length == 0 && (followed.Finish == null || followed.Finish.Bones.Length == 0)) return;
        shapes.Add(followed);
        byShape[shape.transform] = followed;
        if (!PlayRecord.On) return;
        float size = Mathf.Abs(shape.GetScale()) * shape.GetSize().x;
        string swings = followed.Finish == null ? Swings(followed.Start)
            : $"start {Swings(followed.Start)}; end {Swings(followed.Finish)}";
        PlayRecord.Line(RecordName, RecordHeading,
            $"BODY {body}: {(followed.Finish == null ? "ball" : "rod")} on {owner.name} r {size * 1000f:0} mm, swings with {swings}");
    }

    /// <summary>Nothing watched any more: the body's cloth is being built again.</summary>
    internal void Unwatch()
    {
        foreach (var followed in shapes)
        {
            followed.Watchers.Clear();
            followed.Drawn = null;
        }
    }

    /// <summary>
    /// A cloth part's moving bones and the shapes it meets (PlayerBodyCloth.Touching): each of those shapes that swings
    /// with its flesh, or the smaller copy of one, watches the bones for the record.
    /// </summary>
    internal void Watch(IEnumerable<Transform> bones, List<ColliderComponent> touching)
    {
        if (!PlayRecord.On || bones == null || touching == null) return;
        var list = new List<Transform>(bones);
        foreach (var met in touching)
        {
            if (met == null) continue;
            if (!byShape.TryGetValue(met.transform, out var followed)
                && (met.transform.parent == null || !byShape.TryGetValue(met.transform.parent, out followed))) continue;
            foreach (var bone in list)
                if (bone != null) followed.Watchers.Add((bone, met));
            followed.Drawn = null;
        }
    }

    /// <summary>Just before Magica reads the colliders: each shape where its flesh will be this frame.</summary>
    internal void Place()
    {
        foreach (var followed in shapes)
        {
            if (followed.Shape == null || followed.Owner == null) continue;
            float ahead = followed.Read && followed.Step > 0f ? Mathf.Clamp(Time.deltaTime / followed.Step, 0f, MostAhead) : 0f;
            Vector3 start = Ahead(followed.Start, ahead);
            followed.Shape.localPosition = start;
            if (followed.Finish != null)
                followed.Shape.localRotation = Quaternion.FromToRotation(followed.RestLine, Ahead(followed.Finish, ahead) - start) * followed.RestTurn;
        }
    }

    /// <summary>Once Magica has written the frame's bones: where each shape's flesh has it now.</summary>
    internal void Read()
    {
        if (shapes.Count == 0) return;
        foreach (var followed in shapes)
        {
            if (followed.Shape == null || followed.Owner == null) continue;
            ReadEnd(followed.Owner, followed.Start, followed.Read);
            if (followed.Finish != null) ReadEnd(followed.Owner, followed.Finish, followed.Read);
            followed.Step = Time.deltaTime;
            followed.Read = true;
            if (PlayRecord.On) Note(followed);
        }
        if (PlayRecord.On && Time.time >= recordAt) WriteRecord(false);
    }

    private static End MakeEnd(Transform owner, Vector3 world, Vector3 swungAt, Transform[] bones, float[] shares)
    {
        var end = new End { Rest = owner.InverseTransformPoint(world), Mark = owner.InverseTransformPoint(swungAt) };
        end.Now = end.Before = end.Rest;
        var kept = new List<Transform>();
        var keptShares = new List<float>();
        if (bones != null && shares != null)
            for (int i = 0; i < bones.Length && i < shares.Length; i++)
                if (bones[i] != null && shares[i] > 0f)
                {
                    kept.Add(bones[i]);
                    keptShares.Add(shares[i]);
                }
        end.Bones = kept.ToArray();
        end.Shares = keptShares.ToArray();
        end.Points = new Vector3[end.Bones.Length];
        for (int i = 0; i < end.Bones.Length; i++) end.Points[i] = end.Bones[i].InverseTransformPoint(swungAt);
        return end;
    }

    // Where the flesh has an end now: where its body bone carries it, moved by each spring bone's share of how far that
    // bone carries the swung point off where the body bone has it. A spring at rest carries it right there, so it adds
    // nothing.
    private static void ReadEnd(Transform owner, End end, bool read)
    {
        Vector3 drawn = owner.TransformPoint(end.Mark), swing = Vector3.zero;
        for (int i = 0; i < end.Bones.Length; i++)
            if (end.Bones[i] != null) swing += (end.Bones[i].TransformPoint(end.Points[i]) - drawn) * end.Shares[i];
        Vector3 local = end.Rest + owner.InverseTransformVector(swing);
        end.Before = read ? end.Now : local;
        end.Now = local;
    }

    private static Vector3 Ahead(End end, float ahead) => end.Now + (end.Now - end.Before) * ahead;

    // A capsule's line from its start to its end's middle (world), as PlayerBodyClothColliders.Gap reads it.
    private static Vector3 Line(ColliderComponent shape)
    {
        Vector3 size = shape.GetSize() * Mathf.Abs(shape.GetScale());
        return -shape.transform.up * Mathf.Max(0f, size.z - size.x - size.y);
    }

    private static string Swings(End end)
    {
        if (end.Bones.Length == 0) return "nothing";
        var carried = new List<string>();
        for (int i = 0; i < end.Bones.Length; i++)
            if (end.Bones[i] != null) carried.Add($"{end.Bones[i].name} {end.Shares[i]:0.00}");
        return string.Join(", ", carried);
    }

    private void Note(Followed followed)
    {
        float moved = (followed.Start.Now - followed.Start.Rest).magnitude;
        if (followed.Finish != null) moved = Mathf.Max(moved, (followed.Finish.Now - followed.Finish.Rest).magnitude);
        followed.Moved = Mathf.Max(followed.Moved, moved * Mathf.Abs(followed.Owner.lossyScale.x));
        if (followed.Watchers.Count == 0) return;
        bool first = followed.Drawn == null;
        if (first) followed.Drawn = new float[followed.Watchers.Count];
        for (int i = 0; i < followed.Watchers.Count; i++)
        {
            var (bone, met) = followed.Watchers[i];
            if (bone == null || met == null) continue;
            float inside = -PlayerBodyClothColliders.Gap(bone.position, met);
            // A part drawn into the shape (its copy was made smaller for it, or it rests on it) is that deep from the start.
            if (first) followed.Drawn[i] = Mathf.Max(0f, inside);
            if (inside <= 0f) continue;
            float deeper = inside - followed.Drawn[i];
            if (deeper <= followed.Deeper) continue;
            followed.Deeper = deeper;
            followed.DeeperBone = bone.name;
        }
    }

    private void WriteRecord(bool last)
    {
        recordAt = Time.time + RecordEvery;
        if (!PlayRecord.On) return;
        foreach (var followed in shapes)
        {
            if (followed.Owner == null) continue;
            if (followed.Moved >= RecordFrom || (last && followed.Read))
                PlayRecord.Line(RecordName, RecordHeading,
                    $"SWING {body} {(followed.Finish == null ? "ball" : "rod")} on {followed.Owner.name}: moved up to {followed.Moved * 1000f:0} mm | "
                    + (followed.Watchers.Count == 0 ? "no cloth meets it"
                        : $"cloth bone deeper than drawn {followed.Deeper * 1000f:0.0} mm{(followed.Deeper > 0f ? " (" + followed.DeeperBone + ")" : "")}"));
            followed.Moved = followed.Deeper = 0f;
            followed.DeeperBone = null;
        }
    }
}
