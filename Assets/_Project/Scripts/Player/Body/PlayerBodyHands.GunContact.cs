using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Both palms go where KINEMATION's own clips have them on the gun (FPSBody.HandContact): in the plain hold (standing,
/// aiming, walking, sprinting) where its pose clip has them, and through a reload, a draw, a holster or a look over the gun
/// where that clip has them at the moment. The arm bends until the middle of the palm is there, the fingers pointing and the
/// palm facing as the clip's hand does, the elbow staying on the side it bends to now. So the trigger hand is on the grip
/// and the support hand on the handguard, the magazine or the charging handle on any character, which the remade clips'
/// joint angles alone could only do for the reference body. A character's palms move by its row in Tables/GunHands.csv.
/// The fingers follow the same clips (GunFingers).
/// In the editor, the final hands (after every pass on them) are measured against KINEMATION's places and go to
/// Logs/gun-hold-report.txt (no console), for the user's playtest numbers: the first time each character holds each gun
/// still, and the first time each character's reload of each gun hands over to the hold (its last frame fully on the
/// reload's clip against the hold's first still frame; the user, 2026-11-12: "재장전 끝나자마자 손을 이상하게 잡아").
/// </summary>
public sealed partial class PlayerBodyHands
{
    // How much of each hand ([left, right]) the clips' contacts placed this frame, for GunFingers.
    private readonly float[] contactShare = new float[2];
    // Each palm's place on the gun this frame as the IK took it (world), to measure the final hand against.
    private readonly Vector3[] placePalm = new Vector3[2], placeFingers = new Vector3[2], placeFacing = new Vector3[2];
    private readonly bool[] placed = new bool[2];
    // The old hold's palms before this frame's contacts (m and degrees), while the plain hold is measured.
    private readonly float[] palmMissBefore = new float[2], palmTurnBefore = new float[2];
    private bool holdMeasured;
    // A reload's last frame fully on its clip, measured for the hand-over line, whether that reload then settled (played out,
    // not cut short), and the gun it was on.
    private readonly HandMeasure[] reloadEnd = new HandMeasure[2];
    private bool reloadEndMeasured, reloadEndSettled;
    private WeaponData reloadEndGun;
    private float reloadEndAt;
    // Each character and gun reported once per session, for the hold and for the hand-over.
    private static readonly HashSet<(string skin, WeaponData gun)> reportedHolds = new(), reportedHandOvers = new();
    private static readonly Vector3[] measureWays = new Vector3[GunArms.DigitCount];

    // A final hand against KINEMATION's place: the palm's miss (m), its turn (degrees), and the fingers' mean and most turn
    // per joint off the clip's ways (degrees; -1 where the fingers aren't the clip's).
    private struct HandMeasure
    {
        public float Miss, Turn, Fingers, FingersMost;
    }

    private string SkinId => body != null && body.Skin != null ? body.Skin.Id : null;

    // Both hands on the gun with nothing else holding either (a one-handed vault or a lent hand isn't the hold).
    private bool HandsAllOnGun => weights[(int)Hold.Gun] >= .999f && leftOffGun <= 0f && offHandLent <= 0f;

    private void HoldGunContacts()
    {
        var gunBody = FPSBody.Holding(weapons);
        holdMeasured = false;
        contactShare[0] = contactShare[1] = 0f;
        placed[0] = placed[1] = false;
        if (gunBody == null) return;
        bool measure = Application.isEditor && gunBody.HoldSteady && HandsAllOnGun
            && weapons.EquippedData != null && !reportedHolds.Contains((SkinId ?? "?", weapons.EquippedData));
        Follow(right, false);
        Follow(left, true);
        holdMeasured = measure;

        void Follow(ArmIK arm, bool isLeft)
        {
            if (arm == null || !gunBody.HandContact(isLeft, out Vector3 palm, out Vector3 fingers, out Vector3 facing, out float weight)) return;
            // As far as the hand is on the gun at all: a one-handed vault takes the left off it, and so does lending it (OffHand).
            weight *= weights[(int)Hold.Gun] * (isLeft ? (1f - leftOffGun) * OffHandKept : 1f);
            if (weight <= 0f) return;
            palm = GunHands.Palm(SkinId, isLeft, palm, fingers, facing);
            int side = isLeft ? 0 : 1;
            contactShare[side] = Mathf.Clamp01(weight);
            placePalm[side] = palm;
            placeFingers[side] = fingers;
            placeFacing[side] = facing;
            placed[side] = true;
            if (measure) Miss(arm, palm, fingers, facing, out palmMissBefore[side], out palmTurnBefore[side]);
            Quaternion rotation = arm.HandRotation(fingers, facing);
            // The elbow turns toward the way KINEMATION's points at this moment (FPSBody.ElbowWay), not the side this
            // frame's pose happens to bend to; without their way, that side as before.
            Vector3 hint = arm.Elbow;
            if (gunBody.ElbowWay(isLeft, out Vector3 elbowWay))
            {
                hint = arm.Upper.position + (palm - arm.Upper.position) * .5f + elbowWay * .3f;
                if (Application.isEditor) NoteElbow(arm, isLeft, palm, elbowWay);
            }
            arm.Solve(arm.WristAtPalm(palm, rotation), rotation, hint, weight, 1f, shoulderReach);
        }
    }

    // Each character, gun and arm noted once per session the first time the pose this frame bent the elbow over a right
    // angle off KINEMATION's side (the folds and flips the user saw, 2026-11-12), to Logs/gun-hold-report.txt.
    private static readonly HashSet<(string skin, WeaponData gun, bool isLeft)> notedElbows = new();

    private void NoteElbow(ArmIK arm, bool isLeft, Vector3 palm, Vector3 way)
    {
        var data = weapons != null ? weapons.EquippedData : null;
        if (data == null) return;
        Vector3 line = palm - arm.Upper.position;
        Vector3 now = Vector3.ProjectOnPlane(arm.Elbow - arm.Upper.position, line), wanted = Vector3.ProjectOnPlane(way, line);
        if (now.sqrMagnitude < 1e-6f || wanted.sqrMagnitude < 1e-6f) return;
        float off = Vector3.Angle(now, wanted);
        if (off < 90f || !notedElbows.Add((SkinId ?? "?", data, isLeft))) return;
        Write($"{System.DateTime.Now:HH:mm:ss} {SkinId ?? "?"} {data.name}: {(isLeft ? "left" : "right")} elbow came in {off:0}° off "
            + "KINEMATION's side (this frame's arm pose), turned to theirs\n");
    }

    // How far a palm is from its place on the gun (m), and the larger of how far its fingers' way and its facing are turned
    // from the place's (degrees).
    private static void Miss(ArmIK arm, Vector3 palm, Vector3 fingers, Vector3 facing, out float miss, out float turn)
    {
        miss = Vector3.Distance(arm.PalmPosition, palm);
        turn = Mathf.Max(Vector3.Angle(arm.FingersForward, fingers), Vector3.Angle(arm.PalmFacing, facing));
    }

    // The final hand this frame against where the contacts put it and the clip's finger ways. False if the contacts didn't
    // place it this frame.
    private bool Measure(FPSBody gunBody, ArmIK arm, bool isLeft, out HandMeasure measure)
    {
        measure = default;
        int side = isLeft ? 0 : 1;
        if (arm == null || !placed[side]) return false;
        Miss(arm, placePalm[side], placeFingers[side], placeFacing[side], out measure.Miss, out measure.Turn);
        measure.Fingers = measure.FingersMost = -1f;
        if (gunBody.HandDigits(isLeft, out Vector3[] digits, out _, out _) && digits != null && arm.FingerWays(measureWays))
            Turns(measureWays, digits, out measure.Fingers, out measure.FingersMost);
        return true;
    }

    // Last in the frame, after the fingers: the measured hold to Logs/gun-hold-report.txt, once per character and gun, and
    // the hand-over from a reload's end into the hold, once per character and gun.
    private void ReportGunHold()
    {
        if (!Application.isEditor) return;
        var gunBody = FPSBody.Holding(weapons);
        var data = weapons != null ? weapons.EquippedData : null;
        if (gunBody == null || data == null)
        {
            holdMeasured = reloadEndMeasured = false;
            return;
        }
        var key = (SkinId ?? "?", data);
        if (holdMeasured)
        {
            holdMeasured = false;
            if (!reportedHolds.Contains(key) && Measure(gunBody, right, false, out var rightHold) && Measure(gunBody, left, true, out var leftHold))
            {
                reportedHolds.Add(key);
                Write(HoldLine(gunBody, data, rightHold, leftHold));
            }
        }

        if (data != reloadEndGun)
        {
            reloadEndGun = data;
            reloadEndMeasured = false;
        }
        if (reportedHandOvers.Contains(key) || !HandsAllOnGun) return;
        if (gunBody.OnReloadClip)
        {
            reloadEndMeasured = Measure(gunBody, right, false, out reloadEnd[1]) && Measure(gunBody, left, true, out reloadEnd[0]);
            reloadEndSettled = false;
            reloadEndAt = Time.time;
            return;
        }
        if (gunBody.ReloadSettling) reloadEndSettled = true;
        if (!reloadEndMeasured || !gunBody.HoldSteady) return;
        reloadEndMeasured = false;
        // Only a reload that played out hands over this way; one cut short lets go of the hands by reloadBlendOut instead.
        if (!reloadEndSettled) return;
        if (!Measure(gunBody, right, false, out var rightNow) || !Measure(gunBody, left, true, out var leftNow)) return;
        reportedHandOvers.Add(key);
        Write($"{System.DateTime.Now:HH:mm:ss} {SkinId ?? "?"} {data.name} reload's end -> hold {Time.time - reloadEndAt:0.00} s later: "
            + $"right palm {Hand(reloadEnd[1])} -> {Hand(rightNow)}; left palm {Hand(reloadEnd[0])} -> {Hand(leftNow)} "
            + "(each off KINEMATION's place on the gun at that moment)\n");
    }

    private string HoldLine(FPSBody gunBody, WeaponData data, HandMeasure rightHold, HandMeasure leftHold)
    {
        var ways = new Vector3[GunArms.DigitCount];
        string Fingers(ArmIK arm, bool isLeft, HandMeasure now)
        {
            int side = isLeft ? 0 : 1;
            if (arm == null || !gunBody.HandDigits(isLeft, out Vector3[] digits, out _, out _) || digits == null || now.Fingers < 0f)
                return "our shapes";
            string shape = "-";
            if (arm.ShapeWays(gunShape[side], gunCustom[side], ways))
            {
                Turns(ways, digits, out float mean, out _);
                shape = $"{mean:0}";
            }
            return $"shape {gunShape[side]} {shape}° off KINEMATION's, now {now.Fingers:0.0}° (most {now.FingersMost:0}°)";
        }
        string Palm(int side, HandMeasure now)
            => $"{palmMissBefore[side] * 100f:0.0} cm {palmTurnBefore[side]:0}° off KINEMATION's place, now {now.Miss * 100f:0.0} cm {now.Turn:0}°";
        return $"{System.DateTime.Now:HH:mm:ss} {SkinId ?? "?"} {data.name}: right palm {Palm(1, rightHold)}; left palm {Palm(0, leftHold)}; "
            + $"right fingers {Fingers(right, false, rightHold)}; left fingers {Fingers(left, true, leftHold)} (per finger joint, thumbs aside)\n";
    }

    private static string Hand(HandMeasure hand)
        => $"{hand.Miss * 100f:0.0} cm {hand.Turn:0}°" + (hand.Fingers >= 0f ? $" fingers {hand.Fingers:0.0}° (most {hand.FingersMost:0}°)" : "");

    private static void Write(string line)
    {
        try
        {
            string path = System.IO.Path.Combine(Application.dataPath, "..", "Logs", "gun-hold-report.txt");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.AppendAllText(path, line);
        }
        catch (System.Exception error)
        {
            Debug.LogWarning($"PlayerBodyHands: the gun hold report could not be written: {error.Message}");
        }
    }

    // The mean and the largest angle between two hands' finger ways over the four fingers' joints (degrees).
    private static void Turns(Vector3[] a, Vector3[] b, out float mean, out float most)
    {
        float sum = 0f;
        most = 0f;
        for (int i = 0; i < 12; i++)
        {
            float angle = Vector3.Angle(a[i], b[i]);
            sum += angle;
            most = Mathf.Max(most, angle);
        }
        mean = sum / 12f;
    }
}
