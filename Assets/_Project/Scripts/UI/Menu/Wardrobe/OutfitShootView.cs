using System;
using System.Collections.Generic;
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
/// Then (2026-11-12) the user drew how the list should lay its pictures, big, leaning and cut by the plates, and asked
/// for the lean here too ("사진 다시 찍기 기능에 그 옷을 시계 방향, 반시계 방향으로 좀 turn해서 이렇게 스타일리쉬한 각도에서도
/// 찍을 수 있게"): the 기울기 slider, or ‹ 반시계 · 시계 › a degree a press, leans the piece in the picture
/// (OutfitFraming.roll); the square shows in pink the part a plate of the list keeps and dims what the plate cuts
/// (OutfitCropVeil); under it a plate and a tile of the wardrobe's own size show the live picture laid just as the
/// wardrobe lays it (OutfitPictureGraphic), words and all.
/// Then (2026-11-12: "옷장 사진 찍은 거 기본값으로 되돌리기 기능 넣어 줘"): 기본값으로 puts this piece back as it came, and
/// 모두 기본값으로 every piece framed by hand, of every character (OutfitIcons.Restore, RestoreAll: in the editor the
/// table's rows go and the pictures are taken again framed automatically, over the project's; in a build this PC's own
/// framings and pictures are deleted). Each asks once more before it throws anything away, as the key window's 모두
/// 기본값 does, and is faint when there is nothing to undo.
/// </summary>
[DisallowMultipleComponent]
public sealed class OutfitShootView : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
{
    // The panel's least width, its margin, the picture's square on it (px at 1080p), the buttons' lean, and the live
    // picture's side (px).
    private const float LeastWidth = 920f, Margin = 36f, ViewSize = 440f, ViewTop = 112f, Slant = 10f;
    private const int LiveSize = 512;
    // Degrees turned per dragged pixel (1080p), how much nearer one wheel step comes, and the zoom's range.
    private const float TurnPerPixel = .4f, ZoomStep = .1f, LeastZoom = .4f, MostZoom = 8f, MostPitch = 80f;
    // How far one press of ‹ 반시계 or 시계 › leans it (°), and those buttons' width (px).
    private const float TiltStep = 1f, TiltButton = 92f;
    // At most this many parts get a choice of their own (the rest are reached through 모두).
    private const int MostPartChoices = 4;
    // How long 기본값으로 and 모두 기본값으로 wait for their second press (s), and their row's height (px).
    private const float ConfirmSeconds = 3f, ResetHeight = 34f;
    // The plate and tile shown under the square: the gap between them and the room for their names above (px).
    private const float PreviewGap = 21f, PreviewName = 22f;
    // When the wardrobe doesn't say: its plates' and tiles' size and lean (px), as MainMenuWardrobe makes them.
    private static readonly Vector2 PlainPlate = new Vector2(550f, 72f), PlainTile = new Vector2(277f, 50f);
    private const float PlainSlant = 12f;
    private static readonly Vector2[] Corners = { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };

    private RectTransform veil;
    private RawImage picture;
    private TextMeshProUGUI state;
    private Slider tilt;
    private readonly List<OutfitPictureGraphic> previews = new List<OutfitPictureGraphic>();
    // A piece in parts well apart (OutfitStudio.Parts: gloves, a pair of earrings): a row choosing which the picture
    // holds, each part from the picture's left and then all of them (part 0 in the row = all).
    private RectTransform partsRow;
    private TextMeshProUGUI partsLabel;
    private readonly List<(Image back, TextMeshProUGUI label, int part)> partButtons = new List<(Image, TextMeshProUGUI, int)>();
    // 기본값으로 and 모두 기본값으로, each waiting a while for its second press, and what they would undo: whether this
    // piece was framed by hand, how many pieces are. The question stands where the note on where pictures are kept is.
    private (Image back, TextMeshProUGUI label) reset, resetAll;
    private TextMeshProUGUI keepNote;
    private string keepText, pieceKey;
    private float resetUntil = -1f, resetAllUntil = -1f;
    private bool canReset;
    private int changed;
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
    /// <paramref name="slot"/> in a colour set); <paramref name="closed"/> hears whether the piece's pictures were taken
    /// again. <paramref name="plate"/>, <paramref name="tile"/> and <paramref name="slant"/> are the wardrobe's plates'
    /// and tiles' size (px) and lean, for the crop line and the previews.
    /// </summary>
    public static OutfitShootView Open(RectTransform canvas, PlayerBodyProfile.Skin skin, OutfitItem item, string slot, string colour,
        string name, Action<bool> closed, Vector2 plate = default, Vector2 tile = default, float? slant = null)
    {
        if (plate.x <= 0f || plate.y <= 0f) plate = PlainPlate;
        if (tile.x <= 0f || tile.y <= 0f) tile = PlainTile;
        float lean = slant ?? PlainSlant;
        float width = Mathf.Max(LeastWidth, Margin * 2f + plate.x + PreviewGap + tile.x);
        float viewBottom = ViewTop + ViewSize, previewTop = viewBottom + 16f + PreviewName;
        float height = previewTop + Mathf.Max(plate.y, tile.y) + Margin;

        var veil = KitStyle.Stretch("Shoot Picture", canvas);
        KitStyle.Fill(veil, KitStyle.With(KitStyle.Night, .8f)).raycastTarget = true; // nothing under it takes a click
        var panel = FeelUI.Rect("Panel", veil);
        panel.sizeDelta = new Vector2(width, height);
        KitStyle.Cut(KitStyle.Stretch("Back", panel), KitStyle.Panel);
        KitStyle.Bar("Rail", panel, Vector2.zero, new Vector2(width, KitStyle.AccentWidth), KitStyle.Accent);
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", panel, new Vector2(Margin, 22f), new Vector2(520f, 56f)), Texts.Get("wardrobe.shoot.title"), 40f,
            TextAlignmentOptions.MidlineLeft);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 3f);
        Note(panel, new Vector2(Margin + 4f, 78f), width - Margin * 2f, name, 18f);

        // The picture's square: dark under the see-through piece, what a plate of the list keeps inside the pink line
        // and the rest dimmed, the menus' pink viewfinder corners round it.
        var box = KitStyle.TopLeft("View", panel, new Vector2(Margin, ViewTop), new Vector2(ViewSize, ViewSize));
        KitStyle.Fill(box, KitStyle.With(KitStyle.Night, .95f));
        var image = KitStyle.Stretch("Picture", box).gameObject.AddComponent<RawImage>();
        image.raycastTarget = true;
        OutfitCropVeil.Lay(box, plate, lean, KitStyle.With(KitStyle.Night, .68f), KitStyle.Accent);
        foreach (var corner in Corners) Bracket(box, corner);

        var view = image.gameObject.AddComponent<OutfitShootView>();
        view.veil = veil;
        view.picture = image;
        view.closed = closed;

        float column = Margin * 2f + ViewSize, columnWidth = width - column - Margin;
        var hint = Note(panel, new Vector2(column, ViewTop + 4f), columnWidth, Texts.Get("wardrobe.shoot.hint"), 15f);
        hint.textWrappingMode = TextWrappingModes.Normal;
        hint.rectTransform.sizeDelta = new Vector2(columnWidth, 84f);
        hint.alignment = TextAlignmentOptions.TopLeft;

        // The lean: a slider between a press either way, a faint tick at upright and a brighter one at the automatic lean.
        Note(panel, new Vector2(column, ViewTop + 94f), columnWidth, Texts.Get("wardrobe.shoot.tilt"), 14f);
        float row = ViewTop + 118f, track = column + TiltButton + 10f, trackWidth = columnWidth - (TiltButton + 10f) * 2f;
        float range = OutfitPictureLook.TiltRange, auto = OutfitPictureLook.AutoTilt;
        Tick(panel, track, trackWidth, row, Mathf.InverseLerp(-range, range, 0f), KitStyle.With(KitStyle.Ice, .3f));
        Tick(panel, track, trackWidth, row, Mathf.InverseLerp(-range, range, auto), KitStyle.With(KitStyle.Ice, .65f));
        view.tilt = SettingsWidgets.Slider(panel, -range, range, auto, view.SetTilt, wholeNumbers: true);
        var slider = (RectTransform)view.tilt.transform;
        slider.anchorMin = slider.anchorMax = slider.pivot = new Vector2(0f, 1f);
        slider.anchoredPosition = new Vector2(track, -(row + 3f));
        slider.sizeDelta = new Vector2(trackWidth, 28f);
        Button(panel, new Vector2(column, row), TiltButton, 34f, Texts.Get("wardrobe.shoot.ccw"), false, () => view.Lean(-TiltStep));
        Button(panel, new Vector2(column + columnWidth - TiltButton, row), TiltButton, 34f, Texts.Get("wardrobe.shoot.cw"), false, () => view.Lean(TiltStep));

        // Which part of a piece in parts well apart the picture holds (filled in once the piece stands: Begin).
        view.partsLabel = Note(panel, new Vector2(column, ViewTop + 160f), columnWidth, Texts.Get("wardrobe.shoot.part"), 14f);
        view.partsRow = KitStyle.TopLeft("Parts", panel, new Vector2(column, ViewTop + 182f), new Vector2(columnWidth, 34f));

        view.state = Note(panel, new Vector2(column, ViewTop + 224f), columnWidth, "", 15f);
        var where = Note(panel, new Vector2(column, ViewTop + 252f), columnWidth,
            Texts.Get(OutfitPictures.ProjectWriter != null ? "wardrobe.shoot.editor" : "wardrobe.shoot.local"), 14f);
        where.textWrappingMode = TextWrappingModes.Normal;
        where.rectTransform.sizeDelta = new Vector2(columnWidth, 76f);
        where.alignment = TextAlignmentOptions.TopLeft;
        view.keepNote = where;
        view.keepText = where.text;

        // 기본값으로 · 모두 기본값으로 above them: each asks once more before it throws anything away.
        float half = (columnWidth - 8f) / 2f, resetRow = viewBottom - 46f - 8f - ResetHeight;
        view.reset = Button(panel, new Vector2(column, resetRow), half, ResetHeight, Texts.Get("wardrobe.shoot.reset"), false, view.RestorePiece);
        view.resetAll = Button(panel, new Vector2(column + half + 8f, resetRow), half, ResetHeight, Texts.Get("wardrobe.shoot.reset_all"), false,
            view.RestoreEvery);

        // 자동으로 · 취소 · 저장 side by side along the square's foot.
        float third = (columnWidth - 16f) / 3f;
        Button(panel, new Vector2(column, viewBottom - 46f), third, 46f, Texts.Get("wardrobe.shoot.auto"), false, view.Auto);
        Button(panel, new Vector2(column + third + 8f, viewBottom - 46f), third, 46f, Texts.Get("wardrobe.shoot.cancel"), false, () => view.Close(false));
        Button(panel, new Vector2(column + (third + 8f) * 2f, viewBottom - 46f), third, 46f, Texts.Get("wardrobe.shoot.save"), true, view.Save);

        // Under the square, a plate of the list and a tile of the slots showing the live picture as the wardrobe lays it.
        float tileAt = Margin + plate.x + PreviewGap;
        Note(panel, new Vector2(Margin + 2f, viewBottom + 16f), plate.x, Texts.Get("wardrobe.shoot.plate"), 14f);
        Note(panel, new Vector2(tileAt + 2f, viewBottom + 16f), tile.x, Texts.Get("wardrobe.shoot.tile"), 14f);
        view.previews.Add(PreviewPlate(panel, new Vector2(Margin, previewTop), plate, lean, name));
        string slotName = OutfitCatalog.Slot(slot) is OutfitSlot shown ? shown.Name : "";
        view.previews.Add(PreviewTile(panel, new Vector2(tileAt, previewTop), tile, lean, slotName, name, item != null));

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
        pieceKey = key;
        canReset = OutfitPictures.IsChanged(pieceKey);
        changed = OutfitPictures.ChangedPieces().Count;
        LabelResets();
        if (tilt != null) tilt.SetValueWithoutNotify(Tilt);
        studio = new OutfitStudio();
        standing = studio.Stand(skin, item, colour, slot);
        BuildParts();
        live = new RenderTexture(LiveSize, LiveSize, 0, RenderTextureFormat.ARGB32) { name = "Outfit Shoot Live" };
        live.Create();
        picture.texture = live;
        foreach (var preview in previews) if (preview != null) preview.Picture = live;
        dirty = true;
    }

    // How far the piece leans clockwise in the picture now (°): the automatic lean and the framing's own.
    private float Tilt => OutfitPictureLook.AutoTilt + framing.roll;

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
            // Moved with the pointer: the whole square is two halves of the picture. The picture's sides are the leaning
            // lens's, so it moves the way the pointer does whatever the lean.
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

    // The slider moved: the piece leans so far clockwise (°).
    private void SetTilt(float lean)
    {
        if (done) return;
        framing.roll = lean - OutfitPictureLook.AutoTilt;
        dirty = true;
    }

    // ‹ 반시계 or 시계 ›: a whole degree further one way.
    private void Lean(float by)
    {
        if (done) return;
        float range = OutfitPictureLook.TiltRange;
        float lean = Mathf.Clamp(Mathf.Round(Tilt + by), -range, range);
        framing.roll = lean - OutfitPictureLook.AutoTilt;
        if (tilt != null) tilt.SetValueWithoutNotify(lean);
        dirty = true;
        KitSound.Play(KitSound.Cue.Ping, .15f);
    }

    private void Auto()
    {
        framing = OutfitFraming.Auto;
        if (tilt != null) tilt.SetValueWithoutNotify(Tilt);
        RefreshParts();
        dirty = true;
        KitSound.Play(KitSound.Cue.Ping, .25f);
    }

    // ---- A piece in parts well apart ----

    // One choice a part (왼쪽 · 오른쪽, 왼쪽 · 가운데 · 오른쪽, or numbered past three) and 모두; none for a piece in one.
    private void BuildParts()
    {
        partButtons.Clear();
        int count = standing ? studio.Parts : 1;
        bool show = count > 1 && partsRow != null;
        if (partsLabel != null) partsLabel.gameObject.SetActive(show);
        if (partsRow != null) partsRow.gameObject.SetActive(show);
        if (!show) return;
        int shown = Mathf.Min(count, MostPartChoices);
        float width = (partsRow.sizeDelta.x - 8f * shown) / (shown + 1);
        for (int i = 0; i <= shown; i++)
        {
            int part = i < shown ? i + 1 : 0;
            string text = part == 0 ? Texts.Get("wardrobe.shoot.part.all")
                : shown == 2 ? Texts.Get(part == 1 ? "wardrobe.shoot.part.left" : "wardrobe.shoot.part.right")
                : shown == 3 ? Texts.Get(part == 1 ? "wardrobe.shoot.part.left" : part == 2 ? "wardrobe.shoot.part.middle" : "wardrobe.shoot.part.right")
                : part.ToString(CultureInfo.InvariantCulture);
            var (back, label) = Choice(partsRow, new Vector2(i * (width + 8f), 0f), width, 34f, text, () => PickPart(part));
            partButtons.Add((back, label, part));
        }
        RefreshParts();
    }

    // A part picked (0: all of them). The automatic framing's own choice stays automatic, so it is saved as such.
    private void PickPart(int part)
    {
        if (done || !standing) return;
        int automatic = studio.AutoPart;
        framing.part = part == automatic ? 0 : part == 0 ? -1 : part;
        RefreshParts();
        dirty = true;
        KitSound.Play(KitSound.Cue.Ping, .2f);
    }

    private void RefreshParts()
    {
        if (studio == null || !standing) return;
        int holds = studio.PartOf(framing);
        foreach (var (back, label, part) in partButtons)
        {
            bool on = part == holds;
            if (back != null) back.color = on ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .09f);
            if (label != null) label.color = on ? KitStyle.Ink : KitStyle.Text;
        }
    }

    private void LateUpdate()
    {
        if (done) return;
        // A first press of 기본값으로 or 모두 기본값으로 not followed by a second in time: they go back to what they were.
        if ((resetUntil > 0f || resetAllUntil > 0f) && Time.unscaledTime > Mathf.Max(resetUntil, resetAllUntil))
        {
            resetUntil = resetAllUntil = -1f;
            LabelResets();
        }
        if (!dirty) return;
        dirty = false;
        if (standing)
        {
            studio.Frame(framing);
            studio.Shoot(live);
        }
        if (state != null)
            state.text = Texts.Format("wardrobe.shoot.state", Mathf.RoundToInt(framing.yaw), Mathf.RoundToInt(framing.pitch),
                framing.zoom.ToString("0.0", CultureInfo.InvariantCulture), Mathf.RoundToInt(Tilt));
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

    // ---- Back as it came ----

    // 기본값으로: this piece's own framing and pictures go and it is pictured as it came (OutfitIcons.Restore). Asked twice,
    // since what was framed by hand is thrown away. (Not named Reset: Unity calls a MonoBehaviour's Reset in the editor.)
    private void RestorePiece()
    {
        if (done) return;
        if (!canReset)
        {
            Say(Texts.Get("wardrobe.shoot.reset.none"));
            KitSound.Play(KitSound.Cue.Refuse, .4f);
            return;
        }
        if (Time.unscaledTime > resetUntil)
        {
            resetUntil = Time.unscaledTime + ConfirmSeconds;
            resetAllUntil = -1f;
            Ask(Texts.Get(OutfitPictures.ProjectWriter != null ? "wardrobe.shoot.reset.ask.editor" : "wardrobe.shoot.reset.ask.local"));
            return;
        }
        // The window's own studio goes first: the editor takes the pictures again in one of their own.
        studio?.Clear();
        OutfitIcons.Restore(skin, item, slot);
        KitSound.Play(KitSound.Cue.Buy, .5f);
        Close(true);
    }

    // 모두 기본값으로: every piece framed by hand, of every character, put back the same way. Asked twice.
    private void RestoreEvery()
    {
        if (done) return;
        if (changed == 0)
        {
            Say(Texts.Get("wardrobe.shoot.reset_all.none"));
            KitSound.Play(KitSound.Cue.Refuse, .4f);
            return;
        }
        if (Time.unscaledTime > resetAllUntil)
        {
            resetAllUntil = Time.unscaledTime + ConfirmSeconds;
            resetUntil = -1f;
            Ask(Texts.Format(OutfitPictures.ProjectWriter != null ? "wardrobe.shoot.reset_all.ask.editor" : "wardrobe.shoot.reset_all.ask.local",
                changed));
            return;
        }
        studio?.Clear();
        OutfitIcons.RestoreAll();
        KitSound.Play(KitSound.Cue.Buy, .5f);
        Close(true);
    }

    // The question where the note on where pictures are kept stands, and the buttons relabelled for the second press.
    private void Ask(string question)
    {
        if (keepNote != null)
        {
            keepNote.text = question;
            keepNote.color = KitStyle.Accent;
        }
        LabelResets();
        KitSound.Play(KitSound.Cue.Ping, .3f);
    }

    private void Say(string text)
    {
        if (state != null) state.text = text;
    }

    // The two as they stand: faint with nothing to undo, pink while one waits for its second press; the note comes back
    // once neither waits.
    private void LabelResets()
    {
        float now = Time.unscaledTime;
        bool askingOne = now <= resetUntil, askingAll = now <= resetAllUntil;
        Paint(reset, canReset, askingOne, askingOne ? "wardrobe.shoot.reset.confirm" : "wardrobe.shoot.reset");
        Paint(resetAll, changed > 0, askingAll, askingAll ? "wardrobe.shoot.reset_all.confirm" : "wardrobe.shoot.reset_all");
        if (keepNote != null && !askingOne && !askingAll)
        {
            keepNote.text = keepText;
            keepNote.color = KitStyle.Soft;
        }
    }

    private static void Paint((Image back, TextMeshProUGUI label) button, bool usable, bool asking, string text)
    {
        if (button.label != null)
        {
            button.label.text = Texts.Get(text);
            button.label.color = asking ? KitStyle.Ink : usable ? KitStyle.Text : KitStyle.With(KitStyle.Text, .35f);
        }
        if (button.back != null) button.back.color = asking ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, usable ? .09f : .04f);
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
        foreach (var preview in previews) if (preview != null) preview.Picture = null;
        if (live != null) { live.Release(); Destroy(live); }
        live = null;
    }

    private void OnDestroy()
    {
        if (done) return;
        done = true;
        Release();
    }

    // ---- The previews under the square ----

    // A choice's plate of the list as it would show this piece: the wardrobe's size and lean, its faint back and left
    // bar, the name kept clear of the picture, the live picture laid as the list lays it.
    private static OutfitPictureGraphic PreviewPlate(RectTransform panel, Vector2 at, Vector2 size, float lean, string name)
    {
        var plate = KitStyle.TopLeft("Plate Preview", panel, at, size);
        KitSkew.On(KitStyle.Fill(KitStyle.Stretch("Back", plate), KitStyle.With(KitStyle.Ice, .07f)), lean);
        KitSkew.On(KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(4f, size.y), KitStyle.With(KitStyle.Ice, .35f)), lean);
        var shown = OutfitPictureGraphic.Lay(plate, OutfitPictureLook.Where.Plate, lean, null);
        float room = Mathf.Max(60f, OutfitPictureLook.LeftOf(size, OutfitPictureLook.Of(OutfitPictureLook.Where.Plate)) - 32f);
        var title = KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(22f, 10f), new Vector2(room, 30f)), name, 22f,
            TextAlignmentOptions.MidlineLeft, KitStyle.With(KitStyle.Text, .85f));
        title.overflowMode = CombatUIAssets.ClippedOverflow(title);
        return shown;
    }

    // A slot's tile as it would show this piece worn: an item darkens it with a pink edge (a changed slot), the
    // character's own piece leaves it faint.
    private static OutfitPictureGraphic PreviewTile(RectTransform panel, Vector2 at, Vector2 size, float lean, string slotName, string name,
        bool changed)
    {
        var tile = KitStyle.TopLeft("Tile Preview", panel, at, size);
        KitSkew.On(KitStyle.Fill(KitStyle.Stretch("Back", tile), changed ? KitStyle.With(KitStyle.Night, .95f) : KitStyle.With(KitStyle.Ice, .07f)), lean);
        if (changed)
        {
            var rim = KitStyle.Stretch("Edge", tile);
            rim.sizeDelta = new Vector2(2f, 2f);
            rim.SetAsFirstSibling();
            KitSkew.On(KitStyle.Fill(rim, KitStyle.With(KitStyle.Accent, .75f)), lean);
        }
        KitSkew.On(KitStyle.Bar("Bar", tile, Vector2.zero, new Vector2(3f, size.y), changed ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .3f)), lean);
        var shown = OutfitPictureGraphic.Lay(tile, OutfitPictureLook.Where.Tile, lean, null);
        float room = size.x - 84f;
        Note(tile, new Vector2(16f, 4f), room, slotName, 13f);
        var worn = KitStyle.Flat(KitStyle.TopLeft("Worn", tile, new Vector2(16f, 20f), new Vector2(room, 26f)), name, 18f,
            TextAlignmentOptions.MidlineLeft, changed ? KitStyle.Text : KitStyle.With(KitStyle.Text, .7f));
        worn.fontStyle = FontStyles.Normal;
        worn.overflowMode = CombatUIAssets.ClippedOverflow(worn);
        return shown;
    }

    // ---- The kit's pieces as this window uses them ----

    private static TextMeshProUGUI Note(RectTransform parent, Vector2 at, float width, string text, float size)
    {
        var note = KitStyle.Flat(KitStyle.TopLeft("Note", parent, at, new Vector2(width, size + 8f)), text, size, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        note.fontStyle = FontStyles.Normal;
        note.overflowMode = CombatUIAssets.ClippedOverflow(note);
        return note;
    }

    // A thin mark on the lean slider's track, under its handle (which runs 7 px inside each end).
    private static void Tick(RectTransform parent, float track, float width, float row, float place, Color color) =>
        KitStyle.Fill(KitStyle.TopLeft("Tick", parent, new Vector2(track + 7f + place * (width - 14f) - 1f, row + 7f), new Vector2(2f, 20f)), color);

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

    // A slanted choice whose back and words the caller lights (pink for the one taken).
    private static (Image back, TextMeshProUGUI label) Choice(RectTransform parent, Vector2 at, float width, float height, string text,
        Action pressed)
    {
        var plate = KitStyle.TopLeft("Choice " + text, parent, at, new Vector2(width, height));
        var back = KitStyle.Fill(plate, KitStyle.With(KitStyle.Ice, .09f));
        KitSkew.On(back, Slant);
        back.raycastTarget = true;
        var label = KitStyle.Flat(KitStyle.Stretch("Text", plate), text, 16f, TextAlignmentOptions.Center, KitStyle.Text);
        label.overflowMode = CombatUIAssets.ClippedOverflow(label);
        var button = plate.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(() => pressed());
        plate.gameObject.AddComponent<KitHover>();
        return (back, label);
    }

    // A slanted plate of a button: 저장 solid pink, the others faint. Its back and words, for a caller that relights them.
    private static (Image back, TextMeshProUGUI label) Button(RectTransform parent, Vector2 at, float width, float height, string text, bool accent,
        Action pressed)
    {
        var plate = KitStyle.TopLeft("Button " + text, parent, at, new Vector2(width, height));
        var back = KitStyle.Fill(plate, accent ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .09f));
        KitSkew.On(back, Slant);
        back.raycastTarget = true;
        var label = KitStyle.Flat(KitStyle.Stretch("Text", plate), text, height < 40f ? 16f : 20f, TextAlignmentOptions.Center,
            accent ? KitStyle.Ink : KitStyle.Text);
        label.overflowMode = CombatUIAssets.ClippedOverflow(label);
        var button = plate.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(() => pressed());
        plate.gameObject.AddComponent<KitHover>();
        return (back, label);
    }
}
