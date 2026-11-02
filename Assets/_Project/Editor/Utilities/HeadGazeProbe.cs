using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

// Temporary validation helper. Remove after the camera/head change has been checked.
public static class HeadGazeProbe
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly StringBuilder Report = new StringBuilder();
    private static int checks;
    private static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    private static object Call(object target, string name, params object[] values) => target.GetType().GetMethod(name, Flags).Invoke(target, values);
    private static void Check(bool pass, string message)
    {
        if (!pass) throw new Exception(message);
        checks++;
    }

    [MenuItem("Tools/Development/Validate Head Gaze")]
    public static void Run()
    {
        Report.Clear(); checks = 0;
        try
        {
            if (EditorApplication.isPlaying) throw new Exception("Run the isolated probe outside Play mode.");
            var profile = AssetDatabase.LoadAssetAtPath<PlayerBodyProfile>("Assets/_Project/Resources/Characters/PlayerBody.asset");
            Check(profile != null, "Missing body profile");
            foreach (var skin in profile.Skins) Probe(profile, skin.Model, skin.Id);
            Report.AppendLine("PASS: " + checks + " assertions.");
            Debug.Log(Report.ToString());
        }
        catch (Exception exception)
        {
            Report.AppendLine("FAIL: " + exception);
            Debug.LogError(Report.ToString());
        }
        finally
        {
            Directory.CreateDirectory(".utmp/head-camera");
            File.WriteAllText(".utmp/head-camera/validation.txt", Report.ToString());
        }
    }

    private static void Probe(PlayerBodyProfile profile, GameObject prefab, string label)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Head Gaze Probe") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, scene);
        root.SetActive(false);
        var movement = root.AddComponent<PlayerMovement>(); movement.enabled = false;
        var body = root.AddComponent<PlayerBody>(); body.enabled = false;
        var model = UnityEngine.Object.Instantiate(prefab, root.transform);
        var animator = model.GetComponentInChildren<Animator>();
        PlayableGraph graph = default;
        try
        {
            Check(animator != null && animator.isHuman, label + " requires a humanoid avatar");
            animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            var left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var pivot = new GameObject("LookPivot").transform; pivot.SetParent(root.transform, false);
            var effects = new GameObject("CameraEffectsPivot").transform; effects.SetParent(pivot, false);
            var view = new GameObject("Camera").transform; view.SetParent(effects, false);
            Set(body, "player", movement); Set(body, "profile", profile); Set(body, "body", model.transform);
            Set(body, "animator", animator); Set(body, "head", head); Set(body, "neck", neck);
            Set(body, "view", view); Set(body, "lookPivot", pivot);
            Set(body, "restForward", Vector3.Cross(right.position - left.position, model.transform.up).normalized);
            var spine = (List<Transform>)Get(body, "spine");
            foreach (var bone in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
                if (animator.GetBoneTransform(bone) is Transform found) spine.Add(found);
            Call(body, "SetUpGaze");
            Quaternion basis = (Quaternion)Get(body, "headToGaze");
            graph = PlayableGraph.Create("Head Gaze Probe"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var mixer = AnimationMixerPlayable.Create(graph, 0);
            Set(body, "graph", graph); Set(body, "mixer", mixer);
            AnimationPlayableOutput.Create(graph, "Probe", animator).SetSourcePlayable(mixer);
            string[] clips = { "mx_idle", "mx_turn_left", "mx_turn_right", "mx_fall_roll" };
            foreach (string clip in clips) { Check(profile.Find(clip) != null, "Missing clip " + clip); Call(body, "AddChannel", clip, 0f); }
            Call(body, "SetUpAnimationViewChannels");
            var channels = (IList)Get(body, "channels");
            root.SetActive(true); graph.Play();
            for (int clip = 0; clip < clips.Length; clip++)
            {
                for (int input = 0; input < clips.Length; input++) mixer.SetInputWeight(input, input == clip ? 1f : 0f);
                for (int sample = 0; sample <= 10; sample++)
                {
                    var playable = (AnimationClipPlayable)Get(channels[clip], "Playable");
                    playable.SetTime((float)Get(channels[clip], "Length") * sample / 10f); graph.Evaluate(0f);
                    pivot.rotation = Quaternion.Euler(sample % 2 == 0 ? 0f : -37f, 123f, 0f);
                    Set(body, "animationViewRotation", Quaternion.identity);
                    Quaternion authored = head.rotation;
                    Call(body, "UpdateGaze", 1f);
                    Call(body, "BendAndTwist", 0f, 0f, 1f);
                    Call(body, "ApplyGazeToCamera");
                    if (clip < 3)
                    {
                        Check(Quaternion.Angle(head.rotation * basis, pivot.rotation) < .1f, label + " head aim " + clips[clip]);
                        Check(Quaternion.Angle(view.rotation, pivot.rotation) < .1f, label + " camera aim " + clips[clip]);
                    }
                    else
                    {
                        Check(Quaternion.Angle(head.rotation, authored) < .1f, label + " preserves rolling head");
                        Check(Quaternion.Angle(view.rotation, pivot.rotation) < 27f, label + " bounds rolling camera");
                    }
                }
                Report.AppendLine(label + ": " + clips[clip] + " — 11 actual humanoid poses passed.");
            }
            foreach (float pitch in new[] { 0f, 80f, 100f, 179f, 181f, 260f, 280f, 360f })
            {
                head.rotation = model.transform.rotation * Quaternion.Euler(pitch, 0f, 0f) * Quaternion.Inverse(basis);
                Set(body, "animationViewRotation", Quaternion.identity); Call(body, "UpdateGaze", 5f);
                var offset = (Quaternion)Get(body, "animationViewRotation");
                Check(Vector3.Angle(offset * Vector3.right, Vector3.right) < .1f, label + " inversion adds no yaw/roll " + pitch);
                Check(Quaternion.Angle(offset, Quaternion.identity) <= 11.8f, label + " pitch is comfort bounded");
                pivot.rotation = Quaternion.Euler(28f, -77f, 0f); Call(body, "UpdateGaze", .000001f);
                Check(Quaternion.Angle((Quaternion)Get(body, "gazeRotation"), pivot.rotation * offset) < .1f, label + " immediate mouse input");
            }
            effects.localRotation = Quaternion.Euler(2f, 3f, 4f); Call(body, "ApplyGazeToCamera");
            Check(Quaternion.Angle(view.rotation, (Quaternion)Get(body, "gazeRotation") * effects.localRotation) < .1f, label + " effects applied once");
            Call(body, "PlaceViewBehind"); Check(Quaternion.Angle(view.localRotation, Quaternion.identity) < .1f, label + " F5 reset");
            view.localRotation = Quaternion.Euler(9f, 8f, 7f);
            Set(body, "body", null); Call(body, "Teardown");
            Check(Quaternion.Angle(view.localRotation, Quaternion.identity) < .1f, label + " teardown reset");
            Report.AppendLine(label + ": inversion, immediate input, effects, F5 and teardown passed.");
        }
        finally
        {
            Set(body, "body", null);
            if (graph.IsValid()) graph.Destroy();
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
