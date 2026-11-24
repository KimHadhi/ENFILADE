using System.Text;
using MagicaCloth2;
using UnityEngine;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// 튐 기록 (Logs/pops.txt, "튐 기록 봐 줘"): the body as it is drawn each frame (after the cloth: MagicaManager.OnPostSimulation),
/// each humanoid bone's turn and place against the body's root, the root's own turn, the eyes against the capsule, and
/// the view's own turn (the clips' share of it, not the mouse's); a bone that turns or moves far faster in one frame than in
/// the frame before is a pop, written with what the body was doing (its state, its speed, the clip a transition plays and its
/// moment, the clips that weigh most), so a pop in any move shows by name without it being looked for (the user, 2026-11-24:
/// "다른 서다가 걷기 전환이나 뭐 움직임 등에 더 근본적으로 고칠 점 있는지 확인"). A fast move held over frames (a tumble, a
/// punch) is no pop. What is drawn is what counts: a frame the root is turned at once with the bones held where they were
/// (BodyInertia.Reroot) has the last frame's pose turned with it (until 0.9.401 the ends of the run's turns showed as pops). Only this PC's own player, in the editor and development builds (PlayRecord); nothing in it changes the play.
/// </summary>
public sealed partial class PlayerBody
{
    private const string PopRecord = "pops";
    private const string PopRecordHeading = "튐 기록 (PlayerBody.PopRecord): 그려지는 몸(천 물리 뒤)에서 뼈가 몸에 대해 한 프레임에 바로 앞 프레임의 "
        + "3배 넘게, 그리고 초당 1800°(돌기)나 초당 8m(움직임) 넘게 바뀌면 한 줄(몸 뿌리를 한 번에 넘긴 프레임은 넘긴 만큼 빼고 잼): 지난 초, 상태, "
        + "속도(m/s), 튄 뼈 셋(돈 각도°·움직인 거리cm), 몸 뿌리가 돈 각도(넘긴 것 빼고), 눈이 캡슐에 대해 움직인 거리(cm), 화면이 마우스 말고 "
        + "제 스스로 돈 각도(동작 클립 몫), 넘겨주는 클립과 그 시각, 무게가 큰 클립 셋, 프레임 길이(ms)";
    // A pop turns faster than PopTurn degrees a second, or moves faster than PopMove m/s, and PopOver times as fast as the frame
    // before; the root turning faster than PopRootTurn, the eyes moving faster than PopEyes against the capsule, or the view
    // turning of itself faster than PopView, likewise.
    private const float PopTurn = 1800f, PopMove = 8f, PopOver = 3f, PopRootTurn = 1200f, PopEyes = 4f, PopView = 720f;
    private const int MostPops = 400;
    private static readonly HumanBodyBones[] PopBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
        HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
        HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
    };
    private Transform[] popBones;
    private Quaternion[] popTurnWas;
    private Vector3[] popPlaceWas;
    private float[] popTurnBefore, popMoveBefore;
    private Quaternion popRootWas, popViewWas;
    private Vector3 popEyesWas;
    private float popRootBefore, popEyesBefore, popViewBefore, popReroot;
    private int popRerootFrame = -1;
    private bool popHooked, popKnown;
    private int popsWritten;
    private readonly StringBuilder popLine = new StringBuilder();
    private readonly System.Collections.Generic.List<(float turn, float move, int bone)> popWorst = new System.Collections.Generic.List<(float, float, int)>();

    // From Update: the record listens to the cloth's end of frame once there is a player of this PC's own to watch.
    private void HookPopRecord()
    {
        if (popHooked || !PlayRecord.On || player == null || !player.IsLocal) return;
        MagicaManager.OnPostSimulation += ReadPops;
        popHooked = true;
    }

    // From Update: the root was turned at once by this much (degrees) with the bones held where they were (BodyInertia.Reroot).
    private void NotePopReroot(float jump)
    {
        popReroot = jump;
        popRerootFrame = Time.frameCount;
    }

    private void UnhookPopRecord()
    {
        if (!popHooked) return;
        MagicaManager.OnPostSimulation -= ReadPops;
        popHooked = false;
        popKnown = false;
        popBones = null;
    }

    private void ReadPops()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || body == null || animator == null || player == null || !animator.isHuman) return;
        if (popBones == null)
        {
            popBones = new Transform[PopBones.Length];
            for (int i = 0; i < PopBones.Length; i++) popBones[i] = animator.GetBoneTransform(PopBones[i]);
            popTurnWas = new Quaternion[PopBones.Length];
            popPlaceWas = new Vector3[PopBones.Length];
            popTurnBefore = new float[PopBones.Length];
            popMoveBefore = new float[PopBones.Length];
            popKnown = false;
        }
        Quaternion into = Quaternion.Inverse(body.rotation);
        // A root turned at once this frame turns the last frame's pose against it as much: the bones were held in the world.
        if (popRerootFrame == Time.frameCount && popKnown)
        {
            Quaternion turned = Quaternion.Euler(0f, -popReroot, 0f);
            for (int i = 0; i < popTurnWas.Length; i++)
            {
                popTurnWas[i] = turned * popTurnWas[i];
                popPlaceWas[i] = turned * popPlaceWas[i];
            }
            popRootWas = Quaternion.Euler(0f, popReroot, 0f) * popRootWas;
        }
        Vector3 eyes = bodyCamera.HasPlace ? bodyCamera.Placed - transform.position : Vector3.zero;
        Quaternion view = Quaternion.Inverse(bodyGaze.Input) * bodyGaze.Rotation;
        float rootTurn = popKnown ? Quaternion.Angle(popRootWas, body.rotation) / dt : 0f;
        float eyesMove = popKnown ? (eyes - popEyesWas).magnitude / dt : 0f;
        float viewTurn = popKnown ? Quaternion.Angle(popViewWas, view) / dt : 0f;
        bool popped = popKnown && ((rootTurn > PopRootTurn && rootTurn > PopOver * popRootBefore)
            || (eyesMove > PopEyes && eyesMove > PopOver * popEyesBefore)
            || (viewTurn > PopView && viewTurn > PopOver * popViewBefore));
        popWorst.Clear();
        for (int i = 0; i < popBones.Length; i++)
        {
            var bone = popBones[i];
            if (bone == null) continue;
            Quaternion turn = into * bone.rotation;
            Vector3 place = into * (bone.position - body.position);
            float turned = popKnown ? Quaternion.Angle(popTurnWas[i], turn) / dt : 0f;
            float moved = popKnown ? (place - popPlaceWas[i]).magnitude / dt : 0f;
            if (popKnown && ((turned > PopTurn && turned > PopOver * popTurnBefore[i]) || (moved > PopMove && moved > PopOver * popMoveBefore[i])))
                popWorst.Add((turned * dt, moved * dt, i));
            popTurnWas[i] = turn;
            popPlaceWas[i] = place;
            popTurnBefore[i] = turned;
            popMoveBefore[i] = moved;
        }
        popRootWas = body.rotation;
        popEyesWas = eyes;
        popViewWas = view;
        popRootBefore = rootTurn;
        popEyesBefore = eyesMove;
        popViewBefore = viewTurn;
        popKnown = true;
        if ((!popped && popWorst.Count == 0) || popsWritten >= MostPops) return;
        popsWritten++;
        popWorst.Sort((a, b) => b.turn.CompareTo(a.turn));
        popLine.Clear().Append($"{state} {HorizontalSpeed():0.0}m/s |");
        for (int k = 0; k < popWorst.Count && k < 3; k++)
            popLine.Append($" {PopBones[popWorst[k].bone]} {popWorst[k].turn:0}° {popWorst[k].move * 100f:0}cm");
        popLine.Append($" | 뿌리 {rootTurn * dt:0}° 눈 {eyesMove * dt * 100f:0}cm 화면 {viewTurn * dt:0}°");
        if (transition != null && transition.Playable.IsValid()) popLine.Append($" | 넘겨주는 {transition.Clip}@{transition.Playable.GetTime():0.00}");
        popLine.Append(" |");
        int shown = 0;
        foreach (var channel in channels)
            if (channel.Weight > .1f && shown++ < 3) popLine.Append($" {channel.Clip} {channel.Weight:0.00}");
        popLine.Append($" | {dt * 1000f:0}ms");
        PlayRecord.Line(PopRecord, PopRecordHeading, popLine.ToString());
    }
}
