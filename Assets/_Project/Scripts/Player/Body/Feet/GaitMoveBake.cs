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
/// Bakes, from BIPER Female Runner's turns on the spot (TurnIP_L/R 45, 90, 135, 180), how far round each one's root is
/// through its length and its whole turn, so the legs turn by that very curve while the clip steps them round
/// (PlayerBody.TurnOnTheSpot). Read from the clip's own root curves. Written to Tables/Player/GaitMoves.csv by ClipBench
/// (request line bake=gaitmoves); the game only reads it. These are measurements: bake again after a clip changes, never
/// edit by hand. (Until 0.9.263 it baked the start and stop clips too, for the starts and stops that went then.)
/// </summary>
internal static class GaitMoveBake
{
    internal const string TablePath = "Assets/_Project/Data/Resources/Tables/Player/GaitMoves.csv";
    // Samples along a clip.
    internal const int Samples = 24;
    private static readonly string[] Sides = { "L", "R" };

    internal static void Bake(IReadOnlyDictionary<string, AnimationClip> clips, StringBuilder report)
    {
        var rows = new StringBuilder();
        rows.Append("id,kind,length,profile,turn,#설명\n");
        rows.Append("\"#설명 (ClipBench가 BIPER Female Runner의 제자리 돌기(TurnIP_*)에서 구운 값: 손으로 고치지 않고 클립이 바뀌면 다시 굽는다"
            + "(Temp/enfilade-clipbench.request에 bake=gaitmoves). length 클립 길이(초). profile 루트가 그때까지 돈 몫(0~1), 클립 길이를 고르게 나눈 "
            + Samples + "곳의 끝에서(';'). turn 그 클립이 도는 각도(+ 오른쪽). 다리는 이 곡선대로 돈다(PlayerBody.TurnOnTheSpot))\",,,,,\n");
        int baked = 0;
        foreach (string side in Sides)
            foreach (int angle in new[] { 45, 90, 135, 180 })
            {
                string name = $"TurnIP_{side}{angle}";
                if (!clips.TryGetValue(name, out var clip)) continue;
                var round = Round(clip, out float whole);
                rows.Append(string.Join(",", name, "turn", F(clip.length), string.Join(";", round.Select(F)), F(whole), ""));
                rows.Append('\n');
                report.AppendLine(Invariant($"  {name,-20} turn  {clip.length:0.00}s turns {whole:0.0} share {string.Join(" ", round.Where((_, i) => i % 4 == 3).Select(v => v.ToString("0.00", CultureInfo.InvariantCulture)))}"));
                baked++;
            }
        WriteTable(rows.ToString());
        report.AppendLine($"  baked {baked} turns on the spot into {TablePath}");
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // How far round the root has turned by the end of each of Samples shares of the clip (0-1 of the whole), and the whole
    // (degrees, + to the right; a half turn reads as the way it went).
    private static float[] Round(AnimationClip clip, out float whole)
    {
        var round = new float[Samples];
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
        if (qx == null || qy == null || qz == null || qw == null) return round;
        float Yaw(float time)
        {
            var q = new Quaternion(qx.Evaluate(time), qy.Evaluate(time), qz.Evaluate(time), qw.Evaluate(time)).normalized;
            Vector3 ahead = Vector3.ProjectOnPlane(q * Vector3.forward, Vector3.up);
            return Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg;
        }
        // Summed in small steps, so a half turn and more is followed round rather than read as its shortest way.
        int steps = Samples * 8;
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
        for (int i = 0; i < Samples; i++)
            round[i] = Mathf.Abs(whole) > 1e-3f ? along[(i + 1) * 8] / whole : 0f;
        return round;
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
