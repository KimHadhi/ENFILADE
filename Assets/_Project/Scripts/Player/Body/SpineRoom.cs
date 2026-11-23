using System;
using UnityEngine;

/// <summary>
/// How far forward the spine may bend for the face, the eyes and the shoulders to stay off the player's own legs and the
/// face out of walls: one limit for every way the spine is bent down, the look's own bend with nothing in hand (SpineLook)
/// and FPS Animation Framework's Look with a gun (SpineRoomLayer), worked out before the bend goes on and never put right
/// after it (the user, 2026-11-23: "앉아서 맨 아래 보고 걸을 때 무릎 뚫리는 현상", "나중에 양손으로 총도 들고 할 텐데 다양한
/// 상황을 가정해도 안정성 있는 방법이 있을까?"). The spine as it stands is turned about each of its joints by its share of a
/// bend, as the bend would turn it, and the most it can take with every point clear is found by halving. The bend is then
/// eased into that limit (Soft), and the head takes what the spine gives up, so the view still looks where it looks.
/// What it may bend closes at once and opens again only once the legs have stayed clear of it for HoldFor, at OpenPace: a
/// crouched walk's knees, coming up and going down at every step, would otherwise bob the head with them.
/// The legs are never walls to the gun: it keeps its hold and its own wall pose (GunCoverLayer); only the body bends less.
/// Up to 0.9.362 the face was kept out of walls by giving back the spine's bend once it was on (PlayerBody.ClearFace), with
/// nothing in hand only, and nothing kept the head and the camera out of the knees.
/// </summary>
internal sealed class SpineRoom
{
    /// <summary>The most the spine bends down whatever is in front of it (degrees): no limit.</summary>
    internal const float Widest = 90f;
    // Within this of the limit the bend eases into it instead of stopping at it (degrees).
    private const float Knee = 8f;
    // A bend smaller than this is never looked into (degrees).
    private const float Least = 1f;
    private const int Halvings = 9;
    // The limit stays shut this long after the legs last came within Touch of it (s, degrees), then opens at this pace (degrees
    // a second). Held only while the bend goes on getting nearer than that, a crouched walk's knees, at their highest at every
    // step, swung the bend 8 to 11 degrees (worked out on a crouch leaning 25 to 35 degrees forward); held so, it stays.
    private const float HoldFor = .6f, OpenPace = 45f, Touch = 2f;
    // A ball is kept this far off the legs' skin (m).
    private const float LegGap = .01f;
    private const int MostJoints = 8, MostPoints = 8;

    private readonly Vector3[] joints = new Vector3[MostJoints];
    private readonly float[] shares = new float[MostJoints];
    private readonly Vector3[] points = new Vector3[MostPoints], offsets = new Vector3[MostPoints];
    private readonly float[] radii = new float[MostPoints];
    private readonly bool[] walled = new bool[MostPoints];
    private int jointCount, pointCount;
    private Vector3 axis = Vector3.right;
    private float bent, most = Widest, shutFor;

    /// <summary>The most the spine may bend down now (degrees, all its bones together).</summary>
    internal float Most => most;

    /// <summary>How far the nearest point was from the legs' skin at the bend wanted, the last time it was looked into (m).</summary>
    internal float Nearest { get; private set; } = float.PositiveInfinity;

    internal void Reset()
    {
        most = Widest;
        shutFor = 0f;
        Nearest = float.PositiveInfinity;
    }

    /// <summary>
    /// The bend is about <paramref name="bendAxis"/> (world; a turn about it by a positive angle bends the top forward and down),
    /// and the spine as it stands is already bent <paramref name="bentNow"/> degrees of it.
    /// </summary>
    internal void Begin(Vector3 bendAxis, float bentNow)
    {
        axis = bendAxis.sqrMagnitude > 1e-8f ? bendAxis.normalized : Vector3.right;
        bent = bentNow;
        jointCount = pointCount = 0;
    }

    /// <summary>A joint of the spine where it stands, from the hips up, and its share of the bend (the shares come to 1).</summary>
    internal void Joint(Vector3 at, float share)
    {
        if (jointCount >= MostJoints) return;
        joints[jointCount] = at;
        shares[jointCount++] = share;
    }

    /// <summary>
    /// A ball that must stay off the legs, and out of walls if <paramref name="walls"/>: <paramref name="offset"/> from a point
    /// <paramref name="at"/> that the spine carries, the offset staying as it is whatever the spine does (the eyes from the
    /// head, which looks where the view does).
    /// </summary>
    internal void Point(Vector3 at, Vector3 offset, float radius, bool walls)
    {
        if (pointCount >= MostPoints) return;
        points[pointCount] = at;
        offsets[pointCount] = offset;
        radii[pointCount] = radius;
        walled[pointCount++] = walls;
    }

    /// <summary>
    /// How far the spine may bend for a bend of <paramref name="want"/> (degrees) wanted now, kept in Most. <paramref name="clear"/>
    /// says whether a ball of a radius gets to a point without meeting a wall; null leaves walls out.
    /// </summary>
    internal float Measure(float want, float dt, OwnLegs legs, Func<Vector3, float, bool> clear)
    {
        float room = Widest;
        Nearest = float.PositiveInfinity;
        if (want > Least && jointCount > 0 && pointCount > 0)
        {
            bool walls = clear != null;
            Nearest = GapAt(want, legs);
            if (!Clear(want, walls, legs, clear))
            {
                // A wall the face is in with the spine straight isn't one bending less gets it out of; the camera's own sweep
                // (CameraClearance) has that.
                if (walls && !Clear(0f, true, null, clear)) walls = false;
                if (Clear(want, walls, legs, clear)) room = Widest;
                else if (!Clear(0f, walls, legs, clear)) room = 0f;
                else
                {
                    float clearTo = 0f, blockedAt = want;
                    for (int i = 0; i < Halvings; i++)
                    {
                        float half = (clearTo + blockedAt) * .5f;
                        if (Clear(half, walls, legs, clear)) clearTo = half;
                        else blockedAt = half;
                    }
                    room = clearTo;
                }
            }
        }
        if (room < most + Touch)
        {
            most = Mathf.Min(most, room);
            shutFor = 0f;
        }
        else
        {
            shutFor += dt;
            if (shutFor >= HoldFor) most = Mathf.MoveTowards(most, room, OpenPace * dt);
        }
        return most;
    }

    /// <summary>
    /// A bend of <paramref name="bend"/> degrees (down positive) as it goes on under a limit of <paramref name="most"/>: as it is
    /// well short of the limit, eased into the limit near it, never past it. A bend up is never limited.
    /// </summary>
    internal static float Soft(float bend, float most)
    {
        if (bend <= 0f) return bend;
        most = Mathf.Max(0f, most);
        float knee = Mathf.Min(Knee, most), from = most - knee;
        if (bend <= from) return bend;
        if (knee <= 1e-4f) return most;
        return most - knee * Mathf.Exp(-(bend - from) / knee);
    }

    private bool Clear(float bend, bool walls, OwnLegs legs, Func<Vector3, float, bool> clear)
    {
        for (int i = 0; i < pointCount; i++)
        {
            Vector3 at = Moved(points[i], bend) + offsets[i];
            if (legs != null && legs.Gap(at, radii[i]) < LegGap) return false;
            if (walls && walled[i] && !clear(at, radii[i])) return false;
        }
        return true;
    }

    private float GapAt(float bend, OwnLegs legs)
    {
        float nearest = float.PositiveInfinity;
        if (legs == null) return nearest;
        for (int i = 0; i < pointCount; i++) nearest = Mathf.Min(nearest, legs.Gap(Moved(points[i], bend) + offsets[i], radii[i]));
        return nearest;
    }

    // Where a point above the spine goes with the spine bent `bend` instead of as it stands: each joint turns what is above it
    // by its share of the difference, from the top down, each about where it stands now.
    private Vector3 Moved(Vector3 point, float bend)
    {
        float more = bend - bent;
        for (int j = jointCount - 1; j >= 0; j--)
            point = joints[j] + Quaternion.AngleAxis(more * shares[j], axis) * (point - joints[j]);
        return point;
    }
}
