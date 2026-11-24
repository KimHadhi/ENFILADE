using System.Text;
using UnityEngine;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// 걸음 기록 (Logs/gait.txt, "걸음 기록 봐 줘"): what the legs do for a moment after a one-off hands the body to the walk or run
/// (a slide's getting up, a roll, a landing on the run: StepFrom) and after the view turns sharply on the move, standing or
/// crouched (2026-11-24: "앉아서 뒤로 가도 몸이 순간 뒤로 쏠릴 때가 있네"), and through a landing roll from its start, a line a
/// frame with how far the body, its hips, its head and its hanging hair went in that frame (2026-11-24: "랜딩 롤 할 때 … 조깅이든
/// 달리기든 모션 바뀔 때 머리카락이 확 튀어서 … 머리카락이 갑자기 텔포하니까 모션이 끊긴 것처럼"),
/// so a change the user finds awkward can be read without playing it (2026-11-24: "슬라이딩 후 조깅 여전히 좀 빠른 것 같은데
/// 그니까 모션이 어색하다고", "달리다가 바로 반대편으로 돌면 캐릭터 모션이 굉장히 우스꽝스러워지는데"). Only this PC's own
/// player, in the editor and development builds (PlayRecord); nothing in it changes the play.
/// </summary>
public sealed partial class PlayerBody
{
    private const string GaitRecord = "gait";
    private const string GaitRecordHeading = "걸음 기록 (PlayerBody.GaitRecord): 한 번 트는 동작에서 걸음으로 넘어갈 때(슬라이딩에서 일어나기, "
        + "구르기, 달리며 착지)와 움직이며(서서 2.2m/s, 앉아서 1m/s 이상) 시선을 확 돌릴 때부터 1.2초 동안 프레임마다 한 줄: 지난 초, 상태, 실제 속도(m/s), 걸음새(0 걷기, 1 조깅, "
        + "2 전력질주)와 다리가 트는 자리, 분당 걸음, 박자와 보폭 배율, 발 미끄러짐(m/s), 가는 쪽(시선 기준 도), 몸이 시선에서 돈 각도, "
        + "시선이 도는 빠르기(도/초), 이 프레임에 몸(모델)·엉덩이·머리가 움직인 거리(cm)와 몸이 돈 각도(도), 머리카락처럼 매달린 뼈가 머리보다 "
        + "더 움직인 가장 큰 거리(cm, 머리카락 물리가 리셋되면 크게 튄다), 넘겨주는 클립과 그 시각, 무게가 큰 클립 셋. 착지 구르기는 시작부터 남긴다";
    // How long a capture goes on (s); the view's turn on the move that starts one (degrees a second, at least m/s standing and
    // crouched).
    private const float GaitRecordFor = 1.2f, SharpViewTurn = 300f, SharpTurnSpeed = 2.2f, SharpTurnCrouched = 1f;
    private float gaitRecordLeft, gaitRecordFrom, gaitViewYaw;
    private readonly StringBuilder gaitLine = new StringBuilder();
    private readonly System.Collections.Generic.List<Channel> gaitTop = new System.Collections.Generic.List<Channel>();
    // Where the body, its hips, its head and each hanging bone (PlayerBodyCloth.Hanging) were on the last frame, and its yaw.
    private Vector3 gaitBodyWas, gaitHipsWas, gaitHeadWas;
    private float gaitBodyYawWas;
    private readonly System.Collections.Generic.Dictionary<Transform, Vector3> gaitHangingWas = new System.Collections.Generic.Dictionary<Transform, Vector3>();
    private PlayerBodyCloth gaitCloth;

    // A capture begins, or goes on from now, saying why.
    private void StartGaitRecord(string why)
    {
        if (!PlayRecord.On || player == null || !player.IsLocal) return;
        if (gaitRecordLeft <= 0f) gaitRecordFrom = Time.time;
        gaitRecordLeft = GaitRecordFor;
        PlayRecord.Line(GaitRecord, GaitRecordHeading, "-- " + why);
    }

    // Last in the frame's pose choice: a line while a capture goes on.
    private void RecordGait(float dt)
    {
        if (!PlayRecord.On || player == null || !player.IsLocal || dt <= 0f) return;
        float viewYaw = transform.eulerAngles.y, viewTurn = Mathf.DeltaAngle(gaitViewYaw, viewYaw) / dt;
        gaitViewYaw = viewYaw;
        float speed = HorizontalSpeed();
        bool onFeet = state == MovementState.Standing || state == MovementState.Crouching;
        if (gaitRecordLeft <= 0f && onFeet && Mathf.Abs(viewTurn) >= SharpViewTurn
            && speed >= (state == MovementState.Crouching ? SharpTurnCrouched : SharpTurnSpeed))
            StartGaitRecord($"시선 확 돌기 {viewTurn:0}°/s, {speed:0.0} m/s, {state}");
        if (gaitRecordLeft <= 0f) return;
        gaitRecordLeft -= dt;
        TryGaitNow(out _, out float steps, out float stretch, out float slide);
        float position = player.Actions != null ? player.Actions.Gait.Position : 0f;
        gaitLine.Clear();
        gaitLine.Append($"+{Time.time - gaitRecordFrom:0.00} {state} {speed:0.00}m/s 걸음새 {position:0.00} 다리 {gaitPlace:0.00} ")
            .Append($"분당 {steps:0} 박자 x{gaitRateNow:0.00} 보폭 x{stretch:0.00} 미끄러짐 {slide:0.00} ")
            .Append($"가는 쪽 {HeadingOf(player.ActualVelocity):0}° 몸 {yaw:0}° 시선 {viewTurn:0}°/s");
        if (transition != null && transition.Playable.IsValid())
            gaitLine.Append($" | 넘겨주는 {transition.Clip}@{transition.Playable.GetTime():0.00}");
        gaitTop.Clear();
        foreach (var channel in channels)
            if (channel.Weight > .05f) gaitTop.Add(channel);
        gaitTop.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        gaitLine.Append(" |");
        for (int i = 0; i < gaitTop.Count && i < 3; i++) gaitLine.Append($" {gaitTop[i].Clip} {gaitTop[i].Weight:0.00}");
        AppendPlaces();
        PlayRecord.Line(GaitRecord, GaitRecordHeading, gaitLine.ToString());
    }

    // How far the body, its hips and head went this frame (cm) and how far its yaw turned; and the most any hanging bone went
    // beyond the head (cm): hair whose simulation is reset jumps there.
    private void AppendPlaces()
    {
        if (body == null || hips == null || head == null) return;
        Vector3 bodyAt = body.position, hipsAt = hips.position, headAt = head.position;
        float bodyYaw = body.eulerAngles.y;
        bool first = gaitRecordFrom == Time.time;
        Vector3 headMoved = headAt - gaitHeadWas;
        if (!first)
            gaitLine.Append($" | 몸 {(bodyAt - gaitBodyWas).magnitude * 100f:0}cm {Mathf.DeltaAngle(gaitBodyYawWas, bodyYaw):0}° 엉덩이 {(hipsAt - gaitHipsWas).magnitude * 100f:0}cm 머리 {headMoved.magnitude * 100f:0}cm");
        if (gaitCloth == null) player.TryGetComponent(out gaitCloth);
        if (gaitCloth != null)
        {
            float most = 0f;
            foreach (var bone in gaitCloth.Hanging)
            {
                if (bone == null) continue;
                Vector3 at = bone.position;
                if (!first && gaitHangingWas.TryGetValue(bone, out var was)) most = Mathf.Max(most, (at - was - headMoved).magnitude);
                gaitHangingWas[bone] = at;
            }
            if (!first) gaitLine.Append($" 매달린 뼈 {most * 100f:0}cm");
        }
        gaitBodyWas = bodyAt;
        gaitHipsWas = hipsAt;
        gaitHeadWas = headAt;
        gaitBodyYawWas = bodyYaw;
    }
}
