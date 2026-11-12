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
        if (scripted)
        {
            most = stepMost;
            rate = stepRate;
            return;
        }
        Advance(dt);
        float share = Now == Phase.Relax ? tuning.relaxShare : 1f;
        most = Mathf.RoundToInt(stepMost * share);
        rate = stepRate * share;
        if (Now != Phase.BuildUp || mobLeft <= 0) return;
        most += tuning.mobSize;
        rate = Mathf.Max(rate, tuning.mobRate);
    }

    /// <summary>The horde made this many: a mob under way counts them off.</summary>
    public void Spawned(int count) => mobLeft = Mathf.Max(0, mobLeft - count);

    public string Report(int alive)
    {
        string phase = Now == Phase.BuildUp ? "쌓아 올리는 중" : Now == Phase.Peak ? "몰아치는 중" : "쉬는 중";
        return $"디렉터: {phase} · 긴장도 {Intensity:0.00} · 적 {alive} · " +
               (mobLeft > 0 ? $"떼 {mobLeft} 마리 오는 중" : $"다음 떼까지 {Mathf.CeilToInt(Mathf.Max(0f, mobIn))}초");
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
                mobLeft = Mathf.Max(0, tuning.mobSize);
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
