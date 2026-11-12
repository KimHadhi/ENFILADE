using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 사진 다시 찍기: one piece of the wardrobe framed by hand and kept as its picture (the user, 2026-11-12: "미리보기 사진
/// 바꾸기 기능 넣어서 내가 직접 인게임에서 화면에 담길 만큼 보이는 걸로 각도나 위치 조절해서 찍어서 할 수 있게"). The wardrobe
/// (MainMenuWardrobe, on the title screen and at the hub's mirror) opens it from a choice in its 사진 다시 찍기 mode.
/// The piece stands alone in the photo studio (OutfitStudio) and shows live in a square that is the picture: dragging
/// turns it round (OutfitFraming yaw and pitch), the wheel brings it nearer or farther (zoom), a right or middle drag
/// moves it in the picture. 저장 takes its pictures again framed so and keeps them (OutfitIcons.Reshoot: in the editor
/// written into the project, so they ship; in a build on this PC), the framing too, for every colour of the piece;
/// 자동으로 goes back to the whole piece seen from its front (saved only with 저장); 취소, or Esc through the wardrobe,
/// leaves everything as it was. The look is the menus' (a dark veil, the kit's panel, white and one pink).
/// </summary>
[DisallowMultipleComponent]
public sealed class OutfitShootView : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
{
    // The panel and the picture's square on it (px at 1080p), its margin, and the live picture's side (px).
    private const float PanelWidth = 920f, PanelHeight = 640f, ViewSize = 480f, Margin = 36f, Slant = 10f;
    private const int LiveSize = 512;
    // Degrees turned per dragged pixel (1080p), how much nearer one wheel step comes, and the zoom's range.
    private const float TurnPerPixel = .4f, ZoomStep = .1f, LeastZoom = .4f, MostZoom = 8f, MostPitch = 80f;

    private RectTransform veil;
    private RawImage picture;
    private TextMeshProUGUI state;
    private OutfitStudio studio;
    private RenderTexture live;
    private OutfitFraming framing;
    private PlayerBodyProfile.Skin skin;
    private OutfitItem item;
    private string slot, colour;
    private Action<bool> closed;
    private bool dirty = true, done, standing;

    /// <summary>Open (closing it gives no answer to its caller).</summary>
    public bool IsOpen => !done;

    /// <summary>
    /// Opens over <paramref name="canvas"/> for an item in a colour (or, no item, the character's own piece of
    /// <paramref name="slot"/> in a colour set); <paramref name="closed"/> hears whether the piece's pictures were taken again.
    /// </summary>
    public static OutfitShootView Open(RectTransform canvas, PlayerBodyProfile.Skin skin, OutfitItem item, string slot, string colour,
        string name, Action<bool> closed)
    {
        var veil = KitStyle.Stretch("Shoot Picture", canvas);
        KitStyle.Fill(veil, KitStyle.With(KitStyle.Night, .8f)).raycastTarget = true; // nothing under it takes a click
        var panel = FeelUI.Rect("Panel", veil);
        panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        KitStyle.Cut(KitStyle.Stretch("Back", panel), KitStyle.Panel);
        KitStyle.Bar("Rail", panel, Vector2.zero, new Vector2(PanelWidth, KitStyle.AccentWidth), KitStyle.Accent);
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", panel, new Vector2(Margin, 22f), new Vector2(520f, 56f)), Texts.Get("wardrobe.shoot.title"), 40f,
            TextAlignmentOptions.MidlineLeft);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 3f);
        Note(panel, new Vector2(Margin + 4f, 78f), PanelWidth - Margin * 2f, name, 18f);

        // The picture's square: dark under the see-through piece, the menus' pink viewfinder corners round it.
        var box = KitStyle.TopLeft("View", panel, new Vector2(Margin, 116f), new Vector2(ViewSize, ViewSize));
        KitStyle.Fill(box, KitStyle.With(KitStyle.Night, .95f));
        var image = KitStyle.Stretch("Picture", box).gameObject.AddComponent<RawImage>();
        image.raycastTarget = true;
        foreach (var corner in new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) })
            Bracket(box, corner);

        float column = Margin * 2f + ViewSize, width = PanelWidth - column - Margin;
        var hint = Note(panel, new Vector2(column, 120f), width, Texts.Get("wardrobe.shoot.hint"), 16f);
        hint.textWrappingMode = TextWrappingModes.Normal;
        hint.rectTransform.sizeDelta = new Vector2(width, 90f);
        hint.alignment = TextAlignmentOptions.TopLeft;
        var where = Note(panel, new Vector2(column, 220f), width,
            Texts.Get(OutfitPictures.ProjectWriter != null ? "wardrobe.shoot.editor" : "wardrobe.shoot.local"), 14f);
        where.textWrappingMode = TextWrappingModes.Normal;
        where.rectTransform.sizeDelta = new Vector2(width, 76f);
        where.alignment = TextAlignmentOptions.TopLeft;
        var readout = Note(panel, new Vector2(column, 306f), width, "", 15f);

        var view = image.gameObject.AddComponent<OutfitShootView>();
        view.veil = veil;
        view.picture = image;
        view.state = readout;
        view.closed = closed;
        float buttons = PanelHeight - Margin - 46f;
        Button(panel, new Vector2(column, buttons - 112f), width, Texts.Get("wardrobe.shoot.auto"), false, view.Auto);
        Button(panel, new Vector2(column, buttons - 56f), width, Texts.Get("wardrobe.shoot.cancel"), false, () => view.Close(false));
        Button(panel, new Vector2(column, buttons), width, Texts.Get("wardrobe.shoot.save"), true, view.Save);
        view.Begin(skin, item, slot, colour);
        KitSound.Play(KitSound.Cue.Ping, .35f);
        return view;
    }

    private void Begin(PlayerBodyProfile.Skin who, OutfitItem piece, string place, string set)
    {
        skin = who;
        item = piece;
        slot = place;
        colour = set ?? "";
        string key = item != null ? OutfitPictures.ItemPiece(skin.Id, item.id) : OutfitPictures.OwnPiece(skin.Id, slot);
        framing = OutfitPictures.FramingOf(key);
        studio = new OutfitStudio();
        standing = studio.Stand(skin, item, colour, slot);
        live = new RenderTexture(LiveSize, LiveSize, 0, RenderTextureFormat.ARGB32) { name = "Outfit Shoot Live" };
        live.Create();
        picture.texture = live;
        dirty = true;
    }

    // ---- Framing by hand ----

    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        if (done) return;
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            // In 1080p pixels, so the same drag turns it as far at any resolution. A drag to the right brings the piece's
            // front round to the right, as the wardrobe's character turns (MainMenuPreview.Turn): the lens goes round the
            // other way, and + yaw is the lens round to the piece's right. A drag up shows more of it from below.
            Vector2 turn = eventData.delta * 1080f / Mathf.Max(1, Screen.height) * TurnPerPixel;
            framing.yaw = Mathf.Repeat(framing.yaw + turn.x + 180f, 360f) - 180f;
            framing.pitch = Mathf.Clamp(framing.pitch - turn.y, -MostPitch, MostPitch);
        }
        else
        {
            // Moved with the pointer: the whole square is two halves of the picture.
            var canvas = GetComponentInParent<Canvas>();
            float across = ViewSize * (canvas != null ? canvas.scaleFactor : 1f);
            framing.x += eventData.delta.x / Mathf.Max(1f, across) * 2f;
            framing.y += eventData.delta.y / Mathf.Max(1f, across) * 2f;
        }
        dirty = true;
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (done || Mathf.Approximately(eventData.scrollDelta.y, 0f)) return;
        framing.zoom = Mathf.Clamp(framing.zoom * (1f + Mathf.Sign(eventData.scrollDelta.y) * ZoomStep), LeastZoom, MostZoom);
        dirty = true;
    }

    private void Auto()
    {
        framing = OutfitFraming.Auto;
        dirty = true;
        KitSound.Play(KitSound.Cue.Ping, .25f);
    }

    private void LateUpdate()
    {
        if (done || !dirty) return;
        dirty = false;
        if (standing)
        {
            studio.Frame(framing);
            studio.Shoot(live);
        }
        if (state != null)
            state.text = Texts.Format("wardrobe.shoot.state", Mathf.RoundToInt(framing.yaw), Mathf.RoundToInt(framing.pitch),
                framing.zoom.ToString("0.0", CultureInfo.InvariantCulture));
    }

    // ---- Keeping it ----

    private void Save()
    {
        if (done) return;
        if (!standing)
        {
            KitSound.Play(KitSound.Cue.Refuse, .4f);
            return;
        }
        // The window's own studio goes first: the pictures are taken again in one of their own.
        studio.Clear();
        int kept = OutfitIcons.Reshoot(skin, item, slot, framing);
        KitSound.Play(kept > 0 ? KitSound.Cue.Buy : KitSound.Cue.Refuse, .5f);
        // Kept or not (a file that couldn't be written says so in the console), the piece's old pictures were let go and
        // new ones taken: the wardrobe puts them in.
        Close(true);
    }

    /// <summary>Closes it; <paramref name="retaken"/> tells the wardrobe the piece's pictures were taken again.</summary>
    public void Close(bool retaken)
    {
        if (done) return;
        done = true;
        Release();
        if (veil != null) Destroy(veil.gameObject);
        closed?.Invoke(retaken);
    }

    private void Release()
    {
        studio?.Dispose();
        studio = null;
        if (picture != null) picture.texture = null;
        if (live != null) { live.Release(); Destroy(live); }
        live = null;
    }

    private void OnDestroy()
    {
        if (done) return;
        done = true;
        Release();
    }

    // ---- The kit's pieces as this window uses them ----

    private static TextMeshProUGUI Note(RectTransform parent, Vector2 at, float width, string text, float size)
    {
        var note = KitStyle.Flat(KitStyle.TopLeft("Note", parent, at, new Vector2(width, size + 8f)), text, size, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        note.fontStyle = FontStyles.Normal;
        note.overflowMode = CombatUIAssets.ClippedOverflow(note);
        return note;
    }

    private static void Bracket(RectTransform frame, Vector2 corner)
    {
        const float length = 40f, thickness = 4f;
        for (int i = 0; i < 2; i++)
        {
            var bar = FeelUI.Rect("Bracket", frame);
            bar.anchorMin = bar.anchorMax = bar.pivot = corner;
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = i == 0 ? new Vector2(length, thickness) : new Vector2(thickness, length);
            KitStyle.Fill(bar, KitStyle.Accent);
        }
    }

    // A slanted plate of a button: 저장 solid pink, the others faint.
    private static void Button(RectTransform parent, Vector2 at, float width, string text, bool accent, Action pressed)
    {
        var plate = KitStyle.TopLeft("Button " + text, parent, at, new Vector2(width, 46f));
        var back = KitStyle.Fill(plate, accent ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .09f));
        KitSkew.On(back, Slant);
        back.raycastTarget = true;
        KitStyle.Flat(KitStyle.Stretch("Text", plate), text, 20f, TextAlignmentOptions.Center, accent ? KitStyle.Ink : KitStyle.Text);
        var button = plate.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(() => pressed());
        plate.gameObject.AddComponent<KitHover>();
    }
}
