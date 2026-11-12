using System;
using UnityEngine;

/// <summary>
/// The chat's numbers, from Tables/Chat.csv (a key table: Place.*, Line.*, Pop.*, Fade.*, Open.*). They shape the
/// 2026-11-12 redesign (the user: "채팅창도 채팅 눈에 더 잘 띄게 리디자인하면 좋을 듯"): every line on its own dark plate, so it
/// holds up over a bright sky, a dark tunnel or a busy fight, which bare text never did; the name in the speaker's colour
/// beside their profile tile; a short pop as a line arrives; system and command lines drawn apart from what players say;
/// an input box in the HUD's black, white and one pink. SinglePlayerChatManager binds it, ChatFrame and ChatMessageItem
/// read it, and a saved table reaches a running game (GameTables.Reloaded).
/// </summary>
[Serializable]
public sealed class ChatLook
{
    public const string TableName = "Chat";

    /// <summary>
    /// Where the chat stands on the HUD's left plane. Measured from the screen's edges, not its middle as the prefab was,
    /// so a 16:10, 4:3 or half-width window keeps it on the screen and in the same place against the teammate cards
    /// (TeamHealthPanel, also measured from the bottom left).
    /// </summary>
    [Serializable]
    public sealed class PlaceRow
    {
        [Tooltip("The chat's left edge from the screen's left (px at 1080p), in line with the mission bar above it.")]
        public float left = 70f;
        [Tooltip("The input box's bottom from the screen's bottom (px at 1080p): over the third teammate card " +
            "(Hud.csv Team.bottom 292 + three 32 px cards and their gaps reach 398).")]
        public float bottom = 412f;
        [Tooltip("The chat's width (px). Its right edge stays far left of the crosshair at every aspect.")]
        public float width = 470f;
        [Tooltip("The history's height over the input box (px): what scrolls while typing.")]
        public float height = 300f;
        [Tooltip("Highest the chat's top may reach above the screen's middle (px), under the mission bar's TAB row " +
            "(+363 to +388 there). A short screen takes the history's height down first.")]
        public float ceiling = 340f;
        [Tooltip("The input box's height (px).")]
        public float inputHeight = 36f;
        [Tooltip("Room between the history and the input box (px).")]
        public float gap = 6f;
        [Tooltip("The chat's own lean on its plane (°), times Settings → HUD 기울기, so it reads as a piece of its own " +
            "(Persona-style; the teammate cards lean +8° and roll +1.5°). Positive yaw sends the right side away.")]
        public float yaw = 4f;
        [Tooltip("The chat's own turn within the screen (°), times Settings → HUD 기울기. Positive raises the right side.")]
        public float roll = -.6f;
    }

    /// <summary>How one line is drawn (ChatMessageItem).</summary>
    [Serializable]
    public sealed class LineRow
    {
        [Tooltip("The words' size for what players say, dialogue and system notices.")]
        public float textSize = 18f;
        [Tooltip("The size of a command the player typed (/help …).")]
        public float commandSize = 16f;
        [Tooltip("The size of a command's answer, quieter than what people say.")]
        public float resultSize = 15f;
        [Tooltip("Room inside the plate at the outer end and past the words (px).")]
        public float padX = 9f;
        [Tooltip("Room above and below the words (px).")]
        public float padY = 5f;
        [Tooltip("Gap between two lines (px).")]
        public float gap = 4f;
        [Tooltip("The speaker's profile tile (px): their picture, or their initial on their colour.")]
        public float tile = 22f;
        [Tooltip("Room between the tile or mark and the words (px).")]
        public float tileGap = 8f;
        [Tooltip("The size of a system notice's diamond and a command's chevron (px).")]
        public float glyph = 13f;
        [Tooltip("The bar at the plate's outer end, in the speaker's colour (px).")]
        public float bar = 3f;
        [Tooltip("How far the plate's inner end leans (px), the HUD's slant.")]
        public float shear = 10f;
        [Tooltip("The plate behind a line: near black, see-through enough to keep the world, dark enough for white words on snow.")]
        public Color plate = new Color32(11, 11, 14, 173);
        [Tooltip("Share of the plate's opacity left at its inner end, so it eases off toward the middle of the screen.")]
        public float plateEnd = .72f;
        [Tooltip("The words' colour.")]
        public Color ink = new Color32(244, 245, 248, 255);
        [Tooltip("The player's own name and bar: the HUD's one pink.")]
        public Color mine = new Color32(255, 79, 120, 255);
        [Tooltip("A system notice's card (방송: friendly fire switched, a test notice): light, so it stands out from the dark lines.")]
        public Color system = new Color32(255, 245, 247, 235);
        [Tooltip("A system notice's words, dark on its light card.")]
        public Color systemInk = new Color32(23, 23, 27, 255);
        [Tooltip("A system notice's diamond and bar.")]
        public Color systemMark = new Color32(255, 79, 120, 255);
        [Tooltip("The plate behind a command and its answer: darker and plainer, so they read as the console's.")]
        public Color console = new Color32(11, 11, 14, 217);
        [Tooltip("A command's answer and its bar.")]
        public Color consoleInk = new Color32(184, 193, 206, 255);
    }

    /// <summary>The pop as a line arrives: short and over, the way the HUD's confirms are.</summary>
    [Serializable]
    public sealed class PopRow
    {
        [Tooltip("How long the slide and the scale take (s).")]
        public float seconds = .18f;
        [Tooltip("How far left of its place a line starts (px).")]
        public float slide = 28f;
        [Tooltip("How big a line starts (its outer end stays put).")]
        public float scale = 1.07f;
        [Tooltip("How long the plate's flash of the speaker's colour lasts (s).")]
        public float flashSeconds = .14f;
        [Tooltip("How strong that flash is in its first frame (0-1).")]
        public float flash = .85f;
        [Tooltip("How many times its width the end bar starts at.")]
        public float bar = 3f;
    }

    /// <summary>How lines leave while the chat is closed. Opening it brings every line back.</summary>
    [Serializable]
    public sealed class FadeRow
    {
        [Tooltip("How long a line stays after it arrives (s, real time).")]
        public float hold = 8f;
        [Tooltip("How long its fade takes (s).")]
        public float seconds = .5f;
        [Tooltip("Most lines shown while the chat is closed; older ones go at once.")]
        public int lines = 5;
        [Tooltip("How long the lines stay after the chat closes (s) when their own time is up.")]
        public float linger = 1.5f;
        [Tooltip("How much fainter each older line is than the one under it, while closed.")]
        public float older = .07f;
        [Tooltip("The faintest an older line gets that way.")]
        public float floor = .72f;
        [Tooltip("How long a line pushed past the most takes to go, and lines take to come back on opening (s).")]
        public float push = .25f;
    }

    /// <summary>The box while typing: the history's panel, the scrollbar and the input box.</summary>
    [Serializable]
    public sealed class OpenRow
    {
        [Tooltip("The history's panel behind the lines.")]
        public Color panel = new Color32(11, 11, 14, 140);
        [Tooltip("The pink: the panel's top edge mark, the input's underline, the scrollbar's handle.")]
        public Color edge = new Color32(255, 79, 120, 255);
        [Tooltip("The edge mark's length along the panel's top (px).")]
        public float edgeLength = 64f;
        [Tooltip("The input box.")]
        public Color input = new Color32(11, 11, 14, 240);
        [Tooltip("The input's underline (px).")]
        public float underline = 2f;
        [Tooltip("The blinking caret's colour (TMP_InputField's own caret).")]
        public Color caret = new Color32(255, 79, 120, 255);
        [Tooltip("The caret's width (px).")]
        public int caretWidth = 2;
        [Tooltip("What selected words sit on.")]
        public Color selection = new Color32(255, 79, 120, 89);
        [Tooltip("The empty box's hint.")]
        public Color placeholder = new Color32(255, 255, 255, 115);
        [Tooltip("The scrollbar's track.")]
        public Color track = new Color32(255, 255, 255, 20);
        [Tooltip("The scrollbar's width (px).")]
        public float scrollbar = 4f;
        [Tooltip("How far one wheel step scrolls the history (px).")]
        public float scroll = 32f;
        [Tooltip("The channel tab's width at the input's left (px). Tab or a click on it changes the channel.")]
        public float tab = 92f;
        [Tooltip("The channel's name on the tab.")]
        public float tabSize = 15f;
    }

    public readonly PlaceRow Place = new PlaceRow();
    public readonly LineRow Line = new LineRow();
    public readonly PopRow Pop = new PopRow();
    public readonly FadeRow Fade = new FadeRow();
    public readonly OpenRow Open = new OpenRow();

    /// <summary>Applies Chat.csv to every row, kept for later reloads while <paramref name="owner"/> lives.</summary>
    public void Bind(UnityEngine.Object owner)
    {
        GameTables.BindNested(TableName, "Place", owner, Place);
        GameTables.BindNested(TableName, "Line", owner, Line);
        GameTables.BindNested(TableName, "Pop", owner, Pop);
        GameTables.BindNested(TableName, "Fade", owner, Fade);
        GameTables.BindNested(TableName, "Open", owner, Open);
    }
}
