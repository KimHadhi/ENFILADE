using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The title screen's 캐릭터 page: a wardrobe in the lobby's look, the chosen character standing free in the middle
/// (MainMenuPreview; a drag across it turns it). Left wing: the characters as picture cards (SkinPortraits) and the chosen
/// one's colour chips. Right wing: the outfit slots by body part (OutfitSlots.csv) as tiles saying what is worn there.
/// A tile opens its slot: the camera comes close on that part of the body and the wing lists what can go there (the
/// character's own piece, taking it off, every item made for this character), the worn item's colour chips under it.
/// Every pick is worn at once and saved. The user asked for an equipment window with slots by body part, the finer the
/// better, and found the old list running into the bottom bar (2026-11-11); the page stays between the bars.
/// Then (2026-11-11, "의상 슬롯 기능 부위 별로 장착한 옷 뭔지 보이게, 그리고 옷 선택 옵션에서도 투명하게 옷 미리
/// 보기가 있게끔"): a tile names what is on the slot, the character's own pieces too ("기본 · 스타킹"), and the pointer on a
/// tile marks those pieces on the body with a breathing pink rim; resting on a choice tries it on (MainMenuPreview.Try,
/// OutfitGhost), until the pointer leaves or the choice is picked.
/// Then (2026-11-12): the see-through try read as something appearing, not a preview, a pretty piece looked plain that
/// way, and resting on 벗기 looked already taken off. A try is now worn as it really looks under a quick pink rim, inside
/// a pulsing pink viewfinder whose 미리 보기 plate says what a click would do; a removal keeps the piece on, fading. Every
/// plate and tile carries a see-through picture of its piece at its right end (OutfitIcons), and a slot changed from how
/// the character came is dark with a pink edge and a 바꿈 tag ("텍스트로만 돼 있어서 내가 뭘 바꿨고 뭘 입고 있는지
/// 직관적으로 눈에 안 띄어. 사진이 필요해").
/// A character whose model can be shaped (Mayo, Ichigo: BodyShapes.csv) has a 체형 plate under its colours. It opens the
/// body page in the right wing: every look the model offers, tab by tab (body, face, eyes, brows and lashes, ears, hair,
/// animal ears and tail, outfit details), as sliders and chips that shape the character at once, the camera close on
/// the face or eyes for their tabs (the user, 2026-11-12, asked for all of each character's own customization).
/// The same pages open in the hub at the stage's mirror (HubDressing; user, 2026-11-12: the Esc menu's 캐릭터 tab went
/// and the mirror took its place), where the character is the player's own body seen in the mirror. What differs
/// between the two is the stage (IWardrobeStage): the hub's body is made again whole for every outfit, so there a click
/// wears a choice at once and nothing is tried on under the pointer.
/// The pictures then (2026-11-12) went the way the user drew them ("이런 딱 스타일리쉬하게 살짝 기울어져 있으면서 전체적으로
/// 패널이 꽉 차게끔"): each much bigger than its plate or tile, over its right part, the piece leaning a little clockwise,
/// cut by the plate's own slanted shape (OutfitPictureGraphic; where and how big: OutfitPictureLook,
/// WardrobePictures.csv). A plate's name, colour count and 입음 tag stay left of its picture; a tile's words run on over
/// its picture's left side, which fades in under them.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class MainMenuWardrobe : MonoBehaviour
{
    private const float LeftWidth = 520f, RightWidth = 560f, CardGap = 10f, ChipHeight = 30f, ChipGap = 8f, TileHeight = 50f,
        TileGap = 6f, PlateHeight = 72f, PlateGap = 10f, Slant = 12f, ChipSlant = 7f;
    private const int CardColumns = 5, ChipColumns = 4;
    private static readonly string[] Groups = { "head", "neck", "torso", "arms", "legs" };

    private RectTransform root, content;
    private IWardrobeStage preview;
    private KitReveal reveal;
    private float edge, growAt;
    private string openSlot;
    private bool growNext;
    // The choice the pointer rests on, waiting this long before it is tried on, so sweeping over the list doesn't stand
    // the character up again for every plate it crosses (s).
    private const float TryAfter = .12f;
    private OutfitLook wanted;
    private string wantedSlot, wantedText, triedText, wantedLine;
    private bool wantedRemoves;
    private float wantedAt;
    // The badge over the character while a choice is tried on, so a try never reads as worn (user, 2026-11-12: a try
    // didn't feel like a preview, and 벗기 looked already taken off), and what it says the click would do.
    private RectTransform badge;
    private CanvasGroup badgeGroup;
    private TextMeshProUGUI badgeLine;
    private string shownLine;
    // The viewfinder: how far inside the wings and the bars it stands, and its corner brackets (px).
    private const float FrameGap = 40f, FrameInset = 14f, BracketLength = 46f, BracketWidth = 4f;
    private static readonly Vector2[] Corners = { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
    // Pictures (OutfitIcons) not taken yet when their plate or tile was made; they go in as they come.
    private readonly List<(OutfitPictureGraphic image, System.Func<Texture> picture)> pictures = new List<(OutfitPictureGraphic, System.Func<Texture>)>();
    // A choice's plate and a slot's tile across (px; tall as PlateHeight and TileHeight): the shooting window shows its
    // previews and crop line at the same size.
    private static float PlateWidth => RightWidth - 10f;
    private static float TileWidth => Mathf.Floor((RightWidth - TileGap) * .5f);
    // The 입음 tag of the worn plate and its gap to the picture (px).
    private const float WornTag = 84f, WornGap = 8f;
    // 사진 다시 찍기: a choice pressed opens the shooting window for its piece instead of wearing it (OutfitShootView).
    private bool shooting;
    private OutfitShootView shoot;

    // The body page (BodyShapes) stands where a slot's list would, under this name in openSlot, on one of its tabs. A save
    // there rebuilds nothing (a slider may be held, the list scrolled): its marks, the 원래대로 chip and the plate's count
    // follow instead.
    private const string BodyPage = "#body";
    private string bodyTab;
    private TextMeshProUGUI bodyCount;
    private RectTransform bodyReset;
    private readonly Dictionary<BodyShapes.Option, Image> bodyMarks = new Dictionary<BodyShapes.Option, Image>();
    // The body plate, the tabs (four to a line), a row's name column, the gap under each row, a choice chip's height, a
    // slider's number at its right, where the list stops above the page's foot and how far one wheel step scrolls it (px).
    private const float BodyPlateHeight = 62f, TabHeight = 30f, TabGap = 6f, RowName = 176f, RowGap = 8f, OptionChip = 28f,
        ValueWidth = 46f, ListFoot = 16f, ScrollStep = 40f;
    private const int TabColumns = 4;
    // The open slot's choices scroll. A pick builds the page again and the list comes back where it was scrolled to;
    // opening a slot, or going back to all of them, starts it at the top.
    private RectTransform slotList;
    // The slots' grid on the page of every slot (KitScroll), and where it was scrolled to when the page was built again.
    private KitScroll slotsGrid;
    private float slotsOffset;
    private string slotListFor;
    private float slotScroll;

    public bool IsOpen => root != null && root.gameObject.activeSelf;
    /// <summary>One slot's list, or the shooting window over it, is up: Esc goes back a step first.</summary>
    public bool ShowsSlot => IsOpen && (openSlot != null || shoot != null);

    /// <param name="preview">The character the page dresses: the title screen's stand-in, or the hub's player at the mirror.</param>
    public static MainMenuWardrobe Make(RectTransform canvas, float edge, float top, float bottom, IWardrobeStage preview)
    {
        var root = FeelUI.Rect("Wardrobe", canvas);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = new Vector2(0f, bottom);
        root.offsetMax = new Vector2(0f, -top);
        var page = root.gameObject.AddComponent<MainMenuWardrobe>();
        page.root = root;
        page.edge = edge;
        page.preview = preview;
        root.gameObject.SetActive(false);
        return page;
    }

    /// <summary>All the slots, the whole character in view, everything growing in.</summary>
    public void Open()
    {
        openSlot = null;
        slotListFor = null;
        growNext = true;
        root.gameObject.SetActive(true);
        if (preview != null) preview.Focus(null);
        Build();
    }

    public void Close()
    {
        if (root == null) return;
        CloseShoot();
        shooting = false;
        root.gameObject.SetActive(false);
        openSlot = null;
        StopTrying();
        if (preview != null) preview.Focus(null);
    }

    /// <summary>Back from one slot's list to all the slots; with the shooting window up, Esc closes that first.</summary>
    public void ShowSlots()
    {
        if (shoot != null)
        {
            CloseShoot();
            return;
        }
        if (LetGoOfPreset()) return;
        shooting = false;
        openSlot = null;
        slotListFor = null;
        StopTrying();
        if (preview != null) preview.Focus(null);
        Build();
    }

    private void OpenSlot(OutfitSlot slot)
    {
        openSlot = slot.id;
        slotListFor = null;
        shooting = false;
        StopTrying();
        if (preview != null) preview.Focus(slot.group);
        KitSound.Play(KitSound.Cue.Ping, .35f);
        Build();
    }

    // 사진 다시 찍기 on or off. While on, a choice pressed opens the shooting window for its piece instead of wearing it,
    // and nothing is tried on under the pointer (a try would stand the character up behind the window for nothing).
    private void ToggleShoot()
    {
        shooting = !shooting;
        StopTrying();
        KitSound.Play(KitSound.Cue.Ping, .35f);
        Build();
    }

    private void OpenShoot(PlayerBodyProfile.Skin skin, OutfitItem item, string slot, string colour, string name)
    {
        if (shoot != null || root == null) return;
        StopTrying();
        shoot = OutfitShootView.Open((RectTransform)root.parent, skin, item, slot, colour, name, retaken =>
        {
            shoot = null;
            // The piece's pictures were let go and taken again: the page puts the new ones in.
            if (retaken && IsOpen) Build();
        }, new Vector2(PlateWidth, PlateHeight), new Vector2(TileWidth, TileHeight), Slant);
    }

    private void CloseShoot()
    {
        if (shoot != null) shoot.Close(false);
        shoot = null;
    }

    // The page as the settings are now. Opening, it grows in; after a pick it is simply there again, so clicking through
    // the clothes doesn't replay the whole page.
    private void OnEnable() => OutfitIcons.Taken += FillPictures;

    private void OnDisable() => OutfitIcons.Taken -= FillPictures;

    private void Build()
    {
        if (slotList != null) slotScroll = slotList.anchoredPosition.y;
        KeepPresetScroll();
        slotList = null;
        if (slotsGrid != null) slotsOffset = slotsGrid.Offset;
        slotsGrid = null;
        if (content != null) Destroy(content.gameObject);
        pictures.Clear();
        bodyMarks.Clear();
        bodyCount = null;
        bodyReset = null;
        content = KitStyle.Stretch("Content", root);
        reveal = growNext ? content.gameObject.AddComponent<KitReveal>() : null;
        reveal.OrNull()?.Hide();
        growAt = .04f;
        var profile = PlayerBodyProfile.Load();
        var skins = profile == null ? new List<PlayerBodyProfile.Skin>() : profile.Skins.Where(s => s != null && s.Model != null).ToList();
        var skin = profile != null ? profile.CurrentSkin : null;
        if (skin == null || !skins.Contains(skin)) skin = skins.FirstOrDefault();
        // A character whose model has no body options shows the slots instead.
        if (openSlot == BodyPage && (skin == null || BodyShapes.For(skin.Id).Count == 0)) openSlot = null;
        BuildLeft(skins, skin);
        if (skin != null)
        {
            var right = FeelUI.Rect("Right", content);
            right.anchorMin = right.anchorMax = right.pivot = new Vector2(1f, 1f);
            right.anchoredPosition = new Vector2(-edge, 0f);
            right.sizeDelta = new Vector2(RightWidth, 0f);
            if (openSlot == PresetPage) BuildPresets(right, skin);
            else if (openSlot == BodyPage) BuildBody(right, skin);
            else if (openSlot != null && OutfitCatalog.Slot(openSlot) is OutfitSlot slot) BuildSlot(right, skin, slot);
            else BuildSlots(right, skin);
        }
        BuildBadge();
        reveal.OrNull()?.Open();
        growNext = false;
    }

    // While a choice is tried on, a pink viewfinder stands around the middle of the page where the character is, as a
    // camera's would: its 미리 보기 plate at the top left, clear of the head, and under it what a click would do. Hidden
    // until a try. A plate over the head hid the very hat being tried.
    private void BuildBadge()
    {
        badge = KitStyle.Stretch("Preview Frame", content);
        badge.offsetMin = new Vector2(edge + LeftWidth + FrameGap, FrameInset);
        badge.offsetMax = new Vector2(-(edge + RightWidth + FrameGap), -FrameInset);
        badgeGroup = badge.gameObject.AddComponent<CanvasGroup>();
        badgeGroup.interactable = badgeGroup.blocksRaycasts = false;
        foreach (var corner in Corners) Bracket(badge, corner);
        var plate = KitStyle.TopLeft("Plate", badge, new Vector2(18f, 16f), new Vector2(190f, 36f));
        KitSkew.On(KitStyle.Fill(plate, KitStyle.Accent), Slant);
        KitStyle.Flat(plate, Texts.Get("wardrobe.preview"), 22f, TextAlignmentOptions.Center, KitStyle.Ink);
        badgeLine = KitStyle.Flat(KitStyle.TopLeft("Line", badge, new Vector2(22f, 58f), new Vector2(380f, 22f)), "", 15f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Text);
        badgeLine.fontStyle = FontStyles.Normal;
        shownLine = null;
        badge.gameObject.SetActive(false);
    }

    // One corner of the viewfinder: two pink bars meeting at it.
    private static void Bracket(RectTransform frame, Vector2 corner) => Bracket(frame, corner, BracketLength, BracketWidth);

    private static void Bracket(RectTransform frame, Vector2 corner, float length, float width)
    {
        for (int i = 0; i < 2; i++)
        {
            var bar = FeelUI.Rect("Bracket", frame);
            bar.anchorMin = bar.anchorMax = bar.pivot = corner;
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = i == 0 ? new Vector2(length, width) : new Vector2(width, length);
            KitStyle.Fill(bar, KitStyle.Accent);
        }
    }

    // Pictures that have come in since their plates and tiles were made.
    private void FillPictures()
    {
        for (int i = pictures.Count - 1; i >= 0; i--)
        {
            var (image, picture) = pictures[i];
            var texture = image != null ? picture() : null;
            if (image != null && texture == null) continue;
            if (image != null)
            {
                image.Picture = texture;
                image.enabled = true;
            }
            pictures.RemoveAt(i);
        }
    }

    // A piece's picture laid over a plate or tile as the user drew it (big over its right part, cut by its lean): shown
    // at once when taken, else as soon as it is. Add the plate's words after it, so they lie over it. Null with no
    // picture to show.
    private OutfitPictureGraphic Picture(RectTransform parent, OutfitPictureLook.Where where, System.Func<Texture> picture)
    {
        if (picture == null) return null;
        var texture = picture();
        var image = OutfitPictureGraphic.Lay(parent, where, Slant, texture);
        image.enabled = texture != null;
        if (texture == null) pictures.Add((image, picture));
        return image;
    }
}
