using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The fingers of the gun clips made for our bodies (GunClipRetarget), taken from KINEMATION's own clip through Unity's
/// humanoid muscles: the clip plays on a humanoid model of KINEMATION's whose skeleton is the one the clip was made for
/// (the FPS Animation Pack's arms share their bone names and paths with KINEMATION's humanoid mannequin), Unity reads its
/// fingers as muscles, and those muscle curves replace the baked clip's. Muscles mean the same on every humanoid, so each
/// body's hand takes KINEMATION's shape in its own proportions, as any humanoid clip carries fingers across. Retarget
/// Pro carried them flat or spread past a hand's reach, and fingers closed on the gun's mesh came out wrong (user,
/// 2026-11-07). The model is picked by matching the clip's own finger paths, so another KINEMATION pack's clips find
/// their own humanoid model.
/// </summary>
internal sealed class GunClipFingers : IDisposable
{
    private const string KinemationFolder = "Assets/ThirdParty/KINEMATION";
    private static readonly Regex FingerMuscle = new(@"^(Left|Right) (Thumb|Index|Middle|Ring|Little) (.+)$");
    // Finger muscles all within this of 0 over the whole clip: the clip didn't move the model's fingers.
    private const float StillRange = .02f;

    private sealed class Model
    {
        public GameObject asset, instance;
        // The bones the clip's paths start from (where the model's Animator was).
        public GameObject root;
        public HumanPoseHandler handler;
        // Where a finger bone rests differently in this model and in the clips' own skeleton: its rest here, and the turn
        // from theirs to this one.
        public readonly List<(Transform bone, Quaternion rest, Quaternion fix)> rests = new();
        public float restOff;
        // Every finger bone the clips move, at rest, to tell whether a clip moved them at all.
        public readonly List<(Transform bone, Quaternion rest)> fingers = new();
    }

    private readonly FPSBodyAssets assets;
    private readonly List<GameObject> candidates = new();
    private readonly Dictionary<GameObject, Model> open = new();
    private readonly List<(int index, string attribute)> muscles = new();
    private readonly HashSet<string> reported = new();

    private GunClipFingers(FPSBodyAssets assets)
    {
        this.assets = assets;
        for (int i = 0; i < HumanTrait.MuscleCount; i++)
        {
            var match = FingerMuscle.Match(HumanTrait.MuscleName[i]);
            // Unity names a finger muscle "Left Index 1 Stretched"; a humanoid clip keys it as "LeftHand.Index.1 Stretched".
            if (match.Success) muscles.Add((i, $"{match.Groups[1].Value}Hand.{match.Groups[2].Value}.{match.Groups[3].Value}"));
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { KinemationFolder }))
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var animator = model != null ? model.GetComponentInChildren<Animator>() : null;
            if (animator != null && animator.avatar != null && animator.avatar.isHuman) candidates.Add(model);
        }
    }

    public static GunClipFingers Open(FPSBodyAssets assets) => new(assets);

    /// <summary>
    /// Puts <paramref name="source"/>'s fingers into <paramref name="baked"/>, frame by frame, and returns it. A clip no
    /// humanoid model of KINEMATION's can play keeps the fingers Retarget Pro gave it.
    /// </summary>
    public AnimationClip Apply(AnimationClip source, AnimationClip baked)
    {
        if (source == null || baked == null) return baked;
        var model = ModelFor(source);
        if (model == null)
        {
            Note($"no humanoid model of KINEMATION's plays {source.name}; its fingers stay as Retarget Pro made them");
            return baked;
        }
        float rate = baked.frameRate > 0f ? baked.frameRate : source.frameRate > 0f ? source.frameRate : 30f;
        int frames = Mathf.Max(1, Mathf.CeilToInt(source.length * rate));
        var curves = new AnimationCurve[muscles.Count];
        for (int m = 0; m < curves.Length; m++) curves[m] = new AnimationCurve();
        var pose = new HumanPose();
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        int moved = 0;
        for (int f = 0; f <= frames; f++)
        {
            float time = Mathf.Min(source.length, f / rate);
            // From rest each frame, so a bone the clip leaves alone isn't turned again and again.
            foreach (var (bone, rest) in model.fingers) bone.localRotation = rest;
            source.SampleAnimation(model.root, time);
            if (f == 0)
                foreach (var (bone, rest) in model.fingers)
                    if (Quaternion.Angle(bone.localRotation, rest) > 1f) moved++;
            foreach (var (bone, _, fix) in model.rests) bone.localRotation = fix * bone.localRotation;
            model.handler.GetHumanPose(ref pose);
            for (int m = 0; m < muscles.Count; m++)
            {
                float value = pose.muscles[muscles[m].index];
                low = Mathf.Min(low, value);
                high = Mathf.Max(high, value);
                curves[m].AddKey(new Keyframe(time, value));
            }
        }
        // Fingers that never left the model's rest mean the clip didn't reach them: Retarget Pro's stay.
        if (high - low < StillRange && Mathf.Max(Mathf.Abs(low), Mathf.Abs(high)) < StillRange)
        {
            Note($"{source.name} didn't move {model.asset.name}'s fingers ({moved} of {model.fingers.Count} finger bones turned off rest, muscles "
                + $"{low:0.00}..{high:0.00}); its fingers stay as Retarget Pro made them");
            return baked;
        }
        for (int m = 0; m < muscles.Count; m++)
            AnimationUtility.SetEditorCurve(baked, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), muscles[m].attribute), curves[m]);
        EditorUtility.SetDirty(baked);
        Note($"{source.name}: fingers from {model.asset.name} ({moved} of {model.fingers.Count} finger bones off rest, muscles {low:0.00}..{high:0.00}); its finger rests differ from "
            + $"{(assets.armsModel != null ? assets.armsModel.name : "the clips' skeleton")} by up to {model.restOff:0.0}°" + (model.rests.Count > 0 ? ", made up for" : ""));
        return baked;
    }

    // The humanoid model that plays the most of the clip's finger paths (at least most of them).
    private Model ModelFor(AnimationClip clip)
    {
        var paths = new List<string>();
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            if (binding.type == typeof(Transform) && Regex.IsMatch(Path.GetFileName(binding.path), @"^(thumb|index|middle|ring|pinky)_0\d_[lr]$")
                && !paths.Contains(binding.path)) paths.Add(binding.path);
        if (paths.Count == 0) return null;
        GameObject best = null;
        int bestCount = 0;
        foreach (var candidate in candidates)
        {
            int count = 0;
            foreach (string path in paths)
                if (candidate.transform.Find(path) != null) count++;
            if (count > bestCount) { bestCount = count; best = candidate; }
        }
        if (best == null || bestCount < paths.Count * .9f) return null;
        if (open.TryGetValue(best, out var model)) return model;
        model = new Model { asset = best, instance = Object.Instantiate(best) };
        model.instance.hideFlags = HideFlags.HideAndDontSave;
        model.instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var animator = model.instance.GetComponentInChildren<Animator>();
        Avatar avatar = animator.avatar;
        model.root = animator.gameObject;
        // A humanoid Animator keeps a generic clip's bone curves off the bones it maps: sampled with it on, the fingers
        // never moved and every finger muscle read 0 (2026-11-07). Without it the clip poses the bones by their paths, and
        // the avatar still reads them.
        Object.DestroyImmediate(animator);
        model.handler = new HumanPoseHandler(avatar, model.root.transform);
        foreach (string path in paths)
            if (model.root.transform.Find(path) is Transform finger) model.fingers.Add((finger, finger.localRotation));
        MatchRests(model, paths);
        open[best] = model;
        return model;
    }

    // Where the model's finger bones rest differently from the clips' own skeleton (KINEMATION's arms), the clip's local
    // rotations are taken from that skeleton's rest onto the model's: rest × (their rest⁻¹ × clip).
    private void MatchRests(Model model, List<string> paths)
    {
        if (assets.armsModel == null) return;
        var arms = Object.Instantiate(assets.armsModel);
        arms.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            foreach (string path in paths)
            {
                var bone = model.instance.transform.Find(path);
                var theirs = arms.transform.Find(path);
                if (bone == null || theirs == null) continue;
                float off = Quaternion.Angle(bone.localRotation, theirs.localRotation);
                model.restOff = Mathf.Max(model.restOff, off);
                if (off > .5f) model.rests.Add((bone, bone.localRotation, bone.localRotation * Quaternion.Inverse(theirs.localRotation)));
            }
        }
        finally
        {
            Object.DestroyImmediate(arms);
        }
    }

    // One line per finding in Logs/gun-bake-report.txt.
    private void Note(string line)
    {
        if (!reported.Add(line)) return;
        try
        {
            string path = Path.Combine(Application.dataPath, "..", "Logs", "gun-bake-report.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} {line}\n");
        }
        catch (Exception error)
        {
            Debug.LogWarning($"GunClipFingers: the bake report could not be written: {error.Message}");
        }
    }

    public void Dispose()
    {
        foreach (var model in open.Values)
        {
            model.handler?.Dispose();
            if (model.instance != null) Object.DestroyImmediate(model.instance);
        }
        open.Clear();
    }
}
