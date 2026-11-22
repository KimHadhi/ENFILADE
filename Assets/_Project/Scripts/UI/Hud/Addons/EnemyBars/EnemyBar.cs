using System.Collections.Generic;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// One enemy's bar, shown or not: the addon's TBL.HUD.HealthBars entry, kept from the moment the enemy comes into play
    /// (as the addon keeps one for every NPC from OnEntityCreated), so its follower and pillars are already right when its
    /// bar comes up. Pooled by EnemyBarsHud: a horde spawns and dies all the time.
    /// <para>Widths are shares of the bar's width and heights shares of ScrH, so a change of resolution mid-fight keeps
    /// every piece where it was (the addon took ScrW and ScrH once, when its file loaded, and kept those sizes).</para>
    /// </summary>
    internal sealed class EnemyBar
    {
        public EnemyHealth Enemy;
        public ZombieType Kind;
        public CapsuleCollider Hull;

        /// <summary>posworld_temp: the hull's top as last seen, where the bar stays once the enemy is gone.</summary>
        public Vector3 Top;
        public bool Placed;

        /// <summary>bo6_lasthp: the health read last frame (NaN before the first reading), and the maximum.</summary>
        public float LastHealth = float.NaN, Maximum = 1f;
        /// <summary>The health a hit's report left it at, for a body removed before this frame could read it.</summary>
        public float Reported = float.NaN;

        /// <summary>follower_w, as a share of the bar.</summary>
        public float Follower;
        /// <summary>currently_delaying (with the game time it ends; +inf until a hit gives it one) and currently_following.</summary>
        public bool Holding, Following;
        public float HoldEnd;

        public bool Show, Dead;
        /// <summary>lifetime_after_removed, real seconds.</summary>
        public float Gone;

        /// <summary>The name as the enemy gives it (to see when it changes) and printname, in capitals.</summary>
        public string RawName, Name;
        public bool Boss;

        /// <summary>dmg_total, its text (made when it changes), and when it clears (0: no hit has opened it).</summary>
        public float Total, TotalShown, TotalEnd;
        public string TotalText;

        /// <summary>A hit's word arrived since the last frame: the newest pillar takes its colour (the addon's 0.05 s timer).</summary>
        public bool Paint, PaintHead;

        public readonly List<Pillar> Pillars = new List<Pillar>(4);

        /// <summary>A fresh bar for <paramref name="enemy"/>, whose follower starts at <paramref name="health"/>.</summary>
        public void Reset(EnemyHealth enemy, float health)
        {
            Enemy = enemy;
            enemy.TryGetComponent(out Kind);
            enemy.TryGetComponent(out Hull);
            Top = default;
            Placed = false;
            LastHealth = float.NaN;
            Maximum = Mathf.Max(1f, enemy.MaximumHealth);
            Reported = float.NaN;
            // The addon starts the follower full (follower_init) and holding (currently_delaying = true) until a hit's
            // timer lets it go. Full is right for an enemy that comes in at full health; one that comes in hurt (a copy
            // made mid-fight on a joining PC) would show a white trail of damage this bar never saw, so it starts at the
            // health it has.
            Follower = Mathf.Clamp01(health / Maximum);
            Holding = true;
            Following = false;
            HoldEnd = float.PositiveInfinity;
            Show = Dead = false;
            Gone = 0f;
            RawName = null;
            Name = "";
            Boss = false;
            Total = TotalShown = TotalEnd = 0f;
            TotalText = "";
            Paint = PaintHead = false;
            Pillars.Clear();
        }

        /// <summary>Back to the pool: lets go of the enemy and its parts.</summary>
        public void Release()
        {
            Enemy = null;
            Kind = null;
            Hull = null;
            Pillars.Clear();
        }
    }

    /// <summary>
    /// What one drop in health raised (the addon's DamageBlocks entry): a translucent block rising from the bar's foot
    /// where the health now ends, as wide as the drop, a 2-pixel line at its left rising faster, and a solid chunk inside
    /// the bar. Left and Width are shares of the bar's width, Height and Line shares of ScrH, alphas 0-255.
    /// </summary>
    internal struct Pillar
    {
        public float Left, Width, Height, Line, Alpha, LineAlpha;
        public bool Head, FrontLine;
    }
}
