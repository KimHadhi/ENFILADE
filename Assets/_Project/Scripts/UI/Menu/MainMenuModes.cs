using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The title screen's ways to play, stacked down the left like an FPS lobby's playlists: big slanted plates, each at its
/// own small tilt and indent, with a rail beside them whose pink marker follows the pointer (KitRail). Kept quiet (the
/// user's "too mobile" note, 2026-11-09): the chosen one is a dark plate with a thin pink edge and a pink bar down its
/// slanted left side, and says "다시 누르면 시작"; the others are faint see-through plates with a grey bar. Nothing bobs.
/// Pressing another one chooses it (the launch button then says what it will start); pressing the chosen one again
/// launches it. Everything under <see cref="Root"/> opens with <see cref="Reveal"/>. Each has its own mark as well (its
/// icon large and faint at the right, and a chip saying what sets it apart: offline, how many can join, LAN or the
/// internet), and they stack by KitStack so that, leaning their own ways, they never lie over each other and keep room
/// between them at their nearest (방 만들기 lay over 서버 찾기; user, 2026-11-11: "각자 포인트랑 특색은 주면서 겹치거나 너무
/// 여유 없이 좁아 보이진 않게").
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuModes : MonoBehaviour
{
    public enum Mode { Solo, Host, Browse }

    public const float Width = 560f;
    // Gap: the least room between two plates where they come nearest (KitStack), not a fixed step.
    private const float Height = 92f, Gap = 24f, Slant = 18f, BlendSeconds = .15f, IconSize = 66f, ChipWidth = 128f, ChipHeight = 22f;
    // Each slot's indent, width and tilt (degrees): every plate leans its own way.
    private static readonly float[] Indents = { 0f, 30f, 12f }, Widths = { 560f, 480f, 440f }, Tilts = { 2.5f, -1.2f, 1.5f };
    private static readonly Color Clear = KitStyle.With(KitStyle.Ice, .05f), Dark = KitStyle.With(KitStyle.Night, .92f),
        Grey = KitStyle.With(KitStyle.Ice, .35f);

    private sealed class Entry
    {
        public Mode Mode;
        public RectTransform Root, Body;
        public Image Edge, Plate, Bar, Lid, Chip;
        public RawImage Icon;
        public TMP_Text Title, Note, Again, Tag;
        public float Chosen;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private Action<Mode> changed, launched;
    private KitRail rail;
    private int chosen;

    public RectTransform Root { get; private set; }
    public KitReveal Reveal { get; private set; }
    public Mode Chosen => entries.Count > 0 ? entries[chosen].Mode : Mode.Solo;
    /// <summary>Where the plates end (px down from the top of Root), for what goes under them.</summary>
    public float Bottom { get; private set; }

    /// <param name="items">Each way to play, its name and the small line under it, top to bottom.</param>
    /// <param name="height">How tall the block is (things can hang at its foot).</param>
    public static MainMenuModes Make(RectTransform canvas, Vector2 at, float height, IList<(Mode mode, string title, string note)> items,
        Action<Mode> changed, Action<Mode> launched)
    {
        var root = KitStyle.TopLeft("Modes", canvas, at, new Vector2(Width, height));
        var modes = root.gameObject.AddComponent<MainMenuModes>();
        modes.Root = root;
        modes.changed = changed;
        modes.launched = launched;
        modes.Reveal = root.gameObject.AddComponent<KitReveal>();
        modes.Reveal.Hide();
        modes.Build(items);
        return modes;
    }

    private void Build(IList<(Mode mode, string title, string note)> items)
    {
        float y = 0f, at = .1f, lastX = 0f, lastWidth = 0f, lastTilt = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            int slot = Mathf.Min(i, Widths.Length - 1), index = i;
            float width = Widths[slot], x = Indents[slot], tilt = Tilts[slot];
            if (i > 0) y = KitStack.Below(lastX, y, lastWidth, Height, lastTilt, x, width, tilt, Gap);
            var root = KitStyle.TopLeft("Mode " + items[i].title, Root, new Vector2(x, y), new Vector2(width, Height));
            root.localRotation = Quaternion.Euler(0f, 0f, tilt);
            // The see-through image that takes the plate's clicks leans as the plate does (KitSkew's hit shape), so the
            // pointer picks the plate it is drawn on, not the square round it (user, 2026-11-12).
            var hit = KitStyle.Fill(root, Color.clear);
            hit.raycastTarget = true;
            KitSkew.On(hit, Slant);
            var body = KitStyle.Stretch("Body", root);
            // A thin pink edge round the chosen plate: a slightly larger plate behind it.
            var edgeRect = KitStyle.Stretch("Edge", body);
            edgeRect.sizeDelta = new Vector2(2f, 2f);
            var edge = KitStyle.Fill(edgeRect, Color.clear);
            KitSkew.On(edge, Slant);
            var plate = KitStyle.Fill(KitStyle.Stretch("Plate", body), Clear);
            KitSkew.On(plate, Slant);
            var bar = KitStyle.Bar("Bar", body, Vector2.zero, new Vector2(4f, Height), Grey);
            KitSkew.On(bar, Slant);
            var title = KitStyle.Flat(KitStyle.TopLeft("Title", body, new Vector2(30f, 8f), new Vector2(width - 60f, 52f)), items[i].title, 40f,
                TextAlignmentOptions.MidlineLeft);
            title.fontStyle = FontStyles.Normal;
            var note = KitStyle.Flat(KitStyle.TopLeft("Note", body, new Vector2(32f, 60f), new Vector2(width - 60f, 22f)), items[i].note, 15f,
                TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
            note.fontStyle = FontStyles.Normal;
            // Its own mark: its icon large and faint toward the right, under the chip and the words.
            var icon = KitButtons.IconImage(body, IconOf(items[i].mode), IconSize, Color.clear);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(1f, .5f);
            icon.rectTransform.anchoredPosition = new Vector2(-IconSize * .5f - 34f, 0f);
            icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -6f);
            // ...and a chip at the top right saying what sets it apart.
            var chipRect = KitStyle.TopLeft("Chip", body, new Vector2(width - 34f - ChipWidth, 12f), new Vector2(ChipWidth, ChipHeight));
            var chip = KitStyle.Fill(chipRect, Color.clear);
            KitSkew.On(chip, 8f);
            var tag = KitStyle.Flat(chipRect, TagOf(items[i].mode), 13f, TextAlignmentOptions.Center, KitStyle.Soft);
            tag.fontStyle = FontStyles.Normal;
            var again = KitStyle.Flat(KitStyle.TopLeft("Again", body, new Vector2(width - 234f, 52f), new Vector2(200f, 30f)), Texts.Get("menu.mode.again"), 14f,
                TextAlignmentOptions.MidlineRight, KitStyle.With(KitStyle.Accent, 0f));
            again.fontStyle = FontStyles.Normal;
            var lid = KitStyle.Fill(KitStyle.Stretch("Flash", body), KitStyle.Flash);
            KitSkew.On(lid, Slant);
            Reveal.Card(root, lid, at);
            at += .07f;

            var button = root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => Press(index));
            root.gameObject.AddComponent<KitHover>();
            entries.Add(new Entry { Mode = items[i].mode, Root = root, Body = body, Edge = edge, Plate = plate, Bar = bar, Lid = lid, Chip = chip,
                Icon = icon, Title = title, Note = note, Again = again, Tag = tag });
            lastX = x; lastWidth = width; lastTilt = tilt;
        }
        Bottom = items.Count > 0 ? KitStack.Lowest(y, lastWidth, Height, lastTilt) : 0f;
        rail = KitRail.Make(Root, Root, -26f, 0f, Bottom);
        Reveal.Line((RectTransform)rail.transform, true, 0f, .35f);
        Choose(0, false);
        foreach (var entry in entries) Paint(entry, entry.Chosen);
    }

    /// <summary>Puts the rail's marker on the chosen plate (after the block opens).</summary>
    public void PointRail() { if (entries.Count > 0) rail.Point(entries[chosen].Root, true); }

    private void Press(int index)
    {
        if (index == chosen) { launched?.Invoke(entries[index].Mode); KitPulse.Play(entries[index].Lid, new Color(1f, 1f, 1f, .55f), .3f); return; }
        Choose(index, true);
    }

    private void Choose(int index, bool flash)
    {
        if (entries.Count == 0) return;
        chosen = Mathf.Clamp(index, 0, entries.Count - 1);
        if (flash)
        {
            KitPulse.Play(entries[chosen].Lid, KitStyle.With(KitStyle.Accent, .45f), .35f);
            rail.Point(entries[chosen].Root, false);
        }
        else entries[chosen].Chosen = 1f;
        changed?.Invoke(entries[chosen].Mode);
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            float goal = i == chosen ? 1f : 0f;
            if (!Mathf.Approximately(entry.Chosen, goal))
            {
                entry.Chosen = Mathf.MoveTowards(entry.Chosen, goal, dt / BlendSeconds);
                Paint(entry, entry.Chosen);
            }
        }
    }

    private static void Paint(Entry entry, float chosen)
    {
        entry.Edge.color = KitStyle.With(KitStyle.Accent, .7f * chosen);
        entry.Plate.color = Color.Lerp(Clear, Dark, chosen);
        entry.Bar.color = Color.Lerp(Grey, KitStyle.Accent, chosen);
        entry.Title.color = Color.Lerp(KitStyle.Soft, KitStyle.Text, .4f + .6f * chosen);
        entry.Again.color = KitStyle.With(KitStyle.Accent, .85f * chosen);
        // Chosen, its icon warms to pink behind the words and its chip turns solid pink with dark type.
        entry.Icon.color = Color.Lerp(KitStyle.With(KitStyle.Ice, .07f), KitStyle.With(KitStyle.Accent, .22f), chosen);
        entry.Chip.color = Color.Lerp(KitStyle.With(KitStyle.Ice, .09f), KitStyle.With(KitStyle.Accent, .9f), chosen);
        entry.Tag.color = Color.Lerp(KitStyle.Soft, KitStyle.Ink, chosen);
    }

    // Each way to play's icon (Resources/UI/Icons).
    private static string IconOf(Mode mode) => mode == Mode.Host ? "Room" : mode == Mode.Browse ? "Servers" : "Profile";

    // What sets each apart, in a few words (Texts.csv).
    private static string TagOf(Mode mode) => mode == Mode.Host ? Texts.Get("menu.mode.host.tag")
        : mode == Mode.Browse ? Texts.Get("menu.mode.browse.tag") : Texts.Get("menu.mode.solo.tag");
}
