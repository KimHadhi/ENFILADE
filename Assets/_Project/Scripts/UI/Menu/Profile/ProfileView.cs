using TMPro;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The window for the player's own profile, laid out like a profile header: the picture large and square at the left
/// (any image from this PC, ProfilePicture; the name's first letter until one is chosen) with a pink bar under it as on
/// the lobby's chip, and at its right the nickname others see in the chat and the buttons to choose or remove the
/// picture. A picture is kept the moment it is chosen or removed; the nickname waits for 저장.
/// </summary>
public sealed class ProfileView
{
    private const int NameLimit = 16;
    private const float PictureSize = 128f, Gap = 28f;
    private readonly RectTransform shade, panel;
    private string typed;
    private RawImage picture;
    private TMP_Text letter;

    public ProfileView(Transform canvas)
    {
        panel = W.Window(canvas, "Profile", 700f, out shade);
    }

    /// <summary>Showing rebuilds the window, so a change that was cancelled doesn't stay in the box.</summary>
    public void Show()
    {
        W.ClearContent(panel);
        typed = GameSettings.Current.DisplayName;

        W.Size(W.Label(panel, Texts.Get("profile.title"), 36f, W.Text), height: 54f);
        var header = W.Rect("Header", panel);
        var row = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = Gap;
        row.childAlignment = TextAnchor.UpperLeft;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        BuildPicture(header);
        BuildDetails(header);

        var buttons = W.ButtonRow(panel);
        W.ActionButton(buttons, Texts.Get("common.cancel"), false, Hide);
        W.ActionButton(buttons, Texts.Get("profile.save"), true, Confirm);
        ShowPicture();
        shade.gameObject.SetActive(true);
    }

    // The square, never squeezed: its smallest size is its size.
    private void BuildPicture(RectTransform header)
    {
        var frame = W.Rect("Picture", header);
        var size = W.Size(frame, width: PictureSize, height: PictureSize);
        size.minWidth = size.minHeight = PictureSize;
        var image = W.Rect("Image", frame);
        KitStyle.Stretch(image);
        picture = image.gameObject.AddComponent<RawImage>();
        picture.raycastTarget = false;
        letter = W.Label(frame, "", 64f, KitStyle.Ink, TextAlignmentOptions.Center);
        KitStyle.Stretch(letter.rectTransform);
        letter.fontStyle = FontStyles.Bold;
        var bar = W.Rect("Accent", frame);
        bar.anchorMin = Vector2.zero;
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(.5f, 0f);
        bar.sizeDelta = new Vector2(0f, 5f);
        bar.anchoredPosition = Vector2.zero;
        W.Fill(bar, KitStyle.Accent).raycastTarget = false;
    }

    // At the picture's right: the nickname with its hint, then the picture's buttons.
    private void BuildDetails(RectTransform header)
    {
        var column = W.Rect("Details", header);
        var stack = column.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.spacing = 8f;
        stack.childAlignment = TextAnchor.UpperLeft;
        stack.childControlWidth = stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;
        W.Size(column, flexibleWidth: 1f);

        W.Size(W.Label(column, Texts.Get("profile.nickname"), 20f, W.Muted), height: 26f);
        W.TextInput(column, typed, name =>
        {
            typed = name;
            ShowPicture();
        }).characterLimit = NameLimit;
        W.Size(W.Label(column, Texts.Get("profile.nickname.hint"), 17f, W.Muted), height: 24f);

        var buttons = W.Rect("Picture Buttons", column);
        var row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 10f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        W.Size(buttons, height: 44f);
        W.Size(W.ActionButton(buttons, Texts.Get("profile.picture.choose"), false, () =>
        {
            ProfilePicture.Choose();
            ShowPicture();
        }), width: 150f, height: 40f);
        W.Size(W.ActionButton(buttons, Texts.Get("profile.picture.clear"), false, () =>
        {
            ProfilePicture.Clear();
            ShowPicture();
        }), width: 150f, height: 40f);
    }

    private void ShowPicture()
    {
        string name = string.IsNullOrWhiteSpace(typed) ? GameSettings.Current.DisplayName : typed;
        ProfilePicture.Paint(picture, letter, name);
    }

    public bool IsOpen => shade.gameObject.activeSelf;

    public void Close() => Hide();

    private void Hide() => shade.gameObject.SetActive(false);

    private void Confirm()
    {
        GameSettings.Current.nickname = typed.Trim();
        GameSettings.Save();
        Hide();
    }
}
