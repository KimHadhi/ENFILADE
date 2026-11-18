using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// The one owner of a player's colour: the colour that says who someone is, wherever they are named or marked (the Tab
/// board, the team cards, the marks over heads, the kill feed, the chat, callouts and their pins, pings, the results).
/// Until 2026-11-18 it was a seat colour by network id (SeatColors[OwnerClientId % 8]), which a player could not choose
/// and which changed with who had connected before, while several screens painted this PC's own player the HUD's pink
/// instead and every ping and callout was that pink whoever made it. So a player saw themself pink where the others saw
/// them blue (the user: "타인 화면에서의 내 색상(파란색)과 내 화면에서의 내 색상(핑크색)이 다르게 표시되는 동기화 오류",
/// "콜아웃 색상이 Tab 현황판의 팀원 색상(파랑, 주황 등)과 연동되지 않고 분홍색으로 고정되는 문제 및 팀원 색상이 랜덤
/// 배정되는 문제").
/// Now:
/// - A player chooses their colour on their own PC (GameSettings.playerColour): one of the colours of their own they
///   keep in slots and make on a colour board (PlayerColourRow, ColourPanel; the user, 2026-11-18: "미리 정해둔 프리셋
///   방식이 아닌 여러 색깔 슬롯을 좀 두고 직접 그 색상 판에서 색깔을 커스텀"), or nothing. A colour too dark to read on
///   the HUD's dark plates is brought up to one that reads (Readable), whatever way it was asked for.
/// - Whoever chose nothing is given the palette's colours in the table's order (Tables/Hud/PlayerColours.csv; a mod adds
///   rows), and the first is the HUD's pink (the user: "분홍으로 합시다 기본"): alone, or first in a room, they are pink,
///   and the next who chose nothing are the colours after it, each far from those before.
/// - Their PC sends the colour asked for (PlayerNetworkScore) and nothing else: no PC is asked what colour anyone is.
/// - Every PC gives every player their colour by the same rule from the same facts (Resolve: who asked for what, and
///   their network ids), so one player is one colour on every screen, their own included.
/// - Two who want the same colour: the one with the lower id (the host, then whoever came first) keeps it, the other
///   gets the palette colour nearest to it that nobody has. Whoever chose nothing takes the first palette colour left,
///   after everyone who chose, so a choice is never lost to a seat.
/// - Everything reads PlayerTally.Color. Nothing paints "this PC's player" another colour.
/// The console's /colours writes the palette, who has what and why to Logs/player-colours.txt, and "cases" runs the rule
/// alone over many rooms.
/// </summary>
public static class PlayerColours
{
    public const string TableName = "PlayerColours";

    /// <summary>
    /// A player as the rule sees them: their network owner id (the lower keeps a colour two want) and the colour they
    /// asked for as Code packs it (0 = they chose none).
    /// </summary>
    public struct Seat
    {
        public ulong Id;
        public uint Wish;
    }

    // Black against white in Distance's own measure, so it reads 0 (the same colour) to 1.
    private static readonly float Farthest = Mathf.Sqrt((2f + 127.5f / 256f) * 2f + 4f) * 255f;

    private static readonly List<PlayerTally> tallies = new List<PlayerTally>();
    // Those of them who play in the room (this PC's player and the others' copies), each beside their seat.
    private static readonly List<PlayerTally> seated = new List<PlayerTally>();
    private static readonly List<Seat> seats = new List<Seat>();
    private static readonly List<Color32> palette = new List<Color32>();
    private static readonly List<string> paletteIds = new List<string>();
    private static readonly List<int> order = new List<int>();
    private static readonly List<Color32> taken = new List<Color32>();
    private static Color32[] given = new Color32[0];
    private static bool dirty = true, paletteRead, listening;
    // The colour this PC's player was last told they were moved to (0 = their wish stands, or they chose none).
    private static uint toldMoved;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        tallies.Clear();
        seated.Clear();
        seats.Clear();
        palette.Clear();
        paletteIds.Clear();
        dirty = true;
        paletteRead = listening = false;
        toldMoved = 0;
    }

    // ---- The palette (Tables/Hud/PlayerColours.csv) ----

    /// <summary>The palette's colours, in the table's order: the order players who chose none are given them in.</summary>
    public static IReadOnlyList<Color32> Palette { get { ReadPalette(); return palette; } }

    /// <summary>The palette's row ids, beside Palette (what a setting holds for a palette colour).</summary>
    public static IReadOnlyList<string> PaletteIds { get { ReadPalette(); return paletteIds; } }

    /// <summary>
    /// How near two colours may be before they count as the same colour for two players (Hud.csv PlayerColour.apart; 0 the
    /// same colour, 1 black against white). The palette's own colours are all further apart than this.
    /// </summary>
    public static float Apart => Mathf.Max(0f, GameTables.Number(HudCamera.TableName, "PlayerColour", "apart", .1f));

    /// <summary>
    /// The least a player's colour may weigh (ColourWeight: 0 black to 1 white; Hud.csv PlayerColour.dim, 0 lets every
    /// colour through). A player's colour is their name on the board, in the chat and over their head, on the HUD's
    /// near-black plates, so one below this is brought up to it (Readable) and the colour board does not hold it.
    /// </summary>
    public static float Dim => Mathf.Clamp01(GameTables.Number(HudCamera.TableName, "PlayerColour", "dim", .07f));

    /// <summary>How many colours of their own a player keeps (Hud.csv PlayerColour.slots).</summary>
    public static int SlotCount => Mathf.Clamp(Mathf.RoundToInt(GameTables.Number(HudCamera.TableName, "PlayerColour", "slots", 5f)), 1, 12);

    private static void ReadPalette()
    {
        if (paletteRead) return;
        paletteRead = true;
        if (!listening) { listening = true; GameTables.Reloaded += Forget; }
        palette.Clear();
        paletteIds.Clear();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row) || !row.TryGetValue("colour", out string text)) continue;
            if (!ColorUtility.TryParseHtmlString(text.Trim(), out Color colour)) continue;
            Color32 solid = colour;
            solid.a = 255;
            palette.Add(solid);
            paletteIds.Add(id);
        }
    }

    /// <summary>Goes up each time the palette is read again, so a wish held as a palette row's id is looked up again (PlayerTally).</summary>
    internal static int PaletteVersion { get; private set; }

    // The table was saved: the palette is read again and everyone's colour settled again.
    private static void Forget()
    {
        paletteRead = false;
        dirty = true;
        PaletteVersion++;
    }

    // ---- What a player asked for ----

    /// <summary>A colour as one number that travels and compares: 0xFFRRGGBB. 0 is "none asked for".</summary>
    public static uint Code(Color32 colour) => 0xFF000000u | ((uint)colour.r << 16) | ((uint)colour.g << 8) | colour.b;

    /// <summary>The colour a code stands for (opaque).</summary>
    public static Color32 Colour(uint code) => new Color32((byte)(code >> 16), (byte)(code >> 8), (byte)code, 255);

    /// <summary>
    /// What a setting asks for (GameSettings.playerColour): "#RRGGBB" gives that colour, brought up to one that reads
    /// (Readable); a palette row's id gives that row's colour (what a setting saved before the slots held); and nothing,
    /// or an id the table no longer has, gives 0: none asked for.
    /// </summary>
    public static uint WishOf(string setting)
    {
        if (string.IsNullOrWhiteSpace(setting)) return 0u;
        string text = setting.Trim();
        ReadPalette();
        for (int i = 0; i < paletteIds.Count; i++)
            if (string.Equals(paletteIds[i], text, System.StringComparison.OrdinalIgnoreCase)) return Code(Readable(palette[i]));
        return ColourCode.TryRead(text, out Color32 colour) ? Code(Readable(colour)) : 0u;
    }

    // ---- A colour that reads ----

    /// <summary>
    /// The colour itself where it reads on the HUD's dark plates, else the nearest of its hue that does (Dim,
    /// ColourWeight.AtLeast: brighter, and for a hue too dark at its brightest, paler). Whatever way a colour is asked
    /// for comes through here (WishOf), and the colour board is handed the same least weight, so it never offers a
    /// colour this would change.
    /// </summary>
    public static Color32 Readable(Color32 colour) => ColourWeight.AtLeast(colour, Dim);

    // ---- The player's own colours (the slots) ----

    /// <summary>
    /// Brings the colours a player keeps (GameSettings.playerColourSlots) and the one in use
    /// (<paramref name="chosen"/>, GameSettings.playerColour) to what the 내 색 row shows, and says which slot is in use
    /// (-1: none, a free colour is given). There are <paramref name="count"/> slots; one never filled, or holding
    /// anything but a colour, starts as the palette's colour of its place, so the row opens on the colours players are
    /// given anyway. Every slot is written "#RRGGBB" and weighs <paramref name="dim"/> or more (as Readable makes it). A colour in use that no slot holds
    /// (a palette row or a code picked before there were slots) goes into the first slot, so nothing a player chose is
    /// lost. A pure rule over what it is handed, the palette (<paramref name="colours"/> and their row ids
    /// <paramref name="names"/>) too (PlayerColourCheck runs it without a play).
    /// </summary>
    public static int FillSlots(List<string> slots, ref string chosen, int count, IReadOnlyList<Color32> colours,
        IReadOnlyList<string> names, float dim)
    {
        for (int i = 0; i < count; i++)
        {
            if (i >= slots.Count) slots.Add("");
            Color32 colour = ColourCode.TryRead(slots[i], out Color32 kept) ? kept
                : colours.Count > 0 ? colours[i % colours.Count] : new Color32(255, 255, 255, 255);
            slots[i] = ColourCode.Of(ColourWeight.AtLeast(colour, dim));
        }
        string wanted = (chosen ?? "").Trim();
        bool asks = ColourCode.TryRead(wanted, out Color32 asked);
        for (int i = 0; i < names.Count && i < colours.Count && !asks && wanted.Length > 0; i++)
            if (string.Equals(names[i], wanted, System.StringComparison.OrdinalIgnoreCase)) { asked = colours[i]; asks = true; }
        if (!asks) { chosen = ""; return -1; }
        chosen = ColourCode.Of(ColourWeight.AtLeast(asked, dim));
        for (int i = 0; i < count; i++)
            if (string.Equals(slots[i], chosen, System.StringComparison.OrdinalIgnoreCase)) return i;
        slots[0] = chosen;
        return 0;
    }

    /// <summary>The same for this PC's settings, by the tables.</summary>
    public static int FillSlots(GameSettings settings)
    {
        ReadPalette();
        settings.playerColourSlots ??= new List<string>();
        return FillSlots(settings.playerColourSlots, ref settings.playerColour, SlotCount, palette, paletteIds, Dim);
    }

    // ---- The rule ----

    /// <summary>
    /// How far apart two colours look, 0 for the same colour to 1 for black against white (the "redmean" weighting: the
    /// eye tells greens apart better than blues, and reds better in bright colours). A measure, not a tuning: it only
    /// says which palette colour is nearest to a wish, and whether two colours are one colour (Apart).
    /// </summary>
    public static float Distance(Color32 a, Color32 b)
    {
        float mean = (a.r + b.r) * .5f, red = a.r - b.r, green = a.g - b.g, blue = a.b - b.b;
        return Mathf.Sqrt((2f + mean / 256f) * red * red + 4f * green * green + (2f + (255f - mean) / 256f) * blue * blue) / Farthest;
    }

    /// <summary>
    /// Everyone's colour from who asked for what: a pure rule, the same answer on every PC whatever order it holds the
    /// seats in. Those who asked first, by id: each keeps their wish unless someone before them holds a colour within
    /// <paramref name="apart"/> of it, and then takes the free palette colour nearest to it (their wish all the same when
    /// the palette has none free). Then those who asked for nothing, by id: each the first free palette colour in the
    /// table's order (a taken one in turn when none is free: more players than the palette has colours).
    /// <paramref name="given"/> takes each seat's colour at the seat's own index.
    /// </summary>
    public static void Resolve(IReadOnlyList<Seat> seats, IReadOnlyList<Color32> palette, float apart, Color32[] given)
    {
        order.Clear();
        for (int i = 0; i < seats.Count; i++)
        {
            int at = order.Count;
            // By id, and by place for two of one id (a player not yet on the network reads 0 for a moment).
            while (at > 0 && seats[order[at - 1]].Id > seats[i].Id) at--;
            order.Insert(at, i);
        }
        taken.Clear();
        foreach (int i in order)
        {
            if (seats[i].Wish == 0u) continue;
            Color32 wish = Colour(seats[i].Wish), colour = wish;
            if (!Free(wish, apart))
            {
                float best = float.PositiveInfinity;
                for (int p = 0; p < palette.Count; p++)
                {
                    if (!Free(palette[p], apart)) continue;
                    float far = Distance(wish, palette[p]);
                    if (far < best) { best = far; colour = palette[p]; }
                }
            }
            given[i] = colour;
            taken.Add(colour);
        }
        int unasked = 0;
        foreach (int i in order)
        {
            if (seats[i].Wish != 0u) continue;
            Color32 colour = palette.Count > 0 ? palette[unasked % palette.Count] : new Color32(255, 255, 255, 255);
            for (int p = 0; p < palette.Count; p++)
                if (Free(palette[p], apart)) { colour = palette[p]; break; }
            unasked++;
            given[i] = colour;
            taken.Add(colour);
        }
    }

    // Nobody given a colour so far holds one within apart of this.
    private static bool Free(Color32 colour, float apart)
    {
        foreach (var held in taken) if (Distance(colour, held) < apart) return false;
        return true;
    }

    // ---- The players on this PC ----

    internal static void Join(PlayerTally tally)
    {
        if (!tallies.Contains(tally)) tallies.Add(tally);
        dirty = true;
    }

    internal static void Leave(PlayerTally tally)
    {
        tallies.Remove(tally);
        dirty = true;
    }

    /// <summary>Something the rule reads changed (who is here, a wish, an id, the table): colours are settled again at the next look.</summary>
    internal static void Changed() => dirty = true;

    /// <summary>
    /// Gives every player on this PC their colour, if anything changed since the last time (PlayerTally.Color calls it,
    /// so whoever reads a colour reads a settled one).
    /// </summary>
    internal static void Settle()
    {
        if (!dirty) return;
        dirty = false;
        ReadPalette();
        seats.Clear();
        seated.Clear();
        for (int i = tallies.Count - 1; i >= 0; i--) if (tallies[i] == null) tallies.RemoveAt(i);
        foreach (var tally in tallies)
        {
            // Only those who play in the room take a colour: a stand-in body on this PC alone (a practice dummy, a shown
            // copy) must not take one from a player, and reads white.
            if (!tally.Plays) { tally.Give(new Color32(255, 255, 255, 255)); continue; }
            seated.Add(tally);
            seats.Add(new Seat { Id = tally.SeatId, Wish = tally.Wish });
        }
        if (given.Length < seats.Count) given = new Color32[seats.Count];
        Resolve(seats, palette, Apart, given);
        for (int i = 0; i < seated.Count; i++) seated[i].Give(given[i]);
        TellIfMoved();
    }

    // This PC's player asked for a colour and someone else holds it: said once, with the colour they are shown in, so the
    // change never looks like the game ignoring their choice.
    private static void TellIfMoved()
    {
        for (int i = 0; i < seated.Count; i++)
        {
            if (!seated[i].IsMine) continue;
            // Not while their player is being taken down with its scene: nothing is put on the screen then.
            if (!seated[i].isActiveAndEnabled) return;
            uint wish = seats[i].Wish, got = Code(given[i]);
            uint moved = wish != 0u && got != wish ? got : 0u;
            if (moved == toldMoved) return;
            toldMoved = moved;
            if (moved != 0u && Application.isPlaying)
                AlertHUD.Self(Texts.Format("profile.colour.moved", "<color=#" + ColorUtility.ToHtmlStringRGB(given[i]) + ">■</color>"));
            return;
        }
    }

    // ---- /colours ----

    /// <summary>
    /// The console's /colours: the palette and how far apart its colours are, every player on this PC with what they asked
    /// for and what they were given, to Logs/player-colours.txt. "cases" runs the rule alone over fixed and random rooms
    /// and counts the promises it broke (none is the right answer), so the rule can be checked without a second PC.
    /// </summary>
    public static string Command(string args)
    {
        var text = new StringBuilder();
        ReadPalette();
        float apart = Apart;
        text.AppendLine("palette (Tables/Hud/PlayerColours.csv), apart " + apart.ToString("0.###")); // dev-text
        for (int i = 0; i < palette.Count; i++)
        {
            float nearest = float.PositiveInfinity;
            string which = "";
            for (int j = 0; j < palette.Count; j++)
            {
                if (j == i) continue;
                float far = Distance(palette[i], palette[j]);
                if (far < nearest) { nearest = far; which = paletteIds[j]; }
            }
            text.AppendLine("  " + paletteIds[i].PadRight(10) + " #" + ColorUtility.ToHtmlStringRGB(palette[i]) + "  nearest " + which + " "
                + nearest.ToString("0.000") + (nearest < apart ? "  TOO NEAR: two palette colours count as one" : "")); // dev-text
        }
        var mine = GameSettings.Current;
        text.AppendLine("this PC's own colours (the slots): " + string.Join(" ", mine.playerColourSlots ?? new List<string>()) + "  in use: "
            + (string.IsNullOrEmpty(mine.playerColour) ? "none (a free colour is given)" : mine.playerColour) + "  least weight " + Dim.ToString("0.###")); // dev-text
        dirty = true;
        Settle();
        text.AppendLine("players on this PC: " + seated.Count); // dev-text
        for (int i = 0; i < seated.Count; i++)
        {
            uint wish = seats[i].Wish;
            text.AppendLine("  id " + seats[i].Id + "  " + (seated[i].IsMine ? "(mine) " : "") + seated[i].DisplayName + "  asked "
                + (wish == 0u ? "nothing" : "#" + ColorUtility.ToHtmlStringRGB(Colour(wish))) + "  given #" + ColorUtility.ToHtmlStringRGB(given[i])
                + (wish != 0u && Code(given[i]) != wish ? "  (moved: someone before them holds it)" : "")); // dev-text
        }
        if ((args ?? "").Trim().ToLowerInvariant() == "cases") Cases(text, palette, apart);
        string all = text.ToString();
        try { System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "..", "Logs", "player-colours.txt"), all); }
        catch (System.Exception problem) { Debug.LogWarning("[Colours] Logs/player-colours.txt: " + problem.Message); }
        return all + "Logs/player-colours.txt"; // dev-text
    }

    /// <summary>
    /// The rule over rooms nobody has to gather: its promises counted where they break, written to
    /// <paramref name="text"/>. True when none broke (the console's /colours cases, and PlayerColourCheck by itself).
    /// </summary>
    internal static bool Cases(StringBuilder text, IReadOnlyList<Color32> palette, float apart)
    {
        var random = new System.Random(11);
        var room = new List<Seat>();
        var shuffled = new List<Seat>();
        var places = new List<int>();
        var got = new Color32[16];
        var again = new Color32[16];
        int rooms = 0, movedWithoutClash = 0, orderMatters = 0, tooNear = 0;
        for (int run = 0; run < 20000; run++)
        {
            room.Clear();
            int count = 1 + random.Next(10);
            for (int k = 0; k < count; k++)
            {
                double roll = random.NextDouble();
                uint wish = roll < .45 || palette.Count == 0 ? 0u
                    : roll < .9 ? Code(palette[random.Next(palette.Count)])
                    : Code(new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255));
                // Ids are all different, as in a room.
                room.Add(new Seat { Id = (ulong)(k * 4 + random.Next(4)), Wish = wish });
            }
            // As this PC holds them, in any order.
            for (int k = room.Count - 1; k > 0; k--) { int j = random.Next(k + 1); (room[k], room[j]) = (room[j], room[k]); }
            Resolve(room, palette, apart, got);
            rooms++;
            // Another PC holds the same seats in another order and must say the same.
            places.Clear();
            for (int k = 0; k < count; k++) places.Add(k);
            for (int k = count - 1; k > 0; k--) { int j = random.Next(k + 1); (places[k], places[j]) = (places[j], places[k]); }
            shuffled.Clear();
            foreach (int k in places) shuffled.Add(room[k]);
            Resolve(shuffled, palette, apart, again);
            for (int k = 0; k < count; k++) if (Code(again[k]) != Code(got[places[k]])) { orderMatters++; break; }
            for (int k = 0; k < count; k++)
            {
                if (room[k].Wish == 0u || Code(got[k]) == room[k].Wish) continue;
                // Moved: someone who asked, with a lower id, must hold a colour within apart of the wish.
                bool clash = false;
                for (int j = 0; j < count; j++)
                    if (j != k && room[j].Wish != 0u && room[j].Id < room[k].Id && Distance(Colour(room[k].Wish), got[j]) < apart) clash = true;
                if (!clash) movedWithoutClash++;
            }
            for (int k = 0; k < count; k++)
                for (int j = k + 1; j < count; j++)
                {
                    if (Distance(got[k], got[j]) >= apart) continue;
                    // Two as one colour is only right when the palette had nothing free left for the later one.
                    bool spare = false;
                    foreach (var colour in palette)
                    {
                        bool free = true;
                        for (int m = 0; m < count; m++) if (Distance(colour, got[m]) < apart) free = false;
                        if (free) spare = true;
                    }
                    if (spare) tooNear++;
                }
        }
        text.AppendLine("cases: " + rooms + " rooms of 1-10 players"); // dev-text
        text.AppendLine("  a wish moved with nobody before them holding it: " + movedWithoutClash); // dev-text
        text.AppendLine("  another order of the same seats gave other colours: " + orderMatters); // dev-text
        text.AppendLine("  two players one colour while the palette had a free one: " + tooNear); // dev-text
        text.AppendLine(movedWithoutClash + orderMatters + tooNear == 0 ? "CASES OK" : "CASES FAILED"); // dev-text
        return movedWithoutClash + orderMatters + tooNear == 0;
    }
}
