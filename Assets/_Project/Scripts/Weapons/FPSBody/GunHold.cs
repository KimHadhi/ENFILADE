using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

/// <summary>
/// How a kind of gun is held by a body: where the gun is on that body. The answer is made from the body itself (HoldBody:
/// its shoulder pocket, its eyes, its arms) and the gun itself (GunHandles: its butt, its grips), so it holds for any body
/// and any gun, and it is the one place that says it.
/// Until 0.9.111 nothing said where a body holds a gun. The place was KINEMATION's camera framing, made for arms without a
/// body, carried onto the reference body by the bake and from its eyes onto every other, and then moved by rules stacked on
/// it as each character or gun showed a new break: toward the shoulders at most 6 cm, the support hand back at most 10 cm,
/// a long gun down 2 to 15 cm, the trigger arm folded no tighter than 0.4, one body's numbers for all (the user, 2026-11-15:
/// "근본적으로 처음에 어렵지만 기반을 잘 다져 두는 방식이어야 앞으로 고생을 덜하는데"; asked how: "OOP스럽게").
/// A kind of hold is a class: ShoulderedHold (a gun with a stock: the butt on the front of the body's own shoulder) and
/// HandgunHold (held out before the eyes). <see cref="For"/> picks by the gun's own shape. Another way of holding (a launcher on the
/// shoulder, a gun slung at the hip) is another class and nothing else changes.
/// Every number here is a thing about holding a gun with a name of its own (Rules), from Tables/FPSBody.csv, not a limit on
/// a correction.
/// </summary>
internal abstract class GunHold
{
    /// <summary>What holding a gun comfortably means, as shares of an arm's own length and metres on the gun.</summary>
    internal struct Rules
    {
        /// <summary>No arm holds a gun straighter than this (shoulder joint to wrist over the arm's length).</summary>
        public float ReachMost;
        /// <summary>Nor folded tighter than this: past it the forearm lies on the upper arm.</summary>
        public float BendLeast;
        /// <summary>How far a stock's butt presses into the shoulder when an arm would be too straight (m): the flesh and
        /// what is worn there give this much to a gun held firmly in.</summary>
        public float StockPress;
        /// <summary>The support hand holds the gun no nearer the trigger hand than this (m): the magazine and the trigger
        /// guard are between them.</summary>
        public float SupportNearest;
        /// <summary>Holding a stocked gun ready, the trigger wrist is at least this far under its shoulder joint (m): a hand
        /// is not kept up by the face.</summary>
        public float WristUnderShoulder;
    }

    /// <summary>A gun as one body holds it.</summary>
    internal struct Pose
    {
        /// <summary>KINEMATION's weapon bone in the body's model space: where FPSAF's Pose Sampler starts the gun.</summary>
        public KTransform Gun;
        /// <summary>How far back along the gun the support palm holds it from where KINEMATION's does (m).</summary>
        public float Slide;
        /// <summary>How far the gun came along its barrel for the arms (m): toward the body over zero, away from it under.</summary>
        public float Fit;
        /// <summary>How far under the shoulder pocket a stock's butt rests, down the chest (m).</summary>
        public float Lowered;
        /// <summary>How straight each arm then is.</summary>
        public float Right, Left;
    }

    protected readonly GunHandles gun;
    protected readonly Rules rules;

    protected GunHold(GunHandles gun, in Rules rules)
    {
        this.gun = gun;
        this.rules = rules;
    }

    /// <summary>The hold a gun takes, by its shape: to the shoulder when its back end reaches behind the trigger hand as a
    /// stock does, else held out before the eyes.</summary>
    internal static GunHold For(GunHandles gun, in Rules rules)
        => gun == null ? null : gun.StockBehindGrip >= GunHandles.StockReach ? new ShoulderedHold(gun, rules) : new HandgunHold(gun, rules);

    internal GunHandles Gun => gun;

    /// <summary>The kind of hold, for the reports.</summary>
    internal abstract string Name { get; }

    /// <summary>
    /// The point of the gun that stays where it is while its barrel turns onto what the crosshair is on (the gun's frame, m):
    /// what the body holds it by. A stocked gun turns in the shoulder, a handgun in the hand.
    /// </summary>
    internal abstract Vector3 Pivot { get; }

    /// <summary>
    /// The gun on <paramref name="body"/>. <paramref name="clip"/> is where the gun's pose clip alone has it on that body
    /// (KINEMATION's weapon bone in the body's space): what the animators chose that no body decides (the cant of the gun,
    /// where a handgun sits in the view) is kept from it.
    /// </summary>
    internal abstract Pose On(HoldBody body, KTransform clip);

    /// <summary>
    /// How far behind the sight's own eye point the eye rests fully aimed, on <paramref name="body"/> (m; 0: the sight's own).
    /// <paramref name="sightAt"/> and <paramref name="sightTurn"/> are the sight's eye point in the gun's frame.
    /// </summary>
    internal virtual float EyeRelief(HoldBody body, Vector3 sightAt, Quaternion sightTurn) => 0f;

    // The gun's axes with its barrel pointing where the body looks, turned the least way from where the clip has them, so
    // the clip's cant stays. The barrel is on the crosshair whenever the hands are free (GunConvergeLayer), so this is the
    // hold as it is seen and as the arms must reach it; the clips' own idles point the gun up to 14 degrees off it.
    protected Quaternion Levelled(Quaternion axes) => Quaternion.FromToRotation(axes * gun.Barrel, HoldBody.Ahead) * axes;

    /// <summary>The body's wrists with the gun at <paramref name="bone"/> (KINEMATION's weapon bone in the body's space).</summary>
    internal void WristsAt(HoldBody body, KTransform bone, out Vector3 right, out Vector3 left)
        => Wrists(body, bone.position, bone.rotation * gun.Frame, out right, out left);

    /// <summary>
    /// How far the pose clip's own hands are from their places on the gun at <paramref name="bone"/>: each wrist's distance
    /// (model units) and how far its fingers' way is turned from the place's (degrees). FPSAF starts the gun glued to the
    /// clip's right hand, and the hands are then brought to the gun: what they are brought across is this.
    /// </summary>
    internal void ClipHandsOff(HoldBody body, KTransform bone, out float right, out float rightTurn, out float left, out float leftTurn)
    {
        Quaternion axes = bone.rotation * gun.Frame;
        Wrists(body, bone.position, axes, out Vector3 rightWrist, out Vector3 leftWrist);
        right = Vector3.Distance(body.Right.PosedWrist, rightWrist);
        left = Vector3.Distance(body.Left.PosedWrist, leftWrist);
        rightTurn = Vector3.Angle(body.Right.PosedFingers, axes * gun.RightFingers);
        leftTurn = Vector3.Angle(body.Left.PosedFingers, axes * gun.LeftFingers);
    }

    // The body's wrists for its palms on the gun at a place: each palm's middle where KINEMATION's is on the gun, the wrist
    // that hand's own run back from it.
    protected void Wrists(HoldBody body, Vector3 at, Quaternion axes, out Vector3 right, out Vector3 left)
    {
        right = body.Right.Wrist(at + axes * (gun.RightPalm / body.Scale), axes * gun.RightFingers);
        left = body.Left.Wrist(at + axes * (gun.LeftPalm / body.Scale), axes * gun.LeftFingers);
    }

    // KINEMATION's weapon bone for the gun at a place with these axes.
    protected KTransform Bone(Vector3 at, Quaternion axes) => new KTransform(at, axes * Quaternion.Inverse(gun.Frame), Vector3.one);
}
