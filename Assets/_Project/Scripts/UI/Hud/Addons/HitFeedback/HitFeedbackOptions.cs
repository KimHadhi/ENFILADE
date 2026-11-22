using System;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The HitFeedback module's options: what a hit and a kill show: the Black Ops VI Hitmarker addon (its marker and its
    /// score popup) and the Cool Killchains addon, refactored as one topic. Saved in settings.json as
    /// GameSettings.addonHitFeedback.
    /// The field initialisers are the defaults (the addons' own convar defaults): a new player starts with them and the
    /// HUD tab's reset returns to them (GameSettings.ResetToDefaults copies a fresh preset's block). Keep every field a
    /// value or a string, so JsonUtility's copy of the preset is a full one. Player-visible names go in Texts.csv.
    /// <para>The achievement names are the addon's text-entry convars (bo6hitmarker_achievements_*). Their defaults are
    /// player text, which lives in Texts.csv (hitfeedback.name.*), so an empty field here means "the addon's own name"
    /// and the score popup reads it from there; a name the player typed is kept as typed.</para>
    /// <para>bo6hitmarker_debug (a still sample of the popup to place it by) is left out too: the HUD settings play the popup
    /// itself while it is placed (HudStage).</para>
    /// <para>Left out, with nothing in this game to drive them: the names for a device kill ("Device Shutdown") and an
    /// alien kill ("Cleaner") — no enemy here is a turret, a scanner or a headcrab — and Cool Killchains' "Server
    /// Overrides" form, since no host here pushes its HUD options onto the other players.</para>
    /// </summary>
    [Serializable]
    public sealed class HitFeedbackOptions
    {
        // ---- Black Ops VI Hitmarker: its "Main" panel ------------------------------------------------------------------

        /// <summary>bo6hitmarker_enable 1: hits and kills show and sound. Off, nothing new shows (the score still counts:
        /// it is the player's experience, not a picture).</summary>
        public bool enable = true;
        /// <summary>bo6hitmarker_hitmark 1: the white marker on a hit.</summary>
        public bool hitmark = true;
        /// <summary>bo6hitmarker_killmark 1: the red marker on a kill.</summary>
        public bool killmark = true;
        /// <summary>bo6hitmarker_score 1: the yellow total and each row's "+N".</summary>
        public bool score = true;
        /// <summary>bo6hitmarker_achievements 1: each row's name.</summary>
        public bool achievements = true;
        /// <summary>bo6hitmarker_hit_sound 1.</summary>
        public bool hitSound = true;
        /// <summary>bo6hitmarker_kill_sound 1.</summary>
        public bool killSound = true;
        /// <summary>bo6hitmarker_horizontal 0: the popup's offset right, in 1920x1080 units (the panel's slider runs
        /// -ScrW()..ScrW()). The marker itself stays on the crosshair, as in the addon.</summary>
        public float horizontal;
        /// <summary>bo6hitmarker_vertical 0: the popup's offset down (-ScrH()..ScrH()).</summary>
        public float vertical;
        /// <summary>bo6hitmarker_marksize 1: the marker's size and spread, 0..10.</summary>
        public float markSize = 1f;
        /// <summary>bo6hitmarker_textsize 1: the popup's text size, 0..10.</summary>
        public float textSize = 1f;

        // ---- Black Ops VI Hitmarker: its "Achievements" panel (empty = the addon's own name, Texts hitfeedback.name.*) -

        /// <summary>bo6hitmarker_achievements_eliminate 'Eliminate' (the panel's "Kill Human").</summary>
        public string nameEliminate = "";
        /// <summary>bo6hitmarker_achievements_headshot 'Headshot'.</summary>
        public string nameHeadshot = "";
        /// <summary>bo6hitmarker_achievements_jump 'Jumpshot' (the panel's "Jump Kill").</summary>
        public string nameJump = "";
        /// <summary>bo6hitmarker_achievements_melee 'Brutal Kill' (the panel's "Meele Kill").</summary>
        public string nameMelee = "";
        /// <summary>bo6hitmarker_achievements_burn 'Burn Kill'.</summary>
        public string nameBurn = "";
        /// <summary>bo6hitmarker_achievements_expl 'Explosive Kill'.</summary>
        public string nameExplosive = "";
        /// <summary>bo6hitmarker_achievements_3 'Slaughter' (the panel's "Triple kill").</summary>
        public string nameTriple = "";
        /// <summary>bo6hitmarker_achievements_4 'Warrior' (the panel's "Quadra kill").</summary>
        public string nameQuadra = "";
        /// <summary>bo6hitmarker_achievements_5 'God Slayer' (the panel's "Penta kill").</summary>
        public string namePenta = "";

        // ---- Cool Killchains: its "Client" form --------------------------------------------------------------------------

        /// <summary>profiteers_skulls 1: "Show killstreak skulls under crosshair".</summary>
        public bool skulls = true;
        /// <summary>profiteers_skull_scale 1 (0.25..2.5).</summary>
        public float skullScale = 1f;
        /// <summary>profiteers_skull_offset 0.7 (0.05..0.95): the row's height as a share of the screen's.</summary>
        public float skullOffset = .7f;
        /// <summary>profiteers_skull_max 0 (0..50): the most skulls on screen; 0 works it out (no more than half the
        /// screen's width).</summary>
        public int skullMax;

        /// <summary>The panels' slider ranges (the convars' own bounds where they have them).</summary>
        public const float MaxSize = 10f, MinSkullScale = .25f, MaxSkullScale = 2.5f, MinSkullOffset = .05f, MaxSkullOffset = .95f;
        public const int MaxSkulls = 50;

        /// <summary>Keeps every field in range after settings.json is read or a row changes it (GameSettings.Clamp).</summary>
        public void Clamp()
        {
            horizontal = Mathf.Clamp(Finite(horizontal, 0f), -Gm.ScrW, Gm.ScrW);
            vertical = Mathf.Clamp(Finite(vertical, 0f), -Gm.ScrH, Gm.ScrH);
            markSize = Mathf.Clamp(Finite(markSize, 1f), 0f, MaxSize);
            textSize = Mathf.Clamp(Finite(textSize, 1f), 0f, MaxSize);
            nameEliminate ??= ""; nameHeadshot ??= ""; nameJump ??= ""; nameMelee ??= ""; nameBurn ??= "";
            nameExplosive ??= ""; nameTriple ??= ""; nameQuadra ??= ""; namePenta ??= "";
            skullScale = Mathf.Clamp(Finite(skullScale, 1f), MinSkullScale, MaxSkullScale);
            skullOffset = Mathf.Clamp(Finite(skullOffset, .7f), MinSkullOffset, MaxSkullOffset);
            skullMax = Mathf.Clamp(skullMax, 0, MaxSkulls);
        }

        private static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }
}
