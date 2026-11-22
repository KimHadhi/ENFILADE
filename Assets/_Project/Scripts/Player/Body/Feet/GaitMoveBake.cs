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
/// Bakes from BIPER Female Runner's clips, off each one's own root curve, the crouch's way down and up (kind posture:
/// IdleToCrouch, CrouchToIdle; PlayerBody.Posture follows the posture's ease on them) and the travel of the moves the body
/// goes through at its clip's own pace (kind travel: the landing rolls Roll_* and RollToRun_*, the slide's jump DiveRoll_F,
/// the dodges QuickShift_* and ShiftToSprint_*: how far it goes and the share of that gone as the clip goes, ClipTravel).
/// Written to Tables/Player/GaitMoves.csv by ClipBench (request line bake=gaitmoves); the game only reads it. These are
/// measurements: bake again after a clip changes, never edit by hand. (It baked the start and stop clips until 0.9.263,
/// the turns on the spot's curves until 0.9.279 and the jumps' flights until 0.9.333: the jump is one Beatrun clip on its
/// own clock.)
/// </summary>
internal static class GaitMoveBake
{
    internal const string TablePath = "Assets/_Project/Data/Resources/Tables/Player/GaitMoves.csv";
    // Samples along a clip.
    internal const int Samples = 48;

    // The moves whose travel the body goes at: every clip that begins with one of these (one that is a whole name, itself).
    private static readonly string[] TravelClips = { "Roll_", "RollToRun_", "QuickShift_", "ShiftToSprint_", "DiveRoll_F" };

    // metres: what one unit of a clip's root curve is on the body the numbers are for (its human scale at the game's size).
    internal static void Bake(IReadOnlyDictionary<string, AnimationClip> clips, float metres, StringBuilder report)
    {
        var rows = new StringBuilder();
        rows.Append("id,kind,length,profile,leave,top,land,metres,#설명\n");
        rows.Append("\"#설명 (ClipBench가 BIPER Female Runner 클립의 루트 곡선에서 구운 값: 손으로 고치지 않고 클립이 바뀌면 다시 굽는다"
            + "(Temp/enfilade-clipbench.request에 bake=gaitmoves). length 클립 길이(초). profile은 처음부터 끝까지 고르게 " + Samples + "곳(';'). "
            + "kind posture(IdleToCrouch·CrouchToIdle): profile 몸 중심이 "
            + "처음 높이에서 끝 높이까지 간 정도(0~1), leave 움직이기 시작하는 때(2%), land 다 간 때(98%). 서서 앉고 일어설 때 클립이 자세가 바뀌는 만큼을 "
            + "따른다(PlayerBody.Posture). kind travel(Roll_*·RollToRun_*·QuickShift_*·ShiftToSprint_*·DiveRoll_F): profile 루트가 땅 위로 간 길의 몫(끝 = 1), "
            + "top 몸 중심이 가장 낮은 때, land 몸이 다시 선 때(낮아진 만큼의 10% 안으로 돌아옴), metres 기준 몸(이치고)에서 간 길이(m). "
            + "구르기·다이브 롤·회피는 이 길이를 이 빠르기로 간다(ClipTravel))\",,,,,,,,\n");
        int postures = 0, travels = 0;
        foreach (var clip in clips.Values.OrderBy(c => c.name, StringComparer.Ordinal))
        {
            if (Array.IndexOf(PostureClips, clip.name) >= 0 && Posture(clip, out float[] share, out float from, out float to))
            {
                rows.Append(string.Join(",", clip.name, "posture", F(clip.length), string.Join(";", share.Select(F)), F(from), "", F(to), "", ""));
                rows.Append('\n');
                report.AppendLine(Invariant($"  {clip.name,-20} posture  {clip.length:0.00}s sets off {from:0.00} there {to:0.00}"));
                postures++;
            }
            else if (TravelClips.Any(t => clip.name.StartsWith(t, StringComparison.Ordinal))
                    && Travel(clip, out float[] gone, out float way, out float low, out float up))
            {
                rows.Append(string.Join(",", clip.name, "travel", F(clip.length), string.Join(";", gone.Select(F)), "", F(low), F(up), F(way * metres), ""));
                rows.Append('\n');
                report.AppendLine(Invariant($"  {clip.name,-20} travel  {clip.length:0.00}s {way * metres:0.00} m, lowest {low:0.00} up {up:0.00}"));
                travels++;
            }
        }
        WriteTable(rows.ToString());
        report.AppendLine($"  baked {postures} postures and {travels} travels into {TablePath}");
    }

    // The crouch's way down and up, standing still (PlayerBody.Posture).
    private static readonly string[] PostureClips = { "IdleToCrouch", "CrouchToIdle" };

    // A change of posture from its body centre's height (RootT.y): how far of its way from where it began to where it ends
    // it has come (0-1) at Samples evenly spaced moments, when it sets off (past 2%) and when it is there (98%). False
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
        share = new float[Samples];
        for (int i = 0; i < Samples; i++) share[i] = Share(length * i / (Samples - 1));
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

    // A move's travel from its root's path along the ground (RootT.x, RootT.z): its length, the share of it gone at
    // Samples evenly spaced moments; when its body centre (RootT.y) is lowest, and when it is back up, within a tenth of
    // that dip of where it began. False for a clip that goes nowhere.
    private static bool Travel(AnimationClip clip, out float[] gone, out float way, out float low, out float up)
    {
        gone = null;
        way = low = up = 0f;
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
        way = total;
        gone = new float[Samples];
        for (int i = 0; i < Samples; i++)
        {
            float f = (float)i * steps / (Samples - 1);
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

    private static AnimationCurve Curve(AnimationClip clip, string property)
    {
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            if (binding.type == typeof(Animator) && binding.propertyName == property) return AnimationUtility.GetEditorCurve(clip, binding);
        return null;
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
