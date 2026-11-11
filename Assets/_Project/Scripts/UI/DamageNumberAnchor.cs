using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where one target's damage numbers stand on screen: pinned to the body part the newest hit landed on, a little off it
/// toward the side away from the crosshair, so the number rides the enemy (a charging runner, a crawling legless one, a
/// jolt) the way Apex Legends' stacked numbers do. The user asked for that ("에이펙스 레전드처럼 데미지 팝업이 적을
/// 따라가게", 2026-11-11); before, the numbers stood beside the body's collider outline, which the physics step only
/// moved now and then, so they slid and lagged behind a moving zombie. A new hit elsewhere on the body glides the pin
/// there. The side flips only once the hit is clearly over on the other half of the screen, or when the numbers would
/// leave it. After a finishing blow, or once the target is gone, the number holds the spot where it fell instead of
/// riding a flung corpse.
/// </summary>
public sealed class DamageNumberAnchor
{
    private const float ScreenEdge = 16f, FlipBand = 24f, FarHit = 3f;
    // The parts a hit can pin to; the nearest to the hit wins.
    private static readonly HumanBodyBones[] Parts =
    {
        HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine,
        HumanBodyBones.Hips, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot,
        HumanBodyBones.RightFoot,
    };

    private Transform follow;
    private readonly List<Transform> bones = new List<Transform>();
    private readonly List<Collider> colliders = new List<Collider>();
    // The part the newest hit pinned to and the hit in its space; the one before, while gliding away from it.
    private Transform pin, lastPin;
    private Vector3 local, lastLocal, held;
    private float glideStart = float.NegativeInfinity;
    private bool frozen, pinned;

    /// <summary>+1 while the numbers stand right of the hit, -1 left of it, 0 before the first placement.</summary>
    public int Side { get; private set; }

    public void Begin(Component target)
    {
        follow = target.transform;
        bones.Clear();
        var animator = target.GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
            foreach (var part in Parts)
            {
                var bone = animator.GetBoneTransform(part);
                if (bone != null) bones.Add(bone);
            }
        target.GetComponentsInChildren(colliders);
        frozen = pinned = false;
        pin = lastPin = null;
        held = follow.position;
        glideStart = float.NegativeInfinity;
        Side = 0;
    }

    public void Release()
    {
        follow = pin = lastPin = null;
        bones.Clear();
        colliders.Clear();
    }

    /// <summary>
    /// A hit landed at <paramref name="point"/> (world): the numbers pin to the body part there, gliding over from where
    /// they stood. A hit with no place on the body (a fall, a blast centred far off) pins at the body's middle.
    /// </summary>
    public void Hit(Vector3 point, DamageNumberLayout layout)
    {
        // A number left after a finishing blow stays put; a target that came back thawed it before this.
        if (follow == null || frozen) return;
        if (point == Vector3.zero || (point - follow.position).sqrMagnitude > FarHit * FarHit) point = Middle(layout);
        var part = Nearest(point);
        if (pinned && !frozen)
        {
            lastPin = pin;
            lastLocal = local;
            glideStart = Time.unscaledTime;
        }
        pin = part;
        local = part.InverseTransformPoint(point);
        held = point;
        pinned = true;
    }

    /// <summary>Stops following: the numbers stay where they stand now.</summary>
    public void Freeze()
    {
        if (frozen) return;
        held = Spot(null);
        frozen = true;
    }

    /// <summary>Follows the target again if it still exists (a practice target that came back).</summary>
    public void Thaw()
    {
        if (follow != null && pin != null) frozen = false;
    }

    /// <summary>
    /// The spot a number's near edge stands on (canvas px from the centre, which is the crosshair) and its distance
    /// scale (1 near, smaller far off). room is the widest number's width, to keep it on screen. False while the hit is
    /// behind the camera.
    /// </summary>
    public bool Place(Camera view, RectTransform canvas, DamageNumberLayout layout, CombatFeedbackSettings settings, float room,
        out Vector2 spot, out float far)
    {
        spot = default;
        far = 1f;
        if (view == null) return false;
        if (!frozen && (follow == null || pin == null)) Freeze(); // destroyed by the blow: finish where it stood
        if (!pinned && !frozen) Hit(Middle(layout), layout);
        Vector3 world = frozen ? held : Spot(layout);
        if (!ToCanvas(view, canvas, world, out Vector2 at)) return false;
        float distance = Vector3.Distance(view.transform.position, world);
        far = Mathf.Clamp(settings.popupFullSizeDistance / Mathf.Max(.01f, distance), settings.popupMinimumDistanceScale, 1f);

        // Away from the crosshair, keeping the current side until the hit is clearly over on the other half.
        if (Side == 0) Side = at.x < -FlipBand ? -1 : 1;
        else if (Side > 0 && at.x < -FlipBand) Side = -1;
        else if (Side < 0 && at.x > FlipBand) Side = 1;
        float x = at.x + Side * layout.offset.x * far;
        // A side that would push the numbers off the screen gives way to the other one when that one fits.
        float half = canvas.rect.width * .5f, halfHeight = canvas.rect.height * .5f;
        bool rightFits = at.x + layout.offset.x * far + room <= half - ScreenEdge;
        bool leftFits = at.x - layout.offset.x * far - room >= ScreenEdge - half;
        if (Side > 0 && !rightFits && leftFits) Side = -1;
        else if (Side < 0 && !leftFits && rightFits) Side = 1;
        x = at.x + Side * layout.offset.x * far;
        x = Side > 0 ? Mathf.Min(x, half - ScreenEdge - room) : Mathf.Max(x, ScreenEdge - half + room);
        float y = Mathf.Clamp(at.y + layout.offset.y * far, ScreenEdge - halfHeight, halfHeight - ScreenEdge);
        spot = new Vector2(x, y);
        return true;
    }

    // Where the pin is now, gliding from the last one for a moment after a hit somewhere else; once the part is gone,
    // the last place it was seen (kept in held as it goes).
    private Vector3 Spot(DamageNumberLayout layout)
    {
        if (!pinned || pin == null) return held;
        Vector3 now = pin.TransformPoint(local);
        float glide = layout != null ? layout.glide : 0f;
        float t = glide > 0f ? (Time.unscaledTime - glideStart) / glide : 1f;
        if (t < 1f && lastPin != null)
        {
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            now = Vector3.Lerp(lastPin.TransformPoint(lastLocal), now, eased);
        }
        held = now;
        return now;
    }

    private Transform Nearest(Vector3 point)
    {
        Transform best = follow;
        float closest = float.PositiveInfinity;
        foreach (var bone in bones)
        {
            if (bone == null) continue;
            float distance = (bone.position - point).sqrMagnitude;
            if (distance >= closest) continue;
            closest = distance;
            best = bone;
        }
        return best;
    }

    // The body's middle at the layout's height, from its colliders; the target's own place when it has none.
    private Vector3 Middle(DamageNumberLayout layout)
    {
        bool found = false;
        Bounds bounds = default;
        foreach (var collider in colliders)
        {
            if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
            if (!found) { bounds = collider.bounds; found = true; }
            else bounds.Encapsulate(collider.bounds);
        }
        if (!found) return follow != null ? follow.position + Vector3.up * (1.8f * layout.height) : held;
        return new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * layout.height, bounds.center.z);
    }

    private static bool ToCanvas(Camera view, RectTransform canvas, Vector3 world, out Vector2 local)
    {
        local = default;
        Vector3 screen = view.WorldToScreenPoint(world);
        return screen.z > view.nearClipPlane && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out local);
    }
}
