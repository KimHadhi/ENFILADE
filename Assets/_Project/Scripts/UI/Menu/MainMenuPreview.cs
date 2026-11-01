using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>The chosen character standing beside the title menu in its idle pose, with the camera and light it needs.</summary>
[DisallowMultipleComponent]
public sealed class MainMenuPreview : MonoBehaviour
{
    private const string IdleClip = "mx_idle";
    private const float FieldOfView = 26f, ScreenFill = .86f, SideShift = .42f;
    private static readonly Color Background = new Color(.055f, .06f, .075f);
    private Camera view;
    private GameObject model;
    private PlayableGraph graph;

    private void Awake()
    {
        view = new GameObject("Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        view.transform.SetParent(transform, false);
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = Background;
        view.fieldOfView = FieldOfView;
        view.nearClipPlane = .05f;
        var sun = new GameObject("Light", typeof(Light)).GetComponent<Light>();
        sun.transform.SetParent(transform, false);
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(30f, 200f, 0f);
        sun.intensity = 1.2f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.5f, .5f, .55f);
    }

    /// <summary>Stands the current skin in front of the camera, replacing the last one.</summary>
    public void Show()
    {
        Clear();
        var profile = PlayerBodyProfile.Load();
        if (profile == null) return;
        model = Instantiate(profile.Model, transform);
        var bounds = new Bounds(model.transform.position, Vector3.zero);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            bounds.Encapsulate(renderer.bounds);
            if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        }
        Frame(bounds);
        var animator = model.GetComponentInChildren<Animator>();
        var idle = profile.Find(IdleClip);
        if (animator == null || idle == null) return;
        animator.applyRootMotion = false;
        AnimationPlayableUtilities.PlayClip(animator, idle, out graph);
    }

    // The character faces +Z. Moving the camera toward +X puts it right of the menu.
    private void Frame(Bounds bounds)
    {
        float height = Mathf.Max(.1f, bounds.size.y);
        float distance = height * .5f / Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad) / ScreenFill;
        view.transform.position = bounds.center + Vector3.forward * distance + Vector3.right * (height * SideShift);
        view.transform.rotation = Quaternion.LookRotation(Vector3.back);
    }

    private void Clear()
    {
        if (graph.IsValid()) graph.Destroy();
        if (model != null) Destroy(model);
    }

    private void OnDestroy() => Clear();
}
