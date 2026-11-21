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
/// Bakes from BIPER Female Runner's jumps (Jump_*, RunJump_*; 0.9.266) where the body leaves the ground, tops out and
/// comes back down to where it left, and how high the body centre is between, read from each clip's own root curve, so
/// the clip's moment follows the game's own flight (PlayerBody.FollowFlight). Since 0.9.285 also the crouch's way down and
/// up (kind posture: IdleToCrouch, CrouchToIdle; PlayerBody.Posture follows the posture's ease on them) and the
/// landing rolls' travel (kind travel: Roll_*, RollToRun_*, the share of the way gone as the clip goes; MovementLanding
/// moves the body at that pace; the dive rolls had it 0.9.285-0.9.288). Written to Tables/Player/GaitMoves.csv by
/// ClipBench (request line bake=gaitmoves); the game only reads it. These are measurements: bake again after a clip
/// changes, never edit by hand. (It baked the start and stop clips until 0.9.263, and the turns on the spot's curves
/// until 0.9.279, when the feet began to step round themselves.)
/// </summary>
internal static class GaitMoveBake
{
    internal const string TablePath = "Assets/_Project/Data/Resources/Tables/Player/GaitMoves.csv";
    // Samples along a jump (its moment is looked up by its height).
    internal const int JumpSamples = 48;

    internal static void Bake(IReadOnlyDictionary<string, AnimationClip> clips, StringBuilder report, float metres)
    {
        var rows = new StringBuilder();
        rows.Append("id,kind,length,profile,leave,top,land,#설명\n");
        rows.Append("\"#설명 (ClipBench가 BIPER Female Runner 클립의 루트 곡선에서 구운 값: 손으로 고치지 않고 클립이 바뀌면 다시 굽는다"
            + "(Temp/enfilade-clipbench.request에 bake=gaitmoves). length 클립 길이(초). profile은 처음부터 끝까지 고르게 " + JumpSamples + "곳(';'). "
            + "kind jump(Jump_*·RunJump_*): profile 몸 중심이 처음보다 높은 정도(꼭대기 = 1), leave 땅을 떠나는 때, top 꼭대기, land 처음 높이로 돌아오는 때(초). "
            + "점프 클립은 이 곡선대로 게임의 비행 높이를 따른다(PlayerBody.FollowFlight). kind posture(IdleToCrouch·CrouchToIdle): profile 몸 중심이 "
            + "처음 높이에서 끝 높이까지 간 정도(0~1), leave 움직이기 시작하는 때(2%), land 다 간 때(98%). 서서 앉고 일어설 때 클립이 자세가 바뀌는 만큼을 "
            + "따른다(PlayerBody.Posture). kind travel(Roll_*·RollToRun_*): profile 루트가 땅 위로 간 길의 몫(끝 = 1), top 몸 중심이 가장 낮은 때, land 몸이 "
            + "다시 선 때(낮아진 만큼의 10% 안으로 돌아옴). 착지 구르기는 이 빠르기로 간다(MovementLanding.RollSpeed). "
            + "kind turn(*_TurnL·R_Lfoot·Rfoot): profile 곳마다 루트가 처음 자리에서 간 x:z(m, 기준 몸, 처음 앞이 +z):루트가 돈 각도(도, + 오른쪽), "
            + "top 다 돈 각도. 달리다 뒤로 돌기는 이 길로 간다(MovementTurnBack))\",,,,,,,\n");
        int jumps = 0, postures = 0, travels = 0, turns = 0;
        foreach (var clip in clips.Values.OrderBy(c => c.name, StringComparer.Ordinal))
        {
            bool jump = clip.name.StartsWith("RunJump_", StringComparison.Ordinal)
                || clip.name.StartsWith("Jump_", StringComparison.Ordinal) && !clip.name.StartsWith("Jump_Climb", StringComparison.Ordinal);
            if (jump && Flight(clip, out float[] rise, out float leave, out float top, out float land))
            {
                rows.Append(string.Join(",", clip.name, "jump", F(clip.length), string.Join(";", rise.Select(F)), F(leave), F(top), F(land), ""));
                rows.Append('\n');
                report.AppendLine(Invariant($"  {clip.name,-20} jump  {clip.length:0.00}s leaves {leave:0.00} tops {top:0.00} lands {land:0.00}"));
                jumps++;
            }
            else if (Array.IndexOf(PostureClips, clip.name) >= 0 && Posture(clip, out float[] share, out float from, out float to))
            {
                rows.Append(string.Join(",", clip.name, "posture", F(clip.length), string.Join(";", share.Select(F)), F(from), "", F(to), ""));
                rows.Append('\n');
                report.AppendLine(Invariant($"  {clip.name,-20} posture  {clip.length:0.00}s sets off {from:0.00} there {to:0.00}"));
                postures++;
            }
            else if ((clip.name.StartsWith("Roll_", StringComparison.Ordinal) || clip.name.StartsWith("RollToRun_", StringComparison.Ordinal))
                    && Travel(clip, out float[] gone, out float low, out float up))
            {
                rows.Append(string.Join(",", clip.name, "travel", F(clip.length), string.Join(";", gone.Select(F)), "", F(low), F(up), ""));
                rows.Append('\n');
                report.AppendLine(Invariant($"  {clip.name,-20} travel  {clip.length:0.00}s lowest {low:0.00} up {up:0.00}"));
                travels++;
            }
            else if (TurnName.IsMatch(clip.name) && Turn(clip, metres, out Vector2[] at, out float[] round))
            {
                string profile = string.Join(";", Enumerable.Range(0, JumpSamples).Select(i => F(at[i].x) + ":" + F(at[i].y) + ":" + F(round[i])));
                rows.Append(string.Join(",", clip.name, "turn", F(clip.length), profile, "", F(round[JumpSamples - 1]), "", ""));
                rows.Append('\n');
                report.AppendLine(Invariant($"  {clip.name,-20} turn  {clip.length:0.00}s turns {round[JumpSamples - 1]:0} to ({at[JumpSamples - 1].x:0.00}, {at[JumpSamples - 1].y:0.00}) m"));
                turns++;
            }
        }
        WriteTable(rows.ToString());
        report.AppendLine($"  baked {jumps} jumps, {postures} postures, {travels} travels and {turns} turns into {TablePath}");
    }

    // The crouch's way down and up, standing still (PlayerBody.Posture).
    private static readonly string[] PostureClips = { "IdleToCrouch", "CrouchToIdle" };

    // A change of posture from its body centre's height (RootT.y): how far of its way from where it began to where it ends
    // it has come (0-1) at JumpSamples evenly spaced moments, when it sets off (past 2%) and when it is there (98%). False
    // for a clip whose body ends about where it began.
    private static bool Posture(AnimationClip clip, out float[] share, out float from, out float to)
    {
        share = null;
        from = to = 0f;
        var height = Curve(clip, "RootT.y");
        if (height == null) return false;
        float length = clip.length, start = height.Evaluate(0f), end = height.Evaluate(length);
        if (Mathf.Abs(end - start) < .05f) return false;
        float Share(float time) => Mathf.Clamp01((height.Evaluate(time) - start) / (end - start));
        share = new float[JumpSamples];
        for (int i = 0; i < JumpSamples; i++) share[i] = Share(length * i / (JumpSamples - 1));
        int steps = Mathf.Max(60, Mathf.CeilToInt(length * 240f));
        from = -1f;
        to = length;
        for (int i = 0; i <= steps; i++)
        {
            float time = length * i / steps, s = Share(time);
            if (from < 0f && s > .02f) from = time;
            if (s >= .98f) { to = time; break; }
        }
        from = Mathf.Max(0f, from);
        return true;
    }

    // A roll's travel from its root's path along the ground (RootT.x, RootT.z): the share of the whole path gone at
    // JumpSamples evenly spaced moments; when its body centre (RootT.y) is lowest, and when it is back up, within a tenth of
    // that dip of where it began. False for a clip that goes nowhere.
    private static bool Travel(AnimationClip clip, out float[] gone, out float low, out float up)
    {
        gone = null;
        low = up = 0f;
        AnimationCurve x = Curve(clip, "RootT.x"), z = Curve(clip, "RootT.z"), y = Curve(clip, "RootT.y");
        if (x == null || z == null || y == null) return false;
        float length = clip.length;
        int steps = Mathf.Max(60, Mathf.CeilToInt(length * 240f));
        var along = new float[steps + 1];
        var last = new Vector2(x.Evaluate(0f), z.Evaluate(0f));
        float start = y.Evaluate(0f), lowest = start;
        int lowAt = 0;
        for (int i = 1; i <= steps; i++)
        {
            float time = length * i / steps;
            var at = new Vector2(x.Evaluate(time), z.Evaluate(time));
            along[i] = along[i - 1] + Vector2.Distance(at, last);
            last = at;
            float height = y.Evaluate(time);
            if (height < lowest) { lowest = height; lowAt = i; }
        }
        float total = along[steps];
        if (total < .05f) return false;
        gone = new float[JumpSamples];
        for (int i = 0; i < JumpSamples; i++)
        {
            float f = (float)i * steps / (JumpSamples - 1);
            int a = Mathf.Min(steps, Mathf.FloorToInt(f)), b = Mathf.Min(steps, a + 1);
            gone[i] = Mathf.Lerp(along[a], along[b], f - a) / total;
        }
        low = length * lowAt / steps;
        up = length;
        float dip = start - lowest;
        for (int i = lowAt; i <= steps; i++)
            if (start - y.Evaluate(length * i / steps) <= dip * .1f) { up = length * i / steps; break; }
        return true;
    }

    // The turns on the run (MovementTurnBack).
    private static readonly System.Text.RegularExpressions.Regex TurnName =
        new System.Text.RegularExpressions.Regex("^(Walk|Jog|Run|Sprint)_Turn[LR]_[LR]foot$");

    // A turn on the run from its root: where the root is at JumpSamples evenly spaced moments, from where it began, in metres
    // on the reference body (x right, y ahead of the way it began facing), and how far round it has turned (deg, + to the
    // right, unwrapped). False for a clip without its root's curves.
    private static bool Turn(AnimationClip clip, float metres, out Vector2[] at, out float[] round)
    {
        at = null;
        round = null;
        AnimationCurve x = Curve(clip, "RootT.x"), z = Curve(clip, "RootT.z");
        AnimationCurve qx = Curve(clip, "RootQ.x"), qy = Curve(clip, "RootQ.y"), qz = Curve(clip, "RootQ.z"), qw = Curve(clip, "RootQ.w");
        if (x == null || z == null || qx == null || qy == null || qz == null || qw == null) return false;
        float Yaw(float time)
        {
            var q = new Quaternion(qx.Evaluate(time), qy.Evaluate(time), qz.Evaluate(time), qw.Evaluate(time)).normalized;
            Vector3 ahead = Vector3.ProjectOnPlane(q * Vector3.forward, Vector3.up);
            return Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg;
        }
        float length = clip.length, start = Yaw(0f);
        var origin = new Vector2(x.Evaluate(0f), z.Evaluate(0f));
        int steps = Mathf.Max(60, Mathf.CeilToInt(length * 240f));
        var unwrapped = new float[steps + 1];
        float last = start;
        for (int i = 1; i <= steps; i++)
        {
            float now = Yaw(length * i / steps);
            unwrapped[i] = unwrapped[i - 1] + Mathf.DeltaAngle(last, now);
            last = now;
        }
        at = new Vector2[JumpSamples];
        round = new float[JumpSamples];
        float r = -start * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        for (int i = 0; i < JumpSamples; i++)
        {
            float time = length * i / (JumpSamples - 1);
            Vector2 gone = (new Vector2(x.Evaluate(time), z.Evaluate(time)) - origin) * metres;
            at[i] = new Vector2(gone.x * c + gone.y * s, -gone.x * s + gone.y * c);
            float f = (float)i * steps / (JumpSamples - 1);
            int a = Mathf.Min(steps, Mathf.FloorToInt(f)), b = Mathf.Min(steps, a + 1);
            round[i] = Mathf.Lerp(unwrapped[a], unwrapped[b], f - a);
        }
        return true;
    }

    private static AnimationCurve Curve(AnimationClip clip, string property)
    {
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            if (binding.type == typeof(Animator) && binding.propertyName == property) return AnimationUtility.GetEditorCurve(clip, binding);
        return null;
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
