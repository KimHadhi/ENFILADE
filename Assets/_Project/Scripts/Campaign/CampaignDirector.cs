using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stage 5D: the campaign's horde director, after Left 4 Dead's. It reads how hard the players are having it (their
/// tension: health lost, going down, bleeding on the ground, easing off with time) and paces the step's own numbers
/// (CampaignSteps.horde, rate) through three phases: build-up at the step's numbers, with a mob rushing in now and then;
/// a short peak once someone's tension crosses the line; then a relaxed stretch with only a trickle, until it has passed
/// and the tension has eased. Holds and the escape are scripted crescendos and always run at full numbers; nobody comes
/// at all while everyone sits in a rest stop with its door shut. The host runs it (CampaignRun); health and going down
/// reach the host from every player, so it can read everyone's tension. Numbers: Tables/Campaign, row Director.
/// Bundle 3, after GMod Horde (sv_horde.lua: at most 20 + 5 a player alive at once, 50 at most; a wave's total and each
/// spawn tick grow with the players): with more players in the room (Crew) the step's horde, how fast they come and a
/// mob's size all grow, each by its share a player past the first, and so does an endless wave's Extra; whatever grew
/// stops at mostAlive. Alone, the numbers are the step's own, exactly as before.
/// </summary>
internal sealed class CampaignDirector
{
    [Serializable]
    public sealed class Tuning
    {
        [Tooltip("Tension a player gains from losing all their health (scaled by the share lost).")]
        public float intensityPerDamage = 1.2f;
        [Tooltip("Tension a player gains on going down.")]
        public float intensityDowned = .6f;
        [Tooltip("Tension lost a second (0-1).")]
        public float intensityDecay = .035f;
        [Tooltip("Someone's tension at this or above turns build-up into a peak (0-1).")]
        public float peakIntensity = .7f;
        [Tooltip("How long a peak keeps coming before the relax (s).")]
        public float peakSeconds = 5f;
        [Tooltip("How long the relax lasts at least (s); it also waits for the tension to halve.")]
        public float relaxSeconds = 25f;
        [Tooltip("Share of the step's numbers that still come while relaxed (0-1).")]
        public float relaxShare = .25f;
        [Tooltip("Shortest wait between mobs during build-up (s).")]
        public float mobEveryMin = 55f;
        [Tooltip("Longest wait between mobs (s).")]
        public float mobEveryMax = 95f;
        [Tooltip("How many come in a mob, over the step's own number.")]
        public int mobSize = 8;
        [Tooltip("How many a second while a mob comes in.")]
        public float mobRate = 4f;
        [Tooltip("Each player past the first lets this share more of the step's horde be about at once (Horde: 20 + 5 a player, so 25 alone and 40 with four).")]
        public float hordePerPlayer = .2f;
        [Tooltip("Each player past the first makes them come this share faster, a mob too (Horde: 6 a tick alone, 8.5 with four).")]
        public float ratePerPlayer = .15f;
        [Tooltip("Each player past the first makes a mob this share bigger (Horde: a wave's total times ceil(0.75 a player), three times with four).")]
        public float mobPerPlayer = .67f;
        [Tooltip("What grew with the players or an endless wave stops at this many about at once (Horde: 50); never below the step's own number alone.")]
        public int mostAlive = 50;
    }

    public enum Phase { BuildUp, Peak, Relax }

    private sealed class Track
    {
        public float Health, Intensity;
        public bool Downed;
    }

    private readonly Tuning tuning;
    private readonly Dictionary<PlayerMovement, Track> tracks = new Dictionary<PlayerMovement, Track>();
    private readonly List<PlayerMovement> gone = new List<PlayerMovement>();
    private float phaseLeft, mobIn;
    private int mobLeft;

    public CampaignDirector(Tuning tuning)
    {
        this.tuning = tuning;
        mobIn = NextMob();
    }

    public Phase Now { get; private set; }
    /// <summary>The tensest player's tension (0-1).</summary>
    public float Intensity { get; private set; }
    /// <summary>How many play in the room, the dead included (bundle 3; the run sets it each frame). 1 alone.</summary>
    public int Crew { get; set; } = 1;
    /// <summary>An endless wave: this many more may be about than the step's own horde, before the players' growth.</summary>
    public int Extra { get; set; }
    /// <summary>How much the horde grows for the players: 1 alone. The calm zombies and the elites' caps grow by it too.</summary>
    public float HordeGrowth => Growth(tuning.hordePerPlayer);
    // A mob's size for the players in the room.
    private int MobSize => Mathf.RoundToInt(Mathf.Max(0, tuning.mobSize) * Growth(tuning.mobPerPlayer));

    /// <summary>
    /// How many may be about (<paramref name="most"/>) and how many come a second (<paramref name="rate"/>) this frame,
    /// from the step's own numbers. <paramref name="scripted"/>: a hold or the escape; <paramref name="resting"/>:
    /// everyone in a rest stop with its door shut.
    /// </summary>
    public void Pace(float dt, List<PlayerMovement> players, bool scripted, bool resting, int stepMost, float stepRate,
        out int most, out float rate)
    {
        Feel(dt, players);
        if (resting)
        {
            // A rest stop starts the pacing afresh: the next stretch begins with a build-up and a full wait for its mob.
            Now = Phase.BuildUp;
            mobLeft = 0;
            mobIn = NextMob();
            most = 0;
            rate = 0f;
            return;
        }
        if (!scripted) Advance(dt);
        Numbers(stepMost, stepRate, scripted, 1f, 1f, tuning.mobSize, out most, out rate);
        // Bundle 3: more players, or an endless wave, let more be about, send them faster and make mobs bigger. Alone and
        // not endless, the numbers above stand as they were; whatever grew stops at mostAlive, never below them.
        if (Crew <= 1 && Extra <= 0) return;
        Numbers(stepMost + Mathf.Max(0, Extra), stepRate, scripted, HordeGrowth, Growth(tuning.ratePerPlayer), MobSize,
            out int grown, out float faster);
        most = Mathf.Max(most, Mathf.Min(grown, tuning.mostAlive));
        rate = Mathf.Max(rate, faster);
    }

    // This frame's numbers from the step's own grown by the given scales: a hold or the escape at full numbers, a relax at
    // its share, and a mob under way on top during a build-up.
    private void Numbers(int stepMost, float stepRate, bool scripted, float hordeScale, float rateScale, int mob, out int most, out float rate)
    {
        float share = scripted || Now != Phase.Relax ? 1f : tuning.relaxShare;
        most = Mathf.RoundToInt(stepMost * hordeScale * share);
        rate = stepRate * rateScale * share;
        if (scripted || Now != Phase.BuildUp || mobLeft <= 0) return;
        most += mob;
        rate = Mathf.Max(rate, tuning.mobRate * rateScale);
    }

    // 1 alone; each player past the first adds the share.
    private float Growth(float perPlayer) => 1f + Mathf.Max(0f, perPlayer) * (Mathf.Max(1, Crew) - 1);

    /// <summary>The horde made this many: a mob under way counts them off.</summary>
    public void Spawned(int count) => mobLeft = Mathf.Max(0, mobLeft - count);

    public string Report(int alive)
    {
        string phase = Now == Phase.BuildUp ? "쌓아 올리는 중" : Now == Phase.Peak ? "몰아치는 중" : "쉬는 중";
        return $"디렉터: {phase} · 긴장도 {Intensity:0.00} · 적 {alive} · " +
               (mobLeft > 0 ? $"떼 {mobLeft} 마리 오는 중" : $"다음 떼까지 {Mathf.CeilToInt(Mathf.Max(0f, mobIn))}초") +
               (Crew > 1 ? $" · {Crew}명 기준(동시 상한 ×{HordeGrowth:0.##}, 떼 {MobSize}마리)" : "");
    }

    private void Advance(float dt)
    {
        switch (Now)
        {
            case Phase.BuildUp:
                if (Intensity >= tuning.peakIntensity) { Now = Phase.Peak; phaseLeft = tuning.peakSeconds; break; }
                if (mobLeft > 0) break;
                mobIn -= dt;
                if (mobIn > 0f) break;
                mobLeft = MobSize;
                mobIn = NextMob();
                break;
            case Phase.Peak:
                phaseLeft -= dt;
                if (phaseLeft > 0f) break;
                Now = Phase.Relax;
                phaseLeft = tuning.relaxSeconds;
                mobLeft = 0;
                break;
            case Phase.Relax:
                phaseLeft -= dt;
                if (phaseLeft <= 0f && Intensity < tuning.peakIntensity * .5f) Now = Phase.BuildUp;
                break;
        }
    }

    // Each player's tension from what their health did since the last frame; the dead keep theirs until they're back.
    private void Feel(float dt, List<PlayerMovement> players)
    {
        float tensest = 0f;
        foreach (var player in players)
        {
            var receiver = player.DamageReceiver;
            if (receiver == null) continue;
            if (!tracks.TryGetValue(player, out var track))
                tracks[player] = track = new Track { Health = receiver.Health, Downed = receiver.IsDowned };
            if (receiver.IsDowned && !track.Downed) track.Intensity += tuning.intensityDowned;
            // Going down or getting up resets health; only a fall within the same state is damage (bleeding included).
            else if (receiver.IsDowned == track.Downed && receiver.Health < track.Health)
                track.Intensity += (track.Health - receiver.Health) / Mathf.Max(1f, receiver.MaximumHealth) * tuning.intensityPerDamage;
            track.Health = receiver.Health;
            track.Downed = receiver.IsDowned;
            track.Intensity = Mathf.Clamp01(track.Intensity - tuning.intensityDecay * dt);
            tensest = Mathf.Max(tensest, track.Intensity);
        }
        gone.Clear();
        foreach (var player in tracks.Keys) if (player == null) gone.Add(player);
        foreach (var player in gone) tracks.Remove(player);
        Intensity = tensest;
    }

    private float NextMob() => UnityEngine.Random.Range(Mathf.Min(tuning.mobEveryMin, tuning.mobEveryMax), Mathf.Max(tuning.mobEveryMin, tuning.mobEveryMax));
}
