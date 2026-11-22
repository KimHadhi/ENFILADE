using System;
using System.Collections.Generic;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// HUD Mk II's numbers as the addon wrote them, one row group a part (Tables/Hud/PlayerHud.csv, "Row.field,value,note"),
    /// bound onto the PlayerHud host by GameTables.BindNested. The defaults here are the same numbers, so a missing row
    /// draws the addon as it is. Sizes are the addon's pixel numbers before its Scale (ScreenScale(0.25) x the scale option);
    /// places are shares of half the screen (the addon's sw, sh = ScrW()*.5, ScrH()*.5); rates are the addon's
    /// FrameTime() multipliers (Lerp(FrameTime()*rate)), run frame-rate independent by Smooth.
    /// </summary>
    public sealed class PlayerHudLook
    {
        public const string TableName = "PlayerHud";

        /// <summary>The canvas and the shared motion (shared.lua, configs.lua).</summary>
        [Serializable]
        public sealed class HudRow
        {
            public int order = 26;
            /// <summary>shared.lua: the view copy the sway trails, LerpAngle(FrameTime()*10).</summary>
            public float swayFollow = 10f;
            /// <summary>shared.lua: health and armour fade in and out (Nene.Lerp('statusAlpha', 10)).</summary>
            public float statusFollow = 10f;
            /// <summary>cl_indicator.lua: a hit shows health and armour this long when they hide by themselves.</summary>
            public float hideSeconds = 3f;
            /// <summary>configs.lua: Nene.Config.Scale = ScreenScale(0.25) x the scale option.</summary>
            public float scale = .25f;
            /// <summary>Every rounded box's corner (each draw.RoundedBox call passes 6).</summary>
            public float corner = 6f;
        }

        /// <summary>configs.lua Nene.Config.Colors and the outline every text gets.</summary>
        [Serializable]
        public sealed class ColourRow
        {
            public Color background = new Color32(0, 0, 0, 50);
            public Color low = new Color32(255, 0, 0, 255);
            public Color loss = new Color32(255, 150, 0, 255);
            public Color ammo = new Color32(155, 155, 155, 255);
            /// <summary>rect.lua: a value past full draws again over the bar in these two.</summary>
            public Color overOne = new Color32(150, 150, 255, 255);
            public Color overTwo = new Color32(255, 150, 20, 255);
            public Color outline = new Color32(0, 0, 0, 20);
            public float outlineWidth = 2f;
            /// <summary>Nene.LerpColor(name, 10).</summary>
            public float follow = 10f;
            /// <summary>An empty count's colour: each channel / 1.75 (stims, plates, grenades).</summary>
            public float dim = 1.75f;
        }

        /// <summary>configs.lua CreateFont: size = sw x share x scale, weight = unit x the weight option. The face is the
        /// font option's (never empty: PlayerHudOptions.Clamp).</summary>
        [Serializable]
        public sealed class FontRow
        {
            public float label = .01f, mags = .03f, ammo = .02f, alts = .015f, history = .022f;
            public float weightUnit = 500f;
        }

        /// <summary>configs.lua Nene.Config.Positions, as multiples of (sw, sh).</summary>
        [Serializable]
        public sealed class PlaceRow
        {
            public Vector2 health = new Vector2(1f, 1.87f);
            /// <summary>The armour bar under health: sh x this x the scale option lower.</summary>
            public float armorDrop = .03f;
            public Vector2 hazards = new Vector2(.805f, 1.775f);
            public Vector2 ammo = new Vector2(1.75f, 1.8f);
            public Vector2 history = new Vector2(1.45f, 1.5f);
            public Vector2 sense = new Vector2(1f, 1f);
        }

        /// <summary>rect.lua BL_Rect / BSS_Rect: the bars' lag behind a change and the orange loss under it.</summary>
        [Serializable]
        public sealed class BarRow
        {
            public float follow = 10f;
            public float lossFollow = 5f;
            public float lossHold = .5f;
        }

        /// <summary>cl_health.lua and cl_armor.lua.</summary>
        [Serializable]
        public sealed class StatusRow
        {
            public float width = 500f, healthHeight = 10f, armorHeight = 8f, frame = 5f;
            /// <summary>Health at or under this share of its most turns the bar the low colour.</summary>
            public float low = .3f;
            public float stims = 55f;
            /// <summary>The stims box stands this many of its own widths outside the bar's left end (stims_w / 1.25).</summary>
            public float stimsOut = 1.25f;
            public int armorParts = 10;
            /// <summary>Each armour segment leaves this share of its room empty after it.</summary>
            public float armorGap = .1f;
            /// <summary>The shield the armour bar holds when full: GMod's GetMaxArmor() (our shield has no most).</summary>
            public float armorMost = 100f;
        }

        /// <summary>cl_ammo.lua.</summary>
        [Serializable]
        public sealed class AmmoRow
        {
            public float magWidth = 125f, magHeight = 50f, reserveWidth = 75f, reserveHeight = 50f;
            /// <summary>The reserve box sits (mag + reserve width) / this to the right.</summary>
            public float reserveSpread = 1.95f;
            /// <summary>A magazine at or under this share of its size turns the low colour.</summary>
            public float low = .3f;
            /// <summary>The counts roll to a new number (Nene.Lerp('mags', 10) / ('ammo', 10)).</summary>
            public float countFollow = 10f;
            /// <summary>The shot's light: alpha at its peak and how fast it goes (Nenefireflick, FrameTime()*3).</summary>
            public float flickAlpha = 100f, flickFade = 3f;
            /// <summary>How quickly the drawn light follows the shot's (1/s): the photosensitivity rule's smoothing; 0 draws
            /// it as the addon did.</summary>
            public float flickFollow = 10f;
            /// <summary>The weapon's name: from the box's middle, mag width x these left and up.</summary>
            public float nameLeft = .45f, nameUp = .35f;
            public float eqp = 55f;
            /// <summary>The grenade box stands this many of its heights above the ammo.</summary>
            public float eqpRise = 2f;
            /// <summary>The faint digits behind the magazine count.</summary>
            public string ghost = "000";
            public float ghostAlpha = 50f;
            public float iconAlpha = 150f;
            public float reserveAlpha = 150f;
        }

        /// <summary>
        /// Which of the addon's ammo pictures (ammoType/1..10, 0 = the crate) a gun of our ammunition shows: GMod's
        /// ammo types numbered as HL2 numbers them (1 AR2, 3 Pistol, 4 SMG1, 5 357, 7 Buckshot, 10 Grenade), matched to
        /// Weapons.csv's ammo kinds. An ammunition not listed shows the crate, as a type past 10 does in the addon.
        /// </summary>
        [Serializable]
        public sealed class AmmoIconRow : ITableBound
        {
            /// <summary>"ammunition:number" pairs, ;-separated.</summary>
            public string kinds = "rifle:1;pistol:3;smg:4;shotgun:7;marksman:5;mg:1";
            /// <summary>The grenade box's picture (cl_ammo.lua: Nene.Materials.ammo[10]).</summary>
            public int grenade = 10;

            private readonly Dictionary<string, int> numbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private string read;

            void ITableBound.OnTableApplied() => read = null;

            /// <summary>The picture number for an ammunition, 0 for one not listed.</summary>
            public int Of(string ammunition)
            {
                if (read != kinds) Parse();
                return !string.IsNullOrEmpty(ammunition) && numbers.TryGetValue(ammunition, out int number) ? number : 0;
            }

            private void Parse()
            {
                read = kinds;
                numbers.Clear();
                if (string.IsNullOrEmpty(kinds)) return;
                foreach (string pair in kinds.Split(';'))
                {
                    int colon = pair.IndexOf(':');
                    if (colon <= 0 || !int.TryParse(pair.Substring(colon + 1).Trim(), out int number)) continue;
                    numbers[pair.Substring(0, colon).Trim()] = number;
                }
            }
        }

        /// <summary>cl_history.lua.</summary>
        [Serializable]
        public sealed class HistoryRow
        {
            /// <summary>A record shows for duration + life x its place in the list (s).</summary>
            public float duration = 2f, life = 3f;
            public float gap = 15f, icon = 30f, iconDrop = 4f, textShift = 15f;
            /// <summary>The newest record's foot: this many text heights above the anchor.</summary>
            public float rise = 2f;
            public float padLeft = .075f, boxWidth = 1.15f, boxTop = .45f, boxHeight = 1.2f;
            public float boxAlpha = 50f;
            public float follow = 10f;
            /// <summary>Records past this many fade: 1 + clamp((count - place) x step, -1, 0).</summary>
            public float shown = 4f, fadeStep = .3f;
        }

        /// <summary>cl_hazard.lua.</summary>
        [Serializable]
        public sealed class HazardRow
        {
            /// <summary>A box's side: ScrW() x this x Scale.</summary>
            public float size = .016f;
            public float hold = 5f, fadeIn = 3f, fadeOut = 2f, grow = 5f, flareFade = 3f;
            public float spacing = 1.25f, follow = 10f;
            public float backAlpha = 100f;
            /// <summary>The boxes' backs pulse |sin(CurTime() x this)| (0.64 Hz: within the photosensitivity rule).</summary>
            public float pulse = 2f;
            public float shown = 4f, fadeStep = .3f;
        }

        /// <summary>cl_indicator.lua and sv_indicator.lua.</summary>
        [Serializable]
        public sealed class SenseRow
        {
            /// <summary>The arc's outer radius and its thickness at the hit: ScrH() x these.</summary>
            public float radius = .25f, thickness = .15f;
            /// <summary>It thins to this share, then fades over a second.</summary>
            public float thinnest = .15f, thin = 3f;
            /// <summary>It starts 30° wide (start .. end) and narrows by 2 x narrow degrees as it settles.</summary>
            public float start = 255f, end = 285f, narrow = 14f, narrowFollow = 15f;
            public int segments = 30;
            public Color hit = new Color32(200, 30, 30, 255);
            public Color shielded = new Color32(200, 200, 200, 255);
            public Color blast = new Color32(255, 170, 64, 255);
        }

        /// <summary>cl_effects.lua and sv_effects.lua.</summary>
        [Serializable]
        public sealed class FlareRow
        {
            /// <summary>The vignette's grey before the flare's colour, and its alpha at the hit.</summary>
            public float grey = 150f, alpha = 50f;
            public Color damage = new Color(1f, 0f, 0f);
            public Color heal = new Color(.8f, 1f, 0f);
            public Color armor = new Color(1f, 1f, 1f);
            /// <summary>The view shake: CurTime() x speed, yaw .25 x intensity, roll 1 x intensity (degrees).</summary>
            public float shakeSpeed = 100f, shakeYaw = .25f, shakeRoll = 1f;
            /// <summary>How quickly the drawn vignette follows the flare (1/s): the photosensitivity rule's smoothing, so
            /// hits in quick succession glow instead of flashing; 0 draws it as the addon did.</summary>
            public float follow = 10f;
        }

        /// <summary>sfx.lua: the HUD drawn into a picture of its own and put on the screen with these.</summary>
        [Serializable]
        public sealed class FxRow
        {
            /// <summary>The HUD's picture is laid on the screen at this alpha ($alpha 0.75).</summary>
            public float alpha = .75f;
            /// <summary>The red and cyan copies: this much of the screen's width to each side.</summary>
            public float fringe = .0005f;
            /// <summary>Every other line of the HUD darkened by a black line of this alpha.</summary>
            public float scanline = 175f;
            /// <summary>Nene.GenerateGlitch(0.5, 12, 10): strips cut ScrH()/cutFine .. ScrH()/cutCoarse tall, moved up to
            /// strength px, their copies 2..8 px apart, starting 10 px above the screen.</summary>
            public float strength = 10f, cutFine = 75f, cutCoarse = 25f, abMin = 2f, abMax = 8f, top = -10f;
            public Color tint = new Color32(255, 100, 125, 255);
            /// <summary>The photosensitivity rule: the glitch eases in over rise and out over fall (s) as one smooth pulse
            /// (≤ 1.6 Hz) instead of twelve jumps in half a second; a hit keeps it up for hold.</summary>
            public float rise = .3f, fall = .33f, hold = .3f;
        }

        /// <summary>
        /// Our portrait, ultimate and rank (the addon has none of them), in its boxes over the health bar's left end.
        /// Sizes are px x Scale like the addon's; the 196 picture is 147 px on 1080p, the old HUD portrait's picture and mat.
        /// </summary>
        [Serializable]
        public sealed class PortraitRow
        {
            /// <summary>The picture's side.</summary>
            public float size = 196f;
            /// <summary>The ultimate's bar left of the picture, its outer width.</summary>
            public float magazine = 32f;
            /// <summary>Between the ultimate and the picture, the picture and the rank row, the rank box and its bar.</summary>
            public float gap = 10f;
            /// <summary>Between the rank row's foot and the health bar's frame.</summary>
            public float rise = 10f;
            /// <summary>The rank's box, as the stims box (55).</summary>
            public float insignia = 55f;
            /// <summary>The rank bar's thickness (the health bar's 10 and a half: 9 px on 1080p).</summary>
            public float rankHeight = 12f;
            /// <summary>With no picture chosen, the name's first letter: its cell this share of the picture.</summary>
            public float initial = .5f;
            /// <summary>The picture is drawn over the special effects with its own faint scanlines (alpha of the dark line
            /// every other screen row; the rest of the HUD's is Fx.scanline, 175).</summary>
            public float scanline = 70f;
            /// <summary>The rank bar: its empty length, the growth held when the game began, this game's kills (the score
            /// popup's total colour, so it reads as the score's), the share of the money held.</summary>
            public Color track = new Color32(255, 255, 255, 38);
            public Color held = new Color32(255, 255, 255, 200);
            public Color game = new Color32(255, 233, 85, 255);
            public Color kept = new Color32(255, 233, 85, 110);
            /// <summary>A kill's "+N XP" over the bar: how long it floats and how far it rises.</summary>
            public float gainSeconds = .9f, gainRise = 10f;
        }

        /// <summary>
        /// The ultimate's magazine left of the picture: the old HUD's magazine (11-11 round 4: rounds lying on their side,
        /// the next drawn as it charges and pushed in, the cover slammed shut when ready, the change when spent), drawn in
        /// the addon's colours. Sizes px on 1080p (times the scale option), times seconds.
        /// </summary>
        [Serializable]
        public sealed class MagazineRow
        {
            /// <summary>One use's charge in this many rounds; room up to the most held makes more (to 16).</summary>
            public int rounds = 10;
            public float seatSeconds = .22f, seatPush = 16f, slamSeconds = .16f, stripSeconds = .3f;
            public float dropSeconds = .45f, dropFall = 120f, freshDelay = .2f, freshSeconds = .35f, freshRise = 70f;
            public float sweepSeconds = 1.25f, caseSeconds = .55f, beatsPerSecond = 1.1f;
            /// <summary>A round: its case and its bullet. Past one use, both are the player's own colour (PlayerHud.Class).</summary>
            public Color round = new Color32(255, 255, 255, 255);
            public Color bullet = new Color32(155, 155, 155, 255);
            public Color spent = new Color32(255, 150, 0, 255);
        }

        /// <summary>
        /// Our class UI in the addon's parts (PlayerHud.Class): sizes are px on 1080p times the scale option and the class
        /// UI's own size; text sizes are GMod cell heights like the addon's fonts.
        /// </summary>
        [Serializable]
        public sealed class ClassRow
        {
            /// <summary>Under the screen's middle: the class box's top left this far left of the middle and up from the foot.</summary>
            public float centreLeft = 148f, centreUp = 159f;
            /// <summary>Under the middle, the bar's length; beside the portrait it reaches the health bar's right end.</summary>
            public float barWidth = 244f;
            /// <summary>Beside the portrait: the room between the portrait panel and the class box.</summary>
            public float portraitGap = 11f;
            public float box = 41.25f, gap = 8f, barHeight = 7.5f, frame = 3.75f, under = 12f;
            /// <summary>Each pip takes its share of the bar and fills the first (1 - gap) of it, as the armour bar's segments.</summary>
            public float pipGap = .1f;
            public float keyHeight = 28f, keyAbove = 37f, rowHeight = 22f, rowGap = 4f, chipMost = 92f;
            /// <summary>Text cells: the class name, the small words, the key's word, the gun digits, the mark.</summary>
            public float nameText = 16.8f, smallText = 14.4f, wordText = 19.2f, digitText = 28.8f, markText = 19.2f;
            /// <summary>The text outline: the addon's (black, 2 px) a little darker, so the words read on bright ground.</summary>
            public Color outline = new Color32(0, 0, 0, 110);
            /// <summary>The ready glow of the key box and the mark's beat: between these, this many times a second.</summary>
            public float glowLeast = .12f, glowMost = .42f, beatsPerSecond = .7f;
            /// <summary>The faint empty segments, a recharging box's fill and the deploy chips of a kind no longer held.</summary>
            public float emptyAlpha = .13f, riseAlpha = .28f, unheldAlpha = .4f;
        }

        public readonly HudRow Hud = new HudRow();
        public readonly ColourRow Colour = new ColourRow();
        public readonly FontRow Font = new FontRow();
        public readonly PlaceRow Place = new PlaceRow();
        public readonly BarRow Bar = new BarRow();
        public readonly StatusRow Status = new StatusRow();
        public readonly AmmoRow Ammo = new AmmoRow();
        public readonly AmmoIconRow AmmoIcon = new AmmoIconRow();
        public readonly HistoryRow History = new HistoryRow();
        public readonly HazardRow Hazard = new HazardRow();
        public readonly SenseRow Sense = new SenseRow();
        public readonly FlareRow Flare = new FlareRow();
        public readonly FxRow Fx = new FxRow();
        public readonly PortraitRow Portrait = new PortraitRow();
        public readonly MagazineRow Magazine = new MagazineRow();
        public readonly ClassRow Class = new ClassRow();

        /// <summary>Binds every row to the table for as long as <paramref name="owner"/> lives (and again on a reload).</summary>
        public void Bind(UnityEngine.Object owner)
        {
            GameTables.BindNested(TableName, "Hud", owner, Hud);
            GameTables.BindNested(TableName, "Colour", owner, Colour);
            GameTables.BindNested(TableName, "Font", owner, Font);
            GameTables.BindNested(TableName, "Place", owner, Place);
            GameTables.BindNested(TableName, "Bar", owner, Bar);
            GameTables.BindNested(TableName, "Status", owner, Status);
            GameTables.BindNested(TableName, "Ammo", owner, Ammo);
            GameTables.BindNested(TableName, "AmmoIcon", owner, AmmoIcon);
            GameTables.BindNested(TableName, "History", owner, History);
            GameTables.BindNested(TableName, "Hazard", owner, Hazard);
            GameTables.BindNested(TableName, "Sense", owner, Sense);
            GameTables.BindNested(TableName, "Flare", owner, Flare);
            GameTables.BindNested(TableName, "Fx", owner, Fx);
            GameTables.BindNested(TableName, "Portrait", owner, Portrait);
            GameTables.BindNested(TableName, "Magazine", owner, Magazine);
            GameTables.BindNested(TableName, "Class", owner, Class);
        }
    }
}
