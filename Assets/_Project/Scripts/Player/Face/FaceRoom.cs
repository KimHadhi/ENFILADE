using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How much room a face leaves a blink, measured on the face's own mesh (the user, 2026-11-14, of Ichigo's toon cat
/// eyes: "홍조 쪽 쉐이더가 좀 눈도 가리고 그런 것 같은데": no shader, but the blink drawing the eyelids' skin down over
/// the toon eyes her maker's expression puts in front of them). A blink is a shape key made over the face at rest, and
/// two kinds of face leave it nowhere to go:
/// - one that has closed the eyes itself, or half closed them (a smile, a wink, sleepy eyes): the blink on top would
///   carry the lids past shut. Over the vertices the blink moves, eye by eye, the face's own move is laid along the
///   blink's (least squares): 1 is a face that has done all of it. The eye with less room decides, a blink being one
///   key for both: a wink leaves none.
/// - one that puts something by the eyes that whoever made the blink never saw the lids against (drawn toon eyes, kept
///   inside the head until the expression brings them out): what the face moves that the blink doesn't, standing by
///   the eyes and seen from in front of them, is looked at again with the lids shut. Once the lids hide more than a
///   small share of it the blink is wrong there, and it goes altogether: a blink that stops half way is no blink.
/// What was in sight by the eyes as the face was made is left out of that: an iris the lids are there to cover, a lower
/// lid a smile lifts into the upper one's way. The blink was made against those, and a face that moves them keeps
/// blinking as it always has.
/// Nothing is named or listed per character, so a modder's avatar and its expressions are measured the same way.
/// One of these per mesh and blink: what only they decide (the eyes, their front, what was in sight) is worked out once,
/// and a face then costs a walk over the mesh's triangles (under a millisecond for a face of 30,000).
/// Plain arrays and no engine objects, so the offline checks run this same file on the real meshes.
/// </summary>
internal sealed class FaceRoom
{
    /// <summary>Lengths are in the mesh's own units.</summary>
    internal struct Rules
    {
        /// <summary>A vertex counts as moved from this far.</summary>
        public float Moved;
        /// <summary>How far in front of a thing a lid still hides it.</summary>
        public float Reach;
        /// <summary>How far round what the blink moves still counts as by the eyes.</summary>
        public float Margin;
        /// <summary>The share of what the face put by the eyes that the shut lids may hide with the blink left whole, and the share at which none of it is left.</summary>
        public float HiddenFrom, HiddenTo;
        /// <summary>Fewer vertices than this are too few to judge by.</summary>
        public int Least;
    }

    internal struct Result
    {
        /// <summary>What is left of the blink, 0-1.</summary>
        public float Room;
        /// <summary>How much of the blink's own move the face has made, on the eye where it made more.</summary>
        public float Along;
        /// <summary>How many of the vertices the face brought by the eyes are seen from in front, and how many of those the shut lids hide.</summary>
        public int Seen, Hidden;
    }

    // An eye whose share of the whole blink's move is under this is no eye of its own (a few stray vertices).
    private const float EyeShare = .1f;
    // A surface counts as in front of a vertex from this far (a share of Moved), so the one it stands on doesn't; and
    // the blink has a say on a vertex it moves this share of Moved.
    private const float Lift = .4f, BlinkShare = .2f;
    // The vertices asked about are sorted into squares this many across at most, each no smaller than this share of Moved.
    private const int MostCells = 64;
    private const float CellShare = 4f;
    // How far outside a triangle's edges a vertex still counts as behind it, as a share of the triangle.
    private const float EdgeSlack = 1e-4f;

    private readonly Vector3[] rest;
    private readonly int[] corners;
    private readonly Rules rules;
    // The vertices the blink moves, how far, and which eye each is of.
    private readonly int[] blinkAt;
    private readonly Vector3[] blinkBy;
    private readonly bool[] right, moved;
    private readonly float wholeLeft, wholeRight;
    private readonly bool left, other;
    // Where the eyes part, which way they face (front, with across and up the two ways along the face), the box round
    // each eye's lids open and shut as made, and the triangles the blink moves.
    private readonly Vector3 middle, apart, front, across, up;
    private readonly bool fronted;
    private readonly Vector3 lowLeft, highLeft, lowRight, highRight;
    private readonly int[] lids;
    // What was in sight by the eyes as the face was made (asked for by the first face that needs it).
    private bool[] inSight;
    private Vector3[] open, shut;
    // The vertices being asked about: which, where each stands along the face and how far out, and what was found.
    private readonly List<int> asked = new List<int>();
    private float[] askedAcross = Array.Empty<float>(), askedUp = Array.Empty<float>(), askedOut = Array.Empty<float>();
    private bool[] covered = Array.Empty<bool>(), coveredShut = Array.Empty<bool>();
    private int[] cellStart = Array.Empty<int>(), cellItems = Array.Empty<int>();
    private float lowAcross, lowUp, cell, leastOut;
    private int cellsAcross, cellsUp;

    /// <param name="rest">The mesh's vertices as made. Kept, not copied.</param>
    /// <param name="corners">Three vertices per triangle. Kept, not copied.</param>
    /// <param name="blink">How far the blink moves every vertex of the mesh at full.</param>
    public FaceRoom(Vector3[] rest, int[] corners, Vector3[] blink, in Rules rules)
    {
        this.rest = rest;
        this.corners = corners;
        this.rules = rules;
        float least = rules.Moved * rules.Moved * BlinkShare * BlinkShare;
        var at = new List<int>();
        for (int v = 0; v < rest.Length; v++)
            if (blink[v].sqrMagnitude >= least) at.Add(v);
        blinkAt = at.ToArray();
        int count = blinkAt.Length;
        blinkBy = new Vector3[count];
        right = new bool[count];
        moved = new bool[rest.Length];
        lids = Array.Empty<int>();
        float whole = 0f;
        for (int i = 0; i < count; i++)
        {
            blinkBy[i] = blink[blinkAt[i]];
            moved[blinkAt[i]] = true;
            float weight = blinkBy[i].sqrMagnitude;
            middle += rest[blinkAt[i]] * weight;
            whole += weight;
        }
        if (!(whole > 0f)) return;
        // The eyes lie apart along the blink's longest spread; each vertex's say is its move squared.
        middle /= whole;
        apart = Spread();
        lowLeft = lowRight = Far(1f);
        highLeft = highRight = Far(-1f);
        for (int i = 0; i < count; i++)
        {
            Vector3 made = rest[blinkAt[i]];
            right[i] = Vector3.Dot(made - middle, apart) >= 0f;
            if (right[i])
            {
                wholeRight += blinkBy[i].sqrMagnitude;
                Grow(ref lowRight, ref highRight, made);
                Grow(ref lowRight, ref highRight, made + blinkBy[i]);
            }
            else
            {
                wholeLeft += blinkBy[i].sqrMagnitude;
                Grow(ref lowLeft, ref highLeft, made);
                Grow(ref lowLeft, ref highLeft, made + blinkBy[i]);
            }
        }
        left = wholeLeft >= whole * EyeShare;
        other = wholeRight >= whole * EyeShare;
        Vector3 margin = new Vector3(rules.Margin, rules.Margin, rules.Margin);
        lowLeft -= margin;
        highLeft += margin;
        lowRight -= margin;
        highRight += margin;
        // The way the eyes face: the faces of the triangles the blink moves whole, from their winding. Sheets drawn on
        // both sides cancel: with little left, the eyes' front can't be told and only the lids' own move is judged.
        var lidCorners = new List<int>();
        Vector3 faces = Vector3.zero;
        float size = 0f;
        for (int t = 0; t + 2 < corners.Length; t += 3)
        {
            int a = corners[t], b = corners[t + 1], c = corners[t + 2];
            if (!moved[a] && !moved[b] && !moved[c]) continue;
            lidCorners.Add(a);
            lidCorners.Add(b);
            lidCorners.Add(c);
            if (!moved[a] || !moved[b] || !moved[c]) continue;
            Vector3 normal = Vector3.Cross(rest[b] - rest[a], rest[c] - rest[a]);
            faces += normal;
            size += normal.magnitude;
        }
        lids = lidCorners.ToArray();
        if (!(size > 0f) || faces.magnitude < size * .2f) return;
        front = faces / faces.magnitude;
        // Two ways along the face, square to its front: across the eyes, and the third.
        across = apart - front * Vector3.Dot(apart, front);
        if (across.magnitude < .1f)
        {
            Vector3 any = Mathf.Abs(front.x) < .9f ? new Vector3(1f, 0f, 0f) : new Vector3(0f, 1f, 0f);
            across = any - front * Vector3.Dot(any, front);
        }
        across /= across.magnitude;
        up = Vector3.Cross(front, across);
        fronted = true;
    }

    /// <param name="face">How far the face moves every vertex of the mesh.</param>
    public Result Measure(Vector3[] face)
    {
        var result = new Result { Room = 1f };
        if (!left && !other) return result;
        float doneLeft = 0f, doneRight = 0f;
        for (int i = 0; i < blinkAt.Length; i++)
        {
            float done = Vector3.Dot(blinkBy[i], face[blinkAt[i]]);
            if (right[i]) doneRight += done;
            else doneLeft += done;
        }
        float along = float.NegativeInfinity;
        if (left) along = Mathf.Max(along, doneLeft / wholeLeft);
        if (other) along = Mathf.Max(along, doneRight / wholeRight);
        result.Along = along;
        result.Room = Mathf.Clamp01(1f - along);
        if (result.Room <= 0f || !fronted || !(rules.HiddenTo > 0f)) return result;

        // What the face put by the eyes that the blink has no say on and its maker never saw there.
        float least = rules.Moved * rules.Moved;
        asked.Clear();
        for (int v = 0; v < rest.Length; v++)
        {
            if (moved[v] || face[v].sqrMagnitude < least || !ByEyes(rest[v] + face[v])) continue;
            asked.Add(v);
        }
        if (asked.Count < rules.Least) return result;
        if (inSight == null) Look();
        int kept = 0;
        for (int i = 0; i < asked.Count; i++)
            if (!inSight[asked[i]]) asked[kept++] = asked[i];
        asked.RemoveRange(kept, asked.Count - kept);
        result.Seen = asked.Count;
        if (asked.Count < rules.Least) return result;

        // Behind the shut lids: only the triangles the blink moves can newly hide anything.
        for (int v = 0; v < rest.Length; v++) open[v] = shut[v] = rest[v] + face[v];
        for (int i = 0; i < blinkAt.Length; i++) shut[blinkAt[i]] += blinkBy[i];
        Sort(open);
        Cover(shut, lids, coveredShut);
        int behind = 0;
        for (int i = 0; i < asked.Count; i++)
            if (coveredShut[i]) behind++;
        if (behind < rules.Least) return result;
        // Of those, the ones nobody saw anyway (inside the head, the back of their own shape) are no loss.
        Cover(open, corners, covered);
        result.Seen = 0;
        for (int i = 0; i < asked.Count; i++)
        {
            if (covered[i]) continue;
            result.Seen++;
            if (coveredShut[i]) result.Hidden++;
        }
        if (result.Seen < rules.Least || result.Hidden < rules.Least) return result;
        float share = result.Hidden / (float)result.Seen;
        float band = Mathf.Max(1e-4f, rules.HiddenTo - rules.HiddenFrom);
        result.Room *= 1f - Mathf.Clamp01((share - rules.HiddenFrom) / band);
        return result;
    }

    // What stands by the eyes and is seen from in front of them, as the face was made.
    private void Look()
    {
        inSight = new bool[rest.Length];
        open = new Vector3[rest.Length];
        shut = new Vector3[rest.Length];
        var mine = new List<int>(asked);
        asked.Clear();
        for (int v = 0; v < rest.Length; v++)
            if (!moved[v] && ByEyes(rest[v])) asked.Add(v);
        if (asked.Count > 0)
        {
            Sort(rest);
            Cover(rest, corners, covered);
            for (int i = 0; i < asked.Count; i++) inSight[asked[i]] = !covered[i];
        }
        asked.Clear();
        asked.AddRange(mine);
    }

    // The asked vertices, standing where the points have them, sorted into squares along the face.
    private void Sort(Vector3[] points)
    {
        int count = asked.Count;
        if (askedAcross.Length < count)
        {
            askedAcross = new float[count];
            askedUp = new float[count];
            askedOut = new float[count];
            covered = new bool[count];
            coveredShut = new bool[count];
            cellItems = new int[count];
        }
        float highAcross = float.NegativeInfinity, highUp = float.NegativeInfinity;
        lowAcross = lowUp = leastOut = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Vector3 at = points[asked[i]];
            float a = Vector3.Dot(at, across), u = Vector3.Dot(at, up), o = Vector3.Dot(at, front);
            askedAcross[i] = a;
            askedUp[i] = u;
            askedOut[i] = o;
            if (a < lowAcross) lowAcross = a;
            if (a > highAcross) highAcross = a;
            if (u < lowUp) lowUp = u;
            if (u > highUp) highUp = u;
            if (o < leastOut) leastOut = o;
        }
        cell = Mathf.Max(rules.Moved * CellShare, Mathf.Max(highAcross - lowAcross, highUp - lowUp) / MostCells);
        cellsAcross = Mathf.Max(1, Mathf.Min(MostCells, Mathf.FloorToInt((highAcross - lowAcross) / cell) + 1));
        cellsUp = Mathf.Max(1, Mathf.Min(MostCells, Mathf.FloorToInt((highUp - lowUp) / cell) + 1));
        int cells = cellsAcross * cellsUp;
        if (cellStart.Length < cells + 1) cellStart = new int[cells + 1];
        Array.Clear(cellStart, 0, cells + 1);
        for (int i = 0; i < count; i++) cellStart[CellOf(i)]++;
        // Each square's end first; filling from the ends back then leaves each square's start, and the next one's start
        // is where it ends.
        int sum = 0;
        for (int c = 0; c < cells; c++)
        {
            sum += cellStart[c];
            cellStart[c] = sum;
        }
        cellStart[cells] = count;
        for (int i = count - 1; i >= 0; i--) cellItems[--cellStart[CellOf(i)]] = i;
    }

    private int CellOf(int i)
    {
        int a = Mathf.Min(cellsAcross - 1, Mathf.Max(0, Mathf.FloorToInt((askedAcross[i] - lowAcross) / cell)));
        int u = Mathf.Min(cellsUp - 1, Mathf.Max(0, Mathf.FloorToInt((askedUp[i] - lowUp) / cell)));
        return u * cellsAcross + a;
    }

    // For each asked vertex (sorted where Sort had them), whether one of these triangles, standing where the points
    // have them, lies in front of it within reach: the vertex seen from straight in front is behind the triangle.
    private void Cover(Vector3[] points, int[] triangles, bool[] found)
    {
        int count = asked.Count;
        Array.Clear(found, 0, count);
        float lift = rules.Moved * Lift, reach = rules.Reach;
        float highAcross = lowAcross + cellsAcross * cell, highUp = lowUp + cellsUp * cell;
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            Vector3 a = points[triangles[t]], b = points[triangles[t + 1]], c = points[triangles[t + 2]];
            float outA = Vector3.Dot(a, front), outB = Vector3.Dot(b, front), outC = Vector3.Dot(c, front);
            if (outA <= leastOut && outB <= leastOut && outC <= leastOut) continue;
            float acrossA = Vector3.Dot(a, across), acrossB = Vector3.Dot(b, across), acrossC = Vector3.Dot(c, across);
            float least = Mathf.Min(acrossA, Mathf.Min(acrossB, acrossC)), most = Mathf.Max(acrossA, Mathf.Max(acrossB, acrossC));
            if (most < lowAcross || least > highAcross) continue;
            float upA = Vector3.Dot(a, up), upB = Vector3.Dot(b, up), upC = Vector3.Dot(c, up);
            float leastUp = Mathf.Min(upA, Mathf.Min(upB, upC)), mostUp = Mathf.Max(upA, Mathf.Max(upB, upC));
            if (mostUp < lowUp || leastUp > highUp) continue;
            // Twice the triangle's size as seen from in front; edge on, it hides nothing.
            float size = (acrossB - acrossA) * (upC - upA) - (upB - upA) * (acrossC - acrossA);
            if (size > -1e-12f && size < 1e-12f) continue;
            float inverse = 1f / size;
            int fromA = Mathf.Max(0, Mathf.FloorToInt((least - lowAcross) / cell)), toA = Mathf.Min(cellsAcross - 1, Mathf.FloorToInt((most - lowAcross) / cell));
            int fromU = Mathf.Max(0, Mathf.FloorToInt((leastUp - lowUp) / cell)), toU = Mathf.Min(cellsUp - 1, Mathf.FloorToInt((mostUp - lowUp) / cell));
            for (int u = fromU; u <= toU; u++)
            {
                for (int x = fromA; x <= toA; x++)
                {
                    int at = u * cellsAcross + x;
                    for (int k = cellStart[at]; k < cellStart[at + 1]; k++)
                    {
                        int i = cellItems[k];
                        if (found[i]) continue;
                        float pa = askedAcross[i], pu = askedUp[i];
                        float shareA = ((acrossB - pa) * (upC - pu) - (upB - pu) * (acrossC - pa)) * inverse;
                        if (shareA < -EdgeSlack) continue;
                        float shareB = ((acrossC - pa) * (upA - pu) - (upC - pu) * (acrossA - pa)) * inverse;
                        if (shareB < -EdgeSlack) continue;
                        float shareC = 1f - shareA - shareB;
                        if (shareC < -EdgeSlack) continue;
                        float ahead = shareA * outA + shareB * outB + shareC * outC - askedOut[i];
                        if (ahead > lift && ahead <= reach) found[i] = true;
                    }
                }
            }
        }
    }

    private bool ByEyes(Vector3 at) => (left && Inside(at, lowLeft, highLeft)) || (other && Inside(at, lowRight, highRight));

    // The direction the blink's vertices spread furthest along (the main axis of their places, by power iteration).
    private Vector3 Spread()
    {
        float xx = 0f, xy = 0f, xz = 0f, yy = 0f, yz = 0f, zz = 0f;
        for (int i = 0; i < blinkAt.Length; i++)
        {
            Vector3 d = rest[blinkAt[i]] - middle;
            float weight = blinkBy[i].sqrMagnitude;
            xx += weight * d.x * d.x;
            xy += weight * d.x * d.y;
            xz += weight * d.x * d.z;
            yy += weight * d.y * d.y;
            yz += weight * d.y * d.z;
            zz += weight * d.z * d.z;
        }
        Vector3 axis = xx >= yy && xx >= zz ? new Vector3(1f, 0f, 0f) : yy >= zz ? new Vector3(0f, 1f, 0f) : new Vector3(0f, 0f, 1f);
        for (int turn = 0; turn < 24; turn++)
        {
            var next = new Vector3(xx * axis.x + xy * axis.y + xz * axis.z, xy * axis.x + yy * axis.y + yz * axis.z, xz * axis.x + yz * axis.y + zz * axis.z);
            float length = next.magnitude;
            if (!(length > 0f)) break;
            axis = next / length;
        }
        return axis;
    }

    private static Vector3 Far(float sign) => new Vector3(sign * float.MaxValue, sign * float.MaxValue, sign * float.MaxValue);

    private static void Grow(ref Vector3 low, ref Vector3 high, Vector3 point)
    {
        if (point.x < low.x) low.x = point.x;
        if (point.y < low.y) low.y = point.y;
        if (point.z < low.z) low.z = point.z;
        if (point.x > high.x) high.x = point.x;
        if (point.y > high.y) high.y = point.y;
        if (point.z > high.z) high.z = point.z;
    }

    private static bool Inside(Vector3 point, Vector3 low, Vector3 high) =>
        point.x >= low.x && point.x <= high.x && point.y >= low.y && point.y <= high.y && point.z >= low.z && point.z <= high.z;
}
