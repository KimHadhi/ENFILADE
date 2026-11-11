using UnityEngine;

/// <summary>
/// Where a character stands for a camera to frame it (the menu's preview, its portrait cards): from the soles to the top
/// of the head, measured against its skeleton. Renderer bounds alone made characters different sizes on screen: a mesh's
/// bounds before its first pose, a hidden prop, a tall ahoge or animal ears all counted, so a BOOTH avatar's box came out
/// taller than its body and it stood small in the frame (user, 2026-11-12: "마요랑 이치고는 … 너무 키가 작아서"). With a
/// humanoid skeleton the renderers only say where the soles and the crown are, held within reach of the feet and head
/// bones; without one, the renderers are all there is.
/// </summary>
public static class CharacterFrame
{
    // Within these shares of the feet-to-head-bone height: how far under the feet bones the soles may be, how far over
    // the head bone the crown must and may be, and how wide either side of the hips the body may reach.
    private const float SoleRoom = .12f, CrownLeast = .12f, CrownMost = .3f, SideRoom = .6f;

    private static readonly HumanBodyBones[] Feet =
        { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes };

    /// <summary>The character's box as it stands now (world space).</summary>
    public static Bounds Measure(GameObject model)
    {
        var rendered = Rendered(model);
        var animator = model != null ? model.GetComponentInChildren<Animator>() : null;
        if (animator == null || !animator.isHuman) return rendered;
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (head == null || hips == null) return rendered;
        float feet = float.PositiveInfinity;
        foreach (var bone in Feet)
        {
            var found = animator.GetBoneTransform(bone);
            if (found != null) feet = Mathf.Min(feet, found.position.y);
        }
        if (float.IsInfinity(feet)) feet = rendered.size.y > 0f ? rendered.min.y : hips.position.y * .1f;
        float reach = Mathf.Max(.1f, head.position.y - feet);
        bool hasRendered = rendered.size.sqrMagnitude > 0f;
        float bottom = hasRendered ? Mathf.Clamp(rendered.min.y, feet - reach * SoleRoom, feet) : feet;
        float top = hasRendered ? Mathf.Clamp(rendered.max.y, head.position.y + reach * CrownLeast, head.position.y + reach * CrownMost)
            : head.position.y + reach * CrownLeast;
        float side = reach * SideRoom;
        Vector3 middle = hips.position;
        float halfX = hasRendered ? Mathf.Clamp(Mathf.Max(rendered.max.x - middle.x, middle.x - rendered.min.x), .1f, side) : side * .5f;
        float halfZ = hasRendered ? Mathf.Clamp(Mathf.Max(rendered.max.z - middle.z, middle.z - rendered.min.z), .1f, side) : side * .5f;
        return new Bounds(new Vector3(middle.x, (top + bottom) * .5f, middle.z), new Vector3(halfX * 2f, top - bottom, halfZ * 2f));
    }

    // Only what draws a body: skinned and plain meshes that are switched on (no particles, lines or trails).
    private static Bounds Rendered(GameObject model)
    {
        var bounds = new Bounds(model != null ? model.transform.position : Vector3.zero, Vector3.zero);
        if (model == null) return bounds;
        bool any = false;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) continue;
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }
}
