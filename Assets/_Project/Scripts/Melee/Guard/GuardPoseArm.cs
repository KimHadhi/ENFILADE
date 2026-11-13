using UnityEngine;
using static ArmBones;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The guard pose of a player holding a gun (GuardPose): the gun stays in the right hand and the left hand leaves it to
/// knock a blow aside, the forearm swept up across before the face with the fist turned out, held a moment, and back to
/// the gun. The counterpart, gun in hand, of the fists' parry sweep (FistsView.Deflect), for a Knight whose cover turns a
/// blow (the user, 2026-11-12: "특유의 패링 자세와 함께"); the Knight's loadout is a rifle and a pistol.
/// The hand leaves the gun the one shared way for every off-hand use (the manager's decision, 2026-11-12):
/// PlayerBodyHands.LendOffHand, so its gun contact, grip and fingers let go over the hands' own blend time and take the gun
/// again after; the pose eases by how far the hand is lent (OffHandLent), never ahead of it. A throw on G or a reload has
/// the left hand first, and then no pose is struck. It runs after the point and the throw (GunFramework + 8) and before
/// the cloth, so a sleeve follows; the first-person camera was placed earlier, so the view never moves with the arm.
/// The same on every PC: GuardPose strikes it on the owner's body and on the copies. Rig-agnostic: any humanoid a modder
/// brings, from this body's own shoulder and lengths, no clip or bake of its own.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunFramework + 8)]
public sealed class GuardPoseArm : MonoBehaviour
{
    /// <summary>The pose's shape and timing (GuardPose's numbers): eyes space, metres; real seconds; the fist's close in degrees.</summary>
    internal struct Shape
    {
        public float Seconds, Attack, Hold, Curl;
        public Vector3 From, Wrist, Elbow;
    }

    // How far each joint of a closing fist has turned from the hand's run, knuckle first, as a share of the whole close
    // (ArmBones.CurlShare is the same close told joint by joint).
    private static readonly float[] FistShare = { .4f, .75f, 1f };
    // The pose's own ease in and out (real s); the hand's leaving the gun has its own (PlayerBodyHands blendTime).
    private const float PoseIn = .05f, PoseOut = .12f;
    // The block snaps a touch past its place and settles back, as the fists' sweep does.
    private const float Overshoot = 1.08f;

    private PlayerMovement movement;
    private PlayerBodyHands hands;
    private WeaponController weapon;
    private Animator animator;
    private Transform eyes;
    private readonly ArmBones arm = new ArmBones();
    private Shape shape;
    private float elapsed = float.PositiveInfinity, own, weight;
    private bool bound;

    /// <summary>A player's arm for the gun-in-hand guard pose, made on their body's model the first time; null without a humanoid body.</summary>
    internal static GuardPoseArm Of(PlayerMovement who)
    {
        if (who == null || !who.TryGetComponent(out PlayerBody body)) return null;
        var model = body.BodyAnimator;
        if (model == null || !model.isHuman) return null;
        return model.TryGetComponent(out GuardPoseArm arm) ? arm : model.gameObject.AddComponent<GuardPoseArm>();
    }

    /// <summary>Strikes the pose now, in this shape.</summary>
    internal void Strike(Shape pose)
    {
        shape = pose;
        elapsed = 0f;
    }

    private void LateUpdate()
    {
        if (Time.timeScale <= 0f || GamePause.IsPaused) return;
        if (!bound && !Bind()) return;
        if (eyes == null) eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return;
        // Real time, as a parry's sweep: the hit stop doesn't hold it.
        float dt = Mathf.Min(.05f, Time.unscaledDeltaTime);
        if (elapsed < float.PositiveInfinity) elapsed += dt;
        bool on = elapsed < shape.Seconds && Free();
        if (!on && own <= 0f) { if (hands != null) hands.LendOffHand(this, 0f); return; }
        // The left hand off the gun through PlayerBodyHands, as every off-hand use takes it; the same share each frame
        // costs nothing.
        if (hands != null) hands.LendOffHand(this, on ? 1f : 0f);
        own = Mathf.MoveTowards(own, on ? 1f : 0f, dt / (on ? PoseIn : PoseOut));
        weight = hands != null ? Mathf.Min(own, hands.OffHandLent) : own;
        if (weight <= 0f) return;
        Pose();
    }

    private void OnDisable()
    {
        own = weight = 0f;
        elapsed = float.PositiveInfinity;
        if (hands != null) hands.LendOffHand(this, 0f);
    }

    private bool Bind()
    {
        movement = GetComponentInParent<PlayerMovement>();
        hands = movement != null ? movement.GetComponent<PlayerBodyHands>() : null;
        weapon = movement != null ? movement.GetComponent<WeaponController>() : null;
        animator = GetComponent<Animator>();
        if (movement == null || animator == null || !animator.isHuman) return false;
        if (!arm.Bind(animator)) return false;
        bound = true;
        return true;
    }

    // The body can lift the arm (alive, up, standing or crouched, walking too; not held, climbing or vaulting) and the
    // left hand isn't busy with a throw or a reload.
    private bool Free()
    {
        if (movement.IsDead || movement.IsDowned || movement.IsHeld || movement.IsParkour) return false;
        if (PlayerGrenades.HandBusy(movement)) return false;
        if (weapon != null && weapon.Runtime != null && weapon.Runtime.IsReloading) return false;
        var state = movement.CurrentState;
        return state == MovementState.Standing || state == MovementState.Crouching;
    }

    // Up from below and across into the block before the face, the fist turned out to the blow, eased by the weight.
    private void Pose()
    {
        arm.Keep();
        Quaternion view = eyes.rotation;
        Vector3 wrist = eyes.position + view * Vector3.LerpUnclamped(shape.From, shape.Wrist, Reach());
        TwoBoneIK.Solve(arm.Upper, arm.Lower, arm.Hand, wrist, eyes.position + view * shape.Elbow);
        // The fist's run up and in across the face, the back of the hand to the blow.
        Vector3 along = view * new Vector3(.35f, .93f, .1f).normalized, palm = view * Vector3.back;
        arm.TurnHand(along, palm);
        Fist(along, palm, shape.Curl);
        arm.Ease(weight);
    }

    // How far into the block the wrist has come: snapping in over Attack a touch past it, settling over Hold.
    private float Reach()
    {
        float attack = Mathf.Max(.01f, shape.Attack);
        if (elapsed < attack)
        {
            float rest = 1f - elapsed / attack;
            return Overshoot * (1f - rest * rest * rest);
        }
        float held = elapsed - attack;
        return held < shape.Hold ? Mathf.Lerp(Overshoot, 1f, held / Mathf.Max(.01f, shape.Hold)) : 1f;
    }

    // The four fingers closed into the palm, each joint's way turned from the hand's run by its share of the close; the
    // thumb across over them.
    private void Fist(Vector3 along, Vector3 palm, float curl)
    {
        Vector3 axis = Vector3.Cross(along, palm);
        if (axis.sqrMagnitude < 1e-10f || curl <= 0f) return;
        axis.Normalize();
        for (int finger = Index; finger <= Little; finger++)
            for (int joint = 0; joint < 3; joint++)
            {
                var bone = arm.Fingers[finger * 3 + joint];
                if (bone == null) break;
                Turn(bone, arm.JointWay(finger, joint), Quaternion.AngleAxis(curl * FistShare[joint], axis) * along);
            }
        if (arm.Fingers[Thumb * 3] == null) return;
        Vector3 over = (along * .5f + palm * .6f - axis * .45f).normalized;
        for (int joint = 0; joint < 3; joint++)
        {
            var bone = arm.Fingers[Thumb * 3 + joint];
            if (bone == null) break;
            Vector3 bendAxis = Vector3.Cross(over, palm);
            if (joint > 0 && bendAxis.sqrMagnitude > 1e-8f) over = Quaternion.AngleAxis(joint == 1 ? 15f : 20f, bendAxis.normalized) * over;
            Turn(bone, arm.JointWay(Thumb, joint), over);
        }
    }
}
