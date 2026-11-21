using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tiles stacked over the reserve box (the addon's equipment tile, Nene.BT_Text, 55 * Scale square: the frag count
/// two tiles up, the tactical knife's 3.1 up; the draft the user picked put the class and the ultimate in that column):
/// <list type="bullet">
/// <item>The class throwable (PlayerGrenades): its picture, how many are left in the corner, dimmed when none, its key.</item>
/// <item>The class's own resource (ClassReadout, as ClassHUD works it out): the class icon, its ticks up the tile's
/// left (adrenaline, Head Hunter, rounds, doses, the bag, the Point's two guns), the tile filling from the foot as they
/// fill, breathing in the accent while the class key has something to do, the key in the corner. What the old plate
/// said beside its pips stands left of the tile in Mk II's words: the key's word, DEADEYE or DESPERADO, the wait, the
/// offer, the bag; and under that what the player has standing in the world, a line a kind ("판자 1/2", amber when the
/// next one set down takes the oldest away, pink while it is the one being placed: DeployStrip's rules).</item>
/// <item>The ultimate: ten ticks along the foot, one a tenth of a use (the old magazine's rounds), the tile filling as
/// they come and breathing in the accent once full (the user's draft: "1 tick = 10%, full = accent").</item>
/// </list>
/// Every breath is a smooth sine under 1.6 Hz that never dims below about half (no blinking).
/// </summary>
public sealed class MkGear : MkPiece
{
    private sealed class Tile
    {
        public RawImage Icon;
        public TextMeshProUGUI Count, Key;
        public Color Colour = Color.white;
    }

    private readonly MkShapes shapes;
    private readonly Tile grenade, kit, ult;
    private readonly TextMeshProUGUI word, mark, note;
    private readonly List<TextMeshProUGUI> deploys = new List<TextMeshProUGUI>();
    private readonly List<Deployables.Line> lines = new List<Deployables.Line>();
    private Texture ultIcon;

    public MkGear(MkHud hud, RectTransform canvas) : base(hud, canvas, "Mk Gear", new Vector2(1f, 0f))
    {
        shapes = MkShapes.Make("Shapes", Frame);
        grenade = Make("Throwable");
        kit = Make("Class");
        ult = Make("Ultimate");
        word = MkText.Make("Class word", Frame, Look, 1f, 0f);
        mark = MkText.Make("Class mark", Frame, Look, 1f, 0f);
        note = MkText.Make("Class note", Frame, Look, 1f, 0f);
        ultIcon = MkHud.Icon("shock");
    }

    private Tile Make(string name) => new Tile
    {
        Icon = Picture(name, 0f, 0f, Look.Kit.tile),
        Count = MkText.Make(name + " count", Frame, Look, 1f, 0f),
        Key = MkText.Make(name + " key", Frame, Look, 0f, 1f),
    };

    public override void Tick(float dt)
    {
        var g = Look.Kit;
        var player = Hud.Player;
        bool alive = player != null && !player.IsDead;
        Group.alpha = alive ? 1f : 0f;
        if (!alive) return;
        shapes.Clear();
        float y = Look.Gun.y;
        Grenade(y - g.tile * g.grenadeRise, dt);
        float classY = y - g.tile * g.classRise;
        Class(classY, dt);
        Ultimate(player, y - g.tile * g.ultRise, dt);
        shapes.Show();
    }

    // The tile's box, its picture, the count in its corner and the key at its foot (BT_Text).
    // A tile whose box is drawn already, under its fill and ticks, passes box false.
    private void Draw(Tile tile, float y, Texture picture, string count, string key, Color colour, bool box = true)
    {
        var g = Look.Kit;
        float t = g.tile, x = g.x;
        if (box) shapes.Box(x - t * .5f, y - t * .5f, t, t, Look.Colour.background, Look.Health.corner);
        Place(tile.Icon, x, y, t);
        Show(tile.Icon, picture, MkText.Fade(colour, g.iconAlpha));
        MkText.Size(tile.Count, Look, Look.Text.alts);
        MkText.Say(tile.Count, count);
        MkText.At(tile.Count, x + t / 2.5f, y - t * .5f);
        tile.Count.color = colour;
        MkText.Label(tile.Key, Look, key);
        MkText.At(tile.Key, x - t * .5f + g.keyPad, y + t * .5f - g.keyPad);
        tile.Key.color = Look.Colour.tag;
    }

    private void Hide(Tile tile)
    {
        Show(tile.Icon, null, Color.clear);
        MkText.Say(tile.Count, "");
        MkText.Say(tile.Key, "");
    }

    private Color Dim(Color colour) => new Color(colour.r / Look.Colour.empty, colour.g / Look.Colour.empty, colour.b / Look.Colour.empty, colour.a);

    private void Grenade(float y, float dt)
    {
        var grenades = PlayerGrenades.Local;
        if (grenades == null || grenades.Kind == Throwable.None) { Hide(grenade); return; }
        int count = grenades.Count;
        grenade.Colour = MkText.Follow(grenade.Colour, count <= 0 ? Dim(Look.Colour.text) : Look.Colour.text, Look.Kit.colourFollow, dt);
        Draw(grenade, y, Throwables.Icon(grenades.Kind), count.ToString(), KeyBindings.Name("Player/Grenade"), grenade.Colour);
    }

    private void Class(float y, float dt)
    {
        var g = Look.Kit;
        var c = Look.Colour;
        var read = ClassReadout.Local;
        if (read == null || read.Hidden)
        {
            Hide(kit);
            MkText.Say(word, ""); MkText.Say(mark, ""); MkText.Say(note, "");
            Deploys(float.NaN);
            return;
        }
        float t = g.tile, left = g.x - t * .5f, top = y - t * .5f, foot = y + t * .5f;
        int count = Mathf.Clamp(read.Count, 0, ClassReadout.MostTicks);
        float share = count > 0 ? (read.Guns ? Average(read, count) : read.Lit / (float)count) : read.KeyReady ? 1f : 0f;
        bool any = count > 0 || read.KeyReady;
        kit.Colour = MkText.Follow(kit.Colour, any ? c.text : Dim(c.text), g.colourFollow, dt);
        // The box first, then its fill from the foot, the ticks up its left, the picture over them.
        shapes.Box(left, top, t, t, c.background, Look.Health.corner);
        Color fill = read.KeyReady ? MkText.Fade(c.accent, Breath(g.readyFloor, g.readyRate)) : MkText.Fade(c.text, g.fill);
        shapes.Box(left, foot - t * share, t, t * share, fill);
        float step = count > 0 ? (t - 2f * g.tickPad) / count : 0f;
        for (int i = 0; i < count; i++)
        {
            Color hue = read.Colour[i];
            hue.a = g.tickOn;
            Color tick = Color.Lerp(MkText.Fade(c.text, g.tickOff), hue, Mathf.Clamp01(read.Fill[i]));
            shapes.Box(left + g.tickPad, foot - g.tickPad - (i + 1) * step, g.tickThick, step - g.tickGap, tick);
        }
        Draw(kit, y, read.Icon, count > 0 && !read.Guns ? read.Lit.ToString() : "", read.Key, kit.Colour, false);
        // Its words, right-aligned left of the tile, top to bottom: the key's word, the mark, the note.
        float right = left - g.wordsGap, line = top;
        MkText.Size(word, Look, Look.Text.name);
        MkText.Say(word, read.KeyReady ? read.Word : "");
        MkText.At(word, right, line);
        word.color = read.WordColour;
        if (word.text.Length > 0) line += Look.Text.name;
        MkText.Size(mark, Look, Look.Text.name);
        MkText.Say(mark, read.MarkShown ? MkText.Spaced(read.Mark) : "");
        MkText.At(mark, right, line);
        mark.color = read.MarkColour;
        if (mark.text.Length > 0) line += Look.Text.name;
        MkText.Label(note, Look, read.Note);
        MkText.At(note, right, line);
        note.color = c.tag;
        if (note.text.Length > 0) line += g.lineStep;
        Deploys(line);
    }

    private static float Average(ClassReadout read, int count)
    {
        float sum = 0f;
        for (int i = 0; i < count; i++) sum += Mathf.Clamp01(read.Fill[i]);
        return sum / count;
    }

    // What the player has standing, a line a kind under the class's words (Deployables, DeployStrip's colours).
    private void Deploys(float top)
    {
        var player = Hud.Player;
        if (float.IsNaN(top) || player == null) lines.Clear();
        else Deployables.Readout(player, lines);
        string chosen = lines.Count > 0 ? ClassHUD.Placing() ?? "" : "";
        float breath = .775f + .225f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI * 1.2f);
        for (int i = 0; i < Mathf.Max(lines.Count, deploys.Count); i++)
        {
            if (i == deploys.Count) deploys.Add(MkText.Make("Deployed " + i, Frame, Look, 1f, 0f));
            var text = deploys[i];
            if (i >= lines.Count) { MkText.Say(text, ""); continue; }
            var line = lines[i];
            bool picked = chosen.Length > 0 && string.Equals(chosen, line.Kind.Id, System.StringComparison.OrdinalIgnoreCase);
            MkText.Label(text, Look, line.Kind.Short + "  " + line.Count + "/" + line.Most);
            MkText.At(text, Look.Kit.x - Look.Kit.tile * .5f - Look.Kit.wordsGap, top + i * Look.Kit.lineStep);
            Color colour = line.Full ? MkText.Fade(DeployStrip.Amber, picked ? breath : 1f) : picked ? Look.Colour.accent : Look.Colour.text;
            text.color = MkText.Fade(colour, line.Held ? 1f : .4f);
        }
    }

    private void Ultimate(PlayerMovement player, float y, float dt)
    {
        var g = Look.Kit;
        var c = Look.Colour;
        var charge = player.Ultimate;
        if (charge == null || charge.RequiredCharge <= 0f || charge.MaximumCharge <= 0f) { Hide(ult); return; }
        int ticks = Mathf.Max(1, g.ultTicks);
        float uses = charge.CurrentCharge / charge.RequiredCharge;
        int rounds = Mathf.Min(ticks, Mathf.FloorToInt(uses * ticks + 1e-3f));
        bool full = rounds >= ticks;
        float t = g.tile, left = g.x - t * .5f, top = y - t * .5f, foot = y + t * .5f;
        ult.Colour = MkText.Follow(ult.Colour, rounds > 0 ? c.text : Dim(c.text), g.colourFollow, dt);
        shapes.Box(left, top, t, t, c.background, Look.Health.corner);
        float share = rounds / (float)ticks;
        shapes.Box(left, foot - t * share, t, t * share, full ? MkText.Fade(c.accent, Breath(g.ultFloor, g.ultRate)) : MkText.Fade(c.text, g.fill));
        float step = (t - 2f * g.tickPad) / ticks;
        for (int i = 0; i < ticks; i++)
            shapes.Box(left + g.tickPad + i * step, foot - g.tickPad - g.tickThick, step - g.tickGap, g.tickThick,
                MkText.Fade(c.text, i < rounds ? g.tickOn : g.tickOff));
        int whole = Mathf.FloorToInt(uses + 1e-3f);
        Draw(ult, y, ultIcon, full ? (whole > 1 ? "x" + whole : "") : rounds.ToString(), KeyBindings.Name("Player/PerkCards"), ult.Colour, false);
    }

    // A slow, smooth swell: from the floor up by Gear.readyAmp, so many times a second (never a blink).
    private float Breath(float floor, float rate) => floor + Look.Kit.readyAmp * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI * rate);
}
