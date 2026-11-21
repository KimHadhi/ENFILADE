#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes from BIPER Female Runner's clips what the game follows them by, read from each clip's own root curves:
/// - turns on the spot (Female Runner's TurnIP_L/R 45, 90, 135, 180; Combat Girls' gun turns P_/R_/S_AimTurn_L90/R90,
///   0.9.275): how far round the root is through the clip and its whole turn, so the legs turn by that very curve while the
///   clip steps them round (PlayerBody.TurnOnTheSpot), and where the turn starts and is done (start, end): the clips stand
///   a while before and after it, and the game plays only between;
/// - jumps (Jump_*, RunJump_*; 0.9.266): where the body leaves the ground, tops out and comes back down to where it left,
///   and how high the body centre is between, so the clip's moment follows the game's own flight (PlayerBody.FollowFlight).
/// Written to Tables/Player/GaitMoves.csv by ClipBench (request line bake=gaitmoves); the game only reads it. These are
/// measurements: bake again after a clip changes, never edit by hand. (Until 0.9.263 it baked the start and stop clips too,
/// for the starts and stops that went then.)
/// </summary>
internal static class GaitMoveBake
{
    internal const string TablePath = "Assets/_Project/Data/Resources/Tables/Player/GaitMoves.csv";
    // Samples along a turn, and along a jump (a jump's moment is looked up by its height, so it is read finer).
    internal const int Samples = 24, JumpSamples = 48;
    private static readonly string[] Sides = { "L", "R" };

    internal static void Bake(IReadOnlyDictionary<string, AnimationClip> clips, StringBuilder report)
    {
        var rows = new StringBuilder();
        rows.Append("id,kind,length,profile,turn,leave,top,land,start,end,#설명\n");
        rows.Append("\"#설명 (ClipBench가 BIPER Female Runner의 클립에서 구운 값: 손으로 고치지 않고 클립이 바뀌면 다시 굽는다"
            + "(Temp/enfilade-clipbench.request에 bake=gaitmoves). length 클립 길이(초). kind turn 제자리 돌기(TurnIP_*): profile 루트가 그때까지 돈 몫(0~1), "
            + "클립 길이를 고르게 나눈 " + Samples + "곳의 끝에서(';'), turn 그 클립이 도는 각도(+ 오른쪽), start 돌기 시작(" + StartShare * 100f
            + "%가 되기 " + StartLead + "초 전: 첫 발을 드는 때), end 다 돈 때(" + EndShare * 100f + "%). 게임은 start부터 end까지만 틀고, 다리는 이 곡선대로 돈다"
            + "(PlayerBody.TurnOnTheSpot). "
            + "kind jump 점프(Jump_*·RunJump_*): profile 몸 중심이 처음보다 높은 정도(꼭대기 = 1), 처음부터 끝까지 고르게 " + JumpSamples + "곳(';'), "
            + "leave 땅을 떠나는 때, top 꼭대기, land 처음 높이로 돌아오는 때(초). 점프 클립은 이 곡선대로 게임의 비행 높이를 따른다(PlayerBody.FollowFlight))\",,,,,,,,,,\n");
        int baked = 0;
        var turns = new List<string>();
        foreach (string side in Sides)
            foreach (int angle in new[] { 45, 90, 135, 180 })
                turns.Add($"TurnIP_{side}{angle}");
        foreach (string gun in new[] { "P", "R", "S" })
            foreach (string side in Sides)
                turns.Add($"{gun}_AimTurn_{side}90");
        foreach (string name in turns)
        {
            if (!clips.TryGetValue(name, out var clip)) continue;
            var round = Round(clip, out float whole);
            Span(clip, out float start, out float end);
            rows.Append(string.Join(",", name, "turn", F(clip.length), string.Join(";", round.Select(F)), F(whole), "", "", "", F(start), F(end), ""));
            rows.Append('\n');
            report.AppendLine(Invariant($"  {name,-20} turn  {clip.length:0.00}s turns {whole:0.0} from {start:0.00} to {end:0.00}s share {string.Join(" ", round.Where((_, i) => i % 4 == 3).Select(v => v.ToString("0.00", CultureInfo.InvariantCulture)))}"));
            baked++;
        }
        int jumps = 0;
        foreach (var clip in clips.Values.OrderBy(c => c.name, StringComparer.Ordinal))
        {
            bool jump = clip.name.StartsWith("RunJump_", StringComparison.Ordinal)
                || clip.name.StartsWith("Jump_", StringComparison.Ordinal) && !clip.name.StartsWith("Jump_Climb", StringComparison.Ordinal);
            if (!jump || !Flight(clip, out float[] rise, out float leave, out float top, out float land)) continue;
            rows.Append(string.Join(",", clip.name, "jump", F(clip.length), string.Join(";", rise.Select(F)), "", F(leave), F(top), F(land), "", "", ""));
            rows.Append('\n');
            report.AppendLine(Invariant($"  {clip.name,-20} jump  {clip.length:0.00}s leaves {leave:0.00} tops {top:0.00} lands {land:0.00}"));
            jumps++;
        }
        WriteTable(rows.ToString());
        report.AppendLine($"  baked {baked} turns on the spot and {jumps} jumps into {TablePath}");
    }

    // A jump's flight from its body centre's height (RootT.y): its rise at JumpSamples evenly spaced moments, as a share of
    // the top; where it leaves the ground (the last moment before the top that is no higher than where it began: after the
    // dip of the push), its top, and where it comes back down to where it began (its end if it never does). False for a
    // clip that never rises.
    private static bool Flight(AnimationClip clip, out float[] rise, out float leave, out float top, out float land)
    {
        rise = null;
        leave = top = land = 0f;
        AnimationCurve height = null;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            if (binding.type == typeof(Animator) && binding.propertyName == "RootT.y") height = AnimationUtility.GetEditorCurve(clip, binding);
        if (height == null) return false;
        float start = height.Evaluate(0f), length = clip.length;
        int steps = Mathf.Max(60, Mathf.CeilToInt(length * 240f));
        var along = new float[steps + 1];
        int highest = 0;
        for (int i = 0; i <= steps; i++)
        {
            along[i] = height.Evaluate(length * i / steps) - start;
            if (along[i] > along[highest]) highest = i;
        }
        float peak = along[highest];
        if (peak < .05f) return false;
        float At(int i) => length * i / steps;
        // Where a crossing of the start's height falls between two steps.
        float Crossing(int a, int b) => Mathf.Lerp(At(a), At(b), Mathf.InverseLerp(along[a], along[b], 0f));
        top = At(highest);
        leave = 0f;
        for (int i = highest; i > 0; i--)
            if (along[i - 1] <= 0f) { leave = Crossing(i - 1, i); break; }
        land = length;
        for (int i = highest; i < steps; i++)
            if (along[i + 1] <= 0f) { land = Crossing(i, i + 1); break; }
        rise = new float[JumpSamples];
        for (int i = 0; i < JumpSamples; i++)
            rise[i] = (height.Evaluate(length * i / (JumpSamples - 1)) - start) / peak;
        return true;
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // A turn starts this long before its root has turned StartShare of the whole (the first foot comes up before the hips
    // turn), and is done once it has turned EndShare.
    private const float StartShare = .03f, StartLead = .15f, EndShare = .98f;

    // Where a turn starts and is done (s), read finely from its root's turn.
    private static void Span(AnimationClip clip, out float start, out float end)
    {
        start = 0f;
        end = clip.length;
        var fine = Turned(clip, 240, out float whole);
        if (fine == null || Mathf.Abs(whole) < 1f) return;
        int steps = fine.Length - 1;
        // Its own way only: some first turn a little the other way as the weight shifts (TurnIP_R135 -9%).
        int first = Array.FindIndex(fine, v => v / whole >= StartShare);
        int done = Array.FindIndex(fine, v => v / whole >= EndShare);
        if (first > 0) start = Mathf.Max(0f, clip.length * first / steps - StartLead);
        if (done > 0) end = clip.length * done / steps;
    }

    // How far round the root has turned by the end of each of Samples shares of the clip (0-1 of the whole), and the whole
    // (degrees, + to the right; a half turn reads as the way it went).
    private static float[] Round(AnimationClip clip, out float whole)
    {
        var round = new float[Samples];
        var along = Turned(clip, Samples * 8, out whole);
        if (along == null) return round;
        for (int i = 0; i < Samples; i++)
            round[i] = Mathf.Abs(whole) > 1e-3f ? along[(i + 1) * 8] / whole : 0f;
        return round;
    }

    // The root's turn summed from the start at each of steps+1 evenly spaced moments (degrees), and the whole; null without
    // root curves.
    private static float[] Turned(AnimationClip clip, int steps, out float whole)
    {
        whole = 0f;
        AnimationCurve qx = null, qy = null, qz = null, qw = null;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type != typeof(Animator)) continue;
            switch (binding.propertyName)
            {
                case "RootQ.x": qx = AnimationUtility.GetEditorCurve(clip, binding); break;
                case "RootQ.y": qy = AnimationUtility.GetEditorCurve(clip, binding); break;
                case "RootQ.z": qz = AnimationUtility.GetEditorCurve(clip, binding); break;
                case "RootQ.w": qw = AnimationUtility.GetEditorCurve(clip, binding); break;
            }
        }
        if (qx == null || qy == null || qz == null || qw == null) return null;
        float Yaw(float time)
        {
            var q = new Quaternion(qx.Evaluate(time), qy.Evaluate(time), qz.Evaluate(time), qw.Evaluate(time)).normalized;
            Vector3 ahead = Vector3.ProjectOnPlane(q * Vector3.forward, Vector3.up);
            return Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg;
        }
        // Summed in small steps, so a half turn and more is followed round rather than read as its shortest way.
        float last = Yaw(0f), turned = 0f;
        var along = new float[steps + 1];
        for (int i = 1; i <= steps; i++)
        {
            float now = Yaw(clip.length * i / steps);
            turned += Mathf.DeltaAngle(last, now);
            last = now;
            along[i] = turned;
        }
        whole = turned;
        return along;
    }

    // A table the editor may hold open is written in place, its old tail cut after (a shorter text written over a mapped
    // file left the old one's end behind it, 2026-11-14 and 2026-11-21).
    private static void WriteTable(string text)
    {
        byte[] bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        for (int attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                using (var stream = new FileStream(TablePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.SetLength(bytes.Length);
                }
                return;
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(100);
            }
        }
        Debug.LogWarning($"GaitMoveBake: could not write {TablePath}.");
    }
}
#endif
