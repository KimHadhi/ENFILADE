#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Clip pictures and readings for ClipBench (request lines lifts= and cyclefilm=): how a captured turn on the spot steps
/// (Lifts) and walk and run cycles side by side (Cycles). Its step film of the feet stepping a turn by themselves (stepfilm=)
/// went with those steps in 0.9.430 (a turn on the spot is BIPER's clip now, PlayerBody.StandTurn).
/// </summary>
internal static class StepBench
{
    private const int Size = 200;

    /// <summary>
    /// How a captured turn on the spot steps (ClipBench lifts=), to take the game's step from people rather than from a
    /// guess: each time a foot comes up (its ankle more than LiftFrom over the lowest it stands in the clip) until it is down
    /// again, how long, how high, and how far round the body had turned (its root, degrees) when it came up and when it was
    /// down. Sizes are the game's.
    /// </summary>
    internal static void Lifts(Animator animator, float scale, AnimationClip clip, StringBuilder text)
    {
        var root = animator.transform;
        var feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
        if (feet[0] == null || feet[1] == null) return;
        AnimationCurve qx = null, qy = null, qz = null, qw = null;
        foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type != typeof(Animator)) continue;
            switch (binding.propertyName)
            {
                case "RootQ.x": qx = UnityEditor.AnimationUtility.GetEditorCurve(clip, binding); break;
                case "RootQ.y": qy = UnityEditor.AnimationUtility.GetEditorCurve(clip, binding); break;
                case "RootQ.z": qz = UnityEditor.AnimationUtility.GetEditorCurve(clip, binding); break;
                case "RootQ.w": qw = UnityEditor.AnimationUtility.GetEditorCurve(clip, binding); break;
            }
        }
        float Yaw(float time)
        {
            if (qx == null || qy == null || qz == null || qw == null) return 0f;
            var q = new Quaternion(qx.Evaluate(time), qy.Evaluate(time), qz.Evaluate(time), qw.Evaluate(time)).normalized;
            Vector3 ahead = Vector3.ProjectOnPlane(q * Vector3.forward, Vector3.up);
            return Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg;
        }
        var bones = root.GetComponentsInChildren<Transform>();
        var poses = Array.ConvertAll(bones, b => (b.localPosition, b.localRotation));
        Vector3 rootPosition = root.position;
        Quaternion rootRotation = root.rotation;
        try
        {
            int count = Mathf.Max(2, Mathf.CeilToInt(clip.length * 120f));
            var heights = new float[2, count + 1];
            var turned = new float[count + 1];
            float last = Yaw(0f), sum = 0f;
            var lowest = new[] { float.PositiveInfinity, float.PositiveInfinity };
            for (int i = 0; i <= count; i++)
            {
                float time = clip.length * i / count;
                float now = Yaw(time);
                sum += Mathf.DeltaAngle(last, now);
                last = now;
                turned[i] = sum;
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                ClipSampler.Sample(clip, animator.gameObject, time);
                for (int f = 0; f < 2; f++)
                {
                    heights[f, i] = root.InverseTransformPoint(feet[f].position).y * scale;
                    lowest[f] = Mathf.Min(lowest[f], heights[f, i]);
                }
            }
            text.AppendLine(Invariant($"  {clip.name}: {clip.length:0.00}s, turns {sum:0}"));
            for (int f = 0; f < 2; f++)
            {
                int from = -1;
                float top = 0f;
                for (int i = 0; i <= count; i++)
                {
                    float over = heights[f, i] - lowest[f];
                    bool up = over > LiftFrom;
                    if (up && from < 0) { from = i; top = 0f; }
                    if (up) top = Mathf.Max(top, over);
                    if ((!up || i == count) && from >= 0)
                    {
                        float a = clip.length * from / count, b = clip.length * i / count;
                        text.AppendLine(Invariant($"    {(f == 0 ? "L" : "R")} up {a:0.00}-{b:0.00}s ({b - a:0.00}s) {top * 100f:0.0}cm high, body turned {turned[from]:0} -> {turned[i]:0}"));
                        from = -1;
                    }
                }
            }
        }
        finally
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(poses[i].localPosition, poses[i].localRotation);
            root.SetPositionAndRotation(rootPosition, rootRotation);
        }
    }

    // A foot is up while its ankle is more than this over the lowest it stands in the clip (m, game size).
    private const float LiftFrom = .015f;

    /// <summary>
    /// Cycles side by side (ClipBench cyclefilm=): each clip one row of eight moments through its cycle, from the side at the
    /// game's size, to look at how a jog, a run and a sprint differ, not only measure their speeds.
    /// </summary>
    internal static void Cycles(Animator animator, float scale, System.Collections.Generic.IReadOnlyList<AnimationClip> clips, string path, StringBuilder text)
    {
        var root = animator.transform;
        var bones = root.GetComponentsInChildren<Transform>();
        var poses = Array.ConvertAll(bones, b => (b.localPosition, b.localRotation));
        Vector3 rootPosition = root.position, rootScale = root.localScale;
        Quaternion rootRotation = root.rotation;
        var made = new List<Object>();
        var pictures = new List<Texture2D>();
        bool wasAsync = UnityEditor.ShaderUtil.allowAsyncCompilation;
        const int Moments = 8;
        try
        {
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
            var lens = Lens(animator.gameObject.scene, made);
            foreach (var clip in clips)
                for (int m = 0; m < Moments; m++)
                {
                    root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    root.localScale = rootScale * scale;
                    ClipSampler.Sample(clip, animator.gameObject, clip.length * m / Moments);
                    root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Vector3 hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    Vector3 aim = new Vector3(hips.x, .85f, hips.z), side = aim + new Vector3(2.6f, .1f, .2f);
                    pictures.Add(Shoot(lens, side, Quaternion.LookRotation(aim - side), 42f));
                }
            var sheet = new Texture2D(Size * Moments, Size * clips.Count, TextureFormat.RGB24, false);
            made.Add(sheet);
            for (int i = 0; i < pictures.Count; i++)
                sheet.SetPixels32(i % Moments * Size, (clips.Count - 1 - i / Moments) * Size, Size, Size, pictures[i].GetPixels32());
            sheet.Apply();
            System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
            text.AppendLine($"  {clips.Count} cycles, {Moments} moments each -> {path}");
        }
        finally
        {
            UnityEditor.ShaderUtil.allowAsyncCompilation = wasAsync;
            foreach (var picture in pictures)
                if (picture != null) Object.DestroyImmediate(picture);
            foreach (var thing in made)
            {
                if (thing is RenderTexture texture) texture.Release();
                if (thing != null) Object.DestroyImmediate(thing);
            }
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(poses[i].localPosition, poses[i].localRotation);
            root.SetPositionAndRotation(rootPosition, rootRotation);
            root.localScale = rootScale;
        }
    }

    private static Camera Lens(UnityEngine.SceneManagement.Scene scene, List<Object> made)
    {
        // Outside play nothing re-skins a mesh between two renders: each picture skins it again (as GunBench's do).
        foreach (var thing in scene.GetRootGameObjects())
            foreach (var skinned in thing.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skinned.updateWhenOffscreen = true;
                skinned.forceMatrixRecalculationPerRender = true;
            }
        var lensObject = new GameObject("Step bench lens") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lensObject, scene);
        made.Add(lensObject);
        var lens = lensObject.AddComponent<Camera>();
        lens.enabled = false;
        lens.clearFlags = CameraClearFlags.SolidColor;
        lens.backgroundColor = new Color(.42f, .44f, .47f);
        lens.allowHDR = false;
        lens.scene = scene;
        var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(lens);
        data.renderPostProcessing = false;
        data.renderShadows = false;
        foreach (var (name, facing, power) in new[] { ("Key", Quaternion.Euler(40f, 30f, 0f), 1.1f), ("Fill", Quaternion.Euler(25f, 210f, 0f), .8f) })
        {
            var holder = new GameObject("Step bench " + name) { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, scene);
            made.Add(holder);
            holder.transform.rotation = facing;
            var light = holder.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = power;
            light.shadows = LightShadows.None;
        }
        var film = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, hideFlags = HideFlags.HideAndDontSave };
        film.Create();
        made.Add(film);
        lens.targetTexture = film;
        return lens;
    }

    private static Texture2D Shoot(Camera lens, Vector3 spot, Quaternion facing, float fieldOfView)
    {
        lens.transform.SetPositionAndRotation(spot, facing);
        lens.fieldOfView = fieldOfView;
        lens.nearClipPlane = .02f;
        lens.aspect = 1f;
        var picture = new Texture2D(Size, Size, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            lens.Render();
            RenderTexture.active = lens.targetTexture;
            picture.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
            picture.Apply();
        }
        finally
        {
            RenderTexture.active = previous;
        }
        return picture;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
#endif
