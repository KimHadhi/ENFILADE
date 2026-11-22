using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The HitFeedback module on screen: one overlay with the kill chain (Cool Killchains draws in HUDPaint), then the
    /// marker and the score popup over it (Black Ops VI Hitmarker draws in DrawOverlay, marker first). It runs after
    /// this frame's hits are in (FrameOrder.HitMarkers, after HitConfirm), so a hit shows the frame it lands.
    /// <para>One confirm per frame: everything HitFeedbackModule heard this frame (a shotgun's pellets in a crowd, a
    /// grenade's kills) shows and sounds once. A kill outranks a hit, and the frame is a headshot, on flesh or by fire if
    /// any of its hits was. The addon got one message per damaged target and played every message's three sounds on
    /// top of each other, its marker taking whichever message came last (a hit after a kill in the same tick turned the
    /// red kill marker white).</para>
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.HitMarkers)]
    internal sealed class HitFeedbackHud : MonoBehaviour
    {
        /// <summary>The module's numbers: Tables/Hud/HitFeedback.csv (rows Marker, Score, Chain, Sound, Points).</summary>
        public const string TableName = "HitFeedback";
        /// <summary>Over the crosshair and the HUD's pieces near the middle, under the scoreboard and the menus.</summary>
        private const int SortOrder = 27;
        private const string MarkerRow = "Marker", ScoreRow = "Score", ChainRow = "Chain", SoundRow = "Sound";
        // The sound bank's keys: Art/Audio/Addons/HitFeedback/<key>, the addon's sound scripts.
        private const string HitMark = "HitFeedback/HitMark", HeadMark = "HitFeedback/HeadMark", ItemMark = "HitFeedback/ItemMark",
            KillMark = "HitFeedback/KillMark", HeadKillMark = "HitFeedback/HeadKillMark", Body = "HitFeedback/Body",
            BodyLayer = "HitFeedback/BodyLayer", Head = "HitFeedback/Head", HeadLayer = "HitFeedback/HeadLayer";

        private readonly HitFeedbackMarkerLook markerLook = new HitFeedbackMarkerLook();
        private readonly HitFeedbackScoreLook scoreLook = new HitFeedbackScoreLook();
        private readonly HitFeedbackChainLook chainLook = new HitFeedbackChainLook();
        private readonly HitFeedbackSoundLook soundLook = new HitFeedbackSoundLook();
        private Overlay overlay;
        private Painter chainPainter, markerPainter, scorePainter;
        private HitFeedbackMarker marker;
        private HitFeedbackScore score;
        private HitFeedbackChain chain;

        // This frame's confirmed hits and kills, shown once in LateUpdate.
        private bool hit, hitHead, hitFlesh, hitBurn;
        private bool kill, killHead, killFlesh, killBurn;

        // The local player's guns, for hip fire (bo6_hit's !KeyDown(IN_ATTACK2)): found again when the player changes.
        private PlayerMovement aimer;
        private WeaponController guns;

        public static HitFeedbackHud Create()
        {
            var overlay = Overlay.Create("Hit Feedback", SortOrder);
            var hud = overlay.gameObject.AddComponent<HitFeedbackHud>();
            hud.Build(overlay);
            return hud;
        }

        private void Build(Overlay on)
        {
            overlay = on;
            GameTables.BindNested(TableName, MarkerRow, this, markerLook);
            GameTables.BindNested(TableName, ScoreRow, this, scoreLook);
            GameTables.BindNested(TableName, ChainRow, this, chainLook);
            GameTables.BindNested(TableName, SoundRow, this, soundLook);
            chainPainter = overlay.AddPainter("Kill Chain");
            markerPainter = overlay.AddPainter("Hit Marker");
            scorePainter = overlay.AddPainter("Score");
            marker = new HitFeedbackMarker();
            score = new HitFeedbackScore();
            chain = new HitFeedbackChain();
        }

        /// <summary>A hit that killed nothing, one of this frame's.</summary>
        public void QueueHit(bool headshot, bool organism, bool burn)
        {
            hit = true;
            hitHead |= headshot;
            hitFlesh |= organism;
            hitBurn |= burn;
        }

        /// <summary>A kill, one of this frame's.</summary>
        public void QueueKill(bool headshot, bool organism, bool burn)
        {
            kill = true;
            killHead |= headshot;
            killFlesh |= organism;
            killBurn |= burn;
        }

        /// <summary>A row of the score popup.</summary>
        public void AddRow(string name, int points) => score.Add(name, points, scoreLook);

        /// <summary>A skull of the kill chain, which now lasts until <paramref name="ends"/>.</summary>
        public void AddSkull(HitFeedbackKill kind, bool head, float ends) => chain.Add(kind, head, ends, chainLook);

        private void LateUpdate()
        {
            var o = GameSettings.Current.addonHitFeedback;
            if (HudStage.Fight) Stage(o);
            Confirm(o);
            chainPainter.Begin();
            if (o.skulls) chain.Draw(chainPainter, overlay, chainLook, o);
            if (HudStage.Fight) MarkParts(o);
            chainPainter.End();
            markerPainter.Begin();
            marker.Draw(markerPainter, overlay, markerLook, o.markSize);
            markerPainter.End();
            scorePainter.Begin();
            score.Draw(scorePainter, overlay, scoreLook, o);
            scorePainter.End();
        }

        // bo6_kill's and bo6_hit's receivers, once for the frame.
        private void Confirm(HitFeedbackOptions o)
        {
            if (!kill && !hit) return;
            if (o.enable)
            {
                if (kill)
                {
                    marker.Take(true, killHead, killFlesh, false, killBurn, o.killmark, markerLook);
                    if (o.killSound && !HudStage.On) KillSounds(killHead);
                }
                else
                {
                    marker.Take(false, hitHead, hitFlesh, !Aimed(), hitBurn, o.hitmark, markerLook);
                    if (o.hitSound && !HudStage.On) HitSounds(hitHead, hitFlesh);
                }
            }
            hit = hitHead = hitFlesh = hitBurn = false;
            kill = killHead = killFlesh = killBurn = false;
        }

        // The HUD settings' short fight (HudStage): two hits, a headshot, then four kills, silent and counted nowhere.
        private void Stage(HitFeedbackOptions o)
        {
            if (HudStage.At(HudStage.FightSeconds, .1f)) QueueHit(false, true, false);
            if (HudStage.At(HudStage.FightSeconds, .4f)) QueueHit(true, true, false);
            if (HudStage.At(HudStage.FightSeconds, .75f)) StageKill(o, HitFeedbackKill.Plain, false, o.nameEliminate, "hitfeedback.name.eliminate");
            if (HudStage.At(HudStage.FightSeconds, 1.2f)) StageKill(o, HitFeedbackKill.Plain, true, o.nameHeadshot, "hitfeedback.name.headshot");
            if (HudStage.At(HudStage.FightSeconds, 1.75f)) StageKill(o, HitFeedbackKill.Blast, false, o.nameExplosive, "hitfeedback.name.explosive");
            if (HudStage.At(HudStage.FightSeconds, 2.2f)) StageKill(o, HitFeedbackKill.Melee, false, o.nameMelee, "hitfeedback.name.melee");
        }

        private void StageKill(HitFeedbackOptions o, HitFeedbackKill kind, bool head, string typed, string textId)
        {
            QueueKill(head, true, false);
            if (o.enable) AddRow(string.IsNullOrEmpty(typed) ? Texts.InEnglish(textId) : typed, HitFeedbackModule.KillPoints);
            if (o.skulls) AddSkull(kind, head, Smooth.Now + HudStage.FightSeconds - HudStage.FightTime);
        }

        // Where the fight's parts stand, for the settings to press and drag.
        private void MarkParts(HitFeedbackOptions o)
        {
            float cx = overlay.Width * .5f, cy = overlay.Height * .5f;
            float screen = overlay.ScreenScale(scoreLook.screenScale), text = screen * o.textSize;
            float x = cx * scoreLook.anchorX + o.horizontal, y = cy * scoreLook.anchorY + o.vertical;
            float side = (markerLook.baseSize + markerLook.growSize) * overlay.ScreenScale(markerLook.screenScale) * Mathf.Max(.3f, o.markSize) * 1.6f;
            HudStage.Mark(HudPart.Marker, cx - side * .5f, cy - side * .5f, side, side);
            float left = x + scoreLook.columnGap * screen, top = y - scoreLook.rowStep * screen - scoreLook.totalSize * text;
            HudStage.Mark(HudPart.Score, left, top, (scoreLook.nameSwing + 190f) * screen, y + scoreLook.rowStep * screen * 3f - top);
            float cell = (chainLook.skullSize + chainLook.gap) * overlay.Width / chainLook.unit * o.skullScale, wide = cell * 4f;
            if (o.skulls) HudStage.Mark(HudPart.Skulls, cx - wide * .5f, overlay.Height * o.skullOffset - cell * .5f, wide, cell);
        }

        // A headshot: Hit.Head.Mark_bo6 + Hit.Head + Hit.Head.Layer; a hit: Hit.Mark_bo6 (Hit.Mark.Item off flesh) +
        // Hit.Body + Hit.Body.Layer.
        private void HitSounds(bool head, bool flesh)
        {
            if (head)
            {
                Mark(HeadMark);
                Flesh(Head);
                Flesh(HeadLayer);
            }
            else
            {
                Mark(flesh ? HitMark : ItemMark);
                Flesh(Body);
                Flesh(BodyLayer);
            }
        }

        // A headshot kill: Hit.Head + Hit.Head.Layer + Kill.Head.Mark_bo6; a kill: Hit.Body + Hit.Body.Layer + Kill.Mark_bo6.
        private void KillSounds(bool head)
        {
            Flesh(head ? Head : Body);
            Flesh(head ? HeadLayer : BodyLayer);
            Mark(head ? HeadKillMark : KillMark);
        }

        private void Mark(string key) => Sound.Play(key, soundLook.markPitchMin, soundLook.markPitchMax, soundLook.markVolume);

        private void Flesh(string key) => Sound.Play(key, 1f, 1f, soundLook.fleshVolume);

        // Down the sights (IN_ATTACK2 held in the addon): the local player's aim has begun to come up.
        private bool Aimed()
        {
            var me = PlayerRoster.Local;
            if (me != aimer || (guns == null && me != null))
            {
                aimer = me;
                guns = me != null ? me.GetComponentInChildren<WeaponController>() : null;
            }
            return guns != null && guns.AimBlend > 0f;
        }
    }
}
