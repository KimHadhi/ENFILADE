using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The character the wardrobe's pages (MainMenuWardrobe) dress, and the view on it. The pages and what a pick does are the
/// same everywhere; this is what differs between the two places they open:
/// - the title screen's 캐릭터 tab: a stand-in of the chosen character in front of a camera of its own (MainMenuPreview),
///   which a choice under the pointer is worn on for a look before it is picked;
/// - the hub's mirror (HubDressing; user, 2026-11-12: "로비 무대 엔필레이드 쪽에 거울 넣어서 자기 볼 수 있게 만들고 여기서 자기
///   캐릭터 커스터마이징"): the player's own body, seen in the stage's mirror. A pick is worn on it at once, as on the title
///   screen, and the room sees it.
/// </summary>
public interface IWardrobeStage
{
    /// <summary>
    /// A choice under the pointer can be worn for a look before it is picked (Try). The hub's body is the player's own,
    /// made again whole for every outfit (its animation graph, hands, cloth), so there a click wears the choice instead.
    /// </summary>
    bool CanTry { get; }
    /// <summary>The small line under the page's title: what a pick does and how the character turns.</summary>
    string WhereLine { get; }
    /// <summary>The view comes close on a part of the body (an OutfitSlot group, or the body page's face, eyes, hair);
    /// null for the whole character.</summary>
    void Focus(string group);
    /// <summary>Marks on the body what the slot holds (the tile under the pointer); null takes the marks off.</summary>
    void Highlight(string slot);
    /// <summary>Wears <paramref name="look"/> instead of the saved outfit while a choice is under the pointer; null goes
    /// back to the saved one. Only asked of a stage that <see cref="CanTry"/>.</summary>
    void Try(OutfitLook look, string slot, bool removes);
    /// <summary>A pick is about to be saved: the try is let go without a rebuild of its own.</summary>
    void DropTry();
    /// <summary>Moves one body option on the character at once, while a slider is dragged or a chip picked.</summary>
    void Shape(BodyShapes.Option option, float value);
    /// <summary>The wardrobe saved the look the character already shows (its body options set live by Shape).</summary>
    void Kept(OutfitLook look);
    /// <summary>Turns the character by a drag of <paramref name="pixels"/> (1080p) across the screen; right turns its front
    /// to the right as seen.</summary>
    void Turn(float pixels, float deltaTime);
    /// <summary>The drag ended.</summary>
    void Release();
}

/// <summary>Where a part of a character is for a view to frame it: the wardrobe's close-ups on either stage.</summary>
internal static class WardrobeFraming
{
    // The body page's close-ups, as shares of the character's height: how far over the head bone the face's middle is
    // and how much of the screen the face fills; the same for the eyes when the skeleton has no eye bones.
    private const float FaceLift = .06f, FaceSpan = .2f, EyesLift = .07f, EyesSpan = .11f;

    /// <summary>
    /// The middle of a part of the body (an OutfitSlot group: head, neck, torso, arms, legs; or the body page's face, eyes,
    /// hair; null for the whole character) and how much of the character's height a view of it shows. Measured from the
    /// skeleton as it stands, each bone's place put through <paramref name="at"/> (the stages measure as if the character
    /// faced the view, so a turned head or chest a little off the middle doesn't swing the view round with every turn).
    /// </summary>
    public static void Part(Animator animator, Bounds whole, string group, Func<Transform, Vector3> at, out Vector3 center, out float span)
    {
        float height = Mathf.Max(.1f, whole.size.y);
        Transform Bone(HumanBodyBones bone) => animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;
        var head = Bone(HumanBodyBones.Head);
        var neck = Bone(HumanBodyBones.Neck);
        var chest = Bone(HumanBodyBones.Chest);
        var hips = Bone(HumanBodyBones.Hips);
        var leftEye = Bone(HumanBodyBones.LeftEye);
        var rightEye = Bone(HumanBodyBones.RightEye);
        center = whole.center;
        span = height;
        switch (group)
        {
            case "head" when head != null:
                center = at(head) + Vector3.up * height * .05f;
                span = height * .3f;
                break;
            // The body page's face and eye tabs: their keys are small (a pupil's heart, a lash), so the view comes in
            // closer than for a hat.
            case "face" when head != null:
                center = at(head) + Vector3.up * height * FaceLift;
                span = height * FaceSpan;
                break;
            case "eyes" when leftEye != null && rightEye != null:
                center = (at(leftEye) + at(rightEye)) * .5f;
                span = height * EyesSpan;
                break;
            case "eyes" when head != null:
                center = at(head) + Vector3.up * height * EyesLift;
                span = height * EyesSpan;
                break;
            // Hair falls past the shoulders (twintails, long sides): the head and chest together.
            case "hair" when head != null && chest != null:
                center = Vector3.Lerp(at(chest), at(head), .6f) + Vector3.up * height * .03f;
                span = height * .5f;
                break;
            case "neck" when neck != null:
                center = at(neck);
                span = height * .32f;
                break;
            case "torso" when chest != null:
                center = at(chest);
                span = height * .45f;
                break;
            case "arms" when chest != null && hips != null:
                center = (at(chest) + at(hips)) * .5f;
                span = height * .55f;
                break;
            case "legs" when hips != null:
                var middle = at(hips);
                center = new Vector3(middle.x, (middle.y + whole.min.y) * .5f, middle.z);
                span = height * .6f;
                break;
        }
    }
}

/// <summary>Where a slot's pieces are on a dressed character, for the wardrobe's marks (OutfitGhost) on either stage.</summary>
internal static class WardrobePieces
{
    /// <summary>
    /// A slot's pieces as <paramref name="look"/> has them on <paramref name="model"/>: the item worn there (what dressing
    /// put on, <paramref name="dressing"/>), or the character's own pieces (CharacterParts) that show; nothing once the
    /// slot is taken off.
    /// </summary>
    public static List<Renderer> Of(GameObject model, string skinId, OutfitDresser.Dressing dressing, string slot, OutfitLook look)
    {
        var found = new List<Renderer>();
        if (model == null || string.IsNullOrEmpty(skinId) || slot == null || look == null) return found;
        bool picked = look.TryGet(slot, out var pick) && pick.Item.Length > 0;
        if (picked && !pick.IsOff && dressing != null && dressing.Pieces.TryGetValue(pick.Item, out var worn)) found.AddRange(worn);
        else if (!picked)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in OutfitCatalog.PartsOf(skinId))
                if (string.Equals(part.slot, slot, StringComparison.OrdinalIgnoreCase)) names.UnionWith(OutfitCatalog.Split(part.parts));
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                if (renderer.enabled && names.Contains(renderer.name)) found.Add(renderer);
        }
        return found;
    }
}
