#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The editor's check of the players' colours, without a play or a second PC: the game's own rules (PlayerColours.Resolve,
/// ColourWeight, FillSlots, WishOf, ColourCode, the colour board's square ColourPanel.At) run over the game's own tables (PlayerColours.csv, Hud.csv PlayerColour.*).
/// Logs/player-colours-check.txt has a line per rule and what was measured on the way (how far apart the palette's
/// colours are, what a room of each size is given). The user's own words are among the rules: whoever chose nothing is
/// pink ("분홍으로 합시다 기본"), and a player's colours are their own, kept in slots ("여러 색깔 슬롯을 좀 두고").
/// It runs by itself once after every script reload and after a play, from ENFILADE → HUD → 플레이어 색 규칙 점검, and when
/// Temp/enfilade-colourcheck.request is made (CheckOnRequest).
/// </summary>
[InitializeOnLoad]
internal static class PlayerColourCheck
{
    private const string Request = "Temp/enfilade-colourcheck.request", ReportPath = "Logs/player-colours-check.txt";

    static PlayerColourCheck() => new CheckOnRequest(Request, Run);

    [MenuItem("ENFILADE/HUD/플레이어 색 규칙 점검")]
    private static void Run()
    {
        var report = new CheckReport();
        var palette = PlayerColours.Palette;
        var ids = PlayerColours.PaletteIds;
        float apart = PlayerColours.Apart, dim = PlayerColours.Dim;
        int slots = PlayerColours.SlotCount;
        try
        {
            report.Rule("the palette has colours to give (Tables/Hud/PlayerColours.csv)");
            report.Expect(palette.Count > 0, palette.Count + " rows");
            if (palette.Count > 0)
            {
                Palette(report, palette, ids, apart, dim);
                Rooms(report, palette, ids, apart);
                Reading(report, dim);
                Board(report, dim);
                Slots(report, palette, ids, slots, dim);
                Codes(report, dim);
            }
        }
        catch (Exception problem)
        {
            report.Stopped(problem);
        }
        report.Write(ReportPath, "player colour rules check, " + palette.Count + " palette colours, apart " + SettingsWidgets.Bare(apart) + ", dim "
            + SettingsWidgets.Bare(dim) + ", " + slots + " slots");
    }

    private static string Hex(Color32 colour) => ColourCode.Of(colour);

    private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

    // The table itself: what the first colour is, and that no two rows count as one colour.
    private static void Palette(CheckReport report, IReadOnlyList<Color32> palette, IReadOnlyList<string> ids, float apart, float dim)
    {
        report.Rule("the palette's first colour is the HUD's pink: whoever chose nothing and is alone is pink");
        report.Expect(Same(palette[0], KitStyle.Accent), "first row " + ids[0] + " " + Hex(palette[0]) + ", the HUD's pink is " + Hex(KitStyle.Accent));

        report.Rule("no two palette colours are near enough to count as one (further apart than PlayerColour.apart)");
        for (int i = 0; i < palette.Count; i++)
        {
            float nearest = float.PositiveInfinity;
            int which = -1;
            for (int j = 0; j < palette.Count; j++)
            {
                if (j == i) continue;
                float far = PlayerColours.Distance(palette[i], palette[j]);
                if (far < nearest) { nearest = far; which = j; }
            }
            report.Expect(which < 0 || nearest >= apart, ids[i] + " and " + (which >= 0 ? ids[which] : "?") + " are " + SettingsWidgets.Bare(nearest) + " apart");
            report.Note(ids[i].PadRight(8) + Hex(palette[i]) + "  nearest " + (which >= 0 ? ids[which] : "none") + " " + SettingsWidgets.Bare(nearest));
        }

        report.Rule("every palette colour reads on the HUD's dark as it is (weighs PlayerColour.dim or more)");
        for (int i = 0; i < palette.Count; i++)
            report.Expect(Same(ColourWeight.AtLeast(palette[i], dim), palette[i]), ids[i] + " " + Hex(palette[i]) + " weighs "
                + SettingsWidgets.Bare(ColourWeight.Of(palette[i])));
    }

    // Who gets what in a room.
    private static void Rooms(CheckReport report, IReadOnlyList<Color32> palette, IReadOnlyList<string> ids, float apart)
    {
        var seats = new List<PlayerColours.Seat>();
        var given = new Color32[16];

        report.Rule("players who chose nothing are given the palette's colours in the table's order, each a different one");
        for (int count = 1; count <= Mathf.Min(10, palette.Count); count++)
        {
            seats.Clear();
            for (int k = 0; k < count; k++) seats.Add(new PlayerColours.Seat { Id = (ulong)k, Wish = 0u });
            PlayerColours.Resolve(seats, palette, apart, given);
            bool inOrder = true;
            float nearest = float.PositiveInfinity;
            var names = new StringBuilder();
            for (int k = 0; k < count; k++)
            {
                if (!Same(given[k], palette[k])) inOrder = false;
                for (int j = 0; j < k; j++) nearest = Mathf.Min(nearest, PlayerColours.Distance(given[k], given[j]));
                names.Append(k > 0 ? ", " : "").Append(ids[k]);
            }
            report.Expect(inOrder, count + " players: " + names);
            if (count > 1) report.Note(count + " players: " + names + "  (the two nearest are " + SettingsWidgets.Bare(nearest) + " apart)");
        }

        report.Rule("one who chose a colour keeps it over one who chose nothing, whoever came first");
        // The second to come chose the first palette colour (pink); the first chose nothing and takes the next.
        seats.Clear();
        seats.Add(new PlayerColours.Seat { Id = 0, Wish = 0u });
        seats.Add(new PlayerColours.Seat { Id = 1, Wish = PlayerColours.Code(palette[0]) });
        PlayerColours.Resolve(seats, palette, apart, given);
        report.Expect(Same(given[1], palette[0]) && palette.Count > 1 && Same(given[0], palette[1]),
            "the guest asked for " + ids[0] + " and got " + Hex(given[1]) + ", the host who chose nothing got " + Hex(given[0]));

        report.Rule("two who chose the same colour: the first to come keeps it, the other gets the nearest palette colour left, and both are told apart");
        Color32 own = new Color32(40, 200, 120, 255);
        seats.Clear();
        seats.Add(new PlayerColours.Seat { Id = 0, Wish = PlayerColours.Code(own) });
        seats.Add(new PlayerColours.Seat { Id = 1, Wish = PlayerColours.Code(own) });
        PlayerColours.Resolve(seats, palette, apart, given);
        report.Expect(Same(given[0], own) && !Same(given[1], own) && PlayerColours.Distance(given[0], given[1]) >= apart,
            "both asked for " + Hex(own) + ": the first got " + Hex(given[0]) + ", the second " + Hex(given[1]));

        report.Rule("the rule over 20000 rooms of 1 to 10: no wish moved without a clash, the same on every PC, no two alike while a colour is free");
        var said = new StringBuilder();
        report.Expect(PlayerColours.Cases(said, palette, apart), said.ToString().Replace("\r", "").Replace("\n", " | "));
    }

    // A colour too dark for the HUD is brought up, and only that.
    private static void Reading(CheckReport report, float dim)
    {
        // Three rules over one sweep of colours; what broke each is gathered and reported under its rule after it.
        int kept = 0, raised = 0, light = 0, settled = 0, hued = 0, grid = 0;
        var brokenKept = new List<string>();
        var brokenRaised = new List<string>();
        var brokenHue = new List<string>();
        for (int r = 0; r <= 255; r += 15)
            for (int g = 0; g <= 255; g += 15)
                for (int b = 0; b <= 255; b += 15)
                {
                    grid++;
                    var asked = new Color32((byte)r, (byte)g, (byte)b, 255);
                    Color32 got = ColourWeight.AtLeast(asked, dim);
                    if (ColourWeight.Of(asked) >= dim)
                    {
                        if (Same(got, asked)) kept++;
                        else if (brokenKept.Count < 6) brokenKept.Add(Hex(asked) + " became " + Hex(got));
                        continue;
                    }
                    raised++;
                    bool weighs = ColourWeight.Of(got) >= dim, again = Same(ColourWeight.AtLeast(got, dim), got);
                    if (weighs) light++;
                    if (again) settled++;
                    if ((!weighs || !again) && brokenRaised.Count < 6)
                        brokenRaised.Add(Hex(asked) + " became " + Hex(got) + " weighing " + SettingsWidgets.Bare(ColourWeight.Of(got)));
                    Color.RGBToHSV(asked, out float hueAsked, out float strengthAsked, out float brightAsked);
                    Color.RGBToHSV(got, out float hueGot, out float strengthGot, out _);
                    // A grey or black has no hue to keep, and one made nearly white has almost none left to compare.
                    if (strengthAsked < .2f || brightAsked < .1f || strengthGot < .1f) { hued++; continue; }
                    float turn = Mathf.Abs(Mathf.DeltaAngle(hueAsked * 360f, hueGot * 360f));
                    if (turn <= 6f) hued++;
                    else if (brokenHue.Count < 6) brokenHue.Add(Hex(asked) + " became " + Hex(got) + ", its hue turned " + SettingsWidgets.Bare(turn) + "°");
                }
        report.Rule("a colour that reads already is left exactly as it is");
        report.Expect(brokenKept.Count == 0, string.Join("; ", brokenKept));
        report.Note(kept + " of " + grid + " colours tried read already");
        report.Rule("a colour too dark is brought up to one that weighs enough, and asking again changes nothing");
        report.Expect(light == raised && settled == raised, string.Join("; ", brokenRaised));
        report.Note(raised + " colours were too dark and were brought up");
        report.Rule("a colour brought up keeps its hue (within 6°)");
        report.Expect(hued == raised, string.Join("; ", brokenHue));

        report.Rule("black becomes a grey, a hue too dark even at its brightest becomes paler and stays that hue, and white stays white");
        Color32 black = ColourWeight.AtLeast(new Color32(0, 0, 0, 255), dim), blue = ColourWeight.AtLeast(new Color32(0, 0, 255, 255), dim),
            white = ColourWeight.AtLeast(new Color32(255, 255, 255, 255), dim);
        report.Expect(dim <= 0f || (black.r == black.g && black.g == black.b && black.r > 0), "black became " + Hex(black));
        // Pure blue is the darkest of the pure hues: it stays as it is unless the least asked is above its own weight.
        report.Expect(dim <= ColourWeight.Of(new Color32(0, 0, 255, 255)) ? Same(blue, new Color32(0, 0, 255, 255)) : blue.b == 255 && blue.r == blue.g && blue.r > 0,
            "pure blue became " + Hex(blue));
        report.Expect(Same(white, new Color32(255, 255, 255, 255)), "white became " + Hex(white));
        report.Note("black → " + Hex(black) + ", pure blue → " + Hex(blue) + ", dark red #800000 → " + Hex(ColourWeight.AtLeast(new Color32(128, 0, 0, 255), dim)));
    }

    // The colour board as a player's colour is made on it (ColourPanel.At and Place, with the players' least weight).
    private static void Board(CheckReport report, float dim)
    {
        report.Rule("every place on the colour board's square is a colour that reads: the board holds no colour a player may not have");
        float darkest = 1f;
        Color32 darkestColour = default;
        for (int h = 0; h < 36; h++)
            for (int x = 0; x <= 16; x++)
                for (int y = 0; y <= 16; y++)
                {
                    Color32 here = ColourPanel.At(h / 36f, x / 16f, y / 16f, dim);
                    float weight = ColourWeight.Of(here);
                    if (weight < darkest) { darkest = weight; darkestColour = here; }
                    report.Expect(weight >= dim, "hue " + h * 10 + "° at " + SettingsWidgets.Bare(x / 16f) + " across, " + SettingsWidgets.Bare(y / 16f) + " up is " + Hex(here)
                        + " weighing " + SettingsWidgets.Bare(weight));
                }
        report.Note("the darkest colour on any square is " + Hex(darkestColour) + " weighing " + SettingsWidgets.Bare(darkest));

        report.Rule("the square's corners are what a player expects: white top left, the hue's own colour top right");
        foreach (int degrees in new[] { 0, 60, 120, 180, 240, 300 })
        {
            Color32 white = ColourPanel.At(degrees / 360f, 0f, 1f, dim), pure = ColourPanel.At(degrees / 360f, 1f, 1f, dim);
            Color32 wanted = ColourWeight.AtLeast(ColourWeight.Colour(degrees / 360f, 1f, 1f), dim);
            report.Expect(Same(white, new Color32(255, 255, 255, 255)) && Near(pure, wanted, 2),
                degrees + "°: top left " + Hex(white) + ", top right " + Hex(pure) + " (the hue's own colour as it may be used is " + Hex(wanted) + ")");
        }

        report.Rule("a colour's mark lies where the colour is: the place found for a colour gives that colour back (within 2 steps a channel)");
        int tried = 0;
        for (int r = 0; r <= 255; r += 17)
            for (int g = 0; g <= 255; g += 17)
                for (int b = 0; b <= 255; b += 17)
                {
                    Color32 colour = ColourWeight.AtLeast(new Color32((byte)r, (byte)g, (byte)b, 255), dim);
                    float hue = 0f, across = 0f;
                    ColourPanel.Place(colour, dim, ref hue, ref across, out float up);
                    Color32 back = ColourPanel.At(hue, across, up, dim);
                    tried++;
                    report.Expect(Near(back, colour, 2), Hex(colour) + " is placed at hue " + SettingsWidgets.Bare(hue * 360f) + "°, " + SettingsWidgets.Bare(across) + " across, "
                        + SettingsWidgets.Bare(up) + " up, where the board holds " + Hex(back));
                }
        report.Note(tried + " colours placed and read back");
    }

    private static bool Near(Color32 a, Color32 b, int steps)
        => Mathf.Abs(a.r - b.r) <= steps && Mathf.Abs(a.g - b.g) <= steps && Mathf.Abs(a.b - b.b) <= steps;

    // The colours a player keeps.
    private static void Slots(CheckReport report, IReadOnlyList<Color32> palette, IReadOnlyList<string> ids, int count, float dim)
    {
        report.Rule("slots never filled start as the palette's colours in order, and nothing is in use");
        var slots = new List<string>();
        string chosen = "";
        int inUse = PlayerColours.FillSlots(slots, ref chosen, count, palette, ids, dim);
        bool first = slots.Count == count && inUse == -1 && chosen == "";
        for (int i = 0; i < count && i < slots.Count; i++) if (slots[i] != Hex(palette[i % palette.Count])) first = false;
        report.Expect(first, "slots " + string.Join(" ", slots) + ", in use " + inUse);

        report.Rule("a colour made in a slot is kept as it was made, and filling again changes nothing");
        slots[count - 1] = "#12c8a0";
        chosen = "#12C8A0";
        inUse = PlayerColours.FillSlots(slots, ref chosen, count, palette, ids, dim);
        var once = new List<string>(slots);
        string onceChosen = chosen;
        int again = PlayerColours.FillSlots(slots, ref chosen, count, palette, ids, dim);
        bool same = inUse == count - 1 && again == inUse && chosen == onceChosen && chosen == "#12C8A0" && slots.Count == once.Count;
        for (int i = 0; i < once.Count && i < slots.Count; i++) if (slots[i] != once[i]) same = false;
        report.Expect(same, "slots " + string.Join(" ", slots) + ", in use " + inUse + " then " + again + ", chosen " + chosen);

        report.Rule("a colour chosen before there were slots (a palette row, a code) is kept: it goes into the first slot when no slot holds it");
        foreach (string before in new[] { ids[Mathf.Min(1, ids.Count - 1)], ids[ids.Count - 1], "#3366CC" })
        {
            slots = new List<string>();
            chosen = before;
            inUse = PlayerColours.FillSlots(slots, ref chosen, count, palette, ids, dim);
            uint wish = PlayerColours.WishOf(before);
            report.Expect(inUse >= 0 && inUse < slots.Count && slots[inUse] == chosen && wish != 0u && PlayerColours.WishOf(chosen) == wish,
                "chosen before: " + before + " → slot " + inUse + " " + chosen + " (the colour asked for was " + Hex(PlayerColours.Colour(wish)) + ")");
        }

        report.Rule("a slot holding anything but a colour starts again as the palette's colour of its place, and a colour too dark is brought up");
        slots = new List<string> { "red", "#000000", "", "#GGGGGG" };
        chosen = "nothing a setting ever held";
        inUse = PlayerColours.FillSlots(slots, ref chosen, count, palette, ids, dim);
        bool mended = inUse == -1 && chosen == "" && slots.Count >= count && slots[0] == Hex(palette[0])
            && (count < 2 || (ColourCode.TryRead(slots[1], out Color32 dark) && ColourWeight.Of(dark) >= dim));
        report.Expect(mended, "slots " + string.Join(" ", slots) + ", in use " + inUse + ", chosen '" + chosen + "'");

        report.Rule("slots kept beyond the number shown are left alone");
        slots = new List<string>();
        for (int i = 0; i < count + 2; i++) slots.Add("#0A0B0C");
        chosen = "";
        PlayerColours.FillSlots(slots, ref chosen, count, palette, ids, dim);
        report.Expect(slots.Count == count + 2 && slots[count] == "#0A0B0C" && slots[count + 1] == "#0A0B0C", "slots " + string.Join(" ", slots));
    }

    // A colour as words, and a wish from words.
    private static void Codes(CheckReport report, float dim)
    {
        report.Rule("a colour's code is six hex digits, with the # or without, and nothing else reads as a colour");
        foreach (string good in new[] { "#FF4F78", "ff4f78", "  #ff4F78 " })
            report.Expect(ColourCode.TryRead(good, out Color32 read) && Hex(read) == "#FF4F78", "'" + good + "' should read as #FF4F78");
        foreach (string bad in new[] { "", "red", "#FFF", "#GGGGGG", "#FF4F7800", "FF4F7", "# FF4F78", null })
            report.Expect(!ColourCode.TryRead(bad, out _), "'" + bad + "' should not read as a colour");

        report.Rule("a wish from a setting is the colour written there, brought up where it is too dark, and nothing for no colour");
        foreach (string code in new[] { "#FF4F78", "#3366CC", "#101010", "#000080" })
        {
            ColourCode.TryRead(code, out Color32 written);
            report.Expect(PlayerColours.WishOf(code) == PlayerColours.Code(ColourWeight.AtLeast(written, dim)),
                code + " asks for " + Hex(PlayerColours.Colour(PlayerColours.WishOf(code))));
        }
        report.Expect(PlayerColours.WishOf("") == 0u && PlayerColours.WishOf(null) == 0u && PlayerColours.WishOf("no such colour") == 0u,
            "an empty setting or one that is no colour should ask for nothing");
    }
}
#endif
