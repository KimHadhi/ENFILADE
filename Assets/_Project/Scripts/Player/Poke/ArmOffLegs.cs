using UnityEngine;

/// <summary>
/// Keeps a posed arm out of the player's own legs along its whole length: the upper arm, the forearm, the hand and its finger
/// (PointArm; the user, 2026-11-23: "앉은 채로 내 다리쪽으로 가면 내 팔과 손이 다리를 뚫는데"). The elbow, the wrist and the
/// fingertip are points joined by the arm's own lengths, drawn to where the pose has them and pushed out of the legs as a body
/// would be; what they were pushed lasts from frame to frame and eases away, so the arm rests on a leg, slides over it and comes
/// off it, and never jumps. A push goes up as well as out, so an arm pressed against the side of a thigh climbs over it.
/// Not the engine's physics ("팔이 물리로 움직이는 게 나을 거 같아요 아니면 이 방식이 나을 거 같아요?"): the arm is posed every
/// frame after the clips, so a body of its own would be thrown about by that pose, would stand otherwise on every PC, and would
/// go on shaking where it rests. This is worked out afresh from the same look and the same legs everywhere.
/// 0.9.360 asked only how far the line the finger points along may go before it meets a leg: the forearm went through a thigh
/// beside that line, and the arm jumped between two poses at a leg's edge (the user's film of 2026-11-23). Tried on crouched
/// legs with the look thrown about, the arm as posed goes 11 cm deep and this none, and it moves no more in a frame than the
/// pose does.
/// </summary>
internal sealed class ArmOffLegs
{
    private struct Rod
    {
        public Transform From, To;
        // How thick toward each end: as it stands on this body, and as its length says.
        public float Start, End, SaidStart, SaidEnd;
    }

    // Shoulder, elbow, wrist, fingertip, and the three parts between them.
    private const int Joints = 4, Parts = 3;
    // How far up a push goes beside straight out (1 as far).
    private const float Lift = 1f;
    // What an arm was pushed eases away over this long (s).
    private const float Ease = .12f;
    private const int Passes = 8;
    // The pose is followed at most this far at a time (m), so it never carries the arm past a leg, in at most this many goes.
    private const float StepMost = .03f;
    private const int StepsMost = 6;
    // Pushed, the wrist may bend this much more than it is posed (degrees); the arm holds it within its own limit after.
    private const float WristGive = 20f;
    // A limb is never taken thinner or thicker than these shares of what its length says.
    private const float Thinnest = .7f, Thickest = 2f;

    private readonly Rod[] legs = new Rod[4];
    private readonly Vector3[] legFrom = new Vector3[4], legTo = new Vector3[4];
    private readonly float[] thick = new float[Parts], said = new float[Parts], length = new float[Parts];
    // From the shoulder, in the body's own frame: where the pose has each joint now and had it last, how far off it stands,
    // and where it is.
    private readonly Vector3[] goal = new Vector3[Joints], last = new Vector3[Joints], off = new Vector3[Joints], at = new Vector3[Joints];
    private Vector3 up;
    private float bend;
    private bool held;

    internal void Bind(Animator animator, ArmBones arm)
    {
        Transform Bone(HumanBodyBones bone) => animator.GetBoneTransform(bone);
        Leg(0, Bone(HumanBodyBones.LeftUpperLeg), Bone(HumanBodyBones.LeftLowerLeg), Share("thighStart", .17f), Share("thighEnd", .165f));
        Leg(1, Bone(HumanBodyBones.RightUpperLeg), Bone(HumanBodyBones.RightLowerLeg), Share("thighStart", .17f), Share("thighEnd", .165f));
        Leg(2, Bone(HumanBodyBones.LeftLowerLeg), Bone(HumanBodyBones.LeftFoot), Share("shinStart", .15f), Share("shinEnd", .13f));
        Leg(3, Bone(HumanBodyBones.RightLowerLeg), Bone(HumanBodyBones.RightFoot), Share("shinStart", .15f), Share("shinEnd", .13f));
        said[0] = Vector3.Distance(arm.Upper.position, arm.Lower.position) * Share("upperArmEnd", .11f);
        said[1] = Vector3.Distance(arm.Lower.position, arm.Hand.position) * Share("forearmStart", .12f);
        said[2] = arm.HandLength * .4f;
        for (int part = 0; part < Parts; part++) thick[part] = said[part];
        held = false;
    }

    private static float Share(string column, float fallback) => GameTables.Number(PlayerBodyCloth.TableName, PlayerBodyCloth.TableRow, column, fallback);

    private void Leg(int index, Transform from, Transform to, float start, float end)
    {
        float span = from != null && to != null ? Vector3.Distance(from.position, to.position) : 0f;
        legs[index] = new Rod { From = from, To = to, Start = span * start, End = span * end, SaidStart = span * start, SaidEnd = span * end };
    }

    /// <summary>The legs and the arm as thick as they are on this body with what is worn on them (PokeBody), where it could be read.</summary>
    internal void Measure(PokeBody body, ArmBones arm)
    {
        for (int i = 0; i < legs.Length; i++)
            if (body.Thickness(legs[i].From, legs[i].To, out float start, out float end))
            {
                legs[i].Start = Between(start, legs[i].SaidStart);
                legs[i].End = Between(end, legs[i].SaidEnd);
            }
        if (body.Thickness(arm.Upper, arm.Lower, out _, out float upper)) thick[0] = Between(upper, said[0]);
        if (body.Thickness(arm.Lower, arm.Hand, out float fore, out float wrist)) thick[1] = Between(Mathf.Max(fore, wrist), said[1]);
    }

    private static float Between(float measured, float length) => Mathf.Clamp(measured, length * Thinnest, length * Thickest);

    /// <summary>The arm is down: the next one begins from its pose.</summary>
    internal void Let() => held = false;

    /// <summary>The arm's joints off the legs; false where the pose stands as it is.</summary>
    internal bool Solve(Quaternion frame, Vector3 shoulder, ref Vector3 elbow, ref Vector3 wrist, ref Vector3 tip, float dt)
    {
        Quaternion into = Quaternion.Inverse(frame);
        up = into * Vector3.up;
        goal[1] = into * (elbow - shoulder);
        goal[2] = into * (wrist - shoulder);
        goal[3] = into * (tip - shoulder);
        for (int part = 0; part < Parts; part++) length[part] = (goal[part + 1] - goal[part]).magnitude;
        for (int i = 0; i < legs.Length; i++)
            if (legs[i].From != null && legs[i].To != null)
            {
                legFrom[i] = into * (legs[i].From.position - shoulder);
                legTo[i] = into * (legs[i].To.position - shoulder);
            }
        bend = Vector3.Angle(goal[2] - goal[1], goal[3] - goal[2]) + WristGive;

        float move = 0f;
        for (int joint = 1; joint < Joints; joint++)
        {
            if (!held)
            {
                last[joint] = goal[joint];
                off[joint] = Vector3.zero;
            }
            move = Mathf.Max(move, (goal[joint] - last[joint]).magnitude);
        }
        held = true;
        int steps = Mathf.Clamp(Mathf.CeilToInt(move / StepMost), 1, StepsMost);
        float keep = Mathf.Exp(-dt / (steps * Ease));
        for (int step = 1; step <= steps; step++)
        {
            float share = step / (float)steps;
            for (int joint = 1; joint < Joints; joint++) at[joint] = Vector3.Lerp(last[joint], goal[joint], share) + off[joint] * keep;
            Settle();
            for (int joint = 1; joint < Joints; joint++) off[joint] = at[joint] - Vector3.Lerp(last[joint], goal[joint], share);
        }
        float most = 0f;
        for (int joint = 1; joint < Joints; joint++)
        {
            last[joint] = goal[joint];
            most = Mathf.Max(most, off[joint].sqrMagnitude);
        }
        if (most < 1e-8f) return false;
        elbow = shoulder + frame * at[1];
        wrist = shoulder + frame * at[2];
        tip = shoulder + frame * at[3];
        return true;
    }

    // Out of the legs and back to the arm's lengths, over and over: both hold at the end.
    private void Settle()
    {
        for (int pass = 0; pass < Passes; pass++)
        {
            for (int part = Parts - 1; part >= 0; part--)
                for (int i = 0; i < legs.Length; i++)
                    if (legs[i].From != null && legs[i].To != null) Push(part, i);
            at[1] = at[1].normalized * length[0];
            Join(1, .5f);
            Join(2, .3f);
            HoldWrist();
            at[1] = at[1].normalized * length[0];
        }
        at[2] = at[1] + (at[2] - at[1]).normalized * length[1];
        HoldWrist();
    }

    // Two joints back to the length between them, the nearer one taking this share of it.
    private void Join(int part, float share)
    {
        Vector3 between = at[part + 1] - at[part];
        float now = between.magnitude;
        if (now < 1e-6f) return;
        between *= (now - length[part]) / now;
        at[part] += between * share;
        at[part + 1] -= between * (1f - share);
    }

    private void HoldWrist()
    {
        Vector3 forearm = at[2] - at[1], hand = at[3] - at[2];
        if (Vector3.Angle(forearm, hand) > bend) hand = Vector3.RotateTowards(forearm, hand, bend * Mathf.Deg2Rad, float.PositiveInfinity);
        at[3] = at[2] + hand.normalized * length[2];
    }

    private void Push(int part, int leg)
    {
        Nearest(at[part], at[part + 1], legFrom[leg], legTo[leg], out float along, out float down);
        Vector3 away = Vector3.Lerp(at[part], at[part + 1], along) - Vector3.Lerp(legFrom[leg], legTo[leg], down);
        float apart = away.magnitude, need = thick[part] + Mathf.Lerp(legs[leg].Start, legs[leg].End, down) - apart;
        if (need <= 0f) return;
        away = apart > 1e-4f ? away / apart : up;
        Vector3 way = (away + up * Lift).normalized;
        float nearShare = part == 0 ? 0f : 1f - along, farShare = along;
        Vector3 push = way * (need / Mathf.Max(.35f, Vector3.Dot(way, away)) / Mathf.Max(.2f, nearShare * nearShare + farShare * farShare));
        at[part] += push * nearShare;
        at[part + 1] += push * farShare;
    }

    // Where two lines, each from one end to the other, come nearest each other (0-1 along each).
    private static void Nearest(Vector3 p, Vector3 q, Vector3 a, Vector3 b, out float along, out float down)
    {
        Vector3 first = q - p, second = b - a, between = p - a;
        float firstLong = first.sqrMagnitude, secondLong = second.sqrMagnitude;
        float f = Vector3.Dot(second, between), c = Vector3.Dot(first, between), both = Vector3.Dot(first, second);
        float spread = firstLong * secondLong - both * both;
        along = spread > 1e-9f ? Mathf.Clamp01((both * f - c * secondLong) / spread) : .5f;
        down = secondLong > 1e-9f ? (both * along + f) / secondLong : 0f;
        if (down >= 0f && down <= 1f) return;
        down = Mathf.Clamp01(down);
        along = firstLong > 1e-9f ? Mathf.Clamp01((both * down - c) / firstLong) : 0f;
    }
}
