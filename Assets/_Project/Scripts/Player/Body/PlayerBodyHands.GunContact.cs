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
/// In the editor, the first time each character holds each gun still, how far FPSAF's hold had each palm and the fingers
/// from KINEMATION's, and how far after, goes to Logs/gun-hold-report.txt (no console), for the user's playtest numbers.
/// </summary>
public sealed partial class PlayerBodyHands
{
    // The palms before and after this frame's contacts (m and degrees, [left, right]) while the plain hold is measured.
    private readonly float[] palmMissBefore = new float[2], palmTurnBefore = new float[2], palmMissAfter = new float[2], palmTurnAfter = new float[2];
    private bool holdMeasured;
    // Each character and gun reported once per session.
    private static readonly HashSet<(string skin, WeaponData gun)> reportedHolds = new();

    private string SkinId => body != null && body.Skin != null ? body.Skin.Id : null;

    private void HoldGunContacts()
    {
        var gunBody = FPSBody.Holding(weapons);
        holdMeasured = false;
        if (gunBody == null) return;
        bool measure = Application.isEditor && gunBody.HoldSteady && weights[(int)Hold.Gun] >= .999f && leftOffGun <= 0f
            && weapons.EquippedData != null && !reportedHolds.Contains((SkinId ?? "?", weapons.EquippedData));
        Follow(right, false);
        Follow(left, true);
        holdMeasured = measure;

        void Follow(ArmIK arm, bool isLeft)
        {
            if (arm == null || !gunBody.HandContact(isLeft, out Vector3 palm, out Vector3 fingers, out Vector3 facing, out float weight)) return;
            // As far as the hand is on the gun at all: a one-handed vault takes the left off it.
            weight *= weights[(int)Hold.Gun] * (isLeft ? 1f - leftOffGun : 1f);
            if (weight <= 0f) return;
            palm = GunHands.Palm(SkinId, isLeft, palm, fingers, facing);
            int side = isLeft ? 0 : 1;
            if (measure) Miss(arm, palm, fingers, facing, out palmMissBefore[side], out palmTurnBefore[side]);
            Quaternion rotation = arm.HandRotation(fingers, facing);
            arm.Solve(arm.WristAtPalm(palm, rotation), rotation, arm.Elbow, weight, 1f, shoulderReach);
            if (measure) Miss(arm, palm, fingers, facing, out palmMissAfter[side], out palmTurnAfter[side]);
        }
    }

    // How far a palm is from its place on the gun (m), and the larger of how far its fingers' way and its facing are turned
    // from the place's (degrees).
    private static void Miss(ArmIK arm, Vector3 palm, Vector3 fingers, Vector3 facing, out float miss, out float turn)
    {
        miss = Vector3.Distance(arm.PalmPosition, palm);
        turn = Mathf.Max(Vector3.Angle(arm.FingersForward, fingers), Vector3.Angle(arm.PalmFacing, facing));
    }

    // Last in the frame, after the fingers: the measured hold to Logs/gun-hold-report.txt, once per character and gun.
    private void ReportGunHold()
    {
        if (!holdMeasured) return;
        holdMeasured = false;
        var gunBody = FPSBody.Holding(weapons);
        var data = weapons != null ? weapons.EquippedData : null;
        if (gunBody == null || data == null || !reportedHolds.Add((SkinId ?? "?", data))) return;
        var ways = new Vector3[GunArms.DigitCount];
        string Fingers(ArmIK arm, bool isLeft)
        {
            int side = isLeft ? 0 : 1;
            if (arm == null || !gunBody.HandDigits(isLeft, out Vector3[] digits, out _, out _) || digits == null) return "our shapes";
            string shape = arm.ShapeWays(gunShape[side], gunCustom[side], ways) ? $"{MeanTurn(ways, digits):0}" : "-";
            string now = arm.FingerWays(ways) ? $"{MeanTurn(ways, digits):0.0}" : "-";
            return $"shape {gunShape[side]} {shape}° off KINEMATION's, now {now}°";
        }
        string Palm(int side) => $"{palmMissBefore[side] * 100f:0.0} cm {palmTurnBefore[side]:0}° off KINEMATION's place, after {palmMissAfter[side] * 100f:0.0} cm {palmTurnAfter[side]:0}°";
        string line = $"{System.DateTime.Now:HH:mm:ss} {SkinId ?? "?"} {data.name}: right palm {Palm(1)}; left palm {Palm(0)}; "
            + $"right fingers {Fingers(right, false)}; left fingers {Fingers(left, true)} (mean per finger joint, thumbs aside)\n";
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

    // The mean angle between two hands' finger ways over the four fingers' joints (degrees).
    private static float MeanTurn(Vector3[] a, Vector3[] b)
    {
        float sum = 0f;
        for (int i = 0; i < 12; i++) sum += Vector3.Angle(a[i], b[i]);
        return sum / 12f;
    }
}
