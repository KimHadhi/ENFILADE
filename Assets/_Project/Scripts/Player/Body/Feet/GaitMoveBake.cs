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
/// Bakes, from BIPER Female Runner's start and stop clips, what the game needs to move the body as they do (the user,
/// 2026-11-21, on starts and stops: "걍 자연스럽고 모션 부드럽게 좋은 대로 ㄱㄱ"): how fast the clip's root goes through it, as a
/// share of its gait's cycle (so it scales to whatever speed the game gives that gait), and where in the cycle's stride it
/// is at its end (a start, handing over to the cycle in step) or its beginning (a stop, chosen to follow the stride the
/// body is in). Read from the clip's own root curves (the performer's travel) and, for the stride, on the reference body.
/// Written to Tables/Player/GaitMoves.csv by ClipBench (request line bake=gaitmoves); the game only reads it (MovementGait,
/// PlayerBody.GaitMoves). These are measurements: bake again after a clip changes, never edit by hand.
/// </summary>
internal static class GaitMoveBake
{
    internal const string TablePath = "Assets/_Project/Data/Resources/Tables/Player/GaitMoves.csv";
    // Speed samples along a clip.
    internal const int Samples = 24;
    private static readonly (string gait, string loop)[] Gaits =
        { ("walk", "Walk_Lfoot"), ("jog", "Jog_Lfoot"), ("run", "Run_Lfoot"), ("sprint", "Sprint_Lfoot") };
    private static readonly string[] Feet = { "L", "R" };

    internal static void Bake(IReadOnlyDictionary<string, AnimationClip> clips, GameObject model, Animator animator, StringBuilder report)
    {
        var rows = new StringBuilder();
        rows.Append("id,gait,kind,length,profile,phase,#설명\n");
        rows.Append("\"#설명 (ClipBench가 BIPER Female Runner의 출발·멈춤 클립에서 구운 값: 손으로 고치지 않고 클립이 바뀌면 다시 굽는다(Temp/enfilade-clipbench.request에 bake=gaitmoves). "
            + "length 클립 길이(초). profile 클립의 루트가 가는 속도를 같은 걸음새 반복 클립 루트 속도의 몫으로, 클립 길이를 고르게 나눈 " + Samples + "곳에서(';'). "
            + "게임은 그 걸음새 속도(Gaits.csv)에 이 몫을 곱해 몸을 움직인다. phase 출발은 끝에서, 멈춤은 처음에 반복 클립의 걸음 박자 어디인지(0 = 왼발 딛는 때, 0~1). "
            + "출발은 그 박자로 반복 클립에 넘기고, 멈춤은 지금 박자에 가장 가까운 것을 고른다)\",,,,,,\n");
        int baked = 0;
        foreach (var (gait, loopName) in Gaits)
        {
            if (!clips.TryGetValue(loopName, out var loop)) { report.AppendLine($"! no {loopName}"); continue; }
            float loopSpeed = RootSpeed(loop);
            var loopGait = GaitClips.For(model, loop, animator);
            if (loopSpeed <= 1e-3f || loopGait == null) { report.AppendLine($"! {loopName}: no root travel or no gait"); continue; }
            float stride = loopGait.Stride;
            foreach (string kind in new[] { "start", "stop" })
                foreach (string foot in Feet)
                {
                    string name = kind == "start" ? $"{Cap(gait)}_Start_{foot}0" : $"{Cap(gait)}_Stop_{foot}foot";
                    if (!clips.TryGetValue(name, out var clip)) continue;
                    var profile = Profile(clip, loopSpeed);
                    float left = LeftDowns(clip, animator, out var downs);
                    float phase = 0f;
                    if (downs.Count > 0)
                    {
                        // A start: how far into the stride it is at its end, from its last left touchdown. A stop: where its
                        // first left touchdown falls, counted back to its beginning.
                        phase = kind == "start" ? Mathf.Repeat((clip.length - downs[downs.Count - 1]) / stride, 1f)
                            : Mathf.Repeat(-downs[0] / stride, 1f);
                    }
                    rows.Append(string.Join(",", name, gait, kind, F(clip.length),
                        string.Join(";", profile.Select(F)), F(phase), ""));
                    rows.Append('\n');
                    report.AppendLine(Invariant($"  {name,-20} {kind,-5} {clip.length:0.00}s phase {phase:0.00} speed {string.Join(" ", profile.Where((_, i) => i % 4 == 0).Select(v => v.ToString("0.00", CultureInfo.InvariantCulture)))} (left down x{downs.Count}, leg {left:0.00})"));
                    baked++;
                }
        }
        WriteTable(rows.ToString());
        report.AppendLine($"  baked {baked} start and stop clips into {TablePath}");
    }

    private static string Cap(string gait) => char.ToUpperInvariant(gait[0]) + gait.Substring(1);
    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // The root's ground speed over the whole clip (m/s in the clip's own units; only ever divided by another's).
    private static float RootSpeed(AnimationClip clip)
    {
        Curves(clip, out var tx, out var tz);
        if (tx == null || tz == null || clip.length <= 0f) return 0f;
        return new Vector2(tx.Evaluate(clip.length) - tx.Evaluate(0f), tz.Evaluate(clip.length) - tz.Evaluate(0f)).magnitude / clip.length;
    }

    // The root's ground speed through the clip, at Samples moments (each the mean over its share), over the cycle's.
    private static float[] Profile(AnimationClip clip, float loopSpeed)
    {
        Curves(clip, out var tx, out var tz);
        var speeds = new float[Samples];
        if (tx == null || tz == null) return speeds;
        float length = clip.length;
        for (int i = 0; i < Samples; i++)
        {
            float a = length * i / Samples, b = length * (i + 1) / Samples;
            speeds[i] = new Vector2(tx.Evaluate(b) - tx.Evaluate(a), tz.Evaluate(b) - tz.Evaluate(a)).magnitude / (b - a) / loopSpeed;
        }
        return speeds;
    }

    private static void Curves(AnimationClip clip, out AnimationCurve tx, out AnimationCurve tz)
    {
        tx = tz = null;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type != typeof(Animator)) continue;
            if (binding.propertyName == "RootT.x") tx = AnimationUtility.GetEditorCurve(clip, binding);
            else if (binding.propertyName == "RootT.z") tz = AnimationUtility.GetEditorCurve(clip, binding);
        }
    }

    // When the left foot comes down in a one-off clip on this body (s): its ankle or toes back near their lowest after
    // being up, as GaitClips reads a foot down. Returns the leg's length.
    private static float LeftDowns(AnimationClip clip, Animator animator, out List<float> downs)
    {
        downs = new List<float>();
        var root = animator.transform;
        var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        var lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var toes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
        if (upper == null || lower == null || foot == null) return 0f;
        var bones = root.GetComponentsInChildren<Transform>();
        var poses = bones.Select(b => (b.localPosition, b.localRotation)).ToArray();
        Vector3 rootPosition = root.position;
        Quaternion rootRotation = root.rotation;
        int count = Mathf.Max(8, Mathf.CeilToInt(clip.length * 60f));
        var ankle = new float[count + 1];
        var toe = new float[count + 1];
        float leg = 0f;
        try
        {
            for (int i = 0; i <= count; i++)
            {
                ClipSampler.Sample(clip, animator.gameObject, clip.length * i / count);
                ankle[i] = root.InverseTransformPoint(foot.position).y;
                toe[i] = toes != null ? root.InverseTransformPoint(toes.position).y : ankle[i];
                if (i == 0) leg = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, foot.position);
            }
        }
        finally
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(poses[i].localPosition, poses[i].localRotation);
            root.SetPositionAndRotation(rootPosition, rootRotation);
        }
        if (leg <= 1e-4f) return 0f;
        float lowAnkle = ankle.Min(), lowToe = toe.Min();
        bool down = true;
        for (int i = 0; i <= count; i++)
        {
            float a = (ankle[i] - lowAnkle) / leg, t = (toe[i] - lowToe) / leg;
            bool now = down ? a < .08f || t < .05f : a < .04f || t < .025f;
            if (now && !down && i > 0) downs.Add(clip.length * i / count);
            down = now;
        }
        return leg * Mathf.Abs(root.lossyScale.y);
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
