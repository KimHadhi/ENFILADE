using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// The chosen character standing in the title menu in its idle pose, with the camera and light it needs. Follows the
/// saved skin and colour set: another colour set swaps materials on the same model. Dragging across the screen turns it
/// (MainMenuDrag → Turn); let go and it keeps spinning a little, slows, and after a while eases back to face the camera.
/// The menu's backdrop reads the camera and where the character stands (View, Feet, Head) to draw behind it.
/// The wardrobe tries things on it (Try: the choice under the pointer worn see-through, OutfitGhost) and marks what a
/// slot holds (Highlight); the idle carries on through each rebuild so a try never restarts the pose.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuPreview : MonoBehaviour
{
    public const string IdleClip = "mx_idle";
    // Nearly in the middle, between the menu's left column and its right-hand briefing, against the night.
    private const float FieldOfView = 26f, ScreenFill = .8f, SideShift = .06f;
    // Turning: degrees per dragged pixel, how fast a flick's spin dies (per second), how long it rests before facing
    // the camera again (s) and how fast it then turns back.
    private const float DegreesPerPixel = .45f, SpinDecay = 4.5f, RestBeforeReturn = 3.5f, ReturnSpeed = 2.2f, MaxSpin = 900f;
    private Camera view;
    private GameObject model;
    private PlayerBodyProfile.Skin shownSkin;
    private SkinVariant wornVariant;
    private string shownOutfit;
    // The part of the body the wardrobe looks at (OutfitSlot.group), null for the whole character, and where the camera
    // is heading for it.
    private string focus;
    private Vector3 cameraGoal;
    private bool cameraMoving;
    // How quickly the camera moves to a part of the body and back (1/s).
    private const float FocusSpeed = 7f;
    private PlayableGraph graph;
    private AnimationClipPlayable idlePlayable;
    // A look tried on from the wardrobe instead of the saved one (null: the saved one), the item in it shown see-through,
    // the slot whose pieces are marked, and what dressing put on, to find them by.
    private OutfitLook tried;
    private string triedText, ghostItem, highlighted;
    private OutfitDresser.Dressing dressing;
    private Bounds shown;
    private float yaw, spin, sinceTurned = float.PositiveInfinity;

    /// <summary>The camera the character is seen through.</summary>
    public Camera View => view;
    /// <summary>Whether a character is standing.</summary>
    public bool HasModel => model != null;
    /// <summary>The middle of the ground under the character, and the top of its head (world).</summary>
    public Vector3 Feet => new Vector3(shown.center.x, shown.min.y, shown.center.z);
    public Vector3 Head => new Vector3(shown.center.x, shown.max.y, shown.center.z);
    /// <summary>Which way it faces, 0 to 360 degrees (0 is toward the camera).</summary>
    public float Facing => Mathf.Repeat(yaw, 360f);
    /// <summary>How far the camera is from the character (m).</summary>
    public float Distance => view != null ? Vector3.Distance(view.transform.position, shown.center) : 0f;

    private void Awake()
    {
        view = new GameObject("Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        view.transform.SetParent(transform, false);
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = KitStyle.Night;
        view.fieldOfView = FieldOfView;
        view.nearClipPlane = .05f;
        var sun = new GameObject("Light", typeof(Light)).GetComponent<Light>();
        sun.transform.SetParent(transform, false);
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(30f, 200f, 0f);
        sun.intensity = 1.2f;
        // Edge lights from behind (the character faces +Z, the camera): a white one high on the screen's right and a pink
        // one low on its left, in the menu's two colours.
        Rim("Ice Rim", Quaternion.Euler(15f, 35f, 0f), KitStyle.Ice, 1.1f);
        Rim("Pink Rim", Quaternion.Euler(-10f, -40f, 0f), KitStyle.Accent, .6f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.42f, .45f, .55f);
        GameSettings.Changed += Follow;
    }

    private void Rim(string name, Quaternion rotation, Color color, float intensity)
    {
        var rim = new GameObject(name, typeof(Light)).GetComponent<Light>();
        rim.transform.SetParent(transform, false);
        rim.type = LightType.Directional;
        rim.transform.rotation = rotation;
        rim.color = color;
        rim.intensity = intensity;
        rim.shadows = LightShadows.None;
    }

    /// <summary>Stands the current skin in front of the camera, replacing the last one.</summary>
    public void Show()
    {
        double idleTime = idlePlayable.IsValid() ? idlePlayable.GetTime() : 0d;
        Clear();
        var profile = PlayerBodyProfile.Load();
        if (profile == null) return;
        shownSkin = profile.CurrentSkin;
        model = Instantiate(profile.Model, transform);
        wornVariant = profile.CurrentVariant;
        SkinVariant.Wear(model, null, wornVariant);
        var bounds = new Bounds(model.transform.position, Vector3.zero);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            bounds.Encapsulate(renderer.bounds);
            if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        }
        // Measured before dressing, so putting on a helmet doesn't move the camera back and shrink the character.
        var outfit = tried ?? OutfitLook.Saved(shownSkin?.Id);
        dressing = OutfitDresser.Dress(model, profile.Model, shownSkin, outfit);
        // A try is never what is saved: whatever changes next stands the saved outfit again.
        shownOutfit = tried != null ? null : outfit.ToString();
        if (ghostItem != null && dressing.Pieces.TryGetValue(ghostItem, out var trying)) OutfitGhost.Wear(trying);
        Mark();
        shown = bounds;
        Frame(bounds);
        Face(); // measured facing the camera, then turned as it was
        // Made again for another outfit while the wardrobe looks at a part: the camera stays on it, no jump.
        if (focus != null)
        {
            view.transform.position = cameraGoal = FocusedCamera(focus);
            cameraMoving = false;
        }
        var animator = model.GetComponentInChildren<Animator>();
        var idle = profile.Find(IdleClip);
        if (animator == null || idle == null) return;
        animator.applyRootMotion = false;
        idlePlayable = AnimationPlayableUtilities.PlayClip(animator, idle, out graph);
        idlePlayable.SetTime(idleTime);
    }

    /// <summary>
    /// Wears <paramref name="look"/> instead of the saved outfit while the wardrobe's pointer rests on a choice, the item
    /// <paramref name="ghost"/> see-through (OutfitGhost); null goes back to the saved outfit.
    /// </summary>
    public void Try(OutfitLook look, string ghost)
    {
        string text = look != null ? look.ToString() : null;
        if (text == triedText && ghost == ghostItem) return;
        tried = look;
        triedText = text;
        ghostItem = ghost;
        if (model != null) Show();
    }

    /// <summary>The wardrobe is about to save a pick: the try is let go without a rebuild of its own, and the settings'
    /// change that follows stands the saved outfit in its own colours.</summary>
    public void DropTry()
    {
        tried = null;
        triedText = ghostItem = null;
        shownOutfit = null;
    }

    /// <summary>Marks on the body what the slot holds (the wardrobe's tile under the pointer); null takes the marks off.</summary>
    public void Highlight(string slot)
    {
        slot = string.IsNullOrEmpty(slot) ? null : slot;
        if (slot == highlighted) return;
        highlighted = slot;
        Mark();
    }

    // The slot's pieces: the item worn there, or the character's own (CharacterParts) as they show; nothing once it is off.
    private void Mark()
    {
        if (model == null || highlighted == null || shownSkin == null) { OutfitGhost.Show(null); return; }
        var found = new List<Renderer>();
        var look = tried ?? OutfitLook.Saved(shownSkin.Id);
        bool picked = look.TryGet(highlighted, out var pick) && pick.Item.Length > 0;
        if (picked && !pick.IsOff && dressing != null && dressing.Pieces.TryGetValue(pick.Item, out var worn)) found.AddRange(worn);
        else if (!picked)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in OutfitCatalog.PartsOf(shownSkin.Id))
                if (string.Equals(part.slot, highlighted, StringComparison.OrdinalIgnoreCase)) names.UnionWith(OutfitCatalog.Split(part.parts));
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                if (renderer.enabled && names.Contains(renderer.name)) found.Add(renderer);
        }
        OutfitGhost.Show(found);
    }

    /// <summary>Turns the character by a drag of <paramref name="pixels"/> across the screen (right turns it right).</summary>
    public void Turn(float pixels, float deltaTime)
    {
        float degrees = -pixels * DegreesPerPixel;
        yaw += degrees;
        // What the drag was doing at the end is the spin it keeps when let go.
        if (deltaTime > 0f) spin = Mathf.Clamp(Mathf.Lerp(spin, degrees / deltaTime, .5f), -MaxSpin, MaxSpin);
        sinceTurned = 0f;
        Face();
    }

    /// <summary>The drag ended: from here it coasts.</summary>
    public void Release() => sinceTurned = 0f;

    /// <summary>
    /// Brings the camera close on a part of the body (an OutfitSlot group: head, neck, torso, arms, legs), as the wardrobe
    /// opens one of its slots; null goes back to the whole character.
    /// </summary>
    public void Focus(string group)
    {
        focus = string.IsNullOrEmpty(group) ? null : group;
        if (model == null || view == null) return;
        cameraGoal = focus != null ? FocusedCamera(focus) : CameraFor(shown.center, shown.size.y);
        cameraMoving = true;
    }

    // Where the camera stands to frame that part of the character, measured from its bones as it stands now.
    private Vector3 FocusedCamera(string group)
    {
        float height = Mathf.Max(.1f, shown.size.y);
        var animator = model != null ? model.GetComponentInChildren<Animator>() : null;
        Transform Bone(HumanBodyBones bone) => animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;
        var head = Bone(HumanBodyBones.Head);
        var neck = Bone(HumanBodyBones.Neck);
        var chest = Bone(HumanBodyBones.Chest);
        var hips = Bone(HumanBodyBones.Hips);
        Vector3 center = shown.center;
        float span = height;
        switch (group)
        {
            case "head" when head != null:
                center = head.position + Vector3.up * height * .05f;
                span = height * .3f;
                break;
            case "neck" when neck != null:
                center = neck.position;
                span = height * .32f;
                break;
            case "torso" when chest != null:
                center = chest.position;
                span = height * .45f;
                break;
            case "arms" when chest != null && hips != null:
                center = (chest.position + hips.position) * .5f;
                span = height * .55f;
                break;
            case "legs" when hips != null:
                center = new Vector3(hips.position.x, (hips.position.y + shown.min.y) * .5f, hips.position.z);
                span = height * .6f;
                break;
        }
        return CameraFor(center, span);
    }

    private void Update()
    {
        if (model == null) return;
        float dt = Time.unscaledDeltaTime;
        if (highlighted != null) OutfitGhost.Breathe(Time.unscaledTime);
        if (cameraMoving && view != null)
        {
            view.transform.position = Vector3.Lerp(view.transform.position, cameraGoal, 1f - Mathf.Exp(-FocusSpeed * dt));
            cameraMoving = (view.transform.position - cameraGoal).sqrMagnitude > 1e-6f;
        }
        sinceTurned += dt;
        if (Mathf.Abs(spin) > .5f)
        {
            yaw += spin * dt;
            spin *= Mathf.Exp(-SpinDecay * dt);
        }
        else spin = 0f;
        if (sinceTurned > RestBeforeReturn && spin == 0f)
            yaw = Mathf.LerpAngle(yaw, 0f, 1f - Mathf.Exp(-ReturnSpeed * dt));
        Face();
    }

    private void Face()
    {
        if (model != null) model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    // The character faces +Z. Moving the camera toward +X puts it right of the menu.
    private void Frame(Bounds bounds)
    {
        view.transform.position = CameraFor(bounds.center, bounds.size.y);
        view.transform.rotation = Quaternion.LookRotation(Vector3.back);
    }

    // The camera's place to show something this tall around this point, looking straight back at it.
    private static Vector3 CameraFor(Vector3 center, float height)
    {
        height = Mathf.Max(.1f, height);
        float distance = height * .5f / Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad) / ScreenFill;
        return center + Vector3.forward * distance + Vector3.right * (height * SideShift);
    }

    // A new skin or outfit stands a new model (items join the bones in the pose the model was made in); a new colour set
    // of the same skin dresses the one standing.
    private void Follow()
    {
        var profile = PlayerBodyProfile.Load();
        if (profile == null || model == null) return;
        if (profile.CurrentSkin != shownSkin || OutfitLook.Tidy(OutfitLook.SavedText(shownSkin?.Id)) != shownOutfit) { Show(); return; }
        var next = profile.CurrentVariant;
        SkinVariant.Wear(model, wornVariant, next);
        wornVariant = next;
    }

    private void Clear()
    {
        // Marks hold the renderers' own materials: let go of them before the model goes.
        OutfitGhost.Show(null);
        dressing = null;
        if (graph.IsValid()) graph.Destroy();
        if (model != null) Destroy(model);
        model = null;
        shownSkin = null;
        wornVariant = null;
        shownOutfit = null;
    }

    private void OnDestroy()
    {
        GameSettings.Changed -= Follow;
        Clear();
    }
}
