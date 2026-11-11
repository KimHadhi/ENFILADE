using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The hub's corner of the HUD (memo 46): the chapter the party will set off to, as the title screen's map card shows it
/// (its picture, the big pink number, the name), and, once everyone is ready, the count to setting off, big. Drawn
/// through the HUD camera like the rest of the HUD, at the top right, leaning its own way (the HUD's Persona tilt).
/// Who is ready is the objective line's (HubLobby) and the board's over the way out.
/// </summary>
[DisallowMultipleComponent]
public sealed class HubPanel : MonoBehaviour
{
    // Under the money and the feed's first rows at the top right.
    private const float Width = 420f, Height = 168f, Edge = 48f, Top = 190f, Tilt = -2.5f;

    private RectTransform root;
    private RawImage picture;
    private Image flash;
    private TMP_Text number, title, count, hint;
    private string shownChapter;
    private int shownCount = int.MinValue;

    public static HubPanel Make()
    {
        var host = new GameObject("Hub Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        FeelUI.SetupHud(host, 30);
        var panel = host.AddComponent<HubPanel>();
        panel.Build();
        return panel;
    }

    private void Build()
    {
        root = KitStyle.TopLeft("Card", transform, Vector2.zero, new Vector2(Width, Height));
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 1f);
        root.anchoredPosition = new Vector2(-Edge, -Top);
        root.localRotation = Quaternion.Euler(0f, 0f, Tilt);
        KitStyle.Cut(KitStyle.Stretch("Back", root), KitStyle.Panel);
        KitStyle.Bar("Rail", root, Vector2.zero, new Vector2(Width, KitStyle.AccentWidth), KitStyle.Accent);
        var frame = KitStyle.TopLeft("Picture", root, new Vector2(12f, 16f), new Vector2(150f, 100f));
        picture = frame.gameObject.AddComponent<RawImage>();
        picture.raycastTarget = false;
        KitStyle.Flat(KitStyle.TopLeft("Caption", root, new Vector2(176f, 14f), new Vector2(230f, 22f)), Texts.Get("hub.panel.next"), 15f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
        number = KitStyle.Flat(KitStyle.TopLeft("Number", root, new Vector2(176f, 34f), new Vector2(230f, 52f)), "", 46f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
        title = KitStyle.Flat(KitStyle.TopLeft("Title", root, new Vector2(176f, 86f), new Vector2(232f, 30f)), "", 22f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Text);
        hint = KitStyle.Flat(KitStyle.TopLeft("Hint", root, new Vector2(12f, 128f), new Vector2(Width - 24f, 28f)), "", 15f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        hint.fontStyle = FontStyles.Normal;
        count = KitStyle.Flat(KitStyle.TopLeft("Count", root, new Vector2(Width - 120f, 24f), new Vector2(110f, 96f)), "", 84f,
            TextAlignmentOptions.MidlineRight, KitStyle.Ice);
        flash = KitStyle.Fill(KitStyle.Stretch("Flash", root), Color.clear);
    }

    /// <summary>This frame's chapter and count (negative: not counting).</summary>
    public void Show(string chapter, float countdown)
    {
        if (root == null) return;
        if (chapter != shownChapter)
        {
            shownChapter = chapter;
            HubDisplays.SplitChapter(MapTable.DisplayName(chapter), out string chapterNumber, out string name);
            number.text = chapterNumber;
            title.text = name;
            picture.texture = MapTable.Picture(chapter);
            picture.color = picture.texture != null ? Color.white : KitStyle.With(KitStyle.Night, .9f);
            KitPulse.Play(flash, KitStyle.Flash, .35f);
        }
        int now = countdown >= 0f ? Mathf.CeilToInt(countdown) : -1;
        if (now == shownCount) return;
        shownCount = now;
        count.text = now >= 0 ? now.ToString() : "";
        title.gameObject.SetActive(now < 0);
        hint.text = now >= 0 ? Texts.Get("hub.panel.going") : Texts.Get(GameSession.IsOnline ? "hub.panel.hint.room" : "hub.panel.hint.solo");
        if (now >= 0) count.rectTransform.localScale = Vector3.one * 1.25f;
    }

    private void LateUpdate()
    {
        // The count's number lands big and settles.
        if (count != null && count.rectTransform.localScale.x > 1f)
            count.rectTransform.localScale = Vector3.one * Mathf.MoveTowards(count.rectTransform.localScale.x, 1f, Time.unscaledDeltaTime * 2.5f);
    }
}
