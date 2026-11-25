using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The body's slack (몸의 여유, 0.9.437): on its feet the drawn body follows the capsule as a body can, not rigidly.
/// The capsule goes as the keys ask at once (0.9.435): it stops a reversal in 0.07 s (Move.groundSharpness 10), which no
/// legs do, and while the body was bolted to it every leg motion slid or flew as the keys changed, and every way of
/// slowing the capsule to the legs made the controls stiff (0.9.415 the legs carrying the body, 0.9.433 a limit on its
/// change). The user, 2026-11-25: "어찌해야 이 괴랄해진 movement system을 잘 만들 수 있을까?", then "브렌치 하지 말고 ㄱㄱ".
/// This is the usual way out (Daniel Holden, "Code vs Data Driven Displacement": the drawn character pulled toward the
/// simulated one, its distance from it bounded):
/// - The drawn body's own speed changes no faster than slackPush (m/s², a body's legs pushing off), and it closes on the
///   capsule over slackClose (s) when the capsule goes steadily, so on a steady run it is on the capsule.
/// - How far it stands off the capsule (the slack) eases out of a soft bound (tanh): up to slackMost it shows what it
///   is, never past it, and it is held at SlackReach times that inside, so the body never comes up hard against the bound
///   (a hard bound threw its speed in a frame where it struck, worse than the capsule's own turn: an offline simulation
///   of the capsule's ground rule, 2026-11-25).
/// - The legs read the drawn body's speed (PlayerBody.Locomotion), so the cycles carry the body that is drawn and its feet
///   stay down; the feet's footfalls read it too (PlantFeet).
/// - While a pivot plays (PlayerBody.Pivot, 0.9.442) its clip's own travel carries the drawn body instead, as it was made,
///   so its planted foot stays put; the slack it leaves shows as it is, up to the bound, and the pivot ends at the bound.
/// - A wall stops the slack: the body never goes into one past the capsule (a sprint into a wall stops the capsule, and
///   the body would have run on into the wall).
/// - Only on the feet (standing or crouched, on the ground, the keys moving it: PlayerMovement.StrideFrame), and none
///   while a clip carries the body (vaults, climbs, a run's turn back, a pair), a grip holds it, a slide anchors it or it
///   is dead. Elsewhere the slack eases out over slackEase.
/// - slackMost.x is the body seen from its own eyes, slackMost.y seen from outside (a friend's copy, F5, the rookie
///   camera). In first person the gun is held on the camera (FPSBody, PlayerView) and the hands on the gun: a body left
///   behind the camera stretches the arms to it, and the head comes to the camera only HeadReach's 8 cm. So the first
///   person's own body keeps 0 and goes as it did; the camera never takes the slack (PlayerBody.Look), so a value tried
///   there leaves the view where the capsule has it.
/// - Teleports, spawns and a copy put somewhere carry no slack over (SlackJump).
/// Numbers: Player.csv, row Body (slackMost, slackPush, slackClose, slackEase). slackMost 0;0 is the body bolted to the
/// capsule, as before 0.9.437, for a before and after in the same build (MoveTest's outside pass, /gait).
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Tooltip("How far the drawn body may stand off the capsule on its feet (m): x seen from its own eyes (first "
        + "person), y seen from outside (friends' copies, F5). 0 keeps it on the capsule.")]
    private Vector2 slackMost = new Vector2(0f, .3f);
    [SerializeField, Min(0f), Tooltip("How fast the drawn body's speed may change (m/s²): legs pushing off. 0 keeps the body on the "
        + "capsule.")]
    private float slackPush = 20f;
    [SerializeField, Min(.01f), Tooltip("How quickly the drawn body closes on the capsule while the capsule goes steadily (s).")]
    private float slackClose = .25f;
    [SerializeField, Min(.01f), Tooltip("How long the slack takes to come in or go out as the body gets onto or off its feet, or the "
        + "view goes in or out of the eyes (s).")]
    private float slackEase = .15f;

    // The slack is kept up to this many times slackMost inside, eased out of the soft bound (tanh(3) is .995).
    private const float SlackReach = 3f;
    // A capsule that went further than this in a frame (m), or faster than SlackJumpSpeed (m/s), was put there: no slack.
    private const float SlackJump = .5f, SlackJumpSpeed = 30f;
    // Under this the slack is none (m).
    private const float SlackNone = .001f;
    // Off the ground this long the feet are off (s): the capsule's ground can flicker for a frame at a stair's edge, and
    // closing the bound for that frame would move the drawn body a centimetre or so.
    private const float SlackAirGrace = .1f;

    // The drawn body's own speed (world, level) and how far it is off the capsule inside the bound (world, level).
    private Vector3 slackVelocity, slackGap;
    // The slack shown this frame (world, level, m), its change this frame (m/s), and how far the bound is open now (m).
    private Vector3 slackShown, slackShownVelocity;
    private float slackLimit;
    // Where the capsule was last frame.
    private Vector3 slackCapsuleWas;
    private bool slackKnown;
    private CharacterController slackCapsule;

    /// <summary>How far the drawn body stands off the capsule this frame (m), for the /gait readout.</summary>
    internal float SlackNow => slackShown.magnitude;

    // The drawn body's speed over the ground this frame on top of the capsule's (m/s, world, level): what the legs add.
    private Vector3 SlackSpeed => slackShownVelocity;

    // How far the bound is open, of the widest it opens (0-1): a copy's legs go on the drawn body's speed as far as it is, and
    // on the stride eased as before the rest of the way (Locomotion).
    private float SlackShare
    {
        get
        {
            float widest = Mathf.Max(slackMost.x, slackMost.y);
            return widest > SlackNone ? Mathf.Clamp01(slackLimit / widest) : 0f;
        }
    }

    /// <summary>The player was put somewhere (PlayerMovement.MoveTo): the body stands on the capsule there.</summary>
    internal void DropSlack() => ResetSlack();

    private void ResetSlack()
    {
        slackVelocity = slackGap = slackShown = slackShownVelocity = Vector3.zero;
        slackLimit = 0f;
        slackKnown = false;
    }

    // The body may stand off the capsule this frame: on its feet, the keys moving it, nothing else holding it.
    private bool SlackHolds()
    {
        if (dead || player.Carried || (!player.IsGrounded && airTime + Time.deltaTime > SlackAirGrace)) return false;
        var now = player.CurrentState;
        if (now != MovementState.Standing && now != MovementState.Crouching) return false;
        if (ClipCarriesBody || turnClip != null || gripWeight > 0f || slideBodyAnchored) return false;
        // The player's own body only while the keys move it: a surge or a hold moves the capsule otherwise, and the vault a
        // surge runs into warps the clip's hands to the capsule (PlayerMovement.StrideFrame).
        return player.IsNetworkDriven || player.StrideFrame == Time.frameCount;
    }

    // From Update, before the pose is chosen, so the legs read this frame's drawn speed (Locomotion).
    private void FollowCapsule(float dt)
    {
        Vector3 at = transform.position;
        Vector3 moved = at - slackCapsuleWas;
        moved.y = 0f;
        slackCapsuleWas = at;
        Vector3 shownWas = slackShown;
        if (!slackKnown || dt <= 0f || moved.magnitude > Mathf.Max(SlackJump, SlackJumpSpeed * dt))
        {
            // A new body, or a capsule put somewhere (a spawn, a teleport, a copy put far off, MoveTest's start). A copy's
            // small correction is no jump: the drawn body takes it over a few frames like any move.
            ResetSlack();
            slackKnown = true;
            return;
        }
        Vector3 capsule = moved / dt;
        float target = SlackHolds() ? Mathf.Max(0f, SeenFromOutside ? slackMost.y : slackMost.x) : 0f;
        float widest = Mathf.Max(slackMost.x, slackMost.y, SlackNone);
        slackLimit = Mathf.MoveTowards(slackLimit, target, dt * widest / Mathf.Max(.01f, slackEase));
        if (slackLimit <= SlackNone || slackPush <= 0f)
        {
            // On the capsule. A bound that closed on the way here (off the feet, into the eyes) took the gap in with it
            // frame by frame (the clamp in the other branch), so this is the last millimetre.
            slackGap = Vector3.zero;
            slackVelocity = capsule;
        }
        else if (PivotCarries(dt, capsule, out Vector3 carry))
        {
            // A pivot's own travel carries the drawn body, as the clip was made on it (PlayerBody.Pivot), so the foot it
            // plants stays where it planted it: the slack shown is what that travel has left between the body and the
            // capsule, as it is (the soft bound eased the body's speed off the clip's as the slack grew: a third of it at
            // 20 cm), never past the bound (the pivot ends at its edge). The gap the soft bound takes on from is what shows
            // that far.
            // From the body's own speed, at once but for what the clip never asks (PivotCatch): coming in, the clip goes only
            // its way in, and the body may have gone a little aside of it.
            slackVelocity = Vector3.MoveTowards(slackVelocity, carry, PivotCatch * dt);
            slackShown = Vector3.ClampMagnitude(slackShown + (slackVelocity - capsule) * dt, slackLimit);
            slackShown.y = 0f;
            slackGap = Unshown(slackShown, slackLimit);
            KeepSlackOutOfWalls(capsule);
            slackShownVelocity = (slackShown - shownWas) / dt;
            return;
        }
        else
        {
            // Toward the capsule's speed, and toward the capsule itself, no faster than legs change a body's speed.
            Vector3 want = capsule - slackGap / Mathf.Max(.01f, slackClose);
            slackVelocity = Vector3.MoveTowards(slackVelocity, want, slackPush * dt);
            slackVelocity.y = 0f;
            slackGap += (slackVelocity - capsule) * dt;
            float reach = slackLimit * SlackReach, gap = slackGap.magnitude;
            if (gap > reach)
            {
                Vector3 away = slackGap / gap;
                slackGap = away * reach;
                float outward = Vector3.Dot(slackVelocity - capsule, away);
                if (outward > 0f) slackVelocity -= away * outward;
            }
        }
        slackShown = Shown(slackGap, slackLimit);
        KeepSlackOutOfWalls(capsule);
        slackShownVelocity = (slackShown - shownWas) / dt;
    }

    // The slack shown for a gap kept inside the bound: as it is when small, never past the bound.
    private static Vector3 Shown(Vector3 gap, float limit)
    {
        float length = gap.magnitude;
        if (limit <= SlackNone || length <= SlackNone) return Vector3.zero;
        return gap * (limit * (float)System.Math.Tanh(length / limit) / length);
    }

    // The gap that shows this much slack (the inverse of Shown), within the bound's reach.
    private static Vector3 Unshown(Vector3 shown, float limit)
    {
        float length = shown.magnitude;
        if (limit <= SlackNone || length <= SlackNone) return Vector3.zero;
        return shown * (Mathf.Min(limit * Atanh(length / limit), limit * SlackReach) / length);
    }

    // The body goes no further off the capsule than the capsule could go itself: a wall that way stops it there, and what
    // the gap held past it goes, so it never comes back out of the wall later.
    private void KeepSlackOutOfWalls(Vector3 capsule)
    {
        float length = slackShown.magnitude;
        if (length <= .01f) return;
        if (slackCapsule == null && !TryGetComponent(out slackCapsule)) return;
        float radius = slackCapsule.radius - slackCapsule.skinWidth - .01f;
        if (radius <= .02f) return;
        Vector3 centre = transform.TransformPoint(slackCapsule.center);
        float half = Mathf.Max(0f, slackCapsule.height * .5f - slackCapsule.radius);
        // Its lowest round lifted by a step, so the floor and a stair under it don't stop it.
        Vector3 top = centre + Vector3.up * half;
        Vector3 bottom = centre - Vector3.up * Mathf.Max(0f, half - slackCapsule.stepOffset);
        if (bottom.y > top.y) bottom = top;
        Vector3 way = slackShown / length;
        LayerMask walls = player.groundLayers & ~LayerMask.GetMask("Enemy");
        if (!Physics.CapsuleCast(top, bottom, radius, way, out RaycastHit hit, length, walls, QueryTriggerInteraction.Ignore)) return;
        float free = Mathf.Max(0f, hit.distance - .01f);
        slackShown = way * free;
        // The gap goes back to what shows that far, and the body's speed into the wall goes.
        slackGap = Unshown(slackShown, slackLimit);
        float into = Vector3.Dot(slackVelocity - capsule, way);
        if (into > 0f) slackVelocity -= way * into;
    }

    private static float Atanh(float x)
    {
        x = Mathf.Clamp(x, 0f, .999f);
        return .5f * Mathf.Log((1f + x) / (1f - x));
    }

    // From Update right after PlaceBody: the body stands its slack off the capsule.
    private void ShowSlack()
    {
        if (body == null || slackShown.sqrMagnitude < SlackNone * SlackNone) return;
        body.position += slackShown;
    }
}
