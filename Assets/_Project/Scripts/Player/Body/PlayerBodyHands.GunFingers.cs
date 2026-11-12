using UnityEngine;

/// <summary>
/// The hands' fingers on the gun. Wherever the palms follow KINEMATION's own clips (FPSBody.Contacts: the plain hold, a
/// reload, a draw, a look over the gun), the fingers take that clip's grip too (FPSBody.HandDigits, HandGrip.WearDigits):
/// each finger joint points as KINEMATION's hand's does in the hand's own frame, so the trigger finger lies along the
/// trigger guard and the fingers wrap the grip as the pack's animators made them, on any humanoid's own fingers, in the
/// plain hold exactly as through a reload (with Rig.stillHoldFixes, held still, a finger a smaller hand would sink into
/// the gun opens until it rests on the gun's real surface, GunSurface). Our shapes
/// (HandGrip.Shape) wrapped every gun alike, about 20 degrees per joint off the clips' grip on average and up to 38 on a
/// gun (the Mk14 EBR's support hand), measured 2026-11-12.
/// The shapes stay underneath and wherever the clips' fingers don't reach: the shooting hand round the pistol grip with
/// the index finger on the trigger, the support hand cradling the handguard or pump, round a vertical grip or against an
/// angled grip the gun wears, or under a pistol's grip hand, and a hand off its grip mid-reload loosely closed. They are set
/// angles inside a hand's reach on any humanoid's own fingers; the clips' finger curves through Unity's muscles broke some
/// hands (user, 2026-11-09: "손가락이 자꾸 문제가 생기는 거 보니 … 우리 식으로"), the joints' own ways do not. A palm on our
/// shapes left inside the gun is still pushed back out along the way it faces until its skin rests on the gun's surface;
/// a palm on the clips' contacts is where KINEMATION's palm is and stays there. Each hand holds a shape by how far it is
/// on its grip (FPSBody.GripHold).
/// A gun may carry its own shape for either hand (GunGrips, set with the Grip Tuner): it wins over the clips' fingers, for
/// the shooting hand always, for the support hand while it is on the gun's own body (a grip attachment keeps its shape).
/// </summary>
public sealed partial class PlayerBodyHands
{
    [SerializeField, Range(0f, 1f), Tooltip("How far the hands wear our gun-holding finger shapes (1) instead of the clips' fingers (0).")]
    private float gunFingerWrap = 1f;

    private readonly float[] gunWrap = new float[2];
    private readonly float[] gunHold = { 1f, 1f };
    private readonly HandGrip.Shape[] gunShape = { HandGrip.Shape.Cradle, HandGrip.Shape.Trigger };
    // The gun's own shape per hand (GunGrips), or null for the shared one; kept like gunShape through the ease out.
    private readonly GunGrips.Grip[] gunCustom = new GunGrips.Grip[2];
    private WeaponData namedGun;
    private string gunName;

    // The held gun's table name, read once per gun (Object.name makes a new string on every call).
    private string GunName(WeaponData gun)
    {
        if (gun != namedGun)
        {
            namedGun = gun;
            gunName = gun != null ? gun.name : null;
        }
        return gunName;
    }

    private void CurlGunFingers(ArmIK arm, bool isLeft, float dt)
    {
        int side = isLeft ? 0 : 1;
        var gunBody = FPSBody.Holding(weapons);
        // How far this hand is on its grip and the shape it holds it with, kept through the ease out once the gun is gone.
        // A support hand on a grip the gun wears is away from the bake's place on purpose, and holds on as far as it is
        // on that grip (GunGrip).
        Vector3[] digits = null;
        float clipFingers = 0f;
        bool still = false;
        if (gunBody != null)
        {
            gunHold[side] = isLeft ? Mathf.Max(gunBody.GripHold(true), OnAttachedGrip) : gunBody.GripHold(false);
            gunShape[side] = ShapeFor(gunBody, isLeft);
            string gun = GunName(weapons != null ? weapons.EquippedData : null);
            bool ownBody = !isLeft || gunBody.HeldGripKind == GunAttachments.Kind.None;
            gunCustom[side] = gun != null && ownBody && GunGrips.TryGet(gun, isLeft, out var custom) ? custom : null;
            // KINEMATION's own fingers from the clip the palms follow (FPSBody.HandDigits), unless the gun has a shape of
            // its own for this hand (GunGrips, the Grip Tuner's) or the support hand is round a grip the gun wears.
            if (gunCustom[side] == null && gunBody.HandDigits(isLeft, out digits, out float digitWeight, out still))
                clipFingers = digitWeight * (isLeft ? 1f - OnAttachedGrip : 1f);
        }
        gunWrap[side] = Mathf.MoveTowards(gunWrap[side], gunBody != null ? 1f : 0f, dt / blendTime);
        float weight = gunWrap[side] * weights[(int)Hold.Gun] * (isLeft ? (1f - leftOffGun) * OffHandKept : 1f) * gunFingerWrap;
        if (arm == null || weight <= 0f) return;

        var surface = gunBody != null ? gunBody.HeldSurface : null;
        // The push leaves the share of the hand the clips' contacts placed (HoldGunContacts) alone, as it did through a
        // reload: a body other than the bakes' counted its hands on the grip only once no reload was going (GripHold), so the
        // push came on just as a reload ended and moved the palms off KINEMATION's place (user, 2026-11-12).
        float onClips = gunBody != null && !gunBody.StillHoldFixes ? contactShare[side] * (isLeft ? 1f - OnAttachedGrip : 1f) : 0f;
        if (surface != null && !surface.Empty) PalmOutOfGun(arm, surface, weight * gunHold[side] * (1f - onClips));
        if (gunCustom[side] != null) arm.WearShape(gunCustom[side], HandGrip.Shape.Grab, gunHold[side], weight);
        else if (clipFingers < .999f) arm.WearShape(gunShape[side], HandGrip.Shape.Grab, gunHold[side], weight);
        if (clipFingers <= 0f || digits == null) return;
        // Held still, the fingers rest on the gun's surface rather than sink into it (a smaller hand than KINEMATION's
        // closes its fingers inside the grip at the clip's angles); worked out once while the hand stays put.
        arm.WearDigits(digits, weight * clipFingers, still ? surface : null, gunBody.FingerClearance, gunBody.FingerOpenMost);
    }

    // The shape a hand holds this gun with: the shooting hand always round the pistol grip; the support hand by what it
    // is on.
    private static HandGrip.Shape ShapeFor(FPSBody gunBody, bool isLeft)
    {
        if (!isLeft) return HandGrip.Shape.Trigger;
        switch (gunBody.HeldGripKind)
        {
            case GunAttachments.Kind.VerticalGrip: return HandGrip.Shape.Fist;
            case GunAttachments.Kind.AngledGrip: return HandGrip.Shape.Angled;
        }
        return gunBody.HeldPistol ? HandGrip.Shape.Cup : HandGrip.Shape.Cradle;
    }

    // Six points on this palm's skin (heel and knuckle rows, both edges and the middle), each looked for from behind the
    // hand along the way the palm faces: where the gun's face comes before the skin, the hand is in the gun by that much,
    // and the whole hand goes back out by the deepest. Lengths are this hand's own (palm length, width, depth).
    private static void PalmOutOfGun(ArmIK arm, GunSurface surface, float weight)
    {
        Vector3 facing = arm.PalmFacing.normalized, forward = arm.FingersForward.normalized;
        Vector3 across = Vector3.Cross(facing, forward).normalized;
        Vector3 skin = arm.PalmPosition + facing * arm.PalmDepth;
        float back = arm.PalmLength;
        float depth = 0f;
        for (int i = 0; i < 6; i++)
        {
            Vector3 point = skin + forward * (arm.PalmLength * (i < 3 ? -.25f : .35f)) + across * (arm.HalfPalmWidth * .8f * (i % 3 - 1));
            if (surface.Raycast(point - facing * back, facing, back, out float distance))
                depth = Mathf.Max(depth, back - distance);
        }
        if (depth <= 0f) return;
        depth = Mathf.Min(depth, arm.PalmLength * .5f);
        Transform hand = arm.Hand;
        arm.Solve(hand.position - facing * depth, hand.rotation, arm.Elbow, weight, 1f, 0f, soft: true);
    }
}
