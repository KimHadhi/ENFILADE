using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The key window, opened from Settings → 조작 → 키 설정 (user, 2026-11-12: "설정에서 조작 패널에서 키 설정 창을 아예
/// 따로 만들었으면 좋겠음. 그래서 키들 싹 다 커스텀할 수 있게끔"). Every line of <see cref="KeyBindings.Entries"/> under
/// its section (이동, 전투, 무기 칸·소지품, 클래스·콜아웃, 화면), each with a first and a second key. Pressing a key slot
/// listens for the next key or mouse button (Esc keeps the old one); a right click empties the slot. A key another line
/// already uses is swapped (that line gets this slot's old key), said in the line at the bottom, and the changed slot
/// flashes. "기본" on a line puts that line back; "모두 기본값" (pressed twice) puts every key back. Keys save as they
/// change. In the settings' kit look: the menus' window with cut corners, slanted plates, black, white and one pink.
/// </summary>
[DisallowMultipleComponent]
public sealed class KeyBindingsView : MonoBehaviour
{
    private const float Width = 1060f, Height = 860f, Margin = 44f, ListTop = 154f, ListBottom = 112f, NameWidth = 330f,
        KeyWidth = 230f, ResetWidth = 64f, RowHeight = 38f, Gap = 12f, ConfirmSeconds = 3f;

    private sealed class Line
    {
        public KeyBindings.Entry Entry;
        public readonly KeyBindingCell[] Cells = new KeyBindingCell[2];
        public GameObject Reset;
    }

    private static KeyBindingsView current;

    private readonly List<Line> lines = new List<Line>();
    private TextMeshProUGUI notice, resetAllLabel;
    private KeyBindingCell listening;
    private Action closed;
    private float confirmUntil = -1f;

    /// <summary>Opens the window over everything under <paramref name="layer"/>; <paramref name="onClosed"/> runs when it goes.</summary>
    public static KeyBindingsView Open(Transform layer, Action onClosed)
    {
        if (current != null) current.Close();
        var shade = W.Rect("Key Bindings", layer);
        KitStyle.Stretch(shade);
        W.Fill(shade, new Color(0f, 0f, 0f, .72f)); // also keeps clicks off the settings beneath
        shade.SetAsLastSibling();
        var view = shade.gameObject.AddComponent<KeyBindingsView>();
        view.closed = onClosed;
        current = view;
        Deselect();
        view.Build(shade);
        return view;
    }

    /// <summary>Closes the window if it is up (the settings menu closing).</summary>
    public static void CloseIfOpen()
    {
        if (current != null) current.Close();
    }

    /// <summary>
    /// The settings menu's Esc while the window is up: when a key was being listened for, the Esc already let it go (the
    /// old key stays) and does nothing more; otherwise the window closes. True when the window took the Esc.
    /// </summary>
    public static bool TakeEscape()
    {
        if (KeyBindings.Busy) return true;
        if (current == null) return false;
        current.Close();
        return true;
    }

    public void Close()
    {
        if (current == this) current = null;
        KeyBindings.Cancel();
        var then = closed;
        closed = null;
        Destroy(gameObject);
        then?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => current = null;

    private void OnDestroy()
    {
        if (current == this) current = null;
    }

    // ---- Building ----

    private void Build(RectTransform shade)
    {
        var panel = W.Rect("Panel", shade);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.sizeDelta = new Vector2(Width, Height);
        W.Panel(panel);
        var reveal = panel.gameObject.AddComponent<KitReveal>();
        reveal.Hide();

        var title = KitStyle.TopLeft("Title", panel, new Vector2(Margin, 34f), new Vector2(420f, 64f));
        title.localRotation = Quaternion.Euler(0f, 0f, 3f);
        KitStyle.Flat(title, Texts.Get("settings.keys.title"), 48f, TextAlignmentOptions.MidlineLeft);
        var hint = KitStyle.TopLeft("Hint", panel, new Vector2(Width - Margin - 600f, 44f), new Vector2(600f, 30f));
        KitStyle.Flat(hint, Texts.Get("settings.keys.hint"), 16f, TextAlignmentOptions.MidlineRight, KitStyle.Soft).fontStyle = FontStyles.Normal;

        // The column heads over the slots, and a rule under them.
        float first = Margin + NameWidth + Gap, second = first + KeyWidth + Gap;
        Head(panel, Texts.Get("settings.keys.column.action"), Margin, NameWidth, TextAlignmentOptions.BottomLeft);
        Head(panel, Texts.Get("settings.keys.column.first"), first, KeyWidth, TextAlignmentOptions.Bottom);
        Head(panel, Texts.Get("settings.keys.column.second"), second, KeyWidth, TextAlignmentOptions.Bottom);
        KitStyle.Bar("Rule", panel, new Vector2(Margin, ListTop - 8f), new Vector2(Width - Margin * 2f, 1f), KitStyle.With(KitStyle.Ice, .1f));

        var list = KitStyle.TopLeft("List", panel, new Vector2(Margin, ListTop), new Vector2(Width - Margin * 2f, Height - ListTop - ListBottom));
        var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        var content = W.ScrollArea(list);
        string section = null;
        foreach (var entry in KeyBindings.Entries)
        {
            if (entry.Section != section)
            {
                section = entry.Section;
                Section(content, Texts.Get("settings.keys.section." + section));
            }
            lines.Add(BuildLine(content, entry));
        }
        var fixedKeys = W.Label(content, Texts.Get("settings.keys.fixed"), 16f, W.Muted);
        fixedKeys.textWrappingMode = TextWrappingModes.Normal;
        W.Size(fixedKeys, height: 64f);

        // The bottom: what just happened at the left, then every key back and closing in pink.
        KitStyle.Bar("Line", panel, new Vector2(Margin, Height - 100f), new Vector2(Width - Margin * 2f, 1f), KitStyle.With(KitStyle.Ice, .1f));
        var bottom = KitStyle.TopLeft("Buttons", panel, new Vector2(Margin, Height - 82f), new Vector2(Width - Margin * 2f, 50f));
        var row = bottom.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 12f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        notice = W.Label(bottom, "", 17f, W.Muted);
        notice.textWrappingMode = TextWrappingModes.Normal;
        notice.overflowMode = TextOverflowModes.Ellipsis;
        W.Size(notice, 0f, 48f, 1f);
        var resetAll = W.ActionButton(bottom, Texts.Get("settings.keys.reset_all"), false, ResetAll);
        W.Size(resetAll, 236f, 46f);
        resetAllLabel = resetAll.GetComponentInChildren<TextMeshProUGUI>();
        W.Size(W.ActionButton(bottom, Texts.Get("settings.keys.close"), true, Close), 176f, 46f);

        Refresh();
        Say(null);
        reveal.Open();
    }

    private static void Head(RectTransform panel, string words, float x, float width, TextAlignmentOptions alignment)
    {
        var rect = KitStyle.TopLeft("Head", panel, new Vector2(x, ListTop - 40f), new Vector2(width, 28f));
        var label = KitStyle.Flat(rect, words, 15f, alignment, KitStyle.Soft);
        label.fontStyle = FontStyles.Normal;
        label.characterSpacing = 4f;
    }

    private static void Section(Transform parent, string words)
    {
        var label = W.Label(parent, words, 16f, W.Accent);
        W.Size(label, height: 38f);
        label.alignment = TextAlignmentOptions.BottomLeft;
        label.fontStyle = FontStyles.Bold;
        label.characterSpacing = 4f;
    }

    private Line BuildLine(Transform parent, KeyBindings.Entry entry)
    {
        var line = new Line { Entry = entry };
        var row = W.Rect(entry.Id, parent);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = Gap;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        W.Size(row, height: RowHeight, flexibleHeight: 0f);
        W.Size(W.Label(row, entry.Name, 21f, W.Text), NameWidth, RowHeight);
        for (int which = 0; which < 2; which++)
            line.Cells[which] = KeyBindingCell.Build(row, line, which, KeyWidth, RowHeight - 4f, Press);
        var reset = W.ActionButton(row, Texts.Get("settings.keys.reset"), false, () =>
        {
            Deselect();
            ResetLine(line);
        });
        W.Size(reset, ResetWidth, RowHeight - 6f);
        var resetLabel = reset.GetComponentInChildren<TextMeshProUGUI>();
        resetLabel.fontSize = 15f;
        line.Reset = reset.gameObject;
        W.Size(W.Rect("Gap", row), flexibleWidth: 1f);
        return line;
    }

    // ---- Doing ----

    private void Press(KeyBindingCell cell, PointerEventData.InputButton button)
    {
        // A key is being listened for, or that ended with this very press (a mouse button being bound).
        if (KeyBindings.Busy || !(cell.Line is Line line)) return;
        if (button == PointerEventData.InputButton.Right)
        {
            KeyBindings.Clear(line.Entry, cell.Which);
            Refresh();
            Say(Texts.Format("settings.keys.cleared", line.Entry.Name));
            return;
        }
        if (button != PointerEventData.InputButton.Left) return;
        var before = Snapshot();
        listening = cell;
        if (!KeyBindings.Listen(line.Entry, cell.Which, swapped => Heard(cell, before, swapped)))
        {
            listening = null;
            return;
        }
        Deselect();
        Refresh();
        Say(Texts.Format("settings.keys.listening", line.Entry.Name));
    }

    // The listen ended: a key went in (and maybe swapped with another line), or Esc kept the old one.
    private void Heard(KeyBindingCell cell, string[] before, string swapped)
    {
        if (listening == cell) listening = null;
        if (this == null) return; // the window closed while listening
        Refresh();
        FlashChanged(before, cell);
        Say(swapped, swapped != null);
    }

    private void ResetLine(Line line)
    {
        if (KeyBindings.Busy) return;
        var before = Snapshot();
        string swapped = KeyBindings.Reset(line.Entry);
        Refresh();
        FlashChanged(before, null);
        Say(swapped ?? Texts.Format("settings.keys.reset_done", line.Entry.Name), swapped != null);
    }

    // Every key back to its default: asked twice, since it throws away every key the player set.
    private void ResetAll()
    {
        Deselect();
        if (KeyBindings.Busy) return;
        if (Time.unscaledTime > confirmUntil)
        {
            confirmUntil = Time.unscaledTime + ConfirmSeconds;
            resetAllLabel.text = Texts.Get("settings.keys.reset_all.confirm");
            Say(Texts.Get("settings.keys.reset_all.ask"), true);
            return;
        }
        confirmUntil = -1f;
        resetAllLabel.text = Texts.Get("settings.keys.reset_all");
        var before = Snapshot();
        KeyBindings.ResetAll();
        Refresh();
        FlashChanged(before, null);
        Say(Texts.Get("settings.keys.reset_all.done"));
    }

    private void Update()
    {
        if (confirmUntil > 0f && Time.unscaledTime > confirmUntil)
        {
            confirmUntil = -1f;
            resetAllLabel.text = Texts.Get("settings.keys.reset_all");
            Say(null);
        }
    }

    // ---- Showing ----

    private void Refresh()
    {
        string listenWords = Texts.Get("settings.keys.press");
        foreach (var line in lines)
        {
            var action = KeyBindings.Find(line.Entry.Action);
            for (int which = 0; which < 2; which++)
            {
                int index = KeyBindings.Slot(line.Entry, which);
                var cell = line.Cells[which];
                cell.gameObject.SetActive(index >= 0);
                if (index < 0) continue;
                cell.Show(KeyBindings.KeyName(action, index), KeyBindings.IsDefault(action, index), cell == listening, listenWords);
            }
            line.Reset.SetActive(!KeyBindings.IsDefault(line.Entry));
        }
    }

    // What every slot holds now, to tell after a change which others it moved.
    private string[] Snapshot()
    {
        var paths = new string[lines.Count * 2];
        for (int i = 0; i < lines.Count; i++)
        {
            var action = KeyBindings.Find(lines[i].Entry.Action);
            for (int which = 0; which < 2; which++)
                paths[i * 2 + which] = KeyBindings.PathOf(action, KeyBindings.Slot(lines[i].Entry, which));
        }
        return paths;
    }

    // Flashes each slot that a change moved, but not the one the player just set (it is plain to see already).
    private void FlashChanged(string[] before, KeyBindingCell skip)
    {
        var after = Snapshot();
        for (int i = 0; i < lines.Count; i++)
            for (int which = 0; which < 2; which++)
            {
                var cell = lines[i].Cells[which];
                if (cell != skip && cell.gameObject.activeSelf && !string.Equals(before[i * 2 + which], after[i * 2 + which],
                        StringComparison.OrdinalIgnoreCase))
                    cell.Flash();
            }
    }

    // The line at the bottom: what just happened (pink when a key moved from another line), else the standing hint.
    private void Say(string words, bool loud = false)
    {
        if (notice == null) return;
        bool idle = string.IsNullOrEmpty(words);
        notice.text = idle ? Texts.Format("settings.keys.status", KeyBindings.ChangedCount()) : words;
        notice.color = !idle && loud ? KitStyle.Accent : W.Muted;
    }

    // A button that keeps the selection would take Enter or Space as a click later, while a key is wanted.
    private static void Deselect()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }
}
