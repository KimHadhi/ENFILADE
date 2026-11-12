using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One chat line, redrawn 2026-11-12 so lines catch the eye and read over any scene (the user: "채팅창도 채팅 눈에 더 잘
/// 띄게 리디자인하면 좋을 듯"). Before, a line was bare 16 px text that vanished over a bright sky and every name wore the
/// channel's pink. Now each line sits on its own dark plate (ChatLinePlate) with a bar at its outer end, and what it is
/// shows before it is read:
/// - a player: their profile tile (picture, or their initial on their colour), their name in their colour (pink for
///   this PC's own, else the seat colour the kill feed and the board use), then the words in white; team and all lines
///   get a small tag before the name;
/// - story dialogue: the speaker's own colour and a 대사 tag, no tile;
/// - a system notice (방송): a light card with dark words and the HUD's nested diamond, so it stands out from talk;
/// - a command the player typed: a darker plate with a pink chevron; its answer: the same plate, smaller grey words.
/// A new line pops: it slides out from the edge, starts a touch bigger, its plate flashes the speaker's colour and its bar
/// flares, all over in under .2 s. While the chat is closed it stays Chat.csv Fade.hold seconds, then fades; only the
/// newest Fade.lines show, each older one a step fainter. Opening the chat brings every line back.
/// Built in code: the manager stacks it in the history (the layout group sizes it from its LayoutElement) and ticks it
/// every frame. What shows sits on a child (Visual), so the pop moves and scales it without the layout group pulling it
/// back.
/// </summary>
[DisallowMultipleComponent]
public sealed class ChatMessageItem : MonoBehaviour
{
    private enum Kind { Player, Dialogue, System, Command, Result }

    // How often a shown player line looks again for its speaker's picture (s): a friend's arrives after they join.
    private const float PictureEvery = .5f;

    public ChatEntry Entry { get; private set; }

    private RectTransform visual, tileRect, glyphRect, wordsRect;
    private LayoutElement layout;
    private CanvasGroup group;
    private ChatLinePlate plate;
    private RawImage tile;
    private TextMeshProUGUI letter, words;
    private ChatChannelIcon glyph;
    private Material shadowed, plain;
    private ChatLook look;
    private Kind kind;
    private Color speaker, flashTint;
    private Texture shownPicture;
    private bool hasPicture, settled;
    private float since, holdUntil, alpha, nextPicture;

    /// <summary>A new, empty line at the end of <paramref name="list"/>, in the chat's font.</summary>
    public static ChatMessageItem Create(Transform list, TMP_FontAsset font, ChatLook look)
    {
        var root = new GameObject("Chat Line", typeof(RectTransform));
        root.layer = list.gameObject.layer;
        root.transform.SetParent(list, false);
        var item = root.AddComponent<ChatMessageItem>();
        item.Build(font, look);
        return item;
    }

    private void Build(TMP_FontAsset font, ChatLook chatLook)
    {
        look = chatLook;
        var root = (RectTransform)transform;
        root.anchorMin = new Vector2(0f, 1f);
        root.anchorMax = Vector2.one;
        root.pivot = new Vector2(0f, 1f);
        layout = gameObject.AddComponent<LayoutElement>();

        visual = Child("Visual", root);
        visual.anchorMin = Vector2.zero;
        visual.anchorMax = new Vector2(0f, 1f);
        visual.pivot = new Vector2(0f, .5f);
        group = visual.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        plate = visual.gameObject.AddComponent<ChatLinePlate>();

        tileRect = Corner(Child("Tile", visual));
        tile = tileRect.gameObject.AddComponent<RawImage>();
        tile.raycastTarget = false;
        letter = Words("Initial", tileRect, font);
        var letterRect = letter.rectTransform;
        letterRect.anchorMin = Vector2.zero;
        letterRect.anchorMax = Vector2.one;
        letterRect.offsetMin = letterRect.offsetMax = Vector2.zero;
        letter.alignment = TextAlignmentOptions.Center;
        letter.fontStyle = FontStyles.Bold;
        letter.color = KitStyle.Ink;

        glyphRect = Corner(Child("Mark", visual));
        glyph = glyphRect.gameObject.AddComponent<ChatChannelIcon>();
        glyph.raycastTarget = false;

        words = Words("Words", visual, font);
        wordsRect = Corner(words.rectTransform);
        words.alignment = TextAlignmentOptions.TopLeft;
        words.textWrappingMode = TextWrappingModes.Normal;
        words.richText = true;
        plain = words.font != null ? words.font.material : null;
        // The soft drop shadow the HUD's labels wear (FeelUI), for white words on a see-through plate.
        FeelUI.ShadowScore(words);
        shadowed = words.fontSharedMaterial;
    }

    /// <summary>Shows <paramref name="entry"/> as a new line, at most <paramref name="maxWidth"/> px wide, and starts its pop.</summary>
    public void Show(ChatEntry entry, float maxWidth, float now)
    {
        Entry = entry;
        kind = KindOf(entry);
        var line = look.Line;
        speaker = entry.Mine ? line.mine : entry.SpeakerColor;
        Color fill, bar, ink;
        float size;
        switch (kind)
        {
            case Kind.System: fill = line.system; bar = line.systemMark; ink = line.systemInk; size = line.textSize; flashTint = line.systemMark; break;
            case Kind.Command: fill = line.console; bar = line.mine; ink = line.ink; size = line.commandSize; flashTint = line.mine; break;
            case Kind.Result: fill = line.console; bar = line.consoleInk; ink = line.consoleInk; size = line.resultSize; flashTint = line.consoleInk; break;
            default: fill = line.plate; bar = speaker; ink = line.ink; size = line.textSize; flashTint = speaker; break;
        }
        flashTint.a = 1f;
        Color inner = fill;
        inner.a *= Mathf.Clamp01(line.plateEnd);
        plate.Paint(fill, inner, bar, line.bar, line.shear);

        // The mark at the outer end: a player's profile tile, a notice's diamond, a command's chevron.
        bool showTile = kind == Kind.Player, showGlyph = kind == Kind.System || kind == Kind.Command;
        float mark = showTile ? line.tile : showGlyph ? line.glyph : 0f;
        tileRect.gameObject.SetActive(showTile);
        glyphRect.gameObject.SetActive(showGlyph);
        if (showGlyph)
        {
            glyph.SetChannel(kind == Kind.System ? ChatChannel.Broadcast : ChatChannel.Console);
            glyph.color = kind == Kind.System ? line.systemMark : line.mine;
        }

        words.fontSize = size;
        words.color = ink;
        // Dark words on the light card need no shadow; it would only muddy them.
        var material = kind == Kind.System ? plain : shadowed;
        if (material != null) words.fontSharedMaterial = material;
        float left = line.bar + line.padX + (mark > 0f ? mark + line.tileGap : 0f);
        float room = Mathf.Max(40f, maxWidth - left - line.padX - line.shear);
        // One row's height first: measuring other words leaves TMP's parse buffer on them, so the line's own go last.
        float row = words.GetPreferredValues("가", 9999f, 0f).y;
        words.text = Compose(entry);
        Vector2 measured = words.GetPreferredValues(room, 0f);
        float textWidth = Mathf.Min(room, measured.x), textHeight = Mathf.Max(row, measured.y);

        // The mark sits on the first row's middle; a mark taller than a row pushes the words down to meet it.
        float middle = Mathf.Max(row, mark) * .5f;
        float markTop = middle - mark * .5f, textTop = middle - row * .5f;
        float height = Mathf.Max(markTop + mark, textTop + textHeight) + line.padY * 2f;
        float width = left + textWidth + line.padX + line.shear;
        layout.minHeight = layout.preferredHeight = Mathf.Ceil(height);
        visual.sizeDelta = new Vector2(Mathf.Ceil(width), 0f);
        float markX = line.bar + line.padX;
        if (showTile) Place(tileRect, markX, line.padY + markTop, line.tile, line.tile);
        if (showGlyph) Place(glyphRect, markX, line.padY + markTop, line.glyph, line.glyph);
        // The words' box takes the whole room, so TMP wraps exactly where it was measured; the plate ends at the words.
        Place(wordsRect, left, line.padY + textTop, room, textHeight + 4f);
        if (showTile)
        {
            letter.fontSize = line.tile * .6f;
            if (plain != null) letter.fontSharedMaterial = plain;
            PaintTile(true);
            nextPicture = now + PictureEvery;
        }

        since = now;
        holdUntil = now + look.Fade.hold;
        alpha = 1f;
        group.alpha = 1f;
        settled = false;
        visual.gameObject.SetActive(true);
        Pop(0f);
    }

    /// <summary>The chat closed: a line whose time is up stays Chat.csv Fade.linger longer, so the history doesn't blink out.</summary>
    public void Linger(float now) => holdUntil = Mathf.Max(holdUntil, now + look.Fade.linger);

    /// <summary>
    /// Moves the line one frame: its pop, and how much of it shows. rank is 0 for the newest line; open is whether the
    /// chat is open (every line back, none dimmed). False once nothing moves until something changes, so the manager can
    /// stop at the first such line past the ones shown while closed.
    /// </summary>
    public bool Tick(float now, float dt, int rank, bool open)
    {
        float age = now - since;
        bool popping = !settled && age < Mathf.Max(look.Pop.seconds, look.Pop.flashSeconds);
        if (!settled)
        {
            Pop(age);
            settled = !popping;
        }

        var fade = look.Fade;
        float target;
        if (open) target = 1f;
        else if (rank >= fade.lines) target = 0f;
        else
        {
            float past = now - holdUntil;
            target = past <= 0f ? 1f : 1f - Mathf.Clamp01(past / Mathf.Max(.01f, fade.seconds));
            target *= Mathf.Max(fade.floor, 1f - fade.older * rank);
        }
        float next = Mathf.MoveTowards(alpha, target, dt / Mathf.Max(.01f, fade.push));
        if (next != alpha)
        {
            alpha = next;
            group.alpha = alpha;
        }
        bool shown = alpha > .001f;
        if (visual.gameObject.activeSelf != shown) visual.gameObject.SetActive(shown);
        if (shown && kind == Kind.Player && now >= nextPicture)
        {
            nextPicture = now + PictureEvery;
            PaintTile(false);
        }
        return popping || alpha != target;
    }

    // The arrival, age seconds in: out from the edge, a touch big, the plate washed in the speaker's colour, the bar wide.
    private void Pop(float age)
    {
        var pop = look.Pop;
        float through = Mathf.Clamp01(age / Mathf.Max(.01f, pop.seconds));
        float ease = 1f - (1f - through) * (1f - through) * (1f - through);
        visual.anchoredPosition = new Vector2(-(1f - ease) * pop.slide, 0f);
        float scale = Mathf.LerpUnclamped(pop.scale, 1f, ease);
        visual.localScale = new Vector3(scale, scale, 1f);
        float flash = 1f - Mathf.Clamp01(age / Mathf.Max(.01f, pop.flashSeconds));
        plate.Flash(flashTint, flash * flash * pop.flash, look.Line.bar * (1f + (pop.bar - 1f) * flash));
    }

    // Their picture, or their initial on their colour. A friend's picture can come after the line, and a picture goes
    // when its owner leaves (PlayerNetworkPose destroys it): the initial stands in again then.
    private void PaintTile(bool force)
    {
        Texture picture = Entry.Who != null ? ChatStyle.PictureOf(Entry.Who) : shownPicture;
        bool has = picture != null;
        if (!force && has == hasPicture && picture == shownPicture) return;
        shownPicture = picture;
        hasPicture = has;
        tile.texture = picture;
        tile.color = has ? Color.white : speaker;
        letter.text = has ? "" : ProfilePicture.Initial(Entry.Speaker);
    }

    private static Kind KindOf(ChatEntry entry)
    {
        switch (entry.Channel)
        {
            case ChatChannel.Console: return entry.Mine ? Kind.Command : Kind.Result;
            case ChatChannel.Broadcast: return Kind.System;
            case ChatChannel.Dialogue: return Kind.Dialogue;
            default: return Kind.Player;
        }
    }

    // The words as TMP rich text: a notice or a command is only its words; talk is [tag] name, then the words. What
    // people typed stays literal (ChatStyle.Literal), so nobody's message can restyle the chat.
    private string Compose(ChatEntry entry)
    {
        string body = ChatStyle.Literal(entry.Body);
        if (kind == Kind.System || kind == Kind.Command || kind == Kind.Result) return body;
        string tag = entry.Channel == ChatChannel.Note ? ""
            : "<size=78%><color=#" + ColorUtility.ToHtmlStringRGB(ChatStyle.ColorFor(entry.Channel)) + ">"
                + ChatStyle.Label(entry.Channel) + "</color></size><space=.4em>";
        if (string.IsNullOrWhiteSpace(entry.Speaker)) return tag + body;
        return tag + "<b><color=#" + ColorUtility.ToHtmlStringRGB(speaker) + ">" + ChatStyle.Literal(entry.Speaker)
            + "</color></b><space=.45em>" + body;
    }

    private RectTransform Child(string name, Transform parent)
    {
        var rect = FeelUI.Rect(name, parent);
        rect.gameObject.layer = gameObject.layer;
        return rect;
    }

    // Hung from its parent's top left, so Place can put it by how far right and down it sits.
    private static RectTransform Corner(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        return rect;
    }

    private static void Place(RectTransform rect, float x, float down, float width, float height)
    {
        rect.anchoredPosition = new Vector2(x, -down);
        rect.sizeDelta = new Vector2(width, height);
    }

    private TextMeshProUGUI Words(string name, Transform parent, TMP_FontAsset font)
    {
        var text = Child(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }
}
