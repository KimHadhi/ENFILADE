using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Stage 4D-1, at the user's word (an arm shot off, it still swung with that arm): a zombie that has lost an arm swings
/// with the one it has left, and one with no arms bites. Gore (ZombieGore.Crippled, every PC alike) says which arm went.
/// Which hand a swing clip strikes with isn't in its name, so the first time it is needed each candidate
/// (ZombieLooks.oneArmAttacks) is played through once on this body and the hand that reaches furthest forward is the one
/// it strikes with, that reach its strike frame (an AnimationEvent_AttackHit wins if the clip has one); every zombie of
/// the pack then shares the answer. If its own swing already uses the arm it kept, nothing changes. The chosen clip sits
/// on top input 12 and takes the swing's place, timed to the windup as the swing is (SwingTime). Down at the moment, it
/// waits until it stands.
/// </summary>
public sealed partial class ZombieBody
{
    private const int ArmSwingInput = 12, BiteInput = 13, GrabInput = 14, HoldInput = 15, FeedStartInput = 16, FeedInput = 17,
        ReachSamples = 24;
    // How fast it takes hold in its pose, and lets go (s).
    private const float HoldIn = .15f, HoldOut = .3f;

    // Each swing clip: whether it strikes with the left hand, and when it lands (s). Measured once, shared.
    private static readonly Dictionary<AnimationClip, (bool left, float hit)> swingHands =
        new Dictionary<AnimationClip, (bool, float)>();
    // Which way each grab clip's chest faces against the body's forward (degrees about up): paired grab animations are
    // made facing the other way round (the user saw a shambler holding them with its back turned). Measured once, shared.
    private static readonly Dictionary<AnimationClip, float> clipYaws = new Dictionary<AnimationClip, float>();
    private AnimationClip grabHoldClip, feedStartClip, feedClip;
    private float grabYaw, holdYaw, feedStartYaw, feedYaw;
    private bool yawsMeasured;

    private AnimationClipPlayable armSwing, biteSwing, grabSwing, grabHold, feedStart, feedLoop;
    private AnimationClip grabClip;
    private ZombieGrip grip;
    private float armSwingHit, biteHit, grabHit, grabHoldLength, grabHoldTime, holdWeight, feedStartHit, feedLength, feedShare;
    private bool leftArmLost, rightArmLost, armSwingOn, armsPending, swingIsBite, swingIsGrab, swingIsFeed, grabMeasured;

    // A runner's bite at the neck (ZombieLooks.bite) on top input 13, its bite timed to the windup as a swing is.
    private void BuildBite(ZombieLooks looks, AnimationClip rest)
    {
        var clip = Or(looks.bite, Or(looks.noArmsAttack, Or(looks.attack, rest)));
        biteSwing = Clip(clip, top, BiteInput, out float length);
        biteHit = HitTime(clip, length);
        // A shambler's grab (top 14) and its hold (15).
        grabClip = Or(looks.grab, Or(looks.attack, rest));
        grabSwing = Clip(grabClip, top, GrabInput, out float grabLength);
        grabHit = HitTime(grabClip, grabLength);
        grabHoldClip = Or(looks.grabHold, Or(looks.grab, rest));
        grabHold = Clip(grabHoldClip, top, HoldInput, out grabHoldLength);
        // Kneeling to eat someone lying down (16) and eating them (17).
        var kneel = Or(looks.feedStart, Or(looks.grab, rest));
        feedStartClip = kneel;
        feedStart = Clip(kneel, top, FeedStartInput, out float kneelLength);
        // It bites as the kneel ends.
        feedStartHit = kneelLength * .85f;
        feedClip = Or(looks.feed, Or(looks.grabHold, rest));
        feedLoop = Clip(feedClip, top, FeedInput, out feedLength);
        grip = GetComponent<ZombieGrip>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearArms()
    {
        swingHands.Clear();
        clipYaws.Clear();
    }

    private void OnArmLost(BodyPart part)
    {
        if (part == BodyPart.LeftUpperArm || part == BodyPart.LeftLowerArm) leftArmLost = true;
        else if (part == BodyPart.RightUpperArm || part == BodyPart.RightLowerArm) rightArmLost = true;
        else return;
        armsPending = true;
    }

    // A copy made late: the gore already took arms. Their hitboxes are switched off.
    private void FindLostArms()
    {
        if (!ZombieGore.Losses(enemyHealth, out _, out int arms) || arms <= 0) return;
        leftArmLost = HitboxGone(HumanBodyBones.LeftUpperArm) || HitboxGone(HumanBodyBones.LeftLowerArm);
        rightArmLost = HitboxGone(HumanBodyBones.RightUpperArm) || HitboxGone(HumanBodyBones.RightLowerArm);
        armsPending = leftArmLost || rightArmLost;
    }

    private bool HitboxGone(HumanBodyBones bone)
    {
        var at = human != null ? human[(int)bone] : null;
        if (at == null) return false;
        foreach (Transform child in at)
            if (child.name == "Hitbox") return !child.gameObject.activeSelf;
        return false;
    }

    /// <summary>From Update: once it stands, its swing changes to suit the arms it has.</summary>
    private void SettleArms()
    {
        if (!armsPending || down || animator == null || !animator.enabled) return;
        armsPending = false;
        var looks = ZombieLooks.Instance;
        if (looks == null) return;
        AnimationClip chosen = null;
        if (leftArmLost && rightArmLost) chosen = looks.noArmsAttack;
        else if (leftArmLost || rightArmLost)
        {
            bool keepLeft = rightArmLost;
            // Its own swing (the one it picked, ZombieLooks.attacks) may already strike with the arm it kept.
            if (attackClip != null && Hand(attackClip).left == keepLeft) chosen = null;
            else if (looks.oneArmAttacks != null)
                foreach (var clip in looks.oneArmAttacks)
                    if (clip != null && Hand(clip).left == keepLeft) { chosen = clip; break; }
        }
        UseArmSwing(chosen);
        Weigh();
    }

    private void UseArmSwing(AnimationClip clip)
    {
        if (armSwing.IsValid())
        {
            graph.Disconnect(top, ArmSwingInput);
            armSwing.Destroy();
        }
        armSwingOn = clip != null;
        if (!armSwingOn) return;
        armSwingHit = Hand(clip).hit;
        armSwing = Clip(clip, top, ArmSwingInput, out _);
    }

    /// <summary>From Update, after Act: the arm's swing, or a bite, follows the windup as the swing does.</summary>
    private void ArmsTick()
    {
        if (armSwingOn) armSwing.SetTime(SwingTime(armSwingHit));
        // A bite or a grab shows from its windup to the end of its follow-through.
        if (combat != null && combat.IsBiting) swingIsBite = true;
        else if (!striking) swingIsBite = false;
        if (combat != null && combat.IsGrabbing) swingIsGrab = true;
        else if (!striking) swingIsGrab = false;
        if (combat != null && combat.IsFeeding) swingIsFeed = true;
        else if (!striking) swingIsFeed = false;
        // The grab's reach, and which way the grab clips face, are measured the first time one is under way, standing up.
        if ((swingIsGrab || swingIsFeed || (grip != null && grip.IsHolding)) && !down && animator != null && animator.enabled)
        {
            if (!yawsMeasured) MeasureYaws();
            // Its reach is measured along the way the clip faces, not the body's forward.
            if (!grabMeasured && swingIsGrab) { grabHit = Hand(grabClip, grabYaw).hit; grabMeasured = true; }
        }
        biteSwing.SetTime(SwingTime(biteHit));
        grabSwing.SetTime(SwingTime(grabHit));
        feedStart.SetTime(SwingTime(feedStartHit));
        // Holding someone it hangs on to them, round and round. A candyman dragging someone off walks on its legs instead.
        bool holding = grip != null && grip.IsHolding && !grip.Dragging && !down;
        holdWeight = Mathf.MoveTowards(holdWeight, holding ? 1f : 0f, Time.deltaTime / (holding ? HoldIn : HoldOut));
        // Kneeling to eat, or standing holding on.
        feedShare = grip != null && grip.Feeding ? 1f : holding ? 0f : feedShare;
        if (holdWeight > 0f)
        {
            grabHoldTime += Time.deltaTime;
            grabHold.SetTime(Mathf.Repeat(grabHoldTime, grabHoldLength));
            feedLoop.SetTime(Mathf.Repeat(grabHoldTime, feedLength));
        }
    }

    private void MeasureYaws()
    {
        yawsMeasured = true;
        grabYaw = YawOf(grabClip, grabSwing, GrabInput, grabClip != null ? grabClip.length * .5f : 0f);
        holdYaw = YawOf(grabHoldClip, grabHold, HoldInput, 0f);
        feedStartYaw = YawOf(feedStartClip, feedStart, FeedStartInput, feedStartHit);
        feedYaw = YawOf(feedClip, feedLoop, FeedInput, 0f);
        // All on the same side of the turn as the grab, so one blending into another doesn't swing the body round.
        holdYaw = grabYaw + Mathf.DeltaAngle(grabYaw, holdYaw);
        feedStartYaw = grabYaw + Mathf.DeltaAngle(grabYaw, feedStartYaw);
        feedYaw = grabYaw + Mathf.DeltaAngle(grabYaw, feedYaw);
    }

    // The clip posed alone at this time: which way the body faces in its own space (degrees about up, 0 ahead), square
    // to the line through its shoulders and its hips, so a body bent right over (kneeling to eat) reads as well.
    private float YawOf(AnimationClip clip, AnimationClipPlayable playable, int input, float time)
    {
        if (clip == null) return 0f;
        if (clipYaws.TryGetValue(clip, out float known)) return known;
        var leftArm = human[(int)HumanBodyBones.LeftUpperArm];
        var rightArm = human[(int)HumanBodyBones.RightUpperArm];
        var leftLeg = human[(int)HumanBodyBones.LeftUpperLeg];
        var rightLeg = human[(int)HumanBodyBones.RightUpperLeg];
        if (leftArm == null || rightArm == null || leftLeg == null || rightLeg == null) return 0f;
        for (int i = 0; i < top.GetInputCount(); i++) top.SetInputWeight(i, i == input ? 1f : 0f);
        playable.SetTime(time);
        graph.Evaluate(0f);
        var space = Model.transform;
        Vector3 across = space.InverseTransformDirection(rightArm.position - leftArm.position + rightLeg.position - leftLeg.position);
        Vector3 facing = Vector3.Cross(across, Vector3.up);
        float yaw = new Vector2(facing.x, facing.z).sqrMagnitude > 1e-6f ? Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg : 0f;
        clipYaws[clip] = yaw;
        return yaw;
    }

    /// <summary>
    /// From Weigh, with the grab clips' weights: turns the body back by the way they face, so a shambler reaching for,
    /// holding or eating a player faces them however the clip was made.
    /// </summary>
    private void FaceGrabs(float grab, float hold, float kneel, float eat)
    {
        if (Model == null) return;
        float turn = grabYaw * grab + holdYaw * hold + feedStartYaw * kneel + feedYaw * eat;
        Model.transform.localRotation = Quaternion.Euler(0f, -turn, 0f) * modelTurn;
    }

    // Plays the clip through once on this body (top input 12 alone) and sees which hand reaches furthest forward of the
    // hips: that is the hand it strikes with, and that moment its strike, unless the clip marks it with an event.
    // Forward is the body's, turned by yaw (degrees about up) for a clip made facing another way.
    private (bool left, float hit) Hand(AnimationClip clip, float yaw = 0f)
    {
        if (clip == null) return (false, 0f);
        if (swingHands.TryGetValue(clip, out var known)) return known;
        var hips = human[(int)HumanBodyBones.Hips];
        var leftHand = human[(int)HumanBodyBones.LeftHand];
        var rightHand = human[(int)HumanBodyBones.RightHand];
        if (hips == null || leftHand == null || rightHand == null) return (false, HitTime(clip, clip.length));
        bool hadSwing = armSwing.IsValid();
        if (hadSwing) graph.Disconnect(top, ArmSwingInput);
        var probe = AnimationClipPlayable.Create(graph, clip);
        probe.SetSpeed(0d);
        graph.Connect(probe, 0, top, ArmSwingInput);
        for (int i = 0; i < top.GetInputCount(); i++) top.SetInputWeight(i, i == ArmSwingInput ? 1f : 0f);
        var space = Model.transform;
        Vector3 ahead = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        float leftReach = float.NegativeInfinity, rightReach = float.NegativeInfinity, leftAt = 0f, rightAt = 0f;
        for (int sample = 0; sample <= ReachSamples; sample++)
        {
            float time = clip.length * sample / ReachSamples;
            probe.SetTime(time);
            graph.Evaluate(0f);
            Vector3 middle = space.InverseTransformPoint(hips.position);
            float left = Vector3.Dot(space.InverseTransformPoint(leftHand.position) - middle, ahead);
            float right = Vector3.Dot(space.InverseTransformPoint(rightHand.position) - middle, ahead);
            if (left > leftReach) { leftReach = left; leftAt = time; }
            if (right > rightReach) { rightReach = right; rightAt = time; }
        }
        graph.Disconnect(top, ArmSwingInput);
        probe.Destroy();
        if (hadSwing) graph.Connect(armSwing, 0, top, ArmSwingInput);
        bool usesLeft = leftReach > rightReach;
        float hit = usesLeft ? leftAt : rightAt;
        foreach (var cue in clip.events)
            if (cue.functionName != null && cue.functionName.EndsWith("AttackHit")) { hit = cue.time; break; }
        var answer = (usesLeft, Mathf.Clamp(hit, .05f, clip.length));
        swingHands[clip] = answer;
        return answer;
    }
}
