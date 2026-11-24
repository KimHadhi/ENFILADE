using UnityEngine;

/// <summary>
/// The planted foot carries the body (0.9.425): walking, running and through a run's turn, the foot the pose has down keeps
/// its place on the ground, and the body goes over it and turns on it as the clip has it go, whatever the clip, its blend or
/// its pace. Each frame, after the animation pass and before anything bends the pose: of the foot the pose has down, the end
/// of its sole that is lowest (heel or toe tip, FootPlanting.SoleUnder) stays where it stood last frame, and the body's root
/// moves by as much as the pose moved it, so a heel rolls onto the toes, a toe pivots under a turn and the body goes ahead as
/// the clip's feet push it. With no foot down (a run's flight, an end of the sole taking over) the body goes on at its pace.
/// The capsule goes after the body (Wanted), so the body never leads it by more than LeadMost: a wall that stops the capsule
/// stops the body there.
/// Until 0.9.425 the body went as the walk's cycles were measured to carry it at their pace (PlayerBody.LegsCarry, 0.9.415)
/// and through a run's turn as its clip's root curve went, and the foot the pose had down went over the ground wherever the
/// two parted: a third of the feet record's frames slid, most in a turn, in its hand-over and in the blends between clips
/// (the feet record of 2026-11-25 05:51; the user: "wasd 누르니까 발이 그냥 주체를 못 하고 막 날라다녀 … 근본이 이상한 거
/// 같은데?").
/// </summary>
internal sealed class FootCarry
{
    // The body leads the capsule's rest place by no more than this (m).
    private const float LeadMost = .2f;
    // The capsule closes on the body over this long (s): fast enough that the body stays over it, slow enough that a step's
    // push and catch don't shake it.
    private const float Catch = .1f;
    // The body's pace over the ground is eased over this long (s): what it goes on at with no foot down.
    private const float PaceTime = .1f;
    // A sole carries on its other end once that end is this much lower (m), so a foot lying flat doesn't swap ends each frame.
    private const float EndSwap = .01f;
    // A foot comes down to carry the body only while it goes slower than this over the ground (m/s): a foot swinging through
    // low is in the air, and taking it as down would drag the body back with it. Read against the body going on at its pace,
    // which lags the feet by PaceTime: at a footfall in a sharp change of way (a turn's pivot, a reversal) a foot that is down
    // can seem faster than this, so once no foot has carried the body for LooseMost (s, longer than a sprint's flight) a foot
    // low on its floor comes down however fast. Without it the body went on at a pace nothing measured again, frozen, and
    // the capsule after it (the review of 2026-11-25).
    private const float LandsSlower = 1.5f, LooseMost = .15f;

    // The foot carrying (0 none, 1 left, 2 right), at which end, and where that end stands on the ground (world); where each
    // ankle was last frame (world), for how fast a foot coming down goes.
    private int foot;
    private bool heel;
    private Vector3 anchor;
    private readonly Vector3[] ankles = new Vector3[2];
    private int frame = -10;
    // How long no foot has carried the body (s).
    private float loose;

    /// <summary>Where the body's root was put last (world), and its pace over the ground (world, level, m/s).</summary>
    internal Vector3 Root { get; private set; }
    internal Vector3 Pace { get; private set; }
    /// <summary>How fast the capsule goes after the body (world, level, m/s): its pace, and closing on it over Catch.</summary>
    internal Vector3 Wanted { get; private set; }
    /// <summary>The feet carried the body last frame.</summary>
    internal bool Carried => frame == Time.frameCount - 1;

    /// <summary>Where the body's root goes on to this frame before its pose is read: on at its pace from where it was put.</summary>
    internal Vector3 Ahead(float dt) => Root + Pace * dt;

    /// <summary>The ground under the capsule moved it <paramref name="by"/> this frame (someone it stands on, MovementMotor.Ride):
    /// the foot that is down stands on that ground, and goes with it.</summary>
    internal void Ride(Vector3 by)
    {
        if (!Carried || by.sqrMagnitude < 1e-10f) return;
        Vector3 flat = Flat(by);
        anchor += flat;
        Root += flat;
        ankles[0] += flat;
        ankles[1] += flat;
    }

    /// <summary>The carry stops: the body was put somewhere else with its capsule (PlayerMovement.MoveTo).</summary>
    internal void Drop() => frame = -10;

    /// <summary>
    /// Where the body's root goes this frame (world): from <paramref name="root"/> (where it stands now, gone on at its pace),
    /// moved so the foot the pose has down stays where it stood, within LeadMost of <paramref name="rest"/> (its place over the
    /// capsule). <paramref name="pace"/>: how fast it goes as it begins (world, m/s).
    /// </summary>
    internal Vector3 Step(FootPlanting feet, Vector3 root, Vector3 rest, Vector3 pace, float dt)
    {
        bool going = Carried;
        if (!going)
        {
            foot = 0;
            loose = 0f;
            Pace = Flat(pace);
        }
        bool leftDown = Down(feet, feet.Left, 1, going, dt, out Vector3 leftAt, out bool leftHeel);
        bool rightDown = Down(feet, feet.Right, 2, going, dt, out Vector3 rightAt, out bool rightHeel);
        // The foot carrying goes on carrying until its clip lifts it; then the lower one that is down takes over.
        int now = foot == 1 && leftDown ? 1 : foot == 2 && rightDown ? 2
            : leftDown && (!rightDown || leftAt.y <= rightAt.y) ? 1 : rightDown ? 2 : 0;
        Vector3 point = now == 1 ? leftAt : rightAt;
        bool atHeel = now == 1 ? leftHeel : rightHeel;
        Vector3 from = root;
        // Only the same end of the same foot as last frame: a foot coming down, or a heel rolling onto its toes, takes its
        // place from here, and the body goes on at its pace this frame.
        if (going && now != 0 && now == foot && atHeel == heel) root += Flat(anchor - point);
        // The pace is how the feet carry the body, before it is held to the capsule: with the hold in it, a capsule put a few
        // metres off (a step of a skill, a hitch) flung the body after it at tens of metres a second (the review of 2026-11-25).
        if (going) Pace = Vector3.Lerp(Pace, Flat(root - Root) / dt, 1f - Mathf.Exp(-dt / PaceTime));
        loose = now != 0 ? 0f : loose + dt;
        Vector3 lead = Flat(root - rest);
        if (lead.sqrMagnitude > LeadMost * LeadMost)
        {
            Vector3 held = lead.normalized * LeadMost;
            root += held - lead;
            lead = held;
        }
        Vector3 moved = Flat(root - from);
        anchor = point + moved;
        ankles[0] = feet.Left.Foot.position + moved;
        ankles[1] = feet.Right.Foot.position + moved;
        Root = root;
        foot = now;
        heel = atHeel;
        frame = Time.frameCount;
        Wanted = Pace + lead / Catch;
        return root;
    }

    // Whether this foot is down, and its sole's lowest end (world) and which. The one carrying stays down until its clip lifts
    // it; another comes down low on its floor (FootPlanting.SoleDown) and slower than LandsSlower over the ground, or however
    // fast once the body has gone LooseMost with no foot.
    private bool Down(FootPlanting feet, FootPlanting.Leg leg, int side, bool going, float dt, out Vector3 point, out bool atHeel)
    {
        bool carries = going && foot == side;
        float over = feet.SoleUnder(leg, carries && heel, EndSwap, out point, out atHeel);
        if (carries) return FootPlanting.SoleDown(over, true);
        if (!FootPlanting.SoleDown(over, false)) return false;
        return !going || loose >= LooseMost
            || Flat(leg.Foot.position - ankles[side - 1]).sqrMagnitude <= LandsSlower * LandsSlower * dt * dt;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
