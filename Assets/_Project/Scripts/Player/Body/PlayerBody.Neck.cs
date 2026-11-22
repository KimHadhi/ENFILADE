using System.Text;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The camera is the head, and the head turns no further from the chest than a neck turns (the user, 2026-11-21: "지금
/// 카메라 = 머리가 아닌 것 같거든 … 머리가 몸을 못 뚫게 하면 … 다른 파쿠르 동작 중에 몸 뚫으려해도 안 될 거"). The head
/// is put on the view (BodyGaze.FaceHead), so a view turned further from the chest than a neck turns wrung the head round
/// into the shoulder, and the camera on its eyes looked out from inside the body. BIPER's slide sits turned to the right of
/// its way (hips 48°, chest 50°, ClipBench track), so a look 120° to the left left the neck turned about 116°; to the right
/// it was 16°.
/// - The view's reach (HeadReachArc): wherever the legs don't come round after the view (anything but standing and
///   crouching, where they step round: TurnOnTheSpot), the view turns only as far as the head can on the pose the clip has:
///   from where the clip turns the chest (the shoulders' line, read after the animation pass), what the look's own twist of
///   the spine adds (SpineLook: 60% of the view's turn) and the neck's turn on top
///   (Body.neckTurnLimit). PlayerLook holds the mouse there, so the camera and the head stay one and look the same way.
///   Lying, diving, rolling, hanging from a ledge or a bar keep their own limits: their head turns are the clips' own or a
///   roll of the whole body, and a body that isn't upright has no chest facing to turn from. Sliding has fixed limits
///   (Body.slideLookLimits): its clips swing the chest from 6 to 70 degrees to the right and back within the slide (measured
///   in Blender, 2026-11-22), so a reach read each frame opened to 120 degrees left and shut to 12 again, and a view caught
///   outside it was flung. A step dodge has none: the body turns with the view through it.
/// - The neck's guard (FaceHead): a clip that turns the chest away faster than the view is held could still wring the head
///   for a frame, so the head itself stops a little past the same limit.
/// - The record (카메라 기록, Logs/camera-head.txt): each time the camera is off the eyes by more than CameraOffMetres or
///   looks more than CameraTurnDegrees away from where the head looks, one line when it is back: how long, how far, in which
///   state and clip, and what had it off (a ladder's or a punch's steadying, the run's calm, the lean, a wall, an action
///   clip leading the head, the gun's own head, the neck's guard).
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Range(30f, 150f), Tooltip("The most the head turns from the chest (degrees): the neck's own turn. The view turns "
        + "no further on a pose whose legs don't step round after it.")]
    private float neckTurnLimit = 75f;

    // The share of the look's turn the spine bones take between them (SpineLook.SpineYawShare).
    private const float SpineTwistShare = .6f;
    // A body whose hips-to-head line leans further than this from upright (its y) has no chest facing to turn the head from.
    private const float TorsoUpright = .35f;
    // The head's own guard stops this far past the view's reach, so it only catches a frame the view was not held in time.
    private const float NeckGuardSlack = 10f;

    // Where the clip turns the chest from the body's facing (degrees, + to the right), read after the animation pass; and
    // whether the body is upright enough to have one.
    private float clipChestTurn, restYawOffset;
    private bool torsoUpright, chestMeasured;

    // From SetUpLook: how far the rest pose's facing (the shoulders' line) is from the model's own ahead.
    private void SetUpNeck()
    {
        restYawOffset = body != null ? Vector3.SignedAngle(Vector3.ProjectOnPlane(body.forward, Vector3.up),
            Vector3.ProjectOnPlane(restForward, Vector3.up), Vector3.up) : 0f;
        chestMeasured = false;
        cameraAwaySince = -1f;
    }

    // The way the body faces (world yaw): the model's turn and its rest pose's own facing.
    private float BodyFacingYaw => body.eulerAngles.y + restYawOffset;

    // First thing after the animation pass, before the look bends the spine: where the clip has the chest.
    private void MeasureClipChest()
    {
        chestMeasured = false;
        if (leftUpperArm == null || rightUpperArm == null || hips == null || head == null) return;
        Vector3 ahead = Vector3.Cross(rightUpperArm.position - leftUpperArm.position, Vector3.up);
        if (ahead.sqrMagnitude < 1e-6f) return;
        clipChestTurn = Mathf.DeltaAngle(BodyFacingYaw, Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg);
        Vector3 up = head.position - hips.position;
        torsoUpright = up.sqrMagnitude > 1e-6f && up.normalized.y > TorsoUpright;
        chestMeasured = true;
    }

    // The states whose legs don't step round after the view, and whose head the look turns.
    private static bool ReachHolds(MovementState state)
    {
        switch (state)
        {
            case MovementState.Vault:
            case MovementState.Mantle:
            case MovementState.WallClimb:
            case MovementState.WallKick:
            case MovementState.LedgeJump:
            case MovementState.Ladder:
            case MovementState.Zipline:
            case MovementState.DashPush:
            case MovementState.HeavyLanding:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// How far the view can turn this frame for the head to follow it without turning further from the chest than a neck
    /// does: <paramref name="lo"/> to <paramref name="hi"/> degrees from <paramref name="facing"/>, the way the body faces
    /// (world yaw). False where the legs step round after the view, or the body isn't upright (PlayerLook keeps the state's
    /// own limits there).
    /// </summary>
    internal bool HeadReachArc(out float facing, out float lo, out float hi)
    {
        facing = lo = hi = 0f;
        if (body == null || !chestMeasured || !torsoUpright || dead || limp || !ReachHolds(state)) return false;
        facing = BodyFacingYaw;
        // The spine twists its share of the view's turn (AimLook); the neck turns the rest, as far as it can.
        float share = SpineTwistShare * Mathf.Clamp01(spineLookWeight);
        float Edge(float side) => (side * neckTurnLimit + clipChestTurn) / Mathf.Max(.05f, 1f - share);
        lo = Mathf.Max(-180f, Edge(-1f));
        hi = Mathf.Min(180f, Edge(1f));
        return hi > lo;
    }

    // The chest's ahead now, after the look's bend, for the neck's guard; zero where there is none to turn from.
    private Vector3 GuardedChestAhead()
    {
        if (!chestMeasured || !torsoUpright || leftUpperArm == null || rightUpperArm == null) return Vector3.zero;
        return Vector3.Cross(rightUpperArm.position - leftUpperArm.position, Vector3.up);
    }

    // 카메라 기록: when the camera is off the eyes or looks away from the head's gaze, and why.
    private const string CameraRecord = "camera-head";
    private const string CameraRecordHeading = "카메라 기록 (PlayerBody.Neck): 1인칭 카메라가 눈에서 CameraOffMetres(3cm)보다 떨어지거나 "
        + "머리가 보는 쪽에서 CameraTurnDegrees(8°)보다 돌아간 때마다, 돌아온 뒤 한 줄: 상태, 몇 초, 가장 멀리 떨어진 거리(cm)와 각도, 그때 "
        + "가장 무거운 클립, 왜 떨어졌는지(사다리·주먹의 고정, 달리기 흔들림 잡기, 몸 흔들림 설정, 기울이기, 벽, 동작 클립이 머리를 이끎, 총이 "
        + "머리를 가짐(FPSAF), 조준(오른눈), 목의 한계, 머리를 카메라로 데려갈 수 있는 거리(Body.headReachMove)를 넘음)";
    private const float CameraOffMetres = .03f, CameraTurnDegrees = 8f, CameraBackFor = .25f, CameraLongest = 5f;
    private float cameraAwaySince = -1f, cameraBackFor, cameraOffMost, cameraTurnMost;
    private MovementState cameraAwayState;
    private readonly StringBuilder cameraWhy = new StringBuilder();
    private string cameraClip;

    // Last in Look, once the head has been bent to the camera: how far the camera is from the eyes and the head's gaze.
    private void RecordCameraOnHead(float dt, bool neckHeld)
    {
        if (!PlayRecord.On || player == null || !player.IsLocal || thirdPerson || head == null || !bodyCamera.HasPlace) return;
        float off = dead ? 0f : Vector3.Distance(bodyCamera.Placed, head.TransformPoint(eyeLocal));
        float turn = dead ? 0f : Quaternion.Angle(bodyGaze.Rotation, bodyGaze.HeadGaze);
        bool away = off > CameraOffMetres || turn > CameraTurnDegrees || neckHeld;
        if (away)
        {
            if (cameraAwaySince < 0f)
            {
                cameraAwaySince = Time.time;
                cameraOffMost = cameraTurnMost = 0f;
                cameraWhy.Clear();
                cameraAwayState = state;
            }
            cameraBackFor = 0f;
            if (off >= cameraOffMost || turn >= cameraTurnMost)
            {
                cameraOffMost = Mathf.Max(cameraOffMost, off);
                cameraTurnMost = Mathf.Max(cameraTurnMost, turn);
                cameraClip = HeaviestClip();
            }
            NoteWhy(off, neckHeld);
            if (Time.time - cameraAwaySince < CameraLongest) return;
        }
        else
        {
            if (cameraAwaySince < 0f) return;
            cameraBackFor += dt;
            if (cameraBackFor < CameraBackFor) return;
        }
        PlayRecord.Line(CameraRecord, CameraRecordHeading,
            $"{cameraAwayState} {Time.time - cameraAwaySince - cameraBackFor:0.00}s: 눈에서 {cameraOffMost * 100f:0.0}cm, "
            + $"머리 시선에서 {cameraTurnMost:0}°, 클립 {cameraClip}; {(cameraWhy.Length > 0 ? cameraWhy.ToString() : "원인 모름")}");
        cameraAwaySince = away ? Time.time : -1f;
        cameraOffMost = cameraTurnMost = 0f;
        cameraWhy.Clear();
    }

    private void NoteWhy(float off, bool neckHeld)
    {
        void Why(bool happens, string what)
        {
            if (happens && cameraWhy.ToString().IndexOf(what, System.StringComparison.Ordinal) < 0)
                cameraWhy.Append(cameraWhy.Length > 0 ? ", " : "").Append(what);
        }
        Why(bodyCamera.EyeWeight < .99f, "주먹 고정");
        Why(runCalmWeight > .05f, "달리기 흔들림 잡기");
        Why(GameSettings.Current.bodySway < .999f, "몸 흔들림 설정");
        Why(bodyLean.CameraMakesUp > 0f && Mathf.Abs(player.LeanOffset) > .01f, "기울이기");
        Why(bodyCamera.WallCut > .01f, $"벽({bodyCamera.WallCut * 100f:0}cm)");
        Why(GazeClipWeight() > .1f, "동작 클립이 머리를 이끎");
        Why(UpperBodyFromFramework, "총(FPSAF)이 머리를 가짐");
        Why(AimsOnClip > .05f, "조준(오른눈)");
        Why(neckHeld, "목의 한계");
        Why(off > headReachMove, "머리를 데려갈 거리 넘음");
    }

    // The clip with the most weight in the moves mixer, by its name.
    private string HeaviestClip()
    {
        string best = "-";
        float most = 0f;
        foreach (var entry in byName)
            if (entry.Value.Weight > most) { most = entry.Value.Weight; best = entry.Key; }
        return best;
    }
}
