using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The balls cloth rests on over a soft lump of the body (a bust, a bottom, a belly: Cloth.csv's springs, measured by
/// DressedBody) follow the lump as it bounces. Magica Cloth puts a spring's bones back where they rest before it reads
/// any collider, so the ball stood where the flesh was drawn while the flesh bounced centimetres off it, and the hair
/// laid on Mayo's bust went into it at every bounce (the user, 2026-11-21: "머리카락이 내 몸에 충돌이 안 일어나서 내
/// 몸을 뚫고"); a skirt round a bouncing bottom the same.
/// Each ball moves as the skin it was measured from moves: its middle is skinned by the mean of those points' bone
/// weights (DressedBody.LumpCarriers), read once Magica has written the frame's bones and kept in the space of the body
/// bone the lump rests on. Just before the next simulation it is put where the lump will be: carried on by its last
/// frame's step. A lump springs a few times a second, so one frame's straight step lands within a few percent of the
/// swing, where standing still would leave the ball a whole frame of bounce behind it. A ball made smaller for one cloth
/// part (PlayerBodyClothColliders.Fit) hangs from its lump's ball and moves with it.
/// It also keeps the play's record, Logs/cloth.txt ("천 기록 봐 줘"): each lump as made, how far it moved off where it
/// was drawn, and how much deeper than drawn the hanging cloth's bones got inside it while it moved.
/// </summary>
internal sealed class FleshFollow
{
    internal const string RecordName = "cloth";
    private const string RecordHeading =
        "BODY: a body's lumps as made (radius, the bones they move with) | LUMP: since the last line, how far the lump moved off its drawn place, "
        + "and the deepest a hanging cloth bone got inside it beyond how deep it was drawn (0 = never went in; while the ball stood still, "
        + "the flesh went through what lay on it by up to the distance it moved)";

    // A frame's step is carried on at most this many times when the next frame is longer (a hitch): further out, the
    // step is no longer a straight line along the swing.
    private const float MostAhead = 2f;
    // Seconds between a lump's record lines while it moves, and the least move worth a line (m).
    private const float RecordEvery = 5f, RecordFrom = .001f;

    private sealed class Lump
    {
        public Transform Shape, Owner;
        public Transform[] Bones;
        public float[] Weights;
        public Vector3[] Points;
        public float Radius;
        // In the owner's space: where it was drawn, where the flesh was after the last simulation and the one before.
        public Vector3 Rest, Now, Before;
        public float Step;
        public bool Read;
        // The record, since its last line: the farthest move off its drawn place (m), the deepest a cloth bone got
        // inside it beyond how deep it was drawn (m), which bone, and how deep each watched bone was drawn.
        public float Moved, Deeper;
        public string DeeperBone;
        public float[] Drawn;
    }

    private readonly List<Lump> lumps = new List<Lump>();
    private readonly List<Transform> watched = new List<Transform>();
    private string body = "";
    private float recordAt;

    /// <summary>A new body: the lumps of the one before are gone with it.</summary>
    internal void Clear(string bodyName)
    {
        WriteRecord(true);
        lumps.Clear();
        watched.Clear();
        body = bodyName ?? "";
    }

    /// <summary>
    /// A lump's ball to move: the collider's object (a child of <paramref name="owner"/>, the body bone the lump rests
    /// on), how the lump's middle moves (DressedBody.LumpCarriers) and its size (m), for the record.
    /// </summary>
    internal void Add(Transform shape, Transform owner, Transform[] bones, float[] weights, Vector3[] points, float radius)
    {
        if (shape == null || owner == null || bones == null || weights == null || points == null) return;
        var rest = owner.InverseTransformPoint(shape.position);
        lumps.Add(new Lump
        {
            Shape = shape, Owner = owner, Bones = bones, Weights = weights, Points = points, Radius = radius,
            Rest = rest, Now = rest, Before = rest
        });
        if (!PlayRecord.On) return;
        var carried = new List<string>();
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null) carried.Add($"{bones[i].name} {weights[i]:0.00}");
        PlayRecord.Line(RecordName, RecordHeading,
            $"BODY {body}: lump on {owner.name} r {radius * 1000f:0} mm, moves with {string.Join(", ", carried)}");
    }

    /// <summary>The hanging cloth's bones, watched against the lumps for the record.</summary>
    internal void Watch(IEnumerable<Transform> cloth)
    {
        watched.Clear();
        if (!PlayRecord.On || cloth == null) return;
        watched.AddRange(cloth);
        foreach (var lump in lumps) lump.Drawn = null;
    }

    /// <summary>Just before Magica reads the colliders: each ball where its lump will be this frame.</summary>
    internal void Place()
    {
        foreach (var lump in lumps)
        {
            if (lump.Shape == null || lump.Owner == null) continue;
            Vector3 at = lump.Now;
            if (lump.Read && lump.Step > 0f) at += (lump.Now - lump.Before) * Mathf.Clamp(Time.deltaTime / lump.Step, 0f, MostAhead);
            lump.Shape.localPosition = at;
        }
    }

    /// <summary>Once Magica has written the frame's bones: where each lump's flesh is now.</summary>
    internal void Read()
    {
        if (lumps.Count == 0) return;
        foreach (var lump in lumps)
        {
            if (lump.Shape == null || lump.Owner == null) continue;
            Vector3 world = Vector3.zero;
            float total = 0f;
            for (int i = 0; i < lump.Bones.Length; i++)
            {
                var bone = lump.Bones[i];
                if (bone == null) continue;
                world += bone.TransformPoint(lump.Points[i]) * lump.Weights[i];
                total += lump.Weights[i];
            }
            if (total <= 0f) continue;
            world /= total;
            Vector3 local = lump.Owner.InverseTransformPoint(world);
            lump.Before = lump.Read ? lump.Now : local;
            lump.Now = local;
            lump.Step = Time.deltaTime;
            lump.Read = true;
            if (PlayRecord.On) Note(lump, world);
        }
        if (PlayRecord.On && Time.time >= recordAt) WriteRecord(false);
    }

    private void Note(Lump lump, Vector3 world)
    {
        lump.Moved = Mathf.Max(lump.Moved, Vector3.Distance(lump.Owner.TransformPoint(lump.Rest), world));
        if (watched.Count == 0) return;
        bool first = lump.Drawn == null;
        if (first) lump.Drawn = new float[watched.Count];
        for (int i = 0; i < watched.Count; i++)
        {
            var bone = watched[i];
            if (bone == null) continue;
            float inside = lump.Radius - Vector3.Distance(bone.position, world);
            // A chain drawn into the lump (its ball was made smaller for it) is that deep from the start.
            if (first) lump.Drawn[i] = Mathf.Max(0f, inside);
            float deeper = inside - lump.Drawn[i];
            if (deeper <= lump.Deeper) continue;
            lump.Deeper = deeper;
            lump.DeeperBone = bone.name;
        }
    }

    private void WriteRecord(bool last)
    {
        recordAt = Time.time + RecordEvery;
        if (!PlayRecord.On) return;
        foreach (var lump in lumps)
        {
            if (lump.Owner == null) continue;
            if (lump.Moved >= RecordFrom || (last && lump.Read))
                PlayRecord.Line(RecordName, RecordHeading,
                    $"LUMP {body} on {lump.Owner.name}: moved up to {lump.Moved * 1000f:0} mm | cloth bone deeper than drawn "
                    + (watched.Count == 0 ? "(no hanging cloth)" : $"{lump.Deeper * 1000f:0.0} mm{(lump.Deeper > 0f ? " (" + lump.DeeperBone + ")" : "")}"));
            lump.Moved = lump.Deeper = 0f;
            lump.DeeperBone = null;
        }
    }
}
