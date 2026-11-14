using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

/// <summary>
/// FPSBody: each gun's hold made for this body. Where a gun is on a body is GunHold's to say (ShoulderedHold: the butt on the
/// front of the body's own shoulder; HandgunHold: before the eyes at the arms' own bend), from the body's own measure
/// (HoldBody: its bones and its surface) and the gun's own points (GunHandles). This part only asks for it as a gun is built and hands the answer on: to FPSAF's
/// Pose Sampler (where the gun starts), to the gun (what it turns about onto the crosshair, where its support hand and its
/// grips sit, where the eye rests behind its sight) and, in the editor, to Logs/gun-hold-report.txt once a session.
/// It replaces the chain this file was until 0.9.111: the bake's place carried over from the eyes, then a long gun lowered
/// (HipDrop), the gun pulled toward the shoulders (FitToReach), every body given the reference body's numbers (SharedPlan).
/// Each was a rule over a model that never said where a body holds a gun; the user met each new break by playing (the user,
/// 2026-11-12: "팔이 이미 최대로 스트레치된 상황이라"; 11-14: "나 샷건을 왜 이따구로 듦?", "캐릭터마다 총 드는 게 달라서
/// 고장나는데"; 11-15: "기반을 잘 다져 두는 방식이어야 앞으로 고생을 덜하는데"). ENFILADE → 총 → 쥐기 점검 measures every
/// character with every gun through the same classes without playing (GunHoldProbe).
/// </summary>
public sealed partial class FPSBody
{
    [SerializeField, Range(.5f, 1f), Tooltip("No arm holds a gun straighter than this (shoulder joint to wrist over the arm's own length).")]
    private float reachMost = .9f;
    [SerializeField, Range(0f, .8f), Tooltip("Nor folded tighter than this: past it the forearm lies on the upper arm.")]
    private float reachLeast = .4f;
    [SerializeField, Min(0f), Tooltip("How far a stock's butt presses into the shoulder when an arm would be too straight (m in game): the "
        + "flesh and what is worn there give this much to a gun held firmly in.")]
    private float stockPress = .025f;
    [SerializeField, Min(0f), Tooltip("The support hand holds a gun no nearer the trigger hand than this (m in game): the magazine and "
        + "the trigger guard are between them.")]
    private float supportNearest = .14f;
    [SerializeField, Min(0f), Tooltip("Holding a stocked gun ready, the trigger wrist is at least this far under its shoulder joint (m in "
        + "game): a gun whose grip would hold the hand higher has its butt lower on the chest.")]
    private float wristUnderShoulder = .02f;
    [SerializeField, Min(.01f), Tooltip("Behind the plain hold's place on the handguard the support hand's slide fades out over this far, so a "
        + "reload's hand on the magazine keeps its clip's place (m in game; at least twice the slide).")]
    private float reachSlideFade = .1f;

    // Each body and gun's hold, noted once a session.
    private static readonly HashSet<(string skin, WeaponData gun)> notedHolds = new();
    // This body itself round its right shoulder, where a stock rests: read once and kept between its guns, read again when
    // the body is another or wears something else (BodyFront.Reads).
    private BodyFront shoulder;

    // This body's hold of one gun, as building the gun needs it: where FPSAF's Pose Sampler starts it (KINEMATION's weapon
    // bone in the model's space), the hold and the body it was made for, the support palm's slide (m in game) from where
    // along the gun the plain hold has it, and the gun from the eyes (m in game, the IK weapon bone as the camera sees it
    // at rest) for a first draw to come up into.
    private struct HoldFit
    {
        public KTransform Start;
        public GunHold Hold;
        public HoldBody Body;
        public float Slide, SlideAt;
        public bool Seeded;
        public Vector3 FromEyes;
        public Quaternion TurnFromEyes;
    }

    private GunHold.Rules HoldRules => new GunHold.Rules
    {
        ReachMost = reachMost, BendLeast = reachLeast, StockPress = stockPress, SupportNearest = supportNearest,
        WristUnderShoulder = wristUnderShoulder
    };

    /// <summary>The same rules as the table has them (Tables/FPSBody.csv, row Rig), for the editor's hold check.</summary>
    internal static GunHold.Rules TableRules() => new GunHold.Rules
    {
        ReachMost = GameTables.Number(TableName, TableRow, nameof(reachMost), .9f),
        BendLeast = GameTables.Number(TableName, TableRow, nameof(reachLeast), .4f),
        StockPress = GameTables.Number(TableName, TableRow, nameof(stockPress), .025f),
        SupportNearest = GameTables.Number(TableName, TableRow, nameof(supportNearest), .14f),
        WristUnderShoulder = GameTables.Number(TableName, TableRow, nameof(wristUnderShoulder), .02f)
    };

    // The hold of <paramref name="entry"/>'s gun on this body. <paramref name="own"/>: the gun's clips were made on this
    // body. <paramref name="gunModel"/> is this body's copy of the gun (its own space is the gun's frame) and
    // <paramref name="aimPoint"/> its sight's. A body that can't be measured, or a gun whose pose can't be read, keeps the
    // gun where its clip alone has it.
    private HoldFit HoldFor(in FPSBodyAssets.Gun entry, in GunBakes.Entry bake, bool own, Quaternion gunFrame, Transform gunModel, Transform aimPoint)
    {
        var fit = new HoldFit
        {
            Start = new KTransform(bake.gunPosition, bake.gunRotation, Vector3.one),
            Seeded = bake.hasEyeFrame,
            FromEyes = bake.gunFromEyes,
            TurnFromEyes = bake.gunTurnFromEyes * gunFrame
        };
        if (animator != null && (shoulder == null || !shoulder.Reads(animator.transform))) shoulder = HoldBody.ShoulderOf(animator);
        var holder = HoldBody.Measure(animator, bake.pose, shoulder);
        if (holder == null) return fit;
        var clip = holder.PlaceOf(bake, own);
        var hold = GunHold.For(GunHandles.Of(entry, assets != null ? assets.armsModel : null, gunFrame, gunModel, aimPoint, gunArms), HoldRules);
        var pose = hold != null ? hold.On(holder, clip) : new GunHold.Pose { Gun = clip };
        fit.Start = pose.Gun;
        fit.Hold = hold;
        fit.Body = holder;
        fit.Slide = pose.Slide;
        fit.SlideAt = hold != null ? hold.Gun.LeftPalm.z : 0f;
        // From the eyes in the body's own axes, which are the camera's at rest; the IK weapon bone carries the gun's frame.
        fit.Seeded = true;
        fit.FromEyes = holder.FromEyes(pose.Gun.position);
        fit.TurnFromEyes = pose.Gun.rotation * gunFrame;
        if (hold != null) NoteHold(entry.weapon, hold, holder, clip, pose);
        return fit;
    }

    private void NoteHold(WeaponData weapon, GunHold hold, HoldBody holder, KTransform clip, in GunHold.Pose pose)
    {
        if (!Application.isEditor || weapon == null) return;
        string skin = body != null && body.Skin != null ? body.Skin.Id : "?";
        if (!notedHolds.Add((skin, weapon))) return;
        hold.WristsAt(holder, clip, out Vector3 right, out Vector3 left);
        Vector3 at = holder.FromEyes(pose.Gun.position) * 100f;
        PlayerBodyHands.NoteGunHold($"{System.DateTime.Now:HH:mm:ss} {skin} {weapon.name} held {hold.Name}: gun ({at.x:0.0}, {at.y:0.0}, {at.z:0.0}) cm "
            + $"from the eyes; as its clip alone has it the arms would be straight right {holder.Right.Straight(right):0.00} left "
            + $"{holder.Left.Straight(left):0.00}, held so right {pose.Right:0.00} left {pose.Left:0.00}; {pose.Fit * 100f:0.0} cm along the barrel "
            + $"toward the body for the arms (a stock's butt pressed that far into the shoulder), support palm {pose.Slide * 100f:0.0} cm back on the gun, butt {pose.Lowered * 100f:0.0} cm under the "
            + $"shoulder pocket (on {holder.PocketOn})\n");
    }
}
