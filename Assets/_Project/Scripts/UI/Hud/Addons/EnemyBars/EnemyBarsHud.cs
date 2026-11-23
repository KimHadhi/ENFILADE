using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The Black Ops 6 Zombies Healthbars addon (cl_bo6healthbar.lua, sv_bo6healthbar.lua) on the shared layer: the one
    /// owner of every enemy's bar. One bar shows at a time, over the enemy under the crosshair within the max distance or
    /// the one a hit last landed on; it hides three seconds after the crosshair was last on something in reach. A flat bar
    /// of fixed size at any distance over the middle of the top of the enemy's hull: grey ground (or bg_alt_1.png), the
    /// white follower, the red health, a black line where the health ends, a black outline, the name in capitals under it
    /// over a blurred black shadow, the normal or the boss icon left of it, the damage total right of it, and the pillar
    /// each drop in health raises (yellow for a headshot) with its solid chunk inside the bar.
    /// <para>The addon's design and numbers are kept as they are (Tables/Hud/EnemyBars.csv, EnemyBarsLook); the code is
    /// ours, and the addon's bugs are fixed (each where it is fixed, and Docs/Features/EnemyBars.md): bars mirrored from
    /// behind the camera, a pillar left standing for good by one long frame, the second removal that took another enemy's
    /// bar away, timers of earlier hits cutting later holds short, the damage total flickering under steady fire.</para>
    /// <para>Data: EnemyHealth.Living (the enemies in play), CombatDamageEvents.Damaged (every hit, on every PC: the
    /// addon's SendDamageToClient, which its server broadcasts), the local camera's ray (GetEyeTrace). Read only.</para>
    /// </summary>
    [DefaultExecutionOrder(FrameOrder.EnemyBars)]
    [DisallowMultipleComponent]
    internal sealed class EnemyBarsHud : MonoBehaviour
    {
        // Ray hits looked at before falling back to RaycastAll; a crowd of triggers along one ray is rare.
        private const int RayRoom = 64;

        private readonly EnemyBarsLook.Bar bar = new EnemyBarsLook.Bar();
        private readonly EnemyBarsLook.Name nameLook = new EnemyBarsLook.Name();
        private readonly EnemyBarsLook.Icon icon = new EnemyBarsLook.Icon();
        private readonly EnemyBarsLook.Total total = new EnemyBarsLook.Total();
        private readonly EnemyBarsLook.Pillar pillar = new EnemyBarsLook.Pillar();
        private readonly EnemyBarsLook.Aim aim = new EnemyBarsLook.Aim();

        // TBL.HUD.HealthBars, in the order the enemies came (the order they draw in), and the same by enemy.
        private readonly List<EnemyBar> bars = new List<EnemyBar>();
        private readonly Dictionary<EnemyHealth, EnemyBar> byEnemy = new Dictionary<EnemyHealth, EnemyBar>();
        private readonly Stack<EnemyBar> spare = new Stack<EnemyBar>();
        // bo6_pause_trace, per dealer: whoever just killed moves no bar until then (game time).
        private readonly List<KillPause> pauses = new List<KillPause>(4);
        private readonly RaycastHit[] rays = new RaycastHit[RayRoom];

        private Overlay overlay;
        private Painter painter;
        // The fonts, made for the screen height their sizes are shares of.
        private HudFont nameFont, blurFont, totalFont;
        private float fontsFor = -1f;
        // The blurred shadow as its kernel's taps: the offset of each and black at its weight over six passes.
        private Vector2[] tapAt = Array.Empty<Vector2>();
        private Color32[] tapColor = Array.Empty<Color32>();
        private Texture normalPicture, bossPicture, patternPicture;
        // TimeWithoutTarget (real seconds) and GET_DAMAGED (until this game time).
        private float sinceTarget, damagedUntil = float.NegativeInfinity;
        // The options' boss list as last read, and its mask.
        private string bossesRead;
        private int bossMask;

        private struct KillPause
        {
            public GameObject Who;
            public float Until;
        }

        /// <summary>Puts the HUD on its overlay: one painter, the table's rows, the pictures and the blur's taps.</summary>
        internal void Setup(Overlay host)
        {
            overlay = host;
            painter = host.AddPainter("Bars");
            GameTables.BindNested(EnemyBarsLook.Table, EnemyBarsLook.BarRow, this, bar);
            GameTables.BindNested(EnemyBarsLook.Table, EnemyBarsLook.NameRow, this, nameLook);
            GameTables.BindNested(EnemyBarsLook.Table, EnemyBarsLook.IconRow, this, icon);
            GameTables.BindNested(EnemyBarsLook.Table, EnemyBarsLook.TotalRow, this, total);
            GameTables.BindNested(EnemyBarsLook.Table, EnemyBarsLook.PillarRow, this, pillar);
            GameTables.BindNested(EnemyBarsLook.Table, EnemyBarsLook.AimRow, this, aim);
            Prepare();
        }

        private void OnEnable()
        {
            CombatDamageEvents.Damaged -= OnDamaged;
            CombatDamageEvents.Damaged += OnDamaged;
            GameTables.Reloaded -= Prepare;
            GameTables.Reloaded += Prepare;
        }

        private void OnDisable()
        {
            CombatDamageEvents.Damaged -= OnDamaged;
            GameTables.Reloaded -= Prepare;
        }

        // What the rows make: the pictures, the fonts (again on the next frame) and the blur's taps.
        private void Prepare()
        {
            normalPicture = Pictures.Get(icon.normal);
            bossPicture = Pictures.Get(icon.boss);
            patternPicture = Pictures.Get(bar.pattern);
            fontsFor = -1f;
            MakeTaps();
        }

        // ---- The HUD settings' stage (HudStage): one enemy's bar over the screen's middle, hurt by the short fight -----

        private EnemyBar stageBar;
        private float stageHealth = 1f;

        private void StageBar(EnemyBarsOptions options)
        {
            float now = Time.time, width = overlay.Width, height = overlay.Height;
            if (height != fontsFor) MakeFonts(height);
            stageBar ??= new EnemyBar();
            var item = stageBar;
            item.Maximum = 1f;
            item.Show = true;
            item.Name = Texts.Get("settings.hudedit.sample.enemy").ToUpperInvariant();
            if (HudStage.At(HudStage.FightSeconds, .05f))
            {
                stageHealth = item.Follower = item.LastHealth = 1f;
                item.Total = 0f;
                item.Pillars.Clear();
            }
            float before = stageHealth;
            if (HudStage.At(HudStage.FightSeconds, .1f)) stageHealth = .72f;
            if (HudStage.At(HudStage.FightSeconds, .4f)) stageHealth = .3f;
            if (HudStage.At(HudStage.FightSeconds, .75f)) stageHealth = 0f;
            if (HudStage.At(HudStage.FightSeconds, 2.6f)) stageHealth = item.Follower = item.LastHealth = 1f;
            else Read(item, stageHealth);
            if (stageHealth < before)
            {
                if (options.damageNumbers) item.Total += Mathf.Round((before - stageHealth) * 100f);
                item.Paint = true;
                item.PaintHead = stageHealth <= .3f && stageHealth > 0f;
            }
            if (!options.damageNumbers) item.Total = 0f;
            if (item.Paint) PaintNewest(item);
            item.Follower = Smooth.Lerp(1f / Mathf.Max(1e-4f, bar.followTime), item.Follower, stageHealth);
            if (!options.enabled) return;
            float barW = width * bar.width, barH = height * bar.height, x = (width - barW) * .5f, y = height * .33f;
            HudStage.Mark(HudPart.EnemyBar, x - barW * .2f, y - barH * 2f, barW * 1.4f, barH * 5f);
            DrawBar(item, options, false, x, y, width, height, barW, barH, stageHealth);
            Rise(item, true, x, y, height, barW, barH);
        }

        // ---- The hit's word (net.Receive "BO6HBAR_SendDamageToClient") ------------------------------------------------

        /// <summary>
        /// A hit landed on an enemy, on this PC (the shooter's, the host's or another's copy: every PC hears every hit, as
        /// the addon's server broadcasts each). As the addon: if a player or an NPC dealt it, its enemy's bar comes up and
        /// every other one hides, unless the dealer has just killed; the follower starts its hold; the damage adds to the
        /// total; a kill pauses its dealer; and the newest pillar takes the hit's colour.
        /// </summary>
        private void OnDamaged(CombatDamageReport report)
        {
            if (!(report.Target is EnemyHealth enemy) || enemy == null) return;
            var hit = report.Hit;
            // The addon's server tells of a hit only when a player or an NPC dealt it (PostEntityTakeDamage).
            if (hit.Source == null) return;
            if (!byEnemy.TryGetValue(enemy, out var item))
            {
                if (!enemy.isActiveAndEnabled) return;
                // Hit in the frame it came into play, before this HUD took it up: its bar starts from before the hit.
                item = Add(enemy, report.HealthBefore);
                item.LastHealth = report.HealthBefore;
            }
            float now = Time.time;
            var options = GameSettings.Current.addonEnemyBars;
            var dealer = DealerOf(hit.Source);
            // Where it is and what the hit left: a body this very hit destroys is gone before the frame reads it.
            item.Reported = report.HealthAfter;
            if (report.MaximumHealth > 0f) item.Maximum = report.MaximumHealth;
            item.Top = TopOf(item);
            item.Placed = true;
            // GET_DAMAGED: target:GetPos():Distance(attacker:GetPos()) <= max distance. Each hit keeps it on for its own
            // 0.1 s; the addon's timer of an earlier hit switched off what a later one had switched on.
            if (Vector3.Distance(enemy.transform.position, dealer.transform.position) <= options.maxDistance)
                damagedUntil = Mathf.Max(damagedUntil, now + aim.damagedWindow);
            // The hit's enemy takes the screen. A paused dealer's hit leaves every bar as it is: the addon still hid the
            // others then, so the kill's own bar vanished at the next hit and no bar showed for the pause (fixed).
            if (!Paused(dealer, now))
                for (int i = 0; i < bars.Count; i++) bars[i].Show = bars[i] == item;
            if (options.damageNumbers) item.Total += report.Dealt;
            // The total clears a second after the hit that opened it. The addon cleared it a second after every hit, so
            // under steady fire it fell back to one hit's worth (or nothing) several times a second (fixed).
            if (item.TotalEnd <= 0f) item.TotalEnd = now + total.reset;
            // The follower holds a second from the hit that starts a hold; hits during it or during the slide don't
            // start another. The addon gave every hit its own release timer, so one left from an earlier hold could end
            // a new hold at once (fixed): only the starting hit's second counts.
            if (!item.Holding && !item.Following)
            {
                item.Holding = true;
                item.HoldEnd = now + bar.hold;
            }
            else if (item.Holding && float.IsPositiveInfinity(item.HoldEnd)) item.HoldEnd = now + bar.hold;
            // bo6_pause_trace, set after the show above (the killing hit still brings its bar up). A later kill extends
            // it; the addon's first timer ended the second kill's pause early (fixed).
            if (report.HealthAfter <= 0f) Pause(dealer, now + aim.killPause);
            // HITGROUP_HEAD, of this hit. The addon read a flag its last bullet left on the NPC, so a blast after a
            // headshot painted yellow (fixed). Our head test is EnemyHealth's own.
            item.Paint = true;
            item.PaintHead = hit.Part == BodyPart.Head || (hit.IsCritical && hit.Method == DamageMethod.Bullet);
        }

        // The attacker entity: the player whose hit it is, else the enemy that dealt it, else the object itself.
        private static GameObject DealerOf(GameObject source)
        {
            var player = source.GetComponentInParent<PlayerMovement>();
            if (player != null) return player.gameObject;
            var enemy = source.GetComponentInParent<EnemyHealth>();
            return enemy != null ? enemy.gameObject : source;
        }

        private bool Paused(GameObject who, float now)
        {
            if (who == null) return false;
            for (int i = 0; i < pauses.Count; i++)
                if (pauses[i].Who == who) return pauses[i].Until > now;
            return false;
        }

        private void Pause(GameObject who, float until)
        {
            float now = Time.time;
            for (int i = pauses.Count - 1; i >= 0; i--)
            {
                var kept = pauses[i];
                if (kept.Who == who)
                {
                    kept.Until = Mathf.Max(kept.Until, until);
                    pauses[i] = kept;
                    return;
                }
                if (kept.Who == null || kept.Until <= now) pauses.RemoveAt(i);
            }
            pauses.Add(new KillPause { Who = who, Until = until });
        }

        // ---- The frame (hook "HUDPaint") -------------------------------------------------------------------------------

        private void LateUpdate()
        {
            if (painter == null) return;
            Track();
            painter.Begin();
            if (HudStage.Fight) StageBar(GameSettings.Current.addonEnemyBars);
            else if (bars.Count > 0) Frame(GameSettings.Current.addonEnemyBars);
            painter.End();
        }

        // OnEntityCreated: every enemy alive in play has a bar. A dead one waiting to be removed (a copy) gets none again.
        private void Track()
        {
            var living = EnemyHealth.Living;
            for (int i = 0; i < living.Count; i++)
            {
                var enemy = living[i];
                if (enemy != null && enemy.IsAlive && !byEnemy.ContainsKey(enemy)) Add(enemy, enemy.Health);
            }
        }

        private EnemyBar Add(EnemyHealth enemy, float health)
        {
            var item = spare.Count > 0 ? spare.Pop() : new EnemyBar();
            item.Reset(enemy, health);
            bars.Add(item);
            byEnemy[enemy] = item;
            return item;
        }

        private void Frame(EnemyBarsOptions options)
        {
            float now = Time.time, width = overlay.Width, height = overlay.Height;
            if (height != fontsFor) MakeFonts(height);
            var view = Camera.main;
            var local = PlayerRoster.Local;

            // ply:GetEyeTrace(): what the crosshair is on.
            EnemyHealth aimed = null;
            bool met = false;
            Vector3 metAt = default;
            if (view != null) Look(view, local, now, out aimed, out met, out metAt);
            float reach = options.maxDistance, distance = reach * 2f;
            // The addon's test, "not IsNPC() or not IsNextBot()", holds for anything, so this clock runs every frame and
            // only something in reach under the crosshair sets it back: an enemy, or while GET_DAMAGED anything solid.
            sinceTarget += Smooth.Dt;
            if (sinceTarget >= aim.forget)
                for (int i = 0; i < bars.Count; i++) bars[i].Show = false;
            if (met && (aimed != null || now < damagedUntil))
            {
                Vector3 own = local != null ? local.transform.position : view.transform.position;
                distance = Vector3.Distance(metAt, own);
                if (distance <= reach) sinceTarget = 0f;
            }
            // The aimed enemy's bar takes the screen, before any bar is drawn: the addon switched it inside its drawing
            // loop, so the bar it hid had already been drawn that frame (a frame of two bars).
            bool paused = local != null && Paused(local.gameObject, now);
            if (!paused && aimed != null && distance <= reach && byEnemy.TryGetValue(aimed, out var aimedBar))
                for (int i = 0; i < bars.Count; i++) bars[i].Show = bars[i] == aimedBar;

            float barW = width * bar.width, barH = height * bar.height;
            int kept = 0;
            for (int i = 0; i < bars.Count; i++)
            {
                var item = bars[i];
                Step(item, options, now, view, width, height, barW, barH);
                // One removal per bar, after it is drawn. The addon removed inside its loop, which skipped the next bar
                // for a frame, and an NPC both dead and removed counted twice and was removed twice: the second removal
                // took the next enemy's bar away for good (fixed).
                if (item.Gone >= bar.linger)
                {
                    Retire(item);
                    continue;
                }
                bars[kept++] = item;
            }
            bars.RemoveRange(kept, bars.Count - kept);
        }

        private void Retire(EnemyBar item)
        {
            if (byEnemy.TryGetValue(item.Enemy, out var held) && held == item) byEnemy.Remove(item.Enemy);
            item.Release();
            spare.Push(item);
        }

        // One bar's frame: the addon's timers, the health read (a drop raises a pillar), the death, the follower, the
        // drawing and the pillars.
        private void Step(EnemyBar item, EnemyBarsOptions options, float now, Camera view, float width, float height,
            float barW, float barH)
        {
            // timer.Simple(1, ...): the hold lets go, the total clears (game time, as the addon's timers run on CurTime).
            if (item.Holding && now >= item.HoldEnd)
            {
                item.Holding = false;
                item.Following = true;
            }
            if (item.TotalEnd > 0f && now >= item.TotalEnd)
            {
                item.Total = 0f;
                item.TotalEnd = 0f;
            }

            var enemy = item.Enemy;
            bool valid = enemy != null && enemy.isActiveAndEnabled;
            float health = 0f;
            if (valid)
            {
                health = enemy.Health;
                item.Maximum = Mathf.Max(1f, enemy.MaximumHealth);
                Read(item, health);
                item.Reported = float.NaN;
                // BO6HBAR_is_dead (OnNPCKilled).
                if (!enemy.IsAlive) item.Dead = true;
            }
            // Gone before this frame could read it (destroyed by the kill): the hit's own report stands for the reading,
            // so the killing hit raises its pillar too.
            else if (!float.IsNaN(item.Reported))
            {
                Read(item, item.Reported);
                item.Reported = float.NaN;
            }
            if (item.Paint) PaintNewest(item);
            // lifetime_after_removed: counted once a frame, dead or removed or both.
            if (!valid || item.Dead) item.Gone += Smooth.Dt;

            float share = valid && health > 0f ? Mathf.Clamp01(health / item.Maximum) : 0f;
            // Lerp(RFT / 0.11, follower_w, hp_w), made frame-rate independent, while not holding.
            if (!item.Holding) item.Follower = Smooth.Lerp(1f / Mathf.Max(1e-4f, bar.followTime), item.Follower, share);
            if (share * bar.snap > item.Follower)
            {
                item.Follower = share;
                item.Following = false;
            }

            bool shown = false;
            float x = 0f, y = 0f;
            if (options.enabled && item.Show)
            {
                if (valid)
                {
                    item.Top = TopOf(item);
                    item.Placed = true;
                }
                // A point behind the camera has no place on the screen: the addon's ToScreen mirrored it and drew the
                // bar somewhere in front (fixed).
                if (item.Placed && view != null && Project(view, item.Top, width, height, out float sx, out float sy))
                {
                    x = sx - barW * .5f;
                    y = sy - barH * .5f - height * bar.raise;
                    shown = true;
                    DrawBar(item, options, valid, x, y, width, height, barW, barH, share);
                }
            }
            Rise(item, shown, x, y, height, barW, barH);
        }

        // A drop in health since the last reading raises a pillar where the health now ends, as wide as the drop.
        private void Read(EnemyBar item, float health)
        {
            float before = item.LastHealth;
            item.LastHealth = health;
            if (float.IsNaN(before) || before <= health) return;
            float most = item.Maximum;
            // dmg_num = math.min(pre_hp, math.abs(pre_hp - post_hp)); the left end is Remap(post_hp) unclamped.
            float taken = Mathf.Min(before, Mathf.Abs(before - health));
            item.Pillars.Add(new Pillar
            {
                Left = health / most,
                Width = Mathf.Clamp01(taken / most),
                Height = bar.height * pillar.start,
                Line = 0f,
                Alpha = pillar.plainAlpha,
                LineAlpha = pillar.plainLineAlpha,
                FrontLine = true,
            });
        }

        // The hit's word paints the newest pillar: its colour (yellow for a headshot), its alphas, and no front line while
        // the follower holds or slides (which, the hold starting with the hit, is always).
        private void PaintNewest(EnemyBar item)
        {
            item.Paint = false;
            int last = item.Pillars.Count - 1;
            if (last < 0) return;
            var newest = item.Pillars[last];
            newest.Head = item.PaintHead;
            newest.Alpha = pillar.alpha;
            newest.LineAlpha = pillar.lineAlpha;
            if (item.Holding || item.Following) newest.FrontLine = false;
            item.Pillars[last] = newest;
        }

        // ---- Drawing ---------------------------------------------------------------------------------------------------

        private void DrawBar(EnemyBar item, EnemyBarsOptions options, bool valid, float x, float y, float width, float height,
            float barW, float barH, float share)
        {
            var p = painter;
            float w = Whole(barW), h = Whole(barH);
            if (options.altBackground && patternPicture != null)
            {
                p.Picture = patternPicture;
                p.DrawColor = Color.white;
                p.TexturedRect(x, y, w, h);
            }
            else Fill(x, y, w, h, bar.background);
            Fill(x, y, Whole(item.Follower * barW), h, bar.follower);
            float healthW = share * barW;
            Fill(x, y, Whole(healthW), h, bar.health);
            if (healthW < barW) Fill(x + healthW - bar.edgeWidth, y, bar.edgeWidth, h, bar.edge);
            p.DrawColor = bar.outline;
            p.OutlinedRect(x, y, w, h, bar.outlineWidth);

            Label(item, options, valid, x, y + barH + nameLook.gap);

            // The icon: the boss's for an elite or named enemy or a kind on the boss list, the normal one otherwise. Its
            // picture is settled while the enemy is there and kept after.
            if (valid) item.Boss = IsBoss(item, options);
            var picture = item.Boss ? bossPicture : normalPicture;
            if (picture != null)
            {
                p.Picture = picture;
                p.DrawColor = icon.tint;
                float gap = width * icon.gap, top = y - height * icon.raise;
                if (item.Boss)
                {
                    float iconW = width * icon.bossWidth;
                    p.TexturedRect(x - iconW - gap, top, Whole(iconW), Whole(height * icon.bossHeight));
                }
                else
                {
                    // W * 0.0125 on both sides: square by the screen's width.
                    float side = width * icon.normalSize;
                    p.TexturedRect(x - side - gap, top, Whole(side), Whole(side));
                }
            }

            if (item.Total > 0f && totalFont != null)
            {
                if (item.Total != item.TotalShown)
                {
                    item.TotalShown = item.Total;
                    item.TotalText = Number(item.Total);
                }
                float tx = x + barW + total.gap, ty = y - height * total.size * total.raise;
                p.TextLines(item.TotalText, totalFont, tx + total.shadow, ty + total.shadow, total.shadowColor);
                p.TextLines(item.TotalText, totalFont, tx, ty, total.color);
            }
        }

        // The name under the bar: the blurred shadow, then the name over its own drop shadow (both its font's).
        private void Label(EnemyBar item, EnemyBarsOptions options, bool valid, float x, float top)
        {
            // The addon only stopped learning the name with the option off and kept drawing the one it had (fixed: off
            // draws none).
            if (!options.name || nameFont == null) return;
            if (valid)
            {
                string raw = item.Enemy.DisplayName;
                if (!string.Equals(raw, item.RawName, StringComparison.Ordinal))
                {
                    item.RawName = raw;
                    // PrintName, else its prefab's name; string.upper.
                    string shown = string.IsNullOrWhiteSpace(raw) ? CombatDamageEvents.DisplayNameOf(item.Enemy) : raw.Trim();
                    item.Name = shown.ToUpperInvariant();
                }
            }
            string text = item.Name;
            if (string.IsNullOrEmpty(text)) return;
            var p = painter;
            if (options.nameShadow && blurFont != null)
            {
                // The blurred font is a size up; the addon centres it on the name by both texts' sizes, the name's height
                // counting its drop shadow (Source's font height is the cell plus the shadow's offset).
                float nameW = nameFont.Width(text), nameH = nameFont.Size + nameLook.shadow;
                float blurW = blurFont.Width(text), blurH = blurFont.Size;
                float bx = x - (blurW - nameW) * .5f, by = top - (blurH - nameH) * .5f;
                for (int t = 0; t < tapAt.Length; t++)
                    p.TextLines(text, blurFont, bx + tapAt[t].x, by + tapAt[t].y, tapColor[t]);
            }
            p.TextLines(text, nameFont, x + nameLook.shadow, top + nameLook.shadow, nameLook.shadowColor);
            p.TextLines(text, nameFont, x, top, nameLook.color);
        }

        // DamageBlocks: each pillar rises from the bar's foot and fades once nearly up, shown or not; drawn over the rest
        // of the bar: its line, its block (one pixel wider without the front line) and the solid chunk inside the bar,
        // cut to the follower (render.SetScissorRect).
        private void Rise(EnemyBar item, bool shown, float x, float y, float height, float barW, float barH)
        {
            var list = item.Pillars;
            if (list.Count == 0) return;
            float target = bar.height + pillar.rise;
            float riseRate = Rate(pillar.riseTime), lineRate = Rate(pillar.lineTime);
            float fadeRate = Rate(pillar.fadeTime), lineFadeRate = Rate(pillar.lineFadeTime);
            int kept = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var one = list[i];
                one.Height = Smooth.Lerp(riseRate, one.Height, target);
                one.Line = Smooth.Lerp(lineRate, one.Line, target);
                // The addon also asked that the block be still below its full height ("h < target_h"). A frame of 0.16 s
                // or more put it exactly there (Lerp clamps at 1), the test never passed again, and the pillar stood for
                // good. Past 95% is the whole test here, so it always fades (fixed).
                if (one.Height > pillar.fadeFrom * target) one.Alpha = Smooth.Lerp(fadeRate, one.Alpha, 0f);
                if (one.Line > pillar.fadeFrom * target) one.LineAlpha = Smooth.Lerp(lineFadeRate, one.LineAlpha, 0f);
                one.Alpha = Mathf.Max(pillar.gone, one.Alpha);
                one.LineAlpha = Mathf.Max(pillar.gone, one.LineAlpha);
                if (shown) DrawPillar(item, one, x, y, height, barW, barH);
                if (one.Alpha <= pillar.gone) continue;
                list[kept++] = one;
            }
            list.RemoveRange(kept, list.Count - kept);
        }

        private void DrawPillar(EnemyBar item, in Pillar one, float x, float y, float height, float barW, float barH)
        {
            Color32 tint = one.Head ? pillar.headColor : pillar.color;
            float left = Mathf.Max(x, x + one.Left * barW), wide = one.Width * barW;
            float line = Whole(one.Line * height), rise = Whole(one.Height * height), foot = y + barH - pillar.foot;
            var lineColor = Gm.WithAlpha(tint, one.LineAlpha);
            Fill(left, foot - one.Line * height, pillar.lineWidth, line, lineColor);
            float widen = 0f;
            if (one.FrontLine) Fill(left + wide - pillar.lineWidth, foot - one.Line * height, pillar.lineWidth, line, lineColor);
            else widen = pillar.widen;
            Fill(left, foot - one.Height * height, Whole(wide + widen), rise, Gm.WithAlpha(tint, one.Alpha));
            var p = painter;
            p.Clip(x, y, x + item.Follower * barW, y + barH);
            Fill(left, y + pillar.inset, Whole(wide), Whole(barH - 2f * pillar.inset), Gm.WithAlpha(tint, pillar.chunkAlpha));
            p.NoClip();
        }

        // surface.SetDrawColor + surface.DrawRect; nothing for an empty rectangle.
        private void Fill(float x, float y, float w, float h, Color32 color)
        {
            if (w <= 0f || h <= 0f) return;
            painter.DrawColor = color;
            painter.Rect(x, y, w, h);
        }

        // GMod's surface functions take whole pixels for a rectangle's size (the C cast drops the fraction), and the
        // addon's shares were tuned on that: its bar is 141 x 8 at 1080p (W * 0.07395833 = 141.99, H * 0.00833 = 8.996).
        // Places are left as the shared Painter keeps them, so a moving bar does not step.
        private static float Whole(float length) => length > 0f ? (int)length : 0f;

        private static float Rate(float seconds) => 1f / Mathf.Max(1e-4f, seconds);

        // tostring(dmg_total) for our float damage: whole numbers without a point, others in their shortest form.
        private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        // ---- Where things are --------------------------------------------------------------------------------------------

        /// <summary>
        /// LocalToWorld(OBBCenter.x, OBBCenter.y, OBBMaxs.z): the middle of the top of the enemy's hull in its own space. A
        /// zombie's hull is its capsule (fitted to its head, grown for a brute and shrunk to a crawl by ZombieBody), read
        /// whether it is on or off, as an NPC's hull is always there; without one, the top of its solid parts.
        /// </summary>
        private static Vector3 TopOf(EnemyBar item)
        {
            var hull = item.Hull;
            if (hull != null)
            {
                Vector3 centre = hull.center;
                float up = hull.direction == 1 ? Mathf.Max(hull.height * .5f, hull.radius) : hull.radius;
                return hull.transform.TransformPoint(new Vector3(centre.x, centre.y + up, centre.z));
            }
            return CombatFoe.Top(item.Enemy);
        }

        // ToScreen in the overlay's units (y down), or false for a point behind the camera.
        private static bool Project(Camera view, Vector3 world, float width, float height, out float x, out float y)
        {
            var screen = view.WorldToScreenPoint(world);
            x = y = 0f;
            if (screen.z <= 0f || Screen.width <= 0 || Screen.height <= 0) return false;
            x = screen.x * (width / Screen.width);
            y = (Screen.height - screen.y) * (height / Screen.height);
            return true;
        }

        /// <summary>
        /// ply:GetEyeTrace(): the first solid thing along the camera's ray, the local player left out (its filter). An
        /// enemy is met by its hull or its bone hitboxes (triggers; an NPC's hull is solid to traces whatever it does);
        /// other triggers are no solid thing. Something that is not an enemy counts only while GET_DAMAGED, and only an
        /// entity in GMod's sense: a loose object or a player, never the world.
        /// </summary>
        private void Look(Camera view, PlayerMovement local, float now, out EnemyHealth aimed, out bool met, out Vector3 metAt)
        {
            aimed = null;
            met = false;
            metAt = default;
            var eye = view.transform;
            Vector3 origin = eye.position, direction = eye.forward;
            int count = Physics.RaycastNonAlloc(origin, direction, rays, aim.traceLength, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            var hits = rays;
            if (count >= rays.Length)
            {
                // A full buffer may have left out the nearest; the rare crowded ray asks for all of them.
                hits = Physics.RaycastAll(origin, direction, aim.traceLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                count = hits.Length;
            }
            var own = local != null ? local.transform : null;
            int best = -1;
            float nearest = float.PositiveInfinity;
            EnemyHealth bestEnemy = null;
            for (int i = 0; i < count; i++)
            {
                var collider = hits[i].collider;
                if (collider == null || hits[i].distance >= nearest) continue;
                if (own != null && collider.transform.IsChildOf(own)) continue;
                EnemyHealth enemy = null;
                if (collider.isTrigger)
                {
                    enemy = collider.GetComponentInParent<EnemyHealth>();
                    if (enemy == null) continue;
                }
                nearest = hits[i].distance;
                best = i;
                bestEnemy = enemy;
            }
            if (best < 0) return;
            var struck = hits[best].collider;
            if (bestEnemy == null) bestEnemy = struck.GetComponentInParent<EnemyHealth>();
            if (bestEnemy != null)
            {
                aimed = bestEnemy;
                met = true;
                metAt = bestEnemy.transform.position;
                return;
            }
            // Anything else only matters while GET_DAMAGED holds.
            if (now >= damagedUntil) return;
            var body = struck.attachedRigidbody;
            if (body != null)
            {
                met = true;
                metAt = body.position;
                return;
            }
            var player = struck.GetComponentInParent<PlayerMovement>();
            if (player != null)
            {
                met = true;
                metAt = player.transform.position;
            }
        }

        /// <summary>
        /// Boss icon: the addon's test is a class name with "boss" in it, or one on its boss list. Ours: an enemy of elite
        /// or named rank (Enemies.csv rank, /spawn n elite, a song's brute), or a zombie kind on the options' boss list.
        /// </summary>
        private bool IsBoss(EnemyBar item, EnemyBarsOptions options)
        {
            if (item.Enemy.Rank != EnemyRank.Normal) return true;
            if (!ReferenceEquals(options.bossKinds, bossesRead))
            {
                bossesRead = options.bossKinds;
                bossMask = EnemyBarsBosses.Mask(bossesRead);
            }
            return bossMask != 0 && item.Kind != null && (bossMask & EnemyBarsBosses.Bit(item.Kind.Kind)) != 0;
        }

        // ---- Fonts and the blur ------------------------------------------------------------------------------------------

        // surface.CreateFont for this screen height (the sizes are shares of it, cut to a whole size by HudFont).
        private void MakeFonts(float height)
        {
            fontsFor = height;
            nameFont = HudFont.Get(nameLook.face, height * nameLook.size, nameLook.weight);
            blurFont = HudFont.Get(nameLook.face, height * nameLook.blurSize, nameLook.weight);
            totalFont = HudFont.Get(total.face, height * total.size, total.weight);
        }

        /// <summary>
        /// BO6HBAR_font_name_blur (blursize 2) drawn six times. Source blurs a glyph into its bitmap with
        /// ApplyGaussianBlurToTexture: every pixel takes Σ g(i)·g(j)·coverage over the (2r+1)² pixels around it, g(d) =
        /// 1/√(2·3.14·σ²) · 2.7^(−d²/2σ²), σ = 0.683·r (its own rounded π and e). A TextMeshPro glyph can't be blurred that
        /// far (the font atlas's distance field reaches about 1.4 px at this size, the kernel 2.5), so the convolution is
        /// drawn as it is defined: one copy of the name per kernel pixel, offset by it, in black at its weight. Six passes
        /// of the same black over each other come to 1 − (1 − w)⁶, so each copy carries that alpha instead of six copies.
        /// </summary>
        private void MakeTaps()
        {
            int radius = Mathf.Clamp(nameLook.blur, 0, 8), side = radius * 2 + 1, count = side * side;
            if (tapAt.Length != count)
            {
                tapAt = new Vector2[count];
                tapColor = new Color32[count];
            }
            double sigma = nameLook.sigma * radius;
            int passes = Mathf.Max(1, nameLook.passes), k = 0;
            Color32 black = nameLook.blurColor;
            for (int j = -radius; j <= radius; j++)
            for (int i = -radius; i <= radius; i++, k++)
            {
                double weight = radius > 0 ? Gauss(i, sigma) * Gauss(j, sigma) : 1.0;
                double alpha = 1.0 - Math.Pow(1.0 - Math.Min(1.0, weight), passes);
                tapAt[k] = new Vector2(i, j);
                tapColor[k] = Gm.WithAlpha(black, (float)(black.a * alpha));
            }
        }

        private double Gauss(int d, double sigma) =>
            1.0 / Math.Sqrt(2.0 * nameLook.pi * sigma * sigma) * Math.Pow(nameLook.e, -(d * d) / (2.0 * sigma * sigma));
    }
}
