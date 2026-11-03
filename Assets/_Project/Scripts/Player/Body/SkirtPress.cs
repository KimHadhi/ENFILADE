using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Presses a ring skirt in toward the body wherever an arm is in its way. The clips were made for bodies without skirts,
/// so an empty hand often hangs where a skirt is, and an arm the cloth collided with would swell it from inside. So once
/// the cloth has put its bones for the frame, the joints beside an arm go in, toward the skirt's middle line, until the
/// skirt's outside at the arm's height lies behind the arm, and the joints below come in as far, as cloth hangs from where
/// it is held. Each bone keeps its length, so the skirt bends in at its joints. Against the body the skirt flattens only
/// as far as the part's squash allows and stops before its outside reaches the skin; past that, the arm sinks into the
/// skirt, and how far is told back (Contact) so the arm can swing out of it from the shoulder (PlayerBodyCloth). How thick
/// the skirt is inside and outside each joint is measured from its mesh as it is made. Only what is drawn changes: each
/// frame starts again from the cloth's own state, so the simulation never feels the arms, and every PC presses its own
/// copy with the arms it shows.
/// </summary>
internal sealed class SkirtPress
{
    /// <summary>How far an arm still sinks into the skirt once it is pressed as far as it goes, and where.</summary>
    internal struct Contact
    {
        /// <summary>How far the skirt's outside still lies beyond the arm's inside (metres).</summary>
        public float Depth;
        /// <summary>The point of the arm's middle line that sinks deepest.</summary>
        public Vector3 Point;
        /// <summary>Straight out from the skirt's middle line there: the way the arm would have to go.</summary>
        public Vector3 Outward;
    }

    // The layers a joint carries lie between these shares of its vertices, counted from the inside out: past the odd
    // stray vertex, yet out to the outside a hand would touch.
    private const float InnerShare = .15f, OuterShare = .85f;
    // A joint carrying fewer vertices than this takes the table's thickness instead.
    private const int LeastLayerPoints = 6;
    // Air kept between a pressed skirt and the skin, so the two don't flicker into each other (metres).
    private const float SkinClearance = .003f;

    private readonly Transform anchor;
    // In the anchor's space: the middle of the waist and the way the skirt hangs from it.
    private readonly Vector3 centre, axis;
    // Each chain of bones from its root down, how far apart neighbouring chains hang at each joint, and how far the
    // skirt's layers reach inside and outside each joint's line (anchor space; NaN where nothing was measured).
    private readonly Transform[][] chains;
    private readonly float[][] spacing, inner, outer;
    // The chain being pressed, joint by joint: where the cloth put it, how the cloth turned it, and where it goes.
    private readonly Vector3[] was, goes;
    private readonly Quaternion[] turns;
    // At the joint being pressed, for each arm bone: how far in it wants the joint, where on it, and which way is out.
    private float[] wants;
    private Vector3[] points, outs;

    /// <param name="skin">The body's vertices, or null where they couldn't be read.</param>
    internal SkirtPress(List<Transform> roots, SkinSample skin)
    {
        anchor = roots[0].parent;
        chains = new Transform[roots.Count][];
        int longest = 0, count = 0;
        Vector3 deep = Vector3.zero;
        for (int k = 0; k < chains.Length; k++)
        {
            var chain = new List<Transform>();
            for (var bone = roots[k]; bone != null; bone = bone.childCount > 0 ? bone.GetChild(0) : null) chain.Add(bone);
            chains[k] = chain.ToArray();
            longest = Mathf.Max(longest, chain.Count);
            centre += Rest(chain[0]);
            foreach (var bone in chain) { deep += Rest(bone); count++; }
        }
        centre /= chains.Length;
        axis = (deep / count - centre).normalized;
        if (axis == Vector3.zero) axis = anchor.InverseTransformDirection(Vector3.down).normalized;

        spacing = new float[chains.Length][];
        inner = new float[chains.Length][];
        outer = new float[chains.Length][];
        for (int k = 0; k < chains.Length; k++)
        {
            spacing[k] = new float[chains[k].Length];
            inner[k] = new float[chains[k].Length];
            outer[k] = new float[chains[k].Length];
            for (int j = 0; j < chains[k].Length; j++)
            {
                float before = Apart(k, j, k - 1), after = Apart(k, j, k + 1);
                spacing[k][j] = before >= 0f && after >= 0f ? (before + after) * .5f : Mathf.Max(0f, Mathf.Max(before, after));
                inner[k][j] = outer[k][j] = float.NaN;
            }
        }
        if (skin != null) Measure(skin);
        was = new Vector3[longest];
        goes = new Vector3[longest];
        turns = new Quaternion[longest];
    }

    /// <summary>Presses the skirt where the arms are. Called once its bones are where the cloth put them this frame.</summary>
    /// <param name="sink">How far into the skirt's outer layers an arm sinks (0-1).</param>
    /// <param name="squash">How flat the skirt can be pressed against the body: 0 stops its inside at the skin, 1 its outside.</param>
    /// <param name="fallback">How far out the skirt's outside lies where its mesh couldn't be measured (metres).</param>
    /// <param name="spread">How far round the skirt past an arm's girth its press reaches, as a share of the distance between neighbouring chains.</param>
    /// <param name="limit">The furthest in a joint may be pressed (metres).</param>
    /// <param name="room">How far a point is outside the body (metres).</param>
    /// <param name="contacts">For each arm bone, the deepest it still sinks into this skirt or any pressed before it.</param>
    internal void Apply(PlayerBodyClothColliders.ArmBone[] arms, float sink, float squash, float fallback, float spread,
        float limit, Func<Vector3, float> room, Contact[] contacts)
    {
        if (arms == null || arms.Length == 0 || limit <= 0f) return;
        if (wants == null || wants.Length != arms.Length)
        {
            wants = new float[arms.Length];
            points = new Vector3[arms.Length];
            outs = new Vector3[arms.Length];
        }
        float scale = Mathf.Abs(anchor.lossyScale.x);
        for (int k = 0; k < chains.Length; k++)
        {
            var chain = chains[k];
            for (int j = 0; j < chain.Length; j++)
            {
                was[j] = goes[j] = chain[j].position;
                turns[j] = chain[j].rotation;
            }
            bool pressed = false;
            float held = 0f;
            // The root holds the chain to the body.
            for (int j = 1; j < chain.Length; j++)
            {
                Vector3 radial = Radial(was[j]);
                float far = radial.magnitude;
                if (far < 1e-4f) continue;
                float push = Mathf.Max(held, Push(k, j, arms, (1f - sink) * scale, fallback * (1f - sink), spread * scale));
                if (push > 0f)
                {
                    // The skirt's inner layers flatten against the body first; it stops before its outside is in the skin.
                    float inside = Inner(k, j) * scale, outside = Outer(k, j, scale, fallback);
                    float stop = inside - squash * (inside + outside) + SkinClearance;
                    push = Mathf.Min(Mathf.Min(push, limit), Mathf.Max(0f, room(was[j]) - stop));
                }
                held = push;
                // What the body and the limit leave of each arm's overlap here, for the arm to swing out of.
                for (int i = 0; i < arms.Length; i++)
                    if (wants[i] - push > contacts[i].Depth)
                        contacts[i] = new Contact { Depth = wants[i] - push, Point = points[i], Outward = outs[i] };
                if (push <= 0f) continue;
                goes[j] = was[j] - radial / far * push;
                pressed = true;
            }
            if (!pressed) continue;
            // Each bone keeps its length, so the skirt bends in at its joints instead of squashing.
            for (int j = 1; j < chain.Length; j++)
            {
                Vector3 aim = goes[j] - goes[j - 1];
                if (aim.sqrMagnitude > 1e-10f) goes[j] = goes[j - 1] + aim.normalized * Vector3.Distance(was[j], was[j - 1]);
            }
            // Down the chain, each bone to where it goes, turned by as much as its aim moved, so the cloth's own twist stays.
            for (int j = 1; j < chain.Length; j++)
            {
                bool last = j + 1 == chain.Length;
                Vector3 before = last ? was[j] - was[j - 1] : was[j + 1] - was[j];
                Vector3 after = last ? goes[j] - goes[j - 1] : goes[j + 1] - goes[j];
                chain[j].SetPositionAndRotation(goes[j], Quaternion.FromToRotation(before, after) * turns[j]);
            }
        }
    }

    // How far in a joint has to go for the skirt's outside to lie behind every arm on its side of the body. Each arm is
    // met at its own height on the chain, between this joint and the one above or below it: the joint takes all of an
    // arm up to halfway there and less beyond, and the skirt's outside is measured there, so a hand above the waist or
    // below the hem leaves it alone. Round the skirt, the press fades out past the arm's girth. The layers come in the
    // anchor's units scaled by `layers`, or as `fallback` metres where unmeasured; `spread` is in the anchor's units.
    private float Push(int chain, int joint, PlayerBodyClothColliders.ArmBone[] arms, float layers, float fallback, float spread)
    {
        int count = chains[chain].Length;
        Vector3 bone = was[joint], up = was[joint - 1];
        // Below the last joint the skirt hangs about one more bone's length.
        Vector3 below = joint + 1 < count ? was[joint + 1] : bone + (bone - up);
        int lower = Mathf.Min(joint + 1, count - 1);
        float most = 0f;
        for (int i = 0; i < arms.Length; i++)
        {
            wants[i] = 0f;
            Vector3 middle = arms[i].Nearest(bone, out float girth);
            bool down = Vector3.Dot(middle - bone, below - bone) >= 0f;
            Vector3 along = (down ? below : up) - bone;
            float t = along.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(middle - bone, along) / along.sqrMagnitude) : 0f;
            float share = Mathf.Clamp01(2f * (1f - t));
            if (share <= 0f) continue;
            Vector3 surface = bone + along * t, radial = Radial(surface), reach = Radial(middle);
            float far = radial.magnitude;
            if (far < 1e-4f) continue;
            Vector3 outward = radial / far;
            // Round the skirt by a quarter or more, the arm is on another side of the body.
            if (Vector3.Dot(reach, outward) <= 0f) continue;
            // The skirt is round, so how deep the arm is in it is how much nearer its middle line the arm is.
            float layer = Mathf.Lerp(Layer(chain, joint, layers, fallback), Layer(chain, down ? lower : joint - 1, layers, fallback), t);
            float overlap = layer + girth - (reach.magnitude - far);
            if (overlap <= 0f) continue;
            // How far round the skirt the arm is, measured on the skirt: near the middle line a small step is a wide angle.
            float round = Vector3.Angle(reach, radial) * Mathf.Deg2Rad * far;
            float wide = spacing[chain][joint] * spread;
            if (round > girth) share *= wide > 0f ? 1f - Mathf.SmoothStep(0f, 1f, (round - girth) / wide) : 0f;
            wants[i] = overlap * share;
            points[i] = middle;
            outs[i] = outward;
            most = Mathf.Max(most, wants[i]);
        }
        return most;
    }

    // How far out the skirt's outer layer lies at a joint, for an arm to press: the root's own is never measured, so it
    // takes the one below it.
    private float Layer(int chain, int joint, float layers, float fallback)
    {
        float measured = outer[chain][Mathf.Max(joint, 1)];
        return float.IsNaN(measured) ? fallback : measured * layers;
    }

    private float Inner(int chain, int joint) => float.IsNaN(inner[chain][joint]) ? 0f : inner[chain][joint];

    private float Outer(int chain, int joint, float scale, float fallback)
        => float.IsNaN(outer[chain][joint]) ? fallback : outer[chain][joint] * scale;

    // Each vertex a chain's bone carries is measured from the chain's line at its height: how much farther from the skirt's
    // middle line it is. The nearer joint of that bone gathers it.
    private void Measure(SkinSample skin)
    {
        var place = new Dictionary<Transform, Vector2Int>();
        for (int k = 0; k < chains.Length; k++)
            for (int j = 0; j < chains[k].Length; j++) place[chains[k][j]] = new Vector2Int(k, j);
        var gathered = new List<float>[chains.Length][];
        for (int k = 0; k < chains.Length; k++) gathered[k] = new List<float>[chains[k].Length];
        foreach (var point in skin.Points)
        {
            var main = skin.Main(point);
            if (main == null || !place.TryGetValue(main, out var at)) continue;
            var chain = chains[at.x];
            if (chain.Length < 2) continue;
            int j = at.y;
            Vector3 start = Rest(chain[j]);
            Vector3 end = j + 1 < chain.Length ? Rest(chain[j + 1]) : start + (start - Rest(chain[j - 1]));
            Vector3 vertex = anchor.InverseTransformPoint(point.Position), line = end - start;
            float t = Mathf.Clamp01(Vector3.Dot(vertex - start, line) / Mathf.Max(line.sqrMagnitude, 1e-10f));
            float offset = Flat(vertex).magnitude - Flat(start + line * t).magnitude;
            int joint = Mathf.Min(t < .5f ? j : j + 1, chain.Length - 1);
            (gathered[at.x][joint] ??= new List<float>()).Add(offset);
        }
        for (int k = 0; k < chains.Length; k++)
            for (int j = 0; j < chains[k].Length; j++)
            {
                var list = gathered[k][j];
                if (list == null || list.Count < LeastLayerPoints) continue;
                list.Sort();
                inner[k][j] = Mathf.Max(0f, -list[(int)((list.Count - 1) * InnerShare)]);
                outer[k][j] = Mathf.Max(0f, list[(int)((list.Count - 1) * OuterShare)]);
            }
        // An outside standing far beyond both neighbours' is something hanging off the skirt there, a ribbon or a few
        // stray vertices on a sparse hem, not the skirt's own: a hand would sink through it. Sleeves tied over several
        // chains stay as they are.
        var measured = new float[chains.Length][];
        for (int k = 0; k < chains.Length; k++) measured[k] = (float[])outer[k].Clone();
        for (int k = 0; k < chains.Length; k++)
            for (int j = 1; j < chains[k].Length; j++)
            {
                float beside = Mathf.Max(Beside(measured, k - 1, j), Beside(measured, k + 1, j));
                if (!float.IsNaN(outer[k][j]) && beside >= 0f)
                    outer[k][j] = Mathf.Min(outer[k][j], beside * 2f + ClothBones.LeastThickness / Mathf.Max(Mathf.Abs(anchor.lossyScale.x), 1e-5f));
            }
    }

    // A neighbouring chain's measured outside at a joint, or -1 where it has none.
    private float Beside(float[][] measured, int chain, int joint)
    {
        chain = (chain + chains.Length) % chains.Length;
        return joint < measured[chain].Length && !float.IsNaN(measured[chain][joint]) ? measured[chain][joint] : -1f;
    }

    // From the skirt's middle line straight out to a point, in the world.
    private Vector3 Radial(Vector3 point) => anchor.TransformVector(Flat(anchor.InverseTransformPoint(point)));

    // From the skirt's middle line straight out to a point, in the anchor's space.
    private Vector3 Flat(Vector3 local)
    {
        Vector3 off = local - centre;
        return off - axis * Vector3.Dot(off, axis);
    }

    // How far a joint hangs from the same joint of another chain round the ring, or -1 when that chain is shorter.
    private float Apart(int chain, int joint, int other)
    {
        other = (other + chains.Length) % chains.Length;
        if (other == chain || joint >= chains[other].Length) return -1f;
        return Vector3.Distance(Rest(chains[chain][joint]), Rest(chains[other][joint]));
    }

    private Vector3 Rest(Transform bone) => anchor.InverseTransformPoint(bone.position);
}
