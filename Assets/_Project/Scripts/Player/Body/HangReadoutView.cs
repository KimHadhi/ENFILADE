using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

// dev-text: file (a developer's console readout; nothing here is player text)

/// <summary>
/// Console /hang: the ledge hang measured, so a change to it can be judged by numbers before and after (user, 2026-11-13:
/// the catch popped, the hands held oddly, the feet went through the wall). After this frame's pose, hands and cloth:
/// - each catch, from just before its hang begins until a moment after its hands hold: the most the eyes (the camera), the
///   capsule and the wrists were jerked (acceleration, m/s²), the biggest jump of the eyes off the path they were on (cm),
///   and how fast the wrists went; each catch also goes to the console;
/// - while hanging, each palm: its skin's gap over the top under it (mm; below zero, sunk in), how far in from the edge it
///   rests, how far it is from where its hold wants it (the IK's error) and whether it is moving to another hold;
/// - each leg: how deep its knee, ankle, toes, the sole under them and its heel are in the real wall under the ledge (cm,
///   HangWall, as the legs keep off it: PlayerBody.HangLegReadout), and how far its toes stand off the wall.
/// Typed again, it goes and leaves the worst of each since it came, copied for pasting back.
/// </summary>
[DefaultExecutionOrder(FrameOrder.Cloth + 5)]
internal sealed class HangReadoutView : MonoBehaviour
{
    // A catch is watched from this long before its hang begins to this long after its hands hold (s); deeper than Deep in
    // the wall counts as through it (m).
    private const float Before = .1f, After = .3f, Deep = .01f;
    private const float Width = 860f, Margin = 12f, LineHeight = 20f;
    private const int KeptCatches = 4;
    private static readonly string[] LegParts = { "무릎", "발목", "발끝", "발끝 밑창", "뒤꿈치" };

    private struct Frame { public float Time; public Vector3 Eye, Capsule, LeftWrist, RightWrist; }
    private struct Jerk { public float Time, Eye, Capsule, Wrist, Speed, Jump; }

    private PlayerMovement player;
    private PlayerBody body;
    private PlayerBodyHands hands;
    private Transform view;
    private string text = "";
    private readonly Frame[] frames = new Frame[3];
    private int framesSeen;
    private readonly Queue<Jerk> recent = new Queue<Jerk>();
    private MovementState lastState;

    // The catch being watched, and the last few.
    private bool catching;
    private float catchBegan, catchTime;
    private Jerk catchWorst;
    private int catches;
    private readonly List<string> catchLines = new List<string>();

    // The worst since it came: the catches', the palms' and the legs'; and how long the hands held, and the legs were in.
    private float worstEye, sumEye, worstJump, palmLow = float.PositiveInfinity, palmHigh = float.NegativeInfinity, worstIk;
    private float heldSeconds, deepSeconds;
    private readonly float[] worstLeg = new float[5], depths = new float[5];
    private static readonly RaycastHit[] Hits = new RaycastHit[8];

    /// <summary>Shows the box for <paramref name="player"/>, or, if it is up, takes it down and returns the worst of each.</summary>
    internal static string Toggle(PlayerMovement player)
    {
        if (player == null) return "플레이어가 없습니다.";
        if (player.TryGetComponent(out HangReadoutView shown))
        {
            string result = shown.Result();
            Destroy(shown);
            GUIUtility.systemCopyBuffer = result;
            return result + " (복사됨)";
        }
        if (!player.TryGetComponent(out PlayerBody body) || !player.TryGetComponent(out PlayerBodyHands hands)) return "몸이 없습니다.";
        var view = player.gameObject.AddComponent<HangReadoutView>();
        view.player = player;
        view.body = body;
        view.hands = hands;
        view.lastState = player.CurrentState;
        return "매달리기 수치를 켰습니다. 난간을 잡고 매달리고 옆으로 간 뒤 /hang를 다시 입력하면 가장 나빴던 값이 나옵니다.";
    }

    private void LateUpdate()
    {
        if (player == null || body == null || hands == null) { Destroy(this); return; }
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (view == null)
        {
            var camera = player.GetComponentInChildren<Camera>();
            view = camera != null ? camera.transform : null;
        }
        Track();
        var state = player.CurrentState;
        if (state == MovementState.LedgeHang && lastState != MovementState.LedgeHang) BeginCatch();
        lastState = state;
        if (catching && (state != MovementState.LedgeHang || Time.time - catchBegan >= catchTime + After)) EndCatch();

        var lines = new StringBuilder("매달리기 측정 (/hang 다시 입력하면 끄고 요약)");
        var ledge = player.Abilities.Traversal.Ledge;
        if (state == MovementState.LedgeHang && ledge.Surface != null)
        {
            float share = body.LedgeCatchShare;
            lines.Append($"\n잡기: 손이 닿기까지 {player.Abilities.Traversal.LedgeCatchTime:0.00}초 · 지금 {share * 100f:0}%");
            Transform support = ledge.Surface.transform;
            Vector3 inward = Vector3.ProjectOnPlane(-support.TransformDirection(ledge.LocalNormal), Vector3.up).normalized;
            Vector3 edge = support.TransformPoint(ledge.LocalEdge);
            bool held = share >= 1f;
            if (held) heldSeconds += dt;
            lines.Append('\n').Append(HandLine(true, edge, inward, held));
            lines.Append('\n').Append(HandLine(false, edge, inward, held));
            bool deep = false;
            lines.Append('\n').Append(LegLine(true, held, ref deep));
            lines.Append('\n').Append(LegLine(false, held, ref deep));
            if (deep) deepSeconds += dt;
        }
        else lines.Append("\n매달려 있지 않음");
        foreach (string line in catchLines) lines.Append('\n').Append(line);
        text = lines.ToString();
    }

    // This frame's places, and from the last three, how much each was jerked and how far the eyes jumped off their path.
    private void Track()
    {
        var animator = body.BodyAnimator;
        Transform leftWrist = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        Transform rightWrist = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        frames[0] = frames[1];
        frames[1] = frames[2];
        frames[2] = new Frame
        {
            Time = Time.time, Eye = view != null ? view.position : player.transform.position, Capsule = player.transform.position,
            LeftWrist = leftWrist != null ? leftWrist.position : player.transform.position,
            RightWrist = rightWrist != null ? rightWrist.position : player.transform.position
        };
        if (++framesSeen < 3) return;
        Frame a = frames[0], b = frames[1], c = frames[2];
        float dt1 = b.Time - a.Time, dt2 = c.Time - b.Time;
        if (dt1 <= 0f || dt2 <= 0f) return;
        var jerk = new Jerk
        {
            Time = c.Time,
            Eye = Acceleration(a.Eye, b.Eye, c.Eye, dt1, dt2),
            Capsule = Acceleration(a.Capsule, b.Capsule, c.Capsule, dt1, dt2),
            Wrist = Mathf.Max(Acceleration(a.LeftWrist, b.LeftWrist, c.LeftWrist, dt1, dt2), Acceleration(a.RightWrist, b.RightWrist, c.RightWrist, dt1, dt2)),
            Speed = Mathf.Max((c.LeftWrist - b.LeftWrist).magnitude, (c.RightWrist - b.RightWrist).magnitude) / dt2,
            Jump = (c.Eye - (b.Eye + (b.Eye - a.Eye) * (dt2 / dt1))).magnitude
        };
        recent.Enqueue(jerk);
        while (recent.Count > 0 && c.Time - recent.Peek().Time > Before) recent.Dequeue();
        if (catching) Fold(jerk);
    }

    private static float Acceleration(Vector3 a, Vector3 b, Vector3 c, float dt1, float dt2)
        => ((c - b) / dt2 - (b - a) / dt1).magnitude / ((dt1 + dt2) * .5f);

    private void BeginCatch()
    {
        catching = true;
        catchBegan = Time.time;
        catchTime = player.Abilities.Traversal.LedgeCatchTime;
        catchWorst = default;
        // The frames just before count too: a catch that snaps may do it on its very first frame.
        foreach (var jerk in recent) Fold(jerk);
    }

    private void Fold(in Jerk jerk)
    {
        catchWorst.Eye = Mathf.Max(catchWorst.Eye, jerk.Eye);
        catchWorst.Capsule = Mathf.Max(catchWorst.Capsule, jerk.Capsule);
        catchWorst.Wrist = Mathf.Max(catchWorst.Wrist, jerk.Wrist);
        catchWorst.Speed = Mathf.Max(catchWorst.Speed, jerk.Speed);
        catchWorst.Jump = Mathf.Max(catchWorst.Jump, jerk.Jump);
    }

    private void EndCatch()
    {
        catching = false;
        catches++;
        worstEye = Mathf.Max(worstEye, catchWorst.Eye);
        sumEye += catchWorst.Eye;
        worstJump = Mathf.Max(worstJump, catchWorst.Jump);
        string line = $"잡기 {catches}: 손이 닿기까지 {catchTime:0.00}초 · 눈 가속 최대 {catchWorst.Eye:0} m/s² · 눈 튐 {catchWorst.Jump * 100f:0.0} cm"
            + $" · 캡슐 가속 {catchWorst.Capsule:0} m/s² · 손목 가속 {catchWorst.Wrist:0} m/s² · 손목 속도 최대 {catchWorst.Speed:0.0} m/s";
        catchLines.Add(line);
        if (catchLines.Count > KeptCatches) catchLines.RemoveAt(0);
        Debug.Log("[/hang] " + line, player);
    }

    // A palm against the top under it and the edge it holds; counted toward the worst only while it holds still.
    private string HandLine(bool isLeft, Vector3 edge, Vector3 inward, bool held)
    {
        string name = isLeft ? "왼손" : "오른손";
        if (!hands.LedgeReadout(isLeft, out Vector3 palm, out Vector3 wanted, out float hold, out bool moving)) return name + ": 난간을 잡지 않음";
        float ik = Vector3.Distance(palm, wanted), inFromEdge = Vector3.Dot(palm - edge, inward);
        bool still = held && !moving && hold >= .99f;
        string over = "밑에 윗면 없음";
        if (TopUnder(palm, out RaycastHit top))
        {
            float gap = Vector3.Dot(palm - top.point, top.normal) - hands.PalmDepth;
            over = "윗면 위 " + (gap * 1000f).ToString("+0.0;-0.0") + " mm";
            if (still)
            {
                palmLow = Mathf.Min(palmLow, gap);
                palmHigh = Mathf.Max(palmHigh, gap);
            }
        }
        if (still) worstIk = Mathf.Max(worstIk, ik);
        return $"{name}: {over} · 모서리 안쪽 {inFromEdge * 100f:0.0} cm · IK 오차 {ik * 1000f:0.0} mm · 잡은 정도 {hold * 100f:0}%"
            + (moving ? " · 옮겨 잡는 중" : "");
    }

    private string LegLine(bool isLeft, bool held, ref bool deep)
    {
        string name = isLeft ? "왼다리" : "오른다리";
        if (!body.HangLegReadout(isLeft, depths, out float toesOff)) return name + ": 재지 못함";
        var line = new StringBuilder(name).Append(": 벽에 박힌 깊이");
        float most = 0f;
        for (int i = 0; i < depths.Length; i++)
        {
            line.Append(i == 0 ? " " : " · ").Append(LegParts[i]).Append(' ').Append((depths[i] * 100f).ToString("0.0"));
            most = Mathf.Max(most, depths[i]);
            if (held) worstLeg[i] = Mathf.Max(worstLeg[i], depths[i]);
        }
        line.Append(" cm · 발끝과 벽 ").Append(float.IsPositiveInfinity(toesOff) ? "벽 없음" : (toesOff * 100f).ToString("0.0") + " cm");
        if (held && most > Deep) deep = true;
        return line.ToString();
    }

    // The first top under a palm that isn't the player's own.
    private bool TopUnder(Vector3 palm, out RaycastHit top)
    {
        top = default;
        int count = Physics.RaycastNonAlloc(palm + Vector3.up * .15f, Vector3.down, Hits, .4f, HangWall.Layers(player.groundLayers), QueryTriggerInteraction.Ignore);
        int nearest = -1;
        for (int i = 0; i < count; i++)
            if ((nearest < 0 || Hits[i].distance < Hits[nearest].distance) && !Hits[i].collider.transform.IsChildOf(player.transform))
                nearest = i;
        if (nearest < 0) return false;
        top = Hits[nearest];
        return true;
    }

    private string Result()
    {
        if (catching) EndCatch();
        var result = new StringBuilder("매달리기 측정 요약");
        if (catches > 0)
            result.Append($": 잡기 {catches}번 · 눈 가속 최대 {worstEye:0} m/s²(평균 {sumEye / catches:0}) · 눈 튐 최대 {worstJump * 100f:0.0} cm");
        else result.Append(": 잡기 없음");
        if (palmLow <= palmHigh)
            result.Append(" · 손바닥과 윗면 틈 ").Append((palmLow * 1000f).ToString("+0.0;-0.0")).Append('~')
                .Append((palmHigh * 1000f).ToString("+0.0;-0.0")).Append(" mm");
        result.Append($" · 손 IK 오차 최대 {worstIk * 1000f:0.0} mm · 다리 박힘 최대");
        for (int i = 0; i < worstLeg.Length; i++) result.Append(i == 0 ? " " : " · ").Append(LegParts[i]).Append(' ').Append((worstLeg[i] * 100f).ToString("0.0"));
        result.Append(" cm");
        if (heldSeconds > 0f) result.Append($" · 매달린 {heldSeconds:0.0}초 중 1 cm 넘게 박힌 시간 {deepSeconds / heldSeconds * 100f:0}%");
        return result.ToString();
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(text)) return;
        int lines = 1;
        foreach (char c in text) if (c == '\n') lines++;
        GUILayout.BeginArea(new Rect(Margin, Screen.height * .3f, Width, LineHeight * (lines + 1)), GUI.skin.box);
        foreach (string line in text.Split('\n')) GUILayout.Label(line);
        GUILayout.EndArea();
    }
}
