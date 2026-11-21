using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What this player just picked up, right of the screen's middle, as the addon's pickup history (cl_history.lua) lists
/// it: "AMMO PILE*2" in capitals with the thing's icon before it (ammo and items show their own icon, a gun the addon's
/// crate), in a faint rounded box. A new row decodes over two seconds, each letter a random symbol until its turn; the
/// same thing taken again while its row stands adds to its number (counting up) instead of making another. Rows stack
/// upward, the newest on top, every row past the fourth fainter, and each goes after decode + life × its place seconds
/// from its last taking (the draft the user picked keeps a row while it is taken again). FieldPickups says what was
/// taken (PickupFeedHUD.Show hands it here while HUD Mk II stands in); its number is the first number of what the
/// taking gave (health +25 → 25, magazines +2 → 2, money +1,240 → 1240), 1 when it gave none.
/// </summary>
public sealed class MkPickups : MkPiece
{
    private sealed class Row
    {
        public string Name = "";
        public Texture Icon;
        public RawImage Picture;
        public TextMeshProUGUI Text;
        public float Start, Touched, Alpha, Offset, Amount, Shown;
    }

    private readonly MkShapes shapes;
    private readonly List<Row> rows = new List<Row>();
    private readonly Stack<Row> spare = new Stack<Row>();
    private Texture crate;

    public MkPickups(MkHud hud, RectTransform canvas) : base(hud, canvas, "Mk Pickups", new Vector2(1f, .5f))
    {
        shapes = MkShapes.Make("Shapes", Frame);
        crate = MkHud.Icon("weapon");
    }

    /// <summary>One more thing taken: its icon (null for a gun), its name and what it gave.</summary>
    public void Add(Texture icon, string name, string gives, bool gun)
    {
        string key = (name ?? "").ToUpperInvariant();
        float amount = FirstNumber(gives), now = Time.unscaledTime;
        foreach (var row in rows)
            if (row.Name == key)
            {
                row.Amount += amount;
                row.Touched = now;
                return;
            }
        var fresh = spare.Count > 0 ? spare.Pop() : Make();
        fresh.Name = key;
        fresh.Icon = gun || icon == null ? crate : icon;
        fresh.Start = fresh.Touched = now;
        fresh.Alpha = fresh.Offset = fresh.Shown = 0f;
        fresh.Amount = amount;
        fresh.Text.gameObject.SetActive(true);
        rows.Add(fresh);
    }

    // The first whole number in the words (thousands commas allowed), or 1.
    private static float FirstNumber(string words)
    {
        if (string.IsNullOrEmpty(words)) return 1f;
        long value = 0;
        bool found = false;
        foreach (char c in words)
        {
            if (c >= '0' && c <= '9') { value = value * 10 + (c - '0'); found = true; }
            else if (c == ',' && found) continue;
            else if (found) break;
        }
        return found ? value : 1f;
    }

    private Row Make()
    {
        var row = new Row
        {
            Picture = Picture("Pickup icon", 0f, 0f, Look.Taken.icon),
            Text = MkText.Make("Pickup", Frame, Look, 0f, .5f),
        };
        return row;
    }

    public override void Tick(float dt)
    {
        var p = Look.Taken;
        var c = Look.Colour;
        float now = Time.unscaledTime, h = Look.Text.history;
        shapes.Clear();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            int k = i + 1;
            row.Shown = MkText.Follow(row.Shown, row.Amount, p.follow, dt);
            string amount = Mathf.RoundToInt(row.Shown).ToString();
            row.Offset = MkText.Follow(row.Offset, k * (h + p.rowGap), p.follow, dt);
            bool live = now - row.Touched <= p.decode + p.life * k;
            row.Alpha = MkText.Follow(row.Alpha, live ? 1f : 0f, p.follow, dt);
            if (!live && row.Alpha <= .01f)
            {
                row.Text.gameObject.SetActive(false);
                Show(row.Picture, null, Color.clear);
                spare.Push(row);
                rows.RemoveAt(i--);
                continue;
            }
            float place = 1f + Mathf.Clamp((p.clear - k) * p.dim, -1f, 0f), alpha = row.Alpha * place;
            float y = p.y - h * 2f - row.Offset;
            MkText.Size(row.Text, Look, h);
            float w = row.Text.GetPreferredValues(row.Name + "  " + amount).x;
            shapes.Box(p.x - w * p.padX, y - h * p.padY, w * p.padW, h * p.padH, MkText.Fade(Color.black, p.back * alpha), Look.Health.corner);
            MkText.Say(row.Text, MkText.Scramble(row.Name, (now - row.Start) / Mathf.Max(.01f, p.decode), p.chars) + "*" + amount);
            MkText.At(row.Text, p.x + p.textX, y);
            row.Text.color = MkText.Fade(c.text, alpha);
            Place(row.Picture, p.x, y + p.iconDrop, p.icon);
            Show(row.Picture, row.Icon, MkText.Fade(c.text, alpha));
        }
        shapes.Show();
    }
}
