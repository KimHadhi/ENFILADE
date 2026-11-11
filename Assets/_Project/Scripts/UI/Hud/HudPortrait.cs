using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The HUD's portrait: the player's own profile picture (ProfilePicture, chosen in the main menu's profile) in the square
/// tile SquarePortrait.shader draws on this RawImage. Two slabs lean behind it at their own angles (pink top left, a
/// black shadow bottom right), a black mat holds the picture, whose corners are cut like the menus' cards with the pink
/// bar along its foot as on the lobby's profile chip, and faint corner brackets on the mat. With no picture chosen it shows
/// the name's first letter on pink, as every other profile square does. Replaced the hexagon with the drawn character on
/// 2026-11-09 (the user: "design it your way"). The ultimate is the magazine on its left edge (UltimateMagazine, round 4,
/// 2026-11-11), which this makes and fits to the picture; the brackets no longer glow with it, as the old glow lit the
/// picture for no reason.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RawImage))]
public sealed class HudPortrait : MonoBehaviour
{
    [Tooltip("Size of the first letter shown when no picture is chosen (px at 1080p).")]
    [SerializeField, Min(1f)] private float letterSize = 76f;

    private RawImage picture;
    private TMP_Text letter;

    private void Awake()
    {
        picture = GetComponent<RawImage>();
        MakeMagazine();
    }

    // The magazine stands against the black mat's left edge, as tall as the mat: the picture's size comes from the
    // tile's own material (SquarePortrait: _Half and _MatWidth are shares of the quad's side).
    private void MakeMagazine()
    {
        var tile = (RectTransform)transform;
        var material = picture.material;
        float side = Mathf.Min(tile.rect.width, tile.rect.height);
        float half = side * (material != null && material.HasProperty("_Half") ? material.GetFloat("_Half") : .27f);
        float mat = side * (material != null && material.HasProperty("_MatWidth") ? material.GetFloat("_MatWidth") : .024f);
        var rect = new GameObject("UltMagazine", typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(tile, false);
        rect.gameObject.layer = gameObject.layer;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(1f, .5f);
        var magazine = rect.gameObject.AddComponent<UltimateMagazine>();
        GameTables.Bind(UltimateMagazine.TableName, UltimateMagazine.Row, magazine);
        rect.sizeDelta = new Vector2(magazine.Width, 2f * (half + mat));
        rect.anchoredPosition = new Vector2(-(half + mat + magazine.Gap), 0f);
    }

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
