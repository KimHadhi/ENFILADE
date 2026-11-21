#if UNITY_EDITOR
using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// A turn on the spot as the game puts it together, without playing (ClipBench, request line turnsim=): the stance's idle
/// under the turn clip on a layer (PlayerBody's TurnLayer), the body turned by the clip's own root curve from where its turn
/// starts to where it is done (GaitMoves.csv start, end), read at 60 a second. Per layer mask it says how far each foot
/// slides over the ground while it is down (a foot down should stay put: the curve is there for that), the most it moves in
/// a frame then, and how far each foot is from where the idle has it when the layer comes in and goes out (what the layer's
/// blend moves it by in its 0.15 s). Made for the user's report of 2026-11-21 that a turn on the spot crept round and then
/// thumped a foot down ("쪼로로... 오다가 쿵! 발 딛는"), to tell the cause before changing anything.
/// </summary>
internal static class TurnBench
{
    private const float Rate = 60f;
    // A foot counts as down while its ankle is within this of the lowest it gets in the turn (m, game size).
    private const float DownWithin = .03f;

    internal static void Simulate(Animator animator, float scale, AnimationClip turn, AnimationClip idle, float start, float end,
        StringBuilder text)
    {
        var turned = GaitMoveBake.TurnedFinely(turn, out float whole);
        if (turned == null || idle == null)
        {
            text.AppendLine($"  {turn.name}: ! no root curve or no idle");
            return;
        }
        var legs = Mask(AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg);
        var bodyLegs = Mask(AvatarMaskBodyPart.Body, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg);
        text.AppendLine(Invariant($"  {turn.name} over {idle.name}: turns {whole:0} from {start:0.00} to {end:0.00}s"));
        foreach (var (label, mask) in new[] { ("legs only (now)", legs), ("hips + legs", bodyLegs), ("clip alone", (AvatarMask)null) })
            text.AppendLine("    " + label.PadRight(16) + Run(animator, scale, turn, idle, start, end, turned, mask));
        Object.DestroyImmediate(legs);
        Object.DestroyImmediate(bodyLegs);
    }

    private static string Run(Animator animator, float scale, AnimationClip turn, AnimationClip idle, float start, float end,
        float[] turned, AvatarMask mask)
    {
        var root = animator.transform;
        var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var bones = root.GetComponentsInChildren<Transform>();
        var poses = Array.ConvertAll(bones, b => (b.localPosition, b.localRotation));
        Vector3 rootPosition = root.position;
        Quaternion rootRotation = root.rotation;
        // The bones the layer's mask gives the turn clip: the legs, and with the body part the hips and spine too. A
        // PlayableGraph would layer in muscle space, but in the editor's preview scene it left the model unposed; the same
        // avatar's bones copied over in their own space come to the same pose.
        var taken = new System.Collections.Generic.HashSet<Transform>();
        void Take(params HumanBodyBones[] list)
        {
            foreach (var bone in list)
                if (animator.GetBoneTransform(bone) is Transform found) taken.Add(found);
        }
        if (mask == null || mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg))
            Take(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
        if (mask == null || mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg))
            Take(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
        if (mask == null || mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body))
            Take(HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest);
        bool whole = mask == null;
        try
        {
            float Yaw(float time) => turned[Mathf.Clamp(Mathf.RoundToInt(time / turn.length * (turned.Length - 1)), 0, turned.Length - 1)];
            var idlePose = new (Vector3, Quaternion)[bones.Length];
            (Vector3 left, Vector3 right) Pose(float time, bool withTurn)
            {
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                ClipSampler.Sample(idle, animator.gameObject, time % Mathf.Max(.01f, idle.length));
                for (int i = 0; i < bones.Length; i++) idlePose[i] = (bones[i].localPosition, bones[i].localRotation);
                if (withTurn)
                {
                    root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    ClipSampler.Sample(turn, animator.gameObject, time);
                    for (int i = 0; i < bones.Length; i++)
                        if (bones[i] != root && !whole && !taken.Contains(bones[i]))
                            bones[i].SetLocalPositionAndRotation(idlePose[i].Item1, idlePose[i].Item2);
                }
                root.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, Yaw(time), 0f));
                return (leftFoot.position * scale, rightFoot.position * scale);
            }

            // How far each foot is from where the idle alone has it, as the layer comes in and goes out.
            var idleIn = Pose(start, false);
            var turnIn = Pose(start, true);
            var idleOut = Pose(end, false);
            var turnOut = Pose(end, true);
            var idleLast = Pose(turn.length, false);
            var turnLast = Pose(turn.length, true);

            int count = Mathf.Max(2, Mathf.CeilToInt((end - start) * Rate));
            var left = new Vector3[count + 1];
            var right = new Vector3[count + 1];
            // How far the head goes from where the idle has it then, in the turning body's own frame (the camera rides it).
            float headMost = 0f;
            for (int i = 0; i <= count; i++)
            {
                float time = start + (end - start) * i / count;
                Pose(time, false);
                Vector3 still = root.InverseTransformPoint(head.position);
                var feet = Pose(time, true);
                headMost = Mathf.Max(headMost, (root.InverseTransformPoint(head.position) - still).magnitude * scale);
                left[i] = feet.left;
                right[i] = feet.right;
            }
            Slide(left, count, end - start, out float leftSlide, out float leftFast);
            Slide(right, count, end - start, out float rightSlide, out float rightFast);
            return Invariant($"slide while down L {leftSlide * 100f,4:0}cm R {rightSlide * 100f,4:0}cm (fastest L {leftFast:0.0} R {rightFast:0.0} m/s) | ")
                + Invariant($"in L {Flat(idleIn.left - turnIn.left) * 100f,3:0}cm R {Flat(idleIn.right - turnIn.right) * 100f,3:0}cm | ")
                + Invariant($"out L {Flat(idleOut.left - turnOut.left) * 100f,3:0}cm R {Flat(idleOut.right - turnOut.right) * 100f,3:0}cm")
                + Invariant($" (at its end L {Flat(idleLast.left - turnLast.left) * 100f,3:0}cm R {Flat(idleLast.right - turnLast.right) * 100f,3:0}cm)")
                + Invariant($" | head off the idle's up to {headMost * 100f:0}cm");
        }
        finally
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(poses[i].localPosition, poses[i].localRotation);
            root.SetPositionAndRotation(rootPosition, rootRotation);
        }
    }

    // Where a turn's feet have come to rest (s): the first moment from <paramref name="from"/> (its turn all but done) after
    // which both feet stay within SettledWithin of where the clip leaves them, the body turned by its own curve. The game
    // lets a turn go there (GaitMoveBake's end): let go once the hips had turned (98%), the last foot was still on its way
    // and the layer's blend dragged it there (TurnIP_R90's left foot 19 cm).
    internal static float SettledAt(Animator animator, float scale, AnimationClip turn, float from)
    {
        var turned = GaitMoveBake.TurnedFinely(turn, out _);
        if (turned == null) return from;
        var root = animator.transform;
        var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (leftFoot == null || rightFoot == null) return from;
        var bones = root.GetComponentsInChildren<Transform>();
        var poses = Array.ConvertAll(bones, b => (b.localPosition, b.localRotation));
        Vector3 rootPosition = root.position;
        Quaternion rootRotation = root.rotation;
        try
        {
            (Vector3, Vector3) Feet(float time)
            {
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                ClipSampler.Sample(turn, animator.gameObject, time);
                float yaw = turned[Mathf.Clamp(Mathf.RoundToInt(time / turn.length * (turned.Length - 1)), 0, turned.Length - 1)];
                root.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
                return (leftFoot.position * scale, rightFoot.position * scale);
            }
            var (leftLast, rightLast) = Feet(turn.length);
            int count = Mathf.Max(2, Mathf.CeilToInt((turn.length - from) * Rate));
            float settled = turn.length;
            for (int i = count; i >= 0; i--)
            {
                float time = from + (turn.length - from) * i / count;
                var (left, right) = Feet(time);
                if ((left - leftLast).magnitude > SettledWithin || (right - rightLast).magnitude > SettledWithin) break;
                settled = time;
            }
            return settled;
        }
        finally
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(poses[i].localPosition, poses[i].localRotation);
            root.SetPositionAndRotation(rootPosition, rootRotation);
        }
    }

    // Feet this near where a turn leaves them have come to rest (m, game size).
    private const float SettledWithin = .02f;

    // ---- Pictures of a turn (ClipBench turnfilm=), to look at what the game puts together, not only measure it ----

    private const int FilmSize = 256;
    private static readonly Color Backdrop = new Color(.42f, .44f, .47f);

    /// <summary>
    /// A sheet of a turn as the game now puts it together (the stance's idle, the turn clip's hips and legs over it, the body
    /// turned by the clip's own curve), at moments from just before it starts to just after its feet have come to rest:
    /// from outside (a fixed camera, so the turn shows), from above (where the feet go), and from the eyes looking down
    /// with the view already turned (what the player sees of it). Written to <paramref name="path"/>.
    /// </summary>
    internal static void Film(Animator animator, float scale, AnimationClip turn, AnimationClip idle, float start, float end, string path,
        StringBuilder text)
    {
        var turned = GaitMoveBake.TurnedFinely(turn, out float whole);
        if (turned == null || idle == null)
        {
            text.AppendLine($"  {turn.name}: ! no root curve or no idle");
            return;
        }
        var root = animator.transform;
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
        var bones = root.GetComponentsInChildren<Transform>();
        var poses = Array.ConvertAll(bones, b => (b.localPosition, b.localRotation));
        Vector3 rootPosition = root.position;
        Quaternion rootRotation = root.rotation;
        var taken = new System.Collections.Generic.HashSet<Transform>();
        foreach (var bone in new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
                     HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
                     HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
            if (animator.GetBoneTransform(bone) is Transform found) taken.Add(found);
        var scene = animator.gameObject.scene;
        var made = new System.Collections.Generic.List<Object>();
        var pictures = new System.Collections.Generic.List<Texture2D>();
        var renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        bool wasAsync = UnityEditor.ShaderUtil.allowAsyncCompilation;
        try
        {
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
            var lensObject = new GameObject("Turn film lens") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lensObject, scene);
            made.Add(lensObject);
            var lens = lensObject.AddComponent<Camera>();
            lens.enabled = false;
            lens.clearFlags = CameraClearFlags.SolidColor;
            lens.backgroundColor = Backdrop;
            lens.allowHDR = false;
            lens.scene = scene;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(lens);
            data.renderPostProcessing = false;
            data.renderShadows = false;
            foreach (var (name, facing, power) in new[] { ("Key", Quaternion.Euler(40f, 30f, 0f), 1.1f), ("Fill", Quaternion.Euler(25f, 210f, 0f), .8f) })
            {
                var lightObject = new GameObject("Turn film " + name) { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, scene);
                made.Add(lightObject);
                lightObject.transform.rotation = facing;
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = power;
                light.shadows = LightShadows.None;
            }
            var flat = new UnityEngine.Rendering.SphericalHarmonicsL2();
            flat.AddAmbientLight(new Color(.45f, .45f, .48f));
            var block = new MaterialPropertyBlock();
            foreach (var skinned in renderers)
            {
                skinned.updateWhenOffscreen = true;
                skinned.forceMatrixRecalculationPerRender = true;
                skinned.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                skinned.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.CustomProvided;
                skinned.GetPropertyBlock(block);
                block.CopySHCoefficientArraysFrom(new[] { flat });
                skinned.SetPropertyBlock(block);
            }
            var film = new RenderTexture(FilmSize, FilmSize, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, hideFlags = HideFlags.HideAndDontSave };
            film.Create();
            made.Add(film);
            Texture2D Shoot(Vector3 spot, Quaternion facing, float fieldOfView)
            {
                lens.transform.SetPositionAndRotation(spot, facing);
                lens.fieldOfView = fieldOfView;
                lens.nearClipPlane = .02f;
                lens.aspect = 1f;
                var picture = new Texture2D(FilmSize, FilmSize, TextureFormat.RGB24, false);
                var previous = RenderTexture.active;
                try
                {
                    lens.targetTexture = film;
                    lens.Render();
                    RenderTexture.active = film;
                    picture.ReadPixels(new Rect(0f, 0f, FilmSize, FilmSize), 0, 0);
                    picture.Apply();
                }
                finally
                {
                    RenderTexture.active = previous;
                    lens.targetTexture = null;
                }
                return picture;
            }
            float Yaw(float time) => turned[Mathf.Clamp(Mathf.RoundToInt(time / turn.length * (turned.Length - 1)), 0, turned.Length - 1)];
            var idlePose = new (Vector3, Quaternion)[bones.Length];
            void Pose(float time, bool withTurn, float yaw)
            {
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                ClipSampler.Sample(idle, animator.gameObject, time % Mathf.Max(.01f, idle.length));
                for (int i = 0; i < bones.Length; i++) idlePose[i] = (bones[i].localPosition, bones[i].localRotation);
                if (withTurn)
                {
                    root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    ClipSampler.Sample(turn, animator.gameObject, time);
                    for (int i = 0; i < bones.Length; i++)
                        if (bones[i] != root && !taken.Contains(bones[i]))
                            bones[i].SetLocalPositionAndRotation(idlePose[i].Item1, idlePose[i].Item2);
                }
                root.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            }
            // From just before the turn (the idle, not turned) to just after its end (the idle, turned all the way).
            const int Frames = 11;
            float unit = 1f / scale;
            float final = Yaw(end);
            for (int frame = 0; frame < Frames; frame++)
            {
                float time = Mathf.Lerp(start, end, Mathf.Clamp01((frame - 1) / (float)(Frames - 3)));
                if (frame == 0) Pose(start, false, 0f);
                else if (frame == Frames - 1) Pose(end, false, final);
                else Pose(time, true, Yaw(time));
                Vector3 centre = new Vector3(hips.position.x, 0f, hips.position.z);
                Vector3 aim = centre + Vector3.up * .55f * unit;
                Vector3 outside = aim + new Vector3(1.7f, .9f, 1.9f) * unit;
                pictures.Add(Shoot(outside, Quaternion.LookRotation(aim - outside), 38f));
                Vector3 above = centre + Vector3.up * 2.4f * unit;
                pictures.Add(Shoot(above, Quaternion.LookRotation(Vector3.down, Vector3.forward), 40f));
                Vector3 eyes = leftEye != null && rightEye != null ? (leftEye.position + rightEye.position) * .5f : head.position;
                pictures.Add(Shoot(eyes + Quaternion.Euler(0f, final, 0f) * Vector3.forward * .03f * unit, Quaternion.Euler(60f, final, 0f), 75f));
            }
            var sheet = new Texture2D(FilmSize * Frames, FilmSize * 3, TextureFormat.RGB24, false);
            made.Add(sheet);
            for (int frame = 0; frame < Frames; frame++)
                for (int view = 0; view < 3; view++)
                    sheet.SetPixels32(frame * FilmSize, (2 - view) * FilmSize, FilmSize, FilmSize, pictures[frame * 3 + view].GetPixels32());
            sheet.Apply();
            System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
            text.AppendLine(Invariant($"  {turn.name} over {idle.name}: {Frames} moments from {start:0.00} to {end:0.00}s, turns {whole:0} -> {path}"));
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
        }
    }

    // Over the ground while the foot is down (within DownWithin of its lowest): how far it went, and its fastest.
    private static void Slide(Vector3[] path, int count, float span, out float slid, out float fastest)
    {
        slid = fastest = 0f;
        float lowest = float.PositiveInfinity;
        foreach (var at in path) lowest = Mathf.Min(lowest, at.y);
        float dt = span / count;
        for (int i = 1; i <= count; i++)
        {
            if (path[i].y > lowest + DownWithin || path[i - 1].y > lowest + DownWithin) continue;
            float step = Flat(path[i] - path[i - 1]);
            slid += step;
            fastest = Mathf.Max(fastest, step / dt);
        }
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    private static AvatarMask Mask(params AvatarMaskBodyPart[] parts)
    {
        var mask = new AvatarMask();
        for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
            mask.SetHumanoidBodyPartActive(part, Array.IndexOf(parts, part) >= 0);
        return mask;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
#endif
