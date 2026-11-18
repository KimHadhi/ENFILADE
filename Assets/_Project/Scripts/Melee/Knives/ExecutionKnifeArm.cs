using UnityEngine;

/// <summary>
/// The body's arms through an execution's knife stroke (ExecutionKnife drives the stroke, KnifeStroke holds its path):
/// the right hand draws the class's knife from the hip, raises it, drives it down into the victim's neck and puts it away,
/// while the left takes hold of the victim's head; the knife rides in the right fist as ClassKnives seats it. Both wrists
/// follow the path from their own shoulders in their own arm's lengths, as RepairArm's loops do, and are drawn toward the
/// victim (the neck, the head) by each key's aim, so the blade meets the neck whatever body holds it. The same on every PC:
/// each copy's ExecutionKnife plays the stroke its owner's PC sent. The gun is put away meanwhile (WeaponController reads
/// ExecutionKnife.HoldsHands), so both arms are free. After the hands, the gun and FPS Animation Framework (GunFramework
/// + 5), before the off-hand uses, which take the left arm when they run (none does while executing), and before the
/// cloth, so a sleeve follows. Rig-agnostic: any humanoid a modder brings.
/// With no execution under way the same knife may be out in the melee slot (MeleeKnife): then the right arm takes the
/// knife's swing or guard clip from the eyes (KnifeSwing, the Blender knife workspace's) over the fists' pose, and the
/// knife rides the fist in its fighting grip (forward or reverse, ClassKnives melee*), and where its blade then is, is
/// what the swing cuts with (told to PlayerMeleeCombat each frame). An execution comes first; a knife
/// with a draw of its own (ExecutionKnife.DrawCurrent) is drawn with that clip the same way before the stroke or the pair
/// takes the right arm back, its seat turning from the fighting grip to the execution's as it does.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunFramework + 5)]
public sealed class ExecutionKnifeArm : MonoBehaviour
{
    // Where the wrists aim (m): the right fist over the back of the neck by this plus a share of the knife's reach (so a
    // long blade goes in deep and a short one still meets it), the left hand on top of the head and a little toward the
    // player, as a hand grips a head to hold it.
    private const float NeckBack = .07f, FistOverNeck = .05f, ReachShareOverNeck = .3f, MostOverNeck = .23f, HeadAbove = .1f, HeadBack = .07f;
    // The arms change hands with a paired execution clip (ExecutionKnife.ArmsPosedElsewhere) over these (s), as the clip
    // blends in and out (Executions.csv blendIn, blendOut): from the drawn knife's pose into the clip's as the pair begins,
    // and back to the stroke's should the pair end first (cut short), never a jump either way.
    private const float ArmsToClip = .12f, ArmsFromClip = .25f;

    private PlayerMovement movement;
    private PlayerBodyHands hands;
    private ExecutionKnife driver;
    private MeleeKnife melee;
    private Animator animator;
    private Transform eyes, holder;
    private readonly ArmBones right = new ArmBones(true), left = new ArmBones();
    private bool bound;
    private GameObject model;
    private string worn;
    // The knife's own shape from its model's markers (KnifeSeat): its ways, and where its blade starts and ends.
    private KnifeSeat.Shape shape = KnifeSeat.Shape.Plain;
    private float reach = .2f;
    // How much of the arms the stroke poses now (1) or leaves to a paired clip (0), and whether a stroke posed last frame.
    private float armsShare = 1f;
    private bool stroking;
    // The finger joints each pose brings (FingerWays): the stroke's two hands, the knife's own draw, the melee clip.
    private readonly FingerWays rightWays = new FingerWays(), leftWays = new FingerWays(), drawWays = new FingerWays(),
        meleeWays = new FingerWays();

    /// <summary>A player's knife arms, made on their body's model the first time; null without a humanoid body.</summary>
    internal static ExecutionKnifeArm Of(PlayerMovement who)
    {
        if (who == null || !who.TryGetComponent(out PlayerBody body)) return null;
        var bodyModel = body.BodyAnimator;
        if (bodyModel == null || !bodyModel.isHuman) return null;
        return bodyModel.TryGetComponent(out ExecutionKnifeArm arm) ? arm : bodyModel.gameObject.AddComponent<ExecutionKnifeArm>();
    }

    /// <summary>
    /// This knife's model in the right fist's holder, made once per model and kept hidden until a stroke holds it: the
    /// class's knife is put on before its first execution, so drawing it never waits on loading it.
    /// </summary>
    internal void Wear(ClassKnives.Knife knife)
    {
        if (knife == null || !Bind()) return;
        if (holder == null)
        {
            holder = new GameObject("Execution Knife").transform;
            holder.SetParent(transform, false);
            holder.gameObject.SetActive(false);
        }
        if (model != null && worn == knife.Model) return;
        if (model != null) Destroy(model);
        model = ClassKnives.Make(knife);
        worn = knife.Model;
        shape = KnifeSeat.Measure(model != null ? model.transform : null, knife.Scale);
        reach = shape.Tip.magnitude;
        if (model == null) return;
        var root = model.transform;
        root.SetParent(holder, false);
        // The model's own ways come from its markers (KnifeSeat.Measure, taken above while the model still stood as its
        // prefab does): it sits in the holder with its blade along the holder's +z and its edge along +y.
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.Inverse(Quaternion.LookRotation(shape.Blade, shape.Edge));
        root.localScale = Vector3.one * knife.Scale;
        // Drawn on the body's own layer, so every view sees it as it sees the body.
        var skin = GetComponentInChildren<SkinnedMeshRenderer>();
        int layer = skin != null ? skin.gameObject.layer : gameObject.layer;
        foreach (var part in model.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;
    }

    private void LateUpdate()
    {
        if (!Bind()) return;
        if (driver == null) driver = movement.GetComponent<ExecutionKnife>();
        if (eyes == null) eyes = PlayerView.EyesOf(movement);
        if (driver == null || eyes == null || !driver.Current(out var key, out var aim, out var knife, rightWays, leftWays))
        {
            stroking = false;
            // No execution: the knife may be out in the melee slot instead (MeleeKnife).
            if (eyes == null || !HoldForMelee()) ShowKnife(false);
            return;
        }
        if (knife != null && worn != knife.Model) Wear(knife);
        Quaternion view = eyes.rotation;
        Vector3 toPlayer = aim.Has ? Vector3.ProjectOnPlane(eyes.position - aim.Neck, Vector3.up) : Vector3.zero;
        toPlayer = toPlayer.sqrMagnitude > 1e-6f ? toPlayer.normalized : -(view * Vector3.forward);
        Vector3 neckAim = aim.Neck + toPlayer * NeckBack + Vector3.up * Mathf.Min(MostOverNeck, FistOverNeck + reach * ReachShareOverNeck);
        Vector3 headAim = aim.Head + toPlayer * HeadBack + Vector3.up * HeadAbove;
        // A paired execution clip may have the arms (ExecutionKnife.ArmsPosedElsewhere): then the knife only rides the fist.
        // The arms change hands eased; a stroke begins as the switch has it.
        float wantArms = driver.ArmsPosedElsewhere ? 0f : 1f;
        armsShare = stroking
            ? Mathf.MoveTowards(armsShare, wantArms, Time.deltaTime / (wantArms < armsShare ? ArmsToClip : ArmsFromClip))
            : wantArms;
        stroking = true;
        if (armsShare > 0f)
        {
            Pose(left, key.Left, view, aim.Has, headAim, armsShare, leftWays);
            Pose(right, key.Right, view, aim.Has, neckAim, armsShare, rightWays);
        }
        // The knife's own draw (its addon's) has the right arm first, over the stroke's or a paired clip's pose, and hands
        // it back before the blade goes in; meanwhile the knife sits in its fighting grip (the draw's own) and turns to the
        // execution's grip as the draw lets go.
        var drawKey = default(KnifeSwing.Key);
        float drawn = 0f;
        if (knife != null && driver.DrawCurrent(out drawKey, out float drawWeight, drawWays)) drawn = drawWeight;
        if (drawn > 0f) PoseFromEyes(right, drawKey, eyes, drawn, drawWays);
        bool held = model != null && (drawn > .5f ? drawKey.Held : key.Held && key.Right.Weight > 0f);
        if (held && drawn > 0f)
            Seat(Vector3.Lerp(knife.Held, knife.MeleeHeld, drawn), Vector3.Slerp(knife.Blade, knife.MeleeBlade, drawn),
                Vector3.Slerp(knife.Edge, knife.MeleeEdge, drawn));
        else if (held) Seat(knife.Held, knife.Blade, knife.Edge);
        ShowKnife(held);
    }

    // The class knife out in the melee slot (MeleeKnife): the right arm takes its swing or its guard from the eyes, by how
    // much of the arm the knife has (the fists' block and a run take it back), and the knife rides the fist in its
    // fighting grip (ClassKnives melee*). False when it is not out.
    private bool HoldForMelee()
    {
        if (melee == null) melee = movement.GetComponent<MeleeKnife>();
        if (melee == null || !melee.Current(out var key, out float weight, out var knife, meleeWays) || knife == null) return false;
        if (worn != knife.Model) Wear(knife);
        if (weight > 0f) PoseFromEyes(right, key, eyes, weight, meleeWays);
        bool held = key.Held && model != null;
        if (held) Seat(knife.MeleeHeld, knife.MeleeBlade, knife.MeleeEdge);
        ShowKnife(held);
        // Where the blade is as this frame shows it, for the swing's cut (PlayerMeleeCombat.BladePosed, BladeTrace): the
        // knife's own from where its blade starts to its tip, or the fist's own length with no model to hold.
        var combat = movement.MeleeCombat;
        if (combat != null && held && holder != null)
            combat.BladePosed(holder.position + holder.rotation * shape.Base, holder.position + holder.rotation * shape.Tip);
        else if (combat != null && model == null)
            combat.BladePosed(right.Hand.position, right.Hand.position + right.AlongNow() * (right.HandLength * 2f));
        return true;
    }

    // The arm where an eye-space key has it (KnifeSwing: the wrist and the elbow from the eyes in metres, the hand's ways in
    // the view's axes), its fingers as the clip has every joint (else closed by its curls), eased by weight from the pose
    // the frame had.
    private void PoseFromEyes(ArmBones side, in KnifeSwing.Key key, Transform eyes, float weight, FingerWays fingers)
    {
        side.Keep();
        Quaternion view = eyes.rotation;
        TwoBoneIK.Solve(side.Upper, side.Lower, side.Hand, eyes.position + view * key.Wrist, eyes.position + view * key.Elbow);
        Vector3 along = view * key.Along, palm = view * key.Palm;
        side.TurnHand(along, palm);
        Fingers(side, false, fingers, along, palm, key.Curl, key.Thumb);
        side.Ease(weight);
    }

    // A hand's fingers: every joint as the clip has it from Blender, onto this body's own hand (FingerWays through
    // PlayerBodyHands, the gun clips' working); a table from before them (or no hands to wear them) closes by its curls.
    private void Fingers(ArmBones side, bool isLeft, FingerWays fingers, Vector3 along, Vector3 palm, float curl, float thumb)
    {
        if (hands == null && movement != null) hands = movement.GetComponent<PlayerBodyHands>();
        if (hands == null || !hands.WearFingerWays(animator, isLeft, fingers, 1f)) side.Close(along, palm, curl, thumb);
    }

    private void OnDisable()
    {
        stroking = false;
        ShowKnife(false);
    }

    private void OnDestroy()
    {
        if (holder != null) Destroy(holder.gameObject);
    }

    private bool Bind()
    {
        if (bound) return true;
        movement = GetComponentInParent<PlayerMovement>();
        animator = GetComponent<Animator>();
        if (movement == null || animator == null || !animator.isHuman) return false;
        bound = right.Bind(animator) && right.HasLength && left.Bind(animator) && left.HasLength;
        return bound;
    }

    // The arm where the key has it: the wrist from this shoulder in this arm's lengths, drawn toward the aim by the key's
    // share, the elbow toward its pole; the hand turned to the key's run and facing, the fingers closed; all eased by the
    // key's weight, times the stroke's share of the arms, from the pose the frame had.
    private void Pose(ArmBones side, in KnifeStroke.Arm key, Quaternion view, bool aimed, Vector3 aim, float share, FingerWays fingers)
    {
        float weight = key.Weight * share;
        if (weight <= 0f) return;
        side.Keep();
        Vector3 shoulder = side.Upper.position;
        Vector3 wrist = shoulder + view * (key.Wrist * side.ArmLength);
        if (aimed && key.Aim > 0f) wrist = Vector3.Lerp(wrist, aim, key.Aim);
        TwoBoneIK.Solve(side.Upper, side.Lower, side.Hand, wrist, shoulder + view * (key.Elbow * side.ArmLength));
        Vector3 along = view * key.Along, palm = view * key.Palm;
        side.TurnHand(along, palm);
        Fingers(side, side == left, fingers, along, palm, key.Curl, key.Thumb);
        side.Ease(weight);
    }

    // The knife in the right fist as the hand finally is this frame: its handle's middle at the grip's place in the fist,
    // its blade and edge along the grip's ways (ClassKnives' hand axes: x away from the thumb, which on a right hand is the
    // frame's -x). An execution holds it in its reverse grip, the melee slot in the knife's fighting grip.
    private void Seat(Vector3 heldAxes, Vector3 bladeAxes, Vector3 edgeAxes)
    {
        if (holder == null) return;
        Vector3 along = right.AlongNow();
        Vector3 palm = right.PalmNow(along);
        // The seat's working is KnifeSeat's, the same that tells where a clip's blade goes without a body.
        if (!KnifeSeat.Place(right.Hand.position, along, palm, right.HandLength, heldAxes, bladeAxes, edgeAxes, out Vector3 grip,
                out Quaternion turn)) return;
        // A body imported at another scale (an avatar's armature at 100) must not scale the knife with it.
        float scale = Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));
        holder.localScale = Vector3.one / scale;
        holder.SetPositionAndRotation(grip, turn);
    }

    /// <summary>The right hand's length on this body (wrist to middle knuckle, m), and the worn knife's shape: a clip's blade is worked out with them (MeleeKnife).</summary>
    internal float HandLength => bound ? right.HandLength : KnifeSeat.NominalHand;
    internal KnifeSeat.Shape Shape => shape;
    /// <summary>The arm has this knife's model on and measured.</summary>
    internal bool Wears(ClassKnives.Knife knife) => knife != null && model != null && worn == knife.Model;

    private void ShowKnife(bool show)
    {
        if (holder != null && holder.gameObject.activeSelf != show) holder.gameObject.SetActive(show);
    }
}
