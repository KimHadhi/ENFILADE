using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Teammates above the player's own bars, a row each from the top down (the draft the user picked, 2026-11-22, in the
/// addon's vocabulary): a short bar in the teammate's own colour before the name, a dark rounded box, the health as a
/// 3 px line (lime, red at 30 % and below) and the shield as ten 2 px cells under it. What the old cards also said keeps
/// its place: someone down shows the time left to get them up as the line, in the downed colour, with the word at the
/// row's right end; someone dead or still waiting to join greys out with theirs. With more teammates than Team.limit
/// the rows go away and only the marks over their heads stay (as TeamHealthPanel did).
/// </summary>
public sealed class MkTeam : MkPiece
{
    private sealed class Row
    {
        public TextMeshProUGUI Name, State;
    }

    private readonly MkShapes shapes;
    private readonly List<Row> rows = new List<Row>();

    public MkTeam(MkHud hud, RectTransform canvas) : base(hud, canvas, "Mk Team", Vector2.zero)
    {
        shapes = MkShapes.Make("Shapes", Frame);
    }

    public override void Tick(float dt)
    {
        var t = Look.Mates;
        var c = Look.Colour;
        var me = Hud.Player;
        int count = 0;
        foreach (var other in PlayerRoster.All)
            if (IsTeammate(other, me)) count++;
        shapes.Clear();
        int used = 0;
        if (me != null && count <= t.limit)
            foreach (var other in PlayerRoster.All)
            {
                if (!IsTeammate(other, me)) continue;
                if (used == rows.Count) rows.Add(Make());
                Draw(rows[used], other, t.y + used * t.step, t, c);
                used++;
            }
        for (int i = used; i < rows.Count; i++)
        {
            MkText.Say(rows[i].Name, "");
            MkText.Say(rows[i].State, "");
        }
        shapes.Show();
    }

    private static bool IsTeammate(PlayerMovement other, PlayerMovement me)
        => other != null && other != me && other.IsInitialized && other.DamageReceiver != null;

    private Row Make()
    {
        var row = new Row
        {
            Name = MkText.Make("Name", Frame, Look, 0f, 0f),
            State = MkText.Make("State", Frame, Look, 1f, 0f),
        };
        row.Name.characterSpacing = Look.Mates.nameSpacing;
        return row;
    }

    private void Draw(Row row, PlayerMovement other, float y, MkLook.Team t, MkLook.Palette c)
    {
        var health = other.DamageReceiver;
        bool named = other.TryGetComponent(out PlayerTally tally);
        string name = named && !string.IsNullOrEmpty(tally.DisplayName) ? tally.DisplayName : other.name;
        Color seat = named ? tally.Color : c.text;
        float x = t.x, w = t.width, nameTop = y - t.nameRise;
        bool alive = health.IsAlive;
        // The seat colour's short bar before the name, then the name.
        shapes.Box(x, nameTop + 1f, t.seatWidth, t.seatHeight, alive ? seat : c.gone);
        MkText.Size(row.Name, Look, t.nameSize);
        MkText.Say(row.Name, name.ToUpperInvariant());
        MkText.At(row.Name, x + t.seatWidth + t.seatGap, nameTop);
        row.Name.color = alive ? c.text : c.gone;
        // The box, the health line, the shield cells.
        shapes.Box(x - t.back, y - t.back, w + 2f * t.back, t.box, c.background, Look.Health.corner);
        float share, shield = 0f;
        Color line;
        string state = "";
        if (!alive)
        {
            // A friend who joined a run under way and waits to come in never died (PlayerDamageReceiver.IsWaiting).
            state = Texts.Get(health.IsWaiting ? "team.joining" : "team.dead");
            share = 0f;
            line = c.gone;
        }
        else if (other.IsDowned)
        {
            // The line is the time left to get them up now, not health: no shield under it.
            share = other.TryGetComponent(out DownedAura aura) ? 1f - aura.Urgency : 0f;
            line = HealthSegments.Downed;
            state = Texts.Get("team.downed");
        }
        else
        {
            share = Mathf.Clamp01(health.Health / Mathf.Max(1f, health.MaximumHealth));
            line = share <= t.low ? c.low : c.health;
            shield = health.Shield / Mathf.Max(health.MaximumHealth, Look.Health.armorLeast);
        }
        shapes.Box(x, y, w * share, t.hp, line);
        int cells = Mathf.Max(1, t.cells);
        float pitch = w / cells, wide = pitch * t.cell;
        for (int k = 0; k < cells; k++)
        {
            float left = x + k * pitch;
            shapes.Box(left, y + t.cellY, wide, t.cellHeight, MkText.Fade(c.text, t.cellBack));
            shapes.Box(left, y + t.cellY, wide * Mathf.Clamp01(shield * cells - k), t.cellHeight, c.armor);
        }
        MkText.Label(row.State, Look, state);
        MkText.At(row.State, x + w, nameTop);
        row.State.color = other.IsDowned && alive ? HealthSegments.Downed : c.tag;
    }
}
