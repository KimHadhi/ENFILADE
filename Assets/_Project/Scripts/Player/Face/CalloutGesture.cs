using System;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// A callout's motion on the body (콜아웃 모션; the manager after the user's A.V.A. note, 2026-11-12): a nod for yes and
/// thanks, a head shake for no, a tilt for sorry, the chin up for a shout; with the off hand free, an arm too: pointing at
/// the zombie or the spot it pins, a wave for help, a hand raised for wait or the charge, a beckon for hurry and follow.
/// Callouts.csv gesture names it (empty: a nod). It plays on every PC's copy of the body when that PC hears the callout
/// (PlayerFace.Said), so friends see it in the world and in the callout feed (CalloutFeedHUD).
/// Procedural and rig-agnostic, for any humanoid a modder brings: the head and neck turn about the character's own axes,
/// the arm's bones are aimed in world space at directions taken from the character (FromToRotation on each bone toward its
/// child), never through a rig's local axes. It runs after FPS Animation Framework and the hands' IK and before the cloth,
/// so the hair follows the nod; the first-person camera was placed earlier in the frame, so the player's own view never
/// nods with it. While a gun is held (FPSAF poses the arms) or the body is busy (ladder, ledge, held, down) only the head
/// and a little of the chest move: the gun hold is never broken.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunFramework + 5)]
public sealed class CalloutGesture : MonoBehaviour
{
    public enum Kind { None, Nod, Shake, Tilt, Jut, Point, Wave, Raise, Beckon, Hush }

    // How long each kind plays (s), as long as the motion reads; the face holds as long as the voice.
    private static readonly float[] Lengths = { 0f, .75f, .85f, .9f, .55f, 1.15f, 1.35f, 1.05f, 1.25f, 1.3f };
    private const float In = .14f, Out = .22f;
    // Head turns at most this far toward what it points at (°), and how the neck shares a head motion.
    private const float TurnYaw = 40f, TurnPitch = 20f, NeckShare = .4f;

    private PlayerBody body;
    private PlayerMovement movement;
    private Animator animator;
    private Transform neck, head, chest, upperArm, lowerArm, hand;
    private Kind kind;
    private float clock, length, wave;
    private Vector3 toward;
    private bool aims;

    /// <summary>The motion named in a Callouts.csv gesture cell (empty or unknown: a nod).</summary>
    public static Kind Parse(string name) =>
        !string.IsNullOrEmpty(name) && Enum.TryParse(name.Trim(), true, out Kind found) ? found : Kind.Nod;

    /// <summary>Plays the motion now (a new one replaces one under way); <paramref name="at"/> is what a point aims at.</summary>
    public void Play(Kind gesture, Vector3 at, bool hasTarget)
    {
        if (gesture == Kind.None) return;
        kind = gesture;
        clock = 0f;
        length = Lengths[(int)gesture];
        toward = at;
        aims = hasTarget;
        wave = UnityEngine.Random.value * 6.28f;
    }

    private void Start() => Bind();

    private void Bind()
    {
        body = GetComponentInParent<PlayerBody>();
        movement = GetComponentInParent<PlayerMovement>();
        animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return;
        neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        head = animator.GetBoneTransform(HumanBodyBones.Head);
        chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        upperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        lowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
    }

    private void LateUpdate()
    {
        if (kind == Kind.None) return;
        float dt = Time.deltaTime;
        if (dt <= 0f || GamePause.IsPaused) return;
        clock += dt;
        if (clock >= length || head == null)
        {
            kind = Kind.None;
            return;
        }
        float u = clock / length;
        float weight = Smooth(clock / In) * Smooth((length - clock) / Out);
        Vector3 up = Vector3.up;
        Vector3 forward = Facing();
        Vector3 right = Vector3.Cross(up, forward).normalized;
        bool free = HandsFree();
        var shown = free ? kind : HeadOnly(kind);
        switch (shown)
        {
            case Kind.Nod:
                // Two nods down and back up.
                Turn(right, 12f * (.5f - .5f * Mathf.Cos(u * Mathf.PI * 4f)) * weight, true);
                break;
            case Kind.Shake:
                Turn(up, 14f * Mathf.Sin(u * Mathf.PI * 4f) * weight, false);
                break;
            case Kind.Tilt:
                Turn(forward, -11f * weight, false);
                Turn(right, 5f * weight, false);
                break;
            case Kind.Jut:
                Turn(right, -8f * weight, true);
                break;
            case Kind.Point:
                LookToward(forward, right, weight);
                if (free) Arm(PointWay(forward, up), PointWay(forward, up), weight);
                break;
            case Kind.Wave:
                Turn(right, -5f * weight, false);
                if (free)
                {
                    Vector3 left = -right;
                    Vector3 sway = Quaternion.AngleAxis(26f * Mathf.Sin((clock * 2.6f) * Mathf.PI * 2f + wave), forward) * up;
                    Arm((left * .75f + up * .55f + forward * .2f).normalized, sway, weight);
                }
                break;
            case Kind.Raise:
                Turn(right, -6f * weight, true);
                if (free) Arm((up * .9f - right * .25f + forward * .25f).normalized, up, weight);
                break;
            case Kind.Beckon:
                Turn(right, 5f * (.5f - .5f * Mathf.Cos(u * Mathf.PI * 4f)) * weight, false);
                if (free)
                {
                    float curl = .5f + .5f * Mathf.Sin(clock * 2.2f * Mathf.PI * 2f);
                    Vector3 lower = Quaternion.AngleAxis(-55f * curl, right) * forward;
                    Arm((forward * .8f - right * .25f - up * .15f).normalized, lower, weight);
                }
                break;
            case Kind.Hush:
                // "Shh!": the head dips a little and the hand comes up to the lips, the elbow low (the user: the quiet
                // face must read "쉿!" at a glance).
                Turn(right, 5f * weight, false);
                if (free && upperArm != null && lowerArm != null)
                {
                    Vector3 elbowWay = (-up * .7f + forward * .55f + right * .22f).normalized;
                    Vector3 elbow = upperArm.position + elbowWay * Vector3.Distance(upperArm.position, lowerArm.position);
                    Vector3 lips = head.position + forward * .09f - up * .07f;
                    Arm(elbowWay, (lips - elbow).normalized, weight);
                }
                break;
        }
    }

    // Holding a gun (FPSAF has the arms), or the body busy with its hands or down, the arm stays where it is.
    private static Kind HeadOnly(Kind kind) => kind switch
    {
        Kind.Wave => Kind.Jut,
        Kind.Raise => Kind.Jut,
        Kind.Beckon => Kind.Nod,
        Kind.Hush => Kind.Tilt,
        _ => kind,
    };

    private bool HandsFree()
    {
        if (body == null || movement == null || upperArm == null || lowerArm == null || hand == null) return false;
        if (body.UpperBodyFromFramework || movement.IsDead || movement.IsDowned || movement.IsHeld) return false;
        var state = movement.CurrentState;
        return state == MovementState.Standing || state == MovementState.Crouching;
    }

    // Where the character looks, level: its view's heading, else its body's.
    private Vector3 Facing()
    {
        var view = body != null ? body.View : null;
        Vector3 forward = view != null ? view.forward : transform.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        return forward.sqrMagnitude < 1e-4f ? Vector3.forward : forward.normalized;
    }

    // A turn about one of the character's axes, shared by the neck and the head; with the chest a little when asked.
    private void Turn(Vector3 axis, float degrees, bool withChest)
    {
        if (Mathf.Abs(degrees) < .01f) return;
        if (withChest && chest != null) chest.rotation = Quaternion.AngleAxis(degrees * .25f, axis) * chest.rotation;
        if (neck != null)
        {
            neck.rotation = Quaternion.AngleAxis(degrees * NeckShare, axis) * neck.rotation;
            head.rotation = Quaternion.AngleAxis(degrees * (1f - NeckShare), axis) * head.rotation;
        }
        else head.rotation = Quaternion.AngleAxis(degrees, axis) * head.rotation;
    }

    // The head turns to what the callout is about (a zombie, a pinned spot), within limits.
    private void LookToward(Vector3 forward, Vector3 right, float weight)
    {
        if (!aims) return;
        Vector3 to = toward - head.position;
        if (to.sqrMagnitude < .01f) return;
        Vector3 flat = Vector3.ProjectOnPlane(to, Vector3.up);
        float yaw = flat.sqrMagnitude > 1e-4f ? Mathf.Clamp(Vector3.SignedAngle(forward, flat, Vector3.up), -TurnYaw, TurnYaw) : 0f;
        float pitch = Mathf.Clamp(-Mathf.Atan2(to.y, Mathf.Max(.01f, flat.magnitude)) * Mathf.Rad2Deg, -TurnPitch, TurnPitch);
        Turn(Vector3.up, yaw * weight, false);
        Turn(right, pitch * weight, false);
    }

    // The way a point goes: at the target from the shoulder when there is one, else ahead and a little up.
    private Vector3 PointWay(Vector3 forward, Vector3 up)
    {
        if (aims && upperArm != null)
        {
            Vector3 to = toward - upperArm.position;
            if (to.sqrMagnitude > .04f && Vector3.Dot(to.normalized, forward) > -.2f) return to.normalized;
        }
        return (forward + up * .15f).normalized;
    }

    // The off arm aimed in world space: the upper arm at one way, then the forearm at another.
    private void Arm(Vector3 upperWay, Vector3 lowerWay, float weight)
    {
        Aim(upperArm, lowerArm, upperWay, weight);
        Aim(lowerArm, hand, lowerWay, weight);
    }

    private static void Aim(Transform bone, Transform child, Vector3 want, float weight)
    {
        Vector3 now = child.position - bone.position;
        if (now.sqrMagnitude < 1e-8f || want.sqrMagnitude < 1e-8f || weight <= 0f) return;
        bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(now, want), weight) * bone.rotation;
    }

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
