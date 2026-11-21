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
/// the clip's moment follows the game's own flight (PlayerBody.FollowFlight). Written to Tables/Player/GaitMoves.csv by
/// ClipBench (request line bake=gaitmoves); the game only reads it. These are measurements: bake again after a clip
/// changes, never edit by hand. (It baked the start and stop clips until 0.9.263, and the turns on the spot's curves
/// until 0.9.279, when the feet began to step round themselves.)
/// </summary>
internal static class GaitMoveBake
{
    internal const string TablePath = "Assets/_Project/Data/Resources/Tables/Player/GaitMoves.csv";
    // Samples along a jump (its moment is looked up by its height).
    internal const int JumpSamples = 48;

    internal static void Bake(IReadOnlyDictionary<string, AnimationClip> clips, StringBuilder report)
    {
        var rows = new StringBuilder();
        rows.Append("id,kind,length,profile,leave,top,land,#설명\n");
        rows.Append("\"#설명 (ClipBench가 BIPER Female Runner의 점프(Jump_*·RunJump_*)에서 구운 값: 손으로 고치지 않고 클립이 바뀌면 다시 굽는다"
            + "(Temp/enfilade-clipbench.request에 bake=gaitmoves). length 클립 길이(초). profile 몸 중심이 처음보다 높은 정도(꼭대기 = 1), 처음부터 끝까지 고르게 "
            + JumpSamples + "곳(';'), leave 땅을 떠나는 때, top 꼭대기, land 처음 높이로 돌아오는 때(초). 점프 클립은 이 곡선대로 게임의 비행 높이를 따른다"
            + "(PlayerBody.FollowFlight))\",,,,,,,\n");
        int jumps = 0;
        foreach (var clip in clips.Values.OrderBy(c => c.name, StringComparer.Ordinal))
        {
            bool jump = clip.name.StartsWith("RunJump_", StringComparison.Ordinal)
                || clip.name.StartsWith("Jump_", StringComparison.Ordinal) && !clip.name.StartsWith("Jump_Climb", StringComparison.Ordinal);
            if (!jump || !Flight(clip, out float[] rise, out float leave, out float top, out float land)) continue;
            rows.Append(string.Join(",", clip.name, "jump", F(clip.length), string.Join(";", rise.Select(F)), F(leave), F(top), F(land), ""));
            rows.Append('\n');
            report.AppendLine(Invariant($"  {clip.name,-20} jump  {clip.length:0.00}s leaves {leave:0.00} tops {top:0.00} lands {land:0.00}"));
            jumps++;
        }
        WriteTable(rows.ToString());
        report.AppendLine($"  baked {jumps} jumps into {TablePath}");
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
