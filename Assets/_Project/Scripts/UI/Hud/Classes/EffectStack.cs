using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The timed effects on this PC's player, each in a slot of its own under the class plate (TimedEffects' entries): its
/// name, how many seconds are left as a number, and a line that drains with it, in the effect's own colour and with its
/// icon. The user, 2026-11-18: "건 카타 남은 지속시간 등 클래스별 필수 메커니즘 정보를 시인성 좋게 HUD에 명확히 표시 …
/// 독립적인 다중 슬롯/스택 레이아웃으로 분리 표시".
/// <para>
/// It takes the place of the one bar at the plate's foot, which could show a single effect and chose it by a chain of
/// "this one if it runs, else that one" (ClassHUD, ClassCards.BarShare): a gun swapped during a Gun Kata started
/// Gunslinger and the kata's time was gone from the screen. Here nothing is chosen: every entry has its slot and keeps
/// it for as long as it runs, in the order they began; when one ends the ones under it move up. A column holds as many
/// slots as fit above the screen's foot, and more of them go into columns side by side, narrower, never over each other.
/// </para>
/// It stands in the flow under the class plate, below the count of what the player has standing (DeployStrip) when
/// that shows. Where each slot goes is a pure function of how many there are and how much room there is (Slot), so the
/// stack can be checked without playing. Its box, text and font are the addon HUD's (HudTone); each effect keeps its own
/// colour. Numbers: Tables/ClassHud, rows Effects.*. Real time, this PC's own player only.
/// </summary>
public sealed class EffectStack
{
    public const string Table = "ClassHud", Row = "Effects";
    private const float FadeIn = 10f, FadeOut = 7f;

    private sealed class Cell
    {
        public RectTransform Root, Fill;
        public CanvasGroup Group;
        public Image Edge, Line;
        public RawImage Icon;
        public TextMeshProUGUI Name, Time;
        public string Id = "", Words = "";
        public float Value = float.NaN, ShownTime = -1f;
        public int Stacks = -1;
        public bool Placed, Live;
    }

    private readonly RectTransform root;
    private readonly List<Cell> slots = new List<Cell>();
    private readonly float width;
    private float below, height, gap, floor, nameSize, timeSize, follow;

    /// <param name="canvas">The class HUD's canvas: the stack is a piece of its own there.</param>
    /// <param name="plateWidth">The class plate's width (px): the stack is as wide.</param>
    public EffectStack(Transform canvas, float plateWidth)
    {
        width = plateWidth;
        root = FeelUI.Rect("Effects", canvas);
        root.anchorMin = root.anchorMax = new Vector2(.5f, 0f);
        root.pivot = new Vector2(.5f, 1f);
        root.sizeDelta = new Vector2(plateWidth, 0f);
        Read();
        GameTables.Reloaded += Read;
    }

    /// <summary>The class HUD is going: the table is no longer listened to.</summary>
    public void Dispose() => GameTables.Reloaded -= Read;

    private void Read()
    {
        below = Number("below", 6f);
        height = Mathf.Max(10f, Number("height", 18f));
        gap = Mathf.Max(0f, Number("gap", 3f));
        floor = Mathf.Max(0f, Number("floor", 8f));
        nameSize = Number("nameSize", 12f);
        timeSize = Number("timeSize", 13f);
        follow = Mathf.Max(1f, Number("follow", 14f));
        if (root != null) root.localRotation = Quaternion.Euler(0f, 0f, Number("lean", .5f));
    }

    private static float Number(string name, float fallback) => GameTables.Number(Table, Row, name, fallback);

    /// <summary>
    /// This frame's stack. <paramref name="top"/>: how high over the screen's foot the thing above it ends (px: the class
    /// plate's bottom edge, or the count line's); the stack starts its own gap under that.
    /// </summary>
    public void Show(float top, bool visible)
    {
        if (root == null) return;
        float start = top - below;
        root.anchoredPosition = new Vector2(0f, start);
        float dt = Time.unscaledDeltaTime;
        var running = TimedEffects.Running;
        foreach (var slot in slots) slot.Live = false;
        int shown = 0;
        if (visible)
            for (int i = 0; i < running.Count; i++)
                if (running[i].Kind.Shown) shown++;
        int index = 0;
        for (int i = 0; visible && i < running.Count; i++)
        {
            var entry = running[i];
            if (!entry.Kind.Shown) continue;
            var slot = Find(entry.Kind.Id) ?? Make(entry.Kind);
            slot.Live = true;
            var place = Slot(index++, shown, width, start, floor, height, gap);
            // A new slot appears in its place; one whose place changed (the one above it ended) glides there.
            Vector2 at = new Vector2(place.x, -place.y);
            if (!slot.Placed)
            {
                slot.Root.anchoredPosition = at;
                slot.Root.sizeDelta = place.size;
                slot.Placed = true;
            }
            else
            {
                float ease = 1f - Mathf.Exp(-follow * dt);
                slot.Root.anchoredPosition = Vector2.Lerp(slot.Root.anchoredPosition, at, ease);
                slot.Root.sizeDelta = Vector2.Lerp(slot.Root.sizeDelta, place.size, ease);
            }
            Fill(slot, entry);
            slot.Group.alpha = Mathf.MoveTowards(slot.Group.alpha, 1f, dt * FadeIn);
        }
        // The ones that ended fade where they stand, then go back to wait for the next effect of their kind.
        foreach (var slot in slots)
        {
            if (slot.Live) continue;
            slot.Group.alpha = Mathf.MoveTowards(slot.Group.alpha, 0f, dt * FadeOut);
            if (slot.Group.alpha <= 0f) slot.Placed = false;
        }
    }

    private Cell Find(string id)
    {
        foreach (var slot in slots)
            if (slot.Id == id) return slot;
        return null;
    }

    private void Fill(Cell slot, in TimedEffects.Entry entry)
    {
        var kind = entry.Kind;
        if (slot.Value != entry.Value || slot.Stacks != entry.Stacks || slot.Words.Length == 0)
        {
            slot.Value = entry.Value;
            slot.Stacks = entry.Stacks;
            slot.Words = kind.Name(entry.Value, entry.Stacks);
            slot.Name.text = slot.Words;
        }
        // Its time as a number, to a tenth: written only when the tenth changes.
        float tenth = Mathf.Ceil(Mathf.Max(0f, entry.Left) * 10f) / 10f;
        if (!Mathf.Approximately(tenth, slot.ShownTime))
        {
            slot.ShownTime = tenth;
            slot.Time.SetText("{0:1}", tenth);
        }
        slot.Fill.anchorMax = new Vector2(entry.Share, 1f);
        slot.Edge.color = slot.Line.color = kind.Colour;
        slot.Time.color = kind.Colour;
    }

    private Cell Make(TimedEffects.Kind kind)
    {
        var slot = new Cell { Id = kind.Id, Root = KitStyle.TopLeft("Effect " + kind.Id, root, Vector2.zero, new Vector2(width, height)) };
        slot.Group = slot.Root.gameObject.AddComponent<CanvasGroup>();
        slot.Group.interactable = slot.Group.blocksRaycasts = false;
        slot.Group.alpha = 0f;
        KitSkew.On(KitStyle.Fill(KitStyle.Stretch("Back", slot.Root), HudTone.Box), 4f);
        var edge = KitStyle.Stretch("Edge", slot.Root);
        edge.anchorMax = new Vector2(0f, 1f);
        edge.pivot = new Vector2(0f, .5f);
        edge.sizeDelta = new Vector2(3f, 0f);
        slot.Edge = KitStyle.Fill(edge, kind.Colour);
        // The line that drains with it, along the slot's foot.
        var track = KitStyle.Stretch("Track", slot.Root);
        track.anchorMax = new Vector2(1f, 0f);
        track.pivot = new Vector2(.5f, 0f);
        track.offsetMin = new Vector2(3f, 0f);
        track.offsetMax = new Vector2(0f, 2f);
        KitStyle.Fill(track, HudTone.With(HudTone.Text, .12f));
        slot.Fill = KitStyle.Stretch("Fill", track);
        slot.Line = KitStyle.Fill(slot.Fill, kind.Colour);
        float left = 8f;
        var texture = kind.Icon.Length > 0 ? KitButtons.Icon(kind.Icon) : null;
        if (texture != null)
        {
            slot.Icon = KitButtons.IconImage(slot.Root, kind.Icon, height - 6f, HudTone.Text);
            slot.Icon.rectTransform.anchorMin = slot.Icon.rectTransform.anchorMax = new Vector2(0f, .5f);
            slot.Icon.rectTransform.anchoredPosition = new Vector2(left + (height - 6f) * .5f, 1f);
            left += height - 2f;
        }
        var time = KitStyle.Stretch("Time", slot.Root);
        time.anchorMin = new Vector2(1f, 0f);
        time.pivot = new Vector2(1f, .5f);
        time.offsetMin = new Vector2(-40f, 2f);
        time.offsetMax = new Vector2(-6f, 0f);
        slot.Time = KitStyle.Label(time, "", timeSize, TextAlignmentOptions.MidlineRight, kind.Colour);
        HudTone.Apply(slot.Time);
        slot.Time.textWrappingMode = TextWrappingModes.NoWrap;
        var name = KitStyle.Stretch("Name", slot.Root);
        name.offsetMin = new Vector2(left, 2f);
        name.offsetMax = new Vector2(-44f, 0f);
        slot.Name = KitStyle.Label(name, "", nameSize, TextAlignmentOptions.MidlineLeft, HudTone.Text);
        HudTone.Apply(slot.Name);
        slot.Name.textWrappingMode = TextWrappingModes.NoWrap;
        slot.Name.overflowMode = TextOverflowModes.Ellipsis;
        slots.Add(slot);
        return slot;
    }

    // ---- Where the slots stand (a pure function: checked without the game) ----

    /// <summary>How many slots one column holds between <paramref name="start"/> (px over the screen's foot) and the floor.</summary>
    public static int Fit(float start, float floor, float height, float gap)
        => Mathf.Max(1, Mathf.FloorToInt((start - floor + gap) / (height + gap)));

    /// <summary>
    /// Slot <paramref name="index"/> of <paramref name="count"/>: its place from the stack's top left (x right, y down)
    /// and its size. They go down a column in the order they began; past what fits over the screen's foot, further
    /// columns stand beside the first and all of them share the stack's width.
    /// </summary>
    public static Rect Slot(int index, int count, float width, float start, float floor, float height, float gap)
    {
        int fit = Fit(start, floor, height, gap);
        int columns = Mathf.Max(1, (count + fit - 1) / fit);
        float column = (width - (columns - 1) * gap) / columns;
        return new Rect(index / fit * (column + gap), index % fit * (height + gap), column, height);
    }
}
