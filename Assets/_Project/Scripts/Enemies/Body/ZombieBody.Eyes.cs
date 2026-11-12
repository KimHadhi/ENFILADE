using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// At the user's word (runners were deadly but couldn't be told apart): a runner's eyes glow red and leave an afterimage
/// as it moves, on every PC (its kind is shared, ZombieType). Each eye is a small object on the head bone: a camera-facing
/// glow (Resources/Enemies/RunnerEyes.shader), never smaller on screen than a few pixels so a runner far off still shows,
/// and a short TrailRenderer behind it. Where the eyes sit is measured on the head as the body is dressed: the rig's
/// eye bones if it has them, else ahead of and above the head bone (ZombieRunner's eye numbers). They go out as it dies,
/// before its body becomes the corpse.
/// </summary>
public sealed partial class ZombieBody
{
    private const string EyeShaderPath = "Enemies/RunnerEyes";

    private static Material eyeMaterial, trailMaterial;
    private static Mesh eyeQuad;
    private static readonly System.Collections.Generic.HashSet<Renderer> eyeGlows = new System.Collections.Generic.HashSet<Renderer>();

    /// <summary>A runner's eye glow: not the body, so no hit flash is laid over it (HitFlashRenderer).</summary>
    public static bool IsEye(Renderer renderer) => renderer != null && eyeGlows.Contains(renderer);
    private Vector3 leftEyeLocal, rightEyeLocal;
    private bool eyesMeasured;
    private GameObject leftEye, rightEye;
    private ZombieType kind;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearEyes()
    {
        eyeMaterial = trailMaterial = null;
        eyeQuad = null;
        eyeGlows.Clear();
    }

    // From Dress, with the body in its dressed pose: where each eye sits on the head bone.
    private void MeasureEyes()
    {
        var head = Bone(HumanBodyBones.Head);
        if (head == null) return;
        if (runner == null) runner = GetComponent<ZombieRunner>();
        var leftBone = Bone(HumanBodyBones.LeftEye);
        var rightBone = Bone(HumanBodyBones.RightEye);
        Vector3 up = Model.transform.up, forward = Model.transform.forward, right = Model.transform.right;
        Vector3 left, rightPoint;
        if (leftBone != null && rightBone != null)
        {
            left = leftBone.position + forward * .015f;
            rightPoint = rightBone.position + forward * .015f;
        }
        else
        {
            float ahead = runner != null ? runner.EyeForward : .1f, above = runner != null ? runner.EyeUp : .085f;
            float apart = (runner != null ? runner.EyeApart : .064f) * .5f;
            Vector3 middle = head.position + up * above + forward * ahead;
            left = middle - right * apart;
            rightPoint = middle + right * apart;
        }
        leftEyeLocal = head.InverseTransformPoint(left);
        rightEyeLocal = head.InverseTransformPoint(rightPoint);
        eyesMeasured = true;
    }

    /// <summary>From Update: a living runner's eyes burn; anything else has none.</summary>
    private void EyesTick()
    {
        if (kind == null) kind = GetComponent<ZombieType>();
        bool burn = eyesMeasured && Model != null && kind != null && kind.Kind == ZombieKind.Runner
            && (enemyHealth == null || enemyHealth.IsAlive);
        if (burn && leftEye == null) MakeEyes();
        if (leftEye != null && leftEye.activeSelf != burn) { leftEye.SetActive(burn); rightEye.SetActive(burn); }
    }

    private void MakeEyes()
    {
        var head = Bone(HumanBodyBones.Head);
        if (runner == null) runner = GetComponent<ZombieRunner>();
        if (head == null || !Materials()) return;
        leftEye = Eye("Runner Eye (L)", head, leftEyeLocal);
        rightEye = Eye("Runner Eye (R)", head, rightEyeLocal);
    }

    private GameObject Eye(string label, Transform head, Vector3 local)
    {
        var eye = new GameObject(label);
        eye.layer = gameObject.layer;
        eye.transform.SetParent(head, false);
        eye.transform.localPosition = local;
        eye.AddComponent<MeshFilter>().sharedMesh = eyeQuad;
        var glow = eye.AddComponent<MeshRenderer>();
        glow.sharedMaterial = eyeMaterial;
        glow.shadowCastingMode = ShadowCastingMode.Off;
        glow.receiveShadows = false;
        glow.lightProbeUsage = LightProbeUsage.Off;
        glow.reflectionProbeUsage = ReflectionProbeUsage.Off;
        eyeGlows.Add(glow);
        var trail = eye.AddComponent<TrailRenderer>();
        trail.sharedMaterial = trailMaterial;
        trail.time = runner != null ? runner.EyeTrailSeconds : .2f;
        trail.widthMultiplier = runner != null ? runner.EyeTrailWidth : .045f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.35f, .4f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = fade;
        trail.minVertexDistance = .03f;
        trail.alignment = LineAlignment.View;
        trail.numCapVertices = 2;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.generateLightingData = false;
        return eye;
    }

    // One glow material and one trail material, shared by every runner; made the first time a runner shows its eyes.
    private bool Materials()
    {
        if (eyeMaterial != null && trailMaterial != null && eyeQuad != null) return true;
        var shader = Resources.Load<Shader>(EyeShaderPath);
        if (shader == null) { Debug.LogWarning("[ZombieBody] Resources/" + EyeShaderPath + " is missing: runners have no glowing eyes."); return false; }
        float brightness = runner != null ? runner.EyeBrightness : 6f;
        var color = new Color(1f, .05f, .02f, 1f) * brightness;
        color.a = 1f;
        eyeMaterial = new Material(shader) { name = "Runner Eyes", hideFlags = HideFlags.DontSave };
        eyeMaterial.SetColor("_Color", color);
        eyeMaterial.SetFloat("_Billboard", 1f);
        eyeMaterial.SetFloat("_Size", runner != null ? runner.EyeSize : .16f);
        trailMaterial = new Material(shader) { name = "Runner Eye Trails", hideFlags = HideFlags.DontSave };
        trailMaterial.SetColor("_Color", color * .6f);
        trailMaterial.SetFloat("_Billboard", 0f);
        eyeQuad = new Mesh { name = "Runner Eye", hideFlags = HideFlags.DontSave };
        eyeQuad.vertices = new[] { new Vector3(-.5f, -.5f, 0f), new Vector3(.5f, -.5f, 0f), new Vector3(.5f, .5f, 0f), new Vector3(-.5f, .5f, 0f) };
        eyeQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        eyeQuad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        eyeQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        // The quad grows to its size on screen in the shader: room enough here that it isn't culled at the edge.
        eyeQuad.bounds = new Bounds(Vector3.zero, Vector3.one * .5f);
        return true;
    }

    // Dying, before the body becomes the corpse: the eyes go out.
    private void PutOutEyes()
    {
        foreach (var eye in new[] { leftEye, rightEye })
        {
            if (eye == null) continue;
            if (eye.TryGetComponent(out MeshRenderer glow)) eyeGlows.Remove(glow);
            Destroy(eye);
        }
        leftEye = rightEye = null;
    }
}
