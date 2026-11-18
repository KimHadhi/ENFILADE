using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One way a piece can be worn, a row of WearPositions.csv: on the face or on the back of the head (a welding mask), on
/// the ear lobes (earrings). An item says which of them it can be worn in (Outfits.csv positions; else its slot says for
/// every piece made for anyone that goes there, OutfitSlots.csv positions), the player picks one per item in the
/// wardrobe and may then move, turn and size the piece freely on top of it (ItemAdjust), and both are part of the
/// character's look ("wear:&lt;item&gt;=&lt;position&gt;", OutfitLook), so presets, the hub's body and every other PC wear it
/// the same.
/// The user asked for this instead of an item per way of wearing (2026-11-18: "용접 마스크의 앞/뒤 착용 상태를 별도
/// 아이템으로 분리하지 않고, 착용 위치 변경 설정 기능으로 통합. 추후 다른 의상 및 창작마당 모드에서도 착용 위치·오프셋을
/// 자유롭게 커스텀할 수 있도록 확장성 있는 구조로"): the mask worn back was a second row of Outfits.csv with a wear
/// column only the code for face pieces read. A position is a row now, so a mod adds one by adding a row (a mask on the
/// side of the head, glasses pushed up, a halo) and names it in its item.
/// A position says how the piece is put on, by rules that measure the body it goes on as it is dressed, never by
/// numbers kept per character:
/// - fit "face": fitted to the head's own surface as FaceFit does it (its glass at eye height, sized to the face, its
///   lower edge clear of the chest), looking at the head from <see cref="yaw"/> degrees round it (0 the face, 180 the
///   back of the head);
/// - fit "spot": hung at a spot given from an <see cref="anchor"/> on the body (the middle of the eyes, or a humanoid
///   bone; none is the slot's own bone) along the way the body faces, in eye spacings (<see cref="at"/>: right, up,
///   ahead), so it lands in proportion on a small head and on a large one. A <see cref="pair"/> is hung twice, mirrored
///   (the model's parts named "… R" and "… L").
/// On top of either go the row's own turn and size, and then the player's.
/// </summary>
public sealed class WearPosition
{
    public const string TableName = "WearPositions";
    /// <summary>The look's entry for the position an item is worn in: "wear:&lt;item&gt;=&lt;position&gt;".</summary>
    public const string Prefix = "wear:";
    /// <summary>Where the anchor is the middle of the eye bones, not a bone.</summary>
    public const string Eyes = "eyes";
    private const string FaceFitName = "face";

    public string id = "";
    /// <summary>Its name on the wardrobe's chip: a Texts id (wear.&lt;id&gt;.name) or plain words; empty reads the id.</summary>
    public string name = "";
    /// <summary>"face" or "spot" (anything else, or empty, is a spot).</summary>
    public string fit = "";
    /// <summary>A spot's anchor: "eyes", a HumanBodyBones name, or empty for the slot's own bone.</summary>
    public string anchor = "";
    /// <summary>A face fit's way round the head, degrees: 0 the face, 180 the back of the head, 90 the right side.</summary>
    public float yaw;
    /// <summary>From the anchor along the body's right, up and ahead, in eye spacings (a pair's left piece mirrors right).</summary>
    public Vector3 at;
    /// <summary>The piece's own turn there, degrees: pitch, yaw, roll about the body's right, up and ahead (a pair's left piece mirrors yaw and roll).</summary>
    public Vector3 turn;
    /// <summary>The piece's size there, a share of its maker's (after a face fit: of the size the fit gave it).</summary>
    public float scale = 1f;
    /// <summary>Hung twice, mirrored: the model's parts named "… R" and "… L" on the right and the left (earrings).</summary>
    public bool pair;
    /// <summary>
    /// Whether the item covers there what its row says it covers (Outfits.csv hides): a mask over the face hides the
    /// glasses under it, the same mask on the back of the head hides nothing.
    /// </summary>
    public bool covers = true;

    /// <summary>
    /// How a piece with no position is worn: on its slot's own bone, where its own origin puts it, turned the way the body
    /// faces. Its item covers what it says it covers.
    /// </summary>
    public static readonly WearPosition Plain = new WearPosition();

    public string DisplayName => string.IsNullOrEmpty(name) ? id : Texts.Cell(name);
    public bool FitsFace => string.Equals(fit, FaceFitName, StringComparison.OrdinalIgnoreCase);
    public bool AtEyes => string.Equals(anchor, Eyes, StringComparison.OrdinalIgnoreCase);
    /// <summary>Whether the row moves, turns or sizes the piece itself, on top of where its fit puts it.</summary>
    public bool Shifts => at != Vector3.zero || turn != Vector3.zero || Mathf.Abs(scale - 1f) > 1e-4f;

    /// <summary>The humanoid bone a spot hangs from, when it names one; false for "eyes", none, or a name that is no bone.</summary>
    public bool TryBone(out HumanBodyBones bone)
    {
        bone = HumanBodyBones.LastBone;
        return !AtEyes && !string.IsNullOrEmpty(anchor) && Enum.TryParse(anchor, true, out bone) && bone != HumanBodyBones.LastBone
            && Enum.IsDefined(typeof(HumanBodyBones), bone);
    }

    // ---- What a look says ----

    /// <summary>
    /// The position the look wears the item in: the one it names when the item can be worn there, else the item's first;
    /// <see cref="Plain"/> for an item that has none.
    /// </summary>
    public static WearPosition Worn(OutfitLook look, OutfitItem item)
    {
        var offered = OutfitCatalog.PositionsOf(item);
        if (offered.Count == 0) return Plain;
        if (look != null && item != null && look.TryGet(Prefix + item.id, out var pick))
            foreach (var position in offered)
                if (string.Equals(position.id, pick.Item, StringComparison.OrdinalIgnoreCase)) return position;
        return offered[0];
    }

    /// <summary>Keeps in the look where the item is worn; its first position (the one worn unsaid) takes the entry out.</summary>
    public static void Wear(OutfitLook look, OutfitItem item, WearPosition position)
    {
        if (look == null || item == null) return;
        var offered = OutfitCatalog.PositionsOf(item);
        bool first = position == null || offered.Count == 0 || offered[0] == position;
        look.Set(Prefix + item.id, first ? null : position.id);
    }
}
