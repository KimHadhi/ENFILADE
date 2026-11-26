#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// The editor's bench for body clips, without playing: every clip of a folder under Art/Animations is played on a
/// character with the game's own measure (GaitClips: how fast and which way a cycle's feet carry the body, its strides,
/// where the left foot comes down) and read for what a one-off does (how far and which way its root travels and turns,
/// how high the hips are at its start, lowest and end), so which clip a moment of the game should play is chosen from
/// numbers taken on a real body and not from a file's name (the user, 2026-11-21, on BIPER's Combat Girls and Female
/// Runner: "애니메이션들 싹 다 내 게임에 적용"). Its numbers decide nothing in the game.
/// ENFILADE → 몸 → 클립 검사대 runs both BIPER folders on Ichigo (the reference body, see make-needed-animations). A tool
/// outside Unity asks through Temp/enfilade-clipbench.request, whose lines (all optional) say:
///   folders=BiperRunner,BiperCombat   folders under Art/Animations (these two)
///   clips=Run_Lfoot,R_Run             only these clips
///   skins=ichigo,mayo                 characters by id (ichigo)
///   models=Assets/…/X.fbx             any humanoid model by path too (a pack's own body)
///   cyclefilm=Run_Lfoot,Sprint_Lfoot   cycles side by side, eight moments each (StepBench.Cycles)
///   lifts=TurnIP_R90                  when and how a captured turn's feet come up (StepBench.Lifts)
///   stepfilm=Idle,R_Idle              a turn on the spot as the game steps it (StepBench): pictures and each step
///   track=Jump_1h,SlideToRun_Loop     a timeline for these too: the body's rise, the hips, which way the hips, chest and
///                                     head face, how fast the hands move against the chest and how the body and head lean (Track),
///                                     and the clip's motion as the game reads it: its root's turn, its chest against its root
///                                     and against where it began, its root's way, its pace and its footfalls (TrackMotion)
/// Logs/ClipBench/report.txt has a line a clip. It lives beside GaitClips because it measures with it.
/// </summary>
[InitializeOnLoad]
internal static class ClipBench
{
    private const string Request = "Temp/enfilade-clipbench.request", Folder = "Logs/ClipBench", ReportPath = Folder + "/report.txt";
    private const string AnimationsFolder = "Assets/_Project/Art/Animations/";
    // The game stands every character with its eyes this high (m); the numbers are given at that size too.
    private const float GameEyes = 1.6f;
    // Moments a one-off is read at, at least, for its hips' height.
    private const int HeightSamples = 60;
    private static readonly string[] DefaultFolders = { "BiperRunner", "BiperCombat" };

    private static double next;

    static ClipBench() => EditorApplication.update += Tick;

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + .5;
        if (!File.Exists(Request)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string[] lines;
        try
        {
            lines = File.ReadAllLines(Request);
            File.Delete(Request);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return;
        }
        Run(lines);
    }

    [MenuItem("ENFILADE/몸/클립 검사대 (BIPER 두 묶음, 이치고)")]
    private static void RunMenu() => Run(Array.Empty<string>());

    private static void Run(string[] lines)
    {
        string[] folders = DefaultFolders, skins = { "ichigo" }, models = Array.Empty<string>();
        HashSet<string> only = null, tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] stepped = Array.Empty<string>(), lifted = Array.Empty<string>(), cycled = Array.Empty<string>();
        foreach (string raw in lines)
        {
            int cut = raw.IndexOf('=');
            if (cut <= 0) continue;
            var values = raw.Substring(cut + 1).Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            switch (raw.Substring(0, cut).Trim().ToLowerInvariant())
            {
                case "folders": folders = values; break;
                case "skins": skins = values; break;
                case "models": models = values; break;
                case "clips": only = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase); break;
                case "stepfilm": stepped = values; break;
                case "lifts": lifted = values; break;
                case "cyclefilm": cycled = values; break;
                case "track": tracked = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase); break;
            }
        }

        var text = new StringBuilder();
        text.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} clip bench: each clip played on the character with the game's own gait measure "
            + "(GaitClips) and read from its root curves. Sizes are the game's (eyes at 1.6 m). Per clip: length s, loop; gait: the speed "
            + "its down feet carry the body (m/s), the way (x right, y ahead, unit), strides in the clip, where the left foot first comes "
            + "down (share); root: travel over the whole clip (x right, z ahead, m), turn (degrees, + to the right), its rise and fall "
            + "(lowest..highest, m); hips over the lowest foot at the start / lowest / end (m).");
        var profile = PlayerBodyProfile.Load();
        if (profile == null)
        {
            text.AppendLine("! no PlayerBody profile");
            Write(text);
            return;
        }
        var preview = EditorSceneManager.NewPreviewScene();
        int measured = 0;
        try
        {
            // A character by id, or any humanoid model by its asset path (a pack's own body: what its clips were made on).
            foreach (string id in skins.Concat(models))
            {
                var skin = id.StartsWith("Assets/", StringComparison.Ordinal) ? null : profile.SkinOf(id);
                var model = skin != null ? profile.ModelOf(skin) : AssetDatabase.LoadAssetAtPath<GameObject>(id);
                if (model == null)
                {
                    text.AppendLine($"! no character '{id}'");
                    continue;
                }
                var body = Object.Instantiate(model);
                SceneManager.MoveGameObjectToScene(body, preview);
                body.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = body.GetComponentInChildren<Animator>();
                if (animator == null || !animator.isHuman)
                {
                    text.AppendLine($"! '{id}' has no humanoid Animator");
                    Object.DestroyImmediate(body);
                    continue;
                }
                float eyes = EyeHeight(animator);
                float scale = eyes > .2f ? GameEyes / eyes : 1f;
                text.AppendLine();
                text.AppendLine($"== {id}: eyes {eyes:0.000} in its own units, so x{scale:0.000} to the game's size; human scale {animator.humanScale:0.000}");
                if (cycled.Length > 0)
                {
                    text.AppendLine("-- cyclefilm");
                    var reel = ClipsIn(AnimationsFolder + "BiperRunner").Concat(ClipsIn(AnimationsFolder + "BiperCombat"))
                        .GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
                    var chosen = cycled.Where(reel.ContainsKey).Select(n => reel[n]).ToList();
                    if (chosen.Count > 0) StepBench.Cycles(animator, scale, chosen, $"{Folder}/cycles-{id}.png", text);
                }
                if (lifted.Length > 0)
                {
                    text.AppendLine("-- lifts");
                    var takes = ClipsIn(AnimationsFolder + "BiperRunner").Concat(ClipsIn(AnimationsFolder + "BiperCombat"))
                        .GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
                    foreach (string name in lifted)
                        if (takes.TryGetValue(name, out var take)) StepBench.Lifts(animator, scale, take, text);
                        else text.AppendLine($"  ! no clip '{name}'");
                }
                if (stepped.Length > 0)
                {
                    text.AppendLine("-- stepfilm");
                    var stances = ClipsIn(AnimationsFolder + "BiperRunner").Concat(ClipsIn(AnimationsFolder + "BiperCombat"))
                        .GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
                    foreach (string name in stepped)
                        if (stances.TryGetValue(name, out var stance)) StepBench.Film(animator, scale, stance, $"{Folder}/steps-{name}-{id}.png", text);
                        else text.AppendLine($"  ! no clip '{name}'");
                }
                foreach (string folder in folders)
                {
                    text.AppendLine($"-- {folder}");
                    foreach (var clip in ClipsIn(AnimationsFolder + folder).Where(c => only == null || only.Contains(c.name)))
                    {
                        text.AppendLine(Measure(model, clip, animator, scale));
                        if (tracked.Contains(clip.name))
                        {
                            Track(clip, animator, scale, text);
                            TrackMotion(clip, animator, text);
                        }
                        measured++;
                    }
                }
                Object.DestroyImmediate(body);
            }
        }
        catch (Exception error)
        {
            text.AppendLine("! the bench stopped: " + error.Message);
            Debug.LogException(error);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
        text.AppendLine();
        text.AppendLine($"({measured} clips)");
        Write(text);
        Debug.Log($"ClipBench: {measured} clips measured; {ReportPath}.");
    }

    private static void Write(StringBuilder text)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(ReportPath, text.ToString());
    }

    private static IEnumerable<AnimationClip> ClipsIn(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder)) yield break;
        foreach (string path in AssetDatabase.FindAssets("t:Model", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal))
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                if (!clip.name.StartsWith("__preview__", StringComparison.Ordinal)) yield return clip;
    }

    private static float EyeHeight(Animator animator)
    {
        var left = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        var right = animator.GetBoneTransform(HumanBodyBones.RightEye);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        Vector3 at = left != null && right != null ? (left.position + right.position) * .5f : head != null ? head.position : Vector3.zero;
        return animator.transform.InverseTransformPoint(at).y;
    }

    private static string Measure(GameObject model, AnimationClip clip, Animator animator, float scale)
    {
        var line = new StringBuilder();
        line.Append(Invariant($"{clip.name,-28} {clip.length,6:0.000}s {(clip.isLooping ? "loop" : "once")}"));
        if (!clip.isHumanMotion)
        {
            line.Append("  ! not a humanoid clip");
            return line.ToString();
        }
        var gait = GaitClips.For(model, clip, animator);
        if (gait != null && gait.Moves)
            line.Append(Invariant($"  gait {gait.Speed * scale,5:0.00} m/s way ({gait.Way.x,5:0.00},{gait.Way.y,5:0.00}) x{gait.Cycles} left@{gait.LeftDown:0.00}"));
        else line.Append("  gait -");
        if (Root(clip, out Vector3 travel, out float turn, out float low, out float high, out float reached))
        {
            float human = animator.humanScale * scale;
            line.Append(Invariant($"  root ({travel.x * human,5:0.00},{travel.z * human,5:0.00}) m = {travel.magnitude * human / Mathf.Max(.01f, clip.length):0.00} m/s turn {turn,6:0.0} rise {low * human:0.00}..{high * human:0.00}"));
            if (!clip.isLooping && reached >= 0f) line.Append(Invariant($" 90%@{reached:0.00}s"));
            // Unity's own average of the root's travel, as a build can read it.
            var average = clip.averageSpeed;
            line.Append(Invariant($" avg {new Vector2(average.x, average.z).magnitude * human:0.00}"));
        }
        // What the game goes by (ClipMotion): a cycle's own speed; a move's way and its moments.
        if (clip.isLooping) line.Append(Invariant($"  game {ClipMotion.CycleSpeed(clip):0.00} m/s"));
        else if (ClipMotion.Of(clip, animator) is ClipMotion motion)
            line.Append(Invariant($"  game {motion.Distance:0.00} m burst {motion.BurstFrom:0.00}..{motion.BurstTo:0.00} down {motion.Down:0.00} lowest {motion.Lowest:0.00} rises {motion.Rises:0.00} legs {motion.LegsUnder:0.00} feet {motion.OnFeet:0.00} up {motion.Up:0.00}"));
        Hips(clip, animator, scale, out float start, out float lowest, out float end);
        line.Append(Invariant($"  hips {start:0.00}/{lowest:0.00}/{end:0.00}"));
        return line.ToString();
    }

    // The clip's body centre (RootT, RootQ), as Unity keeps it in every humanoid clip whatever its import bakes: its travel
    // and turn from the first moment to the last, how far it rises and falls from the first, and when its ground speed first
    // reaches nine tenths of what it goes at over its last fifth (a start's run-up; -1 when it never moves).
    private static bool Root(AnimationClip clip, out Vector3 travel, out float turn, out float low, out float high, out float reached)
    {
        travel = Vector3.zero;
        turn = low = high = 0f;
        reached = -1f;
        var bindings = AnimationUtility.GetCurveBindings(clip);
        AnimationCurve Curve(string property)
        {
            foreach (var binding in bindings)
                if (binding.propertyName == property && binding.type == typeof(Animator)) return AnimationUtility.GetEditorCurve(clip, binding);
            return null;
        }
        var tx = Curve("RootT.x");
        var ty = Curve("RootT.y");
        var tz = Curve("RootT.z");
        var qx = Curve("RootQ.x");
        var qy = Curve("RootQ.y");
        var qz = Curve("RootQ.z");
        var qw = Curve("RootQ.w");
        if (tx == null || tz == null) return false;
        float end = clip.length;
        travel = new Vector3(tx.Evaluate(end) - tx.Evaluate(0f), 0f, tz.Evaluate(end) - tz.Evaluate(0f));
        if (qx != null && qy != null && qz != null && qw != null)
        {
            Quaternion Q(float t) => new Quaternion(qx.Evaluate(t), qy.Evaluate(t), qz.Evaluate(t), qw.Evaluate(t)).normalized;
            Vector3 from = Q(0f) * Vector3.forward, to = Q(end) * Vector3.forward;
            turn = Vector3.SignedAngle(Vector3.ProjectOnPlane(from, Vector3.up), Vector3.ProjectOnPlane(to, Vector3.up), Vector3.up);
            // The travel told in the clip's own first facing.
            travel = Quaternion.Inverse(Quaternion.LookRotation(Vector3.ProjectOnPlane(from, Vector3.up).normalized + Vector3.forward * 1e-6f)) * travel;
        }
        {
            int steps = Mathf.Max(8, Mathf.CeilToInt(end * 60f));
            var speed = new float[steps];
            for (int i = 0; i < steps; i++)
            {
                float a = end * i / steps, b = end * (i + 1) / steps;
                speed[i] = new Vector2(tx.Evaluate(b) - tx.Evaluate(a), tz.Evaluate(b) - tz.Evaluate(a)).magnitude / (b - a);
            }
            float last = 0f;
            int from = steps - Mathf.Max(1, steps / 5);
            for (int i = from; i < steps; i++) last += speed[i];
            last /= steps - from;
            if (last > 1e-3f)
                for (int i = 0; i < steps; i++)
                    if (speed[i] >= last * .9f) { reached = end * (i + 1) / steps; break; }
        }
        if (ty != null)
        {
            float first = ty.Evaluate(0f);
            low = high = 0f;
            int steps = Mathf.Max(8, Mathf.CeilToInt(end * 60f));
            for (int i = 0; i <= steps; i++)
            {
                float y = ty.Evaluate(end * i / steps) - first;
                low = Mathf.Min(low, y);
                high = Mathf.Max(high, y);
            }
        }
        return true;
    }

    // The hips over the lowest foot (m, the game's size) at the clip's first moment, its lowest and its last: a crouch, a
    // landing's dip, a roll's tuck and whether it ends standing. The body's pose is put back afterwards.
    private static void Hips(AnimationClip clip, Animator animator, float scale, out float start, out float lowest, out float end)
    {
        start = lowest = end = 0f;
        var root = animator.transform;
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (hips == null || leftFoot == null || rightFoot == null) return;
        var bones = root.GetComponentsInChildren<Transform>();
        var poses = bones.Select(b => (b.localPosition, b.localRotation)).ToArray();
        Vector3 rootPosition = root.position;
        Quaternion rootRotation = root.rotation;
        try
        {
            int count = Mathf.Max(HeightSamples, Mathf.CeilToInt(clip.length * 60f));
            lowest = float.PositiveInfinity;
            for (int i = 0; i <= count; i++)
            {
                float t = clip.length * i / count;
                ClipSampler.Sample(clip, animator.gameObject, t);
                float feet = Mathf.Min(root.InverseTransformPoint(leftFoot.position).y, root.InverseTransformPoint(rightFoot.position).y);
                float over = (root.InverseTransformPoint(hips.position).y - feet) * scale;
                if (i == 0) start = over;
                if (i == count) end = over;
                lowest = Mathf.Min(lowest, over);
            }
        }
        finally
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(poses[i].localPosition, poses[i].localRotation);
            root.SetPositionAndRotation(rootPosition, rootRotation);
        }
    }

    // A clip's timeline, TrackRate times a second: t s; rise = the body centre over where it began (m, RootT); hips over the
    // lowest foot (m); which way the hips (the thighs' line), the chest (the shoulders' line) and the head (its rest-pose
    // ahead) face, degrees from the body's ahead, + to the right; each hand against the chest (x right, y up, z ahead, m)
    // and how fast the faster hand moves against it (m/s); how far the body centre has gone over the ground (m). Sizes are
    // the game's. The body's pose is put back afterwards.
    private const float TrackRate = 15f;

    private static void Track(AnimationClip clip, Animator animator, float scale, StringBuilder text)
    {
        var root = animator.transform;
        Transform Bone(HumanBodyBones bone) => animator.GetBoneTransform(bone);
        var hips = Bone(HumanBodyBones.Hips);
        var chest = Bone(HumanBodyBones.UpperChest) ?? Bone(HumanBodyBones.Chest) ?? Bone(HumanBodyBones.Spine);
        var head = Bone(HumanBodyBones.Head);
        var leftArm = Bone(HumanBodyBones.LeftUpperArm);
        var rightArm = Bone(HumanBodyBones.RightUpperArm);
        var leftLeg = Bone(HumanBodyBones.LeftUpperLeg);
        var rightLeg = Bone(HumanBodyBones.RightUpperLeg);
        var leftHand = Bone(HumanBodyBones.LeftHand);
        var rightHand = Bone(HumanBodyBones.RightHand);
        var leftFoot = Bone(HumanBodyBones.LeftFoot);
        var rightFoot = Bone(HumanBodyBones.RightFoot);
        if (new[] { hips, chest, head, leftArm, rightArm, leftLeg, rightLeg, leftHand, rightHand, leftFoot, rightFoot }.Any(b => b == null))
        {
            text.AppendLine("    track: ! a bone is missing");
            return;
        }
        AnimationCurve rootX = null, rootY = null, rootZ = null;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            if (binding.type == typeof(Animator))
                switch (binding.propertyName)
                {
                    case "RootT.x": rootX = AnimationUtility.GetEditorCurve(clip, binding); break;
                    case "RootT.y": rootY = AnimationUtility.GetEditorCurve(clip, binding); break;
                    case "RootT.z": rootZ = AnimationUtility.GetEditorCurve(clip, binding); break;
                }
        float human = animator.humanScale * scale;
        var bones = root.GetComponentsInChildren<Transform>();
        var poses = bones.Select(b => (b.localPosition, b.localRotation)).ToArray();
        Vector3 rootPosition = root.position;
        Quaternion rootRotation = root.rotation;
        // The head's ahead, as it is in the model's own rest pose (facing the body's ahead).
        Vector3 headAhead = Quaternion.Inverse(head.rotation) * root.forward;
        Vector3 headUp = Quaternion.Inverse(head.rotation) * root.up;
        float Facing(Vector3 left, Vector3 right)
        {
            Vector3 ahead = Vector3.Cross(root.InverseTransformVector(right - left), Vector3.up);
            return Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg;
        }
        try
        {
            text.AppendLine("    track: t  rise  hips | face hips chest head | left hand (x,y,z)  right hand (x,y,z)  hand m/s | gone m | lean ahead side | head down roll");
            int count = Mathf.Max(2, Mathf.CeilToInt(clip.length * TrackRate));
            Vector3 lastLeft = Vector3.zero, lastRight = Vector3.zero;
            float first = rootY != null ? rootY.Evaluate(0f) : 0f;
            for (int i = 0; i <= count; i++)
            {
                float t = clip.length * i / count;
                ClipSampler.Sample(clip, animator.gameObject, t);
                Vector3 Against(Transform hand) => root.InverseTransformVector(hand.position - chest.position) * scale;
                Vector3 left = Against(leftHand), right = Against(rightHand);
                float speed = i == 0 ? 0f : Mathf.Max((left - lastLeft).magnitude, (right - lastRight).magnitude) / (clip.length / count);
                lastLeft = left;
                lastRight = right;
                float feet = Mathf.Min(root.InverseTransformPoint(leftFoot.position).y, root.InverseTransformPoint(rightFoot.position).y);
                float over = (root.InverseTransformPoint(hips.position).y - feet) * scale;
                Vector3 headForward = root.InverseTransformDirection(head.rotation * headAhead);
                float headYaw = Mathf.Atan2(headForward.x, headForward.z) * Mathf.Rad2Deg;
                // How the body leans (degrees): the hips-to-head line ahead and to the right, and the head's own nod down and
                // roll to the right, all against the body's upright. A run's lean is what a first-person body shows of it.
                Vector3 trunk = root.InverseTransformVector(head.position - hips.position);
                float leanAhead = Mathf.Atan2(trunk.z, trunk.y) * Mathf.Rad2Deg, leanSide = Mathf.Atan2(trunk.x, trunk.y) * Mathf.Rad2Deg;
                Vector3 headTop = root.InverseTransformDirection(head.rotation * headUp);
                float headDown = -Mathf.Atan2(headForward.y, new Vector2(headForward.x, headForward.z).magnitude) * Mathf.Rad2Deg;
                float headRoll = Mathf.Atan2(headTop.x, headTop.y) * Mathf.Rad2Deg;
                float rise = rootY != null ? (rootY.Evaluate(t) - first) * human : 0f;
                // How far the body centre has gone over the ground since the start (m).
                float gone = rootX != null && rootZ != null
                    ? new Vector2(rootX.Evaluate(t) - rootX.Evaluate(0f), rootZ.Evaluate(t) - rootZ.Evaluate(0f)).magnitude * human : 0f;
                text.AppendLine(Invariant($"    {t,5:0.00} {rise,5:0.00} {over,4:0.00} | {Facing(leftLeg.position, rightLeg.position),5:0} {Facing(leftArm.position, rightArm.position),5:0} {headYaw,5:0} | ({left.x,5:0.00},{left.y,5:0.00},{left.z,5:0.00}) ({right.x,5:0.00},{right.y,5:0.00},{right.z,5:0.00}) {speed,5:0.0} | {gone,5:0.00} | {leanAhead,5:0} {leanSide,5:0} | {headDown,5:0} {headRoll,5:0}"));
            }
        }
        finally
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(poses[i].localPosition, poses[i].localRotation);
            root.SetPositionAndRotation(rootPosition, rootRotation);
        }
    }

    // The clip's motion as the game reads it (ClipMotion), TrackRate times a second: how far round its root has turned, which
    // way its chest faces against its root and so against where it began (degrees, + to the right), where its root has gone
    // in the frame it began in (m: x right, y ahead), its pace (m/s); then its footfalls (s, L or R).
    private static void TrackMotion(AnimationClip clip, Animator animator, StringBuilder text)
    {
        if (!(ClipMotion.Of(clip, animator) is ClipMotion motion)) return;
        text.AppendLine("    motion: t  root turned  chest (root)  chest (start) | root way x, y m | pace m/s");
        int count = Mathf.Max(2, Mathf.CeilToInt(clip.length * TrackRate));
        for (int i = 0; i <= count; i++)
        {
            float t = clip.length * i / count, turned = motion.TurnedAt(t), chest = motion.FacingAt(t);
            Vector2 way = motion.WayAt(t);
            text.AppendLine(Invariant($"    {t,5:0.00} {turned,6:0} {chest,6:0} {turned + chest,6:0} | {way.x,5:0.00} {way.y,5:0.00} | {motion.PaceAt(t),4:0.0}"));
        }
        var steps = new StringBuilder("    footfalls:");
        for (int i = 0; i < motion.StepCount; i++)
        {
            var (time, place) = motion.Step(i);
            steps.Append(Invariant($" {time:0.00}{(place < .25f ? "L" : "R")}"));
        }
        text.AppendLine(steps.ToString());
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
#endif
