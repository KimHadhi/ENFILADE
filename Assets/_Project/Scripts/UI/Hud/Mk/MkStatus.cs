using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD Mk II's health and armour at the bottom left (the addon's DrawHealth and DrawArmor, placed where the draft the user
/// picked put them): the health bar 375 x 7.5 px in its dark rounded box, lime, red at 30 % and below, its loss trailing
/// in orange; under it the armour (our shield) in ten cells with 0 · 50 · 100 under them, each cut from one fill, its
/// loss trailing too. A shield of more than one bar's worth lays its second bar in blue over the first, a third in
/// orange (the addon's overheal colours).
/// <para>Left of the bars, in the addon's stims tile, the player's own profile picture (the user's note on the pick,
/// 2026-11-22: "내 프로필 아이콘 넣을 공간 넣을 것"): the menu's picture (ProfilePicture), or the name's first letter on
/// the HUD's accent when there is none. Right of the bars, in the addon's armour-plate tile, the class's rank (the
/// insignia, the tile filling from below to the next one: white what was held when the game began, the accent what this
/// game added; "+N XP" floats over it at a kill, "+N" in its corner for ranks climbed this game), and past it the stance
/// the body is in (StanceFigure's pictures, pink while down). The old portrait, rank strip, number row, bar and stance
/// figure had no Mk II slot of their own; these tiles are where they went.</para>
/// </summary>
public sealed class MkStatus : MkPiece
{
    private readonly MkShapes shapes;
    private readonly RawImage picture, insignia, stance;
    private readonly TextMeshProUGUI letter, climb, gain;
    private readonly TextMeshProUGUI[] ticks = new TextMeshProUGUI[3];
    private readonly MkTrail health = new MkTrail(), armor = new MkTrail();
    private Color healthColour;
    private bool coloured;
    private long lastKills = -1;
    private float gainAge = float.MaxValue, gainAmount, popAge = 1f;
    private StanceFigure.Pose pose = StanceFigure.Pose.None;

    public MkStatus(MkHud hud, RectTransform canvas) : base(hud, canvas, "Mk Status", Vector2.zero)
    {
        var s = Look.Health;
        shapes = MkShapes.Make("Shapes", Frame);
        picture = Picture("Profile", s.profileX, s.tileY, s.tile - 2f * s.profileInset);
        letter = MkText.Make("Initial", Frame, Look, .5f, .5f);
        letter.color = KitStyle.Ink;
        insignia = Picture("Rank", s.rankX, s.tileY, s.tile);
        stance = Picture("Stance", s.stanceX, s.tileY, s.tile);
        climb = MkText.Make("Climb", Frame, Look, 1f, 0f);
        gain = MkText.Make("Gain", Frame, Look, .5f, 1f);
        for (int i = 0; i < ticks.Length; i++) ticks[i] = MkText.Make("Tick " + i, Frame, Look, i * .5f, .5f);
        ProfilePicture.Changed += Paint;
        Paint();
    }

    public override void Dispose() => ProfilePicture.Changed -= Paint;

    // The menu's profile picture, or the first letter on the accent.
    private void Paint()
    {
        ProfilePicture.Paint(picture, letter, GameSettings.Current.DisplayName);
        picture.enabled = true;
        if (ProfilePicture.Current == null) picture.color = Look.Colour.accent;
    }

    public override void Tick(float dt)
    {
        var s = Look.Health;
        var c = Look.Colour;
        var player = Hud.Player;
        var receiver = player != null ? player.DamageReceiver : null;
        float maximum = receiver != null ? Mathf.Max(1f, receiver.MaximumHealth) : 100f;
        float value = receiver != null ? receiver.Health / maximum : 0f;
        // The shield measured against a bar of the larger of the health's maximum and Status.armorLeast, as the old band.
        float shield = receiver != null ? receiver.Shield / Mathf.Max(maximum, s.armorLeast) : 0f;
        health.Tick(value, dt, s.follow, s.wait, s.lag);
        armor.Tick(shield, dt, s.follow, s.wait, s.lag);
        Color wanted = value <= s.low ? c.low : c.health;
        healthColour = coloured ? MkText.Follow(healthColour, wanted, s.colourFollow, dt) : wanted;
        coloured = true;

        shapes.Clear();
        float w = s.width, pad = s.pad, left = s.x - w * .5f;
        // Health (BL_Rect).
        float top = s.y - s.height * .5f;
        shapes.Box(left - pad, top - pad, w + pad * 2f, s.height + pad * 2f, c.background, s.corner);
        shapes.Box(left, top, w * Mathf.Clamp01(health.Delta), s.height, c.loss);
        shapes.Box(left, top, w * Mathf.Clamp01(health.Shown), s.height, healthColour);
        if (value > 1f) shapes.Box(left, top, w * Mathf.Clamp01(health.Shown - 1f), s.height, c.over);
        if (value > 2f) shapes.Box(left, top, w * Mathf.Clamp01(health.Shown - 2f), s.height, c.overMore);
        // Armour (BSS_Rect): its box reaches down over the 0 · 50 · 100 under it.
        float cell = Look.Text.label, armorTop = s.armorY - s.armorHeight * .5f;
        shapes.Box(left - pad, armorTop - pad, w + pad * 2f, s.armorHeight + cell + pad * 2f, c.background, s.corner);
        Cells(left, armorTop, w, s.armorHeight, armor.Delta, c.loss);
        Cells(left, armorTop, w, s.armorHeight, armor.Shown, c.armor);
        if (shield > 1f) Cells(left, armorTop, w, s.armorHeight, armor.Shown - 1f, c.over);
        if (shield > 2f) Cells(left, armorTop, w, s.armorHeight, armor.Shown - 2f, c.overMore);
        for (int i = 0; i < ticks.Length; i++)
        {
            MkText.Label(ticks[i], Look, i == 0 ? "0" : i == 1 ? "50" : "100");
            MkText.At(ticks[i], left + w * .5f * i, s.armorY + cell);
            ticks[i].color = c.tag;
        }

        // The profile tile (the addon's stims tile, BT_Text): its box, the picture inside.
        float t = s.tile;
        shapes.Box(s.profileX - t * .5f, s.tileY - t * .5f, t, t, c.background, s.corner);
        MkText.Size(letter, Look, t * s.letter * Look.Text.em);
        MkText.At(letter, s.profileX, s.tileY);

        Rank(dt, s, c);
        Stance(player, dt, s, c);
        shapes.Show();
    }

    // Ten cells cut from one fill (the addon's stencil): cell i starts at i tenths and is Status.cell of a tenth wide.
    private void Cells(float left, float top, float width, float height, float value, Color colour)
    {
        int parts = Mathf.Max(1, Look.Health.parts);
        float pitch = width / parts, wide = pitch * Look.Health.cell, filled = width * Mathf.Clamp01(value);
        for (int i = 0; i < parts; i++)
        {
            float x = pitch * i, part = Mathf.Clamp(filled - x, 0f, wide);
            if (part > 0f) shapes.Box(left + x, top, part, height, colour);
        }
    }

    // The class's rank in the armour-plate tile (BTC_Text: a fill rising from the foot, the picture over it).
    private void Rank(float dt, MkLook.Status s, MkLook.Palette c)
    {
        var view = RankProgress.Now();
        float t = s.tile, foot = s.tileY + t * .5f, x = s.rankX - t * .5f;
        shapes.Box(x, s.tileY - t * .5f, t, t, c.background, s.corner);
        float held = Mathf.Clamp01(view.StoredShare), total = Mathf.Clamp01(view.Share);
        shapes.Box(x, foot - t * held, t, t * held, MkText.Fade(c.text, s.fillAlpha));
        if (total > held) shapes.Box(x, foot - t * total, t, t * (total - held), MkText.Fade(c.accent, s.fillAlpha));
        Show(insignia, ClassGrowth.Insignia(view.Rank), MkText.Fade(c.text, s.iconAlpha));
        MkText.Size(climb, Look, Look.Text.alts);
        MkText.Say(climb, view.Climb > 0 ? "+" + view.Climb : "");
        MkText.At(climb, s.rankX + t / 2.5f, s.tileY - t * .5f);
        climb.color = c.accent;
        // A kill's experience floats up over the tile (RankStrip's "+100 XP").
        if (lastKills >= 0 && view.Kills > lastKills && view.Playing)
        {
            gainAmount = gainAge < .4f ? gainAmount + (view.Kills - lastKills) : view.Kills - lastKills;
            gainAge = 0f;
            MkText.Label(gain, Look, Texts.Format("hud.rank.gain", gainAmount.ToString("N0")));
        }
        lastKills = view.Kills;
        if (gainAge < s.gainSeconds)
        {
            gainAge += dt;
            float k = Mathf.Clamp01(gainAge / Mathf.Max(.05f, s.gainSeconds));
            MkText.At(gain, s.rankX, s.tileY - t * .5f - 2f - s.gainRise * k);
            gain.color = MkText.Fade(c.accent, 1f - k * k);
        }
        else if (gain.text.Length > 0) MkText.Say(gain, "");
    }

    // The stance the body is in, in its own tile past the rank; pops as it changes, pink while down.
    private void Stance(PlayerMovement player, float dt, MkLook.Status s, MkLook.Palette c)
    {
        var now = StanceFigure.Of(player);
        if (now != pose) { pose = now; popAge = 0f; }
        if (pose == StanceFigure.Pose.None) { Show(stance, null, Color.clear); return; }
        float t = s.tile;
        shapes.Box(s.stanceX - t * .5f, s.tileY - t * .5f, t, t, c.background, s.corner);
        popAge += dt;
        float k = Mathf.Clamp01(popAge / Mathf.Max(.01f, s.popSeconds)), eased = 1f - (1f - k) * (1f - k) * (1f - k);
        stance.rectTransform.localScale = Vector3.one * Mathf.Lerp(Mathf.Max(1f, s.popScale), 1f, eased);
        Color rest = pose == StanceFigure.Pose.Downed ? c.accent : c.text;
        Show(stance, StanceFigure.Picture(pose), MkText.Fade(Color.Lerp(c.accent, rest, eased), Mathf.Lerp(1f, s.iconAlpha, eased)));
    }
}
