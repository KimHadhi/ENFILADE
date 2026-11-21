using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The timed effects on this PC's player (TimedEffects), a tile each in a row over the bars, in the look of the addon's
/// hazard tiles (Nene.Hazards) as the draft the user picked drew them (2026-11-22): a dark square whose darkness breathes
/// slowly (|sin 2t|, never down to nothing: no blinking), the accent filling it from the foot as much of the effect as is
/// left, the effect's icon over it in white, and its name spaced out beside it with the seconds left. New ones fade in at
/// the row's end and the rest slide over as one ends (the addon's x easing). Nothing chooses between effects: every one
/// that runs has its tile, as EffectStack gave each its slot.
/// </summary>
public sealed class MkEffects : MkPiece
{
    private sealed class Tile
    {
        public string Id = "", Words = "";
        public RawImage Icon;
        public TextMeshProUGUI Tag;
        public float Alpha, X = float.NaN, Share, Left, Value = float.NaN, ShownTenth = -1f;
        public int Stacks = -1;
        public bool Live;
        public Texture Picture;
    }

    private readonly MkShapes shapes;
    private readonly List<Tile> tiles = new List<Tile>();

    public MkEffects(MkHud hud, RectTransform canvas) : base(hud, canvas, "Mk Effects", Vector2.zero)
    {
        shapes = MkShapes.Make("Shapes", Frame);
    }

    public override void Tick(float dt)
    {
        var e = Look.Timed;
        var running = TimedEffects.Running;
        foreach (var tile in tiles) tile.Live = false;
        bool alive = Hud.Player != null && !Hud.Player.IsDead;
        for (int i = 0; alive && i < running.Count; i++)
        {
            var entry = running[i];
            if (!entry.Kind.Shown) continue;
            var tile = Find(entry.Kind.Id) ?? Make(entry.Kind);
            tile.Live = true;
            tile.Share = entry.Share;
            tile.Left = entry.Left;
            if (tile.Value != entry.Value || tile.Stacks != entry.Stacks || tile.Words.Length == 0)
            {
                tile.Value = entry.Value;
                tile.Stacks = entry.Stacks;
                tile.Words = MkText.Spaced(entry.Kind.Name(entry.Value, entry.Stacks));
                tile.ShownTenth = -1f;
            }
        }
        // In the order they began, the ended ones fading where they stand.
        shapes.Clear();
        float breath = e.breathFloor + (1f - e.breathFloor) * Mathf.Abs(Mathf.Sin(Time.unscaledTime * e.breathRate));
        int k = 0;
        for (int i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            tile.Alpha = Mathf.MoveTowards(tile.Alpha, tile.Live ? 1f : 0f, dt * (tile.Live ? e.fadeIn : e.fadeOut));
            if (!tile.Live && tile.Alpha <= 0f)
            {
                Object.Destroy(tile.Icon.gameObject);
                Object.Destroy(tile.Tag.gameObject);
                tiles.RemoveAt(i--);
                continue;
            }
            float slot = k * e.step;
            tile.X = float.IsNaN(tile.X) ? slot : MkText.Follow(tile.X, slot, e.follow, dt);
            if (tile.Live) k++;
            Draw(tile, breath, e);
        }
        shapes.Show();
    }

    private void Draw(Tile tile, float breath, MkLook.Effects e)
    {
        var c = Look.Colour;
        float size = e.size, x = e.x + size * tile.X * e.spread, y = e.y;
        shapes.Box(x, y, size, size, MkText.Fade(Color.black, (e.back + e.breath * breath) * tile.Alpha));
        float fill = size * Mathf.Clamp01(tile.Share);
        shapes.Box(x, y + size - fill, size, fill, MkText.Fade(c.accent, e.fill * tile.Alpha));
        Place(tile.Icon, x + size * .5f, y + size * .5f, size - 2f * e.inset);
        Show(tile.Icon, tile.Picture, MkText.Fade(c.text, tile.Alpha));
        // Its seconds left to a tenth, written only when the tenth changes.
        float tenth = Mathf.Ceil(Mathf.Max(0f, tile.Left) * 10f) / 10f;
        if (!Mathf.Approximately(tenth, tile.ShownTenth))
        {
            tile.ShownTenth = tenth;
            MkText.Label(tile.Tag, Look, tile.Words + "  " + tenth.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        }
        MkText.At(tile.Tag, x + size + e.labelGap, y + size * .5f);
        tile.Tag.color = MkText.Fade(c.text, tile.Alpha);
    }

    private Tile Find(string id)
    {
        foreach (var tile in tiles)
            if (tile.Id == id) return tile;
        return null;
    }

    private Tile Make(TimedEffects.Kind kind)
    {
        var tile = new Tile
        {
            Id = kind.Id,
            Icon = Picture("Effect " + kind.Id, 0f, 0f, Look.Timed.size),
            Tag = MkText.Make("Effect " + kind.Id + " name", Frame, Look, 0f, .5f),
            Picture = kind.Icon.Length > 0 ? KitButtons.Icon(kind.Icon) : null,
        };
        tiles.Add(tile);
        return tile;
    }
}
