using System;
using System.Collections.Generic;
using HudAddons;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The HUD settings (the user, 2026-11-22: "이 hud 설정하는 창 글만 보고선 이해할 수가 없는데 직관적이고 바로 볼 수 있게 좀
/// hud를 위한 설정창을 만들어 봐"; the HUD tab was a list of words and numbers as long as the addons' panels). Opened by the
/// settings' HUD tab, it clears the screen down to the HUD itself, drawn in its place with sample numbers (HudStage), over
/// the game, or over a map's picture on the title screen:
/// <list type="bullet">
/// <item>along the top the kinds of thing (전체, 내 상태, 무기, 전투, 맞을 때, 임무) and under them the parts of the one
/// chosen, a part that is off faint;</item>
/// <item>on the HUD, pointing at a part frames it with its name, pressing chooses it, dragging moves it, the arrow keys move
/// the chosen one by one (ten with Shift);</item>
/// <item>at the side away from the chosen part, its page (HudSettingsPages): what it is in a line, its few settings (a
/// switch, pictures to choose by, its place, its colour), its defaults. A change is on the HUD at once, and the part moves
/// as it does in play while it is chosen.</item>
/// </list>
/// Esc or 돌아가기 goes back to the settings.
/// </summary>
[DisallowMultipleComponent]
public sealed class HudSettingsView : MonoBehaviour
{
    public const float BarHeight = 116f;
    private const float Edge = 24f, PageWide = 480f, PageMost = 616f, HeadTall = 96f, FootTall = 66f, ConfirmSeconds = 3f, FirstRepeat = .35f, Repeat = .035f;

    private static HudSettingsView current;
    private static HudPart last = HudPart.Health; // reopening shows the part last set

    private readonly List<HudMenu.Entry> groups = new List<HudMenu.Entry>();
    private readonly List<(HudPart part, HudMenu.Entry entry)> chips = new List<(HudPart, HudMenu.Entry)>();
    private readonly List<HudStageFrame> frames = new List<HudStageFrame>();
    private Action closed;
    private CanvasGroup group;
    private RectTransform root, chipRow, page, body, content;
    private TextMeshProUGUI resetAllLabel;
    private GameObject backdrop;
    private HudPart chosen;
    private float peekUntil, confirmUntil = -1f, nudgeAt;
    private int builtGroup = -1;

    /// <summary>Shows the chosen part's place again in its page's boxes (HudPageRows.Place), after a drag.</summary>
    public Action PlaceShown { get; set; }

    /// <summary>Opens over everything under <paramref name="layer"/>; <paramref name="onClosed"/> runs when it goes.</summary>
    public static HudSettingsView Open(Transform layer, Action onClosed)
    {
        if (current != null) current.Close();
        var root = W.Rect("HUD Settings", layer);
        KitStyle.Stretch(root);
        root.SetAsLastSibling();
        var view = root.gameObject.AddComponent<HudSettingsView>();
        view.root = root;
        view.closed = onClosed;
        current = view;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        view.Build();
        return view;
    }

    /// <summary>Closes it if it is up (the settings menu closing).</summary>
    public static void CloseIfOpen()
    {
        if (current == null) return;
        current.closed = null;
        current.Close();
    }

    /// <summary>The settings menu's Esc while it is up: back to the settings. True when it took the Esc.</summary>
    public static bool TakeEscape()
    {
        if (current == null) return false;
        current.Close();
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => current = null;

    private void Close()
    {
        if (current == this) current = null;
        ColourPanel.Close();
        HudStage.Close();
        if (backdrop != null) Destroy(backdrop);
        var then = closed;
        closed = null;
        Destroy(gameObject);
        then?.Invoke();
    }

    private void OnDestroy()
    {
        if (current != this) return;
        current = null;
        HudStage.Close();
        if (backdrop != null) Destroy(backdrop);
    }

    // ---- Building ----

    private void Build()
    {
        group = gameObject.AddComponent<CanvasGroup>();
        W.Fill(root, Color.clear); // keeps presses off whatever is under the HUD
        HudStage.Open(last);
        if (PlayerRoster.Local == null) MakeBackdrop();

        var stage = W.Rect("Stage", root);
        KitStyle.Stretch(stage);
        foreach (var part in HudSettingsPages.Pressable) frames.Add(HudStageFrame.Make(stage, this, part, HudSettingsPages.Name(part)));

        BuildBar();
        page = W.Rect("Page", root);
        page.gameObject.AddComponent<HudMenuHost>();
        var host = page.GetComponent<HudMenuHost>();
        host.LabelWidth = 164f;
        host.RowHeight = 42f;
        host.BoxWidth = 72f;
        host.LabelSize = 17f;
        Show(last);
    }

    // The title screen has no game behind it: a map's picture over the title, under the HUD.
    private void MakeBackdrop()
    {
        backdrop = new GameObject("HUD Settings Backdrop", typeof(RectTransform), typeof(Canvas));
        var canvas = backdrop.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = HudStage.BackdropOrder;
        var black = KitStyle.Stretch("Black", backdrop.transform);
        W.Fill(black, new Color(.1f, .1f, .11f));
        Texture picture = null;
        foreach (string map in MapTable.Playable())
        {
            picture = MapTable.Picture(map);
            if (picture != null) break;
        }
        if (picture == null) return;
        var image = KitStyle.Stretch("Picture", backdrop.transform).gameObject.AddComponent<RawImage>();
        image.texture = picture;
        image.color = new Color(.75f, .75f, .75f);
        image.raycastTarget = false;
        // The whole screen, the picture's own shape kept, cut at its sides or its top and bottom.
        float screen = Screen.width / Mathf.Max(1f, Screen.height), shape = picture.width / Mathf.Max(1f, picture.height);
        image.uvRect = shape > screen ? new Rect((1f - screen / shape) * .5f, 0f, screen / shape, 1f) : new Rect(0f, (1f - shape / screen) * .5f, 1f, shape / screen);
    }

    private void BuildBar()
    {
        var bar = W.Rect("Bar", root);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = Vector2.one;
        bar.pivot = new Vector2(.5f, 1f);
        bar.sizeDelta = new Vector2(0f, BarHeight);
        bar.anchoredPosition = Vector2.zero;
        var ground = W.Fill(bar, HudMenu.Ground);
        ground.raycastTarget = true;
        var foot = W.Rect("Foot", bar);
        foot.anchorMin = Vector2.zero;
        foot.anchorMax = new Vector2(1f, 0f);
        foot.pivot = new Vector2(.5f, 0f);
        foot.sizeDelta = new Vector2(0f, 1f);
        W.Fill(foot, HudMenu.Line).raycastTarget = false;

        var title = W.Label(KitStyle.TopLeft("Title", bar, new Vector2(Edge, 12f), new Vector2(170f, 46f)), HudPageRows.T("title"), 27f, HudMenu.Text);
        KitStyle.Stretch(title.rectTransform);
        title.fontStyle = FontStyles.Bold;
        float x = Edge + 178f;
        for (int i = 0; i < HudSettingsPages.Groups.Length; i++)
        {
            int index = i;
            var rect = KitStyle.TopLeft("Group " + HudSettingsPages.Groups[i].id, bar, new Vector2(x, 13f), new Vector2(118f, 44f));
            groups.Add(new HudMenu.Entry(rect, HudPageRows.T("group." + HudSettingsPages.Groups[i].id), 20f, () => Choose(HudSettingsPages.Groups[index].parts[0]),
                TextAlignmentOptions.Center));
            x += 124f;
        }
        chipRow = KitStyle.TopLeft("Parts", bar, new Vector2(Edge, 64f), new Vector2(1500f, 42f));
        var layout = chipRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        var right = W.Rect("Right", bar);
        right.anchorMin = right.anchorMax = right.pivot = Vector2.one;
        right.anchoredPosition = new Vector2(-Edge, -12f);
        right.sizeDelta = new Vector2(600f, 46f);
        var row = right.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 10f;
        row.childAlignment = TextAnchor.MiddleRight;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        var reset = W.ActionButton(right, HudPageRows.T("reset_all"), false, ResetAll);
        W.Size(reset, 250f, 42f);
        resetAllLabel = reset.GetComponentInChildren<TextMeshProUGUI>();
        W.Size(W.ActionButton(right, Texts.Get("settings.resume"), true, Close), 170f, 42f);

        var hint = W.Rect("Hint", bar);
        hint.anchorMin = hint.anchorMax = hint.pivot = Vector2.one;
        hint.anchoredPosition = new Vector2(-Edge, -64f);
        hint.sizeDelta = new Vector2(700f, 42f);
        KitStyle.Stretch(W.Label(hint, HudPageRows.T("hint"), 17f, HudMenu.Muted, TextAlignmentOptions.MidlineRight).rectTransform);
    }

    // ---- Choosing ----

    /// <summary>The part is set from now on: its group and its page come up.</summary>
    public void Choose(HudPart part)
    {
        // Nothing keeps the keyboard, so the arrow keys move the part and not a button.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (part == chosen && page.childCount > 0) return;
        ColourPanel.Close();
        Show(part);
    }

    private void Show(HudPart part)
    {
        chosen = last = part;
        HudStage.Chosen = part;
        int at = HudSettingsPages.GroupOf(part);
        for (int i = 0; i < groups.Count; i++) groups[i].Show(i == at);
        if (at != builtGroup) BuildChips(at);
        PaintChips();
        BuildPage();
    }

    private void BuildChips(int at)
    {
        builtGroup = at;
        W.Clear(chipRow);
        chips.Clear();
        foreach (var part in HudSettingsPages.Groups[at].parts)
        {
            var shown = part;
            var rect = W.Rect("Part " + part, chipRow);
            string name = HudSettingsPages.Name(part);
            var entry = new HudMenu.Entry(rect, name, 18f, () => Choose(shown), TextAlignmentOptions.Center);
            W.Size(rect, rect.GetComponentInChildren<TextMeshProUGUI>().GetPreferredValues(name).x + 40f, 38f);
            chips.Add((part, entry));
        }
    }

    private void PaintChips()
    {
        foreach (var (part, entry) in chips) entry.Show(part == chosen, !HudSettingsPages.IsOn(part));
    }

    /// <summary>A setting changed: what is off shows faint in the list.</summary>
    public void Changed() => PaintChips();

    private void BuildPage()
    {
        PlaceShown = null;
        W.Clear(page);
        // At the side away from the part, so it is never under its own page.
        bool left = HudSettingsPages.AtRight(chosen);
        page.anchorMin = page.anchorMax = page.pivot = new Vector2(left ? 0f : 1f, 1f);
        page.anchoredPosition = new Vector2(left ? Edge : -Edge, -(BarHeight + 16f));
        page.sizeDelta = new Vector2(PageWide, PageMost);

        var ground = W.Rect("Ground", page);
        KitStyle.Stretch(ground);
        HudMenu.Panel(ground);
        var name = W.Label(KitStyle.TopLeft("Name", page, new Vector2(24f, 16f), new Vector2(PageWide - 48f, 34f)), HudSettingsPages.Name(chosen), 25f, HudMenu.Text);
        KitStyle.Stretch(name.rectTransform);
        name.fontStyle = FontStyles.Bold;
        var about = W.Label(KitStyle.TopLeft("About", page, new Vector2(24f, 50f), new Vector2(PageWide - 48f, 42f)), HudSettingsPages.About(chosen), 16f, HudMenu.Muted,
            TextAlignmentOptions.TopLeft);
        KitStyle.Stretch(about.rectTransform);
        about.textWrappingMode = TextWrappingModes.Normal;

        body = W.Rect("Body", page);
        KitStyle.Stretch(body);
        body.offsetMin = new Vector2(12f, FootTall);
        body.offsetMax = new Vector2(-12f, -HeadTall);
        var layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content = W.ScrollArea(body);
        HudSettingsPages.Build(chosen, new HudPageRows(this, content), this);

        var foot = W.Rect("Foot", page);
        foot.anchorMin = Vector2.zero;
        foot.anchorMax = new Vector2(1f, 0f);
        foot.pivot = new Vector2(.5f, 0f);
        foot.offsetMin = new Vector2(12f, 12f);
        foot.offsetMax = new Vector2(-12f, 54f);
        var reset = W.ActionButton(foot, HudPageRows.T("reset"), false, () =>
        {
            HudSettingsPages.Reset(chosen);
            PaintChips();
            BuildPage();
        });
        KitStyle.Stretch((RectTransform)reset.transform);
        // Laid out now, so the page opens at its own height.
        LayoutRebuilder.ForceRebuildLayoutImmediate(body);
        Fit();
    }

    // The page as tall as its rows, up to the most; more than that scrolls.
    private void Fit()
    {
        if (content == null) return;
        float tall = Mathf.Min(PageMost, HeadTall + FootTall + content.rect.height + 4f);
        if (Mathf.Abs(page.sizeDelta.y - tall) > .5f) page.sizeDelta = new Vector2(PageWide, tall);
    }

    private void ResetAll()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (Time.unscaledTime > confirmUntil)
        {
            confirmUntil = Time.unscaledTime + ConfirmSeconds;
            resetAllLabel.text = HudPageRows.T("reset_all.confirm");
            return;
        }
        confirmUntil = -1f;
        resetAllLabel.text = HudPageRows.T("reset_all");
        GameSettings.ResetToDefaults(SettingsGroup.Hud);
        PaintChips();
        BuildPage();
    }

    // ---- Moving ----

    /// <summary>Moves the chosen part by this much (the HUD's units, y down); <paramref name="done"/> settles it.</summary>
    public void Move(Vector2 by, bool done)
    {
        var place = HudSettingsPages.PlaceOf(chosen);
        if (place == null) return;
        var to = place.Get() + by;
        if (done) to = new Vector2(Mathf.Round(to.x), Mathf.Round(to.y));
        place.Set(to);
        if (done) GameSettings.Apply();
        PlaceShown?.Invoke();
    }

    /// <summary>Everything but the HUD steps aside for a moment, for a sample that plays over the whole screen.</summary>
    public void Peek(float seconds)
    {
        peekUntil = Time.unscaledTime + seconds;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private void Update()
    {
        HudStage.Step();
        bool peeking = Time.unscaledTime < peekUntil;
        group.alpha = Mathf.MoveTowards(group.alpha, peeking ? 0f : 1f, Time.unscaledDeltaTime / .12f);
        group.blocksRaycasts = !peeking;
        if (confirmUntil > 0f && Time.unscaledTime > confirmUntil)
        {
            confirmUntil = -1f;
            resetAllLabel.text = HudPageRows.T("reset_all");
        }
        Nudge();
    }

    // The arrow keys move the chosen part; held, again and again.
    private void Nudge()
    {
        var keys = Keyboard.current;
        if (keys == null || (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)) return;
        Vector2 way = Vector2.zero;
        bool pressed = false;
        void Key(UnityEngine.InputSystem.Controls.KeyControl key, Vector2 to)
        {
            if (!key.isPressed) return;
            way += to;
            pressed |= key.wasPressedThisFrame;
        }
        Key(keys.leftArrowKey, Vector2.left);
        Key(keys.rightArrowKey, Vector2.right);
        Key(keys.upArrowKey, Vector2.down);
        Key(keys.downArrowKey, Vector2.up);
        if (way == Vector2.zero) return;
        float now = Time.unscaledTime;
        if (pressed) nudgeAt = now + FirstRepeat;
        else if (now < nudgeAt) return;
        else nudgeAt = now + Repeat;
        Move(way * (keys.shiftKey.isPressed ? 10f : 1f), true);
    }

    private void LateUpdate()
    {
        foreach (var frame in frames) frame.Place(frame.Part == chosen);
        Fit();
    }
}
