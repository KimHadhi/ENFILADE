using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The HUD's portrait: the player's own profile picture (ProfilePicture, chosen in the main menu's profile) in the square
/// tile SquarePortrait.shader draws on this RawImage. Two slabs lean behind it at their own angles (pink top left, a
/// black shadow bottom right), a black mat holds the picture, whose corners are cut like the menus' cards with the pink
/// bar along its foot as on the lobby's profile chip, and corner brackets on the mat glow as the ultimate charges
/// (UltimatePortraitGlow drives the material). With no picture chosen it shows the name's first letter on pink, as every
/// other profile square does. Replaced the hexagon with the drawn character on 2026-11-09 (the user: "design it your way").
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RawImage))]
public sealed class HudPortrait : MonoBehaviour
{
    [Tooltip("Size of the first letter shown when no picture is chosen (px at 1080p).")]
    [SerializeField, Min(1f)] private float letterSize = 76f;

    private RawImage picture;
    private TMP_Text letter;

    private void Awake() => picture = GetComponent<RawImage>();

    private void OnEnable()
    {
        ProfilePicture.Changed += Paint;
        Paint();
    }

    private void OnDisable() => ProfilePicture.Changed -= Paint;

    private void Paint()
    {
        if (picture == null) return;
        if (letter == null && ProfilePicture.Current == null) letter = MakeLetter();
        ProfilePicture.Paint(picture, letter, GameSettings.Current.DisplayName);
    }

    // Made only when it is needed: a player with a picture never has one.
    private TMP_Text MakeLetter()
    {
        var box = KitStyle.Stretch("Initial", transform);
        var label = KitStyle.Flat(box, "", letterSize, TextAlignmentOptions.Center, KitStyle.Ink);
        box.gameObject.layer = label.gameObject.layer = gameObject.layer;
        return label;
    }
}
