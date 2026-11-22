using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The line under the class plate that says what this player has standing in the world: a chip a kind (Deployables'
/// rows: a Support's planks, wire, ammo crate, searchlight and claymores, and whatever a mod adds), each with how many
/// stand and how many may ("1/2"). The user, 2026-11-18: "추가 설치 시 기존 설치물이 소멸하는 기준을 파악할 수 있도록 현재
/// 설치 개수/최대 제한 현황 표시". Before, the class plate's note gave the count of the one kind in hand, hid it while a
/// barricade was being placed (the very moment it matters), and the mines had none.
/// <para>
/// A kind with as many standing as may stand reads in the addon HUD's orange (HudTone.Loss), near the amber the preview
/// blinks over the oldest one in the world (BarricadeGhost's, ClaymorePreview's): the next one set down takes that one
/// away. The kind being placed now has a white edge on top, and its orange breathes while it is full. A count that
/// changes pops. Every kind the player holds shows, placing or not, and one they no longer hold shows dim for as long as
/// any of it still stands. Its surfaces are the addon HUD's (HudTone); its shapes and motion are its own.
/// </para>
/// It stands in the flow under the class plate: ClassHUD puts whatever comes next (the timed effects) under Reach.
/// The chips' places are a pure function of how many there are (Slot), so the line can be checked without playing.
/// Numbers: Tables/ClassHud, rows Deploy.*. This PC's own player only.
/// </summary>
public sealed class DeployStrip
{
    public const string Table = "ClassHud", Row = "Deploy";
    /// <summary>The colour of the one that goes next in the world: the amber the previews blink over it (ClaymoreMine's).</summary>
    public static readonly Color Amber = new Color(1f, .62f, .18f);
    private const float PopSeconds = .22f;

    private sealed class Chip
    {
        public RectTransform Root, FootFill;
        public Image Top, Foot;
        public TextMeshProUGUI Name, Count;
        public string Kind = "";
        public int Shown = -1, Most = -1;
        public float PopAge = 1f;
    }

    private readonly RectTransform root;
    private readonly CanvasGroup group;
    private readonly List<Chip> chips = new List<Chip>();
    private readonly List<Deployables.Line> lines = new List<Deployables.Line>();
    private readonly float width, plateBottom;
    private float below, height, least, most, gap, nameSize, countSize;

    /// <summary>
    /// How far under the class plate's bottom edge it reaches now (px; 0 while it shows nothing): what stands under it in
    /// the flow starts there.
    /// </summary>
    public float Reach { get; private set; }

    /// <param name="canvas">The class HUD's canvas: it is a piece of its own there (the overlap check measures it apart).</param>
    /// <param name="plateWidth">The class plate's width (px): the line is as wide.</param>
    /// <param name="plateBottom">The class plate's bottom edge over the screen's (px).</param>
    public DeployStrip(Transform canvas, float plateWidth, float plateBottom)
    {
        width = plateWidth;
        this.plateBottom = plateBottom;
        root = FeelUI.Rect("Deployables", canvas);
        root.anchorMin = root.anchorMax = new Vector2(.5f, 0f);
        root.pivot = new Vector2(.5f, 1f);
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        group.alpha = 0f;
        Read();
        GameTables.Reloaded += Read;
    }

    /// <summary>The class HUD is going: the table is no longer listened to.</summary>
    public void Dispose() => GameTables.Reloaded -= Read;

    // The table's numbers, read as the line is made and whenever a table is saved while playing.
    private void Read()
    {
        below = Number("below", 11f);
        height = Mathf.Max(12f, Number("height", 30f));
        least = Mathf.Max(20f, Number("chip", 56f));
        most = Mathf.Max(least, Number("chipMost", 92f));
        gap = Mathf.Max(0f, Number("gap", 4f));
        nameSize = Number("nameSize", 10f);
        countSize = Number("countSize", 14f);
        if (root == null) return;
        root.anchoredPosition = new Vector2(0f, plateBottom - below);
        root.sizeDelta = new Vector2(width, height);
        // Its own small lean, the other way from the plate's (the HUD's pieces each lean their own way and never touch:
        // the gap under the plate holds both leans' rise at the ends).
        root.localRotation = Quaternion.Euler(0f, 0f, Number("lean", -1f));
        foreach (var chip in chips) chip.Shown = chip.Most = -1;
    }

    private static float Number(string name, float fallback) => GameTables.Number(Table, Row, name, fallback);

    /// <summary>
    /// This frame's line for <paramref name="owner"/>: every kind they hold or have standing. <paramref name="chosen"/>:
    /// the kind being placed now ("" for none).
    /// </summary>
    public void Show(PlayerMovement owner, string chosen, bool visible)
    {
        if (root == null) return;
        chosen ??= "";
        Deployables.Readout(visible ? owner : null, lines);
        bool any = lines.Count > 0;
        float dt = Time.unscaledDeltaTime;
        group.alpha = Mathf.MoveTowards(group.alpha, any ? 1f : 0f, dt * 8f);
        if (!any)
        {
            if (group.alpha <= 0f) Reach = 0f;
            return;
        }
        while (chips.Count < lines.Count) chips.Add(Make());
        int rows = Rows(lines.Count, width, least, gap);
        Reach = below + rows * height + (rows - 1) * gap;
        // The breath of the one that goes next: smooth, slow, never down to nothing (the user's rule: no strobes).
        float breath = .775f + .225f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI * 1.2f);
        for (int i = 0; i < chips.Count; i++)
        {
            var chip = chips[i];
            bool on = i < lines.Count;
            if (chip.Root.gameObject.activeSelf != on) chip.Root.gameObject.SetActive(on);
            if (!on) continue;
            var line = lines[i];
            var slot = Slot(i, lines.Count, width, least, most, gap, height);
            chip.Root.anchoredPosition = new Vector2(slot.x, -slot.y);
            chip.Root.sizeDelta = slot.size;
            if (chip.Kind != line.Kind.Id)
            {
                chip.Kind = line.Kind.Id;
                chip.Name.text = line.Kind.Short;
                chip.Shown = chip.Most = -1;
            }
            if (chip.Shown != line.Count || chip.Most != line.Most)
            {
                // A count that changed pops (not the first one it shows).
                if (chip.Shown >= 0 && chip.Shown != line.Count) chip.PopAge = 0f;
                chip.Shown = line.Count;
                chip.Most = line.Most;
                chip.Count.SetText("{0}/{1}", line.Count, line.Most);
            }
            chip.PopAge += dt;
            chip.Count.rectTransform.localScale = Vector3.one * (1f + .16f * (1f - Mathf.Clamp01(chip.PopAge / PopSeconds)));
            bool picked = chosen.Length > 0 && string.Equals(chosen, line.Kind.Id, System.StringComparison.OrdinalIgnoreCase);
            float held = line.Held ? 1f : .4f;
            chip.Name.color = HudTone.With(HudTone.Text, (picked ? .95f : .6f) * held);
            Color count = line.Full ? HudTone.With(HudTone.Loss, picked ? breath : 1f) : HudTone.Text;
            count.a *= held;
            chip.Count.color = count;
            chip.Top.color = picked ? HudTone.Text : Color.clear;
            // The foot: how full, as a bar; orange once full.
            chip.FootFill.anchorMax = new Vector2(line.Most > 0 ? Mathf.Clamp01((float)line.Count / line.Most) : 0f, 1f);
            chip.Foot.color = line.Full ? HudTone.With(HudTone.Loss, held) : HudTone.With(HudTone.Text, .55f * held);
        }
    }

    private Chip Make()
    {
        var chip = new Chip { Root = KitStyle.TopLeft("Chip", root, Vector2.zero, new Vector2(least, height)) };
        KitSkew.On(KitStyle.Fill(KitStyle.Stretch("Back", chip.Root), HudTone.Box), 4f);
        var top = KitStyle.Stretch("Top", chip.Root);
        top.anchorMin = new Vector2(0f, 1f);
        top.anchorMax = Vector2.one;
        top.pivot = new Vector2(.5f, 1f);
        top.sizeDelta = new Vector2(0f, 2f);
        chip.Top = KitStyle.Fill(top, Color.clear);
        var track = KitStyle.Stretch("Foot", chip.Root);
        track.anchorMin = Vector2.zero;
        track.anchorMax = new Vector2(1f, 0f);
        track.pivot = new Vector2(.5f, 0f);
        track.sizeDelta = new Vector2(0f, 3f);
        KitStyle.Fill(track, HudTone.With(HudTone.Text, .12f));
        chip.FootFill = KitStyle.Stretch("Fill", track);
        chip.Foot = KitStyle.Fill(chip.FootFill, HudTone.Text);
        // The name small over the count: a chip is too narrow for both on one line.
        var name = KitStyle.Stretch("Name", chip.Root);
        name.anchorMin = new Vector2(0f, .5f);
        name.offsetMin = new Vector2(5f, 0f);
        name.offsetMax = new Vector2(-3f, -2f);
        chip.Name = KitStyle.Label(name, "", nameSize, TextAlignmentOptions.TopLeft, HudTone.Grey);
        HudTone.Apply(chip.Name);
        chip.Name.textWrappingMode = TextWrappingModes.NoWrap;
        chip.Name.overflowMode = TextOverflowModes.Ellipsis;
        var count = KitStyle.Stretch("Count", chip.Root);
        count.anchorMax = new Vector2(1f, .62f);
        count.offsetMin = new Vector2(5f, 4f);
        count.offsetMax = new Vector2(-3f, 0f);
        chip.Count = KitStyle.Label(count, "", countSize, TextAlignmentOptions.BottomLeft, HudTone.Text);
        HudTone.Apply(chip.Count);
        chip.Count.textWrappingMode = TextWrappingModes.NoWrap;
        return chip;
    }

    // ---- Where the chips stand (a pure function: checked without the game) ----

    /// <summary>How many rows <paramref name="count"/> chips take in a line this wide.</summary>
    public static int Rows(int count, float width, float least, float gap)
    {
        int perRow = PerRow(width, least, gap);
        return count <= 0 ? 0 : (count + perRow - 1) / perRow;
    }

    private static int PerRow(float width, float least, float gap) => Mathf.Max(1, Mathf.FloorToInt((width + gap) / (least + gap)));

    /// <summary>
    /// Chip <paramref name="index"/> of <paramref name="count"/>: its place from the line's top left (x right, y down)
    /// and its size. As many to a row as fit at their least width; a row's chips share its width, none wider than
    /// <paramref name="most"/>; further rows go under the first.
    /// </summary>
    public static Rect Slot(int index, int count, float width, float least, float most, float gap, float height)
    {
        int perRow = PerRow(width, least, gap), rows = Rows(count, width, least, gap);
        int row = index / perRow, column = index % perRow;
        int inRow = row < rows - 1 ? perRow : count - perRow * (rows - 1);
        float chip = Mathf.Min(most, (width - (inRow - 1) * gap) / Mathf.Max(1, inRow));
        return new Rect(column * (chip + gap), row * (height + gap), chip, height);
    }
}
