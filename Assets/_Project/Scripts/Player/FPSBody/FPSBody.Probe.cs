using System.IO;
using System.Reflection;
using System.Text;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;

// Temporary, 2026-11-07: the gun's pose from FPS Animation Framework doesn't show on the body in Unity, with nothing in
// the console. Two seconds after this PC's player draws a gun, what the Animator's graph and FPSAF are doing is written
// once to Logs/fpsaf-probe.txt (no console), and once more a second into a full aim: where the sight and hands are seen
// from the camera. Goes once the gun's pose and aim pass in Unity.
public sealed partial class FPSBody
{
    private float probeAt = -1f, adsProbeAt = -1f;
    private bool probed, adsProbed;

    // 2026-11-08: small turns standing with a gun leave the gun and hands still (the upper body doesn't turn after the view),
    // and the hands change abruptly when a reload ends, at the hip too. Both are written while they happen.
    private float lastProbeYaw, turnProbeUntil, turnProbeNext, reloadProbeFrom = -1f, reloadProbeUntil, reloadEndsAt;
    private bool hasProbeYaw;
    private int turnProbes, reloadProbes;

    // From the last 0.6 s of a reload's clip to 0.5 s after it, every frame, for the first two reloads.
    private void StartReloadProbe(float length)
    {
        if (reloadProbes >= 2 || length <= 0f) return;
        reloadProbes++;
        reloadEndsAt = Time.unscaledTime + length;
        reloadProbeFrom = reloadEndsAt - .6f;
        reloadProbeUntil = reloadEndsAt + .5f;
        Write(new StringBuilder($"==== {System.DateTime.Now:HH:mm:ss} RELOAD {animator.name} gun {held?.Data?.name}, clip {length:0.00} s\n"));
    }

    private void ProbeTurn()
    {
        Transform frame = lookFrame != null ? lookFrame : eyes;
        if (frame == null || turnProbes >= 3) return;
        float yaw = transform.eulerAngles.y;
        if (!hasProbeYaw) { lastProbeYaw = yaw; hasProbeYaw = true; return; }
        bool still = movement != null && Vector3.ProjectOnPlane(movement.ActualVelocity, Vector3.up).magnitude < .1f;
        if (Time.unscaledTime > turnProbeUntil)
        {
            float turned = Mathf.Abs(Mathf.DeltaAngle(lastProbeYaw, yaw));
            lastProbeYaw = yaw;
            if (!still || Framework < .999f || turned < .3f || turned > 10f) return;
            turnProbeUntil = Time.unscaledTime + 1.5f;
            turnProbeNext = 0f;
            turnProbes++;
            Write(new StringBuilder($"==== {System.DateTime.Now:HH:mm:ss} TURN {animator.name} gun {held?.Data?.name}: view turned {turned:0.0}° in a frame, standing still\n"));
        }
        if (Time.unscaledTime < turnProbeNext) return;
        turnProbeNext = Time.unscaledTime + .1f;
        float bodyYaw = animator.transform.eulerAngles.y;
        float Yaw(Transform t) => t != null ? Mathf.DeltaAngle(bodyYaw, Mathf.Atan2(t.forward.x, t.forward.z) * Mathf.Rad2Deg) : float.NaN;
        var aimPoint = held?.AimPoint;
        Write(new StringBuilder($"  view {Mathf.DeltaAngle(bodyYaw, yaw):0.0} turnOffset {input.GetValue<float>(FPSANames.TurnOffset):0.0} "
            + $"mouse {input.GetValue<Vector4>(FPSANames.MouseInput)} look weight {input.GetValue<float>("LookLayerWeight"):0.00} framework {Framework:0.00} | "
            + $"from the body: upper chest {Yaw(animator.GetBoneTransform(HumanBodyBones.UpperChest)):0.0} head {Yaw(animator.GetBoneTransform(HumanBodyBones.Head)):0.0} "
            + $"gun {Yaw(aimPoint):0.0} camera {Yaw(view):0.0}\n"));
    }

    private void ProbeReloadEnd()
    {
        if (reloadProbeFrom < 0f || Time.unscaledTime < reloadProbeFrom || Time.unscaledTime > reloadProbeUntil || view == null || held == null) return;
        string In(Transform point) => point != null ? view.InverseTransformPoint(point.position).ToString("F3") : "-";
        var type = typeof(FPSPlayablesController);
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        string slot = type.GetField("_slotMixer", Flags)?.GetValue(playables) is FPSAnimatorMixer mixer ? $"{mixer.BlendInWeight:0.00}/{mixer.BlendOutWeight:0.00}" : "-";
        Write(new StringBuilder($"  clip end {Time.unscaledTime - reloadEndsAt:+0.000;-0.000}: reloading {held.Reloading} runtime {controller.Runtime?.IsReloading} slot in/out {slot} "
            + $"framework {Framework:0.00} gun {In(held.transform)} right {In(animator.GetBoneTransform(HumanBodyBones.RightHand))} left {In(animator.GetBoneTransform(HumanBodyBones.LeftHand))}\n"));
    }

    private void Probe()
    {
        if (movement == null || !movement.IsLocal || movement.IsNetworkDriven || animator == null) return;
        ProbeTurn();
        ProbeReloadEnd();
        ProbeAds();
        if (probed) return;
        if (probeAt < 0f) { probeAt = Time.unscaledTime + 2f; return; }
        if (Time.unscaledTime < probeAt) return;
        probed = true;
        var text = new StringBuilder();
        text.AppendLine($"==== {System.DateTime.Now:HH:mm:ss} {animator.name} gun {held?.Data?.name} linked {linked} hold {holdWeight:0.00} share {GunShare:0.00} "
            + $"upperFromFramework {body.UpperBodyFromFramework} profileLinked {framework.HasLinkedProfile}");
        var graph = animator.playableGraph;
        text.AppendLine($"graph valid {graph.IsValid()} playing {graph.IsPlaying()} outputs {graph.GetOutputCount()}");
        for (int i = 0; i < graph.GetOutputCount(); i++)
        {
            var output = graph.GetOutput(i);
            var source = output.GetSourcePlayable();
            string line = $"  out {i}: ";
            if (output.IsPlayableOutputOfType<AnimationPlayableOutput>())
            {
                var animation = (AnimationPlayableOutput)output;
                line += $"anim sort {animation.GetSortingOrder()} stream {animation.GetAnimationStreamSource()} weight {animation.GetWeight():0.00} ";
            }
            line += $"source {(source.IsValid() ? source.GetPlayableType().Name : "none")} inputs {(source.IsValid() ? source.GetInputCount() : 0)}";
            if (source.IsValid())
                for (int k = 0; k < source.GetInputCount(); k++)
                    line += $" [{k}:{(source.GetInput(k).IsValid() ? source.GetInput(k).GetPlayableType().Name : "-")} w{source.GetInputWeight(k):0.00}]";
            text.AppendLine(line);
        }

        var type = typeof(FPSPlayablesController);
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        object Field(string field) => type.GetField(field, Flags)?.GetValue(playables);
        text.AppendLine($"controllerWeight {Field("_controllerWeight")} playablesWeightIndex {Field("_playablesWeightPropertyIndex")} "
            + $"playablesWeight {input.GetValue<float>(FPSANames.PlayablesWeight)} upperMask {(playables.upperBodyMask != null ? playables.upperBodyMask.name : "null")}");
        if (Field("_masterMixer") is AnimationLayerMixerPlayable master && master.IsValid())
        {
            string line = $"master inputs {master.GetInputCount()}";
            for (int k = 0; k < master.GetInputCount(); k++)
                line += $" [{k}:{(master.GetInput(k).IsValid() ? master.GetInput(k).GetPlayableType().Name : "-")} w{master.GetInputWeight(k):0.00}]";
            text.AppendLine(line + $" outputs {master.GetOutputCount()}");
        }
        foreach (string mixerName in new[] { "_overlayPoseMixer", "_slotMixer", "_overrideMixer" })
        {
            if (!(Field(mixerName) is FPSAnimatorMixer mixer) || !mixer.mixer.IsValid()) { text.AppendLine($"{mixerName} invalid"); continue; }
            string line = $"{mixerName} blendIn {mixer.BlendInWeight:0.00} inputs {mixer.mixer.GetInputCount()}";
            for (int k = 0; k < mixer.mixer.GetInputCount(); k++)
            {
                var playable = mixer.mixer.GetInput(k);
                string what = !playable.IsValid() ? "-" : playable.GetPlayableType() == typeof(AnimationClipPlayable)
                    ? (((AnimationClipPlayable)playable).GetAnimationClip() is AnimationClip clip ? clip.name + (clip.isHumanMotion ? "(human)" : "(generic)") : "no clip")
                    + $" t{playable.GetTime():0.00}" : playable.GetPlayableType().Name;
                line += $" [{k}:{what} w{mixer.mixer.GetInputWeight(k):0.00}]";
            }
            text.AppendLine(line);
        }
        var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (rightHand != null && leftHand != null && held != null)
            text.AppendLine($"right hand to gun {Vector3.Distance(rightHand.position, held.transform.position):0.000} m, left hand to gun "
                + $"{Vector3.Distance(leftHand.position, held.transform.position):0.000} m, hands apart {Vector3.Distance(rightHand.position, leftHand.position):0.000} m");
        AppendGunInView(text);
        Write(text);
    }

    // Once, a second after the sights are fully up: where the sight and the hands are seen from the camera.
    private void ProbeAds()
    {
        if (adsProbed || !HoldsGun) return;
        if (controller.AimBlend < .999f) { adsProbeAt = -1f; return; }
        if (adsProbeAt < 0f) { adsProbeAt = Time.unscaledTime + 1f; return; }
        if (Time.unscaledTime < adsProbeAt) return;
        adsProbed = true;
        var text = new StringBuilder();
        text.AppendLine($"==== {System.DateTime.Now:HH:mm:ss} ADS {animator.name} gun {held?.Data?.name} aimBlend {controller.AimBlend:0.00} aiming {aiming}");
        AppendGunInView(text);
        Write(text);
    }

    // The gun and both hands from the camera (x right, y up, z ahead, m), and how far the gun points off the view.
    private void AppendGunInView(StringBuilder text)
    {
        if (view == null || held == null) return;
        string In(Transform point) => point != null ? view.InverseTransformPoint(point.position).ToString("F3") : "-";
        var aimPoint = held.AimPoint;
        float off = aimPoint != null ? Vector3.Angle(aimPoint.forward, view.forward) : -1f;
        text.AppendLine($"in view: sight {In(aimPoint)} gun {In(held.transform)} muzzle {In(muzzle)} right hand {In(animator.GetBoneTransform(HumanBodyBones.RightHand))} "
            + $"left hand {In(animator.GetBoneTransform(HumanBodyBones.LeftHand))} eyes {In(eyes)} socket {In(rig?.CameraSocket)}");
        text.AppendLine($"sight off the view {off:0.0} deg, cover {cover:0.00} highReady {highReady} aimBlend {controller.AimBlend:0.00}");
        held.GripGaps(animator.GetBoneTransform(HumanBodyBones.RightHand), animator.GetBoneTransform(HumanBodyBones.LeftHand), rig?.IkWeaponBone,
            ModelScale, out float rightGap, out float leftGap);
        text.AppendLine($"grip measured {held.HasGrip}: right hand off its grip {rightGap:0.000} m, left hand off its grip {leftGap:0.000} m, fit scale {ModelScale:0.000}");
    }

    private static void Write(StringBuilder text)
    {
        string path = Path.Combine(Application.dataPath, "..", "Logs", "fpsaf-probe.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.AppendAllText(path, text.ToString());
    }
}
