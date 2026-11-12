using System;
using UnityEngine;

/// <summary>
/// One equipment slot on the body, a row of OutfitSlots.csv: hair, helmet, mask, earrings, choker, top, gloves, shoes and
/// so on. A slot holds one item at a time (<see cref="OutfitItem"/>). Left empty it shows the character's own piece there
/// (CharacterParts.csv), so a character looks as its maker dressed it until something is put on: the user asked for
/// exactly that ("빈칸이면 기본 캐릭터 세팅으로", 2026-11-11), and for slots by body part, the finer the better, so a
/// welding mask, a choker and earrings each have their own.
/// </summary>
public sealed class OutfitSlot
{
    public const string TableName = "OutfitSlots";

    /// <summary>How an item in the slot shows in first person, where the camera sits inside the head.</summary>
    public enum View
    {
        /// <summary>Always seen, as the body is: sleeves, gloves, shoes.</summary>
        Show,
        /// <summary>
        /// Seen with the head and hair, when the settings show the player's own head; the near clip plane then cuts what
        /// sits round the camera (PlayerBody.ApplyHeadVisibility).
        /// </summary>
        Head,
        /// <summary>Only its shadow: a mask or glasses sit right in front of the eyes and would cover the view.</summary>
        Hide,
    }

    public string id = "";
    /// <summary>The part of the body (head, neck, torso, arms, legs); the character screen groups slots by it.</summary>
    public string group = "";
    /// <summary>
    /// The humanoid bone (a HumanBodyBones name) an item follows when it brings no bones of its own: a helmet or a welding
    /// mask made as one rigid mesh.
    /// </summary>
    public string bone = "";
    /// <summary>show, head or hide (<see cref="View"/>).</summary>
    public string firstPerson = "show";
    /// <summary>Whether the character's own piece here can be taken off with nothing in its place. Hair can't.</summary>
    public bool removable = true;
    /// <summary>
    /// Worn on the skin, under what the other slots wear: the underwear slots (속옷 상의, 속옷 하의). What is in them is
    /// always on, the character's own underwear unless an item is picked, and the rest of the outfit decides only how
    /// much of it shows (UnderwearFit). Underwear used to hang off the top and bottom (CharacterParts.csv under): it came
    /// on once they were off and some tops went only with the set that swapped it (the user, 2026-11-12: "커스터마이징은
    /// 자유도에서 오는 건데 … 따로 칸을 만들어야겠다").
    /// </summary>
    public bool under;
    /// <summary>
    /// How a rigid piece made for anyone (OutfitLibrary.AnySkin) is put on in this slot: empty hangs it on the spots
    /// measured for each character (OutfitSpots.csv: the Ene earrings on the lobes), "face" fits it to the face measured
    /// from the character's own meshes as it is worn (FaceFit: the welding mask, 2026-11-13), so it sits on any humanoid
    /// with nothing set up per character.
    /// </summary>
    public string fit = "";

    /// <summary>Whether rigid pieces made for anyone are fitted to the face here (<see cref="fit"/> "face").</summary>
    public bool FitsFace => string.Equals(fit, "face", StringComparison.OrdinalIgnoreCase);

    public string Name => Texts.Get("outfit.slot." + id);

    public View FirstPerson => string.Equals(firstPerson, "hide", StringComparison.OrdinalIgnoreCase) ? View.Hide
        : string.Equals(firstPerson, "head", StringComparison.OrdinalIgnoreCase) ? View.Head : View.Show;

    /// <summary>The bone an item without bones of its own hangs from; null when the model has no such bone.</summary>
    public Transform Anchor(Animator animator)
    {
        if (animator == null || !animator.isHuman || !Enum.TryParse(bone, true, out HumanBodyBones human)
            || human == HumanBodyBones.LastBone) return null;
        return animator.GetBoneTransform(human);
    }
}
