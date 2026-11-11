using KINEMATION.FPSAnimationFramework.Runtime.Camera;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// KINEMATION FPS Animation Framework's camera controller (FPSCameraController) on our camera. The camera rides the body's
/// eyes, the camera socket FPSAF lines the sight up with, and turns with our look (PlayerBody places it). On top of that
/// this adds what KINEMATION's guns play on the camera:
/// - the fire shake (FPSCameraShake, per gun);
/// - the camera bone of KINEMATION's own gun clips, applied as FPS Animation Pack's FPSCameraAnimator applies it: the gun's
///   original clips (made for KINEMATION's arms, which carry the camera bone) play on a small camera-bone rig of its own,
///   and the bone's turn away from where the gun's pose holds it turns the camera. A reload sways the view as KINEMATION's
///   animators made it.
/// FPSAnimator calls UpdateCamera after the body and its hands are posed; the component itself stays switched off, so
/// FPSAF's own Update never moves the camera back to where it first was. The field of view stays ours (PlayerCameraEffects).
/// </summary>
[DisallowMultipleComponent]
public sealed class FPSBodyCamera : FPSCameraController
{
    // How quickly a gun clip's camera motion comes in and goes back to the gun's pose (s).
    private const float MotionBlend = .15f;
    // KINEMATION's camera bone in its gun clips, under the arms skeleton's root.
    private const string RootBone = "root", CameraBone = "camera_bone";

    private System.Func<bool> placed;
    private GameObject rig;
    private Transform cameraBone;
    private PlayableGraph motionGraph;
    private AnimationMixerPlayable motionMixer;
    private AnimationClipPlayable idle, action;
    private Quaternion rest = Quaternion.identity;
    private float gunWeight, actionLeft, actionWeight;
    private bool hasMotion;

    /// <summary><paramref name="isPlaced"/> says whether the body placed the camera this frame (so shakes never pile up).</summary>
    internal void Bind(System.Func<bool> isPlaced) => placed = isPlaced;

    /// <summary>How much a gun is held (drawn and on the hands): the gun's camera motion scales with it.</summary>
    internal void SetGunWeight(float weight) => gunWeight = Mathf.Clamp01(weight);

    public override void UpdateCamera()
    {
        // Paused (Esc stops time), nothing places the view afresh, so a reload's camera motion laid on again each frame
        // kept turning the view the way it was going (user, 2026-11-11 playtest: "장전 중일 때 ESC 누르면 카메라가 움직이던
        // 방향으로 계속 움직임"). Nothing moves the camera while time stands still.
        if (Time.deltaTime <= 0f) return;
        if (placed != null && !placed()) return;
        UpdateCameraShake();
        ApplyGunMotion();
    }

    // The field of view stays ours (Settings, aiming, sprint); FPSAF's weapons only ask for the aiming one, which ours sets.
    protected override void UpdateFOV() { }
    public override void UpdateTargetFOV(float newFov) { }

    /// <summary>A gun drawn: its pose clip holds the camera bone where the camera is at rest.</summary>
    internal void HoldGunMotion(AnimationClip pose)
    {
        if (pose == null || !BuildRig()) return;
        pose.SampleAnimation(rig, 0f);
        rest = Quaternion.Inverse(rig.transform.rotation) * cameraBone.rotation;
        Play(ref idle, 0, pose, 1f);
        actionLeft = 0f;
        hasMotion = true;
    }

    /// <summary>
    /// A gun clip (a reload) moves the camera as its camera bone moves, at the clip's own rate, from <paramref name="from"/>
    /// of the way through (a reload going on from where a cut-short one got to).
    /// </summary>
    internal void PlayGunMotion(AnimationClip clip, float rate, float from = 0f)
    {
        if (clip == null || !BuildRig()) return;
        rate = Mathf.Max(.01f, rate);
        from = Mathf.Clamp01(from);
        Play(ref action, 1, clip, rate, from * clip.length);
        actionLeft = clip.length * (1f - from) / rate;
    }

    /// <summary>The clip was cut short: the camera eases back to the gun's pose.</summary>
    internal void StopGunMotion() => actionLeft = 0f;

    private void ApplyGunMotion()
    {
        if (!hasMotion || cameraBone == null) return;
        float dt = Time.deltaTime;
        actionLeft -= dt;
        actionWeight = Mathf.MoveTowards(actionWeight, actionLeft > MotionBlend ? 1f : 0f, dt / MotionBlend);
        motionMixer.SetInputWeight(0, 1f - actionWeight);
        motionMixer.SetInputWeight(1, actionWeight);
        if (gunWeight <= 0f) return;
        Quaternion now = Quaternion.Inverse(rig.transform.rotation) * cameraBone.rotation;
        transform.rotation *= Quaternion.Slerp(Quaternion.identity, now * Quaternion.Inverse(rest), gunWeight);
    }

    private void Play(ref AnimationClipPlayable playable, int input, AnimationClip clip, float rate, float start = 0f)
    {
        if (playable.IsValid())
        {
            motionGraph.Disconnect(motionMixer, input);
            playable.Destroy();
        }
        playable = AnimationClipPlayable.Create(motionGraph, clip);
        playable.SetApplyFootIK(false);
        playable.SetTime(start);
        playable.SetSpeed(rate);
        motionGraph.Connect(playable, 0, motionMixer, input);
    }

    // KINEMATION's camera bone on a skeleton of its own (root, then the camera bone), which the gun clips animate by name.
    private bool BuildRig()
    {
        if (rig != null) return true;
        rig = new GameObject("KINEMATION camera bone") { hideFlags = HideFlags.DontSave };
        var root = new GameObject(RootBone).transform;
        root.SetParent(rig.transform, false);
        cameraBone = new GameObject(CameraBone).transform;
        cameraBone.SetParent(root, false);
        var animator = rig.AddComponent<Animator>();
        // KINEMATION's clips play here whole, events and all.
        rig.AddComponent<KinemationClipEvents>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        motionGraph = PlayableGraph.Create("KINEMATION camera bone");
        motionGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        motionMixer = AnimationMixerPlayable.Create(motionGraph, 2);
        AnimationPlayableOutput.Create(motionGraph, "Camera bone", animator).SetSourcePlayable(motionMixer);
        motionGraph.Play();
        return true;
    }

    private void OnDestroy()
    {
        if (motionGraph.IsValid()) motionGraph.Destroy();
        if (rig != null) Destroy(rig);
    }
}
