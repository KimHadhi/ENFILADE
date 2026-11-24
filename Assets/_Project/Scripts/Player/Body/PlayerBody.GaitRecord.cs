using System.Text;
using UnityEngine;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// 걸음 기록 (Logs/gait.txt, "걸음 기록 봐 줘"): what the legs do for a moment after a one-off hands the body to the walk or run
/// (a slide's getting up, a roll, a landing on the run: StepFrom) and after the view turns sharply on the run, a line a frame,
/// so a change the user finds awkward can be read without playing it (2026-11-24: "슬라이딩 후 조깅 여전히 좀 빠른 것 같은데
/// 그니까 모션이 어색하다고", "달리다가 바로 반대편으로 돌면 캐릭터 모션이 굉장히 우스꽝스러워지는데"). Only this PC's own
/// player, in the editor and development builds (PlayRecord); nothing in it changes the play.
/// </summary>
public sealed partial class PlayerBody
{
    private const string GaitRecord = "gait";
    private const string GaitRecordHeading = "걸음 기록 (PlayerBody.GaitRecord): 한 번 트는 동작에서 걸음으로 넘어갈 때(슬라이딩에서 일어나기, "
        + "구르기, 달리며 착지)와 달리다 시선을 확 돌릴 때부터 1.2초 동안 프레임마다 한 줄: 지난 초, 상태, 실제 속도(m/s), 걸음새(0 걷기, 1 조깅, "
        + "2 전력질주)와 다리가 트는 자리, 분당 걸음, 박자와 보폭 배율, 발 미끄러짐(m/s), 가는 쪽(시선 기준 도), 몸이 시선에서 돈 각도, "
        + "시선이 도는 빠르기(도/초), 넘겨주는 클립과 그 시각, 무게가 큰 클립 셋";
    // How long a capture goes on (s); the view's turn on the run that starts one (degrees a second, at least m/s).
    private const float GaitRecordFor = 1.2f, SharpViewTurn = 300f, SharpTurnSpeed = 2.2f;
    private float gaitRecordLeft, gaitRecordFrom, gaitViewYaw;
    private readonly StringBuilder gaitLine = new StringBuilder();
    private readonly System.Collections.Generic.List<Channel> gaitTop = new System.Collections.Generic.List<Channel>();

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
        if (gaitRecordLeft <= 0f && state == MovementState.Standing && Mathf.Abs(viewTurn) >= SharpViewTurn && speed >= SharpTurnSpeed)
            StartGaitRecord($"시선 확 돌기 {viewTurn:0}°/s, {speed:0.0} m/s");
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
        PlayRecord.Line(GaitRecord, GaitRecordHeading, gaitLine.ToString());
    }
}
