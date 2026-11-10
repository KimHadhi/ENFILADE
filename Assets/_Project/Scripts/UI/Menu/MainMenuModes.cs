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
/// launches it. Everything under <see cref="Root"/> opens with <see cref="Reveal"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuModes : MonoBehaviour
{
    public enum Mode { Solo, Host, Browse }

    public const float Width = 560f;
    private const float Height = 92f, Gap = 18f, Slant = 18f, BlendSeconds = .15f;
    // Each slot's indent, width and tilt (degrees): every plate leans its own way.
    private static readonly float[] Indents = { 0f, 30f, 12f }, Widths = { 560f, 470f, 430f }, Tilts = { 3f, -1.5f, 1f };
    private static readonly Color Clear = KitStyle.With(KitStyle.Ice, .05f), Dark = KitStyle.With(KitStyle.Night, .92f),
        Grey = KitStyle.With(KitStyle.Ice, .35f);

    private sealed class Entry
    {
        public Mode Mode;
        public RectTransform Root, Body;
        public Image Edge, Plate, Bar, Lid;
        public TMP_Text Title, Note, Again;
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
        float y = 0f, at = .1f;
        for (int i = 0; i < items.Count; i++)
        {
            int slot = Mathf.Min(i, Widths.Length - 1), index = i;
            float width = Widths[slot];
            var root = KitStyle.TopLeft("Mode " + items[i].title, Root, new Vector2(Indents[slot], y), new Vector2(width, Height));
            root.localRotation = Quaternion.Euler(0f, 0f, Tilts[slot]);
            KitStyle.Fill(root, Color.clear).raycastTarget = true;
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
            var again = KitStyle.Flat(KitStyle.TopLeft("Again", body, new Vector2(width - 230f, 0f), new Vector2(200f, Height)), Texts.Get("menu.mode.again"), 14f,
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
            entries.Add(new Entry { Mode = items[i].mode, Root = root, Body = body, Edge = edge, Plate = plate, Bar = bar, Lid = lid, Title = title,
                Note = note, Again = again });
            y += Height + Gap;
        }
        Bottom = y - Gap;
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
    }
}
