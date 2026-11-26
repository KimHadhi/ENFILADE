#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// A turn on the spot as the game does it since 0.9.279, without playing (ClipBench, request line stepfilm=): the stance's
/// idle on the body at the game's size, the legs following a view that flicks 90° right and then 150° left as
/// PlayerBody.TurnOnTheSpot follows it, and the game's own FootPlanting keeping the feet on a floor and stepping them
/// round. It says when each foot stepped and how far a foot moved over the ground while it kept its place (it should
/// not), and draws the turns from outside and from above to Logs/ClipBench/steps-&lt;idle&gt;-&lt;skin&gt;.png, so the steps are
/// looked at, not only measured (the user, 2026-11-21: "turn in place 모션이 있는 거야 없는 거야").
/// </summary>
internal static class StepBench
{
    private const float Dt = 1f / 60f, End = 3.6f;
    // PlayerBody's numbers (Player.csv Body.*), as the game turns and plants.
    private const float TurnStart = 60f, TurnStepTime = .3f;
    private const float Twist = 45f, Drop = .05f, Release = .12f, StepTime = .22f, StepHeight = .05f;
    private const int Size = 200, Columns = 13;

    // The view: still, a flick 90° to the right, held, a turn 150° to the left, held.
    private static float View(float time) =>
        time < .5f ? 0f : time < .65f ? Mathf.Lerp(0f, 90f, (time - .5f) / .15f)
        : time < 2.2f ? 90f : time < 2.45f ? Mathf.Lerp(90f, -60f, (time - 2.2f) / .25f) : -60f;

    internal static void Film(Animator animator, float scale, AnimationClip idle, string path, StringBuilder text)
    {
        var root = animator.transform;
        var bones = root.GetComponentsInChildren<Transform>();
        var poses = Array.ConvertAll(bones, b => (b.localPosition, b.localRotation));
        Vector3 rootPosition = root.position, rootScale = root.localScale;
        Quaternion rootRotation = root.rotation;
        var scene = animator.gameObject.scene;
        var made = new List<Object>();
        var pictures = new List<Texture2D>();
        // The floor the feet find with the game's probes: a collider in the open scene (the probes ask the default physics
        // scene), for this run only; and one drawn in the preview scene, so the pictures show where the feet stand.
        var floor = new GameObject("Step bench floor") { hideFlags = HideFlags.HideAndDontSave };
        made.Add(floor);
        var box = floor.AddComponent<BoxCollider>();
        box.size = new Vector3(40f, 1f, 40f);
        floor.transform.position = Vector3.down * .5f;
        Physics.SyncTransforms();
        bool wasAsync = UnityEditor.ShaderUtil.allowAsyncCompilation;
        try
        {
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.localScale = rootScale * scale;
            // Measured in the rest pose, as PlayerBody does when it makes the body.
            var feet = FootPlanting.Measure(animator, root, root, root.forward);
            if (feet == null)
            {
                text.AppendLine("  ! no legs to plant");
                return;
            }
            var lens = Lens(scene, made);
            var shine = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(.30f, .32f, .30f) };
            made.Add(shine);
            var tiles = new GameObject("Step bench tiles") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(tiles, scene);
            made.Add(tiles);
            // A chequer of tiles 25 cm a side, so a foot that slides shows against it.
            var light = new Material(shine) { color = new Color(.52f, .55f, .52f) };
            made.Add(light);
            for (int x = -6; x < 6; x++)
                for (int z = -6; z < 6; z++)
                {
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Object.DestroyImmediate(tile.GetComponent<Collider>());
                    tile.hideFlags = HideFlags.HideAndDontSave;
                    tile.transform.SetParent(tiles.transform, false);
                    tile.transform.SetPositionAndRotation(new Vector3(x * .25f + .125f, .001f, z * .25f + .125f), Quaternion.Euler(90f, 0f, 0f));
                    tile.transform.localScale = Vector3.one * .25f;
                    tile.GetComponent<MeshRenderer>().sharedMaterial = (x + z & 1) == 0 ? shine : light;
                }
            var tuning = new FootPlanting.Tuning(.3f, .5f, .08f, 30f, 20f, 25f, .15f);
            float legYaw = 0f;
            var planner = new TurnPlanner();
            bool leftWas = false, rightWas = false;
            Vector3 leftKept = Vector3.zero, rightKept = Vector3.zero;
            float leftSlid = 0f, rightSlid = 0f;
            var steps = new StringBuilder();
            var shots = new List<float>();
            for (int i = 0; i < Columns; i++) shots.Add(.45f + i * .1f);
            for (int i = 0; i < Columns; i++) shots.Add(2.15f + i * .1f);
            int shot = 0;
            for (float time = 0f; time <= End; time += Dt)
            {
                // The legs as TurnOnTheSpot turns them.
                legYaw = planner.Tick(View(time), legYaw, true, Dt, TurnStart, TurnStepTime, TurnStart + 30f);
                // The animation pass, then the feet, as in the game's frame.
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                ClipSampler.Sample(idle, animator.gameObject, time % Mathf.Max(.01f, idle.length));
                root.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, legYaw, 0f));
                root.localScale = rootScale * scale;
                feet.Plant(true, 0f, Physics.DefaultRaycastLayers, tuning, Dt, FootPlanting.Stride.None,
                    new FootPlanting.Hold(true, Vector3.zero, root.eulerAngles.y, Twist, Drop, Release, StepTime, StepHeight, planner.Lead, planner.Share),
                    new FootPlanting.Steps(Vector3.zero, 0f, 0f));
                Note(feet.Left, "L", time, ref leftWas, ref leftKept, ref leftSlid, steps);
                Note(feet.Right, "R", time, ref rightWas, ref rightKept, ref rightSlid, steps);
                while (shot < shots.Count && time >= shots[shot] - Dt * .5f)
                {
                    Vector3 hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    Vector3 centre = new Vector3(hips.x, 0f, hips.z);
                    // Low at the feet from two sides, fixed in the world, so the steps and the turn both show.
                    Vector3 aim = centre + Vector3.up * .22f;
                    Vector3 side = aim + new Vector3(1.25f, .35f, .55f), front = aim + new Vector3(-.45f, .55f, 1.3f);
                    pictures.Add(Shoot(lens, side, Quaternion.LookRotation(aim - side), 38f));
                    pictures.Add(Shoot(lens, front, Quaternion.LookRotation(aim - front), 38f));
                    shot++;
                }
            }
            var sheet = new Texture2D(Size * Columns, Size * 4, TextureFormat.RGB24, false);
            made.Add(sheet);
            // Rows from the top: the right flick from the side, from the front; the left turn from the side, from the front.
            for (int i = 0; i < pictures.Count / 2; i++)
            {
                int column = i % Columns, turn = i / Columns;
                sheet.SetPixels32(column * Size, (3 - turn * 2) * Size, Size, Size, pictures[i * 2].GetPixels32());
                sheet.SetPixels32(column * Size, (2 - turn * 2) * Size, Size, Size, pictures[i * 2 + 1].GetPixels32());
            }
            sheet.Apply();
            System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
            text.AppendLine($"  over {idle.name}: view flicks 90° right at 0.5 s, turns 150° left at 2.2 s -> {path}");
            text.Append(steps);
            text.AppendLine(Invariant($"  a kept foot moved over the ground: L {leftSlid * 100f:0.0} cm, R {rightSlid * 100f:0.0} cm in all (0 = planted)"));
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
            Physics.SyncTransforms();
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(poses[i].localPosition, poses[i].localRotation);
            root.SetPositionAndRotation(rootPosition, rootRotation);
            root.localScale = rootScale;
        }
    }

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

    // A foot's steps as they start and land, and how far it moved while it kept its place.
    private static void Note(FootPlanting.Leg leg, string name, float time, ref bool was, ref Vector3 kept, ref float slid, StringBuilder steps)
    {
        Vector3 at = leg.Foot.position;
        if (leg.Stepping != was)
            steps.AppendLine(Invariant($"    {time:0.00}s {name} {(leg.Stepping ? "lifts" : "lands")}"));
        was = leg.Stepping;
        if (leg.Kept && !leg.Stepping)
        {
            if (kept != Vector3.zero) slid += new Vector2(at.x - kept.x, at.z - kept.z).magnitude;
            kept = at;
        }
        else kept = Vector3.zero;
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
