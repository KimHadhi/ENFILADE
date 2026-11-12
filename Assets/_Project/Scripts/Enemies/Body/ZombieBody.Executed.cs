using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// A paired execution or takedown on the zombie's body (ExecutionPairPlayer, Tables/Executions.csv): the pair's zombie
/// clip covers everything else on its own top input, and the body's root (Model) stands on the pair's anchor, the root
/// both of the pair's clips share. The clip runs on the pair's clock, which ExecutionPairPlayer sets each frame on every
/// PC, so the player's clip and this one stay together whatever either body's own pace is. Killed at the pair's kill
/// moment, the body leaves as it is, as any corpse does (Fall): from the clip's pose into the ragdoll, its muscles giving
/// out (ZombieCorpse). A zombie that lives through it (a brute takes only a share) eases back into its own clips.
/// Lying knocked down, it comes off its doll into the clip (EndDown already stood the host's up, easing from the doll's
/// pose: EaseFromDoll); the doll's last pose is kept a moment (CaptureDown) so the pair can be laid over where it lay.
/// </summary>
public sealed partial class ZombieBody
{
    // After the getting up from a pose (20); Moods' TopInputs leaves room for it.
    private const int ExecutedInput = 21;

    private AnimationClipPlayable executedClip;
    private bool executed;
    private float executedWeight, executedTime, executedLength, executedIn = .12f, executedOut = .25f, executedSettle = .2f, executedAge;
    private Vector3 executedAt, homePosition;
    private Quaternion executedTurn = Quaternion.identity, homeRotation = Quaternion.identity;
    private Vector3 settleFromPosition;
    private Quaternion settleFromRotation = Quaternion.identity;
    // The doll's pose as an execution stood it up (EndDown), for laying the pair's clip over where it lay.
    private Vector3 downHips, downHead;
    private bool downFaceDown;
    private int downFrame = -100;

    /// <summary>A pair's clip has (or still eases out of) this body.</summary>
    internal bool IsExecuted => executed || executedWeight > 0f;

    /// <summary>Its size against the pack's (a brute is bigger): the pair's offsets grow with it.</summary>
    internal float Grown => grown;

    /// <summary>Its body's root, which the pair's clips share; null once it is a corpse.</summary>
    internal Transform BodyRoot => Model != null ? Model.transform : null;

    /// <summary>One of its humanoid bones (a missing upper chest is its chest, then its spine); null without a body.</summary>
    internal Transform HumanBone(HumanBodyBones bone)
    {
        if (human == null || Model == null) return null;
        var found = human[(int)bone];
        if (found == null && bone == HumanBodyBones.UpperChest) found = human[(int)HumanBodyBones.Chest];
        if (found == null && (bone == HumanBodyBones.UpperChest || bone == HumanBodyBones.Chest)) found = human[(int)HumanBodyBones.Spine];
        if (found == null && bone == HumanBodyBones.Neck) found = human[(int)HumanBodyBones.Head];
        return found;
    }

    // From EndDown, the moment an execution stands it up off its doll: where it lay, for the pair to lie over it.
    private void CaptureDown()
    {
        if (!down || human == null) return;
        var hips = human[(int)HumanBodyBones.Hips];
        var head = human[(int)HumanBodyBones.Head];
        if (hips == null || head == null) return;
        downHips = hips.position;
        downHead = head.position;
        downFaceDown = LiesFaceDown();
        downFrame = Time.frameCount;
    }

    /// <summary>
    /// Which pair fits it now (an Executions.csv row id), for a player at <paramref name="from"/>: lying face down
    /// (a crawler, a calm one on the floor, one knocked down) "prone", on its back "supine", in a calm pose "sit", "feed"
    /// or "crouch", standing "back" (the player behind it) or "front". Lying, <paramref name="hips"/> and
    /// <paramref name="head"/> are where its body lies, for laying the pair over it.
    /// </summary>
    internal string ExecutionState(Vector3 from, out bool lying, out Vector3 hips, out Vector3 head)
    {
        lying = false;
        hips = head = default;
        if (Model == null || human == null) return null;
        var hipsBone = human[(int)HumanBodyBones.Hips];
        var headBone = human[(int)HumanBodyBones.Head];
        if (down && hipsBone != null && headBone != null)
        {
            lying = true;
            hips = hipsBone.position;
            head = headBone.position;
            return LiesFaceDown() ? "prone" : "supine";
        }
        if (Time.frameCount - downFrame <= 2)
        {
            lying = true;
            hips = downHips;
            head = downHead;
            return downFaceDown ? "prone" : "supine";
        }
        if (crawling) return "prone";
        if (pose == PoseState.Held && calmShare > .5f && poseLoop.IsValid())
        {
            var clip = poseLoop.GetAnimationClip();
            string name = clip != null ? clip.name.ToLowerInvariant() : "";
            if (name.Contains("fd_") || name.Contains("crawl") || name.Contains("prone")) return "prone";
            if (name.Contains("sit")) return "sit";
            if (name.Contains("feed") || name.Contains("eat")) return "feed";
            if (name.Contains("crouch")) return "crouch";
            // A modder's pose by what it is busy with, and how low its head is.
            if (headBone != null && headBone.position.y - transform.position.y - feetHeight < .55f * grown) return "prone";
            return poseKind == CalmPoseKind.Eat ? "feed" : poseKind == CalmPoseKind.Doze ? "sit" : "crouch";
        }
        Vector3 toward = Vector3.ProjectOnPlane(from - transform.position, Vector3.up);
        return toward.sqrMagnitude < 1e-4f || Vector3.Angle(-transform.forward, toward) <= 90f ? "back" : "front";
    }

    /// <summary>
    /// The pair's zombie clip takes the body: over blendIn, its root moving onto the anchor over settle, then held there;
    /// its time is the pair's (SetExecutedTime). False when there is no body or graph to play it on.
    /// </summary>
    internal bool BeginExecuted(AnimationClip clip, Vector3 anchor, Quaternion turn, float length, float blendIn, float blendOut, float settle)
    {
        if (clip == null || Model == null || !graph.IsValid() || !top.IsValid() || top.GetInputCount() <= ExecutedInput) return false;
        // Still on its doll here (another PC's copy, its host's word not in yet): off it into the clip, easing from it.
        if (down)
        {
            HoldDollPose();
            GiveDollBack();
            rising = false;
            riseWeight = 0f;
            Stand();
        }
        StopJolt();
        if (executedClip.IsValid())
        {
            graph.Disconnect(top, ExecutedInput);
            executedClip.Destroy();
        }
        else
        {
            homePosition = Model.transform.localPosition;
            homeRotation = Model.transform.localRotation;
        }
        executedClip = AnimationClipPlayable.Create(graph, clip);
        executedClip.SetApplyFootIK(false);
        executedClip.SetSpeed(0d);
        graph.Connect(executedClip, 0, top, ExecutedInput);
        executed = true;
        executedAt = anchor;
        executedTurn = turn;
        executedLength = Mathf.Max(.05f, Mathf.Min(length, clip.length));
        executedIn = Mathf.Max(.01f, blendIn);
        executedOut = Mathf.Max(.01f, blendOut);
        executedSettle = Mathf.Max(.01f, settle);
        executedTime = executedAge = 0f;
        settleFromPosition = Model.transform.position;
        settleFromRotation = Model.transform.rotation;
        return true;
    }

    /// <summary>The pair's clock, set each frame by ExecutionPairPlayer on every PC.</summary>
    internal void SetExecutedTime(float time) => executedTime = time;

    /// <summary>The pair is over (or was cut short): the body eases back into its own clips, its root back home.</summary>
    internal void EndExecuted() => executed = false;

    // From Update, before Weigh: the clip at the pair's time, the root on the anchor.
    private void ExecutedTick(float real)
    {
        if (!executedClip.IsValid() || Model == null) return;
        executedAge += real;
        executedWeight = Mathf.MoveTowards(executedWeight, executed ? 1f : 0f, real / (executed ? executedIn : executedOut));
        executedClip.SetTime(Mathf.Clamp(executedTime, 0f, executedLength));
        var root = Model.transform;
        if (executed)
        {
            float t = Mathf.Clamp01(executedAge / executedSettle);
            t = t * t * (3f - 2f * t);
            root.SetPositionAndRotation(Vector3.Lerp(settleFromPosition, executedAt, t), Quaternion.Slerp(settleFromRotation, executedTurn, t));
            return;
        }
        // Easing out: the root goes home under the enemy as the body's own clips come back.
        float back = 1f - executedWeight;
        root.localPosition = Vector3.Lerp(root.localPosition, homePosition, back);
        root.localRotation = Quaternion.Slerp(root.localRotation, homeRotation, back);
        if (executedWeight > 0f) return;
        root.localPosition = homePosition;
        root.localRotation = homeRotation;
        graph.Disconnect(top, ExecutedInput);
        executedClip.Destroy();
        top.SetInputWeight(ExecutedInput, 0f);
        lastPosition = transform.position;
    }

    // The end of Weigh: the pair's clip over the rest of the body, all of it adding up to one as ever.
    private void WeighExecuted()
    {
        if (!executedClip.IsValid() || executedWeight <= 0f || top.GetInputCount() <= ExecutedInput) return;
        float keep = 1f - executedWeight;
        for (int i = 0; i < top.GetInputCount(); i++)
            if (i != ExecutedInput) top.SetInputWeight(i, top.GetInputWeight(i) * keep);
        top.SetInputWeight(ExecutedInput, executedWeight);
    }
}
