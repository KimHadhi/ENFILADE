using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

/// <summary>
/// The gun and both hands go where KINEMATION's own clips have them, on any body: the plain hold (standing, aiming,
/// walking, sprinting, crouched), the draw, the holster, the look over the gun and every reload (the user, 2026-11-12: "그대로
/// 가죠" to holding by contact points instead of a hand offset per character; after the reloads came right, "그냥 손 잡는
/// 손이 이상해 … 이것도 제대로 고칠 수 있을 것 같아서").
/// The clips remade for our bodies (GunBakes) carry joint angles only, made on the reference body. On any other body the same
/// angles put the hands somewhere else, a different miss at every moment, so no single offset could fix them: worked out
/// 2026-11-12 from the characters' arms, the wrists of Mayo and Ichigo sat about 3 to 6 cm off KINEMATION's place on the gun
/// in the plain hold; even on the reference body, whose wrists sit exactly there (0.0 cm, its bake), its 5.9 cm palms
/// (KINEMATION's are 9.5 cm) left the middle of the palm 2 cm short; and the fingers were our shared shapes, about 20 degrees
/// per joint off the clips' grip (11 to 38 by gun), the trigger hand's fingertips flattened besides (HandGrip.KeepHuman).
/// - The gun: in the hold it stays where FPSAF holds it (its eyes-fitted pose, sights, sway, recoil). Through a clip of the
///   gun's own, that clip's moment is read from the gun's tracks (GunTrackSet: KINEMATION's clip as numbers, read once in
///   the editor on KINEMATION's own arms); how it moves the gun from where its pose clip holds it, in the gun's own frame,
///   goes onto the gun as this body holds it from the eyes (the gun's own learnt hold, FPSGun.HoldFromEyes), so a draw
///   comes up into the hold and a reload moves the gun as KINEMATION's animators made it. Until 0.9.117 the game played
///   KINEMATION's clip on a hidden copy of those arms every frame to read the same (GunArms): a second player of the clip
///   beside the animation's own, which twice went wrong in builds only (ClipSampler's notes).
/// - The hands: where each palm is on the gun (the pose clip's for the hold, the playing clip's moment for the rest), the way
///   the fingers point and the palm faces, and each finger joint's way (GunArms.Digits). PlayerBodyHands puts this body's
///   palms there by IK (HoldGunContacts) and its fingers in the clip's grip (GunFingers, HandGrip.WearDigits), so a hand on
///   the grip is on the grip and a hand on the magazine is on the magazine whatever the arms' length or the hand's size
///   (palm-centred: ArmIK.WristAtPalm). Tables/GunHands.csv moves a character's palms a little if its hands are thicker.
/// The hold eases in as the gun comes into the hands; a clip eases in with it (contactBlend), eases back into the hold at its
/// end as the clip does, and lets go as fast as a cut-short reload (reloadBlendOut). Only where FPSAF holds the upper body
/// (standing, crouched); in the other moves the hands go onto the gun kept in view by the same hold (PlayerBodyHands.GunGrip).
/// The plain hold wears the hands exactly as a reload's end does, so nothing changes as a reload hands over to it (the user,
/// 2026-11-12: "재장전할 땐 정상이거든? 근데 재장전 끝나자마자 손을 이상하게 잡아"). KINEMATION's clips agree: every reload's last
/// frame has the palms and fingers where the gun's pose clip has them (0.0 cm, 0 degrees, every gun, measured). What changed
/// was ours on top, on only in the still hold: the fingers opened up to fingerOpenMost off the gun's surface, and, on bodies
/// other than the bakes' (Mayo, Ichigo), our shapes' palm push, which counted a hand on its grip only once no reload was
/// going (FPSBody.GripHold). Rig.stillHoldFixes brings both back, for comparing.
/// Rig.holdContacts, Rig.reloadContacts and Rig.contactFingers in Tables/FPSBody.csv switch each part back to the old way.
/// </summary>
public sealed partial class FPSBody
{
    [SerializeField, Tooltip("Reloads hold the gun and put the palms where KINEMATION's own clip has them (the gun's tracks). Off: the remade "
        + "clip alone, with the gun kept in the right hand.")]
    private bool reloadContacts = true;
    [SerializeField, Tooltip("The plain hold (standing, aiming, walking, sprinting), the draw, the holster and the look over the gun "
        + "put the palms where KINEMATION's own clips have them on the gun, as reloads do. Off: FPSAF's hold from the remade pose clip "
        + "and the remade draw, holster and inspect, as before.")]
    private bool holdContacts = true;
    [SerializeField, Tooltip("The fingers take KINEMATION's own clip's grip (GunArms.Digits) wherever the palms follow a clip. Off: our "
        + "shared finger shapes (HandGrip.Shape: Trigger, Cradle, Cup).")]
    private bool contactFingers = true;
    [SerializeField, Min(.01f), Tooltip("How quickly a clip's hold on the gun, or the plain hold's, comes in (s).")]
    private float contactBlend = .15f;
    [SerializeField, Tooltip("Off: the plain hold wears the hands exactly as a reload's end does. On (for comparing): the two passes the "
        + "still hold alone had come back: the fingers open off the gun's surface (fingerOpenMost) and our shapes' palm push moves "
        + "palms that are on the clips' contacts (PlayerBodyHands.GunFingers).")]
    private bool stillHoldFixes;
    [SerializeField, Range(0f, 60f), Tooltip("With stillHoldFixes, holding the gun still, the most a finger joint opens so a smaller "
        + "hand's finger rests on the gun instead of sinking into it (degrees). 0: the clip's angles as they are.")]
    private float fingerOpenMost = 30f;
    [SerializeField, Min(0f), Tooltip("With stillHoldFixes, how far off the gun's surface a finger's skin rests (m).")]
    private float fingerClearance = .002f;
    [SerializeField, Range(0f, 1f), Tooltip("Down the sights, the share of the gun's own motion that stays while its action is worked "
        + "after a shot (a bolt, a pump: FPSGun.Cycle), and of the camera's sway with it: the sight stays before the eye on what "
        + "was shot while the hand works the bolt, as Rainbow Six Siege's guns stay on the sight through their kick. 1: as at the hip.")]
    private float aimedCycleMove = .25f;

    // A pose clip holds the gun in both hands when the support palm is this close to the weapon bone (m): asked once, as the
    // editor reads the guns' actions, and kept with them (GunTrackBake, GunTrackSet.holds).
    internal const float PoseReach = .8f;

    // The gun's own clip followed now (a reload, a draw, a holster, a look over the gun): its moment, and the buffers its
    // finger ways are read into.
    private GunContact contact;
    private readonly Vector3[] clipRightDigits = new Vector3[GunArms.DigitCount], clipLeftDigits = new Vector3[GunArms.DigitCount];
    private readonly Vector3[] mixRightDigits = new Vector3[GunArms.DigitCount], mixLeftDigits = new Vector3[GunArms.DigitCount];
    private float contactWeight;
    private bool hasContact;
    // The followed clip is easing back into the hold at its end (its blend out), not cut short.
    private bool clipSettling;
    private FPSGun contactGun;
    // How far the plain hold's contacts hold the hands (0-1): in as a gun comes into the hands, out while a clip of its own
    // has them without being followed.
    private float holdContactWeight;

    /// <summary>The most a finger joint opens off the gun (degrees) and the skin's rest off it (m), for HandGrip.WearDigits.</summary>
    internal float FingerOpenMost => fingerOpenMost;
    internal float FingerClearance => fingerClearance;
    /// <summary>The still hold's own passes are on (Rig.stillHoldFixes, off unless comparing).</summary>
    internal bool StillHoldFixes => stillHoldFixes;
    /// <summary>A reload's own clip has both hands fully now, before it settles into the hold (for the hand-over report).</summary>
    internal bool OnReloadClip => HoldsGun && hasContact && held.ContactIsReload && contactWeight >= .999f && Framework >= .999f;
    /// <summary>A reload that played out is easing back into the hold now (not one cut short).</summary>
    internal bool ReloadSettling => HoldsGun && hasContact && held.ContactIsReload && clipSettling;

    // After KeepGunInView, before RideGun and the hands (PlayerBodyHands, 320): the gun to where the playing clip has it, and
    // the plain hold's contacts made ready.
    private void FollowContacts()
    {
        if (held != contactGun)
        {
            contactGun = held;
            contactWeight = 0f;
            hasContact = false;
            holdContactWeight = 0f;
        }
        float dt = Time.deltaTime;
        bool playing = false, settling = false;
        // The bolt or pump worked after a shot moves the gun out of the hold as a reload does, so the reload's switch has it.
        if (rig != null && eyes != null && held.ContactTrack(out GunTrack track, out float clipTime, out settling)
            && (held.ContactIsReload || held.ContactIsCycle ? reloadContacts : holdContacts)
            && PoseHolds(held) && track.Sample(clipTime, clipRightDigits, clipLeftDigits, out GunContact now, out _))
        {
            contact = now;
            hasContact = true;
            playing = true;
        }
        clipSettling = playing && settling;
        float target = playing && !settling ? 1f : 0f;
        float ease = target > contactWeight ? contactBlend : settling ? held.ContactSettle : reloadBlendOut;
        contactWeight = Mathf.MoveTowards(contactWeight, target, dt / Mathf.Max(.01f, ease));
        if (contactWeight <= 0f) hasContact = false;

        // The plain hold: from the gun's pose clip, measured once per gun. A clip of the gun's own that isn't followed (its
        // switch off) has the hands as the remade clip has them, so the hold lets go of them meanwhile.
        bool holdOn = holdContacts && rig != null && HoldContactOf(held) && !(held.HandsBusy && !playing);
        holdContactWeight = Mathf.MoveTowards(holdContactWeight, holdOn ? 1f : 0f, dt / Mathf.Max(.01f, holdOn ? contactBlend : reloadBlendOut));

        // Letting go, the last moment read is held while the weight runs out.
        float weight = hasContact ? contactWeight * Framework : 0f;
        if (weight <= 0f || !held.HoldFromEyes(controller.AimBlend, out KTransform kept)) return;
        var gun = rig.IkWeaponBone;
        Quaternion hold = eyes.rotation * kept.rotation;
        Vector3 move = contact.GunMove;
        Quaternion turn = contact.GunTurn;
        // Down the sights a bolt or pump worked after a shot moves the gun only aimedCycleMove of its way off the hold, so the
        // sight stays before the eye while the hand works it (the hands keep their places on the gun, HandContact).
        if (held.ContactIsCycle)
        {
            float keep = Mathf.Lerp(1f, aimedCycleMove, controller.AimBlend);
            move *= keep;
            turn = Quaternion.Slerp(Quaternion.identity, turn, keep);
        }
        Vector3 at = eyes.TransformPoint(kept.position) + hold * move;
        gun.SetPositionAndRotation(Vector3.Lerp(gun.position, at, weight), Quaternion.Slerp(gun.rotation, hold * turn, weight));
    }

    /// <summary>
    /// Down the sights, the share of the camera's sway from a gun's action worked after a shot that stays (aimedCycleMove), as
    /// the gun's own motion is eased (FollowContacts); 1 otherwise.
    /// </summary>
    private float CycleViewShare => HoldsGun && held.Cycling ? Mathf.Lerp(1f, aimedCycleMove, controller.AimBlend) : 1f;

    // How a hand's two contacts mix: the playing clip's (clip, its weight) over the plain hold's (hold). Put on one after
    // the other, they come to one target the mix of the way to the clip's, with this much of the hand.
    private static float MixOf(float clip, float hold, out float mix)
    {
        float weight = clip + hold * (1f - clip);
        mix = weight > 0f ? clip / weight : 0f;
        return weight;
    }

    /// <summary>
    /// Where a hand's palm goes on the gun now: the middle of the palm, the way the fingers point and the way the palm faces,
    /// in the world as the gun sits now, as the playing clip has it over the plain hold's, and how much of the hand it takes
    /// (0-1). False when neither holds the hand.
    /// </summary>
    internal bool HandContact(bool isLeft, out Vector3 palm, out Vector3 fingers, out Vector3 facing, out float weight)
    {
        palm = fingers = facing = Vector3.zero;
        weight = 0f;
        if (!HoldsGun || rig == null) return false;
        float clip = hasContact ? contactWeight : 0f;
        float hold = HoldSide(isLeft, out GunContact steady) ? holdContactWeight : 0f;
        weight = MixOf(clip, hold, out float mix) * Framework;
        if (weight <= 0f) return false;
        Vector3 holdPalm = isLeft ? steady.LeftPalm : steady.RightPalm, clipPalm = isLeft ? contact.LeftPalm : contact.RightPalm;
        Vector3 holdFingers = isLeft ? steady.LeftFingers : steady.RightFingers, clipFingers = isLeft ? contact.LeftFingers : contact.RightFingers;
        Vector3 holdFacing = isLeft ? steady.LeftFacing : steady.RightFacing, clipFacing = isLeft ? contact.LeftFacing : contact.RightFacing;
        // One alone: the other's may not have been read at all.
        if (mix >= 1f) { holdPalm = clipPalm; holdFingers = clipFingers; holdFacing = clipFacing; }
        else if (mix <= 0f) { clipPalm = holdPalm; clipFingers = holdFingers; clipFacing = holdFacing; }
        var gun = rig.IkWeaponBone;
        Quaternion turn = gun.rotation;
        Vector3 onGun = Vector3.Lerp(holdPalm, clipPalm, mix);
        // A short support arm holds a bare handguard further back (ShoulderedHold); a reload's hand elsewhere keeps its place.
        if (isLeft) onGun = held.SlideSupport(onGun);
        palm = gun.position + turn * onGun;
        fingers = turn * Vector3.Slerp(holdFingers, clipFingers, mix);
        facing = turn * Vector3.Slerp(holdFacing, clipFacing, mix);
        return true;
    }

    /// <summary>
    /// The way a hand's elbow points off its shoulder-to-hand line as KINEMATION's arms have it now (the playing clip's over
    /// the plain hold's, mixed as HandContact mixes the palms), in the world: their arms' frame laid on this gun's rest hold
    /// from the eyes (FPSGun.HoldFromEyes), so it stays with the body and the view while a wall pulls the gun back or a shot
    /// kicks it. The arm IK turns the elbow toward it (PlayerBodyHands.HoldGunContacts): bending to the side this frame's
    /// pose happened to have, an arm folded up into the view or flipped as FPSAF's arms came in nearly straight or turned
    /// over (user, 2026-11-12: "팔이 있을 위치를 못 잡아서 기괴하게 꺾이거나 … 확 튀는"). False where neither was read.
    /// </summary>
    internal bool ElbowWay(bool isLeft, out Vector3 way)
    {
        way = Vector3.zero;
        if (!HoldsGun || rig == null) return false;
        float clip = hasContact ? contactWeight : 0f;
        float hold = HoldSide(isLeft, out GunContact steady) ? holdContactWeight : 0f;
        if (MixOf(clip, hold, out float mix) <= 0f) return false;
        Vector3 holdWay = isLeft ? steady.LeftElbow : steady.RightElbow, clipWay = isLeft ? contact.LeftElbow : contact.RightElbow;
        // One alone, or one of them too straight to tell: the other.
        if (mix >= 1f || holdWay.sqrMagnitude < .5f) holdWay = clipWay;
        if (mix <= 0f || clipWay.sqrMagnitude < .5f) clipWay = holdWay;
        if (holdWay.sqrMagnitude < .5f) return false;
        Quaternion rest = eyes != null && held.HoldFromEyes(controller.AimBlend, out KTransform kept)
            ? eyes.rotation * kept.rotation : rig.IkWeaponBone.rotation;
        // A pistol held in close rolls the elbows with it (FPSBody.CloseHold).
        way = CloseElbow(rest * Vector3.Slerp(holdWay, clipWay, mix).normalized);
        return true;
    }

    /// <summary>
    /// The plain hold's palm on the gun as it sits now (the gun kept in view through a vault or a slide too): the palm's
    /// middle, the way the fingers point and the way the palm faces. False while it isn't on.
    /// </summary>
    internal bool HoldContact(bool isLeft, out Vector3 palm, out Vector3 fingers, out Vector3 facing)
    {
        palm = fingers = facing = Vector3.zero;
        if (!HoldsGun || rig == null || holdContactWeight <= 0f || !HoldSide(isLeft, out GunContact steady)) return false;
        var gun = rig.IkWeaponBone;
        Quaternion turn = gun.rotation;
        palm = gun.position + turn * (isLeft ? held.SlideSupport(steady.LeftPalm) : steady.RightPalm);
        fingers = turn * (isLeft ? steady.LeftFingers : steady.RightFingers);
        facing = turn * (isLeft ? steady.LeftFacing : steady.RightFacing);
        return true;
    }

    /// <summary>
    /// The way each of a hand's finger joints points as KINEMATION's clips have it now (GunArms.Digits: the playing clip's
    /// over the plain hold's), how much of the hand it takes (0-1, as HandContact), and whether HandGrip rests the fingers on
    /// the gun's surface (only with Rig.stillHoldFixes, the plain hold alone and fully on). False when the fingers aren't the
    /// clips' (Rig.contactFingers off, or no clip's hand could be read).
    /// </summary>
    internal bool HandDigits(bool isLeft, out Vector3[] digits, out float weight, out bool still)
    {
        digits = null;
        weight = 0f;
        still = false;
        if (!contactFingers || !HoldsGun || rig == null) return false;
        Vector3[] clipWays = hasContact ? (isLeft ? contact.LeftDigits : contact.RightDigits) : null;
        Vector3[] holdWays = HoldSide(isLeft, out GunContact steady) ? (isLeft ? steady.LeftDigits : steady.RightDigits) : null;
        float clip = clipWays != null ? contactWeight : 0f, hold = holdWays != null ? holdContactWeight : 0f;
        weight = MixOf(clip, hold, out float mix) * Framework;
        if (weight <= 0f) return false;
        if (mix >= .999f) digits = clipWays;
        else if (mix <= .001f) digits = holdWays;
        else
        {
            digits = isLeft ? mixLeftDigits : mixRightDigits;
            for (int i = 0; i < GunArms.DigitCount; i++) digits[i] = Vector3.Slerp(holdWays[i], clipWays[i], mix);
        }
        // Through a reload the fingers wear the clip's ways as they are; the hold wears its own the same way, so they don't
        // open off the gun the moment the reload ends.
        still = stillHoldFixes && mix <= .001f && hold >= .999f && Framework >= .999f;
        return true;
    }

    /// <summary>The plain hold is on and alone, both hands still on the gun (for the gun hold report).</summary>
    internal bool HoldSteady => HoldsGun && holdContactWeight >= .999f && !hasContact && !held.HandsBusy && Framework >= .999f;

    // The plain hold a hand takes on the held gun: the support hand's from the gun's no-grip pose while the gun wears no
    // grip, where it has one (the TR15's pose clip holds a grip it may not wear).
    private bool HoldSide(bool isLeft, out GunContact hold)
    {
        hold = default;
        if (held == null || !held.HasHoldContact) return false;
        hold = isLeft && held.HasHoldContactNoGrip && held.GripKind == GunAttachments.Kind.None ? held.HoldContactNoGrip : held.HoldContact;
        return true;
    }

    // The gun's plain hold on KINEMATION's arms, from its tracks, once per gun: its pose clip's palms, elbows and fingers,
    // and its no-grip pose's.
    private bool HoldContactOf(FPSGun gun)
    {
        if (gun.HoldContactTried) return gun.HasHoldContact;
        gun.HoldContactTried = true;
        if (!PoseHolds(gun)) return false;
        gun.HasHoldContact = ReadHold(gun.Tracks.Track(GunTrackKind.Hold), ref gun.HoldContact);
        if (gun.HasHoldContact) gun.HasHoldContactNoGrip = ReadHold(gun.Tracks.Track(GunTrackKind.HoldNoGrip), ref gun.HoldContactNoGrip);
        // Its grips no further back than KINEMATION's support hand on the bare handguard (GunAttachments.SeatGrips).
        if (gun.HasHoldContact)
            gun.SeatGrips(rig.IkWeaponBone, gun.HasHoldContactNoGrip ? gun.HoldContactNoGrip.LeftPalm : gun.HoldContact.LeftPalm);
        return gun.HasHoldContact;
    }

    // A pose's track as a hold: both palms on the gun as that pose holds it, each elbow's way in that gun's frame, and the
    // finger ways in arrays the hold keeps (both hands' or neither's, as a hold was always read).
    private static bool ReadHold(GunTrack pose, ref GunContact into)
    {
        if (pose == null) return false;
        var right = into.RightDigits ?? new Vector3[GunArms.DigitCount];
        var left = into.LeftDigits ?? new Vector3[GunArms.DigitCount];
        if (!pose.Sample(0f, right, left, out GunContact read, out _)) return false;
        // A track keeps the elbows in the frame of the gun as the gun's pose clip holds it; a hold's are in the frame of the
        // gun as this very pose holds it, which is another only for a no-grip pose that holds the gun a little otherwise.
        Quaternion toOwn = Quaternion.Inverse(read.GunTurn);
        read.RightElbow = toOwn * read.RightElbow;
        read.LeftElbow = toOwn * read.LeftElbow;
        read.GunMove = Vector3.zero;
        read.GunTurn = Quaternion.identity;
        if (read.RightDigits == null || read.LeftDigits == null) read.RightDigits = read.LeftDigits = null;
        into = read;
        return true;
    }

    // Whether the gun's own clips can be followed at all: it has tracks, and its pose clip holds it in both hands (the
    // support palm within PoseReach of the weapon bone, asked as the tracks were read: a handguard is under 0.6 m from it).
    // If a pose never took on KINEMATION's arms, their rest pose would hold the arms out wide, and following that would
    // throw the hands off the gun; the gun's clips then stay on the remade ones. Said once for a gun that can't.
    private bool PoseHolds(FPSGun gun)
    {
        if (gun.TracksHold) return true;
        if (gun.ToldNoHold) return false;
        gun.ToldNoHold = true;
        Report(gun.Tracks == null
            ? $"{gun.name} has no tracks of its own clips (ENFILADE → 총 → 총 동작 굽기); its hands stay on the remade clips alone."
            : $"{gun.name}'s pose clip is no two-handed hold on KINEMATION's arms; its hands stay on the remade clips alone.");
        return false;
    }

    // ---- When a gun worked by hand can fire again (WeaponCycle) ----

    // A hand is back on the gun once its palm is within this of where the gun's pose clip holds it (m); a clip that takes neither
    // palm further than CycleAway from there works no action (a recoil clip, as FPS Animation Ultimate's X18_Fire) and holds
    // nothing. Sampled every CycleStep (s).
    private const float CycleBack = .015f, CycleAway = .04f, CycleStep = 1f / 60f;

    /// <summary>
    /// Once per gun worked by hand after every shot: the moment in KINEMATION's clip of the action from which both palms stay
    /// where the gun's pose clip holds them, read from the gun's tracks (as the contacts are), so the rules hold the next shot
    /// until the hands are back (WeaponCycle) and the clip is never cut short. The same asset on every PC gives every PC the
    /// same moment. FPSBodyAssets.Gun.fireReady sets it by hand instead; without the track, the clip's end as it eases into
    /// the hold. Measured Kar98k and L96X 1.27 s, KXG12 0.5 s (2026-11-13, from the clips' hand targets).
    /// </summary>
    private void MeasureCycle(FPSGun gun)
    {
        var clip = gun.Entry.sourceFire;
        if (gun.Data == null || clip == null || !gun.HasCycle || WeaponCycle.Has(gun.Data)) return;
        float rate = gun.CycleRate;
        if (gun.Entry.fireReady > 0f)
        {
            WeaponCycle.Set(gun.Data, gun.Entry.fireReady / rate);
            return;
        }
        float ready = Mathf.Max(0f, clip.length - gun.CycleSettle);
        if (PoseHolds(gun))
        {
            if (HandsBack(gun.Tracks.Track(GunTrackKind.Cycle), gun.Tracks.Track(GunTrackKind.Hold), out float back)) ready = back;
            else Report($"{gun.name}'s action ({clip.name}) has no track; its next shot waits for the clip's end.");
        }
        WeaponCycle.Set(gun.Data, ready / rate);
    }

    /// <summary>
    /// When in a worked action both palms are back where the gun's pose holds them and stay there (s into the clip): 0 for a
    /// clip that takes neither palm off its place (a recoil clip works no action). False without the two tracks. The
    /// editor's report of the tracks asks the same, so what the rules wait for can be read there (Logs/gun-tracks.txt).
    /// </summary>
    internal static bool HandsBack(GunTrack action, GunTrack hold, out float ready)
    {
        ready = 0f;
        if (action == null || hold == null || !hold.Sample(0f, null, null, out GunContact pose, out _)) return false;
        float back = 0f, farthest = 0f;
        for (float time = 0f; time <= action.length + 1e-4f; time += CycleStep)
        {
            if (!action.Sample(Mathf.Min(time, action.length), null, null, out GunContact at, out _)) return false;
            float away = Mathf.Max(Vector3.Distance(at.RightPalm, pose.RightPalm), Vector3.Distance(at.LeftPalm, pose.LeftPalm));
            farthest = Mathf.Max(farthest, away);
            // The last moment a hand was still off the gun's hold: from the next sample on, both stay on it.
            if (away >= CycleBack) back = Mathf.Min(time + CycleStep, action.length);
        }
        ready = farthest < CycleAway ? 0f : back;
        return true;
    }

    // The body went: nothing is being followed.
    private void ForgetContacts()
    {
        contactGun = null;
        contactWeight = holdContactWeight = 0f;
        hasContact = false;
    }
}
