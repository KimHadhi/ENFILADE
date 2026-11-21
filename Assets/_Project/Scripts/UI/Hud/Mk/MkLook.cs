using System;
using UnityEngine;

/// <summary>
/// HUD Mk II's numbers, every one from Tables/Hud/MkHud.csv (a row a class here). Places are px on a 1920 x 1080 screen
/// from its top left, y down, as the Garry's Mod addon "HUD Mk II" draws them at that size (its Scale, ScreenScale(0.25),
/// is 0.75 there, so its "500 * Scale" bar is 375 px); each piece keeps to the screen corner it stands nearest
/// (MkHud.Frame), so another screen shape keeps the margins. Font sizes are the addon's GMod sizes: a GMod size is a GDI
/// cell height, and Hitmarker Normal's cell is 1.25 em (Text.em), so 9.6 draws at 7.68 px. Colours are the addon's, with
/// one accent, the HUD's pink (the user's pick of the HUD drafts, 2026-11-22: the Mk II card "좋아").
/// </summary>
public sealed class MkLook
{
    /// <summary>The canvas and what moves every piece (row Hud).</summary>
    [Serializable]
    public sealed class Frame
    {
        /// <summary>HUD Mk II stands in for the old HUD (MkHud.On reads the table itself, before anything is bound).</summary>
        public bool on = true;
        public int order = 26;
        /// <summary>Sway: px per degree the look has run ahead of its lagged copy (addon sway_intensity 2), how fast the
        /// copy follows (1/s, LerpAngle(FrameTime * 10)), and the most it moves (px).</summary>
        public float sway = 2f, swayFollow = 10f, swayMost = 40f;
        /// <summary>Pieces turn with a Q/E lean as the rest of the HUD does (HudCamera.LeanRoll).</summary>
        public bool lean = true;
        /// <summary>How long the pieces fade as the Tab screen comes up (1/s).</summary>
        public float tabFade = 1f;
    }

    /// <summary>The colours (row Colour).</summary>
    [Serializable]
    public sealed class Palette
    {
        public Color health = new Color(223f / 255f, 1f, 143f / 255f), armor = Color.white, low = Color.red;
        public Color loss = new Color(1f, 150f / 255f, 0f), over = new Color(150f / 255f, 150f / 255f, 1f), overMore = new Color(1f, 150f / 255f, 20f / 255f);
        public Color text = Color.white, tag = new Color(155f / 255f, 155f / 255f, 155f / 255f), background = new Color(0f, 0f, 0f, 50f / 255f);
        public Color accent = new Color(1f, .31f, .47f), rest = new Color(.43f, .66f, 1f), gone = new Color(.6f, .62f, .66f);
        /// <summary>An empty resource's colour is the colour divided by this (addon: Color(r / 1.75, ...)).</summary>
        public float empty = 1.75f;
    }

    /// <summary>The type (row Text).</summary>
    [Serializable]
    public sealed class Typeface
    {
        public float em = 1.25f, outline = .2f, outlineAlpha = .22f, hangul = 10f;
        public float label = 9.6f, mags = 28.8f, ammo = 19.2f, alts = 14.4f, history = 21.12f, name = 16.8f;
    }

    /// <summary>Health, armour, the profile tile and the tiles past the bars (row Status).</summary>
    [Serializable]
    public sealed class Status
    {
        public float x = 317f, y = 1009.8f, width = 375f, height = 7.5f, pad = 3.75f, corner = 6f;
        public float armorY = 1026f, armorHeight = 6f, cell = .9f, armorLeast = 100f;
        public int parts = 10;
        public float follow = 10f, wait = .5f, lag = 5f, low = .3f, colourFollow = 10f;
        public float tile = 41.25f, tileY = 1022.9f, profileX = 96.5f, profileInset = 2f, letter = .5f;
        public float rankX = 537.5f, stanceX = 589.1f, iconAlpha = 150f / 255f, fillAlpha = 100f / 255f;
        public float gainSeconds = .9f, gainRise = 10f, popSeconds = .14f, popScale = 1.35f;
    }

    /// <summary>Teammates' rows (row Team).</summary>
    [Serializable]
    public sealed class Team
    {
        public float x = 129f, y = 880f, step = 27f, width = 168f, back = 2.5f, box = 9.5f, hp = 3f;
        public float cellY = 4.5f, cellHeight = 2f, cell = .9f, cellBack = .08f;
        public int cells = 10, limit = 3;
        public float nameRise = 14f, nameSize = 13.44f, nameSpacing = 8f, seatWidth = 3f, seatHeight = 9f, seatGap = 6f, low = .3f;
    }

    /// <summary>Timed effects, a tile each (row Effects).</summary>
    [Serializable]
    public sealed class Effects
    {
        public float x = 129f, y = 962f, size = 28.8f, step = 1.25f, spread = 4.2f, inset = 2f;
        public float fill = .55f, back = .2f, breath = .19f, breathRate = 2f, breathFloor = .55f;
        public float fadeIn = 3f, fadeOut = 2f, follow = 10f, labelGap = 6f;
    }

    /// <summary>The gun in hand (row Ammo).</summary>
    [Serializable]
    public sealed class Ammo
    {
        public float x = 1680f, y = 972f, magWidth = 93.75f, magHeight = 37.5f, reserveWidth = 56.25f, reserveSpread = 1.95f;
        public float textAt = 2.5f, nameX = .45f, nameY = .35f, ghost = 50f / 255f, reserveAlpha = 150f / 255f, iconAlpha = 150f / 255f;
        public float flickAlpha = 100f / 255f, flickFade = 3f, flickFollow = 14f, low = .3f, follow = 10f, colourFollow = 10f;
        public float reloadGap = 4f, reloadHeight = 2f, reloadAlpha = .7f, statusGap = 9f, rankUpSeconds = 2.5f;
    }

    /// <summary>The tiles over the reserve: throwable, class, ultimate (row Gear).</summary>
    [Serializable]
    public sealed class Gear
    {
        public float x = 1762.5f, tile = 41.25f, grenadeRise = 2f, classRise = 3.1f, ultRise = 4.2f, iconAlpha = 150f / 255f;
        public float tickOn = .9f, tickOff = .22f, tickPad = 3f, tickGap = 1.5f, tickThick = 2f;
        public float fill = .3f, readyFloor = .45f, readyAmp = .15f, readyRate = .8f, ultFloor = .5f, ultRate = .7f;
        public int ultTicks = 10;
        public float keyPad = 3f, wordsGap = 8f, lineStep = 12f, colourFollow = 10f;
    }

    /// <summary>What was picked up (row Pickups).</summary>
    [Serializable]
    public sealed class Pickups
    {
        public float x = 1392f, y = 810f, decode = 2f, life = 3f, icon = 22.5f, iconDrop = 3f, textX = 11.25f, rowGap = 11.25f;
        public float follow = 10f, back = 50f / 255f, padX = .075f, padW = 1.15f, padY = .45f, padH = 1.2f, dim = .3f;
        public int clear = 4;
        public string chars = "!@#$%^&*()_+{}:<>?~/|`";
    }

    /// <summary>
    /// The weapon slots in Mk II's look (row Slots). WeaponSlotsHUD keeps drawing them as it always did, in Mk II's frame
    /// (MkHud.Lend), because the user wanted the old slots back in the new look (2026-11-22: "무기 슬롯 1,2,3,4번 그거는
    /// 기존 방식이 더 맘에 들어. 기존 방식으로 디자인만 바꿔서"). The column spans the gun's block (the magazine box's left to
    /// the reserve box's right) and stands a gap over the tiles over the reserve; its colours are the Mk II palette's, but
    /// for the slot in hand (the addon selector's white box with black words).
    /// </summary>
    [Serializable]
    public sealed class WeaponColumn
    {
        public float height = 41.25f, heightHeld = 60f, gap = 4.125f, pad = 4f;
        public float icon = 93.75f, iconShift = 7.5f, iconAlpha = 150f / 255f;
        public Color held = new Color(1f, 1f, 1f, 200f / 255f), heldText = Color.black;
        public float kick = 14f, grow = .14f, flash = .3f, decode = .35f, colourFollow = 10f;
    }

    /// <summary>The objective (row Mission).</summary>
    [Serializable]
    public sealed class Mission
    {
        public float x = 70f, y = 58f, width = 375f, titleRise = 14f, barY = 102f, barHeight = 5f, segment = .9f;
        public int mostSegments = 30;
        public float track = .1f, zero = .2f, slash = .5f, decode = 1f, decodeWindow = 2f, songRise = 9f;
        public float breathRate = 1.2f, breathFloor = .55f;
    }

    /// <summary>The line at the top that says a new objective (row Notice).</summary>
    [Serializable]
    public sealed class Notice
    {
        public float y = 150f, textRise = 14f, lineRise = 50f, lineWidth = 440f, lineGrow = .5f, lineAlpha = .7f, lineThick = 1.5f;
        public float seconds = 2.4f, fadeOut = .4f, decode = .9f, size = 1.25f;
    }

    /// <summary>The screen's edge glow on a hit, a heal or a shield taking it (row Flare).</summary>
    [Serializable]
    public sealed class Flare
    {
        public float seconds = .3f, alpha = 50f / 255f;
        public Color damage = new Color(150f / 255f, 0f, 0f), heal = new Color(120f / 255f, 150f / 255f, 0f), armor = new Color(150f / 255f, 150f / 255f, 150f / 255f);
    }

    public readonly Frame Hud = new Frame();
    public readonly Palette Colour = new Palette();
    public readonly Typeface Text = new Typeface();
    public readonly Status Health = new Status();
    public readonly Team Mates = new Team();
    public readonly Effects Timed = new Effects();
    public readonly Ammo Gun = new Ammo();
    public readonly Gear Kit = new Gear();
    public readonly Pickups Taken = new Pickups();
    public readonly WeaponColumn Weapons = new WeaponColumn();
    public readonly Mission Goal = new Mission();
    public readonly Notice Line = new Notice();
    public readonly Flare Glow = new Flare();

    /// <summary>Binds every row to its object; the owner keeps the bindings alive for a table saved during Play.</summary>
    public void Bind(UnityEngine.Object owner)
    {
        GameTables.BindNested(MkHud.TableName, "Hud", owner, Hud);
        GameTables.BindNested(MkHud.TableName, "Colour", owner, Colour);
        GameTables.BindNested(MkHud.TableName, "Text", owner, Text);
        GameTables.BindNested(MkHud.TableName, "Status", owner, Health);
        GameTables.BindNested(MkHud.TableName, "Team", owner, Mates);
        GameTables.BindNested(MkHud.TableName, "Effects", owner, Timed);
        GameTables.BindNested(MkHud.TableName, "Ammo", owner, Gun);
        GameTables.BindNested(MkHud.TableName, "Gear", owner, Kit);
        GameTables.BindNested(MkHud.TableName, "Pickups", owner, Taken);
        GameTables.BindNested(MkHud.TableName, "Slots", owner, Weapons);
        GameTables.BindNested(MkHud.TableName, "Mission", owner, Goal);
        GameTables.BindNested(MkHud.TableName, "Notice", owner, Line);
        GameTables.BindNested(MkHud.TableName, "Flare", owner, Glow);
    }

    /// <summary>A GMod font size (a cell height) as a TMP size (the em).</summary>
    public float Em(float gmodSize) => gmodSize / Mathf.Max(.1f, Text.em);
}
